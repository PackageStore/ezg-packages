#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using Ezg.Editor.Shared.Readiness;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using UnityEngine.UIElements;

namespace Ezg.Editor.Shared.EzgKit.Pages
{
    /// <summary>
    ///     Tổng quan: thẻ trạng thái của mọi trang (bấm để mở) + danh sách Readiness (IAP / Firebase / SDK / Store /
    ///     Social) đọc thẳng từ project, kèm nút copy báo cáo cho PM. Chỉ đọc.
    /// </summary>
    internal sealed class OverviewPage : SetupPage
    {
        private ReadinessReport _readiness;
        private AppStoreLookup _lookup;
        private bool _lookupRunning;

        internal override string Id => PageIds.OVERVIEW;

        internal override string Title => "Tổng quan";

        internal override string Description =>
            "Trạng thái từng bước setup và danh sách sẵn sàng phát hành. Chỉ đọc — bấm một thẻ để mở trang đó.";

        internal override string Group => GROUP_OVERVIEW;

        internal override PageReport Detect() => new() { State = SetupState.Info, Summary = string.Empty };

        internal override void Build(VisualElement body, IPageHost host)
        {
            var window = host as EzgKitWindow;
            if (window != null) BuildTiles(body, window, host);

            var requests = EzgKitState.Requests();
            if (requests.Count > 0)
                Ui.Message(body, "Đang nhờ Claude: " + string.Join(", ", requests)
                                 + " — chạy /setup-project trong Claude Code để xử lý.", EzgStatus.Warn);

            BuildReadiness(body, host);
        }

        private static void BuildTiles(VisualElement body, EzgKitWindow window, IPageHost host)
        {
            foreach (var group in new[] { (GROUP_SETUP, "Setup"), (GROUP_ADVANCED, "Nâng cao") })
            {
                body.Add(Ui.Text(group.Item2.ToUpperInvariant(), "ezg-nav-group-title"));
                var tiles = new VisualElement();
                tiles.AddToClassList("ezg-tiles");
                body.Add(tiles);

                foreach (var page in window.Pages)
                {
                    if (page.Group != group.Item1) continue;
                    var report = window.ReportFor(page.Id);
                    var tile = new VisualElement();
                    tile.AddToClassList("ezg-tile");
                    var top = Ui.Row(tile);
                    top.Add(Ui.Text(page.Title, "ezg-tile-title"));
                    top.Add(Ui.Pill(report.State));
                    tile.Add(Ui.Text(string.IsNullOrEmpty(report.Summary) ? page.Description : report.Summary, "ezg-tile-summary"));
                    if (report.Todos.Count > 0)
                        tile.Add(Ui.Text($"{report.Todos.Count} việc còn lại", "ezg-tile-summary"));
                    var id = page.Id;
                    tile.RegisterCallback<ClickEvent>(_ => host.Open(id));
                    tiles.Add(tile);
                }
            }
        }

        private void BuildReadiness(VisualElement body, IPageHost host)
        {
            try
            {
                _readiness = ReadinessChecks.Collect(_lookup);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                Ui.Message(body, "Không đọc được readiness: " + exception.Message, EzgStatus.Error);
                return;
            }

            var content = Ui.Card(body, "Sẵn sàng phát hành",
                $"{_readiness.Errors} lỗi · {_readiness.Warns} cảnh báo · {_readiness.Oks} sẵn sàng — đọc từ project, không gọi mạng.");

            var actions = Ui.Row(content, "ezg-actions");
            Ui.Button(actions, "Copy báo cáo cho PM", () =>
            {
                var android = PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android);
                var ios = PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.iOS);
                EditorGUIUtility.systemCopyBuffer = _readiness.ToText(PlayerSettings.productName, android, ios, PlayerSettings.bundleVersion);
                host.Toast("Đã copy báo cáo (dán vào Discord / Slack).", EzgStatus.Ok);
            }, "primary");
            Ui.Button(actions, _lookupRunning ? "Đang tra App Store…" : "Tra App Store", () => LookupAppStore(host), "secondary",
                "Tra App Store ID đang khai là app nào (iTunes lookup, công khai).");

            foreach (ReadinessGroup group in Enum.GetValues(typeof(ReadinessGroup)))
            {
                var items = new List<ReadinessItem>();
                foreach (var item in _readiness.Items)
                    if (item.Group == group)
                        items.Add(item);
                if (items.Count == 0) continue;

                var pending = 0;
                foreach (var item in items)
                    if (item.IsPending)
                        pending++;

                var fold = Ui.Fold(content, $"{ReadinessReport.GroupTitle(group)}   ({items.Count} mục · {pending} cần xử lý)", pending > 0);
                items.Sort((a, b) => b.Status.CompareTo(a.Status));
                foreach (var item in items) ReadinessRow(fold, item);
            }
        }

        internal static void ReadinessRow(VisualElement parent, ReadinessItem item)
        {
            var row = Ui.Row(parent, "ezg-ready-row");
            row.Add(Ui.Dot(item.Status));
            var col = new VisualElement();
            col.AddToClassList("ezg-grow");
            col.Add(Ui.Text(item.Label, "ezg-ready-label"));
            if (!string.IsNullOrEmpty(item.Value)) col.Add(Ui.Text(item.Value, "ezg-ready-value"));
            if (!string.IsNullOrEmpty(item.Note)) col.Add(Ui.Text(item.Note, "ezg-hint"));
            if (item.IsPending && !string.IsNullOrEmpty(item.Fix)) col.Add(Ui.Text("→ " + item.Fix, "ezg-todo-fix"));

            var buttons = Ui.Row(null, "ezg-actions");
            if (item.Actions != null)
                foreach (var (label, run) in item.Actions)
                {
                    var captured = run;
                    Ui.Button(buttons, label, () => ReadinessActions.Defer(captured), "link");
                }

            if (item.Links != null)
                foreach (var (label, url) in item.Links)
                    Ui.Link(buttons, label, url);
            if (buttons.childCount > 0) col.Add(buttons);
            row.Add(col);
        }

        private void LookupAppStore(IPageHost host)
        {
            if (_lookupRunning) return;
            var id = Ezg.Editor.Shared.EzgKit.AppSecretsSink.TypeExists
                ? AppSecretsSink.Read(AppSecretsSink.F_IOS_APP_ID)
                : Social.SocialChecks.ReadConst(ReadGameConstant(), "IOSAppId");
            if (string.IsNullOrEmpty(id))
            {
                host.Toast("Chưa có App Store ID để tra.", EzgStatus.Warn);
                return;
            }

            _lookupRunning = true;
            AsyncHttp.Get("https://itunes.apple.com/lookup?id=" + id.Trim(), response =>
            {
                _lookupRunning = false;
                _lookup = new AppStoreLookup { QueriedId = id };
                if (!response.Ok) _lookup.Error = response.Error;
                else
                {
                    var json = MiniJson.ParseObject(response.Body);
                    var results = json?.List("results");
                    if (results != null && results.Count > 0 && results[0] is JsonObject app)
                    {
                        _lookup.Found = true;
                        _lookup.TrackName = app.Str("trackName");
                        _lookup.BundleId = app.Str("bundleId");
                        _lookup.Seller = app.Str("sellerName");
                    }
                }

                host.Rebuild();
                host.Toast(_lookup.Found ? $"App Store: \"{_lookup.TrackName}\" · {_lookup.BundleId}" : "App Store không trả về app nào cho id này.",
                    _lookup.Found ? EzgStatus.Ok : EzgStatus.Warn);
            });
        }

        private static string ReadGameConstant()
        {
            var path = Social.SocialChecks.FindGameConstant();
            return path == null ? null : System.IO.File.ReadAllText(path);
        }
    }
}
#endif
