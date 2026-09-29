using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace Ezg.AutoTest
{
    /// <summary>Tiện ích reflection cho adapter: gọi hàm có tham số mặc định, lấy member static/instance.</summary>
    public static class Reflect
    {
        public const BindingFlags ALL_STATIC =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.FlattenHierarchy;

        public const BindingFlags ALL_INSTANCE = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        public const BindingFlags PUBLIC_ANY = BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance |
                                               BindingFlags.FlattenHierarchy;

        /// <summary>Tìm method theo tên + điều kiện tham số (overload đầu tiên khớp, ưu tiên ít tham số bắt buộc).</summary>
        public static MethodInfo FindMethod(Type type, string name, Func<ParameterInfo[], bool> match = null,
            bool? isStatic = null)
        {
            if (type == null) return null;
            return type.GetMethods(PUBLIC_ANY | BindingFlags.NonPublic)
                .Where(m => m.Name == name && !m.IsGenericMethodDefinition)
                .Where(m => isStatic == null || m.IsStatic == isStatic.Value)
                .Where(m => match == null || match(m.GetParameters()))
                .OrderBy(m => m.GetParameters().Count(p => !p.HasDefaultValue))
                .FirstOrDefault();
        }

        /// <summary>
        ///     Gọi method: tham số theo vị trí cho <paramref name="positional" />, còn lại lấy theo tên trong
        ///     <paramref name="named" /> hoặc giá trị mặc định khai báo.
        /// </summary>
        public static object Invoke(MethodInfo method, object target, object[] positional,
            params (string name, object value)[] named)
        {
            var ps = method.GetParameters();
            var args = new object[ps.Length];
            for (var i = 0; i < ps.Length; i++)
            {
                if (positional != null && i < positional.Length)
                {
                    args[i] = Coerce(positional[i], ps[i].ParameterType);
                    continue;
                }

                var found = false;
                if (named != null)
                    foreach (var (n, v) in named)
                        if (string.Equals(n, ps[i].Name, StringComparison.OrdinalIgnoreCase))
                        {
                            args[i] = Coerce(v, ps[i].ParameterType);
                            found = true;
                            break;
                        }

                if (found) continue;
                args[i] = ps[i].HasDefaultValue && ps[i].DefaultValue != DBNull.Value
                    ? Coerce(ps[i].DefaultValue, ps[i].ParameterType)
                    : DefaultOf(ps[i].ParameterType);
            }

            try
            {
                return method.Invoke(method.IsStatic ? null : target, args);
            }
            catch (TargetInvocationException e) when (e.InnerException != null)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e.InnerException).Throw();
                throw;
            }
        }

        public static object DefaultOf(Type t)
        {
            return t.IsValueType && t != typeof(void) ? Activator.CreateInstance(t) : null;
        }

        /// <summary>Đổi kiểu số/enum cho khớp tham số (double → long, int → enum…).</summary>
        public static object Coerce(object value, Type target)
        {
            if (value == null) return DefaultOf(target);
            var t = Nullable.GetUnderlyingType(target) ?? target;
            if (t.IsInstanceOfType(value)) return value;
            if (t.IsEnum)
                return value is string s ? Enum.Parse(t, s, true) : Enum.ToObject(t, Convert.ToInt64(value));
            if (value is IConvertible && typeof(IConvertible).IsAssignableFrom(t))
                return Convert.ChangeType(value, t, System.Globalization.CultureInfo.InvariantCulture);
            return value;
        }

        /// <summary>Đọc property/field static (kể cả kế thừa từ base generic).</summary>
        public static object GetStatic(Type type, string name)
        {
            if (type == null) return null;
            var p = type.GetProperty(name, ALL_STATIC);
            if (p != null && p.GetIndexParameters().Length == 0) return p.GetValue(null);
            var f = type.GetField(name, ALL_STATIC);
            return f?.GetValue(null);
        }

        public static bool HasStatic(Type type, string name)
        {
            return type != null && (type.GetProperty(name, ALL_STATIC) != null || type.GetField(name, ALL_STATIC) != null);
        }

        public static void SetStatic(Type type, string name, object value)
        {
            var p = type?.GetProperty(name, ALL_STATIC);
            if (p != null && p.CanWrite)
            {
                p.SetValue(null, Coerce(value, p.PropertyType));
                return;
            }

            var f = type?.GetField(name, ALL_STATIC);
            f?.SetValue(null, Coerce(value, f.FieldType));
        }

        /// <summary>Đọc property/field instance (public hoặc private, kể cả của base class).</summary>
        public static object GetMember(object target, string name)
        {
            if (target == null) return null;
            for (var t = target.GetType(); t != null; t = t.BaseType)
            {
                var p = t.GetProperty(name, ALL_INSTANCE | BindingFlags.DeclaredOnly);
                if (p != null && p.GetIndexParameters().Length == 0) return p.GetValue(target);
                var f = t.GetField(name, ALL_INSTANCE | BindingFlags.DeclaredOnly);
                if (f != null) return f.GetValue(target);
            }

            return null;
        }

        /// <summary>Đếm/duyệt key của IDictionary trả về dạng object (không cần biết kiểu generic).</summary>
        public static IEnumerable DictionaryKeys(object dict)
        {
            return dict is IDictionary d ? d.Keys : Array.Empty<object>();
        }

        public static long ToLong(object enumOrNumber)
        {
            return enumOrNumber == null ? 0 : Convert.ToInt64(enumOrNumber);
        }
    }

    /// <summary>
    ///     Chờ kết quả của bất kỳ "awaitable" nào trả về qua reflection (Task, UniTask, UniTask&lt;T&gt;,
    ///     Awaitable…) mà không cần reference tới thư viện của nó — poll awaiter.IsCompleted mỗi frame.
    /// </summary>
    public static class ReflectionAwait
    {
        public static async Task<object> Await(object awaitable, double timeoutSeconds, CancellationToken token)
        {
            switch (awaitable)
            {
                case null:
                    return null;
                case Task task:
                {
                    var done = await AutoTestClock.Until(() => task.IsCompleted, timeoutSeconds, token);
                    if (!done) throw new TimeoutException($"Chờ quá {timeoutSeconds:0.#}s");
                    await task;
                    var resultProp = task.GetType().GetProperty("Result");
                    return resultProp != null && task.GetType().IsGenericType ? resultProp.GetValue(task) : null;
                }
            }

            var getAwaiter = awaitable.GetType().GetMethod("GetAwaiter", BindingFlags.Public | BindingFlags.Instance,
                null, Type.EmptyTypes, null);
            if (getAwaiter == null) return awaitable; // không phải awaitable → coi như kết quả luôn

            var awaiter = getAwaiter.Invoke(awaitable, null);
            var isCompleted = awaiter.GetType().GetProperty("IsCompleted");
            var getResult = awaiter.GetType().GetMethod("GetResult", Type.EmptyTypes);
            if (isCompleted == null || getResult == null) return null;

            var ok = await AutoTestClock.Until(() => (bool)isCompleted.GetValue(awaiter), timeoutSeconds, token);
            if (!ok) throw new TimeoutException($"Chờ quá {timeoutSeconds:0.#}s");
            try
            {
                return getResult.Invoke(awaiter, null);
            }
            catch (TargetInvocationException e) when (e.InnerException != null)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e.InnerException).Throw();
                throw;
            }
        }
    }
}
