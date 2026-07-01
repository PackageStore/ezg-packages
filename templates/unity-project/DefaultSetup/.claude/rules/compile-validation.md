# COMPILE VALIDATION (Unity MCP)

After editing `.cs` in a normal task, **validate compile via Unity MCP before reporting done** — only when BOTH preconditions hold. Applies to ad-hoc tasks, not just `/run-backlog`. Reusable as skill `/compile-check`.

**Preconditions (both):**
- **A. Needs check** — task created/modified a compilable C# source (`.cs`/`.asmdef`/`.asmref`). **Skip entirely** if it touched only data/docs (CSV/json/md), assets (prefab/scene/mat) with no script change, or was read-only/analysis.
- **B. MCP connected** — probe `unity_list_instances`. Proceed only if it succeeds AND returns a running Editor for this project. On fail/timeout/no-instance → **skip gracefully**: note `compile-check: skipped (Unity MCP không kết nối / Editor chưa mở)`, never fail the task.

**Procedure (both hold):**
1. **Pick instance** — one → use it; several → `unity_select_instance`, capture `port` (pass to every later call).
2. **Force recompile** — `unity_get_compilation_errors` only reads the last cycle. After editing on disk, refresh: `unity_execute_menu_item("Assets/Refresh")` (or `unity_asset_import`).
3. **Wait** — poll `unity_editor_state` until not compiling (reading earlier = stale).
4. **Read** — `unity_get_compilation_errors`, `severity:"error"`.
5. **Fix loop (max 2 rounds)** — errors remain → fix, back to step 2. After 2 rounds still failing → report `COMPILE_BLOCKED` with remaining errors; don't claim done.

**Note:** not a `settings.json` hook (hooks are shell-only, can't call Unity MCP) — this is a model-followed rule.
