using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Ezg.AutoTest.Editor
{
    public sealed partial class AutoTestWindow
    {
        const float RESULT_LIST_WIDTH = 330f;
        const float THUMB_WIDTH = 180f;
        const int MAX_LOG_LINES_SHOWN = 200;

        string _resultCaseFilter;
        string _search = "";
        bool _showPassed = true;
        bool _showWarning = true;
        bool _showFailed = true;
        bool _showSkipped;
        bool _onlyNew;
        string _selectedCaseKey;
        Vector2 _resultListScroll;
        Vector2 _resultDetailScroll;
        bool _showLogs;
        bool _logErrorsOnly = true;
        readonly Dictionary<string, Texture2D> _textures = new();
        readonly HashSet<string> _openStacks = new();

        AutoTestSettingsHolder _settingsHolder;
        SerializedObject _settingsSo;
        Vector2 _settingsScroll;

        TestRunReport ShownReport => AutoTestRunner.IsRunning ? AutoTestRunner.CurrentReport : _lastReport;

        void OnReportChanged()
        {
            ClearTextureCache();
            _selectedCaseKey = null;
        }

        void ClearTextureCache()
        {
            foreach (var t in _textures.Values)
                if (t != null)
                    DestroyImmediate(t);
            _textures.Clear();
        }

        #region Results

        void DrawResultsTab()
        {
            var report = ShownReport;
            if (report == null)
            {
                GUILayout.Space(6);
                EditorGUILayout.HelpBox("Chưa có report nào. Chạy thử một suite.", MessageType.Info);
                return;
            }

            DrawSummaryHeader(report);
            DrawResultFilters();

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUILayout.VerticalScope(GUILayout.Width(RESULT_LIST_WIDTH)))
                {
                    _resultListScroll = EditorGUILayout.BeginScrollView(_resultListScroll);
                    DrawResultList(report);
                    EditorGUILayout.EndScrollView();
                }

                using (new EditorGUILayout.VerticalScope())
                {
                    _resultDetailScroll = EditorGUILayout.BeginScrollView(_resultDetailScroll);
                    var selected = FindCase(report, _selectedCaseKey);
                    if (selected != null) DrawCaseDetail(report, selected);
                    else EditorGUILayout.HelpBox("Chọn một case bên trái để xem chi tiết.", MessageType.None);
                    GUILayout.Space(10);
                    EditorGUILayout.EndScrollView();
                }
            }
        }

        void DrawSummaryHeader(TestRunReport report)
        {
            var s = report.summary ?? new RunSummary();
            using (new EditorGUILayout.HorizontalScope(AutoTestStyles.Card))
            {
                var prev = GUI.contentColor;
                GUI.contentColor = AutoTestStyles.HealthColor(s.healthScore);
                GUILayout.Label(AutoTestRunner.IsRunning ? "…" : s.healthScore.ToString(), AutoTestStyles.BigNumber,
                    GUILayout.Width(64), GUILayout.Height(40));
                GUI.contentColor = prev;
                using (new EditorGUILayout.VerticalScope())
                {
                    GUILayout.Label(report.title, EditorStyles.boldLabel);
                    GUILayout.Label($"{FormatDate(report.startedAt)} · {FormatDuration(report.durationMs)} · {report.trigger}" +
                                    (report.cancelled ? " · ĐÃ DỪNG" : "") +
                                    (string.IsNullOrEmpty(report.env?.gitBranch) ? "" : $" · {report.env.gitBranch}@{report.env.gitCommit}"),
                        AutoTestStyles.Subtle);
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        AutoTestStyles.Pill($"Đạt {s.passed}", AutoTestStyles.StatusColor(TestStatus.Passed), 64);
                        AutoTestStyles.Pill($"C.báo {s.warning}", AutoTestStyles.StatusColor(TestStatus.Warning), 64);
                        AutoTestStyles.Pill($"Lỗi {s.failed}", AutoTestStyles.StatusColor(TestStatus.Failed), 64);
                        AutoTestStyles.Pill($"Crash {s.error}", AutoTestStyles.StatusColor(TestStatus.Error), 64);
                        AutoTestStyles.Pill($"Bỏ qua {s.skipped + s.cancelled}", AutoTestStyles.StatusColor(TestStatus.Skipped), 72);
                        GUILayout.Space(8);
                        GUILayout.Label($"Blocker {s.issuesBlocker} · Critical {s.issuesCritical} · Major {s.issuesMajor} · " +
                                        $"Minor {s.issuesMinor}", AutoTestStyles.Subtle);
                    }

                    var diff = report.diff;
                    if (diff != null && !string.IsNullOrEmpty(diff.previousRunId))
                        GUILayout.Label($"So với lần trước ({FormatDate(diff.previousStartedAt)}): +{diff.newIssues} lỗi mới, " +
                                        $"{diff.fixedIssues} đã sửa, {diff.persistingIssues} còn tồn.", AutoTestStyles.Subtle);
                }

                GUILayout.FlexibleSpace();
                using (new EditorGUILayout.VerticalScope(GUILayout.Width(120)))
                {
                    using (new EditorGUI.DisabledScope(AutoTestRunner.IsRunning))
                    {
                        if (GUILayout.Button("Mở report HTML")) OpenHtml(report.outputDir);
                        if (GUILayout.Button("Mở thư mục")) EditorUtility.RevealInFinder(Path.Combine(report.outputDir ?? "", "report.json"));
                        if (GUILayout.Button("Copy tóm tắt")) CopySummary(report);
                    }
                }
            }
        }

        void DrawResultFilters()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                _search = GUILayout.TextField(_search, EditorStyles.toolbarSearchField, GUILayout.Width(220));
                _showFailed = GUILayout.Toggle(_showFailed, "Lỗi/Crash", EditorStyles.toolbarButton, GUILayout.Width(70));
                _showWarning = GUILayout.Toggle(_showWarning, "Cảnh báo", EditorStyles.toolbarButton, GUILayout.Width(66));
                _showPassed = GUILayout.Toggle(_showPassed, "Đạt", EditorStyles.toolbarButton, GUILayout.Width(40));
                _showSkipped = GUILayout.Toggle(_showSkipped, "Bỏ qua/Dừng", EditorStyles.toolbarButton, GUILayout.Width(84));
                _onlyNew = GUILayout.Toggle(_onlyNew, "Chỉ lỗi mới", EditorStyles.toolbarButton, GUILayout.Width(76));
                if (!string.IsNullOrEmpty(_resultCaseFilter) &&
                    GUILayout.Button($"× {_resultCaseFilter}", EditorStyles.toolbarButton))
                    _resultCaseFilter = null;
                GUILayout.FlexibleSpace();
            }
        }

        bool PassesStatusFilter(TestCaseResult c)
        {
            switch (c.status)
            {
                case TestStatus.Passed: return _showPassed;
                case TestStatus.Warning: return _showWarning;
                case TestStatus.Failed:
                case TestStatus.Error:
                case TestStatus.Running: return _showFailed;
                default: return _showSkipped;
            }
        }

        bool PassesSearch(TestCaseResult c)
        {
            if (_onlyNew && !c.issues.Any(i => i.isNew)) return false;
            if (!string.IsNullOrEmpty(_resultCaseFilter) && c.suiteId + "/" + c.caseId != _resultCaseFilter) return false;
            if (string.IsNullOrEmpty(_search)) return true;
            var q = _search;
            bool Has(string s) => !string.IsNullOrEmpty(s) && s.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;
            return Has(c.name) || Has(c.caseId) || Has(c.message) ||
                   c.issues.Any(i => Has(i.title) || Has(i.message) || Has(i.location) || Has(i.objectPath));
        }

        void DrawResultList(TestRunReport report)
        {
            foreach (var suite in report.suites)
            {
                var cases = suite.cases.Where(c => PassesStatusFilter(c) && PassesSearch(c)).ToList();
                if (cases.Count == 0) continue;
                using (new EditorGUILayout.HorizontalScope())
                {
                    var st = AutoTestStatusUtil.Aggregate(suite.cases);
                    AutoTestStyles.Pill(AutoTestStatusUtil.Label(st), AutoTestStyles.StatusColor(st), 60);
                    GUILayout.Label($"{suite.name} ({cases.Count})", EditorStyles.boldLabel);
                }

                foreach (var c in cases)
                {
                    var key = c.suiteId + "/" + c.caseId;
                    var selected = key == _selectedCaseKey;
                    var r = EditorGUILayout.BeginHorizontal(GUILayout.Height(20));
                    if (selected)
                        EditorGUI.DrawRect(r, AutoTestStyles.Dark ? new Color(0.24f, 0.37f, 0.59f, 0.6f) : new Color(0.6f, 0.75f, 1f, 0.6f));
                    GUILayout.Space(10);
                    var dot = GUILayoutUtility.GetRect(8, 18, GUILayout.Width(8));
                    EditorGUI.DrawRect(new Rect(dot.x, dot.y + 6, 7, 7), AutoTestStyles.StatusColor(c.status));
                    var issuesLabel = c.issues.Count > 0 ? $"  <color=#999>({c.issues.Count})</color>" : "";
                    var newLabel = c.issues.Any(i => i.isNew) ? " <color=#e5534b><b>MỚI</b></color>" : "";
                    if (GUILayout.Button(c.name + issuesLabel + newLabel, AutoTestStyles.RowButton))
                    {
                        _selectedCaseKey = key;
                        _resultDetailScroll = Vector2.zero;
                        GUI.FocusControl(null);
                    }

                    GUILayout.Label(FormatDuration(c.durationMs), AutoTestStyles.Subtle, GUILayout.Width(52));
                    EditorGUILayout.EndHorizontal();
                }

                GUILayout.Space(4);
            }
        }

        static TestCaseResult FindCase(TestRunReport report, string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            foreach (var s in report.suites)
            foreach (var c in s.cases)
                if (c.suiteId + "/" + c.caseId == key)
                    return c;
            return null;
        }

        void DrawCaseDetail(TestRunReport report, TestCaseResult c)
        {
            using (new EditorGUILayout.VerticalScope(AutoTestStyles.Card))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    AutoTestStyles.Pill(AutoTestStatusUtil.Label(c.status), AutoTestStyles.StatusColor(c.status), 72);
                    GUILayout.Label(c.name, AutoTestStyles.Title);
                    GUILayout.FlexibleSpace();
                    GUILayout.Label($"{c.suiteId}/{c.caseId} · {FormatDuration(c.durationMs)}" +
                                    (string.IsNullOrEmpty(c.device) ? "" : $" · {c.device}"), AutoTestStyles.Subtle);
                }

                if (!string.IsNullOrEmpty(c.description)) GUILayout.Label(c.description, AutoTestStyles.Subtle);
                if (!string.IsNullOrEmpty(c.message)) GUILayout.Label(c.message, AutoTestStyles.Wrap);
                using (new EditorGUI.DisabledScope(AutoTestRunner.IsRunning))
                {
                    if (GUILayout.Button("Chạy lại case này", GUILayout.Width(140)))
                        StartRun(new[] { c.suiteId }, new List<string> { c.suiteId + "/" + c.caseId });
                }
            }

            if (c.steps.Count > 0)
            {
                GUILayout.Label("Các bước", AutoTestStyles.SectionHeader);
                using (new EditorGUILayout.VerticalScope(AutoTestStyles.Card))
                {
                    var i = 1;
                    foreach (var step in c.steps)
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            var dot = GUILayoutUtility.GetRect(8, 16, GUILayout.Width(8));
                            EditorGUI.DrawRect(new Rect(dot.x, dot.y + 5, 7, 7), AutoTestStyles.StatusColor(step.status));
                            GUILayout.Label($"{i++}. {step.name}", AutoTestStyles.Wrap);
                            GUILayout.FlexibleSpace();
                            GUILayout.Label(FormatDuration(step.durationMs), AutoTestStyles.Subtle, GUILayout.Width(60));
                        }
                }
            }

            if (c.issues.Count > 0)
            {
                GUILayout.Label($"Vấn đề ({c.issues.Count})", AutoTestStyles.SectionHeader);
                foreach (var issue in c.issues.OrderByDescending(x => x.severity)) DrawIssue(report, c, issue);
            }

            if (c.metrics.Count > 0)
            {
                GUILayout.Label("Số đo", AutoTestStyles.SectionHeader);
                using (new EditorGUILayout.VerticalScope(AutoTestStyles.Card))
                {
                    foreach (var m in c.metrics)
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            var dot = GUILayoutUtility.GetRect(8, 16, GUILayout.Width(8));
                            EditorGUI.DrawRect(new Rect(dot.x, dot.y + 5, 7, 7),
                                AutoTestStyles.StatusColor(m.passed ? TestStatus.Passed : TestStatus.Failed));
                            GUILayout.Label(m.name, GUILayout.Width(220));
                            GUILayout.Label($"{AutoTestContext.Format(m.value)} {m.unit}", EditorStyles.boldLabel, GUILayout.Width(120));
                            var bound = m.hasMax ? $"≤ {AutoTestContext.Format(m.max)}" : m.hasMin ? $"≥ {AutoTestContext.Format(m.min)}" : "";
                            GUILayout.Label(bound, AutoTestStyles.Subtle);
                        }
                }
            }

            if (c.attachments.Count > 0)
            {
                GUILayout.Label("Đính kèm", AutoTestStyles.SectionHeader);
                DrawAttachments(report, c);
            }

            if (c.logs.Count > 0)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    _showLogs = EditorGUILayout.Foldout(_showLogs, $"Log ({c.logs.Count})", true);
                    GUILayout.FlexibleSpace();
                    if (_showLogs) _logErrorsOnly = GUILayout.Toggle(_logErrorsOnly, "Chỉ lỗi/cảnh báo", GUILayout.Width(130));
                }

                if (_showLogs)
                    using (new EditorGUILayout.VerticalScope(AutoTestStyles.Card))
                    {
                        var shown = 0;
                        foreach (var l in c.logs)
                        {
                            if (_logErrorsOnly && l.type == "Log") continue;
                            if (++shown > MAX_LOG_LINES_SHOWN) break;
                            var prev = GUI.contentColor;
                            GUI.contentColor = l.type == "Error" || l.type == "Exception" || l.type == "Assert"
                                ? AutoTestStyles.StatusColor(TestStatus.Failed)
                                : l.type == "Warning" ? AutoTestStyles.StatusColor(TestStatus.Warning) : prev;
                            GUILayout.Label($"[{l.t:0.00}s] {l.type}: {l.message}", AutoTestStyles.Mono);
                            GUI.contentColor = prev;
                        }
                    }
            }
        }

        void DrawIssue(TestRunReport report, TestCaseResult c, TestIssue issue)
        {
            using (new EditorGUILayout.VerticalScope(AutoTestStyles.Card))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    AutoTestStyles.Pill(issue.severity.ToString(), AutoTestStyles.SeverityColor(issue.severity), 64);
                    if (issue.isNew) AutoTestStyles.Pill("MỚI", AutoTestStyles.StatusColor(TestStatus.Failed), 40);
                    GUILayout.Label(issue.title, EditorStyles.boldLabel);
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button(new GUIContent("Copy bug", "Copy nội dung bug để dán vào tracker"), GUILayout.Width(70)))
                    {
                        EditorGUIUtility.systemCopyBuffer = BugText(report, c, issue);
                        ShowNotification(new GUIContent("Đã copy bug"));
                    }
                }

                if (!string.IsNullOrEmpty(issue.category)) GUILayout.Label(issue.category, AutoTestStyles.Subtle);
                if (!string.IsNullOrEmpty(issue.message)) GUILayout.Label(issue.message, AutoTestStyles.Wrap);
                if (!string.IsNullOrEmpty(issue.location) || !string.IsNullOrEmpty(issue.objectPath))
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.Label("Vị trí:", EditorStyles.boldLabel, GUILayout.Width(50));
                        GUILayout.Label($"{issue.location}{(string.IsNullOrEmpty(issue.objectPath) ? "" : " › " + issue.objectPath)}",
                            AutoTestStyles.Wrap);
                        if (CanPing(issue.location) && GUILayout.Button("Mở", GUILayout.Width(40))) Ping(issue.location);
                    }

                if (!string.IsNullOrEmpty(issue.expected) || !string.IsNullOrEmpty(issue.actual))
                    GUILayout.Label($"Kỳ vọng: {issue.expected}    Thực tế: {issue.actual}", AutoTestStyles.Wrap);
                if (!string.IsNullOrEmpty(issue.steps))
                {
                    GUILayout.Label("Các bước tái hiện:", EditorStyles.boldLabel);
                    GUILayout.Label(issue.steps, AutoTestStyles.Mono);
                }

                if (!string.IsNullOrEmpty(issue.stackTrace))
                {
                    var key = c.suiteId + c.caseId + issue.id;
                    var open = _openStacks.Contains(key);
                    var newOpen = EditorGUILayout.Foldout(open, "Stack trace", true);
                    if (newOpen != open)
                    {
                        if (newOpen) _openStacks.Add(key);
                        else _openStacks.Remove(key);
                    }

                    if (newOpen) EditorGUILayout.SelectableLabel(issue.stackTrace, AutoTestStyles.Mono,
                        GUILayout.MinHeight(80));
                }

                if (!string.IsNullOrEmpty(issue.screenshot)) DrawImage(report, issue.screenshot, "Ảnh bằng chứng");
            }
        }

        void DrawAttachments(TestRunReport report, TestCaseResult c)
        {
            var images = c.attachments.Where(a => a.kind == "image").ToList();
            var others = c.attachments.Where(a => a.kind != "image").ToList();
            if (images.Count > 0)
            {
                var perRow = Mathf.Max(1, Mathf.FloorToInt((position.width - LEFT_WIDTH - RESULT_LIST_WIDTH - 40) / (THUMB_WIDTH + 8)));
                for (var i = 0; i < images.Count; i += perRow)
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        for (var j = i; j < Math.Min(images.Count, i + perRow); j++)
                            using (new EditorGUILayout.VerticalScope(GUILayout.Width(THUMB_WIDTH)))
                            {
                                DrawImage(report, images[j].path, images[j].label);
                            }
                    }
            }

            foreach (var a in others)
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label($"• {a.label} ({a.kind})", AutoTestStyles.Wrap);
                    if (GUILayout.Button("Mở", GUILayout.Width(40)))
                        EditorUtility.OpenWithDefaultApp(Path.Combine(report.outputDir ?? "", a.path));
                }

            if (c.suiteId == "visual" && c.caseId.StartsWith("screen-", StringComparison.Ordinal))
            {
                var current = images.FirstOrDefault(a => a.path.EndsWith("_current.png", StringComparison.OrdinalIgnoreCase));
                if (current != null && GUILayout.Button("Chấp nhận ảnh hiện tại làm baseline", GUILayout.Width(260)))
                {
                    var feature = c.caseId.Substring("screen-".Length);
                    var abs = Path.Combine(report.outputDir ?? "", current.path);
                    VisualRegressionSuite.AcceptAsBaseline(abs, feature, AutoTestSettings.Config);
                    ShowNotification(new GUIContent($"Đã cập nhật baseline {feature}"));
                }
            }
        }

        void DrawImage(TestRunReport report, string relativePath, string label)
        {
            var abs = Path.Combine(report.outputDir ?? "", relativePath ?? "");
            var tex = LoadTexture(abs);
            if (tex == null)
            {
                GUILayout.Label($"(không có ảnh: {relativePath})", AutoTestStyles.Subtle);
                return;
            }

            var h = THUMB_WIDTH * tex.height / Mathf.Max(1f, tex.width);
            var r = GUILayoutUtility.GetRect(THUMB_WIDTH, h, GUILayout.Width(THUMB_WIDTH), GUILayout.Height(h));
            GUI.DrawTexture(r, tex, ScaleMode.ScaleToFit);
            if (Event.current.type == EventType.MouseDown && r.Contains(Event.current.mousePosition))
            {
                EditorUtility.OpenWithDefaultApp(abs);
                Event.current.Use();
            }

            GUILayout.Label(label, AutoTestStyles.Subtle, GUILayout.Width(THUMB_WIDTH));
        }

        Texture2D LoadTexture(string abs)
        {
            if (_textures.TryGetValue(abs, out var cached)) return cached;
            Texture2D tex = null;
            if (File.Exists(abs))
            {
                tex = new Texture2D(2, 2) { hideFlags = HideFlags.HideAndDontSave };
                if (!tex.LoadImage(File.ReadAllBytes(abs)))
                {
                    DestroyImmediate(tex);
                    tex = null;
                }
            }

            _textures[abs] = tex;
            return tex;
        }

        static bool CanPing(string location)
        {
            return !string.IsNullOrEmpty(location) &&
                   (location.StartsWith("Assets/", StringComparison.Ordinal) ||
                    location.StartsWith("Packages/", StringComparison.Ordinal) ||
                    location.StartsWith("ProjectSettings/", StringComparison.Ordinal));
        }

        static void Ping(string location)
        {
            var path = location;
            var line = 0;
            var colon = location.LastIndexOf(':');
            if (colon > 0 && int.TryParse(location.Substring(colon + 1), out var l))
            {
                path = location.Substring(0, colon);
                line = l;
            }

            var obj = AssetDatabase.LoadMainAssetAtPath(path);
            if (obj == null)
            {
                EditorUtility.RevealInFinder(path);
                return;
            }

            if (line > 0 || obj is MonoScript) AssetDatabase.OpenAsset(obj, Math.Max(1, line));
            else
            {
                Selection.activeObject = obj;
                EditorGUIUtility.PingObject(obj);
            }
        }

        static string BugText(TestRunReport report, TestCaseResult c, TestIssue issue)
        {
            var env = report.env ?? new RunEnvironment();
            return $"[{issue.severity}] {issue.title}\n" +
                   $"Suite/Case: {c.suiteId} / {c.name}\n" +
                   (string.IsNullOrEmpty(issue.message) ? "" : $"Mô tả: {issue.message}\n") +
                   (string.IsNullOrEmpty(issue.steps) ? "" : $"Các bước tái hiện:\n{issue.steps}\n") +
                   (string.IsNullOrEmpty(issue.expected) ? "" : $"Kỳ vọng: {issue.expected}\n") +
                   (string.IsNullOrEmpty(issue.actual) ? "" : $"Thực tế: {issue.actual}\n") +
                   (string.IsNullOrEmpty(issue.location) ? "" : $"Vị trí: {issue.location} {issue.objectPath}\n") +
                   $"Môi trường: {env.productName} {env.appVersion} · {env.gitBranch}@{env.gitCommit} · Unity {env.unityVersion} · " +
                   $"{(string.IsNullOrEmpty(c.device) ? env.platform : c.device)}\n" +
                   (string.IsNullOrEmpty(issue.screenshot) ? "" : $"Ảnh: {Path.Combine(report.outputDir ?? "", issue.screenshot)}\n") +
                   $"Report: {Path.Combine(report.outputDir ?? "", "report.html")}";
        }

        void CopySummary(TestRunReport report)
        {
            var md = Path.Combine(report.outputDir ?? "", "summary.md");
            EditorGUIUtility.systemCopyBuffer = File.Exists(md)
                ? File.ReadAllText(md)
                : $"{report.title}: sức khoẻ {report.summary.healthScore}/100";
            ShowNotification(new GUIContent("Đã copy tóm tắt"));
        }

        #endregion

        #region History + Settings

        void DrawHistoryTab()
        {
            var config = AutoTestSettings.Config;
            var entries = AutoTestReportHistory.List(config);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label($"Lịch sử ({entries.Count} lượt, giữ tối đa {config.general.keepReports})", AutoTestStyles.SectionHeader);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Mở thư mục report", GUILayout.Width(140)))
                    EditorUtility.RevealInFinder(AutoTestReportHistory.ReportsRoot(config));
            }

            if (entries.Count == 0)
            {
                EditorGUILayout.HelpBox("Chưa có lượt chạy nào.", MessageType.Info);
                return;
            }

            foreach (var e in entries)
                using (new EditorGUILayout.HorizontalScope(AutoTestStyles.Card))
                {
                    var s = e.summary ?? new RunSummary();
                    var prev = GUI.contentColor;
                    GUI.contentColor = AutoTestStyles.HealthColor(s.healthScore);
                    GUILayout.Label(s.healthScore.ToString(), AutoTestStyles.Title, GUILayout.Width(36));
                    GUI.contentColor = prev;
                    using (new EditorGUILayout.VerticalScope())
                    {
                        GUILayout.Label(e.title + (e.cancelled ? "  (đã dừng)" : ""), EditorStyles.boldLabel);
                        GUILayout.Label($"{FormatDate(e.startedAt)} · {FormatDuration(e.durationMs)} · {e.trigger} · " +
                                        $"Đạt {s.passed} · Cảnh báo {s.warning} · Lỗi {s.failed} · Crash {s.error}",
                            AutoTestStyles.Subtle);
                    }

                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("Xem", GUILayout.Width(50)))
                    {
                        _lastReportFolder = e.folder;
                        _lastReport = AutoTestReportHistory.Load(e.folder);
                        OnReportChanged();
                        SetTab(Tab.Results);
                    }

                    if (GUILayout.Button("HTML", GUILayout.Width(50))) OpenHtml(e.folder);
                    if (GUILayout.Button("Xoá", GUILayout.Width(44)) &&
                        EditorUtility.DisplayDialog("Xoá report", $"Xoá report {e.runId}?", "Xoá", "Huỷ"))
                    {
                        AutoTestReportHistory.Delete(e.folder);
                        if (e.folder == _lastReportFolder)
                        {
                            _lastReport = null;
                            _lastReportFolder = null;
                        }

                        GUIUtility.ExitGUI();
                    }
                }
        }

        void DrawSettingsTab()
        {
            AutoTestSettingsProvider.DrawSettingsGUI(ref _settingsHolder, ref _settingsSo, ref _settingsScroll);
        }

        #endregion

        #region Format

        static string FormatDuration(double ms)
        {
            if (ms <= 0) return "";
            if (ms < 1000) return $"{ms:0} ms";
            var t = TimeSpan.FromMilliseconds(ms);
            return t.TotalMinutes >= 1 ? $"{(int)t.TotalMinutes}m {t.Seconds}s" : $"{t.TotalSeconds:0.0}s";
        }

        static string FormatDate(string iso)
        {
            return DateTime.TryParse(iso, null, System.Globalization.DateTimeStyles.RoundtripKind, out var d)
                ? d.ToString("dd/MM/yyyy HH:mm:ss")
                : iso;
        }

        #endregion
    }
}
