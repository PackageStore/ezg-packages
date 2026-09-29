using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;

namespace Ezg.AutoTest.Editor
{
    /// <summary>Một device Android thấy qua <c>adb devices -l</c> (+ thông tin đọc thêm bằng getprop).</summary>
    public sealed class AdbDevice
    {
        public string Serial;

        /// <summary>device / unauthorized / offline / no permissions / recovery / sideload…</summary>
        public string State;

        public string Model;
        public string Product;
        public string DeviceCode;
        public string TransportId;

        // Đọc thêm khi device sẵn sàng.
        public string Manufacturer;
        public string AndroidVersion;
        public int Sdk;
        public string Abi;
        public long RamKb;

        /// <summary>Nhãn hiển thị duy nhất trong lượt chạy (model, thêm hậu tố serial nếu trùng model).</summary>
        public string Label;

        public bool IsReady => State == "device";

        /// <summary>Serial an toàn để làm tên file/thư mục (serial Wi-Fi có dạng ip:port).</summary>
        public string SafeSerial => AutoTestContext.Sanitize(Serial);

        public string DisplayName => string.IsNullOrEmpty(Label) ? Model ?? Serial : Label;

        public override string ToString()
        {
            return $"{DisplayName} ({Serial}, {State})";
        }
    }

    /// <summary>Kết quả <c>am start -W</c>.</summary>
    public sealed class AdbStartResult
    {
        public bool Ok;
        public int TotalTimeMs = -1;
        public int WaitTimeMs = -1;
        public string LaunchState;
        public string Output;
        public string Error;
    }

    /// <summary>
    ///     Cầu nối adb cho Editor: tìm adb, liệt kê device, cài APK, mở app kèm intent extra, đọc logcat dạng
    ///     stream, kéo file, đo RAM. Mọi lệnh chạy bất đồng bộ qua <see cref="DeviceProcess" /> (không chặn main
    ///     thread, huỷ ⇒ kill tiến trình).
    /// </summary>
    public sealed class AndroidDeviceBridge
    {
        const string ANDROID_TOOLS_SETTINGS_TYPE = "UnityEditor.Android.AndroidExternalToolsSettings";
        const double DEFAULT_TIMEOUT = 30;
        const double DEVICES_TIMEOUT = 45;
        const double INSTALL_TIMEOUT = 600;
        const double START_TIMEOUT = 90;
        const double PULL_TIMEOUT = 300;
        const long KB_PER_MB = 1024;

        static readonly Regex TOTAL_TIME = new(@"^\s*TotalTime:\s*(\d+)", RegexOptions.Multiline);
        static readonly Regex WAIT_TIME = new(@"^\s*WaitTime:\s*(\d+)", RegexOptions.Multiline);
        static readonly Regex LAUNCH_STATE = new(@"^\s*LaunchState:\s*(\S+)", RegexOptions.Multiline);
        static readonly Regex PSS_NEW = new(@"TOTAL PSS:\s*(\d+)", RegexOptions.IgnoreCase);
        static readonly Regex PSS_OLD = new(@"^\s*TOTAL\s+(\d+)", RegexOptions.Multiline);
        static readonly Regex MEM_TOTAL = new(@"MemTotal:\s*(\d+)\s*kB", RegexOptions.IgnoreCase);
        static readonly Regex INSTALL_FAILURE = new(@"(INSTALL_[A-Z_]+|DELETE_FAILED_[A-Z_]+)");
        static readonly Regex PLATFORM_TOOLS_VERSION = new(@"Version\s+(\d+)\.(\d+)(?:\.(\d+))?");
        static readonly Regex ADB_VERSION = new(@"Android Debug Bridge version\s+(\S+)");

        public string AdbPath { get; }

        /// <summary>adb tìm được từ đâu (Settings / Unity SDK / ANDROID_HOME / PATH…).</summary>
        public string Source { get; }

        public bool IsAvailable => !string.IsNullOrEmpty(AdbPath);

        AndroidDeviceBridge(string adbPath, string source)
        {
            AdbPath = adbPath;
            Source = source;
        }

        /// <summary>Tạo bridge; <see cref="IsAvailable" /> = false nếu không tìm thấy adb.</summary>
        public static AndroidDeviceBridge Create(AutoTestConfig config)
        {
            var path = FindAdb(config, out var source);
            return new AndroidDeviceBridge(path, source);
        }

        #region Tìm adb

        /// <summary>
        ///     Thứ tự: Settings &gt; Device &gt; adbPath → SDK Unity đang dùng (Preferences &gt; External Tools) →
        ///     SDK nhúng theo Unity Hub → ANDROID_HOME / ANDROID_SDK_ROOT → vị trí mặc định của Android Studio → PATH.
        /// </summary>
        public static string FindAdb(AutoTestConfig config, out string source)
        {
            var adbName = DeviceProcess.Exe("adb");
            var custom = config?.device?.adbPath;
            if (!string.IsNullOrWhiteSpace(custom))
            {
                custom = custom.Trim();
                if (Directory.Exists(custom)) custom = Path.Combine(custom, adbName);
                if (File.Exists(custom))
                {
                    source = "Settings > Device > adbPath";
                    return custom;
                }
            }

            var unitySdk = UnitySdkRoot();
            if (TryAdbInSdk(unitySdk, adbName, out var found))
            {
                source = "Unity Preferences > External Tools (Android SDK)";
                return found;
            }

            var embedded = EmbeddedSdkRoot();
            if (TryAdbInSdk(embedded, adbName, out found))
            {
                source = "Android SDK cài kèm Unity (Unity Hub)";
                return found;
            }

            foreach (var env in new[] { "ANDROID_HOME", "ANDROID_SDK_ROOT" })
                if (TryAdbInSdk(Environment.GetEnvironmentVariable(env), adbName, out found))
                {
                    source = "biến môi trường " + env;
                    return found;
                }

            var home = DeviceProcess.HomeDirectory();
            string defaultSdk;
            if (DeviceProcess.IsWindows)
                defaultSdk = Path.Combine(Environment.GetEnvironmentVariable("LOCALAPPDATA") ?? "", "Android", "Sdk");
            else if (DeviceProcess.IsMac)
                defaultSdk = Path.Combine(home, "Library", "Android", "sdk");
            else
                defaultSdk = Path.Combine(home, "Android", "Sdk");
            if (TryAdbInSdk(defaultSdk, adbName, out found))
            {
                source = "SDK mặc định của Android Studio";
                return found;
            }

            var onPath = DeviceProcess.FindOnPath("adb");
            if (!string.IsNullOrEmpty(onPath))
            {
                source = "PATH";
                return onPath;
            }

            source = null;
            return null;
        }

        static bool TryAdbInSdk(string sdkRoot, string adbName, out string adb)
        {
            adb = null;
            if (string.IsNullOrWhiteSpace(sdkRoot)) return false;
            try
            {
                var candidate = Path.Combine(sdkRoot.Trim(), "platform-tools", adbName);
                if (!File.Exists(candidate)) return false;
                adb = candidate;
                return true;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        /// <summary>
        ///     SDK Unity đang dùng — đọc <c>AndroidExternalToolsSettings.sdkRootPath</c> bằng reflection (type nằm trong
        ///     UnityEditor.Android.Extensions, chỉ có khi cài Android module → không tham chiếu lúc compile).
        /// </summary>
        public static string UnitySdkRoot()
        {
            try
            {
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    var type = asm.GetType(ANDROID_TOOLS_SETTINGS_TYPE, false);
                    if (type == null) continue;
                    var prop = type.GetProperty("sdkRootPath", BindingFlags.Public | BindingFlags.Static);
                    return prop?.GetValue(null) as string;
                }
            }
            catch (Exception e)
            {
                AutoTestLog.Warn("Không đọc được Android SDK của Unity: " + e.Message);
            }

            return null;
        }

        static string EmbeddedSdkRoot()
        {
            try
            {
                var dir = BuildPipeline.GetPlaybackEngineDirectory(BuildTarget.Android, BuildOptions.None);
                return string.IsNullOrEmpty(dir) ? null : Path.Combine(dir, "SDK");
            }
            catch
            {
                // Chưa cài Android module.
                return null;
            }
        }

        #endregion

        #region Lệnh cơ bản

        /// <summary>Chạy adb với argument tuỳ ý.</summary>
        public Task<ToolResult> RunAsync(IEnumerable<string> args, double timeoutSeconds, CancellationToken token)
        {
            if (!IsAvailable)
                return Task.FromResult(new ToolResult { StartFailed = true, Error = "Không tìm thấy adb" });
            return DeviceProcess.RunAsync(AdbPath, args, timeoutSeconds, token);
        }

        /// <summary>Chạy adb nhắm một device (<c>adb -s serial …</c>).</summary>
        public Task<ToolResult> RunOnDeviceAsync(string serial, IEnumerable<string> args, double timeoutSeconds,
            CancellationToken token)
        {
            var list = new List<string> { "-s", serial };
            list.AddRange(args);
            return RunAsync(list, timeoutSeconds, token);
        }

        /// <summary><c>adb -s serial shell &lt;command&gt;</c> — command là MỘT chuỗi, shell của device tự tách.</summary>
        public Task<ToolResult> ShellAsync(string serial, string command, CancellationToken token,
            double timeoutSeconds = DEFAULT_TIMEOUT)
        {
            return RunOnDeviceAsync(serial, new[] { "shell", command }, timeoutSeconds, token);
        }

        /// <summary>Phiên bản adb + platform-tools (vd "1.0.41 / 37.0.0"), null nếu lỗi.</summary>
        public async Task<(string text, double platformTools)> VersionAsync(CancellationToken token)
        {
            var r = await RunAsync(new[] { "version" }, DEFAULT_TIMEOUT, token);
            if (!r.Ok) return (null, 0);
            var adb = ADB_VERSION.Match(r.StdOut);
            var pt = PLATFORM_TOOLS_VERSION.Match(r.StdOut);
            var ptValue = 0.0;
            if (pt.Success)
                ptValue = int.Parse(pt.Groups[1].Value, CultureInfo.InvariantCulture) +
                          int.Parse(pt.Groups[2].Value, CultureInfo.InvariantCulture) / 100.0;
            var text = (adb.Success ? adb.Groups[1].Value : "?") +
                       (pt.Success ? " / platform-tools " + pt.Value.Replace("Version", "").Trim() : "");
            return (text, ptValue);
        }

        /// <summary><c>adb devices -l</c> → mọi device (kể cả unauthorized/offline).</summary>
        public async Task<(List<AdbDevice> devices, ToolResult raw)> ListDevicesAsync(CancellationToken token)
        {
            var r = await RunAsync(new[] { "devices", "-l" }, DEVICES_TIMEOUT, token);
            return (ParseDevices(r.StdOut), r);
        }

        public static List<AdbDevice> ParseDevices(string output)
        {
            var list = new List<AdbDevice>();
            if (string.IsNullOrEmpty(output)) return list;
            foreach (var raw in output.Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("List of devices", StringComparison.Ordinal) ||
                    line.StartsWith("*", StringComparison.Ordinal) || line.StartsWith("adb ", StringComparison.Ordinal))
                    continue;
                var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2) continue;
                var dev = new AdbDevice { Serial = parts[0], State = parts[1] };
                if (line.Contains("no permissions")) dev.State = "no permissions";
                foreach (var p in parts.Skip(2))
                {
                    var idx = p.IndexOf(':');
                    if (idx <= 0) continue;
                    var key = p.Substring(0, idx);
                    var value = p.Substring(idx + 1);
                    switch (key)
                    {
                        case "model": dev.Model = value.Replace('_', ' '); break;
                        case "product": dev.Product = value; break;
                        case "device": dev.DeviceCode = value; break;
                        case "transport_id": dev.TransportId = value; break;
                    }
                }

                list.Add(dev);
            }

            return list;
        }

        /// <summary><c>getprop key</c> (chuỗi rỗng nếu lỗi).</summary>
        public async Task<string> GetPropAsync(string serial, string key, CancellationToken token)
        {
            var r = await ShellAsync(serial, "getprop " + DeviceProcess.ShellQuote(key), token);
            return r.Ok ? r.StdOut.Trim() : "";
        }

        /// <summary>Đọc model / hãng / Android / SDK / ABI / RAM vào <paramref name="dev" />.</summary>
        public async Task FillDeviceInfoAsync(AdbDevice dev, CancellationToken token)
        {
            var model = await GetPropAsync(dev.Serial, "ro.product.model", token);
            if (!string.IsNullOrEmpty(model)) dev.Model = model;
            dev.Manufacturer = await GetPropAsync(dev.Serial, "ro.product.manufacturer", token);
            dev.AndroidVersion = await GetPropAsync(dev.Serial, "ro.build.version.release", token);
            int.TryParse(await GetPropAsync(dev.Serial, "ro.build.version.sdk", token), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out dev.Sdk);
            dev.Abi = await GetPropAsync(dev.Serial, "ro.product.cpu.abi", token);
            var code = await GetPropAsync(dev.Serial, "ro.product.device", token);
            if (!string.IsNullOrEmpty(code)) dev.DeviceCode = code;
            dev.RamKb = await TotalRamKbAsync(dev.Serial, token);
            if (string.IsNullOrEmpty(dev.Model)) dev.Model = dev.Serial;
        }

        /// <summary>RAM tổng của device (KB) từ /proc/meminfo, 0 nếu lỗi.</summary>
        public async Task<long> TotalRamKbAsync(string serial, CancellationToken token)
        {
            var r = await ShellAsync(serial, "cat /proc/meminfo", token);
            var m = MEM_TOTAL.Match(r.StdOut ?? "");
            return m.Success ? long.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : 0;
        }

        #endregion

        #region Cài / mở / dừng app

        /// <summary>
        ///     Cài APK (<c>install -r -t -g -d</c>: ghi đè, cho phép bản test, cấp sẵn quyền runtime, cho phép hạ version).
        ///     <paramref name="uninstallFirst" /> = xoá app cũ trước (MẤT dữ liệu người chơi trên máy đó).
        /// </summary>
        public async Task<ToolResult> InstallAsync(string serial, string apkPath, string packageName,
            bool uninstallFirst, CancellationToken token)
        {
            if (uninstallFirst && !string.IsNullOrEmpty(packageName)) await UninstallAsync(serial, packageName, token);
            var r = await RunOnDeviceAsync(serial, new[] { "install", "-r", "-t", "-g", "-d", apkPath },
                INSTALL_TIMEOUT, token);
            // adb cũ trả exit 0 kể cả khi cài lỗi → kiểm tra thêm chữ "Failure".
            if (r.ExitCode == 0 && r.Combined.IndexOf("Failure", StringComparison.OrdinalIgnoreCase) >= 0)
                r.ExitCode = 1;
            return r;
        }

        public Task<ToolResult> UninstallAsync(string serial, string packageName, CancellationToken token)
        {
            return RunOnDeviceAsync(serial, new[] { "uninstall", packageName }, DEFAULT_TIMEOUT * 2, token);
        }

        /// <summary>Mã lỗi cài đặt (vd INSTALL_FAILED_UPDATE_INCOMPATIBLE) trong output, null nếu không có.</summary>
        public static string InstallFailureCode(ToolResult r)
        {
            var m = INSTALL_FAILURE.Match(r?.Combined ?? "");
            return m.Success ? m.Groups[1].Value : null;
        }

        /// <summary>Hướng dẫn sửa theo mã lỗi cài đặt phổ biến (tiếng Việt, cho QA).</summary>
        public static string InstallFailureHint(string code)
        {
            switch (code)
            {
                case "INSTALL_FAILED_UPDATE_INCOMPATIBLE":
                    return "Máy đang có bản app ký bằng keystore khác (vd bản store). Gỡ app trên máy, hoặc bật " +
                           "Settings > Device > Gỡ app trước khi cài (sẽ mất dữ liệu chơi trên máy đó).";
                case "INSTALL_FAILED_VERSION_DOWNGRADE":
                    return "Máy đang có bản versionCode cao hơn. Gỡ app trên máy hoặc bật 'Gỡ app trước khi cài'.";
                case "INSTALL_FAILED_INSUFFICIENT_STORAGE":
                    return "Máy hết dung lượng — xoá bớt app/ảnh rồi chạy lại.";
                case "INSTALL_FAILED_NO_MATCHING_ABIS":
                    return "APK không có kiến trúc CPU của máy — bật ARM64 (và ARMv7 nếu cần) trong Player Settings > " +
                           "Other Settings > Target Architectures.";
                case "INSTALL_FAILED_OLDER_SDK":
                    return "Android của máy thấp hơn Minimum API Level của game.";
                case "INSTALL_FAILED_USER_RESTRICTED":
                    return "Máy chặn cài qua USB (hay gặp ở Xiaomi/Oppo/Vivo): bật 'Install via USB' / 'Cài đặt qua USB' " +
                           "trong Tuỳ chọn nhà phát triển, rồi bấm Cho phép trên máy khi được hỏi.";
                case "INSTALL_FAILED_ABORTED":
                    return "Người dùng/máy huỷ cài đặt — mở khoá màn hình và bấm Cho phép trên máy.";
                case "INSTALL_PARSE_FAILED_NO_CERTIFICATES":
                case "INSTALL_PARSE_FAILED_INCONSISTENT_CERTIFICATES":
                    return "APK chưa ký / ký lỗi — build lại APK test.";
                case "INSTALL_FAILED_TEST_ONLY":
                    return "APK đánh dấu testOnly — adb đã dùng cờ -t, thử cập nhật platform-tools.";
                default:
                    return "Xem thông báo lỗi của adb bên dưới; thử rút cáp cắm lại và chạy lại.";
            }
        }

        /// <summary>
        ///     Activity khởi động của package (<c>cmd package resolve-activity</c>, Android 7+). Trả "pkg/activity" hoặc
        ///     null nếu không resolve được.
        /// </summary>
        public async Task<string> ResolveLaunchActivityAsync(string serial, string packageName, CancellationToken token)
        {
            var r = await ShellAsync(serial,
                "cmd package resolve-activity --brief -c android.intent.category.LAUNCHER " +
                DeviceProcess.ShellQuote(packageName), token);
            if (!r.Ok) return null;
            var lines = r.StdOut.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
            if (lines.Count == 0) return null;
            var last = lines[lines.Count - 1];
            return last.Contains("/") && !last.StartsWith("No activity", StringComparison.OrdinalIgnoreCase)
                ? last
                : null;
        }

        public Task<ToolResult> ForceStopAsync(string serial, string packageName, CancellationToken token)
        {
            return ShellAsync(serial, "am force-stop " + DeviceProcess.ShellQuote(packageName), token);
        }

        /// <summary><c>am start -W -n &lt;component&gt; --es k v …</c> — chờ Activity vẽ xong, trả TotalTime/WaitTime.</summary>
        public async Task<AdbStartResult> StartWaitAsync(string serial, string component,
            IDictionary<string, string> extras, CancellationToken token)
        {
            var cmd = "am start -W -n " + DeviceProcess.ShellQuote(component);
            if (extras != null)
                foreach (var pair in extras)
                    cmd += " --es " + DeviceProcess.ShellQuote(pair.Key) + " " + DeviceProcess.ShellQuote(pair.Value);
            var r = await ShellAsync(serial, cmd, token, START_TIMEOUT);
            var result = new AdbStartResult { Output = r.Combined };
            var t = TOTAL_TIME.Match(r.StdOut);
            if (t.Success) result.TotalTimeMs = int.Parse(t.Groups[1].Value, CultureInfo.InvariantCulture);
            var w = WAIT_TIME.Match(r.StdOut);
            if (w.Success) result.WaitTimeMs = int.Parse(w.Groups[1].Value, CultureInfo.InvariantCulture);
            var ls = LAUNCH_STATE.Match(r.StdOut);
            if (ls.Success) result.LaunchState = ls.Groups[1].Value;

            var errorLine = r.Combined.Split('\n').FirstOrDefault(l =>
                l.TrimStart().StartsWith("Error", StringComparison.OrdinalIgnoreCase));
            if (!r.Ok || errorLine != null)
                result.Error = errorLine?.Trim() ?? r.Describe();
            result.Ok = result.Error == null;
            return result;
        }

        /// <summary>PID đang chạy của package, -1 nếu không chạy.</summary>
        public async Task<int> PidOfAsync(string serial, string packageName, CancellationToken token)
        {
            var r = await ShellAsync(serial, "pidof " + DeviceProcess.ShellQuote(packageName), token);
            var text = r.StdOut.Trim();
            if (text.Length > 0)
            {
                var first = text.Split(' ', '\n')[0];
                if (int.TryParse(first, NumberStyles.Integer, CultureInfo.InvariantCulture, out var pid)) return pid;
            }

            // Máy không có pidof (hiếm) → dò bằng ps.
            if (r.Combined.IndexOf("not found", StringComparison.OrdinalIgnoreCase) < 0) return -1;
            var ps = await ShellAsync(serial, "ps -A", token);
            foreach (var line in ps.StdOut.Split('\n'))
            {
                var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2 || parts[parts.Length - 1] != packageName) continue;
                if (int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var pid)) return pid;
            }

            return -1;
        }

        /// <summary>Device còn kết nối không (<c>adb get-state</c> = "device").</summary>
        public async Task<bool> IsConnectedAsync(string serial, CancellationToken token)
        {
            var r = await RunOnDeviceAsync(serial, new[] { "get-state" }, DEFAULT_TIMEOUT, token);
            return r.Ok && r.StdOut.Trim() == "device";
        }

        /// <summary>File trên device có tồn tại không (<c>test -f</c>).</summary>
        public async Task<bool> FileExistsAsync(string serial, string remotePath, CancellationToken token)
        {
            var r = await ShellAsync(serial, "test -f " + DeviceProcess.ShellQuote(remotePath) + " && echo EZG_YES",
                token);
            return r.StdOut.Contains("EZG_YES");
        }

        /// <summary>Tổng PSS của app (KB) qua <c>dumpsys meminfo</c>, -1 nếu lỗi / app không chạy.</summary>
        public async Task<long> MemInfoPssKbAsync(string serial, string packageName, CancellationToken token)
        {
            var r = await ShellAsync(serial, "dumpsys meminfo " + DeviceProcess.ShellQuote(packageName), token, 60);
            if (!r.Ok) return -1;
            var m = PSS_NEW.Match(r.StdOut);
            if (!m.Success) m = PSS_OLD.Match(r.StdOut);
            return m.Success ? long.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : -1;
        }

        public static double KbToMb(long kb)
        {
            return kb / (double)KB_PER_MB;
        }

        #endregion

        #region Logcat / file

        /// <summary>Xoá buffer logcat (một số máy từ chối xoá — bỏ qua lỗi).</summary>
        public Task<ToolResult> ClearLogcatAsync(string serial, CancellationToken token)
        {
            return RunOnDeviceAsync(serial, new[] { "logcat", "-c" }, DEFAULT_TIMEOUT, token);
        }

        /// <summary>
        ///     Mở stream <c>adb -s X logcat -v threadtime -T 1</c> (chỉ log MỚI từ lúc gọi). Dòng log được đẩy vào hàng
        ///     đợi (lấy bằng TryDequeue trên main thread) và ghi ra <paramref name="logFilePath" /> nếu có.
        /// </summary>
        public StreamingProcess StartLogcat(string serial, string logFilePath = null)
        {
            return StreamingProcess.Start(AdbPath, new[] { "-s", serial, "logcat", "-v", "threadtime", "-T", "1" },
                logFilePath);
        }

        /// <summary>Dump nhanh <paramref name="lines" /> dòng logcat cuối (không stream).</summary>
        public async Task<string> DumpLogcatAsync(string serial, int lines, CancellationToken token)
        {
            var r = await RunOnDeviceAsync(serial,
                new[] { "logcat", "-d", "-v", "threadtime", "-t", lines.ToString(CultureInfo.InvariantCulture) },
                DEFAULT_TIMEOUT, token);
            return r.StdOut;
        }

        /// <summary><c>adb pull remote local</c>.</summary>
        public Task<ToolResult> PullAsync(string serial, string remote, string local, CancellationToken token)
        {
            return RunOnDeviceAsync(serial, new[] { "pull", remote, local }, PULL_TIMEOUT, token);
        }

        /// <summary>
        ///     Đọc một file text trong thư mục riêng của app bằng <c>run-as</c> (chỉ build debuggable) — dự phòng khi
        ///     persistentDataPath nằm trong /data/user/0 và adb pull không đọc được.
        /// </summary>
        public Task<ToolResult> RunAsCatAsync(string serial, string packageName, string remotePath,
            CancellationToken token)
        {
            return RunOnDeviceAsync(serial,
                new[] { "exec-out", "run-as " + DeviceProcess.ShellQuote(packageName) + " cat " + DeviceProcess.ShellQuote(remotePath) },
                DEFAULT_TIMEOUT * 2, token);
        }

        #endregion
    }
}
