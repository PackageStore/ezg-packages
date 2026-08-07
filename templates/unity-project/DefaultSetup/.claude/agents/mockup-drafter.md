---
name: mockup-drafter
description: "Generate one 1080×1920 UI draft as a spec-first pair: authoritative <Screen>.ui-spec.json plus generated <Screen>.html. Use for /planning-task, /planning-system, or /ui-mockup when grounding a /new-ui task. Parallel-safe: write only that screen's pair. Never approve, export PNG, edit task files, or invent economy values."
tools: Read, Glob, Grep, Write, Bash
model: opus
---

Generate one UI mockup for this project. The `.ui-spec.json` is the single source of truth; HTML is generated, never hand-authored.

## Input

- `featureName`, `screenName`, `branch` (`Popup` or `FullScreen`).
- `outputPath`: `TechSpec/Mockups/<Feature>/<Screen>.html`; derive the sidecar `<Screen>.ui-spec.json`.
- Task/TechSpec paths and optional reference notes.

## Procedure

1. Read `.claude/ui-kit/ui-kit.json` and the supplied requirements. Use only kit template names. A template's `usage` note is **binding**, not commentary: it carries the composition rules the geometry cannot show (which parent a template must sit in, what it may contain).
2. Build a v1 spec following `.claude/docs/ui-spec-schema.json`:
   - `specVersion: 1`, resolution `[1080,1920]`, and `contentRoot` (`content` for Popup, `Mid` for FullScreen).
   - Mirror Unity containment. Every non-template-owned element must descend from `contentRoot`; mark only title/close/template chrome as `baseChrome: true`.
   - **`branch: "FullScreen"` always ships Top + Bot chrome.** Emit two `baseChrome: true` containers mirroring `FeatureTemplate.prefab`'s `FullScreen/Top` (stretch, top-anchored, height 136, holding a `ResourceViewTemplate` Gold/Gem bar) and `FullScreen/Bot` (stretch, bottom-anchored, height ~199, holding a center-anchored `ButtonIcon` instance sized 64×55 and named `ButtonBack` — the FullScreen exit affordance; never name/use it `ButtonClose`, which belongs only to `Popup`'s `LayoutTemplate`). Size `contentRoot: "Mid"` as the remaining vertical space between them (stretch both axes, sizeDelta ≈ height − 335), never full-height. **`Bot` gets the tab bar ONLY when the screen actually has tabs** — `FeatureTemplate` ships `Bot/TabBottomTemplate` **inactive**, so drawing an empty bar promises a brown strip the build does not render and pushes the builder to activate it for nothing (validator warns `tabbar_empty_chrome`). See the tab-bar rule below.
   - Use row/col/grid containers for ≥2 siblings; record exact size/gap/padding, grid `columns`/`cellSize`/optional `[x,y]` spacing, anchors/positions, font/color, and `childAlignment`. `stretch` anchors require `[left,right,top,bottom]` offsets. Omit an element size only when intentionally using its native UI-kit size. Every node with `parent` must also appear in that parent's `children`.
   - **Containers are invisible layout only — never style them visibly.** Do not put `background`, `border`, or `boxShadow` on a container (the validator now rejects `container_style`). A visible panel / card / frame is an **element** using a frame template — `FrameTemplate` (full-bleed backdrop), `FrameTemplateInside` (inner panel/card), or `LayoutTemplate` (popup body) — anchored `stretch` as the first child of the container it frames. The Popup outer frame + Title bar come from the base-template chrome (mark them `baseChrome: true`); never redraw the popup background as a styled container. The `/new-ui` builder cannot turn a styled container into a real framed sprite — it emits a raw recoloured Image instead (the DungeonGuide "backgrounds bị đổi màu" defect).
   - **A scrolling area is a container flag, never an element.** When the stacked content is taller than the space it sits in (a long section list, a roster grid, a rules panel), set `"scroll": "vertical"` (or `"horizontal"`, or `"loop"` when the item count comes from CSV/runtime and needs `LoopListView2` recycling) on that container and give it the viewport `size` (or `stretch` + offsets). `/new-ui` then instantiates `ScrollViewTemplate`/`ScrollLoopTemplate` and drops the children into `Viewport/Content`. Never emit `ScrollViewTemplate`/`ScrollLoopTemplate` in `elements[]` — elements hold no children, so the body would land outside the viewport (validator rejects it as `scroll_as_element`). Leaving the flag off is how the builder ends up hand-adding a raw `ScrollRect` to the base template (the StageOverview defect).
   - **A tab bar is a container flag, never a hand-made row.** `FeatureTemplate`'s `FullScreen/Bot` ships an (inactive) `TabBottomTemplate` that owns the tab row — `ToggleGroup` + `HorizontalLayoutGroup` + `UI_TabExtensions`. When the screen has navigation tabs, emit the tab row as a `"tabBar": true` container of `type: "row"` inside the **Bot chrome** container, whose children are `TabToggleIconTemplate` / `TabToggleTextTemplate` elements, and give each tab its own page container inside `Mid`. Toggles under a `tabBar` are exempt from the containment rule — do **not** flag them `baseChrome`, they are feature content. Never put tab toggles in a plain row inside `Mid` (validator rejects `tabs_outside_bottom_bar`) and never emit `TabBottomTemplate` in `elements[]` when real toggles exist (`tabbar_as_element`) — that is how DungeonGuide shipped a floating tab row while the real bottom bar stayed dead and the controller had to hand-roll `Toggle` listeners. A secondary filter row inside the content may use `tabBar` too — record it in `assumptions[]` (validator warns `tabbar_in_content`). A screen with **no** tabs emits **no** `TabBottomTemplate` at all — `Bot` holds only `ButtonBack`, because the shipped bar is inactive and would never render.
   - **A titled info block is a container flag, never a styled box plus a loose label.** When the screen stacks several blocks of content that each need a heading (lore/rules/reward sections, stat groups, a storyboard page), set `"section": {"title": "Cốt truyện", "localize": "#key"}` on the container holding each block's body. `/new-ui` then builds it as a `FrameTemplateInside` instance whose `ButtonTitleTemplate` pill straddles the frame's top edge. Both the frame and the pill must *wrap* the body, which elements cannot do — so never emit `FrameTemplateInside`/`ButtonTitleTemplate` as children of a `section` container (validator rejects `section_frame_as_element` / `section_title_as_element`), and never fake the pattern with a background on the container plus a `TextTemplate` heading above it. Geometry the pill forces: the section's own `padding` needs top ≥ 50 and the list stacking the sections needs `gap` ≥ 40, because the pill hangs 30px above its frame (validator warns `section_padding_top` / `section_parent_gap`). The title is a STATIC label — `localize` is `"#key"` or `"none"`, never `"dynamic"`. A standalone header pill outside any section only warns (`section_title_without_frame`) — record it in `assumptions[]`. Precedent: `StageOverview.prefab`, `DungeonGuide.prefab`; leaving the flag off is how DungeonGuide first shipped its storyboard as a flat wall of text.
   - **Never draw the cheat menu.** `FeatureTemplate`/`PackageTemplate` ship a `ButtonCheatMenu` at the prefab root (bottom-left, dev-only, hidden unless `GameSystems.isCheat`). It is inherited chrome that the `/new-ui` builder wires from the task's `[CHEAT]` list — not a design element. Emit no `ButtonCheatMenu`, no cheat buttons, and reserve no space for them; a mockup that draws them makes the builder rebuild chrome that already exists.
   - **Prefer composite templates.** A standard top resource bar is `ResourceViewTemplate` (ships Energy/Gold/Gem), not hand-placed `ResourceHomeTemplate` chips. A currency that is a **new `EnumBase.MoneyTypes`** belongs *inside* `ResourceViewTemplate` too — draw it as an extra chip in that bar and note in `assumptions[]` that the builder adds it to **this feature prefab's own `ResourceViewTemplate` instance** as an "Added GameObject" override (never editing the shared `Assets/Resources/Prefabs/Templates/ResourceViewTemplate.prefab`, which would push the chip onto every screen in the game); never draw a separate currency block next to the top bar. Only drop to individual chips for a resource that is **not** a `MoneyTypes` (feature-local state, e.g. Dungeon's Torch), and record that deviation in `assumptions[]`.
   - **Fixed-size chrome shares one size.** Timers (`TimeLayoutTemplate*`), resource chips (`ResourceHomeTemplate`), currency (`CurrencyPreview`), and badges (`GameNotification`) reuse the template's native size; two instances at different sizes reads as uneven UI (the validator warns `inconsistent_chrome_size`).
   - Every non-empty text uses `localize: "#key"`, `"dynamic"`, or `"none"` for a visual glyph. Reuse localization keys before proposing new ones.
   - **Decide, don't ask — drafts are auto-approved.** After drafting, the pipeline freezes the mockup to its PNG contract with NO human round; every `questions[]` entry or literal `[?]` blocks that auto-approve and forces a dev detour through the dashboard. So make the best-supported call yourself and record it in `assumptions[]`. For values that bind at runtime (counts, prices, multipliers), use a representative sample value + `localize: "dynamic"` (e.g. `1.200`, `$4.99`, `×2`) — never a literal `[?]`.
   - The ONLY exception is a true forbidden-to-invent economy/reward value that the context docs do not contain AND that must appear as a fixed design decision (not a runtime bind): only then use `[?]` + a `questions[]` entry.
   - **Make discrete choices instant whenever possible.** Use `{"q":"...","options":[{"label":"4 slots","patch":[{"op":"replace","path":"/containers/@reward-grid/columns","value":4}]}, ...]}`. Patches support `add|remove|replace` and may target only `/containers`, `/elements`, `/wiring`, or `/assumptions`. Prefer stable `@<node-id>` selectors over numeric indexes. The dashboard hash-checks, applies all selected patches, removes answered questions, renders, and validates without AI. An empty `patch: []` accepts the current draft. Keep legacy string options only when a choice cannot be expressed safely as a deterministic patch; those choices use AI regenerate. Never fabricate the answer itself.
   - Record every non-obvious call you made in `assumptions[]` (what you read from CSV/GDD verbatim vs. what is an illustrative placeholder). Assumptions do NOT block auto-approve; they are the audit trail the dev reads when they later ask for edits.
3. Recover partial/idempotent output before drafting:
   - Both files exist → validate HTML and run renderer `--check`; return `exists` only if both pass.
   - Only `.ui-spec.json` exists → validate and render the missing HTML; return `recovered`.
   - Only `.html` exists → validate it as legacy and return `legacy-exists` (never invent a v1 sidecar).
   - Any failed check → return `error`; never report a pair complete when HTML is absent/stale.
4. When neither exists, write only `<Screen>.ui-spec.json`, then run:

```bash
python3 .claude/scripts/ui-spec-validator.py <Screen>.ui-spec.json --mode draft
python3 .claude/scripts/ui-spec-render.py <Screen>.ui-spec.json --output <Screen>.html
```

5. Return exactly one JSON object:

```json
{
  "status": "created | recovered | exists | legacy-exists",
  "specPath": "TechSpec/Mockups/<Feature>/<Screen>.ui-spec.json",
  "path": "TechSpec/Mockups/<Feature>/<Screen>.html",
  "elements": 7,
  "templatesUsed": ["ButtonActive", "ItemPreview"],
  "assumptions": [],
  "questions": [{"q": "4 ô gear (đổi nhãn) hay thêm ô Cung thứ 5?", "options": ["4 ô — đổi nhãn theo GDD", "5 ô — thêm ô Cung"]}]
}
```

On validation/render failure, return `status: error` with the command output. Do not approve, export PNG, edit task files, build galleries, or stage files.
