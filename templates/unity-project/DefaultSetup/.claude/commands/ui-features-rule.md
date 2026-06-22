---
description: Folder structure for UI-based feature organization.
---

# UI Features Rule

## Folder Structure
Each UI feature lives under a domain folder in `Assets/_Project/Features/<Domain>/` (Domain ∈ `Meta, Monetization, Onboarding, Social, System, Events`; most UI screens go under `Meta`) and follows this structure:

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

Most UI features have a dedicated screen. Follow these rules:

1. **Create Prefab Variant** from the template at:
   `Assets/_Project/Visual/ArtAsset/Shared/Resources/Prefabs/Templates/Popup_Template/screen_template`

2. **Naming Format**: `screen_[FeatureName]`
   - *Example*: `screen_inventory`, `screen_shop` (No underscores).

3. **Location**: Place the screen prefab in the feature's `Resources/` folder.

---

## Screen Controller Rules

Each screen has a main controller script attached to the screen prefab.

1. **Naming Format**: `Screen[FeatureName]Controller`
   - *Example*: `ScreenInventoryController`, `ScreenShopController`

2. **Type**: Must inherit `FeatureBaseController` (registered in `GameEnums.Features`; opened via `UIManager.Instance.Show(...)`).

3. **Location**: Place in the `Scripts/Controller/` folder.

### Responsibility Separation

| Layer | Responsibility |
|-------|----------------|
| **FeatureBaseController** | Focuses on **UI Presentation** (display, animations, handling user interactions). |
| **Static Service** | Handles **Business Logic**, data configuration access, player data operations, and cross-feature communication. |

#### Implementation Guideline:
- Controller scripts should only handle UI logic.
- Any logic involving **Data Configuration** or **Player Data**, or any complex operation, must be delegated to the feature's **static Service class**.

```csharp
public class ScreenInventoryController : FeatureBaseController
{
    public void OnButtonClick()
    {
        // UI controller calls the Service for data logic
        InventoryService.EquipItem(itemId);
        
        // Then updates UI
        RefreshUI();
    }
    
    private void RefreshUI()
    {
        // UI update logic only (toggling icons, text updates, etc.)
    }
}
```