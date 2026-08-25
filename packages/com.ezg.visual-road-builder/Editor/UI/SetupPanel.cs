#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace EZG.TechnicalArt.VisualRoadBuilder
{
    /// <summary>Tab Setup: phoi toan bo sprite tilemap dung de VE preview tren canvas.</summary>
    public sealed partial class VisualRoadBuilderTool
    {
        private void DrawSetupTab()
        {
            EnsureRoadSprites();

            EditorGUILayout.HelpBox(
                "Sprite tilemap dùng để VẼ preview trên canvas (không ảnh hưởng prefab bake). " +
                "Đổi sprite bằng ô bên dưới; để trống rồi bấm \"Nạp lại ô trống\" để tự nạp theo tên trong PSD. " +
                "Vẽ lại art trong PSD mà canvas vẫn ra hình cũ → bấm \"Force Reload Sprites\".",
                MessageType.Info);

            DrawSpriteGroup("Road");
            DrawSpriteRow("Side (1x1)", "Road_1x1_side", ref _spTileSide);
            DrawSpriteRow("Side Rim (1x1)", "Road_1x1_side_rim", ref _spTileSideRim);
            DrawSpriteRow("Curve (1x1)", "Road_1x1_curve", ref _spTileCurve);
            DrawSpriteRow("Curve Rim (1x1)", "Road_1x1_curve_rim", ref _spTileCurveRim);
            DrawSpriteRow("Center (1x1)", "Road_1x1_center", ref _spTileCenter);
            DrawSpriteRow("Turn (cua)", "Road_2x2_turn", ref _spTileTurn);
            DrawSpriteRow("Turn Rim (cua)", "Road_2x2_turn_rim", ref _spTileTurnRim);
            DrawSpriteRow("Turn (1x1)", "Road_1x1_turn", ref _spTileTurn1x1);
            DrawSpriteRow("Turn Rim (1x1)", "Road_1x1_turn_rim", ref _spTileTurn1x1Rim);
            DrawSpriteRow("Turn 3x3 (cua lớn Road2)", "Road_3x3_turn", ref _spTileTurn3x3);
            DrawSpriteRow("Turn 3x3 Rim (cua lớn Road2)", "Road_3x3_turn_rim", ref _spTileTurn3x3Rim);
            DrawSpriteRow("Hway→Road (ramp)", "hway_to_road", ref _spRampHway);

            DrawSpriteGroup("Highway");
            DrawSpriteRow("Side (1x2)", "Highway_1x2", ref _spHighway);
            DrawSpriteRow("Side Rim (1x2)", "Highway_1x2_rim", ref _spHighwayRim);

            DrawSpriteGroup("Path");
            DrawSpriteRow("Side", "path_side", ref _spPathSide);
            DrawSpriteRow("Center", "path_center", ref _spPathCenter);
            DrawSpriteRow("Curve", "path_curve", ref _spPathCurve);
            DrawSpriteRow("Turn", "path_turn", ref _spPathTurn);

            DrawSpriteGroup("Building");
            DrawSpriteRow("Station Area", "station_area", ref _spStationArea);
            DrawSpriteRow("Station Area 2", "station_area_2", ref _spStation2Area);
            DrawSpriteRow("Parking Area", "parking_area", ref _spParkingArea);

            EditorGUILayout.Space(4f);
            if (GUILayout.Button("↻ Force Reload Sprites (import lại PSD)", GUILayout.Height(24f)))
                ForceReloadSprites();
            if (GUILayout.Button("Nạp lại ô trống từ PSD"))
                EnsureRoadSprites();
            if (GUILayout.Button("Xoá & nạp lại tất cả từ PSD"))
            {
                _spTileSide = _spTileSideRim = _spTileCurve = _spTileCurveRim = _spTileCenter = null;
                _spTileTurn = _spTileTurnRim = _spTileTurn1x1 = _spTileTurn1x1Rim = null;
                _spTileTurn3x3 = _spTileTurn3x3Rim = null;
                _spHighway = _spHighwayRim = _spRampHway = null;
                _spPathSide = _spPathCenter = _spPathCurve = _spPathTurn = null;
                _spStationArea = _spStation2Area = _spParkingArea = null;
                ClearRoadPieceCache();
                ClearReadableCache();
                EnsureRoadSprites();
            }
            if (GUILayout.Button("Chọn _road_plan.psd trong Project"))
            {
                string path = RoadPlanAtlasPath;
                if (string.IsNullOrEmpty(path)) return;
                var psd = AssetDatabase.LoadMainAssetAtPath(path);
                if (psd != null) EditorGUIUtility.PingObject(psd);
                Selection.activeObject = psd;
            }
        }

        private static void DrawSpriteGroup(string title)
        {
            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
        }

        private void DrawSpriteRow(string label, string spriteName, ref Sprite sprite)
        {
            EditorGUI.BeginChangeCheck();
            var picked = (Sprite)EditorGUILayout.ObjectField(
                new GUIContent(label, spriteName), sprite, typeof(Sprite), false);
            if (EditorGUI.EndChangeCheck())
            {
                sprite = picked;
                ClearRoadPieceCache();
            }
        }

        private void ClearRoadPieceCache()
        {
            foreach (Texture2D tex in _roadPieceTex.Values)
                if (tex != null) UnityEngine.Object.DestroyImmediate(tex);
            _roadPieceTex.Clear();
        }
    }
}
#endif
