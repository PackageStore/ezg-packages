using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace UnityFigmaBridge.Editor.Settings
{
    public static class FigmaSettingsDrawer
    {
        public static readonly string[] MainViewFields =
        {
            "DocumentUrl", "Source", "BridgePort",
            "OnlyImportSelectedPages", "ImportSelectionOnly", "OnlyImportListedScreens"
        };

        const string OtherTitle = "Other";
        const string FoldoutKeyPrefix = "EZG.FigmaBridge.Foldout.";

        static readonly (string Section, string[] Fields)[] Table =
        {
            ("Output Folders", new[]
            {
                "AssetsRootFolder", "ScreenPrefabFolder", "ComponentPrefabFolder", "PagePrefabFolder",
                "ImageFillFolder", "FontsFolder", "FontMaterialPresetsFolder"
            }),
            ("Pages & Naming", new[]
            {
                "ScreensPageName", "ComponentsPageName", "NameImageFillsByNodePath",
                "NameServerRendersByNodePath", "NumberDuplicateSiblings", "CreateScreenNameCSharpFile"
            }),
            ("Text", new[]
            {
                "FontOverride", "EnableGoogleFontsDownloads", "TextFitMode",
                "TextWidthPadding", "TextHeightPadding", "CharacterSpacing"
            }),
            ("Sprites", new[]
            {
                "SpriteMipmaps", "SpriteCompression", "OverrideMobileFormat",
                "MobileTextureFormat", "MobileCompressionQuality"
            }),
            ("Layout & Nine-Slice", new[]
            {
                "CollapseSliceGrids", "SliceServerRenders", "AddLayoutElements",
                "EnableAutoLayout", "ClipContentAsMask", "ButtonNamePattern"
            }),
            ("Advanced", new[]
            {
                "ServerRenderTopLevelExports", "ServerRenderBatchSize", "GenerateNodesMarkedForExport",
                "BuildPrototypeFlow", "RunTimeAssetsScenePath", "ScreenBindingNamespace"
            })
        };

        static readonly HashSet<string> WarnedMissing = new HashSet<string>();

        public static bool IsVisible(SerializedObject so, string fieldName)
        {
            if (so.FindProperty(fieldName) == null)
            {
                WarnMissing(fieldName);
                return false;
            }

            switch (fieldName)
            {
                case "RunTimeAssetsScenePath":
                    return Dependency(so, "BuildPrototypeFlow", p => p.boolValue);
                case "TextWidthPadding":
                case "TextHeightPadding":
                    return Dependency(so, "TextFitMode",
                        p => p.enumValueIndex == (int)TextFitMode.FixedRectAutoSize);
                case "MobileTextureFormat":
                case "MobileCompressionQuality":
                    return Dependency(so, "OverrideMobileFormat", p => p.boolValue);
                case "EnableGoogleFontsDownloads":
                    return Dependency(so, "FontOverride", p => p.objectReferenceValue == null);
                default:
                    return true;
            }
        }

        public static bool DrawField(SerializedObject so, string fieldName)
        {
            if (!IsVisible(so, fieldName)) return false;
            var prop = so.FindProperty(fieldName);
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(prop, true);
            return EditorGUI.EndChangeCheck();
        }

        public static bool DrawFoldouts(SerializedObject so)
        {
            so.Update();
            var changed = false;
            var claimed = new HashSet<string>(MainViewFields);

            foreach (var (section, fields) in Table)
            {
                var own = new List<string>();
                foreach (var field in fields)
                    if (claimed.Add(field)) own.Add(field);
                changed |= DrawSection(so, section, own);
            }

            var other = new List<string>();
            var it = so.GetIterator();
            var first = true;
            while (it.NextVisible(first))
            {
                first = false;
                if (it.propertyPath == "m_Script" || claimed.Contains(it.propertyPath)) continue;
                other.Add(it.propertyPath);
            }
            changed |= DrawSection(so, OtherTitle, other);

            so.ApplyModifiedProperties();
            return changed;
        }

        static bool DrawSection(SerializedObject so, string title, List<string> fields)
        {
            var visible = new List<string>();
            foreach (var field in fields)
                if (IsVisible(so, field)) visible.Add(field);
            if (visible.Count == 0) return false;

            var key = FoldoutKeyPrefix + title;
            var open = EditorGUILayout.Foldout(EditorPrefs.GetBool(key, false), title, true);
            if (open != EditorPrefs.GetBool(key, false)) EditorPrefs.SetBool(key, open);
            if (!open) return false;

            var changed = false;
            EditorGUI.indentLevel++;
            foreach (var field in visible)
                changed |= DrawField(so, field);
            EditorGUI.indentLevel--;
            return changed;
        }

        static bool Dependency(SerializedObject so, string dependencyName, System.Func<SerializedProperty, bool> test)
        {
            var dependency = so.FindProperty(dependencyName);
            if (dependency == null)
            {
                WarnMissing(dependencyName);
                return true;
            }
            return test(dependency);
        }

        static void WarnMissing(string fieldName)
        {
            if (WarnedMissing.Add(fieldName))
                Debug.LogWarning($"FigmaSettingsDrawer: settings has no field '{fieldName}'.");
        }
    }
}
