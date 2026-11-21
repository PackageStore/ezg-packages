using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using FigCraftBridge.Editor.Components;
using FigCraftBridge.Editor.FigmaApi;
using FigCraftBridge.Editor.Fonts;
using FigCraftBridge.Editor.NineSlice;
using FigCraftBridge.Editor.Nodes;
using FigCraftBridge.Editor.PostProcess;
using FigCraftBridge.Editor.PrototypeFlow;
using FigCraftBridge.Editor.Settings;
using FigCraftBridge.Editor.Source;
using FigCraftBridge.Editor.Utils;
using FigCraftBridge.Runtime.UI;
using Object = UnityEngine.Object;

namespace FigCraftBridge.Editor
{
    /// <summary>
    ///  Manages Figma importing and document creation
    /// </summary>
    public static class FigCraftBridgeImporter
    {
        /// <summary>Package version, written into sidecars so a stale one can be told apart.</summary>
        public const string PACKAGE_VERSION = "0.3.1";

        /// <summary>
        ///     When true (or in batch mode) no modal dialog is shown: every message goes to the
        ///     console as an error and the operation stops. Set it before driving the importer from
        ///     automation (CI, an MCP session) where nobody can click OK.
        /// </summary>
        public static bool SuppressDialogs;

        private static bool DialogsSuppressed => SuppressDialogs || Application.isBatchMode;

        /// <summary>Modal dialog, or a console error with the "cancel" answer when dialogs are suppressed.</summary>
        private static bool Dialog(string title, string message, string ok, string cancel = null)
        {
            if (DialogsSuppressed)
            {
                Debug.LogError($"[FigCraftBridge] {title}: {message}");
                return false;
            }
            return cancel == null
                ? EditorUtility.DisplayDialog(title, message, ok)
                : EditorUtility.DisplayDialog(title, message, ok, cancel);
        }

        /// <summary>
        /// The settings asset, containing preferences for importing
        /// </summary>
        private static FigCraftBridgeSettings s_FigCraftBridgeSettings;

        public const string PROGRESS_BOX_TITLE = "Importing FigCraft Document";

        /// <summary>
        /// Scale of every server render: one texel per design unit. Fixed on purpose, with no setting,
        /// because a higher scale grows each render's pixels, download and import time by scale².
        /// </summary>
        private const int SERVER_RENDER_SCALE = 1;

        /// <summary>Renders per progress-bar step.</summary>
        private const int MAX_SERVER_RENDER_IMAGE_BATCH_SIZE = 300;

        private static IFigmaSource s_Source;
        private const string OfflineRequirementsFailed = "Requirements not met for the offline re-import: see the preceding dialog or console message.";
        private static FigCraftSourceKind ActiveSourceKind => s_Source?.Kind ?? s_FigCraftBridgeSettings?.Source ?? FigCraftSourceKind.File;

        /// <summary>
        /// Active canvas used for construction
        /// </summary>
        private static Canvas s_SceneCanvas;

        /// <summary>
        /// The flowScreen controller to mange prototype functionality
        /// </summary>
        private static PrototypeFlowController s_PrototypeFlowController;

        /// <summary>Download the document and rebuild every output from it.</summary>
        public static void SyncDocument()
        {
            SyncAsync(offline: false);
        }

        /// <summary>
        ///     Rebuild every output from the document cached by the last online Sync
        ///     (<c>Assets/FigmaOutput.json</c>) and the image files already on disk. No request
        ///     reaches Figma, so this works when the seat's API quota is spent. Fills or
        ///     server-rendered images that were never downloaded are reported, not fetched.
        /// </summary>
        public static void SyncDocumentOffline()
        {
            SyncAsync(offline: true);
        }

        /// <summary>
        ///     Run every <see cref="IFigmaImportPostProcessor"/> against the prefabs already on
        ///     disk, without rebuilding them. Uses the cached document when present for frame data.
        /// </summary>
        public static void RunPostProcessorsOnly()
        {
            if (!CheckRequirements(requireSource: false)) return;

            var cachedDocument = FigmaApiUtils.LoadCachedDocument();
            if (cachedDocument == null)
                Debug.LogWarning("[FigCraftBridge][PostProcess] No cached document at " +
                                 $"'{FigmaApiUtils.CachedDocumentPath}' - frames are name-only stubs");

            FigmaPaths.Configure(s_FigCraftBridgeSettings, cachedDocument?.name);
            var context = PostProcessorRunner.BuildContextFromDisk(s_FigCraftBridgeSettings, cachedDocument);
            Debug.Log($"[FigCraftBridge][PostProcess] Running post-processors on {context.Screens.Count} screen prefab(s) from disk");
            try
            {
                PostProcessorRunner.Run(context);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                AssetDatabase.Refresh();
            }
        }

        /// <summary>True from the start of a Sync until it has finished or stopped. Automation polls it.</summary>
        public static bool ImportInProgress { get; private set; }

        /// <summary>Message of the error that stopped the last Sync; null when it finished or is running.</summary>
        public static string LastImportError { get; private set; }

        /// <summary>UTC time the last Sync started, as ISO 8601.</summary>
        public static string LastImportStartedUtc { get; private set; }

        /// <summary>
        ///     UTC time the last Sync finished without an error, as ISO 8601. A domain reload kills a
        ///     running Sync without an error, so compare this with the start time rather than trusting
        ///     <see cref="ImportInProgress"/> alone.
        /// </summary>
        public static string LastImportCompletedUtc { get; private set; }

        private static async void SyncAsync(bool offline)
        {
            ImportInProgress = true;
            LastImportError = null;
            LastImportStartedUtc = DateTime.UtcNow.ToString("o");
            FigmaImportTimer.Start();
            try
            {
                await SyncCore(offline);
                if (LastImportError == null) LastImportCompletedUtc = DateTime.UtcNow.ToString("o");
            }
            catch (Exception e)
            {
                LastImportError = e.Message;
                Debug.LogException(e);
            }
            finally
            {
                s_Source?.Dispose();
                s_Source = null;
                ImportInProgress = false;
                FigmaImportTimer.Finish(LastImportError);
            }
        }

        private static async Task SyncCore(bool offline)
        {
            FigmaImportTimer.Begin("Check settings");
            var requirementsMet = CheckRequirements(requireSource: !offline);
            if (!requirementsMet)
            {
                LastImportError ??= offline ? OfflineRequirementsFailed : FigCraftSourceText.RequirementsFailed(ActiveSourceKind);
                return;
            }
            string importKey;
            if (!offline)
            {
                var sourceDescription = FigmaSources.Describe(s_FigCraftBridgeSettings);
                Debug.Log($"[FigCraftBridge] Source: {sourceDescription}");
                FigmaImportTimer.SetDetail("Check settings", sourceDescription);
                var target = ResolveTargetOrReport();
                if (target == null) return;
                s_Source = FigmaSources.Create(s_FigCraftBridgeSettings);
                importKey = target.Value.Key;
            }
            else
            {
                importKey = s_FigCraftBridgeSettings.ListsFileKey;
            }

            FigmaFile figmaFile;
            if (offline)
            {
                FigmaImportTimer.Begin("Load cached document");
                figmaFile = FigmaApiUtils.LoadCachedDocument();
                if (figmaFile == null)
                {
                    ReportError($"No cached FigCraft document at '{FigmaApiUtils.CachedDocumentPath}'. " +
                                "Sync from FigCraft once, then the offline re-import can rebuild from it.", "");
                    return;
                }
                Debug.Log($"[FigCraftBridge] Offline re-import from cache: '{figmaFile.name}' " +
                          $"(lastModified {figmaFile.lastModified}, version {figmaFile.version})");
            }
            else
            {
                figmaFile = await DownloadFigmaDocument(importKey);
                if (figmaFile == null) return;
                BindListsToFile(importKey, figmaFile);
            }
            if (File.Exists(FigmaApiUtils.CachedDocumentPath))
                FigmaImportTimer.SetDetail(offline ? "Load cached document" : "Read document (FigCraft)",
                    (new FileInfo(FigmaApiUtils.CachedDocumentPath).Length / 1048576.0).ToString("0.0", CultureInfo.InvariantCulture) + " MB");

            var pageNodeList = FigmaDataUtils.GetPageNodes(figmaFile);

            if (s_FigCraftBridgeSettings.OnlyImportSelectedPages)
            {
                var downloadPageNodeIdList = pageNodeList.Select(p => p.id).ToList();
                downloadPageNodeIdList.Sort();

                var settingsPageDataIdList = s_FigCraftBridgeSettings.PageDataList.Select(p => p.NodeId).ToList();
                settingsPageDataIdList.Sort();

                if (!settingsPageDataIdList.SequenceEqual(downloadPageNodeIdList))
                {
                    ReportError("The pages found in the FigCraft document have changed - check your settings file and Sync again when ready", "");

                    // Apply the new page list to serialized data and select to allow the user to change
                    s_FigCraftBridgeSettings.RefreshForUpdatedPages(figmaFile);
                    Selection.activeObject = s_FigCraftBridgeSettings;
                    EditorUtility.SetDirty(s_FigCraftBridgeSettings);
                    AssetDatabase.SaveAssetIfDirty(s_FigCraftBridgeSettings);
                    AssetDatabase.Refresh();

                    return;
                }

                var enabledPageIdList = s_FigCraftBridgeSettings.PageDataList.Where(p => p.Selected).Select(p => p.NodeId).ToList();

                if (enabledPageIdList.Count <= 0)
                {
                    ReportError("'Import Selected Pages' is selected, but no pages are selected for import", "");
                    SelectSettings();
                    return;
                }

                pageNodeList = pageNodeList.Where(p => enabledPageIdList.Contains(p.id)).ToList();
            }

            await ImportDocument(importKey, figmaFile, pageNodeList, offline);
        }

        /// <summary>
        /// Check to make sure all requirements are met before syncing
        /// </summary>
        /// <param name="requireSource">False for offline work that never calls FigCraft.</param>
        public static bool CheckRequirements(bool requireSource = true) {

            // Find the settings asset if it exists
            if (s_FigCraftBridgeSettings == null)
                s_FigCraftBridgeSettings = FigCraftBridgeSettingsProvider.FindUnityBridgeSettingsAsset();

            if (s_FigCraftBridgeSettings == null)
            {
                if (
                    Dialog("No FigCraft Bridge Settings File",
                        "Create a new FigCraft Bridge settings file? ", "Create", "Cancel"))
                {
                    s_FigCraftBridgeSettings =
                        FigCraftBridgeSettingsProvider.GenerateFigCraftBridgeSettingsAsset();
                }
                else
                {
                    return false;
                }
            }

            if (Shader.Find("TextMeshPro/Mobile/Distance Field")==null)
            {
                Dialog("Text Mesh Pro" ,"You need to install TestMeshPro Essentials. Use Window->Text Mesh Pro->Import TMP Essential Resources","OK");
                return false;
            }

            if (requireSource)
            {
                try
                {
                    FigmaFileTargets.Resolve(s_FigCraftBridgeSettings);
                }
                catch (FigmaFileTargetException e)
                {
                    var noCli = FigCraftCli.Locate(s_FigCraftBridgeSettings.FigCraftCliPath) == null;
                    Dialog(noCli ? FigCraftSourceText.NoCliTitle : FigCraftSourceText.NoFileTitle, e.Message, "OK");
                    return false;
                }
            }

            if (Application.isPlaying)
            {
                Dialog("FigCraft Bridge Importer","Please exit play mode before importing", "OK");
                return false;
            }

            // Check all requirements for run time if required
            if (s_FigCraftBridgeSettings.BuildPrototypeFlow)
            {
                if (!CheckRunTimeRequirements())
                    return false;
            }

            return true;

        }


        private static bool CheckRunTimeRequirements()
        {
            if (string.IsNullOrEmpty(s_FigCraftBridgeSettings.RunTimeAssetsScenePath))
            {
                if (
                    Dialog("No Figma Bridge Scene set",
                        "Use current scene for generating prototype flow? ", "OK", "Cancel"))
                {
                    var currentScene = SceneManager.GetActiveScene();
                    s_FigCraftBridgeSettings.RunTimeAssetsScenePath = currentScene.path;
                    EditorUtility.SetDirty(s_FigCraftBridgeSettings);
                    AssetDatabase.SaveAssetIfDirty(s_FigCraftBridgeSettings);
                }
                else
                {
                    return false;
                }
            }

            // If current scene doesnt match, switch
            if (SceneManager.GetActiveScene().path != s_FigCraftBridgeSettings.RunTimeAssetsScenePath)
            {
                if (Dialog("Figma Bridge Scene",
                        "Current Scene doesnt match Runtime asset scene - switch scenes?", "OK", "Cancel"))
                {
                    EditorSceneManager.OpenScene(s_FigCraftBridgeSettings.RunTimeAssetsScenePath);
                }
                else
                {
                    return false;
                }
            }

            // Find a canvas in the active scene
            s_SceneCanvas = Object.FindObjectOfType<Canvas>();

            // If doesnt exist create new one
            if (s_SceneCanvas == null)
            {
                s_SceneCanvas = CreateCanvas(true);
            }

            // If we are building a prototype, ensure we have a UI Controller component
            s_PrototypeFlowController = s_SceneCanvas.GetComponent<PrototypeFlowController>();
            if (s_PrototypeFlowController== null)
                s_PrototypeFlowController = s_SceneCanvas.gameObject.AddComponent<PrototypeFlowController>();

            return true;
        }

        static void SelectSettings()
        {
            var bridgeSettings=FigCraftBridgeSettingsProvider.FindUnityBridgeSettingsAsset();
            Selection.activeObject = bridgeSettings;
        }

        private static Canvas CreateCanvas(bool createEventSystem)
        {
            // Canvas
            var canvasGameObject = new GameObject("Canvas");
            var canvas=canvasGameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasGameObject.AddComponent<GraphicRaycaster>();

            if (!createEventSystem) return canvas;

            var existingEventSystem = Object.FindObjectOfType<EventSystem>();
            if (existingEventSystem == null)
            {
                // Create new event system
                var eventSystemGameObject = new GameObject("EventSystem");
                existingEventSystem=eventSystemGameObject.AddComponent<EventSystem>();
            }

            var pointerInputModule = Object.FindObjectOfType<PointerInputModule>();
            if (pointerInputModule == null)
            {
                // TODO - Allow for new input system?
                existingEventSystem.gameObject.AddComponent<StandaloneInputModule>();
            }

            return canvas;
        }


        private static void ReportError(string message,string error)
        {
            LastImportError = message;
            Dialog("FigCraft Bridge Error",message,"Ok");
            Debug.LogWarning($"{message}\n {error}\n");
        }

        private static void ReportApiError(string context, Exception e)
        {
            var firstLine = (e.Message ?? string.Empty).Split('\n')[0].Trim();
            ReportError($"{context}\n{firstLine}{BuildDocumentDecodeHint(e)}", e.ToString());
        }

        /// <summary>
        ///     Gợi ý khi document không parse được vì Figma trả về một giá trị enum mới mà bridge chưa biết
        ///     (vd effect <c>TEXTURE</c> ra đời sau khi bridge được viết). Rỗng khi không phải lỗi này.
        /// </summary>
        public static string BuildDocumentDecodeHint(Exception e)
        {
            var text = e?.ToString() ?? string.Empty;
            if (!text.Contains("Problem decoding")) return string.Empty;

            var match = System.Text.RegularExpressions.Regex.Match(text,
                "Error converting value \"(?<value>[^\"]*)\" to type '(?<type>[^']*)'\\. Path '(?<path>[^']*)'");
            if (!match.Success)
                return "\n\nKhông đọc được document Figma. Nếu file vừa dùng tính năng mới của Figma, cập nhật " +
                       "com.ezg.figcraft-bridge lên bản mới hơn; trong lúc chờ dùng 'Re-import from cache (offline)'.";

            var type = match.Groups["type"].Value;
            var shortType = type.Substring(type.LastIndexOfAny(new[] { '.', '+' }) + 1);
            return $"\n\nFigma trả về giá trị '{match.Groups["value"].Value}' cho {shortType} mà bridge " +
                   $"{PackageVersion()} chưa biết (tại {match.Groups["path"].Value}). Thường là tính năng Figma " +
                   "mới ra, vd effect Texture/Noise/Glass.\n" +
                   "• Cập nhật com.ezg.figcraft-bridge lên bản mới hơn, hoặc gửi dòng này cho team package.\n" +
                   "• Trong lúc chờ: gỡ tính năng đó khỏi layer trong Figma, hoặc 'Re-import from cache (offline)'.";
        }

        private static string PackageVersion()
        {
            var info = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(FigCraftBridgeImporter).Assembly);
            return info != null ? info.version : string.Empty;
        }

        private static FigmaFileTarget? ResolveTargetOrReport()
        {
            try
            {
                return FigmaFileTargets.Resolve(s_FigCraftBridgeSettings);
            }
            catch (FigmaFileTargetException e)
            {
                ReportError(e.Message, e.ToString());
                return null;
            }
        }

        public static async Task<FigmaFile> DownloadCurrentDocument()
        {
            if (!CheckRequirements()) return null;
            var target = ResolveTargetOrReport();
            if (target == null) return null;
            var figmaFile = await DownloadFigmaDocument(target.Value.Key);
            BindListsToFile(target.Value.Key, figmaFile);
            return figmaFile;
        }

        private static void BindListsToFile(string fileKey, FigmaFile figmaFile)
        {
            if (figmaFile == null) return;
            var listsKeyBefore = s_FigCraftBridgeSettings.ListsFileKey;
            if (!s_FigCraftBridgeSettings.BindListsToFile(fileKey, figmaFile) &&
                listsKeyBefore == s_FigCraftBridgeSettings.ListsFileKey) return;
            EditorUtility.SetDirty(s_FigCraftBridgeSettings);
            AssetDatabase.SaveAssetIfDirty(s_FigCraftBridgeSettings);
        }

        public static async Task<FigmaFile> DownloadFigmaDocument(string fileId)
        {
            FigmaImportTimer.Begin("Read document (FigCraft)");
            EditorUtility.DisplayProgressBar(PROGRESS_BOX_TITLE, "Reading the FigCraft document", 0);
            var ownedSource = s_Source == null ? FigmaSources.Create(s_FigCraftBridgeSettings) : null;
            try
            {
                return await (s_Source ?? ownedSource).GetDocument(fileId);
            }
            catch (Exception e)
            {
                ReportApiError("Error reading the FigCraft document", e);
            }
            finally
            {
                ownedSource?.Dispose();
                EditorUtility.ClearProgressBar();
            }
            return null;
        }

        /// <summary>Screens come only from the screens page, so a file without it imports no screen at all.</summary>
        public static void WarnMissingRequiredPages(FigmaFile figmaFile, FigCraftBridgeSettings settings)
        {
            var message = MissingRequiredPagesMessage(figmaFile.document.children.Select(page => page.name).ToList(), settings);
            if (message != null) Debug.LogWarning("[FigCraftBridge] " + message);
        }

        /// <returns>Null when the file has both required pages.</returns>
        public static string MissingRequiredPagesMessage(IReadOnlyCollection<string> pageNames, FigCraftBridgeSettings settings)
        {
            var missing = FigmaDataUtils.MissingRequiredPages(pageNames, settings.ScreensPageName, settings.ComponentsPageName);
            if (missing.Count == 0) return null;
            return $"The Figma file has no page named {string.Join(" or ", missing.Select(name => $"'{name}'"))}. " +
                   $"Screens are read only from '{settings.ScreensPageName}', so without it no screen is listed or imported. " +
                   $"Pages in the file: {string.Join(", ", pageNames.Select(name => $"'{name}'"))}. " +
                   "Rename the page in Figma, or change Screens Page Name / Components Page Name in the settings, then refresh.";
        }

        private static async Task ImportDocument(string fileId, FigmaFile figmaFile, List<Node> downloadPageNodeList, bool offline)
        {

            FigmaImportTimer.Begin("Analyse document (selection, names, render list)");
            // Build a list of page IDs to download
            var downloadPageIdList = downloadPageNodeList.Select(p => p.id).ToList();

            FigmaPaths.Configure(s_FigCraftBridgeSettings, figmaFile.name);
            ComponentManager.ResetProcessedSets();
            WarnMissingRequiredPages(figmaFile, s_FigCraftBridgeSettings);

            var importScope = s_FigCraftBridgeSettings.ImportSelectionOnly
                ? FigmaImportScope.Create(figmaFile, FigmaImportScope.SelectedNodeIds(s_FigCraftBridgeSettings, downloadPageNodeList))
                : null;
            if (importScope != null)
                Debug.Log($"[FigCraftBridge] Importing the selection: {importScope.Roots.Count} screen(s) and component(s), " +
                          $"{importScope.Roots.Count(importScope.IsDependency)} of them reached through instances");

            if (s_FigCraftBridgeSettings.NameImageFillsByNodePath)
                FigmaImageFillNamer.Build(figmaFile, downloadPageNodeList);
            else
                FigmaImageFillNamer.Clear();

            // Ensure we have all required directories, and remove existing files
            // TODO - Once we move to processing only differences, we won't remove existing files
            FigmaPaths.CreateRequiredDirectories();

            // Next build a list of all externally referenced components not included in the document (eg
            // from external libraries) and download
            var externalComponentList = FigmaDataUtils.FindMissingComponentDefinitions(figmaFile);

            // Some of the nodes, we'll want to identify to use Figma server side rendering (eg vector shapes, SVGs)
            // First up create a list of nodes we'll substitute with rendered images
            var serverRenderNodes = FigmaDataUtils.FindAllServerRenderNodesInFile(figmaFile,externalComponentList,downloadPageIdList,
                s_FigCraftBridgeSettings.ServerRenderTopLevelExports, importScope, s_FigCraftBridgeSettings.ScreensPageName);
            FigmaImageFillNamer.BuildServerRenders(figmaFile,
                s_FigCraftBridgeSettings.NameServerRendersByNodePath
                    ? serverRenderNodes.Where(n => n.RenderType != ServerRenderType.Export).Select(n => n.SourceNode.id)
                    : Enumerable.Empty<string>());
            var serverRenderScale = SERVER_RENDER_SCALE;
            var serverRenderBatchSize = Mathf.Clamp(s_FigCraftBridgeSettings.ServerRenderBatchSize, 1, MAX_SERVER_RENDER_IMAGE_BATCH_SIZE);

            // Request a render of these nodes on the server if required
            var serverRenderData=new List<FigmaServerRenderData>();
            ServerRenderCache serverRenderCache = null;
            var renderDownloads = new List<FigmaApiUtils.FigmaDownloadQueueItem>();
            if (serverRenderNodes.Count > 0 && !offline)
            {
                FigmaImportTimer.Begin("Server render cache check");
                serverRenderCache = ServerRenderCache.Check(FigmaApiUtils.CachedDocumentPath, serverRenderNodes, serverRenderScale,
                    s_FigCraftBridgeSettings, FigmaDataUtils.GetPatternSourceNodeIds(figmaFile, downloadPageIdList, importScope));
                var staleRenderNodes = serverRenderCache.StaleNodes;

                FigmaImportTimer.Begin("Server render requests");
                var renderRequestCount = 0;
                string RenderRequestDetail() =>
                    $"{serverRenderNodes.Count} nodes: {serverRenderNodes.Count - staleRenderNodes.Count} cached, " +
                    $"{staleRenderNodes.Count} rendered, {renderRequestCount} request(s), scale {serverRenderScale}";
                FigmaImportTimer.SetDetail("Server render requests", RenderRequestDetail());
                // A pattern tile is the source node's layout box. Every other render keeps what draws
                // outside the box (outside strokes, shadows): its RectTransform covers the render bounds.
                var renderedCount = 0;
                foreach (var group in staleRenderNodes.GroupBy(n => n.RenderType == ServerRenderType.PatternSource))
                {
                    var ids = group.Select(n => n.SourceNode.id).ToList();
                    for (var startIndex = 0; startIndex < ids.Count; startIndex += serverRenderBatchSize)
                    {
                        var nodeBatch = ids.GetRange(startIndex, Mathf.Min(serverRenderBatchSize, ids.Count - startIndex));
                        EditorUtility.DisplayProgressBar(PROGRESS_BOX_TITLE,
                            $"Rendering in FigCraft {renderedCount}/{staleRenderNodes.Count}",
                            (float)renderedCount / staleRenderNodes.Count);
                        try
                        {
                            renderRequestCount++;
                            FigmaImportTimer.SetDetail("Server render requests", RenderRequestDetail());
                            serverRenderData.Add(await s_Source.GetServerRenderData(fileId, nodeBatch, serverRenderScale, group.Key));
                            renderedCount += nodeBatch.Count;
                        }
                        catch (Exception e)
                        {
                            EditorUtility.ClearProgressBar();
                            ReportApiError("Error rendering nodes in FigCraft", e);
                            return;
                        }
                    }
                }
            }
            else if (serverRenderNodes.Count > 0)
            {
                ReportMissingFiles("server-rendered image(s)", serverRenderNodes
                    .Select(n => FigmaPaths.GetPathForServerRenderedImage(n.SourceNode.id, serverRenderNodes))
                    .Where(p => !File.Exists(p)).ToList());
            }

            FigmaImportTimer.Begin("Check existing sprites, list image fills");
            // Make sure that existing downloaded assets are in the correct format
            FigmaApiUtils.CheckExistingAssetProperties();

            // Track fills that are actually used. This is needed as FIGMA has a way of listing any bitmap used rather than active
            var foundImageFills = FigmaDataUtils.GetAllImageFillIdsFromFile(figmaFile,downloadPageIdList,importScope);
            var tiledImageFills = FigmaDataUtils.GetTiledImageFillIds(figmaFile);
            // Source nodes of PATTERN fills tile too, so their server render imports with wrap Repeat
            var patternSourceNodeIds = FigmaDataUtils.GetPatternSourceNodeIds(figmaFile, downloadPageIdList, importScope);

            if (!offline)
            {
                // Get image fill data for the document (list of urls to download any bitmap data used)
                FigmaImportTimer.Begin("Image fills from the .figcraft archive");
                FigmaImageFillData activeFigmaImageFillData;
                EditorUtility.DisplayProgressBar(PROGRESS_BOX_TITLE, $"Downloading image fill data", 0);
                try
                {
                    activeFigmaImageFillData = await s_Source.GetImageFillData(fileId, foundImageFills);
                }
                catch (Exception e)
                {
                    EditorUtility.ClearProgressBar();
                    ReportApiError("Error reading FigCraft image fills", e);
                    return;
                }

                // Generate a list of all items that need to be downloaded
                var downloadList =
                    FigmaApiUtils.GenerateDownloadQueue(activeFigmaImageFillData,foundImageFills, serverRenderData, serverRenderNodes);
                renderDownloads.AddRange(downloadList.Where(item =>
                    item.FileType == FigmaApiUtils.FigmaDownloadQueueItem.FigmaFileType.ServerRenderedImage));

                // Download all required files
                FigmaApiUtils.DownloadFiles(downloadList, s_FigCraftBridgeSettings, tiledImageFills, patternSourceNodeIds);
            }
            else
            {
                var reachedImageFills = foundImageFills.Where(imageRef => !FigmaImageFillNamer.IsUnreachable(imageRef)).ToList();
                ReportMissingFiles("image fill(s)", reachedImageFills
                    .Select(FigmaPaths.GetPathForImageFill)
                    .Where(p => !File.Exists(p)).ToList());
                // An online Sync downloads these again; offline they import with the art on disk
                ReportStaleImageFills(reachedImageFills
                    .Where(imageRef => File.Exists(FigmaPaths.GetPathForImageFill(imageRef)) && !FigmaApiUtils.ImageFillIsCurrent(imageRef))
                    .Select(FigmaPaths.GetPathForImageFill).ToList());
            }

            // A cached render was sliced when it was downloaded and keeps that importer border
            var downloadedRenderIds = new HashSet<string>(renderDownloads.Where(item => item.Succeeded).Select(item => item.NodeId));
            var processedRenderNodes = offline ? serverRenderNodes : serverRenderNodes.Where(n => downloadedRenderIds.Contains(n.SourceNode.id)).ToList();
            FigmaImportTimer.Begin("Whiten tinted renders");
            ServerRenderTint.Run(processedRenderNodes, serverRenderNodes);
            ServerRenderMultiply.Run(processedRenderNodes, serverRenderNodes);
            if (s_FigCraftBridgeSettings.SliceServerRenders)
            {
                FigmaImportTimer.Begin("Slice server renders");
                ServerRenderSlicer.Run(processedRenderNodes, serverRenderScale);
            }
            serverRenderCache?.Record(downloadedRenderIds);

            FigmaImportTimer.Begin("Crop FILL image fills");
            ImageFillCover.Run(figmaFile, foundImageFills);

            FigmaImportTimer.Begin("Fonts");
            // Generate font mapping data
            var figmaFontMapTask = FontManager.GenerateFontMapForDocument(figmaFile,
                s_FigCraftBridgeSettings.EnableGoogleFontsDownloads && !offline,
                s_FigCraftBridgeSettings.FontOverride);
            await figmaFontMapTask;
            var fontMap = figmaFontMapTask.Result;


            FigmaImportTimer.Begin("Prepare build data");
            var componentData = new FigmaBridgeComponentData
            {
                MissingComponentDefinitionsList = externalComponentList,
            };
            // A partial build meets fewer components, so duplicate-name suffixes are counted over the whole document
            if (importScope != null) componentData.CountComponentNames(figmaFile, s_FigCraftBridgeSettings.GenerateNodesMarkedForExport);

            // Stores necessary importer data needed for document generator.
            var figmaBridgeProcessData = new FigmaImportProcessData
            {
                Settings=s_FigCraftBridgeSettings,
                SourceFile = figmaFile,
                ComponentData = componentData,
                ServerRenderNodes = serverRenderNodes,
                ServerRenderScale = serverRenderScale,
                PrototypeFlowController = s_PrototypeFlowController,
                FontMap = fontMap,
                PrototypeFlowStartPoints = FigmaDataUtils.GetAllPrototypeFlowStartingPoints(figmaFile),
                SelectedPagesForImport = downloadPageNodeList,
                NodeLookupDictionary = FigmaDataUtils.BuildNodeLookupDictionary(figmaFile),
                ImportScope = importScope,
                ScreenNodeIds = new HashSet<string>(FigmaDataUtils.GetScreenNodes(figmaFile, s_FigCraftBridgeSettings.ScreensPageName).Select(screen => screen.id)),
                Offline = offline
            };


            // Clear the existing screens on the flowScreen controller
            if (s_FigCraftBridgeSettings.BuildPrototypeFlow)
            {
                if (figmaBridgeProcessData.PrototypeFlowController)
                    figmaBridgeProcessData.PrototypeFlowController.ClearFigmaScreens();
            }
            else
            {
                s_SceneCanvas = CreateCanvas(false);
            }

            try
            {
                FigmaAssetGenerator.BuildFigmaFile(s_SceneCanvas, figmaBridgeProcessData);
            }
            catch (Exception e)
            {
                ReportError("Error generating the FigCraft document. Check log for details", e.ToString());
                EditorUtility.ClearProgressBar();
                CleanUpPostGeneration();
                return;
            }


            FigmaImportTimer.Begin("Clean up and final asset refresh");
            // Lastly, for prototype mode, instantiate the default flowScreen and set the scaler up appropriately
            if (s_FigCraftBridgeSettings.BuildPrototypeFlow)
            {
                // Make sure all required default elements are present
                var screenController = figmaBridgeProcessData.PrototypeFlowController;

                // Find default flow start position
                screenController.PrototypeFlowInitialScreenId =  FigmaDataUtils.FindPrototypeFlowStartScreenId(figmaBridgeProcessData.SourceFile);;

                if (screenController.ScreenParentTransform == null)
                    screenController.ScreenParentTransform=UnityUiUtils.CreateRectTransform("ScreenParentTransform",
                        figmaBridgeProcessData.PrototypeFlowController.transform as RectTransform);

                if (screenController.TransitionEffect == null)
                {
                    // Instantiate and apply the default transition effect (loaded from package assets folder)
                    var defaultTransitionAnimationEffect = AssetDatabase.LoadAssetAtPath("Packages/com.ezg.figcraft-bridge/FigCraftBridge/Assets/TransitionFadeToBlack.prefab", typeof(GameObject)) as GameObject;
                    var transitionObject = (GameObject) PrefabUtility.InstantiatePrefab(defaultTransitionAnimationEffect,
                        screenController.transform.transform);
                    screenController.TransitionEffect =
                        transitionObject.GetComponent<TransitionEffect>();

                    UnityUiUtils.SetTransformFullStretch(transitionObject.transform as RectTransform);
                }

                // Set start flowScreen on stage by default
                var defaultScreenData = figmaBridgeProcessData.PrototypeFlowController.StartFlowScreen;
                if (defaultScreenData != null)
                {
                    var defaultScreenTransform = defaultScreenData.FigmaScreenPrefab.transform as RectTransform;
                    if (defaultScreenTransform != null)
                    {
                        var defaultSize = defaultScreenTransform.sizeDelta;
                        var canvasScaler = s_SceneCanvas.GetComponent<CanvasScaler>();
                        if (canvasScaler == null) canvasScaler = s_SceneCanvas.gameObject.AddComponent<CanvasScaler>();
                        canvasScaler.referenceResolution = defaultSize;
                        // If we are a vertical template, drive by width
                        canvasScaler.matchWidthOrHeight = (defaultSize.x>defaultSize.y) ? 1f : 0f; // Use height as driver
                        canvasScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                    }

                    var screenInstance=(GameObject)PrefabUtility.InstantiatePrefab(defaultScreenData.FigmaScreenPrefab, figmaBridgeProcessData.PrototypeFlowController.ScreenParentTransform);
                    figmaBridgeProcessData.PrototypeFlowController.SetCurrentScreen(screenInstance,defaultScreenData.FigmaNodeId,true);
                }
                // Write CS file with references to flowScreen name
                if (s_FigCraftBridgeSettings.CreateScreenNameCSharpFile) ScreenNameCodeGenerator.WriteScreenNamesCodeFile(figmaBridgeProcessData.ScreenPrefabs);
            }
            CleanUpPostGeneration();
            EditorUtility.ClearProgressBar();
            AssetDatabase.Refresh();
        }

        /// <summary>One warning listing every expected file the offline re-import could not find.</summary>
        private static void ReportMissingFiles(string what, List<string> missingPaths)
        {
            if (missingPaths == null || missingPaths.Count == 0) return;
            Debug.LogWarning($"[FigCraftBridge] Offline re-import: {missingPaths.Count} {what} not on disk - " +
                             "those nodes import without a sprite until a Sync from FigCraft writes them:\n  " +
                             string.Join("\n  ", missingPaths));
        }

        /// <summary>One warning listing every image fill whose file holds art the document no longer shows.</summary>
        private static void ReportStaleImageFills(List<string> stalePaths)
        {
            if (stalePaths == null || stalePaths.Count == 0) return;
            Debug.LogWarning($"[FigCraftBridge] Offline re-import: {stalePaths.Count} image fill(s) on disk hold other art than " +
                             "the document (file SHA-1 differs from its imageRef) - those nodes keep the old art until an " +
                             "Sync from FigCraft writes them again:\n  " + string.Join("\n  ", stalePaths));
        }

        /// <summary>
        ///  Clean up any leftover assets post-generation
        /// </summary>
        private static void CleanUpPostGeneration()
        {
            if (!s_FigCraftBridgeSettings.BuildPrototypeFlow)
            {
                // Destroy temporary canvas
                if (s_SceneCanvas != null) Object.DestroyImmediate(s_SceneCanvas.gameObject);
            }
        }
    }
}
