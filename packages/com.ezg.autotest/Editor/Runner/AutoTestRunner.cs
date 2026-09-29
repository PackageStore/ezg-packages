using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Ezg.AutoTest.Editor
{
    /// <summary>
    ///     Điều phối một lượt chạy trong Editor: suite Edit chạy ngay, suite Play vào Play mode (mỗi suite một phiên
    ///     nếu cô lập), Device giao cho suite device. Trạng thái ghi ra đĩa sau mỗi case nên sống sót qua domain
    ///     reload; nút Dừng huỷ cooperative rồi vẫn xuất report phần đã chạy.
    /// </summary>
    [InitializeOnLoad]
    public static class AutoTestRunner
    {
        const float ENTER_PLAY_TIMEOUT_SECONDS = 12f;
        const int MAX_PLAY_ATTEMPTS = 2;
        const float TEARDOWN_TIMEOUT_SECONDS = 30f;
        const int LIVE_LOG_MAX = 300;
        const string SESSION_LAST_REPORT = "EZGAutoTest.LastReportFolder";
        const int EXIT_OK = 0;
        const int EXIT_FAILED = 2;
        const int EXIT_RUNNER_ERROR = 3;
        const double EXIT_RETRY_SECONDS = 2;
        const double PLAY_START_FALLBACK_SECONDS = 6;

        static AutoTestRunState _state;
        static CancellationTokenSource _cts;
        static bool _executing;
        static long _playRequestCursor;
        static string _inFlightCaseKey;
        static double _lastExitRequest;
        static readonly List<string> _liveLog = new();

        /// <summary>Bắn khi trạng thái/tiến độ đổi (cửa sổ repaint).</summary>
        public static event Action Changed;

        public static bool IsRunning => _state != null && _state.IsActive;

        /// <summary>Thư mục report của lượt chạy gần nhất (sau khi xong).</summary>
        public static string LastReportFolder
        {
            get => SessionState.GetString(SESSION_LAST_REPORT, "");
            private set => SessionState.SetString(SESSION_LAST_REPORT, value ?? "");
        }

        public static string CurrentSuite { get; private set; }
        public static string CurrentCase { get; private set; }
        public static string CurrentStep { get; private set; }
        public static int CompletedCases { get; private set; }
        public static int TotalCases { get; private set; }
        public static string Phase => _state?.phase ?? AutoTestRunState.PHASE_IDLE;
        public static bool CancelRequested => _state != null && _state.cancelRequested;
        public static IReadOnlyList<string> LiveLog => _liveLog;

        /// <summary>Report đang tích luỹ (đọc để hiện kết quả trực tiếp).</summary>
        public static TestRunReport CurrentReport => _state?.report;

        public static DateTime StartedAt =>
            _state != null && DateTime.TryParse(_state.startedAtIso, null, DateTimeStyles.RoundtripKind, out var t)
                ? t
                : DateTime.Now;

        static AutoTestRunner()
        {
            EditorApplication.update -= AutoTestClock.Pump;
            EditorApplication.update += AutoTestClock.Pump;
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorApplication.pauseStateChanged -= OnPauseStateChanged;
            EditorApplication.pauseStateChanged += OnPauseStateChanged;

            _state = AutoTestRunState.Load();
            if (_state == null || !_state.IsActive) return;

            // Domain vừa reload giữa lượt chạy (vào Play / recompile) → bật lại log capture ngay để bắt log boot.
            AutoTestLogCapture.Start(_state.config.general.ignoredLogPatterns);
            _cts = new CancellationTokenSource();
            if (_state.cancelRequested) _cts.Cancel();
            RecountProgress();
        }

        #region Public API

        /// <summary>Chạy các suite theo id (thứ tự theo Order của suite). Trả false nếu không bắt đầu được.</summary>
        public static bool Run(IEnumerable<string> suiteIds, AutoTestRunOptions options = null)
        {
            if (IsRunning)
            {
                AutoTestLog.Warn("Đang có lượt chạy khác.");
                return false;
            }

            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                AutoTestLog.Warn("Editor đang compile/import — thử lại sau.");
                return false;
            }

            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                AutoTestLog.Warn("Thoát Play mode trước khi chạy auto test.");
                return false;
            }

            options ??= new AutoTestRunOptions();
            var config = AutoTestSettings.Snapshot();
            var all = AutoTestRegistry.CreateSuites();
            var wanted = new HashSet<string>(suiteIds ?? all.Select(s => s.Id), StringComparer.OrdinalIgnoreCase);
            var suites = all.Where(s => wanted.Contains(s.Id)).ToList();
            if (suites.Count == 0)
            {
                AutoTestLog.Warn("Không có suite nào để chạy.");
                return false;
            }

            var runId = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            var outputDir = string.IsNullOrEmpty(options.OutputDir)
                ? Path.Combine(AutoTestReportHistory.ReportsRoot(config), runId)
                : options.OutputDir;
            Directory.CreateDirectory(outputDir);

            _state = new AutoTestRunState
            {
                runId = runId,
                suiteIds = suites.Select(s => s.Id).ToList(),
                startedAtIso = DateTime.Now.ToString("o"),
                options = options,
                config = config
            };
            var report = _state.report;
            report.runId = runId;
            report.title = string.IsNullOrEmpty(options.Title)
                ? string.Join(" + ", suites.Select(s => s.DisplayName))
                : options.Title;
            report.startedAt = _state.startedAtIso;
            report.trigger = options.Trigger;
            report.outputDir = outputDir;
            report.env = EnvironmentInfo.Collect(null);

            _liveLog.Clear();
            _cts?.Dispose();
            _cts = new CancellationTokenSource();
            AutoTestLogCapture.Clear();
            AutoTestLogCapture.Start(config.general.ignoredLogPatterns);
            CompletedCases = 0;
            TotalCases = EstimateCases(suites, config);
            AddLive($"▶ Bắt đầu: {report.title} ({TotalCases} case)");
            _state.phase = AutoTestRunState.PHASE_EDIT; // đánh dấu lượt đang hoạt động; Advance đặt phase thật
            _state.Save();
            Changed?.Invoke();
            Advance();
            return true;
        }

        public static bool RunAll(AutoTestRunOptions options = null)
        {
            var config = AutoTestSettings.Config;
            // Device / E2E cần máy thật + build APK → chỉ chạy khi chọn riêng (giống nút "Chạy tất cả" và CLI "all").
            return Run(AutoTestRegistry.CreateSuites()
                .Where(s => config.IsSuiteEnabled(s.Id) && s.Mode != ExecutionMode.Device)
                .Select(s => s.Id), options);
        }

        /// <summary>Dừng lượt chạy (case đang chạy bị huỷ, phần còn lại đánh "Đã dừng", vẫn xuất report).</summary>
        public static void Stop(string reason = "Người dùng bấm Dừng")
        {
            if (!IsRunning || _state.cancelRequested) return;
            _state.cancelRequested = true;
            _state.cancelReason = reason;
            _state.Save();
            _cts?.Cancel();
            AddLive("■ Đang dừng: " + reason);
            Changed?.Invoke();
        }

        /// <summary>Huỷ cứng khi runner kẹt (xoá state, khôi phục sandbox) — dùng khi Dừng thường không ăn.</summary>
        public static void ForceReset()
        {
            _cts?.Cancel();
            _executing = false;
            AutoTestClock.CancelAll();
            RestoreEnvironment();
            if (_state != null)
            {
                _state.phase = AutoTestRunState.PHASE_DONE;
                _state.Save();
            }

            AddLive("⚠ Đã huỷ cứng lượt chạy.");
            Changed?.Invoke();
        }

        #endregion

        #region State machine

        static void Tick()
        {
            if (_state == null || !_state.IsActive) return;
            var opts = _state.options;
            if (!_state.cancelRequested && opts.GlobalTimeoutMinutes > 0 &&
                (DateTime.Now - StartedAt).TotalMinutes > opts.GlobalTimeoutMinutes)
            {
                _state.runnerError = $"Quá thời gian cả lượt ({opts.GlobalTimeoutMinutes:0} phút).";
                Stop(_state.runnerError);
            }

            if (_executing) return;
            switch (_state.phase)
            {
                case AutoTestRunState.PHASE_EDIT:
                    // Domain reload giữa suite Edit → chạy tiếp từ case chưa xong.
                    ResumeCurrentSuite();
                    break;
                case AutoTestRunState.PHASE_ENTERING_PLAY:
                    // Bình thường StartPlayGroup chạy từ sự kiện EnteredPlayMode; đây chỉ là lưới an toàn nếu lỡ
                    // sự kiện (chờ qua thời điểm domain reload để không khởi động trong domain sắp bị huỷ).
                    if (EditorApplication.isPlaying && _state.SecondsInPhase() > PLAY_START_FALLBACK_SECONDS)
                    {
                        StartPlayGroup();
                    }
                    else if (!EditorApplication.isPlayingOrWillChangePlaymode &&
                             _state.SecondsInPhase() > ENTER_PLAY_TIMEOUT_SECONDS)
                    {
                        if (_state.playAttempts < MAX_PLAY_ATTEMPTS && !_state.cancelRequested)
                        {
                            AddLive("Vào Play mode thất bại — thử lại.");
                            RequestEnterPlay();
                        }
                        else
                        {
                            FailPlayGroup("Không vào được Play mode (lỗi compile hoặc bị chặn).");
                        }
                    }

                    break;
                case AutoTestRunState.PHASE_PLAYING:
                    if (EditorApplication.isPlaying) StartPlayGroup(); // reload giữa Play → chạy tiếp
                    else if (!EditorApplication.isPlayingOrWillChangePlaymode) FailInFlightAndContinue();
                    break;
                case AutoTestRunState.PHASE_EXITING_PLAY:
                    if (!EditorApplication.isPlayingOrWillChangePlaymode) Advance();
                    else if (EditorApplication.isPlaying && _state.SecondsInPhase() > EXIT_RETRY_SECONDS &&
                             EditorApplication.timeSinceStartup - _lastExitRequest > EXIT_RETRY_SECONDS)
                    {
                        _lastExitRequest = EditorApplication.timeSinceStartup;
                        EditorApplication.ExitPlaymode();
                    }
                    break;
                case AutoTestRunState.PHASE_FINALIZING:
                    FinalizeRun();
                    break;
            }
        }

        /// <summary>Chọn bước kế tiếp: suite Edit chạy ngay, suite Play vào Play mode, hết suite → xuất report.</summary>
        static void Advance()
        {
            // Có thể được gọi 2 đường (delayCall sau EnteredEditMode + Tick) → chỉ chạy khi lượt còn hoạt động.
            if (_state == null || _executing || !_state.IsActive || _state.phase == AutoTestRunState.PHASE_FINALIZING ||
                _state.phase == AutoTestRunState.PHASE_ENTERING_PLAY || _state.phase == AutoTestRunState.PHASE_PLAYING)
                return;
            if (_state.cancelRequested)
            {
                if (EditorApplication.isPlaying)
                {
                    _state.SetPhase(AutoTestRunState.PHASE_EXITING_PLAY);
                    EditorApplication.ExitPlaymode();
                    return;
                }

                FinalizeRun();
                return;
            }

            var config = _state.config;
            while (_state.suiteIndex < _state.suiteIds.Count)
            {
                var id = _state.suiteIds[_state.suiteIndex];
                var suite = AutoTestRegistry.CreateSuite(id);
                if (suite == null)
                {
                    AddSuiteNote(id, id, TestStatus.Skipped, "Không tìm thấy suite (đã bị xoá?).");
                    _state.suiteIndex++;
                    continue;
                }

                if (!config.IsSuiteEnabled(id))
                {
                    AddSuiteNote(id, suite.DisplayName, TestStatus.Skipped, "Suite bị tắt trong Settings.");
                    _state.suiteIndex++;
                    continue;
                }

                if (suite.Mode != ExecutionMode.Play)
                {
                    if (EditorApplication.isPlaying)
                    {
                        _state.SetPhase(AutoTestRunState.PHASE_EXITING_PLAY);
                        EditorApplication.ExitPlaymode();
                        return;
                    }

                    _state.SetPhase(AutoTestRunState.PHASE_EDIT);
                    RunEditSuite(suite);
                    return;
                }

                // Suite Play: gom nhóm các suite Play liền nhau nếu không cô lập.
                _state.playGroup = new List<string> { id };
                if (!config.general.isolateSuitesInPlaySessions)
                    for (var j = _state.suiteIndex + 1; j < _state.suiteIds.Count; j++)
                    {
                        var next = AutoTestRegistry.CreateSuite(_state.suiteIds[j]);
                        if (next == null || next.Mode != ExecutionMode.Play) break;
                        _state.playGroup.Add(next.Id);
                    }

                _state.playAttempts = 0;
                RequestEnterPlay();
                return;
            }

            FinalizeRun();
        }

        static void RequestEnterPlay()
        {
            PrepareEnterPlay();
            _state.playAttempts++;
            _state.SetPhase(AutoTestRunState.PHASE_ENTERING_PLAY);
            _playRequestCursor = AutoTestLogCapture.Cursor;
            AddLive($"Vào Play mode cho: {string.Join(", ", _state.playGroup)}");
            Changed?.Invoke();
            EditorApplication.EnterPlaymode();
        }

        static void PrepareEnterPlay()
        {
            var config = _state.config;
            if (config.general.sandboxPlayerData && !_state.sandboxTaken && RemainingMutatesData())
            {
                var adapter = AutoTestRegistry.CreateAdapter(config);
                var n = PlayerDataSandbox.TakeBackup(adapter.GetSaveKeys());
                _state.sandboxTaken = true;
                AddLive(n >= 0
                    ? $"Sandbox: sao lưu {n} key dữ liệu người chơi (tự khôi phục khi xong)."
                    : "Sandbox: đã có bản sao lưu từ trước, giữ nguyên.");
            }

            if (!_state.playModeStartSceneOverridden)
            {
                var current = EditorSceneManager.playModeStartScene;
                _state.previousPlayModeStartScene = current != null ? AssetDatabase.GetAssetPath(current) : "";
                var boot = ResolveBootScene(config);
                if (!string.IsNullOrEmpty(boot))
                {
                    EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(boot);
                    _state.playModeStartSceneOverridden = true;
                }
            }

            _state.Save();
        }

        static bool RemainingMutatesData()
        {
            for (var i = _state.suiteIndex; i < _state.suiteIds.Count; i++)
            {
                var s = AutoTestRegistry.CreateSuite(_state.suiteIds[i]);
                if (s != null && s.Mode == ExecutionMode.Play && s.MutatesPlayerData) return true;
            }

            return false;
        }

        public static string ResolveBootScene(AutoTestConfig config)
        {
            if (!string.IsNullOrEmpty(config.general.bootScenePath) &&
                File.Exists(Path.Combine(StaticCheckUtil.ProjectRoot(), config.general.bootScenePath)))
                return config.general.bootScenePath;
            var first = EditorBuildSettings.scenes.FirstOrDefault(s => s.enabled && File.Exists(s.path));
            return first?.path;
        }

        static void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            if (_state == null || !_state.IsActive) return;
            switch (change)
            {
                case PlayModeStateChange.EnteredPlayMode:
                    if (_state.phase == AutoTestRunState.PHASE_ENTERING_PLAY && !_executing) StartPlayGroup();
                    break;
                case PlayModeStateChange.ExitingPlayMode:
                    if (_state.phase == AutoTestRunState.PHASE_PLAYING && _executing)
                        Stop("Người dùng thoát Play mode giữa chừng");
                    break;
                case PlayModeStateChange.EnteredEditMode:
                    if (_state.phase == AutoTestRunState.PHASE_EXITING_PLAY) EditorApplication.delayCall += Advance;
                    break;
            }
        }

        static void OnPauseStateChanged(PauseState state)
        {
            if (state != PauseState.Paused || !IsRunning || !_state.config.general.autoUnpause) return;
            EditorApplication.isPaused = false;
            AddLive("Tự bỏ Pause (Console đang bật Error Pause).");
        }

        #endregion

        #region Execution

        static async void RunEditSuite(AutoTestSuite suite)
        {
            _executing = true;
            try
            {
                var config = _state.config;
                var adapter = AutoTestRegistry.CreateAdapter(config);
                _state.report.env.adapter = adapter.Name;
                var session = CreateSession(config, adapter);
                await RunSuite(suite, session);
                _state.suiteIndex++;
            }
            catch (Exception e)
            {
                _state.runnerError = $"{suite.Id}: {e.GetType().Name}: {e.Message}";
                Debug.LogException(e);
                _state.suiteIndex++;
            }
            finally
            {
                _executing = false;
                _state.Save();
            }

            Advance();
        }

        static void ResumeCurrentSuite()
        {
            if (_state.suiteIndex >= _state.suiteIds.Count)
            {
                Advance();
                return;
            }

            var suite = AutoTestRegistry.CreateSuite(_state.suiteIds[_state.suiteIndex]);
            if (suite == null || suite.Mode == ExecutionMode.Play)
            {
                Advance();
                return;
            }

            AddLive($"Tiếp tục suite {suite.DisplayName} sau khi Editor reload.");
            RunEditSuite(suite);
        }

        static async void StartPlayGroup()
        {
            if (_executing) return;
            _executing = true;
            _state.SetPhase(AutoTestRunState.PHASE_PLAYING);
            try
            {
                var config = _state.config;
                var adapter = AutoTestRegistry.CreateAdapter(config);
                var env = _state.report.env;
                env.adapter = adapter.Name;
                env.resolution = $"{Screen.width}x{Screen.height}";
                var session = CreateSession(config, adapter);
                session.SessionLogCursor = _playRequestCursor;
                foreach (var id in _state.playGroup)
                {
                    var suite = AutoTestRegistry.CreateSuite(id);
                    if (suite == null) continue;
                    await RunSuite(suite, session);
                }

                _state.suiteIndex += _state.playGroup.Count;
            }
            catch (Exception e)
            {
                _state.runnerError = $"Play: {e.GetType().Name}: {e.Message}";
                Debug.LogException(e);
                _state.suiteIndex += Math.Max(1, _state.playGroup.Count);
            }
            finally
            {
                _executing = false;
                _state.SetPhase(AutoTestRunState.PHASE_EXITING_PLAY);
            }

            if (EditorApplication.isPlaying) EditorApplication.ExitPlaymode();
        }

        static AutoTestSession CreateSession(AutoTestConfig config, IGameAdapter adapter)
        {
            var hooks = AutoTestRegistry.CreateHooks();
            var session = new AutoTestSession(config, adapter, hooks, _state.report.outputDir, false)
            {
                RunToken = _cts?.Token ?? CancellationToken.None
            };
            session.Progress = OnProgress;
            return session;
        }

        static async Task RunSuite(AutoTestSuite suite, AutoTestSession session)
        {
            var config = session.Config;
            var result = _state.SuiteResult(suite.Id);
            if (result == null)
            {
                result = new TestSuiteResult
                {
                    suiteId = suite.Id,
                    name = suite.DisplayName,
                    description = suite.Description,
                    mode = suite.Mode
                };
                _state.report.suites.Add(result);
            }

            CurrentSuite = suite.DisplayName;
            AddLive($"— {suite.DisplayName}");
            Changed?.Invoke();

            List<AutoTestCase> cases;
            try
            {
                cases = suite.BuildCases(new AutoTestBuildContext { Config = config, Game = session.Game })
                    .Where(c => PassesFilters(suite, c))
                    .ToList();
            }
            catch (Exception e)
            {
                result.cases.Add(ErrorCase(suite, "build-cases", "Liệt kê case", e));
                _state.Save();
                return;
            }

            var done = new HashSet<string>(result.cases.Select(c => c.caseId));
            // Case đang chạy dở lúc bị gián đoạn lần trước → ghi lỗi, không chạy lại (tránh lặp vô hạn).
            var key = ReadInFlight();
            if (!string.IsNullOrEmpty(key))
            {
                var interrupted = cases.FirstOrDefault(c => suite.Id + "/" + c.Id == key);
                if (interrupted != null && !done.Contains(interrupted.Id))
                {
                    var r = AutoTestExecutor.Placeholder(suite, interrupted, TestStatus.Error,
                        "Case bị gián đoạn (domain reload / Play mode bị thoát / Editor crash).");
                    r.issues.Add(new TestIssue
                    {
                        severity = Severity.Critical, category = "Runner.Interrupted",
                        title = "Case làm gián đoạn phiên chạy", message = r.message
                    });
                    AutoTestExecutor.AssignFingerprints(r);
                    result.cases.Add(r);
                    done.Add(interrupted.Id);
                }

                WriteInFlight(null);
            }

            var startTime = AutoTestClock.Now;
            var suiteCtxResult = new TestCaseResult { suiteId = suite.Id, caseId = "_setup", name = "SetUp" };
            var suiteCtx = new AutoTestContext(session, suiteCtxResult, session.RunToken);
            var setUpFailed = false;
            if (cases.Any(c => !done.Contains(c.Id)))
                try
                {
                    await suite.SetUp(suiteCtx);
                }
                catch (Exception e) when (!(e is OperationCanceledException))
                {
                    setUpFailed = true;
                    result.cases.Add(ErrorCase(suite, "_setup", "SetUp của suite", e));
                }
                catch (OperationCanceledException)
                {
                    // Dừng trong SetUp → các case bên dưới sẽ đánh "Đã dừng".
                }

            foreach (var c in cases)
            {
                if (done.Contains(c.Id)) continue;
                if (session.RunToken.IsCancellationRequested || _state.cancelRequested)
                {
                    result.cases.Add(AutoTestExecutor.Placeholder(suite, c, TestStatus.Cancelled, "Lượt chạy đã dừng."));
                    continue;
                }

                if (setUpFailed)
                {
                    result.cases.Add(AutoTestExecutor.Placeholder(suite, c, TestStatus.Skipped, "SetUp của suite lỗi."));
                    continue;
                }

                CurrentCase = c.Name;
                CurrentStep = null;
                WriteInFlight(suite.Id + "/" + c.Id);
                Changed?.Invoke();
                var r = await AutoTestExecutor.RunCase(session, suite, c, session.RunToken);
                WriteInFlight(null);
                result.cases.Add(r);
                MergeExtraSuites(session);
                CompletedCases++;
                AddLive($"{Icon(r.status)} {suite.DisplayName} › {r.name}" +
                        (string.IsNullOrEmpty(r.message) ? "" : $" — {r.message}"));
                _state.Save();
                Changed?.Invoke();
            }

            try
            {
                var tearDown = suite.TearDown(suiteCtx);
                await Task.WhenAny(tearDown, AutoTestClock.Seconds(TEARDOWN_TIMEOUT_SECONDS));
            }
            catch (Exception e)
            {
                AutoTestLog.Warn($"TearDown {suite.Id} lỗi: {e.Message}");
            }

            result.durationMs += (AutoTestClock.Now - startTime) * 1000.0;
            result.status = AutoTestStatusUtil.Aggregate(result.cases);
            CurrentCase = null;
            _state.Save();
        }

        static bool PassesFilters(AutoTestSuite suite, AutoTestCase c)
        {
            var opts = _state.options;
            if (opts.CaseFilter != null && opts.CaseFilter.Count > 0 &&
                !opts.CaseFilter.Contains(suite.Id + "/" + c.Id) && !opts.CaseFilter.Contains(suite.Id + "/*"))
                return opts.CaseFilter.All(f => !f.StartsWith(suite.Id + "/", StringComparison.Ordinal));
            if (!string.IsNullOrEmpty(opts.ScenarioFilter) && suite.Id == "scenarios")
                return (c.Id ?? "").IndexOf(opts.ScenarioFilter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                       (c.Name ?? "").IndexOf(opts.ScenarioFilter, StringComparison.OrdinalIgnoreCase) >= 0;
            return true;
        }

        static void MergeExtraSuites(AutoTestSession session)
        {
            if (session.ExtraSuites.Count == 0) return;
            foreach (var extra in session.ExtraSuites)
            {
                var existing = _state.SuiteResult(extra.suiteId);
                if (existing != null) _state.report.suites.Remove(existing);
                _state.report.suites.Add(extra);
            }

            session.ExtraSuites.Clear();
        }

        static void FailPlayGroup(string message)
        {
            foreach (var id in _state.playGroup)
            {
                var suite = AutoTestRegistry.CreateSuite(id);
                if (suite == null) continue;
                var result = _state.SuiteResult(id) ?? new TestSuiteResult
                    { suiteId = id, name = suite.DisplayName, description = suite.Description, mode = suite.Mode };
                if (!_state.report.suites.Contains(result)) _state.report.suites.Add(result);
                result.message = message;
                foreach (var c in SafeList(suite, _state.config))
                {
                    if (result.cases.Any(x => x.caseId == c.Id)) continue;
                    var r = AutoTestExecutor.Placeholder(suite, c, TestStatus.Error, message);
                    r.issues.Add(new TestIssue
                        { severity = Severity.Blocker, category = "Runner.PlayMode", title = message });
                    AutoTestExecutor.AssignFingerprints(r);
                    result.cases.Add(r);
                }
            }

            AddLive("✖ " + message);
            _state.suiteIndex += Math.Max(1, _state.playGroup.Count);
            _state.SetPhase(AutoTestRunState.PHASE_EXITING_PLAY); // rời trạng thái EnteringPlay để Advance đi tiếp
            Advance();
        }

        static void FailInFlightAndContinue()
        {
            // Đang Playing mà Editor đã về Edit mode và không có gì chạy (crash/reload) → ghi nhận và đi tiếp.
            AddLive("Phiên Play bị ngắt — chuyển sang phần tiếp theo.");
            _state.SetPhase(AutoTestRunState.PHASE_EXITING_PLAY);
            var key = ReadInFlight();
            if (!string.IsNullOrEmpty(key) && _state.playAttempts < MAX_PLAY_ATTEMPTS)
            {
                // Chạy lại nhóm Play: case gián đoạn sẽ bị ghi lỗi, các case còn lại chạy tiếp.
                RequestEnterPlay();
                return;
            }

            _state.suiteIndex += Math.Max(1, _state.playGroup.Count);
            Advance();
        }

        #endregion

        #region Finalize

        static void FinalizeRun()
        {
            if (_executing || _state == null || _state.phase == AutoTestRunState.PHASE_DONE) return;
            if (EditorApplication.isPlaying)
            {
                _state.SetPhase(AutoTestRunState.PHASE_EXITING_PLAY);
                EditorApplication.ExitPlaymode();
                return;
            }

            _state.SetPhase(AutoTestRunState.PHASE_FINALIZING);
            var report = _state.report;
            var config = _state.config;
            try
            {
                AddNotRunSuites();
                RestoreEnvironment();
                report.cancelled = _state.cancelRequested;
                report.cancelReason = _state.cancelReason;
                if (!string.IsNullOrEmpty(_state.runnerError))
                    report.cancelReason = string.IsNullOrEmpty(report.cancelReason)
                        ? "Lỗi runner: " + _state.runnerError
                        : report.cancelReason + " | Lỗi runner: " + _state.runnerError;
                report.finishedAt = DateTime.Now.ToString("o");
                report.durationMs = (DateTime.Now - StartedAt).TotalMilliseconds;
                report.env.packageVersion = EnvironmentInfo.PackageVersion();
                report.Recalculate();
                var previous = AutoTestReportHistory.FindPrevious(config, report);
                AutoTestReportDiff.Apply(report, previous);
                var html = AutoTestReportWriter.WriteAll(report, report.outputDir);
                AutoTestReportHistory.Prune(config);
                LastReportFolder = report.outputDir;
                AddLive($"✔ Xong — sức khoẻ {report.summary.healthScore}/100, " +
                        $"{report.summary.failed + report.summary.error} case lỗi. Report: {html}");
                if (_state.options.OpenReportWhenFinished && config.general.openReportWhenFinished &&
                    !Application.isBatchMode && File.Exists(html))
                    Application.OpenURL("file://" + html.Replace('\\', '/'));
            }
            catch (Exception e)
            {
                _state.runnerError = "Finalize: " + e.Message;
                Debug.LogException(e);
            }

            var exitWhenDone = _state.options.ExitEditorWhenDone;
            var exitCode = ComputeExitCode();
            _state.SetPhase(AutoTestRunState.PHASE_DONE);
            CurrentCase = null;
            CurrentStep = null;
            Changed?.Invoke();
            if (exitWhenDone)
            {
                AutoTestLog.Info($"Thoát Editor với exit code {exitCode}.");
                EditorApplication.Exit(exitCode);
            }
        }

        /// <summary>Suite chưa kịp chạy (do dừng) → vẫn ghi vào report dạng "Đã dừng" để QA biết phần thiếu.</summary>
        static void AddNotRunSuites()
        {
            for (var i = 0; i < _state.suiteIds.Count; i++)
            {
                var id = _state.suiteIds[i];
                var suite = AutoTestRegistry.CreateSuite(id);
                if (suite == null) continue;
                var result = _state.SuiteResult(id);
                if (result == null)
                {
                    result = new TestSuiteResult
                        { suiteId = id, name = suite.DisplayName, description = suite.Description, mode = suite.Mode };
                    _state.report.suites.Add(result);
                }

                foreach (var c in SafeList(suite, _state.config))
                    if (!result.cases.Any(x => x.caseId == c.Id) && PassesFilters(suite, c))
                        result.cases.Add(AutoTestExecutor.Placeholder(suite, c, TestStatus.Cancelled,
                            _state.cancelRequested ? "Không chạy (lượt chạy đã dừng)." : "Không chạy."));
            }
        }

        static void RestoreEnvironment()
        {
            if (_state == null) return;
            try
            {
                if (_state.sandboxTaken)
                {
                    PlayerDataSandbox.Restore();
                    _state.sandboxTaken = false;
                }
            }
            catch (Exception e)
            {
                Debug.LogError(AutoTestLog.PREFIX + "Khôi phục dữ liệu người chơi lỗi: " + e.Message);
            }

            if (_state.playModeStartSceneOverridden)
            {
                EditorSceneManager.playModeStartScene = string.IsNullOrEmpty(_state.previousPlayModeStartScene)
                    ? null
                    : AssetDatabase.LoadAssetAtPath<SceneAsset>(_state.previousPlayModeStartScene);
                _state.playModeStartSceneOverridden = false;
            }

            WriteInFlight(null);
            _state.Save();
        }

        static int ComputeExitCode()
        {
            if (_state == null) return EXIT_RUNNER_ERROR;
            if (!string.IsNullOrEmpty(_state.runnerError)) return EXIT_RUNNER_ERROR;
            if (_state.options.NoFail) return EXIT_OK;
            foreach (var suite in _state.report.suites)
            foreach (var c in suite.cases)
            {
                if (c.status == TestStatus.Error) return EXIT_FAILED;
                if (c.status == TestStatus.Failed && c.MaxSeverity() >= _state.options.FailOn) return EXIT_FAILED;
            }

            return EXIT_OK;
        }

        #endregion

        #region Helpers

        static void OnProgress(TestCaseResult result, string step)
        {
            if (result != null && result.status == TestStatus.Running) CurrentCase = result.name;
            if (step != null) CurrentStep = step;
            Changed?.Invoke();
        }

        static int EstimateCases(List<AutoTestSuite> suites, AutoTestConfig config)
        {
            var adapter = AutoTestRegistry.CreateAdapter(config);
            var total = 0;
            foreach (var s in suites)
                try
                {
                    total += s.BuildCases(new AutoTestBuildContext { Config = config, Game = adapter, ForListing = true })
                        .Count(c => _state == null || PassesFilters(s, c));
                }
                catch
                {
                    total++;
                }

            return total;
        }

        static void RecountProgress()
        {
            CompletedCases = _state.report.suites.Sum(s => s.cases.Count);
            try
            {
                TotalCases = Math.Max(CompletedCases, EstimateCases(
                    _state.suiteIds.Select(AutoTestRegistry.CreateSuite).Where(s => s != null).ToList(), _state.config));
            }
            catch
            {
                TotalCases = CompletedCases;
            }
        }

        static IEnumerable<AutoTestCase> SafeList(AutoTestSuite suite, AutoTestConfig config)
        {
            try
            {
                return suite.BuildCases(new AutoTestBuildContext
                    { Config = config, Game = AutoTestRegistry.CreateAdapter(config), ForListing = true }).ToList();
            }
            catch
            {
                return Array.Empty<AutoTestCase>();
            }
        }

        static TestCaseResult ErrorCase(AutoTestSuite suite, string id, string name, Exception e)
        {
            var r = new TestCaseResult
            {
                suiteId = suite.Id, caseId = id, name = name, status = TestStatus.Error,
                message = $"{e.GetType().Name}: {e.Message}", startedAt = DateTime.Now.ToString("o")
            };
            r.issues.Add(new TestIssue
            {
                severity = Severity.Critical, category = "Runner.Exception", title = $"{name} lỗi: {e.GetType().Name}",
                message = e.Message, stackTrace = AutoTestExecutor.ShortStack(e.StackTrace)
            });
            AutoTestExecutor.AssignFingerprints(r);
            return r;
        }

        static void AddSuiteNote(string id, string name, TestStatus status, string message)
        {
            if (_state.SuiteResult(id) != null) return;
            _state.report.suites.Add(new TestSuiteResult
                { suiteId = id, name = name, status = status, message = message });
            _state.Save();
        }

        const string IN_FLIGHT_KEY = "EZGAutoTest.InFlightCase";

        static void WriteInFlight(string key)
        {
            _inFlightCaseKey = key;
            if (string.IsNullOrEmpty(key)) SessionState.EraseString(IN_FLIGHT_KEY);
            else SessionState.SetString(IN_FLIGHT_KEY, key);
        }

        static string ReadInFlight()
        {
            return SessionState.GetString(IN_FLIGHT_KEY, "");
        }

        static string Icon(TestStatus s)
        {
            switch (s)
            {
                case TestStatus.Passed: return "✔";
                case TestStatus.Warning: return "⚠";
                case TestStatus.Failed: return "✖";
                case TestStatus.Error: return "💥";
                case TestStatus.Skipped: return "⤼";
                case TestStatus.Cancelled: return "■";
                default: return "•";
            }
        }

        static void AddLive(string line)
        {
            _liveLog.Add($"[{DateTime.Now:HH:mm:ss}] {line}");
            if (_liveLog.Count > LIVE_LOG_MAX) _liveLog.RemoveRange(0, _liveLog.Count - LIVE_LOG_MAX);
            if (Application.isBatchMode) Debug.Log(AutoTestLog.PREFIX + line);
        }

        #endregion
    }
}
