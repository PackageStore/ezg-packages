using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityFigmaBridge.Editor.Components;
using UnityFigmaBridge.Editor.FigmaApi;
using UnityFigmaBridge.Editor.Fonts;
using UnityFigmaBridge.Editor.Nodes;
using UnityFigmaBridge.Editor.PostProcess;
using UnityFigmaBridge.Editor.PrototypeFlow;
using UnityFigmaBridge.Editor.Settings;
using UnityFigmaBridge.Editor.Utils;
using UnityFigmaBridge.Runtime.UI;
using Object = UnityEngine.Object;

namespace UnityFigmaBridge.Editor
{
    /// <summary>
    ///  Manages Figma importing and document creation
    /// </summary>
    public static class UnityFigmaBridgeImporter
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
                Debug.LogError($"[FigmaBridge] {title}: {message}");
                return false;
            }
            return cancel == null
                ? EditorUtility.DisplayDialog(title, message, ok)
                : EditorUtility.DisplayDialog(title, message, ok, cancel);
        }

        /// <summary>
        /// The settings asset, containing preferences for importing
        /// </summary>
        private static UnityFigmaBridgeSettings s_UnityFigmaBridgeSettings;

        public const string PROGRESS_BOX_TITLE = "Importing Figma Document";

        /// <summary>
        /// Figma imposes a limit on the number of images in a single batch. This is batch size
        /// (This is a bit of a guess - 650 is rejected)
        /// </summary>
        private const int MAX_SERVER_RENDER_IMAGE_BATCH_SIZE = 300;

        private static string s_PersonalAccessToken;

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
            if (!CheckRequirements(requireToken: false)) return;

            var cachedDocument = FigmaApiUtils.LoadCachedDocument();
            if (cachedDocument == null)
                Debug.LogWarning("[FigmaBridge][PostProcess] No cached document at " +
                                 $"'{FigmaApiUtils.CachedDocumentPath}' - frames are name-only stubs");

            FigmaPaths.Configure(s_UnityFigmaBridgeSettings, cachedDocument?.name);
            var context = PostProcessorRunner.BuildContextFromDisk(s_UnityFigmaBridgeSettings, cachedDocument);
            Debug.Log($"[FigmaBridge][PostProcess] Running post-processors on {context.Screens.Count} screen prefab(s) from disk");
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

        private static async void SyncAsync(bool offline)
        {
            var requirementsMet = CheckRequirements(requireToken: !offline);
            if (!requirementsMet) return;

            FigmaFile figmaFile;
            if (offline)
            {
                figmaFile = FigmaApiUtils.LoadCachedDocument();
                if (figmaFile == null)
                {
                    ReportError($"No cached Figma document at '{FigmaApiUtils.CachedDocumentPath}'. " +
                                "Run Sync Document online once, then the offline re-import can rebuild from it.", "");
                    return;
                }
                Debug.Log($"[FigmaBridge] Offline re-import from cache: '{figmaFile.name}' " +
                          $"(lastModified {figmaFile.lastModified}, version {figmaFile.version})");
            }
            else
            {
                figmaFile = await DownloadFigmaDocument(s_UnityFigmaBridgeSettings.FileId);
                if (figmaFile == null) return;
            }

            var pageNodeList = FigmaDataUtils.GetPageNodes(figmaFile);

            if (s_UnityFigmaBridgeSettings.OnlyImportSelectedPages)
            {
                var downloadPageNodeIdList = pageNodeList.Select(p => p.id).ToList();
                downloadPageNodeIdList.Sort();

                var settingsPageDataIdList = s_UnityFigmaBridgeSettings.PageDataList.Select(p => p.NodeId).ToList();
                settingsPageDataIdList.Sort();

                if (!settingsPageDataIdList.SequenceEqual(downloadPageNodeIdList))
                {
                    ReportError("The pages found in the Figma document have changed - check your settings file and Sync again when ready", "");

                    // Apply the new page list to serialized data and select to allow the user to change
                    s_UnityFigmaBridgeSettings.RefreshForUpdatedPages(figmaFile);
                    Selection.activeObject = s_UnityFigmaBridgeSettings;
                    EditorUtility.SetDirty(s_UnityFigmaBridgeSettings);
                    AssetDatabase.SaveAssetIfDirty(s_UnityFigmaBridgeSettings);
                    AssetDatabase.Refresh();

                    return;
                }

                var enabledPageIdList = s_UnityFigmaBridgeSettings.PageDataList.Where(p => p.Selected).Select(p => p.NodeId).ToList();

                if (enabledPageIdList.Count <= 0)
                {
                    ReportError("'Import Selected Pages' is selected, but no pages are selected for import", "");
                    SelectSettings();
                    return;
                }

                pageNodeList = pageNodeList.Where(p => enabledPageIdList.Contains(p.id)).ToList();
            }

            await ImportDocument(s_UnityFigmaBridgeSettings.FileId, figmaFile, pageNodeList, offline);

        }

        /// <summary>
        /// Check to make sure all requirements are met before syncing
        /// </summary>
        /// <param name="requireToken">False for offline work that never calls the Figma API.</param>
        public static bool CheckRequirements(bool requireToken = true) {

            // Find the settings asset if it exists
            if (s_UnityFigmaBridgeSettings == null)
                s_UnityFigmaBridgeSettings = UnityFigmaBridgeSettingsProvider.FindUnityBridgeSettingsAsset();

            if (s_UnityFigmaBridgeSettings == null)
            {
                if (
                    Dialog("No Unity Figma Bridge Settings File",
                        "Create a new Unity Figma bridge settings file? ", "Create", "Cancel"))
                {
                    s_UnityFigmaBridgeSettings =
                        UnityFigmaBridgeSettingsProvider.GenerateUnityFigmaBridgeSettingsAsset();
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

            if (s_UnityFigmaBridgeSettings.FileId.Length == 0)
            {
                Dialog("Missing Figma Document" ,"Figma Document Url is not valid, please enter valid URL","OK");
                return false;
            }

            if (requireToken)
            {
                s_PersonalAccessToken = FigmaAccessToken.Read();

                if (string.IsNullOrEmpty(s_PersonalAccessToken))
                {
                    if (DialogsSuppressed)
                    {
                        Debug.LogError("[FigmaBridge] No Figma personal access token stored on this machine.");
                        return false;
                    }
                    if (!FigmaAccessToken.TryPrompt()) return false;
                    s_PersonalAccessToken = FigmaAccessToken.Read();
                    if (string.IsNullOrEmpty(s_PersonalAccessToken)) return false;
                }
            }

            if (Application.isPlaying)
            {
                Dialog("Figma Unity Bridge Importer","Please exit play mode before importing", "OK");
                return false;
            }

            // Check all requirements for run time if required
            if (s_UnityFigmaBridgeSettings.BuildPrototypeFlow)
            {
                if (!CheckRunTimeRequirements())
                    return false;
            }

            return true;

        }


        private static bool CheckRunTimeRequirements()
        {
            if (string.IsNullOrEmpty(s_UnityFigmaBridgeSettings.RunTimeAssetsScenePath))
            {
                if (
                    Dialog("No Figma Bridge Scene set",
                        "Use current scene for generating prototype flow? ", "OK", "Cancel"))
                {
                    var currentScene = SceneManager.GetActiveScene();
                    s_UnityFigmaBridgeSettings.RunTimeAssetsScenePath = currentScene.path;
                    EditorUtility.SetDirty(s_UnityFigmaBridgeSettings);
                    AssetDatabase.SaveAssetIfDirty(s_UnityFigmaBridgeSettings);
                }
                else
                {
                    return false;
                }
            }

            // If current scene doesnt match, switch
            if (SceneManager.GetActiveScene().path != s_UnityFigmaBridgeSettings.RunTimeAssetsScenePath)
            {
                if (Dialog("Figma Bridge Scene",
                        "Current Scene doesnt match Runtime asset scene - switch scenes?", "OK", "Cancel"))
                {
                    EditorSceneManager.OpenScene(s_UnityFigmaBridgeSettings.RunTimeAssetsScenePath);
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
            var bridgeSettings=UnityFigmaBridgeSettingsProvider.FindUnityBridgeSettingsAsset();
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
            Dialog("Unity Figma Bridge Error",message,"Ok");
            Debug.LogWarning($"{message}\n {error}\n");
        }

        /// <summary>
        ///     The dialog used to fold every HTTP failure into "check your token and url", which sent
        ///     people chasing their token when the real answer was a 429. The status line of the
        ///     exception (HTTP code, retry-after, rate-limit tier) is shown first now.
        /// </summary>
        private static void ReportApiError(string context, Exception e)
        {
            var firstLine = (e.Message ?? string.Empty).Split('\n')[0].Trim();
            var hint = firstLine.Contains("HTTP 429")
                ? "\n\nFigma rate limit (429): the seat's API quota is spent for now. Wait for the " +
                  "retry-after shown above, use a token from a Dev/Full seat, or use " +
                  "'Re-import from cache (offline)' to rebuild from the last downloaded document."
                : firstLine.Contains("HTTP 403") || firstLine.Contains("HTTP 401")
                    ? "\n\nCheck that the personal access token is valid and has file read scope for this document."
                    : firstLine.Contains("HTTP 404")
                        ? "\n\nCheck the document url - the file id was not found."
                        : string.Empty;
            ReportError($"{context}\n{firstLine}{hint}", e.ToString());
        }

        public static async Task<FigmaFile> DownloadFigmaDocument(string fileId)
        {
            // Download figma document
            EditorUtility.DisplayProgressBar(PROGRESS_BOX_TITLE, $"Downloading file", 0);
            try
            {
                var figmaTask = FigmaApiUtils.GetFigmaDocument(fileId, s_PersonalAccessToken, true);
                await figmaTask;
                return figmaTask.Result;
            }
            catch (Exception e)
            {
                ReportApiError("Error downloading Figma document", e);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
            return null;
        }

        private static async Task ImportDocument(string fileId, FigmaFile figmaFile, List<Node> downloadPageNodeList, bool offline)
        {

            // Build a list of page IDs to download
            var downloadPageIdList = downloadPageNodeList.Select(p => p.id).ToList();

            FigmaPaths.Configure(s_UnityFigmaBridgeSettings, figmaFile.name);
            ComponentManager.ResetProcessedSets();

            if (s_UnityFigmaBridgeSettings.NameImageFillsByNodePath)
                FigmaImageFillNamer.Build(figmaFile, downloadPageNodeList);
            else
                FigmaImageFillNamer.Clear();

            // Ensure we have all required directories, and remove existing files
            // TODO - Once we move to processing only differences, we won't remove existing files
            FigmaPaths.CreateRequiredDirectories();

            // Next build a list of all externally referenced components not included in the document (eg
            // from external libraries) and download
            var externalComponentList = FigmaDataUtils.FindMissingComponentDefinitions(figmaFile);

            // TODO - Implement external components
            // This is currently not working as only returns a depth of 1 of returned nodes. Need to get original files too
            /*
            FigmaFileNodes activeExternalComponentsData=null;
            if (externalComponentList.Count > 0)
            {
                EditorUtility.DisplayProgressBar(PROGRESS_BOX_TITLE, $"Getting external component data", 0);
                try
                {
                    var figmaTask = FigmaApiUtils.GetFigmaFileNodes(fileId, s_PersonalAccessToken,externalComponentList);
                    await figmaTask;
                    activeExternalComponentsData = figmaTask.Result;
                }
                catch (Exception e)
                {
                    EditorUtility.ClearProgressBar();
                    ReportError("Error downloading external component Data",e.ToString());
                    return;
                }
            }
            */

            // For any missing component definitions, we are going to find the first instance and switch it to be
            // The source component. This has to be done early to ensure download of server images
            //FigmaFileUtils.ReplaceMissingComponents(figmaFile,externalComponentList);

            // Some of the nodes, we'll want to identify to use Figma server side rendering (eg vector shapes, SVGs)
            // First up create a list of nodes we'll substitute with rendered images
            var serverRenderNodes = FigmaDataUtils.FindAllServerRenderNodesInFile(figmaFile,externalComponentList,downloadPageIdList);

            // Request a render of these nodes on the server if required
            var serverRenderData=new List<FigmaServerRenderData>();
            if (serverRenderNodes.Count > 0 && !offline)
            {
                var allNodeIds = serverRenderNodes.Select(serverRenderNode => serverRenderNode.SourceNode.id).ToList();
                // As the API has an upper limit of images that can be rendered in a single request, we'll need to batch
                var batchCount = Mathf.CeilToInt((float)allNodeIds.Count / MAX_SERVER_RENDER_IMAGE_BATCH_SIZE);
                for (var i = 0; i < batchCount; i++)
                {
                    var startIndex = i * MAX_SERVER_RENDER_IMAGE_BATCH_SIZE;
                    var nodeBatch = allNodeIds.GetRange(startIndex,
                        Mathf.Min(MAX_SERVER_RENDER_IMAGE_BATCH_SIZE, allNodeIds.Count - startIndex));
                    var serverNodeCsvList = string.Join(",", nodeBatch);
                    EditorUtility.DisplayProgressBar(PROGRESS_BOX_TITLE, $"Downloading server-rendered image data {i+1}/{batchCount}",(float)i/(float)batchCount);
                    try
                    {
                        var figmaTask = FigmaApiUtils.GetFigmaServerRenderData(fileId, s_PersonalAccessToken,
                            serverNodeCsvList, s_UnityFigmaBridgeSettings.ServerRenderImageScale);
                        await figmaTask;
                        serverRenderData.Add(figmaTask.Result);
                    }
                    catch (Exception e)
                    {
                        EditorUtility.ClearProgressBar();
                        ReportApiError("Error downloading Figma Server Render Image Data", e);
                        return;
                    }
                }
            }
            else if (serverRenderNodes.Count > 0)
            {
                ReportMissingFiles("server-rendered image(s)", serverRenderNodes
                    .Select(n => FigmaPaths.GetPathForServerRenderedImage(n.SourceNode.id, serverRenderNodes))
                    .Where(p => !File.Exists(p)).ToList());
            }

            // Make sure that existing downloaded assets are in the correct format
            FigmaApiUtils.CheckExistingAssetProperties();

            // Track fills that are actually used. This is needed as FIGMA has a way of listing any bitmap used rather than active
            var foundImageFills = FigmaDataUtils.GetAllImageFillIdsFromFile(figmaFile,downloadPageIdList);
            var tiledImageFills = FigmaDataUtils.GetTiledImageFillIds(figmaFile);
            // Source nodes of PATTERN fills tile too, so their server render imports with wrap Repeat
            var patternSourceNodeIds = FigmaDataUtils.GetPatternSourceNodeIds(figmaFile, downloadPageIdList);

            if (!offline)
            {
                // Get image fill data for the document (list of urls to download any bitmap data used)
                FigmaImageFillData activeFigmaImageFillData;
                EditorUtility.DisplayProgressBar(PROGRESS_BOX_TITLE, $"Downloading image fill data", 0);
                try
                {
                    var figmaTask = FigmaApiUtils.GetDocumentImageFillData(fileId, s_PersonalAccessToken);
                    await figmaTask;
                    activeFigmaImageFillData = figmaTask.Result;
                }
                catch (Exception e)
                {
                    EditorUtility.ClearProgressBar();
                    ReportApiError("Error downloading Figma Image Fill Data", e);
                    return;
                }

                // Generate a list of all items that need to be downloaded
                var downloadList =
                    FigmaApiUtils.GenerateDownloadQueue(activeFigmaImageFillData,foundImageFills, serverRenderData, serverRenderNodes);

                // Download all required files
                await FigmaApiUtils.DownloadFiles(downloadList, s_UnityFigmaBridgeSettings, tiledImageFills, patternSourceNodeIds);
            }
            else
            {
                ReportMissingFiles("image fill(s)", foundImageFills
                    .Where(imageRef => !FigmaImageFillNamer.IsUnreachable(imageRef))
                    .Select(FigmaPaths.GetPathForImageFill)
                    .Where(p => !File.Exists(p)).ToList());
            }

            // Generate font mapping data
            var figmaFontMapTask = FontManager.GenerateFontMapForDocument(figmaFile,
                s_UnityFigmaBridgeSettings.EnableGoogleFontsDownloads && !offline,
                s_UnityFigmaBridgeSettings.FontOverride);
            await figmaFontMapTask;
            var fontMap = figmaFontMapTask.Result;


            var componentData = new FigmaBridgeComponentData
            {
                MissingComponentDefinitionsList = externalComponentList,
            };

            // Stores necessary importer data needed for document generator.
            var figmaBridgeProcessData = new FigmaImportProcessData
            {
                Settings=s_UnityFigmaBridgeSettings,
                SourceFile = figmaFile,
                ComponentData = componentData,
                ServerRenderNodes = serverRenderNodes,
                PrototypeFlowController = s_PrototypeFlowController,
                FontMap = fontMap,
                PrototypeFlowStartPoints = FigmaDataUtils.GetAllPrototypeFlowStartingPoints(figmaFile),
                SelectedPagesForImport = downloadPageNodeList,
                NodeLookupDictionary = FigmaDataUtils.BuildNodeLookupDictionary(figmaFile),
                Offline = offline
            };


            // Clear the existing screens on the flowScreen controller
            if (s_UnityFigmaBridgeSettings.BuildPrototypeFlow)
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
                ReportError("Error generating Figma document. Check log for details", e.ToString());
                EditorUtility.ClearProgressBar();
                CleanUpPostGeneration();
                return;
            }


            // Lastly, for prototype mode, instantiate the default flowScreen and set the scaler up appropriately
            if (s_UnityFigmaBridgeSettings.BuildPrototypeFlow)
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
                    var defaultTransitionAnimationEffect = AssetDatabase.LoadAssetAtPath("Packages/com.ezg.figma-bridge/UnityFigmaBridge/Assets/TransitionFadeToBlack.prefab", typeof(GameObject)) as GameObject;
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
                if (s_UnityFigmaBridgeSettings.CreateScreenNameCSharpFile) ScreenNameCodeGenerator.WriteScreenNamesCodeFile(figmaBridgeProcessData.ScreenPrefabs);
            }
            CleanUpPostGeneration();
            EditorUtility.ClearProgressBar();
            AssetDatabase.Refresh();
        }

        /// <summary>One warning listing every expected file the offline re-import could not find.</summary>
        private static void ReportMissingFiles(string what, List<string> missingPaths)
        {
            if (missingPaths == null || missingPaths.Count == 0) return;
            Debug.LogWarning($"[FigmaBridge] Offline re-import: {missingPaths.Count} {what} not on disk - " +
                             "those nodes import without a sprite until an online Sync downloads them:\n  " +
                             string.Join("\n  ", missingPaths));
        }

        /// <summary>
        ///  Clean up any leftover assets post-generation
        /// </summary>
        private static void CleanUpPostGeneration()
        {
            if (!s_UnityFigmaBridgeSettings.BuildPrototypeFlow)
            {
                // Destroy temporary canvas
                if (s_SceneCanvas != null) Object.DestroyImmediate(s_SceneCanvas.gameObject);
            }
        }
    }
}
