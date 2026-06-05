using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using System.Reflection;

namespace AnimationCurveManipulationTool {

    public class ParticleSystemInspectorBinding {

        public static ParticleSystemInspectorBinding Get() {
            var particleSystemInspector = GetCurrentEditor();
            if (particleSystemInspector == null) return null;
            return new ParticleSystemInspectorBinding(particleSystemInspector);
        }

        private static Editor GetCurrentEditor() {
            var editorType = ReflectionUtility.unityEditorAssembly.GetType("UnityEditor.ParticleSystemInspector");
            var activeEditors = ActiveEditorTracker.sharedTracker.activeEditors;
            foreach (var activeEditor in activeEditors) {
                if (activeEditor.GetType() == editorType) {
                    return activeEditor;
                }
            }
            return null;
        }

        private Editor objectReference;

        public ParticleSystemInspectorBinding(Editor _objectReference) {
            objectReference = _objectReference;
        }

        public Editor GetObjectReference() => objectReference;

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

    }

}