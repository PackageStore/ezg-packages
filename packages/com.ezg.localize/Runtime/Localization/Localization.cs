using System;
using System.Globalization;
using UnityEngine;

namespace Ezg.Package.Localize.Localization
{
    /// <summary>
    ///     Singleton service providing localization management, loading, and text translation lookups.
    /// </summary>
    public sealed class Localization
    {
        #region Fields

        private static readonly object InstanceLock = new();
        private static Localization _instance;

        private readonly object cultureInfoLock = new();
        private CultureInfo cultureInfo;
        private EventHandler cultureInfoChanged;

        private readonly LocalizeProvider localizeProvider;

        #endregion

        #region Initialize

        /// <summary>
        ///     Private constructor for singletons without specified CultureInfo.
        /// </summary>
        private Localization() : this(null)
        {
        }

        /// <summary>
        ///     Private constructor for singletons initializing with a specified CultureInfo.
        /// </summary>
        /// <param name="cultureInfo">The starting CultureInfo.</param>
        private Localization(CultureInfo cultureInfo)
        {
            this.cultureInfo = cultureInfo ?? Locale.GetCultureInfo();

            localizeProvider = new LocalizeProvider(this);
        }

        #endregion

        #region Public Methods

        /// <summary>
        ///     Gets the current singleton instance of Localization.
        /// </summary>
        public static Localization Current
        {
            get
            {
                if (_instance != null)
                    lock (InstanceLock)
                    {
                        return _instance;
                    }

                lock (InstanceLock)
                {
                    if (_instance == null)
                        _instance = new Localization();
                    return _instance;
                }
            }
        }

        /// <summary>
        ///     Event triggered when the active culture/language changes.
        /// </summary>
        public event EventHandler CultureInfoChanged
        {
            add
            {
                lock (cultureInfoLock)
                {
                    cultureInfoChanged += value;
                }
            }
            remove
            {
                lock (cultureInfoLock)
                {
                    cultureInfoChanged -= value;
                }
            }
        }

        /// <summary>
        ///     Gets or sets the active CultureInfo, triggering a reload and change event when modified.
        /// </summary>
        public CultureInfo localCultureInfo
        {
            get => cultureInfo;
            set
            {
                if (value == null || (cultureInfo != null && cultureInfo.Equals(value)))
                    return;

                localizeProvider.ClearData();

                cultureInfo = value;

                OnCultureInfoChanged();
            }
        }

        /// <summary>
        ///     Gets the localized string value for the given category and key.
        /// </summary>
        /// <param name="category">The localization category/sheet.</param>
        /// <param name="key">The localization key.</param>
        /// <returns>The formatted localized string.</returns>
        public string Get(string category, string key)
        {
            return localizeProvider.Get(category, key).Replace("\\n", "\n");
        }

        #endregion

        #region Private Methods

        /// <summary>
        ///     Invokes the CultureInfoChanged event safely.
        /// </summary>
        private void RaiseCultureInfoChanged()
        {
            try
            {
                Debug.Log("language: " + cultureInfo.Name);
                cultureInfoChanged?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception)
            {
                // ignored
            }
        }

        /// <summary>
        ///     Triggered when the culture info changes, invoking event handlers.
        /// </summary>
        private void OnCultureInfoChanged()
        {
            RaiseCultureInfoChanged();
        }

        #endregion
    }
}