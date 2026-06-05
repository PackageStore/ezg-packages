using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using System.Reflection;

namespace AnimationCurveManipulationTool {

    public class CurveEditorWindowBinding {

        private static System.Type m_bindedType;
        private static System.Type bindedType {
            get {
                if (m_bindedType == null)
                    m_bindedType = ReflectionUtility.unityEditorAssembly.GetType("UnityEditor.CurveEditorWindow");
                return m_bindedType;
            }
        }

        public static CurveEditorWindowBinding Get() {
            var windowType = bindedType;
            var instance = windowType.GetProperty("instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).GetValue(null) as EditorWindow;
            if (instance == null) return null;
            return new CurveEditorWindowBinding(instance);
        }
        
        private EditorWindow objectReference;

        public CurveEditorWindowBinding(EditorWindow _objectReference) {
            objectReference = _objectReference;
        }

        public EditorWindow GetObjectReference() => objectReference;


        private CurveEditorBinding m_CurveEditor;
        public CurveEditorBinding CurveEditor {
            get {
                if (m_CurveEditor == null) {
                    var field = objectReference.GetType().GetField("m_CurveEditor", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                    var obj = field.GetValue(objectReference);
                    if (obj == null) return null;
                    m_CurveEditor = new CurveEditorBinding(obj);
                }
                return m_CurveEditor;
            }
        }

        public static bool visible {
            get {
                return (bool)bindedType
                    .GetProperty("visible", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
                    .GetValue(null);
            }
        }

        public static AnimationCurve curve {
            get {
                return bindedType
                    .GetProperty("curve", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
                    .GetValue(null) as AnimationCurve;
            }
            set {
                bindedType
                    .GetProperty("curve", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
                    .SetValue(null, value);
            }
        }

        public static Color color {
            get {
                return (Color)bindedType
                    .GetProperty("color", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
                    .GetValue(null);
            }
            set {
                bindedType
                    .GetProperty("color", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
                    .SetValue(null, value);
            }
        }

        public System.Action<AnimationCurve> onCurveChanged {
            set {
                bindedType.GetField("m_OnCurveChanged", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .SetValue(objectReference, value);
            }
        }

        public object delegateView {
            get {
                var delegateViewField = bindedType.GetField("m_DelegateView", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (delegateViewField == null) delegateViewField = bindedType.GetField("delegateView", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                return delegateViewField.GetValue(objectReference);
            }
        }

        public void Show(CurveEditorSettingsBinding settings) {
            object viewToUpdate = GUIViewBinding.current;
            //objectReference.GetType()
            //    .GetMethod("Show", new System.Type[] { GUIViewBinding.GetBindedType(), CurveEditorSettingsBinding.GetBindedType() })
            //    .Invoke(objectReference, new object[] { viewToUpdate, settings.GetObjectReference() });

            var delegateViewField = bindedType.GetField("m_DelegateView", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (delegateViewField == null) delegateViewField = bindedType.GetField("delegateView", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (delegateViewField == null) {
                Debug.LogError("CurveEditorWindow: Can't delegateView field.");
                return;
            }
            delegateViewField.SetValue(objectReference, viewToUpdate);
            bindedType.GetField("m_OnCurveChanged", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).SetValue(objectReference, null);

            bindedType.GetMethod("Init", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(objectReference, new object[] { settings.GetObjectReference() });
            objectReference.Show();

            //bindedType.GetMethod("Show", new System.Type[] { GUIViewBinding.GetBindedType(), CurveEditorSettingsBinding.GetBindedType() }).Invoke(objectReference, new object[] { viewToUpdate, settings.GetObjectReference() });
        }

    }

}