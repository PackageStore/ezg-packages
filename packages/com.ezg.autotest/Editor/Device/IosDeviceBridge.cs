using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace Ezg.AutoTest.Editor
{
    /// <summary>Một iPhone/iPad thấy qua <c>xcrun devicectl list devices</c>.</summary>
    public sealed class IosDevice
    {
        /// <summary>CoreDevice identifier (UUID) — dùng cho --device.</summary>
        public string Identifier;

        public string Udid;
        public string Name;
        public string Model;
        public string ProductType;
        public string OsVersion;
        public string Platform;
        public string PairingState;
        public string TunnelState;
        public string DeveloperMode;
        public bool Physical;

        /// <summary>Dùng được để cài/chạy app (đã pair, là máy thật iOS, không "unavailable").</summary>
        public bool IsAvailable => Physical && PairingState == "paired" && TunnelState != "unavailable" &&
                                   string.Equals(Platform, "iOS", StringComparison.OrdinalIgnoreCase);

        public string Label => string.IsNullOrEmpty(Model) ? Name : Model;

        public string SafeId => AutoTestContext.Sanitize(string.IsNullOrEmpty(Udid) ? Identifier : Udid);

        public override string ToString()
        {
            return $"{Name} ({Model}, iOS {OsVersion}, {PairingState}/{TunnelState})";
        }
    }

    /// <summary>
    ///     THỬ NGHIỆM — cầu nối iOS qua <c>xcrun devicectl</c> (Xcode 15+, chỉ macOS): liệt kê máy, cài .app/.ipa, mở
    ///     app kèm argument + biến môi trường kích hoạt auto test, chép thư mục report từ container của app về.
    ///     <para>
    ///         Không build iOS tự động (cần export Xcode + ký). Cách dùng: build iOS có define <c>EZG_AUTOTEST</c>
    ///         (Player Settings &gt; Scripting Define Symbols, hoặc gọi <see cref="DeviceTestBuilder.WriteBakedRequest" />
    ///         trước khi build để bake cấu hình), archive/ký bằng Xcode, rồi đặt đường dẫn .app/.ipa vào EditorPrefs
    ///         key <see cref="APP_PATH_PREF" /> (<c>EZGAutoTest.IosAppPath</c>). Không có key này ⇒ case iOS bị bỏ qua.
    ///     </para>
    /// </summary>
    public sealed class IosDeviceBridge
    {
        /// <summary>EditorPrefs key chứa đường dẫn .app/.ipa của bản build test iOS (rỗng = bỏ qua iOS).</summary>
        public const string APP_PATH_PREF = "EZGAutoTest.IosAppPath";

        const string XCRUN = "/usr/bin/xcrun";
        const double LIST_TIMEOUT = 45;
        const double INSTALL_TIMEOUT = 900;
        const double LAUNCH_TIMEOUT = 90;
        const double COPY_TIMEOUT = 300;
        const double VERSION_TIMEOUT = 20;

        #region JSON devicectl (chỉ các field cần — JsonUtility bỏ qua field thừa)

        [Serializable]
        class ListOutput
        {
            public ListResult result = new();
        }

        [Serializable]
        class ListResult
        {
            public List<JsonDevice> devices = new();
        }

        [Serializable]
        class JsonDevice
        {
            public string identifier;
            public JsonConnection connectionProperties = new();
            public JsonDeviceProps deviceProperties = new();
            public JsonHardware hardwareProperties = new();
        }

        [Serializable]
        class JsonConnection
        {
            public string pairingState;
            public string tunnelState;
            public string transportType;
        }

        [Serializable]
        class JsonDeviceProps
        {
            public string name;
            public string osVersionNumber;
            public string developerModeStatus;
        }

        [Serializable]
        class JsonHardware
        {
            public string marketingName;
            public string productType;
            public string platform;
            public string udid;
            public string reality;
        }

        #endregion

        /// <summary>Chỉ chạy được trên Editor macOS.</summary>
        public static bool IsSupportedPlatform => DeviceProcess.IsMac && File.Exists(XCRUN);

        /// <summary>Đường dẫn .app/.ipa đặt trong EditorPrefs (rỗng nếu chưa đặt).</summary>
        public static string AppPath
        {
            get => EditorPrefs.GetString(APP_PATH_PREF, "");
            set => EditorPrefs.SetString(APP_PATH_PREF, value ?? "");
        }

        /// <summary>Bundle id iOS của project.</summary>
        public static string BundleId()
        {
            try
            {
                return PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.iOS);
            }
            catch
            {
                return PlayerSettings.applicationIdentifier;
            }
        }

        /// <summary>Phiên bản devicectl (null nếu không có Xcode 15+ / không phải macOS).</summary>
        public async Task<string> VersionAsync(CancellationToken token)
        {
            if (!IsSupportedPlatform) return null;
            var r = await DeviceProcess.RunAsync(XCRUN, new[] { "devicectl", "--version" }, VERSION_TIMEOUT, token);
            return r.Ok ? r.StdOut.Trim() : null;
        }

        /// <summary>Liệt kê máy iOS đã pair (đọc JSON output — giao diện chính thức cho script của devicectl).</summary>
        public async Task<(List<IosDevice> devices, ToolResult raw)> ListDevicesAsync(CancellationToken token)
        {
            var list = new List<IosDevice>();
            if (!IsSupportedPlatform) return (list, new ToolResult { StartFailed = true, Error = "Không phải macOS" });
            var json = TempFile("devicectl_list");
            try
            {
                var r = await DeviceProcess.RunAsync(XCRUN,
                    new[] { "devicectl", "list", "devices", "--quiet", "--json-output", json }, LIST_TIMEOUT, token);
                if (!File.Exists(json)) return (list, r);
                list = ParseDevices(File.ReadAllText(json));
                return (list, r);
            }
            finally
            {
                TryDelete(json);
            }
        }

        public static List<IosDevice> ParseDevices(string json)
        {
            var list = new List<IosDevice>();
            if (string.IsNullOrWhiteSpace(json)) return list;
            ListOutput parsed;
            try
            {
                parsed = JsonUtility.FromJson<ListOutput>(json);
            }
            catch (Exception e)
            {
                AutoTestLog.Warn("Không parse được JSON của devicectl: " + e.Message);
                return list;
            }

            if (parsed?.result?.devices == null) return list;
            foreach (var d in parsed.result.devices)
            {
                if (d == null || string.IsNullOrEmpty(d.identifier)) continue;
                list.Add(new IosDevice
                {
                    Identifier = d.identifier,
                    Udid = d.hardwareProperties?.udid,
                    Name = d.deviceProperties?.name,
                    Model = d.hardwareProperties?.marketingName,
                    ProductType = d.hardwareProperties?.productType,
                    OsVersion = d.deviceProperties?.osVersionNumber,
                    Platform = d.hardwareProperties?.platform,
                    PairingState = d.connectionProperties?.pairingState,
                    TunnelState = d.connectionProperties?.tunnelState,
                    DeveloperMode = d.deviceProperties?.developerModeStatus,
                    // Thiếu field reality (devicectl cũ) ⇒ coi là máy thật.
                    Physical = string.IsNullOrEmpty(d.hardwareProperties?.reality) ||
                               d.hardwareProperties.reality == "physical"
                });
            }

            return list;
        }

        /// <summary>Cài .app / .ipa lên máy.</summary>
        public Task<ToolResult> InstallAsync(string deviceId, string appPath, CancellationToken token)
        {
            return DeviceProcess.RunAsync(XCRUN,
                new[] { "devicectl", "device", "install", "app", "--device", deviceId, appPath }, INSTALL_TIMEOUT,
                token);
        }

        /// <summary>
        ///     Mở app (tắt instance cũ) kèm argument <c>-ezgAutotestRun</c>/<c>-ezgAutotestSuites</c> và biến môi trường
        ///     <c>EZG_AUTOTEST_RUN</c>/<c>EZG_AUTOTEST_SUITES</c> (runtime đọc cả hai cho chắc).
        /// </summary>
        public Task<ToolResult> LaunchAsync(string deviceId, string bundleId, string runId, string suitesCsv,
            CancellationToken token)
        {
            var env = "{\"" + DeviceProtocol.ENV_RUN + "\":\"" + JsonEscape(runId) + "\",\"" +
                      DeviceProtocol.ENV_SUITES + "\":\"" + JsonEscape(suitesCsv) + "\"}";
            return DeviceProcess.RunAsync(XCRUN, new[]
            {
                "devicectl", "device", "process", "launch", "--device", deviceId, "--terminate-existing",
                "--environment-variables", env, bundleId,
                DeviceProtocol.ARG_RUN, runId, DeviceProtocol.ARG_SUITES, suitesCsv
            }, LAUNCH_TIMEOUT, token);
        }

        /// <summary>
        ///     Chép file/thư mục trong container dữ liệu của app về máy (vd source <c>Documents/EZGAutoTest/&lt;run&gt;</c>
        ///     — Application.persistentDataPath trên iOS là thư mục Documents).
        /// </summary>
        public Task<ToolResult> CopyFromAsync(string deviceId, string bundleId, string source, string destination,
            CancellationToken token)
        {
            return DeviceProcess.RunAsync(XCRUN, new[]
            {
                "devicectl", "device", "copy", "from", "--device", deviceId, "--domain-type", "appDataContainer",
                "--domain-identifier", bundleId, "--source", source, "--destination", destination, "--quiet"
            }, COPY_TIMEOUT, token);
        }

        /// <summary>Đường dẫn tương đối trong container của thư mục report một lượt chạy.</summary>
        public static string RemoteRunFolder(string runId)
        {
            return "Documents/" + DeviceProtocol.OUTPUT_FOLDER + "/" + runId;
        }

        static string JsonEscape(string s)
        {
            return (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        static string TempFile(string prefix)
        {
            return Path.Combine(Path.GetTempPath(), $"{prefix}_{Guid.NewGuid():N}.json");
        }

        static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch
            {
                // File tạm — bỏ qua.
            }
        }
    }
}
