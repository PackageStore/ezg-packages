using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace FigCraftBridge.Editor.Source
{
    public sealed class FigCraftCliException : Exception
    {
        public FigCraftCliException(string message, Exception inner = null) : base(message, inner) { }
    }

    /// <summary>
    ///     One <c>figcraft-cli mcp</c> process (headless, or <c>--attach &lt;port&gt;</c> to the running app) spoken to
    ///     over MCP JSON-RPC on stdio. Calls block, so they run off the main thread.
    /// </summary>
    public sealed class FigCraftCli : IDisposable
    {
        public const string ExecutableName = "figcraft-cli";
        const int MaxResponseBytes = 256 * 1024 * 1024;
        static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(20);

        readonly Process _process;
        readonly BlockingCollection<string> _lines = new BlockingCollection<string>();
        readonly StringBuilder _stderr = new StringBuilder();
        readonly object _gate = new object();
        int _nextId;

        FigCraftCli(Process process)
        {
            _process = process;
        }

        /// <summary>The CLI path: the setting when set, else <c>~/.local/bin</c>, else PATH; null when none exists.</summary>
        public static string Locate(string configured)
        {
            if (!string.IsNullOrWhiteSpace(configured))
            {
                var expanded = ExpandHome(configured.Trim());
                return File.Exists(expanded) ? expanded : null;
            }
            var exe = Environment.OSVersion.Platform == PlatformID.Win32NT ? ExecutableName + ".exe" : ExecutableName;
            var candidates = new List<string>
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "bin", exe),
                "/opt/homebrew/bin/" + exe,
                "/usr/local/bin/" + exe,
            };
            foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator))
                if (!string.IsNullOrEmpty(dir)) candidates.Add(Path.Combine(dir, exe));
            foreach (var candidate in candidates)
                if (File.Exists(candidate)) return candidate;
            return null;
        }

        public static string ExpandHome(string path) =>
            path.StartsWith("~/", StringComparison.Ordinal)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), path.Substring(2))
                : path;

        /// <param name="attachPort">Null: a headless session; a port: the app started with <c>--control &lt;port&gt;</c>.</param>
        public static FigCraftCli Start(string executable, int? attachPort)
        {
            var info = new ProcessStartInfo
            {
                FileName = executable,
                Arguments = attachPort.HasValue ? $"mcp --attach {attachPort.Value}" : "mcp",
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                CreateNoWindow = true,
            };
            Process process;
            try
            {
                process = Process.Start(info);
            }
            catch (Exception e)
            {
                throw new FigCraftCliException($"Could not start {executable}: {e.Message}", e);
            }
            if (process == null) throw new FigCraftCliException($"Could not start {executable}.");

            var cli = new FigCraftCli(process);
            process.ErrorDataReceived += (_, e) => { if (e.Data != null) lock (cli._stderr) cli._stderr.AppendLine(e.Data); };
            process.BeginErrorReadLine();
            var reader = new System.Threading.Thread(cli.ReadLoop) { IsBackground = true, Name = "figcraft-cli stdout" };
            reader.Start();
            try
            {
                cli.Request("initialize", new JObject
                {
                    ["protocolVersion"] = "2025-06-18",
                    ["capabilities"] = new JObject(),
                    ["clientInfo"] = new JObject { ["name"] = "com.ezg.figcraft-bridge", ["version"] = "1" },
                }, StartTimeout);
                cli.Notify("notifications/initialized");
            }
            catch
            {
                cli.Dispose();
                throw;
            }
            return cli;
        }

        /// <summary>Calls one MCP tool and returns its result: the JSON its text content holds, or that text.</summary>
        public JToken Call(string tool, JObject arguments, TimeSpan timeout)
        {
            var result = Request("tools/call", new JObject { ["name"] = tool, ["arguments"] = arguments ?? new JObject() }, timeout);
            var text = (result["content"] as JArray)?.First?["text"]?.ToString();
            if (result.Value<bool?>("isError") == true)
                throw new FigCraftCliException($"FigCraft {tool} failed: {text ?? result.ToString(Formatting.None)}");
            if (text == null) return result["structuredContent"] ?? result;
            var trimmed = text.TrimStart();
            if (trimmed.StartsWith("{", StringComparison.Ordinal) || trimmed.StartsWith("[", StringComparison.Ordinal))
            {
                try
                {
                    using var reader = new JsonTextReader(new StringReader(text)) { DateParseHandling = DateParseHandling.None };
                    return JToken.ReadFrom(reader);
                }
                catch (JsonException) { }
            }
            return new JValue(text);
        }

        JObject Request(string method, JObject parameters, TimeSpan timeout)
        {
            lock (_gate)
            {
                var id = ++_nextId;
                Send(new JObject { ["jsonrpc"] = "2.0", ["id"] = id, ["method"] = method, ["params"] = parameters });
                var deadline = DateTime.UtcNow + timeout;
                while (true)
                {
                    var left = deadline - DateTime.UtcNow;
                    if (left <= TimeSpan.Zero || !_lines.TryTake(out var line, left))
                    {
                        if (_lines.IsCompleted) throw new FigCraftCliException("figcraft-cli exited. " + Stderr());
                        throw new FigCraftCliException($"figcraft-cli did not answer {method} within {timeout.TotalSeconds:0} s.");
                    }
                    if (line == null) throw new FigCraftCliException("figcraft-cli exited. " + Stderr());
                    JObject message;
                    try
                    {
                        using var reader = new JsonTextReader(new StringReader(line)) { DateParseHandling = DateParseHandling.None };
                        message = JObject.Load(reader);
                    }
                    catch (JsonException)
                    {
                        continue;
                    }
                    if (message["id"] == null || message.Value<int>("id") != id) continue;
                    if (message["error"] != null)
                        throw new FigCraftCliException($"figcraft-cli {method}: {message["error"].ToString(Formatting.None)}");
                    return message["result"] as JObject ?? new JObject();
                }
            }
        }

        void Notify(string method)
        {
            lock (_gate) Send(new JObject { ["jsonrpc"] = "2.0", ["method"] = method });
        }

        void Send(JObject message)
        {
            if (_process.HasExited) throw new FigCraftCliException("figcraft-cli exited. " + Stderr());
            var bytes = new UTF8Encoding(false).GetBytes(message.ToString(Formatting.None) + "\n");
            var stream = _process.StandardInput.BaseStream;
            stream.Write(bytes, 0, bytes.Length);
            stream.Flush();
        }

        void ReadLoop()
        {
            try
            {
                var output = _process.StandardOutput;
                string line;
                while ((line = output.ReadLine()) != null)
                {
                    if (line.Length > MaxResponseBytes) continue;
                    _lines.Add(line);
                }
            }
            catch (Exception) { }
            finally
            {
                _lines.CompleteAdding();
            }
        }

        string Stderr()
        {
            lock (_stderr) return _stderr.ToString().Trim();
        }

        public void Dispose()
        {
            try
            {
                if (!_process.HasExited)
                {
                    _process.StandardInput.Close();
                    if (!_process.WaitForExit(2000)) _process.Kill();
                }
            }
            catch (Exception) { }
            _process.Dispose();
        }
    }
}
