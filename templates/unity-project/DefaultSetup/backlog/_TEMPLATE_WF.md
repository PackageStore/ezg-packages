# WF Task Template — Workflow-Backed

Use for: a task whose implementation is **already specified deterministically by a `/new-*` command** in `.claude/commands/` (scaffold a new feature / UI screen / class). The command IS the plan — so `/planning-task` does NOT spawn the Plan subagent for these. It only captures the command reference, the args, and any custom delta the command does not cover.

**Registry ([Project Name]) — which commands are workflow-backed:**

| Intent | Command | `Workflow args` format |
|---|---|---|
| New feature module (controller + structure under `Assets/_Project/Features/<Domain>/`) | `/new-feature` | `FeatureName: Description` (FeatureName **PascalCase**) |
| New UI screen/popup prefab (delegates to the `create-ui` skill) | `/new-ui` | `FeatureName` |

> **NOT workflow-backed:**
> - `/new-package` — extracts a UPM module and pushes to the **`ezg-packages` monorepo `main`** (out-of-band, no `agent/dev`, no backlog lifecycle). It cannot be a backlog task; run it manually via the `package-module` skill.
> - `/new-class` — it is an S-tier task (the Plan subagent is **already** skipped for S, so WF routing saves no planner tokens) and has **no deterministic checklist** to lift into acceptance criteria. A new-class request is just a normal S task (`_TEMPLATE_S.md`), not WF.
> - There are no `/new-skill` / `/new-enemy-skill` commands in this project.
>
> **Some commands delegate to a skill:** `/new-ui` → `create-ui` skill (`.claude/skills/create-ui/SKILL.md` + its `references/`). The skill's playbook (and its checklist) is the real authority — `run-backlog` STEP 5.0 follows it.

**Important — tier is orthogonal to WF:**
The filename still carries the real execution tier (`<timestamp>-<TIER>-<slug>.md` — `M` for both `/new-feature` and `/new-ui`, or `L` if the feature is cross-cutting). That tier (mirrored into the `[TIER]` bracket of the BACKLOG.md bullet by `/add-to-backlog`) governs **review rigor** in `run-backlog` (which quality gates run). The WF marker only governs that `run-backlog` **loads the command first** instead of implementing free-form. A hybrid task (scaffold + extra custom logic) is "tier M, backed by workflow" — use `_TEMPLATE_M.md` / `_TEMPLATE_L.md`, which also accept the `**Backed by workflow:**` field.

Filename: `backlog/todo/NNN-short-slug.md`

---

### [PRIORITY] Short output-focused title (≤10 words)

**Required skills:** <`/compile-check`; add `/create-ui` when `Backed by workflow` is `/new-ui`>

**Backed by workflow:** `/new-feature`
<!-- one of: /new-feature | /new-ui -->

**Workflow args:** `Achievements: Achievements meta-feature — controller + save module + CSV scaffold`
<!-- the EXACT args string run-backlog will feed the command's {{args}} parser. /new-feature → `FeatureName: Description` (PascalCase); /new-ui → `FeatureName` only. -->

**Description:**
1–2 sentences: what is being scaffolded and why. State the gameplay/business rule the scaffold must satisfy.

**Custom delta (beyond the workflow):**
- <or "none — pure scaffold, the command output is the complete deliverable">
- <anything the command does NOT generate that this task still needs, e.g. "register the feature in `GameEnums.Features`", "wire the open trigger from the HUD", "add balance value Y in CSV", "extra `EventName` Z">

**Acceptance criteria** (lift the command's / delegated skill's checklist, then append delta criteria):
- [ ] Command checklist fully satisfied | Verify: run through the command's (or its delegated skill's, e.g. `create-ui`) checklist item by item
- [ ] <delta criterion 1, if any> | Verify: ...
- [ ] Compiles in Unity (no CS#### errors) | Verify: open Unity Editor, check Console
- [ ] No violations of rules in `.agents/rules/` | Verify: quick manual code review
- [ ] [CONSOLE] Unity Console has no new red errors or yellow warnings | Verify: Play the relevant scene end-to-end

**Guardrails:** <list ONLY the applicable tags — definitions in `backlog/_GUARDRAILS.md`. e.g. `[PATTERN] [UI] [LOCALIZE]`. Available: PATTERN, UI, TIME, SAVE, ASYNC, LOCALIZE, EVENT, DOTWEEN, DOUBLE-SUBMIT, LOADING/COOLDOWN, BOUNDARY, PERSIST-RESTART, MOBILE-PERF, CSV-CONFIG, CONSOLE.>

**Guardrails skipped:** <only call out a guardrail a reader might expect that you deliberately excluded, + reason ≥10 chars; else "none">

**Manual verify steps (required after the loop stops):**
1. <Step 1 — happy path: open scene X, exercise the scaffolded thing, confirm it works>
2. <Step 2 — the custom delta behaves correctly>
3. <Step 3 — regression: the system the scaffold plugs into (feature registry / HUD trigger / CSV) still works>
4. (if needed) Build Android APK, test on a real device

If any verify step fails, do NOT merge `agent/dev` into `develop`. Write a new fix task in `backlog/todo/`.
