#if UNITY_EDITOR
using Ezg.Editor.Shared.Iap;
using UnityEditor;
using UnityEngine.UIElements;

namespace Ezg.Editor.Shared.EzgKit.Pages
{
    /// <summary>
    ///     Gói bán — CHỈ ĐỌC. Danh sách đúng là thứ client đăng ký với store (<c>ShopService.GetAllProductId()</c>),
    ///     kèm loại, giá tham chiếu và trạng thái trên Google Play / App Store Connect (qua API nội bộ, có cache).
    ///     Tạo / sửa gói bán làm bằng MCP gói bán, không ở đây.
    /// </summary>
    internal sealed class IapPage : SetupPage
    {
        private readonly IapStoreVerifier _verifier = new();
        private TextField _apiKey;

        internal override string Id => PageIds.IAP;

        internal override string Title => "Gói bán (IAP)";

        internal override string Description =>
            "Danh sách gói client thật sự đăng ký với store và trạng thái trên Google Play / App Store Connect. Chỉ đọc.";

        #region Core

        internal override PageReport Detect()
        {
            var audit = IapAudit.Current();
            var report = new PageReport();
            foreach (var todo in audit.Todos) report.Add(todo.Status, todo.What, todo.Fix);
            if (audit.HasClient && audit.Catalog.RegisteredCount > 0) report.Ok();
            report.Resolve(audit.Headline);
            if (report.State == SetupState.Error && audit.HasClient && audit.Snapshot == null) report.State = SetupState.Partial;
            return report;
        }

        internal override JsonObject GetValues(bool maskSecrets)
        {
            var audit = IapAudit.Current();
            var packs = new System.Collections.Generic.List<object>();
            if (audit.HasClient)
                foreach (var group in audit.Groups)
                foreach (var row in group.Rows)
                    packs.Add(new JsonObject()
                        .Set("collection", group.Name)
                        .Set("pack", row.Sku.Label)
                        .Set("androidId", row.Sku.GoogleId ?? string.Empty)
                        .Set("iosId", row.Sku.AppleId ?? string.Empty)
                        .Set("registered", row.Sku.Registered)
                        .Set("type", row.Kind)
                        .Set("price", row.Price)
                        .Set("client", row.ClientLabel ?? string.Empty)
                        .Set("store", row.StoreLabel ?? string.Empty));
            return new JsonObject()
                .Set("headline", audit.Headline)
                .Set("registeredIds", audit.HasClient ? new System.Collections.Generic.List<string>(audit.Catalog.RegisteredIds) : new System.Collections.Generic.List<string>())
                .Set("packs", packs)
                .Set("apiKey", maskSecrets ? Mask.Secret(IapVerifyConfig.ApiKey) : IapVerifyConfig.ApiKey);
        }

        #endregion

        #region UI

        internal override void Build(VisualElement body, IPageHost host)
        {
            var audit = IapAudit.Current();

            var top = Ui.Row(body, "ezg-actions");
            Ui.Button(top, "Làm mới", () =>
            {
                IapAudit.Current(true);
                host.RefreshAll();
                host.Rebuild();
            }, "secondary", "Đọc lại danh mục client (reflection + asset).");
            Ui.Button(top, _verifier.IsRunning ? "Đang kiểm store…" : "Kiểm tra trên store", () => Verify(host), "primary",
                "Hỏi API project-ezg: gói đã có trên Google Play / App Store Connect chưa (server đệm 60 giây).");
            if (!string.IsNullOrEmpty(audit.CopyPayload))
                Ui.Button(top, audit.CopyLabel, () =>
                {
                    EditorGUIUtility.systemCopyBuffer = audit.CopyPayload;
                    host.Toast("Đã copy danh sách id.", EzgStatus.Ok);
                }, "secondary");
            Ui.Message(body, "Tạo / sửa gói bán bằng MCP gói bán — trang này chỉ hiển thị và đối chiếu.", EzgStatus.None);

            if (audit.Snapshot != null)
                Ui.Hint(body, audit.Snapshot.IsValid
                    ? $"Store: dự án \"{audit.Snapshot.Project}\" · đọc lúc {audit.Snapshot.CheckedAtLabel}"
                      + (audit.Snapshot.FromCache ? $" (bản lưu, {IapAudit.Age(audit.Snapshot.FetchedAtLocal)})" : string.Empty)
                    : "Store: " + audit.Snapshot.Error);

            if (!audit.HasClient)
            {
                Ui.Message(body, audit.Catalog?.Error ?? "Không đọc được danh mục client.", EzgStatus.Error);
                BuildApiKey(body, host);
                return;
            }

            foreach (var group in audit.Groups)
            {
                var card = Ui.Card(body, group.Name, group.Trailing + (group.Note == null ? string.Empty : "  ·  " + group.Note));
                var head = Ui.Row(card, "ezg-table-head");
                head.Add(Ui.Text(string.Empty, "ezg-col-xs"));
                head.Add(Ui.Text("Gói", "ezg-col-m"));
                head.Add(Ui.Text("Product id", "ezg-col-l"));
                head.Add(Ui.Text("Loại", "ezg-col-s"));
                head.Add(Ui.Text("Giá", "ezg-col-s"));
                head.Add(Ui.Text("Store", "ezg-col-grow"));

                foreach (var row in group.Rows)
                {
                    var line = Ui.Row(card, "ezg-table-row");
                    var dotWrap = new VisualElement();
                    dotWrap.AddToClassList("ezg-col-xs");
                    dotWrap.Add(Ui.Dot(row.Worst));
                    line.Add(dotWrap);
                    var name = new VisualElement();
                    name.AddToClassList("ezg-col-m");
                    name.Add(Ui.Text(row.Sku.Label, "ezg-strong"));
                    name.Add(Ui.Text(row.ClientLabel, "ezg-hint"));
                    line.Add(name);
                    line.Add(Ui.Text(row.Ids, "ezg-col-l"));
                    line.Add(Ui.Text(row.Kind, "ezg-col-s"));
                    line.Add(Ui.Text(row.Price, "ezg-col-s"));
                    line.Add(Ui.Text(row.StoreLabel ?? "—", "ezg-col-grow"));
                    if (row.Notes.Count > 0) line.tooltip = string.Join("\n", row.Notes);
                }
            }

            if (audit.Catalog.UnmatchedIds.Count > 0)
            {
                var card = Ui.Card(body, "Id đăng ký không map được về gói nào trong data");
                foreach (var id in audit.Catalog.UnmatchedIds) card.Add(Ui.Text(id));
            }

            if (audit.ExtraOnStore.Count > 0)
            {
                var card = Ui.Card(body, "Có trên store mà client không đăng ký");
                foreach (var line in audit.ExtraOnStore) card.Add(Ui.Text(line));
            }

            var source = Ui.Card(body, "Nguồn phía client");
            Ui.InfoRow(source, "Hàm đăng ký", audit.Catalog.SourceLabel);
            Ui.InfoRow(source, "Cờ Non-Consumable", audit.Catalog.NonConsumableSource ?? "không có", audit.Catalog.NonConsumableSource == null ? EzgStatus.Warn : EzgStatus.None);
            Ui.InfoRow(source, "Bundle id (Editor)", audit.Catalog.BundleId);
            Ui.InfoRow(source, "Asset đã đọc", audit.Catalog.PackAssetCount.ToString());

            BuildApiKey(body, host);
        }

        private void BuildApiKey(VisualElement body, IPageHost host)
        {
            var card = Ui.Card(body, "Xác minh với store",
                "API key chỉ-đọc của dự án trên project-ezg (Dự án → tab API key). Lưu theo máy (EditorPrefs), không vào git.");
            _apiKey = Ui.TextRow(card, IapVerifyConfig.ApiKeyFromEnvironment ? "API key (từ biến môi trường)" : "API key",
                IapVerifyConfig.ApiKey, "Hoặc đặt biến môi trường " + IapVerifyConfig.ENV_API_KEY + " (máy CI).", null, true);
            var actions = Ui.Row(card, "ezg-actions");
            Ui.Button(actions, "Lưu key", () =>
            {
                IapVerifyConfig.ApiKey = _apiKey.value;
                host.Toast("Đã lưu API key cho máy này.", EzgStatus.Ok);
            }, "secondary");
            Ui.Link(actions, "project-ezg", IapVerifyConfig.PORTAL_URL);
            if (IapVerifyConfig.IsLocalApi) Ui.Message(card, "Đang trỏ server dev tại máy: " + IapVerifyConfig.ApiUrl, EzgStatus.Warn);
        }

        private void Verify(IPageHost host)
        {
            if (_apiKey != null && !string.IsNullOrWhiteSpace(_apiKey.value) && _apiKey.value != IapVerifyConfig.ApiKey)
                IapVerifyConfig.ApiKey = _apiKey.value;

            if (_verifier.IsRunning) return;
            host.Toast("Đang hỏi store…", EzgStatus.None);
            _verifier.Fetch(IapVerifyConfig.ApiUrl, IapVerifyConfig.ApiKey, snapshot =>
            {
                IapAudit.WithSnapshot(snapshot);
                host.RefreshAll();
                host.Rebuild();
                host.Toast(snapshot.IsValid
                        ? $"Đã đọc danh mục store: {snapshot.Rows.Count} dòng, {snapshot.UsablePlatformCount}/{snapshot.Platforms.Count} nền tảng đọc được."
                        : "Không xác minh được: " + snapshot.Error,
                    snapshot.IsValid ? EzgStatus.Ok : EzgStatus.Error);
            });
        }

        #endregion
    }
}
#endif
