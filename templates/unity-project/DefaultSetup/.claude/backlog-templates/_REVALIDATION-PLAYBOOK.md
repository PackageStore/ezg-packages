# Spec Revalidation Playbook (manual)

A repeatable manual procedure to fix backlog specs that were **drafted ahead of their dependencies** (i.e. authored against code that does not exist yet) BEFORE you promote them to `todo/` and let `/run-backlog` execute them.

> **Why this exists:** When many specs are authored upfront against an empty/stub codebase, parallel planners tend to invent inconsistent paths, duplicate each other's deliverables, and reference configs/classes nobody creates. `/run-backlog` then hits a hard blocker (e.g. a `CS0101` duplicate-type error from recreating an existing class) and a full audit is needed. This playbook is the cure: re-validate each batch of specs against the now-real prior code, in small dependency-ordered batches, right before promotion. Going forward, prefer spec'ing each feature **just-in-time** (after its dependencies ship) so this stays light.

---

## When to run

Run for a batch of specs **only after the code they depend on is implemented and validated** (so their dependencies are real code, not guesses).

- Drafted a wave of specs ahead of time in `backlog/planning/`? → revalidate that wave only after the foundation it builds on compiles and runs.
- Ideally, spec just-in-time after the prior feature ships; if drafted ahead, run this playbook before promoting via `/add-to-backlog`.

Do **not** promote a spec to `todo/` until it passes this playbook — an un-validated spec is what blocks the loop.

---

## The 4 checks (per spec)

For each spec file, open it and cross-check against the **current** codebase (`codegraph_explore` / `codegraph_search` / Grep / Read). Assign a verdict:

| Check | Question | Fail → |
|---|---|---|
| **1. Duplicate deliverable** | Does any NEW file/class/CSV/event it declares already exist, or is it owned by another task (`done`/`todo`/`planning`)? | 🔴 close or re-scope (recreating a class = `CS0101` duplicate-type → loop blocks) |
| **2. Stale path / name** | Do its `Related files` paths + class names match where code actually lives now (`<featuresRoot>/<Domain>/<Feature>/...`, `<featuresRoot>/_Shared/...`)? Or does it assume a tree that doesn't exist? | 🟡 fix the spec paths/names to the real canonical tree |
| **3. Phantom reference** | Does every config/class/accessor it tells the implementer to READ (`PlayerDataManager.X`, a CSV, an `EventName` const, a helper) actually exist or get created by a named upstream task? Are the API method names real? | 🟡 replace with the real owner/API, or add the missing upstream task |
| **4. Dependency reality** | Are its `depends_on` ids real tasks? Are the deps satisfied/ordered before it? Is it hard-blocked on something unbuilt or a design decision? | 🟡 fix dep ids; 🔴 if blocked on a design decision → park in `planning/` with a BLOCKED banner |

**Verdict:** 🟢 GREEN = promote as-is · 🟡 YELLOW = edit spec, then promote · 🔴 RED = close (move to `done/` with a closure note) or park (keep in `planning/` with a BLOCKED banner).

---

## Canonical conventions (the shared truth planners should snap to)

When fixing stale paths/names, snap them to this project's established tree and patterns (paths below are profile keys — see `.claude/project-profile.json`):

- Feature modules → `<featuresRoot>/<Feature>/...` (controller extends `FeatureBaseController`).
- Gameplay / battle → `<gameplayRoot>/...`, following whatever sub-layout that bucket already uses (mirror a sibling; don't invent an id scheme).
- Core modules / framework → `<featuresRoot>/_Shared/...` — **reference, never recreate** (`UI/Framework/FeatureBaseController.cs`, `UI/Framework/UIManager.cs`, `Systems/TimeManager.cs`, `Systems/Utils.cs`).
- Save data = EXTEND `DataPlayer` via `PlayerDataManager.[Module]` with a `SetupDefaultData()` fallback — never duplicate a save module.
- Cross-system events = declare/reuse `EventName` constants for `TigerForge` — verify the const exists before a task emits it (a task that emits `EventName.OnX` must come AFTER the task that declares `OnX`).
- Balance numbers/formulas live in CSV config — reference the existing CSV, do not hardcode or invent a parallel config class.
- Backend writes go through the Cloudflare Worker — never spec a direct Supabase write.

---

## Procedure (manual, per batch)

Do batches in dependency order, one batch at a time:

1. **List** the batch's specs: `ls backlog/planning/ | grep <keyword>`.
2. **Revalidate** each spec with the 4 checks above (open the file, cross-check code). Record verdict + the concrete fix.
3. **Apply fixes** by editing the spec body in `backlog/planning/` (paths, names, deps, scope, phantom refs). For 🔴: `git mv` to `done/` (closed) or add a BLOCKED banner (parked).
3b. **Split UI authoring into its own task** (do this here, on the now-finalized batch — not before revalidation, so closed/re-scoped tasks aren't split in vain). A spec is UI-scoped (strict) only if it **creates or edits a `FeatureBaseController` screen/popup/persistent-HUD-widget prefab, a prefab variant, or wires serialized UI references** — NOT services/enums/CSV that merely emit data a UI reads, and NOT world-space/pooled gameplay VFX (e.g. floating damage numbers, telegraph VFX). For each such "Build X screen" spec, **split code from prefab**:
   - **Keep the original task as the controller/logic (`.cs`) task** — narrow its scope to the controller class + registration; move prefab authoring to `out_of_scope` with a pointer to the new task.
   - **Create a NEW prefab-authoring task** (`Author <X> prefab + wire serialized refs`) inserted **immediately after** the controller task. This new task is `/new-ui` workflow-backed and carries the **UI criteria**; it `depends_on` the controller task.
   - **Renumber** the batch's filename ordinals and update every affected `depends_on`.
   See the `planning-task` SKILL "Workflow-backed detection" + `/new-ui` for the canonical definition.
4. **Promote** the surviving 🟢/🟡 specs via `/add-to-backlog` in dependency order (lowest dependency first).
5. **Run** `/run-backlog` (or the loop) for that batch, verify it flows, THEN move to the next batch.

> Promote + run batch-by-batch, not all at once — each implemented batch becomes real code that makes the NEXT batch's revalidation accurate (this is just-in-time in miniature).

---

## Fast assisted option

This whole pass can be fan-out audited: spawn one read-only agent per batch to run the 4 checks and return a verdict table, then apply the fixes. Ask: *"audit my planning specs"* and the audit runs with parallel agents (read-only), producing a per-task GREEN/YELLOW/RED table with concrete fixes — you approve before any spec is edited.

---

## One-line summary

Don't promote upfront specs blind — re-validate each against real code (dup / stale-path / phantom / deps), fix or close, promote + run **batch-by-batch in dependency order**. Better yet: spec the next feature just-in-time so this stays light.
