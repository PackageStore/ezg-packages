# EZG Editor UI

Shared Unity **editor** UI toolkit for building consistent custom `EditorWindow`
interfaces. Editor-only, no runtime code, no third-party dependencies.

## What's inside

`EditorBaseStyle` — a static, theme-aware helper:

- **`Palette`** — semantic colors (surface, border, accent, danger, success, …)
  resolved per editor skin (light / dark pro-skin).
- **`Styles`** — ready-made `GUIStyle`s (panels, cards, section titles, chips,
  toolbar search field, muted mini labels, …).
- **`EditorBaseStyle.Get(bool proSkin)` / `.Current`** — cached `Theme`
  (`Colors` + `Styles`) for the active skin.
- **Drawer helpers** (`EditorBaseStyle.Drawers.cs`) — immediate-mode drawing for
  card headers, chips, search fields, panel backgrounds and rounded borders.

## Usage

```csharp
#if UNITY_EDITOR
var theme = EditorBaseStyle.Get(EditorGUIUtility.isProSkin);
GUILayout.BeginVertical(theme.Styles.PanelStyle);
EditorBaseStyle.DrawCardHeader("Title", "subtitle");
GUILayout.EndVertical();
#endif
```

## Package ↔ source mapping

| Package file | Origin |
|---|---|
| `Editor/EditorBaseStyle.cs` | `Assets/_Project/Editor/Shared/EditorBaseStyle/EditorBaseStyle.cs` |
| `Editor/EditorBaseStyle.Drawers.cs` | `Assets/_Project/Editor/Shared/EditorBaseStyle/EditorBaseStyle.Drawers.cs` |

## Dependencies

- **Scoped registry:** none.
- **Peer requirements:** none (Unity built-ins only).

## Assembly

`Ezg.EditorUI` — editor-only assembly (`includePlatforms: ["Editor"]`).
Types live in the **global namespace** (e.g. `EditorBaseStyle`).
