# Phase Spec Revalidation Playbook (manual)

A repeatable manual procedure to fix backlog specs that were **drafted ahead of their dependencies** (authored on an empty/stub codebase) BEFORE you promote them to `todo/` and let `/run-backlog` execute them.

> **Why this exists:** specs authored upfront, in parallel, against code that doesn't exist yet will independently invent inconsistent paths, duplicate each other's deliverables, and reference configs/classes nobody creates — which makes `/run-backlog` hit hard blockers (e.g. duplicate-type `CS0101`). This playbook re-validates each phase's specs against the now-real prior-phase code, in small dependency-ordered batches, right before promotion. Going forward, prefer spec'ing each phase **just-in-time** (after the prior phase ships) so this stays light.

---

## When to run

Run for a phase **only after the previous phase is implemented and validated** (so this phase's dependencies are real code, not guesses). Do not promote a phase's specs to `todo/` until they pass this playbook.

---

## The 4 checks (per spec)

For each spec file, open it and cross-check against the **current** codebase (`codegraph_explore` / Grep / Read). Assign a verdict:

| Check | Question | Fail → |
|---|---|---|
| **1. Duplicate deliverable** | Does any NEW file/class/CSV it declares already exist, or is it owned by another task (done/todo/planning)? | 🔴 close or re-scope (recreating a class = CS0101 duplicate-type → loop blocks) |
| **2. Stale path / name** | Do its `Related files` paths + class names match where code actually lives now? Or does it assume a tree that doesn't exist? | 🟡 fix the spec paths/names to the real folder tree |
| **3. Phantom reference** | Does every config/class/accessor it tells the implementer to READ (`DataManager.X`, a CSV, a helper) actually exist or get created by a named upstream task? Are the API method names real? | 🟡 replace with the real owner/API, or add the missing upstream task |
| **4. Dependency reality** | Are its `depends_on` ids real tasks? Are the deps satisfied/ordered before it? Is it hard-blocked on something unbuilt or a design decision? | 🟡 fix dep ids; 🔴 if blocked on a design decision → park in `planning/` with a BLOCKED banner |

**Verdict:** 🟢 GREEN = promote as-is · 🟡 YELLOW = edit spec, then promote · 🔴 RED = close (move to `done/` with a closure note) or park (keep in `planning/` with a BLOCKED banner).

When fixing stale paths, snap them to the project's **established folder convention** — match where sibling/dependency code actually lives (read the shipped prior-phase code to learn it); never invent a parallel tree. Delete references to any config/class that no task produces.

---

## Procedure (manual, per epic)

Do epics in dependency order, one at a time:

1. **List** the epic's specs: `ls backlog/planning/ | grep <epic-keyword>`.
2. **Revalidate** each spec with the 4 checks above (open the file, cross-check code). Record verdict + the concrete fix.
3. **Apply fixes** by editing the spec body in `backlog/planning/` (paths, names, deps, scope, phantom refs). For 🔴: `git mv` to `done/` (closed) or add a BLOCKED banner (parked).
4. **Promote** the surviving 🟢/🟡 specs via `/add-to-backlog` in dependency order (lowest dependency first).
5. **Run** `/run-backlog` for that epic, verify it flows, THEN move to the next epic.

> Promote + run epic-by-epic, not the whole phase at once — each implemented epic becomes real code that makes the NEXT epic's revalidation accurate (just-in-time in miniature).

---

## Fast assisted option

This pass can be fan-out audited: spawn one read-only agent per epic to run the 4 checks and return a verdict table, then apply the fixes. Ask: *"audit <phase> specs"* — parallel read-only agents produce a per-task GREEN/YELLOW/RED table with concrete fixes; you approve before any spec is edited.

---

## One-line summary

Don't promote a phase's upfront specs blind — re-validate each against real code (dup / stale-path / phantom / deps), fix or close, promote + run **epic-by-epic in dependency order**. Better yet: spec the next phase just-in-time so this stays light.
