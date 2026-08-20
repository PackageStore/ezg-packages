---
description: One-time per-machine setup so /package-module can push to ezg-packages over SSH instead of a GitHub PAT
---

# Setup Package Push (SSH onboarding)

When the user runs `/setup-package-push` (or asks "cài SSH cho package-module" / "tôi chưa có SSH,
setup giúp" / "cấp quyền deploy package không cần PAT"):

This is a thin entry point — the full procedure lives in the **`setup-package-push` skill**
(`.claude/skills/setup-package-push/SKILL.md`). Invoke that skill and follow its steps.

> **Not `/package-module`.** This only sets up *how the machine authenticates to GitHub*. It
> never touches package code or the monorepo content. Run it once per machine, then use
> `/package-module` as normal — it will pick up SSH automatically.

## Summary of what the `setup-package-push` skill does

- Checks whether SSH access to GitHub already works on this machine — if so, exits early.
- Otherwise: installs the GitHub CLI (`gh`) if missing, runs its one-time browser login, generates
  an SSH key if the machine doesn't have one, and registers it on the user's GitHub account.
- The browser-approval step is inherently interactive (GitHub's own security control) — if you
  can't complete it yourself, hand the login command to the user and wait for their confirmation.
- Verifies the account actually has **Write** access to `PackageStore/ezg-packages`. It cannot
  grant that access — only report whether it's there, and tell the user who to ask if not.
- Never creates, requests, or stores a GitHub PAT — that remains `/package-module`'s fallback
  path for machines that haven't run this setup.
