# EZG Feature Hub

Editor window that installs Unity packages into the current project from a remote catalog.

Open via the menu **`Ezg > Feature Hub`**.

## What it does

- **Unity Packages tab** — lists `.unitypackage` entries from a remote `asset-catalog.json`. Downloads each to `Temp/`, verifies its SHA-256, imports it via `AssetDatabase.ImportPackage` (silent or interactive), then records the install in `ProjectSettings/EzgFeatureHub/install-record.json` so status (installed / update available) survives across sessions.
- **UPM Packages tab** — lists UPM dependencies from a remote `unity-template.json`. Writes the selected dependency (and any scoped registries) into `Packages/manifest.json`, downloads `.tgz` files for `file:` dependencies, then triggers a package resolve.
- UI Toolkit window with animated **Lottie** icons rendered in the Editor via rlottie (idle state, micro-animation on hover).

## Package ↔ source folder

| Package path | Origin in the game repo |
|---|---|
| `Editor/` (all `.cs`, `.asmdef`, `Lottie/*.json`) | `Assets/_Project/Editor/FeatureHub/` |

Everything ships as a single **Editor-only** assembly (`Ezg.FeatureHub.Editor`).

## Dependencies

None from the EZG scoped registry.

## Peer requirements (the consuming project must already provide these)

These are referenced by assembly name in the asmdef and are **not** declared in `package.json` — install them yourself before adding this package:

- **`com.gindemit.rlottie`** — provides the `LottiePlugin.Runtime` assembly used to render the Lottie icons in-editor.
- **`com.unity.nuget.newtonsoft-json`** — provides the `Newtonsoft.Json` assembly used to parse the catalog/template JSON and to read/write `Packages/manifest.json`.

## Configuration

The remote catalog/template URLs are compiled into `FeatureHubConstants` (Cloudflare R2). Changing a URL means bumping the package version.
