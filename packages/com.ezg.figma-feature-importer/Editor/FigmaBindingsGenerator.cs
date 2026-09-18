#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace Ezg.FigmaFeatureImporter.Editor
{
    /// <summary>
    ///     Sinh file partial `&lt;X&gt;Controller.Figma.cs` từ tên node Figma theo quy ước binding
    ///     (bảng trong README của package): mỗi node `Text_X` / `Value_X` /
    ///     `Btn_X` / `Icon_X` / `Slider_X` / `Toggle_X` thành một field `[SerializeField]`; text tĩnh
    ///     còn có cặp `TEXT_X_KEY` / `TEXT_X_FALLBACK` và được set trong `ApplyFigmaTexts()` qua
    ///     <c>LocalizeFallback.Get</c>. `Container_X` không sinh field (chỉ auto-wire nếu controller
    ///     tự khai).
    ///
    ///     Chỉ ghi khi nội dung đổi (so hash) để không kích reload vô ích. Controller phải là
    ///     <c>partial</c>; không thì bỏ qua và báo — đây là lỗi cấu hình, không phải lỗi tool.
    /// </summary>
    internal static class FigmaBindingsGenerator
    {
        internal sealed class Result
        {
            public string path;
            public bool written;
            public int fieldCount;
            public string warning;
        }

        /// <summary>Một node Figma đã quy về field.</summary>
        internal sealed class Binding
        {
            public string nodeName;
            public string fieldName;
            public string fieldType;   // tên type ngắn: TextMeshProUGUI, Button, Image, Slider, Toggle
            public string localizeKey; // null khi không phải text tĩnh
            public string fallback;    // nội dung text Figma
            public string constBase;   // TEXT_TITLE
        }

        private static readonly Regex NamePattern =
            new(@"^(Text|Value|Btn|Button|Icon|Img|Slider|Toggle)_(.+)$", RegexOptions.Compiled);

        /// <summary>Quy ước tên → field/type. Trả null khi tên không theo quy ước.</summary>
        internal static Binding FromNodeName(string nodeName, string textContent, string localizeKeyPrefix,
            bool isDynamicText)
        {
            var match = NamePattern.Match(nodeName ?? string.Empty);
            if (!match.Success) return null;

            var prefix = match.Groups[1].Value;
            var rest = match.Groups[2].Value;
            var pascal = ToPascal(rest);
            var binding = new Binding { nodeName = nodeName };

            switch (prefix)
            {
                case "Text":
                    binding.fieldName = "_text" + pascal;
                    binding.fieldType = "TextMeshProUGUI";
                    if (!isDynamicText)
                    {
                        binding.constBase = "TEXT_" + ToUpperSnake(rest);
                        binding.localizeKey = string.IsNullOrEmpty(localizeKeyPrefix)
                            ? ToSnake(rest)
                            : $"{localizeKeyPrefix}_{ToSnake(rest)}";
                        binding.fallback = textContent ?? string.Empty;
                    }
                    break;
                case "Value":
                    binding.fieldName = "_value" + pascal;
                    binding.fieldType = "TextMeshProUGUI";
                    break;
                case "Btn":
                case "Button":
                    binding.fieldName = "_btn" + pascal;
                    binding.fieldType = "Button";
                    break;
                case "Icon":
                case "Img":
                    binding.fieldName = "_icon" + pascal;
                    binding.fieldType = "Image";
                    break;
                case "Slider":
                    binding.fieldName = "_slider" + pascal;
                    binding.fieldType = "Slider";
                    break;
                case "Toggle":
                    binding.fieldName = "_toggle" + pascal;
                    binding.fieldType = "Toggle";
                    break;
            }
            return binding;
        }

        /// <summary>`Container_X` → `_containerX` (RectTransform), chỉ dùng cho auto-wire.</summary>
        internal static string ContainerFieldName(string nodeName)
        {
            const string prefix = "Container_";
            if (nodeName == null || !nodeName.StartsWith(prefix, StringComparison.Ordinal)) return null;
            return "_container" + ToPascal(nodeName.Substring(prefix.Length));
        }

        /// <summary>
        ///     Ghi `&lt;X&gt;Controller.Figma.cs` cạnh file controller. <paramref name="controllerType" />
        ///     là chuỗi assembly-qualified trong settings ("Ns.X, Asm").
        /// </summary>
        internal static Result Generate(FigmaFeatureImportSettings settings, string controllerType,
            IReadOnlyList<Binding> bindings)
        {
            var result = new Result();
            var (ns, className) = SplitTypeName(controllerType);
            if (string.IsNullOrEmpty(className))
            {
                result.warning = $"controllerType '{controllerType}' không tách được tên class";
                return result;
            }

            var scriptPath = FindScriptPath(className);
            if (scriptPath == null)
            {
                result.warning = $"Không thấy file {className}.cs trong project — tạo controller rỗng trước rồi import lại";
                return result;
            }

            var source = File.ReadAllText(scriptPath);
            if (!Regex.IsMatch(source, $@"partial\s+class\s+{Regex.Escape(className)}\b"))
            {
                result.warning = $"{className} chưa khai 'partial class' — thêm 'partial' rồi import lại để sinh {className}.Figma.cs";
                return result;
            }

            result.path = Path.Combine(Path.GetDirectoryName(scriptPath) ?? string.Empty, $"{className}.Figma.cs")
                .Replace('\\', '/');
            var content = Render(settings, ns, className, bindings);
            result.fieldCount = bindings.Count;

            if (File.Exists(result.path) && Hash(File.ReadAllText(result.path)) == Hash(content))
                return result;

            File.WriteAllText(result.path, content, new UTF8Encoding(false));
            result.written = true;
            return result;
        }

        private static string Render(FigmaFeatureImportSettings settings, string ns, string className,
            IReadOnlyList<Binding> bindings)
        {
            var sb = new StringBuilder();
            sb.AppendLine("// <auto-generated>");
            sb.AppendLine("//     Sinh bởi com.ezg.figma-feature-importer từ tên node Figma.");
            sb.AppendLine("//     ĐỪNG sửa tay: lần import sau ghi đè. Đổi tên node ở Figma hoặc bindings trong");
            sb.AppendLine("//     FigmaFeatureImportSettings rồi import lại.");
            sb.AppendLine("// </auto-generated>");
            // using do project khai (settings.codegenUsings) — package không ép namespace của game nào.
            var usings = settings != null && settings.codegenUsings != null && settings.codegenUsings.Count > 0
                ? settings.codegenUsings
                : new List<string> { "TMPro", "UnityEngine", "UnityEngine.UI" };
            foreach (var entry in usings.Where(u => !string.IsNullOrWhiteSpace(u)).Select(u => u.Trim()).Distinct())
                sb.AppendLine($"using {entry};");
            sb.AppendLine();
            var indent = string.Empty;
            if (!string.IsNullOrEmpty(ns))
            {
                sb.AppendLine($"namespace {ns}");
                sb.AppendLine("{");
                indent = "    ";
            }

            sb.AppendLine($"{indent}public partial class {className}");
            sb.AppendLine($"{indent}{{");

            var ordered = bindings.OrderBy(b => b.fieldType, StringComparer.Ordinal).ThenBy(b => b.fieldName, StringComparer.Ordinal).ToList();
            foreach (var binding in ordered)
            {
                sb.AppendLine($"{indent}    /// <summary>Node Figma <c>{Escape(binding.nodeName)}</c>.</summary>");
                sb.AppendLine($"{indent}    [SerializeField] private {binding.fieldType} {binding.fieldName};");
            }

            var statics = ordered.Where(b => b.localizeKey != null).ToList();
            if (statics.Count > 0)
            {
                sb.AppendLine();
                foreach (var binding in statics)
                {
                    sb.AppendLine($"{indent}    private const string {binding.constBase}_KEY = \"{Escape(binding.localizeKey)}\";");
                    sb.AppendLine($"{indent}    private const string {binding.constBase}_FALLBACK = \"{Escape(binding.fallback)}\";");
                }
            }

            sb.AppendLine();
            sb.AppendLine($"{indent}    /// <summary>Đổ chữ tĩnh của màn (localizeCallFormat). Gọi trong LoadData().</summary>");
            sb.AppendLine($"{indent}    protected void ApplyFigmaTexts()");
            sb.AppendLine($"{indent}    {{");
            foreach (var binding in statics)
            {
                var expression = FormatLocalizeCall(settings, $"{binding.constBase}_KEY", $"{binding.constBase}_FALLBACK");
                sb.AppendLine($"{indent}        if ({binding.fieldName} != null) {binding.fieldName}.text = {expression};");
            }
            sb.AppendLine($"{indent}    }}");
            sb.AppendLine($"{indent}}}");
            if (!string.IsNullOrEmpty(ns)) sb.AppendLine("}");
            return sb.ToString();
        }

        /// <summary>
        ///     Biểu thức lấy chữ tĩnh, do project khai (<c>localizeCallFormat</c>): {0} = const KEY,
        ///     {1} = const FALLBACK. Trống hoặc sai định dạng → gán thẳng FALLBACK (không localize).
        /// </summary>
        private static string FormatLocalizeCall(FigmaFeatureImportSettings settings, string keyConst, string fallbackConst)
        {
            var format = settings != null ? settings.localizeCallFormat : null;
            if (string.IsNullOrWhiteSpace(format)) return fallbackConst;
            try
            {
                return string.Format(format, keyConst, fallbackConst);
            }
            catch (FormatException e)
            {
                Debug.LogWarning($"[FigmaImport] localizeCallFormat '{format}' sai định dạng ({e.Message}) — " +
                                 "dùng thẳng FALLBACK.");
                return fallbackConst;
            }
        }

        private static string FindScriptPath(string className)
        {
            foreach (var guid in AssetDatabase.FindAssets($"t:MonoScript {className}"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileName(path) == className + ".cs") return path;
            }
            return null;
        }

        private static (string ns, string className) SplitTypeName(string assemblyQualified)
        {
            if (string.IsNullOrWhiteSpace(assemblyQualified)) return (null, null);
            var full = assemblyQualified.Split(',')[0].Trim();
            var dot = full.LastIndexOf('.');
            return dot < 0 ? (string.Empty, full) : (full.Substring(0, dot), full.Substring(dot + 1));
        }

        internal static string ToPascal(string raw)
        {
            var parts = Regex.Split(raw ?? string.Empty, @"[_\s\-]+").Where(p => p.Length > 0);
            var sb = new StringBuilder();
            foreach (var part in parts)
            {
                sb.Append(char.ToUpperInvariant(part[0]));
                if (part.Length > 1) sb.Append(part.Substring(1));
            }
            return sb.ToString();
        }

        internal static string ToSnake(string raw)
        {
            var spaced = Regex.Replace(raw ?? string.Empty, @"([a-z0-9])([A-Z])", "$1_$2");
            spaced = Regex.Replace(spaced, @"[\s\-]+", "_");
            return spaced.ToLowerInvariant();
        }

        private static string ToUpperSnake(string raw) => ToSnake(raw).ToUpperInvariant();

        private static string Escape(string value) =>
            (value ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "").Replace("\n", "\\n");

        private static string Hash(string content)
        {
            using var md5 = MD5.Create();
            return BitConverter.ToString(md5.ComputeHash(Encoding.UTF8.GetBytes(content)));
        }
    }
}
#endif
