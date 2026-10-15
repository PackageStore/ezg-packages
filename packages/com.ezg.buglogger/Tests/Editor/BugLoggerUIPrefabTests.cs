using System;
using Ezg.BugLogger.UI;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Ezg.BugLogger.Tests
{
    /// <summary>
    ///     UI Bug Logger phải trông như nhau ở mọi project ⇒ prefab overlay chỉ được tham chiếu asset nằm trong package
    ///     (cộng script uGUI có sẵn của Unity), và builder phải nối đủ mọi field.
    /// </summary>
    public class BugLoggerUIPrefabTests
    {
        private const string PREFAB_PATH =
            "Packages/com.ezg.buglogger/Runtime/UI/Resources/EzgBugLogger/BugLoggerOverlay.prefab";

        private const string PACKAGE_ROOT = "Packages/com.ezg.buglogger/";
        private const string UGUI_ROOT = "Packages/com.unity.ugui/";
        private const string UI_NAMESPACE = "Ezg.BugLogger.UI";

        [Test]
        public void OverlayPrefab_DependsOnlyOnPackageAssets()
        {
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<GameObject>(PREFAB_PATH), "Thiếu prefab " + PREFAB_PATH);

            foreach (var dependency in AssetDatabase.GetDependencies(PREFAB_PATH, true))
            {
                if (dependency.StartsWith(PACKAGE_ROOT)) continue;

                Assert.IsTrue(dependency.StartsWith(UGUI_ROOT) && dependency.EndsWith(".cs"),
                    "Prefab tham chiếu asset ngoài package: " + dependency);
            }
        }

        [Test]
        public void OverlayPrefab_HasNoMissingScriptOrReference()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PREFAB_PATH);
            foreach (var component in prefab.GetComponentsInChildren<Component>(true))
            {
                Assert.IsNotNull(component, "Prefab có missing script");

                var property = new SerializedObject(component).GetIterator();
                while (property.Next(true))
                    if (property.propertyType == SerializedPropertyType.ObjectReference &&
                        property.objectReferenceValue == null && property.objectReferenceInstanceIDValue != 0)
                        Assert.Fail($"{component.name}.{component.GetType().Name}.{property.propertyPath} mất tham chiếu");
            }
        }

        [Test]
        public void OverlayPrefab_UIComponentsFullyWired()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PREFAB_PATH);
            foreach (var behaviour in prefab.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (behaviour.GetType().Namespace != UI_NAMESPACE) continue;

                var property = new SerializedObject(behaviour).GetIterator();
                property.NextVisible(true);
                while (property.NextVisible(true))
                {
                    if (property.propertyType != SerializedPropertyType.ObjectReference ||
                        IsEmptyByDesign(property.propertyPath)) continue;

                    Assert.IsNotNull(property.objectReferenceValue,
                        $"{behaviour.name}.{behaviour.GetType().Name}.{property.propertyPath} chưa được nối");
                }
            }
        }

        // Tham chiếu của lớp uGUI gốc để trống là hợp lệ: ô kế khi điều hướng, sprite theo trạng thái (InputField),
        // material của Graphic (trống = material UI mặc định — các lớp nét vẽ của màn chỉnh sửa ảnh).
        private static bool IsEmptyByDesign(string propertyPath)
        {
            return propertyPath == "m_Script" || propertyPath == "m_Material" ||
                   propertyPath.StartsWith("m_Navigation.", StringComparison.Ordinal) ||
                   propertyPath.StartsWith("m_SpriteState.", StringComparison.Ordinal);
        }

        /// <summary>Màn chỉnh sửa ảnh: mỗi công cụ một nút (đúng thứ tự enum), mỗi màu nét một ô chọn.</summary>
        [Test]
        public void OverlayPrefab_ImageEditorHasEveryToolAndColor()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PREFAB_PATH);
            var editor = prefab.GetComponentInChildren<BugLoggerImageEditor>(true);
            Assert.IsNotNull(editor, "Prefab thiếu BugLoggerImageEditor");

            var serialized = new SerializedObject(editor);
            var tools = serialized.FindProperty("_toolButtons");
            Assert.AreEqual(Enum.GetValues(typeof(BugEditorTool)).Length, tools.arraySize);
            foreach (BugEditorTool tool in Enum.GetValues(typeof(BugEditorTool)))
            {
                var button = tools.GetArrayElementAtIndex((int)tool).objectReferenceValue;
                Assert.IsNotNull(button, "Thiếu nút công cụ " + tool);
                Assert.AreEqual(tool.ToString(), button.name, "Nút ở vị trí " + tool + " không phải công cụ đó");
            }

            Assert.AreEqual(BugLoggerPalette.AnnotationColors.Length, serialized.FindProperty("_swatches").arraySize);
            Assert.IsNotNull(prefab.GetComponentInChildren<BugLoggerForm>(true), "Prefab thiếu form");
        }

        /// <summary>
        ///     InputField gốc trên điện thoại: Android GameActivity không có ô nhập nổi trên bàn phím ⇒ QA mất con trỏ, chỉ
        ///     xoá được từ cuối; vuốt trên ô không cuộn được form. Mọi ô nhập phải dùng bản đã chỉnh cho cảm ứng.
        /// </summary>
        [Test]
        public void OverlayPrefab_InputFieldsUseTouchFriendlyField()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PREFAB_PATH);
            var fields = prefab.GetComponentsInChildren<InputField>(true);
            Assert.IsNotEmpty(fields, "Prefab không có ô nhập");

            foreach (var field in fields)
                Assert.IsInstanceOf<BugLoggerInputField>(field, field.name + " phải dùng BugLoggerInputField");
        }
    }
}
