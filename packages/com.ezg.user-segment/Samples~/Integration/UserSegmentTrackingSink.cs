using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Ezg.Tracking;
using Ezg.UserSegment;
using Firebase.Analytics;

namespace Ezg.Feature.System.UserSegment
{
    /// <summary>
    ///     Adapter ITrackingSink → TrackingService (Firebase). Lưu ý TrackingService bỏ qua số bằng -1 và chuỗi rỗng;
    ///     engine không dùng -1 làm giá trị có nghĩa nên chấp nhận. Xoá user property gọi thẳng Firebase vì
    ///     TrackingService lọc null.
    /// </summary>
    public sealed class UserSegmentTrackingSink : ITrackingSink
    {
        public void LogEvent(string name, IReadOnlyDictionary<string, object> parameters)
        {
            var dict = new Dictionary<string, object>(parameters.Count);
            foreach (var kv in parameters) dict[kv.Key] = kv.Value;
            TrackingService.SendFirebase(name, dict).Forget();
        }

        public void SetUserProperty(string name, string value)
        {
            if (!TrackingService.IsInitFirebase) return;
            if (value == null)
            {
                FirebaseAnalytics.SetUserProperty(name, null);
                return;
            }

            TrackingService.SetUserProperty(new Dictionary<string, object> { [name] = value });
        }
    }
}
