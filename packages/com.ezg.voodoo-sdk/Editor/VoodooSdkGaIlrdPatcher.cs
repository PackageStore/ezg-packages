#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Ezg.VoodooSdk.Editor
{
    /// <summary>
    /// Gỡ các callback AppLovin đã bị xoá khỏi <c>GAMaxIntegration.cs</c> của GameAnalytics.
    ///
    /// GA đăng ký <c>MaxSdkCallbacks.CrossPromo</c> và <c>.RewardedInterstitial</c>; AppLovin MAX
    /// v13 đã xoá cả hai (13.6.3 chỉ còn Interstitial/AppOpen/Rewarded/Banner/MRec).
    ///
    /// BẪY: khối code nằm sau <c>#if gameanalytics_max_enabled &amp;&amp; !(UNITY_EDITOR)</c> nên
    /// compile trong Editor SẠCH — chỉ vỡ khi build Player với CS0117. Nghĩa là compile-check
    /// không bắt được, phải build thật mới lộ. Cũng chính vì compile Editor sạch nên script này
    /// luôn chạy được để tự chữa.
    ///
    /// Chạy tự động mỗi khi TinySauce được import/nâng cấp, nên vá không bao giờ mất.
    /// </summary>
    public class VoodooSdkGaIlrdPatcher : AssetPostprocessor
    {
        #region Fields

        /// <summary>Callback đã bị AppLovin MAX v13 xoá — mỗi entry là chuỗi nhận diện dòng.</summary>
        private static readonly string[] RemovedCallbacks =
        {
            "MaxSdkCallbacks.CrossPromo.",
            "MaxSdkCallbacks.RewardedInterstitial."
        };

        #endregion

        #region Events

        private static void OnPostprocessAllAssets(string[] imported, string[] deleted,
                                                   string[] moved, string[] movedFrom)
        {
            bool touchesTinySauce = imported.Any(p => p.StartsWith(VoodooSdkPaths.TinySauceRoot));
            if (touchesTinySauce)
                Apply(logWhenClean: false);
        }

        #endregion

        #region Public

        /// <summary>Trả về true nếu file đã sạch (không còn callback nào bị gỡ).</summary>
        public static bool IsPatched()
        {
            string path = VoodooSdkPaths.Absolute(VoodooSdkPaths.GaMaxIntegrationScript);
            if (!File.Exists(path))
                return true; // Không có file thì không có gì để hỏng.

            string content = File.ReadAllText(path);
            return RemovedCallbacks.All(needle => !content.Contains(needle));
        }

        /// <summary>Gỡ các dòng đăng ký callback không còn tồn tại. Idempotent.</summary>
        public static void Apply(bool logWhenClean = true)
        {
            string path = VoodooSdkPaths.Absolute(VoodooSdkPaths.GaMaxIntegrationScript);
            if (!File.Exists(path))
                return;

            string[] lines = File.ReadAllLines(path);
            var kept = new List<string>(lines.Length);
            var removed = new List<string>();

            foreach (string line in lines)
            {
                string hit = RemovedCallbacks.FirstOrDefault(line.Contains);
                if (hit != null)
                    removed.Add(hit.TrimEnd('.'));
                else
                    kept.Add(line);
            }

            if (removed.Count == 0)
            {
                if (logWhenClean)
                    Debug.Log("[VoodooSdk] GAMaxIntegration.cs — đã sạch.");
                return;
            }

            File.WriteAllLines(path, kept);
            Debug.Log($"[VoodooSdk] Đã gỡ khỏi GAMaxIntegration.cs: {string.Join(", ", removed)} " +
                      "(AppLovin MAX v13 không còn các callback này). " +
                      "ILRD của INTER/BANNER/REWARDED/MREC vẫn hoạt động bình thường.");
            AssetDatabase.ImportAsset(VoodooSdkPaths.GaMaxIntegrationScript);
        }

        #endregion
    }
}
#endif
