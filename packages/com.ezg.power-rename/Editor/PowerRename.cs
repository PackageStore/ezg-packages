#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

public class PowerRename : EditorWindow
{
    private string find = "";
    private string newExtension = "";
    private string prefix = "";
    private string replaceWith = "";
    private string suffix = "";
    private int trimEnd;
    private int trimStart;

    private void OnGUI()
    {
        GUILayout.Label("Power Rename", EditorStyles.boldLabel);

        // Prefix Field
        prefix = EditorGUILayout.TextField("Prefix", prefix);

        // Suffix Field
        suffix = EditorGUILayout.TextField("Suffix", suffix);

        // Trim Fields
        GUILayout.Label("Trim Characters", EditorStyles.boldLabel);
        trimStart = EditorGUILayout.IntField("Trim Start", trimStart);
        trimEnd = EditorGUILayout.IntField("Trim End", trimEnd);

        // Find and Replace Fields
        find = EditorGUILayout.TextField("Find", find);
        replaceWith = EditorGUILayout.TextField("Replace with", replaceWith);

        // Change Extension (leave empty to keep the original extension; asset files only)
        newExtension = EditorGUILayout.TextField("New Extension", newExtension);

        if (GUILayout.Button("Rename"))
        {
            foreach (var obj in Selection.objects)
            {
                var newName = obj.name;
                var isAsset = AssetDatabase.Contains(obj);
                var assetPath = isAsset ? AssetDatabase.GetAssetPath(obj) : null;
                var extension = isAsset ? Path.GetExtension(assetPath) : "";

                // Find and Replace (also applies to the extension, e.g. ".png" -> ".psd")
                if (!string.IsNullOrEmpty(find))
                {
                    newName = newName.Replace(find, replaceWith);
                    extension = extension.Replace(find, replaceWith);
                }

                // Override the extension entirely (takes precedence over find/replace above)
                if (isAsset && !string.IsNullOrEmpty(newExtension))
                    extension = newExtension.StartsWith(".") ? newExtension : "." + newExtension;

                // Trim Start
                if (trimStart > 0 && newName.Length > trimStart) newName = newName.Substring(trimStart);

                // Trim End
                if (trimEnd > 0 && newName.Length > trimEnd) newName = newName.Substring(0, newName.Length - trimEnd);

                // Add Prefix and Suffix
                newName = prefix + newName + suffix;

                // Rename GameObjects in the scene (prefab assets are GameObjects too, so exclude persistent ones)
                if (obj is GameObject && !isAsset)
                {
                    var go = obj as GameObject;
                    Undo.RecordObject(go, "Power Rename");
                    go.name = newName;
                }
                // Rename Assets in the project folder (MoveAsset, unlike RenameAsset, can change the extension)
                else if (isAsset)
                {
                    var directory = assetPath.Substring(0, assetPath.LastIndexOf('/') + 1);
                    var error = AssetDatabase.MoveAsset(assetPath, directory + newName + extension);
                    if (!string.IsNullOrEmpty(error)) Debug.LogError($"Power Rename failed for '{assetPath}': {error}");
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }
    }

    [MenuItem("Tools/Power Rename")]
    public static void ShowWindow()
    {
        GetWindow(typeof(PowerRename), false, "Power Rename");
    }
}
#endif