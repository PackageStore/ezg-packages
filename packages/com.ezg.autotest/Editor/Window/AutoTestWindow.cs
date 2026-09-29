using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Ezg.AutoTest.Editor
{
    /// <summary>
    ///     Cửa sổ Ezg &gt; Auto test system (Ctrl/Cmd+Shift+T): chọn suite/case, chạy, dừng, xem tiến độ trực tiếp,
    ///     kết quả chi tiết (issue, ảnh, log), lịch sử report và cấu hình.
    /// </summary>
    public sealed partial class AutoTestWindow : EditorWindow
    {
        const string PREF_SELECTED = "EZGAutoTest.SelectedSuites";
        const string PREF_TAB = "EZGAutoTest.Tab";
        const float LEFT_WIDTH = 310f;
        const float TOOLBAR_HEIGHT = 24f;
        const double REPAINT_INTERVAL = 0.25;

        enum Tab
        {
            Overview = 0,
            Running = 1,
            Results = 2,
            History = 3,
            Settings = 4
        }

        static readonly string[] TAB_LABELS = { "Tổng quan", "Đang chạy", "Kết quả", "Lịch sử", "Cài đặt" };

        List<AutoTestSuite> _suites = new();
        readonly Dictionary<string, List<AutoTestCase>> _casesBySuite = new();
        readonly HashSet<string> _selected = new();
        readonly HashSet<string> _expanded = new();
        IGameAdapter _adapter;
        string _focusSuite;
        Tab _tab;
        Vector2 _leftScroll;
        Vector2 _rightScroll;
        double _lastRepaint;
        TestRunReport _lastReport;
        string _lastReportFolder;

        [MenuItem("Ezg/Auto test system %#t", false, 0)]
        public static void Open()
        {
            var w = GetWindow<AutoTestWindow>();
            w.titleContent = new GUIContent("EZG Auto Test", AutoTestStyles.Icon("d_PlayButton").image);
            w.minSize = new Vector2(900, 520);
            w.Show();
        }

        #region Lifecycle

        void OnEnable()
        {
            _tab = (Tab)EditorPrefs.GetInt(PREF_TAB, 0);
            var saved = EditorPrefs.GetString(PREF_SELECTED, "static,smoke");
            _selected.Clear();
            foreach (var id in saved.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)) _selected.Add(id);
            Reload();
            AutoTestRunner.Changed += OnRunnerChanged;
            EditorApplication.update += OnUpdate;
        }

        void OnDisable()
        {
            AutoTestRunner.Changed -= OnRunnerChanged;
            EditorApplication.update -= OnUpdate;
            ClearTextureCache();
        }

        void OnRunnerChanged()
        {
            if (AutoTestRunner.Phase == AutoTestRunState.PHASE_DONE) LoadLastReport(true);
            Repaint();
        }

        void OnUpdate()
        {
            if (!AutoTestRunner.IsRunning) return;
            if (EditorApplication.timeSinceStartup - _lastRepaint < REPAINT_INTERVAL) return;
            _lastRepaint = EditorApplication.timeSinceStartup;
            Repaint();
        }

        void Reload()
        {
            var config = AutoTestSettings.Config;
            _suites = AutoTestRegistry.CreateSuites();
            _adapter = AutoTestRegistry.CreateAdapter(config);
            _casesBySuite.Clear();
            foreach (var s in _suites)
                try
                {
                    _casesBySuite[s.Id] = s.BuildCases(new AutoTestBuildContext
                        { Config = config, Game = _adapter, ForListing = true }).ToList();
                }
                catch (Exception e)
                {
                    AutoTestLog.Warn($"Không liệt kê được case của {s.Id}: {e.Message}");
                    _casesBySuite[s.Id] = new List<AutoTestCase>();
                }

            if (_focusSuite == null || _suites.All(s => s.Id != _focusSuite)) _focusSuite = _suites.FirstOrDefault()?.Id;
            LoadLastReport(false);
        }

        void LoadLastReport(bool force)
        {
            var folder = AutoTestRunner.LastReportFolder;
            if (string.IsNullOrEmpty(folder))
            {
                var latest = AutoTestReportHistory.List(AutoTestSettings.Config).FirstOrDefault();
                folder = latest?.folder;
            }

            if (!force && folder == _lastReportFolder && _lastReport != null) return;
            _lastReportFolder = folder;
            _lastReport = string.IsNullOrEmpty(folder) ? null : AutoTestReportHistory.Load(folder);
            OnReportChanged();
        }

        void SaveSelection()
        {
            EditorPrefs.SetString(PREF_SELECTED, string.Join(",", _selected));
        }

        #endregion

        #region GUI

        void OnGUI()
        {
            DrawToolbar();
            var body = new Rect(0, TOOLBAR_HEIGHT, position.width, position.height - TOOLBAR_HEIGHT);
            var left = new Rect(body.x, body.y, LEFT_WIDTH, body.height);
            var right = new Rect(left.xMax + 1, body.y, body.width - LEFT_WIDTH - 1, body.height);
            EditorGUI.DrawRect(new Rect(left.xMax, body.y, 1, body.height),
                AutoTestStyles.Dark ? new Color(0.1f, 0.1f, 0.1f) : new Color(0.6f, 0.6f, 0.6f));

            GUILayout.BeginArea(left);
            DrawSuiteList();
            GUILayout.EndArea();

            GUILayout.BeginArea(right);
            DrawRightPanel();
            GUILayout.EndArea();
        }

        void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar, GUILayout.Height(TOOLBAR_HEIGHT)))
            {
                var running = AutoTestRunner.IsRunning;
                using (new EditorGUI.DisabledScope(running || _selected.Count == 0))
                {
                    if (GUILayout.Button(new GUIContent($" Chạy mục đã chọn ({_selected.Count})",
                            AutoTestStyles.Icon("d_PlayButton").image), EditorStyles.toolbarButton, GUILayout.Width(170)))
                        RunSelected();
                }

                using (new EditorGUI.DisabledScope(running))
                {
                    if (GUILayout.Button(new GUIContent(" Chạy tất cả", AutoTestStyles.Icon("d_Animation.NextKey").image,
                            "Chạy mọi suite đang bật (trừ Device / E2E)"), EditorStyles.toolbarButton, GUILayout.Width(100)))
                        RunAll();
                }

                using (new EditorGUI.DisabledScope(!running))
                {
                    var prev = GUI.backgroundColor;
                    if (running) GUI.backgroundColor = new Color(1f, 0.55f, 0.5f);
                    if (GUILayout.Button(new GUIContent(AutoTestRunner.CancelRequested ? " Đang dừng…" : " Dừng",
                            AutoTestStyles.Icon("d_PreMatQuad").image), EditorStyles.toolbarButton, GUILayout.Width(90)))
                    {
                        if (AutoTestRunner.CancelRequested)
                        {
                            if (EditorUtility.DisplayDialog("Huỷ cứng",
                                    "Runner chưa dừng hẳn. Huỷ cứng (bỏ phần đang chạy, khôi phục dữ liệu)?", "Huỷ cứng",
                                    "Đợi thêm"))
                                AutoTestRunner.ForceReset();
                        }
                        else
                        {
                            AutoTestRunner.Stop();
                        }
                    }

                    GUI.backgroundColor = prev;
                }

                GUILayout.Space(8);
                GUILayout.Label(new GUIContent($"Adapter: {_adapter?.Name ?? "?"}",
                        string.Join("\n", _adapter?.Diagnostics ?? Array.Empty<string>())), EditorStyles.miniLabel);
                GUILayout.FlexibleSpace();

                if (_lastReport != null && GUILayout.Button(new GUIContent(" Report gần nhất",
                        AutoTestStyles.Icon("d_Linked").image), EditorStyles.toolbarButton, GUILayout.Width(120)))
                    OpenHtml(_lastReportFolder);

                if (GUILayout.Button(new GUIContent(AutoTestStyles.Icon("d_Refresh").image, "Tải lại danh sách suite/case"),
                        EditorStyles.toolbarButton, GUILayout.Width(28)))
                    Reload();

                if (GUILayout.Button(new GUIContent(AutoTestStyles.Icon("d__Help").image, "Hướng dẫn sử dụng"),
                        EditorStyles.toolbarButton, GUILayout.Width(28)))
                    OpenDoc("Documentation~/index.md");
            }
        }

        void DrawSuiteList()
        {
            var config = AutoTestSettings.Config;
            GUILayout.Space(4);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label("Bộ test", AutoTestStyles.SectionHeader);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Chọn hết", EditorStyles.miniButtonLeft, GUILayout.Width(62)))
                {
                    foreach (var s in _suites.Where(s => s.Mode != ExecutionMode.Device)) _selected.Add(s.Id);
                    SaveSelection();
                }

                if (GUILayout.Button("Bỏ hết", EditorStyles.miniButtonRight, GUILayout.Width(52)))
                {
                    _selected.Clear();
                    SaveSelection();
                }
            }

            _leftScroll = EditorGUILayout.BeginScrollView(_leftScroll);
            foreach (var suite in _suites) DrawSuiteRow(suite, config);
            GUILayout.Space(8);
            EditorGUILayout.EndScrollView();
        }

        void DrawSuiteRow(AutoTestSuite suite, AutoTestConfig config)
        {
            var cases = _casesBySuite.TryGetValue(suite.Id, out var list) ? list : new List<AutoTestCase>();
            var last = SuiteStatus(suite.Id);
            var focused = _focusSuite == suite.Id;
            var rowRect = EditorGUILayout.BeginHorizontal(GUILayout.Height(24));
            if (focused)
                EditorGUI.DrawRect(rowRect, AutoTestStyles.Dark ? new Color(0.24f, 0.37f, 0.59f, 0.5f) : new Color(0.6f, 0.75f, 1f, 0.5f));

            var expanded = _expanded.Contains(suite.Id);
            var newExpanded = GUILayout.Toggle(expanded, GUIContent.none, EditorStyles.foldout, GUILayout.Width(12));
            if (newExpanded != expanded)
            {
                if (newExpanded) _expanded.Add(suite.Id);
                else _expanded.Remove(suite.Id);
            }

            var enabled = config.IsSuiteEnabled(suite.Id);
            var sel = _selected.Contains(suite.Id);
            using (new EditorGUI.DisabledScope(!enabled))
            {
                var newSel = GUILayout.Toggle(sel, GUIContent.none, GUILayout.Width(16));
                if (newSel != sel)
                {
                    if (newSel) _selected.Add(suite.Id);
                    else _selected.Remove(suite.Id);
                    SaveSelection();
                }
            }

            GUILayout.Label(AutoTestStyles.Icon(suite.Icon), GUILayout.Width(18), GUILayout.Height(18));
            if (GUILayout.Button(new GUIContent(suite.DisplayName, suite.Description), AutoTestStyles.RowButton,
                    GUILayout.ExpandWidth(true)))
            {
                _focusSuite = suite.Id;
                if (_tab == Tab.Settings || _tab == Tab.History) SetTab(Tab.Overview);
            }

            GUILayout.Label(ModeLabel(suite.Mode), AutoTestStyles.Subtle, GUILayout.Width(40));
            if (last.HasValue) AutoTestStyles.Pill(AutoTestStatusUtil.Label(last.Value), AutoTestStyles.StatusColor(last.Value), 62);
            else GUILayout.Label($"{cases.Count}", AutoTestStyles.Subtle, GUILayout.Width(62));
            EditorGUILayout.EndHorizontal();

            if (!expanded) return;
            foreach (var c in cases)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Space(34);
                    var on = config.IsCaseEnabled(suite.Id, c.Id);
                    var newOn = GUILayout.Toggle(on, GUIContent.none, GUILayout.Width(16));
                    if (newOn != on)
                    {
                        var key = suite.Id + "/" + c.Id;
                        if (newOn) config.disabledCases.Remove(key);
                        else if (!config.disabledCases.Contains(key)) config.disabledCases.Add(key);
                        AutoTestSettings.Save();
                    }

                    var status = CaseStatus(suite.Id, c.Id);
                    var color = status.HasValue ? AutoTestStyles.StatusColor(status.Value) : Color.gray;
                    var dot = GUILayoutUtility.GetRect(8, 16, GUILayout.Width(8));
                    EditorGUI.DrawRect(new Rect(dot.x, dot.y + 5, 7, 7), color);
                    if (GUILayout.Button(new GUIContent(c.Name, c.Description), AutoTestStyles.RowButton))
                    {
                        _focusSuite = suite.Id;
                        _resultCaseFilter = suite.Id + "/" + c.Id;
                        SetTab(Tab.Results);
                    }
                }
            }
        }

        void DrawRightPanel()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(6);
                var newTab = (Tab)GUILayout.Toolbar((int)_tab, TAB_LABELS, GUILayout.Height(22));
                if (newTab != _tab) SetTab(newTab);
                GUILayout.Space(6);
            }

            if (_tab == Tab.Results)
            {
                DrawResultsTab(); // có scroll riêng 2 cột
                return;
            }

            _rightScroll = EditorGUILayout.BeginScrollView(_rightScroll);
            GUILayout.Space(4);
            switch (_tab)
            {
                case Tab.Overview: DrawOverviewTab(); break;
                case Tab.Running: DrawRunningTab(); break;
                case Tab.History: DrawHistoryTab(); break;
                case Tab.Settings: DrawSettingsTab(); break;
            }

            GUILayout.Space(10);
            EditorGUILayout.EndScrollView();
        }

        void SetTab(Tab tab)
        {
            _tab = tab;
            EditorPrefs.SetInt(PREF_TAB, (int)tab);
            _rightScroll = Vector2.zero;
        }

        #endregion

        #region Overview

        void DrawOverviewTab()
        {
            var suite = _suites.FirstOrDefault(s => s.Id == _focusSuite);
            if (suite == null)
            {
                EditorGUILayout.HelpBox("Chưa có suite nào.", MessageType.Info);
                return;
            }

            using (new EditorGUILayout.VerticalScope(AutoTestStyles.Card))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label(AutoTestStyles.Icon(suite.Icon), GUILayout.Width(24), GUILayout.Height(24));
                    GUILayout.Label(suite.DisplayName, AutoTestStyles.Title);
                    GUILayout.FlexibleSpace();
                    GUILayout.Label($"id: {suite.Id} · {ModeLabel(suite.Mode)}", AutoTestStyles.Subtle);
                }

                GUILayout.Label(suite.Description, AutoTestStyles.Wrap);
                GUILayout.Space(4);
                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(AutoTestRunner.IsRunning))
                    {
                        if (GUILayout.Button(new GUIContent($" Chạy riêng {suite.DisplayName}",
                                AutoTestStyles.Icon("d_PlayButton").image), GUILayout.Height(26), GUILayout.Width(240)))
                            StartRun(new[] { suite.Id }, null);
                    }

                    var enabled = AutoTestSettings.Config.IsSuiteEnabled(suite.Id);
                    var newEnabled = GUILayout.Toggle(enabled, " Bật suite này", GUILayout.Width(120));
                    if (newEnabled != enabled)
                    {
                        var cfg = AutoTestSettings.Config;
                        if (newEnabled) cfg.disabledSuites.Remove(suite.Id);
                        else cfg.disabledSuites.Add(suite.Id);
                        AutoTestSettings.Save();
                    }
                }

                if (suite.Mode == ExecutionMode.Play)
                    EditorGUILayout.HelpBox(
                        "Suite Play sẽ tự vào Play mode từ scene boot (" +
                        (AutoTestRunner.ResolveBootScene(AutoTestSettings.Config) ?? "?") + ")" +
                        (suite.MutatesPlayerData ? ", sao lưu dữ liệu người chơi và khôi phục khi xong." : "."),
                        MessageType.None);
                if (suite.Mode == ExecutionMode.Device)
                    EditorGUILayout.HelpBox(
                        "Cần device Android bật USB debugging (adb). Build APK test có define EZG_AUTOTEST — " +
                        "không cài đè lên máy có dữ liệu thật. Cấu hình ở tab Cài đặt › Device.", MessageType.Info);
            }

            if (suite.Id == CustomScenarioSuite.SUITE_ID) DrawScenarioTools();

            GUILayout.Space(6);
            var cases = _casesBySuite.TryGetValue(suite.Id, out var list) ? list : new List<AutoTestCase>();
            GUILayout.Label($"Các case ({cases.Count})", AutoTestStyles.SectionHeader);
            var grouped = cases.GroupBy(c => string.IsNullOrEmpty(c.Category) ? "Khác" : c.Category);
            foreach (var g in grouped)
            {
                if (grouped.Count() > 1) GUILayout.Label(g.Key, EditorStyles.boldLabel);
                foreach (var c in g)
                    using (new EditorGUILayout.HorizontalScope(AutoTestStyles.Card))
                    {
                        var st = CaseStatus(suite.Id, c.Id);
                        if (st.HasValue) AutoTestStyles.Pill(AutoTestStatusUtil.Label(st.Value), AutoTestStyles.StatusColor(st.Value), 64);
                        else GUILayout.Space(68);
                        using (new EditorGUILayout.VerticalScope())
                        {
                            GUILayout.Label(c.Name, EditorStyles.boldLabel);
                            if (!string.IsNullOrEmpty(c.Description)) GUILayout.Label(c.Description, AutoTestStyles.Subtle);
                        }

                        using (new EditorGUI.DisabledScope(AutoTestRunner.IsRunning))
                        {
                            if (GUILayout.Button(new GUIContent(AutoTestStyles.Icon("d_PlayButton").image, "Chạy riêng case này"),
                                    GUILayout.Width(28), GUILayout.Height(22)))
                                StartRun(new[] { suite.Id }, new List<string> { suite.Id + "/" + c.Id });
                        }
                    }
            }

            if (_adapter != null)
            {
                GUILayout.Space(8);
                GUILayout.Label("Adapter game", AutoTestStyles.SectionHeader);
                using (new EditorGUILayout.VerticalScope(AutoTestStyles.Card))
                {
                    GUILayout.Label($"{_adapter.Name} — {_adapter.Capabilities}", EditorStyles.boldLabel);
                    foreach (var d in _adapter.Diagnostics) GUILayout.Label("• " + d, AutoTestStyles.Subtle);
                }
            }
        }

        void DrawScenarioTools()
        {
            GUILayout.Space(6);
            GUILayout.Label("Công cụ kịch bản (dev + AI)", AutoTestStyles.SectionHeader);
            using (new EditorGUILayout.VerticalScope(AutoTestStyles.Card))
            {
                GUILayout.Label(
                    "Gameplay đặc thù của từng game viết thành kịch bản riêng. Tạo từ template rồi dán prompt cho AI " +
                    "(Claude Code) triển khai theo Documentation~/AI-SCENARIO-GUIDE.md.", AutoTestStyles.Wrap);
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("+ Tạo kịch bản mới", GUILayout.Height(24))) AutoTestNewScenarioWindow.Open();
                    if (GUILayout.Button("Tạo Hooks cho project", GUILayout.Height(24)))
                        ShowResult(AutoTestScaffolder.CreateProjectHooks(), "Hooks");
                    if (GUILayout.Button("Tạo Adapter (project khác template)", GUILayout.Height(24)))
                        ShowResult(AutoTestScaffolder.CreateAdapterTemplate(), "Adapter");
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Cài AI skill vào .claude/skills", GUILayout.Height(22)))
                    {
                        var ok = AutoTestScaffolder.InstallAiSkill(out var path);
                        ShowNotification(new GUIContent(ok ? "Đã cài skill: " + path : "Không tìm thấy file skill trong package"));
                    }

                    if (GUILayout.Button("Mở AI-SCENARIO-GUIDE", GUILayout.Height(22)))
                        OpenDoc("Documentation~/AI-SCENARIO-GUIDE.md");
                }
            }

            var scenarios = AutoTestRegistry.FindScenarios();
            if (scenarios.Count == 0) return;
            GUILayout.Space(4);
            foreach (var (type, info) in scenarios)
                using (new EditorGUILayout.HorizontalScope(AutoTestStyles.Card))
                {
                    GUILayout.Label($"[{info.Category}] {info.Name}" + (info.Disabled ? " (tắt)" : ""), EditorStyles.boldLabel);
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("Mở code", GUILayout.Width(70))) OpenScript(type);
                    if (GUILayout.Button("Copy prompt AI", GUILayout.Width(110)))
                    {
                        EditorGUIUtility.systemCopyBuffer =
                            AutoTestScaffolder.BuildAiPrompt(FindScriptPath(type), info.Name);
                        ShowNotification(new GUIContent("Đã copy prompt cho AI"));
                    }
                }
        }

        void ShowResult(string path, string what)
        {
            if (string.IsNullOrEmpty(path))
            {
                ShowNotification(new GUIContent($"{what}: đã có sẵn hoặc không tạo được"));
                return;
            }

            var obj = AssetDatabase.LoadMainAssetAtPath(path);
            if (obj != null) EditorGUIUtility.PingObject(obj);
            ShowNotification(new GUIContent($"Đã tạo {path}"));
        }

        #endregion

        #region Running

        void DrawRunningTab()
        {
            if (!AutoTestRunner.IsRunning)
            {
                EditorGUILayout.HelpBox(
                    AutoTestRunner.Phase == AutoTestRunState.PHASE_DONE
                        ? "Lượt chạy gần nhất đã xong — xem tab Kết quả."
                        : "Chưa có lượt chạy nào. Chọn suite bên trái rồi bấm Chạy.", MessageType.Info);
                DrawLiveLog();
                return;
            }

            var total = Math.Max(1, AutoTestRunner.TotalCases);
            var done = AutoTestRunner.CompletedCases;
            var elapsed = DateTime.Now - AutoTestRunner.StartedAt;
            using (new EditorGUILayout.VerticalScope(AutoTestStyles.Card))
            {
                GUILayout.Label(AutoTestRunner.CancelRequested ? "Đang dừng…" : "Đang chạy", AutoTestStyles.Title);
                var r = GUILayoutUtility.GetRect(10, 18, GUILayout.ExpandWidth(true));
                AutoTestStyles.Bar(r, done / (float)total, AutoTestStyles.StatusColor(TestStatus.Running));
                GUI.Label(r, $"{done}/{total} case · {elapsed:hh\\:mm\\:ss}", AutoTestStyles.PillStyle);
                GUILayout.Space(4);
                Field("Giai đoạn", AutoTestRunner.Phase);
                Field("Suite", AutoTestRunner.CurrentSuite ?? "—");
                Field("Case", AutoTestRunner.CurrentCase ?? "—");
                Field("Bước", AutoTestRunner.CurrentStep ?? "—");
                var report = AutoTestRunner.CurrentReport;
                if (report != null)
                {
                    var counts = new Dictionary<TestStatus, int>();
                    foreach (var s in report.suites)
                    foreach (var c in s.cases)
                        counts[c.status] = counts.TryGetValue(c.status, out var n) ? n + 1 : 1;
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        foreach (var st in new[] { TestStatus.Passed, TestStatus.Warning, TestStatus.Failed, TestStatus.Error, TestStatus.Skipped })
                            AutoTestStyles.Pill($"{AutoTestStatusUtil.Label(st)} {(counts.TryGetValue(st, out var v) ? v : 0)}",
                                AutoTestStyles.StatusColor(st), 96);
                    }
                }
            }

            DrawLiveLog();
        }

        void DrawLiveLog()
        {
            var log = AutoTestRunner.LiveLog;
            if (log.Count == 0) return;
            GUILayout.Label("Nhật ký", AutoTestStyles.SectionHeader);
            using (new EditorGUILayout.VerticalScope(AutoTestStyles.Card))
            {
                for (var i = log.Count - 1; i >= Math.Max(0, log.Count - 120); i--)
                    GUILayout.Label(log[i], AutoTestStyles.Mono);
            }
        }

        static void Field(string label, string value)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(label, EditorStyles.boldLabel, GUILayout.Width(80));
                GUILayout.Label(value, AutoTestStyles.Wrap);
            }
        }

        #endregion

        #region Actions

        void RunSelected()
        {
            StartRun(_selected.ToList(), null);
        }

        void RunAll()
        {
            var cfg = AutoTestSettings.Config;
            StartRun(_suites.Where(s => cfg.IsSuiteEnabled(s.Id) && s.Mode != ExecutionMode.Device).Select(s => s.Id).ToList(),
                null);
        }

        void StartRun(IEnumerable<string> suiteIds, List<string> caseFilter)
        {
            var ids = suiteIds.ToList();
            var hasPlay = _suites.Any(s => ids.Contains(s.Id) && s.Mode == ExecutionMode.Play);
            if (hasPlay && EditorApplication.isPlaying)
            {
                ShowNotification(new GUIContent("Thoát Play mode trước khi chạy"));
                return;
            }

            if (hasPlay && UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene().isDirty &&
                !EditorUtility.DisplayDialog("Scene chưa lưu",
                    "Scene đang mở có thay đổi chưa lưu. Test vẫn chạy (Unity giữ thay đổi khi thoát Play), tiếp tục?",
                    "Tiếp tục", "Huỷ"))
                return;

            var options = new AutoTestRunOptions { CaseFilter = caseFilter ?? new List<string>() };
            if (AutoTestRunner.Run(ids, options)) SetTab(Tab.Running);
            else ShowNotification(new GUIContent("Không bắt đầu được — xem Console"));
        }

        static void OpenHtml(string folder)
        {
            if (string.IsNullOrEmpty(folder)) return;
            var html = Path.Combine(folder, "report.html");
            if (File.Exists(html)) Application.OpenURL("file://" + html.Replace('\\', '/'));
            else EditorUtility.RevealInFinder(folder);
        }

        static void OpenDoc(string relative)
        {
            var info = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(AutoTestWindow).Assembly);
            var root = info != null ? info.resolvedPath : null;
            if (root == null) return;
            var path = Path.Combine(root, relative);
            if (File.Exists(path)) EditorUtility.OpenWithDefaultApp(path);
        }

        static string FindScriptPath(Type type)
        {
            foreach (var guid in AssetDatabase.FindAssets($"{type.Name} t:MonoScript"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var script = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
                if (script != null && script.GetClass() == type) return path;
                if (Path.GetFileNameWithoutExtension(path) == type.Name) return path;
            }

            return null;
        }

        static void OpenScript(Type type)
        {
            var path = FindScriptPath(type);
            if (path == null) return;
            AssetDatabase.OpenAsset(AssetDatabase.LoadMainAssetAtPath(path));
        }

        static string ModeLabel(ExecutionMode m)
        {
            switch (m)
            {
                case ExecutionMode.Edit: return "Edit";
                case ExecutionMode.Play: return "Play";
                default: return "Device";
            }
        }

        TestStatus? SuiteStatus(string suiteId)
        {
            var report = AutoTestRunner.IsRunning ? AutoTestRunner.CurrentReport : _lastReport;
            var s = report?.suites.FirstOrDefault(x => x.suiteId == suiteId);
            if (s == null || s.cases.Count == 0) return null;
            return AutoTestStatusUtil.Aggregate(s.cases);
        }

        TestStatus? CaseStatus(string suiteId, string caseId)
        {
            var report = AutoTestRunner.IsRunning ? AutoTestRunner.CurrentReport : _lastReport;
            var s = report?.suites.FirstOrDefault(x => x.suiteId == suiteId);
            var c = s?.cases.FirstOrDefault(x => x.caseId == caseId);
            return c?.status;
        }

        #endregion
    }
}
