---
name: VisualRoadBuilder
description: Deep guide + repro tooling for VisualRoadBuilderTool, the Editor window that paints sm6 level roads on a half-cell grid and bakes modular tile prefabs into level_N.prefab/RoadParent. Use whenever a road/junction comes out wrong on the canvas preview or in the baked mesh, and especially when the user demonstrates the wanted result by hand-fixing level_N.prefab and asks you to make the tool produce it.
---

# VisualRoadBuilder — the visual road painter for sm6 levels

`VisualRoadBuilderTool` (`Packages/com.ezg.visual-road-builder/Editor/`,
menu `Tools/EZG Technical Art/Visual Road Builder`) is an `EditorWindow` that paints a road network on a
half-cell grid, then bakes **modular tile prefabs** into `level_N.prefab → RoadParent`.

**Assembly:** `EZG.TechnicalArt.VisualRoadBuilder.Editor` (defined by the `.asmdef` in the tool folder).
**Namespace:** `EZG.TechnicalArt.VisualRoadBuilder`. Reflection-based tooling (e.g. `solver_dump.cs`) must target this assembly+namespace.

**Do not confuse it with two other road systems:**
- `Motorway.Grid.RoadGrid` — runtime car pathfinding / game logic.
- `WaypointSystem` (`/new-map`, `/check-traffic-jam`) — the car routing graph.

This tool only authors **visual geometry**. It never touches routing.

> **Paths in this file are the sm6 project's layout** — `Assets/_Project/Features/_Gameplay_sm6/RoadCanvasSaves/`
> for canvases, `Assets/_Project/Visual/AssetPackSource/Shops/` for level prefabs, `level_3…level_6` for
> map names. In another project, read those as *examples*: find the real folders from the tool's own
> Save Folder and Level Prefab fields, then keep every rule below unchanged. The helper scripts take
> paths as arguments, so nothing else needs editing. This skill ships with
> `com.ezg.visual-road-builder` and is installed by `Tools ▸ EZG Technical Art ▸ Install Claude Skill`.

---

## 1 — Data model (contracts you must not break)

**Source of truth for the map is the `.asset`, not the prefab.**
`Assets/_Project/Features/_Gameplay_sm6/RoadCanvasSaves/level_N_RoadCanvas.asset`
(`RoadCanvasSave`, editor-only, git-tracked). The prefab holds the *baked mesh*; the map *data*
lives only in the SO. `Apply` writes both; `Load`/`Restore` reads the SO.

| Concept | Rule |
|---|---|
| Lattice | Half-cell. `LatticeW = (width-1)*2 + 1`, same for H. Index = `y2 * LatticeW + x2`. |
| Coordinates | `x2 = x * 2` (half-cell integers). A drawn edge spans **1 lattice step = half a cell** (edge LENGTH, fixed), anchored on ANY half-cell lattice point (1-step grid). Connected neighbours along an edge's axis are `±1`; junction probing also checks `±2` (a real node one full cell away) — see §5. |
| Edge encode | `(y2 << 13) | (x2 << 1) | orient`; `orient` 0 = E/W, 1 = N/S. Normalized so the stored end is the lower one. |
| Station encode | `(rot << 24) | (y2 << 12) | x2` — different layout from edges. |
| World mapping | `cellWorldSize = 1`, fixed. `localPosition = (canvasX + originCell.x, 0, canvasY + originCell.y)`. So **local = canvas + originCell**. |
| Baked child name | `{prefabName}_{canvasX}_{canvasY}` — **canvas** coords, not local. Handy cross-check. |
| Direction bits | `DirE = 1 (+X)`, `DirN = 2 (+Z)`, `DirW = 4 (-X)`, `DirS = 8 (-Z)`. |
| Rotation | Yaw only, multiples of 90°, clockwise seen from above. `RotateCellsCW(x,y) = (y,-x)`. Never negative scale. |

**Two station lists, one encode.** `RoadCanvasDoc` carries both `Stations` (station 1) and `Stations2`
(station 2) as `List<int>`. Both use the identical station encode above. A station 1 and a station 2 at
the same anchor and rot produce the **same int** — they are only distinguishable by which list they sit
in. **Never feed station-2 ids into any id-keyed solver set** (especially `CollectResult.Road2Blocks`):
the solver only knows station 1; a collision would make it suppress road tiles around a station 2 that
has no road interaction at all.

`BlockKind` in `ViewState` selects the active block brush: `0` station 1, `1` parking horizontal,
`2` parking vertical, `3` station 2.

**Serialization trap:** Unity writes `List<int>` in these SOs as a **compact little-endian hex blob**
on one line, not a YAML list:
```
edges: 12800000168000001a8000...
```
Every 8 hex chars = one `int32`, byte-reversed. Use `scripts/canvas_decode.py` — never eyeball it.

---

## 2 — Tile vocabulary (`RoadPartLibrary`)

Every road shape is assembled from 0.5×0.5-cell modular tiles. Pivot conventions are load-bearing:

| Field | Role | Pivot / mesh |
|---|---|---|
| `road1x1_side` | road edge tile, occupies exactly 1 logical cell | pivot mid `+Z` edge, mesh toward `-Z` |
| `road1x1_side_rim` | kerb for `side`, sticks **outside** the logical cell | same position + yaw as its `side` |
| `road1x1_curve` / `_rim` | junction corner fillet | pivot at the **inner** corner, mesh toward `+X/-Z` (yaw 0 = SE cell) |
| `road1x1_center` | plain roadway, no kerb — fills the middle of a junction | pivot at cell centre |
| `road3x3_turn` / `_rim` | arc core, replaces the 4 centers on a 2-arm turn | same pivot as the open corner's `curve`, but **one step CW** |
| `road1x1_turn` / `_rim` | fills the quarter-cell where **two junctions' fillets collide**, replacing *both* `curve`s | same convention as `side` (pivot mid `+Z` edge, mesh `-Z`); core only 0.25 deep, rim the full 0.5 |

Assembly constants (`RoadSolver.cs`):
- 1×1 straight = **2 columns** offset `±0.25` along the rotated X axis; each column = the tile at
  `yaw` **and** `yaw+180` around the *same* pivot.
- Junction: centers at `(±0.25, ±0.25)`; open arm tile at `0.75`; closed-edge kerb run at
  `t ∈ {-0.75, -0.25, 0.25, 0.75}`; corner fillet pivot `(0.5, -0.5)` rotated by yaw.
- Half straight (the 0.5×1 piece a junction places into its neighbour's slot) sits at `1.25`.

---

## 3 — The single-solver invariant (most important architectural fact)

These four are the **only** sources of tile position + yaw:

```
ResolveRoadLayout        (RoadLayout.cs)      — masks + midpoint junctions + skip sets + kerb map
ForEachJunctionTile      (RoadSolver.cs)      — every tile of a junction piece
ForEachStraightTile      (RoadSolver.cs)      — every tile of a straight piece
ForEachHalfStraight      (RoadSolver.cs)      — the 0.5-cell pieces around a junction
```

and they are shared by **four consumers**:

| Consumer | Entry point |
|---|---|
| Mesh bake | `CollectRoadPlacements` (RoadSolver.cs) ← `Apply()` |
| 2D canvas preview | `DrawRoadSprites` (RoadSprites.cs) |
| Overlap / collision checks | `Overlap.cs`, `RoadPieceRectCells` (MaskUtil.cs) |
| Debug boundary boxes | `DebugTab.cs` |

**Consequences — internalize these:**
1. "The canvas preview is wrong" **and** "the baked mesh is wrong" are usually **one** bug in the
   shared enumerator, not two bugs. Look for the single cause before touching either renderer.
2. **Never patch the preview to match the mesh** (or vice versa). Fix the shared code; both follow.
3. Verifying the bake output therefore *also* verifies the preview. That is what makes the offline
   diff in §6 a complete check even though the canvas cannot be screenshotted on macOS
   (`unity_screenshot_editor_window` is Windows-only).

**Station 2 is entirely outside this invariant.** It is never passed to any collector, has no
preview/mesh coupling to keep in sync, and the four shared enumerators above do not see it. It bakes
directly from `RoadCanvasDoc.Stations2` via `RoadBaker.BuildInto` — decode, instantiate prefab, done.

---

## 4 — Solve pipeline

`ResolveRoadLayout(edges, blocked)`:

1. **`BuildMasks`** — 4-direction bitmask per lattice node from the edge list. Every edge is half a
   cell long (1 lattice step), chained end to end, so this alone credits every point a drawn run
   passes through — no separate midpoint-synthesis step is needed (§5).
2. **`AddSideBranchJunctions`** — a perpendicular branch that stops 1 cell short of a road (never
   sharing an edge with it) still has to bend that road's node into a junction; probes `±2` and ORs
   in the matching direction.
3. **`CollectHalfStraightSets`** — for each straight adjacent to a junction: exactly 1 neighbouring
   junction → `ReplacedByHalf` (the junction places a half piece instead); 2 junctions squeezing it
   → `DroppedBetween` (place nothing, the two junction meshes already meet).
4. **`MarkStraightRuns`** — classifies interior straight-through nodes between two junctions/dead-ends:
   only every 2nd lattice step (matching the old 1-cell-edge model) is a real placement, the rest are
   pure connectivity → `OffStride` (skip). A run with an odd total length (only possible for freshly
   drawn content, never for migrated data) leaves one node in `TailHalfDir` instead, baked as a half
   piece facing the far end.
5. **`CollectFilletKerb`** — record every quarter-cell where a corner `curve` already lays kerb, so
   `RimCovered` can suppress a `side_rim` that would stack on top of it.
6. **`CollectFilletTurns`** — merge two junctions' facing corner fillets into one `road1x1_turn` when
   they land on the same quarter-cell (§5).

Then `CollectRoadPlacements` walks the lattice: `layout.Skip(i)` nodes are ignored, straight-like
masks go to `AddStraightTiles` (via `StraightAnchorFor`, which honours `TailHalfDir`), everything else
to `AddJunctionTiles` + `AddHalfStraights`. Finally **`DedupePlacements`** drops tiles identical in
(prefab, position, yaw) — which is how two overlapping junctions share their centers cleanly.

`RoadLayout` sets: `ReplacedByHalf`, `DroppedBetween`, `OffStride` (interior run node, no placement of
its own), `FilletKerb`; plus the `TailHalfDir` dictionary (interior node → half-tile facing direction)
for a run's odd remainder.

**Station-2 blocks are inert in the solve pipeline.** They suppress nothing, contribute no blocked set,
and a road bakes straight through underneath one. `CollectAll.Run` never reads `doc.Stations2`; only
`RoadBaker.BuildInto` touches them (instantiate `station2Prefab` into `RoadParent/Stations2`).

---

## 5 — Junctions, including the half-cell case

Every edge is now half a cell long (1 lattice step), chained end to end, so `BuildMasks` credits
**every** lattice point a drawn run passes through — including what used to be an unreachable
"midpoint" under the old 1-cell edge model. There is no more synthesis step: a branch that meets a
road at what looks like the flank of an old-style edge is, today, meeting a real edge endpoint with
its own native mask bits. `AddMidpointJunctions` (and its `Covered` bookkeeping) is gone — nothing
needs to inject mask bits at a point BuildMasks doesn't already populate.

Overlapping half-offset edges (e.g. `(10,18)→(11,18)` *and* `(11,18)→(12,18)` drawn independently)
simply OR their bits together at the shared node like any other pair of adjacent edges — no special
casing. Whether that node ends up **straight-like** (an ordinary interior point on a longer run — see
`MarkStraightRuns` in §4) or a **junction** (a perpendicular branch also lands there) falls out of the
mask value alone.

**Two junctions can sit 1 lattice step (0.5 cell) apart** — this was previously only reachable via a
synthesized midpoint junction; today it's just two ordinary edge-endpoint nodes 1 lattice step apart,
no different in kind from two junctions a full cell apart. They resolve each other automatically:

- `JunctionArms` probes `±1` **as well as** `±2`: the neighbour's `center` tiles land exactly
  on this node's arm `side` tiles, so that arm has to yield.
- `EdgeRim` skips the kerb halves on the yielded side.
- `RimCovered` (from `FilletKerb`) kills kerbs already laid by the neighbour's corner fillet.
- `DedupePlacements` collapses the centers the two junctions share.

### Side branches touch at real nodes too (`AddSideBranchJunctions`)

A branch drawn perpendicular to a road **never has an edge joining it** — its nearest node always sits
1 cell (2 lattice steps) from the road. `AddSideBranchJunctions` closes that: for each road node it
probes `±2` perpendicular and ORs in any direction whose neighbour's mask points *away*. Since every
lattice point along a run now carries a native mask (no midpoint gaps), this probe reaches a branch at
any parity uniformly — there is no even/odd distinction left to worry about.

It requires the node to already hold the **full perpendicular axis pair** (`E|W` or `N|S`) so dead-ends
and corners never sprout an arm. Additions are computed from a mask snapshot and applied in one shot,
so a pass never cascades.

### Colliding fillets become one `road1x1_turn` (`CollectFilletTurns`)

Two junctions **1.5 cells apart** (i.e. 3 lattice steps — reachable natively now, no synthesis
required) put their facing corner fillets on the *identical* quarter-cell — mirror images stacked on
each other. Both yield to a single `road1x1_turn` + `_rim`:

- pivot = **midpoint of the two `curve` pivots** (they are the two corners of that quarter-cell lying
  on the edge facing the roadway),
- yaw = `RimYawFacing(direction from that pivot into the quarter-cell)`.

Both junctions emit the same tile and `DedupePlacements` keeps one. `CollectFilletKerb` needs no
change: `turn_rim` covers the same quarter-cell the `curve_rim` did, so `RimCovered` is unaffected.
Arc-core (2-arm) junctions are skipped — their fillet is bound to the `turn3x3` core.

`ForEachJunctionTile` takes the substitution as a `filletTurn` delegate (built once by
`FilletTurnProbe`, which converts layout absolute coords to piece-centre offsets), so all four
consumers of §3 stay in lockstep.

### Worked example — `level_4`, local `(-0.5, 3.5)`

Canvas: horizontal run at `y2=18` drawn as the half-cell edge chain `10→11→12→13→14→15` plus a
vertical branch at `x2=11` topping out at `y2=16` (edges `16→17→18`).

`BuildMasks` gives node `(11,18)` mask `E|W` natively from the horizontal chain (edges `10→11` and
`11→12` meet there) OR-ed with `S` from the vertical branch's edge ending there — mask `E|W|S`, a T
junction, with **no synthesis step and no bailing**: this is just an ordinary node where three edges
happen to meet, identical in kind to a T junction drawn at any other lattice point.

Everything else falls out with no special-casing:

- its `S` arm sides at local `(-0.5, 3.25)`, kerbs killed by `RimCovered`;
- `(11,16)` → `ReplacedByHalf`, so the half straight lands at `(-0.5, 2.75)` with kerbs;
- it is **1.5 cells** (3 lattice steps) from the real T at `(8,18)`, so their facing fillets merge into
  the `road1x1_turn` at `(-1.25, 3.5)` yaw 0 — the tile the hand-fixed prefab shows;
- the straight-run bake pass (which places one full-cell tile per 2 lattice steps along a run so a
  half-cell edge chain bakes identically to the old 1-cell-edge model) never touches `(11,18)` at all —
  it only classifies *interior* straight-through nodes between two junctions, and this node is itself
  a junction.

Run `canvas_decode.py <asset>` on that canvas and it prints the T junction at `(11,18)`.

---

## 6 — WORKFLOW: the user demonstrates intent by hand-fixing the prefab

**This is the standing convention for this tool.** The user paints a canvas, sees a wrong result,
then edits `level_N.prefab` **by hand** in Unity until it looks right and says *"I fixed the prefab
to show what I want — make the tool produce this."*

### Hard rules

1. **`level_N.prefab` (dirty in git) is the CONTRACT. The `.asset` canvas is the INPUT.**
   Your job: change the *solver* until it maps input → contract. Never edit the prefab to match the
   tool, and never "clean up" the user's demo.
2. **NEVER run `Apply()` / press Apply.** It does
   `LoadPrefabContents → destroy all RoadParent children → rebuild → SaveAsPrefabAsset`, is **not
   undoable**, and would destroy the demonstration. Use `scripts/solver_dump.cs` instead — it runs
   the real solver and writes nothing.
3. **Back up before anything.** Copy the dirty `level_N.prefab` *and* `level_N_RoadCanvas.asset` to
   the scratchpad first. Do it even when you intend to write nothing.
4. **Do not diff against `git HEAD` of the prefab.** If the user also redrew the canvas (check
   `git status` — the `.asset` will be dirty too), `HEAD` is a bake of a *different* layout, and
   diffing it conflates the layout change with the bug. HEAD is only useful when the canvas is
   unchanged. When both are dirty, the only valid comparison is
   **current canvas → solver output vs current prefab**.
5. **Success is binary:** `diff_tiles.py` exits 0 — zero MISSING, zero EXTRA, on (layer, prefab name,
   local x, local z, yaw). Anything less is not done.

### Procedure

```bash
REPO=$(git rev-parse --show-toplevel); S=$REPO/.claude/skills/VisualRoadBuilder/scripts
SCRATCH=<your scratchpad>

# 0 — back up the user's demo + the canvas
cp Assets/_Project/Visual/AssetPackSource/Shops/level_4.prefab $SCRATCH/
cp Assets/_Project/Features/_Gameplay_sm6/RoadCanvasSaves/level_4_RoadCanvas.asset $SCRATCH/

# 1 — understand the INPUT: nodes, junctions, half-cell junction pairs
python3 $S/canvas_decode.py Assets/_Project/Features/_Gameplay_sm6/RoadCanvasSaves/level_4_RoadCanvas.asset --grid
# For road2/path/highway layers: --layer road2, --layer path, --layer highway, --layer hwdecor

# 2 — the CONTRACT: what the user's prefab actually contains (all layers under RoadParent)
python3 $S/prefab_tiles.py Assets/_Project/Visual/AssetPackSource/Shops/level_4.prefab --out $SCRATCH/expected.csv
```

3 — the ACTUAL solver output, without writing the prefab: paste `scripts/solver_dump.cs` into
`mcp__unity__unity_execute_code` (set `LEVEL` and `OUT`; pass the project `port`).

```bash
# 4 — the delta
python3 $S/diff_tiles.py $SCRATCH/expected.csv $SCRATCH/solver_dump.csv
```

5 — read the delta *geometrically*, not textually. Every row is a tile at a known offset from some
node, so each one identifies which enumerator branch is wrong (arm / edge-kerb / corner / center /
half). Locate the shared cause in §3–§5 and fix it there.

6 — recompile (`AssetDatabase.Refresh()` + `RequestScriptCompilation()`, then
`unity_get_compilation_errors`), re-run steps 3–4, iterate to zero.

7 — prove no regression on the other maps (§7). Then tell the user to press Apply themselves, or ask
before doing it — Apply is theirs to trigger since it overwrites their demo with the now-correct bake.

---

## 7 — Regression proof without re-baking other maps

Only levels with a `RoadCanvasSaves/level_N_RoadCanvas.asset` can be re-baked by this tool — today
that is **`level_3`, `level_4`, `level_5`, `level_6`** and **`level_test_tool_xep_map`**. `level_1`/`level_2` have no canvas and are out of scope.

Don't re-bake `level_3` to check for regressions (that mutates a committed map). Instead prove the
change *cannot* apply, by testing its trigger predicates on that canvas:

```bash
python3 .claude/skills/VisualRoadBuilder/scripts/canvas_decode.py \
  Assets/_Project/Features/_Gameplay_sm6/RoadCanvasSaves/level_3_RoadCanvas.asset
```

`canvas_decode.py` does **not** model `AddSideBranchJunctions` or `CollectFilletTurns`. For those, probe
the real statics in-Editor and report the counts (level_3 today: `0` / `0` / `0`, so both rules are
provably inert there):

```csharp
// per canvas: side-branch sites, midpoint junctions, curves swapped for a 1x1 turn
int[] raw = BuildMasks(_edges);                              // via reflection, see §6 step 3
AddSideBranchJunctions(probeLayoutHolding(raw.Clone()), null);  // count masks that changed
var layout = ResolveRoadLayout(_edges, null);                   // layout.FilletTurn.Count
```

Read the summary line: **`junction pairs 0.5 cell apart`**. If a junction-solver change only fires
in those situations and the count is 0, the map is provably untouched. State the counts in your
report rather than claiming "no regression" bare.

For predicates the script doesn't model, run the equivalent probe in-Editor via reflection on the
real statics (`IsStraightLikeMask`, `IsJunctionMask`, `BuildMasks`) — that avoids trusting the Python
mirror.

**Strongest form: count the sites where old and new code differ.** When a change only relaxes a
guard, enumerate the nodes that reach it and satisfy the new predicate but not the old one. Zero such
sites ⇒ bit-identical output, no need to reason about downstream passes. For the midpoint-junction
guard that means, per edge, `raw[mi] != 0 && (raw[mi] & axis) != axis` (level_3 today: **0**).

---

## 8 — Gotchas

- **`Apply()` is not undoable** — prefab contents live outside the Undo system. It only shows a
  confirm dialog.
- **`canvas_decode.py` is a mirror, not the truth.** It reproduces `BuildMasks` +
  `IsStraightLikeMask` in Python for offline reasoning (no more `AddMidpointJunctions` step — deleted
  when edges became half-cell, §5). It knows all edge layers (`road`, `road2`, `path`, `highway`,
  `hwdecor`) and handles `edgeSpanVersion` 0→1 split automatically. **It does NOT decode the
  `stations2` key** — station-2 blocks are invisible to it. If the C# changes, update it or
  fall back to in-Editor reflection.
- **`ScriptableObject.CreateInstance` on this window runs `OnEnable`**, which registers the autosave
  editor tick. Always `DestroyImmediate` in a `finally` (the harness does).
- **`solver_dump.cs` covers ALL layers** (road, road2, path, highway) following `Apply()`'s exact
  collect order including station/parking block suppression, highway ramp suppression, apron plain,
  block edge fills, and isolated-straight dedup. It has a **dual-path** design: tries the post-refactor
  `CollectAll.Run(RoadCanvasDoc, RoadPartLibrary) → CollectResult` API first, falls back to pre-refactor
  private reflection when that type is absent — so the same script works before and after the decompose
  epic. Output is 5-column CSV: `layer,prefabName,localX,localZ,yaw`.
- **No Unity? compile + diff offline.** When `mcp__unity__*` is unavailable you can still (a) compile
  the folder with the SDK's Roslyn against the Editor's own DLLs — reference **only** the
  `UnityEngine.dll` + `UnityEditor.dll` facades (adding `UnityEngine/*Module.dll` too gives CS0433
  ambiguity, omitting the facade gives CS0012), pass args via a response file because the repo path
  contains a space; and (b) mirror `Apply()`'s collect order in Python and **prove the mirror by
  reproducing the committed bake tile-for-tile before touching anything** — that is what makes a
  mirror trustworthy enough to iterate the fix on.
- **Regex over Unity YAML: never use `\s*` after a key** — it matches newlines and silently grabs the
  next key's value. Use `[ \t]*`.
- **`unity_screenshot_editor_window` is Windows-only.** On macOS you cannot screenshot the canvas;
  rely on the shared-solver invariant (§3) instead of asking the user for a screenshot.
- **New `.cs` files need a matching `.meta`** with a fresh GUID, following the sibling files.
- Menu path is `Tools/EZG Technical Art/Visual Road Builder` — *not* under `Tools/sm006/`.
- **Backward compatibility: station-2 blocks are silently lost on 0.1.x.** A canvas saved with
  `Stations2` entries and opened by a tool version that predates the field (0.1.x) silently discards
  them — Unity's YAML deserializer drops unknown keys with no warning. The data is gone after the next
  save; there is no error or dialog.
- **`_spStation2Area` must stay OUT of the sprite readiness chain** in `RoadSprites.cs`. The
  `station_area_2` slice does not exist in the shipped `_road_plan.psd`; including it in the `&&`
  chain that gates `_roadSpritesReady` would make the chain never true, forcing a full atlas re-scan
  every repaint (the logged P7 regression). Station 2 draws as a flat tinted rect via the fallback
  path, same as parking.

---

## 9 — File map

> **Note:** the decompose epic (`docs/plans/vrb-decompose/`) is extracting the god class into
> single-job files organized into folders. The map below shows the **post-refactor** layout. If
> the refactor is not yet complete, some files still live in the root as `.PartialName.cs` partials
> of `VisualRoadBuilderTool` — consult the current directory listing. Level prefabs live under
> `Assets/_Project/Visual/AssetPackSource/Shops/`; road2 tiles bake under `RoadParent/Road2`;
> station-2 blocks bake under `RoadParent/Stations2`.

| Folder | File | Responsibility |
|---|---|---|
| (root) | `VisualRoadBuilderTool.cs` | EditorWindow shell: `OpenWindow`, `OnEnable/OnDisable/OnGUI`, `MenuPath`, `_library` |
| | `EZG.TechnicalArt.VisualRoadBuilder.Editor.asmdef` | assembly definition — isolates from Assembly-CSharp-Editor |
| `Model/` | `RoadCanvasDoc.cs` | map data container: edges (all layers), stations, stations2, parkings, decors, rampFlips, originCell, grid dims, `LatticeW/H`, `EdgesFor(layer)` |
| | `EdgeCodec.cs` | `EncodeEdge`, `DecodeEdge`, `DecodeRampAnchor` |
| | `BlockCodec.cs` | `EncodeStation/Parking`, `DecodeStation/Parking`, `StationPivotCell`, `ParkingPivotCell` |
| | `MaskBuilder.cs` | `BuildMasks`, `BuildLegacyMasksFromEdges`, `PairHalfEdges` |
| | `DirBits.cs` | `DirE/N/W/S`, `OppositeDir`, `DirStep`, direction/rotation helpers |
| | `GridConst.cs` | `MaxGridSize`, `CellWorldSize`, `StationSize`, `ParkingCells`, `EighthsPerCell` |
| | `GridOps.cs` | `ExpandGrid`, `PruneOutOfRangeEdges`, `RemoveContentBelow`, `OffsetAll` |
| | `DecorOps.cs` | `DecorItem` struct, decor list operations |
| | `LatticeKeys.cs` | `CurveKey`, `KerbCellKey`, `QuarterCellCenter` |
| `Solver/Road/` | `RoadLayoutResolver.cs` | `ResolveRoadLayout` |
| | `RoadCollector.cs` | `CollectRoadPlacements` → placement list + isolated-key set |
| | `SideBranchJunctions.cs` | `AddSideBranchJunctions` |
| | `StraightRunMarker.cs` | `MarkStraightRuns` |
| | `FilletCollector.cs` | `CollectFilletKerb`, `CollectFilletTurns` |
| | `RoadStraightAnchor.cs` | `StraightAnchorFor`, half-piece logic |
| `Solver/Road2/` | `Road2Collector.cs` | `CollectRoad2Placements` |
| | `Road2JunctionEmitter.cs` | `ForEachRoad2JunctionTile`, `Road2JunctionArms` |
| | `Road2StraightEmitter.cs` | `ForEachRoad2StraightPart` |
| | `Road2JunctionBaker.cs` | junction tile → prefab for road2 |
| | `Road2StraightBaker.cs` | straight tile → prefab for road2 |
| | `Road2JunctionEffects.cs` | fillet kerb/turns for road2 |
| | `Road2ApronFiller.cs` | `AddRoad2ApronFillers`, `ForEachRoad2ApronFiller` |
| | `Road2Constants.cs` | `Road2SideBranchReachSteps`, cross-section constants |
| | `Road2TileParts.cs` | `Road2TilePart` enum |
| `Solver/Block/` | `StationRoadCollector.cs` | `CollectStationRoadPlacements`, `CollectStationRoad2Placements` |
| | `ParkingKerbCollector.cs` | `CollectParkingRoadKerb`, `CollectParkingRoad2Kerb` |
| | `BlockEdgeFiller.cs` | `CollectBlockEdgeFills` |
| | `BlockApronWalker.cs` | `ApplyApronPlain` |
| | `BlockClearance.cs` | `BlockClearanceSteps`, `BlockPivotInsetFor` |
| | `BlockSuppression.cs` | `BlockSuppression` class |
| | `BlockRoadSkin.cs` | `BlockRoadSkin` class |
| | `BlockStrip.cs` | `BlockStrip` struct |
| | `BlockSide.cs` | `BlockFacingStep`, block geometry helpers |
| `Solver/Highway/` | `HighwayColumnSolver.cs` | `CollectHighwayPlacements` |
| | `HighwayRampCollector.cs` | ramp collection, `CollectHighwayJunctions` / `Road2` |
| | `RampDetector.cs` | ramp anchor detection and flip logic |
| `Solver/Path/` | `PathCollector.cs` | `CollectPathPlacements` |
| | `PathJunctionWalker.cs` | path junction tile emitter |
| | `PathStraightWalker.cs` | path straight tile emitter |
| | `PathTileVocabulary.cs` | `PathTilePart` enum, `PathTilePrefab` |
| `Solver/Shared/` | `CollectAll.cs` | single entry point: `CollectAll.Run(RoadCanvasDoc, RoadPartLibrary) → CollectResult` |
| | `Placement.cs` | `Placement` struct (x, y, prefab, yaw, scaleMul) |
| | `TilePartRegistry.cs` | part → prefab mapping (14 tables collapsed) |
| | `MaskClassifier.cs` | `IsStraightLikeMask`, `IsJunctionMask` |
| | `DedupePlacement.cs` | `DedupePlacements`, `DedupeIsolatedStraightKeys` |
| | `JunctionTileEmitter.cs` | `ForEachJunctionTile` |
| | `StraightTileEmitter.cs` | `ForEachStraightTile`, `ForEachHalfStraight` |
| | `JunctionBaker.cs` | `JunctionArms`, junction → placement |
| | `StraightBaker.cs` | `AddStraightTiles` |
| | `HalfStraightEmitter.cs` | `CollectHalfStraightSets` |
| | `FilletMerge.cs` | shared fillet-turn merge loop |
| `Overlap/` | `OverlapDetector.cs` | overlap / collision checks |
| | `OverlapBoxMath.cs` | `PieceEighthBox`, `RoadPieceRectCells`, `Road2PieceRectCells` |
| `Render/` | `RoadSpriteRenderer.cs` | 2D canvas preview — road layer (consumes `CollectAll`) |
| | `Road2SpriteRenderer.cs` | 2D canvas preview — road2 layer |
| | `PathSpriteRenderer.cs` | 2D canvas preview — path layer |
| | `HighwaySpriteRenderer.cs` | 2D canvas preview — highway layer |
| | `DebugBoundaryCollector.cs` | `CollectDebugBoundaryItems` (consumes `CollectAll`) |
| | `DebugBoundaryItem.cs` | `DebugBoundaryItem` struct |
| | `DebugBoundaryRenderer.cs` | draws boundary boxes from collected items |
| | `DrawPrimitives.cs` | `DrawRectBorder`, `DrawFacingArrow` (7+2 callers) |
| | `SpriteLoader.cs` | atlas → sprite lookup |
| | `SpriteTextureCache.cs` | texture caching for canvas draw |
| | `SpriteWatcher.cs` | atlas-change monitoring |
| | `RoadTileDrawing.cs` | shared tile drawing math |
| | `BlockRenderer.cs` | station/parking block overlay |
| | `CanvasRenderer.cs` | canvas background grid |
| | `CropOverlayRenderer.cs` | crop-mode overlay |
| | `DecorRenderer.cs` | decor item canvas overlay |
| | `HoverRenderer.cs` | hover highlight |
| | `SelectOverlayRenderer.cs` | selection box overlay |
| | `TileRenderer.cs` | shared tile rendering dispatch |
| `Input/` | `IPaintTool.cs` | paint tool interface |
| | `PaintToolRouter.cs` | dispatches input to active tool |
| | `RoadPaintTool.cs` | edge painting (all layers) |
| | `StationTool.cs` | station/parking placement |
| | `DecorTool.cs` | decor item placement |
| | `EraserTool.cs` | edge/block/decor erase |
| | `CropTool.cs` | grid expand/crop mode |
| | `PanTool.cs` | canvas pan/zoom |
| | `HoverTracker.cs` | cursor → lattice tracking |
| | `ShortcutRouter.cs` | keyboard shortcuts |
| | `CancelHelper.cs` | escape / right-click cancel |
| | `MoveAllTool.cs` | move-all-content mode |
| | `SelectMoveTool.cs` | box-select and move |
| | `SelectMoveGeometry.cs` | selection geometry math |
| | `SelectMoveRebuilder.cs` | rebuild edges after selection move |
| `IO/` | `RoadBaker.cs` | `Apply()` — calls `CollectAll.Run()`, destructive prefab write |
| | `CanvasSaveIO.cs` | read/write `RoadCanvasSave`, autosave |
| | `PrefabIO.cs` | prefab path resolution, `LevelPrefabPath` |
| | `ApplyTarget.cs` | bake-target config (`_levelPrefab`, `_roadParentName`) |
| | `DecorApply.cs` | `ApplyDecors` into prefab |
| `Window/` | `ViewState.cs` | transient UI state: paint mode, scroll, hover, fold toggles, debug toggles |
| | `ToolContext.cs` | `ToolContext { Doc, Library, View, Host }` — injected into all extracted classes |
| | `Coordinates.cs` | grid ↔ canvas pixel, mouse snapping |
| | `CanvasPane.cs` | canvas area drawing coordination |
| | `CanvasSnapshot.cs` | snapshot for undo |
| | `UndoHistory.cs` | undo/redo stack |
| | `ToolStyles.cs` | `GUIStyle` / `GUIContent` constants |
| `UI/` | `ControlColumn.cs` | right-side control panel layout |
| | `SetupPanel.cs` | grid/target/library setup section |
| | `ToolsPanel.cs` | tool buttons, layer switcher, paint mode. Brush grid (3 columns): row 1 `Road 1 / Road 2 / Lối đi bộ`, row 2 `Highway / HW Decor / [spacer]`, row 3 `Station 1 / Station 2 / Park`. The spacer is an explicit `ToolBrushItem` with `Name = null` — remove it and Station 1 jumps up beside HW Decor |
| | `DecorSection.cs` | decor palette section |
| | `DecorState.cs` | decor selection state |
| | `SaveHistoryBar.cs` | save/load/history bar |
| | `DebugPanel.cs` | debug boundary toggle panel |
| `Library/` | `RoadPartLibrary.cs` | the tile prefab set (pivot conventions in tooltips); `roadPlanAtlas` field holds the sprite atlas reference; `station2Prefab` slot holds the station-2 block prefab |
| | `RoadCanvasSave.cs` | the map SO |
| | `DecorLibrary.cs` | decor item palette SO |
| | `RoadPartLibraryEditor.cs` | custom inspector for `RoadPartLibrary` (3-tab Road/Highway/Building) |
| `SO_lib/` | | ships `RoadPartLibrary.asset` + `DecorLibrary.asset` (the library SOs) |
| `scripts/` | `solver_dump.cs` | full-Apply solver dump (all layers, dual-path pre/post-refactor) — paste into `unity_execute_code` |
| | `prefab_tiles.py` | extract baked tiles from level prefab as CSV (all groups under RoadParent) |
| | `canvas_decode.py` | offline lattice classification from `.asset` (all edge layers, handles edgeSpanVersion); does NOT decode `stations2` |
| | `diff_tiles.py` | multiset diff of two tile CSVs — exit 0 = match |
