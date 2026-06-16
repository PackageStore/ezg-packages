using System;
using System.IO;

namespace Ezg.Core.Utils
{
    public static class CoreUtils
    {
        #region Public Methods

        /// <summary>
        ///     Parses a string representation of an enum name or value to its enum equivalent.
        /// </summary>
        /// <typeparam name="T">The type of the enum.</typeparam>
        /// <param name="value">The string representation of the enum.</param>
        /// <returns>The parsed enum value.</returns>
        public static T ParseEnum<T>(string value)
        {
            return (T)Enum.Parse(typeof(T), value, true);
        }

#if UNITY_EDITOR
        /// <summary>
        ///     Creates a directory at the specified path if it does not already exist.
        /// </summary>
        /// <param name="path">The directory path to create.</param>
        public static void CreateDirectory(string path)
        {
            if (Directory.Exists(path))
                return;
            Directory.CreateDirectory(path);
        }
#endif

        #endregion
    }
}