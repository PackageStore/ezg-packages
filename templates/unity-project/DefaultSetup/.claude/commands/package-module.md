---
description: Extract a module/folder from Assets/ into a clean UPM package and publish it to the Easygoing registry
---

# Package Module Workflow (UPM extraction)

When the user runs `/package-module [ModulePath]` (or asks to "đóng module X thành package UPM" / "package this module" / "đẩy module X lên registry"):

This workflow is a thin entry point — the full, deterministic procedure lives in the **`package-module` skill** (`.claude/skills/package-module/SKILL.md`). Invoke that skill and follow its steps. Do NOT reinvent the packing/publish flow.

> **Not `/new-package`.** That one builds an **IAP shop pack feature** inside this game
> (`.claude/commands/new-package.md`). This one takes existing C# out of `Assets/` and publishes it as
> a reusable UPM package. Two different meanings of "package" — check which the user means before
> starting.

## Summary of what the `package-module` skill does

- Takes one **specified module folder** (e.g. `Assets/_Project/Features/_Shared/<Module>`) and builds a clean, standards-compliant **UPM package** from it.
- **Non-destructive**: the module stays in `Assets/` and keeps compiling.
- Commits + pushes the package **straight to the `main` branch of the monorepo** (`ezg-packages`), at `packages/com.ezg.<name>/`. Pushing to `main` triggers GitHub Actions → `validate.mjs` → `publish.mjs`, which signs the tarball with `upm pack` and uploads it to the **Easygoing** scoped registry on Cloudflare R2. **No feature branch, no PR.**
- Cross-platform (Windows PowerShell + macOS zsh/bash). Authenticates over **SSH**; if not set up on this machine yet, auto-invokes `/setup-package-push` inline (no need to run it separately first — its only interactive step is a one-time browser approval). Falls back to a GitHub PAT provided out-of-band, never committed, only if SSH auto-onboarding can't complete.
- Required input: `MODULE_PATH`. Other config (`PACKAGE_SCOPE=com.ezg`, `REGISTRY_URL`, `MONOREPO_REMOTE`, `UNITY_VERSION=2022.3`) has defaults — confirm on first run.
- To only PLAN without writing to the monorepo, run the skill's STEP 0–3.

> **Out-of-band by design.** This is a maintainer action against another repo, so it is excluded from
> backlog routing — `/planning-task` never emits a task `**Backed by workflow:** /package-module`, and
> `/run-backlog` never runs it. Invoke it by hand.

> Switching the game to consume the package from the registry (instead of the in-Assets copy) is a separate Phase 2, documented in the skill — not done by this workflow.
