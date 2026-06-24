# Backlog Task Templates — Index

Tasks use **tier-specific templates**. Pick the one matching the task size (the `pending-task` skill triages this automatically at STEP 0).

| Tier | Template | Use when |
|---|---|---|
| **XS** | [_TEMPLATE_XS.md](_TEMPLATE_XS.md) | CSV tweak, constant adjust, dead-code removal, single-variable rename. No new logic. |
| **S** | [_TEMPLATE_S.md](_TEMPLATE_S.md) | Single-file logic tweak, small bug fix in ≤2 files. No new UI screen / save field / event. |
| **M** | [_TEMPLATE_M.md](_TEMPLATE_M.md) | Multi-file feature, new UI screen/popup, new controller, new save field. 3–8 files. |
| **L** | [_TEMPLATE_L.md](_TEMPLATE_L.md) | Cross-cutting: new IAP/purchase flow, save migration, new system integration, 9+ files. |

**Auto-bump rules** (override tier upward if any signal matches):
- Touches `Purchase*`, `IAP*`, `Receipt*`, `Payment*` → at least M.
- Adds new `DataPlayer` field or save module → at least M.
- Adds new TigerForge event cross-system → at least M.
- Touches `Auth*`, `Token*`, `Session*` → at least M.
- Touches >2 feature modules or >8 files → L.

**Lifecycle:**
- `backlog/pending/<timestamp>-<TIER>-<slug>.md` = drafted, not yet queued (`/pending-task` writes here)
- `backlog/todo/NNN-<slug>.md` = queued for `run-backlog` (`/add-to-backlog` picks from pending)
- `backlog/in-progress/` and `backlog/done/` = managed by `run-backlog`

**Filename convention for todo/:** `NNN-short-slug.md` where `NNN` = next sequential number across all of `todo/`, `in-progress/`, `done/`.
