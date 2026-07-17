# Phase Spec Revalidation Playbook (manual)

A repeatable manual procedure to fix backlog specs that were **drafted ahead of their dependencies** (i.e. authored on an empty/stub codebase) BEFORE you promote them to `todo/` and let `/run-backlog` execute them.

> **Why this exists:** specs authored upfront against an empty codebase tend to invent inconsistent paths, duplicate deliverables, and reference artifacts nobody creates. Revalidate against the now-real prior-phase code in small dependency-ordered batches immediately before promotion.

---

## When to run

Run for a phase **only after the previous phase is implemented and validated** (so this phase's dependencies are real code, not guesses).

- Prefer just-in-time specs after the previous phase ships. If a batch was drafted ahead, run this playbook before promoting any dependent task.

Do **not** promote a phase's specs to `todo/` until they pass this playbook — an un-validated spec is what blocks the loop.

---

## The 4 checks (per spec)

For each spec file, open it and cross-check against the **current** codebase (`codegraph_explore` / Grep / Read). Assign a verdict:

| Check | Question | Fail → |
|---|---|---|
| **1. Duplicate deliverable** | Does any NEW file/class/CSV it declares already exist, or is it owned by another task (done/todo/planning)? | 🔴 close or re-scope (recreating a class = CS0101 duplicate-type → loop blocks) |
| **2. Stale path / name** | Do its `Related files` paths + class names match where code actually lives now? Or does it assume a tree that doesn't exist? | 🟡 fix the spec paths/names to the real canonical tree |
| **3. Phantom reference** | Does every config/class/accessor it tells the implementer to READ (`DataManager.X`, a CSV, a helper) actually exist or get created by a named upstream task? Are the API method names real? | 🟡 replace with the real owner/API, or add the missing upstream task |
| **4. Dependency reality** | Are its `depends_on` ids real tasks? Are the deps satisfied/ordered before it? Is it hard-blocked on something unbuilt or a design decision? | 🟡 fix dep ids; 🔴 if blocked on a design decision → park in `planning/` with a BLOCKED banner |

**Verdict:** 🟢 GREEN = promote as-is · 🟡 YELLOW = edit spec, then promote · 🔴 RED = close (move to `done/` with a closure note) or park (keep in `planning/` with a BLOCKED banner).

---

## Canonical conventions (the shared truth planners should have used)

When fixing stale paths, follow `.claude/rules/project-structure.md` and the nearest shipped sibling feature. Existing save/config/event/economy/backend services are reference targets, never duplicate deliverables. Any path that still cannot be proven must remain `[ASSUMED]` and block promotion until revalidated.

---

## Procedure (manual, per epic/module)

Do modules in the dependency order declared by their mapping, one module at a time:

For each epic:

1. **List** its specs from `backlog/planning/` using the batch timestamp/module slug.
2. **Revalidate** each spec with the 4 checks above (open the file, cross-check code). Record verdict + the concrete fix.
3. **Apply fixes** by editing the spec body in `backlog/planning/` (paths, names, deps, scope, phantom refs). For 🔴: `git mv` to `done/` (closed) or add a BLOCKED banner (parked).
3b. **Split UI authoring into its own task** (do this here, on the now-finalized epic — not before revalidation, so closed/re-scoped tasks aren't split in vain). A spec is UI-scoped (strict) only if it **creates or edits a `FeatureBaseController` screen/popup/persistent-HUD-widget prefab, a prefab variant, or wires serialized UI references** — NOT services/enums/CSV that merely emit data a UI reads, and NOT world-space/pooled gameplay VFX (e.g. floating damage numbers, telegraph VFX). For each such "Build X screen" spec, **split code from prefab**:
   - **Keep the original task as the controller/logic (`.cs`) task** — narrow its scope to the controller class + registration; move prefab authoring to `out_of_scope` with a pointer to the new task.
   - **Create a NEW prefab-authoring task** (`Author <X> prefab + wire serialized refs`) inserted **immediately after** the controller task. This new task carries `**Required skills:** /create-ui` (+ `/compile-check` if it touches `.cs`) and the **UI criteria** block from its tier template (`_TEMPLATE_M/L.md`); it `depends_on` the controller task.
   - **Renumber** the batch `NN` filename ordinals if necessary and update every affected `depends_on`.
   See the `planning-task` SKILL `ui_task_split` + "UI skill routing rule" for the canonical definition.
4. **Promote** the surviving 🟢/🟡 specs via `/add-to-backlog` in dependency order (lowest dependency first).
5. **Run** `/run-backlog` (or the loop) for that epic, verify it flows, THEN move to the next epic.

> Promote + run module-by-module — each implemented module becomes real code that makes the next module's revalidation accurate.

---

## Fast assisted option

This pass may be fan-out audited with one read-only agent per independent module, producing a GREEN/YELLOW/RED table before any spec is edited.

---

## One-line summary

Don't promote a phase's upfront specs blind — re-validate each against real code (dup / stale-path / phantom / deps), fix or close, promote + run **epic-by-epic in dependency order**. Better yet: spec the next phase just-in-time so this stays light.
