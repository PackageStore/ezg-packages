# EZG Texture Format Override

Editor window for batch-applying texture import settings to the current Project-window selection.

Open via **Tools ▸ EZG Technical Art ▸ Texture Format Override**, select textures and/or folders in the Project window, pick one of the two mutually-exclusive modes, then click **Apply**. A green/amber indicator shows how many textures are targeted. Selected folders are scanned for textures; the **Recursive** checkbox controls whether subfolders are included. Any `TextureImporter` asset is accepted (png, psd, jpg, tga, exr…).

## Modes

The two modes are a radio toggle — exactly one is active at a time.

- **Override for Android and iOS** — enables the per-platform override tabs for Android and iPhone and writes their **Format** and **Compression Quality**. **Force Set Texture Size** (off by default) additionally sets the platform override's **Max Texture Size**. **Remove PSD Matte** toggles `m_PSDRemoveMatte` on `.psd` files only (PNGs unaffected).
- **Custom Max Size** — writes the **Default** tab's **Max Texture Size** (32–4096 slider) and forces **NPOT scale** to `None`. Does not touch the Android/iOS override tabs.

Reimports are batched inside `AssetDatabase.StartAssetEditing` / `StopAssetEditing`.

## Package ↔ source

| Package path | Type |
|---|---|
| `Editor/TextureFormatOverrideWindow.cs` | `EditorWindow` — the Texture Format Override tool |

## Dependencies

None (`com.ezg.*` registry dependencies).

## Peer requirements

None — uses only `UnityEngine` and `UnityEditor`.
