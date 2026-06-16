using Ezg.Core.Extensions;
using UnityEngine;
using UnityEngine.UI;

namespace Ezg.Core.UI
{
    public class UI_ButtonIconExtensions : MonoBehaviour
    {
        #region Initialize

        /// <summary>
        ///     Caches the Button component at initialization.
        /// </summary>
        private void Awake()
        {
            _thisButton = GetComponent<Button>();
        }

        #endregion

        #region Public Methods

        /// <summary>
        ///     Initializes the button display with the provided text, hides the icon, and sets the button interactable.
        /// </summary>
        /// <param name="text">The text value to display on the button.</param>
        public void InitData(string text)
        {
            Text.text = text;
            Text.color = Color.white;
            Icon.gameObject.SetActive(false);
            _thisButton.interactable = true;
        }

        #endregion

        #region Fields

        public Image Icon;
        public Text Text;
        private Button _thisButton;
        public RebuildUILayoutHelper RebuildLayout;

        #endregion

        //public void InitData(EnumBase.MoneyTypes moneyType, long moneyValue, bool isValid = false)
        //{
        //    if (moneyType is EnumBase.MoneyTypes.None or EnumBase.MoneyTypes.Cash)
        //    {
        //        Icon.gameObject.SetActive(false);
        //    }
        //    else
        //    {
        //        Icon.sprite = GameSystems.Instance.GetImageByMoneyType(moneyType);
        //        Icon.gameObject.SetActive(true);
        //    }

        //    Text.text = moneyValue.MoneyConvert();
        //    if (isValid)
        //        Text.text.SetColor(MoneyManager.ValidMoney(moneyType, moneyValue) ? Color.white : Color.red);
        //}

        //public void InitData(EnumBase.MoneyTypes moneyType, string moneyValue)
        //{
        //    if (moneyType is EnumBase.MoneyTypes.None or EnumBase.MoneyTypes.Cash)
        //    {
        //        Icon.gameObject.SetActive(false);
        //    }
        //    else
        //    {
        //        Icon.sprite = GameSystems.Instance.GetImageByMoneyType(moneyType);
        //        Icon.gameObject.SetActive(true);
        //    }

        //    Text.text = moneyValue;
        //}

        //public void InitData(EnumBase.ItemTypes itemType, int itemId, long quantity, bool isShowCurrentQuantity = false)
        //{
        //    Icon.sprite = GameSystems.Instance.GetImageIconItem(itemType, itemId);
        //    Icon.gameObject.SetActive(true);

        //    if (isShowCurrentQuantity)
        //    {
        //        Text.text = InventoryManager.GetItemQuantity(itemId) + "/" + quantity.MoneyConvert();
        //    }
        //    else
        //    {
        //        Text.text = quantity.MoneyConvert();
        //    }
        //}
    }
}