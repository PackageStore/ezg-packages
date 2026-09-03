using System.IO;
using UnityEditor;
using UnityEngine;

namespace UnityFigmaBridge.Editor.Utils
{
    /// <summary>
    /// Draws a string path as a folder picker: drag a folder from the Project window or browse for
    /// one. The value stays a string so a folder that does not exist yet still resolves and gets
    /// created on import, and so a wiped output folder does not leave a dangling reference.
    /// </summary>
    public sealed class FolderPathAttribute : PropertyAttribute
    {
    }

    [CustomPropertyDrawer(typeof(FolderPathAttribute))]
    public sealed class FolderPathDrawer : PropertyDrawer
    {
        private const float BrowseButtonWidth = 28f;
        private const float Gap = 2f;

        private static readonly GUIContent s_BrowseContent = new("...", "Browse for a folder");

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            if (property.propertyType != SerializedPropertyType.String)
            {
                EditorGUI.PropertyField(position, property, label);
                return;
            }

            label = EditorGUI.BeginProperty(position, label, property);

            var fieldRect = new Rect(position.x, position.y, position.width - BrowseButtonWidth - Gap, position.height);
            var buttonRect = new Rect(fieldRect.xMax + Gap, position.y, BrowseButtonWidth, position.height);

            var path = property.stringValue;
            var folderExists = !string.IsNullOrEmpty(path) && AssetDatabase.IsValidFolder(path);

            if (folderExists || string.IsNullOrEmpty(path))
                DrawObjectField(fieldRect, property, label, folderExists ? path : null);
            else
                DrawMissingFolderField(fieldRect, property, label, path);

            var browse = GUI.Button(buttonRect, s_BrowseContent, EditorStyles.miniButton);
            EditorGUI.EndProperty();

            if (browse)
                Browse(property, label.text, folderExists ? path : null);
        }

        private static void DrawObjectField(Rect rect, SerializedProperty property, GUIContent label, string existingPath)
        {
            var current = existingPath == null ? null : AssetDatabase.LoadAssetAtPath<DefaultAsset>(existingPath);

            EditorGUI.BeginChangeCheck();
            var picked = EditorGUI.ObjectField(rect, label, current, typeof(DefaultAsset), false) as DefaultAsset;
            if (!EditorGUI.EndChangeCheck()) return;

            if (picked == null)
            {
                property.stringValue = "";
                return;
            }

            var pickedPath = AssetDatabase.GetAssetPath(picked);
            if (AssetDatabase.IsValidFolder(pickedPath))
                property.stringValue = pickedPath;
            else
                Debug.LogWarning($"[FigmaBridge] '{pickedPath}' is not a folder, so {label.text} was left unchanged.");
        }

        /// <summary>
        /// An object field would show "None" for a path whose folder is not in the project yet, so
        /// the path is shown as text until the import creates the folder.
        /// </summary>
        private static void DrawMissingFolderField(Rect rect, SerializedProperty property, GUIContent label, string path)
        {
            var missingLabel = new GUIContent(label.text, $"{label.tooltip}\n\nFolder does not exist yet. It is created on import.".Trim());

            EditorGUI.BeginChangeCheck();
            var edited = EditorGUI.TextField(rect, missingLabel, path);
            if (EditorGUI.EndChangeCheck()) property.stringValue = edited.Trim();
        }

        private static void Browse(SerializedProperty property, string title, string existingPath)
        {
            var startFolder = existingPath == null ? Application.dataPath : Path.GetFullPath(existingPath);
            var absolute = EditorUtility.OpenFolderPanel(title, startFolder, "");
            if (string.IsNullOrEmpty(absolute))
            {
                GUIUtility.ExitGUI();
                return;
            }

            if (TryGetProjectRelativePath(absolute, out var assetPath))
            {
                property.stringValue = assetPath;
                property.serializedObject.ApplyModifiedProperties();
            }
            else
            {
                EditorUtility.DisplayDialog("Invalid Folder",
                    "Choose a folder inside this project's Assets folder.", "OK");
            }

            // A modal panel inside OnGUI leaves the layout stack half-built for this event
            GUIUtility.ExitGUI();
        }

        private static bool TryGetProjectRelativePath(string absolutePath, out string assetPath)
        {
            var full = Path.GetFullPath(absolutePath).Replace('\\', '/').TrimEnd('/');
            var dataPath = Path.GetFullPath(Application.dataPath).Replace('\\', '/').TrimEnd('/');

            if (full == dataPath)
            {
                assetPath = "Assets";
                return true;
            }

            if (full.StartsWith(dataPath + "/"))
            {
                assetPath = "Assets" + full.Substring(dataPath.Length);
                return true;
            }

            assetPath = null;
            return false;
        }
    }
}
