---
description: Step 1 - Initialize feature folder structure and UI screen
---

# Step 1: Initialize Feature

## Folder Structure
Create the feature folder at `Assets/_Project/Features/<Domain>/<FeatureName>/` (Domain ∈ `Meta, Monetization, Onboarding, Social, System, Events, Gameplay`; most UI screens go under `Meta`) with this structure:

```
FeatureName/
├── Scripts/
│   ├── Controller/   # Logic processing scripts, static Service classes
│   └── Data/         # User data (PlayerData), ScriptableObject configuration classes
├── CsvConfig/        # Feature CSV configs (auto-loaded by com.ezg.csv-reader), if any
├── Visuals/          # Art assets (sprites, textures, animations)
└── Resources/        # Prefabs, UI screens related to the feature
```

## Screen Prefab Rules

1. **Create Prefab Variant** from template:
   `Assets/_Project/Visual/ArtAsset/Shared/Resources/Prefabs/Templates/Popup_Template/screen_template`

2. **Naming Format**: `screen_[FeatureName]`
   - *Example*: `screen_inventory`, `screen_shop`

3. **Location**: Place in the feature's `Resources/` folder.

## Screen Controller Rules

1. **Naming Format**: `Screen[FeatureName]Controller`
   - *Example*: `ScreenInventoryController`

2. **Type**: Must inherit `FeatureBaseController` (registered in `GameEnums.Features`; opened via `UIManager.Instance.Show(...)`).

3. **Location**: Place in `Scripts/Controller/` folder.

### Responsibility Separation

| Layer | Responsibility |
|-------|----------------|
| **FeatureBaseController** | UI Presentation (display, animations, user interactions) |
| **Static Service** | Business Logic, data access, cross-feature communication |

```csharp
public class ScreenFeatureNameController : FeatureBaseController
{
    public void OnButtonClick()
    {
        FeatureNameService.DoAction(itemId);
        RefreshUI();
    }
    
    private void RefreshUI()
    {
        // UI update logic only
    }
}
```

## Checklist
- [ ] Create folder structure
- [ ] Create screen prefab variant
- [ ] Create screen controller script
