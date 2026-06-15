using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Firebase.Extensions;
using Firebase.Storage;
using Newtonsoft.Json;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Events;

namespace Ezg.Core.Firebase
{
    public class FirebaseStorageManager : ISyncData
    {
        private static readonly string PlayerDataUrl =
            $"{FirebaseConfig.Instance.StorageBucketUrl}/{FirebaseConfig.Instance.PlayerDataFolder}";
        private static readonly long MaxAllowedSize = FirebaseConfig.Instance.MaxDownloadSizeBytes;

        private static FirebaseStorage StorageRoot;
        private static StorageReference StoragePlayersData;

        private DateTime _createDate = DateTime.MinValue;

        private static JsonSerializerSettings _jsonConvertSettings;

        public static JsonSerializerSettings JsonConvertSettings
        {
            get
            {
                if (_jsonConvertSettings == null)
                {
                    JsonConvertSettings = new JsonSerializerSettings
                    {
                        TypeNameHandling = TypeNameHandling.All
                    };
                }

                return _jsonConvertSettings;
            }
            set => _jsonConvertSettings = value;
        }

        static FirebaseStorageManager()
        {
            StorageRoot = FirebaseStorage.DefaultInstance;
            StoragePlayersData =
                StorageRoot.GetReferenceFromUrl(PlayerDataUrl);
        }

        /// <summary>
        /// Đẩy data lên server
        /// </summary>
        /// <param name="data"></param>
        /// <param name="fileName"></param>
        /// <param name="failedAction"></param>
        /// <param name="successAction"></param>
        /// <returns></returns>
        public async UniTask PushPlayerData(string data, string fileName, UnityAction failedAction,
            UnityAction successAction)
        {
            var customBytes = System.Text.Encoding.UTF8.GetBytes(data);
            StorageReference riversRef = StoragePlayersData.Child(fileName);
            Debug.Log(PlayerDataUrl + "/" + fileName);
            await riversRef.PutBytesAsync(customBytes)
                .ContinueWithOnMainThread((System.Threading.Tasks.Task<StorageMetadata> task) =>
                {
                    if (task.IsFaulted)
                    {
                        failedAction?.Invoke();
                        Debug.LogWarning(task.Exception.ToString() + "Push Data Failed");
                    }
                    else
                    {
                        // Lưu thời điểm push từ metadata server trả về
                        _createDate = task.Result.UpdatedTimeMillis.ToLocalTime();
                        successAction?.Invoke();
                        Debug.Log("Push Data Successfully");
                    }
                });
        }

        public async UniTask DeleteData(string fileName, UnityAction failedAction,
            UnityAction successAction)
        {
            StorageReference desertRef = StoragePlayersData.Child(fileName);

            desertRef.DeleteAsync().ContinueWithOnMainThread(task =>
            {
                if (task.IsCompleted)
                {
                    successAction?.Invoke();
                }
                else
                {
                    failedAction?.Invoke();
                }
            });
        }

        /// <summary>
        /// Lấy dữ liệu player
        /// </summary>
        /// <param name="isReloadScene"></param>
        /// <returns></returns>
        public async UniTask<byte[]> GetPlayerData(string fileName)
        {
            Debug.Log(PlayerDataUrl + "/" + fileName);

            var reference = StorageRoot.GetReferenceFromUrl(PlayerDataUrl + "/" + fileName);

            // Lấy metadata để đọc thời điểm data được push lên
            await reference.GetMetadataAsync().ContinueWithOnMainThread(task =>
            {
                if (!task.IsFaulted && !task.IsCanceled)
                    _createDate = task.Result.UpdatedTimeMillis.ToLocalTime();
            });

            return await reference.GetBytesAsync(MaxAllowedSize).ContinueWithOnMainThread(OnGetBytesAsync);
        }

        public DateTime GetTimeCreateData()
        {
            return _createDate;
        }

        public byte[] OnGetBytesAsync(Task<byte[]> task)
        {
            if (task.IsFaulted)
            {
                Debug.Log("Get player data failed");
                return null;
            }

            if (task.IsCanceled)
            {
                return null;
            }

            return task.Result;
        }
    }
}
