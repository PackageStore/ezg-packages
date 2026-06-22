---
description: Build and push changes with flags. If no changes, push an empty commit with flags.
---
// turbo-all
# Build & Push Workflow
Automate the build process by committing and pushing with flags `-fb -fu -fs`.

## 1. PREPARE & ANALYZE
- **OS Check** — run the prepare script for the current platform:
  - On Windows: `powershell -ExecutionPolicy Bypass -File .agents/scripts/git_prepare.ps1`
  - On macOS/Linux: `bash .agents/scripts/git_prepare.sh`
- If output is `NO_CHANGES`:
    - Run `git commit --allow-empty -m "-fb -fu -fs"`
    - Run `git push`
    - Report: "No changes found. Pushed empty commit with flags: `-fb -fu -fs`"
    - **STOP HERE.**
- If output contains changes:
    - Capture any intended `<prefix>` (symbols or bracketed text) from the user's prompt.
    - Analyze the output to generate a concise, descriptive commit message (max 50 chars).
    - Assemble the final message: `<prefix> [Generated Message] -fb -fu -fs`.
    - Run the push script for the current platform:
      - On Windows: `powershell -ExecutionPolicy Bypass -File .agents/scripts/git_push.ps1 "[Final Message]"`
      - On macOS/Linux: `bash .agents/scripts/git_push.sh "[Final Message]"`
    - Report the status and the final message.
