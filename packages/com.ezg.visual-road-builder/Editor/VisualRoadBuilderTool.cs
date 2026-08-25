#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace EZG.TechnicalArt.VisualRoadBuilder
{
    /// <summary>
    /// Tool xếp đường nhanh: hiển thị lưới ô (mặc định 50x50, config được), kéo CHUỘT TRÁI
    /// để nối các điểm kề nhau (chỉ ngang/dọc), kéo CHUỘT PHẢI để xoá. Ấn Apply → tool tính số
    /// nhánh tại mỗi điểm rồi GHÉP mảnh Straight/Turn/T/Cross từ các ô modular trong
    /// <see cref="RoadPartLibrary"/>, xoay đúng hướng và đặt dưới node "RoadParent" của một
    /// LEVEL PREFAB; mỗi điểm lưới cách nhau
    /// mỗi điểm lưới cách nhau 1 đơn vị world (CỐ ĐỊNH). Đầu/cuối đường để hở bằng mảnh Straight.
    /// DỮ LIỆU map là NGUỒN DUY NHẤT ở file SO <see cref="RoadCanvasSave"/> (1 file / Level Prefab
    /// trong RoadCanvasSaves/) — Apply bake mesh vào RoadParent + ghi file SO; Load đọc lại file SO.
    ///
    /// Class chia thành nhiều file partial theo chức năng (mỗi file một việc): state/lifecycle ở đây,
    /// UI panel, canvas, input từng mode, model edge/station, drawing, solver bake ở các file .*.cs.
    /// </summary>
    public sealed partial class VisualRoadBuilderTool : EditorWindow
    {
        private const string MenuPath = "Tools/EZG Technical Art/Visual Road Builder/Editor";

        [SerializeField] private RoadPartLibrary _library;
        [SerializeField] private RoadCanvasDoc _doc = new();
        [SerializeField] private ViewState _view = new();
        [SerializeField] private ApplyTarget _applyTarget = new();

        [MenuItem(MenuPath)]
        private static void OpenWindow()
        {
            var window = GetWindow<VisualRoadBuilderTool>("Visual Road Builder");
            window.minSize = new Vector2(740f, 520f);
            window.Show();
        }

        private void OnEnable()
        {
            wantsMouseMove = true; // để vẽ ghost station theo hover chuột
            DiscoverLibrary();
            ApplyDebugBoundaryDefault();
            LoadDebugBoundaryAlpha();
            MigrateWindowData();
            InitHistoryAndAutosave();
        }

        /// <summary>Đoán Part Library cho window CHƯA gán gì. Mọi field của window (kể cả _library và
        /// 24 ô sprite) chỉ nằm trong file layout của MỘT máy, nên window mở lần đầu ở máy khác luôn
        /// trắng: không có library thì không ra atlas path, và mọi nút "nạp lại từ PSD" im lặng không
        /// làm gì. Đúng 1 RoadPartLibrary trong project thì nhận luôn — cùng cách
        /// <c>DiscoverCanvasSaveDir</c> đoán save folder. Nhiều hơn 1 thì để user tự chọn.</summary>
        private void DiscoverLibrary()
        {
            if (_library != null) return;
            string[] guids = AssetDatabase.FindAssets("t:RoadPartLibrary");
            if (guids.Length != 1) return;
            _library = AssetDatabase.LoadAssetAtPath<RoadPartLibrary>(
                AssetDatabase.GUIDToAssetPath(guids[0]));
        }

        private void OnDisable()
        {
            TeardownHistoryAndAutosave();
        }

        private void OnGUI()
        {
            HandleUndoRedoShortcuts();
            HandleGlobalShortcuts();
            TrackShiftHeld();
            float paneWidth = Mathf.Max(220f, position.width - ControlColumnWidth - 6f);
            EditorGUILayout.BeginHorizontal(GUILayout.ExpandHeight(true));
            DrawControlColumn();
            DrawCanvasPane(paneWidth);
            EditorGUILayout.EndHorizontal();
            DrawStatusBar();
            TrackCanvasState();
        }
    }
}
#endif
