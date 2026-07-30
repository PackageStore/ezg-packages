#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Ezg.VoodooSdk.Editor
{
    /// <summary>
    /// Bù các khai báo manifest mà <c>com.unity.mobile.notifications</c> lẽ ra phải tự tiêm.
    ///
    /// TRIỆU CHỨNG
    /// -----------
    ///     [LocalNotifications] Android notification bridge failed during initialize permission
    ///     status. java.lang.RuntimeException: Failed to determine Activity to be opened when
    ///     tapping notification
    ///
    /// Nhưng hậu quả thật NẶNG HƠN thông báo đó: khi thiếu receiver
    /// <c>UnityNotificationManager</c> thì notification đã hẹn <b>không bao giờ nổ</b>, không chỉ
    /// là lỗi resolve activity lúc chạm. Rất dễ bỏ qua vì class Java vẫn nằm trong APK và
    /// <c>LocalNotificationService</c> báo <c>available=True</c>.
    ///
    /// NGUYÊN NHÂN
    /// -----------
    /// Trên Unity 6, <c>AndroidNotificationPostProcessor</c> chạy theo nhánh
    /// <c>AndroidProjectFilesModifier</c> và ghi vào
    /// <c>unityLibrary/mobilenotifications.androidlib/src/main/AndroidManifest.xml</c>.
    /// Ở build batchmode, file đó không được sinh ra và không thứ gì trong
    /// <c>&lt;application&gt;</c> tới được APK — kiểm chứng bằng cách dump manifest của APK:
    /// không có receiver, không có <c>custom_notification_android_activity</c>, không có
    /// <c>exact_scheduling</c>. Build từ Editor GUI thì có. Cùng họ với bug Apple Unity Plug-ins.
    ///
    /// Vì vậy bật <c>UseCustomActivity</c> trong Notifications settings là ĐÚNG nhưng vô dụng —
    /// chính postprocessor mới là thứ đọc setting đó.
    ///
    /// CÁCH BÙ
    /// -------
    /// Tự tiêm vào <c>Assets/Plugins/Android/AndroidManifest.xml</c>, chạy sau TinySauce
    /// (xem <see cref="VoodooSdkAndroidManifestFixer"/>). Giá trị đọc từ
    /// <c>ProjectSettings/NotificationsSettings.asset</c> nên không hardcode; Unity sửa bug ở bản
    /// sau thì hook này thấy đã có và bỏ qua.
    /// </summary>
    public static class VoodooSdkNotificationManifest
    {
        #region Fields

        private const string SettingsFile = "ProjectSettings/NotificationsSettings.asset";
        private const string NotificationsPackage = "Packages/com.unity.mobile.notifications";

        private const string ManagerReceiver = "com.unity.androidnotifications.UnityNotificationManager";
        private const string RestartReceiver = "com.unity.androidnotifications.UnityNotificationRestartReceiver";
        private const string ExactSchedulingKey = "com.unity.androidnotifications.exact_scheduling";
        private const string CustomActivityKey = "custom_notification_android_activity";

        // AndroidExactSchedulingOption
        private const int ExactWhenAvailable = 1;
        private const int AddScheduleExactPermission = 1 << 1;
        private const int AddUseExactAlarmPermission = 1 << 2;
        private const int AddRequestIgnoreBatteryOptimizationsPermission = 1 << 3;

        private const int ScheduleExactMaxSdkWhenUseExact = 32;

        #endregion

        #region Types

        private class Settings
        {
            public bool RescheduleOnRestart;
            public int ExactAlarm;
            public bool UseCustomActivity;
            public string CustomActivity = "";
        }

        #endregion

        #region Public

        /// <summary>
        /// Tiêm receiver + meta-data + permission còn thiếu vào manifest đích. Idempotent.
        /// Không làm gì nếu project không dùng <c>com.unity.mobile.notifications</c>.
        /// </summary>
        public static void Inject()
        {
            if (!Directory.Exists(VoodooSdkPaths.Absolute(NotificationsPackage)) &&
                !Directory.Exists(VoodooSdkPaths.Absolute("Library/PackageCache")))
                return;

            Settings settings = ReadSettings();
            if (settings == null)
                return;

            string path = VoodooSdkPaths.Absolute(VoodooSdkPaths.AndroidManifest);
            if (!File.Exists(path))
                return;

            string content = File.ReadAllText(path);
            bool hadBom = VoodooSdkXml.HasUtf8Bom(path);
            var added = new List<string>();

            // Receiver bắt buộc — thiếu cái này thì notification đã hẹn không nổ.
            content = AddReceiver(content, ManagerReceiver, intentAction: null, added);

            if (settings.RescheduleOnRestart)
            {
                content = AddReceiver(content, RestartReceiver, "android.intent.action.BOOT_COMPLETED", added);
                content = AddPermission(content, "android.permission.RECEIVE_BOOT_COMPLETED", added);
            }

            bool exact = (settings.ExactAlarm & ExactWhenAvailable) != 0;
            content = AddMetaData(content, ExactSchedulingKey, exact ? "1" : "0", added);

            if (exact)
            {
                bool scheduleExact = (settings.ExactAlarm & AddScheduleExactPermission) != 0;
                bool useExact = (settings.ExactAlarm & AddUseExactAlarmPermission) != 0;

                // Tài liệu Android: chỉ dùng MỘT trong hai, hoặc giới hạn maxSdkVersion cho cái đầu.
                if (scheduleExact)
                {
                    string extra = useExact ? $" android:maxSdkVersion=\"{ScheduleExactMaxSdkWhenUseExact}\"" : "";
                    content = AddPermission(content, "android.permission.SCHEDULE_EXACT_ALARM", added, extra);
                }
                if (useExact)
                    content = AddPermission(content, "android.permission.USE_EXACT_ALARM", added);
                if ((settings.ExactAlarm & AddRequestIgnoreBatteryOptimizationsPermission) != 0)
                {
                    // Battery optimization PHẢI dùng uses-permission-sdk-23, uses-permission thường không ăn.
                    content = AddPermission(content, "android.permission.REQUEST_IGNORE_BATTERY_OPTIMIZATIONS",
                        added, element: "uses-permission-sdk-23");
                }
            }

            content = AddPermission(content, "android.permission.POST_NOTIFICATIONS", added);

            if (settings.UseCustomActivity && !string.IsNullOrWhiteSpace(settings.CustomActivity))
                content = AddMetaData(content, CustomActivityKey, settings.CustomActivity, added);

            if (added.Count == 0)
                return;

            VoodooSdkXml.Write(path, content, hadBom);
            Debug.Log($"[VoodooSdk] Đã bù {added.Count} khai báo notification vào " +
                      $"{VoodooSdkPaths.AndroidManifest}: {string.Join(", ", added)}");
        }

        #endregion

        #region Private

        /// <summary>
        /// Đọc <c>NotificationsSettings.asset</c>. Unity ghi file này dạng JSON với hai list song
        /// song m_Keys/m_Values, nên parse bằng regex thay vì JsonUtility (không đọc được list
        /// lồng kiểu này một cách tiện lợi).
        /// </summary>
        private static Settings ReadSettings()
        {
            string path = VoodooSdkPaths.Absolute(SettingsFile);
            if (!File.Exists(path))
                return null;

            string text = File.ReadAllText(path);
            Match block = Regex.Match(text,
                @"""m_AndroidNotificationSettingsValues""\s*:\s*\{.*?""m_Keys""\s*:\s*\[(?<keys>.*?)\].*?""m_Values""\s*:\s*\[(?<values>.*?)\]",
                RegexOptions.Singleline);
            if (!block.Success)
                return null;

            string[] keys = Extract(block.Groups["keys"].Value);
            string[] values = Extract(block.Groups["values"].Value);
            if (keys.Length != values.Length)
                return null;

            var map = new Dictionary<string, string>();
            for (int i = 0; i < keys.Length; i++)
                map[keys[i]] = values[i];

            var settings = new Settings();
            if (map.TryGetValue("UnityNotificationAndroidRescheduleOnDeviceRestart", out string reschedule))
                settings.RescheduleOnRestart = reschedule.Equals("True", System.StringComparison.OrdinalIgnoreCase);
            if (map.TryGetValue("UnityNotificationAndroidScheduleExactAlarms", out string alarms))
                int.TryParse(alarms, out settings.ExactAlarm);
            if (map.TryGetValue("UnityNotificationAndroidUseCustomActivity", out string useCustom))
                settings.UseCustomActivity = useCustom.Equals("True", System.StringComparison.OrdinalIgnoreCase);
            if (map.TryGetValue("UnityNotificationAndroidCustomActivityString", out string activity))
                settings.CustomActivity = activity;

            return settings;
        }

        private static string[] Extract(string jsonArrayBody)
        {
            var items = new List<string>();
            foreach (Match m in Regex.Matches(jsonArrayBody, @"""((?:[^""\\]|\\.)*)"""))
                items.Add(m.Groups[1].Value);
            return items.ToArray();
        }

        private static string AddPermission(string content, string name, List<string> added,
                                            string extraAttributes = "", string element = "uses-permission")
        {
            if (content.Contains($"android:name=\"{name}\""))
                return content;

            added.Add(name.Substring(name.LastIndexOf('.') + 1));
            return InsertBeforeApplication(content, $"  <{element} android:name=\"{name}\"{extraAttributes} />\n");
        }

        private static string AddMetaData(string content, string name, string value, List<string> added)
        {
            if (content.Contains($"android:name=\"{name}\""))
                return content;

            added.Add(name.Substring(name.LastIndexOf('.') + 1));
            return InsertIntoApplication(content,
                $"    <meta-data android:name=\"{name}\" android:value=\"{value}\" />\n");
        }

        private static string AddReceiver(string content, string name, string intentAction, List<string> added)
        {
            if (content.Contains($"android:name=\"{name}\""))
                return content;

            added.Add(name.Substring(name.LastIndexOf('.') + 1));

            string block = intentAction == null
                ? $"    <receiver android:name=\"{name}\" android:exported=\"false\" />\n"
                : $"    <receiver android:name=\"{name}\" android:exported=\"false\">\n" +
                  $"      <intent-filter>\n" +
                  $"        <action android:name=\"{intentAction}\" />\n" +
                  $"      </intent-filter>\n" +
                  $"    </receiver>\n";

            return InsertIntoApplication(content, block);
        }

        private static string InsertBeforeApplication(string content, string xml)
        {
            return VoodooSdkXml.InsertBeforeLineOf(content, "<application", xml);
        }

        /// <summary>Chèn vào ngay trước thẻ đóng của <c>&lt;application&gt;</c>.</summary>
        private static string InsertIntoApplication(string content, string xml)
        {
            return VoodooSdkXml.InsertBeforeLineOf(content, "</application>", xml, last: true);
        }

        #endregion
    }
}
#endif
