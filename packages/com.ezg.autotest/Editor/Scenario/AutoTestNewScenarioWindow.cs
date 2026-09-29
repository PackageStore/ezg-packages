using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Ezg.AutoTest.Editor
{
    /// <summary>
    ///     Cửa sổ nhỏ tạo kịch bản auto test mới: nhập Tên / Nhóm / Mô tả → sinh file từ template, ping file,
    ///     copy sẵn prompt AI vào clipboard (dán cho Claude Code để triển khai). Mở từ menu
    ///     Assets/Create/Ezg/Auto Test Scenario hoặc từ cửa sổ Auto Test (<see cref="Open()" />).
    /// </summary>
    public sealed class AutoTestNewScenarioWindow : EditorWindow
    {
        const string WINDOW_TITLE = "Kịch bản Auto Test mới";
        const string NAME_CONTROL = "ezg_autotest_scenario_name";
        const string DEFAULT_CATEGORY = "Gameplay";
        const string SKILLS_FOLDER = ".claude";
        const float WINDOW_WIDTH = 520f;
        const float WINDOW_HEIGHT = 430f;
        const float DESCRIPTION_HEIGHT = 64f;
        const float PROMPT_HEIGHT = 120f;

        [SerializeField] string _scenarioName = "";
        [SerializeField] string _category = DEFAULT_CATEGORY;
        [SerializeField] string _description = "";
        [SerializeField] bool _installSkill;
        [SerializeField] string _createdPath;
        [SerializeField] string _prompt;
        [SerializeField] string _error;
        [SerializeField] string _skillResult;

        string _gameAssembly;
        string _targetFolder;
        bool _environmentScanned;
        bool _focusRequested;
        Vector2 _promptScroll;

        #region Mở cửa sổ

        /// <summary>Mở cửa sổ (dùng từ menu hoặc cửa sổ chính Auto Test).</summary>
        public static AutoTestNewScenarioWindow Open()
        {
            return Open(null);
        }

        /// <summary>Mở cửa sổ với nhóm điền sẵn (null = giữ nhóm đang nhập).</summary>
        public static AutoTestNewScenarioWindow Open(string presetCategory)
        {
            var window = GetWindow<AutoTestNewScenarioWindow>(true, WINDOW_TITLE, true);
            window.minSize = new Vector2(WINDOW_WIDTH, WINDOW_HEIGHT);
            if (!string.IsNullOrWhiteSpace(presetCategory)) window._category = presetCategory.Trim();
            window._focusRequested = true;
            window._environmentScanned = false;
            return window;
        }

        void OnEnable()
        {
            // Mặc định cài skill khi project đã dùng Claude Code (có thư mục .claude ở gốc project).
            if (string.IsNullOrEmpty(_createdPath))
                _installSkill = Directory.Exists(Path.Combine(ProjectRoot(), SKILLS_FOLDER));
            _environmentScanned = false;
        }

        void ScanEnvironment()
        {
            if (_environmentScanned) return;
            _environmentScanned = true;
            try
            {
                _gameAssembly = AutoTestScaffolder.DetectGameAssembly();
                _targetFolder = AutoTestScaffolder.GetAutoTestsFolderPath();
            }
            catch (Exception e)
            {
                _gameAssembly = null;
                _targetFolder = "?";
                AutoTestLog.Warn("Không dò được assembly game: " + e.Message);
            }
        }

        #endregion

        #region GUI

        void OnGUI()
        {
            ScanEnvironment();
            if (!string.IsNullOrEmpty(_createdPath))
            {
                DrawResult();
                return;
            }

            DrawForm();
        }

        void DrawForm()
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Tạo kịch bản test riêng của game", EditorStyles.boldLabel);
            var assemblyInfo = string.IsNullOrEmpty(_gameAssembly)
                ? "Assembly game: Assembly-CSharp (không có asmdef) — file được bọc #if UNITY_EDITOR || EZG_AUTOTEST."
                : $"Assembly game: {_gameAssembly} — kịch bản nằm trong asmdef <Product>.AutoTests tham chiếu tới nó.";
            EditorGUILayout.HelpBox($"Thư mục: {_targetFolder}\n{assemblyInfo}", MessageType.Info);

            EditorGUILayout.Space(4);
            GUI.SetNextControlName(NAME_CONTROL);
            _scenarioName = EditorGUILayout.TextField(new GUIContent("Tên kịch bản",
                "Tên hiển thị trong cửa sổ + report, vd \"Mua gói kim cương trong Shop\"."), _scenarioName);
            _category = EditorGUILayout.TextField(new GUIContent("Nhóm",
                "Gom kịch bản trong cửa sổ + tên thư mục con, vd \"Shop\", \"Farm\"."), _category);
            EditorGUILayout.LabelField(new GUIContent("Mô tả",
                "Luồng người chơi cần test — AI đọc mô tả này để triển khai."));
            var areaStyle = new GUIStyle(EditorStyles.textArea) { wordWrap = true };
            _description = EditorGUILayout.TextArea(_description ?? "", areaStyle, GUILayout.MinHeight(DESCRIPTION_HEIGHT));

            if (_focusRequested)
            {
                _focusRequested = false;
                EditorGUI.FocusTextInControl(NAME_CONTROL);
            }

            EditorGUILayout.Space(4);
            var hasName = !string.IsNullOrWhiteSpace(_scenarioName);
            if (hasName)
            {
                string preview;
                try
                {
                    preview = AutoTestScaffolder.GetScenarioAssetPath(_scenarioName, _category);
                }
                catch (Exception e)
                {
                    preview = "? (" + e.Message + ")";
                }

                var exists = File.Exists(Path.Combine(ProjectRoot(), preview));
                EditorGUILayout.HelpBox(
                    (exists ? "File đã tồn tại — sẽ mở file cũ, không ghi đè:\n" : "Sẽ tạo:\n") + preview,
                    exists ? MessageType.Warning : MessageType.None);
            }

            _installSkill = EditorGUILayout.ToggleLeft(new GUIContent(
                "Cài/cập nhật skill AI vào .claude/skills/" + AutoTestScaffolder.SKILL_NAME,
                "Để Claude Code trong project này tự biết quy trình viết + chạy kịch bản."), _installSkill);

            if (!string.IsNullOrEmpty(_error)) EditorGUILayout.HelpBox(_error, MessageType.Error);

            GUILayout.FlexibleSpace();
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Huỷ", GUILayout.Width(90))) Close();
                using (new EditorGUI.DisabledScope(!hasName))
                {
                    var submit = GUILayout.Button("Tạo kịch bản", GUILayout.Width(130));
                    var enter = hasName && Event.current.type == EventType.KeyDown &&
                                (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter) &&
                                GUI.GetNameOfFocusedControl() == NAME_CONTROL;
                    if (submit || enter)
                    {
                        // Tạo file + Refresh ngoài OnGUI để không vỡ layout khi AssetDatabase import/compile.
                        EditorApplication.delayCall += CreateNow;
                        if (enter) Event.current.Use();
                    }
                }
            }

            EditorGUILayout.Space(6);
        }

        void DrawResult()
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Đã tạo kịch bản", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(_createdPath, MessageType.Info);
            if (!string.IsNullOrEmpty(_skillResult)) EditorGUILayout.HelpBox(_skillResult, MessageType.None);

            EditorGUILayout.LabelField("Prompt AI (đã copy vào clipboard — dán cho Claude Code):");
            _promptScroll = EditorGUILayout.BeginScrollView(_promptScroll, GUILayout.MinHeight(PROMPT_HEIGHT));
            var areaStyle = new GUIStyle(EditorStyles.textArea) { wordWrap = true };
            EditorGUILayout.SelectableLabel(_prompt ?? "", areaStyle, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();

            GUILayout.FlexibleSpace();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Sao chép lại prompt"))
                {
                    EditorGUIUtility.systemCopyBuffer = _prompt ?? "";
                    ShowNotification(new GUIContent("Đã copy prompt AI"));
                }

                if (GUILayout.Button("Mở file")) OpenCreatedFile();
                if (GUILayout.Button("Chọn trong Project")) PingCreatedFile();
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Tạo kịch bản khác")) ResetForm();
                if (GUILayout.Button("Đóng")) Close();
            }

            EditorGUILayout.Space(6);
        }

        #endregion

        #region Hành động

        void CreateNow()
        {
            if (this == null) return; // cửa sổ đã đóng trước khi delayCall chạy
            _error = null;
            _skillResult = null;
            try
            {
                var path = AutoTestScaffolder.CreateScenario(_scenarioName, _category, _description);
                var hint = string.IsNullOrWhiteSpace(_description)
                    ? _scenarioName.Trim()
                    : _scenarioName.Trim() + " — " + _description.Trim();
                _prompt = AutoTestScaffolder.BuildAiPrompt(path, hint);
                EditorGUIUtility.systemCopyBuffer = _prompt;

                if (_installSkill)
                    _skillResult = AutoTestScaffolder.InstallAiSkill(out var skillPath)
                        ? "Skill AI: " + skillPath
                        : "Không cài được skill AI (thiếu file trong package): " + skillPath;

                _createdPath = path;
                PingCreatedFile();
                ShowNotification(new GUIContent("Đã tạo kịch bản — prompt AI đã copy"));
                AutoTestLog.Info("Đã tạo kịch bản: " + path);
            }
            catch (Exception e)
            {
                _error = e.Message;
                Debug.LogException(e);
            }

            Repaint();
        }

        void PingCreatedFile()
        {
            if (string.IsNullOrEmpty(_createdPath)) return;
            var asset = AssetDatabase.LoadAssetAtPath<Object>(_createdPath);
            if (asset == null) return;
            Selection.activeObject = asset;
            EditorGUIUtility.PingObject(asset);
        }

        void OpenCreatedFile()
        {
            if (string.IsNullOrEmpty(_createdPath)) return;
            var asset = AssetDatabase.LoadAssetAtPath<Object>(_createdPath);
            if (asset != null) AssetDatabase.OpenAsset(asset);
        }

        void ResetForm()
        {
            _scenarioName = "";
            _description = "";
            _createdPath = null;
            _prompt = null;
            _error = null;
            _skillResult = null;
            _focusRequested = true;
            _environmentScanned = false;
        }

        static string ProjectRoot()
        {
            return Path.GetDirectoryName(Application.dataPath) ?? "";
        }

        #endregion
    }
}
