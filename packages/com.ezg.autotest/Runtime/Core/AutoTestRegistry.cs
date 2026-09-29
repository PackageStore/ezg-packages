using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Ezg.AutoTest
{
    /// <summary>
    ///     Tự tìm suite, kịch bản, hooks và adapter bằng reflection — không cần đăng ký tay. Kết quả cache theo
    ///     domain (Editor reload domain là cache tự làm mới).
    /// </summary>
    public static class AutoTestRegistry
    {
        static List<Type> _allTypes;

        /// <summary>Mọi type cụ thể (không abstract) có constructor rỗng, kế thừa/implement <typeparamref name="T" />.</summary>
        public static List<Type> FindImplementations<T>()
        {
            var baseType = typeof(T);
            var list = new List<Type>();
            foreach (var t in AllTypes())
            {
                if (t.IsAbstract || t.IsInterface || t.IsGenericTypeDefinition) continue;
                if (!baseType.IsAssignableFrom(t)) continue;
                if (t.GetConstructor(Type.EmptyTypes) == null) continue;
                list.Add(t);
            }

            return list;
        }

        /// <summary>Mọi class cụ thể kế thừa <paramref name="baseType" /> (không yêu cầu constructor rỗng).</summary>
        public static List<Type> FindSubclasses(Type baseType)
        {
            var list = new List<Type>();
            if (baseType == null) return list;
            foreach (var t in AllTypes())
                if (!t.IsAbstract && !t.IsInterface && !t.IsGenericTypeDefinition && baseType.IsAssignableFrom(t) &&
                    t != baseType)
                    list.Add(t);
            return list;
        }

        /// <summary>Tạo mọi suite, sắp theo Order rồi tên.</summary>
        public static List<AutoTestSuite> CreateSuites()
        {
            var suites = new List<AutoTestSuite>();
            var seen = new HashSet<string>();
            foreach (var t in FindImplementations<AutoTestSuite>())
            {
                try
                {
                    var suite = (AutoTestSuite)Activator.CreateInstance(t);
                    if (!seen.Add(suite.Id))
                    {
                        AutoTestLog.Warn($"Trùng suite id '{suite.Id}' ({t.FullName}) — bỏ qua bản sau.");
                        continue;
                    }

                    suites.Add(suite);
                }
                catch (Exception e)
                {
                    AutoTestLog.Warn($"Không tạo được suite {t.FullName}: {e.Message}");
                }
            }

            return suites.OrderBy(s => s.Order).ThenBy(s => s.DisplayName).ToList();
        }

        public static AutoTestSuite CreateSuite(string id)
        {
            return CreateSuites().FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Kịch bản riêng của game (class kế thừa AutoTestScenario có attribute).</summary>
        public static List<(Type type, AutoTestScenarioAttribute info)> FindScenarios()
        {
            var list = new List<(Type, AutoTestScenarioAttribute)>();
            foreach (var t in FindImplementations<AutoTestScenario>())
            {
                var attr = t.GetCustomAttribute<AutoTestScenarioAttribute>() ??
                           new AutoTestScenarioAttribute(t.Name) { Category = "Chưa phân loại" };
                list.Add((t, attr));
            }

            return list.OrderBy(x => x.Item2.Category).ThenBy(x => x.Item2.Order).ThenBy(x => x.Item2.Name).ToList();
        }

        /// <summary>Hooks của project (class đầu tiên implement IAutoTestProjectHooks), null nếu không có.</summary>
        public static IAutoTestProjectHooks CreateHooks()
        {
            var types = FindImplementations<IAutoTestProjectHooks>();
            if (types.Count == 0) return null;
            if (types.Count > 1)
                AutoTestLog.Warn("Có nhiều class hooks: " + string.Join(", ", types.Select(t => t.FullName)) +
                                 $" — dùng {types[0].FullName}.");
            try
            {
                return (IAutoTestProjectHooks)Activator.CreateInstance(types[0]);
            }
            catch (Exception e)
            {
                AutoTestLog.Warn($"Không tạo được hooks {types[0].FullName}: {e.Message}");
                return null;
            }
        }

        /// <summary>
        ///     Chọn adapter: type chỉ định trong settings → adapter có Priority cao nhất khởi tạo thành công.
        ///     Luôn có GenericGameAdapter làm lưới an toàn.
        /// </summary>
        public static IGameAdapter CreateAdapter(AutoTestConfig config)
        {
            var candidates = new List<IGameAdapter>();
            foreach (var t in FindImplementations<IGameAdapter>())
                try
                {
                    candidates.Add((IGameAdapter)Activator.CreateInstance(t));
                }
                catch (Exception e)
                {
                    AutoTestLog.Warn($"Không tạo được adapter {t.FullName}: {e.Message}");
                }

            var forced = config.adapter.adapterType;
            if (!string.IsNullOrWhiteSpace(forced))
            {
                var match = candidates.FirstOrDefault(a =>
                    a.GetType().FullName == forced || a.GetType().Name == forced);
                if (match != null && SafeInit(match, config)) return match;
                AutoTestLog.Warn($"Adapter chỉ định '{forced}' không dùng được — chuyển sang tự chọn.");
            }

            foreach (var adapter in candidates.OrderByDescending(a => a.Priority))
                if (SafeInit(adapter, config))
                    return adapter;

            var fallback = new GenericGameAdapter();
            fallback.Initialize(config);
            return fallback;
        }

        static bool SafeInit(IGameAdapter adapter, AutoTestConfig config)
        {
            try
            {
                return adapter.Initialize(config);
            }
            catch (Exception e)
            {
                AutoTestLog.Warn($"Adapter {adapter.Name} lỗi khi khởi tạo: {e.Message}");
                return false;
            }
        }

        static IEnumerable<Type> AllTypes()
        {
            if (_allTypes != null) return _allTypes;
            var list = new List<Type>();
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (asm.IsDynamic) continue;
                var name = asm.GetName().Name;
                // Bỏ assembly hệ thống cho nhanh — suite/kịch bản không bao giờ nằm ở đây.
                if (name.StartsWith("System", StringComparison.Ordinal) ||
                    name.StartsWith("Mono.", StringComparison.Ordinal) ||
                    name.StartsWith("mscorlib", StringComparison.Ordinal) ||
                    name.StartsWith("netstandard", StringComparison.Ordinal) ||
                    name.StartsWith("nunit", StringComparison.OrdinalIgnoreCase) ||
                    name.StartsWith("Unity.Burst", StringComparison.Ordinal) ||
                    name.StartsWith("JetBrains", StringComparison.Ordinal) ||
                    name.StartsWith("Newtonsoft", StringComparison.Ordinal))
                    continue;
                Type[] types;
                try
                {
                    types = asm.GetTypes();
                }
                catch (ReflectionTypeLoadException e)
                {
                    types = e.Types.Where(t => t != null).ToArray();
                }
                catch
                {
                    continue;
                }

                list.AddRange(types);
            }

            _allTypes = list;
            return list;
        }

        /// <summary>Mọi type khớp tên (ngắn / đầy đủ / "Outer+Nested") — để adapter tự chọn type đúng hình dạng.</summary>
        public static List<Type> FindTypes(string name)
        {
            var list = new List<Type>();
            if (string.IsNullOrWhiteSpace(name)) return list;
            var isNested = name.Contains('+');
            foreach (var t in AllTypes())
            {
                var full = t.FullName;
                if (full == null) continue;
                var match = full == name ||
                            (isNested
                                ? full.EndsWith("." + name, StringComparison.Ordinal)
                                : t.Name == name && !t.IsNested);
                if (match) list.Add(t);
            }

            return list;
        }

        /// <summary>Tìm type theo tên ngắn / đầy đủ / dạng "Outer+Nested" trong mọi assembly.</summary>
        public static Type FindType(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            Type shortMatch = null;
            var isNested = name.Contains('+');
            foreach (var t in AllTypes())
            {
                if (t.FullName == name) return t;
                if (shortMatch != null) continue;
                if (isNested)
                {
                    // "GameEnums+Features" khớp "Ns.GameEnums+Features".
                    var full = t.FullName;
                    if (full != null && (full.EndsWith("." + name, StringComparison.Ordinal) || full == name))
                        shortMatch = t;
                }
                else if (t.Name == name && !t.IsNested)
                {
                    shortMatch = t;
                }
            }

            return shortMatch;
        }
    }
}
