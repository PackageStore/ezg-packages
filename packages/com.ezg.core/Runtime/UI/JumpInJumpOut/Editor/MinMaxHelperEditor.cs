#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.IO;
using System.Collections.Generic;
using System.ComponentModel;
using System.Reflection;
namespace Ezg.Core.UI
{
    public static class EditorExtensions
    {
        public static object GetValue(this SerializedProperty property)
        {
            System.Type parentType = property.serializedObject.targetObject.GetType();
            System.Reflection.FieldInfo fi = parentType.GetField(property.propertyPath);
            return fi.GetValue(property.serializedObject.targetObject);
        }

        public static System.Reflection.FieldInfo GetFieldViaPath(this System.Type type, string path)
        {
            System.Type parentType = type;
            System.Reflection.FieldInfo fi = type.GetField(path);
            string[] perDot = path.Split('.');
            foreach (string fieldName in perDot)
            {
                fi = parentType.GetField(fieldName);
                if (fi != null)
                    parentType = fi.FieldType;
                else
                    return null;
            }

            if (fi != null)
                return fi;
            else
                return null;
        }
    }

    [CustomPropertyDrawer(typeof(MinMaxHelper), true)]
    public class RangeFloatDrawer : PropertyDrawer
    {
        private const float space = 10f;

        GUIStyle GetBtnStyle()
        {
            var s = new GUIStyle();
            var b = s.border;
            b.left = 0;
            b.top = 0;
            b.right = 0;
            b.bottom = 0;
            return s;
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            return EditorGUI.GetPropertyHeight(property, label, false) + EditorGUIUtility.singleLineHeight;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);

            int indentLevel = EditorGUI.indentLevel;
            Rect labelRect = EditorGUI.PrefixLabel(position, GUIUtility.GetControlID(FocusType.Passive), label);
            EditorGUI.indentLevel = 0;
            float minMaxLabelSize = 30;

            float halfSize = (labelRect.width - space) / 2f;

            Rect minRect = new Rect(labelRect.x + minMaxLabelSize, position.y, halfSize - minMaxLabelSize,
                EditorGUIUtility.singleLineHeight);
            Rect maxRect = new Rect(labelRect.x + halfSize + minMaxLabelSize + space, position.y,
                halfSize - minMaxLabelSize, EditorGUIUtility.singleLineHeight);

            EditorGUI.PrefixLabel(
                new Rect(minRect.position.x - minMaxLabelSize, position.y, halfSize - minMaxLabelSize, position.height),
                new GUIContent("min"));
            SerializedProperty min = property.FindPropertyRelative("min");
            EditorGUI.PropertyField(minRect, min, GUIContent.none);
            EditorGUI.PrefixLabel(
                new Rect(maxRect.position.x - minMaxLabelSize, position.y, halfSize - minMaxLabelSize, position.height),
                new GUIContent("max"));
            SerializedProperty max = property.FindPropertyRelative("max");
            EditorGUI.PropertyField(maxRect, max, GUIContent.none);

            EditorGUI.EndProperty();
            EditorGUI.indentLevel = indentLevel;
        }
    }
}
#endif