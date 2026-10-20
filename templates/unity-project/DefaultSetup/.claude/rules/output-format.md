---
trigger: always_on
---

# OUTPUT FORMAT

After completing a task, output **ONLY** a list of changed files (one-line description each) — **no** explanation, testing steps, summary, or diff blocks. Reply in the USER's language.

Each file = clickable markdown link with full absolute URI: `[FileName.cs](file:///absolute/path/to/FileName.cs)` — use the REAL path on this machine (`/Users/<you>/Projects/<project>/Assets/_Project/…`), never a placeholder, never plain backticks.

**Exception — polish:** after the list, add one `polish: P<id> chưa đạt — <reason>` line per PolishRules `block` row the task could not satisfy (rule `game-polish`). Nothing else.

Example:
`- [ShopController.cs](file:///Users/<you>/Projects/<project>/…/ShopController.cs) — Added RefreshPackList() call in OnPurchaseSuccess`
