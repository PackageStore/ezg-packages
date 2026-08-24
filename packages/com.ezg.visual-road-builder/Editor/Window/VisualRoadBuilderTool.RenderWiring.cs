#if UNITY_EDITOR
namespace EZG.TechnicalArt.VisualRoadBuilder
{
    /// <summary>Dựng sẵn (lazy) đồ thị lớp vẽ: registry ô → cache texture → tầng vẽ tile → 4 renderer
    /// sprite theo lớp. Cửa ngõ DUY NHẤT để window lấy các lớp này, nên không nơi nào tự new thêm bản
    /// thứ hai (cache texture bị nhân đôi = tốn RAM + art cũ lì lại).
    ///
    /// Chỉ gồm phần LÁ của đồ thị — mấy renderer nhận delegate (Hover/Block/Canvas) nối ở bước dọn
    /// file cũ, khi các hàm chúng cần đã có nhà mới ổn định.</summary>
    public sealed partial class VisualRoadBuilderTool
    {
        [System.NonSerialized] private TilePartRegistry _tilePartRegistry;
        [System.NonSerialized] private RoadTileDrawing _roadTileDrawing;
        [System.NonSerialized] private RoadSpriteRenderer _roadSpriteRenderer;
        [System.NonSerialized] private HighwaySpriteRenderer _highwaySpriteRenderer;
        [System.NonSerialized] private Road2SpriteRenderer _road2SpriteRenderer;
        [System.NonSerialized] private PathSpriteRenderer _pathSpriteRenderer;
        [System.NonSerialized] private DebugBoundaryCollector _debugBoundaryCollector;

        /// <summary>Registry CHỤP sprite theo giá trị lúc dựng, nên mọi ô nạp thêm sau đó (PSD import
        /// muộn, gán tay ở tab Setup) sẽ không tới được bản đã dựng — <see cref="InvalidateTileParts"/>
        /// phải huỷ nó mỗi lần một ô được gán.</summary>
        internal TilePartRegistry EnsureTileParts() => _tilePartRegistry ??= new TilePartRegistry(
            _library,
            _spTileSide, _spTileSideRim, _spTileCurve, _spTileCurveRim,
            _spTileCenter, _spTileTurn, _spTileTurnRim,
            _spTileTurn1x1, _spTileTurn1x1Rim,
            _spTileTurn3x3, _spTileTurn3x3Rim,
            _spHighway, _spHighwayRim,
            _spRoad2Curve, _spRoad2CurveRim, _spRoad2CenterFiller,
            _spPathSide, _spPathCenter, _spPathCurve, _spPathTurn);

        /// <summary>Vứt registry + mọi renderer giữ nó, để lần vẽ sau dựng lại theo bộ sprite mới.
        /// Cache texture KHÔNG bị vứt ở đây (nó key theo instance Sprite, tự đúng).</summary>
        internal void InvalidateTileParts()
        {
            _tilePartRegistry = null;
            _roadTileDrawing = null;
            _roadSpriteRenderer = null;
            _highwaySpriteRenderer = null;
            _road2SpriteRenderer = null;
            _pathSpriteRenderer = null;
            _debugBoundaryCollector = null;
        }

        internal RoadTileDrawing EnsureRoadTileDrawing() =>
            _roadTileDrawing ??= new RoadTileDrawing(EnsureTileParts(), GetSpriteTextureCache());

        internal RoadSpriteRenderer EnsureRoadSpriteRenderer() =>
            _roadSpriteRenderer ??= new RoadSpriteRenderer(
                EnsureRoadTileDrawing(), EnsureTileParts(), GetSpriteTextureCache());

        internal HighwaySpriteRenderer EnsureHighwaySpriteRenderer() =>
            _highwaySpriteRenderer ??= new HighwaySpriteRenderer(
                EnsureRoadTileDrawing(), EnsureTileParts(), GetSpriteTextureCache());

        internal Road2SpriteRenderer EnsureRoad2SpriteRenderer() =>
            _road2SpriteRenderer ??= new Road2SpriteRenderer(
                EnsureRoadTileDrawing(), EnsureTileParts(), GetSpriteTextureCache());

        internal PathSpriteRenderer EnsurePathSpriteRenderer() =>
            _pathSpriteRenderer ??= new PathSpriteRenderer(EnsureRoadTileDrawing(), EnsureTileParts());

        internal DebugBoundaryCollector EnsureDebugBoundaryCollector() =>
            _debugBoundaryCollector ??= new DebugBoundaryCollector(EnsureTileParts());

        // ── Lớp vẽ + tool chỉ cần ctx/styles (không nhận delegate) ──────────────────────────────
        // Dùng CHUNG EnsureToolCtx()/EnsureStyles() đã có ở các file shim — KHÔNG new bản thứ hai,
        // vì ToolContext giữ _doc/_view theo tham chiếu và style cache dựng lại mỗi domain reload.

        [System.NonSerialized] private GridRenderer _gridRenderer;
        [System.NonSerialized] private TileRenderer _tileRenderer;
        [System.NonSerialized] private DecorRenderer _decorRenderer;
        [System.NonSerialized] private CropOverlayRenderer _cropOverlayRenderer;
        [System.NonSerialized] private DebugBoundaryRenderer _debugBoundaryRenderer;
        // DecorState là [Serializable] + [SerializeField] bên trong: phải là FIELD SERIALIZE của window
        // (như _doc/_view), không phải instance lazy [NonSerialized] — nếu lazy thì library decor,
        // density, area-mode mất sạch sau mỗi domain reload.
        [UnityEngine.SerializeField] private DecorState _decorState = new();
        [System.NonSerialized] private PanTool _panTool;
        [System.NonSerialized] private EraserTool _eraserTool;
        [System.NonSerialized] private StationTool _stationTool;
        [System.NonSerialized] private CropTool _cropTool;
        [System.NonSerialized] private DecorTool _decorTool;

        internal GridRenderer EnsureGridRenderer() =>
            _gridRenderer ??= new GridRenderer(EnsureToolCtx(), EnsureStyles());

        internal TileRenderer EnsureTileRenderer() =>
            _tileRenderer ??= new TileRenderer(EnsureToolCtx());

        internal DecorRenderer EnsureDecorRenderer() =>
            _decorRenderer ??= new DecorRenderer(EnsureToolCtx());

        internal CropOverlayRenderer EnsureCropOverlayRenderer() =>
            _cropOverlayRenderer ??= new CropOverlayRenderer(EnsureToolCtx(), EnsureStyles());

        internal DebugBoundaryRenderer EnsureDebugBoundaryRenderer() =>
            _debugBoundaryRenderer ??= new DebugBoundaryRenderer(_view, EnsureStyles());

        internal DecorState EnsureDecorState() => _decorState ??= new DecorState();

        /// <summary>Panel decor cũ giữ state trong field rời trên window; nếu file cũ còn giữ chúng thì
        /// bước dọn phải chuyển sang <see cref="_decorState"/>, KHÔNG để hai bản state song song.</summary>
        internal DecorState DecorStateRef => EnsureDecorState();

        internal PanTool EnsurePanTool() => _panTool ??= new PanTool(EnsureToolCtx());

        internal EraserTool EnsureEraserTool() => _eraserTool ??= new EraserTool(EnsureToolCtx());

        internal StationTool EnsureStationTool() => _stationTool ??= new StationTool(EnsureToolCtx());

        internal CropTool EnsureCropTool() =>
            _cropTool ??= new CropTool(EnsureToolCtx(), EnsureCropOverlayRenderer());

        internal DecorTool EnsureDecorTool() =>
            _decorTool ??= new DecorTool(EnsureToolCtx(), EnsureDecorState());
    }
}
#endif
