using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using System.Reflection;

namespace AnimationCurveManipulationTool {

    public class ParticleEffectUIBinding {

        private object objectReference;

        public ParticleEffectUIBinding(object _objectReference) {
            objectReference = _objectReference;
        }

        private ParticleSystemCurveEditorBinding m_ParticleSystemCurveEditor;
        public ParticleSystemCurveEditorBinding ParticleSystemCurveEditor {
            get {
                if (m_ParticleSystemCurveEditor == null) {
                    var field = objectReference.GetType().GetField("m_ParticleSystemCurveEditor", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                    var obj = field.GetValue(objectReference);
                    if (obj == null) return null;
                    m_ParticleSystemCurveEditor = new ParticleSystemCurveEditorBinding(obj);
                }
                return m_ParticleSystemCurveEditor;
            }
        }

    }

}