// EZG Feature Hub — nạp JSON Lottie đã bundle (Lottie/ezg_fh_*.json) theo key.
// .json import thành TextAsset; tìm bằng AssetDatabase nên hoạt động kể cả khi đóng thành package.
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Ezg.FeatureHub.Editor
{
    public static class LottieLibrary
    {
        #region Constants

        public const string LOADING = "ezg_fh_loading";
        public const string CHECK = "ezg_fh_check";
        public const string BRAND = "ezg_fh_brand";
        public const string CONFETTI = "ezg_fh_confetti";

        // Bộ icon (useAnimations, MIT — đã recolor cho nền tối).
        public const string DOWNLOAD = "ezg_fh_dl";
        public const string CHECK2 = "ezg_fh_check2";
        public const string UPDATE = "ezg_fh_update";
        public const string ALERT = "ezg_fh_alert";
        public const string ARCHIVE = "ezg_fh_archive";
        public const string SETTINGS = "ezg_fh_settings";
        public const string FOLDER = "ezg_fh_folder";
        public const string GITHUB = "ezg_fh_github";
        public const string STAR = "ezg_fh_star";
        public const string HEART = "ezg_fh_heart";
        public const string BELL = "ezg_fh_bell";
        public const string ACTIVITY = "ezg_fh_activity";

        #endregion

        #region Fields

        private static readonly Dictionary<string, string> _cache = new Dictionary<string, string>();

        #endregion

        #region Public Methods

        /// <summary>Trả về nội dung JSON của một lottie theo tên file (không đuôi). Null nếu không thấy.</summary>
        public static string GetJson(string key)
        {
            if (_cache.TryGetValue(key, out string cached))
                return cached;

            string json = LoadFromAssets(key);
            _cache[key] = json; // cache cả null để khỏi tìm lại
            return json;
        }

        #endregion

        #region Private Methods

        private static string LoadFromAssets(string key)
        {
            foreach (string guid in AssetDatabase.FindAssets($"{key} t:TextAsset"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileNameWithoutExtension(path) != key)
                    continue;

                var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
                if (asset != null)
                    return asset.text;
            }

            Debug.LogWarning($"[FeatureHub] Không tìm thấy lottie '{key}'.");
            return null;
        }

        #endregion
    }
}
