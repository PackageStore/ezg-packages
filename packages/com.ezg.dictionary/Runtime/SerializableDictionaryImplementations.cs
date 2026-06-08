using System;
using UnityEngine;

namespace Easygoing.Packages.Dictionary
{
    // ---------------
    //  String => Int
    // ---------------

    /// <summary>
    ///     A serializable dictionary mapping strings to integers.
    /// </summary>
    [Serializable]
    public class StringIntDictionary : SerializableDictionary<string, int>
    {
    }

    // ---------------
    //  String => String
    // ---------------

    /// <summary>
    ///     A serializable dictionary mapping strings to strings.
    /// </summary>
    [Serializable]
    public class StringStringDictionary : SerializableDictionary<string, string>
    {
    }

    /// <summary>
    ///     A serializable dictionary mapping strings to Unity Sprite objects.
    /// </summary>
    [Serializable]
    public class StringSpriteDictionary : SerializableDictionary<string, Sprite>
    {
    }
}