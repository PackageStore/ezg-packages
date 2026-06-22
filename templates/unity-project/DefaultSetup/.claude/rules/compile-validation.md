# COMPILE VALIDATION (Unity MCP)

After editing any `.cs` file in a normal task, **validate compilation via Unity MCP before reporting the task done** — but only when **both preconditions** below hold. This applies to ad-hoc prompted tasks, not only the `/run-backlog` pipeline.

## Preconditions (both must hold — otherwise skip the check)

### A. Task actually needs a compile check
Run the check **only** when the task created or modified at least one **compilable C# source** — `.cs` file (incl. editor scripts) or `.asmdef`/`.asmref`. **Skip entirely** (no Unity call) when the task touched none of these, e.g.:
- Pure data/docs: CSV, `.json`, `.md`, text/config.
- Asset-only: prefab/scene/material/sprite without any script change.
- Questions, analysis, or read-only work with no edits.

### B. Unity MCP is connected
Probe first with `unity_list_instances`. Proceed to the compile check **only if the probe succeeds AND returns at least one running Editor** for this project. If the probe **fails / errors / times out**, or returns **no instance**, the precondition is not met → **skip gracefully**: do not block or fail the task; just note `compile-check: skipped (Unity MCP không kết nối / Editor chưa mở)` and continue/report normally. Never treat a missing connection as a task failure.

## Procedure (only reached when both preconditions hold)
1. **Pick instance** — from the `unity_list_instances` result: if exactly one, use it. If several, call `unity_select_instance` and capture its `port` (pass `port` to every later Unity call).
2. **Force recompile** — `unity_get_compilation_errors` only reads the *last* compile cycle; it does not recompile. After editing files on disk, trigger a refresh: `unity_execute_menu_item("Assets/Refresh")` (or `unity_asset_import`).
3. **Wait for compile to finish** — poll `unity_editor_state` until it is no longer compiling. Reading errors before this returns stale results.
4. **Read errors** — `unity_get_compilation_errors` with `severity: "error"`.
5. **Fix loop (max 2 rounds)** — if errors remain: read them, fix the code, go back to step 2. After 2 rounds still failing → report `COMPILE_BLOCKED` to the user with the remaining errors; do not claim the task is done.

## Notes
- Do **not** use a `settings.json` hook for this — hooks run shell only and cannot call Unity MCP tools. This is a model-followed rule.
- Reusable as the skill `/compile-check` (see `.agents/skills/compile-check/SKILL.md`).
