using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;
using UnityFigmaBridge.Editor.Settings;
using UnityFigmaBridge.Editor.Utils;

namespace UnityFigmaBridge.Editor.FigmaApi
{
    /// <summary>
    /// Lỗi HTTP từ Figma API kèm status code (0 = lỗi kết nối / timeout phía client).
    /// </summary>
    public class FigmaApiRequestException : Exception
    {
        public long StatusCode { get; }

        public FigmaApiRequestException(long statusCode, string message) : base(message)
        {
            StatusCode = statusCode;
        }

        /// <summary>5xx hoặc lỗi kết nối — lỗi tạm thời phía server, thử lại / chia nhỏ batch có thể qua.</summary>
        public bool IsTransient => StatusCode == 0 || StatusCode >= 500;
    }


    /// <summary>
    /// Reason for server rendering
    /// </summary>
    public enum ServerRenderType
    {
        Substitution, // We want to replace a complex node with an image
        Export, // We want to export this image
        PatternSource // The node a PATTERN fill repeats; rendered once, imported with wrap Repeat and tiled
    }
        
    /// <summary>
    /// Encapsulates server render node data
    /// </summary>
    public class ServerRenderNodeData
    {
        public ServerRenderType RenderType = ServerRenderType.Substitution;
        public Node SourceNode;
    }
    
    public static class FigmaApiUtils
    {
        private static string WRITE_FILE_PATH = "FigmaOutput.json";

        /// <summary>Where the last downloaded document is cached; the offline re-import reads it back.</summary>
        public static string CachedDocumentPath => Path.Combine("Assets", WRITE_FILE_PATH).Replace('\\', '/');

        private static readonly JsonSerializerSettings s_DocumentJsonSettings = new JsonSerializerSettings
        {
            // Ignore missing members and null fields that sometimes come from Figma
            DefaultValueHandling = DefaultValueHandling.Include,
            MissingMemberHandling = MissingMemberHandling.Ignore,
            NullValueHandling = NullValueHandling.Ignore,
        };

        /// <summary>Deserialize the cached document, or null when there is none or it does not parse.</summary>
        public static FigmaFile LoadCachedDocument()
        {
            var path = CachedDocumentPath;
            if (!File.Exists(path)) return null;
            try
            {
                var figmaFile = JsonConvert.DeserializeObject<FigmaFile>(File.ReadAllText(path), s_DocumentJsonSettings);
                FigmaDataUtils.PruneIgnoredNodes(figmaFile);
                return figmaFile;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[FigmaApiUtils] Cached document '{path}' did not parse: {e.Message}");
                return null;
            }
        }

        /// <summary>
        ///     One line naming the HTTP status and, for rate limiting, the headers that say how long
        ///     to wait and which quota tier applied. Figma folds every failure into a generic
        ///     "check your token" otherwise.
        /// </summary>
        private static string DescribeFailure(UnityWebRequest request)
        {
            var line = $"HTTP {request.responseCode} {request.error}";
            var retryAfter = request.GetResponseHeader("retry-after");
            if (!string.IsNullOrEmpty(retryAfter)) line += $"; retry-after {retryAfter}s";
            var rateLimitType = request.GetResponseHeader("x-figma-rate-limit-type");
            if (!string.IsNullOrEmpty(rateLimitType)) line += $"; rate-limit-type {rateLimitType}";
            var planTier = request.GetResponseHeader("x-figma-plan-tier");
            if (!string.IsNullOrEmpty(planTier)) line += $"; plan-tier {planTier}";
            return line;
        }
        
        /// <summary>
        /// Encapsulate download data
        /// </summary>
        public class FigmaDownloadQueueItem
        {
            public enum FigmaFileType
            {
                ImageFill,
                ServerRenderedImage
            }

            public FigmaFileType FileType;
            public string Url;
            public string FilePath;
            /// <summary>Figma imageRef for ImageFill items; null for server renders.</summary>
            public string ImageRef;
            /// <summary>Figma node id for ServerRenderedImage items; null for image fills.</summary>
            public string NodeId;
            /// <summary>Set by DownloadFiles once the file is written and imported with its sprite settings.</summary>
            public bool Succeeded;
        }
        
        


        /// <summary>
        /// Get Figma File Id from document Url
        /// </summary>
        /// <param name="url"Document Url</param>
        /// <returns>File Id</returns>
        public static (bool, string) GetFigmaDocumentIdFromUrl(string url)
        {
            // Legacy Format is https://www.figma.com/file/{DOC_ID}/{NAME}?node-id={NODE}
            // New format is https://www.figma.com/design/{DOC_ID}/{NAME}?node-id={NODE}
            
            var legacyInitialSection = "https://www.figma.com/file/";
            var modernInitialSection = "https://www.figma.com/design/";

            var legacyInitialSectionIndex = url.IndexOf(legacyInitialSection, StringComparison.Ordinal);
            var modernInitialSectionIndex = url.IndexOf(modernInitialSection, StringComparison.Ordinal);
            
            // If neither found, it's invalid
            if ( legacyInitialSectionIndex!= 0 && modernInitialSectionIndex!=0) return (false, "");
            // Select best fit
            var targetSectionToUse = legacyInitialSectionIndex == 0 ? legacyInitialSection : modernInitialSection;
            
            var remainder = url.Substring(targetSectionToUse.Length);
            var nextSeperatorIndex = remainder.IndexOf('/');
            if (nextSeperatorIndex == -1) return (false, "");
            return (true, remainder.Substring(0, nextSeperatorIndex));
        }

        /// <summary>
        /// Download a Figma doc from server and deserialize
        /// </summary>
        /// <param name="fileId">Figma File Id</param>
        /// <param name="accessToken">Figma Access Token</param>
        /// <param name="writeFile">Optionally write this file to disk</param>
        /// <returns>The deserialized Figma file</returns>
        /// <exception cref="Exception"></exception>
        public static async Task<FigmaFile> GetFigmaDocument(string fileId, string accessToken, bool writeFile)
        {
            var url =
                $"https://api.figma.com/v1/files/{fileId}?geometry=paths"; // We need geometry=paths to get rotation and full transform

            FigmaFile figmaFile = null;
            FigmaImportTimer.Begin("Document JSON download (Figma API)");
            // Download the Figma Document
            var webRequest = UnityWebRequest.Get(url);
            webRequest.SetRequestHeader("X-Figma-Token", accessToken);
            await webRequest.SendWebRequest();

            if (webRequest.result == UnityWebRequest.Result.ProtocolError ||
                webRequest.result == UnityWebRequest.Result.ConnectionError)
            {
                throw new Exception($"{DescribeFailure(webRequest)}\nError downloading FIGMA document, url - {url}");
            }

            FigmaImportTimer.Begin("Document JSON decode and cache write");
            try
            {
                // Deserialize the document
                figmaFile = JsonConvert.DeserializeObject<FigmaFile>(webRequest.downloadHandler.text, s_DocumentJsonSettings);
                FigmaDataUtils.PruneIgnoredNodes(figmaFile);

                Debug.Log($"Figma file downloaded, name {figmaFile.name}");
            }
            catch (Exception e)
            {
                throw new Exception($"Problem decoding Figma document JSON {e.ToString()}");
            }

            if (writeFile) File.WriteAllText(Path.Combine("Assets", WRITE_FILE_PATH), webRequest.downloadHandler.text);
            return figmaFile;
        }

        /// <summary>
        /// Requests a server-side rendering of nodes from a document, returning list of urls to download
        /// </summary>
        /// <param name="fileId">Figma File Id</param>
        /// <param name="accessToken">Figma Access Token</param>
        /// <param name="serverNodeCsvList">Csv List of nodes to render</param>
        /// <param name="serverRenderImageScale">Scale to render images at</param>
        /// <param name="useAbsoluteBounds">
        ///     True crops the image to the layout box. False keeps what draws outside it (outside
        ///     strokes, shadows), matching the node's <c>absoluteRenderBounds</c>.
        /// </param>
        /// <returns>List of urls to access the rendered images</returns>
        /// <exception cref="Exception"></exception>
        public static async Task<FigmaServerRenderData> GetFigmaServerRenderData(string fileId, string accessToken,
            IEnumerable<string> nodeIds, int serverRenderImageScale, bool useAbsoluteBounds)
        {
            FigmaServerRenderData figmaServerRenderData = null;
            // Instance sublayer ids carry ';', which must not reach the query string raw
            var serverNodeCsvList = string.Join(",", nodeIds.Select(Uri.EscapeDataString));
            // Execute server-side rendering. Sending this webRequest will return a list of all images to download
            var serverRenderUrl =
                $"https://api.figma.com/v1/images/{fileId}?ids={serverNodeCsvList}&scale={serverRenderImageScale}" +
                $"&use_absolute_bounds={(useAbsoluteBounds ? "true" : "false")}";
            var webRequest = UnityWebRequest.Get(serverRenderUrl);
            webRequest.SetRequestHeader("X-Figma-Token", accessToken);

            await webRequest.SendWebRequest();
            if (webRequest.result == UnityWebRequest.Result.ProtocolError ||
                webRequest.result == UnityWebRequest.Result.ConnectionError)
            {
                // Mang theo status code để importer phân biệt 5xx/timeout (chia nhỏ batch) với 4xx (dừng)
                throw new FigmaApiRequestException(webRequest.responseCode,
                    $"{DescribeFailure(webRequest)}\nError downloading FIGMA Server Rendered Images, url - {serverRenderUrl}");
            }

            try
            {
                figmaServerRenderData =
                    JsonConvert.DeserializeObject<FigmaServerRenderData>(webRequest.downloadHandler.text);
            }
            catch (Exception e)
            {
                throw new Exception($"Problem decoding server render JSON {e.ToString()}");
            }

            return figmaServerRenderData;
        }

        /// <summary>
        /// Downloads image fill data for a Figma document
        /// </summary>
        /// <param name="fileId">Figma File Id</param>
        /// <param name="accessToken">Figma Access Token</param>
        /// <returns>List of image fills for the document</returns>
        /// <exception cref="Exception"></exception>
        public static async Task<FigmaImageFillData> GetDocumentImageFillData(string fileId, string accessToken)
        {
            FigmaImageFillData imageFillData;
            // Download a list all the image fills container in the Figma document
            var imageFillUrl = $"https://api.figma.com/v1/files/{fileId}/images";

            var webRequest = UnityWebRequest.Get(imageFillUrl);
            webRequest.SetRequestHeader("X-Figma-Token", accessToken);

            await webRequest.SendWebRequest();

            if (webRequest.result is UnityWebRequest.Result.ProtocolError or UnityWebRequest.Result.ConnectionError)
            {
                throw new Exception($"{DescribeFailure(webRequest)}\nError downloading FIGMA Image Fill Data, url - {imageFillUrl}");
            }
            try
            {
                imageFillData = JsonConvert.DeserializeObject<FigmaImageFillData>(webRequest.downloadHandler.text);
            }
            catch (Exception e)
            {
                throw new Exception($"Problem decoding image fill JSON {e.ToString()}");
            }

            return imageFillData;
        }


        /// <summary>
        /// Retrieves specific nodes from specific files
        /// </summary>
        /// <param name="fileId">Figma File Id</param>
        /// <param name="accessToken">Figma Access Token</param>
        /// <param name="nodeIds">List of Node Ids to process</param>
        /// <returns></returns>
        /// <exception cref="Exception"></exception>
        public static async Task<FigmaFileNodes> GetFigmaFileNodes(string fileId, string accessToken,List<string> nodeIds)
        {
            FigmaFileNodes fileNodes;
            var externalComponentsJoined = string.Join(",",nodeIds);
            var componentsUrl = $"https://api.figma.com/v1/files/{fileId}/nodes/?ids={externalComponentsJoined}";
            
            // Download the FIGMA Document
            var webRequest = UnityWebRequest.Get(componentsUrl);
            webRequest.SetRequestHeader("X-Figma-Token",accessToken);
            await webRequest.SendWebRequest();

            if (webRequest.result is UnityWebRequest.Result.ProtocolError or UnityWebRequest.Result.ConnectionError)
            {
                throw new Exception($"{DescribeFailure(webRequest)}\nError downloading components, url - {componentsUrl}");
            }
            try
            {
                fileNodes = JsonConvert.DeserializeObject<FigmaFileNodes>(webRequest.downloadHandler.text);
                File.WriteAllText("ComponentNodes.json", webRequest.downloadHandler.text);
            }
            catch (Exception e)
            {
                throw new Exception($"Problem decoding Figma components JSON {e.ToString()}");
            }

            return fileNodes;
        }


        /// <summary>
        /// Generates a standardised list of files to download 
        /// </summary>
        /// <param name="imageFillData"></param>
        /// <param name="foundImageFills"></param>
        /// <param name="serverRenderData"></param>
        /// <param name="serverRenderNodes"></param>
        /// <returns></returns>
        public static List<FigmaDownloadQueueItem> GenerateDownloadQueue(FigmaImageFillData imageFillData,List<string> foundImageFills,List<FigmaServerRenderData> serverRenderData,List<ServerRenderNodeData> serverRenderNodes)
        {
            // Check if each image fill file has already been downloaded. If not, add to download list
            //Dictionary<string, string> filteredImageFillList = new Dictionary<string, string>();
            List<FigmaDownloadQueueItem> downloadList = new List<FigmaDownloadQueueItem>();
            foreach (var keyPair in imageFillData.meta.images)
            {
                // Only download if it is used in the document and not already downloaded.
                // A fill only an excluded or unselected frame reaches has no owner, so it would be
                // written as an unreferenced asset under a hash name.
                if (FigmaImageFillNamer.IsUnreachable(keyPair.Key)) continue;

                if (foundImageFills.Contains(keyPair.Key) && !File.Exists(FigmaPaths.GetPathForImageFill(keyPair.Key)))
                {
                    downloadList.Add(new FigmaDownloadQueueItem
                    {
                        Url=keyPair.Value,
                        FilePath = FigmaPaths.GetPathForImageFill(keyPair.Key),
                        FileType = FigmaDownloadQueueItem.FigmaFileType.ImageFill,
                        ImageRef = keyPair.Key
                    });
                }
            }

            // If required, process server render images
           foreach (var serverRenderDataEntry in serverRenderData)
            {
                foreach (var keyPair in serverRenderDataEntry.images)
                {
                    if (string.IsNullOrEmpty(keyPair.Value))
                    {
                        // if the url is invalid...
                        Debug.Log($"Can't download image for Server Node {keyPair.Key}");
                    }
                    else
                    {
                        // Only renders the ServerRenderCache found stale were requested, so each one overwrites
                        downloadList.Add(new FigmaDownloadQueueItem
                        {
                            Url = keyPair.Value,
                            FilePath = FigmaPaths.GetPathForServerRenderedImage(keyPair.Key, serverRenderNodes),
                            FileType = FigmaDownloadQueueItem.FigmaFileType.ServerRenderedImage,
                            NodeId = keyPair.Key
                        });
                    }
                }
            }

            return downloadList;
        }
        

        /// <summary>
        /// Download required files and process
        /// </summary>
        /// <param name="downloadItems"></param>
        /// <param name="settings">Sprite import settings come from here (mipmaps, compression).</param>
        /// <param name="tiledImageRefs">
        ///     Image fills some node draws with scale mode TILE; only those import with wrap mode
        ///     Repeat. Everything else clamps, so a stretched sprite never bleeds its opposite edge.
        /// </param>
        /// <param name="repeatNodeIds">
        ///     Server-rendered nodes that are the source of a PATTERN fill; they tile, so they import
        ///     with wrap mode Repeat like a TILE image fill. Other server renders clamp.
        /// </param>
        public static async Task DownloadFiles(List<FigmaDownloadQueueItem> downloadItems, UnityFigmaBridgeSettings settings,
            HashSet<string> tiledImageRefs = null, HashSet<string> repeatNodeIds = null)
        {
            var downloadCount = downloadItems.Count;
            var fillCount = downloadItems.Count(item => item.FileType == FigmaDownloadQueueItem.FigmaFileType.ImageFill);
            var countsDetail = $"{downloadCount} files: {fillCount} image fills, {downloadCount - fillCount} server renders";
            FigmaImportTimer.SetDetail(DOWNLOAD_PHASE, countsDetail);
            if (downloadCount == 0) return;

            FigmaImportTimer.Begin(DOWNLOAD_PHASE);
            var writtenItems = new List<FigmaDownloadQueueItem>();
            long downloadedBytes = 0;
            var nextIndex = 0;
            var finishedCount = 0;
            EditorUtility.DisplayProgressBar(DOWNLOAD_PROGRESS_TITLE, $"Downloading Server Image 0/{downloadCount}", 0);

            // The request awaiter resumes on the main thread, so the shared counters and lists need no lock
            async Task DownloadWorker()
            {
                while (nextIndex < downloadCount)
                {
                    var downloadItem = downloadItems[nextIndex++];
                    try
                    {
                        using var request = UnityWebRequest.Get(downloadItem.Url);
                        await request.SendWebRequest();
                        if (request.result != UnityWebRequest.Result.Success)
                        {
                            LogDownloadFailure(downloadItem, DescribeFailure(request));
                        }
                        else
                        {
                            var imageBytes = request.downloadHandler.data;
                            downloadedBytes += imageBytes?.Length ?? 0;
                            var directoryPath = Path.GetDirectoryName(downloadItem.FilePath);
                            if (!Directory.Exists(directoryPath)) Directory.CreateDirectory(directoryPath);
                            File.WriteAllBytes(downloadItem.FilePath, imageBytes);
                            writtenItems.Add(downloadItem);
                        }
                    }
                    catch (Exception e)
                    {
                        LogDownloadFailure(downloadItem, e.ToString());
                    }
                    finishedCount++;
                    EditorUtility.DisplayProgressBar(DOWNLOAD_PROGRESS_TITLE, $"Downloading Server Image {finishedCount}/{downloadCount}",
                        (float)finishedCount / downloadCount);
                }
            }

            await Task.WhenAll(Enumerable.Range(0, Math.Min(MAX_CONCURRENT_DOWNLOADS, downloadCount)).Select(_ => DownloadWorker()));
            var failedCount = downloadCount - writtenItems.Count;
            FigmaImportTimer.SetDetail(DOWNLOAD_PHASE, $"{countsDetail}, {(downloadedBytes / 1048576.0).ToString("0.0", CultureInfo.InvariantCulture)} MB" +
                                                       (failedCount > 0 ? $", {failedCount} failed" : string.Empty));

            FigmaImportTimer.Begin(TEXTURE_IMPORT_PHASE);
            ImportDownloadedTextures(writtenItems, settings, tiledImageRefs, repeatNodeIds);
        }

        private const string DOWNLOAD_PHASE = "Image download (network)";
        private const string TEXTURE_IMPORT_PHASE = "Image import into Unity (AssetDatabase)";
        private const string DOWNLOAD_PROGRESS_TITLE = "Importing Figma Document";
        private const int MAX_CONCURRENT_DOWNLOADS = 6;

        /// <summary>
        ///     Imports the written files as two AssetDatabase batches instead of a Refresh per file.
        ///     A new file has no TextureImporter until its first import, so the first batch imports
        ///     every file (new bytes, current settings) and the second applies the sprite settings,
        ///     reimporting only the files whose settings changed.
        /// </summary>
        private static void ImportDownloadedTextures(List<FigmaDownloadQueueItem> writtenItems, UnityFigmaBridgeSettings settings,
            HashSet<string> tiledImageRefs, HashSet<string> repeatNodeIds)
        {
            if (writtenItems.Count == 0) return;
            EditorUtility.DisplayProgressBar(DOWNLOAD_PROGRESS_TITLE, $"Importing {writtenItems.Count} images", 1);
            var newCount = writtenItems.Count(item => AssetImporter.GetAtPath(item.FilePath) == null);

            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var item in writtenItems) AssetDatabase.ImportAsset(item.FilePath);
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }
            // Safety net for a file ImportAsset did not register (e.g. inside a folder this download created)
            if (writtenItems.Any(item => AssetImporter.GetAtPath(item.FilePath) == null)) AssetDatabase.Refresh();

            var reimportCount = 0;
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var item in writtenItems)
                {
                    try
                    {
                        if (AssetImporter.GetAtPath(item.FilePath) is not TextureImporter textureImporter)
                        {
                            LogDownloadFailure(item, "no TextureImporter after import");
                            continue;
                        }
                        if (ApplySpriteImportSettings(textureImporter, settings, GetWrapMode(item, tiledImageRefs, repeatNodeIds)))
                        {
                            textureImporter.SaveAndReimport();
                            reimportCount++;
                        }
                        item.Succeeded = true;
                    }
                    catch (Exception e)
                    {
                        LogDownloadFailure(item, e.ToString());
                    }
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }
            FigmaImportTimer.SetDetail(TEXTURE_IMPORT_PHASE,
                $"{writtenItems.Count} files ({newCount} new), {reimportCount} reimported for sprite settings");
        }

        /// <returns>True when the importer changed and needs a reimport.</returns>
        private static bool ApplySpriteImportSettings(TextureImporter importer, UnityFigmaBridgeSettings settings, TextureWrapMode wrapMode)
        {
            var mipmaps = settings != null && settings.SpriteMipmaps;
            var compression = settings != null && settings.SpriteCompression == SpriteCompressionMode.Compressed
                ? TextureImporterCompression.Compressed
                : TextureImporterCompression.Uncompressed;
            var changed = importer.textureType != TextureImporterType.Sprite ||
                          importer.spriteImportMode != SpriteImportMode.Single ||
                          !importer.alphaIsTransparency ||
                          importer.mipmapEnabled != mipmaps ||
                          importer.textureCompression != compression ||
                          !importer.sRGBTexture ||
                          importer.wrapModeU != wrapMode ||
                          importer.wrapModeV != wrapMode;
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = mipmaps;
            importer.textureCompression = compression;
            importer.sRGBTexture = true;
            importer.wrapMode = wrapMode;
            return SpritePlatformOverride.Apply(importer, settings) || changed;
        }

        private static TextureWrapMode GetWrapMode(FigmaDownloadQueueItem item, HashSet<string> tiledImageRefs, HashSet<string> repeatNodeIds)
        {
            switch (item.FileType)
            {
                case FigmaDownloadQueueItem.FigmaFileType.ImageFill:
                    // Only a fill drawn in "tile" mode needs a repeating texture
                    var isTiled = tiledImageRefs != null && item.ImageRef != null && tiledImageRefs.Contains(item.ImageRef);
                    return isTiled ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
                case FigmaDownloadQueueItem.FigmaFileType.ServerRenderedImage:
                    // Server renders clamp, except the source of a PATTERN fill which is tiled
                    var isPatternSource = repeatNodeIds != null && item.NodeId != null && repeatNodeIds.Contains(item.NodeId);
                    return isPatternSource ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
                default:
                    return TextureWrapMode.Clamp;
            }
        }

        private static void LogDownloadFailure(FigmaDownloadQueueItem item, string reason) =>
            Debug.LogWarning($"Error downloading image file '{item.Url}' of type {item.FileType} for path {item.FilePath}: {reason}");

    
        /// <summary>
        /// Checks that existing assets are in the correct format
        /// </summary>
        public static void CheckExistingAssetProperties()
        {
            CheckImageFillTextureProperties();
        }

        /// <summary>
        /// Checks downloaded image fills
        /// </summary>
        private static void CheckImageFillTextureProperties()
        {
            foreach (var filePath in Directory.GetFiles(FigmaPaths.FigmaImageFillFolder))
            {
                var textureImporter = AssetImporter.GetAtPath(filePath) as TextureImporter;
                if (textureImporter == null) continue;
                // Previous versions may not have sRGB set
                if (textureImporter.sRGBTexture) continue;
                textureImporter.sRGBTexture = true;
                textureImporter.SaveAndReimport();
            }
        }
    }
}