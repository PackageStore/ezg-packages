using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using System.Reflection;

namespace AnimationCurveManipulationTool {

    public class ParticleSystemCurveEditorBinding {

        private object objectReference;

        public ParticleSystemCurveEditorBinding(object _objectReference) {
            objectReference = _objectReference;
        }

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

    }

}