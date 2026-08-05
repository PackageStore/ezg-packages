---
description: Rules for organizing gameplay scripts.
---
# Gameplay Rule

## Location
- This repo is a **template** — it ships no gameplay bucket. A project creates its own gameplay domain
  under `Assets/_Project/Features/<GameplayDomain>/` (e.g. `Gameplay/`, `Combat/`, `Puzzle/`) and puts
  ALL gameplay scripts there.
- Inside that bucket, organize per feature — `Features/<GameplayDomain>/<Feature>/` — and split each
  feature's scripts by role, exactly like every existing feature does.
- Use `/new-feature` to scaffold the tree; never hand-author it.

## Folder Structure (mirror any existing feature)
```
Assets/_Project/Features/<GameplayDomain>/<Feature>/
├── CsvConfig/                 # <X>Config.csv (auto-loaded by com.ezg.csv-reader)
├── Resources/                 # prefab + generated ScriptableObject
├── Visuals/                   # art
└── Scripts/
    ├── Controller/            # Screen<X>Controller.cs, <X>View.cs (extend FeatureBaseController)
    ├── Data/                  # Player<X>.cs + Player<X>Data.cs (extend DataPlayerBase)
    ├── Service/               # <X>Service.cs — static, holds the feature logic
    ├── Config/                # <X>ConfigModel.cs + <X>ConfigCollection.cs (CSV → model)
    └── Events/                # <X>EventName.cs (partial class EventName)
```
Create only the slots you need, but keep the names — `Controller / Data / Service / Config / Events`
is the fixed vocabulary. Do not invent a new `Scripts/` sub-folder when one of these fits.

## Rules
- Gameplay code never goes into `Features/_Shared/` — `_Shared` is for cross-cutting frameworks only.
- Config tables → `CsvConfig/` + `Scripts/Config/`, exposed through `DataManager.<CollectionName>`.
- Persisted progress → `Scripts/Data/`, exposed through `PlayerDataManager.<Module>`.
- Screens → `Scripts/Controller/`, registered in `GameEnums.Features`, opened via `UIManager`.

> Existing domain buckets in this template, siblings of any new gameplay bucket:
> `_Shared, Meta, Monetization, Onboarding, Social, System`.
