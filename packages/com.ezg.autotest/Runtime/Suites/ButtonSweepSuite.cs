using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Ezg.AutoTest
{
    /// <summary>
    ///     Suite "Button Sweep": mở từng màn hình rồi bấm lần lượt mọi nút bấm được (trừ blacklist: mua thật, xoá data,
    ///     mở link…). Mỗi lần bấm theo dõi exception/error log gán đúng cho nút đó, màn mới mở ra (tự đóng lại), màn
    ///     đang test bị đóng (tự mở lại) và chuyển scene.
    /// </summary>
    public sealed class ButtonSweepSuite : AutoTestSuite
    {
        /// <summary>Thời gian dự phòng mỗi nút ngoài waitAfterClick (đóng màn mở ra, mở lại màn đang test).</summary>
        const float PER_BUTTON_OVERHEAD_SECONDS = 3f;

        const float BASE_TIMEOUT_SECONDS = 60f;
        const int MAX_SKIPPED_IN_LOG = 30;
        const int MAX_ERROR_ISSUES_PER_SCREEN = 20;
        const int LABEL_TEXT_LENGTH = 24;
        const int TITLE_MESSAGE_LENGTH = 140;

        public override string Id => "button-sweep";
        public override string DisplayName => "Button Sweep";

        public override string Description =>
            "Mở từng màn hình và bấm lần lượt mọi nút bấm được (bỏ qua nút blacklist: mua thật, xoá dữ liệu, mở link, " +
            "nút có giá tiền…). Bắt exception/error log phát sinh sau mỗi lần bấm và gán cho đúng nút, tự đóng màn mới " +
            "mở ra, tự mở lại màn đang test nếu nút đóng nó, ghi nhận nút chuyển scene.";

        public override ExecutionMode Mode => ExecutionMode.Play;
        public override int Order => 40;
        public override string Icon => "d_EventSystem Icon";
        public override bool MutatesPlayerData => true;

        public override IEnumerable<AutoTestCase> BuildCases(AutoTestBuildContext ctx)
        {
            var config = ctx?.Config ?? new AutoTestConfig();
            return UiAuditUtil.BuildScreenCases(ctx, Describe, Sweep, CaseTimeout(config));
        }

        static string Describe(AutoTestFeature feature)
        {
            return feature == null
                ? "Bấm lần lượt mọi nút trên các Canvas đang hiển thị (adapter không mở được từng màn)."
                : $"Mở màn {feature.Name} và bấm lần lượt mọi nút bấm được.";
        }

        static float CaseTimeout(AutoTestConfig config)
        {
            var sweep = config.buttonSweep;
            var perButton = Mathf.Max(0f, sweep.waitAfterClickSeconds) + PER_BUTTON_OVERHEAD_SECONDS;
            var work = Mathf.Max(1, sweep.maxButtonsPerScreen) * perButton + BASE_TIMEOUT_SECONDS;
            return UiAuditUtil.ScreenCaseTimeout(config, work);
        }

        static async Task Sweep(AutoTestContext ctx, ScreenScope scope)
        {
            var run = new SweepRun(ctx, scope);
            try
            {
                await run.Execute();
            }
            finally
            {
                run.WriteMetrics();
            }
        }

        /// <summary>Nút cần bấm — lưu đủ thông tin để tìm lại khi màn bị dựng lại giữa chừng.</summary>
        sealed class ButtonTarget
        {
            public Selectable Ref;
            public string Path;
            public string Name;
            public string Label;
            public string RootName;
            public readonly List<int> IndexPath = new();

            public static ButtonTarget Create(Selectable sel, Transform root, UiDriver ui)
            {
                var target = new ButtonTarget
                {
                    Ref = sel,
                    Path = UiDriver.PathOf(sel.transform),
                    Name = sel.name,
                    RootName = root.name
                };
                for (var t = sel.transform; t != null && t != root; t = t.parent) target.IndexPath.Add(t.GetSiblingIndex());
                target.IndexPath.Reverse();
                var text = ui.GetText(sel.gameObject);
                var plain = string.IsNullOrWhiteSpace(text) ? null : UiAuditUtil.StripRichText(text).Trim();
                target.Label = string.IsNullOrEmpty(plain)
                    ? sel.name
                    : $"{sel.name} \"{UiAuditUtil.Excerpt(plain, LABEL_TEXT_LENGTH)}\"";
                return target;
            }
        }

        /// <summary>Một lượt quét nút của một màn.</summary>
        sealed class SweepRun
        {
            readonly AutoTestContext _ctx;
            readonly ScreenScope _scope;
            readonly ButtonSweepConfig _cfg;
            readonly List<ButtonTarget> _targets = new();
            readonly HashSet<string> _errorKeys = new();
            int _clicked;
            int _skipped;
            int _errors;
            int _reportedErrors;

            public SweepRun(AutoTestContext ctx, ScreenScope scope)
            {
                _ctx = ctx;
                _scope = scope;
                _cfg = ctx.Config.buttonSweep;
            }

            public async Task Execute()
            {
                using (_ctx.Step("Liệt kê nút"))
                {
                    CollectTargets();
                }

                if (_targets.Count == 0)
                {
                    _ctx.Log($"{_scope.Name}: không có nút nào bấm được (ngoài blacklist).");
                    return;
                }

                for (var i = 0; i < _targets.Count; i++)
                {
                    _ctx.ThrowIfCancelled();
                    var target = _targets[i];
                    _ctx.Session.ReportProgress(_ctx.Result, $"Bấm {i + 1}/{_targets.Count}: {target.Label}");
                    bool keepGoing;
                    using (_ctx.Step($"Bấm '{target.Label}'"))
                    {
                        keepGoing = await ClickOne(target);
                    }

                    if (keepGoing) continue;
                    var left = _targets.Count - i - 1;
                    if (left > 0)
                    {
                        _skipped += left;
                        _ctx.Log($"Dừng quét {_scope.Name}, bỏ {left} nút còn lại.");
                    }

                    break;
                }
            }

            public void WriteMetrics()
            {
                _ctx.Metric("Nút đã bấm", _clicked);
                _ctx.Metric("Nút bỏ qua", _skipped);
                _ctx.Metric("Lỗi khi bấm", _errors);
            }

            void CollectTargets()
            {
                var skippedNames = new List<string>();
                var overCap = 0;
                var max = Mathf.Max(1, _cfg.maxButtonsPerScreen);
                foreach (var root in _scope.GetRoots())
                {
                    if (root == null) continue;
                    foreach (var sel in _ctx.Ui.GetClickables(root))
                    {
                        if (sel == null) continue;
                        if (UiAuditUtil.IsBlacklisted(_ctx.Ui, sel.gameObject, _cfg.blacklistPatterns, out var reason))
                        {
                            skippedNames.Add($"{sel.name} ({reason})");
                            continue;
                        }

                        if (_targets.Count >= max)
                        {
                            overCap++;
                            continue;
                        }

                        _targets.Add(ButtonTarget.Create(sel, root.transform, _ctx.Ui));
                    }
                }

                _skipped += skippedNames.Count + overCap;
                if (skippedNames.Count > 0)
                {
                    var shown = skippedNames.Count > MAX_SKIPPED_IN_LOG
                        ? skippedNames.GetRange(0, MAX_SKIPPED_IN_LOG)
                        : skippedNames;
                    var more = skippedNames.Count > MAX_SKIPPED_IN_LOG ? $"; … +{skippedNames.Count - MAX_SKIPPED_IN_LOG}" : "";
                    _ctx.Log($"Bỏ qua {skippedNames.Count} nút blacklist: {string.Join("; ", shown)}{more}");
                }

                if (overCap > 0)
                    _ctx.Log($"Chỉ bấm {max} nút đầu (maxButtonsPerScreen), bỏ {overCap} nút còn lại.");
                _ctx.Log($"{_scope.Name}: sẽ bấm {_targets.Count} nút.");
            }

            /// <summary>Bấm một nút + xử lý hậu quả. Trả false nếu phải dừng quét màn này.</summary>
            async Task<bool> ClickOne(ButtonTarget target)
            {
                var go = Resolve(target);
                if (go == null)
                {
                    _skipped++;
                    _ctx.Log($"Bỏ qua '{target.Label}': không còn tìm thấy (màn đã dựng lại?).");
                    return true;
                }

                // Màn có thể đã dựng lại (list đổi thứ tự) → kiểm blacklist lại trên object thực sự sắp bấm.
                if (UiAuditUtil.IsBlacklisted(_ctx.Ui, go, _cfg.blacklistPatterns, out var blacklistReason))
                {
                    _skipped++;
                    _ctx.Log($"Bỏ qua '{target.Label}': {blacklistReason}.");
                    return true;
                }

                if (!_ctx.Ui.IsClickable(go, out var reason))
                {
                    _skipped++;
                    _ctx.Log($"Bỏ qua '{target.Label}': {reason}.");
                    return true;
                }

                var openBefore = new HashSet<string>();
                foreach (var f in UiAuditUtil.OpenFeatures(_ctx)) openBefore.Add(f.Name);
                var sceneBefore = SceneManager.GetActiveScene().handle;
                var cursor = AutoTestLogCapture.Cursor;

                if (!await _ctx.Ui.Click(go))
                {
                    _skipped++;
                    return true;
                }

                _clicked++;
                if (_cfg.waitAfterClickSeconds > 0f) await _ctx.WaitSeconds(_cfg.waitAfterClickSeconds);
                else await _ctx.NextFrame();

                await ReportErrors(target, cursor);

                var scene = SceneManager.GetActiveScene();
                if (scene.handle != sceneBefore)
                {
                    await HandleSceneChange(target, scene.name);
                    return false;
                }

                await CloseSpawned(target, openBefore);
                return await EnsureScreenStillOpen(target);
            }

            GameObject Resolve(ButtonTarget target)
            {
                if (target.Ref != null && target.Ref.gameObject.activeInHierarchy) return target.Ref.gameObject;
                var root = CurrentRoot(target);
                if (root != null)
                {
                    var t = root.transform;
                    foreach (var index in target.IndexPath)
                    {
                        if (t == null || index < 0 || index >= t.childCount)
                        {
                            t = null;
                            break;
                        }

                        t = t.GetChild(index);
                    }

                    if (t != null && t.name == target.Name && t.gameObject.activeInHierarchy)
                    {
                        var sel = t.GetComponent<Selectable>();
                        if (sel != null) target.Ref = sel;
                        return t.gameObject;
                    }
                }

                // Lựa chọn cuối: tìm theo đường dẫn đầy đủ (có thể trúng object trùng tên).
                return _ctx.Ui.Find(target.Path);
            }

            GameObject CurrentRoot(ButtonTarget target)
            {
                if (!_scope.IsCurrentScreen)
                {
                    if (_scope.Root != null) return _scope.Root;
                    try
                    {
                        return _ctx.Game.GetFeatureObject(_scope.Feature);
                    }
                    catch (Exception)
                    {
                        return null;
                    }
                }

                foreach (var canvas in UiAuditUtil.ActiveRootCanvases())
                    if (canvas.name == target.RootName)
                        return canvas.gameObject;
                return null;
            }

            async Task ReportErrors(ButtonTarget target, long cursor)
            {
                var errors = UiAuditUtil.NewErrors(_ctx.Config, ref cursor);
                if (errors.Count == 0) return;
                _errors += errors.Count;

                AutoTestLogCapture.Entry first = default;
                var found = false;
                foreach (var e in errors)
                {
                    if (!_errorKeys.Add(e.Type + "|" + UiAuditUtil.FirstLine(e.Message))) continue;
                    first = e;
                    found = true;
                    break;
                }

                if (!found)
                {
                    _ctx.Log($"'{target.Label}' lặp lại lỗi đã báo: {UiAuditUtil.FirstLine(errors[0].Message)}");
                    return;
                }

                if (_reportedErrors >= MAX_ERROR_ISSUES_PER_SCREEN)
                {
                    _ctx.Log($"'{target.Label}' gây lỗi (đã quá {MAX_ERROR_ISSUES_PER_SCREEN} issue): {UiAuditUtil.FirstLine(first.Message)}");
                    return;
                }

                _reportedErrors++;
                var isException = first.Type == LogType.Exception;
                var severity = UiAuditUtil.ErrorSeverity(_ctx.Config, first.Type) ?? Severity.Major;
                var firstLine = UiAuditUtil.Excerpt(UiAuditUtil.FirstLine(first.Message), TITLE_MESSAGE_LENGTH);
                var message = first.Message;
                if (errors.Count > 1) message += $"\n(+{errors.Count - 1} dòng lỗi khác ngay sau lần bấm này)";
                var issue = _ctx.Report(severity, isException ? "ButtonSweep.Exception" : "ButtonSweep.Error",
                    $"Bấm '{UiAuditUtil.ShortPath(target.Path)}' gây {(isException ? "exception" : "lỗi")}: {firstLine}",
                    message, _scope.Name, target.Path, "Không có lỗi", firstLine, ReproSteps(target));
                issue.stackTrace = AutoTestExecutor.ShortStack(first.Stack);
                issue.screenshot = await _ctx.Screenshot("click_error");
            }

            async Task HandleSceneChange(ButtonTarget target, string newScene)
            {
                _ctx.Report(Severity.Info, "ButtonSweep.SceneChange", $"Nút chuyển scene — {target.Name}",
                    $"Bấm '{target.Label}' chuyển sang scene '{newScene}'. Dừng quét các nút còn lại của màn này.",
                    _scope.Name, target.Path, null, null, ReproSteps(target));

                var general = _ctx.Config.general;
                using (_ctx.Step("Chờ game sẵn sàng sau khi chuyển scene"))
                {
                    var ready = await _ctx.WaitUntil(() => UiAuditUtil.IsGameReady(_ctx), general.bootTimeoutSeconds,
                        "game sẵn sàng sau khi chuyển scene", false);
                    if (!ready)
                    {
                        string state;
                        try
                        {
                            state = _ctx.Game.DescribeState();
                        }
                        catch (Exception e)
                        {
                            state = e.Message;
                        }

                        _ctx.Report(Severity.Major, "ButtonSweep.SceneStuck",
                            "Game không sẵn sàng lại sau khi chuyển scene",
                            $"Sau {general.bootTimeoutSeconds:0}s game vẫn chưa sẵn sàng. Trạng thái: {state}",
                            _scope.Name, target.Path, null, null, ReproSteps(target));
                        return;
                    }

                    if (general.settleSecondsAfterBoot > 0f) await _ctx.WaitSeconds(general.settleSecondsAfterBoot);
                }
            }

            async Task CloseSpawned(ButtonTarget target, HashSet<string> openBefore)
            {
                var spawned = new List<AutoTestFeature>();
                foreach (var f in UiAuditUtil.OpenFeatures(_ctx))
                    if (!openBefore.Contains(f.Name))
                        spawned.Add(f);
                if (spawned.Count == 0) return;

                var names = new StringBuilder();
                foreach (var f in spawned)
                {
                    if (names.Length > 0) names.Append(", ");
                    names.Append(f.Name);
                }

                _ctx.Log($"'{target.Label}' mở ra: {names}");
                if (!_cfg.closeSpawnedScreens) return;

                // Đóng từ màn mở sau cùng về trước.
                for (var i = spawned.Count - 1; i >= 0; i--)
                {
                    var f = spawned[i];
                    if (await GameFlow.CloseFeature(_ctx, f)) continue;
                    _ctx.Report(Severity.Minor, "ButtonSweep.CloseFailed", $"Màn mở ra từ nút không đóng được — {f.Name}",
                        $"Bấm '{target.Label}' mở màn {f.Name}, gọi đóng nhưng màn vẫn hiển thị.", _scope.Name,
                        target.Path, "Màn đóng được", "vẫn hiển thị", ReproSteps(target) + $"\n3. Đóng màn {f.Name}");
                }
            }

            /// <summary>Nút đóng màn đang test (nút X) ⇒ mở lại để bấm tiếp; mở lại hỏng ⇒ dừng quét màn này.</summary>
            async Task<bool> EnsureScreenStillOpen(ButtonTarget target)
            {
                if (_scope.IsCurrentScreen) return true;
                bool showing;
                try
                {
                    showing = _ctx.Game.IsFeatureShowing(_scope.Feature);
                }
                catch (Exception)
                {
                    showing = false;
                }

                if (showing) return true;
                _ctx.Log($"'{target.Label}' đã đóng màn {_scope.Name} — mở lại để bấm tiếp.");
                var reopen = await GameFlow.OpenFeature(_ctx, _scope.Feature);
                if (reopen.Error != null)
                {
                    _ctx.Log($"Không mở lại được {_scope.Name}: {reopen.Error}");
                    return false;
                }

                _scope.Root = reopen.Root;
                return true;
            }

            string ReproSteps(ButtonTarget target)
            {
                return $"1. {_scope.ReproOpenStep}\n2. Bấm nút '{target.Path}'";
            }
        }
    }
}
