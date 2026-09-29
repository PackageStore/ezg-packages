using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;

namespace Ezg.AutoTest
{
    /// <summary>
    ///     Chạy các suite Play NGAY TRONG player build trên device (chỉ có trong build bật define EZG_AUTOTEST).
    ///     <para>
    ///         Kích hoạt khi mở app bằng một trong các cách: (1) Android intent extra <c>ezg_autotest_run</c> /
    ///         <c>ezg_autotest_suites</c> (Editor gửi qua <c>am start --es</c>), hoặc action Firebase Test Lab Game Loop;
    ///         (2) argument <c>-ezgAutotestRun &lt;id&gt; -ezgAutotestSuites a,b</c> (iOS devicectl / desktop) hoặc biến môi
    ///         trường <c>EZG_AUTOTEST_RUN</c>; (3) TextAsset bake <c>Resources/EZGAutoTestDeviceConfig</c> có
    ///         <c>autoRunOnLaunch = true</c>. Không bao giờ tự chạy trong Editor.
    ///     </para>
    ///     <para>
    ///         Report ghi ở <c>Application.persistentDataPath/EZGAutoTest/&lt;runId&gt;/report.json</c> sau MỖI case
    ///         (crash giữa chừng vẫn còn phần đã chạy), cuối cùng ghi <c>done.flag</c>. Tiến độ phát ra log dạng
    ///         <c>EZG_AUTOTEST {json}</c> để Editor đọc qua logcat.
    ///     </para>
    /// </summary>
    public static class AutoTestDeviceRunner
    {
        const string RUN_ID_FORMAT = "yyyyMMdd_HHmmss";
        const float SETUP_TIMEOUT_SECONDS = 120f;
        const float TEARDOWN_TIMEOUT_SECONDS = 30f;

#if !UNITY_EDITOR
        static LaunchInfo _pending;
#endif
        static long _bootLogCursor = -1;
        static CancellationTokenSource _cts;
        static TestRunReport _currentReport;
        static string _currentOutputDir;

        /// <summary>Đang có lượt chạy trên device.</summary>
        public static bool IsRunning { get; private set; }

        /// <summary>Id lượt chạy hiện tại (null nếu không chạy).</summary>
        public static string CurrentRunId { get; private set; }

        /// <summary>Bắn khi lượt chạy xong (kể cả lỗi / bị dừng) — menu debug trong game có thể hiện kết quả.</summary>
        public static event Action<TestRunReport> Finished;

        /// <summary>Thông tin kích hoạt đọc được lúc app mở.</summary>
        sealed class LaunchInfo
        {
            public string RunId;
            public List<string> Suites = new();
            #pragma warning disable 0649 // chỉ gán trên Android (Game Loop)
            public bool GameLoop;
            #pragma warning restore 0649
            public int Scenario;
            public string Source;
            public DeviceRunRequest Baked;
        }

        #region Bootstrap (chỉ player build)

#if !UNITY_EDITOR
        /// <summary>Rất sớm: dò yêu cầu chạy và bật bắt log ngay để không sót log lúc game boot.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void EarlyDetect()
        {
            try
            {
                _pending = DetectLaunch();
                if (_pending == null) return;
                var config = ParseConfig(_pending.Baked?.configJson);
                AutoTestLogCapture.Start(config.general.ignoredLogPatterns);
                _bootLogCursor = AutoTestLogCapture.Cursor;
            }
            catch (Exception e)
            {
                Debug.LogWarning(AutoTestLog.PREFIX + "Dò yêu cầu auto test lỗi: " + e.Message);
            }
        }

        /// <summary>Sau khi scene đầu load xong: bắt đầu chạy nếu có yêu cầu.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoStart()
        {
            try
            {
                // SubsystemRegistration có thể chưa đọc được Resources/intent → thử lại lần nữa.
                if (_pending == null) _pending = DetectLaunch();
                if (_pending == null) return;
                var launch = _pending;
                _pending = null;
                var req = new DeviceRunRequest
                {
                    runId = launch.RunId,
                    suites = launch.Suites,
                    configJson = launch.Baked?.configJson,
                    packageVersion = launch.Baked?.packageVersion,
                    buildNumber = launch.Baked?.buildNumber
                };
                Observe(RunInternal(req, launch, CancellationToken.None));
            }
            catch (Exception e)
            {
                Emit(new DeviceProtocol.EventWriter(DeviceProtocol.EVENT_ERROR).Str("msg", "Không khởi động được: " + e.Message));
            }
        }
#endif

        /// <summary>Đọc intent / argument / biến môi trường / config bake. null = không chạy.</summary>
        static LaunchInfo DetectLaunch()
        {
            var baked = LoadBakedRequest();
            var info = new LaunchInfo { Baked = baked };

            ReadAndroidIntent(info);

            if (string.IsNullOrEmpty(info.RunId) && !info.GameLoop)
            {
                var args = SafeCommandLine();
                var run = ArgValue(args, DeviceProtocol.ARG_RUN);
                if (!string.IsNullOrEmpty(run))
                {
                    info.RunId = run;
                    info.Suites = DeviceProtocol.SplitSuites(ArgValue(args, DeviceProtocol.ARG_SUITES));
                    info.Source = "command-line";
                }
            }

            if (string.IsNullOrEmpty(info.RunId) && !info.GameLoop)
            {
                var run = SafeEnv(DeviceProtocol.ENV_RUN);
                if (!string.IsNullOrEmpty(run))
                {
                    info.RunId = run;
                    info.Suites = DeviceProtocol.SplitSuites(SafeEnv(DeviceProtocol.ENV_SUITES));
                    info.Source = "environment";
                }
            }

            var requested = !string.IsNullOrEmpty(info.RunId) || info.GameLoop;
            if (!requested && baked != null && baked.autoRunOnLaunch)
            {
                info.RunId = baked.runId;
                info.Source = "baked-config";
                requested = true;
            }

            if (!requested) return null;

            // Không chỉ định suite → dùng danh sách bake (hoặc config.device.suites) — rỗng hết = mọi suite device.
            if (info.Suites.Count == 0 && baked != null && baked.suites != null) info.Suites = new List<string>(baked.suites);
            if (info.Suites.Count == 0)
            {
                var config = ParseConfig(baked?.configJson);
                if (config.device?.suites != null) info.Suites = new List<string>(config.device.suites);
            }

            return info;
        }

        static DeviceRunRequest LoadBakedRequest()
        {
            try
            {
                var asset = Resources.Load<TextAsset>(DeviceProtocol.RESOURCE_NAME);
                if (asset == null || string.IsNullOrWhiteSpace(asset.text)) return null;
                return JsonUtility.FromJson<DeviceRunRequest>(asset.text);
            }
            catch (Exception e)
            {
                Debug.LogWarning(AutoTestLog.PREFIX + "Config device bake bị hỏng: " + e.Message);
                return null;
            }
        }

        /// <summary>Đọc extra từ intent khởi động Activity (Android). Mọi lỗi JNI đều nuốt.</summary>
        static void ReadAndroidIntent(LaunchInfo info)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                {
                    if (activity == null) return;
                    using (var intent = activity.Call<AndroidJavaObject>("getIntent"))
                    {
                        if (intent == null) return;
                        var action = intent.Call<string>("getAction");
                        if (action == DeviceProtocol.ACTION_GAME_LOOP)
                        {
                            info.GameLoop = true;
                            info.Scenario = intent.Call<int>("getIntExtra", "scenario", 0);
                            info.Source = "game-loop";
                        }

                        var run = intent.Call<string>("getStringExtra", DeviceProtocol.EXTRA_RUN);
                        if (!string.IsNullOrEmpty(run))
                        {
                            info.RunId = run;
                            if (!info.GameLoop) info.Source = "intent";
                        }

                        var suites = intent.Call<string>("getStringExtra", DeviceProtocol.EXTRA_SUITES);
                        if (!string.IsNullOrEmpty(suites)) info.Suites = DeviceProtocol.SplitSuites(suites);
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning(AutoTestLog.PREFIX + "Không đọc được intent: " + e.Message);
            }
#endif
        }

        #endregion

        #region Public API

        /// <summary>
        ///     Chạy test ngay trong tiến trình hiện tại (vd từ menu debug trong game). Trả report khi xong; không
        ///     ném exception (lỗi ghi vào report + log). Trả null nếu đang có lượt khác chạy.
        /// </summary>
        public static Task<TestRunReport> RunInProcess(DeviceRunRequest req)
        {
            return RunInProcess(req, CancellationToken.None);
        }

        /// <inheritdoc cref="RunInProcess(DeviceRunRequest)" />
        public static Task<TestRunReport> RunInProcess(DeviceRunRequest req, CancellationToken token)
        {
            req ??= new DeviceRunRequest();
            var launch = new LaunchInfo
            {
                RunId = req.runId,
                Suites = req.suites != null ? new List<string>(req.suites) : new List<string>(),
                Source = "in-process"
            };
            return RunInternal(req, launch, token);
        }

        /// <summary>Dừng lượt chạy hiện tại (case đang chạy bị huỷ, report vẫn được ghi).</summary>
        public static void Cancel()
        {
            try
            {
                _cts?.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Lượt chạy vừa kết thúc.
            }
        }

        #endregion

        #region Chạy

        static async Task<TestRunReport> RunInternal(DeviceRunRequest req, LaunchInfo launch, CancellationToken external)
        {
            if (IsRunning)
            {
                AutoTestLog.Warn("Đang có lượt auto test khác chạy trên device — bỏ qua yêu cầu mới.");
                return null;
            }

            IsRunning = true;
            var config = ParseConfig(req.configJson);
            var runId = DeviceProtocol.SanitizeRunId(launch.RunId) ??
                        DateTime.Now.ToString(RUN_ID_FORMAT, CultureInfo.InvariantCulture);
            CurrentRunId = runId;
            var outputDir = Path.Combine(Application.persistentDataPath, DeviceProtocol.OUTPUT_FOLDER, runId)
                .Replace('\\', '/');
            _currentOutputDir = outputDir;
            _cts = CancellationTokenSource.CreateLinkedTokenSource(external);
            var token = _cts.Token;
            var prevSleep = Screen.sleepTimeout;
            var startClock = AutoTestClock.Now;
            TestRunReport report = null;

            try
            {
                PrepareOutputDir(outputDir);
                AutoTestDeviceRunnerHost.Ensure();
                AutoTestClock.EnsureRuntimePump();
                AutoTestLogCapture.Start(config.general.ignoredLogPatterns);
                if (_bootLogCursor < 0) _bootLogCursor = AutoTestLogCapture.Cursor;
                // Màn hình tắt ⇒ Unity pause ⇒ test treo → giữ màn hình sáng suốt lượt chạy.
                Screen.sleepTimeout = SleepTimeout.NeverSleep;

                var adapter = AutoTestRegistry.CreateAdapter(config);
                var hooks = AutoTestRegistry.CreateHooks();
                var session = new AutoTestSession(config, adapter, hooks, outputDir, true, SystemInfo.deviceModel)
                {
                    RunToken = token,
                    SessionLogCursor = _bootLogCursor
                };
                session.Progress = (result, step) => OnProgress(runId, result, step);

                report = new TestRunReport
                {
                    runId = runId,
                    title = $"{Application.productName} — {SystemInfo.deviceModel}",
                    startedAt = DateTime.Now.ToString("o", CultureInfo.InvariantCulture),
                    trigger = "Device",
                    outputDir = outputDir,
                    env = CollectEnvironment(adapter, req)
                };
                _currentReport = report;

                var requested = launch.Suites ?? new List<string>();
                var all = AutoTestRegistry.CreateSuites();
                var suites = all.Where(s => s.SupportsDevice && s.Mode == ExecutionMode.Play &&
                                            (requested.Count == 0 || ContainsId(requested, s.Id)) &&
                                            config.IsSuiteEnabled(s.Id)).ToList();
                var missing = requested.Where(id => !all.Any(s => string.Equals(s.Id, id,
                    StringComparison.OrdinalIgnoreCase) && s.SupportsDevice)).ToList();

                Emit(new DeviceProtocol.EventWriter(DeviceProtocol.EVENT_START)
                    .Str("run", runId)
                    .List("suites", suites.Select(s => s.Id))
                    .List("missing", missing)
                    .Str("dir", outputDir)
                    .Str("device", SystemInfo.deviceModel)
                    .Str("src", launch.Source));
                if (missing.Count > 0)
                    AutoTestLog.Warn("Suite không có / không chạy được trên device: " + string.Join(", ", missing));
                WriteReport(report, outputDir);

                if (suites.Count == 0)
                    Emit(new DeviceProtocol.EventWriter(DeviceProtocol.EVENT_ERROR).Str("run", runId)
                        .Str("msg", "Không có suite nào để chạy trên device (kiểm tra danh sách suite trong Settings > Device)."));

                foreach (var suite in suites)
                {
                    if (token.IsCancellationRequested) break;
                    await RunSuite(session, suite, report, runId, outputDir, token);
                }

                if (token.IsCancellationRequested)
                {
                    report.cancelled = true;
                    report.cancelReason = "Lượt chạy bị dừng trên device.";
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning(AutoTestLog.PREFIX + "Device runner lỗi: " + e);
                Emit(new DeviceProtocol.EventWriter(DeviceProtocol.EVENT_ERROR).Str("run", runId)
                    .Str("msg", e.GetType().Name + ": " + e.Message));
                if (report != null)
                {
                    report.cancelled = true;
                    report.cancelReason = "Runner lỗi: " + e.Message;
                }
            }
            finally
            {
                Screen.sleepTimeout = prevSleep;
                if (report != null)
                {
                    report.finishedAt = DateTime.Now.ToString("o", CultureInfo.InvariantCulture);
                    report.durationMs = (AutoTestClock.Now - startClock) * 1000.0;
                    report.Recalculate();
                    WriteReport(report, outputDir);
                }

                var s = report?.summary ?? new RunSummary();
                WriteDoneFlag(outputDir, report);
                Emit(new DeviceProtocol.EventWriter(DeviceProtocol.EVENT_DONE)
                    .Str("run", runId)
                    .Str("dir", outputDir)
                    .Num("total", s.total)
                    .Num("passed", s.passed)
                    .Num("failed", s.failed + s.error)
                    .Num("health", s.healthScore));

                if (launch.GameLoop) FinishGameLoop(launch, report);

                _currentReport = null;
                IsRunning = false;
                CurrentRunId = null;
                _cts.Dispose();
                _cts = null;
                try
                {
                    Finished?.Invoke(report);
                }
                catch (Exception e)
                {
                    AutoTestLog.Warn("Handler Finished lỗi: " + e.Message);
                }
            }

            return report;
        }

        static async Task RunSuite(AutoTestSession session, AutoTestSuite suite, TestRunReport report, string runId,
            string outputDir, CancellationToken token)
        {
            var result = new TestSuiteResult
            {
                suiteId = suite.Id,
                name = suite.DisplayName,
                description = suite.Description,
                mode = suite.Mode
            };
            report.suites.Add(result);
            var startClock = AutoTestClock.Now;

            List<AutoTestCase> cases;
            try
            {
                cases = suite.BuildCases(new AutoTestBuildContext
                    { Config = session.Config, Game = session.Game, IsDevice = true }).ToList();
            }
            catch (Exception e)
            {
                result.cases.Add(ErrorCase(suite.Id, "build-cases", "Liệt kê case", e));
                WriteReport(report, outputDir);
                return;
            }

            var setupResult = new TestCaseResult { suiteId = suite.Id, caseId = "_setup", name = "SetUp" };
            var suiteCtx = new AutoTestContext(session, setupResult, token);
            var setUpFailed = false;
            if (cases.Count > 0)
                try
                {
                    session.CurrentToken = token;
                    var setUp = suite.SetUp(suiteCtx) ?? Task.CompletedTask;
                    var done = await Task.WhenAny(setUp, AutoTestClock.Seconds(SETUP_TIMEOUT_SECONDS, token));
                    token.ThrowIfCancellationRequested();
                    if (done != setUp) throw new TimeoutException($"SetUp quá {SETUP_TIMEOUT_SECONDS:0}s");
                    await setUp;
                }
                catch (OperationCanceledException)
                {
                    // Dừng trong SetUp → case bên dưới đánh "Đã dừng".
                }
                catch (Exception e)
                {
                    setUpFailed = true;
                    result.cases.Add(ErrorCase(suite.Id, "_setup", "SetUp của suite", e));
                }
                finally
                {
                    session.CurrentToken = default;
                }

            foreach (var c in cases)
            {
                TestCaseResult r;
                if (token.IsCancellationRequested)
                    r = AutoTestExecutor.Placeholder(suite, c, TestStatus.Cancelled, "Lượt chạy đã dừng.");
                else if (setUpFailed)
                    r = AutoTestExecutor.Placeholder(suite, c, TestStatus.Skipped, "SetUp của suite lỗi.");
                else
                    r = await AutoTestExecutor.RunCase(session, suite, c, token);

                r.device = SystemInfo.deviceModel;
                result.cases.Add(r);
                result.durationMs = (AutoTestClock.Now - startClock) * 1000.0;
                result.status = AutoTestStatusUtil.Aggregate(result.cases);
                WriteReport(report, outputDir);
                Emit(new DeviceProtocol.EventWriter(DeviceProtocol.EVENT_CASE)
                    .Str("run", runId)
                    .Str("suite", suite.Id)
                    .Str("case", c.Id)
                    .Str("name", c.Name)
                    .Str("status", r.status.ToString())
                    .Num("ms", r.durationMs)
                    .Str("msg", r.message));
            }

            try
            {
                var tearDown = suite.TearDown(suiteCtx) ?? Task.CompletedTask;
                await Task.WhenAny(tearDown, AutoTestClock.Seconds(TEARDOWN_TIMEOUT_SECONDS));
            }
            catch (Exception e)
            {
                AutoTestLog.Warn($"TearDown {suite.Id} lỗi: {e.Message}");
            }

            result.durationMs = (AutoTestClock.Now - startClock) * 1000.0;
            result.status = AutoTestStatusUtil.Aggregate(result.cases);
            WriteReport(report, outputDir);
        }

        static void OnProgress(string runId, TestCaseResult result, string step)
        {
            // Chỉ phát sự kiện lúc case bắt đầu (step null + đang chạy) — không spam logcat theo từng bước.
            if (result == null || step != null || result.status != TestStatus.Running) return;
            Emit(new DeviceProtocol.EventWriter(DeviceProtocol.EVENT_BEGIN)
                .Str("run", runId)
                .Str("suite", result.suiteId)
                .Str("case", result.caseId)
                .Str("name", result.name));
        }

        static TestCaseResult ErrorCase(string suiteId, string caseId, string name, Exception e)
        {
            var r = new TestCaseResult
            {
                suiteId = suiteId,
                caseId = caseId,
                name = name,
                status = TestStatus.Error,
                message = $"{e.GetType().Name}: {e.Message}",
                startedAt = DateTime.Now.ToString("o", CultureInfo.InvariantCulture),
                device = SystemInfo.deviceModel
            };
            r.issues.Add(new TestIssue
            {
                severity = Severity.Critical,
                category = "Runner",
                title = $"{name} lỗi: {e.GetType().Name}",
                message = e.Message,
                stackTrace = AutoTestExecutor.ShortStack(e.StackTrace)
            });
            AutoTestExecutor.AssignFingerprints(r);
            return r;
        }

        #endregion

        #region Report / file

        static void PrepareOutputDir(string outputDir)
        {
            Directory.CreateDirectory(outputDir);
            var flag = Path.Combine(outputDir, DeviceProtocol.DONE_FLAG);
            if (File.Exists(flag)) File.Delete(flag);
        }

        /// <summary>Ghi report.json (qua file tạm rồi thay thế — crash giữa lúc ghi không làm hỏng bản cũ).</summary>
        static void WriteReport(TestRunReport report, string outputDir)
        {
            try
            {
                var path = Path.Combine(outputDir, DeviceProtocol.REPORT_FILE);
                var tmp = path + ".tmp";
                File.WriteAllText(tmp, JsonUtility.ToJson(report));
                if (File.Exists(path)) File.Delete(path);
                File.Move(tmp, path);
            }
            catch (Exception e)
            {
                Debug.LogWarning(AutoTestLog.PREFIX + "Không ghi được report.json: " + e.Message);
            }
        }

        static void WriteDoneFlag(string outputDir, TestRunReport report)
        {
            try
            {
                var s = report?.summary ?? new RunSummary();
                File.WriteAllText(Path.Combine(outputDir, DeviceProtocol.DONE_FLAG),
                    $"total={s.total} passed={s.passed} failed={s.failed + s.error} health={s.healthScore}");
            }
            catch (Exception e)
            {
                Debug.LogWarning(AutoTestLog.PREFIX + "Không ghi được done.flag: " + e.Message);
            }
        }

        /// <summary>Ghi nốt report khi app bị thoát giữa chừng (Application.Quit / hệ điều hành đóng app).</summary>
        internal static void FlushOnQuit()
        {
            var report = _currentReport;
            if (report == null || string.IsNullOrEmpty(_currentOutputDir)) return;
            report.cancelled = true;
            report.cancelReason = "App bị thoát khi đang chạy test.";
            report.finishedAt = DateTime.Now.ToString("o", CultureInfo.InvariantCulture);
            report.Recalculate();
            WriteReport(report, _currentOutputDir);
        }

        static RunEnvironment CollectEnvironment(IGameAdapter adapter, DeviceRunRequest req)
        {
            var env = new RunEnvironment();
            try
            {
                env.projectName = Application.productName;
                env.productName = Application.productName;
                env.companyName = Application.companyName;
                env.bundleId = Application.identifier;
                env.appVersion = Application.version;
                env.buildNumber = req?.buildNumber;
                env.unityVersion = Application.unityVersion;
                env.platform = Application.platform.ToString();
                env.buildTarget = Application.platform.ToString();
                env.renderPipeline = GraphicsSettings.currentRenderPipeline != null
                    ? GraphicsSettings.currentRenderPipeline.GetType().Name
                    : "Built-in";
                env.machine = SystemInfo.deviceName;
                env.os = SystemInfo.operatingSystem;
                env.deviceModel = SystemInfo.deviceModel;
                env.deviceOs = SystemInfo.operatingSystem;
                env.gpu = $"{SystemInfo.graphicsDeviceName} ({SystemInfo.graphicsDeviceType}, {SystemInfo.graphicsMemorySize} MB)";
                env.systemMemoryMb = SystemInfo.systemMemorySize;
                env.resolution = $"{Screen.width}x{Screen.height} @{Screen.dpi:0}dpi";
                env.adapter = adapter?.Name;
                env.packageVersion = req?.packageVersion;
            }
            catch (Exception e)
            {
                AutoTestLog.Warn("Không đọc đủ thông tin device: " + e.Message);
            }

            return env;
        }

        #endregion

        #region Firebase Test Lab Game Loop

        /// <summary>Ghi kết quả vào URI mà Test Lab truyền qua intent data, rồi finish Activity (Test Lab chờ việc này).</summary>
        static void FinishGameLoop(LaunchInfo launch, TestRunReport report)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                var json = JsonUtility.ToJson(BuildGameLoopResult(launch, report), true);
                var bytes = Encoding.UTF8.GetBytes(json);
                var signed = new sbyte[bytes.Length];
                Buffer.BlockCopy(bytes, 0, signed, 0, bytes.Length);

                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                {
                    if (activity == null) return;
                    try
                    {
                        using (var intent = activity.Call<AndroidJavaObject>("getIntent"))
                        using (var uri = intent?.Call<AndroidJavaObject>("getData"))
                        {
                            if (uri != null)
                                using (var resolver = activity.Call<AndroidJavaObject>("getContentResolver"))
                                using (var afd = resolver.Call<AndroidJavaObject>("openAssetFileDescriptor", uri, "w"))
                                using (var pfd = afd.Call<AndroidJavaObject>("getParcelFileDescriptor"))
                                using (var fd = pfd.Call<AndroidJavaObject>("getFileDescriptor"))
                                using (var stream = new AndroidJavaObject("java.io.FileOutputStream", fd))
                                {
                                    stream.Call("write", signed);
                                    stream.Call("flush");
                                    stream.Call("close");
                                    afd.Call("close");
                                }
                        }
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning(AutoTestLog.PREFIX + "Không ghi được kết quả Game Loop: " + e.Message);
                    }

                    activity.Call("finish");
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning(AutoTestLog.PREFIX + "Kết thúc Game Loop lỗi: " + e.Message);
            }
#endif
        }

        static GameLoopResult BuildGameLoopResult(LaunchInfo launch, TestRunReport report)
        {
            var result = new GameLoopResult
            {
                runId = report?.runId,
                scenario = launch.Scenario,
                device = SystemInfo.deviceModel,
                startedAt = report?.startedAt,
                finishedAt = report?.finishedAt,
                summary = report?.summary ?? new RunSummary()
            };
            if (report == null) return result;
            foreach (var suite in report.suites)
            foreach (var c in suite.cases)
                if (AutoTestStatusUtil.IsBad(c.status))
                    result.failures.Add(new GameLoopFailure
                        { suite = suite.suiteId, testCase = c.caseId, status = c.status.ToString(), message = c.message });
            return result;
        }

        #endregion

        #region Helpers

        /// <summary>Phát một dòng giao thức (không kèm stack trace để logcat gọn, dễ parse).</summary>
        static void Emit(DeviceProtocol.EventWriter evt)
        {
            try
            {
                Debug.LogFormat(LogType.Log, LogOption.NoStacktrace, null, "{0}", DeviceProtocol.LOG_PREFIX + evt.Build());
            }
            catch
            {
                // Log không được thì thôi — report.json vẫn là nguồn chính.
            }
        }

        static AutoTestConfig ParseConfig(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return new AutoTestConfig();
            try
            {
                return JsonUtility.FromJson<AutoTestConfig>(json) ?? new AutoTestConfig();
            }
            catch (Exception e)
            {
                Debug.LogWarning(AutoTestLog.PREFIX + "configJson hỏng, dùng cấu hình mặc định: " + e.Message);
                return new AutoTestConfig();
            }
        }

        static bool ContainsId(List<string> ids, string id)
        {
            foreach (var s in ids)
                if (string.Equals(s, id, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        static string[] SafeCommandLine()
        {
            try
            {
                return Environment.GetCommandLineArgs() ?? Array.Empty<string>();
            }
            catch
            {
                return Array.Empty<string>();
            }
        }

        static string ArgValue(string[] args, string name)
        {
            for (var i = 0; i < args.Length; i++)
            {
                var a = args[i];
                if (string.Equals(a, name, StringComparison.OrdinalIgnoreCase))
                    return i + 1 < args.Length ? args[i + 1] : null;
                // Hỗ trợ cả dạng "-ezgAutotestRun=abc".
                if (a != null && a.StartsWith(name + "=", StringComparison.OrdinalIgnoreCase))
                    return a.Substring(name.Length + 1);
            }

            return null;
        }

        static string SafeEnv(string name)
        {
            try
            {
                return Environment.GetEnvironmentVariable(name);
            }
            catch
            {
                return null;
            }
        }

        static void Observe(Task task)
        {
            task.ContinueWith(t => Debug.LogWarning(AutoTestLog.PREFIX + "Device runner lỗi: " + t.Exception),
                TaskContinuationOptions.OnlyOnFaulted);
        }

        #endregion
    }

    /// <summary>GameObject ẩn giữ runner sống qua đổi scene + ghi nốt report khi app bị thoát.</summary>
    [AddComponentMenu("")]
    public sealed class AutoTestDeviceRunnerHost : MonoBehaviour
    {
        const string HOST_NAME = "[EZG AutoTest Device Runner]";

        public static AutoTestDeviceRunnerHost Instance { get; private set; }

        public static void Ensure()
        {
            if (Instance != null) return;
            var go = new GameObject(HOST_NAME) { hideFlags = HideFlags.HideAndDontSave };
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<AutoTestDeviceRunnerHost>();
        }

        void OnApplicationQuit()
        {
            AutoTestDeviceRunner.FlushOnQuit();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }
    }
}
