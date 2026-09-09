#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using Ezg.Editor.Shared.EzgKit;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Networking;

namespace Ezg.Editor.Shared.Iap
{
    /// <summary>
    ///     Trạng thái của MỘT (nền tảng, productId) trên store, sau khi đã đối chiếu danh mục server
    ///     trả về với SKU client đăng ký.
    /// </summary>
    internal enum IapStoreState
    {
        /// <summary>Chưa hỏi server, hoặc nền tảng này server ĐỌC HỤT — chưa biết gì, KHÔNG phải "chưa tạo".</summary>
        Unknown = 0,

        /// <summary>Có trên store, setup đủ, bán được (<c>ready</c>).</summary>
        Ready = 1,

        /// <summary>Đã tạo nhưng còn thiếu metadata / còn nháp / chưa có base plan (<c>incomplete</c>).</summary>
        Incomplete = 2,

        /// <summary>Đủ nhưng đang tắt / đã gỡ khỏi bán (<c>inactive</c>).</summary>
        Inactive = 3,

        /// <summary>Nền tảng đọc được mà danh mục KHÔNG có id này → chưa tạo trên store.</summary>
        Missing = 4,
    }

    /// <summary>
    ///     Một dòng danh mục store, đã quy về đúng một dòng cho mỗi (nền tảng, productId) bởi server.
    /// </summary>
    internal sealed class IapStoreRow
    {
        internal string Platform;
        internal string ProductId;
        internal IapStoreState State;

        /// <summary>Chuỗi thô của store (<c>APPROVED</c>, <c>MISSING_METADATA</c>, <c>ACTIVE</c>…).</summary>
        internal string StoreStatus;

        internal string Key => IapPriceSheet.RowKey(Platform, ProductId);
    }

    /// <summary>Một nền tảng trong response — server đọc được dữ liệu thật của nó hay không.</summary>
    internal sealed class IapStorePlatform
    {
        internal string Platform;

        /// <summary>
        ///     <c>false</c> = KHÔNG đọc được nền tảng này. Danh mục không nói gì về nó, nên tuyệt đối
        ///     không được hiểu là "chưa tạo gói nào" — xem <see cref="IapStoreSnapshot.IsUsable" />.
        /// </summary>
        internal bool Ok;

        internal string CheckedAt;

        /// <summary>Đang phục vụ bản lưu vì lượt đọc mới nhất hỏng — dữ liệu dùng được, chỉ là cũ.</summary>
        internal bool Stale;

        internal string Error;
    }

    /// <summary>
    ///     Kết quả MỘT lượt hỏi server, đóng băng lại. Page chỉ đọc — không hỏi lại giữa lượt vẽ.
    /// </summary>
    internal sealed class IapStoreSnapshot
    {
        #region Fields

        /// <summary>Mã dự án mà API key thuộc về (field <c>project</c> của response).</summary>
        internal string Project;

        /// <summary>Mốc server đọc store, ISO-8601 UTC. Rỗng khi lượt gọi thất bại.</summary>
        internal string CheckedAt;

        /// <summary>Hạn của API key, ISO-8601. Rỗng = key không có hạn.</summary>
        internal string KeyExpiresAt;

        internal readonly List<IapStoreRow> Rows = new();
        internal readonly List<IapStorePlatform> Platforms = new();

        /// <summary>Câu lỗi để hiện thẳng cho dev. Null = lượt gọi thành công.</summary>
        internal string Error;

        /// <summary>Mã HTTP — 401/403/404/503 mỗi mã một cách xử lý khác nhau (xem doc của API).</summary>
        internal long HttpCode;

        /// <summary>Mốc máy này nhận được kết quả — để nói "đọc cách đây N phút".</summary>
        internal DateTime FetchedAtLocal = DateTime.Now;

        private readonly Dictionary<string, IapStoreRow> _byKey = new();

        #endregion

        #region Query

        internal bool IsValid => Error == null;

        /// <summary>Số nền tảng server đọc được — 0 nghĩa là chưa biết gì cả (API trả 503).</summary>
        internal int UsablePlatformCount
        {
            get
            {
                var count = 0;
                foreach (var platform in Platforms)
                    if (platform.Ok) count++;
                return count;
            }
        }

        internal bool AnyStale
        {
            get
            {
                foreach (var platform in Platforms)
                    if (platform.Ok && platform.Stale) return true;
                return false;
            }
        }

        /// <summary>
        ///     Nền tảng này có dữ liệu thật để đối chiếu không. CHẶN ĐÚNG cái bẫy lớn nhất của API:
        ///     Apple lỗi 5 phút → danh mục ios rỗng → tool báo mọi gói ios "chưa tạo" → dev đi tạo lại
        ///     → store sinh một loạt gói trùng (thứ không xoá dễ).
        /// </summary>
        internal bool IsUsable(string platform)
        {
            foreach (var row in Platforms)
                if (string.Equals(row.Platform, platform, StringComparison.OrdinalIgnoreCase))
                    return row.Ok;

            return false;
        }

        /// <summary>
        ///     Trạng thái của một (nền tảng, productId). Nền tảng đọc hụt → <see cref="IapStoreState.Unknown" />;
        ///     đọc được mà không có id → <see cref="IapStoreState.Missing" />.
        /// </summary>
        internal IapStoreState StateOf(string platform, string productId)
        {
            if (!IsUsable(platform)) return IapStoreState.Unknown;

            return _byKey.TryGetValue(IapPriceSheet.RowKey(platform, productId), out var row)
                ? row.State
                : IapStoreState.Missing;
        }

        internal IapStoreRow RowOf(string platform, string productId) =>
            _byKey.TryGetValue(IapPriceSheet.RowKey(platform, productId), out var row) ? row : null;

        /// <summary>Key sắp hết hạn (14 ngày) — xin cấp lại trước khi tool im lặng ngừng chạy.</summary>
        internal bool KeyExpiringSoon(out int daysLeft)
        {
            daysLeft = 0;
            if (string.IsNullOrEmpty(KeyExpiresAt)) return false;
            if (!DateTime.TryParse(KeyExpiresAt, CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var expires))
                return false;

            var left = (expires - DateTime.UtcNow).TotalDays;
            daysLeft = (int)Math.Floor(left);
            return left <= 14d;
        }

        /// <summary>Mốc đọc store dưới dạng chữ người đọc được. Rỗng thì trả null.</summary>
        internal string CheckedAtLabel
        {
            get
            {
                if (string.IsNullOrEmpty(CheckedAt)) return null;
                if (!DateTime.TryParse(CheckedAt, CultureInfo.InvariantCulture,
                        DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var utc))
                    return CheckedAt;

                return utc.ToLocalTime().ToString("dd/MM HH:mm", CultureInfo.InvariantCulture);
            }
        }

        internal void Index()
        {
            _byKey.Clear();
            foreach (var row in Rows) _byKey[row.Key] = row;
        }

        #endregion
    }

    /// <summary>
    ///     Gọi API chỉ-đọc của server nội bộ (<c>project-ezg</c>) để biết gói bán đã có THẬT trên
    ///     App Store Connect / Google Play chưa — thứ mà Unity không có cách nào tự thấy.
    ///     <para>
    ///         <b>Bất đồng bộ qua <see cref="EditorApplication.update" /></b>, không phải đồng bộ +
    ///         progress bar như <c>FirebaseRest</c>: lượt này chỉ ĐỌC, không ghi file nào sau đó, nên
    ///         không có lý do gì để chặn Editor. Kết quả về thì chỉ bắn callback; page tự đổ vào
    ///         snapshot ở ĐẦU lượt Draw kế tiếp (không đổi dữ liệu giữa Layout và Repaint).
    ///     </para>
    ///     <para>
    ///         <b>Không có nút "đọc lại ngay":</b> server đệm 60 giây và cố ý không nhận tham số ép
    ///         đọc lại — một tool lặp vô hạn sẽ đốt hạn mức API của cả team. Vừa tạo gói trên console
    ///         thì đợi tối đa một phút.
    ///     </para>
    /// </summary>
    internal sealed class IapStoreVerifier
    {
        #region Constants

        private const int TIMEOUT_SECONDS = 30;

        /// <summary>Cắt body lỗi lạ (HTML của proxy / captive portal) cho vừa một dòng GUI.</summary>
        private const int ERROR_BODY_MAX = 300;

        #endregion

        #region Dto

        // JsonUtility gán mấy field dưới đây qua reflection nên compiler không thấy chỗ nào assign.
#pragma warning disable CS0649

        /// <summary>Field đặt tên khớp KEY JSON của server (yêu cầu của <c>JsonUtility</c>).</summary>
        [Serializable]
        private class Response
        {
            public string project;
            public string checkedAt;
            public string keyExpiresAt;
            public int count;
            public Product[] products;
            public PlatformInfo[] platforms;
        }

        [Serializable]
        private class Product
        {
            public string productId;
            public string platform;
            public string status;
            public string storeStatus;
        }

        [Serializable]
        private class PlatformInfo
        {
            public string platform;
            public bool ok;
            public string checkedAt;
            public bool stale;
            public string error;
        }

        /// <summary>Mọi mã lỗi của API đều trả <c>{ "error": "câu tiếng Việt nói rõ phải làm gì" }</c>.</summary>
        [Serializable]
        private class ErrorResponse
        {
            public string error;
            public string code;
            public string detail;
            public PlatformInfo[] platforms;
        }

#pragma warning restore CS0649

        #endregion

        #region State

        private UnityWebRequest _request;
        private Action<IapStoreSnapshot> _onDone;

        internal bool IsRunning => _request != null;

        #endregion

        #region Fetch

        /// <summary>
        ///     Bắt đầu một lượt hỏi. Bỏ qua nếu đang có lượt chạy — hai lượt song song chỉ nhận về
        ///     đúng bản đệm 60 giây của server, mà callback thứ hai lại ghi đè snapshot vừa dựng.
        /// </summary>
        internal void Fetch(string url, string apiKey, Action<IapStoreSnapshot> onDone)
        {
            if (IsRunning) return;

            if (string.IsNullOrWhiteSpace(apiKey))
            {
                onDone?.Invoke(new IapStoreSnapshot
                {
                    Error = "Chưa có API key. Xin ở project-ezg → Dự án → tab API key, rồi dán vào ô bên dưới.",
                });
                return;
            }

            _onDone = onDone;
            _request = UnityWebRequest.Get(url);
            // Key đi trong HEADER, không đi trong query string: query string bị ghi vào access log
            // của server, vào lịch sử trình duyệt và vào mọi proxy trên đường.
            _request.SetRequestHeader("Authorization", "Bearer " + apiKey.Trim());
            _request.timeout = TIMEOUT_SECONDS;
            _request.SendWebRequest();
            EditorApplication.update += Poll;
        }

        /// <summary>Huỷ lượt đang chạy (đóng tab / bấm lại). Không bắn callback.</summary>
        internal void Cancel()
        {
            if (_request == null) return;

            EditorApplication.update -= Poll;
            _request.Abort();
            _request.Dispose();
            _request = null;
            _onDone = null;
        }

        private void Poll()
        {
            if (_request == null)
            {
                EditorApplication.update -= Poll;
                return;
            }

            if (!_request.isDone) return;
            EditorApplication.update -= Poll;

            IapStoreSnapshot snapshot;
            try
            {
                snapshot = Parse(_request.responseCode, _request.result, _request.error,
                    _request.downloadHandler?.text);
            }
            catch (Exception exception)
            {
                // Body không phải JSON như mong đợi — đây là lỗi cấu hình/hạ tầng, không phải
                // "gói chưa tạo". Vẫn phải về dưới dạng snapshot lỗi để page không kết luận sai.
                Debug.LogException(exception);
                snapshot = new IapStoreSnapshot { Error = "Không đọc được response: " + exception.Message };
            }
            finally
            {
                _request.Dispose();
                _request = null;
            }

            var callback = _onDone;
            _onDone = null;
            callback?.Invoke(snapshot);
            InternalEditorUtility.RepaintAllViews();
        }

        #endregion

        #region Parse

        private static IapStoreSnapshot Parse(long httpCode, UnityWebRequest.Result result, string error,
            string body)
        {
            var snapshot = new IapStoreSnapshot { HttpCode = httpCode };

            if (result != UnityWebRequest.Result.Success)
            {
                snapshot.Error = Message(httpCode, error, body);
                // 503 vẫn kèm `platforms` — lý do TỪNG nền tảng đọc hụt là thông tin đáng hiện.
                var failure = TryJson<ErrorResponse>(body);
                if (failure?.platforms != null) AddPlatforms(snapshot, failure.platforms);
                return snapshot;
            }

            var response = TryJson<Response>(body);
            if (response == null)
            {
                snapshot.Error = "Server trả về nội dung không phải JSON của API này.";
                return snapshot;
            }

            snapshot.Project = response.project ?? string.Empty;
            snapshot.CheckedAt = response.checkedAt ?? string.Empty;
            snapshot.KeyExpiresAt = response.keyExpiresAt ?? string.Empty;
            AddPlatforms(snapshot, response.platforms);

            if (response.products != null)
                foreach (var product in response.products)
                {
                    if (product == null || string.IsNullOrEmpty(product.productId)) continue;

                    snapshot.Rows.Add(new IapStoreRow
                    {
                        Platform = (product.platform ?? string.Empty).ToLowerInvariant(),
                        ProductId = product.productId,
                        State = StateOf(product.status),
                        StoreStatus = product.storeStatus ?? string.Empty,
                    });
                }

            snapshot.Index();
            return snapshot;
        }

        private static void AddPlatforms(IapStoreSnapshot snapshot, PlatformInfo[] platforms)
        {
            if (platforms == null) return;

            foreach (var platform in platforms)
            {
                if (platform == null) continue;

                snapshot.Platforms.Add(new IapStorePlatform
                {
                    Platform = (platform.platform ?? string.Empty).ToLowerInvariant(),
                    Ok = platform.ok,
                    CheckedAt = platform.checkedAt ?? string.Empty,
                    Stale = platform.stale,
                    Error = platform.error ?? string.Empty,
                });
            }
        }

        /// <summary>
        ///     Chuỗi <c>status</c> của server → enum. Cố ý KHÔNG đọc <c>storeStatus</c>: mỗi lần Apple
        ///     hay Google thêm một trạng thái thô mới là code ở đây phải sửa; <c>status</c> là đúng bốn
        ///     giá trị và server chịu trách nhiệm map.
        /// </summary>
        private static IapStoreState StateOf(string status) =>
            (status ?? string.Empty).ToLowerInvariant() switch
            {
                "ready" => IapStoreState.Ready,
                "incomplete" => IapStoreState.Incomplete,
                "inactive" => IapStoreState.Inactive,
                "not_found" => IapStoreState.Missing,
                _ => IapStoreState.Incomplete,
            };

        /// <summary>
        ///     Câu lỗi hiện cho dev. Server đã viết sẵn câu tiếng Việt nói rõ phải làm gì (key hết hạn
        ///     ngày nào, key thuộc dự án nào) — ưu tiên nó, mã HTTP trơn chỉ là đường chót.
        /// </summary>
        private static string Message(long httpCode, string error, string body)
        {
            var failure = TryJson<ErrorResponse>(body);
            if (!string.IsNullOrEmpty(failure?.error))
            {
                var detail = string.IsNullOrEmpty(failure.detail) ? string.Empty : "  " + failure.detail.Replace('\n', ' ');
                return $"[{httpCode}] {failure.error}{detail}";
            }

            if (httpCode == 0)
                return "Không nối được tới server: " + error
                       + ". Link sai, chưa bật server (npm run dev:api), hay mất mạng?";

            if (string.IsNullOrEmpty(body)) return $"[{httpCode}] {error}";

            var trimmed = body.Length <= ERROR_BODY_MAX ? body : body.Substring(0, ERROR_BODY_MAX) + "…";
            return $"[{httpCode}] {error}. {trimmed}";
        }

        private static T TryJson<T>(string body) where T : class
        {
            if (string.IsNullOrEmpty(body)) return null;

            try
            {
                return JsonUtility.FromJson<T>(body);
            }
            catch (Exception)
            {
                // Body không phải JSON (trang HTML của proxy, hay server dev trả stack trace).
                return null;
            }
        }

        #endregion

        #region Nhãn

        internal static string Label(IapStoreState state) =>
            state switch
            {
                IapStoreState.Ready => "trên store",
                IapStoreState.Incomplete => "store: chưa xong",
                IapStoreState.Inactive => "store: đang tắt",
                IapStoreState.Missing => "CHƯA có trên store",
                _ => "store: chưa rõ",
            };

        internal static EzgStatus StatusOf(IapStoreState state) =>
            state switch
            {
                IapStoreState.Ready => EzgStatus.Ok,
                IapStoreState.Incomplete => EzgStatus.Warn,
                IapStoreState.Inactive => EzgStatus.Warn,
                // Client đăng ký một SKU không tồn tại trên store = bấm mua là fail, ô giá trống.
                IapStoreState.Missing => EzgStatus.Error,
                _ => EzgStatus.None,
            };

        #endregion
    }
}
#endif
