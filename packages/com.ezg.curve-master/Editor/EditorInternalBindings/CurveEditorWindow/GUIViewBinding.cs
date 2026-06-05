using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using System.Reflection;

namespace AnimationCurveManipulationTool {

    public class GUIViewBinding {

        public static System.Type GetBindedType() => ReflectionUtility.unityEditorAssembly.GetType("UnityEditor.GUIView");

        public static object current {
            get {
                var type = GetBindedType();
                return type.GetProperty("current", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
                    .GetValue(null);
            }
        }

    }

}