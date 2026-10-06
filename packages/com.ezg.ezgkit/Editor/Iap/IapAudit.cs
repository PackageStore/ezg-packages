#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using Ezg.Editor.Shared.EzgKit;

namespace Ezg.Editor.Shared.Iap
{
    /// <summary>
    ///     Phán quyết IAP tách khỏi UI: từ danh mục client (<see cref="IapClientSkus" />) và snapshot store
    ///     (<see cref="IapStoreVerifier" /> / <see cref="IapStoreCache" />) dựng ra từng dòng gói, khối việc
    ///     phải làm, trạng thái + một câu headline. Trang "Gói bán" và Tổng quan / Readiness / API đọc CÙNG
    ///     một kết quả, không ai tự tính lại.
    ///     <para>
    ///         Logic giữ nguyên bản 0.5 (trước nằm trong <c>IapSetupPage.Compare</c>), chỉ chuyển chỗ.
    ///     </para>
    /// </summary>
    internal sealed class IapAudit
    {
        #region Types

        internal sealed class Row
        {
            internal IapClientSku Sku;

            /// <summary>Giá tham chiếu từ <c>iapCost</c> của data — KHÔNG phải giá store.</summary>
            internal string Price;

            internal string Ids;
            internal EzgStatus ClientStatus;
            internal string ClientLabel;
            internal EzgStatus StoreStatus;
            internal string StoreLabel;
            internal EzgStatus Worst;
            internal readonly List<string> Notes = new();

            internal string Kind => Sku.NonConsumable ? "Non-Consumable" : "Consumable";
        }

        internal sealed class Group
        {
            internal string Name;
            internal string Note;
            internal EzgStatus Status;
            internal string Trailing;
            internal readonly List<Row> Rows = new();
        }

        internal struct Todo
        {
            internal EzgStatus Status;
            internal string What;
            internal string Fix;
        }

        #endregion

        #region Constants

        private const int NAMES_IN_TODO = 6;

        #endregion

        #region Result

        internal IapClientCatalog Catalog;
        internal IapStoreSnapshot Snapshot;

        internal readonly List<Group> Groups = new();
        internal readonly List<Todo> Todos = new();
        internal readonly List<string> ExtraOnStore = new();
        internal readonly List<string> MissingIds = new();

        internal int Registered;
        internal int StoreMatched;
        internal int StoreReady;

        internal EzgStatus Status = EzgStatus.Warn;
        internal string Headline = "Chưa đọc trạng thái.";

        internal string CopyLabel = "Copy id";
        internal string CopyPayload;

        internal bool HasStore => Snapshot != null && Snapshot.IsValid;

        internal bool HasClient => Catalog != null && Catalog.IsValid;

        #endregion

        #region Cache dùng chung (Tổng quan + trang IAP + API)

        private static IapAudit _last;
        private static int _lastSourceVersion = -1;

        /// <summary>
        ///     Bản audit gần nhất; đọc lại khi source đổi hoặc <paramref name="force" />. Phía client tốn
        ///     reflection + load asset nên không chạy lại mỗi lần cửa sổ vẽ lại.
        /// </summary>
        internal static IapAudit Current(bool force = false)
        {
            if (!force && _last != null && _lastSourceVersion == SourceIndex.Version) return _last;
            var snapshot = _last?.Snapshot ?? IapStoreCache.Load(out _);
            _last = Run(IapClientSkus.Collect(), snapshot);
            _lastSourceVersion = SourceIndex.Version;
            return _last;
        }

        /// <summary>Snapshot store mới về (nút "Kiểm tra trên store") — dựng lại phán quyết, lưu cache.</summary>
        internal static IapAudit WithSnapshot(IapStoreSnapshot snapshot)
        {
            if (snapshot != null && snapshot.IsValid) IapStoreCache.Save(snapshot);
            _last = Run(_last?.Catalog ?? IapClientSkus.Collect(), snapshot);
            _lastSourceVersion = SourceIndex.Version;
            return _last;
        }

        #endregion

        #region Run

        internal static IapAudit Run(IapClientCatalog catalog, IapStoreSnapshot snapshot)
        {
            var audit = new IapAudit { Catalog = catalog, Snapshot = snapshot };
            audit.Compare();
            return audit;
        }

        private void Compare()
        {
            if (!HasClient)
            {
                Add(EzgStatus.Error, Catalog?.Error ?? "Chưa đọc được phía client.",
                    "trang này không có nguồn dự phòng: nó phải gọi được đúng hàm client dùng lúc boot, "
                    + "không thì mọi kết luận về store đều là đoán.");
                BuildStatus();
                BuildCopyAction();
                return;
            }

            var known = new HashSet<string>(StringComparer.Ordinal);
            var generated = new List<string>();
            var badPrefix = new List<string>();
            var halfPlatform = new List<string>();
            var unregistered = new List<string>();
            var missingOnStore = new List<string>();
            var incompleteOnStore = new List<string>();
            var inactiveOnStore = new List<string>();

            var prefix = string.IsNullOrEmpty(Catalog.BundleId) ? null : Catalog.BundleId + ".";

            foreach (var sku in Catalog.Skus)
            {
                var group = GroupFor(sku);
                var row = new Row
                {
                    Sku = sku,
                    Price = sku.Cost > 0f ? "$" + sku.Cost.ToString("0.00", CultureInfo.InvariantCulture) : "—",
                    Ids = Ids(sku),
                };
                group.Rows.Add(row);

                CheckClient(row, prefix, generated, badPrefix, halfPlatform, unregistered);

                if (sku.Registered)
                {
                    Registered++;
                    foreach (var platform in IapPlatform.All)
                    {
                        var id = sku.IdOf(platform);
                        if (!string.IsNullOrEmpty(id)) known.Add(IapPlatform.Key(platform, id));
                    }
                }

                ApplyStore(row, missingOnStore, incompleteOnStore, inactiveOnStore);
                row.Worst = Worse(row.ClientStatus, row.StoreStatus);
            }

            foreach (var group in Groups) FinishGroup(group);
            Groups.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));

            if (HasStore) CollectExtraOnStore(known);

            BuildCopyAction();
            BuildTodos(generated, badPrefix, halfPlatform, unregistered);
            BuildStoreTodos(missingOnStore, incompleteOnStore, inactiveOnStore);
            Todos.Sort((a, b) => b.Status.CompareTo(a.Status));
            BuildStatus();
        }

        private Group GroupFor(IapClientSku sku)
        {
            var name = string.IsNullOrEmpty(sku.Collection) ? "(không rõ collection)" : sku.Collection;
            for (var i = Groups.Count - 1; i >= 0; i--)
                if (Groups[i].Name == name)
                    return Groups[i];

            var group = new Group
            {
                Name = name,
                Note = string.IsNullOrEmpty(sku.AssetPath) || sku.AssetPath.Contains("/Resources/")
                    ? null
                    : $"Asset nằm NGOÀI Resources ({sku.AssetPath}) — Resources.Load không thấy, data ở đây "
                      + "không tới được client.",
            };
            Groups.Add(group);
            return group;
        }

        private static string Ids(IapClientSku sku)
        {
            if (!sku.HasStoreId) return "(data chưa có product id)";
            if (sku.GoogleId == sku.AppleId) return sku.GoogleId;
            return $"android: {Or(sku.GoogleId)}   ·   ios: {Or(sku.AppleId)}";
        }

        private static string Or(string value) => string.IsNullOrEmpty(value) ? "(trống)" : value;

        private void CheckClient(Row row, string prefix, List<string> generated, List<string> badPrefix,
            List<string> halfPlatform, List<string> unregistered)
        {
            var sku = row.Sku;

            if (!sku.Registered)
            {
                row.ClientStatus = sku.HasStoreId ? EzgStatus.Warn : EzgStatus.None;
                row.ClientLabel = "không đăng ký";
                row.Notes.Add(sku.HasStoreId
                    ? "Data có product id nhưng client KHÔNG đăng ký id đó — collection chưa được nối vào "
                      + "nguồn đăng ký, hay gói đã bỏ mà data còn?"
                    : "Data chưa có product id và client cũng không đăng ký — dòng để trống.");
                if (sku.HasStoreId) unregistered.Add(sku.Label);
                return;
            }

            var status = EzgStatus.Ok;

            if (sku.GeneratedId)
            {
                status = EzgStatus.Error;
                row.Notes.Add($"Data chưa điền product id → client đăng ký bằng id SINH TỰ ĐỘNG "
                              + $"`{sku.RegisteredId}`. Id đó không tồn tại trên store: ô giá trống, bấm mua fail.");
                generated.Add(sku.Label);
            }
            else
            {
                if (prefix != null && sku.RegisteredId.IndexOf(prefix, StringComparison.Ordinal) != 0)
                {
                    status = Worse(status, EzgStatus.Warn);
                    row.Notes.Add($"Id không mang prefix bundle `{Catalog.BundleId}` — gói của game/bản khác "
                                  + "còn sót trong data, hay bundle vừa đổi?");
                    badPrefix.Add(sku.Label);
                }

                foreach (var platform in IapPlatform.All)
                {
                    if (!string.IsNullOrEmpty(sku.IdOf(platform))) continue;
                    status = Worse(status, EzgStatus.Warn);
                    row.Notes.Add($"{platform}: data chưa có product id — nền tảng này không kiểm được và không bán được.");
                    if (!halfPlatform.Contains(sku.Label)) halfPlatform.Add(sku.Label);
                }
            }

            if (Catalog.DuplicateIds.Contains(sku.RegisteredId))
            {
                status = Worse(status, EzgStatus.Warn);
                row.Notes.Add("Id này bị nhiều gói dùng chung — nhiều thẻ trong shop bán đúng một SKU của store.");
            }

            if (sku.NonConsumable)
                row.Notes.Add("Client khai Non-Consumable — trên store cũng phải Non-Consumable, và game phải có nút Restore.");

            row.ClientStatus = status;
            row.ClientLabel = status == EzgStatus.Ok
                ? sku.NonConsumable ? "đăng ký · non-consumable" : "đăng ký"
                : sku.GeneratedId
                    ? "id sinh tự động"
                    : "đăng ký · còn vấn đề";
        }

        private void ApplyStore(Row row, List<string> missing, List<string> incomplete, List<string> inactive)
        {
            if (!HasStore)
            {
                row.StoreStatus = EzgStatus.None;
                row.StoreLabel = null;
                return;
            }

            var sku = row.Sku;
            var worst = IapStoreState.Unknown;
            var unknownPlatforms = 0;
            var foundOnStore = false;

            foreach (var platform in IapPlatform.All)
            {
                var id = sku.IdOf(platform);
                if (string.IsNullOrEmpty(id)) continue;

                var state = Snapshot.StateOf(platform, id);
                if (state == IapStoreState.Unknown)
                {
                    unknownPlatforms++;
                    continue;
                }

                if (state != IapStoreState.Missing) foundOnStore = true;

                if (state != IapStoreState.Ready && (sku.Registered || state != IapStoreState.Missing))
                {
                    var storeRow = Snapshot.RowOf(platform, id);
                    var raw = storeRow == null || string.IsNullOrEmpty(storeRow.StoreStatus)
                        ? string.Empty
                        : $" ({storeRow.StoreStatus})";
                    row.Notes.Add($"{platform}: {IapStoreVerifier.Label(state)}{raw}.");
                }

                if (state == IapStoreState.Missing && sku.Registered && !MissingIds.Contains(id)) MissingIds.Add(id);
                if (state > worst) worst = state;
            }

            if (!sku.Registered)
            {
                row.StoreStatus = EzgStatus.None;
                row.StoreLabel = worst switch
                {
                    IapStoreState.Unknown => null,
                    IapStoreState.Missing => "không có trên store",
                    _ => "vẫn còn trên store",
                };
                return;
            }

            if (foundOnStore) StoreMatched++;

            row.StoreStatus = IapStoreVerifier.StatusOf(worst);
            row.StoreLabel = IapStoreVerifier.Label(worst);
            if (row.StoreStatus == EzgStatus.Ok) StoreReady++;

            if (unknownPlatforms > 0 && worst == IapStoreState.Unknown)
                row.Notes.Add("Server chưa đọc được nền tảng nào — CHƯA BIẾT gói này có trên store hay không.");

            switch (worst)
            {
                case IapStoreState.Missing: missing.Add(sku.Label); break;
                case IapStoreState.Incomplete: incomplete.Add(sku.Label); break;
                case IapStoreState.Inactive: inactive.Add(sku.Label); break;
            }
        }

        private void CollectExtraOnStore(HashSet<string> known)
        {
            var inData = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var sku in Catalog.Skus)
            foreach (var platform in IapPlatform.All)
            {
                var id = sku.IdOf(platform);
                if (!string.IsNullOrEmpty(id)) inData[IapPlatform.Key(platform, id)] = sku.Collection;
            }

            foreach (var storeRow in Snapshot.Rows)
            {
                if (known.Contains(storeRow.Key)) continue;
                var tail = inData.TryGetValue(storeRow.Key, out var collection)
                    ? $"  (có trong data {collection} nhưng client không đăng ký)"
                    : string.Empty;
                ExtraOnStore.Add($"{storeRow.Platform}  ·  {storeRow.ProductId}  ·  {storeRow.StoreStatus}{tail}");
            }
        }

        private static void FinishGroup(Group group)
        {
            var worst = EzgStatus.None;
            var pending = 0;
            var registered = 0;
            foreach (var row in group.Rows)
            {
                if (row.Worst is EzgStatus.Warn or EzgStatus.Error) pending++;
                if (row.Sku.Registered) registered++;
                worst = Worse(worst, row.Worst);
            }

            group.Status = worst;
            group.Trailing = pending == 0
                ? $"{group.Rows.Count} gói · {registered} đăng ký"
                : $"{group.Rows.Count} gói · {registered} đăng ký · {pending} cần xử lý";
        }

        #endregion

        #region Todos + status

        private void BuildTodos(List<string> generated, List<string> badPrefix, List<string> halfPlatform,
            List<string> unregistered)
        {
            if (Catalog.RegisteredCount == 0)
            {
                Add(EzgStatus.Error, $"`{Catalog.SourceLabel}` không trả về id nào",
                    "client không đăng ký SKU nào với store: mọi thẻ IAP trong game sẽ hiện \"coming soon\". "
                    + "Data thiếu asset, CSV chưa import, hay collection chưa được nối vào hàm đó?");
                return;
            }

            if (generated.Count > 0)
                Add(EzgStatus.Error, $"{generated.Count} gói đăng ký bằng id sinh tự động: {Names(generated)}",
                    "điền google_product_id + apple_product_id cho các gói này (MCP gói bán) rồi import lại. "
                    + "Client đang gửi lên store một id không tồn tại — bấm mua là fail.");

            if (badPrefix.Count > 0)
                Add(EzgStatus.Warn, $"{badPrefix.Count} id không mang prefix bundle `{Catalog.BundleId}`: {Names(badPrefix)}",
                    "gói của game/bản khác còn sót trong data, hay bundle vừa đổi. Đối chiếu Play Console / "
                    + "App Store Connect TRƯỚC khi sửa: SKU đã tồn tại đúng id đó thì đổi id là mất giao dịch "
                    + "và entitlement của người đã mua.");

            if (halfPlatform.Count > 0)
                Add(EzgStatus.Warn, $"{halfPlatform.Count} gói chỉ có id một nền tảng: {Names(halfPlatform)}",
                    "điền cả google_product_id và apple_product_id — thiếu bên nào là bên đó không bán được.");

            if (Catalog.DuplicateIds.Count > 0)
                Add(EzgStatus.Warn, $"{Catalog.DuplicateIds.Count} id bị nhiều gói dùng chung: {Names(Catalog.DuplicateIds)}",
                    "mỗi gói cần một product id riêng; dùng chung thì mọi thẻ đó bán đúng một SKU, và tracking "
                    + "doanh thu theo gói cũng sai.");

            if (unregistered.Count > 0)
                Add(EzgStatus.Warn, $"{unregistered.Count} gói có product id nhưng client KHÔNG đăng ký: {Names(unregistered)}",
                    $"collection chưa được nối vào `{Catalog.SourceLabel}`, hay gói đã bỏ mà data còn. "
                    + "Người chơi không mua được các gói này.");

            if (Catalog.UnmatchedIds.Count > 0)
                Add(EzgStatus.Warn, $"{Catalog.UnmatchedIds.Count} id đăng ký không map được về gói nào trong data",
                    "id đến từ một đường khác (hard-code, gói dựng lúc runtime?) nên kit không biết tên gói, giá "
                    + "hay id nền tảng còn lại của nó.");

            if (Catalog.NonConsumableSource == null)
                Add(EzgStatus.Warn, "Không đọc được cờ Non-Consumable của client",
                    "không tìm thấy `IPurchasing.GetNonConsumableProducts()` — kit không suy loại theo tên gói, nên "
                    + "phần Consumable / Non-Consumable ở đây là chưa biết.");
            else if (Catalog.NonConsumableCount == 0)
                Add(EzgStatus.Warn, $"Client khai 0/{Catalog.RegisteredCount} id là Non-Consumable",
                    "mọi SKU đang đăng ký là Consumable. Entitlement vĩnh viễn (remove ads, pass vĩnh viễn) phải là "
                    + "Non-Consumable + có nút Restore, không thì mua lại được nhiều lần và mất quyền khi cài lại máy.");
        }

        private void BuildStoreTodos(List<string> missingOnStore, List<string> incompleteOnStore,
            List<string> inactiveOnStore)
        {
            if (Snapshot == null)
            {
                Add(EzgStatus.Warn, "Chưa xác minh với store lần nào",
                    string.IsNullOrWhiteSpace(IapVerifyConfig.ApiKey)
                        ? "dán API key (project-ezg) ở khối \"Xác minh với store\" rồi bấm Kiểm tra trên store."
                        : "bấm \"Kiểm tra trên store\".");
                return;
            }

            if (!Snapshot.IsValid)
            {
                Add(EzgStatus.Error, "Không xác minh được với store: " + Snapshot.Error,
                    Snapshot.HttpCode == 503
                        ? "server chưa đọc được store — CHƯA BIẾT, đừng kết luận gói chưa tạo. Thử lại sau ít phút."
                        : "kiểm API key ở khối \"Xác minh với store\".");
                return;
            }

            if (Snapshot.Rows.Count > 0 && StoreMatched == 0 && Registered > 0)
            {
                Add(EzgStatus.Error,
                    $"Danh mục store có {Snapshot.Rows.Count} dòng nhưng KHÔNG khớp id nào của {Registered} gói client đăng ký",
                    $"API key này thuộc dự án \"{Snapshot.Project}\" — có phải key của dự án khác không? Nếu đúng dự "
                    + "án thì chưa gói nào của game được tạo trên store.");
                return;
            }

            foreach (var platform in Snapshot.Platforms)
            {
                if (platform.Ok) continue;
                Add(EzgStatus.Warn, $"Server không đọc được store {platform.Platform}"
                                    + (string.IsNullOrEmpty(platform.Error) ? string.Empty : ": " + platform.Error),
                    "danh mục KHÔNG nói gì về nền tảng này — kit bỏ qua nó thay vì báo \"chưa tạo\".");
            }

            if (Snapshot.AnyStale)
                Add(EzgStatus.Warn, "Server đang trả bản lưu (stale) vì lượt đọc mới nhất hỏng",
                    $"dữ liệu vẫn dùng được, chỉ là cũ — đọc lúc {Snapshot.CheckedAtLabel}.");

            if (missingOnStore.Count > 0)
                Add(EzgStatus.Error, $"{missingOnStore.Count} gói client đăng ký mà CHƯA có trên store: {Names(missingOnStore)}",
                    "tạo trên Play Console / App Store Connect, đúng loại Consumable / Non-Consumable theo cách client khai. "
                    + $"Nút \"{CopyLabel}\" copy sẵn danh sách id.");

            if (incompleteOnStore.Count > 0)
                Add(EzgStatus.Warn, $"{incompleteOnStore.Count} gói đã tạo nhưng chưa xong trên store: {Names(incompleteOnStore)}",
                    "vào console bổ sung phần còn thiếu (giá, tên, mô tả, ảnh review, base plan).");

            if (inactiveOnStore.Count > 0)
                Add(EzgStatus.Warn, $"{inactiveOnStore.Count} gói đang TẮT trên store: {Names(inactiveOnStore)}",
                    "bật lại trên console, hoặc bỏ gói khỏi data nếu thật sự không bán nữa.");

            if (ExtraOnStore.Count > 0)
                Add(EzgStatus.Warn, $"{ExtraOnStore.Count} dòng có trên store mà client không đăng ký",
                    "gói cũ của bản trước, id gõ sai, hay gói tạo tay? Gói rác trên store không xoá được, chỉ tắt được.");

            if (Snapshot.KeyExpiringSoon(out var daysLeft))
                Add(EzgStatus.Warn,
                    daysLeft >= 0 ? $"API key còn {daysLeft} ngày là hết hạn" : "API key đã hết hạn",
                    "xin admin gia hạn hoặc cấp key mới ở project-ezg → Dự án → tab API key.");
        }

        private void Add(EzgStatus status, string what, string fix) =>
            Todos.Add(new Todo { Status = status, What = what, Fix = fix });

        private void BuildCopyAction()
        {
            if (MissingIds.Count > 0)
            {
                CopyLabel = $"Copy {MissingIds.Count} id chưa có trên store";
                CopyPayload = string.Join("\n", MissingIds);
                return;
            }

            if (HasClient && Catalog.RegisteredCount > 0)
            {
                CopyLabel = $"Copy {Catalog.RegisteredCount} id đăng ký";
                CopyPayload = string.Join("\n", Catalog.RegisteredIds);
                return;
            }

            CopyLabel = "Copy id";
            CopyPayload = null;
        }

        private void BuildStatus()
        {
            var errors = 0;
            var warns = 0;
            foreach (var todo in Todos)
                if (todo.Status == EzgStatus.Error) errors++;
                else if (todo.Status == EzgStatus.Warn) warns++;

            Status = errors > 0 ? EzgStatus.Error : warns > 0 ? EzgStatus.Warn : EzgStatus.Ok;

            if (!HasClient)
            {
                Headline = "Không đọc được nguồn chuẩn phía client.";
                return;
            }

            var store = Snapshot == null
                ? "chưa xác minh store"
                : !Snapshot.IsValid
                    ? "store: lỗi kết nối"
                    : $"store: {StoreReady}/{Registered} gói sẵn sàng" + (Snapshot.FromCache ? " (bản lưu)" : string.Empty);

            Headline = $"{Catalog.RegisteredCount} id client đăng ký · {store}.";
        }

        private static EzgStatus Worse(EzgStatus a, EzgStatus b) => a > b ? a : b;

        private static string Names(List<string> names)
        {
            if (names.Count <= NAMES_IN_TODO) return string.Join(", ", names);
            return string.Join(", ", names.GetRange(0, NAMES_IN_TODO)) + $", … (+{names.Count - NAMES_IN_TODO})";
        }

        internal static string Age(DateTime moment)
        {
            var minutes = (DateTime.Now - moment).TotalMinutes;
            if (minutes < 1d) return "vừa xong";
            if (minutes < 60d) return $"{(int)minutes} phút trước";
            var hours = minutes / 60d;
            return hours < 24d ? $"{(int)hours} giờ trước" : $"{(int)(hours / 24d)} ngày trước";
        }

        #endregion
    }
}
#endif
