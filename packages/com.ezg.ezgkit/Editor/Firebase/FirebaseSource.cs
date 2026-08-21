#if UNITY_EDITOR
using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Ezg.Editor.Shared.Firebase
{
    /// <summary>
    ///     Khai báo "project Firebase nào" cho dự án này — nằm ở <c>ProjectSettings/FirebaseSource.json</c>,
    ///     cùng lý do như <c>MarketingConfig.JsonPath</c>: mỗi dự án một project Firebase riêng nên file
    ///     này KHÔNG được đi theo code-template, mà <c>ProjectSettings/</c> thì vẫn được track git.
    ///     <para>
    ///         Đường dẫn file service account key CỐ Ý không nằm ở đây (xem <see cref="KeyPath" />) —
    ///         nó là secret theo máy, không phải cấu hình dùng chung.
    ///     </para>
    /// </summary>
    [Serializable]
    internal class FirebaseSource
    {
        #region Fields

        /// <summary>Project id (KHÔNG phải project number), ví dụ <c>i001-backyard-empire</c>.</summary>
        public string projectId;

        /// <summary>Tên app hiện trong Firebase console. Rỗng = lấy theo productName.</summary>
        public string androidDisplayName;

        public string iosDisplayName;

        /// <summary>Apple ID số của app (tuỳ chọn, để Firebase link sang App Store).</summary>
        public string appStoreId;

        /// <summary>
        ///     Bật thì tool tự tạo GCP project + bật Firebase khi <see cref="projectId" /> chưa tồn tại.
        ///     Mặc định TẮT: project id là vĩnh viễn, không xoá sạch được, nên không tạo ngầm.
        /// </summary>
        public bool createProjectIfMissing;

        #endregion

        #region Paths

        internal static string JsonPath =>
            Path.Combine(Path.GetFullPath(Path.Combine(Application.dataPath, "..")),
                "ProjectSettings/FirebaseSource.json");

        /// <summary>
        ///     Đường dẫn file key: EditorPrefs theo từng project trên máy này, fallback sang biến môi
        ///     trường <c>GOOGLE_APPLICATION_CREDENTIALS</c> (chuẩn Google, máy đã dùng gcloud là có sẵn).
        /// </summary>
        internal static string KeyPath
        {
            get
            {
                var stored = EditorPrefs.GetString(KeyPathPref, string.Empty);
                if (!string.IsNullOrEmpty(stored)) return stored;

                return Environment.GetEnvironmentVariable("GOOGLE_APPLICATION_CREDENTIALS") ?? string.Empty;
            }
            set => EditorPrefs.SetString(KeyPathPref, value ?? string.Empty);
        }

        private static string KeyPathPref => "Ezg.Firebase.KeyPath:" + Application.dataPath;

        #endregion

        #region Load / Save

        internal static FirebaseSource Load()
        {
            if (!File.Exists(JsonPath)) return new FirebaseSource();

            try
            {
                return JsonUtility.FromJson<FirebaseSource>(File.ReadAllText(JsonPath))
                       ?? new FirebaseSource();
            }
            catch (Exception exception)
            {
                Debug.LogError($"[Firebase] FirebaseSource.json hong: {exception.Message}");
                return new FirebaseSource();
            }
        }

        internal void Save() =>
            File.WriteAllText(JsonPath, JsonUtility.ToJson(this, true), new UTF8Encoding(false));

        #endregion

        #region Derived

        internal string ResolvedAndroidName => string.IsNullOrEmpty(androidDisplayName)
            ? PlayerSettings.productName + " (Android)"
            : androidDisplayName;

        internal string ResolvedIosName => string.IsNullOrEmpty(iosDisplayName)
            ? PlayerSettings.productName + " (iOS)"
            : iosDisplayName;

        #endregion
    }
}
#endif
