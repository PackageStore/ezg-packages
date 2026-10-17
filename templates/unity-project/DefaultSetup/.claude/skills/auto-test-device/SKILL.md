---
name: auto-test-device
description: QA agent that plays the game on a device like a normal player to test ONE feature in depth — visuals and logic — then LOGS BUGS ITSELF to BugHub via the Bug Logger contract (screenshot + log + auto-generated repro steps, de-duplicated). Works on ANY repo from the EZG template - project conventions (screen enum, UI base class, cheats, PlayerData, CSV) come from `.claude/project-profile.json["qa"]` + `.claude/docs/QaProfile.md`, auto-detected by `profile init` when missing; knowledge learned each session (live screens, verified cheat/state paths, noise rules) is written back there. Devices - Android via adb (USB or emulator, AVD booted by name), iOS Simulator, real iPhone, Unity Editor (EZG QA Probe in the build). A new machine / project needs ONE sentence - `doctor` + STEP 0 handle the rest - update the related Feature Hub items (this skill, probe, autotest, cheat-gate, buglogger) via MCP ezg, run /bughub-setup in-session when BugHub is missing (asks only for the Discord forum ID if the project is unregistered), install the API-retry hook, add the probe to the manifest, install the app, open the game; a release build with com.ezg.cheat-gate on a device not in the test list gets added via test_device_add (Lead/QA account). Flow - doctor/selfcheck → import Editor autotest results → charter with case IDs (in the repo, `.claude/docs/qa/charters/`) → play case by case with explicit expectations (`tap --expect`), auto audit on each screen change → logic oracles from CSV/state/tracking/save-migration → environment matrix (font size, dark mode, aspect, pseudo-loc, interruptions) → log bugs → coverage + report. "Whole game" → `plan` groups by bucket/feature ordered by risk, one charter per group, in turn. Errors never stop the task - Claude API errors auto-resume every 30s via the /auto-clear hook, transient device/network errors rerun via `retry`. Use when the user types "/auto-test-device <device id> <tính năng>", "chạy auto test <device id>", "chạy auto test full game", "auto test toàn bộ game", says "auto test trên máy", "test tính năng X trên device/điện thoại/emulator", "chơi thử rồi log bug", "QA tự động trên thiết bị", "run auto test on <device>", "auto test the whole game", "play it and log bugs". Missing BugHub → run /bughub-setup, do not stop. Does NOT write C# autotest scenarios (→ ezg-autotest-scenario), does NOT fix bugs (→ /fix-bug).
---

# Auto Test Device — automated QA that plays the game on a device and logs bugs

You are a **professional QA**: play the game with real input like a player, distrust everything, write the expectation
down before acting, measure before/after every action, reproduce before concluding, and write bugs complete enough for a
dev to fix without asking back. The tools are `scripts/qadevice.py` (device driver) + **EZG QA Probe** (optional, inside
the build — sees the UI tree, reads state, calls cheats). Craft docs:

| File | Read when |
|---|---|
| [references/qa-playbook.md](references/qa-playbook.md) | once per session at STEP 1 — QA mindset, case design, visual/logic checklists, rubric, bug standard, noise, recording knowledge |
| [references/oracles.md](references/oracles.md) | STEP 3 — oracle vs invariant, standard template invariant table, `csv`/`state`/`call`/`track`, save-migration |
| [references/env-matrix.md](references/env-matrix.md) | STEP 4 — what each environment step exposes, pseudo-loc, aspect ratios, interruptions |
| [references/charter.template.md](references/charter.template.md) | STEP 1 — charter skeleton (`charter new` uses it) |

In this document `$QA <command>` is **shorthand** for:

```bash
python3 .claude/skills/auto-test-device/scripts/qadevice.py <command>     # Windows: py .claude/skills/auto-test-device/scripts/qadevice.py <command>
```

Every Bash call is a new shell — type the full path each time. **Do not** assign `QA="python3 …"` and then call `$QA`: zsh
(the macOS default shell) does not split the string into words ⇒ error `no such file or directory: python3 .claude/…`. To
keep a multi-step Bash call short, use a function: `qa() { python3 .claude/skills/auto-test-device/scripts/qadevice.py "$@"; }; qa tap --text "Mua" --as "…"; qa state X.Y`.

## Project sources of truth — this skill is shared by every repo

The skill (this folder) is **logic + process**; Feature Hub overwrites the whole folder on every update. Everything
**project-specific** lives outside the skill and is owned by the project:

| Layer | Location | Who writes |
|---|---|---|
| Machine-read | `.claude/project-profile.json` → `qa` block (conventions: screen enum, UI base class, cheat type/method, PlayerData facade, CSV glob, tracking; `liveness`, `verified`, `noise`, `environments`, `paths`) | `profile init` creates it; the tool writes back at `stop`; devs edit by hand |
| Model-read | `.claude/docs/QaProfile.md` (generated from `QaProfile.template.md`) — conventions explained in words, live screens, verified cheat/state paths, risk areas, this game's typical failure modes, noise rules | `profile init` creates it with `Status: draft`; **you update it at the end of every session**; dev approves → `reviewed` |
| Charter | `.claude/docs/qa/charters/<feature>.md` — test cases with IDs, oracles with sources; **no** results | `charter new`; you add cases; dev commits it with the feature |
| Build-side | `Assets/Resources/EzgQaProbeConfig.json` (read by the probe), `ProjectSettings/EZGAutoTestSettings.json` block `liveness` (read by autotest) | **only** `profile sync` generates them from the profile — never edit by hand |
| Session results | `<git-common-dir>/auto-test-device/<session>/` (screenshots, steps, cases, audit, coverage, report) | the tool — never commit |

Priority when you need a project value: **the dev's words in the task → `qa` in the profile → auto-detection by template
conventions (`profile init`) → the tool's default**. EZG template convention names in this skill (`GameEnums+Features`,
`FeatureBaseController`, `GameSystems`, `screen_*`, `Cheat_*`, `PlayerDataManager`…) are only **illustrative
defaults** — real values are always read from the profile.

## Hard rules

1. **Play like a user.** Every action that checks the feature goes through real input (`tap`/`swipe`/`drag`/`key`/`text`).
   Cheats/`call`/`timescale` are ONLY for setting up preconditions or fast-forwarding waits — record them in the bug's
   `--precondition`, never use them to "prove" the feature works.
2. **Expectation first; evidence before logging.** A tap on a button with consequences always carries `--expect` (text/`@id`/
   `screen:<Screen>`). Visual bug: a screenshot pointing exactly at what is wrong. Logic bug: before/after values + the
   expected value from config/GDD (`csv`, `state` commands). Crash/exception: the log signature. **Reproduce at least once
   more** before logging (a crash/ANR once is enough — `--rate 1/1`). Never log a guess.
3. **Tool failure ≠ game bug.** Run `selfcheck` before trusting any finding; `expectation-failed` from
   `tap --expect` is only a suspicion — it must reproduce **2 times** (the second after a `shot` to look at the screen) to
   become a bug. An action with no response → suspect the tool first (stale coordinates, overlay, lost probe), then the game.
4. **Do not hardcode the project into the skill.** Do not edit files in the skill folder "to fit this game". Project values
   → `qa` block/`QaProfile.md`/charter (table above). A convention mismatch (different base class name, differently named
   cheat…) → fix `qa.conventions` + `profile sync`, do not invent shortcuts within the session.
5. **`QaProfile.md` missing or `Status: template` → `profile init` immediately, do not stop.** Say in the report that the
   file needs dev review. `doctor` reports a convention that does not resolve → still test (black-box for the dependent
   parts), note it in the report.
6. **Learned knowledge goes into the repo within the turn, not only into memory.** Screens seen open (liveness),
   cheat/state paths that worked, noise rules (recurring false findings), this game's failure-prone areas, new cases you
   thought of → `stop` flushes the machine-read part itself; you update `QaProfile.md` + the charter at STEP 6. Personal
   memory does not travel with the repo — the profile does.
7. **No duplicates.** Before each bug: compare with the open bugs on BugHub (STEP 0d) and let the tool block local
   duplicates (same log signature · similar title · same screen + same type). Duplicate → do not log, write "đã có #N" in
   the report. What Editor autotest already caught (`autotest-import`) → **Δ-skip**: confirm quickly on the device, do not
   retest from scratch, do not re-log bugs autotest already listed (write "autotest đã có").
8. **Do not break the QA device.** No `--fresh` / `app clear` unless the feature is a new-player flow (tutorial,
   onboarding, first launch). Never complete a real IAP transaction — open the payment sheet, check the price, cancel. Every
   environment change (`env`, `aspect`, `net`, `rotate`, `pseudoloc`, `timescale`) is reverted by `stop`; `env matrix`
   reverts each step itself. Another app's overlay covers a button → do not change density, system permissions or
   force-stop that app to get around it — mark the case `blocked` (environment), not a game bug.
9. **Do not edit game code, do not commit.** Exceptions: `start` adds/upgrades `com.ezg.qa-probe` in
   `Packages/manifest.json` (and removes the old copied probe `Assets/EzgQaProbe/` if still there); `profile init`/`sync`
   write `project-profile.json`, `QaProfile.md`, `charters/`, `EzgQaProbeConfig.json`, the `liveness` block of the autotest
   settings; `stop` flushes knowledge into the profile. Tell the dev to commit all of them — you do not `git add`.
10. **Do not ask the user mid-way.** Decide by this skill and resolve blockers yourself (device not in the test list →
    add it with `test_device_add` if the MCP allows; if not → black-box + note in the report; BugHub not installed → run
    `/bughub-setup`; outdated skill/package → update from Feature Hub). **The only allowed question:** `/bughub-setup`
    finds the project is not registered on the server → ask the dev for the Discord forum ID (+ the project code if the
    profile has none) — only the dev knows it. Stop completely only when: there is no `bughub` MCP / the account lacks the
    right to register the project and the project is not on the server, the app is not installed and no build is found, or
    there was never any device after one round of `retry -- start …`.
11. **Errors do not stop the task — retry every 30 seconds until it passes.**
    - **Claude API errors** (`Your organization has disabled Claude subscription access for Claude Code`, rate limit,
      overloaded, 5xx, lost session…) kill the turn — the model is no longer running to retry by itself. `start` arms
      retry through the `StopFailure` hook of the `/auto-clear` skill (`retry-arm --max 0`; if the machine has not
      registered the hook, `start` runs `install` itself): the hook waits 30s then types into the right pane `Continue /auto-test-device: the previous turn stopped on an API error - resume the
      exact step in progress (QA session still open).`, with no limit on attempts. `stop` removes it. When you receive that
      prompt → do **not** `start` again: `$QA shot` to see where the device is, `$QA coverage` to see which case is
      unfinished, then carry on (the session, `mark`, `cases.jsonl`, bugs already sent are all still on disk). Line
      `API-error retry: OFF — …` at `start` → note it in the report, keep testing. Dev ran `/auto-clear off` mid-session
      → `$QA api-retry on`.
    - **Transient tool errors** (device unplugged / `offline` / `not found`, adb hang, screenshot failure, lost probe,
      BugHub network error / 5xx / 429) → do not stop, do not drop the case: wrap **side-effect-free** commands in
      `$QA retry -- <command…>` (reruns every 30s, at most ~9 minutes per call — call Bash with `timeout: 600000`; exit
      code 75 = still failing → call the same command again). Tap/swipe/key actions are **not** wrapped in `retry`
      (repeating an action = tapping twice): wait for the device with `$QA retry -- shot`, look at the screenshot, then redo
      the action. Bug send failed on the network → `$QA retry -- bug-resend <id>`. Lost probe → `$QA retry -- probe-connect`.
      No device at `start` → `$QA retry -- start <device> --feature "…"` for one round.
    - Errors that need a person (exit code 4 BugHub not set up, bad syntax) make `retry` stop at once — handle per rule 10.
12. **Always `stop` at the end of the session** (even after a mid-way error) — stops background logging, restores device
    settings, removes the probe trigger, disarms API retry, flushes knowledge into the profile, writes the report.
13. Never mention other games/studios in bugs, notes, charters, QaProfile.md, the report (rule `no-external-game-refs`).

## Reading the arguments

`/auto-test-device [device id] <feature…>` — the first word is the device **if** it looks like a device id (table below),
the rest is the feature (free text, usually Vietnamese). No id → `auto`: exactly one device connected → use it; no device
but exactly one AVD → boot it.

| `<device id>` | Meaning |
|---|---|
| adb serial (`R58N…`, `emulator-5554`, `192.168.1.5:5555`) | Android device connected / emulator running |
| AVD name (`Pixel_8_API_35`, `avd:Pixel_8`) | Emulator — booted headless if it is off |
| Simulator UDID or name (`iPhone 17 Pro`) | iOS Simulator — booted automatically |
| iPhone identifier/UDID/name | Real iPhone via `devicectl` (the build must contain the probe) |
| `editor` | The project's Unity Editor in Play mode (via the probe) — when there is no device; enter Play with Unity MCP `unity_play_mode` then `probe-connect` |
| `auto` / `android` / `ios` / empty | The only connected device (no device + one AVD → boot it) |

Unsure about the id → `$QA devices`. Empty feature → test **the main loop + every *live* screen reachable from the main
screen** (exploratory smoke; the live screen list comes from `qa.liveness` + `QaProfile.md`). Large feature ("toàn bộ game",
"full game", "cả game") → after STEP 0 run **`$QA plan`**: it maps live screens onto the repo's features/buckets, orders them
by risk (money/IAP/pass → gameplay → meta → social → onboarding → system), one charter per group; work through them in plan
order in the **same session** (`charter new` → STEP 1–5 → next group), `coverage` after each group, `stop` once at the end.
A group "without screens" is tested through the gameplay flow; screens marked `?` (in the enum but no code opens them) only
get a quick check that they can be opened.

### Minimal prompts — anyone just types one sentence

Do not require the exact `/auto-test-device` syntax, do not ask for the device when there is only one, do not ask "should I
install X" — everything missing on a new machine is handled by STEP 0 (0a → 0a' → 0b).

| User types | Meaning |
|---|---|
| `chạy auto test <device id>` · `auto test trên <máy>` · `run auto test on <device>` | device = that id, empty feature → exploratory smoke of every live screen |
| `chạy auto test full game` · `test toàn bộ game` · `auto test cả game` · `auto test the whole game` | device `auto`, feature = whole game → `plan` → group by group |
| `chạy auto test <device id> <tính năng>` · `test <tính năng> trên <máy>` · `test <feature> on <device>` | standard syntax |
| `tiếp tục auto test` · `continue auto test` · the API-retry prompt | do not `start` again — `shot` + `coverage` then carry on (rule 11) |

## STEP 0 — Preparation: a new machine onboards itself, right project, know what autotest covered

Run in order; 0a → 0a' → 0b → 0c sequentially, 0d in parallel with `start`. Goal: on **another machine, another project**,
a one-sentence prompt is enough — STEP 0 installs/updates everything itself, without asking (except the rule 10 exception).

**0a. `$QA doctor`** — one command checks everything: machine tools (adb/xcrun/Pillow), packages (`com.ezg.qa-probe` ≥
`PROBE_MIN_VERSION`, `com.ezg.cheat-gate`, `com.ezg.autotest` ≥ 0.2.0, `com.ezg.buglogger`, `com.ezg.featurehub` ≥ 0.4.0,
EZG scoped registry), BugHub (code + server), the `qa` block + `Status` of `QaProfile.md`, whether each convention resolves
to a real file, whether the build-side is in sync with the profile, the API-retry hook. The output ends with
**`remaining tasks`** — an ordered list, each line says who does it:

| Who | Does |
|---|---|
| `[tool]` (`probe-package`, `autotest-package`, `profile-init`, `profile-sync`, `api-retry-hook`) | **`$QA doctor --fix`** does them in one pass (idempotent): `profile init` (auto-detects conventions → `qa` block + draft `QaProfile.md`), adds/upgrades probe + autotest in the manifest, `profile sync`. `start` installs the API-retry hook itself. Read the output: a `default` key in `profile init` means the tool could not detect it → note it in the report for the dev to fill in |
| `[model]` (`mcp-check`, `feature-hub-update`, `bughub-setup`) | You do them per table **0a'** |
| `[dev]` (`cheat-gate`…) | Do not do them yourself (they touch game code) — note them in the final report, keep testing as far as possible |

`--json` for machine reading (`actions[]`, `featureHub.items`, `mcp`). Other `doctor` lines:

| `doctor` says | Do |
|---|---|
| `convention X` does not resolve (e.g. the screen base class is not found) | Quick grep to confirm; the project really uses another name → fix `qa.conventions.X` in `project-profile.json` → `profile sync`; nothing found → black-box test the dependent part, note it in the report |
| `autotest < 0.2.0` / missing | `doctor --fix` adds it; still workable — `autotest-import` will skip the liveness rule (0.1.x reports dead screens as Critical — see 0f) |
| `manifest has no EZG scoped registry` | The project is not connected to the registry → tell the dev to install it through Feature Hub (Editor); this session cannot download probe/autotest → black-box |
| Tool errors (adb missing, Pillow install failed) | Fix per the instructions in the error; adb missing and the device is Android → stop |

**0a'. `[model]` work — automatic onboarding, no questions**

| Task | Do |
|---|---|
| `mcp-check` | ToolSearch `ezg` and `bughub` (+ `unity`). The repo already declares these two HTTP servers in `.mcp.json` (Claude Code asks for approval only the first time, then `/mcp` → Authenticate with Google @easygoing.vn). Still missing → do **not** run `claude mcp add` yourself (it edits the dev's user config): print the exact command `claude mcp add --transport http --scope user ezg <url>` / `… bughub <endpoint>/mcp` (url in `doctor --json` → `mcp`) + "type `/mcp` → Authenticate → reopen the session", then still carry on with the parts that need no MCP (black-box; bug de-dup only on the local machine; the test device cannot be added automatically) and note it in the report |
| `feature-hub-update` | Read `Packages/manifest.json` + `ProjectSettings/EzgFeatureHub/install-record.json` (if present) → `check_project_updates(manifest_json, install_record_json)`. Only QA items matter (`doctor --json` → `featureHub.items`): UPM `com.ezg.qa-probe`, `com.ezg.autotest`, `com.ezg.cheat-gate`, `com.ezg.buglogger`; AI `Skills/auto-test-device`, `Skills/bughub-setup`, `Skills/auto-clear`, `Skills/fix-bug`, `Docs/QaProfile.template`, `Scripts/project_profile.py`, `Scripts/bootstrap.*`. Install when: `update_available`, UPM `notInstalledTemplatePackages`, or **an AI item missing from the record** (never installed through the Hub — the tool reports no line for it, you must compare against the list yourself). Each item: `get_install_steps(kind, name)` → this project's Editor is open (`unity_list_instances`, compare `projectPath` with `git rev-parse --show-toplevel`) → `unity_execute_code` the `install` snippet, wait for the domain reload, the `verify` snippet until `INSTALLED`; **one item at a time**, UPM before AI. No Editor → UPM: `$QA doctor --fix` (writes the manifest); AI item → report "cập nhật khi mở Editor" (update when the Editor is open). `Skills/auto-test-device` was just updated → **Read this SKILL.md again** before going on. Do not install items outside the list (not QA work); `com.ezg.cheat-gate` can be added as a package but still needs code that calls `CheatGate.Initialize` → still `[dev]` work |
| `bughub-setup` | `doctor` ⚠ `BugHub not ready` (task `bughub-setup`) or `start` exits with code 4 → call the Skill **`/bughub-setup`** right in the session (do not tell the dev to run it). That skill asks the dev only when the project is **not registered** on the server: the Discord forum ID (+ the project code if the profile has none) — the rule 10 exception. Done → `start` again. The account is not a BugHub admin and the project is not registered → stop, relay the skill's message to the dev verbatim (who has to do what) |
| `test-device` | Only known after the probe connects: **0c** |

**0b. `$QA start <device id|auto> --feature "<feature>"`** — checks BugHub → installs Pillow if missing → adds the probe
package to the manifest if missing → picks the device → installs the app if absent (the latest build in the repo) → launches
the game with the probe token → connects the probe → if the project has `com.ezg.cheat-gate`, waits for the probe to request
cheat access itself → takes the first screenshot. Read the output:

| Output | Do |
|---|---|
| Exit code 4, `BugHub: NOT READY` + the `✗` lines | Call the Skill **`/bughub-setup`** at once (0a' `bughub-setup`) — it fixes asset/profile/server the right way and asks the dev for the forum ID only when the project is not registered. Done → `start` again. The skill says someone else is needed (insufficient rights) → stop, print its message verbatim |
| Device errors (`unauthorized`, several devices) | `$QA devices`, fix per the instructions in the error |
| No device found / device `offline` (loose cable, AVD still booting) | `$QA retry -- start <device> --feature "…"` (every 30s, rule 11); still no device after one round → stop, tell the dev |
| `API-error retry: ON …` | Correct — an API error mid-session will be resumed automatically after 30s |
| `API-error retry: OFF — …` | Outside iTerm2 / Windows console, or the hook `install` failed → note it in the report, keep testing |
| `app … is not installed on … no build was found` | Stop, tell the dev to install a build |
| `probe: OK … gate open` | Deep testing: `ui`, deep `audit`, `state`, `call`, `cheat`, `tap --text`, `pseudoloc` (probe ≥ 1.3) |
| `probe: OK … GATE CLOSED — device XXXX-XXXX-XXXX is not in the test list of <code>` | **0c** |
| `probe: OK … GATE CLOSED — device has no network` | `$QA net on` if the tool turned the network off → `$QA probe-connect --unlock` |
| `probe: OK … GATE CLOSED — game has not called CheatGate.Initialize…` | Project configuration error (empty CheatGate code) → black-box, note it in the report for the dev |
| `probe: OK … GATE CLOSED (<cheat flag>=false)` (project does not use cheat-gate yet) | Turn cheats on like a QA would if the game allows it by an in-game action (the path is noted in `QaProfile.md`; none yet → open Settings, `$QA tap <version line> --times 10 --interval 0.15`) then `$QA probe-connect`; does not work → black-box |
| `probe: none` | The build on the device has no probe → black-box: screenshots + coordinates, logs, perf; `selfcheck` runs the parts that need no probe |
| `probe: … 1.2.x` | Commands that need ≥ 1.3 (`pseudoloc`, new audit flags, `screenBaseTypes`) report `unsupported` — write "build lại với probe ≥ 1.3 để test đủ" in the report |
| `note: added/bumped com.ezg.qa-probe …` / `note: removed the old copied probe …` | The build on the device is old; write "đã thêm package probe — commit manifest + build" in the report |
| `note: ⚠ manifest has no EZG scoped registry` | The project is not connected to the EZG registry → tell the dev to install it through Feature Hub; this session is black-box |
| `⚠ other-app overlay: <package> … covers view (x,y)-(x,y)` | A game button underneath → `--inject`; an ad/system button underneath → note in the report for the user to close/move the overlay; dependent cases `blocked` |

A `→ …` line under the output is the next thing to do — do exactly that. Screenshot `shot #001` → **Read** it to know where
the game is. To carry on from exactly the state QA left the device in → `--attach` (does not restart the game).

**0c. Device not in the test-device list** (project uses `com.ezg.cheat-gate`): immediately call the `ezg` MCP tool
`test_device_add` (deferred → ToolSearch `test_device`) with exactly the `project` + `device_id` that the `→` line printed,
`label` = `<git user.name> - <model>` → `$QA probe-connect --unlock` (keeps asking the server until accepted, ≤ 3 minutes).
The MCP refuses (account is not Lead/QA) or there is no `ezg` MCP → pass the refusal / `→` line verbatim to the user in the
final report, this session is black-box. Never guess a Device ID, never ask the user.

**0d. Open bugs (de-dup):** if the MCP tool `bughub_list` exists (deferred → ToolSearch `bughub`) → `bughub_list(project:
<code>)`, keep titles + issue numbers to compare at STEP 5. No MCP → write "không đối chiếu được bug server" in the report,
carry on (the tool still blocks duplicates locally).

**0e. `$QA selfcheck`** — the tool checks itself on the device just connected: the probe answers, `/ui` has elements,
`tap --text` hits a button and can go back, `shot` is not black, `logs` is readable. Writes `selfcheck.json`. Any item fails
→ fix the tool first (probe `probe-connect`, screenshot `retry -- shot`); do **not** start testing when `tap --text` fails
while the probe reports OK (that is a tool/overlay fault, every `expectation-failed` after it would be garbage). Cannot fix
it → note it in the report, black-box test that part.

**0f. `$QA autotest-import`** — reads the latest report of `com.ezg.autotest` (`AutoTestReports/`, path from
`qa.paths.autotestReports`) → `autotest-summary.md`: errors by suite/severity, screens/cases `covered-by-autotest`.
Use it to: (1) **Δ-skip** — screens/checks autotest already passed only get a quick confirmation on the device; (2) autotest
errors in this feature → check on the device whether they are real (0.1.x reports many errors on dead screens/orphan
assets — trust them only when that screen is in `qa.liveness`); (3) no report → write "chưa có autotest" and test fully.

STEP 0 is done when: `doctor` has no unfinished `[tool]`/`[model]` work (`[dev]` work has been noted for the report), QA
Feature Hub items are checked, `start` printed the first screenshot, `selfcheck` has no unexplained failures, you know the
probe level and what autotest covered. Feature is "whole game" → `$QA plan` right after, to get the group order.

## STEP 1 — Understand the feature → charter with case IDs

Do not touch the device before you know what "correct" looks like. Read in this order (stop when you have enough):

1. [references/qa-playbook.md](references/qa-playbook.md) — once per session.
2. **`.claude/docs/QaProfile.md`** — this game's conventions, live/dead screens, verified cheat + state paths (use them
   directly, no need to rediscover), risk areas, typical failure modes, recorded noise rules. `Status: draft` → still use
   it, note it in the report.
3. **Existing charter:** `$QA charter list` — does this feature already have `charters/<feature>.md`? Yes → read it, add new
   cases (never delete old cases, never write results into the charter); no → `$QA charter new <feature>` (skeleton from
   `charter.template.md`, then fill it in).
4. **Design docs:** the repo's `GDD/`, `TechSpec/`, `Docs/` (grep the feature name, in Vietnamese as well as the code
   name). GDD on Google Sheets → the `read-gdd` skill if the project has it. `CLAUDE_PROJECT.md` for the big picture.
5. **Code:** `codegraph_explore` (if `.codegraph/` exists) or Grep/Read following the profile's conventions: screen
   controller (base classes `qa.conventions.screenBaseTypes`, prefab by `screenPrefabPrefix`), static Service (business
   rules, formulas), PlayerData module (persisted state), CSV config (`csvConfigGlob` — the **expected values**: prices,
   rewards, durations, unlock conditions — read with `$QA csv <name> [--where col=val]`), localize keys, cheats
   (`cheatMethodPrefix`) to set up conditions. **Liveness:** a screen enum entry nobody opens
   (`qa.liveness.deadFeaturePatterns`/`forceDead`, not in `observed`, autotest reports `Feature.Dead`) → do not test it,
   do not log bugs for dead screens.
6. **Visual reference:** `.claude/docs/ArtStyle.md` (if present) + approved mockups/screenshots — to know which screens are
   "on style".
7. **Past bugs** of the feature (STEP 0d) + autotest errors (0f) — failure-prone areas, do not log them again.

Write the charter with the `charter.template.md` skeleton: goal, entry path, state/conditions, **oracles** (a table of
expectations citing `file:line` or `csv <name>:<line>`; inferences marked "inferred"), **invariants** that must
hold at all times (oracles.md §1), a list of cases with **IDs** (column `#`, e.g. `P0-01`) and priorities:

| Priority | Case group (details + examples in playbook §3) |
|---|---|
| P0 | Full happy path of the feature (enter → use → get the result → exit), repeated a second time |
| P1 | Boundaries & negatives: not enough money/conditions, exactly at the threshold, max level/max stack, locked/unlocked, empty/full lists; invalid state transitions |
| P2 | Interruptions: Back (Android) on every popup, background 5–30s, kill + reopen mid-flow, network loss during network/ad/IAP calls, spam/double tap on reward/buy buttons, taps during animations |
| P3 | Persistence & sync: restart after every important state change; numbers match across screens; build update keeps data (`build update`) if an older build exists |
| P4 | Visual sweep of every screen/popup/state of the feature + environment matrix (STEP 4): large font, dark mode, aspect ratios, pseudo-loc / long languages |
| P5 | Stability & performance: repeat the main flow 5–10 times, RAM/FPS before-after, rapid continuous input (controlled monkey) |

Limited time → choose cases by risk (playbook §10): money/IAP/save first, recently changed screens (git log) first, areas
`QaProfile.md` marks as "failure-prone" first. Skipped cases are recorded as `skip` with a reason, never silently.

`$QA charter diff` at this point = every case not yet run — use it as the to-do list. STEP 1 is done when the charter has
≥ 1 case in each group P0–P3 (P4/P5 may be merged) and every oracle has a source.

## STEP 2 — Exploration: play case by case, explicit expectations, automatic audit

For each test case:

```bash
$QA mark "P1-03 - mua nâng cấp khi thiếu tiền"            # the bug's repro steps are counted from here
… actions …
$QA note --case shop/P1-03 --result pass "Nút mua xám, toast 'Không đủ tiền' đúng"   # fail/blocked/skip; a fail that was logged → --bug N
```

`--case <charter>/<case>` = the charter file name without extension + the case ID (column `#`). `note` without `--case` is
still fine for loose notes, but **case results always go through `--case`** — `coverage`/`charter diff` only count
`cases.jsonl`.

Every action — **observe → expect → act → check**:

1. **Observe:** Read the view image (`*.view.jpg`) the previous command printed. Never act twice in a row without looking
   at the screen (except deliberate spam taps). With the probe → `$QA ui --marks` (image with every button numbered)
   instead of guessing coordinates.
2. **Act — in this order of preference:** `tap --text "<text on the button>"` → `tap @<id>` (number from `ui`) → `tap --name` →
   coordinates (only without the probe or when the object has no text/name; coordinates are **px of the latest view
   image**, the tool converts them to device px). Buttons with consequences **always** carry `--expect`:
   ```bash
   $QA tap --text "Mua" --expect "Đã mua" --as "Bấm Mua ở gói đầu tiên"          # wait for the text to appear
   $QA tap @12 --expect screen:Shop --as "Mở Shop từ HUD"                        # wait for the screen (Features enum name) on the stack
   $QA tap --text "Nhận" --expect @30 --timeout 5 --as "Nhận thưởng nhiệm vụ"   # wait for an element id
   $QA swipe --dir up --as "Cuộn danh sách nhiệm vụ"
   $QA drag 120 900 420 900 --as "Kéo vật phẩm vào ô trống"
   $QA key back --as "Bấm Back hệ thống để đóng popup"
   ```
   `--as` = the action **as the player would tell it**, written in Vietnamese (it becomes a repro step of the bug) —
   always present. Unsure about coordinates → `$QA shot --grid`.
3. **Check** the automatic output after every action:
   - `expectation-failed` → **not a bug yet**: `$QA shot`, look; repeat the action exactly once; still failing and the
     screen shows the game is wrong → case `fail`, go to STEP 5. The screen shows your expectation was wrong (different
     text, differently named screen) → fix the expectation, record it in the charter.
   - `Δ … ALMOST UNCHANGED` → the action got no response: shoot again after 1–2s; still unchanged and it
     is a button → suspect "button not responding"/"covered by an invisible overlay" (probe: `ui` shows `✗covered:`) — check
     `selfcheck` is still OK before logging.
   - `⚠ LOG: … NEW errors` → **stop and handle it now**: at which step the error appeared, reproduce, log a bug with
     `--log-sig`.
   - `⚠ SCREENSHOT:` (plain black/white screen, magenta patches) → visual/render bug — reproduce then log.
   - `⚑ auto audit #n: <k> findings (<m> suppressed)` → the screen just changed and the tool ran `audit` (image
     `*.audit.jpg`). Read the image, handle it per STEP 2.1 while still on that screen. Turn auto-audit off (heavily repeated
     screens, long scrolling) with the global flag `--no-auto-audit` for that command.
   - `⚠ foreground app: …` → an ad/browser/store opened: deal with it like a player, then check the game returns to
     the right place. Ad (Android) → `$QA ad-close` (waits for the countdown, finds the X/Skip button in the native view
     tree, taps it like a player). No button found → `$QA ad-close --list` / `key back`. Browser/store → `key back`.
   - `ERROR: touch point … is under another app's overlay` → follow the instructions in the error, do not try nearby coordinates.
   - Compare the result with the charter's oracle (STEP 3).
4. **Wait:** `$QA wait 3` · `$QA wait 20 --settle` (until the screen stands still) · animations/effects → `$QA burst 6`
   (a strip of frames to spot flicker, layout jumps). Long waits (production, cooldowns, offline) → probe `$QA timescale 10`,
   then `$QA timescale 1`; or the game's time-skip cheat (verified cheat path in `QaProfile.md`).

Navigate by player intuition: look for buttons by eye like a new user; if an ordinary player **cannot find a way** to do
what the feature requires → that is a finding too (UX, `--type ux`).

### 2.1 Visual — every NEW screen/popup/state

Auto-audit already ran when the screen changed; a new state **within the same screen** (after buying, out of money, empty
list, max level) → run `$QA audit` by hand. Audit = screenshot + image check + log; with the probe it adds a deep audit
(text overflow/clipping, missing glyphs, text that is a localize key / `{0}` / NaN / null, buttons offscreen or outside the
safe area, covered buttons, touch targets < 6 mm, white Images missing a sprite, broken materials, overlapping text;
**probe ≥ 1.3** adds flags ported from UiAuditUtil, printed by the tool as findings of the same name: `deadButton` (button is
clickable but has no handler), `noRaycastTarget` (interactive element has no Graphic receiving raycasts — taps do not land),
`overlapButton` (two sibling buttons overlap > 50%), `missingSprite` (Image without a sprite, shows a solid block),
`solidBackground` (sprite-less Image covering ≥ 50% of the screen — usually an intentional backdrop, Info), `noSprite`
extended to opaque white cells with children; each flag carries `flagDetail`) and an `*.audit.jpg` image circling each issue.

- **Every finding still needs a look at the image to confirm** (with a popup open, "covered" is correct; an intentional
  white panel is not a missing sprite; an element deliberately offscreen to slide in is not offscreen).
- A false finding that **recurs** (same flag, same element/screen, ≥ 2 times) → add a noise rule to `qa.noise` (playbook
  §11: `flag` + `match` regex + `reason` + `addedBy` + `date`) — later findings become `suppressed`, the report still counts
  them. Do not add a rule for a finding seen only once, do not add a rule broader than the wrong element (`match` covering
  the whole screen only when the reason holds for the whole screen). `$QA audit --include-suppressed` to review them again.
- Cheat/debug UI (cheat buttons, FPS overlay, Bug Logger button) is not a bug — need a clean screen → `$QA cheat off`, then
  `$QA cheat on` when done.
- Then inspect by eye with the playbook §4 checklist (layout, text, images/icons, states, ArtStyle.md style consistency,
  animation, safe area). The environment matrix (font size, dark mode, aspect ratios, pseudo-loc) is done together at STEP 4.

STEP 2 is done when `$QA charter diff` only has P4/P5 cases left (merged into STEP 4–5) or `blocked` cases with a reason.

## STEP 3 — Logic oracles: every number has a source

Read [references/oracles.md](references/oracles.md). Principle: an **oracle** (the correct value for one action, from
CSV/GDD) is checked **per case**; an **invariant** (something always true: money never negative, total in = total out,
timers use `TimeManager`…) is checked **after each group of cases** and at the end.

- **Measure before/after** every action with consequences: currencies, items, levels, timers, quest progress, red dots.
  With the probe: `$QA state <path>` — verified paths are in `qa.verified.statePaths` / `QaProfile.md` (e.g. the template
  default `PlayerDataManager.PlayerResource` — read the code for the right member if the profile has none yet); without the
  probe: read the numbers off the original `.png`. A new path that works → `stop` writes it into `verified`.
- **Expected values from CSV:** `$QA csv <collection name> --where id=3 --col price` (file name without extension, found via
  `csvConfigGlob`) — cite it in the charter as `csv CoinPack:3`. Do not trust a number in the GDD when the CSV differs → bug
  `--type gd` quoting both sources.
- **Compare with the oracle:** price deducted exactly once, reward added exactly once, correct duration, unlock at the right
  condition, displayed numbers match across HUD/popup/detail screen, consistent number format (1.2K, separators).
- **Invariants via `call`:** the game has check/aggregate functions (static Service) → `$QA call Type.Method` to read the
  aggregate, never to change state. Standard template invariant table + how to check: oracles.md §2.
- **Tracking:** actions with an analytics event (purchase, reward claim, screen open, ad view) → `$QA mark` first, act, then
  `$QA track --since <mark> --expect <event name>` (filters logs by `qa.conventions.trackingLogPattern`; event names from
  `trackingEventNameType` in code). Missing event / duplicate event on spam → bug `--type logic` severity medium (oracles.md §4).
- **Persistence:** after an important state change → `$QA app restart` → is the state intact? Background 30s → timer/offline
  correct? `app restart` by default closes the app **like a player** (to Home → wait 2s → close: the game gets
  `OnApplicationPause` to save); `app restart --hard` = a straight kill like a crash (the game does NOT get to save). A
  data-loss bug **must reproduce with the default `app restart`** to be logged as an ordinary persistence bug; only with
  `--hard` → state clearly "chỉ khi app bị giết đột ngột" (only when the app is killed abruptly) and lower it one level.
- **Save-migration (when the repo has a build older than the one under test):** `$QA build update <apk|aab|path> --keep-data`
  — the tool records the version before/after, snapshots `verified.statePaths` before/after → `migration.md`. A value lost or
  changed that the changelog cannot explain → critical bug (lost progress) (oracles.md §5).
- **Interruptions & repetition:** P2 in the charter — Back on every popup (Android), `$QA tap … --times 5 --interval 0.05` on
  reward/buy buttons (doubled reward = high bug), `$QA net off` mid network flow (a decent error, no hang, no lost/doubled
  reward), `$QA app background --secs 10`.
- **Softlock:** a screen with no way out, a button that does not respond, endless loading (`wait 30 --settle` still
  spinning) → high bug.

### 3.1 Stability & performance

- Logs are checked automatically after every action; `$QA logs --all` shows the whole session (signatures, repeat counts,
  the step where they occurred).
- `$QA perf` at the start, after each case group, at the end: RAM up > 150 MB without recovering, FPS < 25 on a static
  screen, device > 43°C → bug `--type perf`.
- Log noise: ad SDK/Play Services errors on emulators, ordinary warnings → do not log; an unrelated recurring error →
  `$QA logs --ignore "<regex>"` (write the reason in a note). An exception in game code is always a bug even without a
  visible consequence.

STEP 3 is done when every P0–P1 case has before/after measurements recorded in its `note`, and invariants were checked ≥ 1
time after the economy case group.

## STEP 4 — Environment matrix: same feature, different conditions

Read [references/env-matrix.md](references/env-matrix.md) to know what each step exposes. Do it **after** P0–P3 are done (so
logic bugs do not mix with environment bugs), on the feature's 2–3 main screens.

1. **Automatic:** `$QA env matrix` — for each step declared in `qa.environments` (font scales `fontScales`, sizes
   `screenSizes`, `darkMode`, `animatorScale`): apply → `shot` → `audit` → revert; summarized in `env-matrix.md`.
   `--quick` runs only one step per group. Unsupported target (iOS) → each step reports `unsupported`, not an error.
2. **By hand, when the feature has sensitive areas:**
   - `$QA aspect tall|short|tablet|<WxH>` (Android `wm size`) → walk the main screens again → `$QA aspect reset`. Badly
     anchored elements, odd gaps, buttons falling outside the safe area.
   - `$QA env font-scale 1.3` → text-heavy screens → `env font-scale reset`. Text overflow/clipping, stretched buttons
     overlapping.
   - `$QA env dark on` → `env dark off` (the game uses the system theme or a WebView/native popup).
   - `$QA env animator 0` → tap quickly through the flow → `env animator 1` (logic that depends on animations finishing:
     reward not shown, button stuck disabled).
   - `$QA env kill-activities on` → `app background --secs 5` → come back → `env kill-activities off` (Android kills the
     activity in the background: temporary state, open popups, ads being watched).
   - `$QA env trim-memory` (memory pressure) → keep playing 1 minute → `logs` (LMK, lost textures, native crash).
   - `$QA env clock +1d` (emulator/root) → daily reward / timers / expiring events → `env clock reset`.
   - **Pseudo-loc** (probe ≥ 1.3): `$QA pseudoloc on` → go through every screen/popup of the feature → `audit` →
     `$QA pseudoloc off`. Text expanded ~30% + accents + `[…]`: overflow, clipping, ugly wrapping, hardcoded text that does
     not go through localize (unchanged) — catches localize bugs **without** switching languages. Older probe → switch to
     the game's longest-text language if there is one, switch back when done.
   - Interruptions (P2) if not done at STEP 3: table in playbook §3.4.
3. Each finding: confirm with a screenshot, reproduce (turn the condition on again), then STEP 5. A bug that only happens
   under an environment condition → put the condition in `--precondition` + the title (`… khi cỡ chữ hệ thống 130%`, still
   within the 70-character title limit), severity per rubric (usually medium).

STEP 4 is done when `env-matrix.md` has a result for every step (or `unsupported`) and pseudo-loc went through every screen of
the feature (or the reason for skipping is recorded).

## STEP 5 — Log bugs

Once a bug is confirmed (rules 2–3):

1. **Duplicate?** Compare with the open bug list (STEP 0d) and `autotest-summary.md` (0f): same screen + same symptom → do
   not log, write "trùng #N" / "autotest đã có" in `note --case … --result fail`.
2. **Noise?** Audit findings matching a `qa.noise` rule are already `suppressed`; a new finding you see is not a bug → add a
   noise rule (STEP 2.1), do not log.
3. **Screenshot evidence:** bring the game back to the faulty state then `$QA shot`, or reuse an old image: `--shot <file name
   in shots/>`. A bug invisible on screen (pure logic) → the most relevant screen + the measurements in actual.
4. **Send** — all bug text (title, `--actual`, `--expected`, `--precondition`, `--steps`) is written in Vietnamese:
   ```bash
   $QA bug --title "Shop: mua gói vàng đầu tiên trừ kim cương 2 lần khi bấm nhanh" \
     --actual "Bấm Mua 2 lần liên tiếp: kim cương 500 → 300 (trừ 2×100), vàng chỉ +1.000 một lần" \
     --expected "Trừ 100 kim cương một lần, +1.000 vàng, lần bấm thứ hai bị chặn (csv CoinPack:2 price=100)" \
     --precondition "Đã qua tutorial, có 500 kim cương (cheat cộng kim cương)" \
     --type logic --severity high --rate 3/3 --evidence "ảnh #014 (trước) #015 (sau); state PlayerResource trước/sau" \
     --log-sig <sig if any> [--shot 015-tap.png] [--save] [--feature Shop] [--mention "<exact full name>"]
   ```
   - `--title`: Vietnamese, **at most 70 characters**, shaped `<Feature>: <what is wrong>`; no leading `[tags]` — version,
     platform, map and device are attached automatically. The tool strips leading `[tags]` and **rejects** a title over 70
     characters (it does not cut it) → rewrite it shorter; numbers and details go in `--actual`. The Discord thread name is
     built as `#N [version] category · feature · title` (≤ 100 characters).
   - `--feature`: the short name of the feature/screen (e.g. `Shop`, `Upgrade`, `Tutorial`). Default: the title's
     `Feature:` prefix, else the session's `--feature` without its `(…)` part. Never a charter-wide description like
     `Full game Map 1-3 (…)`.
   - `--mention`: the exact full name (as BugHub people lists it — `bughub_people`) of the ONE person who must act; usually
     omit it — a bug nobody is tagged for pings the project's `dev` role (BugHub default for `new`; with `--mention`
     only that person is pinged). A partial name that matches several people (a shared family name,
     "Anh") is an error, never a ping to all of them.
   - `--steps` defaults to the `--as` steps since the latest `mark` (preview with `$QA steps`). Steps missing meaning →
     write `--steps "1. …\n2. …"` yourself.
   - `--type`: crash · logic · visual · ux · perf · gd. `--severity` per the playbook §6 rubric (crash/lost progress/lost
     real money = critical; feature blocked/wrong economy = high; clearly wrong display/secondary flow = medium; minor = low).
   - The tool fills in category + forum tag per the BugHub form, device, build, commit (if the build bakes it in), app log
     (+ the log excerpt for `--log-sig`), extra `reporter=auto-test-device`. With the probe: adds the current screen, player
     id, game context; `--save` attaches a save snapshot.
   - `--via ingame` (needs the probe + Bug Logger in the build): sends through the game's own Bug Logger. Default `http`.
   - Before the first bug of the session you may `--dry-run` to preview the report.
5. Output `BUG #N logged → url` = done → `$QA note --case <charter>/<case> --result fail --bug N "…"`.
   `BugHub rejected …` → fix the parameters, `$QA bug-resend <id>`; network error / 5xx / 429 → `$QA retry -- bug-resend
   <id>` then keep playing. `duplicate of logged bug … (<reason>)` → genuinely a duplicate → drop it; genuinely different
   → add detail to the title (still ≤ 70 characters) or `--force`.
6. `$QA mark "<next case>"` and keep playing — one bug does not block the rest of the charter (a bug that fully blocks the
   feature → dependent cases `blocked`). **One bug → look for its siblings** (playbook §1) before moving to another case.

Good/bad title, actual and expected examples: playbook §7.

## STEP 6 — Wrap-up: coverage, report, record knowledge

Stop when `$QA charter diff` is empty (or only `blocked`/`skip` with reasons remain), the environment matrix is done, and **2
more free exploration rounds** (playbook §3.7) found nothing new — or the device/app is broken beyond rescue.

1. `$QA perf` one last time, `$QA logs --all` — any log error that is a bug not yet logged → STEP 5.
2. `$QA coverage` — cases planned/run/pass/fail/not-run per charter; live screens not visited (`liveness` − `observed`)
   → if they belong to this feature, go back and test them; otherwise write "ngoài phạm vi" (out of scope) in the report.
3. **Record knowledge in the repo (rule 6)** — before `stop`, with Edit:
   - `charters/<feature>.md`: new cases thought of during the session, corrected oracles, cases dropped because they no longer
     apply (no results).
   - `.claude/docs/QaProfile.md`: working cheats/cheat-enabling paths, newly found dead screens, this game's failure-prone
     areas (e.g. "reward buttons tend to double on spam"), new noise rules + reasons, convention mismatches fixed in
     `qa.conventions`. Templates in playbook §12. Keep `Status: draft` if the dev has not approved it.
4. `$QA stop` → restores device settings, disarms API retry, **flushes** `liveness.observed` + `verified.cheats/statePaths`
   into `project-profile.json`, writes `report.md` (bugs, case results, coverage, env matrix, selfcheck, suppressed,
   autotest-import). The output suggests lines to add to `QaProfile.md` if you have not written them yet.
5. Reply to the user (in Vietnamese), briefly:
   - Table of logged bugs: `#N` + title + severity + link (or "trùng #N" / "autotest đã có" / "gửi lỗi").
   - Coverage: pass/fail/blocked/skip counts per charter, failed cases that did not become bugs (and why), live screens not
     visited.
   - Findings not logged (suspicions not reproduced, small UX suggestions, `expectation-failed` seen only once) — one line each.
   - Project files changed that the dev needs to commit: `project-profile.json` (`qa`), `QaProfile.md`, `charters/*.md`,
     `EzgQaProbeConfig.json`/autotest settings (if `profile sync` ran), `Packages/manifest.json` (if `start` added the probe).
     `Status: draft` → remind the dev to review.
   - Path to `report.md`, device + build tested, probe version + how the gate was opened; a device not yet added to the
     test-device list → include the `→ test_device_add …` line; probe < 1.3 → remind them to rebuild to test fully.
   - Remaining `[dev]` work from `doctor` (cheat-gate not installed, MCP missing, AI item not updated for lack of an Editor…)
     — one line each with the concrete command/step; none → do not mention it.

## Quick commands

**Probe** column: `—` not needed · `yes` needs the probe (≥ 1.2.1) · `≥1.3` needs probe 1.3.0 (older → `unsupported`).

| Task | Command | Probe |
|---|---|---|
| Tools / project | `doctor [--strict] [--json] [--fix [--dry-run]]` (checks everything + `remaining tasks`; `--fix` does what the tool can; rc 1 when `--strict` and a required item is missing) · `devices` · `bughub-check` · `selfcheck` | — / selfcheck: deep part `yes` |
| Project profile | `profile init [--force]` · `profile sync` · `profile show` | — |
| Whole game | `plan [--json] [--only-screens]` — groups by bucket/feature ordered by risk, charter present/missing (writes `plan.md` into the session) | — |
| Open / close session | `start [device] --feature "…" [--attach] [--fresh] [--install x.apk] [--no-probe-install] [--no-api-retry] [--no-profile-init]` · `stop` (flushes knowledge + writes the report) · `report` (rewrites the report mid-session) · `probe-connect [--retrigger] [--unlock]` | — |
| Error → retry (rule 11) | `retry [--every 30] [--max-wait 540] -- <command…>` (code 75 = still failing, call again) · `api-retry on\|off\|status` | — |
| Editor autotest | `autotest-import [<report.json>]` → `autotest-summary.md` | — |
| Charter / cases | `charter list\|new <feature>\|diff` · `mark "<case>"` · `note --case <charter>/<case> --result pass\|fail\|blocked\|skip [--bug N] ["…"]` · `note "…"` · `steps` · `coverage [--charter <id>]` | — |
| Look | `shot [--grid] [--marks]` · `burst N` · `wait S [--settle]` · `audit [--include-suppressed]` · `ui [--find x] [--marks]` | deep audit/ui: `yes`; new flags: `≥1.3` |
| Touch | `tap --text "…"` · `tap @id` · `tap --name` · `tap --world` · `tap X Y` · `tap 50% 80%` · `tap … --times 5 --interval 0.05` · `tap … --expect <text\|@id\|screen:<Screen>> [--timeout S]` · `longpress` | --text/@id/--name/--world/--expect: `yes` |
| Swipe/drag | `swipe X1 Y1 X2 Y2` · `swipe --dir up` · `drag X1 Y1 X2 Y2 [--hold ms]` | — |
| Keys / text | `key back\|home\|enter\|app_switch` · `text "abc" [--submit]` · `text "Tiếng Việt" --probe` | --probe: `yes` |
| App / build | `app restart\|stop [--hard] [--settle S]` · `app launch\|background --secs N\|clear\|info\|install x.apk` · `build update <apk\|aab\|path> [--keep-data (default) \| --wipe]` → `migration.md` | migration snapshot: `yes` |
| Ads / overlays (Android) | `ad-close [--timeout S] [--list]` · `overlays` | — |
| Environment | `net off\|on\|wifi-off\|data-off` · `rotate portrait\|landscape\|auto` · `aspect <WxH>\|tall\|short\|tablet\|reset` · `env font-scale 1.3\|reset` · `env dark on\|off` · `env animator 0\|0.5\|1` · `env kill-activities on\|off` · `env trim-memory [level]` · `env clock +1d\|reset` · `env locale <tag>` · `env matrix [--quick]` | — |
| Localize | `pseudoloc on\|off [--mode expand\|accent\|both]` | `≥1.3` |
| Logs / performance / tracking | `logs [--all] [--tail N] [--grep re] [--ignore re]` · `perf` · `track [--since <mark>] [--expect <event>]` | — |
| Expected data | `csv <name> [--where col=val] [--col c]` | — |
| Game | `state Type.Member… [--full\|--out f.json]` · `call Type.Method args [--full\|--out f.json]` · `types <keyword>` · `timescale 10` · `cheat on\|off\|status` | `yes` |
| Bug | `bug --title … --actual … --expected … --type … --severity … [--feature F] [--mention "<full name>"] [--log-sig] [--shot] [--save] [--dry-run] [--force]` · `bug-resend <id>` | — |
| Probe | `probe-install [--gate none\|Ns.Type.member]` | — |

Every action command accepts `--as "<description>"`, `--wait S`, `--no-shot`, `--grid`, `--marks`, `--inject` (touch through
the probe), `--allow-overlay` (deliberately touch another app's overlay — blocked by default). The global flag
`--no-auto-audit` turns off the automatic audit on screen change for that command. `call`/`state` truncate long results and
say `[cut …]` — need the whole thing (id lists, JSON to parse) → `--out <file>` then Read that file.

## Appendix — platforms & limits

| | Android (adb) | iOS Simulator | Real iPhone | Unity Editor |
|---|---|---|---|---|
| Tap/swipe/hold | system input (real) | `idb` if available, otherwise probe | probe only | probe only |
| Screenshot | screencap | simctl | probe | probe |
| Logs | logcat (Java/native crash, ANR, LMK, Unity exception) | log stream + probe | `devicectl --console` + probe | probe |
| Back / network / rotation | yes | no Back; no network/rotation | no | no |
| `aspect` / `env` | yes (`wm`, `settings`, `am`) | `unsupported` (switch simulator device) | `unsupported` | `unsupported` |
| Unicode text input | probe (`text --probe`) | idb/probe | probe | probe |

- **Probe** = package `com.ezg.qa-probe` (shipped with the EZG template; on older projects `start` adds it to the manifest):
  activated by a random token per session (intent extra / argument / trigger file), listens only on localhost on Android.
  A release build opens the gate only when: the device is in the `com.ezg.cheat-gate` test-device list (an activated probe
  asks the server once by itself), or the game's cheat flag is true. Opened via cheat-gate → the probe also turns the game's
  cheats on. The probe reads conventions (screen base class, cheat type/member) from `Assets/Resources/EzgQaProbeConfig.json`
  generated by `profile sync`; file missing → it auto-detects by template conventions. Touches through the probe go through
  the EventSystem — UI works well; 3D objects only receive them if the game uses EventSystem/PhysicsRaycaster or `OnMouse*`
  (Android uses real input, so it has no such limit).
- Multi-touch (pinch zoom) cannot be simulated with adb — test zoom with buttons/scrolling if the game has them, mark pinch
  cases `skip`.
- Other apps' overlays (Android): `start` and `overlays` detect third-party floating windows; adb taps into those areas are
  blocked.
- `env clock`/`env locale` need an emulator or a rooted device; ordinary real devices → `unsupported`, mark `skip`.
- Session state lives in `<git-common-dir>/auto-test-device/` (screenshots, logs, `cases.jsonl`, audit, `coverage.md`,
  `env-matrix.md`, `migration.md`, `report.md`) — never commit it. Knowledge that travels with the repo lives in
  `project-profile.json["qa"]`, `QaProfile.md`, `charters/`.
- `qadevice.py` uses only the Python 3.8+ standard library (Pillow optional); tests: `python3 -m unittest discover -s .claude/skills/auto-test-device/scripts/tests`.
