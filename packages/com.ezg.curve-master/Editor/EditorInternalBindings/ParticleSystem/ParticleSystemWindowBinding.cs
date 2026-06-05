using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using System.Reflection;

namespace AnimationCurveManipulationTool {

    public class ParticleSystemWindowBinding {

        public static ParticleSystemWindowBinding Get() {
            var windowType = ReflectionUtility.unityEditorAssembly.GetType("UnityEditor.ParticleSystemWindow");
            var instance = windowType.GetMethod("GetInstance", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).Invoke(null, new object[0]) as EditorWindow;
            if (instance == null) return null;
            return new ParticleSystemWindowBinding(instance);
        }

        private EditorWindow objectReference;

        public ParticleSystemWindowBinding(EditorWindow _objectReference) {
            objectReference = _objectReference;
        }

        public EditorWindow GetObjectReference() => objectReference;

        private ParticleEffectUIBinding m_ParticleEffectUI;
        public ParticleEffectUIBinding ParticleEffectUI {
            get {
                if (m_ParticleEffectUI == null) {
                    var field = objectReference.GetType().GetField("m_ParticleEffectUI", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                    var obj = field.GetValue(objectReference);
                    if (obj == null) return null;
                    m_ParticleEffectUI = new ParticleEffectUIBinding(obj);
                }
                return m_ParticleEffectUI;
            }
        }

        public bool IsVisible() {
            return (bool)objectReference.GetType().GetMethod("IsVisible", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Invoke(objectReference, new object[0]);
        }

    }

}