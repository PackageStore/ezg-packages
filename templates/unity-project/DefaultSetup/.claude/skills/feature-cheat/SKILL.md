---
name: feature-cheat
description: Add a dev cheat menu to a feature — Cheat_* methods on the controller/service plus buttons wired under a CheatMenu instantiated into the feature prefab. Use whenever building or extending a feature that has state a tester would otherwise have to grind for (timers, daily resets, currency, inventory, progression, unlock flags). Reference implementation shipped by this template - Features/System/GameCheat.
---

# Feature Cheat — dev affordance for a feature

A cheat menu is three layers: **chrome** (the toggle button + its container), **buttons**, and **code**.
This template ships the chrome and the button prefabs; you supply the buttons' labels and the
`Cheat_*` methods.

> ⚠️ **The chrome is NOT inherited here.** `screen_template.prefab` — the base every feature screen is a
> variant of — has exactly two children (`background_button`, `full_screen_template`) and **no cheat
> menu**. You must instantiate `CheatMenu.prefab` into the feature prefab yourself. (If you have seen a
> project where `ButtonCheatMenu` came for free from a `FeatureTemplate`/`PackageTemplate`, that is a
> different codebase — those prefabs do not exist in this template.)

**Everything this skill names ships in the template. Verify before you trust a path** — `Glob` the
prefab or `unity_asset_*` it. If something is missing, say so rather than improvising a substitute.

---

## 1. When to add a cheat (the decision, not a reflex)

Add cheats when the feature has state a tester **cannot reach in a few taps**:

- **Time-gated** — daily/weekly reset, countdown, cooldown, streak, seasonal cycle → jump time.
- **Progression-gated** — level/rank/stage/unlock threshold, quest step, milestone → jump state.
- **Resource-gated** — needs currency/items/tickets the tester does not have → grant/reset.
- **Long/rare flow** — pity counter, streak break, N-th claim, one-shot first-time popup → force it.
- **Destructive to verify** — anything you must be able to **reset** to re-test (claim flags, seen
  flags, purchase state).

**Skip** (and say so explicitly) when the feature is purely presentational (a static info popup, a
settings toggle, a list view over existing data) or already fully drivable from its own UI in under
~3 taps. A cheat that duplicates a normal button is noise.

Rule of thumb: **one cheat per completion criterion you could not otherwise verify in the Editor.**
The verify steps in the task spec are the checklist — if a step reads "wait until tomorrow" or "reach
level 50", that step wants a cheat.

---

## 2. Layer 1 — the chrome

`Assets/_Project/Visual/ArtAsset/Shared/Resources/Prefabs/Templates/CheatMenu.prefab`
(guid `ec2aa73aad0a4ea4ab74e7da72c63287`):

```
CheatMenu              [RectTransform, Image, Button,
                        CheatMenuController, GameCheatObjectController]
└── Menu               [RectTransform + layout group]   ← your cheat buttons go here
    ├── Add Point      ┐ sample buttons shipped with the prefab —
    └── Remove Point   ┘ repurpose or delete them, never ship them untouched
```

Instantiate it as a child of the feature prefab root, next to `full_screen_template`. Do not rebuild
the hierarchy by hand and do not move the two components off the root — `CheatMenuController` is
`[RequireComponent(typeof(Button))]` and drives the child assigned to its `_targetMenu` field.

Both scripts live in `Assets/_Project/Features/System/GameCheat/Scripts/`
(namespace `Ezg.Feature.System.GameCheat`):

- **`CheatMenuController`** — `ToggleMenu()` on the root Button expands/collapses `_targetMenu` by
  animating its scale. Inspector knobs: `_targetMenu` (must point at `Menu`), animation axis (`X`/`Y`),
  expand direction (`LeftToRight` / `RightToLeft` / `TopToBottom` / `BottomToTop`), duration. Keep the
  prefab's values unless the menu would run off-screen from where you placed it.
- **`GameCheatObjectController`** — the visibility gate: `gameObject.SetActive(GameSystems.isCheat)`,
  re-evaluated on `EventName.CheatChanged` / `EventName.CheatUpdateUI`, and hidden/shown by
  `EventName.CheatHideUIUA` / `EventName.CheatShowUIUA` (used to clear the screen for UA capture). It
  adds a `CanvasGroup` if the object has none. `IsOnlyEditor` restricts it further to Editor builds —
  leave it as the prefab ships it unless the task says otherwise.

`GameSystems.isCheat` (`_Shared/Systems/GameSystems.cs`, `public static bool`) is resolved from remote
config at boot. **Because the gate lives on the chrome, cheat code does NOT need `#if UNITY_EDITOR`** —
wrapping it is a review warning, not a fix.

**In-template reference:** `Features/System/GameCheat/` — `screen_game_cheat.prefab`,
`GameCheatController.cs`, `GameCheatManager.cs`, plus `button_item_cheat Variant.prefab`. Read it
before inventing a pattern; it is the only cheat implementation that ships here.

---

## 3. Layer 2 — the buttons

One cheat = one instance of
`Assets/_Project/Visual/ArtAsset/Shared/Resources/Prefabs/Templates/Button_Template/ButtonCheatTemplate.prefab`
(guid `7322ef5c5cec00a4fab0c80fca752b4b`, a Variant of `button_template_common_txt.prefab`, default
label `Cheat`) as a **direct child of `CheatMenu/Menu`**. For an on/off cheat use
`ToggleCheatTemplate.prefab` (guid `df02728a597bbdc4b901a2de81e50c4b`) instead.

| Property | Rule |
|---|---|
| GameObject name | Short PascalCase action name, not the method name — `NextDay`, `ResetClaim`, `GrantPack` |
| Label text | Short **plain-English**, `\n` allowed for two lines — `+1 day`, `Reset claim`, `Grant pack` |
| Localize | **NONE.** Never add `LocalizesUI` / `LangKey` — cheat labels are dev-only |
| Size | Keep the prefab's inherited size; use the **same width** for every button in one menu |
| `onClick` | One persistent call → a `public Cheat_*` method on the feature **Controller** |

Keep the set small (2–5 buttons).

### Building the buttons via Unity MCP

`unity_asset_instantiate_prefab` `ButtonCheatTemplate.prefab` into `<Feature>/CheatMenu/Menu`, rename,
set the label's `m_Text`. Persistent `onClick` wiring has no dedicated MCP tool — use
`unity_execute_code` against the prefab asset:

```csharp
var path = "Assets/_Project/Features/<Domain>/<Feature>/Resources/screen_<feature>.prefab";
var root = UnityEditor.PrefabUtility.LoadPrefabContents(path);
var ctrl = root.GetComponent<<Feature>Controller>();
var btn  = root.transform.Find("CheatMenu/Menu/NextDay").GetComponent<UnityEngine.UI.Button>();
var call = (UnityEngine.Events.UnityAction)System.Delegate.CreateDelegate(
    typeof(UnityEngine.Events.UnityAction), ctrl, "Cheat_NextDay");
UnityEditor.Events.UnityEventTools.AddVoidPersistentListener(btn.onClick, call);
UnityEditor.PrefabUtility.SaveAsPrefabAsset(root, path);
UnityEditor.PrefabUtility.UnloadPrefabContents(root);
UnityEditor.AssetDatabase.SaveAssets();   // SaveAsPrefabAsset alone does not flush
return "ok";
```

Verify afterwards: the prefab YAML must contain the method name at
`m_OnClick.m_PersistentCalls.m_Calls.Array.data[0].m_MethodName`, with a
`m_TargetAssemblyTypeName` naming the controller and **the assembly its asmdef actually defines** —
read the asmdef, do not guess the assembly name.

---

## 4. Layer 3 — the code

**Controller** (`Scripts/Controller/`) — a `#region Cheats` at the end of the class; each method
`public`, Odin-buttoned so it also works straight from the Inspector, ending in a UI refresh:

```csharp
#region Cheats

[TabGroup("Cheats")]
[Button]
public void Cheat_NextDay()
{
    <Feature>Service.Cheat_SetDayOffset(1);
    RefreshUI();
}

#endregion
```

**Service** (static, also in `Scripts/Controller/` per `.claude/rules/project-structure.md`) — cheat
state + mutations live next to the data they touch, in their own `#region Cheats`, prefixed `Cheat_`:

```csharp
private static int _cheatDayOffset;

public static void Cheat_SetDayOffset(int delta)
{
    _cheatDayOffset = Mathf.Clamp(_cheatDayOffset + delta, -3650, 3650);
    CheckAndRunDailyReset();
}
```

Non-negotiable rules for the code layer:

1. **Time cheats go through the feature's single time accessor, never `DateTime.Now`.** Add the offset
   field to the service and read it inside the one accessor that already wraps `TimeManager` — do not
   sprinkle offsets at call sites.
2. **A cheat must leave the feature in a consistent state**, exactly as the real flow does: save →
   refresh → emit (`PlayerDataManager.<Module>.Save()`, then the same `EventName` the production path
   emits).
3. **Cheats reuse production entry points** — call the real claim/grant method, `RewardsService`, the
   service's own setters. Never hand-write a parallel grant path that can drift from the real one.
4. Every cheat set should include a **reset** (`Cheat_ResetAll` / `Cheat_ClearClaims`) so a tester can
   re-run the flow without wiping the profile.
5. Standard project rules still apply inside cheat code: `TimeManager` not `DateTime.Now`, `UIManager`
   not `SetActive`, `UniTask` not coroutines, no magic numbers that belong in CSV.

---

## 5. Checklist

- [ ] Decision recorded: cheats added, **or** an explicit reason why the feature needs none.
- [ ] `CheatMenu.prefab` instantiated into the feature prefab (not hand-built); `_targetMenu` points at `Menu`.
- [ ] Sample `Add Point` / `Remove Point` buttons repurposed or deleted.
- [ ] Cheat buttons are `ButtonCheatTemplate` (or `ToggleCheatTemplate`) instances directly under `CheatMenu/Menu`.
- [ ] Uniform button width within the menu; labels are short plain English with **no** `LocalizesUI`.
- [ ] Each button's `onClick` resolves to a real `public Cheat_*` method on the feature Controller.
- [ ] Controller cheats sit in `#region Cheats` with `[TabGroup("Cheats")] [Button]` and refresh the UI.
- [ ] Service cheats sit in `#region Cheats`, prefixed `Cheat_`, and save/emit like the real flow.
- [ ] Time cheats go through the feature's single time accessor, not scattered `DateTime` math.
- [ ] At least one reset cheat exists.
- [ ] No `#if UNITY_EDITOR` around cheat code — `GameCheatObjectController` is the gate.
- [ ] Play Mode: open the feature, the cheat button shows (`GameSystems.isCheat`), the menu expands,
      every button changes the visible state with no Console error.
