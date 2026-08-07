# Backlog Task Templates — Index

Tasks use **tier-specific templates**. Pick the one matching the task size (the `planning-task` skill triages this automatically at STEP 0).

| Tier | Template | Use when |
|---|---|---|
| **XS** | [_TEMPLATE_XS.md](_TEMPLATE_XS.md) | CSV tweak, constant adjust, dead-code removal, single-variable rename. No new logic. |
| **S** | [_TEMPLATE_S.md](_TEMPLATE_S.md) | Single-file logic tweak, small bug fix in ≤2 files. No new UI screen / save field / event. |
| **M** | [_TEMPLATE_M.md](_TEMPLATE_M.md) | Multi-file feature, new UI screen/popup, new controller, new save field. 3–8 files. |
| **L** | [_TEMPLATE_L.md](_TEMPLATE_L.md) | Cross-cutting: new IAP/purchase flow, new backend surface, save migration, skill system integration, 9+ files. |

**Workflow-backed template (orthogonal to tier):**

| Template | Use when |
|---|---|
| [_TEMPLATE_WF.md](_TEMPLATE_WF.md) | The task is a **pure scaffold** already specified by a `/new-*` workflow (new feature / package / skill / enemy-skill / UI / class). `/planning-task` skips the `task-planner` subagent and the workflow becomes the plan; `run-backlog` loads the workflow first instead of implementing free-form. |

> **WF is NOT a tier — it is a strategy.** A workflow-backed task still gets a real execution tier in its filename (`<timestamp>-<TIER>-<slug>.md`, usually `M`, or `L` if cross-cutting) so `run-backlog` review-gating is unchanged. A **hybrid** task (scaffold + extra custom logic) keeps the `_TEMPLATE_M.md`/`_TEMPLATE_L.md` body — which also accepts a `**Backed by workflow:**` field — and `task-planner` plans only the delta, not the scaffold.

**Auto-bump rules** (override tier upward if any signal matches):
- Touches `Purchase*`, `IAP*`, `Receipt*`, `Payment*` → at least M.
- Adds new `DataPlayer` field or save module → at least M.
- Adds new TigerForge event cross-system → at least M.
- Adds new Cloudflare Worker endpoint or Supabase table → at least M.
- Touches `Backend*`, `Auth*`, `Token*`, `Session*` → at least M.
- Touches >2 feature modules or >8 files → L.

**Batch filename form (design-pipeline / roadmap seeding):** `<timestamp>-<NN>-<TIER>-<slug>.md` — one shared timestamp for the whole batch (minted ONCE by the orchestrator), `NN` = 2-digit topological position from the dependency graph. `backlog-ops.py promote` sorts by `(timestamp, index)`, so `NN` **is** the execution order. `/planning-system` always emits this form. Batch tasks additionally carry `**Context docs:**` / `**Depends on:**` / `**Requires:**` fields (see `_TEMPLATE_WF.md` for semantics).

**Lifecycle:**
- `backlog/planning/<timestamp>-<TIER>-<slug>.md` = drafted, not yet queued (`/planning-task` writes here)
- `backlog/todo/NNN-<TIER>-<slug>.md` = queued for `run-backlog` (`/add-to-backlog` picks from planning)
- `backlog/in-progress/` and `backlog/done/` = managed by `run-backlog`

**Filename convention for todo/:** `NNN-TIER-short-slug.md` where `NNN` = next sequential number across all of `todo/`, `in-progress/`, `done/`, and `TIER` mirrors the body/bullet tier.
