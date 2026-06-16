#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Ezg.Core.UI
{
    public static class CopyPasteComponentCustom
    {
        #region Fields

        private static string _clipboardBuffer;

        #endregion

        #region Nested Classes

        [Serializable]
        private class ComponentClipboardData
        {
            public string SystemType;
            public string ComponentJson;
            public List<ObjectReferenceData> References = new();
        }

        [Serializable]
        private class ObjectReferenceData
        {
            public string PropertyPath;
            public int InstanceID;
            public string GlobalObjectID;
        }

        #endregion

        #region Private Methods

        /// <summary>
        ///     Context menu command to copy a component's serialized state and object references as JSON to the clipboard.
        /// </summary>
        /// <param name="command">The contextual menu command data.</param>
        [MenuItem("CONTEXT/Component/BF Custom/Copy Component As Json", false, 150)]
        private static void CopyAsJson(MenuCommand command)
        {
            if (command.context is Component comp)
            {
                var data = new ComponentClipboardData
                {
                    SystemType = comp.GetType().FullName,
                    ComponentJson = EditorJsonUtility.ToJson(comp, true)
                };

                // Manually extract object references since EditorJsonUtility often fails to serialize scene instance IDs
                var so = new SerializedObject(comp);
                var prop = so.GetIterator();

                while (prop.Next(true))
                    if (prop.propertyType == SerializedPropertyType.ObjectReference &&
                        prop.objectReferenceValue != null)
                    {
                        // Exclude core structural references and Prefab tracking fields
                        if (prop.propertyPath == "m_GameObject" || prop.propertyPath == "m_Script" ||
                            prop.propertyPath == "m_CorrespondingSourceObject" ||
                            prop.propertyPath == "m_PrefabInstance" ||
                            prop.propertyPath == "m_PrefabAsset")
                            continue;

                        data.References.Add(new ObjectReferenceData
                        {
                            PropertyPath = prop.propertyPath,
                            InstanceID = prop.objectReferenceValue.GetInstanceID(),
                            GlobalObjectID = GlobalObjectId.GetGlobalObjectIdSlow(prop.objectReferenceValue).ToString()
                        });
                    }

                _clipboardBuffer = JsonUtility.ToJson(data, true);
                EditorGUIUtility.systemCopyBuffer = _clipboardBuffer;

                Debug.Log(
                    $"[CopyComponentCustom] Copied {comp.GetType().Name} to JSON (with {data.References.Count} custom references safely attached).");
            }
        }

        /// <summary>
        ///     Validates if the context menu paste option should be enabled by checking if valid JSON is in the buffer.
        /// </summary>
        /// <param name="command">The contextual menu command data.</param>
        /// <returns>True if the clipboard contains valid component JSON data; otherwise, false.</returns>
        [MenuItem("CONTEXT/Component/BF Custom/Paste Component As Json", true)]
        private static bool ValidatePasteAsJson(MenuCommand command)
        {
            return HasValidJsonInBuffer();
        }

        /// <summary>
        ///     Context menu command to paste a component's JSON state and restore serialized object references.
        /// </summary>
        /// <param name="command">The contextual menu command data.</param>
        [MenuItem("CONTEXT/Component/BF Custom/Paste Component As Json", false, 151)]
        private static void PasteAsJson(MenuCommand command)
        {
            if (command.context is Component comp)
            {
                var jsonToPaste = string.IsNullOrEmpty(EditorGUIUtility.systemCopyBuffer)
                    ? _clipboardBuffer
                    : EditorGUIUtility.systemCopyBuffer;

                if (string.IsNullOrEmpty(jsonToPaste) || !jsonToPaste.Contains("ComponentJson"))
                {
                    Debug.LogWarning(
                        "[CopyComponentCustom] No valid JSON component data found in clipboard. Please copy a component again.");
                    return;
                }

                Undo.RecordObject(comp, "Paste Component As Json");

                try
                {
                    var data = JsonUtility.FromJson<ComponentClipboardData>(jsonToPaste);

                    if (data == null || string.IsNullOrEmpty(data.ComponentJson))
                    {
                        Debug.LogWarning("[CopyComponentCustom] Clipboard data is not a valid Component Json.");
                        return;
                    }

                    var safeJson = data.ComponentJson;
                    var protectedFields = new[]
                    {
                        "m_Script",
                        "m_ObjectHideFlags",
                        "m_CorrespondingSourceObject",
                        "m_PrefabInstance",
                        "m_PrefabAsset",
                        "m_GameObject"
                    };

                    foreach (var field in protectedFields)
                        safeJson = Regex.Replace(safeJson, $@"""{field}""(\s*:)", $@"""__ignore_{field}""$1");

                    // 1. Paste basic component properties natively using safeJson
                    EditorJsonUtility.FromJsonOverwrite(safeJson, comp);

                    // 2. Restore strict object references that EditorJsonUtility missed or zeroed out
                    var so = new SerializedObject(comp);
                    foreach (var refData in data.References)
                    {
                        if (refData.PropertyPath == "m_GameObject" || refData.PropertyPath == "m_Script" ||
                            refData.PropertyPath == "m_CorrespondingSourceObject" ||
                            refData.PropertyPath == "m_PrefabInstance" ||
                            refData.PropertyPath == "m_PrefabAsset")
                            continue;

                        var prop = so.FindProperty(refData.PropertyPath);
                        if (prop != null && prop.propertyType == SerializedPropertyType.ObjectReference)
                        {
                            Object obj = null;

                            // Try Global ID first (works across sessions and for persistent assets/scene objects)
                            if (!string.IsNullOrEmpty(refData.GlobalObjectID))
                                if (GlobalObjectId.TryParse(refData.GlobalObjectID, out var gId))
                                    obj = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(gId);

                            // Fallback to local InstanceID if Global ID failed (for dynamic unsaved objects in same session)
                            if (obj == null && refData.InstanceID != 0)
                                obj = EditorUtility.InstanceIDToObject(refData.InstanceID);

                            prop.objectReferenceValue = obj;
                        }
                    }

                    so.ApplyModifiedProperties();
                    EditorUtility.SetDirty(comp);

                    if (!string.IsNullOrEmpty(data.SystemType) && comp.GetType().FullName != data.SystemType)
                        Debug.LogWarning(
                            $"[CopyComponentCustom] Pasted JSON from component type '{data.SystemType}' into '{comp.GetType().FullName}'. Some fields might not exactly match.");
                    else
                        Debug.Log($"[CopyComponentCustom] Successfully pasted JSON values to {comp.GetType().Name}.");
                }
                catch (Exception e)
                {
                    Debug.LogError(
                        $"[CopyComponentCustom] Failed to paste JSON to Component '{comp.GetType().Name}': {e.Message}");
                }
            }
        }

        /// <summary>
        ///     Checks whether there is valid JSON component data in the system copy buffer or the local clipboard backup.
        /// </summary>
        /// <returns>True if the buffer holds valid JSON data; otherwise, false.</returns>
        private static bool HasValidJsonInBuffer()
        {
            var buffer = EditorGUIUtility.systemCopyBuffer;
            if (!string.IsNullOrEmpty(buffer))
            {
                buffer = buffer.Trim();
                if (buffer.StartsWith("{") && buffer.EndsWith("}") && buffer.Contains("ComponentJson")) return true;
            }

            if (!string.IsNullOrEmpty(_clipboardBuffer)) return _clipboardBuffer.Contains("ComponentJson");

            return false;
        }

        #endregion
    }
}
#endif