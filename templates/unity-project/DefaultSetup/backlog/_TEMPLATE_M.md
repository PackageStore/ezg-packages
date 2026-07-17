# M Task Template

Use for: multi-file feature, new UI screen/popup, new controller, new save field, new TigerForge event. 3–8 files.

Filename: `backlog/todo/NNN-M-short-slug.md`

---

### [PRIORITY] Short output-focused title (≤10 words)

**Tier:** M
<!-- Source of truth for tier. run-backlog reads this line first; the BACKLOG.md bullet `[Tier]` is a mirror. Never change after capture. -->

<!-- HYBRID workflow-backed task? If this is a /new-* scaffold PLUS custom logic, uncomment the
     two lines below so run-backlog (STEP 5.0) runs the command for the scaffold, then describe
     ONLY the extra logic/wiring/balance below. See backlog/_TEMPLATE_WF.md for the registry.
**Backed by workflow:** /new-feature      (or /new-ui)
**Workflow args:** FeatureName: Description
-->

<!-- Batch / design-pipeline optional fields (fill when applicable, else DELETE this whole comment).
Field names inside this comment are deliberately UN-bolded so comment-blind parsers (backlog-ops
DEPENDS_RE, run-backlog Requires-gate) never match a leftover template comment. When filling,
write them bolded on real lines directly under the title block:
Context docs: `TechSpec/<Name>-Implementation.md` — design doc with the concrete values the implementer must read
Depends on: `<planning filename(s) / task NNN(s) this task builds on>`
Requires: `unity-editor` — only when the task cannot run headless (prefab authoring)
Needs mockup: yes — HYBRID task that builds a NEW screen not backed by /new-ui; /ui-mockup
  sweeps this flag to draft+approve a wireframe before the task is promoted
-->

**Required skills:** <none, or `/create-ui`, `/compile-check`>

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

**UI criteria — include only when `Required skills` contains `/create-ui`:**
- [ ] Follow `/create-ui`: read `.claude/skills/create-ui/SKILL.md`, `references/prefab-templates.md`, and `references/mcp-playbook.md` before prefab work | Verify: task summary names the template and MCP playbook path used
- [ ] Reuse shared prefab templates; do not build production UI from blank raw GameObjects when a matching template exists | Verify: inspect prefab hierarchy for nested template instances
- [ ] For root screens/popups, create a variant from `Popup_Template/screen_template`, attach the correct `FeatureBaseController` subclass, and preserve child order (`child[0]` background, `child[1]` MainUI) | Verify: inspect prefab root and first two children
- [ ] Wire serialized references (close buttons, labels, content containers, tab lists, preview controllers) | Verify: reopen prefab, no missing references in Inspector
- [ ] Register/open through `UIManager.Show(...)` when the UI is a feature screen | Verify: open in Play Mode through the real feature enum and capture a screenshot
- [ ] Screenshot-verify and self-correct after meaningful layout chunks | Verify: final report includes the screenshot check result and any corrections made

**Guardrails:** <list ONLY the applicable tags, space-separated — definitions + verify recipes in `backlog/_GUARDRAILS.md`. e.g. `[SAVE] [ASYNC] [LOCALIZE]`. Available tags: PATTERN, UI, TIME, SAVE, ASYNC, LOCALIZE, EVENT, DOTWEEN, DOUBLE-SUBMIT, LOADING/COOLDOWN, BOUNDARY, PERSIST-RESTART, MOBILE-PERF, CSV-CONFIG, CONSOLE.>

**Guardrails skipped:** <only call out a guardrail you deliberately excluded that a reader might expect, + reason ≥10 chars; else "none">

**Manual verify steps (required after the loop stops):**
1. <Step 1 — happy path: open scene X, do Y, confirm Z>
2. <Step 2 — edge case>
3. <Step 3 — regression check>
4. (if needed) Build Android APK, test on a real device

If any verify step fails, do NOT merge `agent/dev` into `develop`. Write a new fix task in `backlog/todo/`.
