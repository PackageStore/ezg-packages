using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Ezg.AutoTest
{
    /// <summary>So khớp danh sách regex trong settings (dùng chung cho mọi suite để hành vi đồng nhất).</summary>
    public static class AutoTestFilters
    {
        static readonly Dictionary<string, Regex> _cache = new();

        /// <summary>
        ///     True nếu <paramref name="value" /> khớp ít nhất một regex (không phân biệt hoa thường, khớp một phần).
        ///     Regex sai cú pháp được coi là chuỗi con thường.
        /// </summary>
        public static bool MatchesAny(string value, IEnumerable<string> patterns)
        {
            if (string.IsNullOrEmpty(value) || patterns == null) return false;
            foreach (var p in patterns)
            {
                if (string.IsNullOrWhiteSpace(p)) continue;
                var r = Get(p);
                if (r != null ? r.IsMatch(value) : value.IndexOf(p, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }

            return false;
        }

        /// <summary>Feature có bị loại khỏi smoke/UI audit/button sweep không (settings + hooks project).</summary>
        public static bool IsFeatureExcluded(AutoTestConfig config, string featureName,
            IAutoTestProjectHooks hooks = null)
        {
            if (MatchesAny(featureName, config.smoke.excludedFeatures)) return true;
            if (hooks == null) return false;
            try
            {
                var extra = hooks.ExtraExcludedFeatures();
                if (extra == null) return false;
                foreach (var e in extra)
                    if (string.Equals(e, featureName, StringComparison.OrdinalIgnoreCase))
                        return true;
            }
            catch (Exception ex)
            {
                AutoTestLog.Warn("Hooks.ExtraExcludedFeatures lỗi: " + ex.Message);
            }

            return false;
        }

        static Regex Get(string pattern)
        {
            lock (_cache)
            {
                if (_cache.TryGetValue(pattern, out var r)) return r;
                try
                {
                    r = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                }
                catch (ArgumentException)
                {
                    r = null;
                }

                _cache[pattern] = r;
                return r;
            }
        }
    }
}
