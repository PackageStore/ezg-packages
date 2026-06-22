---
name: push
description: Commit and push changes with AI-generated commit message
---

// turbo-all
# Git Push Workflow
Automate the staging, committing, and pushing of changes with an AI-generated message.

> **Cross-platform:** pick the command per OS.
> - **Windows:** `powershell -ExecutionPolicy Bypass -File .agents/scripts/<name>.ps1`
> - **macOS / Linux:** `bash .agents/scripts/<name>.sh`
>
> The `.ps1` and `.sh` scripts are kept behaviorally identical. Use the variant matching the current platform.

## 1. PREPARE & ANALYZE
- Optional: If the user provides additional text or symbols (e.g., `+ /push`, `/push ~`, `/push [UI]`), capture any intended `<prefix>` (such as symbols `*`, `+`, `~`, `!`, `#`, or bracketed text) and/or `<suffix>`. Look for these anywhere in the prompt.
- Run the **prepare** script for the current OS:
  - Windows: `powershell -ExecutionPolicy Bypass -File .agents/scripts/git_prepare.ps1`
  - macOS / Linux: `bash .agents/scripts/git_prepare.sh`
- If output is `NO_CHANGES`, stop and inform the user.
- Analyze the output — use `--- STAT ---` to identify which files changed and their scope, and use `--- DIFF (first 80 lines) ---` for high-level intent. Generate a concise, descriptive commit message (max 50 chars) that captures the actual change.
- If no `<prefix>` is explicitly provided by the user, **DO NOT** generate default prefixes like `feat:`, `fix:`, `refactor:`, etc.
- Assemble the message by prepending the `<prefix>` and appending the `<suffix>` if provided: `<prefix> [Generated Message] <suffix>`.

## 2. FINALIZE
- Run the **push** script for the current OS with the final message as the argument:
  - Windows: `powershell -ExecutionPolicy Bypass -File .agents/scripts/git_push.ps1 "[Final Message]"`
  - macOS / Linux: `bash .agents/scripts/git_push.sh "[Final Message]"`
- Report the status and the final message.
