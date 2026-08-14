# L Task Template

Use for: cross-cutting work — new feature system spanning multiple modules, new IAP/purchase flow, new backend surface, save data migration, new skill system integration, or 9+ files.

Filename: `backlog/todo/NNN-L-short-slug.md`

---

### [PRIORITY] Short output-focused title (≤10 words)

**Tier:** L
<!-- Source of truth for tier. run-backlog reads this line first; the BACKLOG.md bullet `[Tier]` is a mirror. Never change after capture. -->

<!-- HYBRID workflow-backed only (scaffold + cross-cutting custom logic). Omit both lines for a normal L task.
**Backed by workflow:** `/new-package`
**Workflow args:** `MegaGemPack: Weekly mega gem bundle`
run-backlog loads the workflow first, then implements the cross-cutting delta planned below. -->

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
3–6 sentences explaining CLEARLY what needs to be done, why, and the cross-cutting scope. State important business rules / gameplay rules / balance formulas here. For a HYBRID task, describe ONLY the custom cross-cutting logic beyond the scaffold — the scaffold is the workflow's job.

**Context & Constraints:**
- Pattern to follow: <e.g. extend `FeatureBaseController`, `UIManager`, `TigerForge`, `UniTask`, `DataPlayer`, DOTween, CSV config>
- Files that must not be changed: <or "none">
- Behavior that must be preserved: <which features must not break>
- External dependencies: <Cloudflare Worker endpoint, Supabase table, Google Sheet localize key, etc.>

**Related files:**
- `<sourceRoot>/path/to/File1.cs` — reason
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

**Completion criteria** (each criterion has an inline verify recipe):
- [ ] Functional criterion 1 | Verify: open scene X, do Y, confirm Z
- [ ] Functional criterion 2 | Verify: ...
- [ ] Regression: [specific feature name] still works | Verify: replay that flow in the Editor
- [ ] [CHEAT] (when the tag applies) Cheats `<Btn1>` / `<Btn2>` exist under `CheatMenu/Menu` and drive `<the state a tester cannot reach>` | Verify: Play Mode, open the feature, tap the cheat button, confirm the state changes with no Console error
- [ ] Compiles in Unity (no CS#### errors) | Verify: open Unity Editor, check Console
- [ ] No violations of rules in `.claude/rules/` | Verify: quick manual code review
- [ ] [CONSOLE] Unity Console has no new red errors or yellow warnings during the full flow | Verify: Play end-to-end through all phases, check Console

**Guardrails:** <list ONLY the applicable tags from the task-planner's `applicable_guardrails`, space-separated — definitions + verify recipes live in `.claude/backlog-templates/_GUARDRAILS.md`. e.g. `[SAVE] [ASYNC] [BACKEND-SECURITY]`. Available tags: PATTERN, UI, TIME, SAVE, ASYNC, LOCALIZE, EVENT, DOTWEEN, DOUBLE-SUBMIT, LOADING/COOLDOWN, BOUNDARY, PERSIST-RESTART, MOBILE-PERF, ANDROID-BUILD, BACKEND-SECURITY, CSV-CONFIG, CHEAT. For L, `[SAVE]` includes the existing-user migration plan. Do NOT paste the full block text — reviewers look tags up in the catalog.>

<!-- [CHEAT] is a DECISION, not an optional extra: either the tag is here with a matching completion criterion,
     or `**Guardrails skipped:**` carries `cheat (<reason>)`. Silence = planning fail. An L task usually spans
     several gated states (time, progression, currency) — each one a tester cannot reach in a few taps wants a
     cheat. Pattern + design (mirror Features/System/GameCheat): .claude/skills/feature-cheat/SKILL.md -->


**Guardrails skipped:** <only call out a guardrail you deliberately excluded that a reader might expect, + reason ≥10 chars each; else "none". Do NOT enumerate every unused tag.>

**Required verification steps after loop stops (manual):**
1. <Step 1 — happy path phase 1: open scene X, do Y, confirm Z>
2. <Step 2 — happy path phase 2/3/4>
3. <Step 3 — edge case>
4. <Step 4 — regression check on related features>
5. Build Android APK, test on a real device

If any verify step fails, do NOT merge `agent/dev` into the base branch captured when the loop started (`git config "$(python3 .claude/scripts/project_profile.py gitConfigPrefix).agentBaseBranch"` mirrors it for reporting). Write a new fix task in `backlog/todo/`.
