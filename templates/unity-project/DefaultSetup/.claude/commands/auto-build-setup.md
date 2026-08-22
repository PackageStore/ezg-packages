---
description: Install or upgrade the shared ezg-autobuild GitLab CI module (iOS + Android) on this Unity project
---

# Auto Build Setup (ezg-autobuild)

When the user runs `/auto-build-setup` (or asks "setup autobuild", "cài autobuild cho project này",
"dựng CI build iOS/Android", "lên version autobuild mới nhất", "rollback autobuild về vX.Y.Z"):

This is a thin entry point — the full procedure lives in the **`auto-build-setup` skill**
(`.claude/skills/auto-build-setup/SKILL.md`). Invoke that skill and follow its steps.

Any flags the user typed are passed straight through to the module's installer:
`--ios-source <branch>|skip`, `--android-source <branch>|skip`, `--ios-branch <name>`,
`--android-branch <name>`, `--project-code <code>`, `--unity-script-path <path>`,
`--module-ref vX.Y.Z`, `--setup-vars`. With no flags at all the installer auto-detects everything —
that is the normal case for both a first install and an upgrade.

## Summary of what the `auto-build-setup` skill does

- Preflight: this repo is a Unity project on a **gitlab.com** origin, tracked tree is clean, and
  `ezg-sm-space/ezg-autobuild` is reachable.
- Clones the module fresh to `/tmp/ezg-autobuild` (never as the README's `rm -rf … && …` one-liner,
  which eats the target path if the `&&` are lost) and runs its own `installer/install.sh`.
- That creates/refreshes the two build branches (default `IOS/AutoBuild` + `Android/AutoBuild`) with
  the thin `Fastfile` + `.gitlab-ci.yml` shims pinned to a module tag, and bakes
  `AutoBuild/check_build_machine.sh` for the Mac runner.
- **First install only:** pre-seeds `AutoBuild/.env` (git-ignored) so `setup_gitlab_vars.sh` runs
  non-interactively instead of hanging on an editor + Enter, then pushes CI/CD variables and the daily
  pipeline schedules. The user fills the secrets themselves — the skill never echoes a token.
- Flags the one destructive part up front: the installer **overwrites the project's `AutoBuild.cs`**
  with the module's contract version, so a PAD/AssetBundle build script would stop running in CI.
- Verifies both branches landed on the remote with matching refs, and puts HEAD back where it was.

> **Does not build anything.** It also never touches the Mac runner — the skill ends by telling the
> user to run `check_build_machine.sh` there and which commit-message flags trigger the first build.
