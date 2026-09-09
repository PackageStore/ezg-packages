#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Ezg.Editor.Shared.EzgKit;
using Ezg.Editor.Shared.Readiness;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace Ezg.Editor.Shared.Iap
{
    /// <summary>
    ///     Tab <b>IAP</b> — một chỗ trả lời ba câu hỏi về gói bán, theo đúng thứ tự chúng đi:
    ///     <list type="number">
    ///         <item><b>Client đăng ký SKU nào?</b> đọc <c>ShopPackCatalog</c> — đúng nguồn
    ///         <c>ShopService.IapProductIds</c> dùng lúc boot, nên đây là danh sách THẬT client gửi store.</item>
    ///         <item><b>Bảng giá GD đã có đủ chưa?</b> đối chiếu file <c>.xlsx</c> sheet "List gói bán",
    ///         và điền dòng thiếu bằng một nút.</item>
    ///         <item><b>Store đã tạo chưa?</b> hỏi server nội bộ (<see cref="IapStoreVerifier" />) —
    ///         thứ Unity không có cách nào tự thấy.</item>
    ///     </list>
    ///     <para>
    ///         <b>Bố cục:</b> hành động + trạng thái ở đỉnh (không cuộn); trong scroll thì "Việc phải
    ///         làm" trước, bảng SKU chi tiết sau, cấu hình nguồn và việc-tay-ngoài-Unity cuối. Mỗi bảng
    ///         pack là một khối gấp được — 24 dòng phẳng như bản trước thì mắt không bắt được 2 dòng
    ///         thật sự có vấn đề, mà bảng đang TẮT lại nằm lẫn với bảng đang bán.
    ///     </para>
    ///     <para>
    ///         <b>Mọi phán quyết dựng sẵn trong <see cref="Compare" /></b> (kể cả chuỗi nhãn và
    ///         <see cref="Headline" />): cửa sổ đọc <see cref="Status" />/<see cref="Headline" /> mỗi
    ///         lượt OnGUI của MỌI tab, và số widget vẽ ra không được đổi giữa lượt Layout và Repaint.
    ///     </para>
    ///     <para>
    ///         Mở tab chỉ ĐỌC. Hai thứ gọi ra ngoài đều là nút bấm: ghi <c>.xlsx</c>, và hỏi server.
    ///     </para>
    /// </summary>
    internal class IapSetupPage : IEzgKitPage
    {
        #region Constants

        private const string SOURCE_FILE = "ProjectSettings/IapPriceSource.json";
        private const string BACKUP_DIR = "Library/EzgKit/IapPriceBackup";

        /// <summary>Thư mục quy ước chứa bảng giá trong project — nơi tạo file mới và ưu tiên khi tự dò.</summary>
        private const string DEFAULT_FOLDER = "Assets/_Project/IapPrice";

        /// <summary>Tên file quy ước (giữ đúng chính tả GD đang dùng) — cùng tên ở mọi dự án để tool/GD không phải đoán.</summary>
        private const string DEFAULT_FILE_NAME = "Monitization.xlsx";

        /// <summary>Đường dẫn tạo file mới khi dự án chưa có bảng giá.</summary>
        private const string DEFAULT_PATH = DEFAULT_FOLDER + "/" + DEFAULT_FILE_NAME;

        private const string URL_PLAY_CONSOLE = "https://play.google.com/console/";
        private const string URL_ASC = "https://appstoreconnect.apple.com/apps";

        /// <summary>Số tên gói in ra trong một dòng "Việc phải làm" trước khi cắt bằng "…".</summary>
        private const int NAMES_IN_TODO = 6;

        private const float PACK_NAME_WIDTH = 172f;
        private const float PRICE_WIDTH = 62f;

        #endregion

        #region Types

        [Serializable]
        private class Source
        {
            /// <summary>Đường dẫn file .xlsx — tương đối với thư mục project nếu nằm trong project.</summary>
            public string xlsxPath;
        }

        /// <summary>
        ///     Một SKU cùng MỌI phán quyết về nó, dựng sẵn trong <see cref="Compare" />. Vẽ chỉ đọc —
        ///     không tính lại, không nối chuỗi trong lúc vẽ.
        /// </summary>
        private sealed class SkuRow
        {
            internal IapSku Sku;

            /// <summary>Nhãn giá dựng sẵn (<c>$4.99</c>).</summary>
            internal string Price;

            /// <summary>Product id để copy: một dòng nếu apple = google, không thì ghi rõ hai bên.</summary>
            internal string ProductIds;

            internal EzgStatus SheetStatus;
            internal string SheetLabel;

            /// <summary><see cref="EzgStatus.None" /> = chưa hỏi server, hoặc bảng đang tắt.</summary>
            internal EzgStatus StoreStatus;

            internal string StoreLabel;

            /// <summary>Mức nặng nhất trong hàng — icon đầu dòng lấy cái này.</summary>
            internal EzgStatus Worst;

            internal string Tooltip;

            internal readonly List<string> Notes = new();
        }

        /// <summary>Một bảng pack trong catalog = một khối gấp được.</summary>
        private sealed class TableGroup
        {
            internal string Label;
            internal bool Enabled;
            internal bool NonConsumable;
            internal EzgStatus Status;

            /// <summary>Chữ đếm bên phải hàng tiêu đề (loại SKU · số gói · ghi chú bảng tắt).</summary>
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

        #region Fields — catalog + sheet

        private List<IapSku> _skus;
        private readonly List<IapSku> _enabledSkus = new();
        private string _catalogPath;
        private string _xlsxPath;
        private IapPriceSheet _sheet;

        private readonly List<IapPriceRow> _orphans = new();
        private int _missingRows;
        private int _missingAndroid;
        private int _missingIos;
        private int _emptyPriceRows;

        #endregion

        #region Fields — store

        private readonly IapStoreVerifier _verifier = new();
        private IapStoreSnapshot _snapshot;

        /// <summary>Có trên store mà client KHÔNG đăng ký — chiều ngược lại của lượt đối chiếu.</summary>
        private readonly List<string> _extraOnStore = new();

        /// <summary>
        ///     Số SKU đang dùng TÌM THẤY trong danh mục store (đã setup xong hay chưa không quan trọng).
        ///     Dùng cho lượt kiểm độ khớp — thay cho ô "mã dự án" gõ tay, xem <c>BuildStoreTodos</c>.
        /// </summary>
        private int _storeMatched;

        /// <summary>Lượt hỏi server đã xong, chờ đổ vào snapshot ở đầu lượt Draw kế tiếp.</summary>
        private IapStoreSnapshot _pendingSnapshot;

        /// <summary>
        ///     Cần tính lại phán quyết (<see cref="Compare" />) mà KHÔNG cần đọc lại catalog + .xlsx.
        ///     Tách khỏi <see cref="_reloadPending" /> vì <see cref="Reload" /> mở lại file zip .xlsx 19
        ///     sheet của GD — snapshot store về thì chỉ có phán quyết đổi, bảng giá không đổi một byte.
        /// </summary>
        private bool _recomparePending;

        #endregion

        #region Fields — dựng sẵn cho lúc vẽ

        private readonly List<TableGroup> _tables = new();
        private readonly List<Todo> _todos = new();
        private readonly Dictionary<string, bool> _openTables = new();

        private string _headline = "Chưa đọc trạng thái.";
        private EzgStatus _status = EzgStatus.Warn;

        /// <summary>Nhãn nút ghi — nói rõ ghi vào FILE NÀO, để không ai bấm mà chưa biết đích.</summary>
        private string _syncLabel = "Load SKU vào bảng giá";

        private string _skuTrailing = string.Empty;

        private Vector2 _scroll;
        private string _message;
        private EzgStatus _messageStatus = EzgStatus.None;
        private bool _reloadPending;

        #endregion

        #region Page

        public string Title => "IAP";

        public string Subtitle =>
            "SKU client đăng ký (ShopPackCatalog) → bảng giá GD (.xlsx) → gói đã có thật trên store (server nội bộ).";

        public string RunAllLabel => "Load SKU dang dung vao bang gia IAP (.xlsx)";

        public string Headline => _headline;

        public EzgStatus Status => _status;

        private bool HasSheet => _sheet != null && _sheet.IsValid;

        private bool HasStore => _snapshot != null && _snapshot.IsValid;

        public void Reload()
        {
            _skus = IapSkuCatalog.Collect(out _catalogPath);
            _enabledSkus.Clear();
            foreach (var sku in _skus)
                if (sku.Enabled && !string.IsNullOrEmpty(sku.GoogleId) && !string.IsNullOrEmpty(sku.AppleId))
                    _enabledSkus.Add(sku);

            _xlsxPath = LoadSource() ?? AutoFindSheet();
            _sheet = string.IsNullOrEmpty(_xlsxPath) || !File.Exists(Absolute(_xlsxPath))
                ? null
                : IapPriceSheet.Load(Absolute(_xlsxPath));

            Compare();
        }

        public void Draw()
        {
            // Snapshot đổi ở ĐẦU lượt vẽ, không đổi giữa lượt: số widget phải giống nhau giữa Layout
            // và Repaint, không thì Unity ném "Getting control N's position in a group with only M".
            if (_pendingSnapshot != null)
            {
                _snapshot = _pendingSnapshot;
                _pendingSnapshot = null;
                // Câu báo dựng Ở ĐÂY, không dựng trong callback: callback chạy từ
                // EditorApplication.update, có thể rơi vào giữa lượt vẽ, mà _message rỗng hay không
                // quyết định CÓ VẼ một label hay không — đổi số widget giữa Layout và Repaint.
                _message = _snapshot.IsValid
                    ? $"Đã đọc danh mục store của dự án \"{_snapshot.Project}\": {_snapshot.Rows.Count} dòng, "
                      + $"{_snapshot.UsablePlatformCount}/{_snapshot.Platforms.Count} nền tảng đọc được "
                      + $"(store đọc lúc {_snapshot.CheckedAtLabel})."
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

            if (_skus == null) Reload();

            DrawFixedTop();

            using (var scroll = new EditorGUILayout.ScrollViewScope(_scroll))
            {
                _scroll = scroll.scrollPosition;
                DrawTodos();
                DrawTables();
                DrawExtraOnStore();
                DrawOrphans();
                DrawStoreSource();
                DrawSheetSource();
                DrawManualSteps();
            }
        }

        public bool RunAll()
        {
            if (_skus == null) Reload();
            if (_catalogPath == null || _enabledSkus.Count == 0)
            {
                _message = "Bo qua: chua co ShopPackCatalog hoac catalog khong co goi IAP nao dang bat.";
                _messageStatus = EzgStatus.Warn;
                return true;
            }

            if (_sheet != null && !_sheet.IsValid)
            {
                _message = "Bo qua: " + _sheet.Error;
                _messageStatus = EzgStatus.Warn;
                return true;
            }

            // Cố ý KHÔNG hỏi server trong luồng "chạy hết": bước đó ghi file, mà xác minh store chỉ
            // là thông tin đọc — gọi mạng ở giữa là thêm một chỗ hỏng cho một việc không cần thiết.
            return SyncAll();
        }

        #endregion

        #region Compare

        /// <summary>
        ///     Chụp toàn bộ trạng thái thành dữ liệu vẽ sẵn: từng bảng, từng SKU, khối việc phải làm,
        ///     nhãn nút, <see cref="Headline" /> và <see cref="Status" />. Chạy trong
        ///     <see cref="Reload" />, KHÔNG chạy lúc vẽ.
        /// </summary>
        private void Compare()
        {
            _orphans.Clear();
            _tables.Clear();
            _todos.Clear();
            _extraOnStore.Clear();
            _missingRows = 0;
            _missingAndroid = 0;
            _missingIos = 0;
            _emptyPriceRows = 0;
            _storeMatched = 0;

            var known = new HashSet<string>();
            var noProductId = new List<string>();
            var typeMismatch = new List<string>();
            var storeIdIssue = new List<string>();
            var missingOnStore = new List<string>();
            var incompleteOnStore = new List<string>();
            var inactiveOnStore = new List<string>();

            foreach (var sku in _skus)
            {
                var group = GroupFor(sku);
                var row = new SkuRow
                {
                    Sku = sku,
                    Price = sku.Cost > 0f
                        ? "$" + sku.Cost.ToString("0.00", CultureInfo.InvariantCulture)
                        : "—",
                    ProductIds = sku.AppleId == sku.GoogleId
                        ? sku.GoogleId
                        : $"android: {sku.GoogleId}   ·   ios: {sku.AppleId}",
                };
                group.Rows.Add(row);

                if (!sku.Enabled)
                {
                    row.SheetStatus = EzgStatus.None;
                    row.SheetLabel = "không đăng ký";
                    row.Worst = EzgStatus.None;
                    row.Tooltip = "Bảng tắt trong catalog — không đăng ký store, không đưa vào bảng giá.";
                    row.Notes.Add("Bảng đang TẮT (isEnabled = false): gói template tạm bỏ, không gửi store.");
                    // Bảng tắt vẫn được soi trên store: id còn sống trên console mà client không đăng
                    // ký nữa là drift đáng biết, không phải chuyện bỏ qua.
                    ApplyStore(row, sku, null, null, null);
                    continue;
                }

                if (string.IsNullOrEmpty(sku.GoogleId) || string.IsNullOrEmpty(sku.AppleId))
                {
                    row.SheetStatus = EzgStatus.Error;
                    row.SheetLabel = "thiếu product id";
                    row.Worst = EzgStatus.Error;
                    row.Tooltip = "Thiếu product id trong CSV — tool bỏ qua gói này khi load.";
                    row.Notes.Add("Thiếu google/apple product id trong CSV — sửa CSV rồi import lại.");
                    noProductId.Add(sku.PackName);
                    continue;
                }

                known.Add(IapPriceSheet.RowKey(IapPriceSheet.PLATFORM_ANDROID, sku.GoogleId));
                known.Add(IapPriceSheet.RowKey(IapPriceSheet.PLATFORM_IOS, sku.AppleId));

                CheckSheet(row, sku, typeMismatch, storeIdIssue);
                ApplyStore(row, sku, missingOnStore, incompleteOnStore, inactiveOnStore);

                row.Worst = Worse(row.SheetStatus, row.StoreStatus);
            }

            foreach (var group in _tables) FinishGroup(group);

            if (HasSheet)
                foreach (var sheetRow in _sheet.Rows)
                    if (!known.Contains(sheetRow.Key)) _orphans.Add(sheetRow);

            if (HasStore) CollectExtraOnStore(known);

            // Nhãn nút TRƯỚC danh sách việc: một dòng việc phải nói "bấm <đúng chữ trên nút>", mà chữ
            // đó lại phụ thuộc số dòng thiếu vừa đếm xong ở trên.
            BuildActionLabels();
            BuildTodos(noProductId, typeMismatch, storeIdIssue, missingOnStore, incompleteOnStore,
                inactiveOnStore);

            // Đỏ trước vàng sau: người đọc xử lý theo đúng thứ tự đó. Sắp MỘT LẦN ở đây, không sắp
            // trong lúc vẽ.
            _todos.Sort((a, b) => b.Status.CompareTo(a.Status));
            BuildStatus();
        }

        private TableGroup GroupFor(IapSku sku)
        {
            // Gộp theo nhãn bảng, giữ nguyên thứ tự dòng trong catalog (= thứ tự thẻ trên màn shop).
            for (var i = _tables.Count - 1; i >= 0; i--)
                if (_tables[i].Label == sku.TableLabel)
                    return _tables[i];

            var group = new TableGroup
            {
                Label = sku.TableLabel,
                Enabled = sku.Enabled,
                NonConsumable = sku.NonConsumable,
            };
            _tables.Add(group);
            return group;
        }

        /// <summary>Đối chiếu một SKU với bảng giá GD: đủ hai dòng android + ios, đúng loại, có giá.</summary>
        private void CheckSheet(SkuRow row, IapSku sku, List<string> typeMismatch, List<string> storeIdIssue)
        {
            if (!HasSheet)
            {
                row.SheetStatus = EzgStatus.None;
                row.SheetLabel = "chưa có bảng giá";
                row.Notes.Add("Chưa có bảng giá — sẽ được thêm khi bấm Load.");
                return;
            }

            var status = EzgStatus.Ok;
            var missing = 0;
            var noPrice = 0;

            foreach (var platform in Platforms)
            {
                var productId = platform == IapPriceSheet.PLATFORM_IOS ? sku.AppleId : sku.GoogleId;
                var sheetRow = _sheet.Find(platform, productId);

                if (sheetRow == null)
                {
                    _missingRows++;
                    missing++;
                    if (platform == IapPriceSheet.PLATFORM_ANDROID) _missingAndroid++;
                    else _missingIos++;
                    row.Notes.Add($"{platform}: thiếu dòng trong bảng giá.");
                    status = Worse(status, EzgStatus.Warn);
                    continue;
                }

                if (!string.IsNullOrEmpty(sheetRow.ProductType)
                    && !string.Equals(sheetRow.ProductType, sku.ProductType, StringComparison.OrdinalIgnoreCase))
                {
                    row.Notes.Add($"{platform}: product_type trong sheet `{sheetRow.ProductType}` ≠ catalog `{sku.ProductType}`.");
                    typeMismatch.Add(sku.PackName);
                    status = Worse(status, EzgStatus.Warn);
                }

                if (platform == IapPriceSheet.PLATFORM_ANDROID && sheetRow.StoreProductId != productId)
                {
                    row.Notes.Add("android: store_product_id trống hoặc lệch product_id.");
                    storeIdIssue.Add(sku.PackName);
                    status = Worse(status, EzgStatus.Warn);
                }
                else if (platform == IapPriceSheet.PLATFORM_IOS && !string.IsNullOrEmpty(sheetRow.StoreProductId))
                {
                    row.Notes.Add("ios: store_product_id phải để trống (ASC không có trường này).");
                    storeIdIssue.Add(sku.PackName);
                    status = Worse(status, EzgStatus.Warn);
                }

                if (IapPriceSheet.IsPriceEmpty(sheetRow.DefaultPrice))
                {
                    _emptyPriceRows++;
                    noPrice++;
                    status = Worse(status, EzgStatus.Warn);
                }
            }

            if (noPrice > 0)
                row.Notes.Add(noPrice == 2
                    ? "Cả hai dòng chưa có giá — GD điền vào cột default_price."
                    : "Một dòng chưa có giá — GD điền vào cột default_price.");

            row.SheetStatus = status;
            row.SheetLabel = missing > 0
                ? missing == 2 ? "sheet: thiếu cả 2" : "sheet: thiếu 1 dòng"
                : noPrice > 0
                    ? "sheet: chưa có giá"
                    : status == EzgStatus.Ok
                        ? "sheet đủ"
                        : "sheet: lệch";
        }

        /// <summary>
        ///     Gắn phán quyết store vào một hàng. Danh sách <c>null</c> = không gom vào việc phải làm
        ///     (dùng cho bảng đang tắt: vẫn hiện trạng thái, nhưng không phải việc của ai).
        /// </summary>
        private void ApplyStore(SkuRow row, IapSku sku, List<string> missing, List<string> incomplete,
            List<string> inactive)
        {
            if (!HasStore)
            {
                row.StoreStatus = EzgStatus.None;
                row.StoreLabel = null;
                return;
            }

            var worst = IapStoreState.Unknown;
            var unknownPlatforms = 0;
            var foundOnStore = false;

            foreach (var platform in Platforms)
            {
                var productId = platform == IapPriceSheet.PLATFORM_IOS ? sku.AppleId : sku.GoogleId;
                if (string.IsNullOrEmpty(productId)) continue;

                var state = _snapshot.StateOf(platform, productId);
                if (state == IapStoreState.Unknown)
                {
                    unknownPlatforms++;
                    continue;
                }

                // Có mặt trong danh mục (bất kể đã setup xong chưa) — dữ kiện cho lượt kiểm độ khớp
                // ở BuildStoreTodos, thứ thay cho ô "mã dự án" gõ tay.
                if (state != IapStoreState.Missing) foundOnStore = true;

                // Gói của bảng TẮT mà không có trên store là trạng thái ĐÚNG như mong đợi — dòng
                // "chưa có trên store" ở đây chỉ là 2 dòng nhiễu mỗi gói, trong khi note "bảng đang
                // tắt" đã nói hết. Ngược lại, nó VẪN CÒN trên store thì đáng ghi.
                if (state != IapStoreState.Ready && (sku.Enabled || state != IapStoreState.Missing))
                {
                    var storeRow = _snapshot.RowOf(platform, productId);
                    var raw = storeRow == null || string.IsNullOrEmpty(storeRow.StoreStatus)
                        ? string.Empty
                        : $" ({storeRow.StoreStatus})";
                    row.Notes.Add($"{platform}: {IapStoreVerifier.Label(state)}{raw}.");
                }

                if (state > worst) worst = state;
            }

            // Bảng tắt: id còn sống trên store là DỮ KIỆN, không phải việc của ai — client không đăng
            // ký nó nữa nên không có gì fail. Giữ mức None (chip xám, đúng màu icon đầu dòng) để bảng
            // tắt không nhấp nháy cảnh báo; phần drift được báo đúng một lần ở khối
            // "Có trên store mà client không đăng ký".
            if (!sku.Enabled)
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

            if (unknownPlatforms > 0 && worst == IapStoreState.Unknown)
                row.Notes.Add("Server chưa đọc được nền tảng nào — chưa biết gói này có trên store hay không.");

            switch (worst)
            {
                case IapStoreState.Missing:
                    missing?.Add(sku.PackName);
                    break;
                case IapStoreState.Incomplete:
                    incomplete?.Add(sku.PackName);
                    break;
                case IapStoreState.Inactive:
                    inactive?.Add(sku.PackName);
                    break;
            }
        }

        /// <summary>
        ///     Id có trên store mà client không đăng ký. Tách riêng gói của bảng ĐANG TẮT (biết được
        ///     nó là gói template đã bỏ) với id lạ hoàn toàn (gói dự án khác? tạo tay? sai chính tả?).
        /// </summary>
        private void CollectExtraOnStore(HashSet<string> known)
        {
            var disabled = new HashSet<string>();
            foreach (var sku in _skus)
            {
                if (sku.Enabled) continue;
                if (!string.IsNullOrEmpty(sku.GoogleId))
                    disabled.Add(IapPriceSheet.RowKey(IapPriceSheet.PLATFORM_ANDROID, sku.GoogleId));
                if (!string.IsNullOrEmpty(sku.AppleId))
                    disabled.Add(IapPriceSheet.RowKey(IapPriceSheet.PLATFORM_IOS, sku.AppleId));
            }

            foreach (var storeRow in _snapshot.Rows)
            {
                if (known.Contains(storeRow.Key)) continue;

                var tail = disabled.Contains(storeRow.Key)
                    ? "  (bảng đang tắt trong catalog)"
                    : string.Empty;
                _extraOnStore.Add($"{storeRow.Platform}  ·  {storeRow.ProductId}  ·  {storeRow.StoreStatus}{tail}");
            }
        }

        private void FinishGroup(TableGroup group)
        {
            var worst = EzgStatus.None;
            var pending = 0;
            foreach (var row in group.Rows)
            {
                if (row.Worst is EzgStatus.Warn or EzgStatus.Error) pending++;
                worst = Worse(worst, row.Worst);
            }

            group.Status = worst;
            var kind = group.NonConsumable ? "Non-Consumable" : "Consumable";
            group.Trailing = group.Enabled
                ? pending == 0
                    ? $"{kind} · {group.Rows.Count} SKU"
                    : $"{kind} · {group.Rows.Count} SKU · {pending} cần xử lý"
                : $"{kind} · {group.Rows.Count} SKU · KHÔNG đăng ký";

            // Bảng còn việc thì mở sẵn; bảng xanh hoặc bảng tắt thì gấp — trạng thái do người dùng
            // bấm sau đó được giữ nguyên qua các lượt Reload.
            if (!_openTables.ContainsKey(group.Label)) _openTables[group.Label] = group.Enabled && pending > 0;
        }

        #endregion

        #region Compare — việc phải làm + nhãn

        private void BuildTodos(List<string> noProductId, List<string> typeMismatch,
            List<string> storeIdIssue, List<string> missingOnStore, List<string> incompleteOnStore,
            List<string> inactiveOnStore)
        {
            if (_catalogPath == null)
            {
                Add(EzgStatus.Error, "Chưa có ShopPackCatalog trong project",
                    "tạo `Resources/shop_pack_catalog.asset` rồi kéo các bảng pack vào — không có nó thì "
                    + "không ai biết client đăng ký SKU nào.");
                return;
            }

            if (_sheet != null && !_sheet.IsValid)
                Add(EzgStatus.Error, "Không đọc được bảng giá: " + _sheet.Error,
                    "kiểm lại file .xlsx ở khối \"Bảng giá của GD\" cuối trang.");

            if (noProductId.Count > 0)
                Add(EzgStatus.Error, $"{noProductId.Count} gói thiếu product id: {Names(noProductId)}",
                    "điền google_product_id / apple_product_id trong CSV rồi import lại; tool bỏ qua gói "
                    + "thiếu id khi load bảng giá.");

            if (!HasSheet && _enabledSkus.Count > 0)
                Add(EzgStatus.Warn, $"Dự án chưa có bảng giá cho {_enabledSkus.Count} SKU đang dùng",
                    $"bấm \"{_syncLabel}\" — tool tạo {DEFAULT_PATH} rồi điền đủ dòng.");

            if (_missingRows > 0)
                Add(EzgStatus.Warn,
                    $"Bảng giá thiếu {_missingRows} dòng ({_missingAndroid} android · {_missingIos} ios)",
                    "bấm nút Load lại ở đỉnh trang — dòng GD đã điền giá không bị đụng.");

            if (_emptyPriceRows > 0)
                Add(EzgStatus.Warn, $"{_emptyPriceRows} dòng chưa có giá trong bảng giá",
                    "GD thay chữ \"" + IapPriceSheet.PRICE_PLACEHOLDER + "\" ở cột default_price bằng giá "
                    + "thật theo tier từng store. Tool KHÔNG chép iap_cost của CSV vào đây.");

            if (typeMismatch.Count > 0)
                Add(EzgStatus.Warn, $"product_type lệch cờ isNonConsumable: {Names(typeMismatch)}",
                    "sửa cột product_type trong sheet cho khớp catalog — lệch loại là store không fetch "
                    + "được product: ô giá trống, bấm mua fail.");

            if (storeIdIssue.Count > 0)
                Add(EzgStatus.Warn, $"store_product_id sai quy ước: {Names(storeIdIssue)}",
                    "android phải bằng product_id, iOS phải để TRỐNG (ASC không có trường này). Bấm Load "
                    + "lại không sửa ô này — sửa tay trong sheet.");

            if (_orphans.Count > 0)
                Add(EzgStatus.Warn, $"{_orphans.Count} dòng trong sheet không có trong catalog",
                    "gói template đã bỏ / bảng đang tắt / CSV chưa import? Tool không xoá dòng của GD — "
                    + "GD tự quyết, danh sách ở khối bên dưới.");

            BuildStoreTodos(missingOnStore, incompleteOnStore, inactiveOnStore);
        }

        private void BuildStoreTodos(List<string> missingOnStore, List<string> incompleteOnStore,
            List<string> inactiveOnStore)
        {
            if (_snapshot == null) return;

            if (!_snapshot.IsValid)
            {
                Add(EzgStatus.Error, "Không xác minh được với store: " + _snapshot.Error,
                    _snapshot.HttpCode == 503
                        ? "server chưa đọc được store — CHƯA BIẾT, đừng kết luận gói chưa tạo. Thử lại sau ít phút."
                        : "xem khối \"Xác minh với store\" bên dưới: link API và API key.");
                return;
            }

            // ── Bắt ca "dán nhầm key của dự án khác" ──────────────────────────────────────────
            //
            // Key quyết định dự án, nên key của dự án khác vẫn trả 200 — kèm danh mục của dự án ĐÓ, và
            // lúc đó MỌI gói của mình trông như "chưa tạo trên store".
            //
            // Cách bắt: ĐỘ KHỚP danh mục, không phải một ô "mã dự án" gõ tay. Store trả về gói mà
            // KHÔNG trùng một id nào của client thì gần như chắc chắn là danh mục của dự án khác —
            // và không cần ai điền gì để lượt kiểm này chạy. (Ô gõ tay thì mỗi lần gõ sai là một báo
            // động giả cho key hoàn toàn đúng, mà giá trị đoán mặc định theo tên thư mục gốc còn sai
            // ngay khi agent chạy trong git worktree `<repo>-agent-<base>`.)
            //
            // Store rỗng KHÔNG rơi vào đây: dự án mới chưa tạo gói nào là ca thật, đã có dòng
            // "chưa có trên store" ở dưới lo.
            if (_snapshot.Rows.Count > 0 && _storeMatched == 0 && _enabledSkus.Count > 0)
            {
                Add(EzgStatus.Error,
                    $"Danh mục store có {_snapshot.Rows.Count} dòng nhưng KHÔNG khớp id nào của "
                    + $"{_enabledSkus.Count} SKU client đăng ký",
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
                Add(EzgStatus.Error, $"{missingOnStore.Count} SKU client đăng ký mà CHƯA có trên store: {Names(missingOnStore)}",
                    "tạo trên Play Console / App Store Connect, đúng loại Consumable/Non-Consumable theo "
                    + "cờ isNonConsumable. Client đăng ký SKU không tồn tại = ô giá trống, bấm mua fail.");

            if (incompleteOnStore.Count > 0)
                Add(EzgStatus.Warn, $"{incompleteOnStore.Count} SKU đã tạo nhưng chưa xong trên store: {Names(incompleteOnStore)}",
                    "vào console bổ sung phần còn thiếu (giá, tên, mô tả, ảnh review, base plan). "
                    + "Trạng thái thô của store nằm ở từng dòng trong bảng SKU.");

            if (inactiveOnStore.Count > 0)
                Add(EzgStatus.Warn, $"{inactiveOnStore.Count} SKU đang TẮT trên store: {Names(inactiveOnStore)}",
                    "bật lại trên console, hoặc bỏ SKU khỏi catalog nếu thật sự không bán nữa.");

            if (_extraOnStore.Count > 0)
                Add(EzgStatus.Warn, $"{_extraOnStore.Count} dòng có trên store mà client không đăng ký",
                    "gói template đã bỏ, bảng đang tắt, hay id gõ sai? Danh sách ở khối bên dưới. Gói "
                    + "trùng/rác trên store không xoá được, chỉ tắt đi được.");

            if (_snapshot.KeyExpiringSoon(out var daysLeft))
                Add(EzgStatus.Warn,
                    daysLeft >= 0
                        ? $"API key còn {daysLeft} ngày là hết hạn"
                        : "API key đã hết hạn",
                    "xin admin gia hạn hoặc cấp key mới ở project-ezg → Dự án → tab API key. Hết hạn là "
                    + "tab này ngừng xác minh được, không có cảnh báo nào khác.");
        }

        private void Add(EzgStatus status, string what, string fix) =>
            _todos.Add(new Todo { Status = status, What = what, Fix = fix });

        /// <summary>
        ///     Nhãn nút ghi + chữ đếm của khối SKU. Nhãn nút nói rõ ĐÍCH (tên file) và VIỆC (thêm bao
        ///     nhiêu dòng) — bản trước chỉ ghi "Load lại N SKU đang dùng" nên người bấm không biết nó
        ///     sắp ghi vào file nào, mà ô chọn file lại nằm phía dưới nút.
        /// </summary>
        private void BuildActionLabels()
        {
            _syncLabel = !HasSheet
                ? $"Tạo bảng giá + load {_enabledSkus.Count} SKU"
                : _missingRows > 0
                    ? $"Load lại {_enabledSkus.Count} SKU vào {Path.GetFileName(_xlsxPath)} (thêm {_missingRows} dòng)"
                    : $"Load lại {_enabledSkus.Count} SKU vào {Path.GetFileName(_xlsxPath)}";

            var disabled = _skus.Count - _enabledSkus.Count;
            _skuTrailing = disabled > 0
                ? $"{_tables.Count} bảng · {_enabledSkus.Count} SKU đang dùng · {disabled} không đăng ký"
                : $"{_tables.Count} bảng · {_enabledSkus.Count} SKU đang dùng";
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

            if (_catalogPath == null)
            {
                _headline = "Chưa có ShopPackCatalog — không biết client đăng ký SKU nào.";
                return;
            }

            var store = _snapshot == null
                ? "chưa xác minh store"
                : !_snapshot.IsValid
                    ? "store: lỗi kết nối"
                    : $"store: {StoreReadyCount()}/{_enabledSkus.Count} SKU sẵn sàng";

            var sheet = !HasSheet
                ? "chưa có bảng giá"
                : _missingRows > 0
                    ? $"sheet thiếu {_missingRows} dòng"
                    : _emptyPriceRows > 0
                        ? $"sheet còn {_emptyPriceRows} dòng chưa có giá"
                        : "sheet đủ";

            _headline = $"{_enabledSkus.Count} SKU đang dùng · {sheet} · {store}.";
        }

        /// <summary>Số SKU đang dùng mà MỌI nền tảng đọc được đều trả <c>ready</c>.</summary>
        private int StoreReadyCount()
        {
            var count = 0;
            foreach (var group in _tables)
            foreach (var row in group.Rows)
                if (row.Sku.Enabled && row.StoreStatus == EzgStatus.Ok) count++;

            return count;
        }

        private static readonly string[] Platforms =
            { IapPriceSheet.PLATFORM_ANDROID, IapPriceSheet.PLATFORM_IOS };

        private static EzgStatus Worse(EzgStatus a, EzgStatus b) => a > b ? a : b;

        /// <summary>
        ///     Danh sách tên gói trong một dòng việc-phải-làm. Cắt ở <see cref="NAMES_IN_TODO" />: dòng
        ///     liệt kê 18 tên thì tự nó thành thứ phải cuộn qua, mà chi tiết đã có ở bảng SKU.
        /// </summary>
        private static string Names(List<string> names)
        {
            if (names.Count <= NAMES_IN_TODO) return string.Join(", ", names);
            return string.Join(", ", names.GetRange(0, NAMES_IN_TODO)) + $", … (+{names.Count - NAMES_IN_TODO})";
        }

        #endregion

        #region Draw — đỉnh trang (không cuộn)

        private void DrawFixedTop()
        {
            EzgKitStyles.Banner(_headline + "  " + Advice(), _status);

            var canSync = _catalogPath != null && _enabledSkus.Count > 0 && (_sheet == null || _sheet.IsValid);

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(!canSync))
                {
                    if (EzgKitStyles.PrimaryButton(_syncLabel, GUILayout.Width(330f)))
                        ReadinessActions.Defer(() =>
                        {
                            SyncAll();
                            InternalEditorUtility.RepaintAllViews();
                        });
                }

                using (new EditorGUI.DisabledScope(_verifier.IsRunning || _catalogPath == null))
                {
                    if (EzgKitStyles.SecondaryButton(
                            _verifier.IsRunning ? "Đang hỏi server…" : "Kiểm tra trên store",
                            GUILayout.Width(180f)))
                        StartVerify();
                }

                GUILayout.FlexibleSpace();

                using (new EditorGUI.DisabledScope(!HasSheet))
                {
                    if (EzgKitStyles.SecondaryButton("Mở file", GUILayout.Width(90f)))
                        EditorUtility.RevealInFinder(Absolute(_xlsxPath));
                }

                using (new EditorGUI.DisabledScope(_catalogPath == null))
                {
                    if (EzgKitStyles.SecondaryButton("Chọn catalog", GUILayout.Width(120f)))
                        ReadinessActions.Defer(() =>
                        {
                            var asset = AssetDatabase.LoadAssetAtPath<ScriptableObject>(_catalogPath);
                            Selection.activeObject = asset;
                            EditorGUIUtility.PingObject(asset);
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

            EzgKitStyles.CollapsibleHelp("iap-scope", "Ba nguồn của tab này — cái nào nói gì?",
                "1. CATALOG (client) — `ShopPackCatalog`: bảng đang BẬT + gói purchase_type = IAP. Đúng "
                + "danh sách client gửi store lúc boot. Bảng tắt vẫn hiện ở đây nhưng KHÔNG đăng ký.\n\n"
                + "2. BẢNG GIÁ (GD) — file .xlsx sheet \"" + IapPriceSheet.SHEET_NAME + "\". Nút Load lại "
                + "đảm bảo mỗi SKU đang dùng có đúng hai dòng android + ios:\n"
                + "  • Chưa có dòng → thêm cuối vùng dữ liệu (reference_name/title/description từ pack_name, "
                + "product_type theo cờ isNonConsumable, USD, base_plan_id 1, en-US, family_sharable FALSE; "
                + "android store_product_id = product_id, iOS để trống; default_price = \""
                + IapPriceSheet.PRICE_PLACEHOLDER + "\").\n"
                + "  • Đã có dòng nhưng chưa có giá → chỉ điền chữ nhắc vào ô đó.\n"
                + "  • ĐÃ CÓ GIÁ → không đụng ô nào. Giá là của GD; tool không chép iap_cost của CSV.\n"
                + "  • Dòng sheet mà catalog không có → để nguyên, liệt kê riêng.\n"
                + "  Chưa có file → tự tạo " + DEFAULT_PATH + " với hàng tiêu đề 16 cột. Cách ghi: chỉ sửa "
                + "đúng một entry XML của sheet trong zip .xlsx, mọi sheet/ảnh khác giữ nguyên byte; bản sao "
                + "trước mỗi lần ghi ở " + BACKUP_DIR + ".\n\n"
                + "3. STORE (thật) — server nội bộ project-ezg đọc App Store Connect + Google Play. Chỉ đọc, "
                + "server đệm 60 giây nên không có nút ép đọc lại. Đây là nguồn DUY NHẤT biết gói đã tạo "
                + "thật chưa; hai nguồn trên chỉ nói ý ĐỊNH bán.\n\n"
                + "Cột giá ở bảng SKU là iap_cost của CSV (giá client hiển thị khi chưa fetch được store), "
                + "KHÔNG phải giá store. Giá store nằm ở cột default_price của bảng giá GD.");
        }

        private string Advice()
        {
            if (_catalogPath == null) return "Sửa trước khi làm gì khác.";
            if (_status == EzgStatus.Error) return "Có mục đỏ — bán ra là fail, sửa trước khi build store.";
            if (_status == EzgStatus.Warn) return "Chạy được, còn việc trước khi lên store.";
            return _snapshot == null
                ? "Còn thiếu một bước: bấm \"Kiểm tra trên store\" để biết store đã tạo gói chưa."
                : "Client · bảng giá · store đều khớp.";
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
                            : "Không còn việc — client, bảng giá và store đều khớp.",
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

        private void DrawTables()
        {
            EzgKitStyles.SectionHeader(
                _tables.Count == 0 ? "SKU theo bảng pack" : "SKU theo bảng pack — " + _skuTrailing,
                _catalogPath == null
                    ? "Chưa có ShopPackCatalog trong Resources của Shop."
                    : "Mỗi SKU đang dùng cần: hai dòng trong bảng giá (android + ios) và một gói thật trên store.");

            if (_tables.Count == 0)
            {
                using (new EzgKitStyles.CardScope())
                {
                    EditorGUILayout.LabelField("Catalog không có gói IAP nào.", EzgKitStyles.Hint);
                }

                return;
            }

            foreach (var group in _tables)
            {
                using (new EzgKitStyles.CardScope())
                {
                    var open = _openTables[group.Label];
                    var next = EzgKitStyles.CardFoldout(open, group.Label, group.Status, group.Trailing);
                    if (next != open) _openTables[group.Label] = next;
                    if (!next) continue;

                    for (var i = 0; i < group.Rows.Count; i++)
                    {
                        if (i > 0) EzgKitStyles.Divider(2f);
                        DrawSkuRow(group.Rows[i]);
                    }
                }
            }
        }

        /// <summary>
        ///     Một SKU = ba tầng thông tin, không nhồi vào một dòng như bản trước: hàng đầu là tên +
        ///     giá + hai chip trạng thái (bảng giá / store), hàng hai là product id (copy được — dev
        ///     phải dán nó sang console), rồi mỗi vấn đề một dòng riêng.
        /// </summary>
        private void DrawSkuRow(SkuRow row)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EzgKitStyles.StatusIcon(row.Worst, row.Tooltip);
                EditorGUILayout.LabelField(row.Sku.PackName, EditorStyles.boldLabel,
                    GUILayout.Width(PACK_NAME_WIDTH));
                EditorGUILayout.LabelField(row.Price, EzgKitStyles.MutedLabel, GUILayout.Width(PRICE_WIDTH));
                GUILayout.FlexibleSpace();

                if (!string.IsNullOrEmpty(row.SheetLabel)) EzgKitStyles.Pill(row.SheetStatus, row.SheetLabel);
                if (!string.IsNullOrEmpty(row.StoreLabel)) EzgKitStyles.Pill(row.StoreStatus, row.StoreLabel);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(EzgKitStyles.ICON_WIDTH + 4f);
                EditorGUILayout.SelectableLabel(row.ProductIds, EzgKitStyles.Hint,
                    GUILayout.Height(EditorGUIUtility.singleLineHeight));
            }

            foreach (var note in row.Notes) Indented(note, EzgKitStyles.Hint);
        }

        /// <summary>
        ///     Chiều NGƯỢC của lượt đối chiếu: store có, client chưa/không có. Khối này hiện MỌI LÚC khi
        ///     đã xác minh, kể cả lúc sạch — ẩn đi thì không ai biết chiều này có được kiểm hay không, mà
        ///     "không thấy gì" đọc y hệt "tool không kiểm".
        /// </summary>
        private void DrawExtraOnStore()
        {
            if (!HasStore) return;

            EzgKitStyles.SectionHeader($"Có trên store mà client KHÔNG đăng ký ({_extraOnStore.Count})",
                "Chiều ngược: id sống trên store nhưng client không gửi lên lúc boot — gói template đã bỏ, "
                + "bảng đang tắt trong catalog, id gõ sai, hay gói cũ của bản trước. Người chơi không mua "
                + "được qua game; mà gói trên store thì không xoá được, chỉ tắt được.");

            using (new EzgKitStyles.CardScope())
            {
                if (_extraOnStore.Count == 0)
                {
                    EditorGUILayout.LabelField(
                        $"Không có — cả {_snapshot.Rows.Count} dòng trong danh mục store đều khớp SKU client đăng ký.",
                        EzgKitStyles.Hint);
                    return;
                }

                foreach (var row in _extraOnStore)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EzgKitStyles.StatusIcon(EzgStatus.Warn);
                        EditorGUILayout.SelectableLabel(row, EzgKitStyles.ValueStyle,
                            GUILayout.Height(EditorGUIUtility.singleLineHeight));
                    }
                }
            }
        }

        private void DrawOrphans()
        {
            if (_orphans.Count == 0) return;

            EzgKitStyles.SectionHeader($"Dòng trong bảng giá không có trong catalog ({_orphans.Count})",
                "Client không đăng ký SKU này. Tool không xoá dòng của GD — GD tự quyết.");

            using (new EzgKitStyles.CardScope())
            {
                foreach (var row in _orphans)
                    EzgKitStyles.KeyValue($"{row.Platform} · dòng {row.RowNumber}", row.ProductId, EzgStatus.Warn,
                        string.IsNullOrEmpty(row.ReferenceName) ? null : row.ReferenceName);
            }
        }

        #endregion

        #region Draw — nguồn dữ liệu

        /// <summary>
        ///     Khối cấu hình lượt xác minh store. Nằm DƯỚI bảng SKU vì nó là thứ điền một lần rồi thôi,
        ///     còn kết quả thì đã nằm ở trên (chip từng dòng + khối việc phải làm).
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
            if (key != currentKey) IapVerifyConfig.ApiKey = key;
        }

        /// <summary>Trạng thái lượt hỏi gần nhất: chưa hỏi / lỗi / đọc được nền tảng nào, lúc nào.</summary>
        private void DrawStoreState()
        {
            // Key nào đang được dùng — chỉ phần đầu, đủ phân biệt và không đủ để lộ (tab kit hay bị
            // share screen lúc họp). Đứng trước mọi nhánh vì đây là câu hỏi đầu tiên khi gặp 401.
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
                    "Bấm \"Kiểm tra trên store\" ở đỉnh trang. Mở tab không tự gọi mạng.",
                    "chưa hỏi server lần nào");
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

            // Mã dự án là DỮ KIỆN do key quyết định, không phải thứ tool đi so với một ô gõ tay. Bày ra
            // để người dùng nhìn thấy mình đang soi danh mục của dự án nào; lượt kiểm "có dán nhầm key
            // không" đi bằng độ khớp danh mục (xem BuildStoreTodos).
            EzgKitStyles.KeyValue("Dự án của key", _snapshot.Project, EzgStatus.None,
                IapVerifyConfig.IsLocalApi
                    ? "Link đang trỏ về localhost — đây là server dev tại máy này, không phải server thật."
                    : null);

            EzgKitStyles.KeyValue("Khớp danh mục", $"{_storeMatched}/{_enabledSkus.Count} SKU client có mặt trên store",
                _enabledSkus.Count > 0 && _storeMatched == 0 && _snapshot.Rows.Count > 0
                    ? EzgStatus.Error
                    : EzgStatus.None,
                "0/N mà danh mục store lại có gói = gần như chắc chắn key của dự án khác.");

            EzgKitStyles.KeyValue("Store đọc lúc", _snapshot.CheckedAtLabel, EzgStatus.None,
                $"Server đệm 60 giây; hỏi dày hơn cũng chỉ nhận lại đúng bản này. Nhận về máy lúc {_snapshot.FetchedAtLocal:HH:mm:ss}.");

            foreach (var platform in _snapshot.Platforms)
            {
                var status = !platform.Ok ? EzgStatus.Warn : platform.Stale ? EzgStatus.Warn : EzgStatus.Ok;
                var value = !platform.Ok
                    ? "KHÔNG đọc được — bỏ qua khi đối chiếu"
                    : platform.Stale
                        ? "đọc được (bản lưu, dữ liệu cũ)"
                        : "đọc được";

                EzgKitStyles.KeyValue(platform.Platform, value, status,
                    string.IsNullOrEmpty(platform.Error) ? null : platform.Error);
            }

            if (!string.IsNullOrEmpty(_snapshot.KeyExpiresAt))
                EzgKitStyles.KeyValue("API key hết hạn", _snapshot.KeyExpiresAt,
                    _snapshot.KeyExpiringSoon(out _) ? EzgStatus.Warn : EzgStatus.None);
        }

        private void DrawSheetSource()
        {
            EzgKitStyles.SectionHeader("Bảng giá của GD (.xlsx)",
                "Nhớ trong " + SOURCE_FILE + ". Để trống thì tự dò .xlsx trong Assets (ưu tiên " + DEFAULT_FOLDER
                + "); không có thì nút Load tự tạo.");

            var picked = SetupGui.ManualFilePathField("Bảng giá IAP (.xlsx)", _xlsxPath,
                "File GD dùng để tạo SKU trên Play Console / App Store Connect — sheet \"" + IapPriceSheet.SHEET_NAME
                + "\" với các cột platform, product_id, reference_name, product_type, default_price, …, store_product_id. "
                + "Quy ước để trong " + DEFAULT_FOLDER + "/ để đi theo repo.",
                "Chọn bảng giá IAP", "xlsx", SetupGui.FieldNeed.Derived,
                ("Play Console", URL_PLAY_CONSOLE), ("App Store Connect", URL_ASC));

            if (picked == _xlsxPath) return;

            _xlsxPath = Relative(picked);
            SaveSource(_xlsxPath);
            _reloadPending = true;
        }

        private static void DrawManualSteps()
        {
            EzgKitStyles.SectionHeader("Việc còn lại ngoài Unity");

            SetupGui.ManualStep($"GD thay chữ \"{IapPriceSheet.PRICE_PLACEHOLDER}\" bằng giá thật",
                "Tool để chữ nhắc có chủ đích: iap_cost trong CSV chỉ là giá tham chiếu client hiển thị lúc chưa fetch "
                + "được store; giá thật do GD chốt theo tier từng store. Dòng đã có giá thì lần Load sau không đụng.");

            SetupGui.ManualStep("Tạo SKU trên Play Console + App Store Connect đúng loại",
                "Loại (Consumable / Non-Consumable) PHẢI khớp cờ isNonConsumable của catalog — lệch là ô giá trống "
                + "và mua fail. Entitlement vĩnh viễn (remove ads, boost vĩnh viễn) đúng bài là Non-Consumable + nút Restore. "
                + "Tạo xong thì bấm \"Kiểm tra trên store\" — server đệm 60 giây nên đợi tối đa một phút là thấy.",
                ("Play Console", URL_PLAY_CONSOLE), ("App Store Connect", URL_ASC));

            SetupGui.ManualStep("Sandbox test IAP trên máy thật",
                "Mua thử từng SKU bằng tài khoản tester (Play: License testers; ASC: Sandbox testers). Store báo "
                + "\"ready\" vẫn có thể fail ở bước này — SKU lệch loại, chưa link tài khoản thanh toán, chưa ký thoả thuận.",
                ("Play Console", URL_PLAY_CONSOLE), ("App Store Connect", URL_ASC));
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
        ///     callback chạy từ <see cref="EditorApplication.update" />, có thể rơi vào giữa lượt vẽ,
        ///     và số widget đổi giữa Layout với Repaint là cửa sổ ném exception. Nó đợi ở
        ///     <see cref="_pendingSnapshot" /> tới đầu lượt <see cref="Draw" /> kế tiếp.
        /// </summary>
        private void StartVerify()
        {
            _message = null;
            _messageStatus = EzgStatus.None;

            // Callback CHỈ đặt snapshot chờ — mọi thứ ảnh hưởng tới số widget (câu báo, phán quyết)
            // do Draw làm ở đầu lượt kế tiếp.
            _verifier.Fetch(IapVerifyConfig.ApiUrl, IapVerifyConfig.ApiKey,
                snapshot => _pendingSnapshot = snapshot);
        }

        #endregion

        #region Write

        /// <summary>Tạo file nếu chưa có, rồi đồng bộ toàn bộ SKU đang bật. Mọi lỗi báo lên UI, không ném.</summary>
        private bool SyncAll()
        {
            try
            {
                var created = false;
                if (string.IsNullOrEmpty(_xlsxPath) || !File.Exists(Absolute(_xlsxPath)))
                {
                    _xlsxPath = string.IsNullOrEmpty(_xlsxPath) ? DEFAULT_PATH : _xlsxPath;
                    IapPriceSheet.Create(Absolute(_xlsxPath));
                    SaveSource(_xlsxPath);
                    created = true;
                }

                var result = IapPriceSheet.Sync(Absolute(_xlsxPath), _enabledSkus, Path.Combine(ProjectRoot, BACKUP_DIR));
                if (IsInsideAssets(_xlsxPath)) AssetDatabase.ImportAsset(_xlsxPath);

                var file = Path.GetFileName(_xlsxPath);
                if (created)
                    _message = $"Da tao {_xlsxPath} va ghi {result.Added} dong ({_enabledSkus.Count} SKU x android/ios). "
                               + $"GD thay chu \"{IapPriceSheet.PRICE_PLACEHOLDER}\" bang gia that.";
                else if (!result.Changed)
                    _message = $"{file} da du {_enabledSkus.Count} SKU, khong co gi doi (dong da co gia giu nguyen).";
                else
                    _message = $"Da ghi vao {file}: them {result.Added} dong moi, dien chu nhac gia vao {result.Placeholders} dong "
                               + $"chua co gia; dong da co gia giu nguyen. Ban sao truoc khi ghi: {BACKUP_DIR}.";
                _messageStatus = EzgStatus.Ok;
                _reloadPending = true;
                return true;
            }
            catch (Exception e)
            {
                _message = "Khong ghi duoc .xlsx: " + e.Message + " (file dang mo trong Excel/Numbers?)";
                _messageStatus = EzgStatus.Error;
                Debug.LogException(e);
                _reloadPending = true;
                return false;
            }
        }

        #endregion

        #region Source / paths

        private static string ProjectRoot => Path.GetDirectoryName(Application.dataPath) ?? string.Empty;

        private static string Absolute(string path) =>
            string.IsNullOrEmpty(path) || Path.IsPathRooted(path) ? path : Path.Combine(ProjectRoot, path);

        /// <summary>Đường dẫn trong project → tương đối (đi theo repo được); ngoài project giữ tuyệt đối.</summary>
        private static string Relative(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;
            var root = ProjectRoot.Replace('\\', '/').TrimEnd('/') + "/";
            var normalized = path.Replace('\\', '/');
            return normalized.StartsWith(root, StringComparison.OrdinalIgnoreCase)
                ? normalized.Substring(root.Length)
                : path;
        }

        private static bool IsInsideAssets(string path) =>
            !string.IsNullOrEmpty(path) && path.Replace('\\', '/').StartsWith("Assets/", StringComparison.Ordinal);

        private static string LoadSource()
        {
            var file = Path.Combine(ProjectRoot, SOURCE_FILE);
            if (!File.Exists(file)) return null;
            try
            {
                var source = JsonUtility.FromJson<Source>(File.ReadAllText(file));
                return string.IsNullOrEmpty(source?.xlsxPath) ? null : source.xlsxPath;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[EzgKit] Không đọc được {SOURCE_FILE}: {e.Message}");
                return null;
            }
        }

        private static void SaveSource(string xlsxPath)
        {
            var file = Path.Combine(ProjectRoot, SOURCE_FILE);
            File.WriteAllText(file, JsonUtility.ToJson(new Source { xlsxPath = xlsxPath }, true));
        }

        /// <summary>Tự dò .xlsx trong Assets khi chưa ai chọn — ưu tiên file nằm trong thư mục quy ước.</summary>
        private static string AutoFindSheet()
        {
            string fallback = null;
            var hint = "/" + Path.GetFileName(DEFAULT_FOLDER) + "/";
            foreach (var file in Directory.EnumerateFiles(Application.dataPath, "*.xlsx", SearchOption.AllDirectories))
            {
                var name = Path.GetFileName(file);
                if (name.StartsWith("~$", StringComparison.Ordinal)) continue; // lock file của Excel
                var relative = Relative(file);
                if (relative.Replace('\\', '/').Contains(hint)) return relative;
                fallback ??= relative;
            }

            return fallback;
        }

        #endregion
    }
}
#endif
