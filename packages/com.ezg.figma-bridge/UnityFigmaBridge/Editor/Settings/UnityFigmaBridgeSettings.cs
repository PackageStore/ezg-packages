using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityFigmaBridge.Editor.FigmaApi;
using UnityFigmaBridge.Editor.Utils;

namespace UnityFigmaBridge.Editor.Settings
{
    public class UnityFigmaBridgeSettings : ScriptableObject
    {

        [Tooltip("The FIGMA Document URL to import")]
        public string DocumentUrl;

        [Tooltip("Generate logic and linking of screens based on FIGMA's 'Prototype' settings")]
        public bool BuildPrototypeFlow=true;

        [Space(10)]
        [Tooltip("Scene used for prototype assets, including canvas")]
        public string RunTimeAssetsScenePath;

        [Tooltip("Enable Auto layout components (Horizontal/Vertical layout) (EXPERIMENTAL)")]
        public bool EnableAutoLayout = false;

        [Tooltip("C# Namespace filter for binding MonoBehaviours for screens. Use this to ensure it will only bind to MonoBehaviours in that namespace (eg specify 'MyGame.UI' to only bind MyGame.UI.PlayScreen node to 'PlayScreen')")]
        public string ScreenBindingNamespace="";

        [Tooltip("Scale for rendering server images")]
        public int ServerRenderImageScale=3;

        [Tooltip("Tick this to enable downloading missing fonts from Google Fonts")]
        public bool EnableGoogleFontsDownloads = true;

        [Tooltip("Generate a C# file containing all found screens")]
        public bool CreateScreenNameCSharpFile = false;

        [Tooltip("If false, the generator will not attempt to build any nodes marked for export")]
        public bool GenerateNodesMarkedForExport = true;

        [Tooltip("If true, download only selected pages and screens")]
        public bool OnlyImportSelectedPages = false;

        [HideInInspector]
        public List<FigmaPageData> PageDataList = new ();

        [Header("Output Folders")]
        [FolderPath, Tooltip("Root folder for generated assets. Blank = Assets/_Project/UI")]
        public string AssetsRootFolder = "";

        [FolderPath, Tooltip("Folder for screen prefabs. Blank = <root>/Screens")]
        public string ScreenPrefabFolder = "";

        [FolderPath, Tooltip("Folder for component prefabs. Blank = <root>/Components")]
        public string ComponentPrefabFolder = "";

        [FolderPath, Tooltip("Folder for page prefabs. Blank = <root>/Pages")]
        public string PagePrefabFolder = "";

        [FolderPath, Tooltip("Parent folder for image fills. Sprites go in a subfolder named after the " +
                             "Figma document, so two documents never share one folder. Blank = <root>/Sprites")]
        public string ImageFillFolder = "";

        [HideInInspector]
        public List<FigmaScreenNameOverride> ScreenNameOverrides = new();

        [Header("Screen Names")]
        [Tooltip("When true, a frame with no ScreenNameOverrides row is not imported. Rows are " +
                 "filled by the Refresh button, so this only affects frames added to the Figma " +
                 "document since the last refresh.")]
        public bool OnlyImportListedScreens = false;

        [Tooltip("Where downloaded .ttf files and generated TMP font assets go. Blank uses " +
                 "'Assets/TextMesh Pro/Fonts', beside TMP's own fonts, because these are TMP " +
                 "assets rather than Figma output.")]
        public string FontsFolder = "";

        [Tooltip("Where generated font material presets go. Blank uses the fonts folder, so a " +
                 "font's variants sit next to the font asset they derive from.")]
        public string FontMaterialPresetsFolder = "";

        [Header("Image Fills")]
        [Tooltip("Name downloaded image fills after the Figma node that uses them, grouped by " +
                 "owning component or screen, instead of the imageRef content hash. A component " +
                 "owns its art even when a screen also reaches it, because a component prefab is " +
                 "shared across screens. Turn off only to get raw imageRef filenames back.")]
        public bool NameImageFillsByNodePath = true;

        [Header("Nine-Slice")]
        [Tooltip("When true, the nine-slice pass collapses slice_ROW_COL grids into a single Image.Type.Sliced")]
        public bool CollapseSliceGrids = true;

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