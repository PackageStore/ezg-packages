# com.ezg.colors

Standard HTML/X11 color utilities for Unity.

## Contents

| Type | Description |
|---|---|
| `ColorEnum` | 148 named HTML/X11 color identifiers |
| `ColorModel` | Struct pairing a `ColorEnum` with its hex code |
| `ColorSystem` | Static utility: hex↔Color32 conversion, `ColorEnum.GetColor()`, `string.SetColor()` rich-text helpers |

## Source mapping

Extracted from `Assets/_Game/4.CORE/Modules/Colors/` in the BlazeSurvivor game repo.  
Assembly: `Ezg.Colors`

## Dependencies

None — only `UnityEngine` (engine auto-reference).

## Peer requirements

None.

## Installation

Add to `Packages/manifest.json`:

```json
"scopedRegistries": [{
  "name": "Easygoing code base",
  "url": "https://upm-registry-worker.developer-a1f.workers.dev",
  "scopes": ["com.ezg"]
}],
"dependencies": {
  "com.ezg.colors": "0.1.0"
}
```
