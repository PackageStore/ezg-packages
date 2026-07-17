# S Task Template

Use for: single-file logic tweak, small bug fix in ≤2 files. No new UI screen / save field / event.

Filename: `backlog/todo/NNN-S-short-slug.md`

---

### [PRIORITY] Short output-focused title (≤10 words)

**Tier:** S
<!-- Source of truth for tier. run-backlog reads this line first; the BACKLOG.md bullet `[Tier]` is a mirror. Never change after capture. -->

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

**Guardrails:** <list ONLY the applicable tags — definitions + verify recipes in `backlog/_GUARDRAILS.md`. Common for S: `[BOUNDARY] [CONSOLE]`, plus `[DOUBLE-SUBMIT] [LOADING/COOLDOWN]` if there is a user-facing mutation. "none" if the change is purely internal.>
