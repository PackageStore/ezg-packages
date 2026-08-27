---
name: publish-unity-package
description: Export one specified file/folder from the current game repo (via Unity's AssetDatabase.ExportPackage, preserving GUID + path) into a standalone .unitypackage, add/update its entry in the ezg-packages monorepo's asset-catalog.json, and push both straight to the main branch of ezg-packages (MONOREPO_PATH — %USERPROFILE%\ezg-packages on Windows, $HOME/ezg-packages on macOS/Linux). The push triggers GitHub Actions to publish it to Feature Hub's "Unity Packages" tab on Cloudflare R2. NON-destructive to the source repo. Authenticates to the monorepo over SSH; if SSH isn't set up on this machine, auto-invokes the /setup-package-push skill inline instead of stopping, falling back to a GitHub PAT only if that can't complete. Used when the user says "đóng module X thành unitypackage" / "đóng gói X thành Unity package" / "publish unity package cho X" / "đẩy X lên tab Unity Packages" / "thêm X vào asset catalog" / "xuất .unitypackage cho X". Do NOT use for "đóng module X thành package UPM" / "package this module" / "extract X to a UPM package" — those go to /package-module instead (different pipeline: UPM registry + Packages/manifest.json, not a raw file drop back into Assets/).
---

# Publish Unity Package — `.unitypackage` → Feature Hub "Unity Packages" tab

Take one **specified file or folder** (under `Assets/` in the current game repo), export it as a
standalone **`.unitypackage`** with Unity's own `AssetDatabase.ExportPackage` (so GUIDs and asset
paths survive intact), and **publish it to the `main` branch of the `ezg-packages` monorepo**
(`MONOREPO_PATH`) so it shows up in **Feature Hub's "Unity Packages" tab**. Any project sharing
this template's GUID lineage installs or updates it with the file landing back at the **exact same
path** it was exported from — this is the whole point of this pipeline over `/package-module`: the
content stays inside `Features/_Shared/…` (or wherever it lives), it never moves into `Packages/`.

> **Not the same thing as `/package-module`.** That skill extracts code into a **UPM package**
> (own `package.json`/asmdef, resolved via `Packages/manifest.json`, lives in
> `Library/PackageCache/`, meant for genuinely reusable, project-agnostic code). This skill ships a
> **raw asset snapshot** back into `Assets/` at its original path — meant for code that is only
> ever consumed by sibling projects generated from **this same base template** (so it may freely
> depend on other template-shared code like `Ezg.Feature.Shared.Systems.Utils` without that being a
> packaging blocker — the consumer already has it). If the user's phrasing says "package"/"UPM"/
> "registry", that's `/package-module`. If it says "unitypackage"/"asset catalog"/"Unity Packages
> tab", it's this skill. **Ambiguous phrasing → ask once before proceeding.**

**One file/folder per run.**

---

## ⚙️ Configuration

Resolve from user input or sensible defaults; ask only for values that cannot be inferred.

```
MODULE_PATH        = required — path (repo-relative or absolute) to the file or folder to export
PACKAGE_NAME        = derived from MODULE_PATH's basename, confirm with user (this is the catalog
                       entry's "name" — free text shown in Feature Hub, e.g. "ShowingObjectController")
CATEGORY            = user-specified, default "EZG Shared" (groups with other in-house entries,
                       distinct from third-party bundles like "SDK"/"VFX & Shader"/"Editor")
INSTALLED_BY_DEFAULT = default false (opt-in install) — only true if the user explicitly wants this
                       bundled into every new project's bootstrap install
MARKER_PATHS        = default [MODULE_PATH as project-relative path] — lets Feature Hub detect
                       "already installed" from disk even without a local install-record
MONOREPO_REMOTE     = user-specified, default https://github.com/PackageStore/ezg-packages.git
MONOREPO_PATH       = user-specified (or env MONOREPO_PATH), default per OS:
                      Windows:      %USERPROFILE%\ezg-packages
                      macOS/Linux:  $HOME/ezg-packages
                      # If a clone already exists at this path, use it in place; only auto-clone if missing
PAT_FILE            = fallback only, used when SSH isn't set up — user-specified (or env
                      EZG_PACKAGES_PAT_FILE), default per OS:
                      Windows: %LOCALAPPDATA%\ezg-packages.pat
                      macOS:   $HOME/Library/Application Support/ezg-packages/ezg-packages.pat
                      Linux:   ${XDG_CONFIG_HOME:-$HOME/.config}/ezg-packages/ezg-packages.pat
```

**`MODULE_PATH` is the only required input.** Everything else has a default; confirm/override once
on first run for a new item.

### Shell / OS compatibility

Same convention as `/package-module`: zsh/bash and PowerShell snippets given side by side, pick the
one matching the current machine. Only the monorepo path is OS-specific. Quote every path.

### Git authentication — SSH first, PAT fallback

Identical resolution order to `/package-module` — reuse it verbatim rather than inventing a second
scheme:

1. **SSH (preferred).** If `ssh -T git@github.com` reports "successfully authenticated", use the
   SSH remote `git@github.com:PackageStore/ezg-packages.git` directly. No token to inject, nothing
   to scrub from `origin` afterward.
   - **Not set up on this machine yet? Auto-fix it, don't just tell the user.** When the SSH test
     fails, **invoke the `setup-package-push` skill inline right there**
     (`.claude/skills/setup-package-push/SKILL.md`) as part of this same run — it installs `gh` if
     missing, generates/registers an SSH key, and confirms Write access. Then re-run the SSH test
     before deciding whether to fall back to a PAT. The one part that can't be silently automated
     is `gh auth login`'s one-time browser approval — if you can't complete that yourself, hand the
     exact command to the user, wait for their confirmation, then continue this same run.
2. **GitHub PAT (fallback)** — only reached if SSH auto-onboarding genuinely can't complete. Read at
   runtime from `EZG_PACKAGES_PAT` (env, preferred) or `PAT_FILE` (one line, outside the repo).
   **Never** write the PAT into this file or any tracked file, never print it, never echo it into
   logs. Inject it via `https://<PAT>@github.com/...` only on the git command itself, then reset
   `origin` to the clean URL immediately after.

`gh` CLI is needed regardless of git-auth mode, for the release-staging step (STEP 5) — SSH mode
already leaves `gh` authenticated (`setup-package-push` runs `gh auth login`); PAT mode passes
`GH_TOKEN=$pat` to each `gh` invocation ad hoc, no persistent `gh auth login` required.

---

## Why a GitHub Release is involved (read before STEP 5)

`*.unitypackage` is **gitignored** in `ezg-packages` (binary template assets are stored on R2, not
committed — see the monorepo's `.gitignore`). That means the push-triggers-CI pattern `/package-module`
uses (`publish.yml` watches `packages/**`) cannot fire for the binary itself — a CI checkout of
`main` would never contain it. The monorepo already solves this for exactly this situation with
`upload-asset.yml`: attach the file to a throwaway GitHub Release, `workflow_dispatch` that action
with the release tag + asset name + destination R2 key, and CI (using repo secrets, not your local
credentials) downloads the release asset and PUTs it to R2. This skill reuses that existing,
generic workflow **unmodified** — for both the `.unitypackage` binary and the catalog JSON. Nothing
new is added to `.github/workflows/`. Same end result as `/package-module`: **whoever can push to
this repo can publish** — no personal R2 keys ever touch your machine.

---

## Pipeline

```
[0] IDENTIFY  → source path + catalog entry name/fileName/category + new vs update
[1] AUDIT     → classify deps: Unity/BCL (fine) · third-party already bootstrap-installed via this
                same tab (DOTween, Odin — fine, just note as peer requirement) · other
                template-shared code (Utils.cs, IExecute, other Ezg.Feature.Shared.* symbols — fine,
                these are EXPECTED to already exist in every sibling project, not a leak here) ·
                genuine business/game-specific leak (hardcoded CSV keys, DataManager/DataPlayer
                singletons unique to THIS game) → only this last bucket blocks
[2] PLAN      → show catalog-entry summary card, explain the push publishes immediately, confirm
[3] BUILD     → Unity MCP: AssetDatabase.ExportPackage(MODULE_PATH[, Recurse if folder]) to a local
                .unitypackage under MONOREPO_PATH/templates/unity-project/PackageTemplate/
[4] CATALOG   → add/update entry in MONOREPO_PATH/templates/unity-project/asset-catalog.json,
                commit + push straight to main (small tracked text file — normal git push)
[5] STAGE     → gh release create (throwaway tag) with the .unitypackage + the updated
                asset-catalog.json attached → gh workflow run upload-asset.yml twice (binary key,
                catalog key) → gh release delete once both runs succeed
[6] VERIFY    → curl the live catalog + file, confirm sha256 matches what was just built
[7] REPORT    → commit hash, release tag (already deleted), R2 keys published, install snippet
```

STEP 0–2 are **non-destructive** (nothing written anywhere). STEP 3 onward writes to the monorepo
clone and, from STEP 4, to `main` + R2 — proceed only after explicit confirmation in STEP 2.

---

## STEP 0 — Identify the target

1. Resolve `MODULE_PATH` from what the user gave. If it's a file, this run ships exactly that file
   (+ its `.meta`, handled automatically by `ExportPackage`). If it's a folder, everything under it.
2. Derive `PACKAGE_NAME` (human-readable, matches the class/feature name) and `fileName` =
   `"<PACKAGE_NAME>.unitypackage"`. Confirm with the user if derived rather than stated.
3. **New vs update:** read `MONOREPO_PATH/templates/unity-project/asset-catalog.json`, search
   `assets[].name` for an exact match.
   - Missing → **new entry**.
   - Exists → **update** — the new export overwrites the existing `PackageTemplate/<fileName>` and
     the catalog's `sha256`; Feature Hub shows `UnityPackageStatus.UpdateAvailable` to anyone with
     an older install-record. No version field to bump — sha256 IS the version signal here.

State the resolved `{source path, name, fileName, category, installedByDefault, new|update}`.

---

## STEP 1 — Audit dependencies & leaks

Use **codegraph first**; grep only for `using` directives and string literals.

1. **Outgoing deps** — `codegraph_callees`/`codegraph_impact` on the exported symbols +
   `Grep` for `using ` in the target. Classify:

   | Bucket | Verdict |
   |---|---|
   | `UnityEngine`, `System.*` | fine, nothing to declare |
   | Third-party already shipped via this same "Unity Packages" tab bootstrap (DOTween, Odin — check `installedByDefault: true` entries in the catalog) | fine — note as a peer requirement in the catalog entry's `description`, e.g. "Requires DOTween + Odin Inspector (already in the default bootstrap)." |
   | Other **template-shared** code (`Ezg.Feature.Shared.*`, `_Shared/Systems/Utils.cs`, small shared interfaces like `IExecute`) | fine — this pipeline's whole premise is that consumers are sibling projects from the same template lineage and already have it. Do NOT try to inline/duplicate it away (that was the right call for a UPM package; it is unnecessary overhead here). |
   | Hardcoded game-specific CSV keys / `Assets/` paths for CSV or Resources / direct references to `DataManager`, `DataPlayer`, `GameEnums.Features`, or any singleton unique to **this** game | **leak — hard stop.** Continue only if the user explicitly accepts shipping with it (record as known debt in the catalog entry's `description`); better to narrow `MODULE_PATH`. |

2. **Incoming consumers** — `codegraph_callers` to list who in the game repo uses this module today
   (informational only; unlike `/package-module` there is no Phase 2 "switch to consumer" step —
   the source stays exactly where it is, nothing to migrate).

Produce a compact audit (kept in reasoning): peer requirements, business leaks (if any).

---

## STEP 2 — Plan & confirm

```
Catalog entry  : <PACKAGE_NAME>                              [new | update <old-sha256[:8]> → <new>]
fileName       : <PACKAGE_NAME>.unitypackage
Category       : <CATEGORY>
installedByDefault: <true|false>
markerPaths    : [ "<MODULE_PATH as project-relative path>" ]
Description    : <peer requirements / known debt, or omit>
Source         : <MODULE_PATH>  (exported as-is, GUIDs preserved — NOT modified)
Monorepo target: templates/unity-project/PackageTemplate/<fileName>.unitypackage
                 + templates/unity-project/asset-catalog.json  →  ezg-packages main
```

Then ask once:

> **"Publish `<PACKAGE_NAME>` to Feature Hub's Unity Packages tab? Pushing is immediate — CI
> publishes to R2 right after. (yes / plan-only / adjust)"**

Proceed only on explicit **yes**. **adjust** → update fields, re-show. **plan-only** → stop here.

---

## STEP 3 — Export the `.unitypackage`

Requires an Editor connected via Unity MCP (`unity_list_instances`; select if several).

```csharp
// via unity_execute_code, in the game repo's Editor instance
AssetDatabase.ExportPackage(
    new[] { "<MODULE_PATH, project-relative, e.g. Assets/_Project/Features/_Shared/UI/ShowingObjectController.cs>" },
    "<MONOREPO_PATH>/templates/unity-project/PackageTemplate/<fileName>",
    ExportPackageOptions.Recurse   // only if MODULE_PATH is a folder; omit (Default) for a single file
);
```

`ExportPackageOptions.Default` (no `IncludeDependencies`) is intentional — see STEP 1: peer
template-shared deps are expected to already exist on the consumer, not bundled in.

Compute the new sha256 locally (needed for STEP 4):
```bash
# macOS/Linux
shasum -a 256 "$MONOREPO_PATH/templates/unity-project/PackageTemplate/<fileName>" | awk '{print $1}'
```
```powershell
# Windows
(Get-FileHash "$MONOREPO_PATH\templates\unity-project\PackageTemplate\<fileName>" -Algorithm SHA256).Hash.ToLower()
```

---

## STEP 4 — Write the catalog entry & push to `main`

1. **Fresh + clean working tree** — same discipline as `/package-module` STEP 4.1: `checkout main`,
   `fetch`/`pull --ff-only`, refuse to touch a dirty clone without asking the user first.
2. Edit `templates/unity-project/asset-catalog.json` — add or update the object:
   ```json
   {
     "name": "<PACKAGE_NAME>",
     "fileName": "<PACKAGE_NAME>.unitypackage",
     "url": "https://upm-registry-worker.developer-a1f.workers.dev/template/files/<urlencoded fileName>",
     "category": "<CATEGORY>",
     "sha256": "<sha256 from STEP 3>",
     "installedByDefault": <true|false>,
     "markerPaths": [ "<MODULE_PATH as project-relative path>" ]
   }
   ```
   Keep valid JSON (trailing comma, array position) — append near other non-bootstrap entries unless
   updating in place.
3. Commit + push directly to `main` (no branch, no PR — matches `/package-module`):
   ```bash
   git -C "$repo" add templates/unity-project/asset-catalog.json
   git -C "$repo" commit -m "feat(unity-packages): add <PACKAGE_NAME> to asset catalog"
   git -C "$repo" pull --rebase "$remote" main   # $remote = SSH or authUrl per the resolved auth mode
   git -C "$repo" push "$remote" main
   ```
   Non-fast-forward → pull/rebase again, never `--force`.

---

## STEP 5 — Stage & publish the binary (+ catalog) to R2

No new CI — reuse the existing generic `upload-asset.yml` (see rationale above) via `gh`.

```bash
tag="unity-pkg-$(git -C "$repo" rev-parse --short HEAD)-<slug-of-PACKAGE_NAME>"

gh release create "$tag" \
  "$repo/templates/unity-project/PackageTemplate/<fileName>" \
  "$repo/templates/unity-project/asset-catalog.json" \
  --repo PackageStore/ezg-packages \
  --title "Unity Packages: <PACKAGE_NAME>" \
  --notes "Staging release for Feature Hub Unity Packages tab publish. Safe to delete after CI runs."

gh workflow run upload-asset.yml --repo PackageStore/ezg-packages \
  -f release_tag="$tag" -f asset_name="<fileName>" \
  -f key="unity-template/files/<fileName>" -f content_type="application/octet-stream" \
  -f force=<true if update, else false> -f dry_run=false

gh workflow run upload-asset.yml --repo PackageStore/ezg-packages \
  -f release_tag="$tag" -f asset_name="asset-catalog.json" \
  -f key="unity-template/asset-catalog.json" -f content_type="application/json" \
  -f force=true -f dry_run=false
```

Wait for both runs (`gh run watch -R PackageStore/ezg-packages`, or poll `gh run list --workflow=upload-asset.yml -R PackageStore/ezg-packages -L 2`). On success, **delete the staging release** — it only existed to hand the binary to a runner:

```bash
gh release delete "$tag" --repo PackageStore/ezg-packages --yes --cleanup-tag
```

If either `workflow run` fails, leave the release in place (so the artifact is still stageable), report the failure, and stop.

---

## STEP 6 — Verify

```bash
TOKEN=$(python3 -c "import json,os;print(json.load(open(os.path.expanduser('~/.ezg/credentials.json')))['access_token'])")

curl -fsSL -H "Authorization: Bearer $TOKEN" \
  https://upm-registry-worker.developer-a1f.workers.dev/template/asset-catalog.json \
  | python3 -c "import json,sys;d=json.load(sys.stdin);e=[a for a in d['assets'] if a['name']=='<PACKAGE_NAME>'][0];print(e['sha256'])"

curl -fsSL -H "Authorization: Bearer $TOKEN" \
  https://upm-registry-worker.developer-a1f.workers.dev/template/files/<fileName> | shasum -a 256
```
Both sha256 values must match the one computed in STEP 3. Mismatch → re-run the STEP 5 dispatch with `force=true`, don't just re-report.

---

## STEP 7 — Report

1. **Pushed to `main`:** commit hash for `asset-catalog.json` (or "committed locally — no push yet" if auth resolution failed and the user needs to finish it).
2. **Published:** `<PACKAGE_NAME>` under `templates/unity-project/PackageTemplate/<fileName>`, R2 keys `unity-template/files/<fileName>` + refreshed `unity-template/asset-catalog.json`.
3. **Staging release:** deleted (or its tag, if cleanup failed — tell the user to delete it by hand).
4. **Where to see it:** `Ezg > Feature Hub > Unity Packages` tab, category `<CATEGORY>`.
5. **Peer requirements:** any third-party/template-shared deps noted in STEP 1 that the installing project must already have.
6. **Source repo: unchanged** — this pipeline never removes or rewrites `MODULE_PATH`.

---

## Guardrails

- **One file/folder per run.**
- **Never modify the game repo.** `AssetDatabase.ExportPackage` only reads.
- **The `MONOREPO_PATH` clone is a real working clone** — never blindly `reset --hard`; if dirty, show the user and ask before discarding.
- **Monorepo: commit + push directly to `main`** (no branch, no PR). Never `--force`-push, never rewrite history; pull/rebase before pushing.
- **Never leave a PAT in `origin`'s URL** (PAT fallback only — SSH mode has no token to leak).
- **Business/game-specific leak → hard stop**, same bar as `/package-module`.
- **Don't add new GitHub Actions workflows** for this — `upload-asset.yml` already does exactly what's needed; adding a second, narrower workflow would just be duplicate maintenance.
- **Delete the staging release once both R2 uploads succeed** — it has no reason to persist.
- **Ambiguous "package" phrasing → ask, don't guess** whether the user means this skill or `/package-module`.
