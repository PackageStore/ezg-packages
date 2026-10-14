// EZG QA Probe — cầu nối để skill /auto-test-device (qadevice.py trên máy dev) quan sát + điều khiển game đang
// chạy trên thiết bị như một QA: cây UI kèm toạ độ, audit visual, chụp màn, chạm/kéo qua EventSystem, đọc state /
// gọi hàm cheat bằng reflection, log Unity kèm stack, context + gửi bug qua Bug Logger.
//
// FILE DO TOOL QUẢN LÝ — đừng sửa tay. Cập nhật: `python3 .claude/skills/auto-test-device/scripts/qadevice.py probe-install --force`.
//
// An toàn:
//  * Không làm gì cho tới khi được kích hoạt bằng trigger mang token ngẫu nhiên: intent extra `ezg_qa_probe` (Android),
//    argument `-ezgQaProbe` / biến môi trường `EZG_QA_PROBE` (iOS), hoặc file `<persistentDataPath>/ezg_qa_probe.txt`.
//  * Mọi request phải mang đúng token (header X-QA-Token). Android chỉ nghe 127.0.0.1 (vào bằng `adb forward`).
//  * Build release: chỉ phục vụ khi "cổng" mở — Editor / Development Build / define EZG_QA_PROBE, hoặc giá trị bool
//    static khai trong Resources/EzgQaProbeConfig.json (`releaseGate`, vd cờ cheat của game) đang true.
//  * Define EZG_QA_PROBE_DISABLED loại toàn bộ code này khỏi build.
//
// Không phụ thuộc UniTask/TMP/BugLogger (dùng Awaitable của Unity 6 + reflection) để chạy được ở mọi project.

#if !EZG_QA_PROBE_DISABLED
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Profiling;
using UnityEngine.SceneManagement;
using UnityEngine.Scripting;
using UnityEngine.UI;
using Object = UnityEngine.Object;

[assembly: AlwaysLinkAssembly]

namespace Ezg.QaProbe
{
    /// <summary>Điểm vào của probe: kích hoạt theo trigger, mở server HTTP nhỏ, chuyển request về main thread.</summary>
    [Preserve]
    public static class EzgQaProbe
    {
        #region Fields

        /// <summary>Phiên bản probe — qadevice.py so để biết bản trong project có cũ không.</summary>
        public const string VERSION = "1.0.0";

        public const int DEFAULT_PORT = 7531;
        public const string LOG_TAG = "[EzgQaProbe]";
        private const int PORT_SPAN = 5;
        private const string EXTRA_TOKEN = "ezg_qa_probe";
        private const string EXTRA_PORT = "ezg_qa_port";
        private const string ARG_TOKEN = "-ezgQaProbe";
        private const string ARG_PORT = "-ezgQaPort";
        private const string ENV_TOKEN = "EZG_QA_PROBE";
        private const string ENV_PORT = "EZG_QA_PORT";
        private const string TRIGGER_FILE = "ezg_qa_probe.txt";
        private const string CONFIG_RESOURCE = "EzgQaProbeConfig";
        private const int JOB_TIMEOUT_MS = 25000;

        internal static readonly ConcurrentQueue<ProbeJob> Jobs = new ConcurrentQueue<ProbeJob>();
        internal static ProbeConfig Config = new ProbeConfig();

        private static ProbeServer _server;
        private static string _token;
        private static ProbeHost _host;

        /// <summary>Server đang nghe.</summary>
        public static bool IsListening => _server != null;

        /// <summary>Cổng đang nghe (0 khi tắt).</summary>
        public static int Port { get; private set; }

        /// <summary>realtimeSinceStartup lúc bật probe.</summary>
        internal static float StartTime { get; private set; }

        #endregion

        #region Initialize

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Deactivate();
            Application.focusChanged -= OnFocusChanged;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Boot()
        {
            Application.focusChanged -= OnFocusChanged;
            Application.focusChanged += OnFocusChanged;
            TryActivateFromTrigger("launch");
        }

        // Host đẩy file trigger / gửi intent mới khi game đang chạy → app lấy lại focus là nhận.
        private static void OnFocusChanged(bool focused)
        {
            if (focused && _server == null) TryActivateFromTrigger("focus");
        }

        #endregion

        #region Public

        /// <summary>Bật probe bằng tay (menu debug trong game, test Editor). Trả true khi đã nghe.</summary>
        public static bool Activate(string token, int port = DEFAULT_PORT, string source = "api")
        {
            if (_server != null) return true;
            if (!IsValidToken(token))
            {
                Debug.LogWarning(LOG_TAG + " token không hợp lệ (cần 16-64 ký tự chữ/số)");
                return false;
            }

            LoadConfig();
            _token = token;
            StartTime = Time.realtimeSinceStartup;
            EnsureHost();
            ProbeLog.Start();
            var address = BindAddress();
            Exception last = null;
            for (var p = port; p < port + PORT_SPAN; p++)
            {
                try
                {
                    var server = new ProbeServer(address, p, Dispatch);
                    server.Start();
                    _server = server;
                    Port = p;
                    break;
                }
                catch (Exception e)
                {
                    last = e;
                }
            }

            if (_server == null)
            {
                Debug.LogWarning(LOG_TAG + " không mở được cổng " + port + ": " + last?.Message);
                return false;
            }

            ProbeGate.Refresh();
            Debug.Log(string.Format(CultureInfo.InvariantCulture, "{0} listening {1}:{2} gate={3} v{4} source={5}", LOG_TAG,
                address, Port, ProbeGate.IsOpen ? "open" : "closed", VERSION, source));
            return true;
        }

        /// <summary>Tắt probe, đóng cổng.</summary>
        public static void Deactivate()
        {
            _server?.Stop();
            _server = null;
            Port = 0;
            _token = null;
            while (Jobs.TryDequeue(out var job)) job.Finish(ProbeResponse.Error(503, "stopped", "probe đã tắt"));
        }

        #endregion

        #region Private

        private static void TryActivateFromTrigger(string source)
        {
            try
            {
                if (TryReadTrigger(out var token, out var port, out var from)) Activate(token, port, source + ":" + from);
            }
            catch (Exception e)
            {
                Debug.LogWarning(LOG_TAG + " đọc trigger lỗi: " + e.Message);
            }
        }

        private static bool TryReadTrigger(out string token, out int port, out string from)
        {
            token = null;
            port = 0;
            from = "";
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                {
                    if (activity != null)
                    {
                        using (var intent = activity.Call<AndroidJavaObject>("getIntent"))
                        {
                            if (intent != null)
                            {
                                token = intent.Call<string>("getStringExtra", EXTRA_TOKEN);
                                port = intent.Call<int>("getIntExtra", EXTRA_PORT, 0);
                                from = "intent";
                            }
                        }
                    }
                }
            }
            catch (Exception)
            {
                // Không đọc được intent (activity chưa sẵn) — thử nguồn khác.
            }
#endif
            if (!IsValidToken(token))
            {
                var args = SafeArgs();
                for (var i = 0; i < args.Length - 1; i++)
                {
                    if (args[i] == ARG_TOKEN) token = args[i + 1];
                    if (args[i] == ARG_PORT) int.TryParse(args[i + 1], out port);
                }

                from = "args";
            }

            if (!IsValidToken(token))
            {
                token = SafeEnv(ENV_TOKEN);
                int.TryParse(SafeEnv(ENV_PORT), out port);
                from = "env";
            }

            if (!IsValidToken(token))
            {
                var path = Path.Combine(Application.persistentDataPath, TRIGGER_FILE);
                if (File.Exists(path))
                {
                    var parts = File.ReadAllText(path).Trim().Split(':');
                    token = parts[0].Trim();
                    if (parts.Length > 1) int.TryParse(parts[1].Trim(), out port);
                    from = "file";
                }
            }

            if (port <= 0) port = DEFAULT_PORT;
            return IsValidToken(token);
        }

        private static bool IsValidToken(string token)
        {
            if (string.IsNullOrEmpty(token) || token.Length < 16 || token.Length > 64) return false;
            foreach (var c in token)
            {
                if (!char.IsLetterOrDigit(c) || c > 127) return false;
            }

            return true;
        }

        private static string[] SafeArgs()
        {
            try
            {
                return Environment.GetCommandLineArgs() ?? Array.Empty<string>();
            }
            catch (Exception)
            {
                return Array.Empty<string>();
            }
        }

        private static string SafeEnv(string name)
        {
            try
            {
                return Environment.GetEnvironmentVariable(name);
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static IPAddress BindAddress()
        {
            // iPhone thật: host vào qua tunnel CoreDevice (IPv6) → nghe mọi địa chỉ, token bảo vệ.
            // Android/Simulator/Editor: chỉ loopback (Android vào bằng adb forward).
            return Application.platform == RuntimePlatform.IPhonePlayer ? IPAddress.IPv6Any : IPAddress.Loopback;
        }

        private static void LoadConfig()
        {
            try
            {
                var asset = Resources.Load<TextAsset>(CONFIG_RESOURCE);
                if (asset != null && !string.IsNullOrWhiteSpace(asset.text)) Config = JsonUtility.FromJson<ProbeConfig>(asset.text) ?? new ProbeConfig();
            }
            catch (Exception e)
            {
                Debug.LogWarning(LOG_TAG + " config hỏng: " + e.Message);
            }
        }

        private static void EnsureHost()
        {
            if (_host != null) return;
            var go = new GameObject("[EzgQaProbe]") { hideFlags = HideFlags.HideInHierarchy | HideFlags.DontSave };
            Object.DontDestroyOnLoad(go);
            _host = go.AddComponent<ProbeHost>();
        }

        // Chạy trên thread socket: kiểm token/cổng rồi đưa job về main thread, chờ kết quả.
        private static ProbeResponse Dispatch(ProbeRequest req)
        {
            var token = req.Header("x-qa-token") ?? req.Get("token");
            if (string.IsNullOrEmpty(_token) || !FixedTimeEquals(token, _token)) return ProbeResponse.Error(401, "unauthorized", "sai token");
            if (req.Path == "/logs") return ProbeLog.Handle(req);
            if (req.Path != "/ping" && !ProbeGate.IsOpen)
                return ProbeResponse.Error(423, "gate_closed", ProbeGate.Reason);

            var job = new ProbeJob(req);
            Jobs.Enqueue(job);
            if (!job.Done.Wait(JOB_TIMEOUT_MS))
                return ProbeResponse.Error(504, "main_thread_timeout", "game không xử lý request trong 25s — main thread treo hoặc app đang ở nền");
            return job.Response;
        }

        private static bool FixedTimeEquals(string a, string b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            var diff = 0;
            for (var i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
            return diff == 0;
        }

        #endregion
    }

    /// <summary>Cấu hình đọc từ Resources/EzgQaProbeConfig.json.</summary>
    [Serializable]
    [Preserve]
    internal sealed class ProbeConfig
    {
        /// <summary>Đường dẫn bool static (Namespace.Type.member) mở probe ở build release, vd cờ cheat.</summary>
        public string releaseGate = "";

        public int port = EzgQaProbe.DEFAULT_PORT;
    }

    /// <summary>Cổng cho phép phục vụ request (đánh giá trên main thread, cache cho thread socket).</summary>
    [Preserve]
    internal static class ProbeGate
    {
        private static volatile bool _open;
        private static volatile string _reason = "chưa đánh giá";

        public static bool IsOpen => _open;
        public static string Reason => _reason;

        public static void Refresh()
        {
            if (Application.isEditor)
            {
                Set(true, "editor");
                return;
            }

            if (Debug.isDebugBuild)
            {
                Set(true, "development build");
                return;
            }
#if EZG_QA_PROBE
            Set(true, "define EZG_QA_PROBE");
            return;
#else
            var gate = EzgQaProbe.Config?.releaseGate;
            if (string.IsNullOrEmpty(gate))
            {
                Set(false, "build release không khai releaseGate trong EzgQaProbeConfig — dùng development build");
                return;
            }

            var ok = ProbeReflect.TryEval(gate, out var value, out _) && value is bool b && b;
            Set(ok, ok ? gate + "=true" : gate + "=false — bật cheat trên máy để mở probe");
#endif
        }

        private static void Set(bool open, string reason)
        {
            _open = open;
            _reason = reason;
        }
    }

    /// <summary>Một request chờ main thread xử lý.</summary>
    internal sealed class ProbeJob
    {
        public readonly ProbeRequest Request;
        public readonly ManualResetEventSlim Done = new ManualResetEventSlim(false);
        public ProbeResponse Response;

        public ProbeJob(ProbeRequest request)
        {
            Request = request;
        }

        public void Finish(ProbeResponse response)
        {
            Response = response;
            Done.Set();
        }
    }

    /// <summary>GameObject ẩn: bơm job ở main thread, đo FPS, làm mới cổng.</summary>
    [AddComponentMenu("")]
    [DefaultExecutionOrder(-32000)]
    [Preserve]
    internal sealed class ProbeHost : MonoBehaviour
    {
        private const float GATE_REFRESH_SECONDS = 0.5f;
        private const int JOBS_PER_FRAME = 8;
        private const int FRAME_HISTORY = 300;

        private static readonly float[] _frames = new float[FRAME_HISTORY];
        private static int _frameIndex;
        private static int _frameCount;
        private float _nextGate;

        public static float Fps { get; private set; }
        public static float WorstMs { get; private set; }

        private void Update()
        {
            var dt = Time.unscaledDeltaTime;
            _frames[_frameIndex] = dt;
            _frameIndex = (_frameIndex + 1) % FRAME_HISTORY;
            _frameCount = Mathf.Min(_frameCount + 1, FRAME_HISTORY);
            ComputeFrameStats();

            if (Time.unscaledTime >= _nextGate)
            {
                _nextGate = Time.unscaledTime + GATE_REFRESH_SECONDS;
                ProbeGate.Refresh();
            }

            for (var i = 0; i < JOBS_PER_FRAME && EzgQaProbe.Jobs.TryDequeue(out var job); i++) _ = Run(job);
        }

        private void OnDestroy()
        {
            EzgQaProbe.Deactivate();
        }

        private static async Awaitable Run(ProbeJob job)
        {
            ProbeResponse response;
            try
            {
                response = await ProbeHandlers.Handle(job.Request);
            }
            catch (Exception e)
            {
                response = ProbeResponse.Error(500, "exception", e.GetType().Name + ": " + e.Message);
            }

            job.Finish(response);
        }

        private static void ComputeFrameStats()
        {
            float sum = 0f, worst = 0f, window = 0f;
            var n = 0;
            for (var k = 0; k < _frameCount; k++)
            {
                var dt = _frames[(_frameIndex - 1 - k + FRAME_HISTORY) % FRAME_HISTORY];
                if (window < 1f)
                {
                    sum += dt;
                    n++;
                }

                window += dt;
                if (window <= 5f && dt > worst) worst = dt;
            }

            Fps = sum > 0f ? n / sum : 0f;
            WorstMs = worst * 1000f;
        }
    }

    /// <summary>Bộ đệm log Unity (thread-safe) cho /logs — nguồn log chính trên iOS.</summary>
    [Preserve]
    internal static class ProbeLog
    {
        private const int CAPACITY = 600;
        private const int MAX_MESSAGE = 2000;
        private const int MAX_STACK = 4000;
        private static readonly object _lock = new object();
        private static readonly Queue<string> _entries = new Queue<string>();
        private static long _cursor;
        private static bool _started;

        public static void Start()
        {
            if (_started) return;
            _started = true;
            Application.logMessageReceivedThreaded += OnLog;
        }

        private static void OnLog(string message, string stack, LogType type)
        {
            if (message != null && message.StartsWith(EzgQaProbe.LOG_TAG, StringComparison.Ordinal)) return;
            var w = new ProbeJson();
            lock (_lock)
            {
                _cursor++;
                w.BeginObject().Prop("i", _cursor).Prop("t", DateTime.UtcNow.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture))
                    .Prop("type", type.ToString()).Prop("msg", Clip(message, MAX_MESSAGE))
                    .Prop("stack", type == LogType.Log || type == LogType.Warning ? "" : Clip(stack, MAX_STACK)).EndObject();
                _entries.Enqueue(w.ToString());
                while (_entries.Count > CAPACITY) _entries.Dequeue();
            }
        }

        /// <summary>GET /logs?since=N — chạy thẳng trên thread socket (không đụng Unity API).</summary>
        public static ProbeResponse Handle(ProbeRequest r)
        {
            long.TryParse(r.Get("since") ?? "0", out var since);
            var arr = Since(since, out var cursor);
            return ProbeResponse.Json(200, "{\"cursor\":" + cursor + ",\"entries\":" + arr + "}");
        }

        public static string Since(long since, out long cursor)
        {
            var sb = new StringBuilder("[");
            lock (_lock)
            {
                cursor = _cursor;
                var first = _cursor - _entries.Count + 1;
                var idx = first;
                var any = false;
                foreach (var e in _entries)
                {
                    if (idx > since)
                    {
                        if (any) sb.Append(',');
                        sb.Append(e);
                        any = true;
                    }

                    idx++;
                }
            }

            return sb.Append(']').ToString();
        }

        private static string Clip(string s, int n)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Length <= n ? s : s.Substring(0, n);
        }
    }

    /// <summary>Các endpoint. Chạy trên main thread (trừ /logs).</summary>
    [Preserve]
    internal static class ProbeHandlers
    {
        private const int MAX_ELEMENTS = 400;
        private const int MAX_ELEMENTS_ALL = 900;
        private const int MAX_WORLD = 60;
        private const int MAX_COLLIDERS = 6000;
        private static readonly List<RaycastResult> _hits = new List<RaycastResult>();
        private static readonly Dictionary<Type, bool> _screenTypes = new Dictionary<Type, bool>();
        private static readonly Dictionary<Type, bool> _clickTypes = new Dictionary<Type, bool>();

        public static async Awaitable<ProbeResponse> Handle(ProbeRequest r)
        {
            switch (r.Path)
            {
                case "/ping": return Ping();
                case "/ui": return Ui(r.GetInt("all", 0) == 1);
                case "/find": return Find(r.Get("q") ?? "");
                case "/shot": return await Shot(r.Get("fmt") ?? "jpg", r.GetInt("q", 80), r.GetInt("max", 0));
                case "/tap": return await Tap(r.GetFloat("x"), r.GetFloat("y"), r.GetInt("hold", 0));
                case "/drag": return await Drag(r.GetFloat("x1"), r.GetFloat("y1"), r.GetFloat("x2"), r.GetFloat("y2"), r.GetInt("ms", 600), r.GetInt("hold", 0));
                case "/text": return SetText(r.Get("text") ?? "", r.Get("path"), r.GetInt("submit", 0) == 1);
                case "/get": return GetValue(r.Get("path") ?? "");
                case "/call": return Call(r.Get("method") ?? "", r.GetAll("args"));
                case "/types": return Types(r.Get("q") ?? "");
                case "/timescale": return TimeScale(r.GetFloat("value", 1f));
                case "/perf": return Perf();
                case "/context": return await Context(r.GetInt("save", 0) == 1);
                case "/bug": return await Bug(r);
                default: return ProbeResponse.Error(404, "not_found", "endpoint không có: " + r.Path);
            }
        }

        #region ping / perf

        private static ProbeResponse Ping()
        {
            var w = new ProbeJson().BeginObject()
                .Prop("ok", true).Prop("version", EzgQaProbe.VERSION)
                .Prop("gate", ProbeGate.IsOpen ? "open" : "closed").Prop("gateReason", ProbeGate.Reason)
                .Prop("scene", SceneManager.GetActiveScene().name)
                .Prop("timeScale", Time.timeScale).Prop("fps", Math.Round(ProbeHost.Fps, 1))
                .Prop("uptime", Math.Round(Time.realtimeSinceStartup - EzgQaProbe.StartTime, 1))
                .Prop("bugLogger", ProbeReflect.FindType("Ezg.BugLogger.BugLogger") != null);
            w.BeginObject("app").Prop("name", Application.productName).Prop("version", Application.version)
                .Prop("id", Application.identifier).Prop("unity", Application.unityVersion)
                .Prop("platform", Application.platform.ToString()).Prop("debug", Debug.isDebugBuild).EndObject();
            WriteScreen(w);
            return ProbeResponse.Json(200, w.EndObject().ToString());
        }

        private static void WriteScreen(ProbeJson w)
        {
            var safe = Screen.safeArea;
            w.BeginObject("screen").Prop("w", Screen.width).Prop("h", Screen.height).Prop("dpi", Screen.dpi)
                .Prop("orientation", Screen.orientation.ToString());
            w.BeginArray("safe").Value(safe.x).Value(Screen.height - safe.yMax).Value(safe.width).Value(safe.height).EndArray();
            w.EndObject();
        }

        private static ProbeResponse Perf()
        {
            var w = new ProbeJson().BeginObject()
                .Prop("fps", Math.Round(ProbeHost.Fps, 1)).Prop("worstMs", Math.Round(ProbeHost.WorstMs, 1))
                .Prop("monoMb", Math.Round(Profiler.GetMonoUsedSizeLong() / 1048576.0, 1))
                .Prop("allocatedMb", Math.Round(Profiler.GetTotalAllocatedMemoryLong() / 1048576.0, 1))
                .Prop("reservedMb", Math.Round(Profiler.GetTotalReservedMemoryLong() / 1048576.0, 1))
                .Prop("gc0", GC.CollectionCount(0)).Prop("timeScale", Time.timeScale)
                .Prop("targetFps", Application.targetFrameRate);
            return ProbeResponse.Json(200, w.EndObject().ToString());
        }

        private static ProbeResponse TimeScale(float value)
        {
            var prev = Time.timeScale;
            Time.timeScale = Mathf.Clamp(value, 0f, 100f);
            return ProbeResponse.Json(200, new ProbeJson().BeginObject().Prop("previous", prev).Prop("value", Time.timeScale).EndObject().ToString());
        }

        #endregion

        #region UI scan

        private sealed class UiItem
        {
            public int Id;
            public GameObject Go;
            public string Path;
            public string Kind;
            public string Text;
            public string Label;
            public Rect Rect;
            public bool Interactable;
            public string BlockedBy;
            public Vector2? TapPoint;
            public List<string> Flags;
            public string Missing;
            public string Layer;
            public bool Occluded;
            public Rect? Ink;
            public string Container;
            public bool Interesting;
        }

        private static ProbeResponse Ui(bool includeAll)
        {
            var items = ScanUi(includeAll);
            var w = new ProbeJson().BeginObject().Prop("scene", SceneManager.GetActiveScene().name);
            WriteScreen(w);
            w.BeginArray("screens");
            foreach (var s in ActiveScreens()) w.Value(s);
            w.EndArray();
            w.BeginArray("elements");
            foreach (var it in items) WriteItem(w, it);
            w.EndArray();
            w.BeginArray("world");
            ScanWorld(w, null, MAX_WORLD);
            w.EndArray();
            return ProbeResponse.Json(200, w.EndObject().ToString());
        }

        private static ProbeResponse Find(string q)
        {
            var items = ScanUi(true);
            var w = new ProbeJson().BeginObject();
            w.BeginArray("elements");
            foreach (var it in items)
            {
                if (Contains(it.Path, q) || Contains(it.Text, q) || Contains(it.Label, q)) WriteItem(w, it);
            }

            w.EndArray();
            w.BeginArray("world");
            ScanWorld(w, q, MAX_WORLD);
            w.EndArray();
            return ProbeResponse.Json(200, w.EndObject().ToString());
        }

        private static bool Contains(string hay, string q)
        {
            return !string.IsNullOrEmpty(hay) && hay.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static void WriteItem(ProbeJson w, UiItem it)
        {
            w.BeginObject().Prop("id", it.Id).Prop("name", it.Go.name).Prop("path", it.Path).Prop("kind", it.Kind);
            if (!string.IsNullOrEmpty(it.Text)) w.Prop("text", it.Text);
            if (!string.IsNullOrEmpty(it.Label)) w.Prop("label", it.Label);
            w.BeginArray("rect").Value(Mathf.Round(it.Rect.x)).Value(Mathf.Round(it.Rect.y)).Value(Mathf.Round(it.Rect.width))
                .Value(Mathf.Round(it.Rect.height)).EndArray();
            w.Prop("interactable", it.Interactable);
            if (it.BlockedBy != null) w.Prop("blockedBy", it.BlockedBy);
            if (it.TapPoint.HasValue) w.BeginArray("tap").Value(Mathf.Round(it.TapPoint.Value.x)).Value(Mathf.Round(it.TapPoint.Value.y)).EndArray();
            if (it.Flags != null && it.Flags.Count > 0)
            {
                w.BeginArray("flags");
                foreach (var f in it.Flags) w.Value(f);
                w.EndArray();
            }

            if (!string.IsNullOrEmpty(it.Missing)) w.Prop("missing", it.Missing);
            if (it.Layer != null) w.Prop("layer", it.Layer);
            if (it.Occluded) w.Prop("occluded", true);
            if (it.Ink.HasValue)
            {
                var k = it.Ink.Value;
                w.BeginArray("ink").Value(Mathf.Round(k.x)).Value(Mathf.Round(k.y)).Value(Mathf.Round(k.width)).Value(Mathf.Round(k.height)).EndArray();
            }

            if (it.Container != null) w.Prop("container", it.Container);
            w.EndObject();
        }

        private static List<UiItem> ScanUi(bool includeAll)
        {
            var result = new List<UiItem>();
            var cap = includeAll ? MAX_ELEMENTS_ALL : MAX_ELEMENTS;
            var seq = 0;
            var canvases = Object.FindObjectsByType<Canvas>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
                .Where(c => c != null && c.isRootCanvas && c.isActiveAndEnabled)
                .OrderByDescending(c => c.renderMode == RenderMode.ScreenSpaceOverlay)
                .ThenByDescending(c => SortingLayer.GetLayerValueFromID(c.sortingLayerID))
                .ThenByDescending(c => c.sortingOrder)
                .ToList();
            var screen = new Rect(0, 0, Screen.width, Screen.height);
            foreach (var canvas in canvases)
            {
                var cam = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera != null ? canvas.worldCamera : Camera.main;
                foreach (var rt in canvas.GetComponentsInChildren<RectTransform>(false))
                {
                    if (result.Count >= cap) return result;
                    // Đánh số trên toàn bộ phần tử nhìn thấy (kể cả khi không trả về) để id ổn định giữa /ui và /ui?all=1.
                    var item = Inspect(rt, cam, screen);
                    if (item == null) continue;
                    item.Id = ++seq;
                    if (includeAll || item.Interesting) result.Add(item);
                }
            }

            return result;
        }

        private static UiItem Inspect(RectTransform rt, Camera cam, Rect screen)
        {
            var go = rt.gameObject;
            go.TryGetComponent(out Selectable selectable);
            go.TryGetComponent(out ScrollRect scroll);
            go.TryGetComponent(out Graphic graphic);
            var text = ProbeText.Get(graphic);
            var isText = text != null;
            var clickable = selectable == null && scroll == null && HasPointerHandler(go);
            string kind;
            if (selectable != null) kind = SelectableKind(selectable);
            else if (scroll != null) kind = "ScrollRect";
            else if (isText) kind = "Text";
            else if (clickable) kind = "Clickable";
            else if (graphic != null) kind = graphic is RawImage ? "RawImage" : "Image";
            else return null;

            if (kind == "Text" && string.IsNullOrWhiteSpace(text)) return null;
            // Nút hay dùng Image trong suốt (alpha 0) làm vùng chạm → phần tử tương tác chỉ xét alpha CanvasGroup.
            var groupAlpha = GroupAlpha(rt);
            var alpha = groupAlpha * (graphic != null ? graphic.color.a : 1f);
            if ((selectable != null || scroll != null || clickable ? groupAlpha : alpha) < 0.02f) return null;
            if (graphic != null && graphic.canvasRenderer != null && graphic.canvasRenderer.cull) return null;

            var rect = ScreenRect(rt, cam);
            rect = ClipByMasks(rt, rect, cam);
            rect = Intersect(rect, new Rect(-screen.width, -screen.height, screen.width * 3, screen.height * 3));
            if (rect.width * rect.height < 4f) return null;

            List<string> flags = null;
            string missing = null;
            if (isText) ProbeText.Audit(graphic, text, ref flags, ref missing);
            if (graphic is Image img && img.sprite == null && img.color.a * alpha > 0.5f && img.color.r > 0.95f && img.color.g > 0.95f &&
                img.color.b > 0.95f && rect.width > 24f && rect.height > 24f && !go.TryGetComponent(out Mask _) &&
                rt.childCount == 0 && !go.TryGetComponent(out IMeshModifier _) && img.material == img.defaultMaterial)
                AddFlag(ref flags, "noSprite");
            if (graphic != null)
            {
                var mat = graphic.materialForRendering;
                if (mat != null && mat.shader != null && (!mat.shader.isSupported || mat.shader.name == "Hidden/InternalErrorShader"))
                    AddFlag(ref flags, "missingMaterial");
            }

            var interesting = selectable != null || scroll != null || isText || clickable || flags != null;
            var item = new UiItem
            {
                Interesting = interesting,
                Go = go, Path = PathOf(rt), Kind = kind, Text = isText ? Clip(text, 300) : null, Rect = rect, Flags = flags, Missing = missing,
                Interactable = selectable != null ? selectable.IsInteractable() && selectable.isActiveAndEnabled : clickable
            };
            if (selectable != null || clickable) item.Label = ChildLabel(rt);
            item.Layer = LayerOf(rt);
            if (item.Interactable) HitTest(item, screen);
            if (isText && ProbeText.TryInk(graphic, rt, cam, out var ink))
            {
                item.Ink = ink;
                var box = ContainerOf(rt, cam, out var containerName);
                const float slack = 3f;
                if (box.HasValue && (ink.xMin < box.Value.xMin - slack || ink.xMax > box.Value.xMax + slack ||
                                     ink.yMin < box.Value.yMin - slack || ink.yMax > box.Value.yMax + slack))
                {
                    AddFlag(ref item.Flags, "outsideContainer");
                    item.Container = containerName;
                }
            }

            return item;
        }

        // Lớp hiển thị: màn (FeatureBaseController…) gần nhất, không có thì canvas override sorting / canvas gốc.
        private static string LayerOf(Transform t)
        {
            Transform canvasLayer = null;
            for (var cur = t; cur != null; cur = cur.parent)
            {
                foreach (var mb in cur.GetComponents<MonoBehaviour>())
                {
                    if (mb != null && IsScreenType(mb.GetType())) return PathOf(cur);
                }

                if (canvasLayer == null && cur.TryGetComponent(out Canvas c) && (c.isRootCanvas || c.overrideSorting)) canvasLayer = cur;
            }

            return canvasLayer != null ? PathOf(canvasLayer) : "";
        }

        // Nền chứa chữ: Image/RawImage cha gần nhất đang hiện, đủ lớn — để bắt chữ tràn ra ngoài nút/khung.
        private static Rect? ContainerOf(RectTransform rt, Camera cam, out string name)
        {
            name = null;
            var depth = 0;
            for (var cur = rt.parent; cur != null && depth < 4; cur = cur.parent, depth++)
            {
                if (cur.TryGetComponent(out Canvas c) && c.isRootCanvas) break;
                if (!cur.TryGetComponent(out Graphic g) || !(g is Image || g is RawImage) || !g.enabled || g.color.a * GroupAlpha(cur) < 0.15f) continue;
                if (g is Image im && im.sprite == null) continue;
                var r = ScreenRect((RectTransform)cur, cam);
                if (r.width < 8f || r.height < 8f) continue;
                // Chỉ tính khi nền đó là nút (hoặc con trực tiếp của nút): chữ tràn khỏi nút là lỗi rõ; badge/ribbon/tiêu
                // đề đặt đè viền khung thường là chủ ý.
                var t = cur;
                var isButton = false;
                for (var k = 0; k < 3 && t != null && !isButton; k++, t = t.parent) isButton = t.TryGetComponent(out Selectable _);
                if (!isButton) return null;
                name = cur.name;
                return r;
            }

            return null;
        }

        private static string SelectableKind(Selectable s)
        {
            var n = s.GetType().Name;
            if (s is Button) return "Button";
            if (s is Toggle) return "Toggle";
            if (s is Slider) return "Slider";
            if (s is Scrollbar) return "Scrollbar";
            if (s is InputField || n.Contains("InputField")) return "InputField";
            if (s is Dropdown || n.Contains("Dropdown")) return "Dropdown";
            return n.Contains("Button") ? "Button" : "Selectable";
        }

        private static void HitTest(UiItem item, Rect screen)
        {
            var es = EventSystem.current;
            if (es == null)
            {
                item.BlockedBy = "(không có EventSystem)";
                return;
            }

            var r = Intersect(item.Rect, screen);
            if (r.width < 1f || r.height < 1f)
            {
                item.BlockedBy = "(ngoài màn hình)";
                return;
            }

            // Tâm trước, rồi 4 điểm phụ — lấy điểm đầu tiên mà chính phần tử nhận được chạm.
            var probes = new[]
            {
                r.center, new Vector2(r.x + r.width * 0.3f, r.y + r.height * 0.5f), new Vector2(r.x + r.width * 0.7f, r.y + r.height * 0.5f),
                new Vector2(r.x + r.width * 0.5f, r.y + r.height * 0.3f), new Vector2(r.x + r.width * 0.5f, r.y + r.height * 0.7f)
            };
            string firstBlocker = null;
            Transform firstBlockerT = null;
            foreach (var p in probes)
            {
                var top = TopHit(es, p);
                if (top == null)
                {
                    firstBlocker = firstBlocker ?? "(không nhận raycast — raycastTarget tắt?)";
                    continue;
                }

                var handler = ExecuteEvents.GetEventHandler<IPointerClickHandler>(top);
                if (handler == null) handler = ExecuteEvents.GetEventHandler<IPointerDownHandler>(top);
                if (handler == item.Go || (handler != null && handler.transform.IsChildOf(item.Go.transform)) || top.transform.IsChildOf(item.Go.transform))
                {
                    item.TapPoint = p;
                    return;
                }

                if (firstBlocker == null)
                {
                    firstBlocker = PathOf(top.transform);
                    firstBlockerT = top.transform;
                }
            }

            item.BlockedBy = firstBlocker;
            // Bị màn/popup khác (lớp khác, che phần lớn màn hình) phủ lên = người chơi không thấy nút → "occluded",
            // không phải lỗi. Overlay nhỏ hoặc cùng lớp mới đáng nghi.
            if (firstBlockerT is RectTransform brt && LayerOf(brt) != item.Layer)
            {
                var canvas = brt.GetComponentInParent<Canvas>();
                var root = canvas != null ? canvas.rootCanvas : null;
                var cam = root == null || root.renderMode == RenderMode.ScreenSpaceOverlay ? null : root.worldCamera;
                var br = Intersect(ScreenRect(brt, cam), screen);
                item.Occluded = br.width * br.height >= screen.width * screen.height * 0.35f;
            }
        }

        private static GameObject TopHit(EventSystem es, Vector2 topLeft)
        {
            var ped = new PointerEventData(es) { position = new Vector2(topLeft.x, Screen.height - topLeft.y) };
            _hits.Clear();
            es.RaycastAll(ped, _hits);
            return _hits.Count > 0 ? _hits[0].gameObject : null;
        }

        private static string ChildLabel(RectTransform rt)
        {
            var parts = new List<string>();
            foreach (var g in rt.GetComponentsInChildren<Graphic>(false))
            {
                var t = ProbeText.Get(g);
                if (string.IsNullOrWhiteSpace(t)) continue;
                parts.Add(Clip(t.Trim(), 60));
                if (parts.Count >= 3) break;
            }

            return string.Join(" / ", parts);
        }

        private static float GroupAlpha(Transform t)
        {
            var alpha = 1f;
            var groups = new List<CanvasGroup>();
            while (t != null)
            {
                t.GetComponents(groups);
                foreach (var g in groups)
                {
                    if (!g.enabled) continue;
                    alpha *= g.alpha;
                    if (g.ignoreParentGroups) return alpha;
                }

                if (t.TryGetComponent(out Canvas c) && c.isRootCanvas) break;
                t = t.parent;
            }

            return alpha;
        }

        private static readonly Vector3[] _corners = new Vector3[4];

        private static Rect ScreenRect(RectTransform rt, Camera cam)
        {
            rt.GetWorldCorners(_corners);
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            for (var i = 0; i < 4; i++)
            {
                var p = RectTransformUtility.WorldToScreenPoint(cam, _corners[i]);
                minX = Mathf.Min(minX, p.x);
                maxX = Mathf.Max(maxX, p.x);
                minY = Mathf.Min(minY, p.y);
                maxY = Mathf.Max(maxY, p.y);
            }

            return new Rect(minX, Screen.height - maxY, maxX - minX, maxY - minY);
        }

        private static Rect ClipByMasks(RectTransform rt, Rect rect, Camera cam)
        {
            var t = rt.parent;
            while (t != null)
            {
                if (t.TryGetComponent(out RectMask2D _) || (t.TryGetComponent(out Mask m) && m.enabled))
                    rect = Intersect(rect, ScreenRect((RectTransform)t, cam));
                t = t.parent;
            }

            return rect;
        }

        private static Rect Intersect(Rect a, Rect b)
        {
            var x1 = Mathf.Max(a.xMin, b.xMin);
            var y1 = Mathf.Max(a.yMin, b.yMin);
            var x2 = Mathf.Min(a.xMax, b.xMax);
            var y2 = Mathf.Min(a.yMax, b.yMax);
            return x2 > x1 && y2 > y1 ? Rect.MinMaxRect(x1, y1, x2, y2) : new Rect(x1, y1, 0, 0);
        }

        private static bool HasPointerHandler(GameObject go)
        {
            foreach (var mb in go.GetComponents<MonoBehaviour>())
            {
                if (mb == null || !mb.enabled) continue;
                if (mb is IPointerClickHandler || mb is IPointerDownHandler || mb is IPointerUpHandler) return true;
            }

            return false;
        }

        private static void AddFlag(ref List<string> flags, string flag)
        {
            flags ??= new List<string>();
            if (!flags.Contains(flag)) flags.Add(flag);
        }

        internal static string PathOf(Transform t)
        {
            var parts = new List<string>();
            while (t != null)
            {
                parts.Add(t.name);
                t = t.parent;
            }

            parts.Reverse();
            return string.Join("/", parts);
        }

        private static string Clip(string s, int n)
        {
            if (string.IsNullOrEmpty(s)) return s;
            return s.Length <= n ? s : s.Substring(0, n);
        }

        private static List<string> ActiveScreens()
        {
            var found = new List<KeyValuePair<long, string>>();
            foreach (var mb in Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (mb == null || !mb.isActiveAndEnabled || !IsScreenType(mb.GetType())) continue;
                var canvas = mb.GetComponentInParent<Canvas>();
                long order = canvas != null ? (long)canvas.rootCanvas.sortingOrder * 100000 : 0;
                order += mb.transform.GetSiblingIndex() + (mb.transform.parent != null ? mb.transform.parent.GetSiblingIndex() * 1000 : 0);
                found.Add(new KeyValuePair<long, string>(order, mb.gameObject.name));
            }

            if (found.Count == 0)
            {
                // Project không theo template EZG: panel con trực tiếp của canvas gốc phủ ≥ 40% màn hình, không có thì tên canvas.
                var screenArea = (float)Screen.width * Screen.height;
                foreach (var c in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                {
                    if (!c.isRootCanvas || !c.isActiveAndEnabled) continue;
                    var cam = c.renderMode == RenderMode.ScreenSpaceOverlay ? null : c.worldCamera;
                    var any = false;
                    foreach (Transform child in c.transform)
                    {
                        if (!child.gameObject.activeInHierarchy || !(child is RectTransform crt)) continue;
                        var r = ScreenRect(crt, cam);
                        if (r.width * r.height < screenArea * 0.4f) continue;
                        found.Add(new KeyValuePair<long, string>((long)c.sortingOrder * 100000 + child.GetSiblingIndex(), child.name));
                        any = true;
                    }

                    if (!any) found.Add(new KeyValuePair<long, string>((long)c.sortingOrder * 100000, c.name));
                }
            }

            return found.OrderBy(k => k.Key).Select(k => k.Value).Distinct().ToList();
        }

        private static bool IsScreenType(Type t)
        {
            if (_screenTypes.TryGetValue(t, out var v)) return v;
            var cur = t.BaseType;
            v = false;
            while (cur != null && cur != typeof(MonoBehaviour))
            {
                var n = cur.Name;
                if (n == "FeatureBaseController" || n == "UIScreenBase" || n == "ScreenBase" || n == "PopupBase" || n == "UIPanel")
                {
                    v = true;
                    break;
                }

                cur = cur.BaseType;
            }

            _screenTypes[t] = v;
            return v;
        }

        private static void ScanWorld(ProbeJson w, string query, int max)
        {
            var cam = Camera.main;
            if (cam == null) return;
            var list = new List<KeyValuePair<float, string>>();
            var seen = 0;
            void Consider(Component col, Bounds bounds)
            {
                if (seen++ > MAX_COLLIDERS) return;
                var go = col.gameObject;
                string path = null;
                if (!string.IsNullOrEmpty(query))
                {
                    var t = go.transform;
                    for (var k = 0; k < 4 && t != null; k++, t = t.parent)
                    {
                        if (t.name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            path = PathOf(t);
                            break;
                        }
                    }

                    if (path == null) return;
                }
                else if (!IsWorldClickable(go)) return;

                var sp = cam.WorldToScreenPoint(bounds.center);
                if (sp.z <= 0f) return;
                var on = sp.x >= 0 && sp.x <= Screen.width && sp.y >= 0 && sp.y <= Screen.height;
                var j = new ProbeJson().BeginObject().Prop("path", path ?? PathOf(go.transform)).Prop("name", go.name)
                    .Prop("onScreen", on).Prop("dist", Math.Round(sp.z, 1));
                j.BeginArray("pos").Value(Mathf.Round(sp.x)).Value(Mathf.Round(Screen.height - sp.y)).EndArray();
                list.Add(new KeyValuePair<float, string>((on ? 0f : 100000f) + sp.z, j.EndObject().ToString()));
            }

            foreach (var c in Object.FindObjectsByType<Collider>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                if (c.enabled) Consider(c, c.bounds);
            foreach (var c in Object.FindObjectsByType<Collider2D>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                if (c.enabled) Consider(c, c.bounds);
            foreach (var kv in list.OrderBy(k => k.Key).Take(max)) w.Raw(kv.Value);
        }

        private static bool IsWorldClickable(GameObject go)
        {
            foreach (var mb in go.GetComponents<MonoBehaviour>())
            {
                if (mb == null) continue;
                var t = mb.GetType();
                if (!_clickTypes.TryGetValue(t, out var v))
                {
                    v = mb is IPointerClickHandler || mb is IPointerDownHandler || mb is IPointerUpHandler ||
                        HasMethod(t, "OnMouseDown") || HasMethod(t, "OnMouseUpAsButton") || HasMethod(t, "OnMouseUp");
                    _clickTypes[t] = v;
                }

                if (v) return true;
            }

            return false;
        }

        private static bool HasMethod(Type t, string name)
        {
            for (var cur = t; cur != null && cur != typeof(MonoBehaviour); cur = cur.BaseType)
            {
                if (cur.GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly) != null) return true;
            }

            return false;
        }

        #endregion

        #region Screenshot / input

        private static async Awaitable<ProbeResponse> Shot(string fmt, int quality, int maxEdge)
        {
            await Awaitable.EndOfFrameAsync();
            var tex = ScreenCapture.CaptureScreenshotAsTexture();
            try
            {
                var src = tex;
                Texture2D scaled = null;
                var longEdge = Mathf.Max(tex.width, tex.height);
                if (maxEdge > 0 && longEdge > maxEdge)
                {
                    var k = (float)maxEdge / longEdge;
                    scaled = Resize(tex, Mathf.Max(1, Mathf.RoundToInt(tex.width * k)), Mathf.Max(1, Mathf.RoundToInt(tex.height * k)));
                    src = scaled;
                }

                var png = fmt == "png";
                var bytes = png ? src.EncodeToPNG() : src.EncodeToJPG(Mathf.Clamp(quality, 10, 100));
                if (scaled != null) Object.Destroy(scaled);
                return new ProbeResponse(200, png ? "image/png" : "image/jpeg", bytes);
            }
            finally
            {
                Object.Destroy(tex);
            }
        }

        private static Texture2D Resize(Texture2D src, int w, int h)
        {
            var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32);
            var prev = RenderTexture.active;
            Graphics.Blit(src, rt);
            RenderTexture.active = rt;
            var dst = new Texture2D(w, h, TextureFormat.RGB24, false);
            dst.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            dst.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            return dst;
        }

        private static async Awaitable WaitRealtime(float seconds)
        {
            var end = Time.realtimeSinceStartup + seconds;
            do
            {
                await Awaitable.NextFrameAsync();
            } while (Time.realtimeSinceStartup < end);
        }

        private static async Awaitable<ProbeResponse> Tap(float x, float y, int holdMs)
        {
            var es = EventSystem.current;
            if (es == null) return ProbeResponse.Error(409, "no_event_system", "scene không có EventSystem");
            var pos = new Vector2(x, Screen.height - y);
            var ped = new PointerEventData(es) { position = pos, pressPosition = pos, button = PointerEventData.InputButton.Left, clickCount = 1 };
            _hits.Clear();
            es.RaycastAll(ped, _hits);
            if (_hits.Count == 0) return ProbeResponse.Json(200, new ProbeJson().BeginObject().Prop("hit", "").Prop("note", "không có gì nhận chạm ở điểm này (UI). Object 3D dùng input gốc của game.").EndObject().ToString());
            var hit = _hits[0];
            var go = hit.gameObject;
            ped.pointerCurrentRaycast = hit;
            ped.pointerPressRaycast = hit;
            var press = ExecuteEvents.ExecuteHierarchy(go, ped, ExecuteEvents.pointerDownHandler);
            var click = ExecuteEvents.GetEventHandler<IPointerClickHandler>(go);
            if (press == null) press = click;
            ped.pointerPress = press;
            ped.rawPointerPress = go;
            ped.eligibleForClick = true;
            ped.clickTime = Time.unscaledTime;
            if (holdMs > 0) await WaitRealtime(holdMs / 1000f);
            else await Awaitable.NextFrameAsync();
            if (ped.pointerPress != null) ExecuteEvents.Execute(ped.pointerPress, ped, ExecuteEvents.pointerUpHandler);
            if (click != null && ped.pointerPress == click && ped.eligibleForClick) ExecuteEvents.Execute(click, ped, ExecuteEvents.pointerClickHandler);
            var w = new ProbeJson().BeginObject().Prop("hit", PathOf(go.transform)).Prop("handler", click != null ? PathOf(click.transform) : "");
            return ProbeResponse.Json(200, w.EndObject().ToString());
        }

        private static async Awaitable<ProbeResponse> Drag(float x1, float y1, float x2, float y2, int ms, int holdMs)
        {
            var es = EventSystem.current;
            if (es == null) return ProbeResponse.Error(409, "no_event_system", "scene không có EventSystem");
            var start = new Vector2(x1, Screen.height - y1);
            var end = new Vector2(x2, Screen.height - y2);
            var ped = new PointerEventData(es) { position = start, pressPosition = start, button = PointerEventData.InputButton.Left };
            _hits.Clear();
            es.RaycastAll(ped, _hits);
            if (_hits.Count == 0) return ProbeResponse.Json(200, "{\"hit\":\"\"}");
            var go = _hits[0].gameObject;
            ped.pointerCurrentRaycast = _hits[0];
            ped.pointerPressRaycast = _hits[0];
            var press = ExecuteEvents.ExecuteHierarchy(go, ped, ExecuteEvents.pointerDownHandler);
            ped.pointerPress = press != null ? press : ExecuteEvents.GetEventHandler<IPointerClickHandler>(go);
            ped.rawPointerPress = go;
            ped.pointerDrag = ExecuteEvents.GetEventHandler<IDragHandler>(go);
            if (ped.pointerDrag != null) ExecuteEvents.Execute(ped.pointerDrag, ped, ExecuteEvents.initializePotentialDrag);
            if (holdMs > 0) await WaitRealtime(holdMs / 1000f);
            var t0 = Time.realtimeSinceStartup;
            var duration = Mathf.Max(0.05f, ms / 1000f);
            var prev = start;
            var began = false;
            while (true)
            {
                await Awaitable.NextFrameAsync();
                var k = Mathf.Clamp01((Time.realtimeSinceStartup - t0) / duration);
                var p = Vector2.Lerp(start, end, k);
                ped.position = p;
                ped.delta = p - prev;
                prev = p;
                if (ped.pointerDrag != null)
                {
                    if (!began)
                    {
                        ExecuteEvents.Execute(ped.pointerDrag, ped, ExecuteEvents.beginDragHandler);
                        ped.dragging = true;
                        began = true;
                    }

                    ExecuteEvents.Execute(ped.pointerDrag, ped, ExecuteEvents.dragHandler);
                }

                if (k >= 1f) break;
            }

            _hits.Clear();
            es.RaycastAll(ped, _hits);
            var dropTarget = _hits.Count > 0 ? _hits[0].gameObject : null;
            ped.pointerCurrentRaycast = _hits.Count > 0 ? _hits[0] : default;
            if (ped.pointerPress != null) ExecuteEvents.Execute(ped.pointerPress, ped, ExecuteEvents.pointerUpHandler);
            if (dropTarget != null && began) ExecuteEvents.ExecuteHierarchy(dropTarget, ped, ExecuteEvents.dropHandler);
            if (began) ExecuteEvents.Execute(ped.pointerDrag, ped, ExecuteEvents.endDragHandler);
            return ProbeResponse.Json(200, new ProbeJson().BeginObject().Prop("hit", PathOf(go.transform))
                .Prop("drop", dropTarget != null ? PathOf(dropTarget.transform) : "").EndObject().ToString());
        }

        private static ProbeResponse SetText(string value, string path, bool submit)
        {
            GameObject target = null;
            if (!string.IsNullOrEmpty(path)) target = FindByPath(path);
            if (target == null && EventSystem.current != null) target = EventSystem.current.currentSelectedGameObject;
            if (target == null) return ProbeResponse.Error(404, "no_input", "không có ô nhập đang focus — chạm vào ô trước hoặc truyền path");
            foreach (var c in target.GetComponents<Component>())
            {
                if (c == null) continue;
                var t = c.GetType();
                if (!t.Name.Contains("InputField")) continue;
                var prop = t.GetProperty("text", BindingFlags.Instance | BindingFlags.Public);
                if (prop == null || !prop.CanWrite) continue;
                prop.SetValue(c, value);
                InvokeEvent(c, "onEndEdit", value);
                if (submit) InvokeEvent(c, "onSubmit", value);
                return ProbeResponse.Json(200, new ProbeJson().BeginObject().Prop("target", PathOf(target.transform)).EndObject().ToString());
            }

            return ProbeResponse.Error(409, "not_input", PathOf(target.transform) + " không phải ô nhập");
        }

        private static void InvokeEvent(Component c, string name, string value)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var t = c.GetType();
            object ev = t.GetProperty(name, flags)?.GetValue(c) ?? t.GetField(name, flags)?.GetValue(c)
                ?? t.GetField("m_" + char.ToUpperInvariant(name[0]) + name.Substring(1), flags)?.GetValue(c);
            ev?.GetType().GetMethod("Invoke", new[] { typeof(string) })?.Invoke(ev, new object[] { value });
        }

        internal static GameObject FindByPath(string path)
        {
            var parts = path.Split('/');
            foreach (var root in AllRoots())
            {
                if (root.name != parts[0]) continue;
                var t = root.transform;
                for (var i = 1; i < parts.Length && t != null; i++) t = t.Find(parts[i]);
                if (t != null) return t.gameObject;
            }

            return null;
        }

        private static IEnumerable<GameObject> AllRoots()
        {
            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                var s = SceneManager.GetSceneAt(i);
                if (!s.isLoaded) continue;
                foreach (var g in s.GetRootGameObjects()) yield return g;
            }

            // Object DontDestroyOnLoad: lấy qua canvas gốc đang bật.
            foreach (var c in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (c.isRootCanvas && c.transform.parent == null) yield return c.gameObject;
            }
        }

        #endregion

        #region Reflection endpoints

        private static ProbeResponse GetValue(string path)
        {
            if (!ProbeReflect.TryEval(path, out var value, out var error)) return ProbeResponse.Error(404, "eval_failed", error);
            var w = new ProbeJson().BeginObject().Key("value");
            ProbeReflect.Write(w, value, 2);
            return ProbeResponse.Json(200, w.EndObject().ToString());
        }

        private static ProbeResponse Call(string method, List<string> args)
        {
            if (!ProbeReflect.TryCall(method, args, out var value, out var error)) return ProbeResponse.Error(400, "call_failed", error);
            var w = new ProbeJson().BeginObject().Key("value");
            ProbeReflect.Write(w, value, 1);
            return ProbeResponse.Json(200, w.EndObject().ToString());
        }

        private static ProbeResponse Types(string q)
        {
            var w = new ProbeJson().BeginObject().BeginArray("types");
            var count = 0;
            var ordered = ProbeReflect.GameTypes().OrderBy(t => t.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 ? 0 :
                (t.FullName ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 ? 1 : 2);
            foreach (var t in ordered)
            {
                if (count >= 60) break;
                var typeHit = t.FullName != null && t.FullName.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;
                var members = new List<string>();
                const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
                MemberInfo[] all;
                try
                {
                    all = t.GetMembers(flags);
                }
                catch (Exception)
                {
                    continue;
                }

                foreach (var m in all)
                {
                    if (m.Name.StartsWith("<", StringComparison.Ordinal) || m.Name.StartsWith("get_", StringComparison.Ordinal) ||
                        m.Name.StartsWith("set_", StringComparison.Ordinal) || m.Name.StartsWith("add_", StringComparison.Ordinal) ||
                        m.Name.StartsWith("remove_", StringComparison.Ordinal) || m.Name == ".cctor") continue;
                    if (!typeHit && m.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    members.Add(ProbeReflect.Describe(m));
                    if (members.Count >= 50) break;
                }

                if (members.Count == 0 && !typeHit) continue;
                count++;
                w.BeginObject().Prop("type", t.FullName).BeginArray("members");
                foreach (var m in members) w.Value(m);
                w.EndArray().EndObject();
            }

            return ProbeResponse.Json(200, w.EndArray().EndObject().ToString());
        }

        #endregion

        #region Bug context

        private static async Awaitable<ProbeResponse> Context(bool includeSave)
        {
            var w = new ProbeJson().BeginObject().Prop("scene", SceneManager.GetActiveScene().name).Prop("unityVersion", Application.unityVersion);
            var screens = ActiveScreens();
            w.Prop("screen", screens.Count > 0 ? screens[screens.Count - 1] : SceneManager.GetActiveScene().name);
            w.BeginArray("screens");
            foreach (var s in screens) w.Value(s);
            w.EndArray();
            var cfg = ProbeReflect.FindType("Ezg.BugLogger.BugLoggerConfig");
            if (cfg != null)
            {
                try
                {
                    if (cfg.GetProperty("PlayerIdProvider")?.GetValue(null) is Func<string> pid) w.Prop("playerId", pid() ?? "");
                }
                catch (Exception)
                {
                    // provider của game lỗi — bỏ qua.
                }

                w.BeginObject("extra");
                try
                {
                    if (cfg.GetProperty("ExtraContextProvider")?.GetValue(null) is Delegate extra &&
                        extra.DynamicInvoke() is IEnumerable<KeyValuePair<string, string>> pairs)
                    {
                        foreach (var kv in pairs) w.Prop(kv.Key, kv.Value ?? "");
                    }
                }
                catch (Exception)
                {
                    // bỏ qua
                }

                w.EndObject();
                if (includeSave)
                {
                    var save = await ReadSave(cfg);
                    if (save != null) w.Prop("save", save);
                }
            }

            var info = ProbeReflect.FindType("Ezg.BugLogger.BugLoggerBuildInfo");
            if (info != null)
            {
                const BindingFlags f = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
                w.Prop("commitSha", info.GetProperty("CommitSha", f)?.GetValue(null) as string ?? "");
                w.Prop("branch", info.GetProperty("Branch", f)?.GetValue(null) as string ?? "");
                w.Prop("build", info.GetProperty("BuildNumber", f)?.GetValue(null) as string ?? "");
            }

            return ProbeResponse.Json(200, w.EndObject().ToString());
        }

        // SaveSnapshotProvider trả UniTask<string> — chờ qua awaiter bằng reflection, tối đa 5 giây.
        private static async Awaitable<string> ReadSave(Type cfg)
        {
            try
            {
                if (!(cfg.GetProperty("SaveSnapshotProvider")?.GetValue(null) is Delegate provider)) return null;
                var task = provider.DynamicInvoke();
                var awaiter = task?.GetType().GetMethod("GetAwaiter")?.Invoke(task, null);
                if (awaiter == null) return null;
                var done = awaiter.GetType().GetProperty("IsCompleted");
                var end = Time.realtimeSinceStartup + 5f;
                while (!(bool)done.GetValue(awaiter) && Time.realtimeSinceStartup < end) await Awaitable.NextFrameAsync();
                if (!(bool)done.GetValue(awaiter)) return null;
                return awaiter.GetType().GetMethod("GetResult")?.Invoke(awaiter, null) as string;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static async Awaitable<ProbeResponse> Bug(ProbeRequest r)
        {
            var api = ProbeReflect.FindType("Ezg.BugLogger.BugLogger");
            var draftType = ProbeReflect.FindType("Ezg.BugLogger.BugReportDraft");
            if (api == null || draftType == null) return ProbeResponse.Error(409, "no_buglogger", "build không có com.ezg.buglogger");
            var draft = Activator.CreateInstance(draftType);
            void Set(string field, object value) => draftType.GetField(field)?.SetValue(draft, value);
            Set("Title", r.Get("title") ?? "");
            Set("Actual", r.Get("actual") ?? "");
            Set("Expected", r.Get("expected") ?? "");
            Set("Steps", r.Get("steps") ?? "");
            Set("Description", r.Get("description") ?? "");
            Set("FeatureKey", r.Get("featureKey") ?? "");
            Set("Category", r.Get("category") ?? "");
            Set("ReproduceRate", r.Get("reproduceRate") ?? "");
            Set("TagIds", r.GetAll("tagIds").ToArray());
            Set("MentionIds", r.GetAll("mentionIds").ToArray());
            byte[] jpg = null;
            var b64 = r.Get("shot_b64");
            if (!string.IsNullOrEmpty(b64))
            {
                jpg = Convert.FromBase64String(b64);
            }
            else
            {
                await Awaitable.EndOfFrameAsync();
                var tex = ScreenCapture.CaptureScreenshotAsTexture();
                jpg = api.GetMethod("EncodeScreenshot")?.Invoke(null, new object[] { tex }) as byte[];
                Object.Destroy(tex);
            }

            try
            {
                var id = api.GetMethod("Submit")?.Invoke(null, new[] { draft, jpg }) as string;
                return ProbeResponse.Json(200, new ProbeJson().BeginObject().Prop("id", id ?? "").EndObject().ToString());
            }
            catch (TargetInvocationException e)
            {
                return ProbeResponse.Error(400, "submit_failed", e.InnerException?.Message ?? e.Message);
            }
        }

        #endregion
    }

    /// <summary>Đọc chữ của UI.Text / TMP_Text (TMP qua reflection) + kiểm tràn chữ, thiếu glyph.</summary>
    [Preserve]
    internal static class ProbeText
    {
        private static bool _resolved;
        private static Type _tmpText;
        private static PropertyInfo _tmpTextProp;
        private static PropertyInfo _overflowProp;
        private static PropertyInfo _truncatedProp;
        private static PropertyInfo _fontProp;
        private static PropertyInfo _boundsProp;
        private static MethodInfo _hasChars;

        public static string Get(Graphic g)
        {
            if (g == null) return null;
            if (g is Text t) return t.text;
            Resolve();
            if (_tmpText != null && _tmpText.IsInstanceOfType(g)) return _tmpTextProp?.GetValue(g) as string;
            return null;
        }

        public static void Audit(Graphic g, string text, ref List<string> flags, ref string missing)
        {
            try
            {
                if (g is Text t)
                {
                    var visible = t.cachedTextGenerator.characterCountVisible;
                    var plain = StripTags(text).Replace("\n", "").Replace(" ", "");
                    if (t.verticalOverflow == VerticalWrapMode.Truncate && plain.Length > 0 && visible > 0 && visible < plain.Length - 1)
                        Add(ref flags, "truncated");
                    return;
                }

                if (_tmpText == null || !_tmpText.IsInstanceOfType(g)) return;
                if (_overflowProp?.GetValue(g) is bool over && over) Add(ref flags, "overflow");
                if (_truncatedProp?.GetValue(g) is bool trunc && trunc) Add(ref flags, "truncated");
                var font = _fontProp?.GetValue(g);
                if (font == null || _hasChars == null || text.Length > 400) return;
                var plainText = StripTags(text);
                var args = new object[] { plainText, null, true, false };
                if (_hasChars.Invoke(font, args) is bool ok && !ok && args[1] is uint[] miss)
                {
                    var chars = miss.Where(c => c > 32 && c != 0xA0 && c != 0x200B && c != 0xFE0F).Distinct().Take(10)
                        .Select(c => c <= 0xFFFF ? ((char)c).ToString() : "U+" + c.ToString("X")).ToArray();
                    if (chars.Length > 0)
                    {
                        Add(ref flags, "missingGlyph");
                        missing = string.Join(" ", chars);
                    }
                }
            }
            catch (Exception)
            {
                // Bản TMP khác chữ ký — bỏ qua audit này.
            }
        }

        /// <summary>Khung chữ thật sự vẽ (không phải RectTransform), toạ độ màn hình gốc trên-trái.</summary>
        public static bool TryInk(Graphic g, RectTransform rt, Camera cam, out Rect ink)
        {
            ink = default;
            try
            {
                Vector2 min, max;
                if (g is Text t)
                {
                    var verts = t.cachedTextGenerator.verts;
                    if (verts == null || verts.Count == 0) return false;
                    float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
                    for (var i = 0; i < verts.Count; i++)
                    {
                        var p = verts[i].position;
                        x0 = Mathf.Min(x0, p.x);
                        y0 = Mathf.Min(y0, p.y);
                        x1 = Mathf.Max(x1, p.x);
                        y1 = Mathf.Max(y1, p.y);
                    }

                    var ppu = t.pixelsPerUnit > 0f ? t.pixelsPerUnit : 1f;
                    min = new Vector2(x0, y0) / ppu;
                    max = new Vector2(x1, y1) / ppu;
                }
                else
                {
                    Resolve();
                    if (_tmpText == null || !_tmpText.IsInstanceOfType(g) || _boundsProp == null) return false;
                    var b = (Bounds)_boundsProp.GetValue(g);
                    if (b.size.x <= 0f || b.size.y <= 0f) return false;
                    min = b.min;
                    max = b.max;
                }

                var a = RectTransformUtility.WorldToScreenPoint(cam, rt.TransformPoint(new Vector3(min.x, min.y, 0f)));
                var c = RectTransformUtility.WorldToScreenPoint(cam, rt.TransformPoint(new Vector3(max.x, max.y, 0f)));
                var xMin = Mathf.Min(a.x, c.x);
                var xMax = Mathf.Max(a.x, c.x);
                var yMin = Mathf.Min(a.y, c.y);
                var yMax = Mathf.Max(a.y, c.y);
                ink = new Rect(xMin, Screen.height - yMax, xMax - xMin, yMax - yMin);
                return ink.width > 0.5f && ink.height > 0.5f;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static void Add(ref List<string> flags, string f)
        {
            flags ??= new List<string>();
            if (!flags.Contains(f)) flags.Add(f);
        }

        private static string StripTags(string s)
        {
            if (string.IsNullOrEmpty(s) || s.IndexOf('<') < 0) return s ?? "";
            var sb = new StringBuilder(s.Length);
            var depth = 0;
            foreach (var c in s)
            {
                if (c == '<') depth++;
                else if (c == '>' && depth > 0) depth--;
                else if (depth == 0) sb.Append(c);
            }

            return sb.ToString();
        }

        private static void Resolve()
        {
            if (_resolved) return;
            _resolved = true;
            _tmpText = ProbeReflect.FindType("TMPro.TMP_Text");
            if (_tmpText == null) return;
            _tmpTextProp = _tmpText.GetProperty("text", BindingFlags.Instance | BindingFlags.Public);
            _overflowProp = _tmpText.GetProperty("isTextOverflowing", BindingFlags.Instance | BindingFlags.Public);
            _truncatedProp = _tmpText.GetProperty("isTextTruncated", BindingFlags.Instance | BindingFlags.Public);
            _fontProp = _tmpText.GetProperty("font", BindingFlags.Instance | BindingFlags.Public);
            _boundsProp = _tmpText.GetProperty("textBounds", BindingFlags.Instance | BindingFlags.Public);
            var fontType = ProbeReflect.FindType("TMPro.TMP_FontAsset");
            _hasChars = fontType?.GetMethods(BindingFlags.Instance | BindingFlags.Public).FirstOrDefault(m =>
            {
                if (m.Name != "HasCharacters") return false;
                var p = m.GetParameters();
                return p.Length == 4 && p[0].ParameterType == typeof(string) && p[1].ParameterType == typeof(uint[]).MakeByRefType();
            });
        }
    }

    /// <summary>Đánh giá đường dẫn Namespace.Type.Member.Member[0] và gọi hàm bằng reflection.</summary>
    [Preserve]
    internal static class ProbeReflect
    {
        private const BindingFlags ANY = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy;
        private static readonly Dictionary<string, Type> _types = new Dictionary<string, Type>();
        private static List<Type> _gameTypes;

        public static Type FindType(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            lock (_types)
            {
                if (_types.TryGetValue(name, out var cached)) return cached;
            }

            Type found = null;
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            foreach (var asm in assemblies)
            {
                try
                {
                    found = asm.GetType(name, false);
                }
                catch (Exception)
                {
                    found = null;
                }

                if (found != null) break;
            }

            if (found == null && name.IndexOf('.') < 0)
            {
                foreach (var t in GameTypes())
                {
                    if (t.Name != name) continue;
                    found = t;
                    break;
                }
            }

            lock (_types)
            {
                _types[name] = found;
            }

            return found;
        }

        public static IEnumerable<Type> GameTypes()
        {
            if (_gameTypes != null) return _gameTypes;
            var list = new List<Type>();
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var n = asm.GetName().Name;
                if (n.StartsWith("System", StringComparison.Ordinal) || n.StartsWith("Unity", StringComparison.Ordinal) ||
                    n.StartsWith("Mono", StringComparison.Ordinal) || n == "mscorlib" || n == "netstandard" || n.StartsWith("nunit", StringComparison.OrdinalIgnoreCase) ||
                    n.StartsWith("Microsoft", StringComparison.Ordinal) || n.StartsWith("Bee", StringComparison.Ordinal) || n.StartsWith("ExCSS", StringComparison.Ordinal))
                    continue;
                try
                {
                    list.AddRange(asm.GetTypes().Where(t => t != null && !t.IsGenericTypeDefinition));
                }
                catch (ReflectionTypeLoadException e)
                {
                    list.AddRange(e.Types.Where(t => t != null));
                }
                catch (Exception)
                {
                    // bỏ qua assembly lỗi
                }
            }

            _gameTypes = list;
            return list;
        }

        public static bool TryEval(string path, out object value, out string error)
        {
            value = null;
            error = null;
            var segs = SplitPath(path);
            if (segs.Count == 0)
            {
                error = "path rỗng";
                return false;
            }

            Type type = null;
            var start = 0;
            for (var i = segs.Count; i >= 1; i--)
            {
                var name = string.Join(".", segs.Take(i).Select(s => StripIndex(s)));
                type = FindType(name);
                if (type == null) continue;
                start = i;
                break;
            }

            if (type == null)
            {
                error = "không tìm thấy type trong '" + path + "'";
                return false;
            }

            object obj = null;
            var curType = type;
            for (var i = start; i < segs.Count; i++)
            {
                if (!TryMember(obj, curType, segs[i], out obj, out error)) return false;
                if (obj == null)
                {
                    if (i < segs.Count - 1)
                    {
                        error = segs[i] + " = null";
                        return false;
                    }

                    break;
                }

                curType = obj.GetType();
            }

            value = obj;
            return true;
        }

        public static bool TryCall(string path, List<string> args, out object value, out string error)
        {
            value = null;
            var dot = path.LastIndexOf('.');
            if (dot <= 0)
            {
                error = "cần Type.Method";
                return false;
            }

            var targetPath = path.Substring(0, dot);
            var methodName = path.Substring(dot + 1).TrimEnd('(', ')');
            object target = null;
            var type = FindType(targetPath);
            if (type == null)
            {
                if (!TryEval(targetPath, out target, out error)) return false;
                if (target == null)
                {
                    error = targetPath + " = null";
                    return false;
                }

                type = target.GetType();
            }

            var flags = ANY | (target == null ? BindingFlags.Static : BindingFlags.Instance);
            var candidates = type.GetMethods(flags).Where(m => m.Name == methodName && m.GetParameters().Count(p => !p.IsOptional) <= args.Count &&
                                                                   m.GetParameters().Length >= args.Count).ToList();
            if (candidates.Count == 0)
            {
                error = "không có hàm " + methodName + " nhận " + args.Count + " tham số trên " + type.FullName;
                return false;
            }

            foreach (var m in candidates)
            {
                var ps = m.GetParameters();
                var conv = new object[ps.Length];
                var ok = true;
                for (var i = 0; i < ps.Length; i++)
                {
                    if (i < args.Count)
                    {
                        if (!TryConvert(args[i], ps[i].ParameterType, out conv[i]))
                        {
                            ok = false;
                            break;
                        }
                    }
                    else
                    {
                        conv[i] = ps[i].DefaultValue;
                    }
                }

                if (!ok) continue;
                try
                {
                    value = m.Invoke(target, conv);
                    error = null;
                    return true;
                }
                catch (TargetInvocationException e)
                {
                    error = "hàm ném " + e.InnerException?.GetType().Name + ": " + e.InnerException?.Message;
                    return false;
                }
            }

            error = "không đổi được tham số sang kiểu của " + methodName;
            return false;
        }

        private static bool TryMember(object obj, Type type, string seg, out object result, out string error)
        {
            result = null;
            error = null;
            var name = StripIndex(seg);
            var flags = ANY | (obj == null ? BindingFlags.Static : BindingFlags.Instance);
            try
            {
                if (name.EndsWith("()", StringComparison.Ordinal))
                {
                    var m = type.GetMethods(flags).FirstOrDefault(x => x.Name == name.Substring(0, name.Length - 2) && x.GetParameters().Length == 0);
                    if (m == null)
                    {
                        error = "không có hàm " + name;
                        return false;
                    }

                    result = m.Invoke(obj, null);
                }
                else
                {
                    var prop = type.GetProperties(flags).FirstOrDefault(p => p.Name == name && p.GetIndexParameters().Length == 0);
                    if (prop != null)
                    {
                        result = prop.GetValue(obj);
                    }
                    else
                    {
                        var field = type.GetField(name, flags);
                        if (field == null)
                        {
                            error = "không có member " + name + " trên " + type.FullName;
                            return false;
                        }

                        result = field.GetValue(obj);
                    }
                }
            }
            catch (TargetInvocationException e)
            {
                error = name + " ném " + e.InnerException?.Message;
                return false;
            }

            var bracket = seg.IndexOf('[');
            if (bracket < 0) return true;
            var key = seg.Substring(bracket + 1, seg.Length - bracket - 2).Trim('"', '\'');
            if (result is IDictionary dict)
            {
                foreach (DictionaryEntry de in dict)
                {
                    if (Convert.ToString(de.Key, CultureInfo.InvariantCulture) == key)
                    {
                        result = de.Value;
                        return true;
                    }
                }

                error = "không có key " + key;
                return false;
            }

            if (result is IList list && int.TryParse(key, out var idx))
            {
                if (idx < 0 || idx >= list.Count)
                {
                    error = "index " + idx + " ngoài 0.." + (list.Count - 1);
                    return false;
                }

                result = list[idx];
                return true;
            }

            error = seg + ": không index được";
            return false;
        }

        private static List<string> SplitPath(string path)
        {
            var segs = new List<string>();
            var sb = new StringBuilder();
            var depth = 0;
            foreach (var c in path ?? "")
            {
                if (c == '[' || c == '(') depth++;
                if (c == ']' || c == ')') depth--;
                if (c == '.' && depth == 0)
                {
                    segs.Add(sb.ToString());
                    sb.Clear();
                    continue;
                }

                sb.Append(c);
            }

            if (sb.Length > 0) segs.Add(sb.ToString());
            return segs;
        }

        private static string StripIndex(string seg)
        {
            var b = seg.IndexOf('[');
            return b < 0 ? seg : seg.Substring(0, b);
        }

        public static bool TryConvert(string s, Type t, out object value)
        {
            value = null;
            try
            {
                var u = Nullable.GetUnderlyingType(t) ?? t;
                if (s == "null" && (!u.IsValueType || u != t)) return true;
                if (u == typeof(string))
                {
                    value = s;
                    return true;
                }

                if (u.IsEnum)
                {
                    value = Enum.Parse(u, s, true);
                    return true;
                }

                if (u == typeof(bool))
                {
                    value = s == "1" || s.Equals("true", StringComparison.OrdinalIgnoreCase);
                    return s == "0" || s == "1" || bool.TryParse(s, out _);
                }

                if (u == typeof(Vector2) || u == typeof(Vector3))
                {
                    var p = s.Split(',').Select(x => float.Parse(x, CultureInfo.InvariantCulture)).ToArray();
                    value = u == typeof(Vector2) ? (object)new Vector2(p[0], p[1]) : new Vector3(p[0], p[1], p.Length > 2 ? p[2] : 0f);
                    return true;
                }

                if (typeof(IConvertible).IsAssignableFrom(u))
                {
                    value = Convert.ChangeType(s, u, CultureInfo.InvariantCulture);
                    return true;
                }
            }
            catch (Exception)
            {
                return false;
            }

            return false;
        }

        public static string Describe(MemberInfo m)
        {
            switch (m)
            {
                case MethodInfo mi:
                    return "static " + Short(mi.ReturnType) + " " + mi.Name + "(" + string.Join(", ", mi.GetParameters().Select(p => Short(p.ParameterType) + " " + p.Name)) + ")";
                case FieldInfo fi:
                    return "static " + Short(fi.FieldType) + " " + fi.Name + (fi.IsLiteral ? " (const)" : "");
                case PropertyInfo pi:
                    return "static " + Short(pi.PropertyType) + " " + pi.Name + " { get; }";
                default:
                    return m.MemberType + " " + m.Name;
            }
        }

        private static string Short(Type t)
        {
            if (t == null) return "?";
            if (!t.IsGenericType) return t.Name;
            return t.Name.Split('`')[0] + "<" + string.Join(",", t.GetGenericArguments().Select(Short)) + ">";
        }

        public static void Write(ProbeJson w, object v, int depth)
        {
            switch (v)
            {
                case null:
                    w.Null();
                    return;
                case string s:
                    w.Value(s);
                    return;
                case bool b:
                    w.Value(b);
                    return;
                case char ch:
                    w.Value(ch.ToString());
                    return;
                case Enum e:
                    w.Value(e.ToString());
                    return;
                case Object uo:
                    w.Value(uo == null ? "null (destroyed)" : uo.GetType().Name + ":" + uo.name);
                    return;
                case Vector2 _:
                case Vector3 _:
                case Color _:
                case DateTime _:
                case TimeSpan _:
                    w.Value(Convert.ToString(v, CultureInfo.InvariantCulture));
                    return;
            }

            if (v is IConvertible && v.GetType().IsPrimitive || v is decimal)
            {
                w.Value(Convert.ToDouble(v, CultureInfo.InvariantCulture));
                return;
            }

            if (v is IDictionary dict)
            {
                w.BeginObject();
                var n = 0;
                foreach (DictionaryEntry de in dict)
                {
                    if (n++ >= 60) break;
                    w.Key(Convert.ToString(de.Key, CultureInfo.InvariantCulture));
                    if (depth > 0) Write(w, de.Value, depth - 1);
                    else w.Value(Convert.ToString(de.Value, CultureInfo.InvariantCulture));
                }

                w.EndObject();
                return;
            }

            if (v is IEnumerable seq)
            {
                w.BeginArray();
                var n = 0;
                foreach (var item in seq)
                {
                    if (n++ >= 60) break;
                    if (depth > 0) Write(w, item, depth - 1);
                    else w.Value(Convert.ToString(item, CultureInfo.InvariantCulture));
                }

                w.EndArray();
                return;
            }

            if (depth <= 0)
            {
                w.Value(v.ToString());
                return;
            }

            w.BeginObject();
            var t = v.GetType();
            var count = 0;
            foreach (var f in t.GetFields(BindingFlags.Instance | BindingFlags.Public))
            {
                if (count++ >= 40) break;
                w.Key(f.Name);
                Write(w, SafeGet(() => f.GetValue(v)), depth - 1);
            }

            foreach (var p in t.GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                if (count++ >= 40) break;
                if (!p.CanRead || p.GetIndexParameters().Length > 0) continue;
                w.Key(p.Name);
                Write(w, SafeGet(() => p.GetValue(v)), depth - 1);
            }

            w.EndObject();
        }

        private static object SafeGet(Func<object> f)
        {
            try
            {
                return f();
            }
            catch (Exception e)
            {
                return "<lỗi: " + (e.InnerException ?? e).GetType().Name + ">";
            }
        }
    }

    /// <summary>JSON writer tối giản (không phụ thuộc thư viện ngoài).</summary>
    [Preserve]
    internal sealed class ProbeJson
    {
        private readonly StringBuilder _sb = new StringBuilder(256);
        private readonly Stack<bool> _first = new Stack<bool>();
        private bool _afterKey;

        public ProbeJson BeginObject(string key = null)
        {
            Prefix(key);
            _sb.Append('{');
            _first.Push(true);
            return this;
        }

        public ProbeJson EndObject()
        {
            _first.Pop();
            _sb.Append('}');
            return this;
        }

        public ProbeJson BeginArray(string key = null)
        {
            Prefix(key);
            _sb.Append('[');
            _first.Push(true);
            return this;
        }

        public ProbeJson EndArray()
        {
            _first.Pop();
            _sb.Append(']');
            return this;
        }

        public ProbeJson Key(string key)
        {
            Comma();
            AppendString(key);
            _sb.Append(':');
            _afterKey = true;
            return this;
        }

        public ProbeJson Prop(string key, string v) => Key(key).Value(v);
        public ProbeJson Prop(string key, bool v) => Key(key).Value(v);
        public ProbeJson Prop(string key, double v) => Key(key).Value(v);
        public ProbeJson Prop(string key, long v) => Key(key).Value(v);

        public ProbeJson Value(string v)
        {
            Comma();
            if (v == null) _sb.Append("null");
            else AppendString(v);
            return this;
        }

        public ProbeJson Value(bool v)
        {
            Comma();
            _sb.Append(v ? "true" : "false");
            return this;
        }

        public ProbeJson Value(double v)
        {
            Comma();
            _sb.Append(double.IsNaN(v) || double.IsInfinity(v) ? "null" : v.ToString("0.###", CultureInfo.InvariantCulture));
            return this;
        }

        public ProbeJson Value(long v)
        {
            Comma();
            _sb.Append(v.ToString(CultureInfo.InvariantCulture));
            return this;
        }

        public ProbeJson Null()
        {
            Comma();
            _sb.Append("null");
            return this;
        }

        public ProbeJson Raw(string json)
        {
            Comma();
            _sb.Append(json);
            return this;
        }

        public override string ToString() => _sb.ToString();

        private void Prefix(string key)
        {
            if (key != null) Key(key);
            else Comma();
            _afterKey = false;
        }

        private void Comma()
        {
            if (_afterKey)
            {
                _afterKey = false;
                return;
            }

            if (_first.Count == 0) return;
            if (_first.Peek())
            {
                _first.Pop();
                _first.Push(false);
            }
            else
            {
                _sb.Append(',');
            }
        }

        private void AppendString(string s)
        {
            _sb.Append('"');
            foreach (var c in s)
            {
                switch (c)
                {
                    case '"': _sb.Append("\\\""); break;
                    case '\\': _sb.Append("\\\\"); break;
                    case '\n': _sb.Append("\\n"); break;
                    case '\r': _sb.Append("\\r"); break;
                    case '\t': _sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) _sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else _sb.Append(c);
                        break;
                }
            }

            _sb.Append('"');
        }
    }

    /// <summary>Request HTTP đã parse (query + body form-urlencoded gộp chung).</summary>
    internal sealed class ProbeRequest
    {
        public string Method;
        public string Path;
        public readonly Dictionary<string, string> Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, List<string>> Params = new Dictionary<string, List<string>>();

        public string Header(string name) => Headers.TryGetValue(name, out var v) ? v : null;
        public string Get(string name) => Params.TryGetValue(name, out var v) && v.Count > 0 ? v[0] : null;
        public List<string> GetAll(string name) => Params.TryGetValue(name, out var v) ? v : new List<string>();

        public int GetInt(string name, int def) => int.TryParse(Get(name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : def;

        public float GetFloat(string name, float def = 0f) =>
            float.TryParse(Get(name), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : def;

        public void ParseForm(string s)
        {
            if (string.IsNullOrEmpty(s)) return;
            foreach (var pair in s.Split('&'))
            {
                if (pair.Length == 0) continue;
                var eq = pair.IndexOf('=');
                var k = Decode(eq < 0 ? pair : pair.Substring(0, eq));
                var v = eq < 0 ? "" : Decode(pair.Substring(eq + 1));
                if (!Params.TryGetValue(k, out var list)) Params[k] = list = new List<string>();
                list.Add(v);
            }
        }

        private static string Decode(string s) => Uri.UnescapeDataString(s.Replace('+', ' '));
    }

    internal sealed class ProbeResponse
    {
        public readonly int Status;
        public readonly string ContentType;
        public readonly byte[] Body;

        public ProbeResponse(int status, string contentType, byte[] body)
        {
            Status = status;
            ContentType = contentType;
            Body = body ?? Array.Empty<byte>();
        }

        public static ProbeResponse Json(int status, string json) => new ProbeResponse(status, "application/json; charset=utf-8", Encoding.UTF8.GetBytes(json));

        public static ProbeResponse Error(int status, string code, string message) =>
            Json(status, new ProbeJson().BeginObject().Prop("error", code).Prop("message", message ?? "").EndObject().ToString());
    }

    /// <summary>Server HTTP/1.1 tối giản trên TcpListener: mỗi kết nối một request, đóng sau khi trả lời.</summary>
    internal sealed class ProbeServer
    {
        private const int MAX_HEADER = 64 * 1024;
        private const int MAX_BODY = 16 * 1024 * 1024;
        private readonly TcpListener _listener;
        private readonly Func<ProbeRequest, ProbeResponse> _handler;
        private volatile bool _running;

        public ProbeServer(IPAddress address, int port, Func<ProbeRequest, ProbeResponse> handler)
        {
            _listener = new TcpListener(address, port);
            if (address.AddressFamily == AddressFamily.InterNetworkV6) _listener.Server.DualMode = true;
            _handler = handler;
        }

        public void Start()
        {
            _listener.Start(16);
            _running = true;
            new Thread(Loop) { IsBackground = true, Name = "EzgQaProbe" }.Start();
        }

        public void Stop()
        {
            _running = false;
            try
            {
                _listener.Stop();
            }
            catch (Exception)
            {
                // đã đóng
            }
        }

        private void Loop()
        {
            while (_running)
            {
                TcpClient client;
                try
                {
                    client = _listener.AcceptTcpClient();
                }
                catch (Exception)
                {
                    if (!_running) break;
                    Thread.Sleep(50);
                    continue;
                }

                ThreadPool.QueueUserWorkItem(_ => Serve(client));
            }
        }

        private void Serve(TcpClient client)
        {
            using (client)
            {
                try
                {
                    client.ReceiveTimeout = 15000;
                    client.SendTimeout = 30000;
                    var stream = client.GetStream();
                    var req = Read(stream);
                    if (req == null) return;
                    ProbeResponse resp;
                    try
                    {
                        resp = _handler(req);
                    }
                    catch (Exception e)
                    {
                        resp = ProbeResponse.Error(500, "exception", e.Message);
                    }

                    Write(stream, resp);
                }
                catch (Exception)
                {
                    // client rớt giữa chừng
                }
            }
        }

        private static ProbeRequest Read(NetworkStream s)
        {
            var buf = new byte[8192];
            var head = new MemoryStream();
            var headerEnd = -1;
            while (headerEnd < 0)
            {
                var n = s.Read(buf, 0, buf.Length);
                if (n <= 0) return null;
                head.Write(buf, 0, n);
                if (head.Length > MAX_HEADER) return null;
                headerEnd = IndexOf(head.GetBuffer(), (int)head.Length, new byte[] { 13, 10, 13, 10 });
            }

            var all = head.ToArray();
            var headerText = Encoding.ASCII.GetString(all, 0, headerEnd);
            var lines = headerText.Split(new[] { "\r\n" }, StringSplitOptions.None);
            var first = lines[0].Split(' ');
            if (first.Length < 2) return null;
            var req = new ProbeRequest { Method = first[0] };
            var target = first[1];
            var q = target.IndexOf('?');
            req.Path = q < 0 ? target : target.Substring(0, q);
            if (q >= 0) req.ParseForm(target.Substring(q + 1));
            for (var i = 1; i < lines.Length; i++)
            {
                var c = lines[i].IndexOf(':');
                if (c > 0) req.Headers[lines[i].Substring(0, c).Trim()] = lines[i].Substring(c + 1).Trim();
            }

            int.TryParse(req.Header("content-length") ?? "0", out var length);
            if (length > MAX_BODY) return null;
            var body = new MemoryStream();
            body.Write(all, headerEnd + 4, all.Length - headerEnd - 4);
            while (body.Length < length)
            {
                var n = s.Read(buf, 0, (int)Math.Min(buf.Length, length - body.Length));
                if (n <= 0) break;
                body.Write(buf, 0, n);
            }

            if (length > 0 && (req.Header("content-type") ?? "").Contains("x-www-form-urlencoded"))
                req.ParseForm(Encoding.UTF8.GetString(body.ToArray()));
            return req;
        }

        private static int IndexOf(byte[] data, int length, byte[] pattern)
        {
            for (var i = 0; i <= length - pattern.Length; i++)
            {
                var ok = true;
                for (var k = 0; k < pattern.Length; k++)
                {
                    if (data[i + k] == pattern[k]) continue;
                    ok = false;
                    break;
                }

                if (ok) return i;
            }

            return -1;
        }

        private static void Write(NetworkStream s, ProbeResponse r)
        {
            var reason = r.Status == 200 ? "OK" : "Error";
            var header = "HTTP/1.1 " + r.Status + " " + reason + "\r\nContent-Type: " + r.ContentType + "\r\nContent-Length: " + r.Body.Length +
                         "\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n";
            var hb = Encoding.ASCII.GetBytes(header);
            s.Write(hb, 0, hb.Length);
            s.Write(r.Body, 0, r.Body.Length);
            s.Flush();
        }
    }
}
#endif
