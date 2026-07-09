---
name: compile-check
description: Validate C# compilation of the current Unity project via Unity MCP after editing .cs files. Picks the Unity instance, forces a recompile, waits for it to finish, reads compilation errors, and fixes them (max 2 rounds). Use when the user says "compile check", or after finishing an ad-hoc code task outside /run-backlog.
---

# Compile Check — Validate C# compilation via Unity MCP

Confirm the project compiles cleanly through the running Unity Editor (via Unity MCP), and fix any compilation errors. This is the standalone, hand-invoked version of the standing rule in `.agents/rules/compile-validation.md`.

`unity_get_compilation_errors` reads the **last** compilation cycle from `CompilationPipeline` — it does **not** trigger a recompile. So you must force a refresh and wait for compilation to finish before reading, otherwise you get stale results.

## Procedure

### 1 — Select the Unity instance
```
unity_list_instances
```
- **Exactly one instance** → use it.
- **Multiple instances** → call `unity_select_instance` for this project's instance, capture its `port`, and pass `port` to every subsequent Unity call.
- **No instance open** → STOP. Report to the user: `Unity Editor is not open for this project — cannot compile-validate via MCP. Please open Unity and run /compile-check again.` Do not fall back silently.

### 2 — Force a recompile
The agent edited `.cs` files on disk; Unity must re-import + recompile first.
```
unity_execute_menu_item("Assets/Refresh")
```
(or `unity_asset_import` on the changed files). 

### 3 — Wait for compilation to finish
```
unity_editor_state
```
Poll until it reports it is **not compiling** (and not in the middle of an import). Only then read errors.

### 4 — Read compilation errors
```
unity_get_compilation_errors  (severity: "error")
```

### 5 — Fix loop (max 2 rounds)
- **No errors** → report success: list the `.cs` files validated and `compile: clean`.
- **Errors** → read each error (file + line + message), fix the code, then go back to **step 2** and re-validate.
- After **2 fix rounds** still failing → STOP and output:
  `COMPILE_BLOCKED — compile errors remain after 2 fix rounds, manual intervention required.`
  List the remaining errors. Do not claim the task is complete.

## Notes
- Always target the right instance with `port` when multiple Editors are open.
- Warnings are not blocking — filter `severity: "error"` for the gate; surface warnings only if the user asks.
- This duplicates Tier 1 of `/run-backlog` STEP 5b, but for ad-hoc tasks outside the backlog pipeline.
