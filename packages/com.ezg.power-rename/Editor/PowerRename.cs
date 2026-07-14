#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

public class PowerRename : EditorWindow
{
    private static readonly GUIContent PrefixLabel = new GUIContent("Prefix", "Tiền tố thêm vào đầu tên.");
    private static readonly GUIContent SuffixLabel = new GUIContent("Suffix", "Hậu tố thêm vào cuối tên.");
    private static readonly GUIContent TrimStartLabel = new GUIContent("Trim Start", "Số ký tự cắt bỏ tính từ đầu tên.");
    private static readonly GUIContent TrimEndLabel = new GUIContent("Trim End", "Số ký tự cắt bỏ tính từ cuối tên.");
    private static readonly GUIContent FindLabel = new GUIContent("Find", "Chuỗi cần tìm trong tên (áp dụng cả phần đuôi file).");
    private static readonly GUIContent ReplaceLabel = new GUIContent("Replace With", "Chuỗi thay thế cho chuỗi tìm được.");
    private static readonly GUIContent ExtensionLabel = new GUIContent("New Extension", "Đổi đuôi file, vd: png hoặc .psd. Để trống để giữ nguyên. Chỉ áp dụng cho file trong Project.");

    private static readonly Color TargetColor = new Color(0.24f, 0.75f, 0.32f);
    private static readonly Color EmptyColor = new Color(0.90f, 0.70f, 0.20f);

    private string find = "";
    private string newExtension = "";
    private string prefix = "";
    private string replaceWith = "";
    private string suffix = "";
    private int trimEnd;
    private int trimStart;

    private GUIStyle _targetStyle;

    private void OnGUI()
    {
        GUILayout.Label("Power Rename", EditorStyles.boldLabel);

        DrawTargetIndicator();
        EditorGUILayout.Space();

        EditorGUILayout.LabelField("Thêm tiền tố / hậu tố (Append)", EditorStyles.boldLabel);
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        prefix = EditorGUILayout.TextField(PrefixLabel, prefix);
        suffix = EditorGUILayout.TextField(SuffixLabel, suffix);
        EditorGUILayout.EndVertical();

        EditorGUILayout.LabelField("Cắt ký tự (Trim)", EditorStyles.boldLabel);
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        trimStart = EditorGUILayout.IntField(TrimStartLabel, trimStart);
        trimEnd = EditorGUILayout.IntField(TrimEndLabel, trimEnd);
        EditorGUILayout.EndVertical();

        EditorGUILayout.LabelField("Tìm & Thay thế (Find & Replace)", EditorStyles.boldLabel);
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        find = EditorGUILayout.TextField(FindLabel, find);
        replaceWith = EditorGUILayout.TextField(ReplaceLabel, replaceWith);
        EditorGUILayout.EndVertical();

        EditorGUILayout.LabelField("Đổi đuôi file (Extension)", EditorStyles.boldLabel);
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        newExtension = EditorGUILayout.TextField(ExtensionLabel, newExtension);
        EditorGUILayout.EndVertical();

        EditorGUILayout.Space();
        if (GUILayout.Button("Rename")) Rename();
    }

    // Green line telling the user which kind of target the rename will hit.
    private void DrawTargetIndicator()
    {
        var sceneCount = 0;
        var assetCount = 0;
        foreach (var obj in Selection.objects)
        {
            if (AssetDatabase.Contains(obj)) assetCount++;
            else if (obj is GameObject) sceneCount++;
        }

        if (_targetStyle == null) _targetStyle = new GUIStyle(EditorStyles.boldLabel) { wordWrap = true };

        string message;
        if (sceneCount == 0 && assetCount == 0)
        {
            _targetStyle.normal.textColor = EmptyColor;
            message = "Chưa chọn đối tượng nào để đổi tên.";
        }
        else
        {
            _targetStyle.normal.textColor = TargetColor;
            if (assetCount == 0) message = $"▶ Sẽ đổi tên: {sceneCount} GameObject trong Scene";
            else if (sceneCount == 0) message = $"▶ Sẽ đổi tên: {assetCount} File trong Project";
            else message = $"▶ Sẽ đổi tên: {sceneCount} GameObject trong Scene + {assetCount} File trong Project";
        }

        EditorGUILayout.LabelField(message, _targetStyle);
    }

    private void Rename()
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

    [MenuItem("Tools/Power Rename")]
    public static void ShowWindow()
    {
        GetWindow(typeof(PowerRename), false, "Power Rename");
    }
}
#endif
