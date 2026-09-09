#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using Ezg.Editor.Shared.EzgKit;
using Ezg.Editor.Shared.Readiness;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace Ezg.Editor.Shared.Iap
{
    /// <summary>
    ///     Tab <b>IAP</b> — một chỗ trả lời đúng hai câu hỏi về gói bán:
    ///     <list type="number">
    ///         <item><b>Client đang đăng ký SKU nào?</b> <c>ShopService.GetAllProductId()</c> gọi qua
    ///         reflection (<see cref="IapClientSkus" />) — ĐÚNG danh sách client gửi store lúc boot, chứ
    ///         không phải một asset khai báo song song có thể lệch với nó.</item>
    ///         <item><b>Gói đã có thật trên store chưa?</b> API chỉ-đọc của server nội bộ
    ///         (<see cref="IapStoreVerifier" />) đọc App Store Connect + Google Play — thứ Unity không có
    ///         cách nào tự thấy. Mỗi lượt kiểm được LƯU LẠI (<see cref="IapStoreCache" />) nên mở tab lần
    ///         sau thấy ngay kết quả cũ; chưa có bản lưu mà máy đã có API key thì tab tự kiểm một lượt.</item>
    ///     </list>
    ///     <para>
    ///         <b>Tab CHỈ ĐỌC.</b> Không ghi gì vào project (nên <see cref="RunAllLabel" /> = null, không
    ///         tham gia luồng "chạy hết"); thứ duy nhất nó ghi là bản lưu store trong <c>Library/</c>.
    ///     </para>
    ///     <para>
    ///         <b>Mọi phán quyết dựng sẵn trong <see cref="Compare" /></b> (kể cả chuỗi nhãn và
    ///         <see cref="Headline" />): cửa sổ đọc <see cref="Status" />/<see cref="Headline" /> mỗi lượt
    ///         OnGUI của MỌI tab, và số widget vẽ ra không được đổi giữa lượt Layout và Repaint.
    ///     </para>
    /// </summary>
    internal class IapSetupPage : IEzgKitPage
    {
        #region Constants

        private const string URL_PLAY_CONSOLE = "https://play.google.com/console/";
        private const string URL_ASC = "https://appstoreconnect.apple.com/apps";

        /// <summary>Số tên gói in ra trong một dòng "Việc phải làm" trước khi cắt bằng "…".</summary>
        private const int NAMES_IN_TODO = 6;

        private const float PACK_NAME_WIDTH = 172f;
        private const float PRICE_WIDTH = 62f;

        #endregion

        #region Types

        /// <summary>
        ///     Một gói cùng MỌI phán quyết về nó, dựng sẵn trong <see cref="Compare" />. Vẽ chỉ đọc —
        ///     không tính lại, không nối chuỗi trong lúc vẽ.
        /// </summary>
        private sealed class SkuRow
        {
            internal IapClientSku Sku;

            /// <summary>Nhãn giá dựng sẵn từ <c>iapCost</c> của data (giá tham chiếu, KHÔNG phải giá store).</summary>
            internal string Price;

            /// <summary>Product id để copy: một dòng nếu apple = google, không thì ghi rõ hai bên.</summary>
            internal string Ids;

            internal EzgStatus ClientStatus;
            internal string ClientLabel;

            /// <summary><see cref="EzgStatus.None" /> = chưa xác minh store, hoặc gói không được đăng ký.</summary>
            internal EzgStatus StoreStatus;

            internal string StoreLabel;

            /// <summary>Mức nặng nhất trong hàng — icon đầu dòng lấy cái này.</summary>
            internal EzgStatus Worst;

            internal string Tooltip;

            internal readonly List<string> Notes = new();
        }

        /// <summary>Một asset collection = một khối gấp được.</summary>
        private sealed class Group
        {
            internal string Name;

            /// <summary>Ghi chú của cả khối (asset nằm ngoài Resources…). Null = không có gì phải nói.</summary>
            internal string Note;

            internal EzgStatus Status;

            /// <summary>Chữ đếm bên phải hàng tiêu đề (số gói · số đăng ký · số cần xử lý).</summary>
            internal string Trailing;

            internal readonly List<SkuRow> Rows = new();
        }

        /// <summary>Một dòng trong khối "Việc phải làm": chuyện gì + làm gì để hết.</summary>
        private struct Todo
        {
            internal EzgStatus Status;
            internal string What;
            internal string Fix;
        }

        #endregion

        #region Fields — client

        private IapClientCatalog _catalog;

        private readonly List<Group> _groups = new();
        private readonly Dictionary<string, bool> _open = new();

        /// <summary>Số gói ĐANG ĐĂNG KÝ map được về data — mẫu số của "store: x/y sẵn sàng".</summary>
        private int _registered;

        #endregion

        #region Fields — store

        private readonly IapStoreVerifier _verifier = new();
        private IapStoreSnapshot _snapshot;

        /// <summary>Lượt hỏi server đã xong, chờ đổ vào snapshot ở đầu lượt Draw kế tiếp.</summary>
        private IapStoreSnapshot _pendingSnapshot;

        /// <summary>Có trên store mà client KHÔNG đăng ký — chiều ngược lại của lượt đối chiếu.</summary>
        private readonly List<string> _extraOnStore = new();

        /// <summary>Id đăng ký mà store báo chưa có — nội dung nút Copy, để dán sang console.</summary>
        private readonly List<string> _missingIds = new();

        /// <summary>
        ///     Số gói đang đăng ký TÌM THẤY trong danh mục store. Dùng cho lượt kiểm độ khớp — thứ thay
        ///     cho một ô "mã dự án" gõ tay, xem <see cref="BuildStoreTodos" />.
        /// </summary>
        private int _storeMatched;

        private int _storeReady;

        /// <summary>Đã tự hỏi server một lượt trong phiên này — không tự hỏi lại, kể cả khi lượt đó lỗi.</summary>
        private bool _autoVerified;

        #endregion

        #region Fields — dựng sẵn cho lúc vẽ

        private readonly List<Todo> _todos = new();

        private string _headline = "Chưa đọc trạng thái.";
        private EzgStatus _status = EzgStatus.Warn;

        private string _copyLabel = "Copy id";
        private string _copyPayload;

        private Vector2 _scroll;
        private string _message;
        private EzgStatus _messageStatus = EzgStatus.None;
        private bool _reloadPending;

        /// <summary>
        ///     Cần tính lại phán quyết (<see cref="Compare" />) mà KHÔNG cần đọc lại phía client: snapshot
        ///     store về thì chỉ phán quyết đổi, còn data của project không đổi một byte — mà đọc lại data
        ///     là reflection + load asset.
        /// </summary>
        private bool _recomparePending;

        #endregion

        #region Page

        public string Title => "IAP";

        public string Subtitle =>
            "Gói IAP client đăng ký (ShopService.GetAllProductId) → gói thật trên Google Play / App Store Connect.";

        /// <summary>Tab chỉ đọc: không có gì để ghi trong luồng "chạy hết".</summary>
        public string RunAllLabel => null;

        public string Headline => _headline;

        public EzgStatus Status => _status;

        private bool HasStore => _snapshot != null && _snapshot.IsValid;

        private bool HasClient => _catalog != null && _catalog.IsValid;

        /// <summary>
        ///     Đọc phía client (reflection + asset data) và nạp bản lưu store nếu chưa có snapshot trong
        ///     phiên. KHÔNG gọi mạng ở đây — lượt tự kiểm nằm ở <see cref="AutoVerify" />, chỉ chạy khi
        ///     tab thật sự được mở.
        /// </summary>
        public void Reload()
        {
            _catalog = IapClientSkus.Collect();

            if (_snapshot == null)
            {
                _snapshot = IapStoreCache.Load(out var ignored);
                if (ignored != null)
                {
                    _message = ignored;
                    _messageStatus = EzgStatus.Warn;
                }
                else if (_snapshot != null)
                {
                    _message = $"Đang xem bản lưu lượt kiểm trước: dự án \"{_snapshot.Project}\", "
                               + $"{_snapshot.Rows.Count} dòng, store đọc lúc {_snapshot.CheckedAtLabel} "
                               + $"({Age(_snapshot.FetchedAtLocal)}).";
                    _messageStatus = EzgStatus.None;
                }
            }

            Compare();
        }

        public void Draw()
        {
            // Snapshot đổi ở ĐẦU lượt vẽ, không đổi giữa lượt: số widget phải giống nhau giữa Layout và
            // Repaint, không thì Unity ném "Getting control N's position in a group with only M".
            if (_pendingSnapshot != null)
            {
                _snapshot = _pendingSnapshot;
                _pendingSnapshot = null;
                IapStoreCache.Save(_snapshot);

                // Câu báo dựng Ở ĐÂY, không dựng trong callback: callback chạy từ EditorApplication.update,
                // có thể rơi vào giữa lượt vẽ, mà _message rỗng hay không quyết định CÓ VẼ một label hay
                // không — đổi số widget giữa Layout và Repaint.
                _message = _snapshot.IsValid
                    ? $"Đã đọc danh mục store của dự án \"{_snapshot.Project}\": {_snapshot.Rows.Count} dòng, "
                      + $"{_snapshot.UsablePlatformCount}/{_snapshot.Platforms.Count} nền tảng đọc được "
                      + $"(store đọc lúc {_snapshot.CheckedAtLabel}). Đã lưu vào {IapStoreCache.RelativePath}."
                    : "Không xác minh được: " + _snapshot.Error;
                _messageStatus = _snapshot.IsValid ? EzgStatus.Ok : EzgStatus.Error;
                _recomparePending = true;
            }

            if (_reloadPending)
            {
                _reloadPending = false;
                _recomparePending = false;
                Reload();
            }
            else if (_recomparePending)
            {
                _recomparePending = false;
                Compare();
            }

            if (_catalog == null) Reload();

            AutoVerify();
            DrawFixedTop();

            using (var scroll = new EditorGUILayout.ScrollViewScope(_scroll))
            {
                _scroll = scroll.scrollPosition;
                DrawTodos();
                DrawGroups();
                DrawUnmatched();
                DrawExtraOnStore();
                DrawClientSource();
                DrawStoreSource();
            }
        }

        /// <summary>Tab chỉ đọc — không có bước nào trong luồng "chạy hết".</summary>
        public bool RunAll() => true;

        /// <summary>
        ///     Chưa có bản lưu nào mà máy đã có API key → tự hỏi server MỘT lượt. Chạy trong
        ///     <see cref="Draw" /> chứ không trong <see cref="Reload" />: cửa sổ Reload cả 9 tab lúc mở,
        ///     mà gọi mạng cho một tab người dùng chưa nhìn tới là việc không ai xin.
        /// </summary>
        private void AutoVerify()
        {
            if (_autoVerified || _snapshot != null || _verifier.IsRunning) return;
            if (!HasClient || string.IsNullOrWhiteSpace(IapVerifyConfig.ApiKey)) return;

            _autoVerified = true;
            StartVerify();
        }

        #endregion

        #region Compare

        /// <summary>
        ///     Chụp toàn bộ trạng thái thành dữ liệu vẽ sẵn: từng khối collection, từng gói, khối việc
        ///     phải làm, nhãn nút, <see cref="Headline" /> và <see cref="Status" />.
        /// </summary>
        private void Compare()
        {
            _groups.Clear();
            _todos.Clear();
            _extraOnStore.Clear();
            _missingIds.Clear();
            _registered = 0;
            _storeMatched = 0;
            _storeReady = 0;

            if (!HasClient)
            {
                Add(EzgStatus.Error, _catalog?.Error ?? "Chưa đọc được phía client.",
                    "tab này không có nguồn dự phòng: nó phải gọi được đúng hàm client dùng lúc boot, "
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

            var prefix = string.IsNullOrEmpty(_catalog.BundleId) ? null : _catalog.BundleId + ".";

            foreach (var sku in _catalog.Skus)
            {
                var group = GroupFor(sku);
                var row = new SkuRow
                {
                    Sku = sku,
                    Price = sku.Cost > 0f
                        ? "$" + sku.Cost.ToString("0.00", CultureInfo.InvariantCulture)
                        : "—",
                    Ids = Ids(sku),
                };
                group.Rows.Add(row);

                CheckClient(row, prefix, generated, badPrefix, halfPlatform, unregistered);

                if (sku.Registered)
                {
                    _registered++;
                    foreach (var platform in IapPlatform.All)
                    {
                        var id = sku.IdOf(platform);
                        if (!string.IsNullOrEmpty(id)) known.Add(IapPlatform.Key(platform, id));
                    }
                }

                ApplyStore(row, missingOnStore, incompleteOnStore, inactiveOnStore);
                row.Worst = Worse(row.ClientStatus, row.StoreStatus);
            }

            foreach (var group in _groups) FinishGroup(group);
            _groups.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));

            if (HasStore) CollectExtraOnStore(known);

            // Nhãn nút Copy TRƯỚC danh sách việc: dòng "chưa có trên store" nhắc đúng chữ đang ở trên nút.
            BuildCopyAction();
            BuildTodos(generated, badPrefix, halfPlatform, unregistered);
            BuildStoreTodos(missingOnStore, incompleteOnStore, inactiveOnStore);

            // Đỏ trước vàng sau: người đọc xử lý theo đúng thứ tự đó. Sắp MỘT LẦN ở đây, không sắp trong
            // lúc vẽ.
            _todos.Sort((a, b) => b.Status.CompareTo(a.Status));
            BuildStatus();
        }

        private Group GroupFor(IapClientSku sku)
        {
            var name = string.IsNullOrEmpty(sku.Collection) ? "(không rõ collection)" : sku.Collection;
            for (var i = _groups.Count - 1; i >= 0; i--)
                if (_groups[i].Name == name)
                    return _groups[i];

            var group = new Group
            {
                Name = name,
                // Asset ngoài mọi thư mục Resources thì Resources.Load không thấy — data trong đó không
                // bao giờ tới được client, dù nó trông y như một collection thật.
                Note = string.IsNullOrEmpty(sku.AssetPath) || sku.AssetPath.Contains("/Resources/")
                    ? null
                    : $"Asset nằm NGOÀI Resources ({sku.AssetPath}) — Resources.Load không thấy, data ở đây "
                      + "không tới được client.",
            };
            _groups.Add(group);
            return group;
        }

        private static string Ids(IapClientSku sku)
        {
            if (!sku.HasStoreId) return "(data chưa có product id)";
            if (sku.GoogleId == sku.AppleId) return sku.GoogleId;

            return $"android: {Or(sku.GoogleId)}   ·   ios: {Or(sku.AppleId)}";
        }

        private static string Or(string value) => string.IsNullOrEmpty(value) ? "(trống)" : value;

        /// <summary>
        ///     Phán quyết phía client: gói này có nằm trong danh sách đăng ký không, và nếu có thì id của
        ///     nó có phải một id dùng được không.
        /// </summary>
        private void CheckClient(SkuRow row, string prefix, List<string> generated, List<string> badPrefix,
            List<string> halfPlatform, List<string> unregistered)
        {
            var sku = row.Sku;

            if (!sku.Registered)
            {
                // Gói trong data mà client không gửi lên store: có id thì là drift đáng biết (collection
                // chưa được nối vào nguồn đăng ký, hay gói bỏ rồi mà data còn); không có id thì chỉ là
                // dòng data để trống, không phải việc của ai.
                row.ClientStatus = sku.HasStoreId ? EzgStatus.Warn : EzgStatus.None;
                row.ClientLabel = "không đăng ký";
                row.Tooltip = $"Có trong data nhưng {_catalog.SourceLabel} không trả id của gói này.";
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
                    row.Notes.Add($"Id không mang prefix bundle `{_catalog.BundleId}` — gói của game/bản "
                                  + "khác còn sót trong data, hay bundle vừa đổi?");
                    badPrefix.Add(sku.Label);
                }

                foreach (var platform in IapPlatform.All)
                {
                    if (!string.IsNullOrEmpty(sku.IdOf(platform))) continue;

                    status = Worse(status, EzgStatus.Warn);
                    row.Notes.Add($"{platform}: data chưa có product id — nền tảng này không kiểm được và "
                                  + "không bán được.");
                    if (!halfPlatform.Contains(sku.Label)) halfPlatform.Add(sku.Label);
                }
            }

            if (_catalog.DuplicateIds.Contains(sku.RegisteredId))
            {
                status = Worse(status, EzgStatus.Warn);
                row.Notes.Add("Id này bị nhiều gói dùng chung — nhiều thẻ trong shop bán đúng một SKU của store.");
            }

            if (sku.NonConsumable)
                row.Notes.Add("Client khai Non-Consumable — trên store cũng phải Non-Consumable, và game "
                              + "phải có nút Restore.");

            row.ClientStatus = status;
            row.ClientLabel = status == EzgStatus.Ok
                ? sku.NonConsumable ? "đăng ký · non-consumable" : "đăng ký"
                : sku.GeneratedId
                    ? "id sinh tự động"
                    : "đăng ký · còn vấn đề";
        }

        /// <summary>
        ///     Gắn phán quyết store vào một hàng. Gói KHÔNG đăng ký vẫn được soi (id còn sống trên store
        ///     là dữ kiện) nhưng không sinh việc cho ai.
        /// </summary>
        private void ApplyStore(SkuRow row, List<string> missing, List<string> incomplete, List<string> inactive)
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

                var state = _snapshot.StateOf(platform, id);
                if (state == IapStoreState.Unknown)
                {
                    unknownPlatforms++;
                    continue;
                }

                // Có mặt trong danh mục (đã setup xong hay chưa không quan trọng) — dữ kiện cho lượt kiểm
                // độ khớp ở BuildStoreTodos, thứ thay cho ô "mã dự án" gõ tay.
                if (state != IapStoreState.Missing) foundOnStore = true;

                if (state != IapStoreState.Ready && (sku.Registered || state != IapStoreState.Missing))
                {
                    var storeRow = _snapshot.RowOf(platform, id);
                    var raw = storeRow == null || string.IsNullOrEmpty(storeRow.StoreStatus)
                        ? string.Empty
                        : $" ({storeRow.StoreStatus})";
                    row.Notes.Add($"{platform}: {IapStoreVerifier.Label(state)}{raw}.");
                }

                if (state == IapStoreState.Missing && sku.Registered && !_missingIds.Contains(id))
                    _missingIds.Add(id);

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

            if (foundOnStore) _storeMatched++;

            row.StoreStatus = IapStoreVerifier.StatusOf(worst);
            row.StoreLabel = IapStoreVerifier.Label(worst);
            if (row.StoreStatus == EzgStatus.Ok) _storeReady++;

            if (unknownPlatforms > 0 && worst == IapStoreState.Unknown)
                row.Notes.Add("Server chưa đọc được nền tảng nào — CHƯA BIẾT gói này có trên store hay không.");

            switch (worst)
            {
                case IapStoreState.Missing:
                    missing.Add(sku.Label);
                    break;
                case IapStoreState.Incomplete:
                    incomplete.Add(sku.Label);
                    break;
                case IapStoreState.Inactive:
                    inactive.Add(sku.Label);
                    break;
            }
        }

        /// <summary>
        ///     Chiều NGƯỢC của lượt đối chiếu: store có, client không đăng ký. Tách riêng id thuộc một gói
        ///     trong data (biết được là gói đã bỏ / chưa nối vào nguồn đăng ký) với id lạ hoàn toàn.
        /// </summary>
        private void CollectExtraOnStore(HashSet<string> known)
        {
            var inData = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var sku in _catalog.Skus)
            foreach (var platform in IapPlatform.All)
            {
                var id = sku.IdOf(platform);
                if (!string.IsNullOrEmpty(id)) inData[IapPlatform.Key(platform, id)] = sku.Collection;
            }

            foreach (var storeRow in _snapshot.Rows)
            {
                if (known.Contains(storeRow.Key)) continue;

                var tail = inData.TryGetValue(storeRow.Key, out var collection)
                    ? $"  (có trong data {collection} nhưng client không đăng ký)"
                    : string.Empty;
                _extraOnStore.Add($"{storeRow.Platform}  ·  {storeRow.ProductId}  ·  {storeRow.StoreStatus}{tail}");
            }
        }

        private void FinishGroup(Group group)
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

            // Khối còn việc thì mở sẵn; khối sạch thì gấp — trạng thái do người dùng bấm sau đó được giữ
            // nguyên qua các lượt Reload.
            if (!_open.ContainsKey(group.Name)) _open[group.Name] = pending > 0;
        }

        #endregion

        #region Compare — việc phải làm + nhãn

        private void BuildTodos(List<string> generated, List<string> badPrefix, List<string> halfPlatform,
            List<string> unregistered)
        {
            if (_catalog.RegisteredCount == 0)
            {
                Add(EzgStatus.Error, $"`{_catalog.SourceLabel}` không trả về id nào",
                    "client không đăng ký SKU nào với store: mọi thẻ IAP trong game sẽ hiện \"coming soon\". "
                    + "Data thiếu asset, CSV chưa import, hay collection chưa được nối vào hàm đó?");
                return;
            }

            if (generated.Count > 0)
                Add(EzgStatus.Error, $"{generated.Count} gói đăng ký bằng id sinh tự động: {Names(generated)}",
                    "điền google_product_id + apple_product_id trong CSV của collection rồi import lại. "
                    + "Client đang gửi lên store một id không tồn tại — bấm mua là fail.");

            if (badPrefix.Count > 0)
                Add(EzgStatus.Warn, $"{badPrefix.Count} id không mang prefix bundle `{_catalog.BundleId}`: {Names(badPrefix)}",
                    "gói của game/bản khác còn sót trong data, hay bundle vừa đổi. Đối chiếu Play Console / "
                    + "App Store Connect TRƯỚC khi sửa: nếu SKU đã tồn tại đúng id đó thì đổi id là mất "
                    + "giao dịch và entitlement của người đã mua.");

            if (halfPlatform.Count > 0)
                Add(EzgStatus.Warn, $"{halfPlatform.Count} gói chỉ có id một nền tảng: {Names(halfPlatform)}",
                    "điền cả google_product_id và apple_product_id — thiếu bên nào là bên đó không bán được, "
                    + "và tab cũng không kiểm được nó trên store.");

            if (_catalog.DuplicateIds.Count > 0)
                Add(EzgStatus.Warn, $"{_catalog.DuplicateIds.Count} id bị nhiều gói dùng chung: {Names(_catalog.DuplicateIds)}",
                    "mỗi gói cần một product id riêng; dùng chung thì mọi thẻ đó bán đúng một SKU, và "
                    + "tracking doanh thu theo gói cũng sai.");

            if (unregistered.Count > 0)
                Add(EzgStatus.Warn, $"{unregistered.Count} gói có product id nhưng client KHÔNG đăng ký: {Names(unregistered)}",
                    $"collection chưa được nối vào `{_catalog.SourceLabel}`, hay gói đã bỏ mà data còn. "
                    + "Người chơi không mua được các gói này.");

            if (_catalog.UnmatchedIds.Count > 0)
                Add(EzgStatus.Warn, $"{_catalog.UnmatchedIds.Count} id đăng ký không map được về gói nào trong data",
                    "id đến từ một đường khác (hard-code, gói dựng lúc runtime?) nên tab không biết tên gói, "
                    + "giá hay id nền tảng còn lại của nó. Danh sách ở khối bên dưới.");

            if (_catalog.NonConsumableSource == null)
                Add(EzgStatus.Warn, "Không đọc được cờ Non-Consumable của client",
                    "không tìm thấy `IPurchasing.GetNonConsumableProducts()` — tab không suy loại theo tên "
                    + "gói, nên phần Consumable / Non-Consumable ở đây là chưa biết.");
            else if (_catalog.NonConsumableCount == 0)
                Add(EzgStatus.Warn, $"Client khai 0/{_catalog.RegisteredCount} id là Non-Consumable",
                    "mọi SKU đang đăng ký là Consumable. Entitlement vĩnh viễn (remove ads, pass vĩnh viễn, "
                    + "gói mở tính năng) phải là Non-Consumable + có nút Restore, không thì mua lại được "
                    + "nhiều lần và mất quyền khi cài lại máy.");
        }

        private void BuildStoreTodos(List<string> missingOnStore, List<string> incompleteOnStore,
            List<string> inactiveOnStore)
        {
            if (_snapshot == null)
            {
                Add(EzgStatus.Warn, "Chưa xác minh với store lần nào",
                    string.IsNullOrWhiteSpace(IapVerifyConfig.ApiKey)
                        ? "dán API key ở khối \"Xác minh với store\" cuối trang — tab sẽ tự kiểm ngay sau đó. "
                          + "Không có bước này thì không ai biết gói đã tạo thật chưa."
                        : "bấm \"Kiểm tra trên store\" ở đỉnh trang.");
                return;
            }

            if (!_snapshot.IsValid)
            {
                Add(EzgStatus.Error, "Không xác minh được với store: " + _snapshot.Error,
                    _snapshot.HttpCode == 503
                        ? "server chưa đọc được store — CHƯA BIẾT, đừng kết luận gói chưa tạo. Thử lại sau ít phút."
                        : "xem khối \"Xác minh với store\" bên dưới: API key.");
                return;
            }

            // ── Bắt ca "dán nhầm key của dự án khác" ──────────────────────────────────────────
            //
            // Key quyết định dự án, nên key của dự án khác vẫn trả 200 — kèm danh mục của dự án ĐÓ, và lúc
            // đó MỌI gói của mình trông như "chưa tạo trên store".
            //
            // Cách bắt: ĐỘ KHỚP danh mục, không phải một ô "mã dự án" gõ tay. Store trả về gói mà KHÔNG
            // trùng một id nào của client thì gần như chắc chắn là danh mục của dự án khác — và không cần
            // ai điền gì để lượt kiểm này chạy.
            //
            // Store rỗng KHÔNG rơi vào đây: dự án mới chưa tạo gói nào là ca thật, đã có dòng "chưa có
            // trên store" ở dưới lo.
            if (_snapshot.Rows.Count > 0 && _storeMatched == 0 && _registered > 0)
            {
                Add(EzgStatus.Error,
                    $"Danh mục store có {_snapshot.Rows.Count} dòng nhưng KHÔNG khớp id nào của "
                    + $"{_registered} gói client đăng ký",
                    $"API key này thuộc dự án \"{_snapshot.Project}\" — có phải key của dự án khác không? "
                    + "Nếu đúng dự án thì nghĩa là chưa gói nào của game được tạo trên store, và mấy dòng "
                    + "kia là gói lạ (xem khối \"Có trên store mà client không đăng ký\").");
                return;
            }

            foreach (var platform in _snapshot.Platforms)
            {
                if (platform.Ok) continue;

                Add(EzgStatus.Warn, $"Server không đọc được store {platform.Platform}"
                                    + (string.IsNullOrEmpty(platform.Error) ? string.Empty : ": " + platform.Error),
                    "danh mục KHÔNG nói gì về nền tảng này — tool bỏ qua nó thay vì báo \"chưa tạo\". "
                    + "Kiểm credentials của nền tảng đó trên project-ezg.");
            }

            if (_snapshot.AnyStale)
                Add(EzgStatus.Warn, "Server đang trả bản lưu (stale) vì lượt đọc mới nhất hỏng",
                    $"dữ liệu vẫn dùng được, chỉ là cũ — đọc lúc {_snapshot.CheckedAtLabel}.");

            if (missingOnStore.Count > 0)
                Add(EzgStatus.Error, $"{missingOnStore.Count} gói client đăng ký mà CHƯA có trên store: {Names(missingOnStore)}",
                    "tạo trên Play Console / App Store Connect, đúng loại Consumable / Non-Consumable theo "
                    + "cách client khai. Client đăng ký SKU không tồn tại = ô giá trống, bấm mua fail. "
                    + $"Nút \"{_copyLabel}\" ở đỉnh trang copy sẵn danh sách id.");

            if (incompleteOnStore.Count > 0)
                Add(EzgStatus.Warn, $"{incompleteOnStore.Count} gói đã tạo nhưng chưa xong trên store: {Names(incompleteOnStore)}",
                    "vào console bổ sung phần còn thiếu (giá, tên, mô tả, ảnh review, base plan). Trạng thái "
                    + "thô của store nằm ở từng dòng trong bảng gói.");

            if (inactiveOnStore.Count > 0)
                Add(EzgStatus.Warn, $"{inactiveOnStore.Count} gói đang TẮT trên store: {Names(inactiveOnStore)}",
                    "bật lại trên console, hoặc bỏ gói khỏi data nếu thật sự không bán nữa.");

            if (_extraOnStore.Count > 0)
                Add(EzgStatus.Warn, $"{_extraOnStore.Count} dòng có trên store mà client không đăng ký",
                    "gói cũ của bản trước, id gõ sai, hay gói tạo tay? Danh sách ở khối bên dưới. Gói "
                    + "trùng/rác trên store không xoá được, chỉ tắt đi được.");

            if (_snapshot.KeyExpiringSoon(out var daysLeft))
                Add(EzgStatus.Warn,
                    daysLeft >= 0 ? $"API key còn {daysLeft} ngày là hết hạn" : "API key đã hết hạn",
                    "xin admin gia hạn hoặc cấp key mới ở project-ezg → Dự án → tab API key. Hết hạn là tab "
                    + "này ngừng xác minh được, không có cảnh báo nào khác.");
        }

        private void Add(EzgStatus status, string what, string fix) =>
            _todos.Add(new Todo { Status = status, What = what, Fix = fix });

        /// <summary>
        ///     Nút Copy chỉ có MỘT, và nó copy đúng thứ đang cần: id còn thiếu trên store nếu đã xác minh,
        ///     còn không thì toàn bộ id đăng ký (danh sách để dựng gói trên console).
        /// </summary>
        private void BuildCopyAction()
        {
            if (_missingIds.Count > 0)
            {
                _copyLabel = $"Copy {_missingIds.Count} id chưa có trên store";
                _copyPayload = string.Join("\n", _missingIds);
                return;
            }

            if (HasClient && _catalog.RegisteredCount > 0)
            {
                _copyLabel = $"Copy {_catalog.RegisteredCount} id đăng ký";
                _copyPayload = string.Join("\n", _catalog.RegisteredIds);
                return;
            }

            _copyLabel = "Copy id";
            _copyPayload = null;
        }

        /// <summary>Mức trạng thái + <see cref="Headline" /> của tab, suy từ danh sách việc phải làm.</summary>
        private void BuildStatus()
        {
            var errors = 0;
            var warns = 0;
            foreach (var todo in _todos)
                if (todo.Status == EzgStatus.Error) errors++;
                else if (todo.Status == EzgStatus.Warn) warns++;

            _status = errors > 0 ? EzgStatus.Error : warns > 0 ? EzgStatus.Warn : EzgStatus.Ok;

            if (!HasClient)
            {
                _headline = "Không đọc được nguồn chuẩn phía client.";
                return;
            }

            var store = _snapshot == null
                ? "chưa xác minh store"
                : !_snapshot.IsValid
                    ? "store: lỗi kết nối"
                    : $"store: {_storeReady}/{_registered} gói sẵn sàng"
                      + (_snapshot.FromCache ? " (bản lưu)" : string.Empty);

            _headline = $"{_catalog.RegisteredCount} id client đăng ký · {store}.";
        }

        private static EzgStatus Worse(EzgStatus a, EzgStatus b) => a > b ? a : b;

        /// <summary>
        ///     Danh sách tên gói trong một dòng việc-phải-làm. Cắt ở <see cref="NAMES_IN_TODO" />: dòng
        ///     liệt kê 20 tên thì tự nó thành thứ phải cuộn qua, mà chi tiết đã có ở bảng gói.
        /// </summary>
        private static string Names(List<string> names)
        {
            if (names.Count <= NAMES_IN_TODO) return string.Join(", ", names);
            return string.Join(", ", names.GetRange(0, NAMES_IN_TODO)) + $", … (+{names.Count - NAMES_IN_TODO})";
        }

        /// <summary>"vừa xong" / "12 phút trước" / "3 giờ trước" — bản lưu cũ cỡ nào phải đọc được ngay.</summary>
        private static string Age(DateTime moment)
        {
            var minutes = (DateTime.Now - moment).TotalMinutes;
            if (minutes < 1d) return "vừa xong";
            if (minutes < 60d) return $"{(int)minutes} phút trước";

            var hours = minutes / 60d;
            return hours < 24d ? $"{(int)hours} giờ trước" : $"{(int)(hours / 24d)} ngày trước";
        }

        #endregion

        #region Draw — đỉnh trang (không cuộn)

        private void DrawFixedTop()
        {
            EzgKitStyles.Banner(_headline + "  " + Advice(), _status);

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(_verifier.IsRunning || !HasClient))
                {
                    if (EzgKitStyles.PrimaryButton(
                            _verifier.IsRunning ? "Đang hỏi server…" : "Kiểm tra trên store",
                            GUILayout.Width(200f)))
                        StartVerify();
                }

                using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(_copyPayload)))
                {
                    if (EzgKitStyles.SecondaryButton(_copyLabel, GUILayout.Width(260f)))
                    {
                        // Defer: nút bấm được xử lý ở lượt sự kiện, mà _message rỗng hay không quyết định
                        // CÓ VẼ một label hay không — thêm widget giữa Layout và lượt vẽ là cửa sổ ném
                        // "Getting control N's position in a group with only M controls".
                        var payload = _copyPayload;
                        var count = _missingIds.Count > 0 ? _missingIds.Count : _catalog.RegisteredCount;
                        ReadinessActions.Defer(() =>
                        {
                            EditorGUIUtility.systemCopyBuffer = payload;
                            _message = $"Đã copy {count} id vào clipboard, mỗi id một dòng.";
                            _messageStatus = EzgStatus.Ok;
                            InternalEditorUtility.RepaintAllViews();
                        });
                    }
                }

                GUILayout.FlexibleSpace();

                using (new EditorGUI.DisabledScope(!IapStoreCache.Exists))
                {
                    if (EzgKitStyles.SecondaryButton("Xoá bản lưu", GUILayout.Width(120f)))
                        ReadinessActions.Defer(() =>
                        {
                            IapStoreCache.Clear();
                            _snapshot = null;

                            // Xoá là để KHÔNG xem số cũ nữa; muốn số mới thì bấm "Kiểm tra trên store".
                            // Không đặt false ở đây, không thì lượt tự kiểm bắn ngay sau khi vừa xoá.
                            _autoVerified = true;
                            _message = $"Đã xoá {IapStoreCache.RelativePath}.";
                            _messageStatus = EzgStatus.None;
                            _recomparePending = true;
                            InternalEditorUtility.RepaintAllViews();
                        });
                }
            }

            if (!string.IsNullOrEmpty(_message))
            {
                var previous = GUI.contentColor;
                GUI.contentColor = EzgKitStyles.ColorOf(_messageStatus);
                EditorGUILayout.LabelField(_message, EditorStyles.wordWrappedMiniLabel);
                GUI.contentColor = previous;
            }

            EzgKitStyles.CollapsibleHelp("iap-scope", "Hai nguồn của tab này — cái nào nói gì?",
                "1. CLIENT — `ShopService.GetAllProductId()` gọi qua reflection. Đây là ĐÚNG danh sách id "
                + "client gửi cho store lúc boot, nên tab lấy nó làm nguồn chuẩn: gói nào không nằm trong "
                + "danh sách đó thì người chơi không mua được, dù data có đầy đủ.\n"
                + "  • Reflection chỉ trả về chuỗi id, nên tab đọc thêm các asset collection trong project "
                + "để biết tên gói, giá tham chiếu và id của nền tảng còn lại. Asset chỉ để TRA CỨU — "
                + "\"có đăng ký hay không\" luôn do danh sách trên quyết.\n"
                + "  • Trong Editor, getter ProductId trả nhánh Android và chọn theo bundle id hiện tại "
                + "(bản premium có bundle riêng ⇒ danh sách khác). Bundle đang soi nằm ở khối \"Nguồn "
                + "client\" cuối trang.\n\n"
                + "2. STORE — server nội bộ project-ezg đọc App Store Connect + Google Play. Chỉ đọc, "
                + "server đệm 60 giây nên không có nút ép đọc lại. Đây là nguồn DUY NHẤT biết gói đã tạo "
                + "thật chưa; nguồn trên chỉ nói ý ĐỊNH bán.\n"
                + "  • Mỗi lượt kiểm được lưu vào " + IapStoreCache.RelativePath + " (theo máy, không vào "
                + "git) nên mở tab lần sau thấy ngay kết quả cũ, kèm chữ \"bản lưu\" và mốc đọc. Chưa có "
                + "bản lưu mà máy đã có API key thì tab tự kiểm một lượt lúc mở.\n"
                + "  • Nền tảng nào server đọc hụt thì tab để CHƯA BIẾT, không báo \"chưa tạo\" — báo sai "
                + "chiều đó là dev đi tạo lại và store sinh một loạt gói trùng.\n\n"
                + "Cột giá trong bảng gói là iap_cost của data (giá client hiển thị khi chưa fetch được "
                + "store), KHÔNG phải giá thật trên store.");
        }

        private string Advice()
        {
            if (!HasClient) return "Sửa trước khi làm gì khác.";
            if (_status == EzgStatus.Error) return "Có mục đỏ — bán ra là fail, sửa trước khi build store.";
            if (_status == EzgStatus.Warn) return "Chạy được, còn việc trước khi lên store.";

            return _snapshot == null
                ? "Còn một bước: bấm \"Kiểm tra trên store\"."
                : "Client và store khớp nhau.";
        }

        #endregion

        #region Draw — thân trang

        private void DrawTodos()
        {
            EzgKitStyles.SectionHeader($"Việc phải làm ({_todos.Count})",
                _todos.Count == 0 ? null : "Đỏ trước, vàng sau. Mỗi dòng là một hành động cụ thể.");

            using (new EzgKitStyles.CardScope())
            {
                if (_todos.Count == 0)
                {
                    EditorGUILayout.LabelField(
                        _snapshot == null
                            ? "Không còn việc ở phần tool kiểm được — chưa xác minh với store."
                            : "Không còn việc — client và store đều khớp.",
                        EzgKitStyles.Hint);
                    return;
                }

                for (var i = 0; i < _todos.Count; i++)
                {
                    if (i > 0) EzgKitStyles.Divider(2f);

                    var todo = _todos[i];
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EzgKitStyles.StatusIcon(todo.Status);
                        EditorGUILayout.LabelField(todo.What, EditorStyles.wordWrappedLabel);
                    }

                    if (!string.IsNullOrEmpty(todo.Fix)) Indented("→ " + todo.Fix, EditorStyles.wordWrappedLabel);
                }
            }
        }

        private void DrawGroups()
        {
            EzgKitStyles.SectionHeader(
                _groups.Count == 0
                    ? "Gói bán trong project"
                    : $"Gói bán trong project — {_catalog.Skus.Count} gói · {_registered} đang đăng ký",
                "Nhóm theo asset collection. Mỗi gói đang đăng ký cần một gói thật trên store, đúng loại.");

            if (_groups.Count == 0)
            {
                using (new EzgKitStyles.CardScope())
                {
                    EditorGUILayout.LabelField(
                        HasClient
                            ? "Không tìm thấy gói bán nào trong asset data của project."
                            : "Chưa đọc được phía client.",
                        EzgKitStyles.Hint);
                }

                return;
            }

            foreach (var group in _groups)
                using (new EzgKitStyles.CardScope())
                {
                    var open = _open[group.Name];
                    var next = EzgKitStyles.CardFoldout(open, group.Name, group.Status, group.Trailing);
                    if (next != open) _open[group.Name] = next;
                    if (!next) continue;

                    if (!string.IsNullOrEmpty(group.Note)) Indented(group.Note, EzgKitStyles.Hint);

                    for (var i = 0; i < group.Rows.Count; i++)
                    {
                        if (i > 0) EzgKitStyles.Divider(2f);
                        DrawSkuRow(group.Rows[i]);
                    }
                }
        }

        /// <summary>
        ///     Một gói = ba tầng thông tin: hàng đầu là tên + giá + hai chip trạng thái (client / store),
        ///     hàng hai là product id (copy được — dev phải dán nó sang console), rồi mỗi vấn đề một dòng.
        /// </summary>
        private void DrawSkuRow(SkuRow row)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EzgKitStyles.StatusIcon(row.Worst, row.Tooltip);
                EditorGUILayout.LabelField(row.Sku.Label, EditorStyles.boldLabel, GUILayout.Width(PACK_NAME_WIDTH));
                EditorGUILayout.LabelField(row.Price, EzgKitStyles.MutedLabel, GUILayout.Width(PRICE_WIDTH));
                GUILayout.FlexibleSpace();

                if (!string.IsNullOrEmpty(row.ClientLabel)) EzgKitStyles.Pill(row.ClientStatus, row.ClientLabel);
                if (!string.IsNullOrEmpty(row.StoreLabel)) EzgKitStyles.Pill(row.StoreStatus, row.StoreLabel);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(EzgKitStyles.ICON_WIDTH + 4f);
                EditorGUILayout.SelectableLabel(row.Ids, EzgKitStyles.Hint,
                    GUILayout.Height(EditorGUIUtility.singleLineHeight));
            }

            foreach (var note in row.Notes) Indented(note, EzgKitStyles.Hint);
        }

        /// <summary>Id client đăng ký mà không thuộc gói nào trong data — tab không biết gì thêm về chúng.</summary>
        private void DrawUnmatched()
        {
            if (!HasClient || _catalog.UnmatchedIds.Count == 0) return;

            EzgKitStyles.SectionHeader($"Id đăng ký không map được về data ({_catalog.UnmatchedIds.Count})",
                $"`{_catalog.SourceLabel}` trả về những id này, nhưng không asset collection nào trong "
                + "project chứa chúng.");

            using (new EzgKitStyles.CardScope())
            {
                foreach (var id in _catalog.UnmatchedIds)
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EzgKitStyles.StatusIcon(EzgStatus.Warn);
                        EditorGUILayout.SelectableLabel(id, EzgKitStyles.ValueStyle,
                            GUILayout.Height(EditorGUIUtility.singleLineHeight));
                    }
            }
        }

        /// <summary>
        ///     Chiều NGƯỢC của lượt đối chiếu: store có, client chưa/không đăng ký. Khối này hiện MỌI LÚC
        ///     khi đã xác minh, kể cả lúc sạch — ẩn đi thì không ai biết chiều này có được kiểm hay không,
        ///     mà "không thấy gì" đọc y hệt "tool không kiểm".
        /// </summary>
        private void DrawExtraOnStore()
        {
            if (!HasStore) return;

            EzgKitStyles.SectionHeader($"Có trên store mà client KHÔNG đăng ký ({_extraOnStore.Count})",
                "Id sống trên store nhưng client không gửi lên lúc boot — gói cũ của bản trước, gói tạo tay, "
                + "hay id gõ sai. Người chơi không mua được qua game; mà gói trên store thì không xoá được, "
                + "chỉ tắt được.");

            using (new EzgKitStyles.CardScope())
            {
                if (_extraOnStore.Count == 0)
                {
                    EditorGUILayout.LabelField(
                        $"Không có — cả {_snapshot.Rows.Count} dòng trong danh mục store đều khớp id client đăng ký.",
                        EzgKitStyles.Hint);
                    return;
                }

                foreach (var row in _extraOnStore)
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EzgKitStyles.StatusIcon(EzgStatus.Warn);
                        EditorGUILayout.SelectableLabel(row, EzgKitStyles.ValueStyle,
                            GUILayout.Height(EditorGUIUtility.singleLineHeight));
                    }
            }
        }

        #endregion

        #region Draw — nguồn dữ liệu

        private void DrawClientSource()
        {
            EzgKitStyles.SectionHeader("Nguồn client (nguồn chuẩn)",
                "Tab không có ô cấu hình nào ở đây: nguồn là một hàm trong code game, không phải một đường "
                + "dẫn ai đó phải điền.");

            using (new EzgKitStyles.CardScope())
            {
                EzgKitStyles.KeyValue("Hàm nguồn", _catalog?.SourceLabel, EzgStatus.None,
                    "Đúng hàm client gọi lúc dựng danh sách product gửi store.",
                    "không tìm thấy trong project");

                if (!HasClient)
                {
                    EditorGUILayout.LabelField(_catalog?.Error, EditorStyles.wordWrappedMiniLabel);
                    return;
                }

                EzgKitStyles.KeyValue("Bundle đang soi", _catalog.BundleId, EzgStatus.None,
                    "Trong Editor, getter ProductId trả id theo bundle này — bản premium có bundle riêng nên "
                    + "danh sách id sẽ khác. Đổi bundle ở Player Settings rồi bấm \"Làm mới trạng thái\".");

                EzgKitStyles.KeyValue("Id đang đăng ký",
                    _catalog.RegisteredCount.ToString(CultureInfo.InvariantCulture) + " id");

                EzgKitStyles.KeyValue("Non-Consumable client khai",
                    _catalog.NonConsumableSource == null
                        ? null
                        : $"{_catalog.NonConsumableCount} id  ·  {_catalog.NonConsumableSource}",
                    _catalog.NonConsumableSource == null ? EzgStatus.Warn : EzgStatus.None,
                    "Lấy từ đúng hàm client dùng lúc dựng ProductDefinition — tab KHÔNG suy loại theo tên gói.",
                    "không đọc được (project không có IPurchasing.GetNonConsumableProducts)");

                EzgKitStyles.KeyValue("Data đã đọc",
                    $"{_catalog.PackAssetCount} asset collection  ·  {_catalog.Skus.Count} gói",
                    EzgStatus.None,
                    "Asset chứa gói bán được tìm theo cấu trúc (class có cả googleProductId lẫn "
                    + "appleProductId), không theo đường dẫn — chỉ dùng để tra tên gói, giá và id nền tảng "
                    + "còn lại.");
            }
        }

        /// <summary>
        ///     Khối cấu hình lượt xác minh store. Nằm CUỐI trang vì nó là thứ điền một lần rồi thôi, còn
        ///     kết quả thì đã nằm ở trên (chip từng dòng + khối việc phải làm).
        /// </summary>
        private void DrawStoreSource()
        {
            EzgKitStyles.SectionHeader("Xác minh với store (server nội bộ)",
                "API chỉ-đọc của project-ezg đọc App Store Connect + Google Play. Chỉ cần API KEY: đường gọi "
                + "nằm trong code (giống nhau ở mọi dự án) và dự án nào là do chính key quyết định. Key nhớ ở "
                + "EditorPrefs theo máy — KHÔNG đi vào repo.");

            using (new EzgKitStyles.CardScope())
            {
                DrawStoreState();
                EzgKitStyles.Links(("Play Console", URL_PLAY_CONSOLE), ("App Store Connect", URL_ASC));
            }

            var currentKey = IapVerifyConfig.ApiKey;
            var key = SetupGui.ManualPasswordField(
                IapVerifyConfig.ApiKeyFromEnvironment ? "API key  (từ biến môi trường)" : "API key",
                currentKey,
                "Xin ở project-ezg → Dự án → (chọn dự án) → tab API key. Key chỉ hiện ĐÚNG MỘT LẦN lúc tạo "
                + "(server lưu hash, không ai xem lại được, kể cả admin) — mất thì xin cấp lại.\n\n"
                + "Lưu ở EditorPrefs theo từng project trên máy này, nên KHÔNG đi vào git. Máy CI thì đặt "
                + "biến môi trường " + IapVerifyConfig.ENV_API_KEY + " thay vì dán vào đây.\n\n"
                + "Key chỉ ĐỌC: API không tạo, không sửa, không xoá gì trên store.\n\n"
                + "Đổi server (dev tại máy " + IapVerifyConfig.LOCAL_API_URL + ", staging): đặt biến môi trường "
                + IapVerifyConfig.ENV_API_URL + " trước khi mở Unity. Không có ô nhập link — đường gọi là một "
                + "hằng, một ô cho nó chỉ thêm chỗ gõ sai.");
            if (key != currentKey)
            {
                IapVerifyConfig.ApiKey = key;

                // Đổi key là đổi dự án: bản lưu của key cũ không còn nói về store của mình nữa.
                _snapshot = null;
                _autoVerified = false;
                _reloadPending = true;
            }
        }

        /// <summary>Trạng thái lượt hỏi gần nhất: chưa hỏi / bản lưu / lỗi / đọc được nền tảng nào, lúc nào.</summary>
        private void DrawStoreState()
        {
            // Key nào đang được dùng — chỉ phần đầu, đủ phân biệt và không đủ để lộ (tab kit hay bị share
            // screen lúc họp). Đứng trước mọi nhánh vì đây là câu hỏi đầu tiên khi gặp 401.
            EzgKitStyles.KeyValue("Key đang dùng", IapVerifyConfig.ApiKeyHint,
                string.IsNullOrEmpty(IapVerifyConfig.ApiKeyHint) ? EzgStatus.Warn : EzgStatus.None,
                IapVerifyConfig.ApiKeyFromEnvironment
                    ? "Đọc từ biến môi trường " + IapVerifyConfig.ENV_API_KEY + " (không phải từ ô bên dưới)."
                    : null,
                "chưa có key — điền ở ô bên dưới");

            if (_verifier.IsRunning)
            {
                EzgKitStyles.KeyValue("Lượt hỏi", "đang gọi " + IapVerifyConfig.ApiUrl, EzgStatus.None);
                return;
            }

            if (_snapshot == null)
            {
                EzgKitStyles.KeyValue("Lượt hỏi", null, EzgStatus.None,
                    string.IsNullOrWhiteSpace(IapVerifyConfig.ApiKey)
                        ? "Dán API key vào ô bên dưới — tab tự kiểm một lượt ngay sau đó."
                        : "Bấm \"Kiểm tra trên store\" ở đỉnh trang.",
                    "chưa có bản lưu nào");
                return;
            }

            if (!_snapshot.IsValid)
            {
                EzgKitStyles.KeyValue("Lượt hỏi", _snapshot.Error, EzgStatus.Error,
                    "0 = không nối được (link sai / server chưa bật / mất mạng) · 401 = key sai, hết hạn "
                    + "hoặc đã thu hồi · 404 = dự án của key không còn · 503 = server chưa đọc được store "
                    + "(CHƯA BIẾT — đừng kết luận gói chưa tạo).");
                return;
            }

            EzgKitStyles.KeyValue("Bản lưu",
                _snapshot.FromCache
                    ? $"đọc từ {IapStoreCache.RelativePath} · lưu {Age(_snapshot.FetchedAtLocal)}"
                    : $"lượt hỏi của phiên này · {Age(_snapshot.FetchedAtLocal)} · đã lưu lại",
                EzgStatus.None,
                "Mỗi lượt kiểm được lưu để mở tab lần sau khỏi gọi mạng. Muốn số mới thì bấm \"Kiểm tra "
                + "trên store\"; server đệm 60 giây nên hỏi dày hơn cũng nhận lại đúng bản này.");

            // Mã dự án là DỮ KIỆN do key quyết định, không phải thứ tool đi so với một ô gõ tay. Bày ra để
            // người dùng nhìn thấy mình đang soi danh mục của dự án nào; lượt kiểm "có dán nhầm key không"
            // đi bằng độ khớp danh mục (xem BuildStoreTodos).
            EzgKitStyles.KeyValue("Dự án của key", _snapshot.Project, EzgStatus.None,
                IapVerifyConfig.IsLocalApi
                    ? "Link đang trỏ về localhost — đây là server dev tại máy này, không phải server thật."
                    : null);

            EzgKitStyles.KeyValue("Khớp danh mục", $"{_storeMatched}/{_registered} gói client có mặt trên store",
                _registered > 0 && _storeMatched == 0 && _snapshot.Rows.Count > 0
                    ? EzgStatus.Error
                    : EzgStatus.None,
                "0/N mà danh mục store lại có gói = gần như chắc chắn key của dự án khác.");

            EzgKitStyles.KeyValue("Store đọc lúc", _snapshot.CheckedAtLabel, EzgStatus.None);

            foreach (var platform in _snapshot.Platforms)
            {
                var status = !platform.Ok ? EzgStatus.Warn : platform.Stale ? EzgStatus.Warn : EzgStatus.Ok;
                var value = !platform.Ok
                    ? "KHÔNG đọc được — bỏ qua khi đối chiếu"
                    : platform.Stale
                        ? "đọc được (bản lưu của server, dữ liệu cũ)"
                        : "đọc được";

                EzgKitStyles.KeyValue(platform.Platform, value, status,
                    string.IsNullOrEmpty(platform.Error) ? null : platform.Error);
            }

            if (!string.IsNullOrEmpty(_snapshot.KeyExpiresAt))
                EzgKitStyles.KeyValue("API key hết hạn", _snapshot.KeyExpiresAt,
                    _snapshot.KeyExpiringSoon(out _) ? EzgStatus.Warn : EzgStatus.None);
        }

        private static void Indented(string text, GUIStyle style)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(EzgKitStyles.ICON_WIDTH + 4f);
                EditorGUILayout.LabelField(text, style);
            }
        }

        #endregion

        #region Verify

        /// <summary>
        ///     Hỏi server. Kết quả KHÔNG được đổ thẳng vào <see cref="_snapshot" /> trong callback:
        ///     callback chạy từ <see cref="EditorApplication.update" />, có thể rơi vào giữa lượt vẽ, và
        ///     số widget đổi giữa Layout với Repaint là cửa sổ ném exception. Nó đợi ở
        ///     <see cref="_pendingSnapshot" /> tới đầu lượt <see cref="Draw" /> kế tiếp.
        /// </summary>
        private void StartVerify()
        {
            _message = null;
            _messageStatus = EzgStatus.None;

            _verifier.Fetch(IapVerifyConfig.ApiUrl, IapVerifyConfig.ApiKey,
                snapshot => _pendingSnapshot = snapshot);
        }

        #endregion
    }
}
#endif
