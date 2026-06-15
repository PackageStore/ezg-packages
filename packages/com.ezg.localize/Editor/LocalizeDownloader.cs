using System;
using System.Collections.Generic;
using System.IO;
using Cysharp.Threading.Tasks;
using Ezg.Package.CsvReader;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;
using Progress = Cysharp.Threading.Tasks.Progress;
using CsvReaderLib = Ezg.Package.CsvReader.CsvReader;

namespace Ezg.Package.Localize.Editor
{
    /// <summary>
    /// ScriptableObject configuration for Google Sheets localization download settings.
    /// </summary>
    [CreateAssetMenu(fileName = "LocalizeDownloader", menuName = "Localization/Google Sheet", order = 1)]
    public class LocalizeDownloader : ScriptableObject
    {
        #region Fields

        public const string DefaultConfigAssetPath = "Assets/_Project/Localize/LocalizeDownloader.asset";
        public const string DefaultDownloadPath = "https://docs.google.com/spreadsheets/d/1JDChbnV93bYxYP7ulX4X6KYZk9XAS4kHQDihaEnD-3c";
        public const string DefaultSaveFilePath = "Assets/_Project/Localize/LocalizationData";

        public string downloadPath = DefaultDownloadPath;
        public string saveFilePath = DefaultSaveFilePath;
        public List<string> codeList = CreateDefaultCodeList();
        public List<LanguageItem> itemList = CreateDefaultItemList();

        #endregion

        #region Public Methods

        public static LocalizeDownloader CreateWithDefaultValues()
        {
            var downloader = CreateInstance<LocalizeDownloader>();
            downloader.ApplyDefaultValues();
            return downloader;
        }

        public void ApplyDefaultValues()
        {
            downloadPath = DefaultDownloadPath;
            saveFilePath = DefaultSaveFilePath;
            codeList = CreateDefaultCodeList();
            itemList = CreateDefaultItemList();
        }

        #endregion

        #region Private Methods

        private static List<string> CreateDefaultCodeList()
        {
            return new List<string>
            {
                "en",
                "vi",
                "pt",
                "id",
                "ru",
                "th",
                "es",
                "ko",
                "ja",
                "zhcn",
                "zhtw",
                "fr",
                "de",
                "it",
                "pl",
                "nl",
                "tr"
            };
        }

        private static List<LanguageItem> CreateDefaultItemList()
        {
            return new List<LanguageItem>
            {
                new() { sheetName = "common", sheetId = "1569151554", download = true },
                new() { sheetName = "item", sheetId = "448968395", download = true },
                new() { sheetName = "shop", sheetId = "838166083", download = true },
                new() { sheetName = "tutorial", sheetId = "2087979143", download = true },
                new() { sheetName = "scene", sheetId = "1285180064", download = true },
                new() { sheetName = "settings", sheetId = "694454083", download = true },
                new() { sheetName = "email", sheetId = "1762021964", download = true },
                new() { sheetName = "event", sheetId = "1357569831", download = true },
                new() { sheetName = "notification", sheetId = "1012864280", download = true }
            };
        }

        #endregion
    }

    /// <summary>
    /// Represents a single localization sheet definition.
    /// </summary>
    [Serializable]
    public class LanguageItem
    {
        #region Fields

        public string sheetName;
        public string sheetId;
        public bool download = true;

        #endregion
    }

#if UNITY_EDITOR

    public static class LocalizeDownloaderAssetMenu
    {
        #region Fields

        private const string CreateConfigMenuName = "Tools/Localization/Create Downloader Config";

        #endregion

        #region Public Methods

        [MenuItem(CreateConfigMenuName, false, 0)]
        public static void CreateDownloaderConfig()
        {
            var existing = AssetDatabase.LoadAssetAtPath<LocalizeDownloader>(LocalizeDownloader.DefaultConfigAssetPath);
            if (existing != null)
            {
                Selection.activeObject = existing;
                EditorGUIUtility.PingObject(existing);
                return;
            }

            var existingObject = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(LocalizeDownloader.DefaultConfigAssetPath);
            if (existingObject != null)
            {
                Selection.activeObject = existingObject;
                EditorGUIUtility.PingObject(existingObject);
                EditorUtility.DisplayDialog("Create Localize Downloader",
                    $"An asset already exists at:\n{LocalizeDownloader.DefaultConfigAssetPath}", "OK");
                return;
            }

            var folderPath = Path.GetDirectoryName(LocalizeDownloader.DefaultConfigAssetPath);
            if (!string.IsNullOrEmpty(folderPath) && !Directory.Exists(folderPath))
            {
                Directory.CreateDirectory(folderPath);
                AssetDatabase.Refresh();
            }

            var config = LocalizeDownloader.CreateWithDefaultValues();
            AssetDatabase.CreateAsset(config, LocalizeDownloader.DefaultConfigAssetPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Selection.activeObject = config;
            EditorGUIUtility.PingObject(config);
            Debug.Log($"[LocalizeDownloader] Created default config: {LocalizeDownloader.DefaultConfigAssetPath}");
        }

        #endregion
    }

    /// <summary>
    /// Custom property drawer for the LanguageItem class.
    /// </summary>
    [CustomPropertyDrawer(typeof(LanguageItem))]
    public class LanguageItemDrawer : PropertyDrawer
    {
        #region Public Methods

        /// <summary>
        /// Custom GUI drawing for LanguageItem in the Inspector.
        /// </summary>
        /// <param name="position">Rectangle on the screen to use for the property GUI.</param>
        /// <param name="property">The SerializedProperty to make the custom GUI for.</param>
        /// <param name="label">The label of this property.</param>
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            // Using BeginProperty / EndProperty on the parent property means that
            // prefab override logic works on the entire property.
            EditorGUI.BeginProperty(position, label, property);

            // Draw label
            position = EditorGUI.PrefixLabel(position, GUIUtility.GetControlID(FocusType.Passive), label);

            // Don't make child fields be indented
            var indent = EditorGUI.indentLevel;
            EditorGUI.indentLevel = 0;

            // Calculate rects
            var amountRect = new Rect(position.x, position.y, 60,
                position.height);
            var unitRect = new Rect(position.x + 65, position.y, 80,
                position.height);
            var downLoadRect = new Rect(position.x + 150, position.y, 155,
                position.height);

            // Draw fields - passs GUIContent.none to each so they are drawn without labels
            EditorGUI.PropertyField(amountRect, property.FindPropertyRelative("sheetName"), GUIContent.none);
            EditorGUI.PropertyField(unitRect, property.FindPropertyRelative("sheetId"), GUIContent.none);
            EditorGUI.PropertyField(downLoadRect, property.FindPropertyRelative("download"), GUIContent.none);


            // Set indent back to what it was
            EditorGUI.indentLevel = indent;

            EditorGUI.EndProperty();
        }

        #endregion
    }


    /// <summary>
    /// Custom editor for LocalizeDownloader providing download and asset generation commands.
    /// </summary>
    [CustomEditor(typeof(LocalizeDownloader))]
    public class LevelScriptEditor : UnityEditor.Editor
    {
        #region Fields

        private LocalizeDownloader data;
        private static string contentDownloading = string.Empty;

        #endregion

        #region Public Methods

        /// <summary>
        /// Draws the inspector GUI with download and generation buttons.
        /// </summary>
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            data = (LocalizeDownloader)target;
            if (contentDownloading.Equals(string.Empty))
            {
                if (GUILayout.Button("Select All"))
                    SelectAllLanguage();

                if (GUILayout.Button("Download Data Language"))
                    DownLoadFileLanguage();

                EditorGUILayout.Space(4);
                if (GUILayout.Button("Generate Assets from CSVs"))
                {
                    AssetDatabase.Refresh();
                    GenerateLanguageAssets(data.saveFilePath);
                    EditorUtility.DisplayDialog("Generate Assets", "Done!", "OK");
                }

                EditorGUILayout.Space(4);
                if (GUILayout.Button("Reset Default Values"))
                {
                    Undo.RecordObject(data, "Reset Localize Downloader Defaults");
                    data.ApplyDefaultValues();
                    EditorUtility.SetDirty(data);
                }
            }
            else
            {
                EditorGUILayout.HelpBox($"Download: {contentDownloading}", MessageType.None);
            }
        }

        /// <summary>
        /// Prompts the user and starts the language download process.
        /// </summary>
        [ContextMenu("DowLoadFileLanguage")]
        public void DownLoadFileLanguage()
        {
            if (EditorUtility.DisplayDialog("Download language",
                "Do you want to download language", "OK", "Cancel"))
            {
                DownloadLanguage().Forget();
            }
        }

        /// <summary>
        /// Selects or deselects all language items for download.
        /// </summary>
        public void SelectAllLanguage()
        {
            if (data.itemList == null) return;

            bool selectAll = false;
            for (int i = 0; i < data.itemList.Count; i++)
            {
                if (data.itemList[i].download == false)
                {
                    selectAll = true;
                    break;
                }
            }

            for (int i = 0; i < data.itemList.Count; i++)
            {
                data.itemList[i].download = selectAll;
            }
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// Asynchronously downloads the language CSVs from the configured Google Sheet.
        /// </summary>
        /// <returns>A UniTaskVoid task representation.</returns>
        private async UniTaskVoid DownloadLanguage()
        {
            var saveFilePath = NormalizeProjectPath(data.saveFilePath);
            data.saveFilePath = saveFilePath;

            if (Directory.Exists(saveFilePath))
                Directory.Delete(saveFilePath, true);
            Directory.CreateDirectory(saveFilePath);

            await UniTask.SwitchToMainThread();

            var index = 0;
            foreach (var languageItem in data.itemList)
            {
                if (languageItem.download)
                {
                    var path = $"{data.downloadPath}/export?format=csv&gid={languageItem.sheetId}";
                    var item = languageItem;
                    var request = await UnityWebRequest.Get(path).SendWebRequest().ToUniTask(
                        Progress.CreateOnlyValueChanged<float>((percent) =>
                        {
                            contentDownloading = $"{item.sheetName} => {percent * 100}%";
                        }));
                    TextProcessing(request.downloadHandler.text, index);

                    //if (!Directory.Exists(BasePostProcessor.CSV_FULL_PATH + "Localize/"))
                    //{
                    //    Directory.CreateDirectory(BasePostProcessor.CSV_FULL_PATH + "Localize/");
                    //}

                    //await using StreamWriter file = new StreamWriter(BasePostProcessor.CSV_FULL_PATH + "Localize/" + languageItem.sheetName + ".csv");

                    //var result = CsvReader.ParseCsv(request.downloadHandler.text);
                    //string a = "";
                    //foreach (var lang in result)
                    //{
                    //    for (var j = 0; j < lang.Length; j++)
                    //    {
                    //        a += lang[j].Replace("\n", "\\n").Replace("\r", "").Replace("\"", "")
                    //            .Replace("\"\"", "\"").Replace(",", "%%") + "," + (j == lang.Length - 1 ? "\n" : "");
                    //    }
                    //}

                    //await file.WriteLineAsync(a);
                }

                index++;
            }

            contentDownloading = string.Empty;

            AssetDatabase.Refresh();
            GenerateLanguageAssets(saveFilePath);

            EditorUtility.DisplayDialog("Download language",
                "Download language finish", "OK");
        }


        /// <summary>
        /// Formats and writes the localized dictionary to a CSV file.
        /// </summary>
        /// <param name="popup">The sheet/category name.</param>
        /// <param name="languageCode">The target language code.</param>
        /// <param name="dict">The dictionary containing localization key-value pairs.</param>
        public void WriteFileLocalize(string popup, string languageCode, Dictionary<string, string> dict)
        {
            string contentWrite = "key~value\n";
            foreach (var item in dict)
            {
                if (!string.IsNullOrEmpty(item.Key))
                {
                    contentWrite += $"{item.Key}~{item.Value}\n";
                }
            }

            var url = Path.Combine(NormalizeProjectPath(data.saveFilePath), languageCode, popup + ".csv");
            SafeWriteAllText(url, contentWrite);
        }

        /// <summary>
        /// Safely writes text to a file, ensuring directories are created and write permissions are set.
        /// </summary>
        /// <param name="outFile">The target file path.</param>
        /// <param name="text">The text content to write.</param>
        /// <returns>True if the write succeeded, otherwise false.</returns>
        public static bool SafeWriteAllText(string outFile, string text)
        {
            try
            {
                if (string.IsNullOrEmpty(outFile))
                {
                    return false;
                }

                CheckFileAndCreateDirWhenNeeded(outFile);
                if (File.Exists(outFile))
                {
                    File.SetAttributes(outFile, FileAttributes.Normal);
                }

                File.WriteAllText(outFile, text);
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"SafeWriteAllText failed! path = {outFile} with err = {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Checks if a file's directory exists and creates it if needed.
        /// </summary>
        /// <param name="filePath">The target file path.</param>
        public static void CheckFileAndCreateDirWhenNeeded(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
            {
                return;
            }

            FileInfo fileInfo = new FileInfo(filePath);
            DirectoryInfo dirInfo = fileInfo.Directory;
            if (dirInfo != null && !dirInfo.Exists)
            {
                Directory.CreateDirectory(dirInfo.FullName);
            }
        }

        /// <summary>
        /// Scans all .csv files under csvRootPath (e.g. "Assets/_Project/Core/Localize/LocalizationData")
        /// and generates/updates the corresponding LanguageData .asset in the sibling Resources/ folder.
        /// Mapping: {parent}/LocalizationData/{lang}/{name}.csv
        ///       →  {parent}/Resources/LocalizationData/{lang}/{name}.asset
        /// </summary>
        /// <param name="csvRootPath">The root directory path of the CSV files.</param>
        public static void GenerateLanguageAssets(string csvRootPath)
        {
            csvRootPath = NormalizeProjectPath(csvRootPath);

            if (string.IsNullOrEmpty(csvRootPath) || !Directory.Exists(csvRootPath))
            {
                Debug.LogError($"[LocalizeDownloader] GenerateLanguageAssets: path not found: {csvRootPath}");
                return;
            }

            // Normalize to asset path (relative to project root)
            var projectRoot   = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            var csvRootFull   = Path.GetFullPath(csvRootPath);
            var csvRootAsset  = csvRootFull.Replace(projectRoot, "").TrimStart(Path.DirectorySeparatorChar).Replace('\\', '/');

            // Resources root is sibling: replace "LocalizationData" segment with "Resources/LocalizationData"
            var resRootAsset = csvRootAsset.Replace("/LocalizationData", "/Resources/LocalizationData");

            var csvFiles = Directory.GetFiles(csvRootFull, "*.csv", SearchOption.AllDirectories);
            int count = 0;

            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var csvFull in csvFiles)
                {
                    var csvAsset    = csvFull.Replace(projectRoot, "").TrimStart(Path.DirectorySeparatorChar).Replace('\\', '/');
                    var relFromRoot = csvAsset.Substring(csvRootAsset.Length).TrimStart('/');  // e.g. "en/common.csv"
                    var assetPath   = $"{resRootAsset}/{Path.ChangeExtension(relFromRoot, ".asset")}";

                    var csvText = File.ReadAllText(csvFull);
                    BasePostProcessor.GenCustomAsset(csvText, assetPath, typeof(Ezg.Package.Localize.Localization.LanguageData), false);
                    count++;
                }
                AssetDatabase.SaveAssets();
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                AssetDatabase.Refresh();
            }

            Debug.Log($"[LocalizeDownloader] Generated {count} LanguageData assets → {resRootAsset}");
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// Processes the raw sheet CSV text and extracts translation dictionaries per language.
        /// </summary>
        /// <param name="content">The raw CSV content.</param>
        /// <param name="index">The current sheet item index.</param>
        private void TextProcessing(string content, int index)
        {
            List<Language> langList = new List<Language>();
            var csvTable = CsvReaderLib.ParseCsv(content); //CSVSerializer.ParseCSV(content, ',', true);
            var lang = csvTable[0];
            for (int i = 0; i < lang.Length; i++)
            {
                var languageCode = lang[i].Trim().Split('-');
                if (languageCode.Length == 2 &&
                    langList.Find(x => x.code.Equals(languageCode[1])) == null &&
                    data.codeList.Contains(languageCode[1]))
                {
                    langList.Add(new Language(i, languageCode[1]));
                    var folderPath = Path.Combine(NormalizeProjectPath(data.saveFilePath), languageCode[1]);
                    if (Directory.Exists(folderPath) == false)
                    {
                        Directory.CreateDirectory(folderPath);
                    }
                }
            }

            for (int i = 1; i < csvTable.Count; i++)
            {
                lang = csvTable[i];
                var key = lang[0];
                foreach (Language language in langList)
                {
                    if (language.keyDict.ContainsKey(key))
                    {
                        Debug.Log("KeyExist: " + key);
                        continue;
                    }

                    if (key.Equals(string.Empty))
                    {
                        Debug.Log("Key is empty: " + i);
                        continue;
                    }

                    if (lang.Length < language.index)
                    {
                        continue;
                    }

                    try
                    {
                        string localize = lang[language.index].Replace("\n", "\\n").Replace("\r", "").Replace("\"", "")
                            .Replace("\"\"", "\"");
                        language.keyDict.Add(key, localize);
                    }
                    catch (Exception e)
                    {
                        Debug.Log($"Something Error: {language.code} - {language.index} \n=> {e}");
                    }
                }
            }

            foreach (var language in langList)
            {
                if (!data.codeList.Contains(language.code))
                    continue;

                WriteFileLocalize(data.itemList[index].sheetName.ToLowerInvariant(), language.code, language.keyDict);
            }
        }

        private static string NormalizeProjectPath(string path)
        {
            return string.IsNullOrEmpty(path) ? path : path.Replace('\\', '/');
        }

        #endregion
    }

    /// <summary>
    /// Helper class to keep track of a language's index and localization key-value dictionary during parsing.
    /// </summary>
    class Language
    {
        #region Fields

        public readonly int index;
        public readonly string code;
        public readonly Dictionary<string, string> keyDict;

        #endregion

        #region Initialize

        /// <summary>
        /// Initializes a new instance of the Language helper class.
        /// </summary>
        /// <param name="index">The column index of this language in the CSV.</param>
        /// <param name="code">The language code (e.g., "en", "vi").</param>
        public Language(int index, string code)
        {
            this.code = code;
            this.index = index;
            keyDict = new Dictionary<string, string>();
        }

        #endregion
    }
#endif
}
