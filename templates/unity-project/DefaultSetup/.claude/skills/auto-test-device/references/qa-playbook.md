# QA playbook — /auto-test-device

Craft guide for the QA agent: mindset, chartering, test case design, visual/logic/stability checklists, severity rubric, bug
writing standard, telling noise apart, risk-based case selection, recording knowledge in the repo. Applies to every game;
wherever EZG template convention names appear (`PlayerDataManager`, `TimeManager`, `UnlockFeature`…) they are **default
examples** — real values are read from `.claude/project-profile.json["qa"]` + `.claude/docs/QaProfile.md`. Oracle/invariant
details: [oracles.md](oracles.md). Environment matrix: [env-matrix.md](env-matrix.md).

---

## 1. QA mindset

- **Do what a real player does — then do what a clumsy/hasty/curious player does.** Tap twice, tap while an animation
  runs, quit midway, come back after a while, lose the network, rotate the device, press Back.
- **Every action has an expectation written down first** — it becomes the `--expect` of the `tap` command. Without an
  expectation you cannot detect a fault, nor tell "the game is wrong" from "my guess was wrong".
- **Measure, don't estimate.** Record before/after numbers (`state`, numbers on the original screenshot). "Looks right" is
  not a result.
- **Suspect the tool first, the game second.** Trust findings only after `selfcheck` passes. One `expectation-failed` is a
  suspicion; twice, with a screenshot, is a bug.
- **One bug → look for its siblings.** Text overflowing on one button → check every button of the same kind (pseudo-loc
  scans quickly). Reward doubling on spam → try every other reward button.
- **Minimal repro.** Before logging, cut the steps down to the fewest that still trigger the bug.
- **Tell bugs from design.** Suspect it is by design (no doc says otherwise) → log `--type gd` with a clear question,
  severity low/medium, do not assert it is wrong.
- **Do not test dead screens.** An enum entry ≠ a live screen. A screen nobody opens (`qa.liveness`, autotest
  `Feature.Dead`) has no bugs — just one line "màn chết, nên dọn" (dead screen, should be cleaned up) in the report.
- **Knowledge belongs to the repo.** What you learn about this game (which cheats work, which screens tend to break, which
  findings are noise) goes into `QaProfile.md`/charter/profile within the turn — the next session (possibly another agent)
  can reuse it.

## 2. Charter — cases with IDs, oracles with sources, stored in the repo

Skeleton: [charter.template.md](charter.template.md) (`$QA charter new <feature>` creates it from this skeleton in
`qa.paths.chartersDir`, default `.claude/docs/qa/charters/<feature>.md`). A charter is a **project asset**: the dev commits it
with the feature, later sessions read it again and add to it. Therefore:

- **Only cases + oracles, no results.** Results live in the session's `cases.jsonl` (`note --case`). Do not write "pass on
  day X", do not write bug numbers into the charter.
- **Stable case IDs:** column `#` shaped `<P?>-<nn>` (e.g. `P0-01`, `P2-07`). A dropped case → keep the row, write "bỏ — lý
  do" (dropped — reason), never renumber (old sessions reference that ID). New cases are appended at the end of their group.
- **Charter name** = file name without extension, snake/kebab after the feature's name in code (`shop`, `daily-quest`). The
  full case ID used by `note --case` is `<charter>/<#>`, e.g. `shop/P1-03`.
- **Oracles with checkable sources:** `csv CoinPack:2` (readable by the `csv` command), `file.cs:123`, `GDD/x.md §3`.
  Inferences → mark "inferred" so bugs based on them carry `--type gd`.
- **Invariants** of the feature (things true at all times, oracles.md §1) are listed separately — checked after each case group.
- Large feature → several small charters per screen/system, not one 60-case charter.

## 3. Test case design

### 3.1 Happy path (P0)
Go the full loop: enter the feature the way a real player does → perform the main action → get the result (reward, unlock,
screen change) → exit. Check every number along the way against the oracle. Repeat once more (the second pass often exposes
state that does not reset). Every tap with consequences carries `--expect`.

### 3.2 Boundaries & equivalence classes (P1)
- Resources: **0**, **1 unit short of the price**, **exactly the price**, **surplus**, very large (number format 1K/1M/1B,
  text overflow).
- Level/progress: level 1, the level just before an unlock threshold, exactly at the threshold, max level (the upgrade button
  must change state), beyond max.
- Lists: empty, 1 item, full, more than one page (scroll to the end, the last item is not covered).
- Time: just started, about to end (a few seconds left), exactly at the end, ended while the app was in the background.
- Attempts/daily limits: all attempts used, last attempt, rolling to a new day (fast-forward with a cheat or `env clock +1d`
  on an emulator).

### 3.3 State transitions (P1)
Sketch the states in your head (locked → unlocked → running → done → claimed → cooldown). Try **every valid transition** and
**invalid transitions** (claim before it is done, buy while processing, open a popup while another popup is open).

### 3.4 Interruptions (P2) — matrix for each main screen/popup
| Interruption | Command | Expected |
|---|---|---|
| System Back (Android) | `key back` | closes the top popup / asks to quit on the root screen; never closes 2 layers by mistake, never gets stuck |
| Short background | `app background --secs 5` | back to the right screen, state kept, music/timer continue correctly |
| Long background | `app background --secs 60` | timer/offline computed by real time |
| Background + OS kills the activity | `env kill-activities on` → `app background --secs 5` → `env kill-activities off` | like a short background; open popups/ads do not get stuck (env-matrix.md) |
| Kill + reopen (like a player) | `app restart` (Home → wait → close) | committed state was saved; an unfinished flow is handled sensibly (no money lost/reward doubled, pending rewards still there) |
| Crash / OS kills the app | `app restart --hard` | save not corrupted, no doubled rewards; losing a few seconds of recent progress may be by design (periodic saving) — only log when something the player "committed" is lost, and state clearly "chỉ khi crash" (only on crash) |
| Network loss | `net off` (… `net on`) | a decent error message, a retry button, no stuck loading, no reward granted before the request succeeded |
| Spam / double tap | `tap … --times 5 --interval 0.05` | handled only once; tracking only 1 event |
| Tap during animation | tap right as the popup opens / numbers run; `env animator 0` to expose logic waiting on animations | no getting stuck, no double reward |
| Screen rotation (if the game allows it) | `rotate landscape` | layout does not break |
| System notification/call | (simulated with a short background) | like a short background |

### 3.5 Persistence & consistency (P3)
- After every important state change → `app restart` → compare state (`state` before/after).
- The same number shown in several places (HUD, popup, detail screen, tooltip) must match and update together.
- Red dot/badge: appears when there is something to do, disappears when done, does not come back after restart once handled.
- An older build in the repo/on the device → `build update --keep-data`: the old build's save loads into the new build with
  nothing lost (oracles.md §5).

### 3.6 Stability & performance (P5)
- Repeat the main loop 5–10 times, `perf` at the start/end: RAM does not keep growing, FPS does not drop gradually.
- Open/close the same popup 10 times (leaks, stacked popups, slowing down).
- Controlled monkey: 20–30 quick taps on different areas of the feature screen (`tap … --no-shot --no-auto-audit`), then
  `shot` + `logs` — the game neither crashes nor gets stuck.
- `env trim-memory` then keep playing 1 minute — lost textures, native crash, LMK.

### 3.7 Free exploration (the final 2 rounds)
Pick a "tour": **money tour** (follow money in/out), **clumsy tour** (do every step wrong), **return tour** (leave, come back
by another path), **extreme tour** (largest/smallest values), **newcomer tour** (know nothing, follow only on-screen hints).
Record `mark "Thăm dò — tour …"`; a finding that suggests a new case → add it to the charter.

## 4. Visual checklist (every screen/popup/state)

**Layout:** elements off-center/out of line with their siblings; uneven spacing; overlapping elements; clipped by the screen
edge, notch, navigation bar, rounded corners; close/back button hard to see or hard to tap; content at the end of a list
covered.

**Text:** overflow/clipping/ugly wrapping; text outside its frame/button; wrong font or missing characters (boxes, dropped
Vietnamese diacritics); leftover localize keys (`shop_title`), placeholders (`New Text`, `Button`), `{0}`, `NaN`, `null`,
nonsensical negative numbers; spelling mistakes, mixed languages; inconsistent number formatting (1000 vs 1K vs 1,000); text
too small to read; poor contrast. **Text that does not change under `pseudoloc on`** = hardcoded text bypassing localize.

**Images:** white/pink cells (missing sprite/shader), images stretched out of proportion, blurry/pixelated, wrong icon for the
item, jagged edges, transparent background turned black, wrong z-order (effect behind the background, popup under the HUD).

**States:** disabled button that looks enabled (and vice versa); no feedback on tap (no press-down/sound/effect); loading
without an indicator; empty state with no message; numbers not updating after an action.

**Style:** compare with `.claude/docs/ArtStyle.md` (palette, button kit, fonts, layout of the reference screens) — screens off
tone, buttons from another kit, odd fonts.

**Motion:** `burst` when opening/closing popups, claiming rewards, changing screens: one-frame flicker, layout jumps, effects
stuck after closing, count-up ending on the wrong value.

**Environment (STEP 4, env-matrix.md):** 130% system font, dark mode, tall (20:9) / short (16:9) / tablet devices,
pseudo-loc — elements anchored correctly, no odd gaps, no overflowing text.

**Automatic audit** (`audit`, auto-audit on screen change; with the probe) covers part of the list above: text overflow/
clipping, missing glyphs, localize key / `{0}` / NaN / null, buttons offscreen / outside the safe area, covered buttons, touch
targets < 6 mm, Images missing a sprite, broken materials, overlapping text (the tool's kebab-case findings: `text-overflow`,
`missing-glyph`, `missing-sprite`, `missing-material`, `text-outside`, `text-content`, `offscreen`, `unsafe-area`, `blocked`,
`tiny-target`, `text-overlap`). **Probe ≥ 1.3** adds flags ported from autotest's UiAuditUtil — printed by the tool as findings
of the same name (camelCase), with `flagDetail`, same noise mechanism:

| Probe 1.3 flag | Meaning | Suggested severity |
|---|---|---|
| `deadButton` | button is clickable (interactable) but has no handler attached — tapping does nothing | high if it is a button the player needs; low if decor |
| `noRaycastTarget` | interactive element has no Graphic receiving raycasts — taps do not land | high |
| `overlapButton` | two sibling buttons overlap > 50% — which one gets the tap depends on order | medium |
| `missingSprite` | Image without a sprite, shows a solid block | medium (white cell where an icon/image belongs) |
| `solidBackground` | sprite-less Image covering ≥ 50% of the screen — usually an intentional backdrop | Info — a bug only when it is not a backdrop |
| `noSprite` (extended) | opaque white cell with children (sprite-less panel holding content) | medium if it does not match the ArtStyle kit |
| `overflow` · `missingGlyph` · `outsideContainer` · missing material | present since 1.2.1, already mapped to the kebab-case findings above | per finding |

For every finding: look at the `*.audit.jpg` image → real → bug; false → see §11 (noise).

## 5. Logic checklist for common systems

How to measure + invariants in detail: oracles.md.

| System | Check |
|---|---|
| Currency / economy | deducts/adds the right amount, exactly once; never negative; large numbers do not overflow; sources/sinks match the GDD; tracking 1 event per transaction |
| Rewards | right type + amount per CSV; reward popup matches what was actually received; not claimable again after restart/spam |
| Soft-currency purchases | not enough money → blocked + message/redirect to shop; enough → deduct + deliver; displayed price = deducted price = CSV price |
| IAP | the sheet shows the right price/pack name matching the CSV pack; cancel → nothing granted, no stuck loading; (never complete the transaction) |
| Rewarded ads | watched to the end → correct reward; closed early/no ad available → no reward + message; ad buttons hidden after buying no-ads (if any) |
| Upgrades / levels | stats increase per the table; next tier price correct; max level locks the button; the upgrade effect applies to real gameplay |
| Unlocks | right condition (UnlockFeature table or equivalent); before the condition it shows locked + reason; once unlocked it is saved |
| Timers / production / offline | counts correctly; background/kill still counts real time correctly; offline cap correct; changing the device clock (`env clock`) gives no gain if the game guards against it |
| Quests / achievements | progress increases with the right actions; completion → claimable; resets on the right cycle |
| Tutorial | no stuck steps; tapping outside the guided area does not break it; kill mid-tutorial → resumes sensibly; does not show again once done |
| Red dots / notifications | shown/hidden at the right time, on the right screen |
| Settings | sound/vibration/language apply at once and persist after restart |
| Save / account sync | restart keeps progress; build update keeps progress; (if any) login/sync loses no data |
| Leaderboards / events | start/end times; closes cleanly on expiry; milestone rewards |
| Localization | `pseudoloc on` changes every string; switching language changes every string of the feature; no hardcoded text left |
| Tracking / analytics | right event name, right count, right main parameters (oracles.md §4) |

## 6. Severity rubric

| Severity | When | Common `--type` |
|---|---|---|
| **critical** | crash, ANR, hard freeze, lost progress/save (including after a build update), lost real money/IAP not delivered, a softlock with no way out, infinite currency exploit | crash, logic |
| **high** | a main feature unusable or giving a wrong economic result (wrong deduction/addition, doubled reward), exception in game code, main button not responding, progress blocked | logic |
| **medium** | clearly wrong display (text overflow, missing sprite, exposed localize key), a wrong secondary flow, an interruption losing temporary state, a bug only under an environment condition (large font, aspect ratio), missing/duplicate tracking, a clear perf drop | visual, logic, perf |
| **low** | a few px off, animation not smooth, spelling, small UX suggestions, a slightly small touch target | visual, ux |

BugHub forum tags are assigned automatically by type (Logic/Visual/Performance/Improvement) + severity (High/Medium/Low) if
the project has them.

## 7. Bug writing standard

All bug text — title, actual, expected, precondition, steps — is written in Vietnamese (QA and devs read it in BugHub /
Discord / GitLab).

**Title** (Vietnamese, **≤ 70 characters**): `<Feature>: <specific symptom> [khi <condition>]` (the optional `khi …` = "when …").
- `<Feature>` = the short name of the feature/screen (`Shop`, `Upgrade`, `Tutorial`) — it is also the default `--feature`.
- No leading `[tags]`: version, platform, map and device are attached automatically. The tool strips leading `[tags]` and
  rejects a title over 70 characters → rewrite it shorter; numbers and details go in Actual. The Discord thread name is
  built as `#N [version] category · feature · title` (≤ 100 characters).
- Good: `Shop: mua gói vàng trừ kim cương 2 lần khi bấm nhanh` · `Popup nhiệm vụ: chữ nút "Nhận" tràn khung khi cỡ chữ hệ thống 130%`
- Bad: `Bug shop` · `Lỗi hiển thị` · `Game bị lỗi khi bấm nút`

**Actual:** what happened, with measurements/verbatim text/error messages. **Expected:** what should have happened + the
source (`csv X:n`, GDD, the standard behaviour on another screen). **Precondition:** state needed before step 1 (level, money,
cheats used, language, environment condition). **Steps:** numbered, one player action per step, enough for someone else to
redo it on another device. **Rate:** `<times it failed>/<attempts>`. **Evidence:** screenshot numbers, log signature, measured
values (`state` before/after).

One bug = one problem. Two symptoms with different causes → two bugs. One cause showing on several screens → one bug listing
the screens.

`--mention` only when ONE specific person must act: give their exact full name as BugHub people lists it; usually omit it —
the server notifies the right people. A partial name matching several people (a shared family name, "Anh") is an error,
never a ping to all of them.

## 8. Not a bug (do not log)

- The floating Bug Logger button, FPS/debug console overlays, the cheat menu — QA tools.
- Emulator-only errors due to missing Google Play Services / emulated GPU (ads not loading, Google sign-in failing, shaders
  slightly different from real devices) — note "chỉ emulator" (emulator only), recheck on a real device if available.
- No ads available (no fill) on the test device — unless the game mishandles that case (gets stuck, still grants the reward).
- Another app's overlay on the QA device covering game/ad buttons — environment fault: dependent cases `blocked`, mentioned
  in the report.
- Losing a few seconds of progress when the app is killed outright (`app restart --hard`) while a normal `app restart` still
  saves correctly.
- Intentional design stated in the GDD (e.g. prices growing exponentially).
- Warning/info logs from third-party SDKs with no consequence.
- **Dead** screens (no entry path, dead in `qa.liveness`, autotest `Feature.Dead`) with visual/asset faults — one line
  "nên dọn" (should be cleaned up) in the report, not a bug.
- Autotest 0.1.x errors reported as Critical on dead screens / Major on orphan assets that never load — see §13.
- An `expectation-failed` seen once, passing the second time, with nothing wrong in the screenshot — note "nghi vấn"
  (suspicion) in the report.
- An audit finding that already has a noise rule (`suppressed`) — unless a second look at the image shows the rule is wrong
  (fix/delete the rule, log the bug).

## 9. Approach by genre

| Genre | Core loop to walk | Typical failure points |
|---|---|---|
| **Idle / tycoon / management** | collect → upgrade → open a new station/area → change map/prestige | offline earning, very large numbers & formatting, bulk upgrades (x10/max), production speed after upgrades, stacking staff/buffs, timers in the background |
| **Merge** | drag-and-drop to merge → higher-tier items → orders | drops landing in the wrong cell, merging the wrong tier, full storage, items lost when the app is killed mid-drag |
| **Match-3 / puzzle** | enter level → moves → win/lose → reward/stars | out of moves, boosters, chain combos, shuffle when no moves are left, miscounted goals, losing → revive/ads |
| **Runner / action / arcade** | start → control (swipe/hold) → collisions → end → reward | input latency, objects passing through each other, pause/resume mid-run, revive, FPS with many objects |
| **Tower defense / strategy** | pick a stage → place/upgrade towers → enemy waves → win/lose | overlapping placement, selling/refunds, x2 speed, the last wave, negative base HP |
| **RPG / combat / auto battle** | lineup → battle → result → upgrade characters | stats applied correctly, skills/cooldowns, equipping/unequipping gear, auto battle, power comparison |
| **Gacha / collection / cards** | pull → receive → upgrade/merge | displayed odds, pity, duplicates converted to resources, x10 deducts correctly, skippable pull animation |
| **Simulation / building** | place buildings → operate → collect | overlapping/out-of-bounds placement, rotating/moving buildings, NPC pathing, layout saved after restart |
| **Casual / hyper-casual** | one tap → score → replay | replay spam, interstitial ad frequency, high score saved correctly |
| **Event / live-ops / pass** | enter the event → do quests → claim milestones | expiring while open (`env clock`), milestones claimed twice, paid/free pass tracks separated correctly, countdown timers |

3D/world gameplay (tapping objects in the scene): on Android use coordinates on the screenshot as for UI; with the probe,
`ui` lists tappable objects in the World section, `tap --world <name>` taps by name. Camera drag/zoom: `swipe`/`drag` to drag;
pinch cannot be simulated.

## 10. Choosing cases under time pressure (risk-based)

Never cut randomly. Rank cases by **impact × likelihood of failure**, run top-down, skipped cases are recorded as `skip` with a
reason:

| Rank | Run first | Why |
|---|---|---|
| 1 | Cases touching **real money / soft currency / saves** (purchases, big rewards, restart after a transaction, `build update`) | wrong = the player loses money, unrecoverable |
| 2 | Cases in **recently changed areas** — `git log --since=14.days -- <feature folder>` + open BugHub bugs of the feature | new code fails most; old bugs tend to come back |
| 3 | Cases in areas `QaProfile.md` marks **"failure-prone"** | this game's own history |
| 4 | Remaining P0 happy paths + P1 boundaries of the main flow | foundational, cheap |
| 5 | P2 interruptions on popups with transactions | spam/kill + money is the most dangerous combination |
| 6 | P4 visual sweep: screens **not** in `covered-by-autotest` first, one pseudo-loc pass | autotest already covered the rest |
| 7 | Environment matrix `--quick`, P5 | rarer, bugs usually medium |

Exploratory smoke (no feature given): ranks 1–2 on **every live screen** reachable from the main screen, ≤ 5 minutes per
screen, then `coverage` to see unvisited screens.

## 11. Telling noise apart & writing noise rules (`qa.noise`)

Recurring false audit findings ruin the signal-to-noise ratio — but an overly broad noise rule hides real bugs. Rules:

| Finding looks like | Usually noise when | A bug when |
|---|---|---|
| `blocked` / "covered" | a popup/backdrop is open covering ≥ 1/3 of the screen (probe reports `occluded`); the element sits under a temporary loading layer | no popup is open yet the button does not take taps; an invisible covering layer (stray raycast target) |
| `offscreen` | the element is sliding in (animation, `burst` shows it moving in); a hidden panel deliberately placed offscreen | stays offscreen after `wait --settle`; a button the player needs to tap |
| `text-overflow` | text is counting up, scaling in an animation; text deliberately overflowing as an effect | static text still overflows; worse under pseudo-loc/font-scale |
| `missing-sprite` / white Image | an intentional white panel (flat color, no sprite needed) matching the ArtStyle kit | a white cell where an icon/image must be |
| `tiny-target` < 6 mm | decor element with raycast off; buttons in a dense scrolling list by design | a main button the player must tap |
| `text-content` (key/NaN/`{0}`) | key deliberately shown on a debug/cheat screen | any screen the player sees |
| `deadButton` (probe 1.3) | button gets its handler from code at runtime after the probe scanned (check by really tapping: it responds → noise); a button in an unused prefab template | a real tap gets no response |
| `noRaycastTarget` (probe 1.3) | interactive element deliberately with raycast off to let taps through (e.g. a label on a parent button) | a button/cell the player must tap directly |
| `overlapButton` (probe 1.3) | two overlapping buttons, only one active at a time (toggle on/off) — `burst` shows the switch | both tappable at the same time |
| `missingSprite` / `noSprite` (probe 1.3) | intentional flat-color panel matching the ArtStyle kit; transparent mask/hitbox | a white cell where an icon/image must be |
| `solidBackground` (probe 1.3) | intentional popup backdrop (Info by default) | a full-screen background with no popup open (a leftover covering layer) |

Write a rule when **the same flag + the same element/screen is wrong ≥ 2 times** and you have looked at the image and
confirmed it is not a bug. Add it to `.claude/project-profile.json` → `qa.noise[]`:

```json
{ "flag": "blocked", "match": "screen:Shop/.*/Backdrop", "reason": "intentional popup backdrop covering the whole screen", "addedBy": "auto-test-device", "date": "2026-01-15" }
```

- `match` is a regex on the element path (`screen:<Screen>/<path>`); as narrow as possible — one element or a family of
  same-named elements, no `.*` over the whole screen unless the reason holds for the whole screen (e.g. a backdrop).
- `reason` is enough for the next dev/agent to understand why it is not a bug. One line.
- A wrong rule (hiding a real bug) → **delete** the rule + log the bug, note it in `QaProfile.md`.
- `suppressed` findings still appear in the report (counted separately); `audit --include-suppressed` to review them
  periodically.
- Do not write rules for: noise of a single session (emulator without a GPU), findings seen only once, tool faults (old probe).

## 12. Recording knowledge after a session — `QaProfile.md`, charter, profile

The machine-read part (`liveness.observed`, `verified.cheats`, `verified.statePaths`) is flushed by `stop` itself. The part you
write yourself (Edit, before `stop`), templates for each section of `QaProfile.md` (section placement follows the file's
skeleton):

```markdown
### Cheats / condition setups that worked
- `<cheatMethodPrefix>AddGold(1000)` — via `call`, cheat-gate gate; `<cheatMethodPrefix>SkipTutorial` only applies after a restart. (2026-01-15) ← example names, replace with the game's real cheats

### Failure-prone screens / systems
- Shop → pack popup: the Buy button accepts a double tap on a slow network (bugs #41, #57). Always run P2 spam here.
- Daily reward: the timer uses the device clock, `env clock +1d` allows claiming again (gd #60 waiting for the GD).

### Dead screens / not tested
- `GiftCode`, `Admin`: enum entries remain, no entry path from the UI (liveness 2026-01-15) — autotest `Feature.Dead` already lists them.

### Noise rules added (and why)
- `blocked` on `screen:Shop/.*/Backdrop` — intentional backdrop.

### Template convention mismatches fixed in `qa.conventions`
- The screen base class is `UIScreenBase` (not `FeatureBaseController`); cheat type `DebugCheats`.
```

- Short, dated, with bug numbers where relevant. No case results (they live in the session report).
- Charter: add cases found during exploration tours, fix wrong oracles, mark cases "bỏ — lý do" (dropped — reason).
- The `Status` of `QaProfile.md` stays `draft` until the dev approves it as `reviewed` — you never change it yourself.
- Never write into the skill, never write only into personal memory.

## 13. Using Editor autotest results (Δ-skip) correctly

`autotest-import` summarizes the report of `com.ezg.autotest` (Smoke opening every screen, UI Audit, Button Sweep, Economy,
Visual, Static). Read `autotest-summary.md`, then:

- **Already passed in autotest** (screen opens, no UI Audit finding, visual diff unchanged) → on the device only confirm
  quickly by eye when passing through; spend the time on logic/interruptions/environment — what autotest cannot do.
- **Autotest reports errors in this feature** → check on the real device: real → bug (write "autotest cũng báo" in the
  evidence, do not log twice if autotest already pushed it to BugHub); not seen → write "autotest báo nhưng không tái hiện
  trên máy" in the report (an Editor/device difference is useful information for the dev).
- **Autotest < 0.2.0** has no liveness: Critical on screens with no entry path, Major MissingReference on assets that never
  load → ignore them if the screen/asset is not live in `qa.liveness`; write one line "noise autotest 0.1.x" in the report.
- **Autotest 0.2.0** writes `Feature.Dead` → copy into the dead-screens section of `QaProfile.md` (if not there yet), do not
  test them.
- No report → infer nothing, test fully; remind the dev in the report to run autotest.
