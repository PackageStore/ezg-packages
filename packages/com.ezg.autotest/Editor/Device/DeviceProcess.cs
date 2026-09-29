using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Ezg.AutoTest.Editor
{
    /// <summary>Kết quả chạy một tool ngoài (adb, xcrun, gcloud…).</summary>
    public sealed class ToolResult
    {
        public int ExitCode = -1;
        public string StdOut = "";
        public string StdErr = "";
        public bool TimedOut;
        public bool StartFailed;
        public string Error;
        public double Seconds;
        public string CommandLine;

        public bool Ok => !StartFailed && !TimedOut && ExitCode == 0;

        /// <summary>stdout + stderr (đã trim).</summary>
        public string Combined
        {
            get
            {
                var a = (StdOut ?? "").Trim();
                var b = (StdErr ?? "").Trim();
                if (a.Length == 0) return b;
                return b.Length == 0 ? a : a + "\n" + b;
            }
        }

        /// <summary>Mô tả lỗi ngắn gọn để ghi vào issue.</summary>
        public string Describe()
        {
            if (StartFailed) return "Không chạy được lệnh: " + Error;
            if (TimedOut) return $"Quá thời gian ({Seconds:0}s) — lệnh bị huỷ.";
            var text = Combined;
            if (text.Length > DeviceProcess.MAX_DESCRIBE_LENGTH)
                text = text.Substring(0, DeviceProcess.MAX_DESCRIBE_LENGTH) + "…";
            return $"exit {ExitCode}" + (text.Length > 0 ? ": " + text : "");
        }
    }

    /// <summary>
    ///     Chạy tiến trình ngoài KHÔNG chặn main thread của Editor: đọc output bất đồng bộ, chờ bằng
    ///     <see cref="AutoTestClock" /> (Editor bơm qua EditorApplication.update), timeout / huỷ ⇒ kill tiến trình.
    ///     Dùng chung cho adb (Android), xcrun devicectl (iOS) và gcloud.
    /// </summary>
    public static class DeviceProcess
    {
        public const int MAX_DESCRIBE_LENGTH = 1200;
        const int MAX_CAPTURE_CHARS = 8 * 1024 * 1024;
        const double POLL_SECONDS = 0.05;

        /// <summary>Chờ đọc nốt output sau khi tiến trình thoát (tiến trình con giữ pipe thì bỏ qua sau mốc này).</summary>
        const double EOF_GRACE_SECONDS = 1.5;

        static readonly char[] QUOTE_TRIGGERS = { ' ', '\t', '\n', '\v', '"', '\'' };

        public static bool IsWindows => Application.platform == RuntimePlatform.WindowsEditor;
        public static bool IsMac => Application.platform == RuntimePlatform.OSXEditor;

        sealed class Flags
        {
            public volatile bool Exited;
            public volatile bool OutEof;
            public volatile bool ErrEof;
        }

        /// <summary>
        ///     Chạy <paramref name="file" /> với danh sách argument (tự quote). Không bao giờ ném exception trừ
        ///     OperationCanceledException khi <paramref name="token" /> bị huỷ (tiến trình đã bị kill trước đó).
        /// </summary>
        public static async Task<ToolResult> RunAsync(string file, IEnumerable<string> args, double timeoutSeconds,
            CancellationToken token, string workingDirectory = null, IDictionary<string, string> environment = null)
        {
            var arguments = JoinArgs(args);
            var result = new ToolResult { CommandLine = file + " " + arguments };
            token.ThrowIfCancellationRequested();

            var psi = new ProcessStartInfo
            {
                FileName = file,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            if (!string.IsNullOrEmpty(workingDirectory)) psi.WorkingDirectory = workingDirectory;
            if (environment != null)
                foreach (var pair in environment)
                    psi.EnvironmentVariables[pair.Key] = pair.Value;

            var stdout = new StringBuilder();
            var stderr = new StringBuilder();
            var flags = new Flags();
            var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
            process.OutputDataReceived += (_, e) => Append(stdout, e.Data, () => flags.OutEof = true);
            process.ErrorDataReceived += (_, e) => Append(stderr, e.Data, () => flags.ErrEof = true);
            process.Exited += (_, _) => flags.Exited = true;

            var start = AutoTestClock.Now;
            try
            {
                if (!process.Start())
                {
                    result.StartFailed = true;
                    result.Error = "Process.Start trả false";
                    process.Dispose();
                    return result;
                }
            }
            catch (Exception e)
            {
                result.StartFailed = true;
                result.Error = e.Message;
                process.Dispose();
                return result;
            }

            try
            {
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                // Đóng stdin ngay: tool đọc stdin (adb shell) nhận EOF thay vì treo chờ input.
                try
                {
                    process.StandardInput.Close();
                }
                catch
                {
                    // Tiến trình thoát quá nhanh — không sao.
                }

                var finished = await AutoTestClock.Until(() => flags.Exited || SafeHasExited(process),
                    timeoutSeconds, token, POLL_SECONDS);
                if (!finished)
                {
                    result.TimedOut = true;
                    Kill(process);
                }
                else
                {
                    await AutoTestClock.Until(() => flags.OutEof && flags.ErrEof, EOF_GRACE_SECONDS, token,
                        POLL_SECONDS);
                    result.ExitCode = SafeExitCode(process);
                }
            }
            catch (OperationCanceledException)
            {
                Kill(process);
                throw;
            }
            finally
            {
                result.Seconds = AutoTestClock.Now - start;
                lock (stdout)
                {
                    result.StdOut = stdout.ToString();
                }

                lock (stderr)
                {
                    result.StdErr = stderr.ToString();
                }

                SafeDispose(process);
            }

            return result;
        }

        static void Append(StringBuilder sb, string line, Action onEof)
        {
            if (line == null)
            {
                onEof();
                return;
            }

            lock (sb)
            {
                if (sb.Length < MAX_CAPTURE_CHARS) sb.Append(line).Append('\n');
            }
        }

        #region Quote argument

        /// <summary>Ghép argument thành chuỗi dòng lệnh (quote kiểu Windows — Mono/Unix cũng hiểu).</summary>
        public static string JoinArgs(IEnumerable<string> args)
        {
            if (args == null) return "";
            var sb = new StringBuilder();
            foreach (var a in args)
            {
                if (a == null) continue;
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(QuoteArg(a));
            }

            return sb.ToString();
        }

        /// <summary>Quote một argument theo luật CommandLineToArgvW (backslash trước dấu " được nhân đôi).</summary>
        public static string QuoteArg(string arg)
        {
            if (arg.Length > 0 && arg.IndexOfAny(QUOTE_TRIGGERS) < 0) return arg;
            var sb = new StringBuilder(arg.Length + 2);
            sb.Append('"');
            var backslashes = 0;
            foreach (var c in arg)
            {
                if (c == '\\')
                {
                    backslashes++;
                    continue;
                }

                if (c == '"')
                {
                    sb.Append('\\', backslashes * 2 + 1).Append('"');
                    backslashes = 0;
                    continue;
                }

                sb.Append('\\', backslashes).Append(c);
                backslashes = 0;
            }

            sb.Append('\\', backslashes * 2).Append('"');
            return sb.ToString();
        }

        /// <summary>Quote cho shell POSIX phía device (adb shell) — chỉ bọc khi có ký tự đặc biệt.</summary>
        public static string ShellQuote(string value)
        {
            if (string.IsNullOrEmpty(value)) return "''";
            var safe = true;
            foreach (var c in value)
                if (!(char.IsLetterOrDigit(c) || "_@%+=:,./-".IndexOf(c) >= 0))
                {
                    safe = false;
                    break;
                }

            return safe ? value : "'" + value.Replace("'", "'\\''") + "'";
        }

        #endregion

        #region Tìm tool

        /// <summary>Tìm file thực thi theo tên trong PATH (+ thư mục phổ biến mà Editor mở từ Hub không có trong PATH).</summary>
        public static string FindOnPath(string name)
        {
            var candidates = new List<string>();
            var path = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (var dir in path.Split(Path.PathSeparator))
                if (!string.IsNullOrWhiteSpace(dir))
                    candidates.Add(dir.Trim().Trim('"'));
            if (!IsWindows)
                candidates.AddRange(new[] { "/opt/homebrew/bin", "/usr/local/bin", "/usr/bin", "/bin" });

            var exts = IsWindows
                ? (Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.CMD;.BAT").Split(';')
                : new[] { "" };
            foreach (var dir in candidates)
            foreach (var ext in exts)
            {
                try
                {
                    var full = Path.Combine(dir, name + (Path.HasExtension(name) ? "" : ext.ToLowerInvariant()));
                    if (File.Exists(full)) return full;
                }
                catch (ArgumentException)
                {
                    // Thư mục PATH chứa ký tự không hợp lệ — bỏ qua.
                }
            }

            return null;
        }

        /// <summary>Tên file thực thi theo hệ điều hành (thêm .exe trên Windows).</summary>
        public static string Exe(string name)
        {
            return IsWindows ? name + ".exe" : name;
        }

        public static string HomeDirectory()
        {
            var home = Environment.GetEnvironmentVariable("HOME");
            if (string.IsNullOrEmpty(home)) home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return home ?? "";
        }

        #endregion

        #region Tiện ích Process

        internal static bool SafeHasExited(Process p)
        {
            try
            {
                return p.HasExited;
            }
            catch
            {
                return true;
            }
        }

        static int SafeExitCode(Process p)
        {
            try
            {
                return p.ExitCode;
            }
            catch
            {
                return -1;
            }
        }

        internal static void Kill(Process p)
        {
            try
            {
                if (!p.HasExited) p.Kill();
            }
            catch
            {
                // Đã thoát / không có quyền — bỏ qua.
            }
        }

        internal static void SafeDispose(Process p)
        {
            try
            {
                p.Dispose();
            }
            catch
            {
                // Bỏ qua.
            }
        }

        #endregion
    }

    /// <summary>
    ///     Tiến trình chạy dài (vd <c>adb logcat</c>): từng dòng output được đẩy vào hàng đợi thread-safe cho main
    ///     thread lấy dần, đồng thời (tuỳ chọn) ghi thẳng ra file để không giữ log dài trong RAM. Dispose ⇒ kill.
    /// </summary>
    public sealed class StreamingProcess : IDisposable
    {
        const long DEFAULT_MAX_FILE_BYTES = 64L * 1024 * 1024;
        const int MAX_QUEUE = 200000;

        readonly ConcurrentQueue<string> _lines = new();
        readonly object _fileLock = new();
        Process _process;
        StreamWriter _writer;
        long _written;
        readonly long _maxFileBytes;
        volatile bool _exited;
        bool _disposed;

        /// <summary>Số dòng đã nhận (kể cả đã lấy ra).</summary>
        public long LineCount;

        public bool HasExited => _exited || _process == null;
        public bool Truncated { get; private set; }
        public string Error { get; private set; }
        public string LogFilePath { get; }

        StreamingProcess(string logFilePath, long maxFileBytes)
        {
            LogFilePath = logFilePath;
            _maxFileBytes = maxFileBytes;
        }

        /// <summary>Khởi chạy. Lỗi khởi chạy ghi vào <see cref="Error" /> (HasExited = true), không ném exception.</summary>
        public static StreamingProcess Start(string file, IEnumerable<string> args, string logFilePath = null,
            long maxFileBytes = DEFAULT_MAX_FILE_BYTES)
        {
            var sp = new StreamingProcess(logFilePath, maxFileBytes);
            try
            {
                if (!string.IsNullOrEmpty(logFilePath))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(logFilePath)!);
                    sp._writer = new StreamWriter(logFilePath, false, new UTF8Encoding(false));
                }

                var psi = new ProcessStartInfo
                {
                    FileName = file,
                    Arguments = DeviceProcess.JoinArgs(args),
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    RedirectStandardInput = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };
                var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
                p.OutputDataReceived += (_, e) => sp.OnLine(e.Data);
                p.ErrorDataReceived += (_, e) => sp.OnLine(e.Data);
                p.Exited += (_, _) => sp._exited = true;
                if (!p.Start())
                {
                    sp.Error = "Process.Start trả false";
                    sp._exited = true;
                    return sp;
                }

                sp._process = p;
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
                try
                {
                    p.StandardInput.Close();
                }
                catch
                {
                    // Bỏ qua.
                }
            }
            catch (Exception e)
            {
                sp.Error = e.Message;
                sp._exited = true;
            }

            return sp;
        }

        void OnLine(string line)
        {
            if (line == null || _disposed) return;
            line = line.TrimEnd('\r');
            Interlocked.Increment(ref LineCount);
            if (_lines.Count < MAX_QUEUE) _lines.Enqueue(line);
            if (_writer == null) return;
            lock (_fileLock)
            {
                if (_writer == null) return;
                if (_written > _maxFileBytes)
                {
                    if (!Truncated)
                    {
                        Truncated = true;
                        _writer.WriteLine($"… (log vượt {_maxFileBytes / (1024 * 1024)} MB — phần sau bị lược bỏ)");
                    }

                    return;
                }

                _writer.WriteLine(line);
                _written += line.Length + 1;
            }
        }

        /// <summary>Lấy một dòng (main thread gọi trong vòng lặp).</summary>
        public bool TryDequeue(out string line)
        {
            return _lines.TryDequeue(out line);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_process != null)
            {
                DeviceProcess.Kill(_process);
                DeviceProcess.SafeDispose(_process);
                _process = null;
            }

            lock (_fileLock)
            {
                try
                {
                    _writer?.Flush();
                    _writer?.Dispose();
                }
                catch
                {
                    // Bỏ qua.
                }

                _writer = null;
            }
        }
    }
}
