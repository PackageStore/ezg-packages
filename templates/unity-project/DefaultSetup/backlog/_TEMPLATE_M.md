# M Task Template

Use for: multi-file feature, new UI screen/popup, new controller, new save field, new TigerForge event. 3–8 files.

Filename: `backlog/todo/NNN-short-slug.md`

---

### [PRIORITY] Short output-focused title (≤10 words)

**Description:**
2–5 sentences explaining CLEARLY what needs to be done and why. State important business rules / gameplay rules here.

**Context & Constraints:**
- Pattern to follow: <e.g. extend `FeatureBaseController`, `UIManager.Show/Hide`, `UniTask` async, `TigerForge` + `EventName`, `PlayerDataManager.[Module]` (write) + `DataManager` (read-only config), DOTween `SetUpdate(true)` for UI tweens>
- Files that must not be changed: <or "none">
- Behavior that must be preserved: <which features must not break>

**Related files:**
- `Assets/_Project/path/to/File1.cs` — reason
- `Assets/_Project/path/to/File2.cs` — reason
- (3–8 files)

**Acceptance criteria** (each criterion has an inline verify recipe):
- [ ] Functional criterion 1 | Verify: open scene X, do Y, confirm Z
- [ ] Functional criterion 2 | Verify: ...
- [ ] Regression: [specific feature name] still works | Verify: replay that flow in the Editor
- [ ] Compiles in Unity (no CS#### errors) | Verify: open Unity Editor, check Console
- [ ] No violations of rules in `.agents/rules/` | Verify: quick manual code review
- [ ] [CONSOLE] Unity Console has no new red errors or yellow warnings during the full flow | Verify: Play scene end-to-end, check Console

**Guardrails — include only the applicable_guardrails blocks from the Plan subagent:**
- [ ] [PATTERN] New UI extends `FeatureBaseController`; new Notification extends `RedDotBadge` | Verify: check class declaration
- [ ] [UI] Uses `UIManager.Show/Hide`, NOT `gameObject.SetActive()` directly | Verify: grep `SetActive` in new files
- [ ] [TIME] All time operations use `TimeManager`, NOT `DateTime.Now` | Verify: grep `DateTime.Now` in new files
- [ ] [SAVE] Save data goes through `PlayerDataManager.[Module]`; has `SetupDefaultData()` fallback; no `Save()` in Update | Verify: check data class + fallback
- [ ] [ASYNC] Uses `UniTask` (no Coroutine, no async void, no plain Task) | Verify: grep `Coroutine\|async void` in new files
- [ ] [LOCALIZE] All user-facing text goes through the localize system — no hardcoded strings | Verify: grep hardcoded strings in new files
- [ ] [EVENT] Cross-system communication via `TigerForge` with `EventName` constants | Verify: grep direct method calls between features
- [ ] [DOTWEEN] New tweens have `OnComplete`/`Kill`; UI tweens use `SetUpdate(true)` | Verify: inspect tween calls
- [ ] [DOUBLE-SUBMIT] Tapping action button twice fast → only 1 result | Verify: tap fast in Play Mode
- [ ] [LOADING/COOLDOWN] Button is disabled while async is running | Verify: tap fast, confirm no second submission
- [ ] [BOUNDARY] Empty input / extreme values / missing data key → no crash | Verify: enter boundary values
- [ ] [PERSIST-RESTART] Kill app → reopen → saved state restored correctly | Verify: Play, save, Stop, Play again
- [ ] [MOBILE-PERF] No significant GC alloc increase (>1KB) in the gameplay loop | Verify: Profiler in Play Mode
- [ ] [CSV-CONFIG] New balance numbers / formulas placed in CSV, no hardcoded magic numbers | Verify: grep magic numbers in new code

**Guardrails skipped:** <list skipped guardrails + reason ≥10 chars each, or "none">

**Manual verify steps (required after the loop stops):**
1. <Step 1 — happy path: open scene X, do Y, confirm Z>
2. <Step 2 — edge case>
3. <Step 3 — regression check>
4. (if needed) Build Android APK, test on a real device

If any verify step fails, do NOT merge `agent/dev` into `develop`. Write a new fix task in `backlog/todo/`.
