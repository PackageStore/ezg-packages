using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using TigerForge;
using UnityEngine;
using UnityEngine.Events;

namespace Ezg.Feature.LocalNotification
{
    public static class LocalNotificationService
    {
        private sealed class NotificationRegistration
        {
            public NotificationDefinition Definition;
            public readonly Dictionary<string, UnityAction> EventBindings = new Dictionary<string, UnityAction>();
        }

        private static readonly Dictionary<string, NotificationRegistration> Registry = new Dictionary<string, NotificationRegistration>();

        private static INotificationPlatformProvider _provider;
        private static INotificationPermissionService _permissionService;
        private static bool _initialized;
        private static NotificationPayload _pendingLaunchPayload;
        private static string _lastLaunchPayloadFingerprint;

        /// <summary>
        ///     Raised when the application is sent to the background. Project-side rule code
        ///     subscribes here to (re)register pause-driven notifications, so the package never
        ///     references game-specific scheduling logic. Wire it up from your project, e.g.
        ///     <c>LocalNotificationService.AppPaused += LocalNotificationRules.RegisterPauseNotifications;</c>
        /// </summary>
        public static event Action AppPaused;

        public static void Initialize()
        {
            if (_initialized)
            {
                EnsureRuntime();
                CaptureLaunchPayloadAsync().Forget();
                return;
            }

            _provider = CreatePlatformProvider();
            _permissionService = CreatePermissionService();
            _permissionService.StatusChanged += OnPermissionStatusChanged;

            _provider.Initialize();
            _permissionService.Initialize();
            _permissionService.RequestPermissionIfNeeded();

            EnsureRuntime();
            CaptureLaunchPayloadAsync().Forget();

            _initialized = true;
            Debug.Log("[LocalNotifications] Initialized.");
        }

        public static void RegisterOrReplace(string notificationId, NotificationDefinition definition)
        {
            if (string.IsNullOrEmpty(notificationId) || definition == null)
            {
                Debug.LogWarning("[LocalNotifications] Skip register. NotificationId or definition is invalid.");
                return;
            }

            Initialize();
            Stop(notificationId);

            var registration = new NotificationRegistration
            {
                Definition = definition,
            };

            BindEvents(notificationId, registration);
            Registry[notificationId] = registration;

            Debug.Log($"[LocalNotifications] Registered {notificationId} with {registration.EventBindings.Count} trigger(s). RefreshOnRegister={definition.RefreshOnRegister}.");

            if (definition.RefreshOnRegister)
            {
                Debug.Log($"[LocalNotifications] Register stage complete for {notificationId}. Triggering immediate refresh.");
                Refresh(notificationId);
            }
        }

        public static void RegisterOrReplace(string notificationId, RuntimeNotificationRequest request)
        {
            if (request == null)
            {
                Debug.LogWarning($"[LocalNotifications] Skip register for {notificationId}. Runtime request is null.");
                return;
            }

            RegisterOrReplace(notificationId, request.ToDefinition(notificationId));
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
            RegisterOrReplace(notificationId, new RuntimeNotificationRequest
            {
                Title = title,
                Body = content,
                DelaySeconds = delaySeconds,
                Repeat = loop,
                RepeatIntervalSeconds = loopDurationSeconds,
                TriggerEvents = triggerEvents,
                Payload = payload,
                PlatformOptions = platformOptions ?? NotificationPlatformOptions.Default(),
            });
        }

        public static void Refresh(string notificationId)
        {
            Debug.Log($"[LocalNotifications] Refresh requested for {notificationId}.");
            if (!Registry.TryGetValue(notificationId, out var registration))
            {
                Debug.LogWarning($"[LocalNotifications] Refresh ignored. {notificationId} is not registered.");
                return;
            }

            var definition = registration.Definition;
            if (definition == null)
            {
                Debug.LogWarning($"[LocalNotifications] Refresh ignored. {notificationId} definition is missing.");
                return;
            }

            if (definition.PermissionPolicy == NotificationPermissionPolicy.RequireAuthorization &&
                _permissionService.CurrentStatus != NotificationPermissionStatus.Granted)
            {
                if (_permissionService.CurrentStatus == NotificationPermissionStatus.Unknown)
                {
                    Debug.Log($"[LocalNotifications] Permission unknown during refresh for {notificationId}. Requesting permission.");
                    _permissionService.RequestPermissionIfNeeded();
                }

                _provider.Cancel(notificationId);
                Debug.Log($"[LocalNotifications] Refresh skipped for {notificationId}. Permission={_permissionService.CurrentStatus}.");
                return;
            }

            if (!definition.IsEnabled())
            {
                _provider.Cancel(notificationId);
                Debug.Log($"[LocalNotifications] Cancelled {notificationId}. Enabled predicate returned false.");
                return;
            }

            var schedule = definition.ScheduleBuilder != null ? definition.ScheduleBuilder.Invoke() : null;
            if (schedule == null || !schedule.IsValid())
            {
                _provider.Cancel(notificationId);
                Debug.LogWarning($"[LocalNotifications] Cancelled {notificationId}. Schedule is invalid.");
                return;
            }

            Debug.Log(
                $"[LocalNotifications] Refresh passed for {notificationId}. " +
                $"ScheduleMode={schedule.Mode}, DelaySeconds={schedule.DelaySeconds}, FireTime={schedule.FireTime:yyyy-MM-dd HH:mm:ss}, " +
                $"RepeatInterval={(schedule.RepeatInterval.HasValue ? schedule.RepeatInterval.Value.ToString() : "none")}.");

            _provider.ScheduleOrUpdate(
                notificationId,
                schedule,
                definition.ContentTemplate ?? new NotificationContentTemplate(),
                definition.BuildPayload(),
                definition.PlatformOptions ?? NotificationPlatformOptions.Default());

            Debug.Log($"[LocalNotifications] Scheduled {notificationId}. Mode={schedule.Mode}.");
        }

        public static void Stop(string notificationId)
        {
            if (string.IsNullOrEmpty(notificationId))
            {
                return;
            }

            if (!Registry.TryGetValue(notificationId, out var registration))
            {
                _provider?.Cancel(notificationId);
                return;
            }

            foreach (var pair in registration.EventBindings)
            {
                EventManager.StopListening(pair.Key, pair.Value);
            }

            registration.EventBindings.Clear();
            _provider.Cancel(notificationId);
            Registry.Remove(notificationId);

            Debug.Log($"[LocalNotifications] Stopped {notificationId}.");
        }

        public static NotificationPermissionStatus RefreshPermissionStatus()
        {
            Initialize();
            _permissionService?.Initialize();
            return _permissionService?.CurrentStatus ?? NotificationPermissionStatus.Unavailable;
        }

        public static NotificationPermissionStatus GetPermissionStatus()
        {
            Initialize();
            return _permissionService?.CurrentStatus ?? NotificationPermissionStatus.Unavailable;
        }

        public static bool OpenAppNotificationSettings()
        {
            Initialize();
            return _permissionService != null && _permissionService.OpenAppNotificationSettings();
        }

        public static void StopAll()
        {
            var ids = new List<string>(Registry.Keys);
            for (int index = 0; index < ids.Count; index++)
            {
                Stop(ids[index]);
            }
        }

        public static bool TryConsumeLaunchPayload(out NotificationPayload payload)
        {
            payload = _pendingLaunchPayload;
            _pendingLaunchPayload = null;
            return payload != null;
        }

        public static string DumpActiveRegistry()
        {
            if (Registry.Count == 0)
            {
                return "[LocalNotifications] Registry is empty.";
            }

            var lines = new List<string>(Registry.Count + 1)
            {
                "[LocalNotifications] Active registrations:",
            };

            foreach (var pair in Registry)
            {
                lines.Add($"- {pair.Key}: triggers={pair.Value.EventBindings.Count}");
            }

            return string.Join("\n", lines);
        }

        internal static void NotifyAppForegrounded()
        {
            _permissionService?.Initialize();
            CaptureLaunchPayloadAsync().Forget();
        }

        internal static void NotifyAppPaused()
        {
            Debug.Log("[LocalNotifications] App paused. Raising AppPaused hook.");
            AppPaused?.Invoke();
        }

        private static void BindEvents(string notificationId, NotificationRegistration registration)
        {
            var triggerEvents = registration.Definition.TriggerEvents;
            for (int index = 0; index < triggerEvents.Count; index++)
            {
                var eventName = triggerEvents[index];
                UnityAction callback = () => Refresh(notificationId);
                registration.EventBindings[eventName] = callback;
                EventManager.StartListening(eventName, callback);
                Debug.Log($"[LocalNotifications] Bound trigger event '{eventName}' to {notificationId}.");
            }
        }

        private static async UniTaskVoid CaptureLaunchPayloadAsync()
        {
            if (_provider == null)
            {
                return;
            }

            var result = await _provider.CaptureLastRespondedNotificationAsync();
            if (!result.HasPayload || result.Payload == null)
            {
                return;
            }

            var fingerprint = GetPayloadFingerprint(result.Payload);
            if (_lastLaunchPayloadFingerprint == fingerprint)
            {
                return;
            }

            _lastLaunchPayloadFingerprint = fingerprint;
            _pendingLaunchPayload = result.Payload;
            Debug.Log($"[LocalNotifications] Captured launch payload for {_pendingLaunchPayload.Key}.");
        }

        private static void OnPermissionStatusChanged(NotificationPermissionStatus status)
        {
            Debug.Log($"[LocalNotifications] Permission status changed: {status}.");
            if (status != NotificationPermissionStatus.Granted)
            {
                if (status == NotificationPermissionStatus.Denied)
                {
                    var ids = new List<string>(Registry.Keys);
                    for (int index = 0; index < ids.Count; index++)
                    {
                        _provider.Cancel(ids[index]);
                    }
                }

                return;
            }

            var registeredIds = new List<string>(Registry.Keys);
            for (int index = 0; index < registeredIds.Count; index++)
            {
                Refresh(registeredIds[index]);
            }
        }

        private static void EnsureRuntime()
        {
            if (UnityEngine.Object.FindObjectOfType<LocalNotificationRuntime>() != null)
            {
                return;
            }

            var runtimeObject = new GameObject("[LocalNotificationRuntime]");
            runtimeObject.hideFlags = HideFlags.HideInHierarchy;
            UnityEngine.Object.DontDestroyOnLoad(runtimeObject);
            runtimeObject.AddComponent<LocalNotificationRuntime>();
        }

        private static INotificationPlatformProvider CreatePlatformProvider()
        {
#if UNITY_ANDROID
            return new AndroidNotificationPlatformProvider();
#elif UNITY_IOS
            return new IOSNotificationPlatformProvider();
#else
            return new UnsupportedNotificationPlatformProvider();
#endif
        }

        private static INotificationPermissionService CreatePermissionService()
        {
#if UNITY_ANDROID
            return new AndroidNotificationPermissionService();
#elif UNITY_IOS
            return new IOSNotificationPermissionService();
#else
            return new UnsupportedNotificationPermissionService();
#endif
        }

        private static string GetPayloadFingerprint(NotificationPayload payload)
        {
            return payload == null
                ? string.Empty
                : $"{payload.Key}|{payload.Route}|{payload.Data}";
        }
    }

    public sealed class LocalNotificationRuntime : MonoBehaviour
    {
        private void Start()
        {
            LocalNotificationService.NotifyAppForegrounded();
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (hasFocus)
            {
                LocalNotificationService.NotifyAppForegrounded();
            }
        }

        private void OnApplicationPause(bool pauseStatus)
        {
            if (pauseStatus)
            {
                LocalNotificationService.NotifyAppPaused();
                return;
            }

            LocalNotificationService.NotifyAppForegrounded();
        }
    }
}
