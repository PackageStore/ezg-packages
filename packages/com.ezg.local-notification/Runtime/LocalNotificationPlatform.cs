using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
#if UNITY_ANDROID
using Unity.Notifications.Android;
#endif
#if UNITY_IOS
using Unity.Notifications.iOS;
#endif

namespace Ezg.Feature.LocalNotification
{
    public interface INotificationPlatformProvider
    {
        bool IsSupported { get; }
        void Initialize();
        void ScheduleOrUpdate(string notificationId, NotificationScheduleRequest schedule, NotificationContentTemplate contentTemplate, NotificationPayload payload, NotificationPlatformOptions options);
        void Cancel(string notificationId);
        UniTask<NotificationLaunchResult> CaptureLastRespondedNotificationAsync();
    }

    public interface INotificationPermissionService
    {
        NotificationPermissionStatus CurrentStatus { get; }
        event Action<NotificationPermissionStatus> StatusChanged;
        void Initialize();
        void RequestPermissionIfNeeded();
        bool OpenAppNotificationSettings();
    }

    public sealed class UnsupportedNotificationPlatformProvider : INotificationPlatformProvider
    {
        public bool IsSupported => false;

        public void Initialize()
        {
        }

        public void ScheduleOrUpdate(string notificationId, NotificationScheduleRequest schedule, NotificationContentTemplate contentTemplate, NotificationPayload payload, NotificationPlatformOptions options)
        {
        }

        public void Cancel(string notificationId)
        {
        }

        public UniTask<NotificationLaunchResult> CaptureLastRespondedNotificationAsync()
        {
            return UniTask.FromResult(default(NotificationLaunchResult));
        }
    }

    public sealed class UnsupportedNotificationPermissionService : INotificationPermissionService
    {
        public NotificationPermissionStatus CurrentStatus => NotificationPermissionStatus.Unavailable;
        public event Action<NotificationPermissionStatus> StatusChanged;

        public void Initialize()
        {
            StatusChanged?.Invoke(CurrentStatus);
        }

        public void RequestPermissionIfNeeded()
        {
        }

        public bool OpenAppNotificationSettings()
        {
            return false;
        }
    }

#if UNITY_ANDROID
    public sealed class AndroidNotificationPlatformProvider : INotificationPlatformProvider
    {
        private readonly HashSet<string> _registeredChannels = new HashSet<string>();
        private bool _isAvailable = true;

        public bool IsSupported => true;

        public void Initialize()
        {
            _isAvailable = TryRunAndroidNotificationCall("provider initialize", () => { });
            Debug.Log($"[LocalNotifications] Android provider initialize result: available={_isAvailable}.");
        }

        public void ScheduleOrUpdate(string notificationId, NotificationScheduleRequest schedule, NotificationContentTemplate contentTemplate, NotificationPayload payload, NotificationPlatformOptions options)
        {
            if (!_isAvailable)
            {
                Debug.LogWarning($"[LocalNotifications] Android provider unavailable. Skip schedule for {notificationId}.");
                return;
            }

            EnsureChannel(options);

            var notification = new AndroidNotification
            {
                Title = contentTemplate.ResolveTitle(),
                Text = contentTemplate.ResolveBody(),
                FireTime = ResolveFireTime(schedule),
                SmallIcon = options.AndroidSmallIcon,
                LargeIcon = options.AndroidLargeIcon,
                ShowInForeground = options.ShowInForeground,
                IntentData = payload != null ? payload.Serialize() : string.Empty,
            };

            if (schedule.RepeatInterval.HasValue)
            {
                notification.RepeatInterval = schedule.RepeatInterval.Value;
            }

            var platformId = GetPlatformNotificationId(notificationId);
            Debug.Log(
                $"[LocalNotifications] Android schedule request: id={notificationId}, platformId={platformId}, " +
                $"channel={options.AndroidChannelId}, title={notification.Title}, body={notification.Text}, " +
                $"fireTimeLocal={notification.FireTime:yyyy-MM-dd HH:mm:ss}, repeatInterval={(schedule.RepeatInterval.HasValue ? schedule.RepeatInterval.Value.ToString() : "none")}, " +
                $"showInForeground={notification.ShowInForeground}, payload={(payload != null ? payload.Serialize() : string.Empty)}");
            if (!TryRunAndroidNotificationCall($"schedule {notificationId}", () =>
                {
                    AndroidNotificationCenter.CancelNotification(platformId);
                    AndroidNotificationCenter.SendNotificationWithExplicitID(notification, options.AndroidChannelId, platformId);
                }))
            {
                _isAvailable = false;
                Debug.LogError($"[LocalNotifications] Android schedule failed for {notificationId}. Provider marked unavailable.");
                return;
            }

            Debug.Log($"[LocalNotifications] Android schedule submitted: id={notificationId}, platformId={platformId}.");
            LogScheduledStatus(notificationId, platformId);
        }

        public void Cancel(string notificationId)
        {
            if (!_isAvailable)
            {
                return;
            }

            if (!TryRunAndroidNotificationCall($"cancel {notificationId}", () =>
                {
                    AndroidNotificationCenter.CancelNotification(GetPlatformNotificationId(notificationId));
                }))
            {
                _isAvailable = false;
                Debug.LogError($"[LocalNotifications] Android cancel failed for {notificationId}. Provider marked unavailable.");
                return;
            }

            Debug.Log($"[LocalNotifications] Android cancel submitted: id={notificationId}, platformId={GetPlatformNotificationId(notificationId)}.");
        }

        public UniTask<NotificationLaunchResult> CaptureLastRespondedNotificationAsync()
        {
            if (!_isAvailable)
            {
                return UniTask.FromResult(default(NotificationLaunchResult));
            }

            AndroidNotificationIntentData intentData = null;
            var success = TryRunAndroidNotificationCall("capture launch payload", () =>
            {
                intentData = AndroidNotificationCenter.GetLastNotificationIntent();
            });

            if (!success)
            {
                _isAvailable = false;
                return UniTask.FromResult(default(NotificationLaunchResult));
            }

            if (intentData == null)
            {
                return UniTask.FromResult(default(NotificationLaunchResult));
            }

            return UniTask.FromResult(new NotificationLaunchResult
            {
                HasPayload = true,
                Payload = NotificationPayload.Deserialize(intentData.Notification.IntentData),
            });
        }

        private void EnsureChannel(NotificationPlatformOptions options)
        {
            if (!_isAvailable)
            {
                return;
            }

            if (_registeredChannels.Contains(options.AndroidChannelId))
            {
                return;
            }

            var channel = new AndroidNotificationChannel
            {
                Id = options.AndroidChannelId,
                Name = options.AndroidChannelName,
                Importance = Importance.High,
                Description = options.AndroidChannelDescription,
            };

            if (!TryRunAndroidNotificationCall($"register channel {options.AndroidChannelId}", () =>
                {
                    AndroidNotificationCenter.RegisterNotificationChannel(channel);
                }))
            {
                _isAvailable = false;
                Debug.LogError($"[LocalNotifications] Android register channel failed: channel={options.AndroidChannelId}. Provider marked unavailable.");
                return;
            }

            _registeredChannels.Add(options.AndroidChannelId);
            Debug.Log($"[LocalNotifications] Android channel registered: channel={options.AndroidChannelId}, name={options.AndroidChannelName}.");
        }

        private static int GetPlatformNotificationId(string notificationId)
        {
            unchecked
            {
                const int offset = 216613626;
                const int prime = 16777619;
                var hash = offset;
                var value = notificationId ?? string.Empty;
                for (int index = 0; index < value.Length; index++)
                {
                    hash ^= value[index];
                    hash *= prime;
                }

                return 1000 + Math.Abs(hash);
            }
        }

        private static DateTime ResolveFireTime(NotificationScheduleRequest schedule)
        {
            switch (schedule.Mode)
            {
                case NotificationScheduleMode.DelaySeconds:
                    return DateTime.Now.AddSeconds(schedule.DelaySeconds);
                case NotificationScheduleMode.FireAtUtc:
                    return schedule.FireTime.ToLocalTime();
                case NotificationScheduleMode.FireAtLocal:
                    return schedule.FireTime;
                default:
                    return DateTime.Now.AddSeconds(1);
            }
        }

        private static bool TryRunAndroidNotificationCall(string operation, Action action)
        {
            try
            {
                action?.Invoke();
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogError($"[LocalNotifications] Android notification bridge failed during {operation}. {exception}");
                return false;
            }
        }

        private static void LogScheduledStatus(string notificationId, int platformId)
        {
            try
            {
                var status = AndroidNotificationCenter.CheckScheduledNotificationStatus(platformId);
                Debug.Log($"[LocalNotifications] Android status after schedule: id={notificationId}, platformId={platformId}, status={status}.");
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[LocalNotifications] Failed to query Android notification status for {notificationId}/{platformId}. {exception}");
            }
        }
    }

    public sealed class AndroidNotificationPermissionService : INotificationPermissionService
    {
        public NotificationPermissionStatus CurrentStatus { get; private set; } = NotificationPermissionStatus.Unknown;
        public event Action<NotificationPermissionStatus> StatusChanged;
        private bool _isAvailable = true;

        public void Initialize()
        {
            if (!_isAvailable)
            {
                SetStatus(NotificationPermissionStatus.Unavailable);
                return;
            }

            PermissionStatus permissionStatus = PermissionStatus.NotRequested;
            if (!TryGetPermissionStatus("initialize permission status", out permissionStatus))
            {
                _isAvailable = false;
                SetStatus(NotificationPermissionStatus.Unavailable);
                return;
            }

            Debug.Log($"[LocalNotifications] Android permission initialize: raw={permissionStatus}.");
            SetStatus(MapStatus(permissionStatus));
        }

        public void RequestPermissionIfNeeded()
        {
            if (!_isAvailable)
            {
                SetStatus(NotificationPermissionStatus.Unavailable);
                return;
            }

            PermissionStatus currentPermission;
            if (!TryGetPermissionStatus("read permission status", out currentPermission))
            {
                _isAvailable = false;
                SetStatus(NotificationPermissionStatus.Unavailable);
                return;
            }

            Debug.Log($"[LocalNotifications] Android permission request check: raw={currentPermission}.");

            if (currentPermission == PermissionStatus.Allowed ||
                currentPermission == PermissionStatus.NotificationsBlockedForApp ||
                currentPermission == PermissionStatus.Denied)
            {
                SetStatus(MapStatus(currentPermission));
                return;
            }

            MonitorRequestAsync().Forget();
        }

        public bool OpenAppNotificationSettings()
        {
            if (!_isAvailable)
            {
                return false;
            }

            try
            {
                using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var currentActivity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var intent = new AndroidJavaObject("android.content.Intent"))
                using (var settingsClass = new AndroidJavaClass("android.provider.Settings"))
                using (var sdkVersionClass = new AndroidJavaClass("android.os.Build$VERSION"))
                {
                    var sdkInt = sdkVersionClass.GetStatic<int>("SDK_INT");
                    var packageName = Application.identifier;

                    if (sdkInt >= 26)
                    {
                        intent.Call<AndroidJavaObject>("setAction", settingsClass.GetStatic<string>("ACTION_APP_NOTIFICATION_SETTINGS"));
                        intent.Call<AndroidJavaObject>("putExtra", "android.provider.extra.APP_PACKAGE", packageName);
                    }
                    else
                    {
                        intent.Call<AndroidJavaObject>("setAction", settingsClass.GetStatic<string>("ACTION_APPLICATION_DETAILS_SETTINGS"));
                        using (var uriClass = new AndroidJavaClass("android.net.Uri"))
                        using (var uri = uriClass.CallStatic<AndroidJavaObject>("fromParts", "package", packageName, null))
                        {
                            intent.Call<AndroidJavaObject>("setData", uri);
                        }
                    }

                    intent.Call<AndroidJavaObject>("addFlags", 268435456);
                    currentActivity.Call("startActivity", intent);
                }

                return true;
            }
            catch (Exception exception)
            {
                Debug.LogError($"[LocalNotifications] Android open notification settings failed. {exception}");
                return false;
            }
        }

        private async UniTaskVoid MonitorRequestAsync()
        {
            PermissionRequest request;
            try
            {
                request = new PermissionRequest();
            }
            catch (Exception exception)
            {
                _isAvailable = false;
                SetStatus(NotificationPermissionStatus.Unavailable);
                Debug.LogError($"[LocalNotifications] Android notification bridge failed during permission request. {exception}");
                return;
            }

            Debug.Log($"[LocalNotifications] Android permission request started: raw={request.Status}.");
            SetStatus(MapStatus(request.Status));

            while (request.Status == PermissionStatus.RequestPending)
            {
                await UniTask.Yield();
            }

            Debug.Log($"[LocalNotifications] Android permission request completed: raw={request.Status}.");
            SetStatus(MapStatus(request.Status));
        }

        private void SetStatus(NotificationPermissionStatus status)
        {
            if (CurrentStatus == status)
            {
                return;
            }

            CurrentStatus = status;
            StatusChanged?.Invoke(CurrentStatus);
        }

        private static NotificationPermissionStatus MapStatus(PermissionStatus permissionStatus)
        {
            switch (permissionStatus)
            {
                case PermissionStatus.Allowed:
                    return NotificationPermissionStatus.Granted;
                case PermissionStatus.Denied:
                case PermissionStatus.DeniedDontAskAgain:
                case PermissionStatus.NotificationsBlockedForApp:
                    return NotificationPermissionStatus.Denied;
                case PermissionStatus.NotRequested:
                case PermissionStatus.RequestPending:
                    return NotificationPermissionStatus.Unknown;
                default:
                    return NotificationPermissionStatus.Unknown;
            }
        }

        private static bool TryGetPermissionStatus(string operation, out PermissionStatus permissionStatus)
        {
            try
            {
                permissionStatus = AndroidNotificationCenter.UserPermissionToPost;
                return true;
            }
            catch (Exception exception)
            {
                permissionStatus = PermissionStatus.NotRequested;
                Debug.LogError($"[LocalNotifications] Android notification bridge failed during {operation}. {exception}");
                return false;
            }
        }
    }
#endif

#if UNITY_IOS
    public sealed class IOSNotificationPlatformProvider : INotificationPlatformProvider
    {
        public bool IsSupported => true;

        public void Initialize()
        {
        }

        public void ScheduleOrUpdate(string notificationId, NotificationScheduleRequest schedule, NotificationContentTemplate contentTemplate, NotificationPayload payload, NotificationPlatformOptions options)
        {
            var identifier = GetPlatformNotificationId(notificationId);
            var notification = new iOSNotification(identifier)
            {
                Title = contentTemplate.ResolveTitle(),
                Body = contentTemplate.ResolveBody(),
                Data = payload != null ? payload.Serialize() : string.Empty,
                ShowInForeground = options.ShowInForeground,
                ThreadIdentifier = options.IOSThreadIdentifier,
                CategoryIdentifier = options.IOSCategoryIdentifier,
            };

            notification.Trigger = BuildTrigger(schedule);
            Debug.Log(
                $"[LocalNotifications] iOS schedule request: id={notificationId}, identifier={identifier}, " +
                $"title={notification.Title}, body={notification.Body}, trigger={notification.Trigger?.GetType().Name}, " +
                $"scheduleMode={schedule.Mode}, fireTime={schedule.FireTime:yyyy-MM-dd HH:mm:ss}, delaySeconds={schedule.DelaySeconds}, " +
                $"repeatInterval={(schedule.RepeatInterval.HasValue ? schedule.RepeatInterval.Value.ToString() : "none")}, payload={notification.Data}");

            iOSNotificationCenter.RemoveScheduledNotification(identifier);
            iOSNotificationCenter.RemoveDeliveredNotification(identifier);
            iOSNotificationCenter.ScheduleNotification(notification);
        }

        public void Cancel(string notificationId)
        {
            var identifier = GetPlatformNotificationId(notificationId);
            iOSNotificationCenter.RemoveScheduledNotification(identifier);
            iOSNotificationCenter.RemoveDeliveredNotification(identifier);
        }

        public async UniTask<NotificationLaunchResult> CaptureLastRespondedNotificationAsync()
        {
            var operation = iOSNotificationCenter.QueryLastRespondedNotification();
            while (operation.State == QueryLastRespondedNotificationState.Pending)
            {
                await UniTask.Yield();
            }

            if (operation.State != QueryLastRespondedNotificationState.HaveRespondedNotification ||
                operation.Notification == null)
            {
                return default(NotificationLaunchResult);
            }

            return new NotificationLaunchResult
            {
                HasPayload = true,
                Payload = NotificationPayload.Deserialize(operation.Notification.Data),
            };
        }

        private static string GetPlatformNotificationId(string notificationId)
        {
            return $"m1.mobile_notification.{notificationId}";
        }

        private static iOSNotificationTrigger BuildTrigger(NotificationScheduleRequest schedule)
        {
            switch (schedule.Mode)
            {
                case NotificationScheduleMode.DelaySeconds:
                    {
                        var trigger = new iOSNotificationTimeIntervalTrigger
                        {
                            TimeInterval = TimeSpan.FromSeconds(schedule.DelaySeconds),
                            Repeats = schedule.RepeatInterval.HasValue,
                        };

                        if (schedule.RepeatInterval.HasValue &&
                            Math.Abs(schedule.RepeatInterval.Value.TotalSeconds - schedule.DelaySeconds) > 0.5d)
                        {
                            Debug.LogWarning("[LocalNotifications] iOS repeating delay uses the same interval as initial delay. RepeatInterval override was ignored.");
                        }

                        return trigger;
                    }
                case NotificationScheduleMode.FireAtUtc:
                    return BuildCalendarTrigger(schedule.FireTime, true, schedule.RepeatInterval.HasValue);
                case NotificationScheduleMode.FireAtLocal:
                    return BuildCalendarTrigger(schedule.FireTime, false, schedule.RepeatInterval.HasValue);
                default:
                    return new iOSNotificationTimeIntervalTrigger
                    {
                        TimeInterval = TimeSpan.FromSeconds(1),
                        Repeats = false,
                    };
            }
        }

        private static iOSNotificationCalendarTrigger BuildCalendarTrigger(DateTime dateTime, bool isUtc, bool repeats)
        {
            return new iOSNotificationCalendarTrigger
            {
                Year = dateTime.Year,
                Month = dateTime.Month,
                Day = dateTime.Day,
                Hour = dateTime.Hour,
                Minute = dateTime.Minute,
                Second = dateTime.Second,
                UtcTime = isUtc,
                Repeats = repeats,
            };
        }
    }

    public sealed class IOSNotificationPermissionService : INotificationPermissionService
    {
        public NotificationPermissionStatus CurrentStatus { get; private set; } = NotificationPermissionStatus.Unknown;
        public event Action<NotificationPermissionStatus> StatusChanged;

        public void Initialize()
        {
            SetStatus(MapStatus(iOSNotificationCenter.GetNotificationSettings().AuthorizationStatus));
        }

        public void RequestPermissionIfNeeded()
        {
            if (iOSNotificationCenter.GetNotificationSettings().AuthorizationStatus != AuthorizationStatus.NotDetermined)
            {
                SetStatus(MapStatus(iOSNotificationCenter.GetNotificationSettings().AuthorizationStatus));
                return;
            }

            RequestAuthorizationAsync().Forget();
        }

        public bool OpenAppNotificationSettings()
        {
            Application.OpenURL("app-settings:");
            return true;
        }

        private async UniTaskVoid RequestAuthorizationAsync()
        {
            using (var request = new AuthorizationRequest(AuthorizationOption.Alert | AuthorizationOption.Badge | AuthorizationOption.Sound, false))
            {
                while (!request.IsFinished)
                {
                    await UniTask.Yield();
                }

                SetStatus(request.Granted ? NotificationPermissionStatus.Granted : NotificationPermissionStatus.Denied);
            }
        }

        private void SetStatus(NotificationPermissionStatus status)
        {
            if (CurrentStatus == status)
            {
                return;
            }

            CurrentStatus = status;
            StatusChanged?.Invoke(CurrentStatus);
        }

        private static NotificationPermissionStatus MapStatus(AuthorizationStatus authorizationStatus)
        {
            switch (authorizationStatus)
            {
                case AuthorizationStatus.Authorized:
                case AuthorizationStatus.Provisional:
                case AuthorizationStatus.Ephemeral:
                    return NotificationPermissionStatus.Granted;
                case AuthorizationStatus.Denied:
                    return NotificationPermissionStatus.Denied;
                case AuthorizationStatus.NotDetermined:
                    return NotificationPermissionStatus.Unknown;
                default:
                    return NotificationPermissionStatus.Unknown;
            }
        }
    }
#endif
}
