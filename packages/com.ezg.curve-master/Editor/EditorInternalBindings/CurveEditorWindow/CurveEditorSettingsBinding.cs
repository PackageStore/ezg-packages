using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using System.Reflection;

namespace AnimationCurveManipulationTool {

    public class CurveEditorSettingsBinding {

        public static System.Type GetBindedType() => ReflectionUtility.unityEditorAssembly.GetType("UnityEditor.CurveEditorSettings");


        private object objectReference;

        public CurveEditorSettingsBinding() {
            var type = GetBindedType();
            objectReference = System.Activator.CreateInstance(type);
        }

        public CurveEditorSettingsBinding(object _objectReference) {
            objectReference = _objectReference;
        }

        public object GetObjectReference() => objectReference;

    }

}