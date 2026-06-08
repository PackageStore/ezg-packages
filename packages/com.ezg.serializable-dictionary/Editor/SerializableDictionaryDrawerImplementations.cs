using UnityEditor;

namespace Easygoing.Packages.Dictionary
{
#if UNITY_EDITOR
    // ---------------
    //  String => Int
    // ---------------

    /// <summary>
    ///     Property drawer for StringIntDictionary.
    /// </summary>
    [CustomPropertyDrawer(typeof(StringIntDictionary))]
    public class StringIntDictionaryDrawer : SerializableDictionaryDrawer<string, int>
    {
        #region Public Methods

        /// <summary>
        ///     Gets the template for drawing the key-value pairs of the dictionary.
        /// </summary>
        /// <returns>A SerializableKeyValueTemplate for string and int.</returns>
        protected override SerializableKeyValueTemplate<string, int> GetTemplate()
        {
            return GetGenericTemplate<SerializableStringIntTemplate>();
        }

        #endregion
    }

    /// <summary>
    ///     Serialized key-value template implementation for string and int.
    /// </summary>
    internal class SerializableStringIntTemplate : SerializableKeyValueTemplate<string, int>
    {
    }

    // ---------------
    //  String => String
    // ---------------

    /// <summary>
    ///     Property drawer for StringStringDictionary.
    /// </summary>
    [CustomPropertyDrawer(typeof(StringStringDictionary))]
    public class StringStringDictionaryDrawer : SerializableDictionaryDrawer<string, string>
    {
        #region Public Methods

        /// <summary>
        ///     Gets the template for drawing the key-value pairs of the dictionary.
        /// </summary>
        /// <returns>A SerializableKeyValueTemplate for string and string.</returns>
        protected override SerializableKeyValueTemplate<string, string> GetTemplate()
        {
            return GetGenericTemplate<SerializableStringStringTemplate>();
        }

        #endregion
    }

    /// <summary>
    ///     Serialized key-value template implementation for string and string.
    /// </summary>
    internal class SerializableStringStringTemplate : SerializableKeyValueTemplate<string, string>
    {
    }
#endif
}