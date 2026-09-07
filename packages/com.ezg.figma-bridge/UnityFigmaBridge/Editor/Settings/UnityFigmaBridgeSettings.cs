using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityFigmaBridge.Editor.FigmaApi;
using UnityFigmaBridge.Editor.Utils;

namespace UnityFigmaBridge.Editor.Settings
{
    /// <summary>Which nodes receive a LayoutElement.</summary>
    public enum LayoutElementMode
    {
        /// <summary>Every node, as in 0.2 (the element carries the Figma size as preferred size).</summary>
        Always,
        /// <summary>Only nodes whose parent frame uses Figma auto layout - the only place a layout group reads it.</summary>
        OnlyUnderAutoLayout
    }

    /// <summary>How a text node's rect relates to its glyphs.</summary>
    public enum TextFitMode
    {
        /// <summary>Auto-resized text gets a ContentSizeFitter, so the rect follows the glyphs (0.2 behaviour).</summary>
        ContentSizeFitter,
        /// <summary>The rect is fixed (padded), TMP auto-sizes the font down to fit and ellipsises the rest.</summary>
        FixedRectAutoSize
    }

    /// <summary>Texture compression written into imported sprites.</summary>
    public enum SpriteCompressionMode
    {
        Uncompressed,
        Compressed
    }

    public class UnityFigmaBridgeSettings : ScriptableObject, IFolderDefaults
    {

        // Tooltips are Vietnamese for the team that uses this: one line of description, then one
        // example line. Unity draws tooltips as plain text, so no markup here.
        [Tooltip("URL tài liệu Figma cần import.\nVí dụ: https://www.figma.com/design/aBc123/Ten-File")]
        public string DocumentUrl;

        [Tooltip("Tự sinh liên kết chuyển screen theo Prototype của Figma (mở scene runtime, thêm PrototypeFlowController).\n" +
                 "Ví dụ: nút Play mở screen Game. Mặc định tắt từ 0.3.0.")]
        public bool BuildPrototypeFlow=false;

        [Space(10)]
        [Tooltip("Scene chứa canvas và asset runtime của prototype.\nVí dụ: Assets/Scenes/Main.unity")]
        public string RunTimeAssetsScenePath;

        [Tooltip("Bật Horizontal/Vertical Layout Group theo auto layout của Figma (thử nghiệm).\n" +
                 "Ví dụ: frame auto layout dọc thành Vertical Layout Group.")]
        public bool EnableAutoLayout = false;

        [Tooltip("Chỉ bind MonoBehaviour trong namespace này vào screen.\nVí dụ: MyGame.UI")]
        public string ScreenBindingNamespace="";

        [Tooltip("Tỉ lệ khi Figma render ảnh trên server.\nVí dụ: 3 cho ảnh gấp 3 lần kích thước thiết kế.")]
        public int ServerRenderImageScale=3;

        [Tooltip("Tải font còn thiếu từ Google Fonts.\nVí dụ: Figma dùng Roboto, bridge tải Roboto.ttf.")]
        public bool EnableGoogleFontsDownloads = true;

        [Tooltip("Sinh file C# chứa tên mọi screen tìm được.\n" +
                 "Ví dụ: dùng ScreenNames.SHOP thay cho tên dạng chuỗi.")]
        public bool CreateScreenNameCSharpFile = false;

        [Tooltip("Dựng cả node được Figma đánh dấu export.\n" +
                 "Ví dụ: icon đặt Export PNG vẫn thành GameObject.")]
        public bool GenerateNodesMarkedForExport = true;

        [Tooltip("Chỉ tải các page được chọn trong danh sách bên dưới.\n" +
                 "Ví dụ: bỏ page nháp khỏi lần import.")]
        public bool OnlyImportSelectedPages = false;

        [HideInInspector]
        public List<FigmaPageData> PageDataList = new ();

        [Header("Output Folders")]
        [FolderPath, Tooltip("Thư mục gốc cho mọi asset sinh ra. Để trống dùng Assets/_Project/UI.\n" +
                             "Ví dụ: Assets/_Project/UI")]
        public string AssetsRootFolder = "";

        [FolderPath, Tooltip("Thư mục chứa prefab screen. Để trống dùng thư mục gốc kèm /Screens.\n" +
                             "Ví dụ: Assets/_Project/UI/Screens")]
        public string ScreenPrefabFolder = "";

        [FolderPath, Tooltip("Thư mục chứa prefab component. Để trống dùng thư mục gốc kèm /Components.\n" +
                             "Ví dụ: Assets/_Project/UI/Components")]
        public string ComponentPrefabFolder = "";

        [FolderPath, Tooltip("Thư mục chứa prefab page. Để trống dùng thư mục gốc kèm /Pages.\n" +
                             "Ví dụ: Assets/_Project/UI/Pages")]
        public string PagePrefabFolder = "";

        [FolderPath, Tooltip("Thư mục cha của sprite. Mỗi tài liệu Figma có một thư mục con riêng. " +
                             "Để trống dùng thư mục gốc kèm /Sprites.\n" +
                             "Ví dụ: Assets/_Project/UI/Sprites/TenTaiLieu")]
        public string ImageFillFolder = "";

        [HideInInspector]
        public List<FigmaScreenNameOverride> ScreenNameOverrides = new();

        [Header("Screen Names")]
        [Tooltip("Chỉ import screen có trong danh sách bên dưới.\n" +
                 "Ví dụ: frame mới trong Figma bị bỏ qua đến khi bấm Refresh.")]
        public bool OnlyImportListedScreens = false;

        [Tooltip("Thư mục chứa file .ttf tải về và font asset TMP. " +
                 "Để trống dùng Assets/TextMesh Pro/Fonts.\n" +
                 "Ví dụ: Assets/TextMesh Pro/Fonts")]
        public string FontsFolder = "";

        [Tooltip("Thư mục chứa material preset của font. Để trống dùng chung thư mục font.\n" +
                 "Ví dụ: Assets/TextMesh Pro/Fonts")]
        public string FontMaterialPresetsFolder = "";

        [Header("Image Fills")]
        [Tooltip("Đặt tên sprite theo node Figma dùng nó, thay cho mã hash imageRef.\n" +
                 "Ví dụ: btn_play.png thay cho 3f9a2c81.png")]
        public bool NameImageFillsByNodePath = true;

        [Header("Nine-Slice")]
        [Tooltip("Gộp lưới slice_ROW_COL thành một Image kiểu Sliced.\n" +
                 "Ví dụ: 9 ô slice thành 1 sprite có border.")]
        public bool CollapseSliceGrids = true;

        [Header("Output Shape")]
        [Tooltip("Dùng Image thường thay cho FigmaImage (mất viền/bo góc/gradient; node đó được liệt kê cho post-processor).\n" +
                 "Ví dụ: prefab ship không phụ thuộc shader của bridge.")]
        public bool PlainImages = false;

        [Tooltip("Gắn LayoutElement cho node nào. OnlyUnderAutoLayout: chỉ con của frame auto layout.\n" +
                 "Ví dụ: Always giữ nếp 0.2, mỗi node một LayoutElement.")]
        public LayoutElementMode AddLayoutElements = LayoutElementMode.Always;

        [Tooltip("Node trùng tên trong cùng cha được đánh số Name_1, Name_2 theo thứ tự anh em.\n" +
                 "Ví dụ: hai Btn_Blue thành Btn_Blue và Btn_Blue_1.")]
        public bool NumberDuplicateSiblings = false;

        [Tooltip("Regex (không phân biệt hoa thường) trên tên node để gắn Button. Để trống: không gắn theo tên.\n" +
                 "Ví dụ: button | ^Btn_")]
        public string ButtonNamePattern = "button";

        [Header("Text")]
        [Tooltip("Font TMP dùng cho mọi text, bỏ qua font của Figma và không tải Google Fonts.\n" +
                 "Ví dụ: font duy nhất của project.")]
        public TMP_FontAsset FontOverride;

        [Tooltip("ContentSizeFitter: rect bám chữ (nếp 0.2). FixedRectAutoSize: rect cố định có lề, chữ tự co, dư thì cắt bằng dấu ba chấm.\n" +
                 "Ví dụ: FixedRectAutoSize khi font thật khác font Figma.")]
        public TextFitMode TextFitMode = TextFitMode.ContentSizeFitter;

        [Tooltip("Hệ số nới rộng rect chữ auto-size theo chiều ngang (FixedRectAutoSize).\nVí dụ: 1.25 = rộng thêm 25%.")]
        public float TextWidthPadding = 1.25f;

        [Tooltip("Hệ số nới rect chữ auto-size theo chiều dọc (FixedRectAutoSize).\nVí dụ: 1.1 = cao thêm 10%.")]
        public float TextHeightPadding = 1.1f;

        [Tooltip("characterSpacing của TMP cho mọi text.\nVí dụ: -0.7 khớp cách chữ của Figma nhất.")]
        public float CharacterSpacing = -0.7f;

        [Header("Sprites")]
        [Tooltip("Bật mipmap cho sprite tải về. Sprite UI không cần; mặc định tắt từ 0.3.0.\n" +
                 "Ví dụ: bật khi ảnh được scale nhỏ nhiều lần trong world space.")]
        public bool SpriteMipmaps = false;

        [Tooltip("Nén texture cho sprite tải về.\nVí dụ: Uncompressed để giữ đúng màu khi soi pixel.")]
        public SpriteCompressionMode SpriteCompression = SpriteCompressionMode.Uncompressed;

        string IFolderDefaults.DefaultFolder(string propertyPath)
        {
            var folders = FigmaPaths.Resolve(this, "<document name>", warnOnInvalid: false);
            switch (propertyPath)
            {
                case nameof(AssetsRootFolder): return folders.Root;
                case nameof(ScreenPrefabFolder): return folders.Screens;
                case nameof(ComponentPrefabFolder): return folders.Components;
                case nameof(PagePrefabFolder): return folders.Pages;
                case nameof(ImageFillFolder): return folders.ImageFillParent;
                default: return null;
            }
        }

        public string FileId {
            get
            {
                var (isValid, fileId) = FigmaApiUtils.GetFigmaDocumentIdFromUrl(DocumentUrl);
                return isValid ? fileId : "";
            }
        }

        public void RefreshForUpdatedPages(FigmaFile file)
        {
            // Get all pages from Figma Doc
            var pageNodeList = FigmaDataUtils.GetPageNodes(file);
            var downloadPageNodeIdList = pageNodeList.Select(p => p.id).ToList();

            // Get a list of all pages in the settings file
            var settingsPageDataIdList = PageDataList.Select(p => p.NodeId).ToList();

            // Build a list of all new pages to add
            var addPageIdList = downloadPageNodeIdList.Except(settingsPageDataIdList);
            foreach (var addPageId in addPageIdList)
            {
                var addNode = pageNodeList.FirstOrDefault(p => p.id == addPageId);
                PageDataList.Add(new FigmaPageData(addNode.name, addNode.id));
            }

            // Build a list of removed pages to remove from list
            var deletePageIdList = settingsPageDataIdList.Except(downloadPageNodeIdList);
            foreach (var deletePageId in deletePageIdList)
            {
                var index = PageDataList.FindIndex(p => p.NodeId == deletePageId);
                PageDataList.RemoveAt(index);
            }
            PageDataList.OrderBy(p => p.NodeId);
        }

        /// <summary>
        /// Rebuild ScreenNameOverrides from the document: one row per screen frame, in document
        /// order, grouped by the page it sits on. Prefab names and import toggles already entered
        /// are carried over; a row whose frame no longer exists is dropped.
        /// </summary>
        public void RefreshForUpdatedScreens(FigmaFile file)
        {
            var existingRows = new Dictionary<string, FigmaScreenNameOverride>();
            foreach (var row in ScreenNameOverrides)
            {
                if (string.IsNullOrWhiteSpace(row.FrameName)) continue;
                existingRows[row.FrameName] = row;
            }

            var refreshedRows = new List<FigmaScreenNameOverride>();
            var seenNames = new HashSet<string>();

            foreach (var pageNode in FigmaDataUtils.GetPageNodes(file))
            {
                foreach (var screenNode in FigmaDataUtils.GetScreenNodes(pageNode))
                {
                    if (!seenNames.Add(screenNode.name))
                    {
                        Debug.LogWarning("[UnityFigmaBridge] Two screen frames are both named " +
                                         $"'{screenNode.name}'. The frame name is the lookup key, " +
                                         "so only one row is kept - rename one of them in Figma.");
                        continue;
                    }

                    if (!existingRows.TryGetValue(screenNode.name, out var row))
                        row = new FigmaScreenNameOverride { FrameName = screenNode.name };

                    row.PageName = pageNode.name;
                    row.PageNodeId = pageNode.id;
                    refreshedRows.Add(row);
                }
            }

            ScreenNameOverrides = refreshedRows;
        }
    }

    [Serializable]
    public class FigmaPageData
    {
        public string Name;
        public string NodeId;
        public bool Selected;

        public FigmaPageData(){}

        public FigmaPageData(string name, string nodeId)
        {
            Name = name;
            NodeId = nodeId;
            Selected = true; // default is true
        }
    }

    [Serializable]
    public class FigmaScreenNameOverride
    {
        public string FrameName;
        public string PrefabName;

        // Inverted so a row deserialized from an asset written before this field existed still
        // imports, and so a row added by Refresh imports without setting anything.
        public bool ExcludeFromImport;

        public string PageName;
        public string PageNodeId;
    }
}