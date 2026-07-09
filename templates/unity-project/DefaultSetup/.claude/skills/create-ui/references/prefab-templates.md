# Shared UI Prefab Templates

## Base Location

- Root templates folder: `Assets/_Project/Visual/ArtAsset/Shared/Resources/Prefabs/Templates`
- Reusable block folder: `Assets/_Project/Visual/ArtAsset/Shared/Resources/Prefabs/Templates/Templates`
- Feature-screen template folder: `Assets/_Project/Visual/ArtAsset/Shared/Resources/Prefabs/Templates/Popup_Template`
- Reusable block `Resources` path: `Prefabs/Templates/Templates/<PrefabName>`
- Feature-screen `Resources` path: `Prefabs/Templates/Popup_Template/<PrefabName>`

## Current Project Validation

- Root feature controllers in this repo do inherit from `FeatureBaseController`.
- A real example is `Assets/_Project/Features/Social/ChangeName/Scripts/Controller/ScreenChangeNameController.cs`.
- A real screen prefab example is `Assets/_Project/Features/Social/ChangeName/Resources/screen_change_name.prefab`.
- `screen_change_name.prefab` is a prefab variant of `screen_template.prefab` with source guid `56fe2a4fb9f5141488c53da9e7819aa1`.
- Feature folders in this repo use `Assets/_Project/Features/<Category>/<FeatureName>/Resources/` (`<Category>` is one of `Meta`, `Social`, `System`, `Monetization`, `Events`, `Gameplay`, `Onboarding`), not the BlazeSurvivor `_Game/2.BUS/...` path.
- Screen prefab names commonly use the `screen_<snake_case>.prefab` pattern, but there are feature-specific exceptions. Check the local folder before naming a new prefab.

## Selection Cheatsheet

| Need | Preferred prefab |
| --- | --- |
| Root feature screen or popup with `FeatureBaseController` | `screen_template` variant |
| Single currency row | `CurrencyPreview` |
| Multiple currencies in one row | `CurrenciesPreview` |
| Single item or reward tile | `ItemElement` |
| Reward-popup styled item tile | `ItemElementRewardPopup` |
| Dynamic row of items | `ItemPreview` |
| Live resource header chip | `ResourceViewer` |
| Simple framed panel | `FrameTemplate` |
| Read-only progress bar | `SliderTemplate` |
| Scrollable content area | `ScrollViewTemplate` |
| Tab strip container | `tab_template` |
| Single tab button | `toggle_tab_template` |
| Text display box | `textbox_template` |
| Editable text field | `InputField01_Basic_White_NormalText` |
| Trigger or shop card variant | `TriggerTemplate` |

## Prefabs

### `screen_template`

- Resource path: `Prefabs/Templates/Popup_Template/screen_template`
- Purpose: Base root prefab for screens and popups whose root controller inherits from `FeatureBaseController`.
- Use it for: New feature screens, popups, modal views, and similar top-level UI features managed by `UIManager`.
- Do not use it for: Small reusable widgets, list items, buttons, tabs, or content blocks inside another screen.
- Project-validated workflow:
  - Create the feature screen as a prefab variant, not a detached duplicate.
  - Store the variant in `Assets/_Project/Features/<Category>/<FeatureName>/Resources/`.
  - Bind the feature controller that inherits from `FeatureBaseController`.
  - Reuse inner building blocks from `Templates/Templates` and existing neighboring feature prefabs.
- Important runtime rule:
  - `FeatureBaseController` falls back to `transform.GetChild(1)` for `MainUI` when `MainUI` is null.
  - `FeatureBaseController` also uses `transform.GetChild(0)` as the main background image.
  - Because of that, preserve the first-child background and second-child main-content relationship, or explicitly reassign `MainUI` and verify the background reference path still works.
- Built-in behavior:
  - Includes `UITransition` for open/close animation.
  - Includes the base canvas infrastructure needed by `FeatureBaseController`.
  - Supports `ClickBackgroundToExit`, `_closeButtons`, `FeatureType`, sorting-layer assignment, and animated open/close flow through the inherited controller logic.
- Two body containers ship side by side as children `[1]`/`[2]` of the root — pick exactly one, deactivate the other (see `mcp-playbook.md` §6a–§6d for the full decision + wiring recipe). **Each container ships its own, separate `button_close`** — wiring one has no effect on the other:
  - `popup_template` (ships **active**) — for dismissable popups/panels. Body goes under `popup_container/container_content`. Its `popup_container/top_container_popup/button_close` ships **already active**, but that only makes it visible — `FeatureBaseController.Awake` wires listeners only for buttons in `_closeButtons`, so wire it in there and set `ClickBackgroundToExit = true` whenever the root is a popup-type screen, or the X renders but does nothing when tapped.
  - `full_screen_template` (ships **inactive**) — for a feature that *is* the screen, or a persistent screen-level HUD. Body goes under `content`; `top_view`/`botview` are extra docked slots; `bg_fullscreen`/`_bg_Pattern_FullScreen_Normal` are the backdrop layers. Its `botview/button_close` also ships **already active**:
    - Navigation page (player backs out of it) → wire `botview/button_close` into `_closeButtons`, keep `ClickBackgroundToExit = false`.
    - Persistent HUD/shell (never individually dismissed) → deactivate `botview/button_close`, keep `_closeButtons` empty.
  - Explicitly assign `MainUI` to `full_screen_template` in both full-screen cases — child order never changes, so the null-fallback (`transform.GetChild(1)`) always resolves to `popup_template`, which is wrong and inactive here.
  - Default to `popup_template` unless the request explicitly calls for a full-screen feature or HUD.
- Validation checklist after creating a variant:
  - Root still has the feature controller and `UITransition`.
  - `FeatureType` is set correctly.
  - Exactly one of `popup_template` / `full_screen_template` is active.
  - Popup: its `button_close` active, wired into `_closeButtons`, `ClickBackgroundToExit = true`.
  - Full-screen navigation page: `MainUI` set to `full_screen_template`; `botview/button_close` active + wired into `_closeButtons`; `ClickBackgroundToExit = false`.
  - Full-screen persistent HUD/shell: `MainUI` set to `full_screen_template`; `botview/button_close` deactivated; `_closeButtons` empty.
  - The prefab opens and closes without missing references.

### `CurrenciesPreview`

- Resource path: `Prefabs/Templates/Templates/CurrenciesPreview`
- Purpose: Container that renders multiple currency entries at runtime through `CurrencyPreviewController`.
- Root layout: `RectTransform` centered, height `60`, width auto-fit.
- Components: `CurrencyPreviewController`, `HorizontalLayoutGroup`, `ContentSizeFitter`, `ShowingObjectController`.
- Key behavior: `_currencyTemplate` is wired to a separate currency-entry prefab, so the controller can spawn one entry per resource.
- Use it for: Prices or rewards with 2+ currencies, such as coin + gem costs.
- Avoid: Manually placing child entries unless the request explicitly wants a static layout.

### `CurrencyPreview`

- Resource path: `Prefabs/Templates/Templates/CurrencyPreview`
- Purpose: Single currency row with icon and value text.
- Root layout: `RectTransform` centered, height `60`, width auto-fit.
- Key hierarchy: root `CurrencyPreview` with explicit child `Icon` plus a nested text object used for the value label.
- Components: `CurrencyPreviewController`, `HorizontalLayoutGroup`, `ContentSizeFitter`, `ShowingObjectController`, `LayoutElement`, `Image`, `Text`.
- Controller wiring: `_icon` points to the icon image, `_value` points to the value text.
- Use it for: One cost, one reward amount, or any compact icon+number row.
- Notes: The root animates in with scale through `ShowingObjectController`.

### `FrameTemplate`

- Resource path: `Prefabs/Templates/Templates/FrameTemplate`
- Purpose: Plain framed background panel.
- Root layout: centered `600 x 250`.
- Components: single `Image` only.
- Use it for: Popup body, header panel, framed module background, or card base when no controller logic is needed.
- Notes: This is the safest prefab to resize freely because it carries no logic component.

### `InputField01_Basic_White_NormalText`

- Resource path: `Prefabs/Templates/Templates/InputField01_Basic_White_NormalText`
- Purpose: White input field shell with built-in text area and masking.
- Root layout: centered `689.84 x 120.6`.
- Key hierarchy: `BgWhite2`, `BgWhite3`, `Text Area`.
- Components: `InputField`, `Image`, `LayoutElement`, `RectMask2D`, `Text`.
- Use it for: Name entry, note entry, search field, or form input that should match the project's white rounded style.
- Notes: Edit text area sizing and font settings carefully; do not break the root `InputField` references.

### `ItemElement`

- Resource path: `Prefabs/Templates/Templates/ItemElement`
- Purpose: Single item tile with icon, quantity, optional borders, level badge, checked state, and click behavior.
- Root layout: centered `200 x 200`.
- Key hierarchy: direct children include `Mask`, `SkillBorder`, `ItemBorder`, `LevelGroup`, `QuantityGroup`. Additional nested objects include `ItemBackground`, `ItemIcon`, `SkillBackground`, `Empty`.
- Components: `ItemElementController`, `Button`, `UI_ButtonExtensions`, `LayoutElement`, `Image`, `Text`, `Mask`, `HorizontalLayoutGroup`, `ContentSizeFitter`, `UIShadow`.
- Controller wiring: serialized refs cover icon, quantity text, button, notif icon, borders, checked object, and level widgets.
- Use it for: Inventory cell, reward slot, cost item, collectible tile, or clickable item preview.
- Notes: This prefab already supports view-only mode, remaining quantity display, click action, notification badge, and checked state through `ItemElementController`.

### `ItemElementRewardPopup`

- Resource path: `Prefabs/Templates/Templates/ItemElementRewardPopup`
- Purpose: Reward-popup variant of `ItemElement` with stronger presentation.
- Structure: Prefab variant of `ItemElement` plus extra visual treatment such as `glow`.
- Visual differences: Larger emphasis on quantity text, popup-oriented positioning, and some inherited components disabled compared with the base tile.
- Use it for: Reward reveals, completion popups, and celebratory item drops.
- Avoid: Using it as a generic inventory tile. Use `ItemElement` for everyday list/grid UI.

### `ItemPreview`

- Resource path: `Prefabs/Templates/Templates/ItemPreview`
- Purpose: Runtime-generated horizontal list of `ItemElement` entries.
- Root layout: centered, height `170.1689`, width auto-fit.
- Components: `ItemPreviewController`, `HorizontalLayoutGroup`, `ContentSizeFitter`, `ShowingObjectController`.
- Controller wiring: `_itemTemplate` points to the `ItemElement` prefab.
- Use it for: Reward rows, item bundles, shop contents, or small dynamic item strips.
- Notes: Let `ItemPreviewController` instantiate children. Do not replace it with manual children unless necessary.

### `ResourceViewer`

- Resource path: `Prefabs/Templates/Templates/ResourceViewer`
- Purpose: Live resource chip that shows current amount of one currency and exposes a plus button.
- Root layout: anchored at `0,0` with zero `sizeDelta`, so its final size comes from the parent layout or explicit sizing.
- Key hierarchy: `Bg`, `Icon`, `Button_Add`, plus a nested text object for the numeric value.
- Components: `ResourceItemViewController`, `Button`, `UI_ButtonExtensions`, `Image`, `Text`.
- Controller behavior: listens for `UpdateResource` events, refreshes icon/value automatically, and currently shows a placeholder message on click.
- Use it for: Top bar currencies, resource headers, or compact wallet chips.
- Notes: `_currencyType` is serialized on the prefab instance; duplicate and change that enum for other currencies.

### `ScrollViewTemplate`

- Resource path: `Prefabs/Templates/Templates/ScrollViewTemplate`
- Purpose: Generic scroll view shell.
- Root layout: full stretch from `(0,0)` to `(1,1)`.
- Key hierarchy: `Viewport` -> `Content`.
- Components: `ScrollRect`, transparent root `Image`, viewport `Image`, viewport `Mask`.
- Content setup: `Content` is top-stretched with default height `300`.
- Use it for: Scrollable lists, shop entries, inventories, mail lists, or any dynamic vertical content area.
- Critical rule: Put generated children under `Viewport/Content`, never under the scroll root or viewport root.

### `SliderTemplate`

- Resource path: `Prefabs/Templates/Templates/SliderTemplate`
- Purpose: Fill-bar style slider used more like a progress bar than an interactive slider.
- Root layout: centered `1080 x 56`.
- Key hierarchy: `Bg`, `FillArea`, `Fill`.
- Components: `Slider`, `Image`, `Mask`.
- Default state: `Slider.m_Interactable = 0`, no handle rect, current sample value around `0.647`.
- Use it for: Progress bars, loading bars, upgrade progress, energy fill, or XP progress.
- Notes: If the request needs actual dragging input, add a handle and explicitly reconfigure the slider instead of assuming this template is ready for interactive use.

### `tab_template`

- Resource path: `Prefabs/Templates/Templates/tab_template`
- Purpose: Container for a row of tab toggles plus tab-page switching logic.
- Root layout: anchored top-left, `960 x 159.7496`.
- Key hierarchy: `background` plus a nested `toggle_tab_template` instance.
- Components: `UI_TabExtensions`, `ToggleGroup`, `HorizontalLayoutGroup`, `LayoutElement`, `Image`.
- Controller behavior: `UI_TabExtensions` expects serialized toggle and content-object lists, then turns content panels on or off when the selected toggle changes.
- Use it for: Tabs across popup sections, store categories, progression pages, or settings pages — **all tabs live inside the same screen** and swap sibling content panels in place.
- Do **not** use it for: a tab/nav bar whose buttons each open a *different* `FeatureBaseController` screen via `UIManager.Show(...)` (e.g. `MetaTabBarController`, the persistent bottom meta tab bar). That's a launcher, not a panel-switcher — wire plain `Button.onClick` per tab instead (see the "Launcher-style tab bar" note below). `UI_TabExtensions` only makes sense when every tab's content already lives under the same root as siblings.
- Critical rule: When adding tabs, update both the toggle instances and the `UI_TabExtensions` lists. The layout alone is not enough.

**`UI_TabExtensions` field/API reference** (`Assets/_Project/Features/_Shared/UI/UI_TabExtensions.cs`, namespace `Ezg.Core.Extensions`):

| Serialized field | Type | Purpose |
|---|---|---|
| `_mainCanvasScale` | `CanvasScaler` | Reference resolution used to compute the slide distance for `_useAnimSwap`. |
| `_toggleList` | `List<Toggle>` | One entry per tab button, in tab order. |
| `_objectList` | `List<GameObject>` | One content panel per tab, index-aligned with `_toggleList`. |
| `_useListObjects` | `bool` | Switch to multi-panel-per-tab mode (uses `_objectLists` instead of `_objectList`). |
| `_objectLists` | `List<TabListObject>` | Only when `_useListObjects=true`; each entry wraps `ObjectList: List<GameObject>` — one tab can activate several objects at once. Mutually exclusive with `_useAnimSwap`. |
| `_indexOnOpen` | `int` (default `-1`) | Tab auto-selected in `OnEnable` via `SetTabIndex`; `-1` = don't force a tab. |
| `_useAnimSwap` | `bool` | Slide-transition between panels via DOTween instead of instant `SetActive` toggling. |

Public API: `RegisterOnchangeAction(int index, UnityAction action)` (hook a callback per tab index — call this from the owning controller, do not edit the component's private fields directly), `SetTabIndex(int index)` / `JumpToIndex(int slot)` (programmatic tab switch), `GetIndexSelected()`, `GetToggleList()`, `GetObjectList()`.

Usage patterns:
- A screen controller keeps `public UI_TabExtensions MainTab;` as a serialized reference on the owning screen controller and drives the sibling content panels through it.
- Wire per-tab reactions with `_functionTab.RegisterOnchangeAction(0, () => _tabIndex = 0);` (one call per tab index), then branch logic later off the stored `_tabIndex`.
- Prefab consumer: `tab_template.prefab` itself (plus any feature screen that embeds a tab strip).

MCP wiring recipe (same mechanics as playbook §4): `unity_component_batch_wire` with `componentType: "UI_TabExtensions"`, `propertyName: "_toggleList"` (one entry per `Toggle` instance) and a second pass with `propertyName: "_objectList"` (one entry per content panel, same order). Confirm alignment with `unity_component_get_properties` afterward — a mismatched index silently shows the wrong panel for a tab.

### `textbox_template`

- Resource path: `Prefabs/Templates/Templates/textbox_template`
- Purpose: Styled text box with a ready-made nested text object.
- Root layout: centered `466.8269 x 130.4062`.
- Key hierarchy: root image plus nested `content_text` prefab instance stretched to fill the box.
- Components: root `Image`; nested content provides the text label.
- Default styling: nested text is overridden to black with font size `34`.
- Use it for: Static captions, dialogue snippets, helper text, or label panels.
- Notes: This is a display container, not an editable input field.

### `toggle_tab_template`

- Resource path: `Prefabs/Templates/Templates/toggle_tab_template`
- Purpose: One selectable tab button.
- Root layout: anchored at `(0,0)`, `300 x 200`.
- Key hierarchy: `ImgOff`, `ImgOn`, plus nested text/icon content inside `ImgOn`.
- Components: `Toggle`, root transparent `Image`, child `Image`s.
- Behavior: uses the standard Unity `Toggle` state to swap between off and on visuals.
- Use it for: Child unit inside `tab_template`.
- Notes: Rewire any persistent events or linked content objects on cloned instances so the toggle controls the intended panel.

### `TriggerTemplate`

- Resource path: `Prefabs/Templates/Templates/TriggerTemplate`
- Purpose: Gallery of trigger-card and shop-card variants rather than a single production-ready card.
- Root structure: `TriggerTemplate` with many first-level variants including `ShowFeature`, `Employee`, `OpenShop`, `ViewAds`, `Shelf`, `BuyCheckout`, `BuyShelf`, `BuyParkingSlot`, `Expand`, `Lock`, `Lock_ParkingSlot`, `Buy_shop`, `Bus stop`, `FreeResouce`, `ProductAds`.
- Components across variants: `TextMeshPro`, `DOTweenAnimation`, images, and additional serialized components that should be preserved even if their type is not obvious from YAML alone.
- Use it for: Choosing one existing trigger-card composition as the starting point for a shop CTA, unlock card, ad-view card, or feature teaser.
- Preferred workflow: duplicate the single variant child that matches the request, then edit that variant.
- Avoid: Instantiating the entire root gallery into the final UI unless the goal is to showcase multiple trigger types at once.
