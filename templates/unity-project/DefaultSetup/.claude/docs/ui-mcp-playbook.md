---
description: Reference doc (not a command) — executable Unity MCP layer for the new-ui / new-package workflows. Lives in .claude/docs/ so it is not exposed as a slash command; pulled in by new-ui.md.
---

# UI MCP Execution Playbook

This is the **executable layer** shared by [new-ui.md](../workflows/new-ui.md) (via
[new-ui-guide.md](new-ui-guide.md)) and the Package branch of
[new-package.md](../workflows/new-package.md) (via [new-package-guide.md](new-package-guide.md)).
The guides decide *what* to build (template, layout mode, containment, catalog); this file is the
deterministic *how* — the exact Unity MCP tool sequence, property paths, value formats, reference
wiring, and the screenshot verify loop.

It exists because an AI agent drives Unity **through MCP tools, not the Inspector** — it cannot
"drag X into Y". Every "drag/assign/bind" instruction in `new-ui-guide.md` maps to a concrete tool
call here.

> **Scope note:** property paths (`m_*`) and value formats below are Unity-universal and were
> verified live on the sibling project (same Unity 6000.x + same MCP server). **Project-specific
> hierarchy** (`BackgroundButton`/`Popup`/`FullScreen`, `Popup/content`, template GUIDs) must be
> confirmed live against this repo with `unity_prefab_info` / `unity_execute_code` before wiring —
> never guess hierarchy (already required by `new-ui-guide.md` §0).
>
> **Always pass `port`** on every `unity_*` call. Get it once from `unity_select_instance`.
> Examples below omit it for brevity.

---

## 0. Preflight (once per session)

1. `unity_list_instances` → if exactly one editor for THIS project, note its `port`; if several,
   `unity_select_instance`. If none is running, the agent cannot proceed — ask the user to open
   the Unity project.
2. `unity_scene_info` → note the active scene and whether it `isDirty`. Build under the scene's
   `Canvas`. **Never `unity_scene_new`** while the current scene `isDirty`.
3. **Pin the Game view to the design resolution — before any screenshot.** The project's UI
   canvas is CanvasScaler **1080×1920 portrait, match height** (see `SplashScene` /
   `OverviewCanvas`). At Free Aspect or any other size the scaler re-lays the screen, so every
   screenshot — the §5 builder loop AND the `ui-visual-reviewer` checkpoints — would grade a
   composition no device will show. Pin via `unity_execute_code`:

   ```csharp
   UnityEditor.PlayModeWindow.SetViewType(UnityEditor.PlayModeWindow.PlayModeViewTypes.GameView);
   UnityEditor.PlayModeWindow.SetCustomRenderingResolution(1080, 1920, "Design 1080x1920");
   uint w, h; UnityEditor.PlayModeWindow.GetRenderingResolution(out w, out h);
   return $"GameView {w}x{h}";
   ```

   Expect `GameView 1080x1920`; cross-check the pixel size of the first saved screenshot PNG.
   `unity_graphics_game_capture` is **not** a layout-verify substitute — it renders the camera at
   an arbitrary width/height (default 512×512, wrong aspect) and can miss Screen Space – Overlay
   canvases. Layout verification = pinned view + `unity_screenshot_game`.
4. Build in the **scene**, verify visually, then save as the **prefab variant**. Never author blind.

---

## 1. The core tool loop

Every UI build is the same five moves. Repeat per element.

| Move | Tool | Purpose |
|------|------|---------|
| **Place** | `unity_asset_instantiate_prefab` | Drop a template into the scene under the right parent. |
| **Parent** | `unity_gameobject_reparent` | Move it into the content container (`Popup/content` or `FullScreen/Mid`). |
| **Shape** | `unity_component_set_property` | Set RectTransform / Image / Text values (§3). |
| **Wire** | `unity_component_get_referenceable` → `unity_component_batch_wire` | Connect serialized references (§4) — this replaces every "drag" instruction. |
| **See** | `unity_screenshot_game` | Look at the result and correct it (§5). Never declare done blind. |

Keep every returned `instanceId` — names collide in the hierarchy, instanceIds do not, so they are
the most reliable handle for `reparent`, `get_referenceable`, and `batch_wire`.

---

## 2. `instantiate` value formats

`unity_asset_instantiate_prefab`:
- `prefabPath` = **asset path**, e.g. `Assets/Resources/Prefabs/Templates/ButtonNormal.prefab`,
  `Assets/Resources/Prefabs/Templates/FeatureTemplate.prefab`,
  `Assets/Resources/Prefabs/Templates/PackageTemplate.prefab`,
  `Assets/Resources/Prefabs/Templates/TimeLayoutTemplate.prefab`.
- `parent` = hierarchy path of the parent, e.g. `Canvas/[FeatureName]/Popup/content`.
- Per `new-ui-guide.md` §3c **content-containment rule**: new elements go inside `Popup/content`
  (pop-up) or `FullScreen/Mid` (full-screen), **never** as a direct child of `Popup`/`FullScreen`.

---

## 3. Property cheatsheet (exact `m_*` names + value JSON)

`unity_component_set_property` takes `gameObjectPath` (or resolve via `unity_gameobject_info`),
`componentType`, `propertyName`, `value`. To discover any component's real field names, call
`unity_component_get_properties` on it first — cheaper than guessing.

### RectTransform (`componentType: "RectTransform"`)

| propertyName | type | value JSON |
|---|---|---|
| `m_AnchorMin` | Vector2 | `{"x":0,"y":0}` |
| `m_AnchorMax` | Vector2 | `{"x":1,"y":1}` |
| `m_AnchoredPosition` | Vector2 | `{"x":0,"y":-40}` |
| `m_SizeDelta` | Vector2 | `{"x":600,"y":400}` |
| `m_Pivot` | Vector2 | `{"x":0.5,"y":0.5}` |
| `m_LocalScale` | Vector3 | `{"x":1,"y":1,"z":1}` |
| `m_LocalPosition` | Vector3 | `{"x":0,"y":0,"z":0}` |
| `m_LocalRotation` | Quaternion | `{"x":0,"y":0,"z":0,"w":1}` |

**Anchor recipes** (set anchors **before** size/position — anchors reinterpret those values):
- Stretch full-parent: min `{0,0}` max `{1,1}`, then `m_SizeDelta {0,0}`, `m_AnchoredPosition {0,0}`.
- Centered fixed-size: min/max `{0.5,0.5}`, then `m_SizeDelta` = width/height.
- Top-anchored: min/max `{0.5,1}`, pivot `{0.5,1}`, `m_AnchoredPosition {0,-margin}`.

> **⚠ Verified pitfall (this repo, live-tested):** `unity_component_set_property` on
> RectTransform fields (`m_SizeDelta`, `m_AnchorMin/Max`, `m_AnchoredPosition`) returns
> `success` but silently does **not** apply. Drive every RectTransform change through
> `unity_execute_code` with the direct API (`rt.sizeDelta = new Vector2(...)`) and **read the
> value back in the same snippet**. Other components (LayoutGroup, Image, Text) accept
> `set_property` normally — when in doubt, read back after writing.

### Image (`componentType: "Image"`)

| propertyName | type | value JSON |
|---|---|---|
| `m_Color` | Color | `{"r":1,"g":1,"b":1,"a":1}` (0–1 range) |
| `m_Sprite` | ObjectReference | `"Assets/.../icon.png"` or `{"assetPath":"..."}` |
| `m_RaycastTarget` | bool | `true` / `false` |
| `m_Type` | Enum | `"Simple"` / `"Sliced"` / `"Filled"` |
| `m_FillAmount` | float | `0.65` |
| `m_PreserveAspect` | bool | `true` |

Assign a sprite: `unity_search_assets` (`type:"Sprite"`/`"Texture2D"`) to get the path → set
`m_Sprite` to that path string.

### Text / localize

- Legacy `Text` (`componentType:"Text"`): set `m_Text` (string) only.
- TMP (`componentType:"TextMeshProUGUI"`): `m_text`, `m_fontColor` (Color), `m_fontSize` (float).
- **`LocalizesUI`** (this repo's localize component — see `new-ui-guide.md` §3b): set the `LangKey`
  field to `#[featurename]_title` (lowercase, `#` prefix). Then register the key via
  [add-localize.md](.claude/commands/add-localize.md) — an unregistered key renders the raw key.

### Layout groups (layout-group-first — new-ui-guide.md §3e)

The base templates ship **without** layout groups on their containers, so add the component
first — `unity_component_add { gameObjectPath, componentType: "VerticalLayoutGroup" }` (or
`HorizontalLayoutGroup` / `GridLayoutGroup` / `ContentSizeFitter` / `LayoutElement`) — then set
values. Standard Unity serialized names (not yet live-verified on this MCP build — run
`unity_component_get_properties` once to confirm before writing):

| componentType | propertyName | type | value JSON |
|---|---|---|---|
| `VerticalLayoutGroup` / `HorizontalLayoutGroup` | `m_Spacing` | float | `24` |
| | `m_Padding.m_Left` / `m_Right` / `m_Top` / `m_Bottom` | int | `40` |
| | `m_ChildAlignment` | Enum | `"UpperCenter"` |
| | `m_ChildControlWidth` / `m_ChildControlHeight` | bool | `true` |
| | `m_ChildForceExpandWidth` / `m_ChildForceExpandHeight` | bool | `false` |
| `GridLayoutGroup` | `m_CellSize` / `m_Spacing` | Vector2 | `{"x":300,"y":300}` |
| | `m_Constraint` + `m_ConstraintCount` | Enum + int | `"FixedColumnCount"` + `3` |
| `ContentSizeFitter` | `m_HorizontalFit` / `m_VerticalFit` | Enum | `"Unconstrained"` / `"PreferredSize"` |
| `LayoutElement` | `m_PreferredWidth` / `m_PreferredHeight` | float | `600` |

- Nested path `m_Padding.m_Left` rejected by this MCP build? → set the whole `m_Padding` object,
  or fall back to `unity_execute_code`:
  `GameObject.Find("...").GetComponent<UnityEngine.UI.VerticalLayoutGroup>().padding = new RectOffset(40, 40, 32, 32);`
- While `m_ChildControlWidth/Height` is on, children's `m_SizeDelta` is overridden — size
  children via `LayoutElement` (`m_PreferredWidth/Height`) instead.

### Tab bar (new-ui-guide.md §3d — tabs live in `Bot/TabBottomTemplate`, never a row in `Mid`)

The bar already exists on every `FeatureTemplate` variant but ships inactive, so the sequence is
activate → populate → wire, not "build a new row":

1. `unity_gameobject_set_active { path: "<Feature>/FullScreen/Bot/TabBottomTemplate", active: true }`.
2. Tabs are children of that node: rename/duplicate the shipped `Tab1`/`Tab2`
   (`TabToggleTextTemplate`), or `unity_asset_instantiate_prefab` `TabToggleIconTemplate.prefab`
   with `parentPath: ".../Bot/TabBottomTemplate"`. Delete leftover sample tabs. It already owns
   `ToggleGroup` + `HorizontalLayoutGroup` — do **not** add a second layout group.
3. Each tab gets a page GameObject under `FullScreen/Mid`, same order as the toggles.
4. Wire the `UI_TabExtensions` already on the bar (§4 formats): `_toggleList` = the toggles in
   order, `_objectList` = the pages at matching indexes, `_mainCanvasScale` = root `CanvasScaler`,
   `_indexOnOpen` = default tab (`-1` keeps current state), `_useAnimSwap` = slide animation on/off.
5. The controller then only calls `RegisterOnchangeAction(i, action)` / `JumpToIndex(i)`
   (`EquipmentController.Start()` is the reference). No hand-written `onValueChanged` listeners
   and no manual `Focus.SetActive` — `ToggleGroup` + `UI_TabExtensions` own that.

### Value-format rules (verified)
- Vector2 `{x,y}` · Vector3 `+z` · Color `{r,g,b,a}` 0–1 · Quaternion `+w`.
- ObjectReference → asset-path string, scene-object name string, `null` to clear, or
  `{assetPath}` / `{instanceId}` / `{gameObject, componentType}`.

---

## 4. Wiring serialized references (replaces every "drag" in new-ui-guide.md)

A built screen renders but its buttons/controller fields are dead until wired.

1. **Discover targets** — `unity_component_get_referenceable` with the controller's
   `componentType`, the target `path`/`instanceId`, and the `propertyName`. Returns assignable
   scene objects/assets.
2. **Assign** — `unity_component_batch_wire`, one entry per reference:
   - `path`/`instanceId` = GameObject holding the component to set
   - `componentType` = e.g. `[PackageName]Controller`
   - `propertyName` = serialized field
   - `referenceGameObject`/`referenceInstanceId` = object to assign
3. Single reference shortcut: `unity_component_set_property` with ObjectReference value
   `{gameObject:"...", componentType:"..."}`.

**Concrete mappings from new-ui-guide.md Package branch (Step 4):**
- "drag `Popup/content/PurchaseTemplate` into `_purchase`" →
  `batch_wire { path:"[FeatureName]", componentType:"[PackageName]Controller",
  propertyName:"_purchase", referenceGameObject:"[FeatureName]/Popup/content/PurchaseTemplate",
  referenceComponentType:"PurchaseTemplateController" }`.
- "drag the `TimeText` child into `_cooldownTime`" → `batch_wire` with
  `propertyName:"_cooldownTime"`, `referenceGameObject:".../CooldownTime"`,
  `referenceComponentType:"UI_CooldownTimeView"`.
- `_packIndex` is an int, not a reference → `set_property` `{propertyName:"_packIndex", value:0}`.
- `ClickBackgroundToExit = true` (inherited from `FeatureBaseController`) →
  `set_property` `{componentType:"[PackageName]Controller", propertyName:"ClickBackgroundToExit",
  value:true}`.
- `FeatureType` (enum, TabGroup "Common" on `GameFeatureBaseController`) →
  `set_property` `{propertyName:"FeatureType", value:"<EnumBase.Features member>"}`.

**Never wire `MainUI`** (inherited from `FeatureBaseController`, TabGroup "Cấu hình chung"). It is a
deliberate opt-out field: empty → `Awake()` falls back to `transform.GetChild(1)` = `Popup`, which
gives popups their scale-in and full-screen screens their fade-in (new-ui-guide.md §0 "Layout mode →
`MainUI`"). Pointing it at `FullScreen` makes a full-screen screen pop like a dialog. If a
prefab already has it set and the task didn't ask for that, clear it:

```csharp
// unity_execute_code — clear MainUI back to the default fallback
var so = new UnityEditor.SerializedObject(comp);
so.FindProperty("MainUI").objectReferenceValue = null;
so.ApplyModifiedProperties();
```

After wiring, confirm with `unity_component_get_properties` on the controller — `[Required]`
fields must be non-null.

> **Verified alternative** when `batch_wire`/`get_referenceable` can't reach a member
> (private `[SerializeField]`, enum field, persistent `onClick`): `unity_execute_code` +
> `SerializedObject` — `FindProperty("_field").objectReferenceValue = comp;
> ApplyModifiedProperties()`; enum via `.intValue`; persistent listeners via
> `UnityEditor.Events.UnityEventTools.AddPersistentListener(btn.onClick, action)`.
> Read every value back afterwards (same rule as RectTransform above).

---

## 5. Visual verify loop (mandatory — do not skip)

UI built blind via property writes is wrong more often than right. After each meaningful chunk:

1. `unity_screenshot_game` (optionally `superSize:2`) — at the §0-pinned **1080×1920** Game
   view; if anything may have resized it, re-run the §0 pin snippet first.

   > **⚠ Edit-mode reality (verified live):** outside play mode, `unity_screenshot_game`
   > captures only the camera render — **Screen Space Overlay canvases are NOT composited**
   > (Unity 6000.3 + URP + pinned PlayModeWindow resolution), so the PNG looks like an empty
   > dark frame even though the UI exists. In play mode it works normally. The verified
   > edit-mode method is a manual RenderTexture capture via `unity_execute_code` (temporarily
   > point the canvas at a throwaway camera, `cam.Render()` synchronously, then restore the
   > canvas's **original** state — never hardcode a mode).
   >
   > **⚠ Canvas-corruption gotcha (verified — this bug shipped to a prefab):** this snippet
   > mutates the *live* canvas via `unity_execute_code`; if the prefab is later saved, whatever
   > state the canvas is left in gets **baked into the prefab asset**. The `FeatureTemplate` root
   > canvas serializes `m_RenderMode: 1` (Screen Space – Camera) with `m_Camera: {fileID: 0}` —
   > so you MUST capture the original `renderMode`/`worldCamera`/`planeDistance` up front and
   > restore them in `finally`. Restoring to a hardcoded `ScreenSpaceOverlay` (the old bug)
   > silently flips every FeatureTemplate-variant popup to Overlay (DungeonGuide.prefab shipped
   > exactly this defect).
   >
   > **⚠ The live getter lies — do not restore from it (task 111 shipped this to a reviewer).**
   > Because the base's `m_Camera` is null, `Canvas.renderMode` *reads back* as
   > `ScreenSpaceOverlay (0)` even though the asset serializes `1`. So
   > `var orig = cv.renderMode; … cv.renderMode = orig;` is **not** a no-op: it writes `0` and
   > creates a `propertyPath: m_RenderMode` prefab override that differs from the base, which the
   > next `SaveAsPrefabAsset` bakes in. Verify with the **serialized** value
   > (`grep -n "propertyPath: m_RenderMode" <your>.prefab` must find nothing; compare
   > `m_RenderMode` in `FeatureTemplate.prefab`), never with the runtime getter — and after any
   > capture, clear an accidental override:
   >
   > ```csharp
   > var sp = new UnityEditor.SerializedObject(cv).FindProperty("m_RenderMode");
   > UnityEditor.PrefabUtility.RevertPropertyOverride(sp, UnityEditor.InteractionMode.AutomatedAction);
   > ```
   >
   > ```csharp
   > var root = GameObject.Find("<FeatureName>"); var cv = root.GetComponent<Canvas>();
   > // Snapshot the real state BEFORE touching it — this is what we restore to.
   > var origMode = cv.renderMode; var origCam = cv.worldCamera; var origDist = cv.planeDistance;
   > var camGo = new GameObject("__snapCam");
   > try {
   >   var cam = camGo.AddComponent<Camera>();
   >   cam.orthographic = true; cam.clearFlags = CameraClearFlags.SolidColor;
   >   cam.backgroundColor = new Color(0.08f,0.08f,0.12f,1f); cam.cullingMask = 1 << 5;
   >   cv.renderMode = RenderMode.ScreenSpaceCamera; cv.worldCamera = cam; cv.planeDistance = 10f;
   >   Canvas.ForceUpdateCanvases();
   >   var rt = new RenderTexture(1080, 1920, 24); cam.targetTexture = rt; cam.Render();
   >   RenderTexture.active = rt;
   >   var tex = new Texture2D(1080, 1920, TextureFormat.RGB24, false);
   >   tex.ReadPixels(new Rect(0,0,1080,1920), 0, 0); tex.Apply();
   >   System.IO.File.WriteAllBytes("<out>.png", tex.EncodeToPNG());
   >   RenderTexture.active = null; cam.targetTexture = null;
   >   UnityEngine.Object.DestroyImmediate(rt); UnityEngine.Object.DestroyImmediate(tex);
   > } finally {
   >   // Restore the ORIGINAL canvas state — do NOT hardcode Overlay.
   >   cv.renderMode = origMode; cv.worldCamera = origCam; cv.planeDistance = origDist;
   >   UnityEngine.Object.DestroyImmediate(camGo);
   > }
   > ```
   >
   > Related authoring gotchas verified in the same run: a feature root is its own
   > **overlay canvas** — instantiate/reparent it to the **scene root**, not under another
   > canvas; another canvas in the scene (e.g. splash) can cover yours — deactivate it while
   > authoring and restore it in §9 cleanup (never save the scene); non-ASCII text sent
   > through `unity_execute_code` gets mangled — build Vietnamese strings from char codes
   > (`"B" + (char)0x00F9 + ...`) and verify by code point, never by string compare.
2. Read it. Check: element visible, inside its container, anchored correctly, text/icon present,
   not zero-sized, not off-screen, exactly one of `Popup`/`FullScreen` active.
3. Wrong → fix the offending property → screenshot again. **Max 3 rounds per element**; if still
   wrong, report what is off instead of looping.

This per-element loop is the fine-grained mechanic. At the coarser level, `new-ui-guide.md` §3 groups
the whole build into **3 phase checkpoints (A skeleton / B elements / C wiring)** — each phase
ends by either showing the user (interactive) or spawning an independent
[`ui-visual-reviewer`](.claude/agents/ui-visual-reviewer.md) subagent (autonomous `/run-backlog`
runs). The reason: a self-graded "looks fine, moving on" after a 40-50-call build chain is the
actual failure mode behind results not matching intent — the builder is the worst judge of its
own drift. The independent reviewer captures its own screenshot (never trusts yours) and checks
it against the `groundTruth` from `new-ui-guide.md` §0 (a reference image, or a numeric spec-sheet
pulled live from an existing similar prefab).

**Spawning `ui-visual-reviewer` (autonomous mode):**
```
Agent({
  description: "UI visual checkpoint — Phase <A|B|C>",
  subagent_type: "ui-visual-reviewer",
  prompt: `
    port: <unity instance port>
    phase: "<A|B|C>"
    targetPath: "<hierarchy path of the prefab instance root>"
    groundTruth: <reference image path/description, or the Step 0 numeric spec-sheet>
    Task intent: <FeatureName, Popup/Full-screen, Feature/Package branch>
  `
})
```
Read the JSON verdict. `block` → fix each `findings[]` entry, re-stage, re-spawn — max 2 rounds
per phase. `pass` → proceed to the next phase.

> **Edit-time invisibility gotcha:** if the feature template's root plays an open-animation
> (scale 0 → 1) via a transition component, it looks invisible in a static scene. Use
> `unity_play_mode` for a true-to-runtime screenshot, or temporarily set the root
> `RectTransform.m_LocalScale` to `{1,1,1}` for inspection (restore before saving).

---

## 6. Register so `UIManager.Show()` can open it (project convention)

This repo opens features via `UIManager.Instance.Show(EnumBase.Features.X).Forget()`
(see [ui-manager skill](.claude/skills/ui-manager/SKILL.md)). To make a new screen openable:

1. **Enum** — add a member to `EnumBase.Features`. Append; do not renumber existing values.
2. **Controller** — inherit `GameFeatureBaseController` (which adds `FeatureType` and the
   open/close event push). Attach it to the prefab **root** (the template root has no controller
   by default).
3. **Set `FeatureType`** on the controller (TabGroup "Common") to the matching
   `EnumBase.Features` member — required for open/close events and for `FeatureKey` resolution.
4. **Prefab name + location** — `[FeatureName].prefab` (PascalCase, matching the controller +
   folder) in `<featuresRoot>/[FeatureName]/Resources/`.

**Verify open:** `UIManager.Instance.Show(EnumBase.Features.[FeatureName]).Forget()` via
`unity_play_mode` (+ a cheat/menu trigger), then screenshot.

---

## 7. Prefab variant creation (MCP CAN do this — verified)

`unity_asset_create_prefab` **does** produce a true Prefab Variant — the trick is the source
GameObject must be an **instance of the base template**, not a plain GameObject. Verified live:

1. `unity_asset_instantiate_prefab` the base (`FeatureTemplate.prefab` / `PackageTemplate.prefab`)
   into the scene — the scene object is now a prefab *instance* of the base.
2. Assemble on that instance (all the steps above).
3. `unity_asset_create_prefab(gameObjectPath = that instance, savePath = the feature Resources
   path)`. Because the source is a base-template instance, Unity saves the asset as a **Variant**:
   the YAML root is a `PrefabInstance` targeting the base GUID, and the asset's Variant Parent is
   populated — so future base-template edits propagate.
4. **Confirm** with `unity_prefab_info(assetPath)` → expect `prefabType: "Variant"`,
   `isVariant: true`, and `basePrefabPath` = the base template. (`new-ui-guide.md` Steps 2 & 5 require
   this.)

Pitfall: if you build the screen from a **fresh/empty GameObject** instead of instantiating the
base template, `unity_asset_create_prefab` yields a flattened **regular** prefab with no variant
link. The variant relationship comes entirely from the source being a base-template instance — so
always start from an instantiated template, never a blank object.

Save to `<featuresRoot>/[FeatureName]/Resources/[FeatureName].prefab`.

---

## 8. Validate before done (runnable checks — feeds new-ui-guide.md Step 5)

- `unity_gameobject_info` on root → child order intact (`BackgroundButton` 0, `Popup` 1,
  `FullScreen` 2), controller present, exactly one of `Popup`/`FullScreen` active.
- `unity_gameobject_info` on new nodes → none is a direct child of `Popup`/`FullScreen`
  (containment rule); cooldown node, if any, is at `Popup/content/CooldownTime`.
- `unity_component_get_properties` on the controller → `FeatureType`, `_purchase`, `_packIndex`,
  `_cooldownTime` (if time-limited), `ClickBackgroundToExit` all set; no null `[Required]`.
- Same call → `MainUI` is **null** (unless the task explicitly asked otherwise). In YAML:
  `MainUI: {fileID: 0}`. Non-null on a full-screen prefab = wrong open animation (§4).
- `unity_search_missing_references` → no broken references.
- `LocalizesUI.LangKey` set and registered (§3 text / `new-ui-guide.md` §3b).
- `unity_screenshot_game` (or play-mode) → final visual matches intent, captured at the
  §0-pinned 1080×1920 view.
- If any `.cs` was created/edited: `unity_asset_import` (or `unity_execute_menu_item
  "Assets/Refresh"`) → wait for compile via `unity_editor_state` → `unity_get_compilation_errors`
  (severity `error`) → fix (max 2 rounds) before declaring done.

---

## 9. Cleanup

Delete throwaway probe objects with `unity_gameobject_delete` and leave the working scene's
`isDirty` state as you found it (do not save the scene unless the task is about that scene — the
deliverable is the **prefab**).
