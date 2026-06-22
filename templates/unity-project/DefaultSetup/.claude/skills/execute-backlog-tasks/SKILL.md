---
name: execute-backlog-tasks
description: Automatically execute backlog tasks by opening a new PowerShell window and running run-backlog-loop.bat. Triggers on requests like "execute backlog tasks", "run backlog loop", "execute tasks in backlog", "run backlog".
---

# Execute Backlog Tasks

This skill allows the agent to spawn a new PowerShell window and run the backlog loop script (`run-backlog-loop.bat`) in the background, returning control to the user immediately.

## Execution Steps

When the user requests to execute tasks in the backlog:

1. Identify the absolute path of the current workspace directory (do not hardcode it; obtain it dynamically from the tool context or environment).
2. Use the `PowerShell` tool to spawn a new detached PowerShell window and run the backlog loop batch file. Do NOT use `-Verb RunAs` (UAC elevation is not needed and will fail in non-interactive context).
3. Construct the command dynamically using the identified absolute workspace path:
   ```powershell
   Start-Process powershell -ArgumentList "-NoExit", "-Command", "& { Set-Location '<WorkspacePath>'; & '<WorkspacePath>\.agents\scripts\run-backlog-loop.bat' }"
   ```
   *(Replace `<WorkspacePath>` with the actual absolute path, e.g., `C:\Projects\m1`)*.
4. The new PowerShell window runs independently — do NOT wait for it to finish.
5. Notify the user briefly that a new PowerShell window has been opened and the backlog loop is running in the background.
