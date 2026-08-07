---
name: package-module
description: Extract one specified module/folder from BlazeSurvivor into a clean UPM package and commit + push it directly to the main branch of the Packages monorepo (MONOREPO_PATH — D:\Packages on Windows, $HOME/Packages on macOS/Linux), where the push to main triggers GitHub Actions (publish-packages.yml) to publish it to the bf-packages scoped registry. NON-destructive to the source repo (the module stays in Assets/ and keeps compiling). Used when the user says "đóng module X thành package UPM" / "package this module" / "đẩy module X lên registry" / "extract X to a UPM package". Switching the game to consume the package from the registry is a separate Phase 2 (documented, not done here). To only PLAN without writing to the monorepo, run STEP 0–3.
---

# Package Module — UPM Extraction → `Packages` monorepo `main` Agent

Take one **specified module** (a folder under `Assets/` in the BlazeSurvivor repo), build a **clean, standards-compliant UPM package** from it, and **commit + push it straight to the `main` branch of the `Packages` monorepo** (`MONOREPO_PATH`). The push to `main` triggers the monorepo's GitHub Actions workflow `publish-packages.yml`, which scans every `package.json` under `Frameworks/`, `Modules/`, `Packages/` and runs `npm publish` for any whose version is not yet on the **bf-packages scoped registry**. **No feature branch, no PR** — pushing to `main` IS the publish trigger.

> **Difference vs a flat `packages/<scope>.<name>/` monorepo:** the `Packages` monorepo groups packages into **three top-level categories** — `Frameworks/`, `Modules/`, `Packages/` — and each package folder is a **human-readable PascalCase name** (e.g. `B-PoolingManager`), NOT a dotted id. The dotted id lives only in `package.json.name`. Pick the category in STEP 0.

---

## ⚙️ Configuration

At the start of each run, resolve the following from user input or sensible defaults. Ask only for values that cannot be inferred:

```
MODULE_PATH      = required — full path (or repo-relative path) to the module folder in BlazeSurvivor (e.g. Assets/_Game/2.BUS/... or an absolute path); SOURCE_ROOT is derived from it
PACKAGE_CATEGORY = Frameworks | Modules | Packages — default Modules.
                   Frameworks = universal infra with no game logic (Singleton-style).
                   Modules    = functional gameplay/system module extracted from the game (DEFAULT for "đóng module X").
                   Packages   = repacked third-party / general libs (rarely the target when extracting your own code).
PACKAGE_SCOPE    = user-specified, default com.blackface              # forms com.blackface.<name>
REGISTRY_URL     = user-specified, default https://npm-registry.blackface.workers.dev/
REGISTRY_NAME    = scoped-registry display name, default bf-packages  # used in the install snippet
MONOREPO_REMOTE  = user-specified, default https://github.com/PackageStore/Packages.git
MONOREPO_PATH    = user-specified (or env MONOREPO_PATH), default per OS:
                   Windows:      D:\Packages
                   macOS/Linux:  $HOME/Packages
                   # If a clone already exists at this path, use it in place; only auto-clone if it is missing
PAT_FILE         = user-specified (or env BF_PACKAGES_PAT_FILE), default per OS:
                   Windows: %LOCALAPPDATA%\bf-packages.pat
                   macOS:   $HOME/Library/Application Support/bf-packages/bf-packages.pat
                   Linux:   ${XDG_CONFIG_HOME:-$HOME/.config}/bf-packages/bf-packages.pat
UNITY_VERSION    = user-specified (explicit value takes priority); default **2021.3** if not specified
                   # existing repacks use 2019.4–2021.3; pick the LOWEST version the module actually needs so more projects can consume it.
                   # do NOT auto-read from ProjectSettings/ProjectVersion.txt unless the user explicitly asks to match BlazeSurvivor's version
```

**`MODULE_PATH` is the only required input** — the user provides a direct path to the module folder. `SOURCE_ROOT` is derived as the parent tree above it. All other values have defaults; ask once to confirm/override if running for the first time on a new module.

### Shell / OS compatibility

Snippets are provided for both **macOS/Linux (zsh/bash)** and **Windows (PowerShell)** — use the one matching the current machine. Default locations are OS-specific: on **Windows** the game repo is at `D:\GameDevelop\BlazeSurvivor` and the monorepo at `D:\Packages`; on **macOS/Linux** the monorepo defaults to `$HOME/Packages` (the game repo is the current working dir). Quote every path. Do not use `%LOCALAPPDATA%` or backslash path examples on macOS.

### GitHub PAT — provided OUT-OF-BAND (never in this file)

This skill file is committed to git, so the PAT must **never** be written here. The token is read at runtime from a source that is NOT tracked by git, in this priority order:

1. **Environment variable `BF_PACKAGES_PAT`** (preferred — set it once per machine):
   ```powershell
   # Windows PowerShell
   setx BF_PACKAGES_PAT "<your-token>"     # persists for future shells; reopen the terminal afterward
   ```
   ```bash
   # macOS zsh/bash, current shell
   export BF_PACKAGES_PAT='<your-token>'
   # To persist, add the export line to ~/.zshrc (or your shell profile) outside this repo.
   ```
2. **A local secret file outside the repo** (fallback): `PAT_FILE` containing only the token on one line.

At STEP 4 the skill resolves the PAT like this (no token ever printed). Use the current shell's snippet:
```powershell
# Windows PowerShell
$repo = if ($env:MONOREPO_PATH) { $env:MONOREPO_PATH } else { 'D:\Packages' }
$patFile = if ($env:BF_PACKAGES_PAT_FILE) { $env:BF_PACKAGES_PAT_FILE } else { Join-Path $env:LOCALAPPDATA 'bf-packages.pat' }
$pat = $env:BF_PACKAGES_PAT
if ([string]::IsNullOrWhiteSpace($pat)) {
  if (Test-Path $patFile) { $pat = (Get-Content $patFile -Raw).Trim() }
}
if ([string]::IsNullOrWhiteSpace($pat)) { throw "BF_PACKAGES_PAT not set — set the env var or create PAT_FILE outside the repo (see Configuration)." }
```
```bash
# macOS zsh/bash
repo="${MONOREPO_PATH:-$HOME/Packages}"
pat_file="${BF_PACKAGES_PAT_FILE:-$HOME/Library/Application Support/bf-packages/bf-packages.pat}"
pat="${BF_PACKAGES_PAT:-}"
if [ -z "$pat" ] && [ -f "$pat_file" ]; then
  pat="$(tr -d '\r\n' < "$pat_file")"
fi
if [ -z "$pat" ]; then
  echo "BF_PACKAGES_PAT not set — set the env var or create PAT_FILE outside the repo (see Configuration)." >&2
  exit 1
fi
```
If neither source yields a token → **stop** at STEP 4 and ask the user to set `BF_PACKAGES_PAT`. Required scope: classic PAT with `repo`, or fine-grained PAT with **Contents: read+write** on `PackageStore/Packages`.

**PAT handling rules (security):**
- **Never** put the PAT in this file or any tracked file, never print it, never echo it into logs.
- Inject it via the remote URL form `https://<PAT>@github.com/...` **only on the clone/fetch/push command itself**; immediately reset `origin` to the clean `MONOREPO_REMOTE` so the token isn't persisted in `.git/config`.
- Keep it in `$pat` (a shell variable) for the duration of the run only.

> **This skill does NOT modify the BlazeSurvivor repo.** The module stays in `Assets/` and the game keeps compiling. We **copy** the source into the monorepo, never `git mv` it out. **Switching the game to consume the package from the registry (removing the in-`Assets/` copy + adding the manifest dependency) is a separate Phase 2** — see the end of this file. It is intentionally NOT automated here because the registry version does not exist until the push to `main` has triggered CI and it has published.

**One module per run.**

---

## The two repos

| Role | Repo | This skill |
|---|---|---|
| **Source** (game) | BlazeSurvivor repo — current working dir (`D:\GameDevelop\BlazeSurvivor` on Windows; `$HOME/Projects/BlazeSurvivor` on macOS) | **read-only** — reads `<MODULE_PATH>` |
| **Target** (packages) | `Packages` monorepo at `MONOREPO_PATH` (existing clone of `PackageStore/Packages`) | commits + pushes **directly to `main`** adding/updating `<CATEGORY>/<FolderName>/` |

---

## Relationship to the monorepo's existing workflows

`PackageStore/Packages` already ships its own publishing toolchain under `.claude/commands/`. This skill **complements** it — it does NOT replace or fight it. Know the boundaries so you don't run two tools for the same job:

| Existing in the monorepo | What it does | How this skill relates |
|---|---|---|
| `publish-packages.yml` (GitHub Actions) | On push to `main`, scans `Frameworks/`, `Modules/`, `Packages/` and `npm publish`es any version not yet on the registry (no `--force`). | **This skill relies on it** — the skill only pushes to `main`; CI does the actual publish. Same path, no double-publish. |
| `/create-package` | Scaffolds a UPM package from a folder **in place inside the monorepo**. | **Overlaps** with this skill's scaffolding. Use ONE per module: `/create-package` when you start from a folder already sitting in the monorepo; **this skill** when you EXTRACT a module from the BlazeSurvivor repo (cross-repo copy + audit + push). Never run both on the same module. |
| `/publish` | Manual local `npm publish --force` reading `AUTH_TOKEN` from `npm-registry/.env`. | Different token + can overwrite versions. This skill never calls it; it lets CI publish. If someone uses `/publish --force`, the "immutable version" assumption no longer holds. |
| `/push`, `/deploy`, `/remove-version` | git push (no `feat:` prefix), deploy the Worker, delete a version from R2. | Unrelated to extraction. This skill follows the `/push` no-prefix commit rule (STEP 6). |

> **Placement is load-bearing:** CI only scans `Frameworks/**`, `Modules/**`, `Packages/**` at depth 2. A package placed at the workspace **root** (e.g. `<MONOREPO_PATH>/Foo/`) will **NOT** be auto-published. Always create the package inside one of the three category folders — never at the repo root.

---

## Conventions

| Thing | Rule | Example |
|---|---|---|
| Target folder | `<CATEGORY>/<FolderName>/` where `<FolderName>` is human-readable PascalCase/kebab — **not** a dotted id | `Modules/B-PoolingManager/`, `Frameworks/Singleton/` |
| Package id (`name`) | `<PACKAGE_SCOPE>.<foldername-lowercased>` | `com.blackface.b-poolingmanager`, `com.blackface.singleton` |
| `displayName` | human-readable title | `"B-Pooling Manager"`, `"Serializable Dictionary"` |
| Runtime asmdef name | **Modules** → `Modules.<FolderName>`; **Frameworks/Packages** → `<FolderName>`. If the source folder already has an asmdef, reuse its name. | `Modules.B-PoolingManager`, `Singleton`, `SuperScrollView` |
| Editor asmdef | `<RuntimeAsmdef>.Editor`, `includePlatforms: ["Editor"]` | `SerializableDictionary.Editor` |
| `unity` field | Use the `UNITY_VERSION` resolved from config | `"2021.3"` |
| Version | New package → `0.0.1`. Update → bump semver (ask patch/minor/major). Version is **immutable on the CI path** (push→`publish-packages.yml` skips an already-published version) — but the monorepo's manual `/publish` workflow uses `npm publish --force` and **can overwrite** a version. Treat versions as immutable anyway: always bump, never reuse. | — |
| `description` | **Must be a clear, complete sentence** describing what the package does. Generic/placeholder text (`"one-line purpose"`, `"TODO"`, empty) is **not acceptable** — stop and ask for a real description. | `"Runtime object pooling manager for Unity GameObjects and typed controllers."` |
| `author` | Default `author.name` to the **category label**, matching the existing packages in the monorepo: `Modules` → `"Modules"`, `Frameworks` → `"Frameworks"`, `Packages` → `"Extensions"`. The user may override; **never include an email**. | `Modules/B-PoolingManager` → `"Modules"`; `Packages/SerializableDictionary` → `"Extensions"` |
| Optional fields | `keywords` (2–4), `license` (`"MIT"`), `category`, `author.url` — include when known; the minimal published packages omit them. | — |
| **package.json `dependencies`** | **ONLY registry-resolvable ids** already on bf-packages (typically `com.blackface.*`). Every other lib → asmdef `references` by name + a documented **peer requirement** in README. | `"com.blackface.singleton": "0.0.3"` |
| Odin | `using Sirenix` / Odin attributes wrapped in `#if ODIN_INSPECTOR`. | — |
| Layering | `Frameworks` (Layer 1) must never depend on a `Modules` (Layer 2) package. Layer 2 ↔ Layer 2 allowed but **acyclic**. | — |

### Layer model (for reference)

Two-tier architecture — relevant for deciding dependency direction and which category folder to use:

- **Layer 1 — Frameworks** (`Frameworks/`): universal infrastructure usable by any game (Singleton, etc.). Must NOT contain business logic of any specific game or depend on any Layer 2 module.
- **Layer 2 — Modules** (`Modules/`): feature or system modules extracted from a game. Each module = one package. May depend on Layer 1 frameworks and on other Layer 2 modules (acyclic only).
- **Packages/** is for repacked third-party libs (DOTween, UniTask, Odin…) — not normally produced by extracting your own code.

**Business/SDK leak = hard stop** (see DEP-GATE). Examples of leaks that disqualify a module from packaging:
- Hardcoded game-specific CSV key constants (e.g. `ItemMerge`, `CookingRecipes`)
- Hardcoded `Assets/` paths for CSV/Resources (e.g. `Assets/_Game/.../CsvConfig/`)
- Direct references to BlazeSurvivor-specific singletons (`DataManager`, `DataPlayer`, `GameEnums.Features`, `UIManager` if game-specific)
- Third-party SDK types without an asmdef boundary (Supabase, Cloudflare client, Google.Play.AssetDelivery compiled directly into a Framework)

If a module has these leaks, stop and report — do not package unless the user explicitly accepts known debt (document it in README).

**Known Odin pattern:** guard all `using Sirenix.*` and Odin attributes with `#if ODIN_INSPECTOR … #endif` so the package compiles in projects without Odin. Leave the semantic behavior intact, just guard the attribute syntax. (Odin itself is published as `com.sirenix.odininspector` on this registry, so a hard dependency is possible — but a guarded soft dependency is preferred for reusability.)

---

## Pipeline

```
[0] IDENTIFY  → category + source folder + package id + folder name + asmdef name + version (new vs update)
[1] AUDIT     → classify deps (registry-resolvable vs external peer libs) + leaks + editor split
[2] DEP-GATE  → every registry dep must already be published (or pushed in this same run); record external peer libs; block on business/SDK leak
[3] PLAN      → show package contents + deps + version; warn that push to main = immediate publish; get explicit confirmation
[4] BUILD     → on monorepo main (pull first): create <CATEGORY>/<FolderName>/ (copy source + .meta, scaffold package.json/asmdef/README/CHANGELOG, wrap Odin, author metas for new files)
[5] VERIFY    → npm publish --dry-run in the package folder (packs cleanly) + static dep check + .meta/GUID gates
[6] PUSH      → commit on main + push origin main → CI (publish-packages.yml) publishes. No remote → stop at local commit + give push commands.
[7] REPORT    → pushed commit, what CI is publishing, registry install snippet, + the separate Phase 2 (switch game to consumer — NOT done now)
```

STEP 0–3 are **non-destructive** (nothing written anywhere). STEP 4 onward writes to the **monorepo only**, after explicit confirmation. If the user wants a plan only, stop after STEP 3.

---

## STEP 0 — Identify the target

1. Resolve the **source folder** from the `MODULE_PATH` the user provided — use it directly. `SOURCE_ROOT` is the parent tree. If the path is ambiguous or the folder doesn't exist, ask once to clarify.
2. **Choose `PACKAGE_CATEGORY`** (`Frameworks` / `Modules` / `Packages`) using the Layer model — default `Modules` for an extracted gameplay/system module; `Frameworks` only for universal, game-agnostic infra. Confirm with the user if not obvious.
3. Derive the **folder name** (PascalCase/kebab, human-readable), the **package id** (`<PACKAGE_SCOPE>.<foldername-lowercased>`) and the **asmdef name** (per the Conventions table). Confirm with the user if derived rather than explicitly stated.
4. **New vs update:** check `<MONOREPO_PATH>/<CATEGORY>/<FolderName>/package.json`. If absent there, query the registry (`npm view <id> version --registry <REGISTRY_URL>`).
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
   | A package **already on bf-packages** (`com.blackface.*`, `com.cysharp.unitask`, `com.demigiant.dotween`, `com.sirenix.odininspector`, `com.tigerforge.easyeventmanager`, …) | asmdef `references` **+** `package.json` dependency (registry-resolvable). Must already be published → DEP-GATE. |
   | **External peer lib** NOT on the registry | asmdef `references` by name **+ documented as a peer requirement** in README. **Do NOT** put in `package.json` dependencies. |
   | Precompiled DLL (Firebase-style) | `overrideReferences: true` + `precompiledReferences: [...]` in asmdef; DLLs themselves are a peer requirement. |

   > Note: DOTween, UniTask, Odin, EasyEventManager are **already on this registry** — if the host project consumes them from bf-packages, prefer a real `package.json` dependency; otherwise treat them as peer requirements. Confirm which model the consuming project uses.

2. **Leak scan** (grep inside the folder):
   - Odin: `using Sirenix` / `[TabGroup]`/`[ShowIf]`/`[Button]`/`[Title]` → wrap in `#if ODIN_INSPECTOR` (STEP 4).
   - **Business / game-specific leak:** hardcoded BlazeSurvivor CSV paths, CSV key constants, game-specific singletons (`DataManager`, `DataPlayer`, feature enums, data-access facades unique to BlazeSurvivor). A reusable package **must not** carry these → DEP-GATE hard stop.
   - Editor-only code (`#if UNITY_EDITOR`, `using UnityEditor`, an `Editor/` subfolder) → goes into the package `Editor/` assembly.
   - `Resources/`, scenes, `.asmref` inside the folder → flag (load-path semantics change when published).

3. **Incoming consumers** — `codegraph_callers` to list who in BlazeSurvivor uses this module. Record them: they are the Phase-2 work list.

Produce a compact audit (kept in reasoning): outgoing deps by bucket, peer libs, registry deps, Odin files, editor files, business leaks, DLL refs, incoming consumers.

---

## STEP 2 — Dependency gate

- **Registry dependencies must already be published** to bf-packages (check by `npm view <id> version --registry <REGISTRY_URL>` returning a version, or that `<MONOREPO_PATH>/<CATEGORY>/<dep-folder>/package.json` exists), **or** be pushed earlier in this same run. If a needed registry dep is unpublished → report which one to package first, then **stop**.
- **External peer libs** are **not** a blocker — they are documented as peer requirements.
- **Business / SDK leak** → hard stop. Continue only if the user explicitly accepts shipping with leaks (discouraged; record as known debt in README) — better: narrow scope or file a cleanup task.

---

## STEP 3 — Plan & confirm

Present a **package summary card** and wait for explicit confirmation before doing anything destructive.

### New package — summary card

```
Category       : <Frameworks|Modules|Packages>
Package id     : <scope>.<name>@<version>
Folder         : <CATEGORY>/<FolderName>/
Display name   : <displayName>
Description    : <full description — must be a real sentence, not a placeholder>
Source folder  : <MODULE_PATH>
Asmdef         : <RuntimeAsmdef>  [+ <RuntimeAsmdef>.Editor]  (if editor code)
Unity minimum  : <UNITY_VERSION>  ⚠️ UPM hides this package in projects below this version
Registry       : <REGISTRY_URL>  (name: <REGISTRY_NAME>)
Monorepo target: <CATEGORY>/<FolderName>/  →  main

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

> **"Publish `<scope>.<name>@<version>` to the `Packages` monorepo `main`? Pushing is immediate (CI auto-publishes) and the version is immutable. (yes / plan-only / adjust)"**

Proceed only on explicit **yes**. On **adjust** — update the relevant fields and re-show the card. On **plan-only** — stop here.

---

## STEP 4 — Build the package on monorepo `main`

All writes happen in the working clone under `MONOREPO_PATH`, directly on the **`main`** branch (no feature branch).

0. **Resolve paths + PAT** (no token printed):
   ```powershell
   # Windows PowerShell
   $repo = if ($env:MONOREPO_PATH) { $env:MONOREPO_PATH } else { 'D:\Packages' }
   $remote = 'https://github.com/PackageStore/Packages.git'
   $patFile = if ($env:BF_PACKAGES_PAT_FILE) { $env:BF_PACKAGES_PAT_FILE } else { Join-Path $env:LOCALAPPDATA 'bf-packages.pat' }
   $pat = $env:BF_PACKAGES_PAT
   if ([string]::IsNullOrWhiteSpace($pat)) {
     if (Test-Path $patFile) { $pat = (Get-Content $patFile -Raw).Trim() }
   }
   if ([string]::IsNullOrWhiteSpace($pat)) { throw "BF_PACKAGES_PAT not set — set the env var or create PAT_FILE outside the repo (see Configuration)." }
   $authUrl = "https://$pat@github.com/PackageStore/Packages.git"
   ```
   ```bash
   # macOS zsh/bash
   repo="${MONOREPO_PATH:-$HOME/Packages}"
   remote='https://github.com/PackageStore/Packages.git'
   pat_file="${BF_PACKAGES_PAT_FILE:-$HOME/Library/Application Support/bf-packages/bf-packages.pat}"
   pat="${BF_PACKAGES_PAT:-}"
   if [ -z "$pat" ] && [ -f "$pat_file" ]; then
     pat="$(tr -d '\r\n' < "$pat_file")"
   fi
   if [ -z "$pat" ]; then
     echo "BF_PACKAGES_PAT not set — set the env var or create PAT_FILE outside the repo (see Configuration)." >&2
     exit 1
   fi
   authUrl="https://${pat}@github.com/PackageStore/Packages.git"
   ```
   If token resolution fails → **stop** and ask the user to set the PAT. All later steps use `$repo`.

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
   - **Never** leave the token in `origin`'s URL.

2. **Create** `<CATEGORY>/<FolderName>/` with `Runtime/` (+ `Editor/` if editor code).

3. **`package.json`** at the package root (`unity` from `UNITY_VERSION`, deps = registry-resolvable only):
   ```json
   {
     "name": "<scope>.<name>",
     "displayName": "<DisplayName>",
     "version": "<x.y.z>",
     "unity": "<UNITY_VERSION>",
     "description": "<full description confirmed in STEP 3>",
     "author": { "name": "<category label: Modules | Frameworks | Extensions>" },
     "dependencies": { "<scope>.<dep>": "<version>" }
   }
   ```
   - `author.name` defaults to the **category label** per the Conventions table (`Modules`/`Frameworks`/`Extensions`) to match the existing packages in the monorepo. Override only if the user asks.
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

11. **CHANGELOG.md** — follows [Keep a Changelog](https://keepachangelog.com) format. Place at package root (`<CATEGORY>/<FolderName>/CHANGELOG.md`).

    ⚠️ **Root-level files MUST each have a sibling `.meta` — author them.** `npm publish` works without them, but a registry-installed package lives in an **immutable** folder, so Unity cannot generate the missing metas itself and logs for every one: `Asset Packages/<id>/<file> has no meta file, but it's in an immutable folder. The asset will be ignored.` The correctly-published packages in the monorepo (`Singleton`, `B-PoolingManager`, `ProgressThread`, …) all ship `package.json.meta` + `README.md.meta` + `CHANGELOG.md.meta` alongside `Runtime.meta`. Author a meta for **every** root file the package ships — `package.json`, `README.md`, `CHANGELOG.md`, and `LICENSE`/`LICENSE.md` if present — using deterministic hex GUIDs (seed `<scope>.<name>/<filename>`) per the GUID rules in STEP 4.9. Importer type by extension:
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

1. **Dry-run pack** — there is no `publish.mjs`; CI uses plain `npm publish`, so verify with a dry run from the package folder (no auth needed for `--dry-run`):
   ```powershell
   # Windows PowerShell
   $pkgDir = Join-Path $repo '<CATEGORY>/<FolderName>'
   npm publish --dry-run --registry $REGISTRY_URL --prefix $pkgDir
   # or: Push-Location $pkgDir; npm pack --dry-run --json | Out-Host; Pop-Location
   ```
   ```bash
   # macOS zsh/bash
   ( cd "$repo/<CATEGORY>/<FolderName>" && npm publish --dry-run --registry "$REGISTRY_URL" )
   ```
   Confirm the `name`, `version`, and the `files`/`Tarball Contents` list includes `.cs`, `.asmdef`, and `.meta` files (and `package.json`, `README.md`, `CHANGELOG.md`).

2. **Static dep check** — every asmdef `references` name is either a package on bf-packages, a known peer lib, or a Unity/registry assembly; `package.json.dependencies` contains only registry-resolvable ids; no business/SDK leak remains; Odin is guarded.

3. **Description quality gate** — open `package.json` and verify `"description"` is a meaningful, complete sentence (not empty, not `"<one-line purpose>"`, not a TODO). If it fails → stop and ask the user before proceeding to STEP 6.

4. **`.meta` integrity** — each `.cs`/`.asmdef` has a sibling `.meta`; new asmdef + new folders have hand-authored metas; copied source kept its original meta. **GUID hex gate (mandatory):**
   ```powershell
   # Windows PowerShell
   Get-ChildItem (Join-Path $repo '<CATEGORY>/<FolderName>') -Recurse -Filter *.meta | ForEach-Object {
     $g = (Select-String '^guid:\s*(\S+)' $_.FullName).Matches.Groups[1].Value
     if ($g -notmatch '^[0-9a-f]{32}$') { Write-Host "BAD GUID  $g  <-  $($_.FullName)" }
   }
   ```
   ```bash
   # macOS zsh/bash
   find "$repo/<CATEGORY>/<FolderName>" -type f -name '*.meta' | while IFS= read -r file; do
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
   Get-ChildItem (Join-Path $repo '<CATEGORY>/<FolderName>') -Recurse -File |
     Where-Object { $_.Extension -ne '.meta' -and -not (Test-Path "$($_.FullName).meta") } |
     ForEach-Object { Write-Host "MISSING META  $($_.FullName)" }
   ```
   ```bash
   # macOS zsh/bash
   find "$repo/<CATEGORY>/<FolderName>" -type f ! -name '*.meta' | while IFS= read -r f; do
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
2. Commit with a **plain, neutral message** — the monorepo's `/push` convention explicitly forbids generating a `feat:`/`fix:`/`refactor:` prefix, so do NOT add one. Use `Publish <id> v<version>` (or `Add <id> v<version>` for a new package). End with the `Co-Authored-By` line.
   - PowerShell: `git -C $repo commit -m "Publish <id> v<version>"`
   - zsh/bash: `git -C "$repo" commit -m "Publish <id> v<version>"`
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
   - **Auth failure (401/403)** → PAT missing/expired. Stop and ask the user to refresh `BF_PACKAGES_PAT`.
4. **No remote at all** → stop after the local commit and give the `git remote add` / `push` commands.

After the push, CI runs automatically (workflow `publish-packages.yml`, trigger `push` to `main` on `Frameworks/**`, `Modules/**`, `Packages/**`). It scans every `package.json` and runs `npm publish` for any whose version is not already on the registry (already-published versions are skipped — so a re-push without a version bump is a no-op). Watch with `gh run watch -R PackageStore/Packages` if `gh` is available; otherwise tell the user to check the **Actions** tab.

**Never `--force` push. Never rewrite `main` history. Never commit in the BlazeSurvivor repo.**

---

## STEP 7 — Report

1. **Pushed to `main`:** commit hash (or "committed locally — no remote yet, run: …").
2. **Package:** `<scope>.<name>@<version>` under `<CATEGORY>/<FolderName>/`, asmdef `<RuntimeAsmdef>` (+ Editor), files added.
3. **CI is publishing:** verify shortly with `npm view <scope>.<name> version --registry <REGISTRY_URL>`.
4. **Install snippet** once published (note: the scope in `scopedRegistries` must match the package-id prefix — use `com.blackface` for these packages):
   ```json
   "scopedRegistries": [{ "name": "<REGISTRY_NAME>", "url": "<REGISTRY_URL>", "scopes": ["<PACKAGE_SCOPE>"] }],
   "dependencies": { "<scope>.<name>": "<version>" }
   ```
5. **Peer requirements:** external libs the consuming project must already have (and that are NOT on bf-packages).
6. **Source repo: unchanged.** Then spell out **Phase 2** (separate, do later): after the version is published & smoke-tested, switch the game to consume it — remove `<MODULE_PATH>` from BlazeSurvivor, add the `<scope>.<name>` dependency to `Packages/manifest.json`, add the assembly to consumer asmdefs, and let Unity recompile. Warn that keeping **both** the in-`Assets/` copy and a registry dependency causes a **duplicate-package conflict**, so Phase 2 removes the source in the same change.

---

## Guardrails

- **One module per run.** No sibling-folder scope creep.
- **Never modify the BlazeSurvivor repo.** Copy out; do not `git mv`. Phase 2 (the destructive game change) is separate and explicit.
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
