---
name: package-module
description: Extract one specified module/folder into a clean UPM package and commit + push it directly to the main branch of a monorepo, where the push to main triggers GitHub Actions to publish it to a scoped registry. NON-destructive to the source repo (the module stays in Assets/ and keeps compiling). Used when the user says "đóng module X thành package UPM" / "package this module" / "đẩy module X lên registry" / "extract X to a UPM package". Switching the game to consume the package from the registry is a separate Phase 2 (documented, not done here). To only PLAN without writing to the monorepo, run STEP 0–3.
---

# Package Module — UPM Extraction → Monorepo `main` Agent

Take one **specified module** (a folder under a source path in the source repo), build a **clean, standards-compliant UPM package** from it, and **commit + push it straight to the `main` branch of the monorepo**. The push to `main` triggers the monorepo's GitHub Actions, which packs it (`npm pack`) and publishes the tarball + metadata to the **scoped registry**. **No feature branch, no PR** — pushing to `main` IS the publish trigger.

---

## ⚙️ Configuration

At the start of each run, resolve the following from user input or sensible defaults. Ask only for values that cannot be inferred:

```
MODULE_PATH     = required — full path (or repo-relative path) to the module folder the user provides; SOURCE_ROOT is derived from it
PACKAGE_SCOPE   = user-specified, default com.ezg                 # e.g. com.ezg  or  com.acme
PACKAGE_PREFIX  = same as PACKAGE_SCOPE                           # used to form com.<scope>.<name>
REGISTRY_URL    = user-specified, default https://upm-registry-worker.developer-a1f.workers.dev
MONOREPO_REMOTE = user-specified, default https://github.com/PackageStore/ezg-packages.git
MONOREPO_PATH   = user-specified (or env MONOREPO_PATH), default per OS:
                  Windows: %LOCALAPPDATA%\ezg-packages
                  macOS:   $HOME/Library/Caches/ezg-packages
                  Linux:   ${XDG_CACHE_HOME:-$HOME/.cache}/ezg-packages
                  # always user-writable, never C:\Projects or a system dir; auto-clone if missing
PAT_FILE        = user-specified (or env EZG_PACKAGES_PAT_FILE), default per OS:
                  Windows: %LOCALAPPDATA%\ezg-packages.pat
                  macOS:   $HOME/Library/Application Support/ezg-packages/ezg-packages.pat
                  Linux:   ${XDG_CONFIG_HOME:-$HOME/.config}/ezg-packages/ezg-packages.pat
UNITY_VERSION   = user-specified (explicit value takes priority); default **2022.3** if not specified — do NOT auto-read from ProjectSettings/ProjectVersion.txt unless the user explicitly asks to match the source project's version
```

**`MODULE_PATH` is the only required input** — the user provides a direct path to the module folder (e.g. `Assets/_Project/Features/_Shared/<Module>` or an absolute path). `SOURCE_ROOT` is derived as the parent tree above it. All other values have defaults; ask once to confirm/override if running for the first time on a new project.

### Shell / OS compatibility

This skill supports **Windows PowerShell** and **macOS zsh/bash**. Use the snippet that matches the current machine. Quote every path, especially macOS paths under `Application Support`, and prefer forward slashes in cross-platform documentation. Do not use `%LOCALAPPDATA%` or backslash path examples on macOS.

### GitHub PAT — provided OUT-OF-BAND (never in this file)

This skill file is committed to git, so the PAT must **never** be written here. The token is read at runtime from a source that is NOT tracked by git, in this priority order:

1. **Environment variable `GITHUB_PAT`** (preferred — set it once per machine):
   ```powershell
   # Windows PowerShell
   setx GITHUB_PAT "<your-token>"     # persists for future shells; reopen the terminal afterward
   ```
   ```bash
   # macOS zsh/bash, current shell
   export GITHUB_PAT='<your-token>'
   # To persist, add the export line to ~/.zshrc (or your shell profile) outside this repo.
   ```
2. **A local secret file outside the repo** (fallback): `PAT_FILE` containing only the token on one line.

At STEP 4 the skill resolves the PAT like this (no token ever printed). Use the current shell's snippet:
```powershell
# Windows PowerShell
$repo = if ($env:MONOREPO_PATH) { $env:MONOREPO_PATH } else { Join-Path $env:LOCALAPPDATA 'ezg-packages' }
$patFile = if ($env:EZG_PACKAGES_PAT_FILE) { $env:EZG_PACKAGES_PAT_FILE } else { Join-Path $env:LOCALAPPDATA 'ezg-packages.pat' }
$pat = $env:GITHUB_PAT
if ([string]::IsNullOrWhiteSpace($pat)) {
  if (Test-Path $patFile) { $pat = (Get-Content $patFile -Raw).Trim() }
}
if ([string]::IsNullOrWhiteSpace($pat)) { throw "GITHUB_PAT not set — set the env var or create PAT_FILE outside the repo (see Configuration)." }
```
```bash
# macOS zsh/bash
repo="${MONOREPO_PATH:-$HOME/Library/Caches/ezg-packages}"
pat_file="${EZG_PACKAGES_PAT_FILE:-$HOME/Library/Application Support/ezg-packages/ezg-packages.pat}"
pat="${GITHUB_PAT:-}"
if [ -z "$pat" ] && [ -f "$pat_file" ]; then
  pat="$(tr -d '\r\n' < "$pat_file")"
fi
if [ -z "$pat" ]; then
  echo "GITHUB_PAT not set — set the env var or create PAT_FILE outside the repo (see Configuration)." >&2
  exit 1
fi
```
If neither source yields a token → **stop** at STEP 4 and ask the user to set `GITHUB_PAT`. Required scope: classic PAT with `repo`, or fine-grained PAT with **Contents: read+write** on the monorepo.

**PAT handling rules (security):**
- **Never** put the PAT in this file or any tracked file, never print it, never echo it into logs.
- Inject it via the remote URL form `https://<PAT>@github.com/...` **only on the clone/fetch/push command itself**; immediately reset `origin` to the clean `MONOREPO_REMOTE` so the token isn't persisted in `.git/config`.
- Keep it in `$pat` (a shell variable) for the duration of the run only.

> **This skill does NOT modify the source game repo.** The module stays in `Assets/` and the game keeps compiling. We **copy** the source into the monorepo, never `git mv` it out. **Switching the game to consume the package from the registry (removing the in-`Assets/` copy + adding the manifest dependency) is a separate Phase 2** — see the end of this file. It is intentionally NOT automated here because the registry version does not exist until the push to `main` has triggered CI and it has published.

**One module per run.**

---

## The two repos

| Role | Repo | This skill |
|---|---|---|
| **Source** (game) | current working dir | **read-only** — reads `<SOURCE_ROOT>/<folder>` |
| **Target** (packages) | monorepo (auto-cloned to `MONOREPO_PATH`) | commits + pushes **directly to `main`** adding/updating `packages/<PACKAGE_SCOPE>.<name>/` |

---

## Conventions

| Thing | Rule | Example |
|---|---|---|
| Package id | `<PACKAGE_SCOPE>.<module>` | `com.ezg.stats`, `com.acme.networking` |
| asmdef name | PascalCase, dot-separated, mirrors the id without the scope prefix | `Ezg.Stats`, `Acme.Networking` |
| Editor asmdef | `<asmdef>.Editor`, `includePlatforms: ["Editor"]` | `Ezg.Stats.Editor` |
| `unity` field | Use the `UNITY_VERSION` resolved from config | `"6000.2"` |
| Version | New package → `0.1.0`. Update → bump semver (ask patch/minor/major). Version is **immutable** once published. | — |
| `description` | **Must be a clear, complete sentence** describing what the package does and why a consumer would use it. Generic/placeholder text (`"one-line purpose"`, `"TODO"`, empty string) is **not acceptable** — the skill must stop and ask the user to provide a real description before proceeding. | `"Reusable stats system — tracks, buffs, and queries numeric attributes for characters and items."` |
| author | `{ "name": "EZG Studio" }` (do not include email information) | — |
| **package.json `dependencies`** | **ONLY `<PACKAGE_SCOPE>.*`** ids resolvable via the scoped registry. Every other lib → asmdef `references` by name + a documented **peer requirement** in README. | `"com.ezg.core": "0.1.0"` |
| Odin | `using Sirenix` / Odin attributes wrapped in `#if ODIN_INSPECTOR`. | — |
| Layering | Layer 1 (`Core`) must never depend on a Layer 2 package. Layer 2 ↔ Layer 2 allowed but **acyclic**. | — |

### Layer model (for reference)

Two-tier architecture — relevant for deciding dependency direction:

- **Layer 1 — Core** (`<scope>.core`): universal infrastructure usable by any game. Must NOT contain business logic of any specific game or depend on any Layer 2 package.
- **Layer 2 — Module packages** (`<scope>.<module>`): feature or genre modules. Each module = one package. May depend on Layer 1 and on other Layer 2 packages (acyclic only).

**Business/SDK leak = hard stop** (see DEP-GATE). Examples of leaks that disqualify a module from packaging:
- Hardcoded game-specific CSV key constants (e.g. `ItemMerge`, `CookingRecipes`)
- Hardcoded `Assets/` paths for CSV/Resources (e.g. `Assets/_Project/Features/<Feature>/CsvConfig/`)
- Direct references to game-specific singletons (`DataManager`, `PlayerDataManager`, `GameEnums.Features`)
- Third-party SDK types without an asmdef boundary (Supabase, Google.Play.AssetDelivery compiled directly into Core)

If a module has these leaks, stop and report — do not package unless the user explicitly accepts known debt (document it in README).

**Known Odin pattern:** guard all `using Sirenix.*` and Odin attributes with `#if ODIN_INSPECTOR … #endif` so the package compiles in projects without Odin. Leave the semantic behavior intact, just guard the attribute syntax.

---

## Pipeline

```
[0] IDENTIFY  → source folder + package id + asmdef name + version (new vs update)
[1] AUDIT     → classify deps (scoped registry vs external peer libs) + leaks + editor split
[2] DEP-GATE  → every <scope>.* dep must already be published (or pushed in this same run); record external peer libs; block on business/SDK leak
[3] PLAN      → show package contents + deps + version; warn that push to main = immediate publish; get explicit confirmation
[4] BUILD     → on monorepo main (pull first): create packages/<scope>.<name>/ (copy source + .meta, scaffold package.json/asmdef/README/CHANGELOG, wrap Odin, author metas for new files)
[5] VERIFY    → run scripts/publish.mjs --dry-run in the monorepo (packs cleanly, keys/integrity OK) + static dep check
[6] PUSH      → commit on main + push origin main → CI publishes. No remote → stop at local commit + give push commands.
[7] REPORT    → pushed commit, what CI is publishing, registry install snippet, + the separate Phase 2 (switch game to consumer — NOT done now)
```

STEP 0–3 are **non-destructive** (nothing written anywhere). STEP 4 onward writes to the **monorepo only**, after explicit confirmation. If the user wants a plan only, stop after STEP 3.

---

## STEP 0 — Identify the target

1. Resolve the source folder from the `MODULE_PATH` the user provided — use it directly. `SOURCE_ROOT` is the parent tree (e.g. if `MODULE_PATH` is `Assets/_Project/Features/_Shared/<Module>`, then `SOURCE_ROOT` is `Assets/_Project/`). If the path is ambiguous or the folder doesn't exist, ask once to clarify.
2. Derive **package id** (`<PACKAGE_SCOPE>.<slug>`) + **asmdef name** (`<Pascal>.<Name>`); confirm with the user if derived rather than explicitly stated.
3. **New vs update:** check `<MONOREPO_PATH>/packages/<scope>.<name>/package.json` (the cache clone). If the cache clone doesn't exist yet, fall back to querying the registry `<REGISTRY_URL>/<scope>.<name>`.
   - Missing → **new package**, version `0.1.0`.
   - Exists → **update**; read its current version and ask the bump level (patch / minor / major; default patch). The new version must be greater (registry versions are immutable).
4. Detect whether the source folder **already has an asmdef** (Glob `*.asmdef`):
   - **No asmdef** — it currently auto-compiles into its parent assembly. It becomes its own assembly in the package.
   - **Has an asmdef** — reuse its name or normalize to `<Pascal>.<Name>`.
5. If the target is Layer-1 `Core` itself → warn (Core is large and last in the rollout) and confirm before continuing.

State the resolved `{source folder, package id, asmdef name, new|update + version, asmdef status}`.

---

## STEP 1 — Audit dependencies & leaks

Use **codegraph first**; grep only for `using` directives, string literals, define symbols.

1. **Outgoing deps** — `codegraph_callees` / `codegraph_impact` on the folder's public types + `Grep` for `using ` across the folder. Classify each:

   | Bucket | Goes where |
   |---|---|
   | Unity / BCL (`UnityEngine`, `System.*`) | nothing to declare (engine auto); `System.*` asmdef ref only if used |
   | Another **`<scope>.*`** package | asmdef `references` **+** `package.json` dependency (registry-resolvable). Must already be published → DEP-GATE. |
   | **External peer lib** not on the registry (e.g. DOTween, Odin, UniTask, Newtonsoft, TMP, Cinemachine, Firebase — and any other third-party libs the host project uses; extend this list when on-boarding a new project) | asmdef `references` by name **+ documented as a peer requirement** in README. **Do NOT** put in `package.json` dependencies. |
   | Precompiled DLL (Firebase-style) | `overrideReferences: true` + `precompiledReferences: [...]` in asmdef; DLLs themselves are a peer requirement. |

2. **Leak scan** (grep inside the folder):
   - Odin: `using Sirenix` / `[TabGroup]`/`[ShowIf]`/`[Button]`/`[Title]` → wrap in `#if ODIN_INSPECTOR` (STEP 4).
   - **Business / game-specific leak:** hardcoded game-specific CSV paths, CSV key constants, game-specific singletons (global manager classes, feature enums, or data-access facades that are unique to the host project and not part of the module's own API). A reusable package **must not** carry these → DEP-GATE hard stop.
   - Editor-only code (`#if UNITY_EDITOR`, `using UnityEditor`, an `Editor/` subfolder) → goes into the package `Editor/` assembly.
   - `Resources/`, scenes, `.asmref` inside the folder → flag (load-path semantics change when published).

3. **Incoming consumers** — `codegraph_callers` to list who in the game uses this module. Record them: they are the Phase-2 work list.

Produce a compact audit (kept in reasoning): outgoing deps by bucket, peer libs, scoped deps, Odin files, editor files, business leaks, DLL refs, incoming consumers.

---

## STEP 2 — Dependency gate

- **`<scope>.*` dependencies must already be published** to the registry (check by querying `<REGISTRY_URL>/<scope>.<dep>` returns metadata, or that it exists in `<MONOREPO_PATH>/packages/`), **or** be pushed earlier in this same run. If a needed `<scope>.*` dep is unpublished → report which one to package first, then **stop**.
- **External peer libs** (DOTween/Odin/etc.) are **not** a blocker — they are documented as peer requirements.
- **Business / SDK leak** → hard stop. Continue only if the user explicitly accepts shipping with leaks (discouraged; record as known debt in README) — better: narrow scope or file a cleanup task.

---

## STEP 3 — Plan & confirm

Present a **package summary card** and wait for explicit confirmation before doing anything destructive.

### New package — summary card

```
Package        : <scope>.<name>@<version>
Display name   : <displayName>
Description    : <full description — must be a real sentence, not a placeholder>
Source folder  : <MODULE_PATH>
Asmdef         : <Pascal>.<Name>  [+ <Pascal>.<Name>.Editor]  (if editor code)
Unity minimum  : <UNITY_VERSION>  ⚠️ UPM hides this package in projects below this version
Registry       : <REGISTRY_URL>
Monorepo target: packages/<scope>.<name>/  →  main

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
Registry currently : <REGISTRY_URL>/<scope>.<name>  (latest = <old>)
```

After showing the card, **review the `Description` field**:
- If the description is missing, empty, generic (`"one-line purpose"`, `"TODO"`, `"..."`) or doesn't clearly explain what the package does → ask the user to provide a proper description **before** confirming.
- A good description is 1–2 sentences that answer: *"What does this package do, and why would a consumer install it?"*

Then ask once:

> **"Publish `<scope>.<name>@<version>` to monorepo `main`? Pushing is immediate and the version is immutable. (yes / plan-only / adjust)"**

Proceed only on explicit **yes**. On **adjust** — update the relevant fields and re-show the card. On **plan-only** — stop here.

---

## STEP 4 — Build the package on monorepo `main`

All writes happen in the cache clone under `MONOREPO_PATH`, directly on the **`main`** branch (no feature branch).

0. **Resolve paths + PAT** (no token printed):
   ```powershell
   # Windows PowerShell
   $repo = if ($env:MONOREPO_PATH) { $env:MONOREPO_PATH } else { Join-Path $env:LOCALAPPDATA 'ezg-packages' }
   $remote = '<MONOREPO_REMOTE>'
   $patFile = if ($env:EZG_PACKAGES_PAT_FILE) { $env:EZG_PACKAGES_PAT_FILE } else { Join-Path $env:LOCALAPPDATA 'ezg-packages.pat' }
   $pat = $env:GITHUB_PAT
   if ([string]::IsNullOrWhiteSpace($pat)) {
     if (Test-Path $patFile) { $pat = (Get-Content $patFile -Raw).Trim() }
   }
   if ([string]::IsNullOrWhiteSpace($pat)) { throw "GITHUB_PAT not set — set the env var or create PAT_FILE outside the repo (see Configuration)." }
   $authUrl = "https://$pat@github.com/<org>/<repo>.git"   # derived from MONOREPO_REMOTE
   ```
   ```bash
   # macOS zsh/bash
   repo="${MONOREPO_PATH:-$HOME/Library/Caches/ezg-packages}"
   remote='<MONOREPO_REMOTE>'
   pat_file="${EZG_PACKAGES_PAT_FILE:-$HOME/Library/Application Support/ezg-packages/ezg-packages.pat}"
   pat="${GITHUB_PAT:-}"
   if [ -z "$pat" ] && [ -f "$pat_file" ]; then
     pat="$(tr -d '\r\n' < "$pat_file")"
   fi
   if [ -z "$pat" ]; then
     echo "GITHUB_PAT not set — set the env var or create PAT_FILE outside the repo (see Configuration)." >&2
     exit 1
   fi
   authUrl="https://${pat}@github.com/<org>/<repo>.git"   # derived from MONOREPO_REMOTE
   ```
   If token resolution fails → **stop** and ask the user to set the PAT. All later steps use `$repo` (PowerShell) or `$repo` (zsh/bash).

1. **Ensure the cache clone exists & is fresh (auto-clone if missing):**
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
   - **Working tree must be clean** before building: `git -C <repo> status --porcelain` empty. If dirty from a prior failed run, reset to `origin/main` — only safe because this skill never leaves intentional uncommitted work here:
     - PowerShell: `if (git -C $repo status --porcelain) { git -C $repo reset --hard origin/main }`
     - zsh/bash: `if [ -n "$(git -C "$repo" status --porcelain)" ]; then git -C "$repo" reset --hard origin/main; fi`
   - Fast-forward:
     - PowerShell: `git -C $repo pull --ff-only $authUrl main`
     - zsh/bash: `git -C "$repo" pull --ff-only "$authUrl" main`
   - **Never** leave the token in `origin`'s URL.

2. **Create** `packages/<scope>.<name>/` with `Runtime/` (+ `Editor/` if editor code).

3. **`package.json`** (`unity` from `UNITY_VERSION`, deps = `<scope>.*` only):
   ```json
   {
     "name": "<scope>.<name>",
     "version": "<x.y.z>",
     "displayName": "EZG <DisplayName>",
     "description": "<full description confirmed in STEP 3>",
     "unity": "<UNITY_VERSION>",
     "author": { "name": "EZG Studio" },
     "keywords": ["<2-4 keywords>"],
     "dependencies": { "<scope>.<dep>": "<version>" }
   }
   ```
   - **`description` must be the real, user-confirmed sentence from the summary card** — not a placeholder. This text appears in the Unity Package Manager UI, the scoped registry index, and consumer documentation. If STEP 3 was skipped or the field is still a placeholder, **stop and ask** before writing.
   - **Do NOT add any email information** (such as an `"email"` field inside `"author"`, or any email addresses anywhere in the file) to `package.json`.
   - Omit `dependencies` if there are no `<scope>.*` deps.

4. **Runtime asmdef** `Runtime/<Pascal>.<Name>.asmdef`:
   ```json
   {
     "name": "<Pascal>.<Name>",
     "rootNamespace": "",
     "references": [ "<assembly names: scoped deps + peer libs>" ],
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

5. **Editor asmdef** `Editor/<Pascal>.<Name>.Editor.asmdef` (only if editor code): `includePlatforms: ["Editor"]`, references `<Pascal>.<Name>` + editor refs.

6. **Copy source** from `<SOURCE_ROOT>/<folder>` into `Runtime/` (editor files into `Editor/`), **carrying every `.meta`**. Use file copies (cross-repo — NOT `git mv`). A `.cs` without its `.meta` loses its GUID → copy both. Preserve subfolder structure + folder `.meta`. On macOS, `rsync -a "$src/" "$dst/"` is preferred for preserving the tree; on Windows PowerShell, use `Copy-Item -Recurse -Force`.

7. **Wrap Odin** in copied files: guard `using Sirenix...` and Odin attributes with `#if ODIN_INSPECTOR ... #endif`. Leave Vietnamese comments intact.

8. **Namespaces:** keep as-is; renaming ripples into consumers. Only fix if it breaks compilation.

9. **`.meta` for NEW files** the skill creates (new asmdef, new folders, package.json):
   - Hand-author asmdef + folder metas with deterministic GUIDs.
   - ⚠️ **A Unity GUID MUST be exactly 32 lowercase HEX chars (`0-9a-f`).** Do **NOT** build the GUID by slicing letters out of the folder/asmdef name — names may contain non-hex letters (`g h i j k…`) and Unity silently **rejects** any `.meta` with a non-hex GUID, making the installed package appear **EMPTY**.
   - **Generate the GUID by hashing the name** so it's deterministic AND guaranteed hex:
     ```powershell
     # Windows PowerShell
     $md5 = [System.Security.Cryptography.MD5]::Create()
     function New-Guid32([string]$seed){ -join ([System.BitConverter]::ToString($md5.ComputeHash([Text.Encoding]::UTF8.GetBytes($seed))) -replace '-').ToLower()[0..31] }
     New-Guid32 "<scope>.<name>/Runtime"
     New-Guid32 "<scope>.<name>/Runtime/<Pascal>.<Name>.asmdef"
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
     new_guid32 "<scope>.<name>/Runtime/<Pascal>.<Name>.asmdef"
     ```
   - **After writing every new meta, VALIDATE** each `guid:` line matches `^[0-9a-f]{32}$`. If any fails → stop and regenerate; never push a non-hex GUID.
   - Copied source keeps its **original** `.meta` — never regenerate those.

10. **README.md** — purpose, "package ↔ source folder" mapping, `<scope>.*` dependencies, **peer requirements**, any known debt.

11. **CHANGELOG.md** — follows [Keep a Changelog](https://keepachangelog.com) format. Place at package root (`packages/<scope>.<name>/CHANGELOG.md`). The sibling `.meta` is required (covered by STEP 5 root-level asset meta gate — use `New-Guid32`/`new_guid32` with seed `<scope>.<name>/CHANGELOG.md`).

    **Format rules:**
    - Header line: `# Changelog`
    - Each version block: `## [<version>] - <YYYY-MM-DD>` (use the **session date**, ISO 8601).
    - Group changes under one or more of these **categories** (omit empty ones):
      - `### Added` — new features, new files, new public API.
      - `### Changed` — changes to existing functionality (breaking or non-breaking).
      - `### Fixed` — bug fixes.
      - `### Removed` — removed features, deleted files, deprecated API removals.
    - Each bullet: concise, past-tense sentence describing **what** changed and **why** (if non-obvious).
    - Newest version on top; keep all previous entries below.

    **New package (`0.1.0`) — template:**
    ```markdown
    # Changelog

    ## [0.1.0] - 2026-06-16
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

1. **Dry-run pack:**
   ```powershell
   # Windows PowerShell
   $scriptsDir = Join-Path $repo 'scripts'
   if (-not (Test-Path (Join-Path $scriptsDir 'node_modules'))) { npm install --prefix $scriptsDir }
   node (Join-Path $scriptsDir 'publish.mjs') --dry-run
   ```
   ```bash
   # macOS zsh/bash
   if [ ! -d "$repo/scripts/node_modules" ]; then npm install --prefix "$repo/scripts"; fi
   node "$repo/scripts/publish.mjs" --dry-run
   ```
   Confirm the new package appears with a correct tarball key `<scope>.<name>/-/<scope>.<name>-<version>.tgz`, an `integrity`/`shasum`, and `latest=<version>`.

2. **Static dep check** — every asmdef `references` name is either a `<scope>.*` package, a known peer lib, or a Unity/registry assembly; `package.json.dependencies` contains only `<scope>.*`; no business/SDK leak remains; Odin is guarded.

3. **Description quality gate** — open `package.json` and verify `"description"` is a meaningful, complete sentence (not empty, not `"<one-line purpose>"`, not a TODO). If it fails → stop and ask the user before proceeding to STEP 6.

4. **`.meta` integrity** — each `.cs`/`.asmdef` has a sibling `.meta`; new asmdef has a hand-authored meta; copied source kept its original meta. **GUID hex gate (mandatory):**
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

5. **Root-level asset `.meta` gate (mandatory)** — every non-folder file at the package root (`README.md`, `CHANGELOG.md`, `package.json`, `LICENSE`, etc.) **must** have a sibling `.meta`. Unity treats installed packages as immutable and cannot auto-generate these; a missing meta causes a `"has no meta file, but it's in an immutable folder"` console error in every project that consumes the package.
   ```bash
   # macOS zsh/bash — list root-level files that have no sibling .meta
   pkg_dir="$repo/packages/<scope>.<name>"
   find "$pkg_dir" -maxdepth 1 -type f ! -name '*.meta' | while IFS= read -r f; do
     [ -f "${f}.meta" ] || printf 'MISSING META  %s\n' "$f"
   done
   ```
   ```powershell
   # Windows PowerShell
   $pkgDir = Join-Path $repo 'packages/<scope>.<name>'
   Get-ChildItem $pkgDir -File | Where-Object { $_.Extension -ne '.meta' } | ForEach-Object {
     if (-not (Test-Path "$($_.FullName).meta")) { Write-Host "MISSING META  $($_.FullName)" }
   }
   ```
   Any `MISSING META` line → hand-author the `.meta` (use `new_guid32` / `New-Guid32` from STEP 4 with seed `<scope>.<name>/<filename>`) before STEP 6. Zero output = pass.

6. **Tarball contents** (optional) — `npm pack --dry-run --json` in the package dir and confirm the `files` list includes `.cs`, `.asmdef`, and `.meta` files.

Full compile-verification happens when the package is consumed (Phase 2 / smoke test) — say so; do not claim compile-verified here.

---

## STEP 6 — Commit & push to `main` (monorepo only)

1. Stage package changes:
   - PowerShell: `git -C $repo add -A`
   - zsh/bash: `git -C "$repo" add -A`
2. Commit with `feat(<scope>.<name>): publish v<version>`. End with `Co-Authored-By` line.
   - PowerShell: `git -C $repo commit -m "feat(<scope>.<name>): publish v<version>"`
   - zsh/bash: `git -C "$repo" commit -m "feat(<scope>.<name>): publish v<version>"`
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
   - **Auth failure (401/403)** → PAT missing/expired. Stop and ask the user to refresh `GITHUB_PAT`.
4. **No remote at all** → stop after the local commit and give the `git remote add` / `push` commands.

After the push, CI runs automatically (workflow `publish.yml`, trigger `push` to `main` on `packages/**`). Watch with `gh run watch -R <org>/<monorepo>` if `gh` is available; otherwise tell the user to check the **Actions** tab.

**Never `--force` push. Never rewrite `main` history. Never commit in the source repo.**

---

## STEP 7 — Report

1. **Pushed to `main`:** commit hash (or "committed locally — no remote yet, run: …").
2. **Package:** `<scope>.<name>@<version>`, asmdef `<Pascal>.<Name>` (+ Editor), files added.
3. **CI is publishing:** verify shortly at `<REGISTRY_URL>/<scope>.<name>`.
4. **Install snippet** once published:
   ```json
   "scopedRegistries": [{ "name": "Easygoing code base", "url": "<REGISTRY_URL>", "scopes": ["<PACKAGE_SCOPE>"] }],
   "dependencies": { "<scope>.<name>": "<version>" }
   ```
5. **Peer requirements:** external libs the consuming project must already have.
6. **Source repo: unchanged.** Then spell out **Phase 2** (separate, do later): after the version is published & smoke-tested, switch the game to consume it — remove `<SOURCE_ROOT>/<folder>`, add the `<scope>.<name>` dependency to `Packages/manifest.json`, add the assembly to consumer asmdefs, and let Unity recompile. Warn that keeping **both** the in-`Assets/` copy and a registry dependency causes a **duplicate-package conflict**, so Phase 2 removes the source in the same change.

---

## Guardrails

- **One module per run.** No sibling-folder scope creep.
- **Never modify the source game repo.** Copy out; do not `git mv`. Phase 2 (the destructive game change) is separate and explicit.
- **Monorepo: commit + push directly to `main`** (no branch, no PR). Never `--force`-push, never rewrite history; pull/rebase before pushing.
- **Always carry `.meta`** for copied source; hand-author metas for new files.
- **Never regenerate `.meta` for copied source files.** The `.cs.meta` must be preserved byte-for-byte — GUID intact. Only hand-author metas for files that are **new** to the package (asmdefs, new folders).
- **`package.json dependencies` = `<scope>.*` only.** Everything else is an asmdef reference + a documented peer requirement.
- **Never add email information** anywhere in the `package.json` file.
- **Don't publish unclean modules** (business/SDK leak) — stop and report.
- **Version is immutable** once published — never reuse a version; always bump.
- **Leave Vietnamese comments + Odin semantics intact** (guard Odin, don't delete).
- **Don't invent licenses or namespaces** — match the repos.
