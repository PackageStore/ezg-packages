# EZG Power Rename

Editor window for batch-renaming Unity assets and scene objects.

Open via **Tools ▸ EZG Technical Art ▸ Power Rename**, select one or more items in the Project or Hierarchy, set the options, and click **Rename**.

## Features

- **Prefix / Suffix** — prepend and append text to each name.
- **Trim Start / Trim End** — remove a number of characters from the start/end.
- **Find & Replace** — substring replace across the name (and the extension).
- **New Extension** — change the file extension of project assets (e.g. `png` → `psd`), using `AssetDatabase.MoveAsset`. Leave empty to keep the original extension. Applies to project assets only, not scene GameObjects.

Operations apply in order: find & replace → extension override → trim → prefix/suffix. Scene GameObjects are renamed with full Undo support; project assets are renamed through the `AssetDatabase`.

## Package ↔ source

| Package path | Type |
|---|---|
| `Editor/PowerRename.cs` | `EditorWindow` — the Power Rename tool |

## Requirements

- Unity 2022.3+
- Editor-only (no runtime code, no peer libraries).
