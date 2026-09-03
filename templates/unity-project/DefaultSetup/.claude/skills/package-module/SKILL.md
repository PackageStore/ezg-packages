---
name: package-module
description: Extract one specified module/folder from the current game repo into a clean UPM package and commit + push it directly to the main branch of the ezg-packages monorepo (MONOREPO_PATH — %USERPROFILE%\ezg-packages on Windows, $HOME/ezg-packages on macOS/Linux), where the push to main triggers GitHub Actions (publish.yml) to sign and publish it to the Easygoing scoped registry on Cloudflare R2. NON-destructive to the source repo (the module stays in Assets/ and keeps compiling). Authenticates to the monorepo over SSH; if SSH isn't set up on this machine, auto-invokes the /setup-package-push skill inline (one human step: gh auth login's browser approval) instead of stopping — falling back to a GitHub PAT only if that can't complete. Used when the user says "đóng module X thành package UPM" / "package this module" / "đẩy module X lên registry" / "extract X to a UPM package". Switching the game to consume the package from the registry is a separate Phase 2 (documented, not done here). To only PLAN without writing to the monorepo, run STEP 0–3. Do NOT use for "đóng module X thành unitypackage" / "publish unity package" / "đẩy X lên tab Unity Packages" — those go to /publish-unity-package instead (different pipeline: raw .unitypackage snapshot back into Assets/, not a UPM registry package). Cũng KHÔNG dùng cho "publish feature X lên hub" / "đẩy feature lên server" → /publish-feature (tab Features theo mã dự án, .unitypackage của một feature game, không phải UPM package).
---

# Package Module — UPM Extraction → `ezg-packages` monorepo `main` Agent

Take one **specified module** (a folder under `Assets/` in the current game repo), build a **clean, standards-compliant UPM package** from it, and **commit + push it straight to the `main` branch of the `ezg-packages` monorepo** (`MONOREPO_PATH`). The push to `main` triggers the monorepo's GitHub Actions workflow `publish.yml` → `scripts/validate.mjs` (lint gate) → `scripts/publish.mjs`, which packs + **digitally signs** each package with the Unity Package Manager CLI (`upm pack`, required by Unity 6.3+) and uploads the tarball + metadata to the **Cloudflare R2** bucket behind the **Easygoing** scoped registry. **No feature branch, no PR** — pushing to `main` IS the publish trigger.

> **Layout: flat, one folder per package id.** Every package lives at `packages/<scope>.<name>/` — the folder name IS the dotted package id (`packages/com.ezg.pooling/`). There are no category folders and no PascalCase folder names. `publish.mjs` walks `packages/*/package.json`, so a folder anywhere else is never published.
>
> **Publishing also refreshes the template.** `publish.mjs` chains `sync-unity-template-deps.mjs`, which writes the new version into `templates/unity-project/unity-template.json` and re-publishes `unity-template/latest.json`. That is what makes the package appear in Feature Hub's **UPM Packages** tab — you do not need a second step for it.

---

## ⚙️ Configuration

At the start of each run, resolve the following from user input or sensible defaults. Ask only for values that cannot be inferred:

```
MODULE_PATH      = required — full path (or repo-relative path) to the module folder in the game repo (repo-relative, e.g. under `<sourceRoot>/`, or absolute); SOURCE_ROOT is derived from it
PACKAGE_SCOPE    = user-specified, default com.ezg              # forms com.ezg.<name>
REGISTRY_URL     = user-specified, default https://upm-registry-worker.developer-a1f.workers.dev
REGISTRY_NAME    = scoped-registry display name, default Easygoing  # used in the install snippet
MONOREPO_REMOTE  = user-specified, default https://github.com/PackageStore/ezg-packages.git
MONOREPO_PATH    = user-specified (or env MONOREPO_PATH), default per OS:
                   Windows:      %USERPROFILE%\ezg-packages
                   macOS/Linux:  $HOME/ezg-packages
                   # If a clone already exists at this path, use it in place; only auto-clone if it is missing
PAT_FILE         = fallback only, used when SSH isn't set up — user-specified (or env EZG_PACKAGES_PAT_FILE), default per OS:
                   Windows: %LOCALAPPDATA%\ezg-packages.pat
                   macOS:   $HOME/Library/Application Support/ezg-packages/ezg-packages.pat
                   Linux:   ${XDG_CONFIG_HOME:-$HOME/.config}/ezg-packages/ezg-packages.pat
UNITY_VERSION    = user-specified (explicit value takes priority); default **2022.3** if not specified
                   # every com.ezg.* package on the registry ships "unity": "2022.3" — match it unless the module genuinely needs newer.
                   # do NOT auto-read from ProjectSettings/ProjectVersion.txt unless the user explicitly asks to match the game's version
```

**`MODULE_PATH` is the only required input** — the user provides a direct path to the module folder. `SOURCE_ROOT` is derived as the parent tree above it. All other values have defaults; ask once to confirm/override if running for the first time on a new module.

### Shell / OS compatibility

Snippets are provided for both **macOS/Linux (zsh/bash)** and **Windows (PowerShell)** — use the one matching the current machine. The game repo is always the current working directory — never a hardcoded path. Only the monorepo has an OS-specific default: `%USERPROFILE%\ezg-packages` on **Windows**, `$HOME/ezg-packages` on **macOS/Linux**. Quote every path. Do not use `%LOCALAPPDATA%` or backslash path examples on macOS.

### Git authentication — SSH first, PAT fallback

Resolved once per run, at STEP 4.0 (exact code there — this is the summary):

1. **SSH (preferred).** If `ssh -T git@github.com` reports "successfully authenticated", use the
   SSH remote `git@github.com:PackageStore/ezg-packages.git` directly. No token to inject,
   nothing to scrub from `origin` afterward.
   - **Not set up on this machine yet? Auto-fix it, don't just tell the user.** At STEP 4.0,
     when the SSH test fails, **invoke the `setup-package-push` skill inline right there**
     (`.claude/skills/setup-package-push/SKILL.md`) as part of this same run — it installs `gh`
     if missing, generates/registers an SSH key, and confirms Write access. Then re-run the SSH
     test before deciding whether to fall back to a PAT. The one part that can't be silently
     automated is `gh auth login`'s one-time browser approval (a GitHub security control) — if
     you can't complete that yourself, hand the exact command to the user, wait for their
     confirmation, then continue this same run. The user should not need to separately type
     `/setup-package-push` themselves first.
2. **GitHub PAT (fallback)** — only reached if the auto-onboarding above genuinely can't
   complete (`gh` unavailable and can't be installed, or the account confirmed lacking Write
   access to `PackageStore/ezg-packages` — an admin action outside any skill's control). This
   skill file is committed to git, so the PAT must **never** be written here. The token is read
   at runtime from a source that is NOT tracked by git, in this priority order:
   - **Environment variable `EZG_PACKAGES_PAT`** (preferred over the file — set it once per machine):
     ```powershell
     # Windows PowerShell
     setx EZG_PACKAGES_PAT "<your-token>"     # persists for future shells; reopen the terminal afterward
     ```
     ```bash
     # macOS zsh/bash, current shell
     export EZG_PACKAGES_PAT='<your-token>'
     # To persist, add the export line to ~/.zshrc (or your shell profile) outside this repo.
     ```
   - **A local secret file outside the repo** (fallback): `PAT_FILE` containing only the token on one line.

   If neither source yields a token → **stop** at STEP 4 and report exactly what's blocking
   (why SSH auto-onboarding couldn't complete, and that no PAT is set). Required PAT scope:
   classic PAT with `repo`, or fine-grained PAT with **Contents: read+write** on
   `PackageStore/ezg-packages`.

**PAT handling rules (security, PAT fallback only):**
- **Never** put the PAT in this file or any tracked file, never print it, never echo it into logs.
- Inject it via the remote URL form `https://<PAT>@github.com/...` **only on the clone/fetch/push command itself**; immediately reset `origin` to the clean HTTPS remote so the token isn't persisted in `.git/config`.
- Keep it in `$pat` (a shell variable) for the duration of the run only.

> **This skill does NOT modify the game repo.** The module stays in `Assets/` and the game keeps compiling. We **copy** the source into the monorepo, never `git mv` it out. **Switching the game to consume the package from the registry (removing the in-`Assets/` copy + adding the manifest dependency) is a separate Phase 2** — see the end of this file. It is intentionally NOT automated here because the registry version does not exist until the push to `main` has triggered CI and it has published.

**One module per run.**

---

## The two repos

| Role | Repo | This skill |
|---|---|---|
| **Source** (game) | the game repo — always the current working dir, whatever it is | **read-only** — reads `<MODULE_PATH>` |
| **Target** (packages) | `ezg-packages` monorepo at `MONOREPO_PATH` (existing clone of `PackageStore/ezg-packages`) | commits + pushes **directly to `main`** adding/updating `packages/<scope>.<name>/` |

---

## Relationship to the monorepo's existing tooling

`PackageStore/ezg-packages` ships its own publish/admin toolchain under `scripts/`. This skill **complements** it — it does NOT replace or fight it. Know the boundaries so you don't run two tools for the same job:

| In the monorepo | What it does | How this skill relates |
|---|---|---|
| `.github/workflows/publish.yml` | On push to `main` touching `packages/**`: `npm install` in `scripts/`, install the `upm` CLI, run `validate.mjs`, then `publish.mjs`. | **This skill relies on it** — the skill only pushes to `main`; CI does the actual publish. Same path, no double-publish. |
| `scripts/validate.mjs` | Lints every `packages/*/package.json` (name/scope/version/unity/description). CI runs it **before** publishing — a bad manifest fails the run and nothing is published. | Run the same rules mentally at STEP 4; if CI fails here, fix the manifest and push again (no version bump needed — nothing was published). |
| `scripts/publish.mjs` | Skips versions already on R2 · `upm pack` (signed) · computes integrity/shasum · merges registry metadata · uploads tarball + metadata to R2 · chains `sync-unity-template-deps.mjs`. | The publisher. Never invoke it from a game repo — it needs R2 credentials that live only in the maintainer's `scripts/.env` and in CI secrets. |
| `scripts/list.mjs` · `unpublish.mjs` · `rollback.mjs` · `deprecate.mjs` | Maintainer admin (inspect / remove a version / repoint `latest` / deprecate), also exposed via `.github/workflows/admin.yml`. | Out of scope here. If a bad version ships, tell the user to roll back with those — do not try to overwrite a published version. |

> **Placement is load-bearing:** `publish.mjs` globs `packages/*/package.json` — exactly one level deep. A package at the repo root, or nested deeper (`packages/Modules/Foo/`), is **never published**. Always `packages/<scope>.<name>/`.
>
> **Signing:** publishing runs `upm pack`, which needs the Unity service-account credentials configured as CI secrets. This is CI's job — you never sign locally.

---

## Conventions

| Thing | Rule | Example |
|---|---|---|
| Target folder | `packages/<id>/` — the folder name **is** the dotted package id. No category folders, no PascalCase. | `packages/com.ezg.pooling/`, `packages/com.ezg.csv-reader/` |
| Package id (`name`) | `<PACKAGE_SCOPE>.<name>` — lowercase, `kebab-case` for multi-word | `com.ezg.pooling`, `com.ezg.animation-sequencer` |
| `displayName` | human-readable title, prefixed `EZG` | `"EZG Pooling"`, `"EZG CSV Reader"` |
| Runtime asmdef name | `Ezg.Package.<PascalName>`, with `rootNamespace` set to the same string. If the source folder already has an asmdef, reuse its name only if it already matches this shape. | `Ezg.Package.Pooling` |
| Editor asmdef | `<RuntimeAsmdef>.Editor`, `includePlatforms: ["Editor"]` | `Ezg.Package.Pooling.Editor` |
| `unity` field | Use the `UNITY_VERSION` resolved from config | `"2022.3"` |
| Version | New package → `0.0.1`. Update → bump semver (ask patch/minor/major). **Published versions are immutable**: `publish.mjs` skips any version whose tarball is already on R2, so re-pushing without a bump is a silent no-op. To undo a bad release use `rollback.mjs` / `unpublish.mjs` — never reuse a version number. | — |
| `description` | **Must be a clear, complete sentence** describing what the package does. Generic/placeholder text (`"one-line purpose"`, `"TODO"`, empty) is **not acceptable** — stop and ask for a real description. `validate.mjs` gates on this. | `"Game-agnostic GameObject pooling for Unity…"` |
| `author` | `{ "name": "EZG Studio" }` — matches every package on the registry. The user may override; **never include an email**. | `"author": { "name": "EZG Studio" }` |
| Optional fields | `keywords` (2–4), `license` (`"MIT"`), `category`, `author.url` — include when known; the minimal published packages omit them. | — |
| **package.json `dependencies`** | **ONLY registry-resolvable ids** already on the Easygoing registry (typically `com.ezg.*`). Every other lib → asmdef `references` by name + a documented **peer requirement** in README. | `"com.ezg.core": "0.1.0"` |
| Odin | `using Sirenix` / Odin attributes wrapped in `#if ODIN_INSPECTOR`. | — |
| Layering | A package must never depend on something less general than itself, and the dependency graph must stay **acyclic**. Infra (`com.ezg.core`, `com.ezg.singleton`…) never depends on a feature module. | — |

### Generality model (for reference)

There are no category folders, so generality is a judgement call encoded in the dependency direction, not in a path:

- **Infra** — universal, game-agnostic building blocks (`com.ezg.core`, `com.ezg.singleton`, `com.ezg.dictionary`). Must NOT contain any specific game's business logic, and must not depend on a feature module.
- **Feature/system modules** — extracted from a game (`com.ezg.iap`, `com.ezg.csv-reader`, `com.ezg.pooling`). One module = one package. May depend on infra and on each other, acyclically.
- **Repacked third-party libs** live on the registry too (mirrored under the same scope list), but extracting your own code never produces one.

**Business/SDK leak = hard stop** (see DEP-GATE). Examples of leaks that disqualify a module from packaging:
- Hardcoded game-specific CSV key constants (e.g. `ItemMerge`, `CookingRecipes`)
- Hardcoded `Assets/` paths for CSV/Resources (e.g. `<featuresRoot>/<Feature>/CsvConfig/`)
- Direct references to game-specific singletons (`DataManager`, `DataPlayer`, `GameEnums.Features`, `UIManager` if game-specific)
- Third-party SDK types without an asmdef boundary (Supabase, Cloudflare client, Google.Play.AssetDelivery compiled directly into a Framework)

If a module has these leaks, stop and report — do not package unless the user explicitly accepts known debt (document it in README).

**Known Odin pattern:** guard all `using Sirenix.*` and Odin attributes with `#if ODIN_INSPECTOR … #endif` so the package compiles in projects without Odin. Leave the semantic behavior intact, just guard the attribute syntax. Odin is a paid asset that ships **inside `Assets/`**, not on this registry — it can only ever be a guarded **peer requirement**, never a `package.json` dependency.

---

## Pipeline

```
[0] IDENTIFY  → category + source folder + package id + folder name + asmdef name + version (new vs update)
[1] AUDIT     → classify deps (registry-resolvable vs external peer libs) + leaks + editor split
[2] DEP-GATE  → every registry dep must already be published (or pushed in this same run); record external peer libs; block on business/SDK leak
[3] PLAN      → show package contents + deps + version; warn that push to main = immediate publish; get explicit confirmation
[4] BUILD     → on monorepo main (pull first): create packages/<scope>.<name>/ (copy source + .meta, scaffold package.json/asmdef/README/CHANGELOG, wrap Odin, author metas for new files)
[5] VERIFY    → validate.mjs (if the monorepo's scripts/ deps are installed) + npm pack --dry-run + static dep check + .meta/GUID gates
[6] PUSH      → commit on main + push origin main → CI (publish.yml) publishes. No remote → stop at local commit + give push commands.
[7] REPORT    → pushed commit, what CI is publishing, registry install snippet, + the separate Phase 2 (switch game to consumer — NOT done now)
```

STEP 0–3 are **non-destructive** (nothing written anywhere). STEP 4 onward writes to the **monorepo only**, after explicit confirmation. If the user wants a plan only, stop after STEP 3.

---

## STEP 0 — Identify the target

1. Resolve the **source folder** from the `MODULE_PATH` the user provided — use it directly. `SOURCE_ROOT` is the parent tree. If the path is ambiguous or the folder doesn't exist, ask once to clarify.
2. **Judge generality** using the Generality model — is this reusable infra or a feature module? It does not change where the folder goes (always `packages/<id>/`), but it decides the allowed dependency direction and how game-specific the code is allowed to be.
3. Derive the **folder name** (PascalCase/kebab, human-readable), the **package id** (`<PACKAGE_SCOPE>.<foldername-lowercased>`) and the **asmdef name** (per the Conventions table). Confirm with the user if derived rather than explicitly stated.
4. **New vs update:** check `<MONOREPO_PATH>/packages/<scope>.<name>/package.json`. If absent there, query the registry (`npm view <id> version --registry <REGISTRY_URL>`).
   - Missing → **new package**, version `0.0.1`.
   - Exists → **update**; read its current version and ask the bump level (patch / minor / major; default patch). The new version must be greater (registry versions are immutable).
5. Detect whether the source folder **already has an asmdef** (Glob `*.asmdef`):
   - **No asmdef** — it currently auto-compiles into its parent assembly. It becomes its own assembly in the package.
   - **Has an asmdef** — reuse its name or normalize per the Conventions table.

State the resolved `{category, source folder, folder name, package id, asmdef name, new|update + version, asmdef status}`.

---

## STEP 1 — Audit dependencies & leaks

Use **codegraph first**; grep only for `using` directives, string literals, define symbols.

1. **Outgoing deps** — `codegraph_callees` / `codegraph_impact` on the folder's public types + `Grep` for `using ` across the folder. Classify each:

   | Bucket | Goes where |
   |---|---|
   | Unity / BCL (`UnityEngine`, `System.*`) | nothing to declare (engine auto); `System.*` asmdef ref only if used |
   | A package **already on the Easygoing registry** — its scoped-registry scopes are exactly `com.ezg`, `com.cysharp`, `com.google`, `com.coffee` (e.g. `com.ezg.core`, `com.ezg.easy-event-manager`, `com.cysharp.unitask`) | asmdef `references` **+** `package.json` dependency (registry-resolvable). Must already be published → DEP-GATE. |
   | **External peer lib** NOT on the registry | asmdef `references` by name **+ documented as a peer requirement** in README. **Do NOT** put in `package.json` dependencies. |
   | Precompiled DLL (Firebase-style) | `overrideReferences: true` + `precompiledReferences: [...]` in asmdef; DLLs themselves are a peer requirement. |

   > Note: UniTask (`com.cysharp.unitask`) and the `com.ezg.*` repacks (e.g. `com.ezg.easy-event-manager`) ARE on this registry → real `package.json` dependencies. DOTween and Odin are **not** (their scopes aren't registered) → asmdef reference + peer requirement. Confirm what the consuming project actually has before choosing.

2. **Leak scan** (grep inside the folder):
   - Odin: `using Sirenix` / `[TabGroup]`/`[ShowIf]`/`[Button]`/`[Title]` → wrap in `#if ODIN_INSPECTOR` (STEP 4).
   - **Business / game-specific leak:** hardcoded game CSV paths, CSV key constants, game-specific singletons (`DataManager`, `DataPlayer`, feature enums, data-access facades unique to the game). A reusable package **must not** carry these → DEP-GATE hard stop.
   - Editor-only code (`#if UNITY_EDITOR`, `using UnityEditor`, an `Editor/` subfolder) → goes into the package `Editor/` assembly.
   - `Resources/`, scenes, `.asmref` inside the folder → flag (load-path semantics change when published).

3. **Incoming consumers** — `codegraph_callers` to list who in the game repo uses this module. Record them: they are the Phase-2 work list.

Produce a compact audit (kept in reasoning): outgoing deps by bucket, peer libs, registry deps, Odin files, editor files, business leaks, DLL refs, incoming consumers.

---

## STEP 2 — Dependency gate

- **Registry dependencies must already be published** to the Easygoing registry (check by `npm view <id> version --registry <REGISTRY_URL>` returning a version, or that `<MONOREPO_PATH>/packages/<dep-id>/package.json` exists), **or** be pushed earlier in this same run. If a needed registry dep is unpublished → report which one to package first, then **stop**.
- **External peer libs** are **not** a blocker — they are documented as peer requirements.
- **Business / SDK leak** → hard stop. Continue only if the user explicitly accepts shipping with leaks (discouraged; record as known debt in README) — better: narrow scope or file a cleanup task.

---

## STEP 3 — Plan & confirm

Present a **package summary card** and wait for explicit confirmation before doing anything destructive.

### New package — summary card

```
Package id     : <scope>.<name>@<version>
Folder         : packages/<scope>.<name>/
Display name   : <displayName>
Description    : <full description — must be a real sentence, not a placeholder>
Source folder  : <MODULE_PATH>
Asmdef         : <RuntimeAsmdef>  [+ <RuntimeAsmdef>.Editor]  (if editor code)
Unity minimum  : <UNITY_VERSION>  ⚠️ UPM hides this package in projects below this version
Registry       : <REGISTRY_URL>  (name: <REGISTRY_NAME>)
Monorepo target: packages/<scope>.<name>/  →  ezg-packages main

Dependencies (package.json)  : <scope>.dep1@x.y.z | none
Peer requirements (asmdef only, consumer must provide):
  - <lib1>, <lib2>, …

Odin guards needed : yes / no
Editor assembly    : yes / no
Known leaks / debt : none / <description>

Source repo        : NOT modified — Phase 2 (switch to consumer) is separate
```

### Update package — summary card (extends new-package card)

```
(all fields above, plus:)
Previous version   : <old>  →  New version: <new>  (<patch|minor|major> bump)
Changes since last publish:
  - <bullet: what changed in the source folder>
Registry currently : <id> latest = <old>
```

After showing the card, **review the `Description` field**:
- If the description is missing, empty, generic (`"one-line purpose"`, `"TODO"`, `"..."`) or doesn't clearly explain what the package does → ask the user to provide a proper description **before** confirming.
- A good description is 1–2 sentences that answer: *"What does this package do, and why would a consumer install it?"*

Then ask once:

> **"Publish `<scope>.<name>@<version>` to the `ezg-packages` monorepo `main`? Pushing is immediate (CI auto-publishes) and the version is immutable. (yes / plan-only / adjust)"**

Proceed only on explicit **yes**. On **adjust** — update the relevant fields and re-show the card. On **plan-only** — stop here.

---

## STEP 4 — Build the package on monorepo `main`

All writes happen in the working clone under `MONOREPO_PATH`, directly on the **`main`** branch (no feature branch).

0. **Resolve paths + auth (SSH first, auto-onboard if missing, PAT as last resort — no token ever printed):**

   **First, probe SSH:**
   ```powershell
   # Windows PowerShell
   $repo = if ($env:MONOREPO_PATH) { $env:MONOREPO_PATH } else { (Join-Path $env:USERPROFILE 'ezg-packages') }
   $sshRemote = 'git@github.com:PackageStore/ezg-packages.git'
   $httpsRemote = 'https://github.com/PackageStore/ezg-packages.git'

   $sshTest = ssh -o BatchMode=yes -o ConnectTimeout=5 -T git@github.com 2>&1
   $useSsh = $sshTest -match 'successfully authenticated'
   ```
   ```bash
   # macOS zsh/bash
   repo="${MONOREPO_PATH:-$HOME/ezg-packages}"
   ssh_remote='git@github.com:PackageStore/ezg-packages.git'
   https_remote='https://github.com/PackageStore/ezg-packages.git'

   use_ssh=false
   if ssh -o BatchMode=yes -o ConnectTimeout=5 -T git@github.com 2>&1 | grep -qi 'successfully authenticated'; then
     use_ssh=true
   fi
   ```

   **If that probe is false, auto-onboard right now — do not stop and tell the user to run
   something separately.** Invoke the **`setup-package-push`** skill's procedure inline, as part
   of this same run (`.claude/skills/setup-package-push/SKILL.md`): it installs `gh` if missing,
   generates/registers an SSH key, and confirms Write access to `PackageStore/ezg-packages`. Its
   one unavoidable interactive moment is `gh auth login`'s one-time browser approval (a GitHub
   security control, not something to script around) — if you can't complete that yourself, hand
   the user the exact command, wait for their confirmation, then continue this same run. Once it
   finishes (either outcome), **re-run the SSH probe above** and update `$useSsh` / `use_ssh`
   with the fresh result before continuing.

   **Then pick the remote from the (possibly now-updated) result:**
   ```powershell
   # Windows PowerShell
   if ($useSsh) {
     $remote = $sshRemote
     $authUrl = $sshRemote          # no token involved — used as-is everywhere below
   } else {
     $remote = $httpsRemote
     $patFile = if ($env:EZG_PACKAGES_PAT_FILE) { $env:EZG_PACKAGES_PAT_FILE } else { Join-Path $env:LOCALAPPDATA 'ezg-packages.pat' }
     $pat = $env:EZG_PACKAGES_PAT
     if ([string]::IsNullOrWhiteSpace($pat)) {
       if (Test-Path $patFile) { $pat = (Get-Content $patFile -Raw).Trim() }
     }
     if ([string]::IsNullOrWhiteSpace($pat)) {
       throw "SSH auto-onboarding (setup-package-push) could not get push access to PackageStore/ezg-packages, and EZG_PACKAGES_PAT is not set. See Configuration to set the PAT, or resolve what setup-package-push reported."
     }
     $authUrl = "https://$pat@github.com/PackageStore/ezg-packages.git"
   }
   ```
   ```bash
   # macOS zsh/bash
   if [ "$use_ssh" = true ]; then
     remote="$ssh_remote"
     authUrl="$ssh_remote"          # no token involved — used as-is everywhere below
   else
     remote="$https_remote"
     pat_file="${EZG_PACKAGES_PAT_FILE:-$HOME/Library/Application Support/ezg-packages/ezg-packages.pat}"
     pat="${EZG_PACKAGES_PAT:-}"
     if [ -z "$pat" ] && [ -f "$pat_file" ]; then
       pat="$(tr -d '\r\n' < "$pat_file")"
     fi
     if [ -z "$pat" ]; then
       echo "SSH auto-onboarding (setup-package-push) could not get push access to PackageStore/ezg-packages, and EZG_PACKAGES_PAT is not set." >&2
       echo "See Configuration to set the PAT, or resolve what setup-package-push reported." >&2
       exit 1
     fi
     authUrl="https://${pat}@github.com/PackageStore/ezg-packages.git"
   fi
   ```
   `ssh -T git@github.com` always exits non-zero (GitHub closes the channel after the greeting) —
   check the printed text, never the exit code. If auth resolution fails entirely (auto-onboarding
   ran and still couldn't get SSH working, and no PAT is set) → **stop** and report exactly what's
   missing. All later steps use `$repo`/`$remote`/`$authUrl` exactly as before — they work
   unchanged for both auth modes.

1. **Ensure the clone exists & is fresh (use the existing clone at `MONOREPO_PATH`; only auto-clone if missing):**
   ```powershell
   # Windows PowerShell
   if (-not (Test-Path (Join-Path $repo '.git'))) {
     git clone $authUrl $repo
     git -C $repo remote set-url origin $remote
   }
   git -C $repo checkout main
   git -C $repo fetch $authUrl main
   ```
   ```bash
   # macOS zsh/bash
   if [ ! -d "$repo/.git" ]; then
     git clone "$authUrl" "$repo"
     git -C "$repo" remote set-url origin "$remote"
   fi
   git -C "$repo" checkout main
   git -C "$repo" fetch "$authUrl" main
   ```
   - **Working tree must be clean** before building: `git -C <repo> status --porcelain` empty. the `MONOREPO_PATH` clone is a real working clone the user may use directly — if it is dirty, **do NOT blindly reset**; show the dirty files and ask the user before discarding. Only `reset --hard origin/main` if the user confirms the changes are disposable (e.g. leftovers from a prior failed run).
   - Fast-forward:
     - PowerShell: `git -C $repo pull --ff-only $authUrl main`
     - zsh/bash: `git -C "$repo" pull --ff-only "$authUrl" main`
   - **Never** leave the token in `origin`'s URL (PAT fallback only — SSH mode has no token to leak).

2. **Create** `packages/<scope>.<name>/` with `Runtime/` (+ `Editor/` if editor code).

3. **`package.json`** at the package root (`unity` from `UNITY_VERSION`, deps = registry-resolvable only):
   ```json
   {
     "name": "<scope>.<name>",
     "displayName": "<DisplayName>",
     "version": "<x.y.z>",
     "unity": "<UNITY_VERSION>",
     "description": "<full description confirmed in STEP 3>",
     "author": { "name": "EZG Studio" },
     "dependencies": { "<scope>.<dep>": "<version>" }
   }
   ```
   - `author.name` is `"EZG Studio"` on every package in the monorepo — keep it. Override only if the user asks.
   - Optional fields to add when known: `"keywords": [...]`, `"license": "MIT"`, `"category": "..."`, `"author": { "name": "...", "url": "..." }`.
   - **`description` must be the real, user-confirmed sentence from the summary card** — not a placeholder. This text appears in the Unity Package Manager UI, the registry index, and consumer docs. If STEP 3 was skipped or the field is still a placeholder, **stop and ask** before writing.
   - **Do NOT add any email information** anywhere in the file.
   - Use `"dependencies": {}` (or omit) if there are no registry deps.

4. **Runtime asmdef** `Runtime/<RuntimeAsmdef>.asmdef`:
   ```json
   {
     "name": "<RuntimeAsmdef>",
     "rootNamespace": "",
     "references": [ "<assembly names: registry deps + peer libs>" ],
     "includePlatforms": [],
     "excludePlatforms": [],
     "allowUnsafeCode": false,
     "overrideReferences": false,
     "precompiledReferences": [],
     "autoReferenced": true,
     "defineConstraints": [],
     "versionDefines": [],
     "noEngineReferences": false
   }
   ```
   Precompiled DLLs → `overrideReferences: true` + `precompiledReferences`.

5. **Editor asmdef** `Editor/<RuntimeAsmdef>.Editor.asmdef` (only if editor code): `includePlatforms: ["Editor"]`, references `<RuntimeAsmdef>` + editor refs.

6. **Copy source** from `<MODULE_PATH>` into `Runtime/` (editor files into `Editor/`), **carrying every `.meta`**. Use file copies (cross-repo — NOT `git mv`). A `.cs` without its `.meta` loses its GUID → copy both. Preserve subfolder structure + folder `.meta`. On Windows PowerShell, use `Copy-Item -Recurse -Force`; on macOS, `rsync -a "$src/" "$dst/"`.

7. **Wrap Odin** in copied files: guard `using Sirenix...` and Odin attributes with `#if ODIN_INSPECTOR ... #endif`. Leave Vietnamese comments intact.

8. **Namespaces:** keep as-is; renaming ripples into consumers. Only fix if it breaks compilation.

9. **`.meta` for NEW files** the skill creates (new asmdef, new folders, package.json, README.md, CHANGELOG.md, LICENSE):
   - Hand-author asmdef + folder metas with deterministic GUIDs (mirror the shape of the existing metas in the monorepo — folder meta uses `folderAsset: yes` + `DefaultImporter`; asmdef meta uses `AssemblyDefinitionImporter`).
   - ⚠️ **A Unity GUID MUST be exactly 32 lowercase HEX chars (`0-9a-f`).** Do **NOT** build the GUID by slicing letters out of the folder/asmdef name — names may contain non-hex letters and Unity silently **rejects** any `.meta` with a non-hex GUID, making the installed package appear **EMPTY**.
   - **Generate the GUID by hashing the name** so it's deterministic AND guaranteed hex:
     ```powershell
     # Windows PowerShell
     $md5 = [System.Security.Cryptography.MD5]::Create()
     function New-Guid32([string]$seed){ -join ([System.BitConverter]::ToString($md5.ComputeHash([Text.Encoding]::UTF8.GetBytes($seed))) -replace '-').ToLower()[0..31] }
     New-Guid32 "<scope>.<name>/Runtime"
     New-Guid32 "<scope>.<name>/Runtime/<RuntimeAsmdef>.asmdef"
     ```
     ```bash
     # macOS zsh/bash
     new_guid32() {
       if command -v md5sum >/dev/null 2>&1; then
         printf '%s' "$1" | md5sum | awk '{print tolower($1)}'
       else
         printf '%s' "$1" | md5 -q | tr '[:upper:]' '[:lower:]'
       fi
     }
     new_guid32 "<scope>.<name>/Runtime"
     new_guid32 "<scope>.<name>/Runtime/<RuntimeAsmdef>.asmdef"
     ```
   - **After writing every new meta, VALIDATE** each `guid:` line matches `^[0-9a-f]{32}$`. If any fails → stop and regenerate; never push a non-hex GUID.
   - Copied source keeps its **original** `.meta` — never regenerate those.

10. **README.md** — purpose, "package ↔ source folder" mapping, registry dependencies, **peer requirements**, any known debt.

11. **CHANGELOG.md** — follows [Keep a Changelog](https://keepachangelog.com) format. Place at package root (`packages/<scope>.<name>/CHANGELOG.md`).

    ⚠️ **Root-level files MUST each have a sibling `.meta` — author them.** Packing works without them, but a registry-installed package lives in an **immutable** folder, so Unity cannot generate the missing metas itself and logs for every one: `Asset Packages/<id>/<file> has no meta file, but it's in an immutable folder. The asset will be ignored.` The correctly-published packages in the monorepo (`com.ezg.pooling`, `com.ezg.core`, …) all ship `package.json.meta` + `README.md.meta` + `CHANGELOG.md.meta` alongside `Runtime.meta`. Author a meta for **every** root file the package ships — `package.json`, `README.md`, `CHANGELOG.md`, and `LICENSE`/`LICENSE.md` if present — using deterministic hex GUIDs (seed `<scope>.<name>/<filename>`) per the GUID rules in STEP 4.9. Importer type by extension:
    - `package.json` → `PackageManifestImporter`
    - `*.md` / `LICENSE` / any other text → `TextScriptImporter`

    ```yaml
    # package.json.meta
    fileFormatVersion: 2
    guid: <new_guid32 "<scope>.<name>/package.json">
    PackageManifestImporter:
      externalObjects: {}
      userData:
      assetBundleName:
      assetBundleVariant:
    ```
    ```yaml
    # README.md.meta  (and CHANGELOG.md.meta, LICENSE.md.meta — same shape)
    fileFormatVersion: 2
    guid: <new_guid32 "<scope>.<name>/README.md">
    TextScriptImporter:
      externalObjects: {}
      userData:
      assetBundleName:
      assetBundleVariant:
    ```

    **Format rules:**
    - Header line: `# Changelog`
    - Each version block: `## [<version>] - <YYYY-MM-DD>` (use the **session date**, ISO 8601).
    - Group changes under `### Added` / `### Changed` / `### Fixed` / `### Removed` (omit empty ones).
    - Each bullet: concise, past-tense sentence describing **what** changed and **why** (if non-obvious).
    - Newest version on top; keep all previous entries below.

    **New package (`0.0.1`) — template:**
    ```markdown
    # Changelog

    ## [0.0.1] - 2026-06-29
    ### Added
    - Initial release extracted from `<MODULE_PATH>`.
    - <Brief list of key features / public API surface>.
    ```

    **Update package (bump) — template:**
    ```markdown
    ## [<new_version>] - <YYYY-MM-DD>
    ### Added
    - <new feature or file added>.
    ### Changed
    - <what changed and why>.
    ### Fixed
    - <bug description and fix>.
    ### Removed
    - <what was removed>.
    ```
    Append the new block **above** existing entries (below the `# Changelog` header).

---

## STEP 5 — Verify (in the monorepo)

1. **Manifest lint** — run the monorepo's own gate, the same one CI runs before publishing. It needs `scripts/`'s deps installed:
   ```bash
   # macOS zsh/bash
   ( cd "$repo/scripts" && npm install --silent ) && node "$repo/scripts/validate.mjs"
   ```
   ```powershell
   # Windows PowerShell
   Push-Location (Join-Path $repo 'scripts'); npm install --silent; Pop-Location
   node (Join-Path $repo 'scripts/validate.mjs')
   ```
   If `npm install` is not possible offline, skip this and say so — CI will run it anyway, and a failure there publishes nothing (fix + re-push, no version bump needed).

2. **Dry-run pack** — verify the tarball contents locally. Do **not** run `publish.mjs` from a game repo: it needs R2 credentials that only the maintainer and CI have.
   ```bash
   # macOS zsh/bash
   ( cd "$repo/packages/<scope>.<name>" && npm pack --dry-run )
   ```
   ```powershell
   # Windows PowerShell
   Push-Location (Join-Path $repo 'packages/<scope>.<name>'); npm pack --dry-run; Pop-Location
   ```
   Confirm the `name`, `version`, and that the `Tarball Contents` list includes `.cs`, `.asmdef`, and `.meta` files (plus `package.json`, `README.md`, `CHANGELOG.md`). The **signature** is added by `upm pack` in CI — a local `npm pack` is unsigned, and that is expected.

2. **Static dep check** — every asmdef `references` name is either a package on the Easygoing registry, a known peer lib, or a Unity/registry assembly; `package.json.dependencies` contains only registry-resolvable ids; no business/SDK leak remains; Odin is guarded.

3. **Description quality gate** — open `package.json` and verify `"description"` is a meaningful, complete sentence (not empty, not `"<one-line purpose>"`, not a TODO). If it fails → stop and ask the user before proceeding to STEP 6.

4. **`.meta` integrity** — each `.cs`/`.asmdef` has a sibling `.meta`; new asmdef + new folders have hand-authored metas; copied source kept its original meta. **GUID hex gate (mandatory):**
   ```powershell
   # Windows PowerShell
   Get-ChildItem (Join-Path $repo 'packages/<scope>.<name>') -Recurse -Filter *.meta | ForEach-Object {
     $g = (Select-String '^guid:\s*(\S+)' $_.FullName).Matches.Groups[1].Value
     if ($g -notmatch '^[0-9a-f]{32}$') { Write-Host "BAD GUID  $g  <-  $($_.FullName)" }
   }
   ```
   ```bash
   # macOS zsh/bash
   find "$repo/packages/<scope>.<name>" -type f -name '*.meta' | while IFS= read -r file; do
     g="$(sed -nE 's/^guid:[[:space:]]*([^[:space:]]+).*/\1/p' "$file" | head -n 1)"
     if ! printf '%s' "$g" | grep -Eq '^[0-9a-f]{32}$'; then
       printf 'BAD GUID  %s  <-  %s\n' "$g" "$file"
     fi
   done
   ```
   Any `BAD GUID` line → fix before STEP 6. Zero output = pass.

5. **Meta presence gate** — every `.cs`, `.asmdef`, every subfolder under `Runtime/`/`Editor/`, **and every root-level file** (`package.json`, `README.md`, `CHANGELOG.md`, `LICENSE`/`LICENSE.md`) has a sibling `.meta`. Root metas are **required**, not optional — a registry-installed package is immutable so Unity warns about (and ignores) any file lacking one; see STEP 4.11. Quick check from the package folder:
   ```powershell
   # Windows PowerShell — lists any shipped file missing a sibling .meta
   Get-ChildItem (Join-Path $repo 'packages/<scope>.<name>') -Recurse -File |
     Where-Object { $_.Extension -ne '.meta' -and -not (Test-Path "$($_.FullName).meta") } |
     ForEach-Object { Write-Host "MISSING META  $($_.FullName)" }
   ```
   ```bash
   # macOS zsh/bash
   find "$repo/packages/<scope>.<name>" -type f ! -name '*.meta' | while IFS= read -r f; do
     [ -f "$f.meta" ] || printf 'MISSING META  %s\n' "$f"
   done
   ```
   Any `MISSING META` line → author it before STEP 6. Zero output = pass.

Full compile-verification happens when the package is consumed (Phase 2 / smoke test) — say so; do not claim compile-verified here.

---

## STEP 6 — Commit & push to `main` (monorepo only)

1. Stage package changes:
   - PowerShell: `git -C $repo add -A`
   - zsh/bash: `git -C "$repo" add -A`
2. Commit using the monorepo's **Conventional Commits** style (`feat(...)`, `fix(...)`, `chore(...)` — check `git -C $repo log --oneline -10` and match what you see). Scope it with the package name: `feat(pooling): add com.ezg.pooling v0.0.1` / `fix(pooling): bump com.ezg.pooling to v0.1.3`. End with the `Co-Authored-By` line.
   - PowerShell: `git -C $repo commit -m "feat(<name>): add <id> v<version>"`
   - zsh/bash: `git -C "$repo" commit -m "feat(<name>): add <id> v<version>"`
3. **Push to `main`:**
   ```powershell
   # Windows PowerShell
   git -C $repo pull --rebase $authUrl main
   git -C $repo push $authUrl main
   ```
   ```bash
   # macOS zsh/bash
   git -C "$repo" pull --rebase "$authUrl" main
   git -C "$repo" push "$authUrl" main
   ```
   - Non-fast-forward rejection → pull/rebase again, re-run STEP 5 dry-run, then push. Never `--force`.
   - **Auth/permission failure** → SSH mode: re-invoke `setup-package-push` inline (same as STEP 4.0) to diagnose — key revoked, or account lost Write access. PAT mode: PAT missing/expired — ask the user to refresh `EZG_PACKAGES_PAT`.
4. **No remote at all** → stop after the local commit and give the `git remote add` / `push` commands.

After the push, CI runs automatically (workflow `publish.yml`, trigger `push` to `main` on `packages/**`). It runs `validate.mjs`, then `publish.mjs`, which signs (`upm pack`) and uploads to R2 any version whose tarball is not already there (already-published versions are skipped — so a re-push without a version bump is a no-op), then syncs `unity-template.json`. Watch with `gh run watch -R PackageStore/ezg-packages` if `gh` is available; otherwise tell the user to check the **Actions** tab.

**Never `--force` push. Never rewrite `main` history. Never commit in the game repo.**

---

## STEP 7 — Report

1. **Pushed to `main`:** commit hash (or "committed locally — no remote yet, run: …").
2. **Package:** `<scope>.<name>@<version>` under `packages/<scope>.<name>/`, asmdef `<RuntimeAsmdef>` (+ Editor), files added.
3. **CI is publishing:** verify shortly with `npm view <scope>.<name> version --registry <REGISTRY_URL>`.
4. **Install snippet** once published — the scope in `scopedRegistries` must match the package-id prefix. A project generated from this template already has the `Easygoing` registry in `Packages/manifest.json`, so usually only the `dependencies` line is new:
   ```json
   "scopedRegistries": [{ "name": "Easygoing", "url": "<REGISTRY_URL>", "scopes": ["com.ezg", "com.cysharp", "com.google", "com.coffee"] }],
   "dependencies": { "<scope>.<name>": "<version>" }
   ```
   The package also lands in Feature Hub's **UPM Packages** tab automatically (`publish.mjs` syncs `unity-template.json`) — installing from there is usually easier than hand-editing the manifest.
5. **Peer requirements:** external libs the consuming project must already have (and that are NOT on the Easygoing registry).
6. **Source repo: unchanged.** Then spell out **Phase 2** (separate, do later): after the version is published & smoke-tested, switch the game to consume it — remove `<MODULE_PATH>` from the game repo, add the `<scope>.<name>` dependency to `Packages/manifest.json`, add the assembly to consumer asmdefs, and let Unity recompile. Warn that keeping **both** the in-`Assets/` copy and a registry dependency causes a **duplicate-package conflict**, so Phase 2 removes the source in the same change.

---

## Guardrails

- **One module per run.** No sibling-folder scope creep.
- **Never modify the game repo.** Copy out; do not `git mv`. Phase 2 (the destructive game change) is separate and explicit.
- **the `MONOREPO_PATH` clone is a real working clone** — never blindly `reset --hard` it; if dirty, show the user and ask before discarding.
- **Monorepo: commit + push directly to `main`** (no branch, no PR). Never `--force`-push, never rewrite history; pull/rebase before pushing.
- **Always carry `.meta`** for copied source; hand-author metas for new code/asmdef/folder files **and every root-level file** (`package.json`, `README.md`, `CHANGELOG.md`, `LICENSE`) — a registry-installed package is immutable, so any file without a meta is warned about and ignored by Unity.
- **Never regenerate `.meta` for copied source files.** The `.cs.meta` must be preserved byte-for-byte — GUID intact. Only hand-author metas for files that are **new** to the package (asmdefs, new folders).
- **`package.json dependencies` = registry-resolvable ids only.** Everything else is an asmdef reference + a documented peer requirement.
- **Never add email information** anywhere in the `package.json` file.
- **Don't publish unclean modules** (business/SDK leak) — stop and report.
- **Version is immutable** once published — never reuse a version; always bump.
- **Leave Vietnamese comments + Odin semantics intact** (guard Odin, don't delete).
- **Don't invent licenses or namespaces** — match the repos and the existing packages in the monorepo.
