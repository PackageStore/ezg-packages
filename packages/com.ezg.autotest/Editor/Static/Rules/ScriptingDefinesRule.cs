using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.Build;

namespace Ezg.AutoTest.Editor
{
    /// <summary>
    ///     Scripting Define Symbols của Android + iOS: define debug/cheat bị cấm khi release, define lệch giữa hai
    ///     nền tảng, và define thêm từ Build Profile đang active (Unity 6).
    /// </summary>
    public sealed class ScriptingDefinesRule : IStaticRule
    {
        const string CATEGORY = "Build.Defines";
        const string SETTINGS_PATH = "ProjectSettings/ProjectSettings.asset";

        // Một phần (ngăn bởi '_') của tên define gợi ý code debug — chỉ báo Info nếu không nằm trong danh sách cấm.
        static readonly HashSet<string> SUSPICIOUS_PARTS = new(StringComparer.OrdinalIgnoreCase)
            { "CHEAT", "CHEATS", "DEBUG", "TEST", "TESTING", "GODMODE", "DEVMODE", "DEV" };

        public string Id => "scripting-defines";
        public string Name => "Scripting Define Symbols";

        public string Description =>
            "Define debug/cheat bị cấm (theo settings) không được bật cho Android/iOS; liệt kê define mỗi nền tảng và " +
            "define chỉ có ở một bên.";

        public string Category => "Build";
        public int Order => 120;

        public async Task Run(AutoTestContext ctx)
        {
            await StaticCheckUtil.Yield(ctx, 0);
            var forbidden = new HashSet<string>(
                (ctx.Config.staticCheck.forbiddenReleaseDefines ?? new List<string>())
                .Where(d => !string.IsNullOrWhiteSpace(d)).Select(d => d.Trim()),
                StringComparer.OrdinalIgnoreCase);

            var android = Read(NamedBuildTarget.Android);
            var ios = Read(NamedBuildTarget.iOS);
            CheckPlatform(ctx, "Android", android, forbidden);
            CheckPlatform(ctx, "iOS", ios, forbidden);

            var profileDefines = ActiveProfileDefines(out var profileName);
            if (profileDefines != null && profileDefines.Count > 0)
                CheckPlatform(ctx, $"Build Profile \"{profileName}\"", profileDefines, forbidden, true);

            var onlyAndroid = android.Where(d => !ios.Contains(d)).ToList();
            var onlyIos = ios.Where(d => !android.Contains(d)).ToList();
            if (onlyAndroid.Count > 0 || onlyIos.Count > 0)
                ctx.Report(Severity.Info, CATEGORY, "Define chỉ có ở một nền tảng",
                    $"Chỉ Android: {(onlyAndroid.Count > 0 ? string.Join(", ", onlyAndroid) : "—")}. " +
                    $"Chỉ iOS: {(onlyIos.Count > 0 ? string.Join(", ", onlyIos) : "—")}. " +
                    "Bình thường nếu là define SDK riêng nền tảng; kiểm tra nếu là define tính năng.",
                    SETTINGS_PATH, "Project Settings/Player/Other Settings/Scripting Define Symbols");

            ctx.Metric("Define Android", android.Count);
            ctx.Metric("Define iOS", ios.Count);

            var sb = new StringBuilder();
            sb.Append("# Scripting Define Symbols\n\n");
            AppendList(sb, "Android", android);
            AppendList(sb, "iOS", ios);
            if (profileDefines != null) AppendList(sb, $"Build Profile \"{profileName}\" (thêm)", profileDefines);
            ctx.Attach("scripting-defines.txt", sb.ToString(), "Scripting Define Symbols");
        }

        static List<string> Read(NamedBuildTarget target)
        {
            var raw = PlayerSettings.GetScriptingDefineSymbols(target) ?? "";
            return raw.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(d => d.Trim())
                .Where(d => d.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .ToList();
        }

        static void CheckPlatform(AutoTestContext ctx, string platform, List<string> defines,
            HashSet<string> forbidden, bool fromProfile = false)
        {
            ctx.Log($"Define {platform} ({defines.Count}): {(defines.Count > 0 ? string.Join(";", defines) : "(không có)")}");
            var uiPath = fromProfile
                ? "File/Build Profiles/Scripting Defines"
                : $"Project Settings/Player/{platform}/Other Settings/Script Compilation/Scripting Define Symbols";
            var location = fromProfile ? "Build Profile" : SETTINGS_PATH;

            foreach (var d in defines)
            {
                if (forbidden.Contains(d))
                {
                    ctx.Report(Severity.Major, CATEGORY, $"Define debug/cheat đang bật cho {platform}",
                            $"\"{d}\" nằm trong danh sách cấm khi release (staticCheck.forbiddenReleaseDefines) — code debug/cheat sẽ lọt vào bản build. Xoá trước khi build store.",
                            location, uiPath, "không có " + d, d)
                        .WithContentId(ctx, location + "|" + platform, d);
                    continue;
                }

                foreach (var part in d.Split('_'))
                    if (SUSPICIOUS_PARTS.Contains(part))
                    {
                        ctx.Report(Severity.Info, CATEGORY, $"Define có tên giống debug trên {platform}",
                                $"\"{d}\" không nằm trong danh sách cấm nhưng tên gợi ý code debug/test — kiểm tra trước khi build release (thêm vào forbiddenReleaseDefines nếu đúng là debug).",
                                location, uiPath)
                            .WithContentId(ctx, location + "|" + platform, d);
                        break;
                    }
            }
        }

        static void AppendList(StringBuilder sb, string title, List<string> defines)
        {
            sb.Append("## ").Append(title).Append(" (").Append(defines.Count).Append(")\n");
            if (defines.Count == 0) sb.Append("(không có)\n");
            foreach (var d in defines) sb.Append("- ").Append(d).Append('\n');
            sb.Append('\n');
        }

        /// <summary>Unity 6: Build Profile đang active có thể thêm define riêng (đọc qua reflection, không ghi).</summary>
        static List<string> ActiveProfileDefines(out string profileName)
        {
            profileName = null;
            try
            {
                var type = typeof(EditorBuildSettings).Assembly.GetType("UnityEditor.Build.Profile.BuildProfile");
                if (type == null) return null;
                var profile = type.GetMethod("GetActiveBuildProfile",
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static, null, Type.EmptyTypes, null)
                    ?.Invoke(null, null) as UnityEngine.Object;
                if (profile == null) return null;
                var value = type.GetProperty("scriptingDefines",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(profile);
                if (!(value is string[] arr)) return null;
                profileName = profile.name;
                return arr.Where(d => !string.IsNullOrWhiteSpace(d)).Select(d => d.Trim()).Distinct().ToList();
            }
            catch (Exception e)
            {
                AutoTestLog.Warn("Không đọc được define của Build Profile: " + e.Message);
                return null;
            }
        }
    }
}
