#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
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
    private static readonly GUIContent ReplaceContentLabel = new GUIContent("Replace Content in File (Project only)",
        "Thay Find→Replace trong NỘI DUNG file. CHỈ áp dụng cho file text trong Project — KHÔNG áp dụng cho GameObject trong Scene. File nhị phân bị bỏ qua. Cần nhập 'Find'.");
    private static readonly GUIContent CapFirstLabel = new GUIContent("Capitalize First Letter", "Viết hoa chữ cái đầu tiên của tên.");
    private static readonly GUIContent CapEachLabel = new GUIContent("Capitalize Each Word",
        "Viết hoa chữ cái đầu mỗi từ (phân tách bởi space / _ - .). Ưu tiên hơn 'Capitalize First Letter'.");
    private static readonly GUIContent ExtensionLabel = new GUIContent("New Extension", "Đổi đuôi file, vd: png hoặc .psd. Để trống để giữ nguyên. Chỉ áp dụng cho file trong Project.");

    private static readonly Color TargetColor = new Color(0.24f, 0.75f, 0.32f);
    private static readonly Color EmptyColor = new Color(0.90f, 0.70f, 0.20f);

    // "Replace Content in File" only touches text assets — never rewrite binary files (would corrupt them).
    private static readonly HashSet<string> TextExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".cs", ".txt", ".json", ".csv", ".md", ".xml", ".shader", ".cginc", ".hlsl",
        ".compute", ".asmdef", ".asmref", ".uss", ".uxml", ".yml", ".yaml", ".js", ".html", ".css"
    };

    private static readonly Regex WordPattern = new Regex(@"([^\s_\-\.]+)|([\s_\-\.]+)", RegexOptions.Compiled);

    private string find = "";
    private string newExtension = "";
    private string prefix = "";
    private string replaceWith = "";
    private string suffix = "";
    private int trimEnd;
    private int trimStart;
    private bool replaceContentInFile;
    private bool capitalizeFirstLetter;
    private bool capitalizeEachWord;

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
        replaceContentInFile = EditorGUILayout.Toggle(ReplaceContentLabel, replaceContentInFile);
        if (replaceContentInFile)
            EditorGUILayout.HelpBox(
                "'Replace Content in File' CHỈ sửa nội dung file TEXT trong Project (vd .cs .txt .json).\n" +
                "• KHÔNG áp dụng cho GameObject trong Scene.\n" +
                "• File nhị phân (ảnh, prefab, .asset...) bị bỏ qua để tránh hỏng file.\n" +
                "• Cần nhập 'Find'.",
                MessageType.Warning);
        EditorGUILayout.EndVertical();

        EditorGUILayout.LabelField("Viết hoa (Capitalize)", EditorStyles.boldLabel);
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        capitalizeFirstLetter = EditorGUILayout.Toggle(CapFirstLabel, capitalizeFirstLetter);
        capitalizeEachWord = EditorGUILayout.Toggle(CapEachLabel, capitalizeEachWord);
        if (capitalizeEachWord)
            EditorGUILayout.HelpBox("'Capitalize Each Word' được ưu tiên — viết hoa chữ đầu mỗi nhóm ký tự phân tách bởi space / _ - .", MessageType.Info);
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

            // Capitalize (Each Word wins over First Letter when both are on)
            if (!string.IsNullOrEmpty(newName))
            {
                if (capitalizeEachWord) newName = CapitalizeWordsKeepingDelimiters(newName);
                else if (capitalizeFirstLetter) newName = char.ToUpper(newName[0]) + (newName.Length > 1 ? newName.Substring(1) : "");
            }

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
                ReplaceFileContent(assetPath);
                var directory = assetPath.Substring(0, assetPath.LastIndexOf('/') + 1);
                var error = AssetDatabase.MoveAsset(assetPath, directory + newName + extension);
                if (!string.IsNullOrEmpty(error)) Debug.LogError($"Power Rename failed for '{assetPath}': {error}");
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    // Find→Replace inside a Project text file's content. Project files only; binary files are skipped.
    private void ReplaceFileContent(string assetPath)
    {
        if (!replaceContentInFile || string.IsNullOrEmpty(find) || !File.Exists(assetPath)) return;

        var ext = Path.GetExtension(assetPath);
        if (!TextExtensions.Contains(ext))
        {
            Debug.LogWarning($"Power Rename: bỏ qua replace content cho file không phải text '{assetPath}' ({ext}).");
            return;
        }

        try
        {
            var content = File.ReadAllText(assetPath);
            File.WriteAllText(assetPath, content.Replace(find, replaceWith));
        }
        catch (Exception e)
        {
            Debug.LogError($"Power Rename: replace content lỗi cho '{assetPath}': {e.Message}");
        }
    }

    private static string CapitalizeWordsKeepingDelimiters(string input)
    {
        var result = new StringBuilder(input.Length);
        foreach (Match m in WordPattern.Matches(input))
        {
            if (m.Groups[1].Success)
            {
                var word = m.Groups[1].Value;
                result.Append(char.ToUpper(word[0]));
                if (word.Length > 1) result.Append(word.Substring(1));
            }
            else
            {
                result.Append(m.Groups[2].Value);
            }
        }

        return result.ToString();
    }

    [MenuItem("Tools/EZG Technical Art/Power Rename")]
    public static void ShowWindow()
    {
        GetWindow(typeof(PowerRename), false, "Power Rename");
    }
}
#endif
