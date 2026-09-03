using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityFigmaBridge.Editor.FigmaApi;
using UnityFigmaBridge.Editor.Utils;

namespace UnityFigmaBridge.Editor.Settings
{
    public class UnityFigmaBridgeSettings : ScriptableObject, IFolderDefaults
    {

        // Tooltips are Vietnamese for the team that uses this: one line of description, then one
        // example line. Unity draws tooltips as plain text, so no markup here.
        [Tooltip("URL tài liệu Figma cần import.\nVí dụ: https://www.figma.com/design/aBc123/Ten-File")]
        public string DocumentUrl;

        [Tooltip("Tự sinh liên kết chuyển screen theo Prototype của Figma.\nVí dụ: nút Play mở screen Game.")]
        public bool BuildPrototypeFlow=true;

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