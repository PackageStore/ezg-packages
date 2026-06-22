---
trigger: always_on
---

# CORE SYSTEM
**Utils:** Check `Utils.cs` before creating helpers.
**Time:** Use `TimeManager`, NEVER `DateTime.Now`.
**UI:** Use `UIManager` (Show/Hide), NEVER `SetActive()`.
**Error:** `try-catch` for external (Net/Firebase).
**Log:** `Debug.Log` (Info), `Warning`, `Error`.

## Code Exploration — Codegraph First
When exploring the codebase (understanding how something works, tracing a flow, finding callers/callees, assessing impact of a change), **use `codegraph_*` MCP tools before grep/read**:

| Intent | Tool |
|--------|------|
| How does X work / what is X / survey an area | `codegraph_explore` (primary — usually the only call needed) |
| What is the symbol named X? (location only) | `codegraph_search` |
| What calls this method? | `codegraph_callers` |
| What does this method call? | `codegraph_callees` |
| What would break if I change X? | `codegraph_impact` |
| Flow / path from X to Y | `codegraph_explore` with both symbol names |

**Fall back to Grep/Read only** when:
- Looking for string literals, CSV keys, event name strings, or magic values not indexed as symbols.
- Confirming a specific detail that `codegraph_explore` trimmed or didn't cover.
- The `.codegraph/` index hasn't caught up yet (file watcher lag ~1s after a save).
