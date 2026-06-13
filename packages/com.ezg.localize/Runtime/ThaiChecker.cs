using TigerForge;
using UnityEngine;
using UnityEngine.UI;
using LocalizationClass = Ezg.Package.Localize.Localization.Localization;

namespace Ezg.Package.Localize
{
    [RequireComponent(typeof(Text))]
    public class ThaiChecker : MonoBehaviour
    {
        #region Fields

        private Font normalFont;
        private Font thaiFont;
        private Text _textComponent;

        #endregion

        #region Initialize

        /// <summary>
        ///     Awake is called when the script instance is being loaded.
        ///     Initializes font references and registers language change listener.
        /// </summary>
        private void Awake()
        {
            if (_textComponent == null) _textComponent = transform.GetComponent<Text>();
            thaiFont = Resources.Load<Font>("NotoSans/NotoSans_Thai");
            normalFont = Resources.Load<Font>("PoetsenOne-Regular");
            EventManager.StartListening("SettingLanguageChanged", OnEnable);
        }

        /// <summary>
        ///     OnEnable is called when the object becomes enabled and active.
        ///     Adjusts the text font depending on whether the current language is Thai.
        /// </summary>
        private void OnEnable()
        {
            var isThai = LocalizationClass.Current.localCultureInfo.Name == "th";
            _textComponent.font = isThai ? thaiFont : normalFont;
            // if (isThai)
            // {
            //     _textComponent.font = thaiFont;
            // }
        }

        #endregion
    }
}