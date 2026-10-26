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

        /// <summary>
        ///     Resources path của font dùng cho ngôn ngữ không phải tiếng Thái. Game set một lần lúc boot
        ///     (vd <c>ThaiChecker.NormalFontResource = "Mali-Bold";</c>). Không tìm thấy → giữ font gốc của Text.
        /// </summary>
        public static string NormalFontResource = "PoetsenOne-Regular";

        /// <summary>Resources path của font tiếng Thái.</summary>
        public static string ThaiFontResource = "NotoSans/NotoSans_Thai";

        [SerializeField] [Tooltip("Tuỳ chọn: font thường riêng cho Text này (ưu tiên hơn NormalFontResource).")]
        private Font normalFontOverride;

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
            thaiFont = Resources.Load<Font>(ThaiFontResource);
            normalFont = normalFontOverride != null
                ? normalFontOverride
                : Resources.Load<Font>(NormalFontResource);
            // Project không có font NormalFontResource → giữ font đã gán sẵn trên Text thay vì để null.
            if (normalFont == null && _textComponent != null) normalFont = _textComponent.font;
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