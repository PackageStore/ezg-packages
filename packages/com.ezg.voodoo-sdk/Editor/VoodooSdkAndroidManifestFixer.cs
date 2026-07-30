#if UNITY_EDITOR
using System.Linq;
using System.Text.RegularExpressions;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Ezg.VoodooSdk.Editor
{
    /// <summary>
    /// Sửa lại <c>Assets/Plugins/Android/AndroidManifest.xml</c> NGAY SAU khi TinySauce ghi đè nó.
    ///
    /// <c>Voodoo.Sauce.Internal.Editor.AndroidPreBuild</c> có <c>callbackOrder = 0</c> và ghi đè
    /// cứng manifest ở mỗi lần build (chỉ merge lại thẻ <c>&lt;uses-permission&gt;</c>, mọi thứ
    /// trong <c>&lt;application&gt;</c> đều mất). Hook này chạy với order lớn hơn nên vào sau và
    /// khôi phục hai thứ:
    ///
    /// 1. <b>Firebase Messaging</b> — activity launcher + MessageForwardingService. Thiếu thì
    ///    notification có data payload nhận lúc app ở background sẽ mất.
    /// 2. <b>Namespace cho <c>tools:replace</c></b> — template Voodoo ghi
    ///    <c>tools:replace="fullBackupContent,allowBackup"</c> thiếu prefix. AGP 8.x đòi tên đầy đủ,
    ///    thiếu thì manifest merger sinh nhầm khoá và build hỏng:
    ///    <c>Multiple entries with same key: android:allowBackup=REPLACE and tools:allowBackup=REPLACE</c>
    ///
    /// Sửa file ĐÍCH thay vì template trong thư mục SDK, nên nâng cấp TinySauce không làm mất vá.
    /// </summary>
    public class VoodooSdkAndroidManifestFixer : IPreprocessBuildWithReport
    {
        #region Fields

        /// <summary>Phải lớn hơn 0 — AndroidPreBuild của TinySauce dùng 0.</summary>
        public int callbackOrder => 100;

        private const string UnityActivity = "com.unity3d.player.UnityPlayerActivity";
        private const string ForwardingService = "com.google.firebase.messaging.MessageForwardingService";
        private const string ThirdPartyMarker = "<!-- 3rdParty MANIFEST -->";

        #endregion

        #region Events

        public void OnPreprocessBuild(BuildReport report)
        {
            if (report.summary.platform != BuildTarget.Android)
                return;

            Fix(VoodooSdkConfig.Load());
        }

        #endregion

        #region Public

        /// <summary>Áp dụng mọi sửa chữa lên manifest đích. Idempotent.</summary>
        public static void Fix(VoodooSdkConfig config)
        {
            FixToolsReplace(VoodooSdkPaths.AndroidManifest);
            FixToolsReplace(VoodooSdkPaths.LauncherManifest);

            string activity = config?.firebase?.notificationActivity;
            if (!string.IsNullOrWhiteSpace(activity))
                RestoreFirebase(activity);

            // Bù khai báo mà com.unity.mobile.notifications không tiêm được ở build batchmode.
            VoodooSdkNotificationManifest.Inject();
        }

        #endregion

        #region Private

        /// <summary>Thêm prefix <c>android:</c> cho mọi tên chưa có namespace trong tools:replace.</summary>
        private static void FixToolsReplace(string projectRelative)
        {
            string path = VoodooSdkPaths.Absolute(projectRelative);
            if (!File.Exists(path))
                return;

            string original = File.ReadAllText(path);
            bool hadBom = VoodooSdkXml.HasUtf8Bom(path);
            string patched = Regex.Replace(original, "tools:replace=\"([^\"]*)\"", match =>
            {
                string[] names = match.Groups[1].Value
                    .Split(',')
                    .Select(n => n.Trim())
                    .Where(n => n.Length > 0)
                    .Select(n => n.Contains(':') ? n : "android:" + n)
                    .ToArray();
                return $"tools:replace=\"{string.Join(",", names)}\"";
            });

            if (patched == original)
                return;

            VoodooSdkXml.Write(path, patched, hadBom);
            Debug.Log($"[VoodooSdk] Đã bổ sung namespace cho tools:replace trong {projectRelative}.");
        }

        /// <summary>Đưa launcher activity về Firebase và bảo đảm có MessageForwardingService.</summary>
        private static void RestoreFirebase(string notificationActivity)
        {
            string path = VoodooSdkPaths.Absolute(VoodooSdkPaths.AndroidManifest);
            if (!File.Exists(path))
                return;

            string content = File.ReadAllText(path);
            bool hadBom = VoodooSdkXml.HasUtf8Bom(path);
            bool changed = false;

            if (content.Contains(UnityActivity))
            {
                content = content.Replace(UnityActivity, notificationActivity);
                changed = true;
            }

            if (!content.Contains(ForwardingService) && content.Contains(ThirdPartyMarker))
            {
                string service =
                    "<!-- FIREBASE MESSAGING (khôi phục bởi com.ezg.voodoo-sdk) -->\n" +
                    $"<service android:name=\"{ForwardingService}\"\n" +
                    "         android:permission=\"android.permission.BIND_JOB_SERVICE\"\n" +
                    "         android:exported=\"true\" />\n";
                content = VoodooSdkXml.InsertBeforeLineOf(content, ThirdPartyMarker, service);
                changed = true;
            }

            if (!changed)
                return;

            VoodooSdkXml.Write(path, content, hadBom);
            Debug.Log($"[VoodooSdk] Đã khôi phục Firebase Messaging trong {VoodooSdkPaths.AndroidManifest} " +
                      $"(launcher = {notificationActivity}).");
        }


        #endregion
    }
}
#endif
