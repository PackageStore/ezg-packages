// Full-Apply solver dump: ALL layers, no prefab write. Paste into unity_execute_code.
// Output CSV: layer,prefabName,localX,localZ,yaw (positions include originCell = prefab-local).
// DUAL-PATH: post-refactor CollectAll.Run first, pre-refactor private reflection fallback.

var LEVEL = "level_4";
var OUT   = "/tmp/solver_dump.csv";   // point this at your scratchpad

System.Type FindType(string fullName)
{
    foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies())
    { var t = a.GetType(fullName, false); if (t != null) return t; }
    return null;
}

var ns = "EZG.TechnicalArt.VisualRoadBuilder";
var sb = new System.Text.StringBuilder();
int totalN = 0;
var missingAll = new System.Collections.Generic.HashSet<string>();

// ─── POST-REFACTOR PATH: CollectAll.Run(RoadCanvasDoc, RoadPartLibrary) → CollectResult ───
var tCollectAll = FindType(ns + ".CollectAll");
var tDoc = FindType(ns + ".RoadCanvasDoc");
if (tCollectAll != null && tDoc != null)
{
    var so = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.ScriptableObject>(
        "Assets/_Project/Features/_Gameplay_sm6/RoadCanvasSaves/" + LEVEL + "_RoadCanvas.asset");
    if (so == null) return "CANVAS SO NOT FOUND";

    var FP = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public;
    var FI = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
    var loadMethod = tDoc.GetMethod("FromSave", FP);
    object doc;
    if (loadMethod != null) doc = loadMethod.Invoke(null, new object[]{ so });
    else {
        doc = System.Activator.CreateInstance(tDoc);
        var rf = tDoc.GetMethod("ReadFrom", FI);
        if (rf != null) rf.Invoke(doc, new object[]{ so });
        else return "POST-REFACTOR: RoadCanvasDoc has no FromSave or ReadFrom";
    }
    var tLib = FindType(ns + ".RoadPartLibrary");
    object library = null;
    var libProp = tDoc.GetProperty("Library") ?? tDoc.GetProperty("library");
    if (libProp != null) library = libProp.GetValue(doc);
    if (library == null) {
        var gf = so.GetType().GetField("libraryGuid");
        if (gf != null) { string g = (string)gf.GetValue(so), p = UnityEditor.AssetDatabase.GUIDToAssetPath(g);
            if (!string.IsNullOrEmpty(p)) library = UnityEditor.AssetDatabase.LoadAssetAtPath(p, tLib); }
    }
    if (library == null) return "POST-REFACTOR: RoadPartLibrary not found";
    var runMethod = tCollectAll.GetMethod("Run", FP);
    if (runMethod == null) return "POST-REFACTOR: CollectAll.Run not found";
    object result = runMethod.GetParameters().Length == 2
        ? runMethod.Invoke(null, new object[]{ doc, library })
        : runMethod.Invoke(null, new object[]{ doc, library, true });

    var resultType = result.GetType();

    // Read originCell from doc
    var ocProp = tDoc.GetProperty("OriginCell") ?? tDoc.GetProperty("originCell");
    UnityEngine.Vector2Int oc = default;
    if (ocProp != null) oc = (UnityEngine.Vector2Int)ocProp.GetValue(doc);

    // Extract placement lists from CollectResult and emit CSV
    foreach (string ln in new[]{ "Road", "Road2", "Path", "Highway", "HwDecor" })
    {
        var field = resultType.GetField(ln) ?? resultType.GetField(ln,
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
        if (field == null) continue;
        var list = field.GetValue(result);
        if (list == null) continue;
        System.Reflection.FieldInfo fX = null, fY = null, fP = null, fYaw = null;
        foreach (var item in (System.Collections.IEnumerable)list)
        {
            var ity = item.GetType();
            if (fX == null) { fX = ity.GetField("x") ?? ity.GetField("X") ?? ity.GetField("Item1");
                fY = ity.GetField("y") ?? ity.GetField("Y") ?? ity.GetField("Item2");
                fP = ity.GetField("Prefab") ?? ity.GetField("prefab") ?? ity.GetField("Item3");
                fYaw = ity.GetField("Yaw") ?? ity.GetField("yaw") ?? ity.GetField("Item4"); }
            if (fX == null) break;
            var prefab = (UnityEngine.GameObject)fP.GetValue(item);
            if (prefab == null) continue;
            sb.AppendLine(string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0},{1},{2},{3},{4}",
                ln, prefab.name, (float)fX.GetValue(item) + oc.x, (float)fY.GetValue(item) + oc.y,
                ((int)System.Math.Round((float)fYaw.GetValue(item))) % 360));
            totalN++;
        }
    }

    // Missing set
    var missingField = resultType.GetField("Missing");
    if (missingField != null)
    {
        var ms = missingField.GetValue(result) as System.Collections.Generic.HashSet<string>;
        if (ms != null) foreach (var m in ms) missingAll.Add(m);
    }

    System.IO.File.WriteAllText(OUT, sb.ToString());
    return "OK (post-refactor) placements=" + totalN + " missing=" + string.Join("|", missingAll) + " -> " + OUT;
}

// ─── PRE-REFACTOR PATH: reflect on private members of VisualRoadBuilderTool ───

var F = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
var FS = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic;
var t = FindType(ns + ".VisualRoadBuilderTool");
if (t == null) return "TYPE NOT FOUND (editor assembly not compiled?)";
var soAsset = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.ScriptableObject>(
    "Assets/_Project/Features/_Gameplay_sm6/RoadCanvasSaves/" + LEVEL + "_RoadCanvas.asset");
if (soAsset == null) return "CANVAS SO NOT FOUND";

var tool = UnityEngine.ScriptableObject.CreateInstance(t);
try {
    t.GetMethod("ReadFrom", F).Invoke(tool, new object[]{ soAsset });

    // Edge lists
    var edges       = (System.Collections.Generic.List<int>)t.GetField("_edges", F).GetValue(tool);
    var hwEdges     = (System.Collections.Generic.List<int>)t.GetField("_highwayEdges", F).GetValue(tool);
    var hwDecorEdg  = (System.Collections.Generic.List<int>)t.GetField("_hwDecorEdges", F).GetValue(tool);
    var road2Edges  = (System.Collections.Generic.List<int>)t.GetField("_road2Edges", F).GetValue(tool);
    var pathEdges   = (System.Collections.Generic.List<int>)t.GetField("_pathEdges", F).GetValue(tool);
    var stations    = (System.Collections.Generic.List<int>)t.GetField("_stations", F).GetValue(tool);
    var parkings    = (System.Collections.Generic.List<int>)t.GetField("_parkings", F).GetValue(tool);

    var oc = (UnityEngine.Vector2Int)t.GetField("_originCell", F).GetValue(tool);

    // Placement tuple type: (float x, float y, GameObject prefab, float yaw, Vector3 scaleMul)
    // Apron tuple type:     (RoadTilePart part, float x, float y, float yaw)

    // === 1. Build masks ===
    var mBuildMasks = t.GetMethod("BuildMasks", F);
    var mBuildLegacy = t.GetMethod("BuildLegacyMasksFromEdges", F);
    int[] roadMasks = (int[])mBuildMasks.Invoke(tool, new object[]{ edges });
    int[] road2Masks = (int[])mBuildMasks.Invoke(tool, new object[]{ road2Edges });
    int[] hwMasks = (int[])mBuildMasks.Invoke(tool, new object[]{ hwEdges });
    int[] roadMasksLegacy = (int[])mBuildLegacy.Invoke(tool, new object[]{ edges });
    int[] road2MasksLegacy = (int[])mBuildLegacy.Invoke(tool, new object[]{ road2Edges });

    // Create intermediate containers via Activator (private nested types)
    object NewParamType(System.Reflection.MethodInfo method, int paramIndex)
    {
        return System.Activator.CreateInstance(method.GetParameters()[paramIndex].ParameterType);
    }

    // Get method references
    var mCollectStationRoad = t.GetMethod("CollectStationRoadPlacements", F);
    var mCollectStationRoad2 = t.GetMethod("CollectStationRoad2Placements", F);
    var mCollectParkingRoad = t.GetMethod("CollectParkingRoadKerb", F);
    var mCollectParkingRoad2 = t.GetMethod("CollectParkingRoad2Kerb", F);
    var mCollectBlockEdgeFills = t.GetMethod("CollectBlockEdgeFills", F);
    var mCollectHighway = t.GetMethod("CollectHighwayPlacements", F);
    var mCollectRoad2 = t.GetMethod("CollectRoad2Placements", F);
    var mCollectRoad = t.GetMethod("CollectRoadPlacements", F);
    var mCollectPath = t.GetMethod("CollectPathPlacements", F);
    var mApplyApron = t.GetMethod("ApplyApronPlain", F | FS);
    var mAddStraight = t.GetMethod("AddStraightTiles", F);
    var mAddRoad2Straight = t.GetMethod("AddRoad2StraightTiles", F);
    var mAddRoad2Fillers = t.GetMethod("AddRoad2ApronFillers", F);
    var mDedupeIsolated = t.GetMethod("DedupeIsolatedStraightKeys", F);
    var mJunctionTilePrefab = t.GetMethod("JunctionTilePrefab", F);

    System.Type Nested(string name) => t.GetNestedType(name, System.Reflection.BindingFlags.NonPublic) ?? FindType(ns + "." + name);
    object New(System.Type ty) => System.Activator.CreateInstance(ty);
    object NewParam(System.Reflection.MethodInfo m, int i) => New(m.GetParameters()[i].ParameterType);

    var suppressed = New(Nested("BlockSuppression")); var suppressed2 = New(suppressed.GetType());
    var skin = New(Nested("BlockRoadSkin")); var skin2 = New(skin.GetType());

    var placementListType = typeof(System.Collections.Generic.List<>).MakeGenericType(
        mCollectRoad.GetParameters()[4].ParameterType.GenericTypeArguments[0]);
    var roadPlacements = New(placementListType); var road2Placements = New(placementListType);
    var pathPlacements = New(placementListType); var highwayPlacements = New(placementListType);

    var apronType = mCollectStationRoad.GetParameters()[3].ParameterType;
    var stationRoads = New(apronType); var station2Roads = New(apronType);
    var parkingRoads = New(apronType); var parking2Roads = New(apronType);

    var stripType = mCollectStationRoad.GetParameters()[4].ParameterType;
    var blockStrips = New(stripType); var blockStrips2 = New(stripType);

    var efType = mCollectBlockEdgeFills.GetParameters()[2].ParameterType;
    var blockEdgeHalves = New(efType); var blockEdgeFulls = New(efType);
    var blockEdgeHalves2 = New(efType); var blockEdgeFulls2 = New(efType);

    var rampSuppressed = new System.Collections.Generic.HashSet<int>();
    var road2Blocks = new System.Collections.Generic.HashSet<int>();
    var missing = new System.Collections.Generic.HashSet<string>();

    // === 2–5. Station road placements (type-1 and type-2) ===
    mCollectStationRoad.Invoke(tool, new object[]{ stations, roadMasks, suppressed, stationRoads, blockStrips, skin, missing, true });
    mCollectStationRoad2.Invoke(tool, new object[]{ stations, road2Masks, suppressed2, station2Roads, blockStrips2, skin2, missing, true, road2Blocks });
    mCollectParkingRoad.Invoke(tool, new object[]{ parkings, roadMasks, suppressed, parkingRoads, blockStrips, skin, missing, true });
    mCollectParkingRoad2.Invoke(tool, new object[]{ parkings, road2Masks, suppressed2, parking2Roads, blockStrips2, skin2, missing, true, road2Blocks });

    // === 6–7. Block edge fills ===
    mCollectBlockEdgeFills.Invoke(tool, new object[]{ roadMasksLegacy, blockStrips, blockEdgeHalves, blockEdgeFulls });
    mCollectBlockEdgeFills.Invoke(tool, new object[]{ road2MasksLegacy, blockStrips2, blockEdgeHalves2, blockEdgeFulls2 });

    // === 8. Highway (returns rampSuppressed2) ===
    var rampSuppressed2 = (System.Collections.Generic.HashSet<int>)
        mCollectHighway.Invoke(tool, new object[]{ hwMasks, roadMasksLegacy, rampSuppressed, highwayPlacements, roadPlacements, missing });

    // === 9. Road2 ===
    mCollectRoad2.Invoke(tool, new object[]{ road2Edges, hwMasks, suppressed2, rampSuppressed2, road2Placements, missing, skin2 });

    // === 10. Road (type-1) ===
    mCollectRoad.Invoke(tool, new object[]{ edges, hwMasks, suppressed, rampSuppressed, roadPlacements, missing, skin });

    // === 11. Path ===
    mCollectPath.Invoke(tool, new object[]{ pathEdges, pathPlacements, missing });

    // Helper: ApplyApron + push apron tiles into a placement list via reflection
    void PushApron(object apronList, object skinObj, object destPlacements)
    {
        mApplyApron.Invoke(tool, new object[]{ apronList, skinObj });
        var addMethod = destPlacements.GetType().GetMethod("Add");
        var tupleType = destPlacements.GetType().GenericTypeArguments[0];
        foreach (var item in (System.Collections.IEnumerable)apronList)
        {
            var ity = item.GetType();
            var prefab = (UnityEngine.GameObject)mJunctionTilePrefab.Invoke(tool,
                new object[]{ ity.GetField("Item1").GetValue(item) });
            if (prefab == null) continue;
            var tuple = System.Activator.CreateInstance(tupleType);
            tupleType.GetField("Item1").SetValue(tuple, (float)ity.GetField("Item2").GetValue(item));
            tupleType.GetField("Item2").SetValue(tuple, (float)ity.GetField("Item3").GetValue(item));
            tupleType.GetField("Item3").SetValue(tuple, prefab);
            tupleType.GetField("Item4").SetValue(tuple, (float)ity.GetField("Item4").GetValue(item));
            tupleType.GetField("Item5").SetValue(tuple, UnityEngine.Vector3.one);
            addMethod.Invoke(destPlacements, new object[]{ tuple });
        }
    }

    // Helper: iterate block edge halves/fulls and call AddStraightTiles / AddRoad2StraightTiles
    void RunEdgeFills(object halves, object fulls, System.Reflection.MethodInfo addMethod,
                      object destPlacements, object skinObj)
    {
        foreach (var item in (System.Collections.IEnumerable)halves)
        {
            var ity = item.GetType();
            addMethod.Invoke(tool, new object[]{ destPlacements,
                (float)ity.GetField("Item1").GetValue(item), (float)ity.GetField("Item2").GetValue(item),
                (float)ity.GetField("Item3").GetValue(item), false, missing,
                (int)ity.GetField("Item4").GetValue(item), skinObj });
        }
        foreach (var item in (System.Collections.IEnumerable)fulls)
        {
            var ity = item.GetType();
            addMethod.Invoke(tool, new object[]{ destPlacements,
                (float)ity.GetField("Item1").GetValue(item), (float)ity.GetField("Item2").GetValue(item),
                (float)ity.GetField("Item3").GetValue(item), true, missing,
                (int)ity.GetField("Item4").GetValue(item), skinObj });
        }
    }

    // === 12–15. Apron plain → push into placements ===
    PushApron(stationRoads, skin, roadPlacements);
    PushApron(station2Roads, skin2, road2Placements);
    PushApron(parkingRoads, skin, roadPlacements);
    PushApron(parking2Roads, skin2, road2Placements);

    // === 16. Block edge straight passes (road type-1) ===
    RunEdgeFills(blockEdgeHalves, blockEdgeFulls, mAddStraight, roadPlacements, skin);

    // === 17. Dedupe isolated straight keys ===
    mDedupeIsolated.Invoke(tool, new object[]{ roadPlacements });

    // === 18. Block edge straight passes (road2) ===
    RunEdgeFills(blockEdgeHalves2, blockEdgeFulls2, mAddRoad2Straight, road2Placements, skin2);

    // === 19. Road2 apron fillers ===
    mAddRoad2Fillers.Invoke(tool, new object[]{ road2Placements, blockStrips2, missing });

    // === Emit CSV: layer,prefabName,localX,localZ,yaw ===
    void EmitPlacements(string layer, object list)
    {
        foreach (var item in (System.Collections.IEnumerable)list)
        {
            var ity = item.GetType();
            var prefab = (UnityEngine.GameObject)ity.GetField("Item3").GetValue(item);
            if (prefab == null) continue;
            float x = (float)ity.GetField("Item1").GetValue(item);
            float y = (float)ity.GetField("Item2").GetValue(item);
            float yaw = (float)ity.GetField("Item4").GetValue(item);
            sb.AppendLine(string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0},{1},{2},{3},{4}",
                layer, prefab.name, x + oc.x, y + oc.y, ((int)System.Math.Round(yaw)) % 360));
            totalN++;
        }
    }

    EmitPlacements("Road", roadPlacements);
    EmitPlacements("Road2", road2Placements);
    EmitPlacements("Path", pathPlacements);
    EmitPlacements("Highway", highwayPlacements);

    System.IO.File.WriteAllText(OUT, sb.ToString());
    return "OK (pre-refactor) placements=" + totalN + " missing=" + string.Join("|", missing) + " -> " + OUT;
} finally {
    UnityEngine.Object.DestroyImmediate(tool);
}
