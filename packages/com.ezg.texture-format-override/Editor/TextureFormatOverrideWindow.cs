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

    private Mode mode = Mode.PlatformOverride;

    private DefaultAsset targetFolder;
    private List<string> texturePaths = new List<string>();
    private bool forceSetTextureSize = false;
    private bool removePsdMatte = true;

    // Shared Android/iOS Settings
    private int platformMaxTextureSize = 2048;
    private TextureImporterFormat platformFormat = TextureImporterFormat.ASTC_4x4;
    private int platformCompressionQuality = 100;

    // Custom (Default tab) Max Size
    private int customMaxTextureSize = 2048;

    private Vector2 scrollPos;

    [MenuItem("Tools/EZG Technical Art/Texture Format Override")]
    public static void ShowWindow()
    {
        GetWindow<TextureFormatOverrideWindow>("Texture Override");
    }

    private void OnGUI()
    {
        EditorGUILayout.BeginVertical(new GUIStyle { padding = new RectOffset(10, 10, 10, 10) });

        EditorGUILayout.LabelField("Texture Format Override Tool", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        EditorGUI.BeginChangeCheck();
        targetFolder = (DefaultAsset)EditorGUILayout.ObjectField("Target Folder", targetFolder, typeof(DefaultAsset), false);
        if (EditorGUI.EndChangeCheck())
        {
            ScanFolder();
        }

        if (targetFolder != null)
        {
            EditorGUILayout.HelpBox($"Found {texturePaths.Count} PNG/PSD files in folder.", MessageType.Info);
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

        string buttonLabel = mode == Mode.PlatformOverride
            ? "Apply Platform Override to All PNG/PSD Files"
            : "Apply Custom Max Size to All PNG/PSD Files";
        if (GUILayout.Button(buttonLabel, GUILayout.Height(40)))
        {
            ApplySettings();
        }

        EditorGUILayout.EndVertical();
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

    private void ScanFolder()
    {
        texturePaths.Clear();
        if (targetFolder == null) return;

        string folderPath = AssetDatabase.GetAssetPath(targetFolder);
        if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath)) return;

        string[] guids = AssetDatabase.FindAssets("t:Texture", new[] { folderPath });
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            string lowerPath = path.ToLower();
            if (lowerPath.EndsWith(".png") || lowerPath.EndsWith(".psd"))
            {
                texturePaths.Add(path);
            }
        }
    }

    private void ApplySettings()
    {
        if (texturePaths.Count == 0)
        {
            EditorUtility.DisplayDialog("No Textures Found", "Please select a folder containing PNG or PSD images first.", "OK");
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
