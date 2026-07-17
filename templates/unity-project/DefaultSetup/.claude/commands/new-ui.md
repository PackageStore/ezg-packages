---
description: Create a UI screen/popup prefab from the current Unity project's shared templates
---

# Create New UI Workflow

When the user runs `/new-ui [FeatureName]` (or requests a new UI prefab):

This workflow is a thin entry point — the executable detail lives in the **`create-ui` skill** (`.agents/skills/create-ui/SKILL.md` + its `references/prefab-templates.md` and `references/mcp-playbook.md`). Invoke that skill and follow its playbook. Do NOT improvise Unity MCP commands the playbook already covers.

**STEP 0 — ground truth:** args may carry ` | groundTruth=<value>` after the FeatureName (mockup pipeline — `.claude/commands/ui-mockup.md`). An approved `TechSpec/Mockups/<F>/<S>.png` (+ sibling `.ui-spec.json`) is the frozen visual contract the build must match — read it first and follow the create-ui skill's "Ground truth" section (including `ui-visual-reviewer` phase checkpoints). `clone:<Prefab>` → copy that prefab's layout. `PENDING-*` → STOP: the mockup is not approved; run `/ui-mockup` first.

## Summary of what `create-ui` does

1. **Decide**: root feature screen vs reusable child block.
2. **Root screen** (controller inherits `FeatureBaseController`): create a **prefab variant** of `Assets/_Project/Visual/ArtAsset/Shared/Resources/Prefabs/Templates/Popup_Template/screen_template.prefab`.
   - Place it in `Assets/_Project/Features/<Domain>/[FeatureName]/Resources/`, named `screen_<snake_case>.prefab`.
   - `screen_template` ships with NO controller — create and attach the `[FeatureName]Controller : FeatureBaseController` to the root.
   - Preserve root child order: `[0] = background_button`, `[1] = popup_template` (MainUI). Build content under `popup_template/popup_container/container_content`.
3. **Reusable blocks**: pick the closest prefab from `Templates/Templates` (buttons, tabs, lists, sliders, input fields, item/currency previews, resource headers, text boxes) — reuse before creating raw GameObjects.
4. **Register the screen**: add a `GameEnums.Features` entry; prefab name `screen_<snake_case>` must match the enum via `ToSnakeCase` so `UIManager.Instance.Show()` can load it from any `Resources/`.
5. **Screenshot + self-correct** (`unity_screenshot_game`) after each meaningful chunk — never declare UI done without looking at it.
6. **Wire serialized references** (close buttons, `MainUI`, tab toggles); review `FeatureType`, `ClickBackgroundToExit`, `_closeButtons`, `_backgroundAlpha`, time-scale flags.
7. If any `.cs` file was created/edited (the controller), run `/compile-check`.

> Use `UIManager.Instance.Show(GameEnums.Features.[FeatureName], data).Forget()` to open the screen. Never `gameObject.SetActive` on a feature screen.
