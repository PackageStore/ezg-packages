# Unity MCP Playbook — Executable recipes for /create-ui

This is the **executable layer** of `/create-ui`. `SKILL.md` decides *what* to build and
`prefab-templates.md` is the catalog; this file is the deterministic *how* — the exact
Unity MCP tool sequence, property paths, and value formats.

All values below were verified live against this project (Unity 6000.2.6f2, `screen_template`).

> **Always pass `port`** on every `unity_*` call (parallel-safe routing).
> Get it once from `unity_select_instance`. Examples below omit it for brevity.

---

## 0. Preflight (run once per session)

1. `unity_list_instances` → if exactly one, note its `port`; if several, `unity_select_instance`.
2. `unity_scene_info` → note the active scene, whether it `isDirty`, and that a `Canvas` root exists.
   - You will build under that `Canvas`. If none exists, create one (`unity_gameobject_create` with a Canvas) or open a scene that has one.
   - **Never `unity_scene_new`** if the current scene `isDirty` — you would discard unsaved work.
3. Build in the **scene**, verify visually, then save as a **prefab**. Do not author blind.

---

## 1. The core tool loop

Every UI build is the same five-move cycle. Repeat per element.

| Move | Tool | Purpose |
|------|------|---------|
| **Place** | `unity_asset_instantiate_prefab` | Drop a template into the scene (under the right parent). |
| **Parent** | `unity_gameobject_reparent` | Move it into the intended container (e.g. scroll `Content`). |
| **Shape** | `unity_component_set_property` | Set RectTransform / Image / Text values (see §3). |
| **Wire** | `unity_component_get_referenceable` → `unity_component_batch_wire` | Connect serialized references (see §4). |
| **See** | `unity_screenshot_game` | Look at the result, then correct. Never declare done blind (see §5). |

---

## 2. `instantiate` value formats

`unity_asset_instantiate_prefab`:
- `prefabPath` = **asset path**, e.g. `Assets/_Project/Visual/ArtAsset/Shared/Resources/Prefabs/Templates/Templates/ItemElement.prefab`
- `parent` = hierarchy path of the parent GameObject, e.g. `Canvas/screen_my_feature/popup_template/popup_container/container_content`
- Returns `instanceId` (a string like `"-16310"`). **Keep it** — it is the most reliable handle for `reparent`, `get_referenceable`, and `batch_wire` (names collide; instanceIds don't).

Reparent with `unity_gameobject_reparent` (keep `worldPositionStays:false` semantics for UI — set local rect after).

---

## 3. Property cheatsheet (exact `m_*` names + value JSON)

`unity_component_set_property` takes `gameObjectPath` (or use the instanceId via `unity_gameobject_info` first), `componentType`, `propertyName`, `value`.

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

**Anchor recipes** (set `m_AnchorMin` + `m_AnchorMax`):
- Stretch full-parent: min `{0,0}` max `{1,1}`, then `m_SizeDelta {0,0}` and `m_AnchoredPosition {0,0}`.
- Centered fixed-size: min `{0.5,0.5}` max `{0.5,0.5}`, then `m_SizeDelta` = width/height.
- Top-anchored: min `{0.5,1}` max `{0.5,1}`, pivot `{0.5,1}`, `m_AnchoredPosition {0,-margin}`.

> Order matters: set anchors **before** `m_SizeDelta`/`m_AnchoredPosition`, because changing
> anchors reinterprets those values.

### Image (`componentType: "Image"`)

| propertyName | type | value JSON |
|---|---|---|
| `m_Color` | Color | `{"r":1,"g":1,"b":1,"a":1}` (0–1 range) |
| `m_Sprite` | ObjectReference | `"Assets/.../icon.png"` (asset path) or `{"assetPath":"..."}` |
| `m_RaycastTarget` | bool | `true` / `false` |
| `m_Type` | Enum | `"Simple"` / `"Sliced"` / `"Filled"` |
| `m_FillAmount` | float | `0.65` |
| `m_PreserveAspect` | bool | `true` |

To assign a sprite: first `unity_search_assets` (`type:"Texture2D"` or `"Sprite"`) to get the
asset path, then set `m_Sprite` to that path string.

### Text / TextMeshProUGUI

- Legacy `Text` (`componentType:"Text"`): `m_Text` (string), `m_FontData` nested — prefer setting `m_Text` only.
- TMP (`componentType:"TextMeshProUGUI"`): `m_text` (string), `m_fontColor` (Color), `m_fontSize` (float).
- If unsure of a component's exact field names, call `unity_component_get_properties` on it
  **first** — it returns every `m_*` name + current value. Cheaper than guessing.

### Value-format rules (verified)
- Vector2 → `{"x":..,"y":..}` · Vector3 → `+ "z"` · Color → `{"r","g","b","a"}` 0–1 · Quaternion → `+ "w"`.
- ObjectReference → asset-path string, scene-object name string, `null` to clear, or
  `{assetPath}` / `{instanceId}` / `{gameObject, componentType}`.

---

## 4. Wiring serialized references (the part that usually breaks)

A built screen renders but its buttons/tabs are dead until references are wired.

1. **Discover targets** — `unity_component_get_referenceable` with the controller's
   `componentType`, the target `path`/`instanceId`, and the `propertyName`. It returns the
   scene objects/assets assignable to that field.
2. **Assign** — `unity_component_batch_wire` with one entry per reference. Each entry:
   - `path` or `instanceId` = the GameObject holding the component to set
   - `componentType` = the component (e.g. `ScreenMyFeatureController`)
   - `propertyName` = the serialized field (e.g. `_closeButtons`, `MainUI`)
   - `referenceGameObject` / `referenceInstanceId` = the object to assign
   - For lists (e.g. `_closeButtons`), wire each element; confirm with `get_properties` after.

Single reference shortcut: `unity_component_set_property` with an ObjectReference value
`{gameObject:"...", componentType:"Button"}`.

---

## 5. Visual verify loop (mandatory — do not skip)

UI built blind via property writes is wrong more often than right. After each meaningful
chunk:

1. `unity_screenshot_game` (optionally `superSize:2`).
2. Read the screenshot. Check: element visible, inside its container, anchored correctly,
   text/icon present, not zero-sized, not off-screen.
3. If wrong → adjust the offending property → screenshot again. **Max 3 correction rounds**
   per element; if still wrong, report what is off instead of looping forever.

> **Edit-time invisibility gotcha (root Canvas only — harmless at runtime, but ship it at `{1,1,1}` anyway):**
> `screen_template`'s root ships at `localScale (0,0,0)` (verified: root RectTransform,
> `screen_template.prefab` fileID `6025426740827259555`). In a static scene/prefab view this
> looks invisible. **This is harmless at actual runtime** — `FeatureBaseController.Awake()` /
> `UITransition.PlayOpen()` never touch the root's own scale (they only animate `MainUI` =
> child `[1]`), and a root `Canvas` in Screen Space – Overlay mode ignores its own Transform
> scale for rendering. Confirmed: the shipped, in-production `screen_change_name.prefab`
> variant does **not** override this value either, and it renders fine at runtime regardless.
> **Even so, always explicitly set the root `RectTransform` `m_LocalScale` to `{1,1,1}` before
> saving.** Leaving it at `{0,0,0}` is a real workflow trap even though it's runtime-safe: every
> later open of the prefab (Project preview thumbnail, Scene view, a teammate double-clicking
> it, or you reviewing your own work later) shows a blank/invisible screen with no visual cue
> why — easy to mistake for a broken prefab. There is no runtime reason to leave it at 0, so
> don't: restore `{1,1,1}` as the last step before every save. Prefer `unity_play_mode` for a
> true-to-runtime screenshot regardless.
>
> **This exemption is ONLY for the root Canvas GameObject.** Every other RectTransform in the
> hierarchy (`popup_template`, `full_screen_template`, any child widget/prefab you zero out to
> isolate and inspect) is a normal, rendered UI element — nothing restores its scale at
> runtime. If you temporarily set a non-root element's scale to `{0,0,0}` to inspect it in
> isolation, you **must** explicitly restore it (usually `{1,1,1}`) before saving the prefab,
> or that element ships permanently invisible with no self-healing mechanism. This is the most
> likely way `/create-ui` produces an invisible-but-otherwise-correct element — verify via an
> actual `unity_play_mode` screenshot, not just an edit-time scene glance, before declaring an
> element done.

---

## 6. `screen_template` ground truth (verified structure)

Root `screen_template` carries: `Canvas`, `CanvasScaler`, `GraphicRaycaster`, `UITransition`,
`CanvasGroup`, `CanvasRenderer` — **but NO controller component**. You add the feature's own
`FeatureBaseController` subclass yourself.

Child order (FeatureBaseController depends on it — see §7):

```
screen_template (root: Canvas + UITransition + CanvasGroup, NO controller)
├─ [0] background_button          ← MainBackground (Image) + dim Button (ClickBackgroundToExit)
├─ [1] popup_template             ← MainUI fallback (transform.GetChild(1)) · ships ACTIVE
│   └─ popup_container              (VerticalLayoutGroup + ContentSizeFitter + LayoutElement)
│       ├─ BG
│       ├─ button_tooltips
│       ├─ container_content       ← popup body goes here
│       ├─ button_container
│       └─ top_container_popup
│           └─ button_close        ← ships ACTIVE on screen_template — see §6b
└─ [2] full_screen_template       ← ships INACTIVE — see §6a
    ├─ bg_fullscreen
    ├─ _bg_Pattern_FullScreen_Normal
    ├─ content                    ← full-screen body goes here
    ├─ top_view
    └─ botview
        └─ button_close            ← ships ACTIVE — separate close button, see §6c/§6d
```

### 6a. Popup vs full-screen — decide before you build

`popup_template` and `full_screen_template` are siblings under the root; exactly **one** must
stay active on the shipped prefab (deactivate the other via `unity_gameobject_set_active`).
Full-screen itself splits into two sub-cases with opposite close-button treatment — see §6c/§6d.

- **Popup** (default) — a dismissable feature layered on top of whatever screen is behind it:
  confirm dialogs, reward popups, upgrade/shop panels, settings. Keep `popup_template` active
  (its shipped default), set `full_screen_template` inactive. Build under
  `popup_template/popup_container/container_content`.
- **Full screen — navigation page** (§6c): the feature *is* a dedicated page the player
  navigates to and can back out of (Gear, Talent Tree, a shop tab, etc.).
  `SetActive(true)` on `full_screen_template`, `SetActive(false)` on `popup_template`. Build
  under `full_screen_template/content` (`top_view`/`botview` are extra docked slots;
  `bg_fullscreen`/`_bg_Pattern_FullScreen_Normal` are the backdrop layers). Wire
  `botview/button_close` as the back affordance.
- **Full screen — persistent HUD/shell** (§6d): a screen-level HUD or host shell that is never
  individually dismissed (combat HUD, tab bar, the two-screen shell itself). Same container
  setup as above, but deactivate `botview/button_close` — there is nothing to "close".
- Default to popup. Only switch to full screen when the request explicitly calls for a
  full-screen feature or a screen-level HUD, and pick §6c vs §6d based on whether the player
  ever backs out of it.

### 6b. Popup only — wire the close button

`popup_template/popup_container/top_container_popup/button_close` ships **already active** on
`screen_template` — but an active button is not a wired one. `FeatureBaseController.Awake` only
hooks `onClick` for buttons present in `_closeButtons`; an active-but-unwired `button_close` is a
dead button that looks clickable and does nothing (verified as the single most common bug across
existing screens — see the popup/full-screen audit). For every popup-type screen:
1. Confirm `button_close` is active (it should already be, from the template).
2. Wire it into the controller's `_closeButtons` array (`unity_component_batch_wire`, §4). Do not
   assume this happened automatically — check with `unity_component_get_properties` after.
3. Set the controller's `ClickBackgroundToExit` = `true` (`unity_component_set_property`).

Full-screen features skip this specific button — `popup_template` is entirely inactive for them,
so its `button_close` never renders regardless of wiring. Full-screen has its **own**, separate
close button under `full_screen_template/botview/button_close` — see §6c/§6d.

### 6c. Full-screen navigation page — wire the botview close button

`full_screen_template/botview/button_close` ships **already active**, independent from the
popup's `button_close` (different object, different parent). For a full-screen page the player
can back out of (Gear, Talent Tree, a shop tab, etc.):
1. Confirm `botview/button_close` is active.
2. Wire it into the controller's `_closeButtons` array (`unity_component_batch_wire`, §4) — same
   dead-button trap as §6b: active ≠ wired.
3. Leave `ClickBackgroundToExit` = `false` — there is no meaningful "background" to tap away from
   in full screen.

### 6d. Full-screen persistent HUD/shell — deactivate the botview close button

For a screen-level HUD or host shell with no dismiss semantics (combat HUD, training HUD, the
meta tab bar, the two-screen shell host, or a screen hosted inside that shell): deactivate
`full_screen_template/botview/button_close` (`unity_gameobject_set_active` → `false`) and leave
`_closeButtons` empty. These screens are switched away from or always-on, never individually
closed by the player.

### 6e. `_closeWithBackey` — `true` by default, `false` for §6d persistent HUD/shell

`FeatureBaseController._closeWithBackey` (private field, default `true`) gates
`CloseWithBackKey()` — the hardware/gesture Android back key closing the screen. Set (or
explicitly confirm) `_closeWithBackey = true` on every new screen's controller regardless of
popup vs full-screen — this is independent of the popup/full-screen and close-button decisions
above.

**Rule, tied directly to §6d, not a separate ad-hoc list:** any screen classified as full-screen
**persistent HUD/shell** (§6d — "switched away from or always-on, never individually closed by
the player") must set `_closeWithBackey = false`. §6d already deactivates the close button and
never wires `ClickBackgroundToExit`, for exactly one reason — this screen is not meant to be
individually dismissed at all; leaving the back key as a stray fourth dismiss path contradicts
that same reasoning. A screen tied to a Scene's persistent/root content (a Scene-level HUD or
shell, not an overlay page you navigate to and back out of) falls in
this bucket by definition — decide via §6a/§6d, not by guessing per-field.

For every other screen — popup (§6b) or full-screen **navigation page** (§6c) — leave
`_closeWithBackey = true`. Only deviate from that with a documented, specific reason recorded in
the task spec; don't leave it off silently as a one-off guess.

Typical §6d screens, all `_closeWithBackey = false`:
- persistent Scene shell content — screens a shell-host controller cross-fades via `CanvasGroup`
  and never routes through the standard `CloseMe()`/`UIManager.CloseFeature` flow; a back-key
  close here would blank the shell with nothing to return to.
- a combat/gameplay HUD tied to the active Scene run — back-key must not blank the HUD mid-run.
- a tutorial/FTUE mask or overlay — back-key must not let the player skip it.

Build the screen body under the container chosen in §6a. Reuse child templates from
`Prefabs/Templates/Templates/` (see `prefab-templates.md`).

---

## 7. Register a feature screen (so `UIManager.Show()` can open it)

A screen is not usable until all four hold. `UIManager.Show()` loads by
`screen_{FeatureType.ToString().ToSnakeCase()}` from any `Resources/` folder
(`FEATURE_PATH = "{0}"`, loaded via Resources path = the bare name).

1. **Enum** — add an entry to `GameEnums.Features`
   (`Assets/_Project/Features/_Shared/Config/GameEnums.cs`). Append; do not renumber existing
   values (they are persisted / bundle-mapped).
2. **Prefab name + location** — name the prefab `screen_<snake_case>` matching the enum via
   `ToSnakeCase` (e.g. `MyNewScreen` → `screen_my_new_screen.prefab`) and place it in the
   feature's `Resources/` folder: `Assets/_Project/Features/<Category>/<Feature>/Resources/`.
   `ToSnakeCase` = lower-case + `_` before each interior uppercase
   (`Features/_Shared/Systems/Utils.cs`).
3. **Controller** — a C# class inheriting `FeatureBaseController`, attached to the prefab root.
   Override `LoadData()` / `LoadData(object data)` for content; keep namespace
   `Ezg.Feature.<Category>.<Feature>` (match neighbors).
4. **Child-order contract** — `FeatureBaseController.Awake` uses
   `transform.GetChild(0)` as `MainBackground` (must have an `Image`; needs a `Button` if
   `ClickBackgroundToExit`) and `transform.GetChild(1)` as `MainUI` (when `MainUI` is unset).
   Keep `background_button` first and `popup_template` second, **or** explicitly assign
   `MainUI` and verify the background reference.
   - **Full-screen screens must explicitly assign `MainUI` to `full_screen_template`.** Child
     order never changes (`popup_template` stays index `[1]`, `full_screen_template` stays `[2]`
     regardless of which is active) — the null-fallback `transform.GetChild(1)` always resolves
     to `popup_template`, which is wrong (and inactive) for a full-screen screen. Leaving `MainUI`
     unset on a full-screen variant silently breaks `UITransition.PlayOpen`/`PlayClose` targeting.

**Verify open:** after building + saving, confirm via
`UIManager.Instance.Show(GameEnums.Features.MyNewScreen, ...)` (e.g. through `unity_play_mode`
+ a cheat/menu trigger), then screenshot.

---

## 8. Creating the prefab / prefab-variant (MCP CAN make a variant — verified)

`unity_asset_create_prefab` **does** produce a true Prefab Variant of `screen_template` — the
requirement is that the source GameObject is an **instance of `screen_template`**, not a fresh
object. Verified live (`unity_prefab_info` → `prefabType: "Variant"`, `isVariant: true`,
`basePrefabPath` = `screen_template`; YAML root is a `PrefabInstance` targeting the base GUID):

1. `unity_asset_instantiate_prefab` `screen_template.prefab` into the scene (now a base instance).
2. Assemble the screen on that instance (all steps above).
3. `unity_asset_create_prefab(gameObjectPath = that instance, savePath =
   Assets/_Project/Features/<Category>/<Feature>/Resources/screen_<snake>.prefab)` → saved as a
   **variant** with the live link preserved (future `screen_template` edits propagate).
4. Confirm with `unity_prefab_info(assetPath)` → expect `isVariant: true`.

Pitfall: building from a **fresh/empty GameObject** instead of an instantiated `screen_template`
yields a flattened **regular** prefab (no variant link) — the Guardrail's "detached duplicate".
Always start from an instantiated template.

For pure child widgets (no FeatureBaseController), a plain prefab from the assembled GameObject is
fine.

---

## 9. Validate before declaring done (runnable checks)

- `unity_gameobject_info` on the root → confirm child order (`background_button` [0],
  `popup_template` [1]) and that the controller component is present.
- `unity_component_get_properties` on the controller → `FeatureType` set, `_closeButtons`
  populated, `MainUI` consistent.
- Exactly one of `popup_template` / `full_screen_template` is active — never both, never
  neither (§6a).
- Popup screens: `popup_template/.../button_close` active, present in `_closeButtons`, and
  `ClickBackgroundToExit = true` (§6b).
- Full-screen navigation pages: `MainUI` explicitly set to `full_screen_template` (§7 step 4),
  and `full_screen_template/botview/button_close` active + present in `_closeButtons` (§6c).
- Full-screen persistent HUD/shell: `MainUI` explicitly set to `full_screen_template`, and
  `full_screen_template/botview/button_close` deactivated with `_closeButtons` empty (§6d).
- `_closeWithBackey`: `true` for popup (§6b) and full-screen navigation page (§6c); `false` for
  full-screen persistent HUD/shell (§6d) — same reasoning as the deactivated close button (§6e).
- `unity_search_missing_references` → no broken references introduced.
- No non-root element left at `m_LocalScale (0,0,0)` from edit-time inspection (§5) — the
  root-Canvas exemption does not extend to children; if you zeroed any child to isolate it,
  confirm it was restored.
- `unity_screenshot_game` (or play-mode) → final visual matches intent.
- If any `.cs` was created/edited, run `/compile-check` (Unity MCP recompile) before done.

---

## 10. Cleanup

If you instantiated throwaway probes in the scene, `unity_gameobject_delete` them and leave the
scene `isDirty` state as you found it (do not save the working scene unless the task is about
that scene).
