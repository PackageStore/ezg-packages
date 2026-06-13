using System;
using Ezg.Package.CsvReader;
using Easygoing.Packages.Dictionary;
using CsvReaderLib = Ezg.Package.CsvReader.CsvReader;
using UnityEngine;

namespace Ezg.Package.Localize.Localization
{
    /// <summary>
    ///     ScriptableObject holding a mapping of localized string keys to values, implementing ICsvCustomData for CSV imports.
    /// </summary>
    public class LanguageData : ScriptableObject, ICsvCustomData
    {
        #region Fields

        public StringStringDictionary data;

        #endregion

        #region Public Methods

        /// <summary>
        ///     Deserializes raw CSV data using a tilde ('~') separator and loads it into the data dictionary.
        /// </summary>
        /// <param name="data">The raw CSV content.</param>
        public void ImportData(string data)
        {
            try
            {
                var rows = CsvReaderLib.Deserialize<RowData>(data, null, '~');

                this.data.Clear();
                foreach (var row in rows) this.data.Add(row.key, row.value);
            }
            catch (Exception e)
            {
                Debug.LogError($"ImportData failed: {name}");
            }
        }

        #endregion

        #region Nested Types

        /// <summary>
        ///     Representation of a single key-value row from a localization CSV.
        /// </summary>
        public class RowData
        {
            #region Fields

            public string key;
            public string value;

            #endregion
        }

        #endregion
    }
}