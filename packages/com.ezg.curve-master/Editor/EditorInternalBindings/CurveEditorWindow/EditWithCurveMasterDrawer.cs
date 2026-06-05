using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace AnimationCurveManipulationTool {

    [CustomPropertyDrawer(typeof(EditWithCurveMasterAttribute), true)]
    public class EditWithCurveMasterDrawer : PropertyDrawer {

        private static int s_CurveID;

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label) {
            EditorGUI.BeginProperty(position, label, property);

            var pos = position;
            int uniquePropertyHash = property.serializedObject.targetObject.GetInstanceID() + property.propertyPath.GetHashCode();
            int controlId = GUIUtility.GetControlID(uniquePropertyHash, FocusType.Keyboard, position);
            position = EditorGUI.PrefixLabel(position, controlId, label);

            Event e = Event.current;
            if (e.type == EventType.MouseDown && e.button == 0 && position.Contains(e.mousePosition)) {
                s_CurveID = controlId;
                GUIUtility.keyboardControl = controlId;
                var curveWindow = CurveEditorWindowBinding.Get();
                CurveEditorWindowBinding.curve = property.animationCurveValue;
                CurveEditorWindowBinding.color = Color.green;
                curveWindow.Show(new CurveEditorSettingsBinding());
                e.Use();
                GUIUtility.ExitGUI();
            }
            else if (e.type == EventType.ExecuteCommand) {
                if (s_CurveID != controlId || !(GUIViewBinding.current == CurveEditorWindowBinding.Get().delegateView)) {
                    // Window is not referring to this curve
                }
                else {
                    string commandName = e.commandName;
                    string text = commandName;
                    if (text == "CurveChanged") {
                        GUI.changed = true;
                        //AnimationCurvePreviewCache.ClearCache();
                        HandleUtility.Repaint();
                        if (property != null) {
                            property.animationCurveValue = CurveEditorWindowBinding.curve;
                            if (property.hasMultipleDifferentValues) {
                                Debug.LogError("AnimationCurve SerializedProperty hasMultipleDifferentValues is true after writing.");
                            }
                            CurveEditorWindowBinding.curve = property.animationCurveValue; // Refresh curve in editor if built-in preset is clicked.
                        }
                    }
                }

            }
            else if (e.type == EventType.ValidateCommand) {
                if (e.commandName == "UndoRedoPerformed") {
                    CurveEditorWindowBinding.curve = property.animationCurveValue;
                }
            }
            else {
                EditorGUI.PropertyField(pos, property);
            }

            EditorGUI.EndProperty();
        }

    }

}