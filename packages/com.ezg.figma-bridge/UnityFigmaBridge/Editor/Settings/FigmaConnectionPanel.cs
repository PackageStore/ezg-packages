using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityFigmaBridge.Editor.Bridge;
using UnityFigmaBridge.Editor.Source;
using UnityFigmaBridge.Editor.Utils;

namespace UnityFigmaBridge.Editor.Settings
{
    public sealed class FigmaConnectionPanel : IDisposable
    {
        const string TokenControlName = "FigmaConnectionPanelToken";
        const string ConnectingText = "Connecting...";
        const string LastImportedPrefix = "Last imported: ";
        const string NotConnectedSuffix = " (not connected)";
        const string InvalidUrlText = "Invalid Figma Document URL";
        const string NoTokenHelp = "Stored in Unity PlayerPrefs on this machine - never written into the settings asset.";
        static readonly string[] SourceLabels = { "REST API", "Bridge (EZG Tools)" };

        readonly Action m_Repaint;
        BridgeStatusMonitor m_Monitor;
        string m_TokenDraft;
        string m_TokenStored;

        public FigmaConnectionPanel(Action repaint)
        {
            m_Repaint = repaint;
        }

        public void Tick(UnityFigmaBridgeSettings settings)
        {
            if (settings == null) return;
            if (settings.Source == FigmaSourceKind.Bridge)
            {
                if (m_Monitor == null) m_Monitor = new BridgeStatusMonitor(m_Repaint);
                m_Monitor.Tick(settings.BridgePort);
            }
            else
            {
                m_Monitor?.Dispose();
                m_Monitor = null;
            }
        }

        public bool Draw(SerializedObject so, UnityFigmaBridgeSettings settings)
        {
            if (so == null || settings == null) return false;
            so.Update();
            var changed = false;

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                var sourceProp = so.FindProperty("Source");
                EditorGUI.BeginChangeCheck();
                var index = GUILayout.Toolbar(sourceProp.enumValueIndex, SourceLabels);
                if (EditorGUI.EndChangeCheck())
                {
                    sourceProp.enumValueIndex = index;
                    so.ApplyModifiedProperties();
                    changed = true;
                }

                if (settings.Source == FigmaSourceKind.Bridge) changed |= DrawBridgeBranch(so, settings);
                else changed |= DrawRestBranch(so, settings);
            }

            so.ApplyModifiedProperties();
            return changed;
        }

        public string CurrentFileKey(UnityFigmaBridgeSettings settings)
        {
            if (settings == null) return null;
            if (settings.Source != FigmaSourceKind.Bridge) return settings.FileId;
            return ResolveBridge(settings, out var file, out _) ? file.FileKey : null;
        }

        public bool CanSync(UnityFigmaBridgeSettings settings, out string reason)
        {
            reason = null;
            if (settings == null)
            {
                reason = "No settings asset.";
                return false;
            }

            if (settings.Source != FigmaSourceKind.Bridge)
            {
                if (string.IsNullOrEmpty(settings.FileId))
                {
                    reason = InvalidUrlText;
                    return false;
                }
                if (FigmaAccessToken.Read() == null)
                {
                    reason = "No Figma access token set.";
                    return false;
                }
                return true;
            }

            var status = m_Monitor == null ? BridgeStatus.Idle : m_Monitor.Status;
            if (status == BridgeStatus.NoHub)
            {
                reason = FigmaSourceText.BridgeNoHub(m_Monitor.Port);
                return false;
            }
            if (status != BridgeStatus.Connected)
            {
                reason = ConnectingText;
                return false;
            }
            if (!ResolveBridge(settings, out _, out var error))
            {
                reason = error;
                return false;
            }
            return true;
        }

        public void Dispose()
        {
            m_Monitor?.Dispose();
            m_Monitor = null;
        }

        bool DrawRestBranch(SerializedObject so, UnityFigmaBridgeSettings settings)
        {
            var changed = false;

            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(so.FindProperty("DocumentUrl"));
            if (EditorGUI.EndChangeCheck())
            {
                so.ApplyModifiedProperties();
                changed = true;
            }

            var fileId = settings.FileId;
            var isValid = !string.IsNullOrEmpty(fileId);
            EditorGUILayout.HelpBox(
                isValid ? $"Valid Figma Document URL - FileID: {fileId}" : InvalidUrlText,
                isValid ? MessageType.Info : MessageType.Error);

            var stored = FigmaAccessToken.Read() ?? "";
            if (m_TokenStored == null)
            {
                m_TokenDraft = stored;
                m_TokenStored = stored;
            }
            else if (stored != m_TokenStored && GUI.GetNameOfFocusedControl() != TokenControlName)
            {
                m_TokenDraft = stored;
                m_TokenStored = stored;
            }

            GUI.SetNextControlName(TokenControlName);
            m_TokenDraft = EditorGUILayout.TextField("Token", m_TokenDraft);
            EditorGUILayout.LabelField(NoTokenHelp, EditorStyles.miniLabel);

            if (GUILayout.Button("Save Token"))
            {
                FigmaAccessToken.Write(m_TokenDraft);
                var written = FigmaAccessToken.Read() ?? "";
                if (written != m_TokenStored) changed = true;
                m_TokenDraft = written;
                m_TokenStored = written;
                GUI.FocusControl(null);
            }

            var hasToken = !string.IsNullOrEmpty(FigmaAccessToken.Read());
            EditorGUILayout.LabelField("Status", hasToken ? "Token set" : "No token set", EditorStyles.miniLabel);
            return changed;
        }

        bool DrawBridgeBranch(SerializedObject so, UnityFigmaBridgeSettings settings)
        {
            var changed = false;

            var portProp = so.FindProperty("BridgePort");
            EditorGUI.BeginChangeCheck();
            var port = EditorGUILayout.IntField(new GUIContent(portProp.displayName, portProp.tooltip), portProp.intValue);
            if (EditorGUI.EndChangeCheck())
            {
                portProp.intValue = Mathf.Clamp(port, HubProtocol.DefaultPort, HubProtocol.DefaultPort + HubProtocol.PortCount - 1);
                so.ApplyModifiedProperties();
                changed = true;
            }

            var status = m_Monitor == null ? BridgeStatus.Idle : m_Monitor.Status;
            HubFile picked = null;
            if (status == BridgeStatus.Idle || status == BridgeStatus.Connecting)
            {
                EditorGUILayout.LabelField(ConnectingText);
            }
            else if (status == BridgeStatus.NoHub)
            {
                EditorGUILayout.HelpBox(FigmaSourceText.BridgeNoHub(m_Monitor.Port), MessageType.Warning);
            }
            else
            {
                if (ResolveBridge(settings, out picked, out var error))
                    EditorGUILayout.LabelField("Connected: " + FigmaSourceText.DescribeFile(picked.FileName, picked.FileKey));
                else
                    EditorGUILayout.HelpBox(error, MessageType.Warning);

                changed |= DrawFilePicker(so);
            }

            var boundKey = so.FindProperty("BridgeFileKey").stringValue;
            if (!string.IsNullOrEmpty(boundKey) && (status != BridgeStatus.Connected || picked == null || picked.FileKey != boundKey))
                EditorGUILayout.HelpBox(LastImportedPrefix + so.FindProperty("BridgeFileName").stringValue, MessageType.Info);

            return changed;
        }

        bool DrawFilePicker(SerializedObject so)
        {
            var files = FigmaFileTargets.Importable(m_Monitor?.Files, m_Monitor.IsUsable);
            if (files.Count < 2) return false;

            var keyProp = so.FindProperty("BridgeFileKey");
            var nameProp = so.FindProperty("BridgeFileName");
            var boundKey = keyProp.stringValue;

            var labels = new List<string>();
            var current = -1;
            for (var i = 0; i < files.Count; i++)
            {
                labels.Add(FigmaSourceText.DescribeFile(files[i].FileName, files[i].FileKey));
                if (files[i].FileKey == boundKey) current = i;
            }

            var offset = 0;
            if (current < 0)
            {
                labels.Insert(0, nameProp.stringValue + NotConnectedSuffix);
                offset = 1;
                current = 0;
            }
            else current += offset;

            EditorGUI.BeginChangeCheck();
            var chosen = EditorGUILayout.Popup("File", current, labels.ToArray());
            if (!EditorGUI.EndChangeCheck() || chosen == current) return false;
            if (chosen - offset < 0) return false;

            var file = files[chosen - offset];
            keyProp.stringValue = file.FileKey;
            nameProp.stringValue = file.FileName ?? "";
            so.ApplyModifiedProperties();
            return true;
        }

        bool ResolveBridge(UnityFigmaBridgeSettings settings, out HubFile file, out string error)
        {
            file = null;
            error = "";
            if (m_Monitor == null || m_Monitor.Status != BridgeStatus.Connected) return false;
            return FigmaFileTargets.TryPick(settings, m_Monitor.Files, out file, out error, m_Monitor.IsUsable);
        }
    }
}
