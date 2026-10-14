using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Ezg.BugLogger.Editor
{
    /// <summary>
    /// Bake danh tính build vào mọi build player: ghi <c>Temp/BugLogger/buglogger_build.json</c>
    /// (commit: <c>git rev-parse HEAD</c> → env <c>CI_COMMIT_SHA</c> → <c>unknown</c>; branch: git → env
    /// <c>CI_COMMIT_REF_NAME</c> khi git ở detached HEAD như trên CI → <c>unknown</c>; kèm số build của nền
    /// tảng) rồi thêm folder đó vào StreamingAssets của build dưới tên <c>BugLogger/</c> (vài trăm byte — Bug
    /// Logger có trong mọi bản build, chỉ hiện khi cheat bật). Không ghi gì dưới <c>Assets/</c>, nên build xong
    /// <c>git status</c> vẫn sạch. Trong Editor còn cấp HEAD + branch hiện tại cho runtime.
    /// </summary>
    internal sealed class BugLoggerBuildInfoBaker : BuildPlayerProcessor
    {
        #region Fields

        public const string CI_SHA_VARIABLE = "CI_COMMIT_SHA";
        public const string CI_BRANCH_VARIABLE = "CI_COMMIT_REF_NAME";

        private const string GIT_EXECUTABLE = "git";
        private const string GIT_SHA_ARGUMENTS = "rev-parse HEAD";
        private const string GIT_BRANCH_ARGUMENTS = "rev-parse --abbrev-ref HEAD";
        private const string DETACHED_HEAD = "HEAD";
        private const string TEMP_FOLDER = "Temp";
        private const string ROUND_TRIP_FORMAT = "o";
        private const int GIT_TIMEOUT_MS = 5000;

        private static readonly Regex CommitShaPattern = new Regex("^[0-9a-f]{40}$", RegexOptions.CultureInvariant);

        /// <summary>Lấy trên main thread lúc load; resolver chạy trên thread pool.</summary>
        private static string _projectRoot;

        #endregion

        #region Public

        /// <inheritdoc />
        public override int callbackOrder => 0;

        /// <inheritdoc />
        public override void PrepareForBuild(BuildPlayerContext buildPlayerContext)
        {
            var options = buildPlayerContext.BuildPlayerOptions;
            var data = new BugLoggerBuildInfo.Data
            {
                commitSha = ResolveCommitSha(ProjectRoot),
                branch = ResolveBranch(ProjectRoot),
                build = ResolveBuildNumber(options.target),
                builtAtUtc = BugLoggerClock.UtcNow.ToString(ROUND_TRIP_FORMAT, CultureInfo.InvariantCulture),
            };

            var directory = Path.Combine(ProjectRoot, TEMP_FOLDER, BugLoggerBuildInfo.STREAMING_ASSETS_FOLDER);
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, BugLoggerBuildInfo.FILE_NAME), JsonUtility.ToJson(data));
            buildPlayerContext.AddAdditionalPathToStreamingAssets(directory, BugLoggerBuildInfo.STREAMING_ASSETS_FOLDER);

            Debug.Log($"[BugLogger] Baked build info: commitSha {data.commitSha}, branch {data.branch}, build {data.build}");
        }


        /// <summary>Commit hiện tại: git → <see cref="CI_SHA_VARIABLE"/> → <c>unknown</c>. Không Unity API.</summary>
        public static string ResolveCommitSha(string workingDirectory)
        {
            return PickCommitSha(RunGit(workingDirectory, GIT_SHA_ARGUMENTS), Environment.GetEnvironmentVariable(CI_SHA_VARIABLE));
        }

        /// <summary>Branch hiện tại: git → <see cref="CI_BRANCH_VARIABLE"/> (CI checkout detached HEAD) → <c>unknown</c>. Không Unity API.</summary>
        public static string ResolveBranch(string workingDirectory)
        {
            return PickBranch(RunGit(workingDirectory, GIT_BRANCH_ARGUMENTS), Environment.GetEnvironmentVariable(CI_BRANCH_VARIABLE));
        }

        /// <summary>Chọn commit: sha của git nếu hợp lệ, không thì của CI, không thì <c>unknown</c>.</summary>
        public static string PickCommitSha(string gitSha, string ciSha)
        {
            var sha = gitSha?.Trim().ToLowerInvariant();
            if (IsCommitSha(sha))
                return sha;

            sha = ciSha?.Trim().ToLowerInvariant();
            return IsCommitSha(sha) ? sha : BugLoggerBuildInfo.UNKNOWN;
        }

        /// <summary>Chọn branch: của git trừ khi trống/detached (<c>HEAD</c>), không thì của CI, không thì <c>unknown</c>.</summary>
        public static string PickBranch(string gitBranch, string ciBranch)
        {
            var branch = gitBranch?.Trim();
            if (!string.IsNullOrEmpty(branch) && branch != DETACHED_HEAD)
                return branch;

            branch = ciBranch?.Trim();
            return string.IsNullOrEmpty(branch) ? BugLoggerBuildInfo.UNKNOWN : branch;
        }

        #endregion

        #region Private

        private static string ProjectRoot => _projectRoot ??= Path.GetDirectoryName(Application.dataPath);

        [InitializeOnLoadMethod]
        private static void RegisterEditorResolver()
        {
            _projectRoot = Path.GetDirectoryName(Application.dataPath);
            var root = _projectRoot;
            BugLoggerBuildInfo.EditorResolver = () => new BugLoggerBuildInfo.Data
            {
                commitSha = ResolveCommitSha(root),
                branch = ResolveBranch(root),
                build = BugLoggerBuildInfo.EDITOR_BUILD,
            };
        }


        private static string ResolveBuildNumber(BuildTarget target)
        {
            switch (target)
            {
                case BuildTarget.Android:
                    return PlayerSettings.Android.bundleVersionCode.ToString(CultureInfo.InvariantCulture);
                case BuildTarget.iOS:
                    return PlayerSettings.iOS.buildNumber;
                default:
                    return string.Empty;
            }
        }

        private static bool IsCommitSha(string value)
        {
            return !string.IsNullOrEmpty(value) && CommitShaPattern.IsMatch(value);
        }

        private static string RunGit(string workingDirectory, string arguments)
        {
            try
            {
                // Không redirect stderr: pipe không ai đọc có thể đầy và chặn git.
                var startInfo = new ProcessStartInfo(GIT_EXECUTABLE, arguments)
                {
                    WorkingDirectory = workingDirectory,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                };

                using (var process = Process.Start(startInfo))
                {
                    if (process == null)
                        return null;

                    // Đọc bất đồng bộ để timeout bên dưới thật sự chặn được git bị treo.
                    var output = process.StandardOutput.ReadToEndAsync();
                    if (!process.WaitForExit(GIT_TIMEOUT_MS) || !output.Wait(GIT_TIMEOUT_MS))
                    {
                        process.Kill();
                        return null;
                    }

                    return process.ExitCode == 0 ? output.Result.Trim() : null;
                }
            }
            catch (Exception)
            {
                // Không có git / không phải repository: rơi xuống biến môi trường của CI.
                return null;
            }
        }

        #endregion
    }
}
