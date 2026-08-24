# EZG Visual Road Builder

Editor-only tool for Unity 2022.3+. Paints a modular road network on a
half-cell grid canvas and bakes the result as tile prefab instances into a
level prefab's `RoadParent` node.

The tool authors **visual geometry only** — it does not generate car routing,
pathfinding graphs, or any runtime navigation data. Those are separate
systems in a consuming project.

## Install

Add the EZG scoped registry to your project's `Packages/manifest.json`, then
add the package:

```jsonc
// Packages/manifest.json
{
  "scopedRegistries": [
    {
      "name": "EZG Registry",
      "url": "https://upm-registry-worker.developer-a1f.workers.dev",
      "scopes": ["com.ezg"]
    }
  ],
  "dependencies": {
    "com.ezg.visual-road-builder": "0.1.0"
  }
}
```

Or install via the Package Manager window: **Add package by name** →
`com.ezg.visual-road-builder`.

## First-Run Setup

Open the tool: **Tools > EZG Technical Art > Visual Road Builder**.

Complete these assignments in order inside the **Target** foldout:

### 1. Part Library

Drag a `RoadPartLibrary` asset into the **Part Library** field.

Create one via the Project window context menu:
**Create > EZG Technical Art > Road Part Library**.

> **HelpBox (Warning):** *"Gán Part Library (Create > EZG Technical Art >
> Road Part Library) để Apply được."*
> — Apply is blocked until a library is assigned.

### 2. Road Plan Atlas (on the library)

Select the library asset and assign a multi-sprite PSD/texture to the
**Road Plan Atlas** field. This is the 2D preview atlas; see
[Atlas Requirements](#atlas-requirements) below.

> **HelpBox (Info):** *"Gán Road Plan Atlas trong Part Library để hiển thị
> preview đường."*
> — The tool functions without it (colored squares replace previews), but
> road art will not render on the canvas.

### 3. Save Folder

Drag a **folder** (not a file) from the Project window into **Save Folder**.
This is where the tool reads and writes per-level ScriptableObject map files.

> **HelpBox (Warning):** *"Gán Save Folder (thư mục chứa file SO map) để
> Save/Load hoạt động."*
> — Save and Load are disabled until set.

### 4. Level Prefab

Drag a prefab asset into **Level Prefab**. Apply writes baked road meshes
into a child node named by the **Road Parent** text field (default
`RoadParent`). The node is created automatically if it does not exist.

### 5. Road Parent (optional)

Text field, defaults to `RoadParent`. Change it only if your level prefab
uses a different node name for road geometry.

## Art Contract

### Fixed Constants

These values are compile-time constants in the tool source. All art must
conform to them.

| Constant | Value | Meaning |
|---|---|---|
| `CellWorldSize` | `1` world unit | Distance between adjacent grid points. Not configurable. |
| `SpritePixelsPerCell` | `128` px | Atlas art scale: 128 pixels = 1 cell. |
| `StationSize` | `4` | Station block is 4 × 4 cells. |
| `ParkingLong` | `4` | Parking block long side = 4 cells. |
| `ParkingShort` | `2` | Parking block short side = 2 cells. |

Road modular tiles occupy **0.5 × 0.5** cells. Highway tiles occupy
**0.5 × 1** cells.

### Atlas Requirements

The preview atlas is a single texture (typically PSD) imported as a
**Multiple sprite** in Unity.

| Setting | Required value |
|---|---|
| Sprite Mode | **Multiple** |
| Filter Mode | **Point** (recommended for pixel-art preview) |
| Read/Write Enabled | Not required (the tool creates a readable copy at runtime) |
| Pixels Per Unit | Any (the tool uses `SpritePixelsPerCell = 128` internally) |

Each sub-sprite must be named exactly as listed in the table below. The tool
matches sprites by name in `EnsureRoadSprites`. Custom per-slice pivots are
used for preview alignment (see the source PSD `.meta` for reference values).

### Atlas Slice Names

Slices are separated into two groups. The first 17 are **required** — they
are present in the shipped atlas and the performance guard depends on all of
them being non-null. The remaining 7 are **not yet authored** — the tool
handles them gracefully (null sprite = that layer is not drawn, no fallback
color).

**Required slices (must be present in the atlas):**

| # | Slice name | Preview field | Notes |
|---|---|---|---|
| 1 | `Road_1x1_side` | `_spTileSide` | |
| 2 | `Road_1x1_side_rim` | `_spTileSideRim` | |
| 3 | `Road_1x1_curve` | `_spTileCurve` | |
| 4 | `Road_1x1_curve_rim` | `_spTileCurveRim` | |
| 5 | `Road_1x1_center` | `_spTileCenter` | |
| 6 | `Road_2x2_turn` | `_spTileTurn` | |
| 7 | `Road_2x2_turn_rim` | `_spTileTurnRim` | |
| 8 | `Road_1x1_turn` | `_spTileTurn1x1` | |
| 9 | `Road_1x1_turn_rim` | `_spTileTurn1x1Rim` | |
| 10 | `Road_3x3_turn` | `_spTileTurn3x3` | |
| 11 | `Road_3x3_turn_rim` | `_spTileTurn3x3Rim` | |
| 12 | `Highway_1x2` | `_spHighway` | |
| 13 | `Highway_1x2_rim` | `_spHighwayRim` | |
| 14 | `hway_to_road` | `_spRampHway` | |
| 15 | `station_area` | `_spStationArea` | |
| 16 | `parking_area` | `_spParkingArea` | |
| 17 | `Road_0.5x1_center` | `_spRoad2CenterFiller` | |

**Optional slices (not yet authored — tool skips gracefully):**

| # | Slice name | Preview field | Notes |
|---|---|---|---|
| 18 | `road2_curve` | `_spRoad2Curve` | Road 2 intersection corner |
| 19 | `road2_curve_rim` | `_spRoad2CurveRim` | Road 2 corner sidewalk |
| 20 | `hway_to_road2` | `_spRampHway2` | Highway-to-Road 2 ramp |
| 21 | `path_side` | `_spPathSide` | Path edge |
| 22 | `path_center` | `_spPathCenter` | Path intersection center |
| 23 | `path_curve` | `_spPathCurve` | Path corner |
| 24 | `path_turn` | `_spPathTurn` | Path turn arc |

> **Performance note:** the 17 required slices are checked in a short-circuit
> guard at the top of `EnsureRoadSprites`. If all are non-null the method
> returns immediately without re-scanning the atlas. The 7 optional slices are
> deliberately excluded from this guard — including them while their art does
> not exist would force a full atlas re-scan on every editor repaint.

### Prefab Slots — Pivot and Geometry Conventions

Each prefab slot on `RoadPartLibrary` expects a specific pivot position and
mesh orientation. **Yaw** refers to rotation around the Y (up) axis.

#### Road (modular 0.5 × 0.5 cell tiles)

| Field | Pivot | Mesh direction | Yaw 0 meaning |
|---|---|---|---|
| `road1x1_side` | Center of +Z edge | Extends toward -Z; X ∈ [-0.25, 0.25], Z ∈ [-0.5, 0] | A yaw/yaw+180 pair around the same pivot covers 0.5 × 1 cells; two such pairs offset ±0.25 along the rotated X axis cover 1 × 1 cell |
| `road1x1_side_rim` | Same as `road1x1_side` | Extends outward past the -Z edge of side | Always placed with side: same position, same yaw |
| `road1x1_curve` | Inner corner of cell | Extends toward +X / -Z | Yaw 0 = south-east corner cell |
| `road1x1_curve_rim` | Same as `road1x1_curve` | Same as curve | Always placed with curve: same position, same yaw |
| `road1x1_center` | Cell center | Fills cell (no rim) | Fills the 4 center cells of an intersection |
| `road2x2_turn` | Inner corner of the cell with 2 open branches | Extends toward -X / -Z | Yaw 0 = core lies south-west of pivot. Always placed with turn_rim |
| `road2x2_turn_rim` | Same as `road2x2_turn` | Extends outward past the 2 closed edges | Same as turn |
| `road3x3_turn` | Same convention as `road2x2_turn` | Arc core for Road 2 (1.5× cross-section) | Same as road2x2_turn. Always placed with road3x3_turn_rim |
| `road3x3_turn_rim` | Same as `road3x3_turn` | Sidewalk for the large turn arc | Same as road3x3_turn |
| `road1x1_turn` | Center of +Z edge (same as side) | Extends toward -Z, only 0.25 cells deep | Fills the quarter-cell gap between 2 intersections offset by 1.5 cells. Always placed with turn_rim |
| `road1x1_turn_rim` | Same as `road1x1_turn` | Covers full quarter-cell | Same as road1x1_turn |
| `hway_to_road` | *(no explicit pivot constraint in source)* | Ramp connecting highway to standard road | — |

#### Highway (modular 0.5 × 1 cell tiles)

| Field | Pivot | Mesh direction | Yaw 0 meaning |
|---|---|---|---|
| `hway1x2_side` | Center of +Z edge | Extends toward -Z; X ∈ [-0.25, 0.25], Z ∈ [-1, 0] (twice the depth of road side) | A yaw/yaw+180 pair covers 0.5 × 2 cells = full highway width |
| `hway1x2_side_rim` | Same as `hway1x2_side` | Extends outward past the -Z edge | Always placed with side: same position, same yaw |

#### Building

| Field | Pivot | Geometry |
|---|---|---|
| `stationPrefab` | Block center | 4 × 4 cells |
| `parkingPrefab` | Block center | 4 × 2 cells; long side along X axis, face toward +Z |

#### Road 2 (1.5× cross-section)

| Field | Pivot | Notes |
|---|---|---|
| `road2_center_filler` | Same as `road1x1_side` | Half-cell filler between rim and side; reuses `Road_0.5x1_center` mesh. The 2 inner side cells reuse `road1x1_side`/`road1x1_side_rim` |
| `road2_curve` | *(no art yet)* | Road 2 intersection corner — leave empty; produces a missing-part warning but does not block Apply |
| `road2_curve_rim` | *(no art yet)* | Road 2 corner sidewalk — leave empty |
| `hway_to_road2` | *(no art yet)* | Highway-to-Road 2 ramp — leave empty |

#### Path (pedestrian walkway, 0.5 cell cross-section, no rim)

Path slots use **weighted variant lists** (`List<PathPartVariant>`). Each
variant has a `prefab` and a `weight` in [0, 1]. The inspector normalizes
weights so they always sum to 1. The solver picks one variant per cell
according to these weights.

| Field | Pivot | Mesh direction | Yaw 0 meaning |
|---|---|---|---|
| `path_side_variants` | Center of +Z edge | 0.25 wide × 0.5 deep, extends toward -Z | A yaw/yaw+180 pair covers 0.25 along-axis × 1 across-axis; each 0.5-cell node slot needs 2 columns offset ±0.125. No rim. |
| `path_center_variants` | Cell center | 0.5 × 0.5 | Fills center of path intersection. No rim. |
| `path_curve_variants` | Inner corner | 0.25 × 0.25, extends toward +X / -Z | Yaw 0 = south-east cell. No rim. |
| `path_turn_variants` | Cell center | 0.5 × 0.5 | **Not** shifted 1 step CW like `road2x2_turn`. No rim. |

> **Legacy fields:** `path_side`, `path_center`, `path_curve`, `path_turn`
> (single `GameObject`, `[HideInInspector]`) exist for migration only. On
> first inspector open, `RoadPartLibraryEditor.MigrateLegacyPath()` moves
> them into the corresponding `*_variants` lists. The solver reads these as
> fallback if the variant list is empty.

## Samples

The package includes a **Default Libraries** sample (importable via Package
Manager). It contains pre-configured `RoadPartLibrary` and `DecorLibrary`
ScriptableObject assets.

After importing the sample, you **must** populate the prefab slots with your
own road tile meshes — the sample assets reference the source project's
prefabs, which will appear as missing references in your project. The
`roadPlanAtlas` reference (pointing to the shipped `_road_plan.psd`) resolves
automatically.

## Open Items

- **LICENSE:** not yet specified. Must be added before public distribution.
