---
description: Commit and push changes with AI-generated commit message
---

// turbo-all
# Git Push Workflow
Automate the staging, committing, and pushing of changes with an AI-generated message.

## 1. PREPARE & ANALYZE
- Optional: If the user provides additional text or symbols (e.g., `+ /push`, `/push ~`, `/push [UI]`), capture any intended `<prefix>` (such as symbols `*`, `+`, `~`, `!`, `#`, or bracketed text) and/or `<suffix>`. Look for these anywhere in the prompt.
- Run `powershell -ExecutionPolicy Bypass -File .agents/scripts/git_prepare.ps1`
- If output is `NO_CHANGES`, stop and inform the user.
- Analyze the output — use `--- STAT ---` to identify which files changed and their scope, and use `--- DIFF (first 80 lines) ---` for high-level intent. Generate a concise, descriptive commit message (max 50 chars) that captures the actual change.
- If no `<prefix>` is explicitly provided by the user, **DO NOT** generate default prefixes like `feat:`, `fix:`, `refactor:`, etc.
- Assemble the message by prepending the `<prefix>` and appending the `<suffix>` if provided: `<prefix> [Generated Message] <suffix>`.

## 2. FINALIZE
- Run `powershell -ExecutionPolicy Bypass -File .agents/scripts/git_push.ps1 "[Final Message]"`
- Report the status and the final message.