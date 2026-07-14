using UnityEngine;
using UnityEditor;
using System.IO;
using System.Collections.Generic;
using System.Linq;

public class TextureFormatOverrideWindow : EditorWindow
{
    private enum Mode { PlatformOverride, CustomMaxSize }

    private const int MINIMUM_MAX_SIZE = 32;
    private const int MAXIMUM_MAX_SIZE = 4096;

    private static readonly string[] SIZE_LABELS = { "32", "64", "128", "256", "512", "1024", "2048", "4096", "8192" };
    private static readonly int[] SIZE_VALUES = { 32, 64, 128, 256, 512, 1024, 2048, 4096, 8192 };

    private static readonly Color TargetColor = new Color(0.24f, 0.75f, 0.32f);
    private static readonly Color EmptyColor = new Color(0.90f, 0.70f, 0.20f);

    private Mode mode = Mode.PlatformOverride;

    private readonly List<string> texturePaths = new List<string>();
    private readonly HashSet<string> _seen = new HashSet<string>();
    private int _folderCount;
    private int _fileCount;
    private bool recursive = true;

    private bool forceSetTextureSize = false;
    private bool removePsdMatte = true;

    // Shared Android/iOS Settings
    private int platformMaxTextureSize = 2048;
    private TextureImporterFormat platformFormat = TextureImporterFormat.ASTC_4x4;
    private int platformCompressionQuality = 100;

    // Custom (Default tab) Max Size
    private int customMaxTextureSize = 2048;

    private Vector2 scrollPos;
    private GUIStyle _targetStyle;

    [MenuItem("Tools/EZG Technical Art/Texture Format Override")]
    public static void ShowWindow()
    {
        GetWindow<TextureFormatOverrideWindow>("Texture Override");
    }

    private void OnEnable()
    {
        RebuildTargets();
    }

    private void OnSelectionChange()
    {
        RebuildTargets();
    }

    private void OnGUI()
    {
        EditorGUILayout.BeginVertical(new GUIStyle { padding = new RectOffset(10, 10, 10, 10) });

        EditorGUILayout.LabelField("Texture Format Override Tool", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        DrawTargetIndicator();

        EditorGUI.BeginChangeCheck();
        recursive = EditorGUILayout.ToggleLeft(
            new GUIContent("Recursive", "Khi chọn folder: bao gồm cả texture trong subfolder. Tắt = chỉ texture nằm trực tiếp trong folder. Không ảnh hưởng tới texture được chọn trực tiếp."),
            recursive);
        if (EditorGUI.EndChangeCheck())
        {
            RebuildTargets();
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Chọn MỘT chế độ (loại trừ lẫn nhau):", EditorStyles.miniBoldLabel);

        EditorGUILayout.BeginHorizontal();
        DrawModeRadio("Override for Android and iOS", Mode.PlatformOverride);
        DrawModeRadio("Custom Max Size", Mode.CustomMaxSize);
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.Space();

        scrollPos = EditorGUILayout.BeginScrollView(scrollPos);
        if (mode == Mode.PlatformOverride)
            DrawPlatformOverrideBody();
        else
            DrawCustomMaxSizeBody();
        EditorGUILayout.EndScrollView();

        EditorGUILayout.Space();

        EditorGUI.BeginDisabledGroup(texturePaths.Count == 0);
        string buttonLabel = mode == Mode.PlatformOverride
            ? "Apply Platform Override to Selected Textures"
            : "Apply Custom Max Size to Selected Textures";
        if (GUILayout.Button(buttonLabel, GUILayout.Height(40)))
        {
            ApplySettings();
        }
        EditorGUI.EndDisabledGroup();

        EditorGUILayout.EndVertical();
    }

    // Green/amber line telling the user which textures the apply will hit.
    private void DrawTargetIndicator()
    {
        if (_targetStyle == null)
            _targetStyle = new GUIStyle(EditorStyles.boldLabel) { wordWrap = true };

        string message;
        if (texturePaths.Count == 0)
        {
            _targetStyle.normal.textColor = EmptyColor;
            message = "▶ Chưa có texture nào — chọn texture hoặc folder trong Project window.";
        }
        else
        {
            _targetStyle.normal.textColor = TargetColor;
            var parts = new List<string>();
            if (_folderCount > 0) parts.Add($"{_folderCount} folder");
            if (_fileCount > 0) parts.Add($"{_fileCount} file được chọn");
            message = $"▶ Sẽ sửa {texturePaths.Count} texture (từ {string.Join(" + ", parts)})";
        }
        EditorGUILayout.LabelField(message, _targetStyle);
    }

    private void DrawPlatformOverrideBody()
    {
        EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.ExpandWidth(true));

        forceSetTextureSize = EditorGUILayout.Toggle(
            new GUIContent("Force Set Texture Size", "Bật để ép đặt Max Texture Size cho Android và iOS. Tắt để giữ nguyên kích thước texture hiện tại. NÊN TẮT!"),
            forceSetTextureSize);
        removePsdMatte = EditorGUILayout.Toggle(
            new GUIContent("Remove PSD Matte", "Bật để loại bỏ viền matte trên file PSD. Tắt để giữ nguyên thiết lập matte hiện tại của PSD. Lưu ý: Tùy chọn này chỉ áp dụng cho file PSD và sẽ không ảnh hưởng đến file PNG. NÊN BẬT!"),
            removePsdMatte);

        EditorGUI.BeginDisabledGroup(!forceSetTextureSize);
        platformMaxTextureSize = EditorGUILayout.IntPopup("Max Texture Size", platformMaxTextureSize, SIZE_LABELS, SIZE_VALUES);
        EditorGUI.EndDisabledGroup();

        platformFormat = (TextureImporterFormat)EditorGUILayout.EnumPopup("Format", platformFormat);
        platformCompressionQuality = EditorGUILayout.IntSlider("Compression Quality", platformCompressionQuality, 0, 100);

        EditorGUILayout.EndVertical();
    }

    private void DrawCustomMaxSizeBody()
    {
        EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.ExpandWidth(true));
        EditorGUILayout.HelpBox("Đặt Max Size ở tab Default (KHÔNG đụng vào override iOS/Android). NPOT scale sẽ được đặt về None.", MessageType.Info);
        customMaxTextureSize = EditorGUILayout.IntSlider("Default Max Size", customMaxTextureSize, MINIMUM_MAX_SIZE, MAXIMUM_MAX_SIZE);
        EditorGUILayout.EndVertical();
    }

    private void DrawModeRadio(string label, Mode radioMode)
    {
        EditorGUI.BeginChangeCheck();
        bool now = EditorGUILayout.ToggleLeft(label, mode == radioMode, EditorStyles.boldLabel);
        if (EditorGUI.EndChangeCheck() && now)
        {
            mode = radioMode;
        }
    }

    private void RebuildTargets()
    {
        texturePaths.Clear();
        _seen.Clear();
        _folderCount = 0;
        _fileCount = 0;

        foreach (string guid in Selection.assetGUIDs)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(path)) continue;

            if (AssetDatabase.IsValidFolder(path))
            {
                _folderCount++;
                CollectTexturesInFolder(path);
            }
            else if (IsTextureAsset(path) && _seen.Add(path))
            {
                _fileCount++;
                texturePaths.Add(path);
            }
        }

        Repaint();
    }

    private void CollectTexturesInFolder(string folder)
    {
        string[] guids = AssetDatabase.FindAssets("t:Texture", new[] { folder });
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (!recursive)
            {
                string dir = Path.GetDirectoryName(path)?.Replace('\\', '/');
                if (dir != folder) continue;
            }
            if (IsTextureAsset(path) && _seen.Add(path))
            {
                texturePaths.Add(path);
            }
        }
    }

    private static bool IsTextureAsset(string path)
    {
        return AssetImporter.GetAtPath(path) is TextureImporter;
    }

    private void ApplySettings()
    {
        if (texturePaths.Count == 0)
        {
            EditorUtility.DisplayDialog("No Textures Selected", "Select textures or folders in the Project window first.", "OK");
            return;
        }

        int count = 0;
        try
        {
            AssetDatabase.StartAssetEditing();
            foreach (string path in texturePaths)
            {
                TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null) continue;

                bool changed = mode == Mode.PlatformOverride
                    ? ApplyPlatformOverride(importer, path)
                    : ApplyCustomMaxSize(importer);

                if (changed)
                {
                    EditorUtility.DisplayProgressBar("Applying Settings", $"Processing {Path.GetFileName(path)}", (float)count / texturePaths.Count);
                    importer.SaveAndReimport();
                    count++;
                }
            }
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
            EditorUtility.ClearProgressBar();
        }

        Debug.Log($"[TextureFormatOverrideWindow] Applied settings to {count} textures.");
    }

    private bool ApplyPlatformOverride(TextureImporter importer, string path)
    {
        if (path.EndsWith(".psd", System.StringComparison.OrdinalIgnoreCase))
        {
            ApplyPsdRemoveMatte(importer);
        }

        ApplyPlatformSettings(importer, "Android");
        ApplyPlatformSettings(importer, "iPhone");
        return true;
    }

    private bool ApplyCustomMaxSize(TextureImporter importer)
    {
        importer.maxTextureSize = customMaxTextureSize;
        importer.npotScale = TextureImporterNPOTScale.None;
        return true;
    }

    private void ApplyPlatformSettings(TextureImporter importer, string platform)
    {
        TextureImporterPlatformSettings settings = importer.GetPlatformTextureSettings(platform);
        settings.overridden = true;
        if (forceSetTextureSize)
        {
            settings.maxTextureSize = platformMaxTextureSize;
        }
        settings.format = platformFormat;
        settings.compressionQuality = platformCompressionQuality;
        importer.SetPlatformTextureSettings(settings);
    }

    private bool ApplyPsdRemoveMatte(TextureImporter importer)
    {
        SerializedObject serializedImporter = new SerializedObject(importer);
        serializedImporter.Update();

        SerializedProperty psdRemoveMatteProperty = serializedImporter.FindProperty("m_PSDRemoveMatte");
        if (psdRemoveMatteProperty == null)
        {
            Debug.LogWarning($"[TextureFormatOverrideWindow] Could not find m_PSDRemoveMatte on {importer.assetPath}.");
            return false;
        }

        if (psdRemoveMatteProperty.boolValue == removePsdMatte)
        {
            return false;
        }

        psdRemoveMatteProperty.boolValue = removePsdMatte;
        serializedImporter.ApplyModifiedPropertiesWithoutUndo();
        return true;
    }
}
