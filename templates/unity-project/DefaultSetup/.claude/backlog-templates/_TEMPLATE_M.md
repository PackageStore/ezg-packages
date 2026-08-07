# M Task Template

Use for: multi-file feature, new UI screen/popup, new controller, new save field, new TigerForge event. 3–8 files.

Filename: `backlog/todo/NNN-M-short-slug.md`

---

### [PRIORITY] Short output-focused title (≤10 words)

**Tier:** M
<!-- Source of truth for tier. run-backlog reads this line first; the BACKLOG.md bullet `[Tier]` is a mirror. Never change after capture. -->

<!-- HYBRID workflow-backed only (scaffold + custom logic). Omit both lines for a normal M task.
**Backed by workflow:** `/new-feature`
**Workflow args:** `WinStreak: Reward system for consecutive wins`
run-backlog loads the workflow first, then applies the custom logic planned below. -->

<!-- Batch / design-pipeline optional fields (fill when applicable, else omit — see _TEMPLATE_WF.md for semantics).
Field names inside this comment are deliberately UN-bolded so comment-blind parsers (backlog-ops DEPENDS_RE,
run-backlog Requires-gate) never match a leftover template comment. When filling, write them bolded on real lines:
Context docs: `TechSpec/<Name>-Implementation.md` — design doc with the concrete values the implementer must read
Depends on: `<planning filename(s) / task NNN(s) this task builds on>`
Requires: `unity-editor` — only when the task cannot run headless (prefab authoring)
Mockup: groundTruth=PENDING-MOCKUP (screen=<Feature>/<Screen>) — HYBRID/M/L task building a NEW
  screen NOT backed by /new-ui. WIRED marker: /ui-mockup greps the groundTruth= token and
  backlog-ops promote BLOCKS until it resolves (approved .png / clone:<Prefab> / none). Also list
  /new-ui in Required skills. (Legacy `Needs mockup: yes` was a no-op — nothing swept it.)
-->

**Required skills:** <none, or `/compile-check` for code-touching tasks, `/new-ui` for prefab work>

**Description:**
2–5 sentences explaining CLEARLY what needs to be done and why. State important business rules / gameplay rules here. For a HYBRID task, describe ONLY the custom logic beyond the scaffold — the scaffold is the workflow's job.

**Context & Constraints:**
- Pattern to follow: <e.g. extend `FeatureBaseController`, `UIManager.Show/Hide`, `UniTask` async, `TigerForge` + `EventName`, `DataPlayer` via `PlayerDataManager.[Module]`, DOTween `SetUpdate(true)` for UI tweens>
- Files that must not be changed: <or "none">
- Behavior that must be preserved: <which features must not break>

**Related files:**
- `<sourceRoot>/path/to/File1.cs` — reason
- `<sourceRoot>/path/to/File2.cs` — reason
- (3–8 files)

**Completion criteria** (each criterion has an inline verify recipe):
- [ ] Functional criterion 1 | Verify: open scene X, do Y, confirm Z
- [ ] Functional criterion 2 | Verify: ...
- [ ] Regression: [specific feature name] still works | Verify: replay that flow in the Editor
- [ ] [CHEAT] (when the tag applies) Cheats `<Btn1>` / `<Btn2>` exist under `ButtonCheatMenu/MenuParent` and drive `<the state a tester cannot reach>` | Verify: Play Mode, open the feature, tap the cheat button, confirm the state changes with no Console error
- [ ] Compiles in Unity (no CS#### errors) | Verify: open Unity Editor, check Console
- [ ] No violations of rules in `.claude/rules/` | Verify: quick manual code review
- [ ] [CONSOLE] Unity Console has no new red errors or yellow warnings during the full flow | Verify: Play scene end-to-end, check Console

**Guardrails:** <list ONLY the applicable tags from the task-planner's `applicable_guardrails`, space-separated — definitions + verify recipes live in `.claude/backlog-templates/_GUARDRAILS.md`. e.g. `[SAVE] [ASYNC] [LOCALIZE]`. Available tags: PATTERN, UI, TIME, SAVE, ASYNC, LOCALIZE, EVENT, DOTWEEN, DOUBLE-SUBMIT, LOADING/COOLDOWN, BOUNDARY, PERSIST-RESTART, MOBILE-PERF, BACKEND-SECURITY, CSV-CONFIG, CHEAT. Do NOT paste the full block text — the tag is enough; reviewers look it up in the catalog.>

<!-- [CHEAT] is a DECISION, not an optional extra: either the tag is here with a matching completion criterion,
     or `**Guardrails skipped:**` carries `cheat (<reason>)`. Silence = planning fail.
     Pattern + design (mirror DailyLoginV2 / Equipment): .claude/skills/feature-cheat/SKILL.md -->


**Guardrails skipped:** <only call out a guardrail you deliberately excluded that a reader might expect, + reason ≥10 chars each; else "none". Do NOT enumerate every unused tag.>

**Required verification steps after loop stops (manual):**
1. <Step 1 — happy path: open scene X, do Y, confirm Z>
2. <Step 2 — edge case>
3. <Step 3 — regression check>
4. (if needed) Build Android APK, test on a real device

If any verify step fails, do NOT merge `agent/dev` into the base branch captured when the loop started (`git config "$(python3 .claude/scripts/project_profile.py gitConfigPrefix).agentBaseBranch"` mirrors it for reporting). Write a new fix task in `backlog/todo/`.
