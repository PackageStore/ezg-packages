using Firebase.Extensions;
using Firebase.RemoteConfig;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace Ezg.Core.Firebase
{
    /// <summary>
    ///     Generic wrapper quanh Firebase Remote Config: init, fetch &amp; activate, rồi expose các getter
    ///     typed (<see cref="GetInt"/>/<see cref="GetBool"/>/<see cref="GetString"/>). KHÔNG chứa key hay
    ///     field game-specific — phần đọc key của từng game được xử lý ở phía game qua callback
    ///     <see cref="OnRemoteConfigApplied"/>.
    /// </summary>
    public static class FirebaseRemoteManager
    {
        #region Fields

        /// <summary>
        ///     Được invoke ngay sau khi remote config fetch &amp; activate xong (giá trị đã sẵn sàng để đọc).
        ///     Game đăng ký callback này để map các key của mình rồi emit event tuỳ ý — nhờ vậy assembly
        ///     Ezg.Core.Firebase không cần phụ thuộc ngược vào game.
        /// </summary>
        public static Action OnRemoteConfigApplied;

        public static bool isRemoteConfigInitialized;

        private static IDictionary<string, ConfigValue> _values;

        #endregion

        #region Public Methods

        /// <summary>
        ///     Khởi tạo và fetch remote config. Interval fetch tối thiểu lấy từ <see cref="FirebaseConfig"/>.
        /// </summary>
        public static void InitRemoteConfig()
        {
            isRemoteConfigInitialized = false;
            var configSettings = new ConfigSettings
            {
                MinimumFetchIntervalInMilliseconds = (ulong)FirebaseConfig.Instance.MinimumFetchIntervalMs
            };
            FirebaseRemoteConfig.DefaultInstance.SetConfigSettingsAsync(configSettings).ContinueWithOnMainThread(
                task1 =>
                {
                    Debug.Log("start Init RemoteConfig");
                    var defaults = new Dictionary<string, object>();
                    FirebaseRemoteConfig.DefaultInstance.SetDefaultsAsync(defaults)
                        .ContinueWithOnMainThread(task => { FetchDataAsync(); });
                });
        }

        /// <summary>
        ///     True nếu remote config có key này (sau khi đã fetch).
        /// </summary>
        public static bool HasKey(string key)
        {
            return _values != null && _values.ContainsKey(key);
        }

        /// <summary>
        ///     Đọc giá trị int của một key, trả về <paramref name="defaultValue"/> nếu không có / lỗi.
        /// </summary>
        public static int GetInt(string key, int defaultValue = 0)
        {
            try
            {
                if (_values != null && _values.TryGetValue(key, out var value))
                    return (int)value.DoubleValue;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[FirebaseRemoteManager] GetInt('{key}') failed: {ex.Message}");
            }

            return defaultValue;
        }

        /// <summary>
        ///     Đọc giá trị bool của một key, trả về <paramref name="defaultValue"/> nếu không có / lỗi.
        /// </summary>
        public static bool GetBool(string key, bool defaultValue = false)
        {
            try
            {
                if (_values != null && _values.TryGetValue(key, out var value))
                    return value.BooleanValue;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[FirebaseRemoteManager] GetBool('{key}') failed: {ex.Message}");
            }

            return defaultValue;
        }

        /// <summary>
        ///     Đọc giá trị string của một key, trả về <paramref name="defaultValue"/> nếu không có / lỗi.
        /// </summary>
        public static string GetString(string key, string defaultValue = "")
        {
            try
            {
                if (_values != null && _values.TryGetValue(key, out var value))
                    return value.StringValue;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[FirebaseRemoteManager] GetString('{key}') failed: {ex.Message}");
            }

            return defaultValue;
        }

        #endregion

        #region Private Methods

        private static void FetchDataAsync()
        {
            Debug.Log("Set Default finish");
            FirebaseRemoteConfig.DefaultInstance.FetchAsync(TimeSpan.Zero)
                .ContinueWithOnMainThread(FetchComplete);
            FirebaseRemoteConfig.DefaultInstance.FetchAndActivateAsync().ContinueWithOnMainThread(task => { });
        }

        private static void FetchComplete(Task fetchTask)
        {
            if (fetchTask.IsCanceled)
            {
                Debug.Log("Fetch canceled.");
            }
            else if (fetchTask.IsFaulted)
            {
                Debug.Log("Fetch encountered an error.");
            }
            else if (fetchTask.IsCompleted)
            {
                Debug.Log("Fetch completed successfully!");
            }

            var info = FirebaseRemoteConfig.DefaultInstance.Info;
            switch (info.LastFetchStatus)
            {
                case LastFetchStatus.Success:
                    FirebaseRemoteConfig.DefaultInstance.ActivateAsync().ContinueWithOnMainThread(
                        task =>
                        {
                            Debug.Log($"Remote data loaded and ready (last fetch time {info.FetchTime}).");
                            _values = FirebaseRemoteConfig.DefaultInstance.AllValues;

                            isRemoteConfigInitialized = true;
                            OnRemoteConfigApplied?.Invoke();
                            Debug.Log("Fetch remote config success!");
                        });

                    break;
                case LastFetchStatus.Failure:
                    switch (info.LastFetchFailureReason)
                    {
                        case FetchFailureReason.Error:
                            Debug.Log("Fetch failed for unknown reason");
                            break;
                        case FetchFailureReason.Throttled:
                            Debug.Log("Fetch throttled until " + info.ThrottledEndTime);
                            break;
                    }

                    break;
                case LastFetchStatus.Pending:
                    Debug.Log("Latest Fetch call still pending.");
                    break;
            }
        }

        #endregion
    }
}
