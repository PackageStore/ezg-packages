#if UNITY_EDITOR
using System;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace Ezg.Editor.Shared.Iap
{
    /// <summary>
    ///     Bản lưu của lượt xác minh store gần nhất, để mở tab lần sau THẤY NGAY kết quả cũ mà không gọi
    ///     mạng — và để biết "lần cuối ai kiểm là lúc nào".
    ///     <para>
    ///         <b>Nằm trong <c>Library/</c></b>: dữ liệu này dựng lại được bằng một cú bấm, theo từng máy,
    ///         và đổi mỗi lần kiểm — để trong project là nhét vào git một file rác thay đổi liên tục.
    ///     </para>
    ///     <para>
    ///         <b>Khoá theo API key.</b> Dự án nằm trong chính chuỗi key, nên cache của key khác là danh
    ///         mục của DỰ ÁN khác: dùng lại là báo sai toàn bộ. Đổi key → cache cũ bị bỏ qua (và tab nói
    ///         rõ là đã bỏ qua, chứ không im lặng). Chưa có key thì vẫn cho xem bản lưu — không kiểm lại
    ///         được nhưng thông tin cũ vẫn là thông tin.
    ///     </para>
    /// </summary>
    internal static class IapStoreCache
    {
        #region Constants

        private const string DIR = "Library/EzgKit";
        private const string NAME = "IapStoreCache.json";

        #endregion

        #region Dto

        [Serializable]
        private class Dto
        {
            public string keyHint;
            public string project;
            public string checkedAt;
            public string keyExpiresAt;
            public string savedAtUtc;
            public Row[] rows;
            public Platform[] platforms;
        }

        [Serializable]
        private class Row
        {
            public string platform;
            public string productId;
            public int state;
            public string storeStatus;
        }

        [Serializable]
        private class Platform
        {
            public string platform;
            public bool ok;
            public string checkedAt;
            public bool stale;
            public string error;
        }

        #endregion

        #region Paths

        internal static string RelativePath => DIR + "/" + NAME;

        private static string ProjectRoot => Path.GetDirectoryName(Application.dataPath) ?? string.Empty;

        private static string Absolute => Path.Combine(ProjectRoot, DIR, NAME);

        internal static bool Exists => File.Exists(Absolute);

        #endregion

        #region Load / Save

        /// <summary>
        ///     Bản lưu gần nhất, hoặc null khi chưa có / hỏng / của key khác.
        ///     <paramref name="ignored" /> khác null = CÓ file nhưng cố ý không dùng, kèm lý do để tab nói
        ///     ra — "không thấy gì" và "thấy nhưng bỏ" phải đọc khác nhau.
        /// </summary>
        internal static IapStoreSnapshot Load(out string ignored)
        {
            ignored = null;
            var file = Absolute;
            if (!File.Exists(file)) return null;

            Dto dto;
            try
            {
                dto = JsonUtility.FromJson<Dto>(File.ReadAllText(file));
            }
            catch (Exception e)
            {
                ignored = $"Bỏ qua cache store ({RelativePath}): {e.Message}. Bấm \"Kiểm tra trên store\" để dựng lại.";
                return null;
            }

            if (dto == null)
            {
                ignored = $"Bỏ qua cache store ({RelativePath}): nội dung không phải JSON của cache này.";
                return null;
            }

            var currentKey = IapVerifyConfig.ApiKeyHint;
            if (!string.IsNullOrEmpty(currentKey) && !string.IsNullOrEmpty(dto.keyHint)
                                                  && !string.Equals(currentKey, dto.keyHint, StringComparison.Ordinal))
            {
                ignored = $"Bỏ qua cache store: nó thuộc API key `{dto.keyHint}` (dự án \"{dto.project}\"), "
                          + $"còn key đang dùng là `{currentKey}`. Bấm \"Kiểm tra trên store\" để đọc lại "
                          + "bằng key hiện tại.";
                return null;
            }

            var snapshot = new IapStoreSnapshot
            {
                Project = dto.project ?? string.Empty,
                CheckedAt = dto.checkedAt ?? string.Empty,
                KeyExpiresAt = dto.keyExpiresAt ?? string.Empty,
                FromCache = true,
                FetchedAtLocal = ParseUtc(dto.savedAtUtc),
            };

            if (dto.rows != null)
                foreach (var row in dto.rows)
                {
                    if (row == null || string.IsNullOrEmpty(row.productId)) continue;

                    snapshot.Rows.Add(new IapStoreRow
                    {
                        Platform = row.platform ?? string.Empty,
                        ProductId = row.productId,
                        State = (IapStoreState)row.state,
                        StoreStatus = row.storeStatus ?? string.Empty,
                    });
                }

            if (dto.platforms != null)
                foreach (var platform in dto.platforms)
                {
                    if (platform == null) continue;

                    snapshot.Platforms.Add(new IapStorePlatform
                    {
                        Platform = platform.platform ?? string.Empty,
                        Ok = platform.ok,
                        CheckedAt = platform.checkedAt ?? string.Empty,
                        Stale = platform.stale,
                        Error = platform.error ?? string.Empty,
                    });
                }

            snapshot.Index();
            return snapshot;
        }

        /// <summary>Chỉ lưu lượt gọi THÀNH CÔNG: cache một câu lỗi thì lần mở sau chỉ báo lại lỗi cũ.</summary>
        internal static void Save(IapStoreSnapshot snapshot)
        {
            if (snapshot == null || !snapshot.IsValid) return;

            var dto = new Dto
            {
                keyHint = IapVerifyConfig.ApiKeyHint,
                project = snapshot.Project,
                checkedAt = snapshot.CheckedAt,
                keyExpiresAt = snapshot.KeyExpiresAt,
                savedAtUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                rows = new Row[snapshot.Rows.Count],
                platforms = new Platform[snapshot.Platforms.Count],
            };

            for (var i = 0; i < snapshot.Rows.Count; i++)
            {
                var row = snapshot.Rows[i];
                dto.rows[i] = new Row
                {
                    platform = row.Platform,
                    productId = row.ProductId,
                    state = (int)row.State,
                    storeStatus = row.StoreStatus,
                };
            }

            for (var i = 0; i < snapshot.Platforms.Count; i++)
            {
                var platform = snapshot.Platforms[i];
                dto.platforms[i] = new Platform
                {
                    platform = platform.Platform,
                    ok = platform.Ok,
                    checkedAt = platform.CheckedAt,
                    stale = platform.Stale,
                    error = platform.Error,
                };
            }

            try
            {
                var file = Absolute;
                Directory.CreateDirectory(Path.GetDirectoryName(file) ?? Path.Combine(ProjectRoot, DIR));
                File.WriteAllText(file, JsonUtility.ToJson(dto, true));
            }
            catch (Exception e)
            {
                // Không ghi được cache thì tab vẫn chạy bằng snapshot trong RAM — không đáng làm hỏng
                // lượt xác minh vừa thành công.
                Debug.LogWarning($"[EzgKit] Không lưu được {RelativePath}: {e.Message}");
            }
        }

        internal static void Clear()
        {
            try
            {
                if (File.Exists(Absolute)) File.Delete(Absolute);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[EzgKit] Không xoá được {RelativePath}: {e.Message}");
            }
        }

        private static DateTime ParseUtc(string value)
        {
            if (!string.IsNullOrEmpty(value)
                && DateTime.TryParse(value, CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var utc))
                return utc.ToLocalTime();

            return DateTime.Now;
        }

        #endregion
    }
}
#endif
