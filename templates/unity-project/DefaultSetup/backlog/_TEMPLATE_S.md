# S Task Template

Use for: single-file logic tweak, small bug fix in ≤2 files. No new UI screen / save field / event.

Filename: `backlog/todo/NNN-short-slug.md`

---

### [PRIORITY] Short output-focused title (≤10 words)

**Description:**
2–3 sentences explaining what needs to be done and why. No vague words ("improve", "optimize") — must have concrete criteria.

**Context & Constraints:**
Pattern to follow / files that must not be changed / behavior that must be preserved. If none → "Follow conventions in `.agents/rules/`."

**Related files:**
- `Assets/_Project/path/to/File1.cs` — reason
- `Assets/_Project/path/to/File2.cs` — reason (max 2 files)

**Acceptance criteria:**
- [ ] Functional criterion 1 (observable in Editor/build) | Verify: specific check
- [ ] Functional criterion 2 | Verify: ...
- [ ] Regression: [specific feature name] still works correctly | Verify: replay that flow in the Editor
- [ ] Compiles in Unity (no CS#### errors) | Verify: open Unity Editor, check Console
- [ ] No violations of rules in `.agents/rules/` | Verify: quick manual code review

**Conditional guardrails** (include only the blocks that apply to this task):
- [ ] [DOUBLE-SUBMIT] Tapping the action button twice fast → only 1 result produced | Verify: tap fast in Play Mode
- [ ] [LOADING/COOLDOWN] Button is disabled or has cooldown while async is running | Verify: tap fast, confirm no second submission
- [ ] [BOUNDARY] Empty input / extreme values / missing data key → no crash, falls back to safe default | Verify: enter boundary values
- [ ] [CONSOLE] Unity Console has no new red errors or yellow warnings during the flow | Verify: Play scene, check Console

**Guardrails skipped:** <list skipped guardrails + reason ≥10 chars each, or "none">
