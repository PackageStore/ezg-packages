#if UNITY_EDITOR || EZG_AUTOTEST
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;

namespace Ezg.AutoTest.Samples
{
    /// <summary>
    ///     Mẫu: với tối đa vài loại tiền tệ adapter biết — cộng một lượng, trừ lại, thử trừ quá số dư. Số dư phải
    ///     khớp từng bước, game phải từ chối trừ quá số dư và số dư không bao giờ âm. TearDown trả lại số dư gốc.
    ///     Chỉ dùng API chung <see cref="IGameAdapter" /> nên chạy được với mọi adapter có capability Economy.
    /// </summary>
    [AutoTestScenario("Mẫu: Cộng/trừ tiền tệ khứ hồi", Category = "Mẫu", Order = 901, TimeoutSeconds = 60,
        Description = "Cộng, trừ lại và thử trừ quá số dư cho từng loại tiền tệ; số dư phải khớp và không âm.")]
    public sealed class ExampleEconomyScenario : AutoTestScenario
    {
        /// <summary>Số loại tiền tệ tối đa đem test (giữ kịch bản ngắn).</summary>
        const int MAX_CURRENCIES = 3;

        /// <summary>Sai số cho phép khi so số dư (balance kiểu double / làm tròn của game).</summary>
        const double BALANCE_EPSILON = 0.5;

        readonly Dictionary<AutoTestCurrency, double> _original = new Dictionary<AutoTestCurrency, double>();
        double _amount;

        public override async Task SetUp(AutoTestContext ctx)
        {
            await GameFlow.EnsureReady(ctx);
            if ((ctx.Game.Capabilities & AdapterCapabilities.Economy) == 0)
                ctx.Skip($"Adapter {ctx.Game.Name} không hỗ trợ Economy.");

            _amount = ctx.Config.economy.testAmount;
            _original.Clear();
            foreach (var currency in ctx.Game.GetCurrencies())
            {
                if (ctx.Config.economy.skipCurrencies.Contains(currency.Name)) continue;
                // Lưu số dư gốc TRƯỚC khi đổi gì để TearDown khôi phục.
                _original[currency] = ctx.Game.GetBalance(currency);
                if (_original.Count >= MAX_CURRENCIES) break;
            }

            if (_original.Count == 0) ctx.Skip("Không có loại tiền tệ nào để test.");
        }

        public override async Task Run(AutoTestContext ctx)
        {
            foreach (var pair in _original)
            {
                var currency = pair.Key;
                var before = ctx.Game.GetBalance(currency);

                using (ctx.Step($"Cộng {Fmt(_amount)} {currency.Name}"))
                {
                    ctx.Game.AddCurrency(currency, _amount);
                    await ctx.NextFrame();
                    var actual = ctx.Game.GetBalance(currency);
                    ctx.Check(Near(actual, before + _amount), $"Cộng {currency.Name} sai số dư",
                        expected: Fmt(before + _amount), actual: Fmt(actual), category: "Economy.Add");
                }

                using (ctx.Step($"Trừ lại {Fmt(_amount)} {currency.Name}"))
                {
                    var removed = ctx.Game.RemoveCurrency(currency, _amount);
                    await ctx.NextFrame();
                    var actual = ctx.Game.GetBalance(currency);
                    ctx.Check(removed, $"Game từ chối trừ {currency.Name} dù đủ tiền", category: "Economy.Remove");
                    ctx.Check(Near(actual, before), $"Trừ {currency.Name} sai số dư",
                        expected: Fmt(before), actual: Fmt(actual), category: "Economy.Remove");
                }

                using (ctx.Step($"Thử trừ nhiều hơn số dư {currency.Name}"))
                {
                    var current = ctx.Game.GetBalance(currency);
                    var tooMuch = current + _amount;
                    ctx.Check(!ctx.Game.IsEnough(currency, tooMuch), $"IsEnough báo đủ {currency.Name} khi thiếu tiền",
                        severity: Severity.Critical, category: "Economy.IsEnough");

                    var removed = ctx.Game.RemoveCurrency(currency, tooMuch);
                    await ctx.NextFrame();
                    var after = ctx.Game.GetBalance(currency);
                    ctx.Check(!removed && after >= 0, $"Trừ quá số dư {currency.Name} vẫn thành công hoặc số dư âm",
                        severity: Severity.Critical, expected: "bị từ chối, số dư ≥ 0",
                        actual: $"removed={removed}, số dư={Fmt(after)}", category: "Economy.Overspend");
                }
            }
        }

        public override Task TearDown(AutoTestContext ctx)
        {
            // Trả lại số dư gốc (sandbox của runner còn khôi phục PlayerPrefs, nhưng state trong RAM phải sạch
            // cho kịch bản sau).
            foreach (var pair in _original)
            {
                try
                {
                    ctx.Game.SetCurrency(pair.Key, pair.Value);
                }
                catch (NotSupportedException)
                {
                    var diff = pair.Value - ctx.Game.GetBalance(pair.Key);
                    if (diff > 0) ctx.Game.AddCurrency(pair.Key, diff);
                    else if (diff < 0) ctx.Game.RemoveCurrency(pair.Key, -diff);
                }
                catch (Exception e)
                {
                    ctx.Warn($"Không khôi phục được số dư {pair.Key.Name}", e.Message);
                }
            }

            return Task.CompletedTask;
        }

        static bool Near(double a, double b)
        {
            return Math.Abs(a - b) <= BALANCE_EPSILON;
        }

        static string Fmt(double value)
        {
            return value.ToString("0.##", CultureInfo.InvariantCulture);
        }
    }
}
#endif
