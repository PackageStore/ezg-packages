using System;
using UnityEngine;

namespace Ezg.Package.Localize
{
    [Serializable]
    public class DefineCollection : ScriptableObject
    {
        #region Fields

        public DefineData[] dataGroups;

        #endregion

        #region Public Methods

        /// <summary>
        ///     Gets the define data for a collection based on the CSV reimport name.
        /// </summary>
        /// <param name="csvReimport">The CSV reimport file name.</param>
        /// <returns>The matching DefineData, or null if not found.</returns>
        public DefineData GetDefineCollectionData(string csvReimport)
        {
            for (var i = 0; i < dataGroups.Length; i++)
                if (dataGroups[i].IsInDefineCollection(csvReimport))
                    return dataGroups[i];
            Debug.LogWarning($"Warning! Not exist DefineDataCollection, check out csvReimport: {csvReimport}");
            return null;
        }

        #endregion
    }

    [Serializable]
    public class DefineData
    {
        #region Public Methods

        /// <summary>
        ///     Checks if the given CSV reimport matches this define data.
        /// </summary>
        /// <param name="csvReimport">The CSV reimport file name to check.</param>
        /// <returns>True if it is part of this collection, otherwise false.</returns>
        public bool IsInDefineCollection(string csvReimport)
        {
            var data = assetPath.Split('/');
            if (data.Length == 0) return false;

            var csv = data[data.Length - 1] + ".csv";

            if (csv.Equals(csvReimport)) return true;

            return false;
        }

        #endregion

        #region Fields

        public int id;
        public string classCollection;
        public string classData;
        public string assetPath;

        #endregion
    }
}