---
description: Commit and push changes with a short, English, AI-generated commit message
---

// turbo-all
# Git Push Workflow
Automate the staging, committing, and pushing of changes with an AI-generated message.

## 1. PREPARE & ANALYZE
- Optional: If the user provides additional text or symbols (e.g., `+ /push`, `/push ~`, `/push [UI]`), capture any intended `<prefix>` (such as symbols `*`, `+`, `~`, `!`, `#`, or bracketed text) and/or `<suffix>`. Look for these anywhere in the prompt.
- **OS Check**:
  - On Windows: Run `powershell -ExecutionPolicy Bypass -File .claude/scripts/git_prepare.ps1`
  - On macOS/Linux: Run `bash .claude/scripts/git_prepare.sh`
- If output is `NO_CHANGES`, stop and inform the user.
- Analyze the output — use `--- STAT ---` to identify which files changed and their scope, and use `--- DIFF (first 80 lines) ---` for high-level intent. Generate the commit message from the actual change.
- **Message rules (mandatory):**
  - **Short and to the point** — ONE subject line, max 50 chars, imperative mood. No body, no bullet list, no trailers, no `Co-Authored-By`, no explanation of *why*. Never a paragraph.
  - **English only** — write the message entirely in English, even when the user's prompt, the code comments or the diff are in another language. Switch language ONLY when the user explicitly asks for it (e.g. `/push commit tiếng Việt`), and then write the whole message in that language.
  - Describe *what* changed, not which files: `fix shop pack refresh` not `updated ShopController.cs and MoneyBarView.cs to refresh the pack list after a purchase completes`.
- If no `<prefix>` is explicitly provided by the user, **DO NOT** generate default prefixes like `feat:`, `fix:`, `refactor:`, etc.
- Assemble the message by prepending the `<prefix>` and appending the `<suffix>` if provided: `<prefix> [Generated Message] <suffix>`.

## 2. FINALIZE
- **OS Check**:
  - On Windows: Run `powershell -ExecutionPolicy Bypass -File .claude/scripts/git_push.ps1 "[Final Message]"`
  - On macOS/Linux: Run `bash .claude/scripts/git_push.sh "[Final Message]"`
- Report the status and the final message.