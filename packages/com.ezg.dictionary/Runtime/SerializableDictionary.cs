using System;
using System.Collections.Generic;
using UnityEngine;

namespace Easygoing.Packages.Dictionary
{
    /// <summary>
    ///     A generic dictionary class that supports Unity serialization by implementing ISerializationCallbackReceiver.
    /// </summary>
    /// <typeparam name="K">The key type.</typeparam>
    /// <typeparam name="V">The value type.</typeparam>
    [Serializable]
    public class SerializableDictionary<K, V> : Dictionary<K, V>, ISerializationCallbackReceiver
    {
        #region Fields

        [SerializeField] private List<K> keys = new();

        [SerializeField] private List<V> values = new();

        #endregion

        #region Private Methods

        /// <summary>
        ///     Clears and repopulates the dictionary from the serialized key/value lists after deserialization.
        /// </summary>
        void ISerializationCallbackReceiver.OnAfterDeserialize()
        {
            Clear();
            for (var i = 0; i < keys.Count && i < values.Count; i++) this[keys[i]] = values[i];
        }

        /// <summary>
        ///     Populates the serialized key/value lists from the dictionary before serialization.
        /// </summary>
        void ISerializationCallbackReceiver.OnBeforeSerialize()
        {
            keys.Clear();
            values.Clear();

            foreach (var item in this)
            {
                keys.Add(item.Key);
                values.Add(item.Value);
            }
        }

        #endregion
    }
}