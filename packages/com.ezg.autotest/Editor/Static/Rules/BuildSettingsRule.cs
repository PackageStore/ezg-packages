using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using UnityEditor;

namespace Ezg.AutoTest.Editor
{
    /// <summary>
    ///     Danh sách scene trong Build Settings (hoặc Build Profile đang active nếu profile ghi đè scene): không có
    ///     scene bật, scene mất file / GUID hỏng, scene khai báo trùng, scene đang tắt.
    /// </summary>
    public sealed class BuildSettingsRule : IStaticRule
    {
        const string CATEGORY = "Build.Scenes";
        const string SETTINGS_PATH = "ProjectSettings/EditorBuildSettings.asset";
        const string UI_PATH = "File/Build Profiles/Scene List";

        public string Id => "build-settings";
        public string Name => "Build Settings: danh sách scene";

        public string Description =>
            "Có ít nhất một scene được bật, scene bật còn file + GUID hợp lệ, không khai báo trùng; liệt kê scene đang tắt.";

        public string Category => "Build";
        public int Order => 100;

        public async Task Run(AutoTestContext ctx)
        {
            await StaticCheckUtil.Yield(ctx, 0);

            var scenes = EditorBuildSettings.scenes ?? Array.Empty<EditorBuildSettingsScene>();
            var source = "Build Settings";
            var profileScenes = ActiveProfileScenes(out var profileName);
            if (profileScenes != null)
            {
                scenes = profileScenes;
                source = $"Build Profile \"{profileName}\"";
                ctx.Log($"Build Profile \"{profileName}\" đang active và ghi đè danh sách scene — kiểm tra theo profile.");
            }

            var enabledScenes = new List<string>();
            var disabledScenes = new List<string>();
            var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            for (var i = 0; i < scenes.Length; i++)
            {
                var s = scenes[i];
                if (s == null) continue;
                var path = (s.path ?? "").Replace('\\', '/');
                var guid = s.guid.Empty() ? "" : s.guid.ToString();
                var guidPath = string.IsNullOrEmpty(guid) ? "" : AssetDatabase.GUIDToAssetPath(guid);
                var pathExists = !string.IsNullOrEmpty(path) && File.Exists(StaticCheckUtil.AbsolutePath(path));
                var guidExists = !string.IsNullOrEmpty(guidPath) && File.Exists(StaticCheckUtil.AbsolutePath(guidPath));
                var display = string.IsNullOrEmpty(path) ? $"(scene #{i} không có đường dẫn)" : path;

                var dupKey = !string.IsNullOrEmpty(guid) ? "guid:" + guid : "path:" + path;
                if (seen.TryGetValue(dupKey, out var firstIndex))
                    ctx.Report(Severity.Minor, CATEGORY, "Scene bị khai báo trùng trong Build Settings",
                            $"{display} xuất hiện ở vị trí #{firstIndex} và #{i} của {source}. Xoá bớt một dòng.",
                            path, UI_PATH)
                        .WithContentId(ctx, SETTINGS_PATH, dupKey);
                else
                    seen[dupKey] = i;

                if (!s.enabled)
                {
                    disabledScenes.Add(display + (pathExists || guidExists ? "" : " (mất file)"));
                    continue;
                }

                enabledScenes.Add(display);
                if (!pathExists && !guidExists)
                {
                    ctx.Report(Severity.Critical, CATEGORY, "Scene đang bật trong Build Settings không tồn tại",
                            $"{display} (vị trí #{i}) không có trên đĩa và GUID cũng không trỏ tới scene nào — build sẽ lỗi hoặc thiếu scene. Xoá dòng này rồi thêm lại scene đúng.",
                            string.IsNullOrEmpty(path) ? SETTINGS_PATH : path, UI_PATH, "file scene tồn tại", "không có file")
                        .WithContentId(ctx, SETTINGS_PATH, dupKey);
                }
                else if (!pathExists)
                {
                    ctx.Report(Severity.Critical, CATEGORY, "Scene đang bật trong Build Settings không tồn tại",
                            $"{display} (vị trí #{i}) không còn ở đường dẫn này; GUID hiện trỏ tới {guidPath} (scene đã bị đổi tên/di chuyển). Xoá dòng này rồi thêm lại scene.",
                            path, UI_PATH, path, guidPath)
                        .WithContentId(ctx, SETTINGS_PATH, dupKey);
                }
                else if (!guidExists ||
                         !string.Equals(guidPath, path, StringComparison.OrdinalIgnoreCase))
                {
                    ctx.Report(Severity.Major, CATEGORY, "GUID scene trong Build Settings không khớp file",
                            string.IsNullOrEmpty(guid)
                                ? $"{display} không lưu GUID (Build Settings cũ) — đổi tên/di chuyển scene sẽ làm mất scene khỏi build. Xoá rồi thêm lại scene."
                                : $"{display}: GUID {guid} {(guidExists ? "trỏ tới " + guidPath : "không còn trỏ tới asset nào")} (file .meta bị tạo lại?). Xoá rồi thêm lại scene.",
                            path, UI_PATH)
                        .WithContentId(ctx, SETTINGS_PATH, dupKey);
                }
            }

            if (enabledScenes.Count == 0)
                ctx.Report(Severity.Blocker, CATEGORY, "Không có scene nào được bật trong Build Settings",
                    scenes.Length == 0
                        ? $"{source} trống — không build được app."
                        : $"{source} có {scenes.Length} scene nhưng đều đang tắt — không build được app.",
                    SETTINGS_PATH, UI_PATH, "≥ 1 scene bật", "0");
            else
                ctx.Log($"Scene khởi động (index 0): {enabledScenes[0]}");

            if (disabledScenes.Count > 0)
                ctx.Report(Severity.Info, CATEGORY, "Có scene đang tắt trong Build Settings",
                    $"{disabledScenes.Count} scene sẽ không vào bản build: {DataRuleUtil.JoinCapped(disabledScenes, 30)}",
                    SETTINGS_PATH, UI_PATH);

            CheckBootScene(ctx, scenes);

            ctx.Metric("Scene bật", enabledScenes.Count, "scene");
            ctx.Metric("Scene trong danh sách", scenes.Length, "scene");
        }

        /// <summary>Scene khởi động cấu hình cho Auto Test phải là scene đang bật (nếu có cấu hình).</summary>
        static void CheckBootScene(AutoTestContext ctx, EditorBuildSettingsScene[] scenes)
        {
            var boot = ctx.Config.general.bootScenePath;
            if (string.IsNullOrWhiteSpace(boot)) return;
            boot = boot.Trim().Replace('\\', '/');
            foreach (var s in scenes)
                if (s != null && s.enabled && string.Equals(s.path, boot, StringComparison.OrdinalIgnoreCase))
                    return;
            ctx.Report(Severity.Minor, CATEGORY, "Scene khởi động của Auto Test không nằm trong Build Settings",
                $"Settings Auto Test đặt bootScenePath = {boot} nhưng scene này không được bật trong Build Settings — test trên device sẽ khởi động khác Editor.",
                boot);
        }

        /// <summary>
        ///     Unity 6: Build Profile đang active có thể ghi đè danh sách scene. Đọc qua reflection để chạy được trên
        ///     mọi bản 6000.x (API BuildProfile thay đổi giữa các bản).
        /// </summary>
        static EditorBuildSettingsScene[] ActiveProfileScenes(out string profileName)
        {
            profileName = null;
            try
            {
                var type = typeof(EditorBuildSettings).Assembly.GetType("UnityEditor.Build.Profile.BuildProfile");
                if (type == null) return null;
                const BindingFlags STATIC = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
                const BindingFlags INSTANCE = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
                var profile = type.GetMethod("GetActiveBuildProfile", STATIC, null, Type.EmptyTypes, null)
                    ?.Invoke(null, null) as UnityEngine.Object;
                if (profile == null) return null;
                var overrides = type.GetProperty("overrideGlobalScenes", INSTANCE)?.GetValue(profile);
                if (!(overrides is bool b) || !b) return null;
                if (!(type.GetProperty("scenes", INSTANCE)?.GetValue(profile) is EditorBuildSettingsScene[] list))
                    return null;
                profileName = profile.name;
                return list;
            }
            catch (Exception e)
            {
                AutoTestLog.Warn("Không đọc được Build Profile đang active: " + e.Message);
                return null;
            }
        }
    }
}
