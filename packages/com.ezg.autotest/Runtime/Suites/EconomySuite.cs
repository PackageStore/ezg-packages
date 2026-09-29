using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Ezg.AutoTest
{
    /// <summary>
    ///     Logic / Economy: tính đúng đắn của tiền tệ (cộng/trừ, chặn tiêu quá, số âm, tràn số), lưu/đọc lại,
    ///     phát thưởng, mua bằng tiền mềm và độ hợp lệ của bảng config. Mọi case trả số dư về như cũ; runner còn
    ///     sao lưu PlayerPrefs quanh suite nên save của dev không bị ảnh hưởng.
    /// </summary>
    public sealed class EconomySuite : AutoTestSuite
    {
        const double EPSILON = 0.0001;
        const double OVERFLOW_DOUBLE_BASE = 1e15;
        const int MAX_CONFIG_ISSUES = 300;
        const double GROUPING_DUPLICATE_RATIO = 0.5;

        public override string Id => "economy";
        public override string DisplayName => "Logic / Economy";

        public override string Description =>
            "Kiểm tra tiền tệ qua adapter: đọc số dư, cộng/trừ khớp, chặn tiêu quá số dư, không nhận số âm, " +
            "không tràn số, save → load giữ nguyên, phát thưởng cộng đúng ví, mua bằng tiền mềm trừ đúng, " +
            "bảng config không có giá trị âm/NaN/id trùng.";

        public override ExecutionMode Mode => ExecutionMode.Play;
        public override int Order => 50;
        public override string Icon => "d_Profiler.Memory";
        public override bool MutatesPlayerData => true;

        public override IEnumerable<AutoTestCase> BuildCases(AutoTestBuildContext ctx)
        {
            var cfg = ctx.Config.economy;
            yield return Case("balances", "Đọc số dư mọi loại tiền", "Số dư đọc được, không âm, không NaN/Infinity.",
                Balances);
            yield return Case("add-remove", "Cộng / trừ tiền khớp số",
                "Cộng X rồi trừ X phải về đúng số dư ban đầu; IsEnough đúng biên.", AddRemove);
            yield return Case("overspend", "Chặn tiêu quá số dư",
                "Trừ nhiều hơn số dư phải bị từ chối và số dư không đổi (không bao giờ âm).", Overspend);
            yield return Case("negative-amount", "Không nhận số âm",
                "AddCurrency(-X) không được trừ tiền; RemoveCurrency(-X) không được CỘNG tiền (lỗ hổng).",
                NegativeAmount);
            if (cfg.testOverflow)
                yield return Case("overflow", "Không tràn số", "Số dư rất lớn cộng thêm không bị âm/NaN/Infinity.",
                    Overflow);
            if (cfg.testSaveLoad)
                yield return Case("save-load", "Lưu → đọc lại giữ nguyên",
                    "Đổi số dư, save, load lại từ storage → số dư phải giữ nguyên.", SaveLoad);
            if (cfg.testRewards)
                yield return Case("rewards", "Phát thưởng cộng đúng ví",
                    "Phát thưởng X qua hệ thống reward → đúng loại tiền tăng đúng X.", Rewards);
            if (cfg.testPurchaseOffline)
                yield return Case("purchase-offline", "Mua bằng tiền mềm",
                    "Đủ tiền: giao dịch thành công và trừ đúng giá. Thiếu tiền: bị từ chối và không trừ.",
                    PurchaseOffline);
            if (cfg.testConfigSanity)
                yield return Case("config-sanity", "Bảng config hợp lệ",
                    "Mọi bảng config load được, không rỗng, không có giá/thưởng âm, NaN, id trùng hoặc id rỗng.",
                    ConfigSanity);
        }

        static AutoTestCase Case(string id, string name, string description, Func<AutoTestContext, Task> run)
        {
            return new AutoTestCase(id, name, description, async ctx =>
            {
                await GameFlow.EnsureReady(ctx);
                try
                {
                    await run(ctx);
                }
                finally
                {
                    await GameFlow.ReturnToBaseline(ctx);
                }
            }, "Economy");
        }

        #region Helpers

        static List<AutoTestCurrency> Currencies(AutoTestContext ctx)
        {
            if ((ctx.Game.Capabilities & AdapterCapabilities.Economy) == 0)
                ctx.Skip($"Adapter '{ctx.Game.Name}' không hỗ trợ economy.");
            var list = ctx.Game.GetCurrencies()
                .Where(c => !AutoTestFilters.MatchesAny(c.Name, ctx.Config.economy.skipCurrencies.Select(s => "^" + s + "$")))
                .ToList();
            if (list.Count == 0) ctx.Skip("Không có loại tiền nào để test.");
            return list;
        }

        static bool Near(double a, double b)
        {
            return Math.Abs(a - b) <= EPSILON * Math.Max(1, Math.Max(Math.Abs(a), Math.Abs(b)));
        }

        /// <summary>Đặt số dư về giá trị cũ (SetCurrency nếu có, không thì cộng/trừ phần chênh).</summary>
        static void RestoreBalance(AutoTestContext ctx, AutoTestCurrency c, double original)
        {
            try
            {
                ctx.Game.SetCurrency(c, original);
            }
            catch (NotSupportedException)
            {
                var diff = original - ctx.Game.GetBalance(c);
                if (diff > 0) ctx.Game.AddCurrency(c, diff);
                else if (diff < 0) ctx.Game.RemoveCurrency(c, -diff);
            }
            catch (Exception e)
            {
                ctx.Warn($"Không trả được số dư {c.Name} về {original}", e.Message);
            }
        }

        /// <summary>Chụp số dư mọi loại tiền → khôi phục trong finally.</summary>
        static Dictionary<AutoTestCurrency, double> Snapshot(AutoTestContext ctx, IEnumerable<AutoTestCurrency> currencies)
        {
            var map = new Dictionary<AutoTestCurrency, double>();
            foreach (var c in currencies) map[c] = ctx.Game.GetBalance(c);
            return map;
        }

        static void RestoreAll(AutoTestContext ctx, Dictionary<AutoTestCurrency, double> snapshot)
        {
            foreach (var pair in snapshot) RestoreBalance(ctx, pair.Key, pair.Value);
            ctx.Game.SaveAll();
        }

        static string Fmt(double v)
        {
            return AutoTestContext.Format(v);
        }

        #endregion

        #region Cases

        static Task Balances(AutoTestContext ctx)
        {
            foreach (var c in Currencies(ctx))
            {
                double v;
                try
                {
                    v = ctx.Game.GetBalance(c);
                }
                catch (Exception e)
                {
                    ctx.Report(Severity.Major, "Economy.Read", $"Không đọc được số dư {c.Name}", e.Message, c.Name);
                    continue;
                }

                ctx.Metric($"Số dư {c.Name}", v);
                ctx.Check(!double.IsNaN(v) && !double.IsInfinity(v), $"Số dư {c.Name} không hợp lệ", null,
                    Severity.Critical, c.Name, "số hữu hạn", Fmt(v), "Economy.Invalid");
                ctx.Check(v >= 0, $"Số dư {c.Name} đang âm", null, Severity.Critical, c.Name, "≥ 0", Fmt(v),
                    "Economy.Negative");
            }

            return Task.CompletedTask;
        }

        static Task AddRemove(AutoTestContext ctx)
        {
            var currencies = Currencies(ctx);
            var snap = Snapshot(ctx, currencies);
            var x = ctx.Config.economy.testAmount;
            try
            {
                foreach (var c in currencies)
                    using (ctx.Step($"{c.Name}: cộng rồi trừ {Fmt(x)}"))
                    {
                        var before = ctx.Game.GetBalance(c);
                        ctx.Game.AddCurrency(c, x);
                        var afterAdd = ctx.Game.GetBalance(c);
                        ctx.Check(Near(afterAdd, before + x), $"Cộng {c.Name} sai số", null, Severity.Critical, c.Name,
                            Fmt(before + x), Fmt(afterAdd), "Economy.Add");

                        ctx.Check(ctx.Game.IsEnough(c, afterAdd), $"IsEnough({c.Name}, số dư) trả false",
                            "Có đúng bằng số dư phải được coi là đủ.", Severity.Major, c.Name, "true", "false",
                            "Economy.IsEnough");
                        ctx.Check(!ctx.Game.IsEnough(c, afterAdd + 1), $"IsEnough({c.Name}, số dư + 1) trả true",
                            null, Severity.Critical, c.Name, "false", "true", "Economy.IsEnough");

                        var ok = ctx.Game.RemoveCurrency(c, x);
                        var afterRemove = ctx.Game.GetBalance(c);
                        ctx.Check(ok, $"Trừ {c.Name} bị từ chối dù đủ tiền", null, Severity.Critical, c.Name, "true",
                            "false", "Economy.Remove");
                        ctx.Check(Near(afterRemove, before), $"Trừ {c.Name} sai số", null, Severity.Critical, c.Name,
                            Fmt(before), Fmt(afterRemove), "Economy.Remove");
                    }
            }
            finally
            {
                RestoreAll(ctx, snap);
            }

            return Task.CompletedTask;
        }

        static Task Overspend(AutoTestContext ctx)
        {
            var currencies = Currencies(ctx);
            var snap = Snapshot(ctx, currencies);
            try
            {
                foreach (var c in currencies)
                    using (ctx.Step($"{c.Name}: trừ nhiều hơn số dư"))
                    {
                        var balance = ctx.Game.GetBalance(c);
                        var ok = ctx.Game.RemoveCurrency(c, balance + ctx.Config.economy.testAmount);
                        var after = ctx.Game.GetBalance(c);
                        if (after < 0)
                            ctx.Report(Severity.Blocker, "Economy.Overspend", $"{c.Name} bị âm sau khi tiêu quá số dư",
                                null, c.Name, null, "≥ 0", Fmt(after));
                        else
                            ctx.Check(!ok && Near(after, balance), $"Tiêu quá số dư {c.Name} không bị chặn", null,
                                Severity.Critical, c.Name, $"từ chối, số dư giữ {Fmt(balance)}",
                                $"ok={ok}, số dư {Fmt(after)}", "Economy.Overspend");
                    }
            }
            finally
            {
                RestoreAll(ctx, snap);
            }

            return Task.CompletedTask;
        }

        static Task NegativeAmount(AutoTestContext ctx)
        {
            var currencies = Currencies(ctx);
            var snap = Snapshot(ctx, currencies);
            var x = ctx.Config.economy.testAmount;
            var addLeaks = new List<string>();
            var removeLeaks = new List<string>();
            try
            {
                foreach (var c in currencies)
                    using (ctx.Step($"{c.Name}: gửi số âm"))
                    {
                        ctx.Game.AddCurrency(c, x); // đảm bảo có tiền để thấy bị trừ
                        var baseValue = ctx.Game.GetBalance(c);
                        ctx.Game.AddCurrency(c, -x);
                        var afterAddNeg = ctx.Game.GetBalance(c);
                        if (afterAddNeg < baseValue - EPSILON) addLeaks.Add($"{c.Name}: {Fmt(baseValue)} → {Fmt(afterAddNeg)}");
                        RestoreBalance(ctx, c, baseValue);

                        ctx.Game.RemoveCurrency(c, -x);
                        var afterRemoveNeg = ctx.Game.GetBalance(c);
                        if (afterRemoveNeg > baseValue + EPSILON)
                            removeLeaks.Add($"{c.Name}: {Fmt(baseValue)} → {Fmt(afterRemoveNeg)}");
                    }
            }
            finally
            {
                RestoreAll(ctx, snap);
            }

            // Một issue cho mỗi hàm (liệt kê loại tiền) — lỗi nằm ở hàm dùng chung, không phải từng loại tiền.
            if (addLeaks.Count > 0)
                ctx.Report(Severity.Minor, "Economy.NegativeAdd", "AddCurrency nhận số âm và TRỪ tiền",
                    $"Hàm cộng nên chặn số âm để không bị dùng trừ tiền ngoài luồng. Gọi AddCurrency(-{Fmt(x)}):\n- " +
                    string.Join("\n- ", addLeaks), "AddCurrency", null, "số dư không giảm", $"{addLeaks.Count} loại tiền bị giảm");
            if (removeLeaks.Count > 0)
                ctx.Report(Severity.Major, "Economy.NegativeRemove", "RemoveCurrency nhận số âm và CỘNG tiền",
                    "Hàm trừ không chặn số âm — nếu có đường nào truyền số âm (config sai, input) sẽ thành cộng tiền. " +
                    $"Gọi RemoveCurrency(-{Fmt(x)}):\n- " + string.Join("\n- ", removeLeaks), "RemoveCurrency", null,
                    "số dư không tăng", $"{removeLeaks.Count} loại tiền bị tăng");
            return Task.CompletedTask;
        }

        static Task Overflow(AutoTestContext ctx)
        {
            var currencies = Currencies(ctx);
            var snap = Snapshot(ctx, currencies);
            try
            {
                foreach (var c in currencies)
                    using (ctx.Step($"{c.Name}: số dư cực lớn"))
                    {
                        var type = ctx.Game.BalanceType(c);
                        double big;
                        if (type == typeof(int)) big = int.MaxValue - ctx.Config.economy.testAmount;
                        else if (type == typeof(long)) big = long.MaxValue / 2.0;
                        else big = OVERFLOW_DOUBLE_BASE;
                        try
                        {
                            ctx.Game.SetCurrency(c, big);
                        }
                        catch (NotSupportedException)
                        {
                            ctx.Game.AddCurrency(c, big);
                        }

                        ctx.Game.AddCurrency(c, big);
                        var after = ctx.Game.GetBalance(c);
                        ctx.Check(!double.IsNaN(after) && !double.IsInfinity(after) && after > 0,
                            $"Tràn số {c.Name} khi số dư rất lớn",
                            $"Kiểu lưu: {type?.Name}. Cộng {Fmt(big)} vào {Fmt(big)}.", Severity.Major, c.Name,
                            "> 0 và hữu hạn", Fmt(after), "Economy.Overflow");
                    }
            }
            finally
            {
                RestoreAll(ctx, snap);
            }

            return Task.CompletedTask;
        }

        static Task SaveLoad(AutoTestContext ctx)
        {
            var currencies = Currencies(ctx);
            if ((ctx.Game.Capabilities & AdapterCapabilities.PlayerData) == 0)
                ctx.Skip("Adapter không hỗ trợ đọc lại dữ liệu người chơi.");
            var snap = Snapshot(ctx, currencies);
            var expected = new Dictionary<AutoTestCurrency, double>();
            try
            {
                using (ctx.Step("Đổi số dư mọi loại tiền rồi save"))
                {
                    var i = 1;
                    foreach (var c in currencies)
                    {
                        ctx.Game.AddCurrency(c, ctx.Config.economy.testAmount + i++);
                        expected[c] = ctx.Game.GetBalance(c);
                    }

                    ctx.Game.SaveAll();
                }

                using (ctx.Step("Load lại từ storage"))
                {
                    if (!ctx.Game.ReloadPlayerData()) ctx.Skip("Game không hỗ trợ load lại dữ liệu.");
                }

                using (ctx.Step("So sánh số dư"))
                {
                    foreach (var c in currencies)
                    {
                        var v = ctx.Game.GetBalance(c);
                        ctx.Check(Near(v, expected[c]), $"{c.Name} không giữ nguyên sau save/load", null,
                            Severity.Critical, c.Name, Fmt(expected[c]), Fmt(v), "Economy.SaveLoad");
                    }
                }
            }
            finally
            {
                RestoreAll(ctx, snap);
            }

            return Task.CompletedTask;
        }

        static Task Rewards(AutoTestContext ctx)
        {
            var currencies = Currencies(ctx);
            if ((ctx.Game.Capabilities & AdapterCapabilities.Rewards) == 0)
                ctx.Skip("Adapter không hỗ trợ phát thưởng.");
            var snap = Snapshot(ctx, currencies);
            var x = Math.Round(ctx.Config.economy.testAmount);
            try
            {
                foreach (var c in currencies)
                {
                    if (AutoTestFilters.MatchesAny(c.Name, ctx.Config.economy.rewardSkipCurrencies.Select(s => "^" + s + "$")))
                    {
                        ctx.Log($"Bỏ qua thưởng {c.Name} (rewardSkipCurrencies).");
                        continue;
                    }

                    using (ctx.Step($"Phát thưởng {Fmt(x)} {c.Name}"))
                    {
                        var before = ctx.Game.GetBalance(c);
                        if (!ctx.Game.GrantReward(c, x)) continue;
                        var after = ctx.Game.GetBalance(c);
                        if (Near(after, before))
                            ctx.Report(Severity.Minor, "Economy.RewardRouting",
                                $"Thưởng {c.Name} không cộng vào ví {c.Name}",
                                "Có thể game chuyển thưởng loại này sang ví khác — nếu đúng thiết kế, thêm vào " +
                                "economy.rewardSkipCurrencies.", c.Name, null, Fmt(before + x), Fmt(after));
                        else
                            ctx.Check(Near(after, before + x), $"Thưởng {c.Name} cộng sai số", null, Severity.Critical,
                                c.Name, Fmt(before + x), Fmt(after), "Economy.Reward");
                    }
                }
            }
            finally
            {
                RestoreAll(ctx, snap);
            }

            return Task.CompletedTask;
        }

        static async Task PurchaseOffline(AutoTestContext ctx)
        {
            var currencies = Currencies(ctx);
            if ((ctx.Game.Capabilities & AdapterCapabilities.PurchaseOffline) == 0)
                ctx.Skip("Adapter không hỗ trợ mua bằng tiền mềm.");
            var snap = Snapshot(ctx, currencies);
            var price = Math.Round(ctx.Config.economy.testAmount);
            try
            {
                foreach (var c in currencies)
                {
                    using (ctx.Step($"{c.Name}: đủ tiền mua giá {Fmt(price)}"))
                    {
                        ctx.Game.AddCurrency(c, price);
                        var before = ctx.Game.GetBalance(c);
                        var ok = await ctx.Game.PurchaseOffline(c, price, ctx.Config.smoke.screenOpenTimeoutSeconds, ctx.Token);
                        if (ok == null) ctx.Skip("PurchaseOffline không trả kết quả.");
                        var after = ctx.Game.GetBalance(c);
                        ctx.Check(ok == true, $"Mua bằng {c.Name} thất bại dù đủ tiền", null, Severity.Critical,
                            c.Name, "true", "false", "Economy.Purchase");
                        ctx.Check(Near(after, before - price), $"Mua bằng {c.Name} trừ sai giá", null,
                            Severity.Critical, c.Name, Fmt(before - price), Fmt(after), "Economy.Purchase");
                    }

                    using (ctx.Step($"{c.Name}: thiếu tiền"))
                    {
                        RestoreBalance(ctx, c, Math.Max(0, price - 1));
                        var before = ctx.Game.GetBalance(c);
                        var ok = await ctx.Game.PurchaseOffline(c, price, ctx.Config.smoke.screenOpenTimeoutSeconds, ctx.Token);
                        var after = ctx.Game.GetBalance(c);
                        ctx.Check(ok != true, $"Mua bằng {c.Name} thành công dù thiếu tiền", null, Severity.Blocker,
                            c.Name, "false", "true", "Economy.PurchaseInsufficient");
                        ctx.Check(Near(after, before), $"Thiếu tiền nhưng {c.Name} vẫn bị trừ", null, Severity.Critical,
                            c.Name, Fmt(before), Fmt(after), "Economy.PurchaseInsufficient");
                    }

                    await GameFlow.ReturnToBaseline(ctx); // popup "không đủ tiền" nếu có
                }
            }
            finally
            {
                RestoreAll(ctx, snap);
            }
        }

        static Task ConfigSanity(AutoTestContext ctx)
        {
            if ((ctx.Game.Capabilities & AdapterCapabilities.Config) == 0) ctx.Skip("Adapter không hỗ trợ đọc config.");
            var cfg = ctx.Config.economy;
            var nonNegative = SafeRegex(cfg.nonNegativeFieldPattern);
            var idPattern = SafeRegex(cfg.idFieldPattern);
            var collections = ctx.Game.GetConfigCollections();
            var rows = 0;
            var issues = 0;
            foreach (var col in collections)
                using (ctx.Step($"Bảng {col.Name}"))
                {
                    if (col.Asset == null)
                    {
                        ctx.Report(Severity.Major, "Config.Missing", $"Không load được bảng {col.Name}", null, col.Name);
                        continue;
                    }

                    if (col.Items == null || col.ItemType == null) continue;
                    if (col.Items.Count == 0)
                    {
                        ctx.Report(Severity.Minor, "Config.Empty", $"Bảng {col.Name} rỗng", null, col.Name);
                        continue;
                    }

                    rows += col.Items.Count;
                    var members = ReadableMembers(col.ItemType);
                    var idMember = members.FirstOrDefault(m => idPattern != null && idPattern.IsMatch(m.Name));
                    if (idMember != null && IsGroupingColumn(col.Items, idMember))
                    {
                        ctx.Log($"{col.Name}.{idMember.Name} lặp ở phần lớn dòng → coi là cột nhóm, bỏ qua kiểm id trùng.");
                        idMember = null;
                    }

                    var seenIds = new Dictionary<string, int>();
                    for (var i = 0; i < col.Items.Count && issues < MAX_CONFIG_ISSUES; i++)
                    {
                        var item = col.Items[i];
                        if (item == null) continue;
                        foreach (var m in members)
                        {
                            var v = Read(m, item);
                            if (!(v is IConvertible) || v is string || v is bool || v is Enum) continue;
                            double d;
                            try
                            {
                                d = Convert.ToDouble(v);
                            }
                            catch
                            {
                                continue;
                            }

                            var loc = $"{col.Name}[{i}].{m.Name}";
                            if (double.IsNaN(d) || double.IsInfinity(d))
                            {
                                ctx.Report(Severity.Major, "Config.NaN", $"{col.Name}: {m.Name} = {d}", null, loc);
                                issues++;
                            }
                            else if (d < 0 && nonNegative != null && nonNegative.IsMatch(m.Name))
                            {
                                ctx.Report(Severity.Major, "Config.Negative", $"{col.Name}: {m.Name} âm", null, loc,
                                    null, "≥ 0", Fmt(d));
                                issues++;
                            }
                        }

                        if (idMember == null) continue;
                        var id = Read(idMember, item)?.ToString();
                        if (string.IsNullOrWhiteSpace(id))
                        {
                            // Dòng tiếp nối mảng lồng nhau trong CSV để trống id — chỉ báo nhẹ.
                            continue;
                        }

                        if (seenIds.TryGetValue(id, out var firstIndex))
                        {
                            ctx.Report(Severity.Major, "Config.DuplicateId", $"{col.Name}: id '{id}' bị trùng",
                                $"Dòng {firstIndex} và {i}.", $"{col.Name}[{i}].{idMember.Name}");
                            issues++;
                        }
                        else
                        {
                            seenIds[id] = i;
                        }
                    }
                }

            ctx.Metric("Bảng config", collections.Count, "");
            ctx.Metric("Dòng đã kiểm", rows, "");
            if (issues >= MAX_CONFIG_ISSUES) ctx.Info($"Dừng ở {MAX_CONFIG_ISSUES} lỗi config — còn nữa chưa liệt kê.");
            return Task.CompletedTask;
        }

        /// <summary>
        ///     Cột "id" mà ≥ 50% dòng trùng nhau thường là khoá nhóm (bảng nhiều dòng / một id, vd tỉ lệ rơi theo rương),
        ///     không phải khoá duy nhất — cùng heuristic với luật CSV tĩnh.
        /// </summary>
        static bool IsGroupingColumn(IList items, MemberInfo idMember)
        {
            var total = 0;
            var distinct = new HashSet<string>();
            foreach (var item in items)
            {
                if (item == null) continue;
                var v = Read(idMember, item)?.ToString();
                if (string.IsNullOrWhiteSpace(v)) continue;
                total++;
                distinct.Add(v);
            }

            return total > 1 && total - distinct.Count >= total * GROUPING_DUPLICATE_RATIO;
        }

        static Regex SafeRegex(string pattern)
        {
            if (string.IsNullOrEmpty(pattern)) return null;
            try
            {
                return new Regex(pattern, RegexOptions.CultureInvariant);
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        static List<MemberInfo> ReadableMembers(Type t)
        {
            var list = new List<MemberInfo>();
            list.AddRange(t.GetFields(BindingFlags.Public | BindingFlags.Instance));
            list.AddRange(t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanRead && p.GetIndexParameters().Length == 0));
            return list;
        }

        static object Read(MemberInfo m, object target)
        {
            try
            {
                return m is FieldInfo f ? f.GetValue(target) : ((PropertyInfo)m).GetValue(target);
            }
            catch
            {
                return null;
            }
        }

        #endregion
    }
}
