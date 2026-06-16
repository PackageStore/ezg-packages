#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.UI;
using UnityEngine;

namespace Ezg.Core.UI
{
    [CustomEditor(typeof(UI_ButtonCustom))]
    public class UI_ButtonCustomEditor : ButtonEditor
    {
        #region Public Methods

        /// <summary>
        ///     Custom inspector GUI for UI_ButtonCustom, displaying standard Button parameters along with a field for
        ///     DisableObject.
        /// </summary>
        public override void OnInspectorGUI()
        {
            var component = (UI_ButtonCustom)target;

            base.OnInspectorGUI();

            component.DisableObject =
                (GameObject)EditorGUILayout.ObjectField("Disable Object", component.DisableObject, typeof(GameObject),
                    true);
        }

        #endregion
    }
}
#endif