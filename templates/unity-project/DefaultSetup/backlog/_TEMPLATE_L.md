# L Task Template

Use for: cross-cutting work — new feature system spanning multiple modules, new IAP/purchase flow, save data migration, new system integration, or 9+ files.

Filename: `backlog/todo/NNN-short-slug.md`

---

### [PRIORITY] Short output-focused title (≤10 words)

**Description:**
3–6 sentences explaining CLEARLY what needs to be done, why, and the cross-cutting scope. State important business rules / gameplay rules / balance formulas here.

**Context & Constraints:**
- Pattern to follow: <e.g. extend `FeatureBaseController`, `UIManager`, `TigerForge`, `UniTask`, `PlayerDataManager.[Module]`, DOTween, CSV config>
- Files that must not be changed: <or "none">
- Behavior that must be preserved: <which features must not break>

**Related files:**
- `Assets/_Project/path/to/File1.cs` — reason
- ... (9+ files — group by module if many)

**Phases** (split task into ≤4 sequential sub-steps, each with a clear checkpoint):
1. **Phase 1: [name]** — <description>. Checkpoint: <observable result in Editor before moving to phase 2>
2. **Phase 2: [name]** — <description>. Checkpoint: ...
3. **Phase 3: [name]** — <description>. Checkpoint: ...
4. **Phase 4: [name]** — <description>. Checkpoint: ...

**Risks** (cross-cutting impact + what could break):
- <Risk 1>: <mitigation>
- <Risk 2>: <mitigation>
- <Risk 3>: <mitigation>

**Acceptance criteria** (each criterion has an inline verify recipe):
- [ ] Functional criterion 1 | Verify: open scene X, do Y, confirm Z
- [ ] Functional criterion 2 | Verify: ...
- [ ] Regression: [specific feature name] still works | Verify: replay that flow in the Editor
- [ ] Compiles in Unity (no CS#### errors) | Verify: open Unity Editor, check Console
- [ ] No violations of rules in `.agents/rules/` | Verify: quick manual code review
- [ ] [CONSOLE] Unity Console has no new red errors or yellow warnings during the full flow | Verify: Play end-to-end through all phases, check Console

**Guardrails — include only the applicable_guardrails blocks from the Plan subagent:**
- [ ] [PATTERN] New UI extends `FeatureBaseController`; new Notification extends `RedDotBadge` | Verify: check class declaration
- [ ] [UI] Uses `UIManager.Show/Hide`, NOT `gameObject.SetActive()` | Verify: grep `SetActive` in new files
- [ ] [TIME] All time operations use `TimeManager`, NOT `DateTime.Now` | Verify: grep `DateTime.Now`
- [ ] [SAVE] Save data goes through `PlayerDataManager.[Module]`; has `SetupDefaultData()` fallback; no `Save()` in Update | Verify: check fallback + data migration plan for existing users
- [ ] [ASYNC] Uses `UniTask` (no Coroutine, no async void) | Verify: grep `Coroutine\|async void`
- [ ] [LOCALIZE] All user-facing text goes through the localize system | Verify: grep hardcoded strings
- [ ] [EVENT] Cross-system communication via `TigerForge` + `EventName` | Verify: grep direct method calls between features
- [ ] [DOTWEEN] Tweens have `OnComplete`/`Kill`; UI tweens use `SetUpdate(true)` | Verify: inspect tween calls
- [ ] [DOUBLE-SUBMIT] Tapping action button twice fast → only 1 result | Verify: tap fast in Play Mode
- [ ] [LOADING/COOLDOWN] Button is disabled while async is running | Verify: tap fast, confirm no second submission
- [ ] [BOUNDARY] Empty input / extreme values / missing data key → no crash | Verify: enter boundary values
- [ ] [PERSIST-RESTART] Kill app → reopen → saved state restored correctly | Verify: Play, save, Stop, Play again
- [ ] [MOBILE-PERF] No significant GC alloc increase in the gameplay loop | Verify: Profiler in Play Mode
- [ ] [CSV-CONFIG] New balance numbers / formulas placed in CSV, no hardcoded magic numbers | Verify: grep magic numbers

**Guardrails skipped:** <list skipped guardrails + reason ≥10 chars each, or "none">

**Manual verify steps (required after the loop stops):**
1. <Step 1 — happy path phase 1: open scene X, do Y, confirm Z>
2. <Step 2 — happy path phase 2/3/4>
3. <Step 3 — edge case>
4. <Step 4 — regression check on related features>
5. Build Android APK, test on a real device

If any verify step fails, do NOT merge `agent/dev` into `develop`. Write a new fix task in `backlog/todo/`.
