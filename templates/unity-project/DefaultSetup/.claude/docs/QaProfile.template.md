<!--
QaProfile.template.md — shared FRAME, shipped with the template's AI kit (Feature Hub can update this file).
Do NOT put game values here. The project's real file is `.claude/docs/QaProfile.md`:
  - bootstrap.sh / bootstrap.ps1 copy this frame to QaProfile.md when the project has none;
  - `qadevice.py profile init` (skill /auto-test-device) also creates it, auto-detects the code conventions and fills the
    <!-- auto:…:start/end --> regions — text OUTSIDE the auto regions belongs to the dev/agent; the tool never overwrites it.
QaProfile.md is a PROJECT file: Feature Hub does not overwrite it; commit it with the repo. The machine-readable copy of
the same content is the "qa" block in `.claude/project-profile.json` (`profile show`); if they drift → `profile init`
refreshes the auto regions.
Keep the section names + NUMBERS (§0 … §11): the shared skill refers to sections by number.
`<Tên game>` in the H1 is a placeholder token the tool replaces with the product name — keep it verbatim.
-->
# QA Profile — <Tên game>

**Status:** template
<!-- template = not filled in (the QA skill must run § Bootstrap / `profile init` first) · draft = auto-detected by the
     tool, not yet reviewed by a dev (usable, but the report must flag it) · reviewed = read and fixed by a dev (source of truth) -->
**Updated:** <YYYY-MM-DD> · **Reviewer:** <dev>

## 0. What this file is

The **single** source of truth on how to test this game, for the QA agent (`/auto-test-device`) and devs: the code
conventions the tool relies on (screen enum, UI base class, cheat, PlayerData, CSV, tracking), which screens are live /
retired, verified cheats and state paths, confirmed noise rules, environments to try, genre-specific test focus, and
session history.

**Precedence on conflict:** the dev's words in the task → this file (`reviewed`) → the `qa` block in
`project-profile.json` → auto-detection per EZG template conventions → the skill's defaults. The skill holds **no**
game-specific values; anything missing goes here, not into the skill.

## 1. Bootstrap (while Status is still `template`)

1. `python3 .claude/skills/auto-test-device/scripts/qadevice.py profile init` — auto-detects conventions, fills
   §2/§3/§5/§7/§8/§10, sets Status → `draft`, creates `.claude/docs/qa/charters/README.md`.
2. `… profile sync` — pushes the conventions to `Assets/Resources/EzgQaProbeConfig.json` (the probe in the build) and to
   the `liveness` block of `ProjectSettings/EZGAutoTestSettings.json` (Editor autotest skips dead screens).
3. `… doctor --strict` — every convention must resolve to a real file in the code.
4. A dev reads §2 (wrong detection → fix in `project-profile.json["qa"].conventions`, then rerun `profile init`), fills
   §4, §6, §9 and sets Status → `reviewed`.

## 2. Code conventions (auto-detected — edit `project-profile.json["qa"].conventions`, not this table)

<!-- auto:conventions:start -->
| Key | Value | Source | Evidence |
|---|---|---|---|
| (`profile init` not run yet) | | | |
<!-- auto:conventions:end -->

Dev notes on conventions (wrong detections, exceptions, screens that don't follow the standard):
- <e.g.: the Battle screen does not inherit FeatureBaseController; it opens via its own scene>

## 3. Screens / features: live – dead – observed

The tool classifies from the Features enum + static references + screens seen open at runtime. **Dead** = matches a
pattern (`liveness.deadFeaturePatterns`) or `forceDead` → autotest and the skill skip it. **No code reference found** is
not necessarily dead (it may open via a prefab / serialized enum) — confirm by observation or with `forceLive`/`forceDead`.

<!-- auto:features:start -->
- (`profile init` not run yet)
<!-- auto:features:end -->

Why the dev set `forceLive` / `forceDead` (so the next person doesn't ask again):
- <Feature> → <live|dead> because <reason, date>

## 4. Entry paths & unlock conditions of the main screens (filled by dev/agent)

| Screen / feature | Entry path from Home | Unlock condition | Notes |
|---|---|---|---|
| <Shop> | <bottom-right button> | <none> | |
| <Feature X> | <…> | <level ≥ N / tutorial done> | <quick-open cheat: …> |

## 5. Verified cheats & state paths (written by the tool on `stop`)

<!-- auto:verified:start -->
| Kind | Name / path | Last OK |
|---|---|---|
| — | none yet | |
<!-- auto:verified:end -->

Cheats commonly used in testing (dev suggestions — static method name, parameters):
- <`Cheat_AddGold(long)`> — <note>
- <`Cheat_SetLevel(int)`> — <note>

## 6. Oracles & data sources

- GDD: <link / path to the readable spec>; TechSpec: <path>
- CSVs that define the numbers: <`CoinPack.csv` → gem pack prices; `UnlockFeature.csv` → screen unlock milestones; …> (`qadevice.py csv <name>`)
- Formulas to check by hand (not in any CSV): <e.g.: offline income = rate × seconds × 0.2, capped at 8h>
- Tracking events required per flow (`qadevice.py track --expect <event>`): <buy a pack → `iap_success`; …>

## 7. Confirmed noise rules (audit findings to skip)

Each rule: `flag` (finding name or `*`), `match` (regex on the element's path/text, or `screen:<screen name>`), reason,
who added it, date. Add it to `project-profile.json["qa"].noise`, then run `profile init` to refresh the table. Only add
one after **looking at the screenshot and confirming it is not a bug** (e.g. a full-screen backdrop button covered by a
popup is by design).

<!-- auto:noise:start -->
| Flag | Match | Reason | By / date |
|---|---|---|---|
| — | no rules yet | | |
<!-- auto:noise:end -->

## 8. Environments & devices

<!-- auto:environments:start -->
- (`profile init` not run yet)
<!-- auto:environments:end -->

- Available test devices (serial/AVD, who holds it, already on the cheat-gate list?): <…>
- Where to get builds (CI/AAB/APK, install via Play or adb): <…>
- Emulator caveats (test ads won't close, GPU…): <…>

## 9. Game-specific test focus

- Genre / core loop → mandatory P0 cases every session: <…>
- High-risk areas (often break on change): <…>
- Not a bug (by design, agreed with GD): <…>
- Known bugs awaiting a fix (don't log again — dedup via BugHub): <#id — short description>

## 10. Paths

<!-- auto:paths:start -->
- (`profile init` not run yet)
<!-- auto:paths:end -->

## 11. Session history (agent appends 1 row at the end of each session)

| Date | Feature | Device | Bug | Coverage | Notes |
|---|---|---|---|---|---|
| <YYYY-MM-DD> | <feature> | <device> | <#…> | <n/m cases · k screens> | <lessons learned / needs a dev decision> |
