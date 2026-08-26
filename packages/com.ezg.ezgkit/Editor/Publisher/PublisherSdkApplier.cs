#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Ezg.Editor.Shared.Marketing;
using Ezg.Editor.Shared.Social;
using UnityEditor;
using UnityEngine;

namespace Ezg.Editor.Shared.Publisher
{
    /// <summary>
    ///     Nút "Sinh lại SDK theo nhà phát hành": ghi mọi ID PUBLISHER CẤP (<see cref="SdkIdSlot.PublisherValue" />)
    ///     mà <see cref="SdkCatalog" /> biết chỗ ghi (<see cref="SlotReport.GameConstantName" />) vào
    ///     <c>GameConstant.cs</c>. ID game tự tạo (Meta app id, GA key) không ghi — dev điền trên console rồi
    ///     tool kiểm. ID ngoài Unity (Partner ID) chỉ hiện.
    ///     <para>
    ///         Ghi thêm field tương ứng trong <c>ProjectSettings/MarketingConfig.json</c> (nếu có): tab
    ///         Marketing có nút "Apply Config (khong tai sheet)" ghi lại GameConstant từ JSON đó — không sửa
    ///         JSON thì bấm nút ấy một lần là key publisher bị đè về key Ezg. Google Sheet marketing vẫn
    ///         phải đổi tay — applier nói rõ trong danh sách thay đổi.
    ///     </para>
    ///     <para>
    ///         Cùng luật với <c>SocialChecks.Apply</c>: đọc-ghi giữ BOM, chỉ thay phần giá trị trong dấu
    ///         nháy, <c>ImportAsset</c> file .cs để Unity recompile. Gọi qua <c>ReadinessActions.Defer</c>.
    ///     </para>
    /// </summary>
    internal static class PublisherSdkApplier
    {
        /// <summary>Const GameConstant → field trong MarketingConfig.json phải đồng bộ.</summary>
        private static readonly Dictionary<string, string> _marketingField = new()
        {
            { "AppsFlyerId", "appsflyerDevKey" },
            { "IOSAppId", "appleId" },
        };

        /// <summary>
        ///     Ghi các ID publisher cấp. <paramref name="dryRun" /> = chỉ liệt kê. Trả về false khi không có
        ///     gì ghi được (profile không có ID cấp sẵn, thiếu GameConstant) — lý do trong <paramref name="error" />.
        /// </summary>
        internal static bool Apply(IPublisherProfile profile, List<SdkReport> reports, bool dryRun,
            out List<string> changes, out string error)
        {
            changes = new List<string>();
            error = null;

            var writes = new List<(string Const, string Value)>();
            foreach (var report in reports)
            {
                if (!report.Required) continue;
                foreach (var slot in report.Slots)
                    if (slot.Wanted != null && slot.GameConstantName != null)
                        writes.Add((slot.GameConstantName, slot.Wanted));
            }

            if (writes.Count == 0)
            {
                error = $"{profile.DisplayName} khong co ID nao tool ghi duoc bang may (ID cap san + nam trong GameConstant).";
                return false;
            }

            var path = SocialChecks.FindGameConstant();
            if (path == null || !File.Exists(path))
            {
                error = "Khong tim thay GameConstant.cs trong du an.";
                return false;
            }

            var bytes = File.ReadAllBytes(path);
            var hadBom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
            var text = new UTF8Encoding(false).GetString(bytes, hadBom ? 3 : 0, bytes.Length - (hadBom ? 3 : 0));
            var dirty = false;

            foreach (var (name, value) in writes)
            {
                var match = Regex.Match(text, "(public const string " + name + "\\s*=\\s*\")([^\"]*)(\";)");
                if (!match.Success)
                {
                    error = $"GameConstant.cs khong co `public const string {name}` — them tay roi chay lai.";
                    return false;
                }

                var current = match.Groups[2].Value;
                if (current == value)
                {
                    changes.Add($"GameConstant.{name}: giu nguyen ({value})");
                }
                else
                {
                    changes.Add($"GameConstant.{name}: {Display(current)} -> {value}");
                    text = text.Remove(match.Groups[2].Index, current.Length).Insert(match.Groups[2].Index, value);
                    dirty = true;
                }

                if (_marketingField.TryGetValue(name, out var field)) SyncMarketingJson(field, value, dryRun, changes);
            }

            if (dryRun) return true;

            if (dirty)
            {
                File.WriteAllText(path, text, new UTF8Encoding(hadBom));
                var assetPath = "Assets" + path.Replace('\\', '/').Substring(Application.dataPath.Length);
                AssetDatabase.ImportAsset(assetPath);
            }

            var state = PublisherState.Load();
            state.activePublisher = profile.Id;
            state.appliedAtUtc = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm 'UTC'");
            state.Save();
            changes.Add($"PublisherConfig.json: activePublisher = {profile.Id}");
            return true;
        }

        private static void SyncMarketingJson(string field, string value, bool dryRun, List<string> changes)
        {
            string path;
            try
            {
                path = MarketingConfig.JsonPath;
            }
            catch (Exception)
            {
                return;
            }

            if (!File.Exists(path)) return;

            var text = File.ReadAllText(path);
            var match = Regex.Match(text, "(\"" + field + "\"\\s*:\\s*\")([^\"]*)(\")");
            if (!match.Success)
            {
                changes.Add($"MarketingConfig.json: khong co field {field} — bo qua.");
                return;
            }

            var current = match.Groups[2].Value;
            if (current == value)
            {
                changes.Add($"MarketingConfig.json.{field}: giu nguyen");
                return;
            }

            changes.Add($"MarketingConfig.json.{field}: {Display(current)} -> {value}  "
                        + "(Google Sheet marketing van giu gia tri cu — doi tay trong sheet, khong thi lan Tai sheet ke tiep se de nguoc)");
            if (dryRun) return;

            text = text.Remove(match.Groups[2].Index, current.Length).Insert(match.Groups[2].Index, value);
            File.WriteAllText(path, text, new UTF8Encoding(false));
        }

        private static string Display(string value) => string.IsNullOrEmpty(value) ? "(rong)" : value;
    }
}
#endif
