#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Ezg.Editor.Shared.Iap
{
    /// <summary>Một dòng của sheet "List gói bán" — chỉ những cột tool cần đối chiếu/ghi.</summary>
    internal sealed class IapPriceRow
    {
        internal int RowNumber;
        internal string Platform;
        internal string ProductId;
        internal string ReferenceName;
        internal string ProductType;
        internal string DefaultPrice;
        internal string StoreProductId;

        internal string Key => IapPriceSheet.RowKey(Platform, ProductId);
    }

    /// <summary>
    ///     Đọc / ghi thêm dòng vào bảng giá IAP của GD (file <c>.xlsx</c>, sheet <see cref="SHEET_NAME" />)
    ///     bằng cách mở thẳng zip + XML của OOXML — KHÔNG dùng thư viện Excel nào.
    ///     <para>
    ///         <b>Vì sao không dùng lib:</b> file này là export từ Google Sheets, 19 sheet với drawing/ảnh của
    ///         GD ở các sheet khác. Lib đọc-ghi (openpyxl, EPPlus…) load rồi save lại là RƠI drawing/ảnh và
    ///         đổi style toàn file. Tool chỉ chạm đúng một entry <c>xl/worksheets/sheetN.xml</c>, mọi entry
    ///         khác copy nguyên byte — GD mở lại thấy y nguyên, chỉ thêm dòng.
    ///     </para>
    ///     <para>
    ///         <b>Quy ước dòng ghi:</b> Android điền <c>store_product_id</c> = <c>product_id</c>, iOS để TRỐNG
    ///         cột đó (ASC không có khái niệm này); <c>default_price</c> luôn để trống — giá là việc của GD,
    ///         tool không tự chép <c>iap_cost</c> của CSV vào. Style ô lấy theo dòng dữ liệu cuối cùng để
    ///         dòng mới không lệch font/canh lề với dòng GD đã điền tay.
    ///     </para>
    /// </summary>
    internal sealed class IapPriceSheet
    {
        #region Constants

        internal const string SHEET_NAME = "List gói bán";
        internal const string PLATFORM_ANDROID = "android";
        internal const string PLATFORM_IOS = "ios";

        private const string COL_PLATFORM = "A";
        private const string COL_PRODUCT_ID = "B";
        private const string COL_REFERENCE_NAME = "C";
        private const string COL_PRODUCT_TYPE = "D";
        private const string COL_STATUS = "E";
        private const string COL_DEFAULT_PRICE = "F";
        private const string COL_CURRENCY = "G";
        private const string COL_SUBSCRIPTION_PERIOD = "H";
        private const string COL_BASE_PLAN_ID = "I";
        private const string COL_LOCALE = "J";
        private const string COL_TITLE = "K";
        private const string COL_DESCRIPTION = "L";
        private const string COL_REVIEW_NOTE = "M";
        private const string COL_FAMILY_SHARABLE = "N";
        private const string COL_REVIEW_SCREENSHOT = "O";
        private const string COL_STORE_PRODUCT_ID = "P";

        private const string HEADER_PLATFORM = "platform";
        private const string HEADER_PRODUCT_ID = "product_id";
        private const string HEADER_STORE_PRODUCT_ID = "store_product_id";

        private const string DEFAULT_CURRENCY = "USD";
        private const string DEFAULT_LOCALE = "en-US";
        private const string DEFAULT_BASE_PLAN_ID = "1";

        /// <summary>Chữ nhắc ghi vào default_price của dòng mới / dòng chưa có giá — GD thay bằng số.</summary>
        internal const string PRICE_PLACEHOLDER = "CẦN ĐIỀN GIÁ";

        /// <summary>Chỉ số style của hàng tiêu đề trong styles.xml do <see cref="Create" /> sinh (font đậm).</summary>
        private const string HEADER_STYLE_INDEX = "1";

        /// <summary>Thứ tự + tên 16 cột chuẩn của sheet — dùng khi tạo file mới và kiểm header.</summary>
        private static readonly (string Col, string Name)[] HEADERS =
        {
            (COL_PLATFORM, HEADER_PLATFORM), (COL_PRODUCT_ID, HEADER_PRODUCT_ID), (COL_REFERENCE_NAME, "reference_name"),
            (COL_PRODUCT_TYPE, "product_type"), (COL_STATUS, "status"), (COL_DEFAULT_PRICE, "default_price"),
            (COL_CURRENCY, "currency"), (COL_SUBSCRIPTION_PERIOD, "subscription_period"), (COL_BASE_PLAN_ID, "base_plan_id"),
            (COL_LOCALE, "locale"), (COL_TITLE, "title"), (COL_DESCRIPTION, "description"), (COL_REVIEW_NOTE, "review_note"),
            (COL_FAMILY_SHARABLE, "family_sharable"), (COL_REVIEW_SCREENSHOT, "review_screenshot"),
            (COL_STORE_PRODUCT_ID, HEADER_STORE_PRODUCT_ID),
        };

        private const string WORKBOOK_ENTRY = "xl/workbook.xml";
        private const string WORKBOOK_RELS_ENTRY = "xl/_rels/workbook.xml.rels";
        private const string SHARED_STRINGS_ENTRY = "xl/sharedStrings.xml";

        private static readonly XNamespace NsMain = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        private static readonly XNamespace NsRel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        private static readonly XNamespace NsPkgRel = "http://schemas.openxmlformats.org/package/2006/relationships";

        private static readonly Regex CellRef = new("^([A-Z]+)(\\d+)$", RegexOptions.Compiled);

        #endregion

        #region State

        internal readonly List<IapPriceRow> Rows = new();

        /// <summary>Dòng dữ liệu cuối (có product_id). Dòng mới ghi từ đây + 1.</summary>
        internal int LastDataRow;

        /// <summary>Lỗi đọc (file hỏng, thiếu sheet, header đổi). Null = đọc được.</summary>
        internal string Error;

        internal bool IsValid => Error == null;

        internal IapPriceRow Find(string platform, string productId)
        {
            var key = RowKey(platform, productId);
            return Rows.FirstOrDefault(r => r.Key == key);
        }

        internal static string RowKey(string platform, string productId) =>
            (platform ?? string.Empty).Trim().ToLowerInvariant() + "|" + (productId ?? string.Empty).Trim();

        #endregion

        #region Load

        /// <summary>Đọc sheet. Không bao giờ ném: lỗi nằm trong <see cref="Error" />.</summary>
        internal static IapPriceSheet Load(string absolutePath)
        {
            var sheet = new IapPriceSheet();
            try
            {
                if (string.IsNullOrEmpty(absolutePath) || !File.Exists(absolutePath))
                {
                    sheet.Error = "Không thấy file .xlsx.";
                    return sheet;
                }

                using var zip = ZipFile.OpenRead(absolutePath);
                var sheetEntry = FindSheetEntry(zip, SHEET_NAME, out var sheetError);
                if (sheetEntry == null)
                {
                    sheet.Error = sheetError;
                    return sheet;
                }

                var shared = ReadSharedStrings(zip);
                var doc = ReadXml(sheetEntry);
                var sheetData = doc.Root?.Element(NsMain + "sheetData");
                if (sheetData == null)
                {
                    sheet.Error = $"Sheet '{SHEET_NAME}' không có sheetData.";
                    return sheet;
                }

                var headerOk = false;
                foreach (var row in sheetData.Elements(NsMain + "row"))
                {
                    var number = RowNumber(row);
                    var cells = ReadCells(row, shared);
                    if (number == 1)
                    {
                        headerOk = Get(cells, COL_PLATFORM) == HEADER_PLATFORM
                                   && Get(cells, COL_PRODUCT_ID) == HEADER_PRODUCT_ID
                                   && Get(cells, COL_STORE_PRODUCT_ID) == HEADER_STORE_PRODUCT_ID;
                        continue;
                    }

                    var productId = Get(cells, COL_PRODUCT_ID);
                    var platform = Get(cells, COL_PLATFORM);
                    if (string.IsNullOrEmpty(productId) || string.IsNullOrEmpty(platform)) continue;

                    sheet.Rows.Add(new IapPriceRow
                    {
                        RowNumber = number,
                        Platform = platform.ToLowerInvariant(),
                        ProductId = productId,
                        ReferenceName = Get(cells, COL_REFERENCE_NAME),
                        ProductType = Get(cells, COL_PRODUCT_TYPE),
                        DefaultPrice = Get(cells, COL_DEFAULT_PRICE),
                        StoreProductId = Get(cells, COL_STORE_PRODUCT_ID),
                    });
                    if (number > sheet.LastDataRow) sheet.LastDataRow = number;
                }

                if (!headerOk)
                    sheet.Error = $"Sheet '{SHEET_NAME}' đổi header: cần A=platform, B=product_id, P=store_product_id.";
                else if (sheet.LastDataRow == 0) sheet.LastDataRow = 1;
            }
            catch (Exception e)
            {
                sheet.Error = "Không đọc được .xlsx: " + e.Message;
            }

            return sheet;
        }

        #endregion

        #region Create

        /// <summary>
        ///     Tạo file .xlsx mới chỉ có sheet <see cref="SHEET_NAME" /> với hàng tiêu đề chuẩn (16 cột) —
        ///     dùng khi dự án mới chưa có bảng giá. OOXML tối thiểu: [Content_Types], _rels, workbook,
        ///     worksheet, styles (header đậm). Mở được bằng Excel / Numbers / Google Sheets.
        /// </summary>
        internal static void Create(string absolutePath)
        {
            var directory = Path.GetDirectoryName(absolutePath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            if (File.Exists(absolutePath)) throw new InvalidOperationException("File đã tồn tại: " + absolutePath);

            using var zip = ZipFile.Open(absolutePath, ZipArchiveMode.Create);
            WriteEntry(zip, "[Content_Types].xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>"
                + "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">"
                + "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>"
                + "<Default Extension=\"xml\" ContentType=\"application/xml\"/>"
                + "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>"
                + "<Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>"
                + "<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>"
                + "</Types>");
            WriteEntry(zip, "_rels/.rels",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>"
                + "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">"
                + "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>"
                + "</Relationships>");
            WriteEntry(zip, WORKBOOK_ENTRY,
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>"
                + "<workbook xmlns=\"" + NsMain + "\" xmlns:r=\"" + NsRel + "\">"
                + "<sheets><sheet name=\"" + SHEET_NAME + "\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>");
            WriteEntry(zip, WORKBOOK_RELS_ENTRY,
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>"
                + "<Relationships xmlns=\"" + NsPkgRel + "\">"
                + "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/>"
                + "<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>"
                + "</Relationships>");
            WriteEntry(zip, "xl/styles.xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>"
                + "<styleSheet xmlns=\"" + NsMain + "\">"
                + "<fonts count=\"2\"><font><sz val=\"11\"/><name val=\"Arial\"/></font><font><b/><sz val=\"11\"/><name val=\"Arial\"/></font></fonts>"
                + "<fills count=\"2\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill></fills>"
                + "<borders count=\"1\"><border><left/><right/><top/><bottom/><diagonal/></border></borders>"
                + "<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>"
                + "<cellXfs count=\"2\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/>"
                + "<xf numFmtId=\"0\" fontId=\"1\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyFont=\"1\"/></cellXfs>"
                + "<cellStyles count=\"1\"><cellStyle name=\"Normal\" xfId=\"0\" builtinId=\"0\"/></cellStyles>"
                + "</styleSheet>");

            var header = new XElement(NsMain + "row", new XAttribute("r", 1));
            var headerStyle = new Dictionary<string, string>();
            foreach (var (col, name) in HEADERS)
            {
                headerStyle[col] = HEADER_STYLE_INDEX;
                header.Add(Text(col, 1, name, headerStyle));
            }

            var sheet = new XElement(NsMain + "worksheet",
                new XAttribute("xmlns", NsMain.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "r", NsRel.NamespaceName),
                new XElement(NsMain + "sheetViews",
                    new XElement(NsMain + "sheetView", new XAttribute("workbookViewId", 0),
                        new XElement(NsMain + "pane", new XAttribute("ySplit", 1), new XAttribute("topLeftCell", "A2"),
                            new XAttribute("activePane", "bottomLeft"), new XAttribute("state", "frozen")))),
                new XElement(NsMain + "cols",
                    ColumnWidth(1, 12), ColumnWidth(2, 52), ColumnWidth(3, 24), ColumnWidth(4, 16), ColumnWidth(6, 16),
                    ColumnWidth(11, 24), ColumnWidth(12, 32), ColumnWidth(16, 52)),
                new XElement(NsMain + "sheetData", header));
            var doc = new XDocument(new XDeclaration("1.0", "UTF-8", "yes"), sheet);
            using var stream = zip.CreateEntry("xl/worksheets/sheet1.xml", CompressionLevel.Optimal).Open();
            doc.Save(stream, SaveOptions.DisableFormatting);
        }

        private static XElement ColumnWidth(int column, double width) =>
            new(NsMain + "col", new XAttribute("min", column), new XAttribute("max", column),
                new XAttribute("width", width.ToString(CultureInfo.InvariantCulture)), new XAttribute("customWidth", 1));

        private static void WriteEntry(ZipArchive zip, string name, string content)
        {
            using var stream = zip.CreateEntry(name, CompressionLevel.Optimal).Open();
            using var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(false));
            writer.Write(content);
        }

        #endregion

        #region Sync

        /// <summary>Kết quả một lượt đồng bộ — page ghép thành câu báo.</summary>
        internal struct SyncResult
        {
            /// <summary>Dòng mới thêm (android + ios).</summary>
            internal int Added;

            /// <summary>Dòng đã có nhưng default_price trống → điền <see cref="PRICE_PLACEHOLDER" />.</summary>
            internal int Placeholders;

            internal bool Changed => Added > 0 || Placeholders > 0;
        }

        /// <summary>
        ///     Đồng bộ TOÀN BỘ SKU đang dùng (<paramref name="skus" /> đã lọc bảng bật) vào sheet:
        ///     <list type="bullet">
        ///         <item>(SKU, platform) chưa có dòng → thêm dòng mới, default_price = <see cref="PRICE_PLACEHOLDER" />.</item>
        ///         <item>Dòng đã có, default_price trống → chỉ ghi placeholder vào ô đó.</item>
        ///         <item>Dòng đã có và ĐÃ CÓ GIÁ → không đụng một ô nào (giá GD chốt là của GD).</item>
        ///         <item>Dòng trong sheet mà catalog không có (gói template đã bỏ) → để nguyên, tool không xoá.</item>
        ///     </list>
        ///     Trả về số dòng đã thêm/đã điền; ném exception khi không ghi được. Trước khi ghi có copy file
        ///     gốc vào <paramref name="backupDir" />.
        /// </summary>
        internal static SyncResult Sync(string absolutePath, IReadOnlyList<IapSku> skus, string backupDir)
        {
            var result = new SyncResult();
            if (skus == null || skus.Count == 0) return result;

            var current = Load(absolutePath);
            if (!current.IsValid) throw new InvalidOperationException(current.Error);

            var temp = absolutePath + ".ezgkit-tmp";
            File.Copy(absolutePath, temp, true);
            try
            {
                using (var zip = ZipFile.Open(temp, ZipArchiveMode.Update))
                {
                    var sheetEntry = FindSheetEntry(zip, SHEET_NAME, out var sheetError)
                                     ?? throw new InvalidOperationException(sheetError);
                    var doc = ReadXml(sheetEntry);
                    var sheetData = doc.Root?.Element(NsMain + "sheetData")
                                    ?? throw new InvalidOperationException("Sheet không có sheetData.");

                    // Style ô chép từ dòng dữ liệu cuối để dòng mới đồng bộ với dòng GD điền tay. Sheet mới
                    // (chỉ có hàng tiêu đề) thì không chép — không thì dòng dữ liệu bị đậm theo header.
                    var styles = current.LastDataRow > 1
                        ? CellStyles(sheetData, current.LastDataRow)
                        : new Dictionary<string, string>();
                    var rowNumber = current.LastDataRow;

                    foreach (var sku in skus)
                    foreach (var platform in new[] { PLATFORM_ANDROID, PLATFORM_IOS })
                    {
                        var productId = platform == PLATFORM_IOS ? sku.AppleId : sku.GoogleId;
                        if (string.IsNullOrEmpty(productId)) continue;

                        var existing = current.Find(platform, productId);
                        if (existing == null)
                        {
                            rowNumber++;
                            PutRow(sheetData, BuildRow(rowNumber, sku, platform, productId, styles));
                            current.Rows.Add(new IapPriceRow { RowNumber = rowNumber, Platform = platform, ProductId = productId });
                            result.Added++;
                            continue;
                        }

                        if (!IsPriceEmpty(existing.DefaultPrice)) continue; // đã có giá → không đụng
                        if (existing.DefaultPrice == PRICE_PLACEHOLDER) continue; // đã có chữ nhắc rồi
                        if (SetPlaceholder(sheetData, existing.RowNumber, styles)) result.Placeholders++;
                    }

                    if (!result.Changed) return result;

                    Directory.CreateDirectory(backupDir);
                    var backupName = Path.GetFileNameWithoutExtension(absolutePath) + "."
                                     + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".xlsx";
                    File.Copy(absolutePath, Path.Combine(backupDir, backupName), true);

                    ExpandDimension(doc.Root, rowNumber);
                    var entryName = sheetEntry.FullName;
                    sheetEntry.Delete();
                    var fresh = zip.CreateEntry(entryName, CompressionLevel.Optimal);
                    using var stream = fresh.Open();
                    doc.Save(stream, SaveOptions.DisableFormatting);
                }

                File.Copy(temp, absolutePath, true);
                return result;
            }
            finally
            {
                if (File.Exists(temp)) File.Delete(temp);
            }
        }

        /// <summary>Trống, hoặc chỉ là chữ nhắc — cả hai đều nghĩa "GD chưa chốt giá".</summary>
        internal static bool IsPriceEmpty(string price) =>
            string.IsNullOrWhiteSpace(price) || price.Trim() == PRICE_PLACEHOLDER;

        /// <summary>Ghi <see cref="PRICE_PLACEHOLDER" /> vào ô default_price của một dòng ĐÃ CÓ; các ô khác giữ nguyên.</summary>
        private static bool SetPlaceholder(XElement sheetData, int rowNumber, IReadOnlyDictionary<string, string> styles)
        {
            var row = sheetData.Elements(NsMain + "row").FirstOrDefault(x => RowNumber(x) == rowNumber);
            if (row == null) return false;

            var reference = COL_DEFAULT_PRICE + rowNumber;
            var cell = row.Elements(NsMain + "c").FirstOrDefault(c => c.Attribute("r")?.Value == reference);
            var fresh = Text(COL_DEFAULT_PRICE, rowNumber, PRICE_PLACEHOLDER, styles);
            if (cell == null)
            {
                // Chèn đúng thứ tự cột — Excel đòi ô trong dòng tăng dần theo cột.
                var after = row.Elements(NsMain + "c").FirstOrDefault(c => ColumnIndex(c.Attribute("r")?.Value) > ColumnIndex(reference));
                if (after != null) after.AddBeforeSelf(fresh);
                else row.Add(fresh);
                return true;
            }

            var style = cell.Attribute("s")?.Value;
            if (style != null) fresh.SetAttributeValue("s", style);
            cell.ReplaceWith(fresh);
            return true;
        }

        private static int ColumnIndex(string reference)
        {
            if (string.IsNullOrEmpty(reference)) return int.MaxValue;
            var match = CellRef.Match(reference);
            if (!match.Success) return int.MaxValue;
            var index = 0;
            foreach (var ch in match.Groups[1].Value) index = index * 26 + (ch - 'A' + 1);
            return index;
        }

        private static XElement BuildRow(int r, IapSku sku, string platform, string productId,
            IReadOnlyDictionary<string, string> styles)
        {
            var name = sku.ReferenceName;
            var isAndroid = platform == PLATFORM_ANDROID;
            var row = new XElement(NsMain + "row", new XAttribute("r", r));
            row.Add(Text(COL_PLATFORM, r, platform, styles));
            row.Add(Text(COL_PRODUCT_ID, r, productId, styles));
            row.Add(Text(COL_REFERENCE_NAME, r, name, styles));
            row.Add(Text(COL_PRODUCT_TYPE, r, sku.ProductType, styles));
            row.Add(Empty(COL_STATUS, r, styles));
            row.Add(Text(COL_DEFAULT_PRICE, r, PRICE_PLACEHOLDER, styles)); // GD điền giá — tool không chép iap_cost.
            row.Add(Text(COL_CURRENCY, r, DEFAULT_CURRENCY, styles));
            row.Add(Empty(COL_SUBSCRIPTION_PERIOD, r, styles));
            row.Add(Number(COL_BASE_PLAN_ID, r, DEFAULT_BASE_PLAN_ID, styles));
            row.Add(Text(COL_LOCALE, r, DEFAULT_LOCALE, styles));
            row.Add(Text(COL_TITLE, r, name, styles));
            row.Add(Text(COL_DESCRIPTION, r, name, styles));
            row.Add(Empty(COL_REVIEW_NOTE, r, styles));
            row.Add(Bool(COL_FAMILY_SHARABLE, r, false, styles));
            row.Add(Empty(COL_REVIEW_SCREENSHOT, r, styles));
            row.Add(isAndroid ? Text(COL_STORE_PRODUCT_ID, r, productId, styles) : Empty(COL_STORE_PRODUCT_ID, r, styles));
            return row;
        }

        /// <summary>Thay dòng cùng số nếu đã có (Google Sheets export sẵn ~1000 dòng rỗng có style), không thì chèn đúng thứ tự.</summary>
        private static void PutRow(XElement sheetData, XElement row)
        {
            var number = RowNumber(row);
            XElement after = null;
            foreach (var existing in sheetData.Elements(NsMain + "row"))
            {
                var n = RowNumber(existing);
                if (n == number)
                {
                    existing.ReplaceWith(row);
                    return;
                }

                if (n > number)
                {
                    after = existing;
                    break;
                }
            }

            if (after != null) after.AddBeforeSelf(row);
            else sheetData.Add(row);
        }

        private static void ExpandDimension(XElement root, int lastRow)
        {
            var dimension = root?.Element(NsMain + "dimension");
            var reference = dimension?.Attribute("ref")?.Value;
            if (string.IsNullOrEmpty(reference)) return;
            var match = Regex.Match(reference, "^([A-Z]+\\d+):([A-Z]+)(\\d+)$");
            if (!match.Success) return;
            var currentLast = int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);
            if (lastRow > currentLast)
                dimension.SetAttributeValue("ref", $"{match.Groups[1].Value}:{match.Groups[2].Value}{lastRow}");
        }

        #endregion

        #region Cell builders

        private static XElement Cell(string col, int r, IReadOnlyDictionary<string, string> styles)
        {
            var cell = new XElement(NsMain + "c", new XAttribute("r", col + r));
            if (styles.TryGetValue(col, out var s)) cell.Add(new XAttribute("s", s));
            return cell;
        }

        private static XElement Empty(string col, int r, IReadOnlyDictionary<string, string> styles) =>
            Cell(col, r, styles);

        private static XElement Text(string col, int r, string value, IReadOnlyDictionary<string, string> styles)
        {
            var cell = Cell(col, r, styles);
            cell.Add(new XAttribute("t", "inlineStr"));
            cell.Add(new XElement(NsMain + "is", new XElement(NsMain + "t", value ?? string.Empty)));
            return cell;
        }

        private static XElement Number(string col, int r, string value, IReadOnlyDictionary<string, string> styles)
        {
            var cell = Cell(col, r, styles);
            cell.Add(new XElement(NsMain + "v", value));
            return cell;
        }

        private static XElement Bool(string col, int r, bool value, IReadOnlyDictionary<string, string> styles)
        {
            var cell = Cell(col, r, styles);
            cell.Add(new XAttribute("t", "b"));
            cell.Add(new XElement(NsMain + "v", value ? "1" : "0"));
            return cell;
        }

        /// <summary>Map cột → chỉ số style <c>s</c> của dòng mẫu (dòng dữ liệu cuối). Rỗng khi sheet chưa có dòng nào.</summary>
        private static Dictionary<string, string> CellStyles(XElement sheetData, int templateRow)
        {
            var result = new Dictionary<string, string>();
            var row = sheetData.Elements(NsMain + "row").FirstOrDefault(x => RowNumber(x) == templateRow);
            if (row == null) return result;
            foreach (var cell in row.Elements(NsMain + "c"))
            {
                var s = cell.Attribute("s")?.Value;
                var reference = cell.Attribute("r")?.Value;
                if (s == null || reference == null) continue;
                var match = CellRef.Match(reference);
                if (match.Success) result[match.Groups[1].Value] = s;
            }

            return result;
        }

        #endregion

        #region OOXML plumbing

        private static ZipArchiveEntry FindSheetEntry(ZipArchive zip, string sheetName, out string error)
        {
            error = null;
            var workbook = zip.GetEntry(WORKBOOK_ENTRY);
            var rels = zip.GetEntry(WORKBOOK_RELS_ENTRY);
            if (workbook == null || rels == null)
            {
                error = "File không phải .xlsx hợp lệ (thiếu xl/workbook.xml).";
                return null;
            }

            var sheetElement = ReadXml(workbook).Descendants(NsMain + "sheet")
                .FirstOrDefault(s => string.Equals(s.Attribute("name")?.Value, sheetName, StringComparison.Ordinal));
            if (sheetElement == null)
            {
                error = $"File không có sheet tên '{sheetName}'.";
                return null;
            }

            var relId = sheetElement.Attribute(NsRel + "id")?.Value;
            var target = ReadXml(rels).Descendants(NsPkgRel + "Relationship")
                .FirstOrDefault(r => r.Attribute("Id")?.Value == relId)?.Attribute("Target")?.Value;
            if (string.IsNullOrEmpty(target))
            {
                error = $"Sheet '{sheetName}' không có relationship tới file worksheet.";
                return null;
            }

            var entryName = target.StartsWith("/", StringComparison.Ordinal)
                ? target.TrimStart('/')
                : "xl/" + target;
            var entry = zip.GetEntry(entryName);
            if (entry == null) error = $"Không thấy entry '{entryName}' trong .xlsx.";
            return entry;
        }

        private static List<string> ReadSharedStrings(ZipArchive zip)
        {
            var result = new List<string>();
            var entry = zip.GetEntry(SHARED_STRINGS_ENTRY);
            if (entry == null) return result;
            foreach (var si in ReadXml(entry).Root?.Elements(NsMain + "si") ?? Enumerable.Empty<XElement>())
                result.Add(string.Concat(si.Descendants(NsMain + "t").Select(t => t.Value)));
            return result;
        }

        private static XDocument ReadXml(ZipArchiveEntry entry)
        {
            using var stream = entry.Open();
            return XDocument.Load(stream);
        }

        private static Dictionary<string, string> ReadCells(XElement row, List<string> shared)
        {
            var cells = new Dictionary<string, string>();
            foreach (var cell in row.Elements(NsMain + "c"))
            {
                var reference = cell.Attribute("r")?.Value;
                if (reference == null) continue;
                var match = CellRef.Match(reference);
                if (!match.Success) continue;
                var value = CellValue(cell, shared);
                if (value != null) cells[match.Groups[1].Value] = value;
            }

            return cells;
        }

        private static string CellValue(XElement cell, List<string> shared)
        {
            var type = cell.Attribute("t")?.Value;
            if (type == "inlineStr")
                return string.Concat(cell.Descendants(NsMain + "t").Select(t => t.Value)).Trim();

            var v = cell.Element(NsMain + "v")?.Value;
            if (v == null) return null;
            if (type == "s")
                return int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index)
                       && index >= 0 && index < shared.Count
                    ? shared[index].Trim()
                    : null;
            if (type == "b") return v == "1" ? "TRUE" : "FALSE";
            return v.Trim();
        }

        private static int RowNumber(XElement row) =>
            int.TryParse(row.Attribute("r")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 0;

        private static string Get(Dictionary<string, string> cells, string col) =>
            cells.TryGetValue(col, out var value) ? value : null;

        #endregion
    }
}
#endif
