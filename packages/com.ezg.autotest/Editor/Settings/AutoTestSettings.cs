using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Ezg.AutoTest.Editor
{
    /// <summary>
    ///     Cấu hình auto test của project — lưu JSON ở ProjectSettings/EZGAutoTestSettings.json (commit vào git
    ///     để cả team + CI dùng chung). Sửa qua Project Settings &gt; Ezg &gt; Auto Test hoặc tab Settings của cửa sổ.
    /// </summary>
    public static class AutoTestSettings
    {
        public const string FILE_PATH = "ProjectSettings/EZGAutoTestSettings.json";

        static AutoTestConfig _config;
        static DateTime _loadedWriteTime;

        /// <summary>Cấu hình hiện tại (tự load lại nếu file bị sửa bên ngoài, vd git pull).</summary>
        public static AutoTestConfig Config
        {
            get
            {
                var path = AbsolutePath();
                var writeTime = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
                if (_config == null || writeTime != _loadedWriteTime) Load();
                return _config;
            }
        }

        public static string AbsolutePath()
        {
            return Path.Combine(Path.GetDirectoryName(Application.dataPath)!, FILE_PATH);
        }

        public static void Load()
        {
            var path = AbsolutePath();
            _config = new AutoTestConfig();
            if (File.Exists(path))
                try
                {
                    JsonUtility.FromJsonOverwrite(File.ReadAllText(path), _config);
                    _loadedWriteTime = File.GetLastWriteTimeUtc(path);
                    return;
                }
                catch (Exception e)
                {
                    Debug.LogError(AutoTestLog.PREFIX + $"Không đọc được {FILE_PATH}: {e.Message} — dùng mặc định.");
                }

            _loadedWriteTime = DateTime.MinValue;
        }

        public static void Save()
        {
            if (_config == null) return;
            var path = AbsolutePath();
            File.WriteAllText(path, JsonUtility.ToJson(_config, true));
            _loadedWriteTime = File.GetLastWriteTimeUtc(path);
        }

        /// <summary>Bản sao độc lập (runner dùng để cấu hình không đổi giữa chừng lượt chạy).</summary>
        public static AutoTestConfig Snapshot()
        {
            return JsonUtility.FromJson<AutoTestConfig>(JsonUtility.ToJson(Config));
        }

        public static void ResetToDefault()
        {
            _config = new AutoTestConfig();
            Save();
        }
    }

    /// <summary>ScriptableObject tạm để vẽ AutoTestConfig bằng SerializedObject (list, nested… sửa được hết).</summary>
    internal sealed class AutoTestSettingsHolder : ScriptableObject
    {
        public AutoTestConfig config = new();
    }

    /// <summary>Trang Project Settings &gt; Ezg &gt; Auto Test.</summary>
    internal sealed class AutoTestSettingsProvider : SettingsProvider
    {
        AutoTestSettingsHolder _holder;
        SerializedObject _so;
        Vector2 _scroll;

        AutoTestSettingsProvider() : base("Project/Ezg/Auto Test", SettingsScope.Project)
        {
            keywords = new[] { "auto test", "qa", "smoke", "ezg", "test" };
        }

        [SettingsProvider]
        public static SettingsProvider Create()
        {
            return new AutoTestSettingsProvider();
        }

        public override void OnActivate(string searchContext, UnityEngine.UIElements.VisualElement rootElement)
        {
            Bind();
        }

        public override void OnDeactivate()
        {
            if (_holder != null) UnityEngine.Object.DestroyImmediate(_holder);
            _holder = null;
            _so = null;
        }

        void Bind()
        {
            if (_holder == null)
            {
                _holder = ScriptableObject.CreateInstance<AutoTestSettingsHolder>();
                _holder.hideFlags = HideFlags.HideAndDontSave;
            }

            _holder.config = AutoTestSettings.Snapshot();
            _so = new SerializedObject(_holder);
        }

        public override void OnGUI(string searchContext)
        {
            DrawSettingsGUI(ref _holder, ref _so, ref _scroll);
        }

        /// <summary>Vẽ toàn bộ cấu hình — dùng chung cho Project Settings và tab Settings của cửa sổ.</summary>
        internal static void DrawSettingsGUI(ref AutoTestSettingsHolder holder, ref SerializedObject so,
            ref Vector2 scroll)
        {
            if (holder == null || so == null)
            {
                holder = ScriptableObject.CreateInstance<AutoTestSettingsHolder>();
                holder.hideFlags = HideFlags.HideAndDontSave;
                holder.config = AutoTestSettings.Snapshot();
                so = new SerializedObject(holder);
            }

            EditorGUILayout.HelpBox(
                $"Lưu tại {AutoTestSettings.FILE_PATH} — commit file này để cả team và CI dùng chung cấu hình.",
                MessageType.Info);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Tải lại từ file", GUILayout.Width(120)))
                {
                    AutoTestSettings.Load();
                    holder.config = AutoTestSettings.Snapshot();
                    so = new SerializedObject(holder);
                }

                if (GUILayout.Button("Khôi phục mặc định", GUILayout.Width(140)) &&
                    EditorUtility.DisplayDialog("Khôi phục mặc định",
                        "Đặt lại toàn bộ cấu hình auto test về mặc định?", "Đặt lại", "Huỷ"))
                {
                    AutoTestSettings.ResetToDefault();
                    holder.config = AutoTestSettings.Snapshot();
                    so = new SerializedObject(holder);
                }

                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Mở file", GUILayout.Width(80)))
                    EditorUtility.RevealInFinder(AutoTestSettings.AbsolutePath());
            }

            so.Update();
            scroll = EditorGUILayout.BeginScrollView(scroll);
            var prop = so.FindProperty("config");
            var end = prop.GetEndProperty();
            var enterChildren = true;
            EditorGUI.BeginChangeCheck();
            while (prop.NextVisible(enterChildren) && !SerializedProperty.EqualContents(prop, end))
            {
                enterChildren = false;
                EditorGUILayout.PropertyField(prop, new GUIContent(ObjectNames.NicifyVariableName(prop.name)), true);
            }

            var changed = EditorGUI.EndChangeCheck();
            EditorGUILayout.EndScrollView();
            if (!changed) return;
            so.ApplyModifiedPropertiesWithoutUndo();
            var json = JsonUtility.ToJson(holder.config);
            JsonUtility.FromJsonOverwrite(json, AutoTestSettings.Config);
            AutoTestSettings.Save();
        }
    }
}
