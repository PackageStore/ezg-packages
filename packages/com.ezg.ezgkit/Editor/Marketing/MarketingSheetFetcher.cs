#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace Ezg.Editor.Shared.Marketing
{
    /// <summary>
    ///     Tải sheet marketing (Google Sheets) về và ghi đè <c>marketing_config.json</c>.
    ///     Đây là mắt xích đầu của luồng 1-click: sheet → JSON → project/code (xem
    ///     <see cref="MarketingConfigApplier" />).
    ///
    ///     <para>
    ///         <b>Cách lấy dữ liệu:</b> export CSV của Google Sheets
    ///         (<c>.../export?format=csv&amp;gid=N</c>) — không cần API key, không cần OAuth, nhưng sheet
    ///         PHẢI ở chế độ "Anyone with the link → Viewer". Sheet để private thì Google trả về trang
    ///         đăng nhập HTML chứ không phải CSV; hàm <see cref="Fetch" /> phát hiện và báo lỗi rõ thay vì
    ///         parse ra config rỗng.
    ///     </para>
    ///     <para>
    ///         <b>Cách đọc:</b> dò theo NHÃN Ở CỘT ĐẦU (ví dụ "Max Rewarded"), không theo số thứ tự dòng —
    ///         thêm/bớt dòng trong sheet không làm hỏng mapping. Cột nào là Android / iOS thì lấy từ hàng
    ///         tiêu đề (ô chứa "a" ở cuối tên project, ví dụ <c>I001a</c> = Android, <c>I001i</c> = iOS).
    ///         Ô gộp (merged) chỉ có giá trị ở cột trái nên cột iOS trống sẽ tự lấy theo cột Android —
    ///         đúng ý sheet: SDK key / package name / AF key dùng chung.
    ///     </para>
    /// </summary>
    public static class MarketingSheetFetcher
    {
        #region Constants

        /// <summary>
        ///     Nơi lưu URL sheet — tách khỏi MarketingConfig.json vì file kia bị ghi đè mỗi lần fetch.
        ///     Cùng lý do như <see cref="MarketingConfig.JsonPath" />: mỗi dự án một URL/prefix riêng nên
        ///     phải nằm ngoài <c>Assets/</c>, không đi theo code dùng chung.
        /// </summary>
        private static string SourcePath =>
            Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? "",
                "ProjectSettings/MarketingSource.json");

        private const int TIMEOUT_SECONDS = 20;

        #endregion

        #region Types

        /// <summary>Nội dung <c>marketing_source.json</c>.</summary>
        [Serializable]
        private class SheetSource
        {
            public string sheetUrl;

            /// <summary>Tên project trong hàng tiêu đề, ví dụ "I001". Rỗng = tự dò cột.</summary>
            public string projectPrefix;
        }

        #endregion

        #region Public API

        /// <summary>
        ///     Kéo sheet về, dựng lại <c>marketing_config.json</c>. Trả về false (đã log lỗi) nếu hỏng —
        ///     caller PHẢI dừng, không được apply tiếp bằng file JSON cũ mà tưởng là mới.
        /// </summary>
        public static bool Fetch(out string report)
        {
            var source = LoadSource();
            if (source == null || string.IsNullOrEmpty(source.sheetUrl))
            {
                report = "Chua co URL sheet. Dan link vao tab Marketing cua Ezg > EzgKit.";
                return false;
            }

            var csvUrl = ToCsvExportUrl(source.sheetUrl);
            if (csvUrl == null)
            {
                report = $"URL khong phai Google Sheets: {source.sheetUrl}";
                return false;
            }

            if (!Download(csvUrl, out var csv, out var error))
            {
                report = $"Tai sheet that bai: {error}\nURL: {csvUrl}";
                return false;
            }

            // Sheet private -> Google tra ve trang dang nhap (HTML), khong phai CSV.
            if (csv.StartsWith("<", StringComparison.Ordinal) || csv.Contains("<!DOCTYPE html"))
            {
                report = "Google tra ve HTML thay vi CSV -> sheet dang PRIVATE. Vao Share > General access "
                         + "> Anyone with the link (Viewer) roi bam lai.";
                return false;
            }

            var rows = ParseCsv(csv);
            if (rows.Count < 2)
            {
                report = "Sheet rong hoac chi co dong tieu de.";
                return false;
            }

            var config = BuildConfig(rows, source.projectPrefix, out var warnings);
            File.WriteAllText(MarketingConfig.JsonPath, JsonUtility.ToJson(config, true),
                new UTF8Encoding(false));
            AssetDatabase.Refresh();

            var sb = new StringBuilder();
            sb.AppendLine($"Da tai sheet ({rows.Count} dong) -> marketing_config.json");
            foreach (var warning in warnings) sb.AppendLine($"  ! {warning}");
            report = sb.ToString();
            return true;
        }

        /// <summary>Lưu URL sheet vào <c>marketing_source.json</c>.</summary>
        public static void SaveSheetUrl(string url, string projectPrefix)
        {
            var source = LoadSource() ?? new SheetSource();
            source.sheetUrl = url;
            source.projectPrefix = projectPrefix;
            File.WriteAllText(SourcePath, JsonUtility.ToJson(source, true), new UTF8Encoding(false));
            AssetDatabase.Refresh();
        }

        /// <summary>URL sheet đang lưu (rỗng nếu chưa đặt) — cho UI hiển thị lại.</summary>
        public static string CurrentSheetUrl => LoadSource()?.sheetUrl ?? "";

        /// <summary>Tiền tố project đang lưu (ví dụ "I001").</summary>
        public static string CurrentProjectPrefix => LoadSource()?.projectPrefix ?? "";

        #endregion

        #region Download

        private static SheetSource LoadSource()
        {
            if (!File.Exists(SourcePath)) return null;
            try
            {
                return JsonUtility.FromJson<SheetSource>(File.ReadAllText(SourcePath));
            }
            catch (Exception e)
            {
                Debug.LogError($"[Marketing] marketing_source.json hong: {e.Message}");
                return null;
            }
        }

        /// <summary>
        ///     Đổi mọi dạng URL Google Sheets sang link export CSV. Chấp nhận link chia sẻ thường
        ///     (<c>/edit#gid=0</c>), link publish, hoặc chính link export.
        /// </summary>
        internal static string ToCsvExportUrl(string url)
        {
            var idMatch = Regex.Match(url, @"/spreadsheets/d/(?:e/)?([a-zA-Z0-9-_]+)");
            if (!idMatch.Success) return null;

            var gidMatch = Regex.Match(url, @"[#&?]gid=([0-9]+)");
            var gid = gidMatch.Success ? gidMatch.Groups[1].Value : "0";
            return $"https://docs.google.com/spreadsheets/d/{idMatch.Groups[1].Value}/export?format=csv&gid={gid}";
        }

        /// <summary>
        ///     Tải đồng bộ (block Editor tối đa <see cref="TIMEOUT_SECONDS" /> giây). Cố ý KHÔNG dùng
        ///     async: đây là thao tác bấm-rồi-chờ, và code chạy sau nó ghi vào file .cs nên không được
        ///     phép rơi vào giữa một lần domain reload.
        /// </summary>
        private static bool Download(string url, out string body, out string error)
        {
            body = null;
            error = null;

            using var request = UnityWebRequest.Get(url);
            request.timeout = TIMEOUT_SECONDS;
            var operation = request.SendWebRequest();

            try
            {
                while (!operation.isDone)
                {
                    if (EditorUtility.DisplayCancelableProgressBar("Marketing", "Dang tai Google Sheet...",
                            request.downloadProgress))
                    {
                        request.Abort();
                        error = "Nguoi dung huy.";
                        return false;
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            if (request.result != UnityWebRequest.Result.Success)
            {
                error = $"{request.result} - {request.error}";
                return false;
            }

            body = request.downloadHandler.text;
            return true;
        }

        #endregion

        #region CSV

        /// <summary>
        ///     Parse CSV theo RFC 4180 (ô có dấu phẩy / xuống dòng phải nằm trong ngoặc kép, `""` = một
        ///     dấu nháy). Ô trong sheet marketing có chứa dấu phẩy nên KHÔNG được split thô bằng ','.
        /// </summary>
        internal static List<List<string>> ParseCsv(string csv)
        {
            var rows = new List<List<string>>();
            var row = new List<string>();
            var cell = new StringBuilder();
            var inQuotes = false;

            for (var i = 0; i < csv.Length; i++)
            {
                var c = csv[i];

                if (inQuotes)
                {
                    if (c != '"') { cell.Append(c); continue; }
                    if (i + 1 < csv.Length && csv[i + 1] == '"') { cell.Append('"'); i++; continue; }
                    inQuotes = false;
                    continue;
                }

                switch (c)
                {
                    case '"':
                        inQuotes = true;
                        break;
                    case ',':
                        row.Add(cell.ToString().Trim());
                        cell.Clear();
                        break;
                    case '\r':
                        break;
                    case '\n':
                        row.Add(cell.ToString().Trim());
                        cell.Clear();
                        rows.Add(row);
                        row = new List<string>();
                        break;
                    default:
                        cell.Append(c);
                        break;
                }
            }

            if (cell.Length > 0 || row.Count > 0)
            {
                row.Add(cell.ToString().Trim());
                rows.Add(row);
            }

            return rows;
        }

        #endregion

        #region Mapping

        /// <summary>
        ///     Dựng <see cref="MarketingConfig" /> từ các dòng CSV. Nhãn dò theo kiểu "chứa chuỗi",
        ///     không phân biệt hoa thường và bỏ khoảng trắng thừa, để sheet đổi cách viết (thêm dấu hai
        ///     chấm, viết hoa khác) vẫn khớp.
        /// </summary>
        private static MarketingConfig BuildConfig(List<List<string>> rows, string projectPrefix,
            out List<string> warnings)
        {
            warnings = new List<string>();
            FindPlatformColumns(rows, projectPrefix, out var androidCol, out var iosCol, out var appleIdCol,
                warnings);

            var cfg = new MarketingConfig
            {
                max = new MarketingConfig.MaxSection
                {
                    android = new MarketingConfig.MaxPlatform(),
                    ios = new MarketingConfig.MaxPlatform()
                },
                admob = new MarketingConfig.AdmobSection
                {
                    android = new MarketingConfig.AdmobPlatform(),
                    ios = new MarketingConfig.AdmobPlatform()
                },
                facebook = new MarketingConfig.FacebookSection
                {
                    android = new MarketingConfig.FacebookPlatform(),
                    ios = new MarketingConfig.FacebookPlatform()
                },
                unityAds = new MarketingConfig.UnityAdsSection
                {
                    android = new MarketingConfig.UnityAdsPlatform(),
                    ios = new MarketingConfig.UnityAdsPlatform()
                },
                applovin = new MarketingConfig.AppLovinSection(),
                links = new MarketingConfig.LinksSection()
            };

            cfg.packageName = Cell(rows, "package name", androidCol, iosCol);
            cfg.gameName = Cell(rows, "game name", androidCol, iosCol);
            cfg.appleId = Cell(rows, "package name", appleIdCol, -1);
            if (string.IsNullOrEmpty(cfg.appleId)) cfg.appleId = Cell(rows, "apple id", androidCol, iosCol);
            cfg.appsflyerDevKey = Cell(rows, "af key", androidCol, iosCol);

            cfg.max.sdkKey = Cell(rows, "max sdk key", androidCol, iosCol);
            cfg.max.android.rewarded = Cell(rows, "max rewarded", androidCol, -1);
            cfg.max.ios.rewarded = Cell(rows, "max rewarded", iosCol, -1);
            cfg.max.android.interstitial = Cell(rows, "max inter", androidCol, -1);
            cfg.max.ios.interstitial = Cell(rows, "max inter", iosCol, -1);
            cfg.max.android.banner = Cell(rows, "max banner", androidCol, -1);
            cfg.max.ios.banner = Cell(rows, "max banner", iosCol, -1);

            // "Admob" (app id) phải dò khớp CHÍNH XÁC, nếu không nó nuốt luôn "Admob Reward"/"Admob inter".
            cfg.admob.android.appId = CellExact(rows, "admob", androidCol, iosCol);
            cfg.admob.ios.appId = CellExact(rows, "admob", iosCol, -1);
            if (string.IsNullOrEmpty(cfg.admob.ios.appId)) cfg.admob.ios.appId = cfg.admob.android.appId;
            cfg.admob.android.rewarded = Cell(rows, "admob reward", androidCol, -1);
            cfg.admob.ios.rewarded = Cell(rows, "admob reward", iosCol, -1);
            cfg.admob.android.interstitial = Cell(rows, "admob inter", androidCol, -1);
            cfg.admob.ios.interstitial = Cell(rows, "admob inter", iosCol, -1);

            cfg.unityAds.android.gameId = CellExact(rows, "unity", androidCol, -1);
            cfg.unityAds.ios.gameId = CellExact(rows, "unity", iosCol, -1);
            cfg.unityAds.android.rewarded = Cell(rows, "unity reward", androidCol, -1);
            cfg.unityAds.ios.rewarded = Cell(rows, "unity reward", iosCol, -1);
            cfg.unityAds.android.interstitial = Cell(rows, "unity inter", androidCol, -1);
            cfg.unityAds.ios.interstitial = Cell(rows, "unity inter", iosCol, -1);

            cfg.facebook.appId = Cell(rows, "facebook app id", androidCol, iosCol);
            cfg.facebook.clientToken = Cell(rows, "facebook client token", androidCol, iosCol);
            cfg.facebook.appLabel = string.IsNullOrEmpty(cfg.gameName) ? "" : cfg.gameName;
            cfg.facebook.android.rewarded = Cell(rows, "fb rewarded", androidCol, -1);
            cfg.facebook.ios.rewarded = Cell(rows, "fb rewarded", iosCol, -1);
            cfg.facebook.android.interstitial = Cell(rows, "fb inter", androidCol, -1);
            cfg.facebook.ios.interstitial = Cell(rows, "fb inter", iosCol, -1);

            // Các dòng dưới đây thường CHƯA có trong sheet marketing. Đọc được thì lấy, không thì giữ
            // giá trị đang dùng trong project (applier bỏ qua ô rỗng).
            cfg.applovin.consentFlowEnabled = true;
            cfg.applovin.privacyPolicyUrl = Cell(rows, "privacy", androidCol, iosCol);
            cfg.applovin.termsOfServiceUrl = Cell(rows, "term", androidCol, iosCol);
            cfg.applovin.attDescriptionEn = Cell(rows, "att", androidCol, iosCol);
            cfg.links.facebookPage = Cell(rows, "fanpage", androidCol, iosCol);

            MergeFromExisting(cfg);

            if (string.IsNullOrEmpty(cfg.packageName)) warnings.Add("Khong doc duoc dong 'Package name'.");
            if (string.IsNullOrEmpty(cfg.max.sdkKey)) warnings.Add("Khong doc duoc dong 'Max SDK Key'.");
            if (string.IsNullOrEmpty(cfg.max.android.rewarded) || string.IsNullOrEmpty(cfg.max.ios.rewarded))
                warnings.Add("Thieu MAX rewarded id o mot trong hai nen tang.");

            return cfg;
        }

        /// <summary>
        ///     Sheet marketing chỉ phủ một phần config (không có privacy URL, fanpage, banner id...).
        ///     Ô nào sheet không có thì giữ lại giá trị đang nằm trong <c>marketing_config.json</c> —
        ///     nếu không, mỗi lần fetch sẽ xoá sạch những giá trị điền tay.
        /// </summary>
        private static void MergeFromExisting(MarketingConfig cfg)
        {
            var old = MarketingConfig.Load();
            if (old == null) return;

            cfg.packageName = Or(cfg.packageName, old.packageName);
            cfg.gameName = Or(cfg.gameName, old.gameName);
            cfg.appleId = Or(cfg.appleId, old.appleId);
            cfg.appsflyerDevKey = Or(cfg.appsflyerDevKey, old.appsflyerDevKey);

            cfg.max.sdkKey = Or(cfg.max.sdkKey, old.max?.sdkKey);
            MergeMax(cfg.max.android, old.max?.android);
            MergeMax(cfg.max.ios, old.max?.ios);

            cfg.facebook.appLabel = Or(cfg.facebook.appLabel, old.facebook?.appLabel);

            if (old.applovin != null)
            {
                cfg.applovin.privacyPolicyUrl = Or(cfg.applovin.privacyPolicyUrl,
                    old.applovin.privacyPolicyUrl);
                cfg.applovin.termsOfServiceUrl = Or(cfg.applovin.termsOfServiceUrl,
                    old.applovin.termsOfServiceUrl);
                cfg.applovin.attDescriptionEn = Or(cfg.applovin.attDescriptionEn,
                    old.applovin.attDescriptionEn);
            }

            if (old.links == null) return;
            cfg.links.googlePlay = Or(cfg.links.googlePlay, old.links.googlePlay);
            cfg.links.appStore = Or(cfg.links.appStore, old.links.appStore);
            cfg.links.facebookPage = Or(cfg.links.facebookPage, old.links.facebookPage);
        }

        private static void MergeMax(MarketingConfig.MaxPlatform target, MarketingConfig.MaxPlatform old)
        {
            if (old == null) return;
            target.rewarded = Or(target.rewarded, old.rewarded);
            target.interstitial = Or(target.interstitial, old.interstitial);
            target.banner = Or(target.banner, old.banner);
        }

        private static string Or(string value, string fallback) =>
            string.IsNullOrEmpty(value) ? fallback ?? "" : value;

        /// <summary>
        ///     Dò cột Android / iOS / Apple ID từ hàng tiêu đề. Quy ước sheet: tên project + hậu tố
        ///     <c>a</c>/<c>i</c> (I001a / I001i). Không dò ra thì mặc định cột 1 = Android, cột 2 = iOS —
        ///     đúng với layout hiện tại — và cảnh báo để người dùng biết mà kiểm.
        /// </summary>
        private static void FindPlatformColumns(List<List<string>> rows, string projectPrefix,
            out int androidCol, out int iosCol, out int appleIdCol, List<string> warnings)
        {
            androidCol = -1;
            iosCol = -1;
            appleIdCol = -1;

            var prefix = (projectPrefix ?? "").Trim().ToLowerInvariant();

            foreach (var row in rows)
            {
                for (var col = 0; col < row.Count; col++)
                {
                    var value = row[col].Trim().ToLowerInvariant();
                    if (value.Length == 0) continue;

                    if (value == "apple id") appleIdCol = col;

                    // Sheet của dự án khác có thể ghi thẳng "Android"/"iOS" thay vì I001a/I001i.
                    if (value.Contains("android") && androidCol < 0) { androidCol = col; continue; }
                    if ((value.Contains("ios") || value.Contains("iphone")) && iosCol < 0)
                    {
                        iosCol = col;
                        continue;
                    }

                    if (prefix.Length > 0 && !value.StartsWith(prefix, StringComparison.Ordinal)) continue;
                    if (prefix.Length == 0 && !Regex.IsMatch(value, @"^[a-z]\d{3}[ai]$")) continue;

                    if (value.EndsWith("a", StringComparison.Ordinal) && androidCol < 0) androidCol = col;
                    else if (value.EndsWith("i", StringComparison.Ordinal) && iosCol < 0) iosCol = col;
                }

                if (androidCol >= 0 && iosCol >= 0) break;
            }

            if (androidCol < 0 || iosCol < 0)
            {
                warnings.Add("Khong dò duoc cot Android/iOS tu hang tieu de -> dung mac dinh cot 2 va 3.");
                if (androidCol < 0) androidCol = 1;
                if (iosCol < 0) iosCol = 2;
            }

            if (appleIdCol < 0) appleIdCol = Math.Max(androidCol, iosCol) + 1;
        }

        /// <summary>
        ///     Giá trị ô ở dòng có nhãn CHỨA <paramref name="label" />. Ô trống thì lấy theo
        ///     <paramref name="fallbackCol" /> (ô gộp chỉ điền ở cột trái) — truyền -1 để không fallback.
        /// </summary>
        private static string Cell(List<List<string>> rows, string label, int col, int fallbackCol) =>
            CellInternal(rows, label, col, fallbackCol, false);

        /// <summary>Như <see cref="Cell" /> nhưng nhãn phải khớp CHÍNH XÁC (tránh "Admob" nuốt "Admob Reward").</summary>
        private static string CellExact(List<List<string>> rows, string label, int col, int fallbackCol) =>
            CellInternal(rows, label, col, fallbackCol, true);

        private static string CellInternal(List<List<string>> rows, string label, int col, int fallbackCol,
            bool exact)
        {
            if (col < 0) return "";

            foreach (var row in rows)
            {
                if (row.Count == 0) continue;

                var rowLabel = Normalize(row[0]);
                if (rowLabel.Length == 0) continue;

                var matched = exact ? rowLabel == label : rowLabel.Contains(label);
                if (!matched) continue;

                if (col < row.Count && row[col].Length > 0) return row[col].Trim();
                if (fallbackCol >= 0 && fallbackCol < row.Count) return row[fallbackCol].Trim();
                return "";
            }

            return "";
        }

        /// <summary>Nhãn dòng về dạng so sánh được: thường hoá, gộp khoảng trắng, bỏ dấu hai chấm cuối.</summary>
        private static string Normalize(string value) =>
            Regex.Replace(value ?? "", @"\s+", " ").Trim().TrimEnd(':').ToLowerInvariant();

        #endregion
    }
}
#endif
