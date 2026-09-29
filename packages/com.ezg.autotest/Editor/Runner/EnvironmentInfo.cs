using System;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Ezg.AutoTest.Editor
{
    /// <summary>Thu thập thông tin môi trường chạy (project, build, git, máy) cho report.</summary>
    public static class EnvironmentInfo
    {
        const int GIT_TIMEOUT_MS = 3000;

        public static RunEnvironment Collect(string adapterName)
        {
            var env = new RunEnvironment
            {
                projectName = Path.GetFileName(StaticCheckUtil.ProjectRoot()),
                productName = PlayerSettings.productName,
                companyName = PlayerSettings.companyName,
                bundleId = PlayerSettings.applicationIdentifier,
                appVersion = PlayerSettings.bundleVersion,
                buildNumber = $"Android {PlayerSettings.Android.bundleVersionCode} / iOS {PlayerSettings.iOS.buildNumber}",
                unityVersion = Application.unityVersion,
                platform = Application.platform.ToString(),
                buildTarget = EditorUserBuildSettings.activeBuildTarget.ToString(),
                renderPipeline = GraphicsSettings.defaultRenderPipeline != null
                    ? GraphicsSettings.defaultRenderPipeline.GetType().Name
                    : "Built-in",
                machine = Environment.MachineName,
                os = SystemInfo.operatingSystem,
                user = Environment.UserName,
                deviceModel = SystemInfo.deviceModel,
                deviceOs = SystemInfo.operatingSystem,
                gpu = SystemInfo.graphicsDeviceName,
                systemMemoryMb = SystemInfo.systemMemorySize,
                adapter = adapterName,
                packageVersion = PackageVersion()
            };
            env.gitBranch = Git("rev-parse --abbrev-ref HEAD");
            env.gitCommit = Git("rev-parse --short HEAD");
            var status = Git("status --porcelain --untracked-files=no");
            env.gitDirty = !string.IsNullOrWhiteSpace(status);
            return env;
        }

        public static string PackageVersion()
        {
            var info = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(EnvironmentInfo).Assembly);
            return info != null ? info.version : "dev";
        }

        /// <summary>Chạy git (không có git/không phải repo → chuỗi rỗng, không bao giờ ném lỗi).</summary>
        static string Git(string args)
        {
            try
            {
                var psi = new ProcessStartInfo("git", args)
                {
                    WorkingDirectory = StaticCheckUtil.ProjectRoot(),
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var p = Process.Start(psi);
                if (p == null) return "";
                var output = p.StandardOutput.ReadToEnd();
                if (!p.WaitForExit(GIT_TIMEOUT_MS))
                {
                    p.Kill();
                    return "";
                }

                return p.ExitCode == 0 ? output.Trim() : "";
            }
            catch
            {
                return "";
            }
        }
    }
}
