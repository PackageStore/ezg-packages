using System.Collections.Generic;
using UnityEngine;

namespace AnimationCurveManipulationTool {

    public class AnimationCurveManipulationConfig : ScriptableObject {

#if UNITY_EDITOR
        internal const float DefaultPresetSize = 80f;
        internal const float MinPresetSize = 50f;
        internal const float MaxPresetSize = 200f;
        internal static readonly Color DefaultPresetColor = Color.white;

        [SerializeField] internal bool useCustomSelectionOrder = true;
        [SerializeField] internal bool curvePositionRight = false;
        [SerializeField] internal bool autoApply = true;
        [SerializeField] internal bool displayButtonIcon = true;
        [SerializeField] internal Color presetColor = DefaultPresetColor;
        [SerializeField] internal float presetSize = DefaultPresetSize;
#endif

        [SerializeField] private List<CurveLibrary> m_curveLibraries = new List<CurveLibrary>();

        public List<CurveLibrary> curveLibraries => m_curveLibraries;


        internal const string k_SettingsResourcePath = "AnimationCurveManipulationConfig";
        internal const string k_SettingsPath = "Assets/Resources/" + k_SettingsResourcePath + ".asset";

        private static AnimationCurveManipulationConfig instance;
        public static AnimationCurveManipulationConfig Get() {
            if (instance == null) {
                instance = Resources.Load(k_SettingsResourcePath) as AnimationCurveManipulationConfig;
#if UNITY_EDITOR
                if (instance == null) {
                    instance = GetOrCreateSettings();
                    Debug.LogWarning("AnimationCurveManipulationConfig not found, and is now being newly created.");
                    ResetPresets();
                }
#endif
            }
            return instance;
        }

        public static bool TryGet(out AnimationCurveManipulationConfig _config) {
            _config = Get();
            return _config != null;
        }

#if UNITY_EDITOR

        internal static AnimationCurveManipulationConfig GetOrCreateSettings() {
            var settings = (AnimationCurveManipulationConfig)Resources.Load(k_SettingsResourcePath);
            if (settings == null) {
                settings = ScriptableObject.CreateInstance<AnimationCurveManipulationConfig>();
                if (!System.IO.Directory.Exists(Application.dataPath + "/Resources")) {
                    System.IO.Directory.CreateDirectory(Application.dataPath + "/Resources");
                }
                UnityEditor.AssetDatabase.CreateAsset(settings, k_SettingsPath);
                UnityEditor.AssetDatabase.SaveAssets();
            }
            return settings;
        }

        internal static UnityEditor.SerializedObject GetSerializedSettings() {
            return new UnityEditor.SerializedObject(GetOrCreateSettings());
        }

        internal static bool IsSettingsAvailable() {
            return Resources.Load(k_SettingsResourcePath) != null;
        }

        internal static void ResetPresets() {
            var inst = Get();
            if (inst == null) return;

            UnityEditor.Undo.RecordObject(inst, "reset presets");
            inst.curveLibraries.Clear();
            var textAssets = Resources.LoadAll<TextAsset>("CurveMaster_BuiltInPresets");
            foreach (var textAsset in textAssets) {
                var json = textAsset.text;
                inst.ImportLibraryByJson(json);
            }
        }

        internal void ImportLibrary(string filePath) {
            if (!string.IsNullOrEmpty(filePath)) {
                UnityEditor.Undo.RecordObject(this, "import curve library");
                var json = System.IO.File.ReadAllText(filePath);
                ImportLibraryByJson(json);
            }
        }

        private void ImportLibraryByJson(string json) {
            var imported = CurveLibrary.FromJSON(json);
            bool foundExisting = false;
            for (int i = 0; i < curveLibraries.Count; i++) {
                var library = curveLibraries[i];
                if (library.Compare(imported)) {
                    foundExisting = true;
                    int index = i;
                    if (UnityEditor.EditorUtility.DisplayDialog("Library Already Exists", "Are you sure you want replace " + imported.libraryName + "?", "Replace", "Cancel")) {
                        curveLibraries[index] = imported;
                    }
                    break;
                }
            }
            if (!foundExisting) {
                curveLibraries.Add(imported);
                UnityEditor.EditorUtility.SetDirty(this);
#if UNITY_2021_3_OR_NEWER
                UnityEditor.AssetDatabase.SaveAssetIfDirty(this);
#else
                UnityEditor.AssetDatabase.SaveAssets();
#endif
            }
        }

#endif

    }

}