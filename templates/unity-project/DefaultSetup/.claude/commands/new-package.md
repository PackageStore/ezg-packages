---
description: Extract a module/folder into a clean UPM package and publish it
---

# New Package Workflow

When the user runs `/new-package [ModulePath]` (or asks to "đóng module X thành package UPM" / "package this module"):

This workflow is a thin entry point — the full, deterministic procedure lives in the **`package-module` skill** (`.agents/skills/package-module/SKILL.md`). Invoke that skill and follow its steps. Do NOT reinvent the packing/publish flow.

## Summary of what `package-module` does ([Project Name])

- Takes one **specified module folder** (e.g. `Assets/_Project/Features/_Shared/<Module>`) and builds a clean, standards-compliant **UPM package** from it.
- **Non-destructive**: the module stays in `Assets/` and keeps compiling.
- Commits + pushes the package **straight to the `main` branch of the monorepo** (`ezg-packages`). Pushing to `main` triggers GitHub Actions → `npm pack` → publish to the scoped registry (`com.ezg.*`). **No feature branch, no PR.**
- Cross-platform (Windows PowerShell + macOS zsh/bash). GitHub PAT is provided out-of-band, never committed.
- Required input: `MODULE_PATH`. Other config (`PACKAGE_SCOPE=com.ezg`, `REGISTRY_URL`, `MONOREPO_REMOTE`, `UNITY_VERSION=2022.3`) has defaults — confirm on first run.
- To only PLAN without writing to the monorepo, run the skill's STEP 0–3.

> Switching the game to consume the package from the registry (instead of the in-Assets copy) is a separate Phase 2, documented in the skill — not done by this workflow.
