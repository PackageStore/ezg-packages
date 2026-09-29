using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;

namespace Ezg.AutoTest.Editor
{
    /// <summary>
    ///     Suite "Device / E2E": Editor điều phối build APK test → cài lên mọi máy Android đang cắm → đo cold start →
    ///     mở app kèm intent kích hoạt <see cref="AutoTestDeviceRunner" /> → theo dõi logcat (tiến độ, crash, ANR, app
    ///     chết) → kéo report về và gộp các suite chạy trên máy vào report chung. iPhone chạy ở chế độ thử nghiệm
    ///     (xem <see cref="IosDeviceBridge" />). Các case chạy tuần tự trong Edit mode, không chặn Editor (trừ lúc build).
    /// </summary>
    public sealed class DeviceE2ESuite : AutoTestSuite
    {
        const string CATEGORY_PREPARE = "Chuẩn bị";
        const string CATEGORY_BUILD = "Build & cài đặt";
        const string CATEGORY_PERF = "Hiệu năng";
        const string CATEGORY_E2E = "E2E";
        const string CATEGORY_CI = "CI";

        const string KEY_ADB = "device.adb";
        const string KEY_IOS = "device.ios";
        const string KEY_DEVICECTL = "device.devicectl";
        const string KEY_ANDROID = "device.android";
        const string KEY_IOS_DEVICES = "device.iosDevices";
        const string KEY_APK = "device.apk";
        const string KEY_INSTALLED = "device.installed";
        const string KEY_COMPONENT = "device.component.";
        const string KEY_RUN_ID = "device.runId";

        const float TIMEOUT_TOOLING = 90f;
        const float TIMEOUT_DEVICES = 180f;
        const float TIMEOUT_BUILD = 1800f;
        const float TIMEOUT_INSTALL = 1200f;
        const float TIMEOUT_COLD_START = 900f;
        const float TIMEOUT_RUN_EXTRA = 120f;
        const float TIMEOUT_IOS_EXTRA = 900f;
        const float TIMEOUT_FTL = 60f;

        const int COLD_START_RUNS = 3;
        const double COLD_START_SETTLE_SECONDS = 5;
        const double COLD_START_MAX_MS = 5000;
        const double BEFORE_LAUNCH_SECONDS = 1;
        const double START_EVENT_TIMEOUT_SECONDS = 120;
        const double PID_POLL_SECONDS = 5;
        const double NO_PROCESS_GRACE_SECONDS = 20;
        const double CRASH_TAIL_SECONDS = 3;
        const double RUN_POLL_SECONDS = 0.25;
        const double MIN_RUN_TIMEOUT_SECONDS = 60;
        const double IOS_POLL_SECONDS = 10;
        const int MAX_LINES_PER_TICK = 3000;
        const int EXCERPT_LINES = 60;
        const int DUMP_LINES = 400;
        const int MAX_ERRORS_IN_MESSAGE = 10;
        const int SERIAL_SUFFIX = 4;
        const double BYTES_PER_MB = 1024.0 * 1024.0;
        const string RUN_ID_FORMAT = "yyyyMMdd_HHmmss";
        const string DEVICE_FOLDER_PREFIX = "device_";

        public override string Id => "device";
        public override string DisplayName => "Device / E2E";

        public override string Description =>
            "Chạy trên máy thật: build APK test (define EZG_AUTOTEST), cài lên mọi máy Android đang cắm USB, đo thời " +
            "gian khởi động nguội, mở game chạy các suite device (smoke, UI audit, economy, hiệu năng, kịch bản…), bắt " +
            "crash/ANR qua logcat rồi gộp kết quả từng máy vào report. iPhone: thử nghiệm qua xcrun devicectl.";

        public override ExecutionMode Mode => ExecutionMode.Device;
        public override int Order => 80;
        public override string Icon => "d_BuildSettings.Android.Small";
        public override bool SupportsDevice => false;
        public override bool MutatesPlayerData => false;

        public override IEnumerable<AutoTestCase> BuildCases(AutoTestBuildContext ctx)
        {
            var cfg = ctx?.Config?.device ?? new DeviceConfig();
            var runTimeout = (float)Math.Max(MIN_RUN_TIMEOUT_SECONDS, cfg.runTimeoutSeconds);
            yield return new AutoTestCase("tooling", "Công cụ adb / xcrun",
                "Tìm adb (Android SDK của Unity, ANDROID_HOME, PATH…) và xcrun devicectl (iOS, chỉ macOS).",
                Tooling, CATEGORY_PREPARE, TIMEOUT_TOOLING);
            yield return new AutoTestCase("devices", "Device đang kết nối",
                "Liệt kê máy Android (adb) và iPhone (devicectl); báo máy chưa cho phép gỡ lỗi USB / offline.",
                Devices, CATEGORY_PREPARE, TIMEOUT_DEVICES);
            yield return new AutoTestCase("build", "Build APK test",
                "Dùng APK có sẵn (Settings > Device > apkPath) hoặc build APK Development có define EZG_AUTOTEST.",
                Build, CATEGORY_BUILD, TIMEOUT_BUILD);
            yield return new AutoTestCase("install", "Cài APK lên device",
                "adb install lên từng máy; lỗi cài đặt kèm hướng dẫn sửa theo mã lỗi.",
                Install, CATEGORY_BUILD, TIMEOUT_INSTALL);
            yield return new AutoTestCase("cold-start", "Thời gian khởi động nguội",
                $"Mở game {COLD_START_RUNS} lần từ trạng thái tắt hẳn (am start -W), lấy trung vị TotalTime; kiểm tra " +
                "app không tắt ngay sau khi mở.",
                ColdStart, CATEGORY_PERF, TIMEOUT_COLD_START);
            yield return new AutoTestCase("run", "Chạy test trên device Android",
                "Mở game kèm intent kích hoạt auto test, theo dõi tiến độ + crash/ANR qua logcat, kéo report về và gộp " +
                "các suite chạy trên máy vào report.",
                Run, CATEGORY_E2E, runTimeout + TIMEOUT_RUN_EXTRA);
            yield return new AutoTestCase("ios", "Chạy test trên iPhone (thử nghiệm)",
                $"Chỉ chạy khi EditorPrefs '{IosDeviceBridge.APP_PATH_PREF}' trỏ tới .app/.ipa bản build test: cài, " +
                "mở app kèm argument, chờ done.flag rồi chép report về.",
                Ios, CATEGORY_E2E, runTimeout + TIMEOUT_IOS_EXTRA);
            yield return new AutoTestCase("firebase-test-lab", "Lệnh Firebase Test Lab",
                "Sinh sẵn lệnh gcloud chạy APK test ở chế độ Game Loop trên Firebase Test Lab (không tự chạy).",
                FirebaseTestLab, CATEGORY_CI, TIMEOUT_FTL);
        }

        #region Case: tooling

        static async Task Tooling(AutoTestContext ctx)
        {
            AndroidDeviceBridge adb;
            using (ctx.Step("Tìm adb"))
            {
                adb = AndroidDeviceBridge.Create(ctx.Config);
                ctx.Session.Set(KEY_ADB, adb);
            }

            if (!adb.IsAvailable)
            {
                ctx.Report(Severity.Blocker, "Device.Tooling", "Không tìm thấy adb — không chạy được test trên máy Android",
                    "Cách sửa (chọn một):\n" +
                    "1. Unity Hub > Installs > bản Unity này > Add modules > tick Android Build Support + Android SDK & NDK Tools.\n" +
                    "2. Cài Android Studio (hoặc SDK Platform-Tools) rồi khai báo ở Unity > Settings > External Tools > Android SDK.\n" +
                    "3. Điền đường dẫn file adb vào Auto Test > Settings > Device > adbPath.",
                    expected: "tìm thấy adb", actual: "không có");
            }
            else
            {
                using (ctx.Step("Kiểm tra adb chạy được"))
                {
                    var (text, platformTools) = await adb.VersionAsync(ctx.Token);
                    if (text == null)
                    {
                        ctx.Report(Severity.Critical, "Device.Tooling", "adb có nhưng không chạy được",
                            $"Đường dẫn: {adb.AdbPath}\nChạy thử `\"{adb.AdbPath}\" version` trong Terminal để xem lỗi; " +
                            "cài lại Android SDK Platform-Tools nếu file hỏng (macOS: có thể cần bỏ chặn trong System Settings > " +
                            "Privacy & Security).", adb.AdbPath);
                    }
                    else
                    {
                        ctx.Log($"adb: {adb.AdbPath} ({adb.Source}) — {text}");
                        if (platformTools > 0) ctx.Metric("Phiên bản platform-tools", platformTools);
                        ctx.Info($"adb {text}", $"{adb.AdbPath}\nNguồn: {adb.Source}", "Device.Tooling");
                    }
                }
            }

            if (!DeviceProcess.IsMac) return;
            using (ctx.Step("Kiểm tra xcrun devicectl (iOS)"))
            {
                var ios = GetIos(ctx);
                var version = await ios.VersionAsync(ctx.Token);
                ctx.Session.Set(KEY_DEVICECTL, version ?? "");
                if (version == null)
                    ctx.Info("Không có xcrun devicectl — bỏ qua iPhone",
                        "Chỉ cần khi test iPhone: cài Xcode 15 trở lên rồi chạy " +
                        "`sudo xcode-select -s /Applications/Xcode.app/Contents/Developer`.", "Device.Tooling");
                else
                    ctx.Log("xcrun devicectl " + version);
            }
        }

        #endregion

        #region Case: devices

        static async Task Devices(AutoTestContext ctx)
        {
            var adb = GetAdb(ctx);
            var ready = new List<AdbDevice>();
            ctx.Session.Set(KEY_ANDROID, ready);
            var listing = new StringBuilder();

            if (adb.IsAvailable)
            {
                List<AdbDevice> all;
                ToolResult raw;
                using (ctx.Step("adb devices -l"))
                {
                    (all, raw) = await adb.ListDevicesAsync(ctx.Token);
                }

                if (!raw.Ok && all.Count == 0)
                    ctx.Report(Severity.Major, "Device.Adb", "Lệnh adb devices lỗi",
                        raw.Describe() + "\nThử: rút cáp, chạy `adb kill-server` rồi chạy lại.");

                foreach (var d in all)
                {
                    if (d.IsReady)
                    {
                        ready.Add(d);
                        continue;
                    }

                    listing.AppendLine($"{d.Serial}\t{d.State}\t(không dùng được)");
                    ctx.Report(Severity.Major, "Device.Connection",
                        $"Máy {d.Model ?? d.Serial} ở trạng thái '{d.State}' — không dùng được", FixForState(d.State),
                        d.Serial, expected: "device", actual: d.State);
                }

                if (ready.Count > 0)
                    using (ctx.Step($"Đọc thông tin {ready.Count} máy Android"))
                    {
                        foreach (var d in ready) await adb.FillDeviceInfoAsync(d, ctx.Token);
                    }

                AssignLabels(ready);
                var minSdk = MinSdk();
                foreach (var d in ready)
                {
                    var line = $"{d.Label}: {d.Manufacturer} {d.Model} · Android {d.AndroidVersion} (SDK {d.Sdk}) · " +
                               $"{d.Abi} · RAM {RamText(d.RamKb)} · serial {d.Serial} · codename {d.DeviceCode}";
                    listing.AppendLine(line);
                    ctx.Info($"[{d.Label}] Android {d.AndroidVersion} (SDK {d.Sdk}) · {d.Abi} · RAM {RamText(d.RamKb)}",
                        line, "Device.Info");
                    if (minSdk > 0 && d.Sdk > 0 && d.Sdk < minSdk)
                        ctx.Report(Severity.Major, "Device.Compatibility",
                            $"[{d.Label}] Android SDK {d.Sdk} thấp hơn Minimum API Level {minSdk} của game",
                            "Không cài được APK lên máy này — dùng máy Android mới hơn, hoặc hạ Minimum API Level " +
                            "(Player Settings > Other Settings).", d.Serial, expected: $"SDK ≥ {minSdk}",
                            actual: $"SDK {d.Sdk}");
                }

                ctx.Metric("Máy Android sẵn sàng", ready.Count, "máy");
            }

            if (IosDeviceBridge.IsSupportedPlatform && !string.IsNullOrEmpty(ctx.Session.Get(KEY_DEVICECTL, "")))
                using (ctx.Step("xcrun devicectl list devices"))
                {
                    var (ios, _) = await GetIos(ctx).ListDevicesAsync(ctx.Token);
                    ctx.Session.Set(KEY_IOS_DEVICES, ios);
                    foreach (var d in ios)
                    {
                        listing.AppendLine("iOS: " + d);
                        if (d.IsAvailable)
                            ctx.Info($"[{d.Label}] iOS {d.OsVersion} (đã pair)", d.ToString(), "Device.Info");
                    }
                }

            if (listing.Length > 0) ctx.Attach("devices.txt", listing.ToString(), "Danh sách device");

            if (!adb.IsAvailable) ctx.Skip("Không có adb (xem case 'Công cụ adb / xcrun').");
            if (ready.Count == 0 && ctx.Result.issues.All(i => i.severity < Severity.Minor))
                ctx.Skip("Không có máy Android nào đang kết nối. Cắm cáp USB, bật Tuỳ chọn nhà phát triển > Gỡ lỗi USB " +
                         "(USB debugging), bấm Cho phép trên máy rồi chạy lại.");
        }

        static string FixForState(string state)
        {
            switch (state)
            {
                case "unauthorized":
                    return "Bật USB debugging và bấm Cho phép trên máy: mở khoá màn hình, bấm 'Cho phép' ở hộp thoại " +
                           "'Cho phép gỡ lỗi USB?' (tick 'Luôn cho phép từ máy tính này'). Không thấy hộp thoại: Tuỳ chọn " +
                           "nhà phát triển > Thu hồi uỷ quyền gỡ lỗi USB, rút cáp cắm lại.";
                case "offline":
                    return "Rút cáp cắm lại (dùng cáp có truyền dữ liệu); vẫn offline thì chạy `adb kill-server` rồi chạy lại, " +
                           "hoặc khởi động lại máy.";
                case "no permissions":
                    return "Linux: thêm udev rule cho máy Android (51-android.rules) rồi chạy `adb kill-server`.";
                default:
                    return $"Máy đang ở chế độ '{state}' — khởi động lại máy về chế độ bình thường và bật USB debugging.";
            }
        }

        #endregion

        #region Case: build

        static async Task Build(AutoTestContext ctx)
        {
            var cfg = ctx.Config.device;
            var custom = cfg.apkPath?.Trim();
            if (!string.IsNullOrEmpty(custom))
            {
                var abs = ProjectPath(custom);
                if (File.Exists(abs))
                {
                    ctx.Session.Set(KEY_APK, abs);
                    ctx.Metric("Dung lượng APK", new FileInfo(abs).Length / BYTES_PER_MB, "MB");
                    ctx.Info("Dùng APK có sẵn trong Settings > Device", abs, "Device.Build");
                    ctx.Log("APK: " + abs);
                    return;
                }

                ctx.Report(Severity.Major, "Device.Build", "APK điền trong Settings > Device không tồn tại",
                    $"{abs}\nSửa đường dẫn, hoặc để trống để Auto Test tự build APK test.", custom);
                if (!cfg.buildBeforeRun) return;
            }

            if (!cfg.buildBeforeRun)
                ctx.Skip("Không có APK: điền Settings > Device > apkPath hoặc bật 'Build trước khi chạy'.");
            if (ReadyDevices(ctx).Count == 0)
                ctx.Skip("Không có máy Android sẵn sàng — bỏ qua build cho đỡ mất thời gian.");

            var reason = DeviceTestBuilder.CheckCanBuild();
            if (reason != null)
            {
                ctx.Report(Severity.Critical, "Device.Build", "Không build được APK test", reason,
                    expected: "Build target = Android", actual: EditorUserBuildSettings.activeBuildTarget.ToString());
                return;
            }

            DeviceBuildResult result;
            using (ctx.Step("Build APK test (Development + EZG_AUTOTEST)", DeviceTestBuilder.DefaultApkPath(ctx.Config)))
            {
                ctx.Session.ReportProgress(ctx.Result, "Đang build APK test — Editor sẽ đứng trong lúc build…");
                // Nhường vài frame cho cửa sổ vẽ trạng thái trước khi Editor đứng vì build đồng bộ.
                await ctx.WaitFrames(2);
                result = DeviceTestBuilder.BuildTestApk(ctx.Config);
            }

            ctx.Metric("Thời gian build", result.Seconds, "s");
            foreach (var note in result.Notes) ctx.Info("Ghi chú build", note, "Device.Build");
            if (!result.Success)
            {
                var detail = result.Message;
                if (result.Errors.Count > 0)
                    detail += "\n\n" + string.Join("\n", result.Errors.Take(MAX_ERRORS_IN_MESSAGE));
                ctx.Report(Severity.Critical, "Device.Build", "Build APK test thất bại", detail,
                    steps: "1. Mở File > Build Profiles, chọn Android\n2. Build thử để xem lỗi đầy đủ trong Console");
                if (result.Errors.Count > 0)
                    ctx.Attach("build_errors.txt", string.Join("\n\n", result.Errors), "Lỗi build");
                return;
            }

            ctx.Session.Set(KEY_APK, result.ApkPath);
            ctx.Metric("Dung lượng APK", result.SizeBytes / BYTES_PER_MB, "MB");
            ctx.Log("APK: " + result.ApkPath);
        }

        #endregion

        #region Case: install

        static async Task Install(AutoTestContext ctx)
        {
            var cfg = ctx.Config.device;
            var apk = CurrentApk(ctx);
            if (string.IsNullOrEmpty(apk)) ctx.Skip("Chưa có APK (xem case 'Build APK test').");
            var devices = ReadyDevices(ctx);
            if (devices.Count == 0) ctx.Skip("Không có máy Android sẵn sàng (xem case 'Device đang kết nối').");

            var adb = GetAdb(ctx);
            var pkg = DeviceTestBuilder.PackageName(ctx.Config);
            var installed = new List<AdbDevice>();
            ctx.Session.Set(KEY_INSTALLED, installed);

            foreach (var d in devices)
            {
                ctx.ThrowIfCancelled();
                ToolResult r;
                using (ctx.Step($"Cài lên {d.Label}", cfg.uninstallBeforeInstall ? "gỡ app cũ trước" : null))
                {
                    ctx.Session.ReportProgress(ctx.Result, $"Đang cài APK lên {d.Label}…");
                    r = await adb.InstallAsync(d.Serial, apk, pkg, cfg.uninstallBeforeInstall, ctx.Token);
                }

                if (!r.Ok)
                {
                    var code = AndroidDeviceBridge.InstallFailureCode(r);
                    ctx.Report(Severity.Critical, "Device.Install",
                        $"[{d.Label}] Cài APK thất bại" + (code != null ? ": " + code : ""),
                        AndroidDeviceBridge.InstallFailureHint(code) + "\n\nadb: " + r.Describe(), apk,
                        expected: "Success", actual: code ?? "lỗi");
                    continue;
                }

                // APK có package khác cấu hình ⇒ các bước sau không mở được app — báo sớm, rõ ràng.
                var path = await adb.ShellAsync(d.Serial, "pm path " + DeviceProcess.ShellQuote(pkg), ctx.Token);
                if (path.StdOut.IndexOf("package:", StringComparison.Ordinal) < 0)
                {
                    ctx.Report(Severity.Critical, "Device.Install",
                        $"[{d.Label}] Cài xong nhưng không thấy package '{pkg}' trên máy",
                        "APK có package name khác với cấu hình. Điền đúng Settings > Device > packageName (hoặc để trống " +
                        "để dùng Package Name của Player Settings > Android).", apk, expected: pkg, actual: "không có");
                    continue;
                }

                installed.Add(d);
                ctx.Metric($"Thời gian cài ({d.Label})", r.Seconds, "s");
            }
        }

        #endregion

        #region Case: cold-start

        static async Task ColdStart(AutoTestContext ctx)
        {
            if (!ctx.Config.device.measureColdStart) ctx.Skip("Đã tắt trong Settings > Device > Đo cold start.");
            var devices = InstalledDevices(ctx);
            if (devices.Count == 0) ctx.Skip("Chưa cài được APK lên máy nào (xem case 'Cài APK lên device').");
            var adb = GetAdb(ctx);
            var pkg = DeviceTestBuilder.PackageName(ctx.Config);

            foreach (var d in devices)
            {
                ctx.ThrowIfCancelled();
                var component = await ResolveComponent(ctx, adb, d, pkg);
                if (component == null) continue;
                var times = new List<int>();
                using (ctx.Step($"Đo cold start trên {d.Label} ({COLD_START_RUNS} lần)"))
                {
                    for (var i = 0; i < COLD_START_RUNS; i++)
                    {
                        ctx.Session.ReportProgress(ctx.Result, $"{d.Label}: cold start lần {i + 1}/{COLD_START_RUNS}");
                        await adb.ForceStopAsync(d.Serial, pkg, ctx.Token);
                        await ctx.WaitSeconds(BEFORE_LAUNCH_SECONDS);
                        var start = await adb.StartWaitAsync(d.Serial, component, null, ctx.Token);
                        if (!start.Ok)
                        {
                            ReportLaunchFailure(ctx, d, component, start);
                            break;
                        }

                        if (start.TotalTimeMs > 0) times.Add(start.TotalTimeMs);
                        await ctx.WaitSeconds(COLD_START_SETTLE_SECONDS);
                        if (await adb.PidOfAsync(d.Serial, pkg, ctx.Token) > 0) continue;

                        await ReportEarlyExit(ctx, adb, d, pkg);
                        break;
                    }

                    await adb.ForceStopAsync(d.Serial, pkg, ctx.Token);
                }

                if (times.Count == 0) continue;
                ctx.Metric($"Cold start ({d.Label})", Median(times), "ms", COLD_START_MAX_MS, null, Severity.Minor);
                ctx.Log($"{d.Label} cold start (ms): {string.Join(", ", times)}");
            }
        }

        /// <summary>App tắt trong vài giây sau khi mở (không qua auto test) → dump logcat tìm dấu vết crash.</summary>
        static async Task ReportEarlyExit(AutoTestContext ctx, AndroidDeviceBridge adb, AdbDevice d, string pkg)
        {
            var dump = await adb.DumpLogcatAsync(d.Serial, DUMP_LINES, ctx.Token);
            var watcher = new LogcatCrashWatcher(pkg);
            foreach (var raw in (dump ?? "").Split('\n'))
                if (LogcatLine.TryParse(raw.TrimEnd('\r'), out var line))
                    watcher.Feed(line);
            var excerpt = watcher.Excerpt(EXCERPT_LINES);
            var issue = ctx.Report(Severity.Blocker, "Device.Crash",
                $"[{d.Label}] App tắt ngay sau khi mở" + (watcher.Crashed ? " — " + watcher.CrashKind : ""),
                (watcher.CrashLine ?? "Không thấy dòng crash rõ ràng trong logcat.") +
                "\nMở game bằng tay trên máy để xác nhận; xem file logcat đính kèm.",
                d.Serial, steps: $"1. Cài APK lên {d.Label}\n2. Mở game từ màn hình chính\n3. Game tắt trong vài giây");
            issue.stackTrace = excerpt;
            if (!string.IsNullOrEmpty(excerpt)) ctx.Attach($"early_exit_{d.SafeSerial}.txt", excerpt, $"Logcat {d.Label}");
        }

        #endregion

        #region Case: run (Android)

        enum RunOutcome
        {
            None,
            Done,
            Crash,
            Died,
            NoStart,
            Timeout,
            Disconnected
        }

        sealed class RunState
        {
            public RunOutcome Outcome;
            public DeviceProtocolEvent StartEvent;
            public DeviceProtocolEvent DoneEvent;
            public int Cases;
            public int Passed;
            public int Failed;
            public string LastCase;
        }

        static async Task Run(AutoTestContext ctx)
        {
            var devices = InstalledDevices(ctx);
            if (devices.Count == 0) ctx.Skip("Không có máy nào đã cài APK test (xem case 'Cài APK lên device').");
            var adb = GetAdb(ctx);
            var pkg = DeviceTestBuilder.PackageName(ctx.Config);
            var runId = RunId(ctx);
            var suites = ctx.Config.device.suites ?? new List<string>();
            var suitesCsv = string.Join(",", suites);

            using (ctx.Step($"Chạy auto test trên {devices.Count} máy (song song)",
                       $"runId {runId} · suite: {(suitesCsv.Length > 0 ? suitesCsv : "tất cả")}"))
            {
                // Mỗi máy một task — mọi continuation chạy trên main thread (AutoTestClock), không cần khoá.
                var jobs = devices.Select(d => RunOnAndroidDevice(ctx, adb, d, pkg, runId, suitesCsv)).ToList();
                await Task.WhenAll(jobs);
            }
        }

        static async Task RunOnAndroidDevice(AutoTestContext ctx, AndroidDeviceBridge adb, AdbDevice d, string pkg,
            string runId, string suitesCsv)
        {
            var cfg = ctx.Config.device;
            StreamingProcess logcat = null;
            string logRel = null;
            string logAbs = null;
            var watcher = new LogcatCrashWatcher(pkg);
            try
            {
                var component = await ResolveComponent(ctx, adb, d, pkg);
                if (component == null) return;

                Progress(ctx, $"{d.Label}: chuẩn bị (tắt app, xoá logcat)…");
                await adb.ForceStopAsync(d.Serial, pkg, ctx.Token);
                await adb.ClearLogcatAsync(d.Serial, ctx.Token);
                if (cfg.collectLogcat)
                {
                    logRel = AttachmentPath(ctx, $"logcat_{d.SafeSerial}.txt");
                    logAbs = Path.Combine(ctx.Session.OutputDir, logRel);
                }

                logcat = adb.StartLogcat(d.Serial, logAbs);
                if (logcat.Error != null)
                    ctx.Warn($"[{d.Label}] Không mở được logcat — không bắt được crash/tiến độ", logcat.Error,
                        "Device.Logcat");

                var extras = new Dictionary<string, string>
                {
                    [DeviceProtocol.EXTRA_RUN] = runId,
                    [DeviceProtocol.EXTRA_SUITES] = suitesCsv
                };
                Progress(ctx, $"{d.Label}: mở game kèm auto test…");
                var start = await adb.StartWaitAsync(d.Serial, component, extras, ctx.Token);
                if (!start.Ok)
                {
                    ReportLaunchFailure(ctx, d, component, start);
                    return;
                }

                var state = new RunState();
                await WaitForRun(ctx, adb, d, pkg, runId, logcat, watcher, state);
                ReportOutcome(ctx, d, state, watcher, suitesCsv);

                if (state.Outcome == RunOutcome.Done || state.Outcome == RunOutcome.Timeout)
                {
                    var pss = await adb.MemInfoPssKbAsync(d.Serial, pkg, ctx.Token);
                    if (pss > 0)
                        ctx.Metric($"RAM PSS cuối lượt ({d.Label})", AndroidDeviceBridge.KbToMb(pss), "MB",
                            ctx.Config.performance.maxTotalMemoryMb, null, Severity.Minor);
                }

                await adb.ForceStopAsync(d.Serial, pkg, ctx.Token);
                Progress(ctx, $"{d.Label}: kéo report về…");
                await PullAndMergeAndroid(ctx, adb, d, pkg, runId, state);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception e)
            {
                var issue = ctx.Report(Severity.Critical, "Device.Runner",
                    $"[{d.Label}] Lỗi khi điều khiển máy: {e.GetType().Name}", e.Message, d.Serial);
                issue.stackTrace = AutoTestExecutor.ShortStack(e.StackTrace);
            }
            finally
            {
                logcat?.Dispose();
                if (logAbs != null && File.Exists(logAbs))
                    ctx.Result.attachments.Add(new TestAttachment
                        { label = $"Logcat {d.Label}", path = logRel, kind = "text" });
                // Bị dừng giữa chừng → tắt game trên máy (không chờ).
                if (ctx.Token.IsCancellationRequested) _ = adb.ForceStopAsync(d.Serial, pkg, CancellationToken.None);
            }
        }

        /// <summary>Đọc logcat tới khi: runner báo xong / crash / app chết / runner không khởi động / hết giờ.</summary>
        static async Task WaitForRun(AutoTestContext ctx, AndroidDeviceBridge adb, AdbDevice d, string pkg,
            string runId, StreamingProcess logcat, LogcatCrashWatcher watcher, RunState state)
        {
            var launchedAt = AutoTestClock.Now;
            var deadline = launchedAt + Math.Max(MIN_RUN_TIMEOUT_SECONDS, ctx.Config.device.runTimeoutSeconds);
            var nextPidCheck = launchedAt + PID_POLL_SECONDS;
            var sawPid = false;
            var crashAt = -1.0;
            var diedAt = -1.0;
            var warnedLogcat = false;

            while (true)
            {
                ctx.ThrowIfCancelled();
                var n = 0;
                while (n++ < MAX_LINES_PER_TICK && logcat.TryDequeue(out var raw))
                {
                    if (!LogcatLine.TryParse(raw, out var line)) continue;
                    watcher.Feed(line);
                    var evt = line.TryParseEvent();
                    // Sự kiện của lượt khác (buffer cũ không xoá được) → bỏ qua.
                    if (evt == null || !string.Equals(evt.run, runId, StringComparison.Ordinal)) continue;
                    HandleEvent(ctx, d, state, evt, line, watcher);
                }

                var now = AutoTestClock.Now;
                if (state.DoneEvent != null)
                {
                    state.Outcome = RunOutcome.Done;
                    return;
                }

                // Crash / chết: đọc thêm vài giây để có đủ stack trace rồi mới dừng.
                if (watcher.Crashed)
                {
                    if (crashAt < 0) crashAt = now;
                    else if (now - crashAt >= CRASH_TAIL_SECONDS)
                    {
                        state.Outcome = RunOutcome.Crash;
                        return;
                    }
                }
                else if (watcher.ProcessDied && diedAt < 0)
                {
                    diedAt = now;
                }

                if (diedAt > 0 && !watcher.Crashed && now - diedAt >= CRASH_TAIL_SECONDS)
                {
                    state.Outcome = RunOutcome.Died;
                    return;
                }

                // logcat bị ngắt (adb restart, lỗi mở) → không còn thấy sự kiện: dựa vào done.flag + pidof.
                var logcatAlive = !logcat.HasExited;
                if (!logcatAlive && !warnedLogcat)
                {
                    warnedLogcat = true;
                    ctx.Warn($"[{d.Label}] logcat bị ngắt giữa chừng — không theo dõi được tiến độ/crash",
                        logcat.Error ?? "Tiến trình adb logcat đã thoát. Kết quả vẫn lấy qua done.flag + report.json.",
                        "Device.Logcat");
                }

                if (now >= nextPidCheck && crashAt < 0 && diedAt < 0)
                {
                    nextPidCheck = now + PID_POLL_SECONDS;
                    var pid = await adb.PidOfAsync(d.Serial, pkg, ctx.Token);
                    if (pid > 0)
                    {
                        sawPid = true;
                        watcher.SetPid(pid);
                        if (!logcatAlive &&
                            await adb.FileExistsAsync(d.Serial, RemoteRunDir(state, pkg, runId) + "/" + DeviceProtocol.DONE_FLAG,
                                ctx.Token))
                        {
                            state.Outcome = RunOutcome.Done;
                            return;
                        }
                    }
                    else if (!await adb.IsConnectedAsync(d.Serial, ctx.Token))
                    {
                        state.Outcome = RunOutcome.Disconnected;
                        return;
                    }
                    else if (sawPid || AutoTestClock.Now - launchedAt > NO_PROCESS_GRACE_SECONDS)
                    {
                        diedAt = AutoTestClock.Now;
                    }
                }

                if (state.StartEvent == null && logcatAlive && crashAt < 0 && diedAt < 0 &&
                    now - launchedAt > START_EVENT_TIMEOUT_SECONDS)
                {
                    state.Outcome = RunOutcome.NoStart;
                    return;
                }

                if (now >= deadline)
                {
                    state.Outcome = RunOutcome.Timeout;
                    return;
                }

                await ctx.WaitSeconds(RUN_POLL_SECONDS);
            }
        }

        static void HandleEvent(AutoTestContext ctx, AdbDevice d, RunState state, DeviceProtocolEvent evt,
            LogcatLine line, LogcatCrashWatcher watcher)
        {
            switch (evt.e)
            {
                case DeviceProtocol.EVENT_START:
                    state.StartEvent = evt;
                    if (line.Pid > 0) watcher.SetPid(line.Pid);
                    Progress(ctx, $"{d.Label}: runner đã chạy — {evt.suites?.Count ?? 0} suite");
                    if (evt.missing != null && evt.missing.Count > 0)
                        ctx.Info($"[{d.Label}] Suite không có / không chạy được trên device: {string.Join(", ", evt.missing)}",
                            "Kiểm tra id suite trong Settings > Device > suites (suite Edit/Device không chạy trên máy).",
                            "Device.Suites");
                    break;
                case DeviceProtocol.EVENT_BEGIN:
                    state.LastCase = $"{evt.suite}/{evt.@case}";
                    Progress(ctx, $"{d.Label}: {evt.suite} › {evt.name ?? evt.@case} …");
                    break;
                case DeviceProtocol.EVENT_CASE:
                    state.Cases++;
                    state.LastCase = $"{evt.suite}/{evt.@case}";
                    var label = evt.status;
                    if (Enum.TryParse(evt.status, out TestStatus status))
                    {
                        label = AutoTestStatusUtil.Label(status);
                        if (status == TestStatus.Passed || status == TestStatus.Warning) state.Passed++;
                        else if (AutoTestStatusUtil.IsBad(status)) state.Failed++;
                    }

                    Progress(ctx, $"{d.Label}: {evt.suite} › {evt.name ?? evt.@case} {label} ({state.Cases} case)");
                    break;
                case DeviceProtocol.EVENT_ERROR:
                    ctx.Report(Severity.Major, "Device.Runner", $"[{d.Label}] Runner trên máy báo lỗi",
                        evt.msg, d.Serial);
                    break;
                case DeviceProtocol.EVENT_DONE:
                    state.DoneEvent = evt;
                    Progress(ctx, $"{d.Label}: xong — {evt.passed}/{evt.total} case đạt");
                    break;
            }
        }

        static void ReportOutcome(AutoTestContext ctx, AdbDevice d, RunState state, LogcatCrashWatcher watcher,
            string suitesCsv)
        {
            var where = state.LastCase ?? "(chưa vào case nào — lúc game đang khởi động)";
            var repro = $"1. Cài APK test lên {d.Label}\n2. Mở game kèm auto test (suite: " +
                        $"{(suitesCsv.Length > 0 ? suitesCsv : "tất cả")})\n3. Sự cố khi đang chạy {where}";
            switch (state.Outcome)
            {
                case RunOutcome.Done:
                    ctx.Log($"{d.Label}: runner xong — {state.Passed}/{state.Cases} case đạt.");
                    break;
                case RunOutcome.Crash:
                {
                    var excerpt = watcher.Excerpt(EXCERPT_LINES);
                    var issue = ctx.Report(Severity.Blocker, "Device.Crash",
                        $"[{d.Label}] Game crash khi chạy test — {watcher.CrashKind}",
                        $"Đang chạy: {where}\nDòng log: {watcher.CrashLine}\nXem file crash_{d.SafeSerial}.txt " +
                        $"({EXCERPT_LINES} dòng logcat quanh chỗ crash) và logcat đầy đủ.", where, steps: repro);
                    issue.stackTrace = excerpt;
                    if (!string.IsNullOrEmpty(excerpt))
                        ctx.Attach($"crash_{d.SafeSerial}.txt", excerpt, $"Crash {d.Label}");
                    break;
                }
                case RunOutcome.Died:
                    ctx.Report(Severity.Blocker, "Device.Crash", $"[{d.Label}] Game bị tắt đột ngột khi chạy test",
                        watcher.LowMemoryKill
                            ? "Hệ điều hành kill game vì thiếu RAM (low memory killer) — kiểm tra RAM/rò rỉ bộ nhớ " +
                              $"(case hiệu năng, metric RAM PSS). Đang chạy: {where}."
                            : "Tiến trình game biến mất mà không có log crash rõ ràng (tự gọi Application.Quit, bị hệ " +
                              $"điều hành kill, hoặc crash native không để lại log). Đang chạy: {where}. Xem logcat đính kèm.",
                        where, steps: repro);
                    break;
                case RunOutcome.NoStart:
                    ctx.Report(Severity.Critical, "Device.Runner", $"[{d.Label}] Auto test không khởi động trên máy",
                        $"Sau {START_EVENT_TIMEOUT_SECONDS:0}s không thấy dòng '{DeviceProtocol.LOG_PREFIX.Trim()}' nào " +
                        "trong logcat. Nguyên nhân hay gặp:\n" +
                        "1. APK không phải bản build test (thiếu define EZG_AUTOTEST) — để trống Settings > Device > apkPath " +
                        "để Auto Test tự build.\n2. Game kẹt ở màn hình đầu / chờ mạng — xem logcat đính kèm.\n" +
                        "3. Máy khoá màn hình — mở khoá, tắt khoá màn hình tự động khi test.", where, steps: repro);
                    break;
                case RunOutcome.Disconnected:
                    ctx.Report(Severity.Major, "Device.Connection", $"[{d.Label}] Mất kết nối với máy giữa chừng",
                        $"adb không còn thấy máy (đã chạy {state.Cases} case, đang chạy: {where}). Kiểm tra cáp USB / " +
                        "chế độ tiết kiệm pin tắt USB debugging, cắm lại rồi chạy lại.", d.Serial, steps: repro);
                    break;
                case RunOutcome.Timeout:
                    ctx.Report(Severity.Major, "Device.Timeout",
                        $"[{d.Label}] Hết {ctx.Config.device.runTimeoutSeconds:0}s mà máy chưa chạy xong",
                        $"Đã xong {state.Cases} case, đang chạy: {where}. Tăng Settings > Device > runTimeoutSeconds hoặc " +
                        "bớt suite. Report phần đã chạy vẫn được lấy về.", where, steps: repro);
                    break;
            }
        }

        /// <summary>Kéo thư mục report của lượt chạy về, gộp vào report Editor (dự phòng run-as nếu pull lỗi).</summary>
        static async Task PullAndMergeAndroid(AutoTestContext ctx, AndroidDeviceBridge adb, AdbDevice d, string pkg,
            string runId, RunState state)
        {
            var outputDir = ctx.Session.OutputDir;
            var remote = RemoteRunDir(state, pkg, runId);

            var rel = DEVICE_FOLDER_PREFIX + d.SafeSerial;
            var pullDir = Path.Combine(outputDir, rel + "_pull");
            ResetDirectory(pullDir);
            var pull = await adb.PullAsync(d.Serial, remote, pullDir, ctx.Token);
            var reportPath = pull.Ok ? DeviceReportMerger.Normalize(pullDir, outputDir, rel) : null;

            if (reportPath == null)
            {
                // persistentDataPath trong /data/user/0 (máy không có bộ nhớ ngoài) → chỉ đọc được qua run-as.
                var cat = await adb.RunAsCatAsync(d.Serial, pkg, remote + "/" + DeviceProtocol.REPORT_FILE, ctx.Token);
                if (cat.Ok && cat.StdOut.TrimStart().StartsWith("{", StringComparison.Ordinal))
                {
                    var dir = Path.Combine(outputDir, rel);
                    Directory.CreateDirectory(dir);
                    reportPath = Path.Combine(dir, DeviceProtocol.REPORT_FILE);
                    File.WriteAllText(reportPath, cat.StdOut);
                    ctx.Warn($"[{d.Label}] Chỉ lấy được report.json (thiếu ảnh chụp/đính kèm của máy)",
                        "adb pull lỗi: " + pull.Describe(), "Device.Report");
                }
            }

            TryDeleteDirectory(pullDir);
            if (reportPath == null)
            {
                ctx.Report(Severity.Major, "Device.Report", $"[{d.Label}] Không lấy được report từ device",
                    $"Thư mục trên máy: {remote}\nadb pull: {pull.Describe()}\nCách sửa: dùng APK Development do Auto " +
                    "Test build, kiểm tra máy còn dung lượng, rút cáp cắm lại rồi chạy lại.", d.Serial);
                return;
            }

            var report = DeviceReportMerger.Load(reportPath, out var error);
            if (report == null)
            {
                ctx.Report(Severity.Major, "Device.Report", $"[{d.Label}] report.json từ device bị hỏng", error,
                    reportPath);
                return;
            }

            var (total, passed, failed) = DeviceReportMerger.Merge(ctx.Session, report, d.Label, rel);
            ctx.Result.attachments.Add(new TestAttachment
            {
                label = $"report.json ({d.Label})",
                path = rel + "/" + DeviceProtocol.REPORT_FILE,
                kind = "json"
            });
            ctx.Metric($"Case đạt ({d.Label})", passed, "case");
            ctx.Metric($"Case lỗi ({d.Label})", failed, "case");
            if (total == 0)
                ctx.Warn($"[{d.Label}] Report từ máy không có case nào",
                    "Kiểm tra Settings > Device > suites (id suite phải là suite Play hỗ trợ device).", "Device.Report");
        }

        /// <summary>Thư mục report trên máy: runner báo trong sự kiện start/done, không có thì đoán theo persistentDataPath.</summary>
        static string RemoteRunDir(RunState state, string pkg, string runId)
        {
            var dir = state.DoneEvent?.dir;
            if (string.IsNullOrEmpty(dir)) dir = state.StartEvent?.dir;
            return string.IsNullOrEmpty(dir)
                ? $"/sdcard/Android/data/{pkg}/files/{DeviceProtocol.OUTPUT_FOLDER}/{runId}"
                : dir;
        }

        static void ReportLaunchFailure(AutoTestContext ctx, AdbDevice d, string component, AdbStartResult start)
        {
            ctx.Report(Severity.Critical, "Device.Launch", $"[{d.Label}] Không mở được game",
                $"{start.Error}\nActivity: {component}\nKiểm tra game đã cài (case 'Cài APK lên device'); game dùng " +
                "Activity riêng thì điền Settings > Device > launchActivity.", d.Serial, expected: "Status: ok",
                actual: start.Error);
        }

        #endregion

        #region Case: ios (thử nghiệm)

        static async Task Ios(AutoTestContext ctx)
        {
            if (!IosDeviceBridge.IsSupportedPlatform)
                ctx.Skip("iPhone chỉ test được từ Editor trên macOS (xcrun devicectl).");
            var appPath = IosDeviceBridge.AppPath?.Trim();
            if (string.IsNullOrEmpty(appPath))
                ctx.Skip($"iOS (thử nghiệm) chưa bật: đặt đường dẫn .app/.ipa bản build test (define EZG_AUTOTEST) vào " +
                         $"EditorPrefs '{IosDeviceBridge.APP_PATH_PREF}'.");
            if (!File.Exists(appPath) && !Directory.Exists(appPath))
            {
                ctx.Report(Severity.Major, "Device.iOS", "Không thấy file .app/.ipa của bản build iOS",
                    $"{appPath}\nSửa EditorPrefs '{IosDeviceBridge.APP_PATH_PREF}' hoặc xoá key để bỏ qua iOS.", appPath);
                return;
            }

            var ios = GetIos(ctx);
            if (await ios.VersionAsync(ctx.Token) == null)
            {
                ctx.Report(Severity.Major, "Device.iOS", "Không có xcrun devicectl",
                    "Cài Xcode 15 trở lên rồi chạy `sudo xcode-select -s /Applications/Xcode.app/Contents/Developer`.");
                return;
            }

            List<IosDevice> devices;
            using (ctx.Step("xcrun devicectl list devices"))
            {
                (devices, _) = await ios.ListDevicesAsync(ctx.Token);
            }

            var ready = devices.Where(x => x.IsAvailable).ToList();
            if (ready.Count == 0)
                ctx.Skip("Không có iPhone/iPad nào đã pair và sẵn sàng (Xcode > Window > Devices and Simulators).");

            var bundleId = IosDeviceBridge.BundleId();
            var runId = RunId(ctx);
            var suitesCsv = string.Join(",", ctx.Config.device.suites ?? new List<string>());
            foreach (var d in ready)
            {
                ctx.ThrowIfCancelled();
                await RunOnIosDevice(ctx, ios, d, appPath, bundleId, runId, suitesCsv);
            }
        }

        static async Task RunOnIosDevice(AutoTestContext ctx, IosDeviceBridge ios, IosDevice d, string appPath,
            string bundleId, string runId, string suitesCsv)
        {
            ToolResult r;
            using (ctx.Step($"[{d.Label}] Cài app"))
            {
                Progress(ctx, $"{d.Label}: đang cài app…");
                r = await ios.InstallAsync(d.Identifier, appPath, ctx.Token);
            }

            if (!r.Ok)
            {
                ctx.Report(Severity.Critical, "Device.iOS", $"[{d.Label}] Cài app iOS thất bại",
                    "Kiểm tra bản build ký bằng provisioning profile có UDID máy này, máy đã bật Developer Mode " +
                    $"(hiện: {d.DeveloperMode ?? "?"}).\n\ndevicectl: {r.Describe()}", appPath);
                return;
            }

            using (ctx.Step($"[{d.Label}] Mở game kèm auto test"))
            {
                r = await ios.LaunchAsync(d.Identifier, bundleId, runId, suitesCsv, ctx.Token);
            }

            if (!r.Ok)
            {
                ctx.Report(Severity.Critical, "Device.iOS", $"[{d.Label}] Không mở được game trên iPhone",
                    "Mở khoá màn hình máy; lần đầu cần tin cậy nhà phát triển ở Cài đặt > Cài đặt chung > Quản lý VPN & " +
                    $"thiết bị.\n\ndevicectl: {r.Describe()}", bundleId);
                return;
            }

            var remoteDir = IosDeviceBridge.RemoteRunFolder(runId);
            var probeDir = Path.Combine(Path.GetTempPath(), "ezg_autotest_probe_" + Guid.NewGuid().ToString("N"));
            var done = false;
            var startedAt = AutoTestClock.Now;
            var deadline = startedAt + Math.Max(MIN_RUN_TIMEOUT_SECONDS, ctx.Config.device.runTimeoutSeconds);
            using (ctx.Step($"[{d.Label}] Chờ máy chạy xong (dò {DeviceProtocol.DONE_FLAG})"))
            {
                while (AutoTestClock.Now < deadline)
                {
                    await ctx.WaitSeconds(IOS_POLL_SECONDS);
                    Progress(ctx, $"{d.Label}: đang chạy… {AutoTestClock.Now - startedAt:0}s");
                    var probe = await ios.CopyFromAsync(d.Identifier, bundleId, remoteDir + "/" + DeviceProtocol.DONE_FLAG,
                        probeDir, ctx.Token);
                    if (!probe.Ok) continue;
                    done = true;
                    break;
                }
            }

            TryDeleteDirectory(probeDir);
            if (!done)
                ctx.Report(Severity.Major, "Device.iOS", $"[{d.Label}] Hết giờ mà không thấy {DeviceProtocol.DONE_FLAG}",
                    "Game có thể đã crash hoặc chạy quá lâu (iOS chưa đọc được log realtime). Xem crash log ở Xcode > " +
                    "Window > Devices and Simulators > View Device Logs. Report phần đã chạy (nếu có) vẫn được lấy về.",
                    bundleId);

            var outputDir = ctx.Session.OutputDir;
            var rel = DEVICE_FOLDER_PREFIX + d.SafeId;
            var pullDir = Path.Combine(outputDir, rel + "_pull");
            ResetDirectory(pullDir);
            var copy = await ios.CopyFromAsync(d.Identifier, bundleId, remoteDir, pullDir, ctx.Token);
            var reportPath = copy.Ok ? DeviceReportMerger.Normalize(pullDir, outputDir, rel) : null;
            TryDeleteDirectory(pullDir);
            if (reportPath == null)
            {
                ctx.Report(Severity.Major, "Device.Report", $"[{d.Label}] Không lấy được report từ iPhone",
                    $"Nguồn: {remoteDir} (container {bundleId})\ndevicectl: {copy.Describe()}", bundleId);
                return;
            }

            var report = DeviceReportMerger.Load(reportPath, out var error);
            if (report == null)
            {
                ctx.Report(Severity.Major, "Device.Report", $"[{d.Label}] report.json từ iPhone bị hỏng", error,
                    reportPath);
                return;
            }

            var (_, passed, failed) = DeviceReportMerger.Merge(ctx.Session, report, d.Label, rel);
            ctx.Result.attachments.Add(new TestAttachment
                { label = $"report.json ({d.Label})", path = rel + "/" + DeviceProtocol.REPORT_FILE, kind = "json" });
            ctx.Metric($"Case đạt ({d.Label})", passed, "case");
            ctx.Metric($"Case lỗi ({d.Label})", failed, "case");
        }

        #endregion

        #region Case: firebase-test-lab

        static Task FirebaseTestLab(AutoTestContext ctx)
        {
            var pkg = DeviceTestBuilder.PackageName(ctx.Config);
            var apk = CurrentApk(ctx);
            if (string.IsNullOrEmpty(apk))
            {
                var fallback = DeviceTestBuilder.DefaultApkPath(ctx.Config);
                apk = File.Exists(fallback) ? fallback : "<đường dẫn APK test>";
            }

            var first = ReadyDevices(ctx).FirstOrDefault();
            var model = !string.IsNullOrEmpty(first?.DeviceCode) ? first.DeviceCode : "redfin";
            var version = first != null && first.Sdk > 0 ? first.Sdk.ToString(CultureInfo.InvariantCulture) : "30";
            var orientation = IsLandscape() ? "landscape" : "portrait";
            var resultsDir = "ezg-autotest-" + DateTime.Now.ToString(RUN_ID_FORMAT, CultureInfo.InvariantCulture);
            var gcloud = FindGcloud();

            var command =
                "gcloud firebase test android run \\\n" +
                "  --type game-loop \\\n" +
                $"  --app \"{apk}\" \\\n" +
                "  --scenario-numbers 1 \\\n" +
                $"  --device model={model},version={version},locale=vi,orientation={orientation} \\\n" +
                "  --timeout 15m \\\n" +
                $"  --results-dir {resultsDir} \\\n" +
                $"  --directories-to-pull /sdcard/Android/data/{pkg}/files/{DeviceProtocol.OUTPUT_FOLDER}";

            var text = new StringBuilder();
            text.AppendLine("# Chạy APK test trên Firebase Test Lab (chế độ Game Loop)");
            text.AppendLine("#");
            text.AppendLine("# APK do Auto Test build đã có sẵn intent-filter com.google.intent.action.TEST_LOOP: Test Lab mở game,");
            text.AppendLine("# runner tự chạy các suite device (Settings > Device > suites), ghi kết quả tóm tắt vào file kết quả");
            text.AppendLine("# của Game Loop rồi tự đóng game. report.json + ảnh chụp nằm trong thư mục kéo về (--directories-to-pull).");
            text.AppendLine("#");
            text.AppendLine("# Chuẩn bị (một lần): cài Google Cloud SDK, chạy `gcloud auth login` và");
            text.AppendLine("#   `gcloud config set project <firebase-project-id>`.");
            text.AppendLine("# Danh sách máy của Test Lab: `gcloud firebase test android models list` (model = mã máy, vd redfin).");
            text.AppendLine($"# model/version bên dưới lấy theo máy đang cắm ({first?.Label ?? "không có — dùng mặc định"}) — đổi nếu Test Lab không có máy đó.");
            text.AppendLine("# Kết quả: Firebase Console > Test Lab > lượt chạy > tab 'Game loop results' / thư mục kết quả trên GCS.");
            text.AppendLine();
            text.AppendLine(command);
            var rel = ctx.Attach("ftl-command.txt", text.ToString(), "Lệnh Firebase Test Lab");

            ctx.Info("Đã sinh lệnh Firebase Test Lab (Game Loop)",
                $"Xem file đính kèm {rel ?? "ftl-command.txt"}. Lệnh KHÔNG tự chạy — copy vào Terminal khi cần.\n\n{command}",
                "Device.FTL");
            if (gcloud != null) ctx.Info("Đã có gcloud trên máy", gcloud, "Device.FTL");
            else
                ctx.Info("Chưa thấy gcloud",
                    "Cài Google Cloud SDK (https://cloud.google.com/sdk/docs/install) để chạy lệnh Test Lab.", "Device.FTL");
            return Task.CompletedTask;
        }

        static string FindGcloud()
        {
            var onPath = DeviceProcess.FindOnPath("gcloud");
            if (onPath != null) return onPath;
            var home = DeviceProcess.HomeDirectory();
            var candidates = DeviceProcess.IsWindows
                ? new[]
                {
                    Path.Combine(Environment.GetEnvironmentVariable("LOCALAPPDATA") ?? "", "Google", "Cloud SDK",
                        "google-cloud-sdk", "bin", "gcloud.cmd"),
                    Path.Combine(Environment.GetEnvironmentVariable("ProgramFiles(x86)") ?? "", "Google", "Cloud SDK",
                        "google-cloud-sdk", "bin", "gcloud.cmd")
                }
                : new[] { Path.Combine(home, "google-cloud-sdk", "bin", "gcloud"), "/opt/homebrew/bin/gcloud" };
            return candidates.FirstOrDefault(File.Exists);
        }

        static bool IsLandscape()
        {
            var o = PlayerSettings.defaultInterfaceOrientation;
            return o == UIOrientation.LandscapeLeft || o == UIOrientation.LandscapeRight;
        }

        #endregion

        #region Helpers

        static AndroidDeviceBridge GetAdb(AutoTestContext ctx)
        {
            var adb = ctx.Session.Get<AndroidDeviceBridge>(KEY_ADB);
            if (adb != null) return adb;
            adb = AndroidDeviceBridge.Create(ctx.Config);
            ctx.Session.Set(KEY_ADB, adb);
            return adb;
        }

        static IosDeviceBridge GetIos(AutoTestContext ctx)
        {
            var ios = ctx.Session.Get<IosDeviceBridge>(KEY_IOS);
            if (ios != null) return ios;
            ios = new IosDeviceBridge();
            ctx.Session.Set(KEY_IOS, ios);
            return ios;
        }

        static List<AdbDevice> ReadyDevices(AutoTestContext ctx)
        {
            return ctx.Session.Get(KEY_ANDROID, new List<AdbDevice>());
        }

        /// <summary>Máy đã cài APK ở case install; case install không chạy (bị tắt) ⇒ coi mọi máy sẵn sàng là đã cài tay.</summary>
        static List<AdbDevice> InstalledDevices(AutoTestContext ctx)
        {
            return ctx.Session.Has(KEY_INSTALLED)
                ? ctx.Session.Get(KEY_INSTALLED, new List<AdbDevice>())
                : ReadyDevices(ctx);
        }

        /// <summary>APK của lượt chạy: kết quả case build, hoặc apkPath trong Settings nếu case build không chạy.</summary>
        static string CurrentApk(AutoTestContext ctx)
        {
            var apk = ctx.Session.Get<string>(KEY_APK);
            if (!string.IsNullOrEmpty(apk) && File.Exists(apk)) return apk;
            var custom = ctx.Config.device.apkPath?.Trim();
            if (string.IsNullOrEmpty(custom)) return null;
            var abs = ProjectPath(custom);
            return File.Exists(abs) ? abs : null;
        }

        static string RunId(AutoTestContext ctx)
        {
            var id = ctx.Session.Get<string>(KEY_RUN_ID);
            if (!string.IsNullOrEmpty(id)) return id;
            id = DateTime.Now.ToString(RUN_ID_FORMAT, CultureInfo.InvariantCulture);
            ctx.Session.Set(KEY_RUN_ID, id);
            return id;
        }

        static async Task<string> ResolveComponent(AutoTestContext ctx, AndroidDeviceBridge adb, AdbDevice d,
            string pkg)
        {
            var key = KEY_COMPONENT + d.Serial;
            var cached = ctx.Session.Get<string>(key);
            if (!string.IsNullOrEmpty(cached)) return cached;

            var custom = ctx.Config.device.launchActivity?.Trim();
            string component;
            if (!string.IsNullOrEmpty(custom))
                component = custom.Contains("/") ? custom : pkg + "/" + custom;
            else
                component = await adb.ResolveLaunchActivityAsync(d.Serial, pkg, ctx.Token);

            if (string.IsNullOrEmpty(component))
            {
                ctx.Report(Severity.Critical, "Device.Launch", $"[{d.Label}] Không tìm được Activity khởi động của {pkg}",
                    "Game chưa được cài hoặc package name sai (xem case 'Cài APK lên device'). Game dùng Activity riêng " +
                    "thì điền Settings > Device > launchActivity (vd com.unity3d.player.UnityPlayerGameActivity).",
                    d.Serial);
                return null;
            }

            ctx.Session.Set(key, component);
            return component;
        }

        /// <summary>Nhãn duy nhất: model; trùng model thì thêm 4 ký tự cuối của serial.</summary>
        static void AssignLabels(List<AdbDevice> devices)
        {
            foreach (var d in devices)
            {
                var model = string.IsNullOrEmpty(d.Model) ? d.Serial : d.Model;
                var duplicate = devices.Count(x => (string.IsNullOrEmpty(x.Model) ? x.Serial : x.Model) == model) > 1;
                var suffix = d.Serial.Length > SERIAL_SUFFIX ? d.Serial.Substring(d.Serial.Length - SERIAL_SUFFIX) : d.Serial;
                d.Label = duplicate ? $"{model} #{suffix}" : model;
            }
        }

        /// <summary>Đường dẫn tương đối cho file đính kèm lớn ghi trực tiếp ra đĩa (giữ đuôi file).</summary>
        static string AttachmentPath(AutoTestContext ctx, string fileName)
        {
            var ext = Path.GetExtension(fileName);
            var name = Path.GetFileNameWithoutExtension(fileName);
            var rel = ("attachments/" + AutoTestContext.Sanitize(ctx.Result.caseId) + "_" +
                       AutoTestContext.Sanitize(name) + ext).Replace('\\', '/');
            Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(ctx.Session.OutputDir, rel))!);
            return rel;
        }

        static void Progress(AutoTestContext ctx, string message)
        {
            ctx.Session.ReportProgress(ctx.Result, message);
        }

        static int MinSdk()
        {
            try
            {
                return (int)PlayerSettings.Android.minSdkVersion;
            }
            catch
            {
                return 0;
            }
        }

        static string ProjectPath(string path)
        {
            if (Path.IsPathRooted(path)) return path;
            return Path.Combine(Path.GetDirectoryName(UnityEngine.Application.dataPath)!, path).Replace('\\', '/');
        }

        static double Median(List<int> values)
        {
            var sorted = values.OrderBy(v => v).ToList();
            var mid = sorted.Count / 2;
            return sorted.Count % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2.0;
        }

        static string RamText(long kb)
        {
            if (kb <= 0) return "?";
            return (kb / (1024.0 * 1024.0)).ToString("0.0", CultureInfo.InvariantCulture) + " GB";
        }

        static void ResetDirectory(string dir)
        {
            TryDeleteDirectory(dir);
            Directory.CreateDirectory(dir);
        }

        static void TryDeleteDirectory(string dir)
        {
            try
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
            catch (Exception e)
            {
                AutoTestLog.Warn($"Không xoá được thư mục tạm {dir}: {e.Message}");
            }
        }

        #endregion
    }
}
