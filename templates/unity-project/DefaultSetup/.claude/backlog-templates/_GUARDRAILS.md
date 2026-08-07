# Guardrail Catalog

Shared definitions for the guardrail tags referenced by task templates. A task file lists
only the **tags** that apply (e.g. `**Guardrails:** [SAVE] [ASYNC] [LOCALIZE]`); the full
checklist + verify recipe lives here so it is NOT duplicated into every task file (and from
there into every reviewer prompt). Reviewers + qa-verifier read the tag → look it up here.

The underlying rules are in `.claude/rules/` (`code-style`, `core-system`, `data-persistence`,
`third-party`). This catalog is the task-facing checklist view of those rules.

| Tag | Check | Verify recipe |
|---|---|---|
| `[PATTERN]` | New UI extends `FeatureBaseController`; new Notification extends `BaseNotification`. | Check class declaration. |
| `[UI]` | Uses `UIManager.Show/Hide`, NOT `gameObject.SetActive()` on feature UI. Feature prefab leaves `MainUI` **unassigned** on the controller (default fallback = `Popup` → popup scales in, full-screen fades in); assigning it, esp. `MainUI` = `FullScreen`, breaks the open animation convention. | grep `SetActive` in new files; `grep -n "MainUI:" <prefab>` → `{fileID: 0}`. |
| `[TIME]` | All time ops use `TimeManager`, NOT `DateTime.Now`. | grep `DateTime.Now` in new files. |
| `[SAVE]` | Save data via `DataPlayer` / `PlayerDataManager.[Module]`; `SetupDefaultData()` fallback; no `Save()` in Update. For L: include a migration plan for existing users. | Check data class + fallback (+ migration plan for L). |
| `[ASYNC]` | Uses `UniTask` (no Coroutine, no `async void`, no plain `Task`). | grep `Coroutine\|async void` in new files. |
| `[LOCALIZE]` | All user-facing text goes through the localize system — no hardcoded strings. **New keys go in via `/add-localize` (which writes the Google Sheet), never by hand-editing `Localization.csv`.** See the note below before touching that file. | grep hardcoded strings in new files; `git diff --staged -- '*Localization.csv'` must show only additions. |
| `[EVENT]` | Cross-system communication via `TigerForge` + `EventName` constants. | grep direct method calls between features. |
| `[DOTWEEN]` | New tweens have `OnComplete`/`Kill`; UI tweens use `SetUpdate(true)`. | Inspect tween calls. |
| `[DOUBLE-SUBMIT]` | Tapping the action button twice fast → only 1 result. | Tap fast in Play Mode. |
| `[LOADING/COOLDOWN]` | Button disabled / has cooldown while async is running. | Tap fast, confirm no second submission. |
| `[BOUNDARY]` | Empty input / extreme values / missing data key → no crash, safe default. | Enter boundary values. |
| `[PERSIST-RESTART]` | Kill app → reopen → saved state restored correctly. | Play, save, Stop, Play again. |
| `[MOBILE-PERF]` | No significant GC alloc increase (>1KB) in the gameplay loop. | Profiler in Play Mode. |
| `[BACKEND-SECURITY]` | Backend writes go through the Cloudflare Worker — no direct Supabase write calls; no credentials in client code. | Inspect call sites. |
| `[ANDROID-BUILD]` | the Android build target compiles successfully; no Editor-only API in runtime code. | Build APK, check no errors. |
| `[CSV-CONFIG]` | New balance numbers / formulas in CSV, no hardcoded magic numbers. | grep magic numbers in new code. |
| `[CHEAT]` | Feature has state a tester cannot reach in a few taps (time gate, progression gate, resource gate, rare/one-shot flow, anything needing a reset) → it ships a dev cheat: `public Cheat_*` methods in a `#region Cheats` on the Controller (`[TabGroup("Cheats")] [Button]`, refresh the UI) + `ButtonNormal` instances under the **inherited** `ButtonCheatMenu/MenuParent` of the feature prefab, wired to those methods. Design must match `DailyLoginV2.prefab` / `Equipment.prefab` — see `.claude/skills/feature-cheat/SKILL.md`. Cheat labels are **not** localized; the chrome is never re-instantiated. | `grep -n "Cheat_" <Controller>.cs`; `grep -n "m_MethodName" <Feature>.prefab` shows each cheat method; Play Mode → open the feature, cheat button visible, every button changes the visible state with no Console error. |
| `[CONSOLE]` | Unity Console has no new red errors or yellow warnings during the full flow. | Play scene end-to-end, check Console. |

> `[CONSOLE]` is an always-on completion criterion (it appears in the **Completion criteria** block of every M/L task, not the conditional guardrails line). The other tags are conditional — a task lists only the ones its `applicable_guardrails` selected.

---

## `Localization.csv` — read this before you touch it

**The Google Sheet is the source of truth; the local CSV is a generated artifact.**
`CsvImportManager.BuildLocalizationCsvContent` rewrites `<featuresRoot>/Misc/CsvConfig/Localization.csv`
**wholesale** from the Sheet. Any key that exists only in the local file is therefore deleted the
next time anyone re-imports — silently, with no error and no console warning.

This has already cost us twice: `backlog/done/104`, then **144 already-shipped keys** in
`backlog/done/116` (`#dungeon_lobby_*`, `#dungeon_leaderboard_*`, `#dungeon_mastery_*`,
`#elemental_dungeon_bundle`, `#item_304`, `#mod_*` …), which would have broken every Dungeon
feature built in tasks 096–115.

**Rules:**

1. **Add new keys with `/add-localize`.** It writes the Sheet (columns A–R, with `GOOGLETRANSLATE`
   formulas for the other languages). A key added only to the local CSV is a key on a timer.
2. **Never re-download / "Force Reload all data CSV" as a way to pick up your own new key.** If you
   must re-import, treat it as a merge: keep the freshly downloaded rows, then re-append the
   local-only ones from `git show HEAD:<file>`, and diff the key sets before staging.
3. **No `%` immediately before a comma.** The importer escapes `,` as `%%` and
   `LocalizationCollection` unescapes with a left-to-right `Replace("%%", ",")`, so `40%,` is stored
   as `40%%%` and decodes as `40,%`. Reword instead — `"shrinks 40% in area,"`. (13 pre-existing
   rows still carry this artifact; fixing the escape scheme itself is a separate task.)

**Enforcement:** `backlog-preflight` implements both checks deterministically and blocks the run —
`localize-key-loss` (any key in HEAD missing from the staged file) and `localize-escape-collision`
(a `%%%` run on an *added* line). Both are `severity=critical, confidence=definite`, so they trip
`has_blocking_definite` and stop the task before the LLM reviewers ever spawn. Keep the `.py` and
`.ps1` twins in lockstep when editing either rule.
