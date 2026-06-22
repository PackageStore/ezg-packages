---
description: Rules for organizing gameplay scripts.
---
# Gameplay Rule

## Location
- All gameplay scripts live under `Assets/_Project/Features/Gameplay/`.
- Organize by functionality/feature within that folder (Scripts split into `Controller/` and `Data/`).

## Folder Structure Example
```
Assets/_Project/Features/Gameplay/
├── CsvConfig/                     # Gameplay CSV configs (auto-loaded by com.ezg.csv-reader)
└── Scripts/
    ├── Controller/
    │   └── Controller/
    │       ├── ItemMerge.cs
    │       ├── Item/Services/     # ItemMergeService, ItemPolicy, ...
    │       └── Services/          # ItemMergePool, ...
    └── Data/                      # MergeEnum, ItemMergeCollection, PlayerGameplayData, ...
```

> Note: other feature domains live as siblings of `Gameplay` under `Assets/_Project/Features/`:
> `_Shared, Events, Meta, Monetization, Onboarding, Social, System`.
