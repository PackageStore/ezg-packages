using System;
using System.IO;
using FigCraftBridge.Editor.Source;
using UnityEditor;
using UnityEngine;

namespace FigCraftBridge.Editor.Settings
{
    /// <summary>The source block of the window: File (a saved .figcraft) or App (the running FigCraft app).</summary>
    public sealed class FigmaConnectionPanel : IDisposable
    {
        static readonly string[] SourceLabels = { "File (.figcraft)", "FigCraft app" };

        public FigmaConnectionPanel(Action repaint) { }

        public void Tick(FigCraftBridgeSettings settings) { }

        public bool Draw(SerializedObject so, FigCraftBridgeSettings settings)
        {
            if (so == null || settings == null) return false;
            so.Update();
            var changed = false;

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                var sourceProp = so.FindProperty(nameof(FigCraftBridgeSettings.Source));
                EditorGUI.BeginChangeCheck();
                var index = GUILayout.Toolbar(sourceProp.enumValueIndex, SourceLabels);
                if (EditorGUI.EndChangeCheck())
                {
                    sourceProp.enumValueIndex = index;
                    changed = true;
                }

                if (settings.Source == FigCraftSourceKind.File) changed |= DrawFile(so, settings);
                else changed |= DrawApp(so);

                EditorGUI.BeginChangeCheck();
                EditorGUILayout.PropertyField(so.FindProperty(nameof(FigCraftBridgeSettings.FigCraftCliPath)), new GUIContent("FigCraft CLI Path"));
                changed |= EditorGUI.EndChangeCheck();
                var cli = FigCraftCli.Locate(settings.FigCraftCliPath);
                if (cli == null) EditorGUILayout.HelpBox(FigCraftSourceText.NoCliBody(settings.FigCraftCliPath), MessageType.Error);
                else EditorGUILayout.LabelField("CLI: " + cli, EditorStyles.miniLabel);
            }

            so.ApplyModifiedProperties();
            return changed;
        }

        bool DrawFile(SerializedObject so, FigCraftBridgeSettings settings)
        {
            var changed = false;
            var fileProp = so.FindProperty(nameof(FigCraftBridgeSettings.FigCraftFile));
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUI.BeginChangeCheck();
                EditorGUILayout.PropertyField(fileProp, new GUIContent("FigCraft File", fileProp.tooltip));
                changed |= EditorGUI.EndChangeCheck();
                if (GUILayout.Button("Browse", GUILayout.Width(64)))
                {
                    var start = File.Exists(settings.FigCraftFileFullPath) ? Path.GetDirectoryName(settings.FigCraftFileFullPath) : Directory.GetCurrentDirectory();
                    var picked = EditorUtility.OpenFilePanel("Pick a .figcraft file", start, "figcraft");
                    if (!string.IsNullOrEmpty(picked))
                    {
                        fileProp.stringValue = ProjectRelative(picked);
                        changed = true;
                        GUI.FocusControl(null);
                    }
                }
            }
            so.ApplyModifiedProperties();
            var path = settings.FigCraftFileFullPath;
            if (string.IsNullOrEmpty(path)) EditorGUILayout.HelpBox(FigCraftSourceText.NoFileBody(null), MessageType.Warning);
            else if (!File.Exists(path)) EditorGUILayout.HelpBox(FigCraftSourceText.NoFileBody(settings.FigCraftFile), MessageType.Error);
            else
                EditorGUILayout.LabelField($"{Path.GetFileName(path)}: {new FileInfo(path).Length / 1048576.0:0.0} MB, saved {File.GetLastWriteTime(path):yyyy-MM-dd HH:mm}",
                    EditorStyles.miniLabel);
            return changed;
        }

        static bool DrawApp(SerializedObject so)
        {
            var portProp = so.FindProperty(nameof(FigCraftBridgeSettings.AppPort));
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(portProp, new GUIContent("Control Port", portProp.tooltip));
            var changed = EditorGUI.EndChangeCheck();
            EditorGUILayout.HelpBox($"Reads the file in front in FigCraft, unsaved changes included. FigCraft must run with --control {portProp.intValue}. " +
                                    "Image fills still come from the saved file.", MessageType.Info);
            return changed;
        }

        static string ProjectRelative(string path)
        {
            var project = Path.GetFullPath(Directory.GetCurrentDirectory()).Replace('\\', '/').TrimEnd('/') + "/";
            var full = Path.GetFullPath(path).Replace('\\', '/');
            return full.StartsWith(project, StringComparison.Ordinal) ? full.Substring(project.Length) : full;
        }

        public string CurrentFileKey(FigCraftBridgeSettings settings)
        {
            if (settings == null) return null;
            return settings.Source == FigCraftSourceKind.App ? $"app:{settings.AppPort}" : settings.FigCraftFile?.Replace('\\', '/');
        }

        public bool CanSync(FigCraftBridgeSettings settings, out string reason)
        {
            reason = null;
            if (settings == null)
            {
                reason = "No settings asset.";
                return false;
            }
            try
            {
                FigmaFileTargets.Resolve(settings);
                return true;
            }
            catch (FigmaFileTargetException e)
            {
                reason = e.Message;
                return false;
            }
        }

        public void Dispose() { }
    }
}
