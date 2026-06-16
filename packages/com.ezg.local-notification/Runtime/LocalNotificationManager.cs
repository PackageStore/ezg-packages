namespace Ezg.Feature.LocalNotification
{
    public static class LocalNotificationManager
    {
        public static void Init()
        {
            LocalNotificationService.Initialize();
        }

        public static void RegisterOrReplace(string notificationId, RuntimeNotificationRequest request)
        {
            LocalNotificationService.RegisterOrReplace(notificationId, request);
        }

        public static void RegisterOrReplace(
            string notificationId,
            string title,
            string content,
            long delaySeconds,
            bool loop = false,
            long loopDurationSeconds = 0,
            string[] triggerEvents = null,
            NotificationPayload payload = null,
            NotificationPlatformOptions platformOptions = null)
        {
            LocalNotificationService.RegisterOrReplace(
                notificationId,
                title,
                content,
                delaySeconds,
                loop,
                loopDurationSeconds,
                triggerEvents,
                payload,
                platformOptions);
        }

        public static void Stop(string notificationId)
        {
            LocalNotificationService.Stop(notificationId);
        }

        public static NotificationPermissionStatus RefreshPermissionStatus()
        {
            return LocalNotificationService.RefreshPermissionStatus();
        }

        public static NotificationPermissionStatus GetPermissionStatus()
        {
            return LocalNotificationService.GetPermissionStatus();
        }

        public static bool AreNotificationsEnabledBySystem()
        {
            return GetPermissionStatus() == NotificationPermissionStatus.Granted;
        }

        public static bool OpenAppNotificationSettings()
        {
            return LocalNotificationService.OpenAppNotificationSettings();
        }
    }
}
