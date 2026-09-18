#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Ezg.FigmaFeatureImporter.Editor
{
    /// <summary>Bước 11–12 (§5.2): controller, FeatureType/MainUI/_closeButtons, auto-wire, codegen.</summary>
    public static partial class FigmaFeatureImporter
    {
        private static readonly Regex PointerType = new(@"PPtr<\$?(\w+)>", RegexOptions.Compiled);

        private static void WireController(Ctx ctx)
        {
            var entry = ctx.Entry;
            if (string.IsNullOrWhiteSpace(entry.controllerType))
            {
                ctx.Report.notes.Add("controllerType trống — không gắn controller, chỉ dựng cấu trúc.");
                return;
            }

            var type = Type.GetType(entry.controllerType);
            if (type == null)
            {
                ctx.Report.Warn($"Không resolve được controllerType '{entry.controllerType}' — assembly đã compile chưa?");
                return;
            }
            if (!typeof(MonoBehaviour).IsAssignableFrom(type))
            {
                ctx.Report.Warn($"'{type.FullName}' không phải MonoBehaviour.");
                return;
            }

            var controller = ctx.Root.GetComponent(type) as MonoBehaviour;
            if (controller == null)
            {
                // Đổi controllerType giữa hai lần chạy: hai controller cùng lớp cơ sở trên một root là hai
                // bookkeeping của UIManager đè nhau → gỡ cái cũ. Lớp cơ sở khai bằng TÊN trong settings
                // (package không tham chiếu assembly game).
                var baseName = ctx.Settings.baseControllerTypeName;
                if (!string.IsNullOrWhiteSpace(baseName))
                    foreach (var other in ctx.Root.GetComponents<MonoBehaviour>())
                    {
                        if (other == null || other.GetType() == type) continue;
                        if (TypeMatches(other.GetType(), baseName)) DestroyNow(other);
                    }
                controller = ctx.Root.AddComponent(type) as MonoBehaviour;
            }
            if (controller == null)
            {
                ctx.Report.Warn($"AddComponent({type.Name}) thất bại.");
                return;
            }

            var so = new SerializedObject(controller);
            SetFeatureType(so, entry.featureType, ctx.Report);
            SetReference(so, "MainUI", ctx.ActiveBranch);
            SetBool(so, "ClickBackgroundToExit", entry.clickBackgroundToExit);
            SetFloat(so, "_backgroundAlpha", entry.backgroundAlpha);

            var closeButtons = new List<Button>(ctx.CloseButtons);
            var background = ctx.Root.transform.Find(ctx.Settings.backgroundNode);
            var backgroundButton = background != null ? background.GetComponent<Button>() : null;
            if (entry.clickBackgroundToExit && backgroundButton != null) closeButtons.Add(backgroundButton);
            var closeProperty = so.FindProperty("_closeButtons");
            if (closeProperty != null)
            {
                closeProperty.arraySize = closeButtons.Count;
                for (var i = 0; i < closeButtons.Count; i++)
                    closeProperty.GetArrayElementAtIndex(i).objectReferenceValue = closeButtons[i];
            }
            if (entry.layout == FigmaScreenLayout.Popup && closeButtons.Count == 0)
                ctx.Report.Warn("Popup không có nút đóng: không map isCloseButton nào và clickBackgroundToExit tắt.");

            var wired = 0;

            // Bindings tường minh trước
            foreach (var binding in entry.bindings ?? new List<FigmaBinding>())
            {
                if (binding == null || string.IsNullOrEmpty(binding.node) || string.IsNullOrEmpty(binding.field)) continue;
                var property = so.FindProperty(binding.field);
                if (property == null)
                {
                    ctx.Report.unresolvedBindings.Add($"{binding.node} -> {binding.field}: controller không có field");
                    continue;
                }
                var node = FindNode(ctx, binding.node);
                if (node == null)
                {
                    ctx.Report.unresolvedBindings.Add($"{binding.node} -> {binding.field}: không thấy node");
                    continue;
                }
                var value = ObjectForProperty(property, node);
                if (value == null)
                {
                    ctx.Report.unresolvedBindings.Add($"{binding.node} -> {binding.field}: node không có component {property.type}");
                    continue;
                }
                property.objectReferenceValue = value;
                ctx.ExplicitlyBound.Add(node);
                wired++;
            }

            // Quy ước tên sau
            foreach (var node in ConventionCandidates(ctx))
            {
                var binding = FigmaBindingsGenerator.FromNodeName(node.name, null, null, true);
                var fieldName = binding?.fieldName ?? FigmaBindingsGenerator.ContainerFieldName(node.name);
                if (fieldName == null) continue;
                var property = so.FindProperty(fieldName);
                if (property == null) continue; // controller chưa có field — codegen sẽ sinh
                var value = ObjectForProperty(property, node);
                if (value == null)
                {
                    ctx.Report.unresolvedBindings.Add($"{node.name} -> {fieldName}: node không có component {property.type}");
                    continue;
                }
                property.objectReferenceValue = value;
                wired++;
            }

            so.ApplyModifiedPropertiesWithoutUndo();
            ctx.Report.fieldsWired = wired;
        }

        /// <summary>
        ///     Bước 12: sinh partial `&lt;X&gt;Controller.Figma.cs`. Trả true khi có file mới/đổi (cần
        ///     reload rồi import lượt 2 để wire).
        /// </summary>
        private static bool GenerateBindings(Ctx ctx)
        {
            var entry = ctx.Entry;
            if (!entry.generateBindings) return false;
            if (string.IsNullOrWhiteSpace(entry.controllerType))
            {
                ctx.Report.Warn("generateBindings bật nhưng controllerType trống.");
                return false;
            }

            var bindings = new List<FigmaBindingsGenerator.Binding>();
            var seen = new HashSet<string>();
            foreach (var node in ConventionCandidates(ctx))
            {
                var text = node.GetComponent<TMP_Text>();
                var isDynamic = node.name.StartsWith("Value_", StringComparison.Ordinal)
                                || (entry.dynamicTextNodes != null && entry.dynamicTextNodes.Contains(node.name));
                var binding = FigmaBindingsGenerator.FromNodeName(node.name, text != null ? text.text : null,
                    entry.localizeKeyPrefix, isDynamic);
                if (binding == null) continue;
                if (!seen.Add(binding.fieldName))
                {
                    ctx.Report.Warn($"Hai node cùng sinh field '{binding.fieldName}' — đổi tên ở Figma hoặc renameNodes.");
                    continue;
                }
                bindings.Add(binding);
            }

            var result = FigmaBindingsGenerator.Generate(ctx.Settings, entry.controllerType, bindings);
            if (result.warning != null) ctx.Report.Warn(result.warning);
            if (result.path != null) ctx.Report.generatedFile = result.path;
            if (result.path != null && !result.written) ctx.Report.notes.Add($"{result.path} không đổi ({result.fieldCount} field).");
            return result.written;
        }

        /// <summary>Node Figma (và root template) trong thân — bỏ ruột template và node đã bind tường minh.</summary>
        private static IEnumerable<Transform> ConventionCandidates(Ctx ctx)
        {
            foreach (var t in ctx.Body.GetComponentsInChildren<Transform>(true))
            {
                if (t == ctx.Body) continue;
                if (IsInsideTemplate(ctx, t)) continue;
                if (ctx.ExplicitlyBound.Contains(t)) continue;
                yield return t;
            }
        }

        /// <summary>Giá trị cho một field PPtr: Transform/GameObject của node, hoặc component đúng kiểu trên node/con.</summary>
        private static Object ObjectForProperty(SerializedProperty property, Transform node)
        {
            if (property.propertyType != SerializedPropertyType.ObjectReference) return null;
            var match = PointerType.Match(property.type ?? string.Empty);
            var wanted = match.Success ? match.Groups[1].Value : property.type;

            switch (wanted)
            {
                case "GameObject": return node.gameObject;
                case "Transform":
                case "RectTransform": return node;
            }

            foreach (var component in node.GetComponentsInChildren<Component>(true))
            {
                if (component == null) continue;
                if (TypeMatches(component.GetType(), wanted)) return component;
            }
            return null;
        }

        private static bool TypeMatches(Type type, string wantedName)
        {
            for (var current = type; current != null && current != typeof(object); current = current.BaseType)
                if (current.Name == wantedName) return true;
            return false;
        }

        /// <summary>
        ///     Field <c>FeatureType</c> của controller là enum của game. Package không tham chiếu enum đó:
        ///     đọc thẳng danh sách member qua <see cref="SerializedProperty.enumNames" /> rồi gán theo tên,
        ///     nên enum đánh số thưa (giá trị serialize trong prefab) vẫn đúng.
        /// </summary>
        private static void SetFeatureType(SerializedObject so, string featureName, ImportReport report)
        {
            var property = so.FindProperty("FeatureType");
            if (property == null) return;
            if (property.propertyType != SerializedPropertyType.Enum)
            {
                report.Warn("Field 'FeatureType' trên controller không phải enum — bỏ qua.");
                return;
            }

            var names = property.enumNames;
            var wanted = string.IsNullOrWhiteSpace(featureName) ? "none" : featureName.Trim();
            for (var i = 0; i < names.Length; i++)
            {
                if (!string.Equals(names[i], wanted, StringComparison.Ordinal)) continue;
                property.enumValueIndex = i;
                return;
            }

            if (wanted == "none")
            {
                // Enum không có member 'none' → giữ nguyên giá trị đang có, không đoán bừa.
                return;
            }
            report.Warn($"featureType '{wanted}' không có trong enum của field FeatureType " +
                        $"({string.Join(", ", names)}) — thêm member (số CHƯA dùng) rồi import lại.");
        }

        private static void SetReference(SerializedObject so, string field, Object value)
        {
            var property = so.FindProperty(field);
            if (property != null) property.objectReferenceValue = value;
        }

        private static void SetBool(SerializedObject so, string field, bool value)
        {
            var property = so.FindProperty(field);
            if (property != null) property.boolValue = value;
        }

        private static void SetFloat(SerializedObject so, string field, float value)
        {
            var property = so.FindProperty(field);
            if (property != null) property.floatValue = value;
        }
    }
}
#endif
