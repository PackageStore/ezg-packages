---
name: auto-build-setup
description: Install or upgrade the shared GitLab CI build module `ezg-sm-space/ezg-autobuild` on THIS Unity project by running its own `installer/install.sh` non-interactively. Creates/refreshes the two build branches (default `IOS/AutoBuild` + `Android/AutoBuild`), overlays the thin `AutoBuild/fastlane/Fastfile` + `.gitlab-ci.yml` shims pinned to a module tag, overwrites the project's `AutoBuild.cs` with the module's contract version, and — on a FIRST install only — pushes CI/CD variables + daily pipeline schedules to GitLab from `AutoBuild/.env`. Use when the user says "setup autobuild", "cài autobuild cho project này", "dựng CI build iOS/Android", "upgrade autobuild lên bản mới nhất", "cài lại autobuild", "rollback autobuild về vX.Y.Z". Requires: `origin` on gitlab.com, a clean working tree, and (first install) a GitLab PAT with scope `api` + Maintainer on both this project and the module repo. Does NOT run any build and does NOT touch the Mac runner.
---

# Auto Build Setup — install/upgrade the `ezg-autobuild` CI module on this project

`ezg-autobuild` (`gitlab.com/ezg-sm-space/ezg-autobuild`) is a **shared** Unity CI module for iOS +
Android. Every consuming project keeps only two thin shim files pinned to a module tag — all the real
Ruby/fastlane lanes and the 9-job pipeline live in the module repo, so a fix lands once there and every
project picks it up on the next version bump.

This skill drives the module's **own installer** (`installer/install.sh`). It does not reimplement it,
does not copy pipeline logic into the project, and never edits the shims by hand.

**What the installer writes onto the two build branches (and nowhere else):**

| Path | What |
|---|---|
| `AutoBuild/fastlane/Fastfile` | shim — `import_from_git` at `branch: "<MODULE_REF>"` |
| `.gitlab-ci.yml` | shim — `include: project: ezg-sm-space/ezg-autobuild, ref: '<MODULE_REF>'` + the `variables:` block (`PROJECT_CODE`, `AUTOBUILD_BRANCH_*`, `GIT_SOURCE_BRANCH_*`) |
| `AutoBuild/setup_gitlab_vars.sh`, `.env.example`, `Gemfile(.lock)`, `ios/ExportOptions.plist`, `.gitignore` | setup + build support files |
| `AutoBuild/check_build_machine.sh` | per-platform Mac-runner preflight, baked with this project's Unity version / runner tag / branch names |
| `<the project's AutoBuild.cs>` | **overwritten** with the module's template (see STEP 3) |
| `.gitattributes` | `merge=ours` for `/AutoBuild/**` + the `AutoBuild.cs` path |

**Sync direction is one-way: `<Release branch> → <build branch>`.** CI merges Release into the build
branch at the `git_sync` lane on every build. Never merge or push a build branch back into Release.

---

## ⚙️ Configuration

Resolve at the start of each run; ask only for what cannot be inferred.

```
TARGET          = the current Unity project repo root (git toplevel). Never a hardcoded path.
MODULE_REPO_SSH = git@gitlab.com:ezg-sm-space/ezg-autobuild.git
MODULE_REPO_HTTPS = https://gitlab.com/ezg-sm-space/ezg-autobuild.git
MODULE_DIR      = /tmp/ezg-autobuild          # throwaway clone; re-cloned fresh every run
MODULE_REF      = default: the `VERSION` file of the clone (= latest). Override with --module-ref vX.Y.Z
ENV_FILE        = <TARGET>/AutoBuild/.env     # setup-only secrets; git-ignored + .git/info/exclude'd
```

**Pass-through flags** (the user may give any of these; forward them verbatim to `install.sh`):
`--ios-source <branch>|skip`, `--android-source <branch>|skip`, `--ios-branch <name>`,
`--android-branch <name>`, `--project-code <code>`, `--unity-script-path <path>`,
`--module-ref <tag>`, `--setup-vars`.

Everything else auto-resolves — see the module's `README.md` §"3 cách cài AutoBuild" for the exact
detection order. **Do not invent values for flags the user did not ask for**; the installer's own
detection is the source of truth and it prints three `ℹ` lines saying what it picked and why.

### Two hazards this skill exists to avoid

1. **The README's copy-paste one-liner is a foot-gun.** It is
   `rm -rf -- /tmp/ezg-autobuild && git clone … && install.sh <target>`. If the `&&` are lost when the
   command is reflowed, `<target>` — the user's real project — becomes an argument to `rm -rf`.
   **Never emit that chained form.** Run the three commands as separate tool calls, as STEP 1 does.
2. **`install.sh` blocks on a human on a FIRST install.** After creating the branches it chains into
   `AutoBuild/setup_gitlab_vars.sh`, which — when `AutoBuild/.env` is absent or has an empty
   `GITLAB_TOKEN`/`GITLAB_PROJECT` — opens `$VISUAL`/`$EDITOR` and then sits on
   `read -p "…nhấn Enter…"`. That hangs the tool call forever. STEP 4 pre-seeds `.env` so the script
   takes its non-interactive path, and STEP 5 still runs it under `</dev/null` + `EDITOR=true` as a
   belt-and-braces guard.

---

## Pipeline

```
[0] PREFLIGHT   → git repo, origin on gitlab.com, Unity project, clean tracked tree, module reachable
[1] FETCH       → fresh clone of ezg-autobuild into MODULE_DIR; read VERSION + CHANGELOG head
[2] SCOPE       → fresh install or upgrade? show the user what will change; confirm
[3] AUTOBUILD.CS→ find the existing `class AutoBuild`; WARN if it does PAD/AssetBundle work
[4] .ENV        → fresh install only: exclude + pre-seed AutoBuild/.env, user fills the secrets
[5] INSTALL     → run installer/install.sh <TARGET> [flags] — non-interactive
[6] VERIFY      → both build branches on remote, shims pinned to MODULE_REF, HEAD back where it was
[7] REPORT      → check_build_machine.sh on the Mac runner + how to trigger the first build
```

---

## STEP 0 — Preflight

```bash
TARGET="$(git rev-parse --show-toplevel)" && cd "$TARGET" || echo "NOT-A-GIT-REPO"
git remote get-url origin || echo "NO-ORIGIN"
{ [ -d Assets ] && [ -d ProjectSettings ]; } && echo "unity=ok" || echo "unity=MISSING"
if git diff --quiet && git diff --cached --quiet; then
  echo "tree=clean"
else
  echo "tree=DIRTY"; git status --short --untracked-files=no
fi
```

Run it without `set -e` — every check must report, not abort on the first failure. Untracked files are
deliberately not part of the clean-tree test (the installer's own check ignores them too, and STEP 4
relies on that).

- **`origin` must be on `gitlab.com`.** The whole module is GitLab-native (`include: project:`,
  `import_from_git` over `CI_JOB_TOKEN`, CI/CD Variables, pipeline schedules). If `origin` points at
  GitHub or anywhere else → **STOP** and say so; there is no GitHub path.
- **Dirty tracked tree → STOP.** The installer checks out both build branches; uncommitted work rides
  along and lands in the scaffold commit. Tell the user to commit or stash; do not stash for them.
- **Bash required.** `install.sh` is bash-only. On Windows run it from Git Bash and expect
  `uuidgen` (used only when a brand-new `AutoBuild.cs.meta` must be generated) to be missing — if it
  is, hand the run to a macOS/Linux machine rather than patching around it.
- Confirm the module repo is reachable, SSH first, and **never let git prompt**:
  ```bash
  export GIT_TERMINAL_PROMPT=0
  export GIT_SSH_COMMAND='ssh -o BatchMode=yes -o ConnectTimeout=8'
  git ls-remote --heads git@gitlab.com:ezg-sm-space/ezg-autobuild.git >/dev/null 2>&1 \
    && echo "MODULE_URL=ssh" || echo "MODULE_URL=try-https"
  ```
  If SSH fails, retry with the HTTPS URL. If both fail → STOP: the user has no access to
  `ezg-sm-space` (that is a GitLab permission, no skill can grant it).
- Keep `GIT_TERMINAL_PROMPT=0` exported for the rest of the run so a missing credential fails fast
  instead of hanging on a hidden password prompt.

## STEP 1 — Fetch the module

Three **separate** commands — never chained with `&&` (see hazard 1):

```bash
rm -rf /tmp/ezg-autobuild
```
```bash
git clone --quiet git@gitlab.com:ezg-sm-space/ezg-autobuild.git /tmp/ezg-autobuild
```
```bash
cat /tmp/ezg-autobuild/VERSION; sed -n '1,40p' /tmp/ezg-autobuild/CHANGELOG.md
```

Re-cloning every run is deliberate: `install.sh` bakes the clone's `VERSION` into both shims, so a
stale clone would silently pin the project to an old module tag.

If the user asked to **pin or roll back**, they pass `--module-ref vX.Y.Z`; the clone still happens
from the default branch (the installer only reads `--module-ref` for what it writes into the shims).

## STEP 2 — Fresh install or upgrade?

```bash
cd "$TARGET"
git ls-remote --heads origin | awk '{print $2}' | sed 's#refs/heads/##'
```

- Any remote branch matching `autobuild` (case-insensitive), or the names remembered in
  `git config --local --get autobuild.iosBranch` / `autobuild.androidBranch` → **UPGRADE**.
  The installer skips the CI/CD-variable step entirely (they are already set). **Skip STEP 4.**
- Otherwise → **FIRST INSTALL**: the installer will chain into `setup_gitlab_vars.sh`. STEP 4 is
  mandatory.

Report to the user, before touching anything:
- which mode (fresh / upgrade), the current module ref (upgrade: read it out of the existing shim,
  `git show <build-branch>:.gitlab-ci.yml | grep "ref:"`) and the ref about to be installed;
- the release-source branches and build-branch names the installer is likely to resolve — but say
  plainly that the installer's own three `ℹ` lines are authoritative;
- **⚠ if the project has only one shared branch** (e.g. only `main`, no `IOS/Release` /
  `Android/Release`): both platforms will fork from the default branch, so every commit there becomes
  shippable — there is no release gate. Offer to create the two Release branches first.

Get an explicit go-ahead before STEP 5. This pushes branches to a shared remote.

## STEP 3 — The `AutoBuild.cs` overwrite (read this before saying yes)

From module v1.1.0 the installer **always overwrites** the project's `AutoBuild.cs` with its own
template, in place (same path, same `.meta` guid). It does this because the build logic is a contract
with the pipeline: the scene list must come from `EditorBuildSettings.scenes` and a failed build must
`EditorApplication.Exit(1)` — a project script missing either makes Unity exit 0 on a broken build and
the job goes green while the real failure surfaces stages later.

```bash
grep -rl "class AutoBuild" Assets --include="*.cs"
grep -rl "static void PerformBuild" Assets --include="*.cs"
```

- **0 matches** → the installer writes to `--unity-script-path` (default `Assets/_Project/Core/Build`).
- **1 match** → that exact file is overwritten in place.
- **≥2 files declaring `class AutoBuild`** → the installer stops and lists them. Resolve the duplicate
  first; do not pick one arbitrarily.

**⚠ Check what the current file actually does before agreeing.** This template ships
`Editor/Build/AutoBuild.cs` alongside `CustomPADBuildMenu.cs` and `RenameAssetBundlesForPAD.cs` — if
the project's `AutoBuild.cs` drives **Play Asset Delivery / AssetBundle renaming**, the module's
template does **not**, and `merge=ours` in `.gitattributes` keeps the module's version on the build
branch forever. CI builds would then ship without PAD. Read the file, tell the user exactly what it
would lose, and let them decide. The old version is never destroyed — it stays on the Release branch
and in git history.

## STEP 4 — Pre-seed `AutoBuild/.env` (FIRST INSTALL only)

Order matters: exclude the file **before** it contains a token.

```bash
cd "$TARGET"
grep -qxF 'AutoBuild/.env' "$(git rev-parse --git-dir)/info/exclude" 2>/dev/null \
  || printf 'AutoBuild/.env\n' >> "$(git rev-parse --git-dir)/info/exclude"
mkdir -p AutoBuild
cp /tmp/ezg-autobuild/installer/.env.example AutoBuild/.env
```

`AutoBuild/` is untracked at this point, which the installer's clean-tree check explicitly allows
(it only inspects tracked changes), and `git checkout` of the build branch leaves the untracked
`.env` in place. Copying the template from the freshly cloned module — not from memory — keeps the
key set in sync with the module version being installed.

Pre-fill only what is derivable, with `sed`:

```bash
PROJ="$(git remote get-url origin | sed -e 's#^git@gitlab.com:##' -e 's#^https://gitlab.com/##' -e 's#\.git$##')"
sed -i.bak -e "s#^GITLAB_PROJECT=.*#GITLAB_PROJECT=$PROJ#" AutoBuild/.env && rm -f AutoBuild/.env.bak
```

Then **the user fills the rest themselves** — this is the one human step, by design:

- `GITLAB_TOKEN` — PAT scope `api`, Maintainer on **both** this project and `ezg-sm-space/ezg-autobuild`
  (it is used to allowlist Token Access on the module repo). Setup-only; safe to blank out afterwards.
  The machine may already have one exported as `GITLAB_PERSONAL_ACCESS_TOKEN` for the `gitlab` MCP
  server in `.mcp.json` — if the user says to reuse it, **rewrite the existing line in place**; never
  append a second `GITLAB_TOKEN=` (the script's `read_env_var` echoes *every* match, so a duplicated
  key yields a multi-line token and every API call fails), and never put the value on a command line:
  ```bash
  python3 -c 'import os,io,re;p="AutoBuild/.env";s=io.open(p,encoding="utf-8").read();s=re.sub(r"(?m)^GITLAB_TOKEN=.*$","GITLAB_TOKEN="+os.environ["GITLAB_PERSONAL_ACCESS_TOKEN"],s,count=1);io.open(p,"w",encoding="utf-8").write(s)'
  ```
  (one line, no heredoc — an indented heredoc terminator inside a list item would not close)
- `CI_API_TOKEN` — Project/Group Access Token, scope `api`, **Maintainer**. The pipeline writes the
  bumped version back to CI/CD Variables with it; `CI_JOB_TOKEN` cannot, and the store then rejects
  every build after the first as a duplicate build number.
- `GIT_USER`, `GIT_EMAIL`, plus the iOS block (`APPLE_ID`, `APPLE_TEAM_ID`, `IOS_DIST_P12` + password,
  `IOS_MOBILEPROVISION`) and/or the Android block (`ANDROID_KEYSTORE` + passwords, alias,
  `GOOGLE_PLAY_JSON_KEY`). File-type vars take a **path to the file on this machine**; the script
  uploads the contents (base64 for binaries).
- Optional: `IOS_SCHEDULE_TIME` / `ANDROID_SCHEDULE_TIME` (`HH:MM`) to create the daily pipeline
  schedules. Left empty → no schedule is created, which is not an error.
- **Leave commented lines commented.** `PROJECT_CODE` / `GIT_SOURCE_BRANCH_*` / `AUTOBUILD_BRANCH_*`
  are owned by `.gitlab-ci.yml`; a GitLab project variable (precedence 4) silently overrides the yaml
  (precedence 9). Setting `AUTOBUILD_BRANCH_*` wrong makes every job skip with no error to read.

Empty values are skipped, so an unfinished `.env` is fine — signing secrets can be added later and
pushed with a re-run (see "Re-running just the variable step"). Open the file for the user
(`open -e AutoBuild/.env` on macOS) and wait for them to confirm.

> **Never `cat`/`grep` the filled `.env` back into the conversation, and never echo a token value.**
> Verify only that the two required keys are non-empty:
> ```bash
> for k in GITLAB_TOKEN GITLAB_PROJECT; do
>   v=$(sed -n "s/^$k=//p" AutoBuild/.env | head -1 | sed -e 's/ *#.*//' -e 's/[[:space:]]//g')
>   [ -n "$v" ] && echo "$k=set" || echo "$k=EMPTY"
> done
> ```
> Strip the trailing `# comment` the way the script itself does — a naive `-F=` split reports the
> template's own comment as a filled value.

## STEP 5 — Run the installer

```bash
cd "$TARGET"
GIT_TERMINAL_PROMPT=0 EDITOR=true VISUAL=true \
  /tmp/ezg-autobuild/installer/install.sh "$TARGET" </dev/null
```

Append any pass-through flags the user asked for, e.g. `--module-ref v1.3.0`, `--ios-source skip`,
`--project-code V1`, `--setup-vars`.

- `</dev/null` + `EDITOR=true VISUAL=true`: the hang guard from hazard 2. If some future prompt appears,
  the read gets EOF and the script exits with a message instead of blocking forever.
- The installer is **idempotent** — re-running it resets the build branches to the newest scaffold.
- **A failure in the CI/CD-variable step does not undo the branches.** `install.sh` swallows that exit
  code on purpose and prints how to re-run it. Read its output rather than assuming a clean result:
  it prints `ℹ` lines for the resolved branches/sources/`PROJECT_CODE`, `⚠` for the `AutoBuild.cs`
  overwrite and for a build-branch rename, and `↩` when it puts HEAD back.
- It returns the working copy to the branch the user was on, even on Ctrl+C. If the output ends with
  `⚠ Không trả lại được nhánh …`, check out the original branch yourself before finishing.

## STEP 6 — Verify

```bash
cd "$TARGET"
git rev-parse --abbrev-ref HEAD                                    # back on the original branch?
git ls-remote --heads origin | awk '{print $2}' | grep -i autobuild # both build branches pushed?
git fetch --quiet origin
# the installer records the resolved names here (git config --local, not committed)
IOS_BUILD_BRANCH="$(git config --local --get autobuild.iosBranch || echo IOS/AutoBuild)"
ANDROID_BUILD_BRANCH="$(git config --local --get autobuild.androidBranch || echo Android/AutoBuild)"
for B in "$IOS_BUILD_BRANCH" "$ANDROID_BUILD_BRANCH"; do
  echo "--- $B"
  git show "origin/$B:.gitlab-ci.yml" | grep -E "ref:|PROJECT_CODE|AUTOBUILD_BRANCH|GIT_SOURCE_BRANCH"
  git show "origin/$B:AutoBuild/fastlane/Fastfile" | grep 'branch:'
done
```

The `ref:` in `.gitlab-ci.yml` and the `branch:` in the `Fastfile` must be the **same tag** — those are
the only two places a version lives. Anything else means a partial run; re-run STEP 5.

Do not report success on branches that exist only locally: the `git ls-remote` line above is the check
that matters.

## STEP 7 — Report

State plainly: mode (fresh/upgrade), module ref installed, the two build-branch names, each platform's
release source, `PROJECT_CODE`, which `AutoBuild.cs` was overwritten, and whether the CI/CD-variable
step ran / was skipped / failed.

Then hand over the two things the skill cannot do:

1. **The Mac runner is a different machine.** The installer baked
   `AutoBuild/check_build_machine.sh` onto both build branches, per platform. Copy it to the build
   machine and run it **as the user that runs `gitlab-runner`**:
   ```
   ./check_build_machine.sh          # ~5s
   ./check_build_machine.sh --deep   # + a real `bundle install`
   ```
   Any `CHƯA ĐẠT` item means the build will fail; each one prints its own fix. Don't hand-edit that
   script — a re-install overwrites it.
2. **Trigger the first build** with the commit-message flags, on the build branch:
   ```
   git commit -m "release -ab -au -al"    # on the Android build branch
   git commit -m "release -b -e -u -l"    # on the iOS build branch
   ```

Also remind them, once: `GITLAB_TOKEN` in `AutoBuild/.env` was only needed for setup and can be blanked
or the PAT revoked; the build reads GitLab CI/CD Variables, not this file.

---

## Re-running just the variable step

Secrets added later, a rotated certificate, a new schedule — no need to reinstall:

```bash
cd "$TARGET"
git checkout <build-branch>            # AutoBuild/ only exists there
./AutoBuild/setup_gitlab_vars.sh --env "$PWD/AutoBuild/.env"     # --env => non-interactive
git checkout -                          # go straight back; never sit on a build branch
```

Passing `--env` is what keeps it from opening an editor and waiting on Enter. The equivalent during a
full re-install is `install.sh … --setup-vars`.

## Common failures

| Symptom | Cause | Fix |
|---|---|---|
| Tool call hangs with no output | `setup_gitlab_vars.sh` waiting on its editor/Enter | you skipped STEP 4 or the `</dev/null` guard — kill it, pre-seed `.env`, re-run |
| `❌ Repo đích đang có thay đổi chưa commit` | dirty tracked files | commit/stash (user's call), re-run |
| `❌ … không giống project Unity` | run from the wrong directory | `cd` to the Unity project root |
| `❌ Nhánh '<x>' không tồn tại trên remote` | forced `--ios-source`/`--android-source` name is wrong | use one of the branches it lists, or `skip` |
| `❌ Project có N file cùng khai 'class AutoBuild'` | duplicate class | delete/rename the stale one first |
| `❌ Cần GITLAB_TOKEN + GITLAB_PROJECT` | `.env` incomplete | fill it, then re-run only the variable step (above) — branches are already pushed |
| `⚠ KHÔNG thấy runner nào gắn tag '…'` | no Mac runner with that tag | register the runner / fix `IOS_RUNNER_TAG` in GitLab; pipelines would sit `pending` |
| Pipeline shows only "Skipped", no error | `AUTOBUILD_BRANCH_*` set as a GitLab project variable, overriding the yaml | delete those project variables; the yaml owns them |
| `include`/`import_from_git` auth error on every consuming project | this project (or its group) is not allowlisted in the module repo's Settings > CI/CD > Token Access | ask a module maintainer to allowlist it |

## Never

- Never emit the chained `rm -rf … && git clone … && install.sh <path>` one-liner.
- Never hand-edit `.gitlab-ci.yml`, `AutoBuild/fastlane/Fastfile`, or `check_build_machine.sh` in the
  project — re-run the installer with the matching flag instead; the next install overwrites edits.
- Never merge or push a build branch into a Release branch. Sync is Release → build branch only, done
  by CI.
- Never create `IOS_BUILT_VERSION` / `ANDROID_BUILT_VERSION` (or `*_CODE`) as GitLab project variables —
  they are per-pipeline dotenv values; a project variable outranks dotenv and freezes the reported
  version.
- Never leave the working copy on a build branch, and never commit `AutoBuild/.env`.
- Never run a build, edit the Mac runner, or upload signing secrets by any route other than
  `setup_gitlab_vars.sh`.
