---
trigger: always_on
---

# OUTPUT FORMAT RULES

After completing any task:
- **DO NOT** explain what was done
- **DO NOT** provide testing instructions  
- **DO NOT** summarize changes
- **DO NOT** show code diffs or diff blocks
- **ALWAYS** respond in the same language as the USER's request

**Output ONLY:**
- List of files changed with one-line description each

Example:
- [GameplayManager.cs](file:///Users/anhnt/Projects/m1/Assets/_Project/Features/Gameplay/Scripts/GameplayManager.cs) - Added ShowProdInfor() call in FillProductToShelf

**IMPORTANT:** Always format changed files as clickable markdown links using the full absolute file URI:
- Format: `[FileName.cs](file:///absolute/path/to/FileName.cs)`
- Use the ACTUAL absolute path of the file on the current machine (under `/Users/anhnt/Projects/m1/Assets/_Project/...`), NOT a placeholder
- NEVER use plain backtick text like `` `FileName.cs` `` for file names in the output list
