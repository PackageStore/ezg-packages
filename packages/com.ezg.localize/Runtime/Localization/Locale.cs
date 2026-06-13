using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace Ezg.Package.Localize.Localization
{
    /// <summary>
    ///     Helper class to manage Supported Languages and resolve SystemLanguage to CultureInfo.
    /// </summary>
    public class Locale
    {
        #region Fields

        private static readonly CultureInfo DefaultCultureInfo = new("en");

        private static readonly Dictionary<SystemLanguage, CultureInfo> Languages =
            new()
            {
                { SystemLanguage.Afrikaans, new CultureInfo("af") },
                { SystemLanguage.Arabic, new CultureInfo("ar") },
                { SystemLanguage.Basque, new CultureInfo("eu") },
                { SystemLanguage.Belarusian, new CultureInfo("be") },
                { SystemLanguage.Bulgarian, new CultureInfo("bg") },
                { SystemLanguage.Catalan, new CultureInfo("ca") },
                { SystemLanguage.Chinese, new CultureInfo("zh-CN") },
                { SystemLanguage.ChineseSimplified, new CultureInfo("zh-CN") },
                { SystemLanguage.ChineseTraditional, new CultureInfo("zh-TW") },
                { SystemLanguage.Czech, new CultureInfo("cs") },
                { SystemLanguage.Danish, new CultureInfo("da") },
                { SystemLanguage.Dutch, new CultureInfo("nl") },
                { SystemLanguage.English, new CultureInfo("en") },
                { SystemLanguage.Estonian, new CultureInfo("et") },
                { SystemLanguage.Faroese, new CultureInfo("fo") },
                { SystemLanguage.Finnish, new CultureInfo("fi") },
                { SystemLanguage.French, new CultureInfo("fr") },
                { SystemLanguage.German, new CultureInfo("de") },
                { SystemLanguage.Greek, new CultureInfo("el") },
                { SystemLanguage.Hebrew, new CultureInfo("he") },
                { SystemLanguage.Hungarian, new CultureInfo("hu") },
                { SystemLanguage.Icelandic, new CultureInfo("is") },
                { SystemLanguage.Indonesian, new CultureInfo("id") },
                { SystemLanguage.Italian, new CultureInfo("it") },
                { SystemLanguage.Japanese, new CultureInfo("ja") },
                { SystemLanguage.Korean, new CultureInfo("ko") },
                { SystemLanguage.Latvian, new CultureInfo("lv") },
                { SystemLanguage.Lithuanian, new CultureInfo("lt") },
                { SystemLanguage.Norwegian, new CultureInfo("no") },
                { SystemLanguage.Polish, new CultureInfo("pl") },
                { SystemLanguage.Portuguese, new CultureInfo("pt") },
                { SystemLanguage.Romanian, new CultureInfo("ro") },
                { SystemLanguage.Russian, new CultureInfo("ru") },
                { SystemLanguage.SerboCroatian, new CultureInfo("hr") },
                { SystemLanguage.Slovak, new CultureInfo("sk") },
                { SystemLanguage.Slovenian, new CultureInfo("sl") },
                { SystemLanguage.Spanish, new CultureInfo("es") },
                { SystemLanguage.Swedish, new CultureInfo("sv") },
                { SystemLanguage.Thai, new CultureInfo("th") },
                { SystemLanguage.Turkish, new CultureInfo("tr") },
                { SystemLanguage.Ukrainian, new CultureInfo("uk") },
                { SystemLanguage.Vietnamese, new CultureInfo("vi") }
            };

        #endregion

        #region Public Methods

        /// <summary>
        ///     Gets the list of supported system languages.
        /// </summary>
        public static List<SystemLanguage> SupportLanguages => Languages.Keys.ToList();

        /// <summary>
        ///     Checks if the specified SystemLanguage is supported.
        /// </summary>
        /// <param name="systemLanguage">The SystemLanguage to check.</param>
        /// <returns>True if supported, otherwise false.</returns>
        public static bool IsSupportLanguage(SystemLanguage systemLanguage)
        {
            return Languages.ContainsKey(systemLanguage);
        }

        /// <summary>
        ///     Gets the default CultureInfo.
        /// </summary>
        /// <returns>The default CultureInfo instance.</returns>
        public static CultureInfo GetCultureInfo()
        {
            return DefaultCultureInfo;
        }

        /// <summary>
        ///     Gets the CultureInfo associated with a SystemLanguage, falling back to the default if not supported.
        /// </summary>
        /// <param name="language">The system language.</param>
        /// <returns>The matching CultureInfo.</returns>
        public static CultureInfo GetCultureInfoByLanguage(SystemLanguage language)
        {
            return GetCultureInfoByLanguage(language, DefaultCultureInfo);
        }

        /// <summary>
        ///     Gets the CultureInfo associated with a SystemLanguage, falling back to the specified defaultValue if not supported
        ///     or unknown.
        /// </summary>
        /// <param name="language">The system language.</param>
        /// <param name="defaultValue">The fallback CultureInfo to use if the language is unknown or unsupported.</param>
        /// <returns>The matching or fallback CultureInfo.</returns>
        public static CultureInfo GetCultureInfoByLanguage(SystemLanguage language, CultureInfo defaultValue)
        {
            if (language == SystemLanguage.Unknown)
            {
                Debug.LogWarning("The system language of this application is Unknown");

                return defaultValue;
            }

            if (Languages.TryGetValue(language, out var cultureInfo))
                return cultureInfo;

            Debug.LogWarning("The system language of this application cannot be found!");

            return defaultValue;
        }

        #endregion
    }
}