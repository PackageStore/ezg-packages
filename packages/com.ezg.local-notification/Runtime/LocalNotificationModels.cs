using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Ezg.Package.Localize;
using Ezg.Package.Localize.Localization;

namespace Ezg.Feature.LocalNotification
{
    public enum NotificationPermissionStatus
    {
        Unknown = 0,
        Granted = 1,
        Denied = 2,
        Unavailable = 3,
    }

    public enum NotificationPermissionPolicy
    {
        None = 0,
        RequireAuthorization = 1,
    }

    public enum NotificationScheduleMode
    {
        DelaySeconds = 0,
        FireAtUtc = 1,
        FireAtLocal = 2,
    }

    [Serializable]
    public sealed class NotificationPayload
    {
        public string Key;
        public string Route;
        public string Data;

        public static NotificationPayload Create(string notificationId, string route = "", string data = "")
        {
            return new NotificationPayload
            {
                Key = notificationId ?? string.Empty,
                Route = route ?? string.Empty,
                Data = data ?? string.Empty,
            };
        }

        public string Serialize()
        {
            return JsonUtility.ToJson(this);
        }

        public static NotificationPayload Deserialize(string serialized)
        {
            if (string.IsNullOrEmpty(serialized))
            {
                return null;
            }

            try
            {
                return JsonUtility.FromJson<NotificationPayload>(serialized);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[LocalNotifications] Failed to deserialize payload. {exception.Message}");
                return null;
            }
        }
    }

    public sealed class NotificationContentTemplate
    {
        public string TitleKey { get; private set; }
        public string BodyKey { get; private set; }
        public LocalizeCategory TitleCategory { get; private set; } = LocalizeCategory.Notification;
        public LocalizeCategory BodyCategory { get; private set; } = LocalizeCategory.Notification;
        public Func<string> TitleResolver { get; private set; }
        public Func<string> BodyResolver { get; private set; }

        public NotificationContentTemplate WithTitleKey(string titleKey, LocalizeCategory category = LocalizeCategory.Notification)
        {
            TitleKey = titleKey;
            TitleCategory = category;
            return this;
        }

        public NotificationContentTemplate WithBodyKey(string bodyKey, LocalizeCategory category = LocalizeCategory.Notification)
        {
            BodyKey = bodyKey;
            BodyCategory = category;
            return this;
        }

        public NotificationContentTemplate WithTitleResolver(Func<string> resolver)
        {
            TitleResolver = resolver;
            return this;
        }

        public NotificationContentTemplate WithTitleText(string title)
        {
            TitleResolver = () => title ?? string.Empty;
            return this;
        }

        public NotificationContentTemplate WithBodyResolver(Func<string> resolver)
        {
            BodyResolver = resolver;
            return this;
        }

        public NotificationContentTemplate WithBodyText(string body)
        {
            BodyResolver = () => body ?? string.Empty;
            return this;
        }

        public string ResolveTitle()
        {
            if (TitleResolver != null)
            {
                return TitleResolver.Invoke() ?? string.Empty;
            }

            return string.IsNullOrEmpty(TitleKey)
                ? string.Empty
                : Localize(TitleKey, TitleCategory);
        }

        public string ResolveBody()
        {
            if (BodyResolver != null)
            {
                return BodyResolver.Invoke() ?? string.Empty;
            }

            return string.IsNullOrEmpty(BodyKey)
                ? string.Empty
                : Localize(BodyKey, BodyCategory);
        }

        // Resolves a localized string via the com.ezg.localize package, mirroring the
        // host game's GameSystems.Localize so the package stays self-contained.
        private static string Localize(string key, LocalizeCategory category)
        {
            var value = Localization.Current.Get(ToSnakeCase(category.ToString()), key);
            return string.IsNullOrEmpty(value) ? category + ": " + key : value;
        }

        private static string ToSnakeCase(string str)
        {
            return string.Concat(str.Select((x, i) => i > 0 && char.IsUpper(x) ? "_" + x.ToString() : x.ToString()))
                .ToLower();
        }
    }

    public sealed class NotificationPlatformOptions
    {
        public string AndroidChannelId { get; private set; } = "mobile_notification";
        public string AndroidChannelName { get; private set; } = "Local notification";
        public string AndroidChannelDescription { get; private set; } = "Local notification";
        public string AndroidSmallIcon { get; private set; } = "icon_1";
        public string AndroidLargeIcon { get; private set; } = "icon_0";
        public bool ShowInForeground { get; private set; }
        public string IOSCategoryIdentifier { get; private set; } = string.Empty;
        public string IOSThreadIdentifier { get; private set; } = string.Empty;

        public static NotificationPlatformOptions Default()
        {
            return new NotificationPlatformOptions();
        }

        public NotificationPlatformOptions WithAndroidChannel(string channelId, string channelName, string channelDescription)
        {
            AndroidChannelId = string.IsNullOrEmpty(channelId) ? AndroidChannelId : channelId;
            AndroidChannelName = string.IsNullOrEmpty(channelName) ? AndroidChannelName : channelName;
            AndroidChannelDescription = string.IsNullOrEmpty(channelDescription) ? AndroidChannelDescription : channelDescription;
            return this;
        }

        public NotificationPlatformOptions WithAndroidIcons(string smallIcon, string largeIcon = null)
        {
            if (!string.IsNullOrEmpty(smallIcon))
            {
                AndroidSmallIcon = smallIcon;
            }

            if (!string.IsNullOrEmpty(largeIcon))
            {
                AndroidLargeIcon = largeIcon;
            }

            return this;
        }

        public NotificationPlatformOptions WithIOSIdentifiers(string categoryIdentifier = "", string threadIdentifier = "")
        {
            IOSCategoryIdentifier = categoryIdentifier ?? string.Empty;
            IOSThreadIdentifier = threadIdentifier ?? string.Empty;
            return this;
        }

        public NotificationPlatformOptions WithForegroundPresentation(bool showInForeground)
        {
            ShowInForeground = showInForeground;
            return this;
        }
    }

    public sealed class NotificationScheduleRequest
    {
        public NotificationScheduleMode Mode { get; private set; }
        public long DelaySeconds { get; private set; }
        public DateTime FireTime { get; private set; }
        public TimeSpan? RepeatInterval { get; private set; }
        public bool CancelIfConditionFalse { get; private set; } = true;

        public static NotificationScheduleRequest CreateDelay(long delaySeconds, TimeSpan? repeatInterval = null)
        {
            return new NotificationScheduleRequest
            {
                Mode = NotificationScheduleMode.DelaySeconds,
                DelaySeconds = delaySeconds,
                RepeatInterval = repeatInterval,
            };
        }

        public static NotificationScheduleRequest CreateFireAtUtc(DateTime fireTimeUtc, TimeSpan? repeatInterval = null)
        {
            return new NotificationScheduleRequest
            {
                Mode = NotificationScheduleMode.FireAtUtc,
                FireTime = DateTime.SpecifyKind(fireTimeUtc, DateTimeKind.Utc),
                RepeatInterval = repeatInterval,
            };
        }

        public static NotificationScheduleRequest CreateFireAtLocal(DateTime fireTimeLocal, TimeSpan? repeatInterval = null)
        {
            return new NotificationScheduleRequest
            {
                Mode = NotificationScheduleMode.FireAtLocal,
                FireTime = DateTime.SpecifyKind(fireTimeLocal, DateTimeKind.Local),
                RepeatInterval = repeatInterval,
            };
        }

        public NotificationScheduleRequest WithCancelIfConditionFalse(bool shouldCancel)
        {
            CancelIfConditionFalse = shouldCancel;
            return this;
        }

        public bool IsValid()
        {
            switch (Mode)
            {
                case NotificationScheduleMode.DelaySeconds:
                    return DelaySeconds > 0;
                case NotificationScheduleMode.FireAtUtc:
                case NotificationScheduleMode.FireAtLocal:
                    return FireTime != default;
                default:
                    return false;
            }
        }
    }

    public sealed class NotificationDefinition
    {
        public string NotificationId { get; }
        public IReadOnlyList<string> TriggerEvents => _triggerEvents;
        public NotificationContentTemplate ContentTemplate { get; private set; }
        public NotificationPermissionPolicy PermissionPolicy { get; private set; } = NotificationPermissionPolicy.RequireAuthorization;
        public NotificationPlatformOptions PlatformOptions { get; private set; } = NotificationPlatformOptions.Default();
        public Func<NotificationScheduleRequest> ScheduleBuilder { get; private set; }
        public Func<bool> EnabledPredicate { get; private set; }
        public Func<NotificationPayload> PayloadFactory { get; private set; }
        public bool RefreshOnRegister { get; private set; } = true;

        private readonly List<string> _triggerEvents = new List<string>();

        public NotificationDefinition(string notificationId)
        {
            NotificationId = notificationId ?? string.Empty;
        }

        public NotificationDefinition WithTriggerEvents(params string[] triggerEvents)
        {
            _triggerEvents.Clear();
            if (triggerEvents == null)
            {
                return this;
            }

            for (int index = 0; index < triggerEvents.Length; index++)
            {
                var eventName = triggerEvents[index];
                if (!string.IsNullOrEmpty(eventName))
                {
                    _triggerEvents.Add(eventName);
                }
            }

            return this;
        }

        public NotificationDefinition WithContent(NotificationContentTemplate contentTemplate)
        {
            ContentTemplate = contentTemplate;
            return this;
        }

        public NotificationDefinition WithPermissionPolicy(NotificationPermissionPolicy permissionPolicy)
        {
            PermissionPolicy = permissionPolicy;
            return this;
        }

        public NotificationDefinition WithPlatformOptions(NotificationPlatformOptions platformOptions)
        {
            PlatformOptions = platformOptions ?? NotificationPlatformOptions.Default();
            return this;
        }

        public NotificationDefinition WithSchedule(Func<NotificationScheduleRequest> scheduleBuilder)
        {
            ScheduleBuilder = scheduleBuilder;
            return this;
        }

        public NotificationDefinition WithEnabledPredicate(Func<bool> enabledPredicate)
        {
            EnabledPredicate = enabledPredicate;
            return this;
        }

        public NotificationDefinition WithPayload(Func<NotificationPayload> payloadFactory)
        {
            PayloadFactory = payloadFactory;
            return this;
        }

        public NotificationDefinition WithRefreshOnRegister(bool refreshOnRegister)
        {
            RefreshOnRegister = refreshOnRegister;
            return this;
        }

        public bool IsEnabled()
        {
            return EnabledPredicate == null || EnabledPredicate.Invoke();
        }

        public NotificationPayload BuildPayload()
        {
            return PayloadFactory != null ? PayloadFactory.Invoke() : NotificationPayload.Create(NotificationId);
        }
    }

    public struct NotificationLaunchResult
    {
        public bool HasPayload;
        public NotificationPayload Payload;
    }

    public sealed class RuntimeNotificationRequest
    {
        public string Title { get; set; }
        public string Body { get; set; }
        public long DelaySeconds { get; set; }
        public bool Repeat { get; set; }
        public long RepeatIntervalSeconds { get; set; }
        public string[] TriggerEvents { get; set; }
        public bool RefreshOnRegister { get; set; } = true;
        public NotificationPermissionPolicy PermissionPolicy { get; set; } = NotificationPermissionPolicy.RequireAuthorization;
        public NotificationPlatformOptions PlatformOptions { get; set; } = NotificationPlatformOptions.Default();
        public NotificationPayload Payload { get; set; }
        public Func<bool> EnabledPredicate { get; set; }

        public NotificationDefinition ToDefinition(string notificationId)
        {
            var content = new NotificationContentTemplate()
                .WithTitleText(Title)
                .WithBodyText(Body);

            TimeSpan? repeatInterval = null;
            if (Repeat)
            {
                repeatInterval = TimeSpan.FromSeconds(Math.Max(1, RepeatIntervalSeconds > 0 ? RepeatIntervalSeconds : DelaySeconds));
            }

            return new NotificationDefinition(notificationId)
                .WithTriggerEvents(TriggerEvents)
                .WithContent(content)
                .WithPermissionPolicy(PermissionPolicy)
                .WithPlatformOptions(PlatformOptions)
                .WithEnabledPredicate(EnabledPredicate)
                .WithPayload(() => Payload ?? NotificationPayload.Create(notificationId))
                .WithRefreshOnRegister(RefreshOnRegister)
                .WithSchedule(() => NotificationScheduleRequest.CreateDelay(Math.Max(1, DelaySeconds), repeatInterval));
        }
    }
}
