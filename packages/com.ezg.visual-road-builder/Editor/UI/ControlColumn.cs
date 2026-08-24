#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace EZG.TechnicalArt.VisualRoadBuilder
{
    /// <summary>Cot control trai: khung cuon + nhom foldout Target (prefab dich) va Grid (luoi).</summary>
    public sealed partial class VisualRoadBuilderTool
    {
        private static readonly string[] LeftTabLabels = { "Build", "Setup", "Debug" };
        [SerializeField] private int _leftTab;

        private void DrawControlColumn()
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(GridConst.ControlColumnWidth), GUILayout.ExpandHeight(true));
            DrawSaveHistoryBar();

            int newTab = GUILayout.Toolbar(_leftTab, LeftTabLabels);
            if (newTab != _leftTab)
            {
                _leftTab = newTab;
                CancelCanvasInteractions();
                GUIUtility.hotControl = 0;
                GUIUtility.keyboardControl = 0;
                EditorGUIUtility.editingTextField = false;
                GUIUtility.ExitGUI();
            }

            float prevLabel = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = 96f;

            _view.ControlScroll = EditorGUILayout.BeginScrollView(_view.ControlScroll, GUILayout.ExpandHeight(true));
            if (_leftTab == 1)
            {
                DrawSetupTab();
            }
            else if (_leftTab == 2)
            {
                DrawDebugTab();
            }
            else
            {
                DrawTargetSection();
                DrawToolsSection();
                DrawDecorSection();
            }
            EditorGUILayout.EndScrollView();

            EditorGUIUtility.labelWidth = prevLabel;
            if (_leftTab == 0) DrawApplyButton();
            EditorGUILayout.EndVertical();
        }

        private void DrawTargetSection()
        {
            _view.FoldTarget = EditorGUILayout.Foldout(_view.FoldTarget, "Target — prefab đích", true, EditorStyles.foldoutHeader);
            if (!_view.FoldTarget) return;
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            _library = (RoadPartLibrary)EditorGUILayout.ObjectField(
                "Part Library", _library, typeof(RoadPartLibrary), false);
            _applyTarget.LevelPrefab = (GameObject)EditorGUILayout.ObjectField(
                new GUIContent("Level Prefab",
                    "Prefab asset để bake mesh + khoá tên file SO map. Load = đọc file SO; Apply = dựng lại " +
                    "mesh node RoadParent + ghi file SO. KHÔNG đụng object gameplay khác trong prefab."),
                _applyTarget.LevelPrefab, typeof(GameObject), false);
            _applyTarget.RoadParentName = EditorGUILayout.TextField(
                new GUIContent("Road Parent",
                    "Tên node chứa đường bên trong prefab (mặc định RoadParent). Không có sẽ được tạo mới."),
                _applyTarget.RoadParentName);
            if (_applyTarget.SaveFolder == null) ResolvedSaveDir();
            EditorGUI.BeginChangeCheck();
            _applyTarget.SaveFolder = (DefaultAsset)EditorGUILayout.ObjectField(
                new GUIContent("Save Folder",
                    "Thư mục chứa file SO map (1 file / Level Prefab). Kéo thư mục từ Project vào đây."),
                _applyTarget.SaveFolder, typeof(DefaultAsset), false);
            if (_applyTarget.SaveFolder != null && !AssetDatabase.IsValidFolder(AssetDatabase.GetAssetPath(_applyTarget.SaveFolder)))
            {
                _applyTarget.SaveFolder = null;
                Debug.LogWarning("[VisualRoadBuilder] Save Folder phải là thư mục, không phải file.");
            }
            if (EditorGUI.EndChangeCheck() && _applyTarget.SaveFolder != null)
                EditorPrefs.SetString(SaveFolderPrefKey, AssetDatabase.GetAssetPath(_applyTarget.SaveFolder));

            if (_library == null)
                EditorGUILayout.HelpBox(
                    "Gán Part Library (Create > EZG Technical Art > Road Part Library) để Apply được.",
                    MessageType.Warning);
            else if (_library.roadPlanAtlas == null)
                EditorGUILayout.HelpBox(
                    "Gán Road Plan Atlas trong Part Library để hiển thị preview đường.",
                    MessageType.Info);
            if (_applyTarget.SaveFolder == null)
                EditorGUILayout.HelpBox(
                    "Gán Save Folder (thư mục chứa file SO map) để Save/Load hoạt động.",
                    MessageType.Warning);

            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(2f);
        }
    }
}
#endif
