#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace EZG.TechnicalArt.VisualRoadBuilder
{
    /// <summary>EnsureRoadSprites + ForceReloadSprites + ReloadSpriteCaches + RoadCellIconSpanCells.
    /// Quản lý vòng đời sprite piece nạp từ _road_plan.psd.</summary>
    internal sealed class SpriteLoader
    {
        private readonly VisualRoadBuilderTool _host;

        internal SpriteLoader(VisualRoadBuilderTool host) => _host = host;

        /// <summary>Asset path của atlas đường, lấy từ <see cref="RoadPartLibrary.roadPlanAtlas"/>.
        /// Trả "" khi library hoặc atlas chưa gán.</summary>
        internal string RoadPlanAtlasPath
        {
            get
            {
                var lib = _host.GetLibrary();
                return lib != null && lib.roadPlanAtlas != null
                    ? AssetDatabase.GetAssetPath(lib.roadPlanAtlas)
                    : string.Empty;
            }
        }

        /// <summary>Bề ngang (ô) mà 1 ô đường ghép chiếm KỂ CẢ vỉa hè — icon toolbar thu theo số này
        /// để rim không bị cắt. Thân slice nằm dọc trục pivot (= chiều rộng rect) nên bề ngang đường
        /// là 2 lần slice dài nhất; dọc trục đường luôn đúng 1 ô (2 cột × 0.5 ô).</summary>
        internal float RoadCellIconSpanCells
        {
            get
            {
                float widest = 0.5f;
                Sprite side = _host.SpTileSide;
                Sprite sideRim = _host.SpTileSideRim;
                if (side != null) widest = Mathf.Max(widest, side.rect.width / TilePartRegistry.SpritePixelsPerCell);
                if (sideRim != null) widest = Mathf.Max(widest, sideRim.rect.width / TilePartRegistry.SpritePixelsPerCell);
                return Mathf.Max(1f, widest * 2f);
            }
        }

        /// <summary>Nạp sprite piece từ _road_plan.psd cho MỌI ô còn trống (override ở tab Setup được giữ).
        /// Không có art → ô đó vẫn null (lớp tương ứng bỏ vẽ, không fallback ô màu).</summary>
        internal void EnsureRoadSprites()
        {
            // Required: mọi slice ĐÃ có trong psd (kể cả Road_0.5x1_center — D8). Road2 curve/curve_rim/
            // hway_to_road2, PATH path_side/path_center/path_curve/path_turn CHƯA có art nên KHÔNG đưa
            // vào đây — nếu vào, chain && không bao giờ true và hàm re-scan toàn bộ atlas mỗi lần gọi
            // (mỗi repaint), một regression hiệu năng editor thật (P7).
            if (_host.SpTileSide != null && _host.SpTileSideRim != null && _host.SpTileCurve != null
                && _host.SpTileCurveRim != null && _host.SpTileCenter != null
                && _host.SpTileTurn != null && _host.SpTileTurnRim != null
                && _host.SpTileTurn1x1 != null && _host.SpTileTurn1x1Rim != null
                && _host.SpTileTurn3x3 != null && _host.SpTileTurn3x3Rim != null
                && _host.SpHighway != null && _host.SpHighwayRim != null && _host.SpRampHway != null
                && _host.SpStationArea != null && _host.SpParkingArea != null
                && _host.SpRoad2CenterFiller != null) return;
            string path = RoadPlanAtlasPath;
            if (string.IsNullOrEmpty(path)) return;
            foreach (Object obj in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                if (obj is not Sprite s) continue;
                switch (s.name)
                {
                    case "Road_1x1_side":     _host.SetSprite(ref _host._spTileSide, s);       break;
                    case "Road_1x1_side_rim": _host.SetSprite(ref _host._spTileSideRim, s);    break;
                    case "Road_1x1_curve":    _host.SetSprite(ref _host._spTileCurve, s);      break;
                    case "Road_1x1_curve_rim": _host.SetSprite(ref _host._spTileCurveRim, s);  break;
                    case "Road_1x1_center":   _host.SetSprite(ref _host._spTileCenter, s);     break;
                    case "Road_2x2_turn":     _host.SetSprite(ref _host._spTileTurn, s);       break;
                    case "Road_2x2_turn_rim": _host.SetSprite(ref _host._spTileTurnRim, s);    break;
                    case "Road_1x1_turn":     _host.SetSprite(ref _host._spTileTurn1x1, s);    break;
                    case "Road_1x1_turn_rim": _host.SetSprite(ref _host._spTileTurn1x1Rim, s); break;
                    case "Road_3x3_turn":     _host.SetSprite(ref _host._spTileTurn3x3, s);    break;
                    case "Road_3x3_turn_rim": _host.SetSprite(ref _host._spTileTurn3x3Rim, s); break;
                    case "Highway_1x2":     _host.SetSprite(ref _host._spHighway, s);          break;
                    case "Highway_1x2_rim": _host.SetSprite(ref _host._spHighwayRim, s);       break;
                    case "hway_to_road": _host.SetSprite(ref _host._spRampHway, s);            break;
                    case "station_area":   _host.SetSprite(ref _host._spStationArea, s);       break;
                    case "parking_area":   _host.SetSprite(ref _host._spParkingArea, s);       break;
                    // Road 2 (D8/D9): Road_0.5x1_center đã có art (reuse); curve/curve_rim/ramp thì
                    // chưa — case vẫn wire sẵn để tự nạp ngay khi psd bổ sung slice, không đổi code.
                    case "Road_0.5x1_center": _host.SetSprite(ref _host._spRoad2CenterFiller, s); break;
                    case "road2_curve":       _host.SetSprite(ref _host._spRoad2Curve, s);         break;
                    case "road2_curve_rim":   _host.SetSprite(ref _host._spRoad2CurveRim, s);      break;
                    case "hway_to_road2":     _host.SetSprite(ref _host._spRampHway2, s);          break;
                    case "path_side":         _host.SetSprite(ref _host._spPathSide, s);           break;
                    case "path_center":       _host.SetSprite(ref _host._spPathCenter, s);         break;
                    case "path_curve":        _host.SetSprite(ref _host._spPathCurve, s);          break;
                    case "path_turn":         _host.SetSprite(ref _host._spPathTurn, s);           break;
                }
            }
        }

        /// <summary>Import lại PSD từ disk (giữ nguyên slice + custom pivot trong .meta) rồi vứt cache.
        /// Dùng khi Unity chưa kịp thấy file đổi, hoặc canvas vẫn lì ra art cũ.</summary>
        internal void ForceReloadSprites()
        {
            string path = RoadPlanAtlasPath;
            if (!string.IsNullOrEmpty(path))
                AssetDatabase.ImportAsset(path,
                    ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            ReloadSpriteCaches();
        }

        internal void ReloadSpriteCaches()
        {
            ClearRoadPieceCache();
            _host.GetSpriteTextureCache()?.ClearReadableCache();
            EnsureRoadSprites();
            _host.Repaint();
        }

        private void ClearRoadPieceCache()
        {
            _host.GetSpriteTextureCache()?.ClearPieceCache();
        }

        /// <summary>Mọi window đang mở tự nạp lại khi PSD được import (save từ Photoshop, đổi slice…).</summary>
        internal static void OnAssetsImported(string[] importedPaths)
        {
            foreach (VisualRoadBuilderTool window in Resources.FindObjectsOfTypeAll<VisualRoadBuilderTool>())
            {
                var loader = window.GetSpriteLoader();
                if (loader == null) continue;
                string path = loader.RoadPlanAtlasPath;
                if (string.IsNullOrEmpty(path) || System.Array.IndexOf(importedPaths, path) < 0) continue;
                loader.ReloadSpriteCaches();
            }
        }
    }
}
#endif
