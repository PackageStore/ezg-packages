#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Ezg.FigmaFeatureImporter.Editor
{
    /// <summary>
    ///     Inspector của <see cref="FigmaFeatureImportSettings" />. Viết bằng IMGUI thuần (KHÔNG Odin) để
    ///     package compile được ở project không có Odin; project có Odin vẫn dùng inspector này vì
    ///     <c>CustomEditor</c> thắng drawer mặc định của Odin.
    /// </summary>
    [CustomEditor(typeof(FigmaFeatureImportSettings))]
    internal sealed class FigmaFeatureImportSettingsEditor : UnityEditor.Editor
    {
        private static readonly string[] Tabs = { "Màn hình", "Template map", "Vỏ & codegen" };

        private static readonly string[] ShellFields =
        {
            "screenTemplatePath", "templatesRoot", "rawScreensFolder", "spriteReuseRoot", "defaultFontPath",
            "font", "textWidthPadding", "textHeightPadding",
            "backgroundNode", "popupNode", "fullScreenNode", "popupBodyPath", "popupFrameNodes",
            "fullScreenContentNode", "fullScreenChromeNodes", "buttonFaceSlot", "buttonTextSlot",
            "popupMarkerPattern", "snapshotFolder",
            "codegenUsings", "localizeCallFormat", "baseControllerTypeName"
        };

        private int _tab;

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.HelpBox(
                "Mỗi frame Figma → một màn. Bridge ghi prefab thô, package này biến nó thành variant của " +
                "screenTemplatePath. Trống danh sách Màn hình vẫn chạy: frame không khai được dựng theo mặc định.",
                MessageType.None);

            _tab = GUILayout.Toolbar(_tab, Tabs);
            EditorGUILayout.Space();

            switch (_tab)
            {
                case 0:
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("screens"), true);
                    break;
                case 1:
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("defaultButtonTemplate"));
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("templates"), true);
                    break;
                default:
                    foreach (var field in ShellFields)
                    {
                        var property = serializedObject.FindProperty(field);
                        if (property != null) EditorGUILayout.PropertyField(property, true);
                    }
                    break;
            }

            serializedObject.ApplyModifiedProperties();

            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Rebuild Screens")) RebuildScreens();
                if (GUILayout.Button("Audit Screens")) AuditScreens();
            }
            if (GUILayout.Button("Seed lại mặc định (ghi đè template map)")) SeedDefaults();
        }

        private static void RebuildScreens()
        {
            var ship = FigmaFeatureImporter.ImportAllEnabled();
            var inPlace = FigmaFeatureImporter.RebuildAllInPlace();
            Debug.Log($"[FigmaImport] Rebuild: ship {ship.FindAll(r => r.ok).Count}/{ship.Count} · " +
                      $"variant tại chỗ {inPlace.FindAll(r => r.ok).Count}/{inPlace.Count}.");
        }

        private static void AuditScreens()
        {
            foreach (var report in FigmaImportAudit.RunAllInPlace())
            {
                if (report.pass) Debug.Log($"[FigmaImport] Audit PASS {report.frame}\n{report}");
                else Debug.LogError($"[FigmaImport] Audit FAIL {report.frame}\n{report}");
            }
        }

        private void SeedDefaults()
        {
            if (!EditorUtility.DisplayDialog("Seed lại mặc định",
                    "Ghi đè bảng template map + defaultButtonTemplate bằng seed mặc định (template EZG). " +
                    "Danh sách Màn hình KHÔNG bị đụng. Tiếp tục?", "Seed", "Huỷ"))
                return;

            var settings = (FigmaFeatureImportSettings)target;
            var screens = settings.screens;
            Undo.RecordObject(settings, "Seed Figma import settings");
            settings.SeedDefaults();
            settings.screens = screens; // SeedDefaults xoá danh sách màn — giữ lại của người dùng
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssetIfDirty(settings);
        }
    }
}
#endif
