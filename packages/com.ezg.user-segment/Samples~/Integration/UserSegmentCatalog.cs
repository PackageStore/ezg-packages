using System;
using System.Collections.Generic;
using Ezg.Feature.Shared.Config;
using Ezg.UserSegment;
using UnityEngine;

namespace Ezg.Feature.System.UserSegment
{
    /// <summary>
    ///     Bảng map id trong config engine → tài nguyên / màn hình / product của game. Đây là phần manifest game khai
    ///     (rewards / custom events / custom state — §8.5); actions do SDK tự suy từ executor đã đăng ký.
    ///     Kèm Settings (game_id, env, URL, token) để module không cần field nào trong AppSecretsConfig của core.
    ///     Asset đặt tại Resources với tên <see cref="ASSET_NAME" />.
    /// </summary>
    [CreateAssetMenu(fileName = ASSET_NAME, menuName = "Ezg/Config/User Segment Catalog")]
    public class UserSegmentCatalog : ScriptableObject
    {
        public const string ASSET_NAME = "UserSegmentCatalog";
        public const string DEFAULT_GAME_ID = "unity_game_template";

        [Header("Settings")]
        [Tooltip("game_id khớp repo config (games/{game}/) và config.game_id — lệch là config bị từ chối (game_env_mismatch).")]
        [SerializeField] private string gameId = DEFAULT_GAME_ID;

        [Tooltip("env gửi kèm request và phải khớp config.env: prod / dev / staging.")]
        [SerializeField] private string env = "prod";

        [Tooltip("Endpoint GET /config của Worker (SDK tự thêm ?game=&env=). Để trống = không fetch; dev build / Editor dùng UserSegmentDevConfig trong Resources.")]
        [SerializeField] private string configBaseUrl;

        [Tooltip("Header X-Config-Token cho env ≠ prod. CHỈ dùng trong dev build; release bỏ qua.")]
        [SerializeField] private string configToken;

        [Header("Limits (§C.1.5 — range game khai, export vào manifest.limits)")]
        [Tooltip("GIVE_REWARD.amount tối đa. Default spec 1000; game có tiền tệ lớn thì nới. Đổi xong phải commit manifest mới vào repo config.")]
        [SerializeField] private int rewardAmountMax = SdkLimits.DEFAULT_REWARD_AMOUNT_MAX;

        [Tooltip("CHANGE_DIFFICULTY.delta trong ±giá trị này (≠ 0). Default spec 2.")]
        [SerializeField] private int difficultyDeltaMax = SdkLimits.DEFAULT_DIFFICULTY_DELTA_MAX;

        [Tooltip("Số custom event tối đa trong whitelist. Default spec 10.")]
        [SerializeField] private int maxCustomEvents = SdkLimits.DEFAULT_MAX_CUSTOM_EVENTS;

        public string GameId => gameId;
        public string Env => env;
        public string ConfigBaseUrl => configBaseUrl;
        public string ConfigToken => configToken;

        [Serializable]
        public class RewardEntry
        {
            [Tooltip("reward_id trong config engine (ident: a-z0-9_). Chỉ whitelist reward giá trị thấp — §8.5.")]
            public string id;

            [Tooltip("EnumBase.ResourceTypes (Money = tiền tệ).")]
            public int resType;

            [Tooltip("resId — với Money là (int)EnumBase.MoneyTypes.")]
            public int resId;

            [Tooltip("Số lượng cho amount = 1; engine nhân với params.amount.")]
            public long amountPerUnit = 1;
        }

        [Serializable]
        public class PopupEntry
        {
            [Tooltip("popup_id trong config engine.")] public string id;
            public GameEnums.Features feature;
        }

        [Serializable]
        public class OfferEntry
        {
            [Tooltip("offer_id trong config engine.")] public string id;
            [Tooltip("Màn shop / offer sẽ mở.")] public GameEnums.Features feature = GameEnums.Features.Shop;
            [Tooltip("productId của pack tương ứng (để nhận diện purchased qua last-touch).")] public string productId;
        }

        [Serializable]
        public class NotificationEntry
        {
            [Tooltip("template_id trong config engine.")] public string id;
            [Tooltip("Localize key (category Notification) cho title.")] public string titleKey;
            [Tooltip("Localize key (category Notification) cho body.")] public string bodyKey;
        }

        [Serializable]
        public class CustomStateEntry
        {
            public string key;
            public CustomType type;
        }

        [SerializeField] private RewardEntry[] rewards = Array.Empty<RewardEntry>();
        [SerializeField] private PopupEntry[] popups = Array.Empty<PopupEntry>();
        [SerializeField] private OfferEntry[] offers = Array.Empty<OfferEntry>();
        [SerializeField] private NotificationEntry[] notifications = Array.Empty<NotificationEntry>();

        [Tooltip("Whitelist CUSTOM_EVENT (≤ 10 tên) — §4.1.")] [SerializeField]
        private string[] customEvents = Array.Empty<string>();

        [SerializeField] private CustomStateEntry[] customState = Array.Empty<CustomStateEntry>();

        private static UserSegmentCatalog _instance;
        private static bool _missingLogged;

        /// <summary>Asset trong Resources; null nếu project chưa tạo (executor sẽ fail unknown_id).</summary>
        public static UserSegmentCatalog Current
        {
            get
            {
                if (_instance == null)
                {
                    _instance = Resources.Load<UserSegmentCatalog>(ASSET_NAME);
                    if (_instance == null && !_missingLogged)
                    {
                        _missingLogged = true;
                        Debug.LogWarning($"[UserSegment] Không thấy {ASSET_NAME} trong Resources — tạo qua Create > Ezg > Config > User Segment Catalog.");
                    }
                }

                return _instance;
            }
        }

        /// <summary>Limits cho SdkOptions; giá trị &lt; 1 được SDK quay về default.</summary>
        public SdkLimits Limits() => new SdkLimits
        {
            RewardAmountMax = rewardAmountMax,
            DifficultyDeltaMax = difficultyDeltaMax,
            MaxCustomEvents = maxCustomEvents
        };

        public string[] RewardIds()
        {
            var list = new List<string>();
            foreach (var r in rewards)
                if (!string.IsNullOrEmpty(r.id))
                    list.Add(r.id);
            return list.ToArray();
        }

        public string[] CustomEvents => customEvents ?? Array.Empty<string>();

        public Dictionary<string, CustomType> CustomStateMap()
        {
            var d = new Dictionary<string, CustomType>();
            foreach (var c in customState)
                if (!string.IsNullOrEmpty(c.key))
                    d[c.key] = c.type;
            return d;
        }

        public RewardEntry FindReward(string id) => Array.Find(rewards, r => r.id == id);
        public PopupEntry FindPopup(string id) => Array.Find(popups, r => r.id == id);
        public OfferEntry FindOffer(string id) => Array.Find(offers, r => r.id == id);
        public NotificationEntry FindNotification(string id) => Array.Find(notifications, r => r.id == id);
    }
}
