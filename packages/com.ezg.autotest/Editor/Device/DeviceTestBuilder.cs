using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Xml;
using UnityEditor;
using UnityEditor.Android;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Ezg.AutoTest.Editor
{
    /// <summary>Kết quả build APK test.</summary>
    public sealed class DeviceBuildResult
    {
        public bool Success;
        public string ApkPath;
        public long SizeBytes;
        public double Seconds;
        public string Message;
        public int ErrorCount;
        public int WarningCount;
        public List<string> Errors = new();

        /// <summary>Ghi chú cho QA (vd đã tạm dùng debug keystore).</summary>
        public List<string> Notes = new();
    }

    /// <summary>
    ///     Build APK TEST (define <c>EZG_AUTOTEST</c> ⇒ có runtime auto test + <see cref="AutoTestDeviceRunner" />) mà
    ///     KHÔNG đổi cấu hình project: tạm tắt AAB / export Gradle / split APK, tạm dùng debug keystore khi thiếu mật
    ///     khẩu keystore, bake <see cref="DeviceRunRequest" /> vào Resources — mọi thứ được trả lại trong finally.
    ///     Không bao giờ tự đổi platform.
    /// </summary>
    public static class DeviceTestBuilder
    {
        public const string DEFINE = "EZG_AUTOTEST";
        public const string TEMP_FOLDER = "Assets/EZGAutoTestTemp";
        public const string TEMP_RESOURCES = TEMP_FOLDER + "/Resources";
        public const string BAKED_ASSET = TEMP_RESOURCES + "/" + DeviceProtocol.RESOURCE_NAME + ".json";
        const string APK_SUFFIX = "_autotest.apk";
        const string WRONG_PLATFORM =
            "Chuyển platform sang Android trước hoặc điền APK có sẵn trong Settings > Device";

        /// <summary>True trong lúc đang build APK test (post-processor Gradle dùng để chèn intent-filter Game Loop).</summary>
        public static bool IsBuilding { get; private set; }

        /// <summary>Package name Android: Settings &gt; Device &gt; packageName hoặc applicationIdentifier của Android.</summary>
        public static string PackageName(AutoTestConfig config)
        {
            var custom = config?.device?.packageName;
            if (!string.IsNullOrWhiteSpace(custom)) return custom.Trim();
            try
            {
                return PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android);
            }
            catch
            {
                return PlayerSettings.applicationIdentifier;
            }
        }

        /// <summary>Đường dẫn APK test mặc định: &lt;project&gt;/&lt;buildOutputFolder&gt;/&lt;productName&gt;_autotest.apk.</summary>
        public static string DefaultApkPath(AutoTestConfig config)
        {
            var folder = config?.device?.buildOutputFolder;
            if (string.IsNullOrWhiteSpace(folder)) folder = "Builds/AutoTest";
            var root = Path.GetDirectoryName(Application.dataPath)!;
            var dir = Path.IsPathRooted(folder) ? folder : Path.Combine(root, folder);
            return Path.Combine(dir, SafeFileName(PlayerSettings.productName) + APK_SUFFIX).Replace('\\', '/');
        }

        /// <summary>null = build được; ngược lại là lý do (tiếng Việt, kèm cách sửa).</summary>
        public static string CheckCanBuild()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return "Đang ở Play mode — thoát Play rồi chạy lại.";
            if (EditorApplication.isCompiling) return "Unity đang compile script — chờ compile xong rồi chạy lại.";
            if (BuildPipeline.isBuildingPlayer) return "Unity đang build một bản khác.";
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android) return WRONG_PLATFORM + ".";
            if (!EditorBuildSettings.scenes.Any(s => s.enabled && File.Exists(s.path)))
                return "Build Settings không có scene nào được bật (File > Build Profiles > Scene List).";
            return null;
        }

        /// <summary>
        ///     Build APK test (đồng bộ — Editor đứng trong lúc build, giống bấm Build). Không ném exception: lỗi nằm
        ///     trong <see cref="DeviceBuildResult" />.
        /// </summary>
        /// <param name="config">Cấu hình auto test (bake vào build để device dùng cùng cấu hình với Editor).</param>
        /// <param name="development">Development build (debuggable — cần cho run-as và log đầy đủ).</param>
        /// <param name="outputPath">null = <see cref="DefaultApkPath" />.</param>
        public static DeviceBuildResult BuildTestApk(AutoTestConfig config, bool development = true,
            string outputPath = null)
        {
            var result = new DeviceBuildResult();
            var reason = CheckCanBuild();
            if (reason != null)
            {
                result.Message = reason;
                return result;
            }

            config ??= new AutoTestConfig();
            var apk = string.IsNullOrEmpty(outputPath) ? DefaultApkPath(config) : outputPath;
            result.ApkPath = apk;
            var watch = Stopwatch.StartNew();

            // Lưu cấu hình để trả lại nguyên vẹn.
            var prevAab = EditorUserBuildSettings.buildAppBundle;
            var prevExport = EditorUserBuildSettings.exportAsGoogleAndroidProject;
            var prevSplit = PlayerSettings.Android.buildApkPerCpuArchitecture;
            var prevCustomKeystore = PlayerSettings.Android.useCustomKeystore;

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(apk)!);
                if (File.Exists(apk)) File.Delete(apk);

                EditorUserBuildSettings.buildAppBundle = false;
                EditorUserBuildSettings.exportAsGoogleAndroidProject = false;
                PlayerSettings.Android.buildApkPerCpuArchitecture = false;
                if (PlayerSettings.Android.useCustomKeystore && !KeystoreUsable())
                {
                    PlayerSettings.Android.useCustomKeystore = false;
                    result.Notes.Add("Keystore release thiếu mật khẩu/file → APK test ký bằng debug keystore. Máy đang " +
                                     "cài bản ký keystore khác phải gỡ app trước (Settings > Device > Gỡ app trước khi cài).");
                }

                WriteBakedRequest(config);

                var options = new BuildPlayerOptions
                {
                    scenes = EditorBuildSettings.scenes.Where(s => s.enabled && File.Exists(s.path))
                        .Select(s => s.path).ToArray(),
                    locationPathName = apk,
                    target = BuildTarget.Android,
                    targetGroup = BuildTargetGroup.Android,
                    options = development ? BuildOptions.Development : BuildOptions.None,
                    extraScriptingDefines = new[] { DEFINE }
                };

                IsBuilding = true;
                var report = BuildPipeline.BuildPlayer(options);
                FillFromReport(result, report);
            }
            catch (Exception e)
            {
                result.Success = false;
                result.Message = $"Build lỗi: {e.GetType().Name}: {e.Message}";
                result.Errors.Add(e.ToString());
                Debug.LogException(e);
            }
            finally
            {
                IsBuilding = false;
                RestoreSetting(() => EditorUserBuildSettings.buildAppBundle = prevAab);
                RestoreSetting(() => EditorUserBuildSettings.exportAsGoogleAndroidProject = prevExport);
                RestoreSetting(() => PlayerSettings.Android.buildApkPerCpuArchitecture = prevSplit);
                RestoreSetting(() => PlayerSettings.Android.useCustomKeystore = prevCustomKeystore);
                DeleteBakedRequest();
                result.Seconds = watch.Elapsed.TotalSeconds;
            }

            if (result.Success && File.Exists(apk)) result.SizeBytes = new FileInfo(apk).Length;
            else if (result.Success)
            {
                result.Success = false;
                result.Message = "Build báo thành công nhưng không thấy file APK: " + apk;
            }

            return result;
        }

        static void FillFromReport(DeviceBuildResult result, BuildReport report)
        {
            if (report == null)
            {
                result.Message = "BuildPipeline.BuildPlayer không trả report.";
                return;
            }

            var summary = report.summary;
            result.Success = summary.result == BuildResult.Succeeded;
            result.ErrorCount = summary.totalErrors;
            result.WarningCount = summary.totalWarnings;
            try
            {
                foreach (var step in report.steps)
                foreach (var msg in step.messages)
                    if (msg.type == LogType.Error || msg.type == LogType.Exception || msg.type == LogType.Assert)
                        result.Errors.Add($"[{step.name}] {msg.content}");
            }
            catch (Exception e)
            {
                result.Errors.Add("Không đọc được chi tiết lỗi build: " + e.Message);
            }

            result.Message = result.Success
                ? $"Build xong: {result.ApkPath}"
                : $"Build {summary.result}: {summary.totalErrors} lỗi" +
                  (result.Errors.Count > 0 ? " — " + FirstLine(result.Errors[0]) : "");
        }

        /// <summary>Keystore release dùng được không (có file + đã nhập mật khẩu trong phiên Editor này).</summary>
        static bool KeystoreUsable()
        {
            var ks = PlayerSettings.Android.keystoreName;
            if (string.IsNullOrEmpty(ks)) return false;
            var path = Path.IsPathRooted(ks) ? ks : Path.Combine(Path.GetDirectoryName(Application.dataPath)!, ks);
            return File.Exists(path) && !string.IsNullOrEmpty(PlayerSettings.Android.keystorePass) &&
                   !string.IsNullOrEmpty(PlayerSettings.Android.keyaliasPass);
        }

        #region Bake yêu cầu chạy vào Resources

        /// <summary>
        ///     Ghi <c>Assets/EZGAutoTestTemp/Resources/EZGAutoTestDeviceConfig.json</c> (autoRunOnLaunch = false — Editor
        ///     kích hoạt bằng intent). Public để build tay bản iOS: gọi hàm này, build Xcode (thêm define EZG_AUTOTEST),
        ///     rồi gọi <see cref="DeleteBakedRequest" />.
        /// </summary>
        public static void WriteBakedRequest(AutoTestConfig config, bool autoRunOnLaunch = false)
        {
            var request = new DeviceRunRequest
            {
                runId = "",
                suites = new List<string>(config?.device?.suites ?? new List<string>()),
                autoRunOnLaunch = autoRunOnLaunch,
                configJson = JsonUtility.ToJson(config ?? new AutoTestConfig()),
                packageVersion = PackageVersion(),
                buildNumber = PlayerSettings.Android.bundleVersionCode.ToString()
            };
            if (!AssetDatabase.IsValidFolder(TEMP_FOLDER)) AssetDatabase.CreateFolder("Assets", "EZGAutoTestTemp");
            if (!AssetDatabase.IsValidFolder(TEMP_RESOURCES)) AssetDatabase.CreateFolder(TEMP_FOLDER, "Resources");
            var abs = Path.Combine(Path.GetDirectoryName(Application.dataPath)!, BAKED_ASSET);
            File.WriteAllText(abs, JsonUtility.ToJson(request, true));
            AssetDatabase.ImportAsset(BAKED_ASSET, ImportAssetOptions.ForceSynchronousImport);
        }

        /// <summary>Xoá thư mục tạm chứa config bake (không để lọt vào build release).</summary>
        public static void DeleteBakedRequest()
        {
            try
            {
                if (AssetDatabase.IsValidFolder(TEMP_FOLDER)) AssetDatabase.DeleteAsset(TEMP_FOLDER);
                var abs = Path.Combine(Path.GetDirectoryName(Application.dataPath)!, TEMP_FOLDER);
                if (Directory.Exists(abs)) Directory.Delete(abs, true);
                if (File.Exists(abs + ".meta")) File.Delete(abs + ".meta");
            }
            catch (Exception e)
            {
                Debug.LogWarning(AutoTestLog.PREFIX + $"Không xoá được {TEMP_FOLDER}: {e.Message} — xoá tay thư mục này.");
            }
        }

        #endregion

        #region Helpers

        static string PackageVersion()
        {
            try
            {
                var info = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(DeviceTestBuilder).Assembly);
                return info?.version ?? "";
            }
            catch
            {
                return "";
            }
        }

        static void RestoreSetting(Action restore)
        {
            try
            {
                restore();
            }
            catch (Exception e)
            {
                Debug.LogWarning(AutoTestLog.PREFIX + "Không khôi phục được một setting build: " + e.Message);
            }
        }

        static string SafeFileName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "app";
            var invalid = Path.GetInvalidFileNameChars();
            var chars = name.Trim().Select(c => invalid.Contains(c) || c == ' ' ? '_' : c).ToArray();
            return new string(chars);
        }

        static string FirstLine(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var i = s.IndexOf('\n');
            return i < 0 ? s : s.Substring(0, i);
        }

        #endregion
    }

    /// <summary>
    ///     Chèn intent-filter <c>com.google.intent.action.TEST_LOOP</c> vào Activity khởi động — CHỈ khi đang build
    ///     APK test — để APK chạy được Firebase Test Lab Game Loop. Build thường không bị ảnh hưởng.
    /// </summary>
    sealed class DeviceGameLoopManifestInjector : IPostGenerateGradleAndroidProject
    {
        const string ANDROID_NS = "http://schemas.android.com/apk/res/android";
        const string ACTION_MAIN = "android.intent.action.MAIN";
        const string CATEGORY_LAUNCHER = "android.intent.category.LAUNCHER";
        const string CATEGORY_DEFAULT = "android.intent.category.DEFAULT";
        const string GAME_LOOP_MIME = "application/javascript";

        public int callbackOrder => 1000;

        public void OnPostGenerateGradleAndroidProject(string path)
        {
            if (!DeviceTestBuilder.IsBuilding) return;
            try
            {
                var candidates = new[]
                {
                    Path.Combine(path, "src", "main", "AndroidManifest.xml"),
                    Path.Combine(path, "..", "launcher", "src", "main", "AndroidManifest.xml")
                };
                foreach (var manifest in candidates)
                    if (File.Exists(manifest) && TryInject(manifest))
                        return;
                Debug.LogWarning(AutoTestLog.PREFIX +
                                 "Không tìm thấy Activity LAUNCHER để chèn intent-filter Game Loop (Firebase Test Lab).");
            }
            catch (Exception e)
            {
                Debug.LogWarning(AutoTestLog.PREFIX + "Chèn intent-filter Game Loop lỗi: " + e.Message);
            }
        }

        static bool TryInject(string manifestPath)
        {
            var doc = new XmlDocument();
            doc.Load(manifestPath);
            var ns = new XmlNamespaceManager(doc.NameTable);
            ns.AddNamespace("android", ANDROID_NS);
            var activities = doc.SelectNodes("/manifest/application/activity|/manifest/application/activity-alias");
            if (activities == null) return false;
            foreach (XmlElement activity in activities)
            {
                var isLauncher = false;
                var hasGameLoop = false;
                foreach (XmlNode filter in activity.SelectNodes("intent-filter")!)
                {
                    if (filter.SelectSingleNode($"action[@android:name='{ACTION_MAIN}']", ns) != null &&
                        filter.SelectSingleNode($"category[@android:name='{CATEGORY_LAUNCHER}']", ns) != null)
                        isLauncher = true;
                    if (filter.SelectSingleNode($"action[@android:name='{DeviceProtocol.ACTION_GAME_LOOP}']", ns) != null)
                        hasGameLoop = true;
                }

                if (!isLauncher) continue;
                if (hasGameLoop) return true;

                var gameLoop = doc.CreateElement("intent-filter");
                gameLoop.AppendChild(NamedElement(doc, "action", DeviceProtocol.ACTION_GAME_LOOP));
                gameLoop.AppendChild(NamedElement(doc, "category", CATEGORY_DEFAULT));
                var data = doc.CreateElement("data");
                data.Attributes.Append(AndroidAttribute(doc, "mimeType", GAME_LOOP_MIME));
                gameLoop.AppendChild(data);
                activity.AppendChild(gameLoop);
                doc.Save(manifestPath);
                return true;
            }

            return false;
        }

        static XmlElement NamedElement(XmlDocument doc, string tag, string name)
        {
            var e = doc.CreateElement(tag);
            e.Attributes.Append(AndroidAttribute(doc, "name", name));
            return e;
        }

        /// <summary>Thuộc tính android:xxx (giữ đúng prefix "android" đã khai báo ở thẻ manifest).</summary>
        static XmlAttribute AndroidAttribute(XmlDocument doc, string localName, string value)
        {
            var attr = doc.CreateAttribute("android", localName, ANDROID_NS);
            attr.Value = value;
            return attr;
        }
    }
}
