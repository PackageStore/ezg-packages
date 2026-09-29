using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Ezg.AutoTest.Editor
{
    /// <summary>
    ///     Chạy auto test từ dòng lệnh / CI (không kèm -quit — runner tự thoát với exit code khi xong):
    ///     <code>
    ///     Unity -batchmode -projectPath . -executeMethod Ezg.AutoTest.Editor.AutoTestCli.Run
    ///           -autotestSuites static,smoke -autotestOut AutoTestReports/ci -autotestFailOn major -logFile -
    ///     </code>
    ///     Exit code: 0 đạt · 2 có case lỗi ≥ ngưỡng · 3 lỗi runner / quá giờ · 4 tham số sai.
    /// </summary>
    public static class AutoTestCli
    {
        const int EXIT_INVALID_ARGS = 4;
        const int EXIT_RUNNER_ERROR = 3;
        const float DEFAULT_TIMEOUT_MINUTES = 90f;

        /// <summary>Entry point cho -executeMethod.</summary>
        public static void Run()
        {
            try
            {
                var args = ParseArgs(Environment.GetCommandLineArgs());
                if (args.ContainsKey("-autotestHelp"))
                {
                    PrintHelp();
                    EditorApplication.Exit(0);
                    return;
                }

                var all = AutoTestRegistry.CreateSuites();
                var suitesArg = Get(args, "-autotestSuites", "all");
                List<string> suiteIds;
                if (suitesArg.Equals("all", StringComparison.OrdinalIgnoreCase))
                {
                    var cfg = AutoTestSettings.Config;
                    suiteIds = all.Where(s => cfg.IsSuiteEnabled(s.Id) && s.Mode != ExecutionMode.Device)
                        .Select(s => s.Id).ToList();
                }
                else
                {
                    suiteIds = suitesArg.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(s => s.Trim()).ToList();
                    var unknown = suiteIds.Where(id => all.All(s => !s.Id.Equals(id, StringComparison.OrdinalIgnoreCase)))
                        .ToList();
                    if (unknown.Count > 0)
                    {
                        Debug.LogError(AutoTestLog.PREFIX + "Suite không tồn tại: " + string.Join(", ", unknown) +
                                       ". Có: " + string.Join(", ", all.Select(s => s.Id)));
                        EditorApplication.Exit(EXIT_INVALID_ARGS);
                        return;
                    }
                }

                var options = new AutoTestRunOptions
                {
                    Trigger = "CLI",
                    Title = Get(args, "-autotestTitle", null),
                    OutputDir = ToAbsolute(Get(args, "-autotestOut", null)),
                    ScenarioFilter = Get(args, "-autotestScenario", null),
                    ExitEditorWhenDone = true,
                    OpenReportWhenFinished = false,
                    FailOn = ParseSeverity(Get(args, "-autotestFailOn", "major"), out var noFail),
                    NoFail = noFail,
                    GlobalTimeoutMinutes = float.TryParse(Get(args, "-autotestTimeout", ""), NumberStyles.Float,
                        CultureInfo.InvariantCulture, out var minutes)
                        ? minutes
                        : DEFAULT_TIMEOUT_MINUTES
                };
                var cases = Get(args, "-autotestCases", null);
                if (!string.IsNullOrEmpty(cases))
                    options.CaseFilter = cases.Split(',').Select(c => c.Trim()).Where(c => c.Length > 0).ToList();

                Debug.Log(AutoTestLog.PREFIX + $"CLI chạy: {string.Join(", ", suiteIds)} (failOn={options.FailOn})");
                // Đợi Editor ổn định (import/compile xong) rồi mới bắt đầu.
                EditorApplication.delayCall += () => StartWhenReady(suiteIds, options);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                EditorApplication.Exit(EXIT_RUNNER_ERROR);
            }
        }

        static void StartWhenReady(List<string> suiteIds, AutoTestRunOptions options)
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += () => StartWhenReady(suiteIds, options);
                return;
            }

            if (!AutoTestRunner.Run(suiteIds, options))
            {
                Debug.LogError(AutoTestLog.PREFIX + "Không bắt đầu được lượt chạy.");
                EditorApplication.Exit(EXIT_RUNNER_ERROR);
            }
        }

        static Severity ParseSeverity(string s, out bool noFail)
        {
            noFail = false;
            switch ((s ?? "").ToLowerInvariant())
            {
                case "blocker": return Severity.Blocker;
                case "critical": return Severity.Critical;
                case "minor": return Severity.Minor;
                case "info": return Severity.Info;
                case "none":
                case "never":
                    noFail = true;
                    return Severity.Blocker;
                default: return Severity.Major;
            }
        }

        static Dictionary<string, string> ParseArgs(string[] argv)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < argv.Length; i++)
            {
                if (!argv[i].StartsWith("-autotest", StringComparison.OrdinalIgnoreCase)) continue;
                var hasValue = i + 1 < argv.Length && !argv[i + 1].StartsWith("-", StringComparison.Ordinal);
                map[argv[i]] = hasValue ? argv[++i] : "";
            }

            return map;
        }

        static string Get(Dictionary<string, string> map, string key, string fallback)
        {
            return map.TryGetValue(key, out var v) && !string.IsNullOrEmpty(v) ? v : fallback;
        }

        static string ToAbsolute(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            return System.IO.Path.IsPathRooted(path)
                ? path
                : System.IO.Path.Combine(StaticCheckUtil.ProjectRoot(), path);
        }

        static void PrintHelp()
        {
            Debug.Log(AutoTestLog.PREFIX + "Tham số: -autotestSuites all|static,smoke,... -autotestOut <thư mục> " +
                      "-autotestFailOn blocker|critical|major|minor|none -autotestTimeout <phút> " +
                      "-autotestScenario <lọc tên kịch bản> -autotestCases suite/case,... -autotestTitle <tiêu đề>. " +
                      "Suite có: " + string.Join(", ", AutoTestRegistry.CreateSuites().Select(s => s.Id)));
        }
    }
}
