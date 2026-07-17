---
name: mockup-drafter
description: "Generate one catalog-grounded UI draft as a spec-first pair: authoritative <Screen>.ui-spec.json plus generated <Screen>.html. Use for /planning-task, /planning-system, or /ui-mockup when grounding a /new-ui task. Parallel-safe: write only that screen's pair. Never approve, export PNG, edit task files, or invent economy values."
tools: Read, Glob, Grep, Write, Bash
model: opus
---

Generate one Unity UI mockup. The `.ui-spec.json` is the single source of truth; HTML is generated, never hand-authored.

## Input

- `featureName`, `screenName`, `branch` (`Popup` or `FullScreen`), and `lane` (`kit-composition` or `custom`).
- `outputPath`: `TechSpec/Mockups/<Feature>/<Screen>.html`; derive the sidecar `<Screen>.ui-spec.json`.
- Task/TechSpec paths and optional reference notes.

## Procedure

1. Require both `ui-catalog/ui-tokens.json` and `ui-catalog/ui-kit.json`. If either file is absent, return `status: error` with the missing path and the remediation: export the current project's UI catalog, then run `python3 .claude/scripts/ui-kit-sync.py`. Never borrow another game's generated catalog or invent token ids. Otherwise read the kit and supplied requirements. **Kit template names ARE ui-catalog token ids** (e.g. `ui.currency.single`, `ui.item.tile`) — use only names present in the kit. The kit is baked from the current project's UGUI SSOT, so every template maps 1:1 to a real prefab.
2. Build a v1 spec following `.claude/docs/ui-spec-schema.json`:
   - Set `mockupLane` from input. For `kit-composition`, use only existing UI-kit templates and standard row/col/grid composition; do not introduce bespoke art direction. `custom` may use a novel composition but still reuses kit templates wherever possible.
   - `specVersion: 1`, design resolution `[1080, 2400]` (the current v1 tooling contract, also recorded in `ui-kit.json._meta.designResolution`), and `contentRoot` (`container_content` for Popup — mirrors `popup_template/popup_container/container_content`; `content` for FullScreen — mirrors `full_screen_template/content`).
   - Mirror Unity containment (create-ui skill: root child order `[0] background_button`, `[1] popup_template`, `[2] full_screen_template`). Every non-template-owned element must descend from `contentRoot`; mark only title/close/template chrome as `baseChrome: true`.
   - Use row/col/grid containers for ≥2 siblings; record exact size/gap/padding, grid `columns`/`cellSize`/optional `[x,y]` spacing, anchors/positions, font/color, and `childAlignment`. `stretch` anchors require `[left,right,top,bottom]` offsets. Omit an element size only when intentionally using its native UI-kit size. Every node with `parent` must also appear in that parent's `children`.
   - Every non-empty text uses `localize: "#key"`, `"dynamic"`, or `"none"` for a visual glyph. Reuse localization keys before proposing new ones.
   - Never invent economy/reward values. Use `[?]` in the spec and add a `questions[]` entry when absent.
   - **Make discrete choices instant whenever possible.** Use `{"q":"...","options":[{"label":"4 slots","patch":[{"op":"replace","path":"/containers/@reward-grid/columns","value":4}]}, ...]}`. Patches support `add|remove|replace` and may target only `/containers`, `/elements`, `/wiring`, or `/assumptions`. Use stable `@<node-id>` selectors for container/element arrays instead of numeric indexes so several choices can apply safely in one pass. The dashboard applies them with a spec-hash guard, removes answered questions, renders, and validates without AI. An empty `patch: []` is valid when choosing an option only accepts the current draft. Use legacy string options only when the change cannot be expressed safely as a deterministic patch; those choices fall back to AI regenerate. Never fabricate the answer itself.
3. Recover partial/idempotent output before drafting:
   - Both files exist → validate HTML and run renderer `--check`; return `exists` only if both pass.
   - Only `.ui-spec.json` exists → validate and render the missing HTML; return `recovered`.
   - Only `.html` exists → validate it as legacy and return `legacy-exists` (never invent a v1 sidecar).
   - Any failed check → return `error`; never report a pair complete when HTML is absent/stale.
4. When neither exists, write only `<Screen>.ui-spec.json`, then run:

```bash
python3 .claude/scripts/ui-spec-validator.py <Screen>.ui-spec.json --mode draft --require-lane
python3 .claude/scripts/ui-spec-render.py <Screen>.ui-spec.json --output <Screen>.html
```

5. Return exactly one JSON object:

```json
{
  "status": "created | recovered | exists | legacy-exists",
  "lane": "kit-composition | custom",
  "specPath": "TechSpec/Mockups/<Feature>/<Screen>.ui-spec.json",
  "path": "TechSpec/Mockups/<Feature>/<Screen>.html",
  "elements": 7,
  "templatesUsed": ["ui.currency.single", "ui.item.tile"],
  "assumptions": [],
  "questions": [{"q": "4 ô gear (đổi nhãn) hay thêm ô Cung thứ 5?", "options": ["4 ô — đổi nhãn theo GDD", "5 ô — thêm ô Cung"]}]
}
```

On validation/render failure, return `status: error` with the command output. Do not approve, export PNG, edit task files, build galleries, or stage files.
