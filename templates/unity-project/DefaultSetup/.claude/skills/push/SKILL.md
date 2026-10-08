---
name: push
description: Commit and push changes with a short, English, AI-generated commit message
---

// turbo-all
# Git Push Workflow
Automate the staging, committing, and pushing of changes with an AI-generated message.

> **Cross-platform:** pick the command per OS.
> - **Windows:** `powershell -ExecutionPolicy Bypass -File .claude/scripts/<name>.ps1`
> - **macOS / Linux:** `bash .claude/scripts/<name>.sh`
>
> The `.ps1` and `.sh` scripts are kept behaviorally identical. Use the variant matching the current platform.

## 1. PREPARE & ANALYZE
- **Mandatory check — do this first, before anything else:** compare the user's raw prompt against the bare string `/push`. If there is ANY extra character anywhere in the prompt — before, after, or around `/push` (e.g. `* /push`, `+ /push`, `/push ~`, `/push [UI]`), even a single symbol — that extra text is a `<prefix>` and/or `<suffix>` and it is **NOT optional**: it MUST end up in the final commit message. Never silently drop it because it's "just one symbol" or looks like stray formatting (a leading `*`/`-` looks like a markdown bullet but is NOT one here — the whole line is the prompt, not a list).
- Run the **prepare** script for the current OS:
  - Windows: `powershell -ExecutionPolicy Bypass -File .claude/scripts/git_prepare.ps1`
  - macOS / Linux: `bash .claude/scripts/git_prepare.sh`
- If output is `NO_CHANGES`, stop and inform the user.
- Analyze the output — use `--- STAT ---` to identify which files changed and their scope, and use `--- DIFF (first 80 lines) ---` for high-level intent. Generate the commit message from the actual change.
- **Message rules (mandatory):**
  - **Short and to the point** — ONE subject line, max 50 chars, imperative mood. No body, no bullet list, no trailers, no `Co-Authored-By`, no explanation of *why*. Never a paragraph.
  - **English only** — write the message entirely in English, even when the user's prompt, the code comments or the diff are in another language. Switch language ONLY when the user explicitly asks for it (e.g. `/push commit tiếng Việt`), and then write the whole message in that language.
  - Describe *what* changed, not which files: `fix shop pack refresh` not `updated ShopController.cs and MoneyBarView.cs to refresh the pack list after a purchase completes`.
- If no `<prefix>` is explicitly provided by the user, **DO NOT** generate default prefixes like `feat:`, `fix:`, `refactor:`, etc.
- Assemble the message by prepending the `<prefix>` and appending the `<suffix>` if provided: `<prefix> [Generated Message] <suffix>`.

## 2. FINALIZE
- **Verify before running:** re-read the user's original prompt one more time. If it had any `<prefix>`/`<suffix>`, confirm the assembled `[Final Message]` literally contains that exact text — not paraphrased, not dropped.
- Run the **push** script for the current OS with the final message as the argument:
  - Windows: `powershell -ExecutionPolicy Bypass -File .claude/scripts/git_push.ps1 "[Final Message]"`
  - macOS / Linux: `bash .claude/scripts/git_push.sh "[Final Message]"`
- Report the status and the final message.
