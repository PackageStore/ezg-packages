using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Ezg.AutoTest
{
    /// <summary>
    ///     Suite "Monkey / Stress": bấm nút ngẫu nhiên, tap/kéo lung tung, thỉnh thoảng đóng màn (giả phím back) trong
    ///     một khoảng thời gian. Bắt exception/error log mới (kèm 10 thao tác cuối + seed để tái hiện), game khựng,
    ///     game kẹt ở trạng thái chưa sẵn sàng (tự đưa về màn nền).
    /// </summary>
    public sealed class MonkeySuite : AutoTestSuite
    {
        /// <summary>Dư thời gian ngoài durationSeconds (dọn dẹp, chụp ảnh lỗi…).</summary>
        const float TIMEOUT_MARGIN_SECONDS = 60f;

        public override string Id => "monkey";
        public override string DisplayName => "Monkey / Stress";

        public override string Description =>
            "Thao tác ngẫu nhiên liên tục (bấm nút, tap, kéo, đóng màn) trong thời gian cấu hình. Bắt exception/error " +
            "log mới kèm các thao tác cuối + seed để tái hiện, phát hiện game khựng và game kẹt. Nút blacklist (mua " +
            "thật, xoá dữ liệu…) không bị bấm.";

        public override ExecutionMode Mode => ExecutionMode.Play;
        public override int Order => 45;
        public override string Icon => "d_Animation.Play";
        public override bool MutatesPlayerData => true;

        public override IEnumerable<AutoTestCase> BuildCases(AutoTestBuildContext ctx)
        {
            var config = ctx?.Config ?? new AutoTestConfig();
            // Monkey thường là case đầu phiên Play → timeout phải gồm cả thời gian boot.
            var timeout = Mathf.Max(0f, config.monkey.durationSeconds) + TIMEOUT_MARGIN_SECONDS +
                          config.general.bootTimeoutSeconds + config.general.settleSecondsAfterBoot;
            yield return new AutoTestCase("monkey", "Monkey ngẫu nhiên",
                "Thao tác ngẫu nhiên liên tục trên UI đang hiển thị; seed được ghi lại để tái hiện.", RunMonkey,
                "Stress", timeout);
        }

        static async Task RunMonkey(AutoTestContext ctx)
        {
            await GameFlow.EnsureReady(ctx);
            var cfg = ctx.Config.monkey;
            var seed = cfg.seed != 0 ? cfg.seed : Environment.TickCount;
            ctx.Log($"========== MONKEY SEED: {seed} ========== (đặt seed này trong Settings > Monkey để tái hiện)");
            ctx.Report(Severity.Info, "Monkey.Seed", $"Seed: {seed} — đặt seed này trong Settings để tái hiện",
                $"Seed {seed}, {UiAuditUtil.Num(cfg.durationSeconds, "0")}s, {UiAuditUtil.Num(cfg.actionsPerSecond, "0.#")} thao tác/giây.",
                UiAuditUtil.CurrentScreenName());

            var run = new MonkeyRun(ctx, seed);
            try
            {
                await run.Loop();
            }
            finally
            {
                run.Finish();
                await GameFlow.ReturnToBaseline(ctx);
            }
        }

        /// <summary>State của một lượt monkey.</summary>
        sealed class MonkeyRun
        {
            const float MIN_ACTIONS_PER_SECOND = 0.5f;
            const float MAX_ACTIONS_PER_SECOND = 30f;
            const float MIN_DURATION_SECONDS = 1f;
            const double BACK_PROBABILITY = 0.05;
            const double DRAG_RATIO = 0.2;
            const float MIN_DRAG_SECONDS = 0.15f;
            const float MAX_DRAG_SECONDS = 0.45f;
            const int MAX_PICK_ATTEMPTS = 12;
            const int MAX_TAP_ATTEMPTS = 4;
            const int RING_CAPACITY = 30;
            const int STEPS_IN_ISSUE = 10;
            const int MAX_FULL_LOG_LINES = 5000;
            const int MAX_ERROR_ISSUES = 30;
            const int MAX_FREEZE_ISSUES = 5;
            const float FREEZE_THRESHOLD_SECONDS = 5f;
            const double READY_POLL_SECONDS = 1;
            const double RECOVER_CHECK_SECONDS = 15;
            const double STUCK_SECONDS = 10;
            const double PROGRESS_SECONDS = 1;
            const int SCREENSHOT_IGNORE_FRAMES = 3;
            const int TITLE_MESSAGE_LENGTH = 140;
            const double MS_PER_SECOND = 1000;

            readonly AutoTestContext _ctx;
            readonly MonkeyConfig _cfg;
            readonly UiDriver _ui;
            readonly int _seed;
            readonly System.Random _rng;
            readonly List<string> _blacklist;
            readonly Queue<string> _ring = new();
            readonly StringBuilder _fullLog = new();
            readonly HashSet<string> _errorKeys = new();
            Selectable[] _selectables = new Selectable[64];
            MonkeyFrameSampler _sampler;
            double _start;
            long _logCursor;
            string _lastTarget;
            int _fullLogLines;
            int _actions;
            int _buttonTaps;
            int _taps;
            int _drags;
            int _backs;
            int _uniqueErrors;
            int _repeatedErrors;
            int _reportedErrors;
            int _freezes;
            int _recoveries;
            bool _stuckReported;
            bool _finished;

            public MonkeyRun(AutoTestContext ctx, int seed)
            {
                _ctx = ctx;
                _cfg = ctx.Config.monkey;
                _ui = ctx.Ui;
                _seed = seed;
                _rng = new System.Random(seed);
                _blacklist = _cfg.respectBlacklist ? ctx.Config.buttonSweep.blacklistPatterns : null;
            }

            public async Task Loop()
            {
                var interval = 1.0 / Mathf.Clamp(_cfg.actionsPerSecond, MIN_ACTIONS_PER_SECOND, MAX_ACTIONS_PER_SECOND);
                var duration = Math.Max(MIN_DURATION_SECONDS, _cfg.durationSeconds);
                _start = AutoTestClock.Now;
                var endAt = _start + duration;
                var nextAction = _start;
                var nextReadyPoll = _start;
                var nextRecoverCheck = _start + RECOVER_CHECK_SECONDS;
                var nextProgress = _start;
                var notReadySince = -1.0;
                _logCursor = AutoTestLogCapture.Cursor;
                _sampler = MonkeyFrameSampler.Create(FREEZE_THRESHOLD_SECONDS);

                using (_ctx.Step($"Monkey {UiAuditUtil.Num(duration, "0")}s (seed {_seed})"))
                {
                    while (AutoTestClock.Now < endAt)
                    {
                        _ctx.ThrowIfCancelled();
                        var now = AutoTestClock.Now;
                        if (now < nextAction)
                        {
                            await _ctx.WaitSeconds(Math.Min(nextAction, endAt) - now);
                            continue;
                        }

                        await DoRandomAction();
                        // Không dồn thao tác bù khi một thao tác (kéo, đóng màn) chạy lâu.
                        nextAction = Math.Max(nextAction + interval, AutoTestClock.Now);

                        await CheckErrors();
                        CheckFreezes();

                        now = AutoTestClock.Now;
                        if (now >= nextProgress)
                        {
                            nextProgress = now + PROGRESS_SECONDS;
                            _ctx.Session.ReportProgress(_ctx.Result,
                                $"Monkey {UiAuditUtil.Num(now - _start, "0")}/{UiAuditUtil.Num(duration, "0")}s — {_actions} thao tác, {_uniqueErrors} lỗi");
                        }

                        if (now >= nextReadyPoll)
                        {
                            nextReadyPoll = now + READY_POLL_SECONDS;
                            if (UiAuditUtil.IsGameReady(_ctx)) notReadySince = -1;
                            else if (notReadySince < 0) notReadySince = now;
                        }

                        if (now >= nextRecoverCheck)
                        {
                            nextRecoverCheck = now + RECOVER_CHECK_SECONDS;
                            if (notReadySince >= 0 && now - notReadySince > STUCK_SECONDS)
                            {
                                await Recover(now - notReadySince);
                                notReadySince = -1;
                            }
                        }
                    }
                }

                // Lỗi phát sinh ngay sau thao tác cuối.
                await CheckErrors();
                CheckFreezes();
            }

            /// <summary>Ghi số đo + nhật ký thao tác, huỷ sampler. Gọi một lần trong finally.</summary>
            public void Finish()
            {
                if (_finished) return;
                _finished = true;

                double maxFrameMs = 0;
                double avgFps = 0;
                if (_sampler != null)
                {
                    maxFrameMs = _sampler.MaxFrameSeconds * MS_PER_SECOND;
                    avgFps = _sampler.TotalSeconds > 0 ? _sampler.Frames / _sampler.TotalSeconds : 0;
                    _sampler.Dispose();
                    _sampler = null;
                }

                Array.Clear(_selectables, 0, _selectables.Length);
                _ctx.Metric("Số thao tác", _actions);
                _ctx.Metric("Bấm nút", _buttonTaps);
                _ctx.Metric("Tap ngẫu nhiên", _taps);
                _ctx.Metric("Kéo", _drags);
                _ctx.Metric("Back (đóng màn)", _backs);
                _ctx.Metric("Lỗi (không trùng)", _uniqueErrors);
                _ctx.Metric("Frame dài nhất", maxFrameMs, "ms");
                _ctx.Metric("FPS trung bình", avgFps, "fps");
                _ctx.Metric("Lần phục hồi", _recoveries);
                if (_repeatedErrors > 0) _ctx.Log($"{_repeatedErrors} lỗi lặp lại (đã gộp theo dòng đầu).");

                var header = $"Seed: {_seed}\nThời lượng: {UiAuditUtil.Num(_cfg.durationSeconds, "0")}s, " +
                             $"{UiAuditUtil.Num(_cfg.actionsPerSecond, "0.#")} thao tác/giây\n" +
                             $"Tổng thao tác: {_actions}\n\n";
                _ctx.Attach("monkey_actions.txt", header + _fullLog, "Nhật ký thao tác monkey");
            }

            #region Thao tác

            async Task DoRandomAction()
            {
                if (_cfg.allowBackKey && _rng.NextDouble() < BACK_PROBABILITY && await TryBack()) return;

                if (_rng.NextDouble() < _cfg.buttonTapRatio)
                {
                    var button = PickButton();
                    if (button != null)
                    {
                        var path = UiDriver.PathOf(button.transform);
                        Record("Bấm nút", path);
                        if (await _ui.Click(button)) _buttonTaps++;
                        return;
                    }
                }

                if (_rng.NextDouble() < DRAG_RATIO) await RandomDrag();
                else await RandomTap();
            }

            /// <summary>Giả phím back: đóng màn không thuộc màn nền mở sau cùng.</summary>
            async Task<bool> TryBack()
            {
                if (!UiAuditUtil.SupportsFeatures(_ctx.Game)) return false;
                var open = UiAuditUtil.OpenFeatures(_ctx);
                AutoTestFeature last = null;
                for (var i = open.Count - 1; i >= 0; i--)
                {
                    if (GameFlow.IsBaseline(_ctx.Session, open[i])) continue;
                    last = open[i];
                    break;
                }

                if (last == null) return false;
                Record("Back (đóng màn)", last.Name);
                _backs++;
                try
                {
                    _ctx.Game.CloseFeature(last);
                }
                catch (Exception e)
                {
                    var key = "close|" + last.Name;
                    if (_errorKeys.Add(key))
                        _ctx.Report(Severity.Major, "Monkey.Close", $"Đóng màn {last.Name} ném exception",
                            $"{e.GetType().Name}: {e.Message}", last.Name, null, "Đóng được", e.GetType().Name,
                            BuildSteps()).stackTrace = AutoTestExecutor.ShortStack(e.StackTrace);
                }

                await _ctx.NextFrame();
                return true;
            }

            /// <summary>Nút ngẫu nhiên đang bấm được (không blacklist); null nếu không tìm thấy sau vài lần thử.</summary>
            GameObject PickButton()
            {
                var count = Selectable.allSelectableCount;
                if (count <= 0) return null;
                if (_selectables.Length < count) _selectables = new Selectable[Mathf.NextPowerOfTwo(count)];
                var n = Selectable.AllSelectablesNoAlloc(_selectables);
                if (n <= 0) return null;
                for (var attempt = 0; attempt < MAX_PICK_ATTEMPTS; attempt++)
                {
                    var sel = _selectables[_rng.Next(n)];
                    if (sel == null) continue;
                    if (!(sel is Button) && !(sel is Toggle) && sel.GetComponent<IPointerClickHandler>() == null) continue;
                    var go = sel.gameObject;
                    if (_blacklist != null && UiAuditUtil.IsBlacklisted(_ui, go, _blacklist, out _)) continue;
                    if (!_ui.IsClickable(go, out _)) continue;
                    return go;
                }

                return null;
            }

            async Task RandomTap()
            {
                var safe = SafeRect();
                var pos = Vector2.zero;
                var allowed = false;
                for (var attempt = 0; attempt < MAX_TAP_ATTEMPTS; attempt++)
                {
                    pos = RandomPoint(safe);
                    if (IsBlacklistedAt(pos)) continue;
                    allowed = true;
                    break;
                }

                if (!allowed)
                {
                    Record("Tap bỏ qua (trúng nút blacklist)", FormatPoint(pos));
                    return;
                }

                Record("Tap", FormatPoint(pos));
                var hit = await _ui.Tap(pos);
                _taps++;
                if (hit != null) _lastTarget = UiDriver.PathOf(hit.transform);
            }

            async Task RandomDrag()
            {
                var safe = SafeRect();
                var from = RandomPoint(safe);
                var to = RandomPoint(safe);
                var seconds = Mathf.Lerp(MIN_DRAG_SECONDS, MAX_DRAG_SECONDS, (float)_rng.NextDouble());
                Record("Kéo", $"{FormatPoint(from)} → {FormatPoint(to)}");
                await _ui.Drag(from, to, seconds);
                _drags++;
            }

            bool IsBlacklistedAt(Vector2 pos)
            {
                if (_blacklist == null) return false;
                var top = _ui.RaycastTop(pos);
                if (top == null) return false;
                var handler = ExecuteEvents.GetEventHandler<IPointerClickHandler>(top);
                return handler != null && UiAuditUtil.IsBlacklisted(_ui, handler, _blacklist, out _);
            }

            static Rect SafeRect()
            {
                var safe = Screen.safeArea;
                return safe.width > 0f && safe.height > 0f ? safe : new Rect(0f, 0f, Screen.width, Screen.height);
            }

            Vector2 RandomPoint(Rect area)
            {
                return new Vector2(area.x + (float)_rng.NextDouble() * area.width,
                    area.y + (float)_rng.NextDouble() * area.height);
            }

            static string FormatPoint(Vector2 p)
            {
                return $"({UiAuditUtil.Num(p.x, "0")}, {UiAuditUtil.Num(p.y, "0")})";
            }

            void Record(string kind, string target)
            {
                _actions++;
                _lastTarget = target;
                var t = (AutoTestClock.Now - _start).ToString("0.0", CultureInfo.InvariantCulture);
                var line = $"[{t}s] {kind}: {target}";
                _ring.Enqueue(line);
                while (_ring.Count > RING_CAPACITY) _ring.Dequeue();
                if (_fullLogLines >= MAX_FULL_LOG_LINES) return;
                _fullLog.AppendLine(line);
                _fullLogLines++;
            }

            #endregion

            #region Phát hiện lỗi

            async Task CheckErrors()
            {
                var errors = UiAuditUtil.NewErrors(_ctx.Config, ref _logCursor);
                foreach (var e in errors)
                {
                    var firstLine = UiAuditUtil.FirstLine(e.Message);
                    if (!_errorKeys.Add(e.Type + "|" + firstLine))
                    {
                        _repeatedErrors++;
                        continue;
                    }

                    _uniqueErrors++;
                    if (_reportedErrors >= MAX_ERROR_ISSUES) continue;
                    _reportedErrors++;
                    var isException = e.Type == LogType.Exception;
                    var severity = UiAuditUtil.ErrorSeverity(_ctx.Config, e.Type) ?? Severity.Major;
                    var title = $"Monkey: {(isException ? "Exception" : "Error log")} — " +
                                UiAuditUtil.Excerpt(firstLine, TITLE_MESSAGE_LENGTH);
                    var issue = _ctx.Report(severity, isException ? "Monkey.Exception" : "Monkey.Error", title,
                        e.Message, UiAuditUtil.CurrentScreenName(), _lastTarget, "Không có lỗi",
                        UiAuditUtil.Excerpt(firstLine, TITLE_MESSAGE_LENGTH), BuildSteps());
                    issue.stackTrace = AutoTestExecutor.ShortStack(e.Stack);
                    issue.screenshot = await ScreenshotQuietly("monkey_error");
                }
            }

            void CheckFreezes()
            {
                if (_sampler == null) return;
                while (_sampler.TryTakeFreeze(out var seconds))
                {
                    _freezes++;
                    if (_freezes > MAX_FREEZE_ISSUES) continue;
                    var s = UiAuditUtil.Num(seconds, "0.#");
                    _ctx.Report(Severity.Major, "Monkey.Freeze", $"Game khựng {s}s",
                        $"Một frame kéo dài {s}s (ngưỡng {UiAuditUtil.Num(FREEZE_THRESHOLD_SECONDS, "0")}s) ngay sau các thao tác dưới. " +
                        "Nếu Editor bị pause / mất focus lúc đó thì bỏ qua.", UiAuditUtil.CurrentScreenName(), _lastTarget,
                        $"< {UiAuditUtil.Num(FREEZE_THRESHOLD_SECONDS, "0")}s", $"{s}s", BuildSteps());
                }
            }

            async Task Recover(double stuckSeconds)
            {
                _recoveries++;
                var s = UiAuditUtil.Num(stuckSeconds, "0");
                Record("Phục hồi", $"game chưa sẵn sàng {s}s → đưa về màn nền");
                if (!_stuckReported)
                {
                    _stuckReported = true;
                    string state;
                    try
                    {
                        state = _ctx.Game.DescribeState();
                    }
                    catch (Exception e)
                    {
                        state = e.Message;
                    }

                    _ctx.Report(Severity.Minor, "Monkey.Stuck", "Game kẹt ở trạng thái chưa sẵn sàng",
                        $"Game báo chưa sẵn sàng liên tục {s}s (khoá chạm / loading không tắt?). Trạng thái: {state}. " +
                        "Đã tự đưa về màn nền để chạy tiếp.", UiAuditUtil.CurrentScreenName(), _lastTarget,
                        "Game sẵn sàng lại sau thao tác", $"kẹt {s}s", BuildSteps());
                }

                await GameFlow.ReturnToBaseline(_ctx);
            }

            async Task<string> ScreenshotQuietly(string label)
            {
                // Chụp + encode PNG làm frame dài → không tính vào đo khựng/FPS.
                _sampler?.IgnoreNextFrames(SCREENSHOT_IGNORE_FRAMES);
                var shot = await _ctx.Screenshot(label);
                _sampler?.IgnoreNextFrames(SCREENSHOT_IGNORE_FRAMES);
                return shot;
            }

            string BuildSteps()
            {
                var sb = new StringBuilder();
                sb.Append("Seed: ").Append(_seed).Append(" (đặt trong Settings > Monkey > seed để tái hiện)\n");
                sb.Append("Scene: ").Append(SceneManager.GetActiveScene().name).Append('\n');
                sb.Append("Thời điểm: ").Append((AutoTestClock.Now - _start).ToString("0.0", CultureInfo.InvariantCulture))
                    .Append("s sau khi bắt đầu\n");
                sb.Append(STEPS_IN_ISSUE).Append(" thao tác cuối:\n");
                var skip = Math.Max(0, _ring.Count - STEPS_IN_ISSUE);
                var index = 0;
                var number = 1;
                foreach (var line in _ring)
                {
                    if (index++ < skip) continue;
                    sb.Append(number++).Append(". ").Append(line).Append('\n');
                }

                return sb.ToString().TrimEnd();
            }

            #endregion
        }
    }

    /// <summary>Đo thời gian từng frame trong lúc monkey chạy (max frame, FPS trung bình, frame khựng).</summary>
    [AddComponentMenu("")]
    internal sealed class MonkeyFrameSampler : MonoBehaviour
    {
        readonly Queue<double> _freezes = new();
        float _freezeThreshold;
        double _last = -1;
        int _ignoreUntilFrame;

        public int Frames { get; private set; }
        public double TotalSeconds { get; private set; }
        public double MaxFrameSeconds { get; private set; }

        public static MonkeyFrameSampler Create(float freezeThresholdSeconds)
        {
            var go = new GameObject("[EZG AutoTest Monkey Sampler]") { hideFlags = HideFlags.HideAndDontSave };
            if (Application.isPlaying) DontDestroyOnLoad(go);
            var sampler = go.AddComponent<MonkeyFrameSampler>();
            sampler._freezeThreshold = freezeThresholdSeconds;
            return sampler;
        }

        /// <summary>Bỏ qua thời lượng vài frame tới (frame do chính hệ thống test làm chậm).</summary>
        public void IgnoreNextFrames(int frames)
        {
            _ignoreUntilFrame = Mathf.Max(_ignoreUntilFrame, Time.frameCount + frames);
        }

        public bool TryTakeFreeze(out double seconds)
        {
            if (_freezes.Count == 0)
            {
                seconds = 0;
                return false;
            }

            seconds = _freezes.Dequeue();
            return true;
        }

        public void Dispose()
        {
            if (this != null) Object.Destroy(gameObject);
        }

        void Update()
        {
            var now = Time.realtimeSinceStartupAsDouble;
            if (_last < 0)
            {
                _last = now;
                return;
            }

            var dt = now - _last;
            _last = now;
            if (Time.frameCount <= _ignoreUntilFrame) return;
            Frames++;
            TotalSeconds += dt;
            if (dt > MaxFrameSeconds) MaxFrameSeconds = dt;
            if (dt >= _freezeThreshold) _freezes.Enqueue(dt);
        }
    }
}
