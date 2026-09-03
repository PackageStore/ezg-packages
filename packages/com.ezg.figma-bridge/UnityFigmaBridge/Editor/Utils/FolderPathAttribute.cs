using UnityEditor;
using UnityEngine;

namespace UnityFigmaBridge.Editor.Utils
{
    /// <summary>
    /// Draws a string path as Unity's own folder object field: drag a folder in, or use the field's
    /// picker. The value stays a string so a folder that does not exist yet still resolves and gets
    /// created on import, and so a wiped output folder does not leave a dangling reference.
    /// </summary>
    public sealed class FolderPathAttribute : PropertyAttribute
    {
    }

    /// <summary>
    /// Implemented by the object that owns [FolderPath] fields, so a blank field can show the
    /// folder the import falls back to instead of an empty object slot.
    /// </summary>
    public interface IFolderDefaults
    {
        string DefaultFolder(string propertyPath);
    }

    [CustomPropertyDrawer(typeof(FolderPathAttribute))]
    public sealed class FolderPathDrawer : PropertyDrawer
    {
        // Width of the field's own picker button, kept clear of the overlay
        private const float PickerButtonWidth = 19f;

        private static GUIStyle s_PathStyle;
        private static GUIStyle s_DefaultStyle;

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            if (property.propertyType != SerializedPropertyType.String)
            {
                EditorGUI.PropertyField(position, property, label);
                return;
            }

            label = EditorGUI.BeginProperty(position, label, property);

            var path = property.stringValue;
            var folder = string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<DefaultAsset>(path);
            var defaultFolder = string.IsNullOrEmpty(path) ? DefaultFolderFor(property) : null;

            EditorGUI.BeginChangeCheck();
            var picked = EditorGUI.ObjectField(position, FieldLabel(label, path, defaultFolder), folder,
                typeof(DefaultAsset), false) as DefaultAsset;
            if (EditorGUI.EndChangeCheck()) Apply(property, picked, label.text);

            DrawPathOverlay(position, path, folder, defaultFolder);

            EditorGUI.EndProperty();
        }

        private static GUIContent FieldLabel(GUIContent label, string path, string defaultFolder)
        {
            var hint = defaultFolder != null
                ? $"Blank: the import uses {defaultFolder}."
                : AssetDatabase.IsValidFolder(path) ? null : "This folder does not exist yet. It is created on import.";

            if (hint == null) return label;
            return new GUIContent(label.text,
                string.IsNullOrEmpty(label.tooltip) ? hint : $"{label.tooltip}\n\n{hint}");
        }

        private static string DefaultFolderFor(SerializedProperty property)
        {
            return property.serializedObject.targetObject is IFolderDefaults defaults
                ? defaults.DefaultFolder(property.propertyPath)
                : null;
        }

        /// <summary>
        /// The object field shows an asset name, or "None" when nothing is assigned, and neither
        /// says which folder the import writes to. Redraw the field's text area over the top with
        /// the path itself, leaving the picker button as the field drew it.
        /// </summary>
        private static void DrawPathOverlay(Rect position, string path, DefaultAsset folder, string defaultFolder)
        {
            var valueRect = new Rect(position.x + EditorGUIUtility.labelWidth + 2f, position.y,
                position.width - EditorGUIUtility.labelWidth - 2f, position.height);
            var textRect = new Rect(valueRect.x, valueRect.y,
                Mathf.Max(0f, valueRect.width - PickerButtonWidth), valueRect.height);

            // Called on every event so the group's control id stays in step across passes
            GUI.BeginGroup(textRect);
            if (Event.current.type == EventType.Repaint && textRect.width > 0f)
            {
                s_PathStyle ??= new GUIStyle(EditorStyles.label) { alignment = TextAnchor.MiddleLeft };
                s_DefaultStyle ??= new GUIStyle(s_PathStyle) { fontStyle = FontStyle.Italic };
                s_DefaultStyle.normal.textColor = Dimmed(EditorStyles.label.normal.textColor);

                var content = folder != null
                    ? new GUIContent(path, EditorGUIUtility.ObjectContent(folder, typeof(DefaultAsset)).image)
                    : new GUIContent(defaultFolder != null ? $"Default: {defaultFolder}" : $"{path}  (created on import)");
                var style = folder != null ? s_PathStyle : s_DefaultStyle;

                var pad = EditorStyles.objectField.padding.left;
                EditorStyles.objectField.Draw(new Rect(0f, 0f, valueRect.width, valueRect.height),
                    GUIContent.none, false, false, false, false);
                style.Draw(new Rect(pad, 0f, textRect.width - pad, textRect.height),
                    content, false, false, false, false);
            }
            GUI.EndGroup();
        }

        private static Color Dimmed(Color color)
        {
            color.a *= 0.65f;
            return color;
        }

        private static void Apply(SerializedProperty property, DefaultAsset picked, string fieldName)
        {
            if (picked == null)
            {
                property.stringValue = "";
                return;
            }

            var pickedPath = AssetDatabase.GetAssetPath(picked);
            if (AssetDatabase.IsValidFolder(pickedPath))
                property.stringValue = pickedPath;
            else
                Debug.LogWarning($"[FigmaBridge] '{pickedPath}' is not a folder, so {fieldName} was left unchanged.");
        }
    }
}
