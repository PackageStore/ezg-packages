#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using UnityEngine.Networking;
using Debug = UnityEngine.Debug;

namespace Ezg.Editor.Shared.EzgKit
{
    /// <summary>
    ///     Tìm type theo TÊN NGẮN qua mọi assembly đã load. Kit là package dùng chung nên không được tham
    ///     chiếu assembly game (AppSecretsConfig), MAX (AppLovinSettings) hay com.ezg.localize — dò tên lúc
    ///     chạy là cách duy nhất để vừa dùng được vừa không vỡ compile khi dự án chưa cài thứ đó.
    /// </summary>
    internal static class TypeFinder
    {
        private static readonly Dictionary<string, Type> _cache = new();

        /// <summary>Type đầu tiên có <see cref="Type.Name" /> = <paramref name="shortName" />; null = không có.</summary>
        internal static Type Find(string shortName, Type mustDeriveFrom = null)
        {
            var key = shortName + "|" + mustDeriveFrom?.FullName;
            if (_cache.TryGetValue(key, out var cached) && (cached == null || cached.Assembly != null)) return cached;

            Type found = null;
            if (mustDeriveFrom != null)
            {
                foreach (var type in TypeCache.GetTypesDerivedFrom(mustDeriveFrom))
                    if (type.Name == shortName)
                    {
                        found = type;
                        break;
                    }
            }
            else
            {
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    Type[] types;
                    try
                    {
                        types = assembly.GetTypes();
                    }
                    catch (ReflectionTypeLoadException)
                    {
                        continue;
                    }

                    foreach (var type in types)
                        if (type.Name == shortName)
                        {
                            found = type;
                            break;
                        }

                    if (found != null) break;
                }
            }

            _cache[key] = found;
            return found;
        }

        /// <summary>Gọi khi domain có thể đã đổi (sau recompile thì static tự reset nên ít khi cần).</summary>
        internal static void Clear() => _cache.Clear();
    }

    /// <summary>Đọc / ghi scripting define cho Android + iOS.</summary>
    internal static class DefineSymbols
    {
        internal static readonly NamedBuildTarget[] MobileTargets = { NamedBuildTarget.Android, NamedBuildTarget.iOS };

        internal static bool Has(NamedBuildTarget target, string define)
        {
            var defines = PlayerSettings.GetScriptingDefineSymbols(target);
            foreach (var part in defines.Split(';'))
                if (part.Trim() == define)
                    return true;
            return false;
        }

        internal static bool HasOnAll(string define)
        {
            foreach (var target in MobileTargets)
                if (!Has(target, define))
                    return false;
            return true;
        }

        /// <summary>Bật / tắt define trên mọi nền tảng di động. Trả về true nếu có gì thay đổi.</summary>
        internal static bool Set(string define, bool enabled, bool dryRun, List<ChangeRow> changes)
        {
            var any = false;
            foreach (var target in MobileTargets)
            {
                var current = PlayerSettings.GetScriptingDefineSymbols(target);
                var list = new List<string>();
                foreach (var part in current.Split(';'))
                    if (!string.IsNullOrWhiteSpace(part))
                        list.Add(part.Trim());

                var has = list.Contains(define);
                if (has == enabled)
                {
                    changes?.Add(new ChangeRow("Define " + target.TargetName, define, has ? "có" : "không", has ? "có" : "không", true));
                    continue;
                }

                if (enabled) list.Add(define);
                else list.Remove(define);
                changes?.Add(new ChangeRow("Define " + target.TargetName, define, has ? "có" : "không", enabled ? "có" : "không", false));
                any = true;
                if (!dryRun) PlayerSettings.SetScriptingDefineSymbols(target, string.Join(";", list));
            }

            return any;
        }
    }

    /// <summary>
    ///     Một ô được đối chiếu giữa giá trị muốn ghi và giá trị đang có — dòng của bảng "Xem thay đổi".
    ///     Ghi nhận cả ô đã khớp để bảng cho PM thấy đủ, không chỉ phần lệch.
    /// </summary>
    internal readonly struct ChangeRow
    {
        internal readonly string Sink;
        internal readonly string Field;
        internal readonly string OldValue;
        internal readonly string NewValue;
        internal readonly bool Matched;

        /// <summary>Giá trị bí mật — bảng / API chỉ hiện vài ký tự.</summary>
        internal readonly bool Secret;

        internal ChangeRow(string sink, string field, string oldValue, string newValue, bool matched, bool secret = false)
        {
            Sink = sink;
            Field = field;
            OldValue = oldValue ?? string.Empty;
            NewValue = newValue ?? string.Empty;
            Matched = matched;
            Secret = secret;
        }

        internal JsonObject ToJson() =>
            new JsonObject()
                .Set("sink", Sink)
                .Set("field", Field)
                .Set("old", Secret ? Mask.Secret(OldValue) : OldValue)
                .Set("new", Secret ? Mask.Secret(NewValue) : NewValue)
                .Set("changed", !Matched);
    }

    /// <summary>Che secret khi hiện / trả về API: giữ 4 ký tự cuối để còn nhận ra key nào.</summary>
    internal static class Mask
    {
        internal static string Secret(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            if (value.Length <= 6) return new string('•', value.Length);
            return new string('•', 6) + value.Substring(value.Length - 4);
        }
    }

    /// <summary>
    ///     Chạy tiến trình ngoài (python3, git, keytool) không chặn Editor: đọc output ở luồng nền, kết quả
    ///     bắn callback trên main thread qua <see cref="EditorApplication.update" />.
    /// </summary>
    internal static class ProcessRunner
    {
        internal sealed class Result
        {
            internal int ExitCode = -1;
            internal string Output = string.Empty;
            internal string Error = string.Empty;
            internal string StartError;

            internal bool Ok => StartError == null && ExitCode == 0;

            internal string Combined =>
                StartError ?? (Output + (string.IsNullOrEmpty(Error) ? string.Empty : "\n" + Error)).Trim();
        }

        /// <summary>Đồng bộ — CHỈ cho lệnh rất ngắn (git check-ignore). Timeout mặc định 10 giây.</summary>
        internal static Result RunSync(string file, string arguments, string workingDirectory = null, int timeoutMs = 10000)
        {
            var result = new Result();
            try
            {
                using var process = Process.Start(Info(file, arguments, workingDirectory));
                if (process == null)
                {
                    result.StartError = $"Không chạy được {file}.";
                    return result;
                }

                var stdout = process.StandardOutput.ReadToEndAsync();
                var stderr = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(timeoutMs))
                {
                    try { process.Kill(); } catch (Exception) { /* đã thoát */ }
                    result.StartError = $"{file} chạy quá {timeoutMs / 1000}s.";
                    return result;
                }

                result.ExitCode = process.ExitCode;
                result.Output = stdout.Result;
                result.Error = stderr.Result;
            }
            catch (Exception exception)
            {
                result.StartError = $"Không chạy được {file}: {exception.Message}";
            }

            return result;
        }

        /// <summary>Bất đồng bộ — Editor vẫn dùng được trong lúc chạy.</summary>
        internal static void RunAsync(string file, string arguments, string workingDirectory, Action<Result> onDone)
        {
            var result = new Result();
            Process process;
            try
            {
                process = Process.Start(Info(file, arguments, workingDirectory));
            }
            catch (Exception exception)
            {
                result.StartError = $"Không chạy được {file}: {exception.Message}";
                onDone?.Invoke(result);
                return;
            }

            if (process == null)
            {
                result.StartError = $"Không chạy được {file}.";
                onDone?.Invoke(result);
                return;
            }

            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();

            void Poll()
            {
                if (!process.HasExited) return;
                EditorApplication.update -= Poll;
                try
                {
                    result.ExitCode = process.ExitCode;
                    result.Output = stdout.Result;
                    result.Error = stderr.Result;
                }
                catch (Exception exception)
                {
                    result.StartError = exception.Message;
                }
                finally
                {
                    process.Dispose();
                }

                onDone?.Invoke(result);
            }

            EditorApplication.update += Poll;
        }

        private static ProcessStartInfo Info(string file, string arguments, string workingDirectory)
        {
            var info = new ProcessStartInfo(file, arguments)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = string.IsNullOrEmpty(workingDirectory) ? ProjectPaths.Root : workingDirectory,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };

            // Unity mở từ Finder/Hub không mang PATH của shell — thêm các chỗ Homebrew / hệ thống hay đặt tool.
            var path = info.EnvironmentVariables["PATH"] ?? string.Empty;
            foreach (var extra in new[] { "/opt/homebrew/bin", "/usr/local/bin", "/usr/bin", "/bin" })
                if (path.IndexOf(extra, StringComparison.Ordinal) < 0)
                    path = path.Length == 0 ? extra : path + Path.PathSeparator + extra;
            info.EnvironmentVariables["PATH"] = path;
            info.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";
            return info;
        }

        /// <summary>Đường dẫn python3 dùng được; null = máy không có.</summary>
        internal static string FindPython()
        {
            if (Application.platform == RuntimePlatform.WindowsEditor)
            {
                foreach (var candidate in new[] { "py", "python", "python3" })
                    if (RunSync(candidate, "--version", null, 5000).Ok)
                        return candidate;
                return null;
            }

            foreach (var candidate in new[] { "/opt/homebrew/bin/python3", "/usr/local/bin/python3", "/usr/bin/python3" })
                if (File.Exists(candidate))
                    return candidate;

            return RunSync("python3", "--version", null, 5000).Ok ? "python3" : null;
        }

        /// <summary>
        ///     <paramref name="absolutePath" /> có bị git bỏ qua không. null = không biết (không có git /
        ///     không phải repo).
        /// </summary>
        internal static bool? IsGitIgnored(string absolutePath)
        {
            var result = RunSync("git", $"check-ignore -q \"{absolutePath}\"");
            if (result.StartError != null) return null;
            return result.ExitCode switch
            {
                0 => true,
                1 => false,
                _ => null,
            };
        }
    }

    /// <summary>GET bất đồng bộ không chặn Editor — dùng để kiểm link sheet còn mở công khai không.</summary>
    internal static class AsyncHttp
    {
        internal sealed class Response
        {
            internal bool Ok;
            internal long Code;
            internal string Body;
            internal string Error;
        }

        internal static void Get(string url, Action<Response> onDone, int timeoutSeconds = 20, string bearer = null)
        {
            var request = UnityWebRequest.Get(url);
            request.timeout = timeoutSeconds;
            if (!string.IsNullOrEmpty(bearer)) request.SetRequestHeader("Authorization", "Bearer " + bearer);
            request.SendWebRequest();

            void Poll()
            {
                if (!request.isDone) return;
                EditorApplication.update -= Poll;
                var response = new Response
                {
                    Ok = request.result == UnityWebRequest.Result.Success,
                    Code = request.responseCode,
                    Body = request.downloadHandler?.text,
                    Error = request.result == UnityWebRequest.Result.Success ? null : $"{request.result} - {request.error}",
                };
                request.Dispose();
                try
                {
                    onDone?.Invoke(response);
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                }
            }

            EditorApplication.update += Poll;
        }
    }
}
#endif
