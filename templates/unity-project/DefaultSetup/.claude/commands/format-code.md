---
description: Format code and add XML summaries to class methods
---

# Format Code Workflow

When the user runs `/format-code ClassName1 [ClassName2 ...]`:

> [!NOTE]
> If no class name is provided (just `/format-code`), use the **currently open/active file** in the editor as the target class.

> [!NOTE]
> If the target is a **directory path**, process **ALL `.cs` files** in that directory **and its subdirectories** recursively. Execute the formatting workflow on each file sequentially.

1. **Find the class files** — search for the specified class files in the project.
   - If target is a directory: use `Glob` with pattern `**/*.cs` to get all C# files recursively.

2. **Check file size and chunk if needed**:
   - If a file has **more than 300 lines**, split it into chunks of ~100–150 lines (by region or logical section).
   - Process each chunk separately with individual `Edit` calls.
   - Wait for each chunk to complete before starting the next.
   - **DO NOT** try to edit the entire file in one tool call.

3. **Format code according to project rules** (`.agents/rules/code-style.md`):
   - Apply region order: `Fields` → `Initialize` (Awake→OnEnable→Start→OnDisable) → `Public Methods` → `Private Methods` → `Event Handlers`.
   - Ensure proper spacing and indentation.
   - **DO NOT** rename any existing fields, methods, or classes to match naming conventions — renaming breaks references and Unity serialization.

4. **Sort and group methods within each `#region`**:
   - **Identify feature groups**: scan all method names and implementations within the region to determine which feature/subsystem they belong to (e.g., UI, Animation, Data, Reward, Merge, Grid, Timer, Event, etc.).
   - **Group methods by feature**: place methods that belong to the same feature/subsystem adjacent to each other. Separate groups with a single blank line.
   - **Sort within each group**: order methods in logical call-order (entry-point / high-level method first, helpers and sub-methods below it).
   - **Sort across groups**: order feature groups from most general/core to most specific (e.g., Initialization → Core Logic → UI → Animation → Utility/Helper).
   - **Add an inline comment header** before each group when there are 2 or more feature groups in a region:
     ```csharp
     // --- <FeatureName> ---
     ```
   - **Do NOT** merge or move methods across `#region` boundaries — only reorder within the same region.

5. **Add XML documentation summaries**:
   - Add `/// <summary>` to ALL methods that don't have one.
   - Write summaries in **English**.
   - Include `<param>` tags for parameters if applicable.
   - Include `<returns>` tag for non-void methods if applicable.

6. **Important constraints**:
   - **DO NOT** change any logic.
   - **DO NOT** rename fields, methods, or classes under any circumstances (including to match naming conventions — this breaks Unity serialization and references).
   - **DO NOT** add, rename, or remove any existing `namespace` declarations (leave as-is if none exists).
   - **DO NOT** modify method implementations.
   - **CRITICAL**: ensure all braces `{` and `}` are perfectly balanced. The final file MUST be syntactically correct and NOT missing any closing `}` at the end of classes/namespaces.
   - Only format structure and add documentation.

7. **Output format**:
   - List files changed with one-line description each.
