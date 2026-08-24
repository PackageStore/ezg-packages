#if UNITY_EDITOR
using UnityEngine;

namespace EZG.TechnicalArt.VisualRoadBuilder
{
    /// <summary>Ô sprite piece đường + cửa ngõ để các lớp Render/UI đọc chúng.
    /// Field phải nằm TRÊN window (không đẩy sang class thường) vì <c>[SerializeField]</c> là thứ giữ
    /// override gán tay ở tab Setup qua mỗi domain reload; class thường sẽ mất giá trị.</summary>
    public sealed partial class VisualRoadBuilderTool
    {
        // Serialize để override ở tab Setup được giữ lại; ô null sẽ tự nạp từ PSD trong EnsureRoadSprites.
        [SerializeField] internal Sprite _spHighway, _spHighwayRim, _spRampHway;
        // Ô modular dựng mọi mảnh thẳng — pivot slice đặt trùng pivot prefab nên vẽ được bằng ĐÚNG
        // vị trí + yaw mà solver bake ra (DrawTileSprite neo sprite theo pivot như mesh).
        [SerializeField] internal Sprite _spTileSide, _spTileSideRim;
        // Ô cua + ô lõi trong của mảnh giao. Center KHÔNG có rim.
        [SerializeField] internal Sprite _spTileCurve, _spTileCurveRim, _spTileCenter;
        // Ô lòng đường cung của mảnh cua (thay 4 ô center) + vỉa hè cung của nó.
        [SerializeField] internal Sprite _spTileTurn, _spTileTurnRim;
        // Ô lòng đường cung LỚN của mảnh cua Road 2 (mặt cắt x1.5) + vỉa hè cung của nó.
        [SerializeField] internal Sprite _spTileTurn3x3, _spTileTurn3x3Rim;
        // Ô cua nhỏ lấp quarter-cell giữa 2 mảnh giao lệch 1.5 ô (thay cả cặp curve) + vỉa hè của nó.
        [SerializeField] internal Sprite _spTileTurn1x1, _spTileTurn1x1Rim;
        // Road 2 (mặt cắt rộng x1.5, D2): center_filler TÁI DÙNG art Road_0.5x1_center đã có sẵn (D8);
        // curve/curve_rim/ramp CHƯA có art (road2_curve, road2_curve_rim, hway_to_road2) — để trống tới
        // khi psd bổ sung slice, EnsureRoadSprites tự nạp (D4: thiếu chỉ bỏ vẽ, không chặn preview).
        [SerializeField] internal Sprite _spRoad2CenterFiller;
        [SerializeField] internal Sprite _spRoad2Curve, _spRoad2CurveRim, _spRampHway2;
        // Path (lối đi bộ): 4 slice, KHÔNG có rim (D3), chưa có art thì để null
        [SerializeField] internal Sprite _spPathSide, _spPathCenter, _spPathCurve, _spPathTurn;
        // Khối Station/Parking: vẽ art thật thay ô màu.
        [SerializeField] internal Sprite _spStationArea, _spParkingArea;

        [System.NonSerialized] private SpriteLoader _spriteLoader;
        [System.NonSerialized] private SpriteTextureCache _spriteTexCache;

        internal Sprite SpHighway => _spHighway;
        internal Sprite SpHighwayRim => _spHighwayRim;
        internal Sprite SpRampHway => _spRampHway;
        internal Sprite SpTileSide => _spTileSide;
        internal Sprite SpTileSideRim => _spTileSideRim;
        internal Sprite SpTileCurve => _spTileCurve;
        internal Sprite SpTileCurveRim => _spTileCurveRim;
        internal Sprite SpTileCenter => _spTileCenter;
        internal Sprite SpTileTurn => _spTileTurn;
        internal Sprite SpTileTurnRim => _spTileTurnRim;
        internal Sprite SpTileTurn3x3 => _spTileTurn3x3;
        internal Sprite SpTileTurn3x3Rim => _spTileTurn3x3Rim;
        internal Sprite SpTileTurn1x1 => _spTileTurn1x1;
        internal Sprite SpTileTurn1x1Rim => _spTileTurn1x1Rim;
        internal Sprite SpRoad2CenterFiller => _spRoad2CenterFiller;
        internal Sprite SpRoad2Curve => _spRoad2Curve;
        internal Sprite SpRoad2CurveRim => _spRoad2CurveRim;
        internal Sprite SpRampHway2 => _spRampHway2;
        internal Sprite SpPathSide => _spPathSide;
        internal Sprite SpPathCenter => _spPathCenter;
        internal Sprite SpPathCurve => _spPathCurve;
        internal Sprite SpPathTurn => _spPathTurn;
        internal Sprite SpStationArea => _spStationArea;
        internal Sprite SpParkingArea => _spParkingArea;

        internal RoadPartLibrary GetLibrary() => _library;

        internal SpriteLoader GetSpriteLoader() => _spriteLoader ??= new SpriteLoader(this);

        internal SpriteTextureCache GetSpriteTextureCache() => _spriteTexCache ??= new SpriteTextureCache();

        /// <summary>Gán slice vào ô CHỈ KHI ô đang trống — giữ nguyên override gán tay ở tab Setup.
        /// Giữ đúng ngữ nghĩa <c>if (field == null) field = s;</c> của EnsureRoadSprites bản cũ.
        ///
        /// Gán xong PHẢI vứt registry: <see cref="TilePartRegistry"/> chụp sprite theo giá trị lúc
        /// dựng, nên bản đã dựng trước khi PSD nạp sẽ giữ null vĩnh viễn (canvas trắng dù art đã có).</summary>
        internal void SetSprite(ref Sprite field, Sprite value)
        {
            if (field != null) return;
            field = value;
            InvalidateTileParts();
        }
    }
}
#endif
