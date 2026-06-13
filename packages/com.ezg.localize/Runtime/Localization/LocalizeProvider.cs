using System;
using System.Collections.Generic;
using UnityEngine;

namespace Ezg.Package.Localize.Localization
{
    /// <summary>
    ///     Provider class that loads and caches LanguageData resources from the Resources folder dynamically.
    /// </summary>
    public class LocalizeProvider
    {
        #region Initialize

        /// <summary>
        ///     Initializes a new instance of the LocalizeProvider class.
        /// </summary>
        /// <param name="localization">The parent Localization manager.</param>
        public LocalizeProvider(Localization localization)
        {
            this.localization = localization;
            data = new Dictionary<string, LanguageData>();
        }

        #endregion

        #region Fields

        private readonly Localization localization;
        private readonly Dictionary<string, LanguageData> data;

        #endregion

        #region Public Methods

        /// <summary>
        ///     Clears all cached localization data.
        /// </summary>
        public void ClearData()
        {
            data.Clear();
        }

        /// <summary>
        ///     Retrieves the localized string for a specific category and key.
        /// </summary>
        /// <param name="category">The localization category.</param>
        /// <param name="key">The localization key.</param>
        /// <returns>The localized translation, or string.Empty if not found.</returns>
        public string Get(string category, string key)
        {
            if (!IsContain(category))
            {
                var categoryData = Load(category);
                data.Add(category, categoryData);
            }

            try
            {
                return data[category].data[key];
            }
            catch (Exception e)
            {
                Debug.Log($"Key is not exist: {category}-{key} {e}");
                return string.Empty;
            }
        }

        #endregion

        #region Private Methods

        /// <summary>
        ///     Loads localization asset data for the specified category/sheet dynamically from Resources.
        /// </summary>
        /// <param name="category">The localization category name.</param>
        /// <returns>The loaded LanguageData asset.</returns>
        /// <exception cref="Exception">Thrown if category is null/empty or already loaded.</exception>
        private LanguageData Load(string category)
        {
            if (string.IsNullOrEmpty(category)) throw new Exception("category is empty");

            if (IsContain(category)) throw new Exception("category is exist: " + category);

            var name = localization.localCultureInfo.Name.ToLower().Replace("-", string.Empty);
            var path = $"LocalizationData/{name}/{category}";

            return Resources.Load<LanguageData>(path);
        }

        /// <summary>
        ///     Checks if the localization provider has already loaded and cached the specified category.
        /// </summary>
        /// <param name="category">The category to check.</param>
        /// <returns>True if cached, otherwise false.</returns>
        private bool IsContain(string category)
        {
            return data.ContainsKey(category);
        }

        #endregion
    }
}