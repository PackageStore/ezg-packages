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
Pattern to follow / files that must not be changed / behavior that must be preserved. If none → "Follow conventions in `.claude/rules/`."

**Related files:**
- `<sourceRoot>/path/to/File1.cs` — reason
- `<sourceRoot>/path/to/File2.cs` — reason (max 2 files)

**Completion criteria:**
- [ ] Functional criterion 1 (observable in Editor/build) | Verify: specific check
- [ ] Functional criterion 2 | Verify: ...
- [ ] Regression: [specific feature name] still works correctly | Verify: replay that flow in the Editor
- [ ] Compiles in Unity (no CS#### errors) | Verify: open Unity Editor, check Console
- [ ] No violations of rules in `.claude/rules/` | Verify: quick manual code review

**Guardrails:** <list ONLY the applicable tags, space-separated — definitions + verify recipes live in `.claude/backlog-templates/_GUARDRAILS.md`. For S this is usually a small set, e.g. `[BOUNDARY] [DOUBLE-SUBMIT] [LOADING/COOLDOWN] [CONSOLE]`. If the task has user input → include `[BOUNDARY]`; if a user-facing mutation → include `[DOUBLE-SUBMIT] [LOADING/COOLDOWN]`. Do NOT paste the full block text.>

**Guardrails skipped:** <only call out a guardrail you deliberately excluded that a reader might expect, + reason ≥10 chars each; else "none".>
