using Ezg.Feature.LocalNotification;
using Ezg.Feature.Shared.Systems;
using Ezg.Package.Localize;
using Ezg.UserSegment;

namespace Ezg.Feature.System.UserSegment
{
    /// <summary>
    ///     SCHEDULE_LOCAL_NOTIFICATION → LocalNotificationManager.RegisterOrReplace(template_id, ...). Cùng template_id
    ///     thì thay lịch cũ (§8.4). Chưa có permission → action_failed no_permission (SDK hoàn cooldown).
    /// </summary>
    public sealed class ScheduleNotificationExecutor : IActionExecutor
    {
        private const double SECONDS_PER_HOUR = 3600;
        public ActionType ActionType => ActionType.SCHEDULE_LOCAL_NOTIFICATION;

        public void Execute(ActionRequest request)
        {
            var entry = UserSegmentCatalog.Current?.FindNotification(request.Params.TemplateId);
            if (entry == null)
            {
                UnityEngine.Debug.LogWarning($"[UserSegment] ScheduleNotification: template_id '{request.Params.TemplateId}' không có trong catalog → unknown_id");
                request.ReportFailed(FailReason.UnknownId);
                return;
            }

            var status = LocalNotificationManager.GetPermissionStatus();
            if (status == NotificationPermissionStatus.Denied || status == NotificationPermissionStatus.Unavailable)
            {
                if (UnityEngine.Debug.isDebugBuild) UnityEngine.Debug.Log($"[UserSegment] ScheduleNotification #{request.ExecutionId}: permission {status} → no_permission");
                request.ReportFailed(FailReason.NoPermission);
                return;
            }

            if (UnityEngine.Debug.isDebugBuild) UnityEngine.Debug.Log($"[UserSegment] ScheduleNotification #{request.ExecutionId}: {request.Params.TemplateId} delay {request.Params.DelayH}h (permission {status})");

            var title = GameSystems.Localize(entry.titleKey, LocalizeCategory.Notification);
            var body = GameSystems.Localize(entry.bodyKey, LocalizeCategory.Notification);
            var delay = (long)(request.Params.DelayH * SECONDS_PER_HOUR);
            request.ReportPresented();
            LocalNotificationManager.RegisterOrReplace(request.Params.TemplateId, title, body, delay);
            request.ReportExecuted("scheduled");
        }
    }
}
