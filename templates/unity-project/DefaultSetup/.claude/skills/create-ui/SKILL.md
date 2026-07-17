---
name: create-ui
description: Build, extend, and refactor Unity UI with the shared template prefabs under `Assets/_Project/Visual/ArtAsset/Shared/Resources/Prefabs/Templates`. Use when Codex needs to create a new feature screen or popup that inherits from `FeatureBaseController`, create a prefab variant from `Popup_Template/screen_template`, or assemble reusable UI blocks such as tabs, lists, sliders, input fields, item or currency previews, resource headers, text boxes, and trigger cards through Unity MCP.
---

# Create UI

## Overview

Use this skill to choose an existing shared prefab template, instantiate it with Unity MCP, and preserve the prefab's built-in layout groups, controllers, and serialized references.

**Resolve UI elements by token first when the current project has exported a catalog.** If `ui-catalog/ui-tokens.json` exists, treat it as the UGUI SSOT and use its exact `resourcesPath`, `controller`, and `layout`; do not guess paths. If the catalog is absent (normal in a fresh generated project), use the reference tables plus real prefab discovery under `Assets/`, and do not create or copy a catalog from another game. Export the project-specific catalog before using the spec-first mockup lane.

**Ground truth (mockup contract) — check BEFORE building.** When this skill runs for a `/new-ui` backlog task, the task's `**Workflow args:**` carries a `groundTruth=` value (mockup pipeline — see `.claude/commands/ui-mockup.md`):
- `TechSpec/Mockups/<F>/<S>.png` — an **approved, frozen visual contract** (1080×2400). `Read` the PNG *and* its sibling `<S>.ui-spec.json` first; the spec's containers/elements/template ids (ui-catalog tokens) ARE the build plan — element positions/sizes/fonts come from there, not from taste. Spawn the [`ui-visual-reviewer`](../../agents/ui-visual-reviewer.md) agent at phase checkpoints (A skeleton / B elements / C wiring, max 2 fix rounds per phase) to independently compare the live build against the PNG.
  - **Rebuilding an EXISTING screen (redesign) — structural diff first, do NOT default to the recolor shortcut.** When the screen already ships (prefab + controller exist), diff the existing prefab hierarchy against the ui-spec's containers/elements *before* touching anything. If the ui-spec introduces, moves, or removes any container/element the prefab lacks, you MUST **rebuild the body node-by-node from the ui-spec** (each container → RectTransform, each element → its token prefab at the spec'd size/anchor), then re-bind. Recoloring the old prefab in place is a defect because it silently keeps the old layout. The "mostly visual → duplicate + restyle" shortcut below is legal ONLY when that structural diff is empty. Preserve the existing `FeatureBaseController` subclass + every serialized reference across the rebuild (`unity_search_missing_references` before/after = 0).
- `clone:<ExistingPrefab>` — copy that prefab's layout. Resolve it via `ui-catalog/ui-tokens.json` when available, otherwise require one unambiguous prefab match under `Assets/`; no mockup exists.
- `PENDING-MOCKUP` / `PENDING-APPROVAL:*` — the visual contract is NOT approved yet: do not build; route through `/ui-mockup` first (building a screen from a text description alone is the documented main visual-failure mode).
- No `groundTruth` at all (ad-hoc request outside the backlog) — proceed, but screenshot-verify extra carefully (step 7 below).

Read [references/prefab-templates.md](references/prefab-templates.md) before editing when the request depends on choosing a template or understanding a prefab's hierarchy and runtime behavior.

For the **executable layer** — exact Unity MCP tool sequence, property paths (`m_*`), value formats, reference wiring, the screenshot verify loop, and screen registration — follow [references/mcp-playbook.md](references/mcp-playbook.md). This skill decides *what* to build; the playbook is the deterministic *how*. Do not improvise MCP commands when the playbook covers the operation.

## Workflow

1. Decide whether the request is for a root feature screen or a reusable child prefab.
2. If the root object should inherit from `FeatureBaseController`, start from `Popup_Template/screen_template` as a prefab variant.
3. If the request is only for a reusable block inside a screen, choose the closest prefab from `Templates/Templates`.
4. Assemble the UI by reusing existing prefabs before creating raw GameObjects, following the MCP tool loop in [references/mcp-playbook.md](references/mcp-playbook.md) §1.
5. Change only the safe surfaces first: text, icon sprites, anchored position, size delta, spacing, padding, colors, child activation, and serialized lists intended for configuration. Use the exact `m_*` property paths and value formats in the playbook §3.
6. Wire serialized references (close buttons, `MainUI`, tab toggles) per playbook §4 — a screen renders but stays non-functional until references are wired.
7. **Screenshot and self-correct** (`unity_screenshot_game`) after each meaningful chunk — playbook §5. Do not declare the UI done without looking at it; building blind is the main failure mode.
8. Validate the result in hierarchy terms: correct parent, correct anchors, no broken references, dynamic children placed in the intended container, and root feature fields still consistent with `FeatureBaseController` (runnable checks in playbook §9).

## Feature Screen Workflow

Apply this workflow when the new root prefab is a feature screen or popup and the controller inherits from `FeatureBaseController`.

1. Create or reuse the feature folder under `Assets/_Project/Features/<Category>/<FeatureName>/` (`<Category>` is one of `Meta`, `Social`, `System`, `Monetization`, `Events`, `Gameplay`, `Onboarding`).
2. Put the screen prefab in `Assets/_Project/Features/<Category>/<FeatureName>/Resources/`.
3. Create a prefab variant from `Assets/_Project/Visual/ArtAsset/Shared/Resources/Prefabs/Templates/Popup_Template/screen_template.prefab`. `unity_asset_create_prefab` **does** produce a true variant as long as the source is an instantiated `screen_template` instance (verified) — see playbook §8. Do not build from a blank GameObject, or you get a detached regular prefab.
4. Name the prefab consistently with the feature. In this repo, common screen names are `screen_<snake_case>.prefab`, but follow the existing naming already used by that feature folder when present.
5. **Add the feature's `FeatureBaseController` subclass to the root** — `screen_template` ships with NO controller component (verified: root has only `Canvas`/`CanvasScaler`/`GraphicRaycaster`/`UITransition`/`CanvasGroup`). The controller must be created and attached.
6. **Decide popup vs full-screen, then enable the matching container.** Root child `[0] = background_button` (Image + optional Button) stays as-is. Children `[1] = popup_template` and `[2] = full_screen_template` are siblings — `popup_template` ships **active**, `full_screen_template` ships **inactive**; exactly one must stay active on the shipped prefab. Full-screen splits into two sub-cases with opposite close-button treatment (step 7):
   - **Popup** (default) — a dismissable feature layered over whatever screen is behind it: confirm dialogs, reward popups, upgrade/shop panels, settings. Keep `popup_template` active, `full_screen_template` inactive (its shipped default — usually nothing to change). Build the body under `popup_template/popup_container/container_content`.
   - **Full screen — navigation page** — a dedicated page the player navigates to and can back out of (inventory, progression, or a shop tab, for example). `SetActive(true)` on `full_screen_template` and `SetActive(false)` on `popup_template`. Build the body under `full_screen_template/content` (`top_view`/`botview` are extra docked slots; `bg_fullscreen`/background pattern are the backdrop layers).
   - **Full screen — persistent HUD/shell** — a screen-level HUD or host shell that's never individually dismissed (combat HUD, tab bar, a two-screen shell host or the screens it hosts). Same container setup, but there is no back affordance to wire.
   - Default to popup unless the request explicitly calls for a full-screen feature or HUD. **Explicitly assign `MainUI` to `full_screen_template` for both full-screen sub-cases** — child order never changes, so the null-fallback `transform.GetChild(1)` always resolves to `popup_template` (wrong and inactive) regardless of which container is actually active.
7. **Wire (or deactivate) the close button — different button per container.** `popup_template` and `full_screen_template` each ship their **own**, separate `button_close` — wiring one has no effect on the other.
   - **Popup:** `popup_template/popup_container/top_container_popup/button_close` ships already active. Add it to `_closeButtons` and set `ClickBackgroundToExit = true`.
   - **Full screen — navigation page:** `full_screen_template/botview/button_close` ships already active. Add it to `_closeButtons` as the back affordance. Leave `ClickBackgroundToExit = false` (no background to tap away from in full screen).
   - **Full screen — persistent HUD/shell:** deactivate `full_screen_template/botview/button_close` and leave `_closeButtons` empty.
   - In every case: active ≠ wired. `FeatureBaseController.Awake` only hooks buttons listed in `_closeButtons`, so an active-but-unwired `button_close` is a dead button that looks clickable and isn't — this was the single most common bug found auditing existing screens.
8. **Set `_closeWithBackey` on the controller — tied to the §6 classification, not a separate guess.** This gates the Android hardware/gesture back key (`FeatureBaseController.CloseWithBackKey`). Default to `true` for popups and full-screen navigation pages. Set `false` only for a scene-level persistent HUD/shell that is never individually dismissed — the same classification that deactivates the close button in step 7.
9. Build the content of the screen by reusing child templates from `Templates/Templates` and other existing prefabs already used by nearby features.
10. **Register the screen** so `UIManager.Show()` can open it (playbook §7): add a `GameEnums.Features` entry, name the prefab `screen_<snake_case>` matching the enum via `ToSnakeCase`, and attach the controller. `UIManager.Show()` loads `screen_{feature.ToSnakeCase()}` from any `Resources/` folder.
11. Reopen the prefab and verify controller references, close buttons, and transition behavior; then verify it actually opens via `UIManager.Show(...)` (play-mode + screenshot) before considering the work done.
12. If any `.cs` file was created or edited (the controller), run `/compile-check` before reporting done.

## FeatureBaseController Rules

- `FeatureBaseController` expects the root object to carry the screen controller component.
- If `MainUI` is left null, `FeatureBaseController` falls back to `transform.GetChild(1)`.
- `FeatureBaseController` also reads the background image from `transform.GetChild(0)`.
- Because of that fallback, do not reorder the first children of the root variant casually.
- If the root child order changes, explicitly set `MainUI` and verify background access still points to the intended overlay object.
- Preserve `UITransition`, `Canvas`, `CanvasGroup`, and other base screen infrastructure that already exists on `screen_template`.
- Review `FeatureType`, `ClickBackgroundToExit`, `_closeButtons`, `_backgroundAlpha`, and time-scale flags on every new feature screen.
- Popup and full-screen each have their **own** `button_close` (`popup_template/.../top_container_popup/button_close` vs `full_screen_template/botview/button_close`) — wiring one does not affect the other. Popup: `_closeButtons` includes its `button_close`, `ClickBackgroundToExit = true`. Full-screen navigation page: `_closeButtons` includes `botview/button_close`, `ClickBackgroundToExit = false`. Full-screen persistent HUD/shell: `botview/button_close` deactivated, `_closeButtons` empty (Feature Screen Workflow §6–§7).
- `_closeWithBackey` defaults to `true` (popup and full-screen navigation pages). Set it `false` only for full-screen persistent HUD/shell screens (Feature Screen Workflow §6 third bullet / §8, playbook §6d–§6e) — the same screens that already skip the close button and background-tap-to-exit.

## Template Selection

- Use `FrameTemplate` for a plain framed panel background.
- Use `textbox_template` for a framed text surface with built-in content text.
- Use `InputField01_Basic_White_NormalText` for editable text entry.
- Use `ScrollViewTemplate` for any scrollable container. Add dynamic content under `Viewport/Content`, not under the root.
- Use `SliderTemplate` for a read-only progress or fill bar. It ships as a non-interactable slider with fill only.
- Use `tab_template` (drives sibling content panels via `UI_TabExtensions`) as the tab-strip container and `toggle_tab_template` as the tab button unit — only when all tabs switch content *within the same screen*. A tab/nav bar whose buttons each open a separate `FeatureBaseController` screen (e.g. the persistent meta tab bar) is a launcher, not a panel-switcher: wire plain `Button.onClick` per tab instead, see `references/prefab-templates.md` → `tab_template`.
- Use `CurrencyPreview` for one currency row and `CurrenciesPreview` for a runtime-generated row of multiple currencies.
- Use `ItemElement` for one item or reward cell and `ItemPreview` for a runtime-generated strip of multiple item cells.
- Use `ItemElementRewardPopup` when the visual should feel like a reward popup, not an inventory tile.
- Use `ResourceViewer` for a live resource header chip with icon, value, and add button.
- Use `TriggerTemplate` as a source gallery of trigger-card variants. Duplicate a single child variant instead of dropping the whole gallery into production UI.

## Guardrails

- Do not create a new feature screen by plain file duplication when the root should stay linked to `screen_template`. Use a prefab variant.
- Do not use `screen_template` as a child widget. It is for root feature screens only.
- Do not leave `popup_template` and `full_screen_template` both active, or both inactive. Exactly one drives the visible body (Feature Screen Workflow §6).
- Do not remove `CurrencyPreviewController`, `ItemPreviewController`, `ItemElementController`, `ResourceItemViewController`, `UI_TabExtensions`, `UI_ButtonExtensions`, or `ShowingObjectController` unless the user explicitly wants a behavior rewrite.
- Do not remove `FeatureBaseController` or `UITransition` from root feature screens unless the user explicitly wants to replace the screen lifecycle.
- Do not replace a prefab with raw primitives when a matching shared template already exists.
- Do not move dynamic content to the wrong level. Example: keep scroll items under `ScrollViewTemplate/Viewport/Content`.
- Do not clear serialized references on controller components. Most of these prefabs depend on inspector wiring.
- Preserve nested prefab instances and override only what the task needs.
- Preserve unknown or third-party components on `TriggerTemplate` variants. They include animation and display behavior that is easy to break by stripping components.

## Unity MCP Notes

- For the full command sequence, property paths, value formats, and the screenshot verify loop, follow [references/mcp-playbook.md](references/mcp-playbook.md). The notes below are high-level reminders only.
- Prefer instantiating by the `Resources` path `Prefabs/Templates/Templates/<PrefabName>`.
- For feature-screen roots, use the template at `Prefabs/Templates/Popup_Template/screen_template`.
- After instantiation, rename the instance for scene clarity only if it does not break code that relies on child-name lookup.
- When wiring tabs, update both the toggle children and the `UI_TabExtensions` serialized lists so the selected toggle and target content panels stay in sync.
- When wiring list-style prefabs, keep the template child reference intact and let the controller spawn the runtime children.
- When the request is mostly visual, duplicate the closest prefab and override styling instead of composing a new structure from zero. **Exception — redesign against a mockup:** if a `groundTruth` ui-spec is present and its containers/elements differ from the existing prefab, do NOT take this shortcut; rebuild the body from the ui-spec node-by-node (see the "Rebuilding an EXISTING screen" guard in the Ground truth section above).
- Validate against nearby feature folders in the current repo before inventing a new folder or naming pattern.

## Reference Catalog

Use [references/prefab-templates.md](references/prefab-templates.md) as the authoritative catalog for:

- root-screen template workflow
- prefab purpose
- `Resources` path
- root size and anchor behavior
- important children
- built-in Unity UI components
- custom controllers and why they matter
- prefab-specific usage advice
