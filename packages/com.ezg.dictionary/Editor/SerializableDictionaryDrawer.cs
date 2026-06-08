using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Easygoing.Packages.Dictionary
{
#if UNITY_EDITOR
    /// <summary>
    ///     A helper ScriptableObject template representing a key-value pair for serialization drawing.
    /// </summary>
    /// <typeparam name="K">The key type.</typeparam>
    /// <typeparam name="V">The value type.</typeparam>
    public abstract class SerializableKeyValueTemplate<K, V> : ScriptableObject
    {
        #region Fields

        public K key;
        public V value;

        #endregion
    }

    /// <summary>
    ///     A custom property drawer for serializable dictionaries.
    /// </summary>
    /// <typeparam name="K">The key type.</typeparam>
    /// <typeparam name="V">The value type.</typeparam>
    public abstract class SerializableDictionaryDrawer<K, V> : PropertyDrawer
    {
        #region Fields

        private readonly Dictionary<int, SerializedProperty> templateKeyProp = new();
        private readonly Dictionary<int, SerializedProperty> templateValueProp = new();
        private readonly Dictionary<int, SerializedProperty> keysProps = new();
        private readonly Dictionary<int, SerializedProperty> valuesProps = new();

        private readonly Dictionary<int, Dictionary<int, SerializedProperty>> indexedPropertyDicts = new();

        #endregion

        #region Public Methods

        /// <summary>
        ///     Gets the key-value template ScriptableObject.
        /// </summary>
        /// <returns>An instance of SerializableKeyValueTemplate.</returns>
        protected abstract SerializableKeyValueTemplate<K, V> GetTemplate();

        /// <summary>
        ///     Creates an instance of a generic key-value template.
        /// </summary>
        /// <typeparam name="T">The specific template type.</typeparam>
        /// <returns>An instance of the template.</returns>
        protected T GetGenericTemplate<T>() where T : SerializableKeyValueTemplate<K, V>
        {
            return ScriptableObject.CreateInstance<T>();
        }

        /// <summary>
        ///     Draws the property in the GUI.
        /// </summary>
        /// <param name="position">Rectangle on the screen to use for the property GUI.</param>
        /// <param name="property">The SerializedProperty to make the custom GUI for.</param>
        /// <param name="label">The label of this property.</param>
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);

            var firstLine = position;
            firstLine.height = EditorGUIUtility.singleLineHeight;
            EditorGUI.PropertyField(firstLine, property);

            if (property.isExpanded)
            {
                var secondLine = firstLine;
                secondLine.y += EditorGUIUtility.singleLineHeight;

                EditorGUIUtility.labelWidth = 50f;

                secondLine.x += 15f; // indentation
                secondLine.width -= 15f;

                var secondLine_key = secondLine;

                var buttonWidth = 60f;
                secondLine_key.width -= buttonWidth; // assign button
                secondLine_key.width /= 2f;

                var secondLineValue = secondLine_key;
                secondLineValue.x += secondLineValue.width;
                if (GetTemplateValueProp(property).hasVisibleChildren)
                {
                    // if the value has children, indent to make room for fold arrow
                    secondLineValue.x += 15;
                    secondLineValue.width -= 15;
                }

                var secondLineButton = secondLineValue;
                secondLineButton.x += secondLineValue.width;
                secondLineButton.width = buttonWidth;

                var kHeight = EditorGUI.GetPropertyHeight(GetTemplateKeyProp(property));
                var vHeight = EditorGUI.GetPropertyHeight(GetTemplateValueProp(property));
                var extraHeight = Mathf.Max(kHeight, vHeight);

                secondLine_key.height = kHeight;
                secondLineValue.height = vHeight;

                EditorGUI.PropertyField(secondLine_key, GetTemplateKeyProp(property), true);
                EditorGUI.PropertyField(secondLineValue, GetTemplateValueProp(property), true);

                var keysProp = GetKeysProp(property);
                var valuesProp = GetValuesProp(property);

                var numLines = keysProp.arraySize;

                if (GUI.Button(secondLineButton, "Assign"))
                {
                    var assignment = false;
                    for (var i = 0; i < numLines; i++)
                        // Try to replace existing value
                        if (SerializedPropertyExtension.EqualBasics(GetIndexedItemProp(keysProp, i),
                                GetTemplateKeyProp(property)))
                        {
                            SerializedPropertyExtension.CopyBasics(GetTemplateValueProp(property),
                                GetIndexedItemProp(valuesProp, i));
                            assignment = true;
                            break;
                        }

                    if (!assignment)
                    {
                        // Create a new value
                        keysProp.arraySize += 1;
                        valuesProp.arraySize += 1;
                        SerializedPropertyExtension.CopyBasics(GetTemplateKeyProp(property),
                            GetIndexedItemProp(keysProp, numLines));
                        SerializedPropertyExtension.CopyBasics(GetTemplateValueProp(property),
                            GetIndexedItemProp(valuesProp, numLines));
                    }
                }

                for (var i = 0; i < numLines; i++)
                {
                    secondLine_key.y += extraHeight;
                    secondLineValue.y += extraHeight;
                    secondLineButton.y += extraHeight;

                    kHeight = EditorGUI.GetPropertyHeight(GetIndexedItemProp(keysProp, i));
                    vHeight = EditorGUI.GetPropertyHeight(GetIndexedItemProp(valuesProp, i));
                    extraHeight = Mathf.Max(kHeight, vHeight);

                    secondLine_key.height = kHeight;
                    secondLineValue.height = vHeight;

                    EditorGUI.PropertyField(secondLine_key, GetIndexedItemProp(keysProp, i), true);
                    EditorGUI.PropertyField(secondLineValue, GetIndexedItemProp(valuesProp, i), true);

                    if (GUI.Button(secondLineButton, "Remove"))
                    {
                        keysProp.DeleteArrayElementAtIndex(i);
                        valuesProp.DeleteArrayElementAtIndex(i);
                    }
                }
            }

            EditorGUI.EndProperty();
        }

        /// <summary>
        ///     Calculates the vertical spacing required by this property drawer.
        /// </summary>
        /// <param name="property">The SerializedProperty to draw.</param>
        /// <param name="label">The label of this property.</param>
        /// <returns>The total height in pixels.</returns>
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            if (!property.isExpanded)
                return EditorGUIUtility.singleLineHeight;

            var total = EditorGUIUtility.singleLineHeight;

            var kHeight = EditorGUI.GetPropertyHeight(GetTemplateKeyProp(property));
            var vHeight = EditorGUI.GetPropertyHeight(GetTemplateValueProp(property));
            total += Mathf.Max(kHeight, vHeight);

            var keysProp = GetKeysProp(property);
            var valuesProp = GetValuesProp(property);
            var numLines = keysProp.arraySize;
            for (var i = 0; i < numLines; i++)
            {
                kHeight = EditorGUI.GetPropertyHeight(GetIndexedItemProp(keysProp, i));
                vHeight = EditorGUI.GetPropertyHeight(GetIndexedItemProp(valuesProp, i));
                total += Mathf.Max(kHeight, vHeight);
            }

            return total;
        }

        #endregion

        #region Private Methods

        /// <summary>
        ///     Retrieves the serialized key property from the template.
        /// </summary>
        /// <param name="mainProp">The main property being drawn.</param>
        /// <returns>The template key SerializedProperty.</returns>
        private SerializedProperty GetTemplateKeyProp(SerializedProperty mainProp)
        {
            return GetTemplateProp(templateKeyProp, mainProp);
        }

        /// <summary>
        ///     Retrieves the serialized value property from the template.
        /// </summary>
        /// <param name="mainProp">The main property being drawn.</param>
        /// <returns>The template value SerializedProperty.</returns>
        private SerializedProperty GetTemplateValueProp(SerializedProperty mainProp)
        {
            return GetTemplateProp(templateValueProp, mainProp);
        }

        /// <summary>
        ///     Retrieves or caches the template property.
        /// </summary>
        private SerializedProperty GetTemplateProp(Dictionary<int, SerializedProperty> source,
            SerializedProperty mainProp)
        {
            SerializedProperty p;
            if (!source.TryGetValue(mainProp.GetObjectCode(), out p))
            {
                var templateObject = GetTemplate();
                var templateSerializedObject = new SerializedObject(templateObject);
                var kProp = templateSerializedObject.FindProperty("key");
                var vProp = templateSerializedObject.FindProperty("value");
                templateKeyProp[mainProp.GetObjectCode()] = kProp;
                templateValueProp[mainProp.GetObjectCode()] = vProp;
                p = source == templateKeyProp ? kProp : vProp;
            }

            return p;
        }

        /// <summary>
        ///     Gets the cached keys property.
        /// </summary>
        private SerializedProperty GetKeysProp(SerializedProperty mainProp)
        {
            return GetCachedProp(mainProp, "keys", keysProps);
        }

        /// <summary>
        ///     Gets the cached values property.
        /// </summary>
        private SerializedProperty GetValuesProp(SerializedProperty mainProp)
        {
            return GetCachedProp(mainProp, "values", valuesProps);
        }

        /// <summary>
        ///     Finds and caches the relative property.
        /// </summary>
        private SerializedProperty GetCachedProp(SerializedProperty mainProp, string relativePropertyName,
            Dictionary<int, SerializedProperty> source)
        {
            SerializedProperty p;
            var objectCode = mainProp.GetObjectCode();
            if (!source.TryGetValue(objectCode, out p))
                source[objectCode] = p = mainProp.FindPropertyRelative(relativePropertyName);
            return p;
        }

        /// <summary>
        ///     Gets the cached property for an array element at a specific index.
        /// </summary>
        private SerializedProperty GetIndexedItemProp(SerializedProperty arrayProp, int index)
        {
            Dictionary<int, SerializedProperty> d;
            if (!indexedPropertyDicts.TryGetValue(arrayProp.GetObjectCode(), out d))
                indexedPropertyDicts[arrayProp.GetObjectCode()] = d = new Dictionary<int, SerializedProperty>();
            SerializedProperty result;
            if (!d.TryGetValue(index, out result))
                d[index] = result = arrayProp.FindPropertyRelative(string.Format("Array.data[{0}]", index));
            return result;
        }

        #endregion
    }
#endif
}