---
trigger: always_on
---

# DATA SYSTEM
**Player:** Use `DataPlayer`. Access via `PlayerDataManager.[Module]` (Fast). Avoid `DataPlayer.GetModule`.
**Config:** Use `DataManager` for read-only (CSV/SO).
**Save:** NEVER `Save()` in Update loops.
**Defaults:** Must have fallback in `SetupDefaultData()`.
**Sync:** `try-catch` Firebase ops + Local backup.
