# EZG Icon CSV Generator

Editor tool that batch-generates 2D game UI icons with Google's **Gemini** image API, driven by CSV groups.

Open via **Tools ▸ EZG Technical Art ▸ Icon CSV Generator**, paste your Gemini API key, point each group at a CSV, review the generated images, and save the approved ones as flat PSDs.

## Features

- **CSV-driven groups** — each group has its own CSV source, row filter, filename pattern, prompt template and reference images; one settings asset can hold many groups, each an independent icon pipeline.
- **Per-row prompt tokens** — column values are substituted into the group's prompt template per row.
- **Reference images** — per-group reference images are sent alongside the prompt for style consistency.
- **Cost estimate** — shows the projected Gemini spend before you generate.
- **Parallel generation** — configurable concurrency cap (2–10).
- **On-disk cache** — generated results are cached under `Library/EzgIconCsvGenCache/` so re-opening the window restores them without re-calling the API.
- **Review workflow** — approve / reject / regenerate each icon before it is written.
- **Flat PSD export** — approved icons are composited over solid white and written as PSDs (native resolution) to a staging folder.
- **Idempotency** — skips icons that already exist in the staging folder or anywhere under `Assets/` (filename-stem match).

## Setup

1. **API key** — open the window and enter your Google Gemini API key. It is stored in `EditorPrefs` (per-machine, never committed to the project).
2. **Settings asset** — create one via **Assets ▸ Create ▸ EZG Technical Art ▸ Icon CSV Generator ▸ Settings**. The window auto-discovers it by type (`t:IconGeneratorSettings`); multiple assets act as selectable profiles.
3. **Paths** — configure these on the settings asset (Inspector), so the package carries no hardcoded project layout:
   - `incomingRoot` — where generated PSDs are written, under a per-group subfolder. Default `Assets/_Incoming`.
   - `referenceImagesRoot` — where per-group reference images live. Default `Assets/Editor/IconReferenceImages`.

## Package ↔ source

| Package path | Type |
|---|---|
| `Editor/IconGeneratorWindow.cs` | `EditorWindow` — the main tool |
| `Editor/IconGeneratorSettings.cs` | `ScriptableObject` — global params, CSV groups, and the configurable roots |
| `Editor/IconGenPaths.cs` | Resolves the output/input roots from the active settings asset |
| `Editor/GeminiImageClient.cs`, `GeminiRequestDtos.cs` | Gemini image API client (UnityWebRequest + JsonUtility) |
| `Editor/IconCsv*.cs`, `IconRowModel.cs`, `IconFilenameBuilder.cs` | CSV loading, filtering and filename building |
| `Editor/PsdEncoder.cs`, `IconWriter.cs` | Flat-PSD encode and write |
| `Editor/IconGenCache.cs`, `IconReviewState.cs` | Disk cache and review state |
| `Editor/ButtonIcons/` | Lucide UI icons for the toolbar (fail-soft: buttons fall back to text if absent) |
| `Editor/Tests/` | EditMode tests (PSD encoder, filename builder, row filter) |

## Requirements

- Unity 2022.3+
- A Google **Gemini** API key with image generation access.
- Editor-only (no runtime code, no peer libraries; Unity built-ins only).
