# Guardrail Catalog

Shared definitions for the guardrail tags referenced by task templates. A task file lists
only the **tags** that apply (e.g. `**Guardrails:** [SAVE] [ASYNC] [LOCALIZE]`); the full
checklist + verify recipe lives here so it is NOT duplicated into every task file (and from
there into every reviewer prompt). Reviewers + qa-verifier read the tag → look it up here.

The underlying rules are in `.agents/rules/` (`code-style`, `core-system`, `data-persistence`,
`third-party`). This catalog is the task-facing checklist view of those rules.

| Tag | Check | Verify recipe |
|---|---|---|
| `[PATTERN]` | New UI extends `FeatureBaseController`; new Notification extends `RedDotBadge`. | Check class declaration. |
| `[UI]` | Uses `UIManager.Show/Hide`, NOT `gameObject.SetActive()` on feature UI. | grep `SetActive` in new files. |
| `[TIME]` | All time ops use `TimeManager`, NOT `DateTime.Now`. | grep `DateTime.Now` in new files. |
| `[SAVE]` | Save data via `PlayerDataManager.[Module]`; `SetupDefaultData()` fallback; no `Save()` in Update. For L: include a migration plan for existing users. | Check data class + fallback. |
| `[ASYNC]` | Uses `UniTask` (no Coroutine, no `async void`, no plain `Task`). | grep `Coroutine\|async void` in new files. |
| `[LOCALIZE]` | All user-facing text goes through the localize system — no hardcoded strings. | grep hardcoded strings in new files. |
| `[EVENT]` | Cross-system communication via `TigerForge` + `EventName` constants. | grep direct method calls between features. |
| `[DOTWEEN]` | New tweens have `OnComplete`/`Kill`; UI tweens use `SetUpdate(true)`. | Inspect tween calls. |
| `[DOUBLE-SUBMIT]` | Tapping the action button twice fast → only 1 result. | Tap fast in Play Mode. |
| `[LOADING/COOLDOWN]` | Button disabled / has cooldown while async is running. | Tap fast, confirm no second submission. |
| `[BOUNDARY]` | Empty input / extreme values / missing data key → no crash, safe default. | Enter boundary values. |
| `[PERSIST-RESTART]` | Kill app → reopen → saved state restored correctly. | Play, save, Stop, Play again. |
| `[MOBILE-PERF]` | No significant GC alloc increase (>1KB) in the gameplay loop. | Profiler in Play Mode. |
| `[CSV-CONFIG]` | New balance numbers / formulas in CSV, no hardcoded magic numbers. | grep magic numbers in new code. |
| `[CONSOLE]` | Unity Console has no new red errors or yellow warnings during the full flow. | Play scene end-to-end, check Console. |
