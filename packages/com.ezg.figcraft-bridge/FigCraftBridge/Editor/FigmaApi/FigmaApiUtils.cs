using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using FigCraftBridge.Editor.Settings;
using FigCraftBridge.Editor.Utils;

namespace FigCraftBridge.Editor.FigmaApi
{
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
        private static string WRITE_FILE_PATH = "FigCraftOutput.json";

        /// <summary>Where the last downloaded document is cached; the offline re-import reads it back.</summary>
        public static string CachedDocumentPath => Path.Combine("Library/FigCraftBridge", WRITE_FILE_PATH).Replace('\\', '/');


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
                return ReadDocument(path);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[FigmaApiUtils] Cached document '{path}' did not parse: {e.Message}");
                return null;
            }
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
        
        


        /// <summary>Decodes a document file with the shared JSON settings and prunes ignored nodes and nodes with no geometry; used by the FigCraft source and the offline re-import.</summary>
        internal static FigmaFile ReadDocument(string path)
        {
            using var reader = new JsonTextReader(File.OpenText(path));
            var figmaFile = JsonSerializer.Create(s_DocumentJsonSettings).Deserialize<FigmaFile>(reader);
            FigmaDataUtils.PruneIgnoredNodes(figmaFile);
            FigmaDataUtils.PruneNodesWithoutGeometry(figmaFile);
            return figmaFile;
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

                if (foundImageFills.Contains(keyPair.Key) && !ImageFillIsCurrent(keyPair.Key))
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
        ///     A fill named after its node keeps that name when the node gets new art, and a name can
        ///     move to another fill, so an existing file may hold art the document no longer shows.
        ///     An imageRef is the hash of the image bytes: the file is current only when they match.
        ///     A file named by its imageRef is current whenever it exists.
        /// </summary>
        internal static bool ImageFillIsCurrent(string imageRef)
        {
            var path = FigmaPaths.GetPathForImageFill(imageRef);
            if (!File.Exists(path)) return false;
            if (!FigmaImageFillNamer.TryGetRelativeName(imageRef, out _)) return true;

            // FigCraft names an image by the SHA-256 of its bytes (Figma used SHA-1).
            using HashAlgorithm algorithm = imageRef.Length == 64 ? SHA256.Create() : SHA1.Create();
            using var stream = File.OpenRead(path);
            var hash = BitConverter.ToString(algorithm.ComputeHash(stream)).Replace("-", "");
            return string.Equals(hash, imageRef, StringComparison.OrdinalIgnoreCase);
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
        public static void DownloadFiles(List<FigmaDownloadQueueItem> downloadItems, FigCraftBridgeSettings settings,
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
            for (var index = 0; index < downloadCount; index++)
            {
                var downloadItem = downloadItems[index];
                try
                {
                    var sourcePath = new Uri(downloadItem.Url).LocalPath;
                    var directoryPath = Path.GetDirectoryName(downloadItem.FilePath);
                    if (!Directory.Exists(directoryPath)) Directory.CreateDirectory(directoryPath);
                    File.Copy(sourcePath, downloadItem.FilePath, true);
                    downloadedBytes += new FileInfo(downloadItem.FilePath).Length;
                    writtenItems.Add(downloadItem);
                }
                catch (Exception e)
                {
                    LogDownloadFailure(downloadItem, e.ToString());
                }
                if (index % 50 == 0)
                    EditorUtility.DisplayProgressBar(DOWNLOAD_PROGRESS_TITLE, $"Copying images {index}/{downloadCount}", (float)index / downloadCount);
            }

            var failedCount = downloadCount - writtenItems.Count;
            FigmaImportTimer.SetDetail(DOWNLOAD_PHASE, $"{countsDetail}, {(downloadedBytes / 1048576.0).ToString("0.0", CultureInfo.InvariantCulture)} MB" +
                                                       (failedCount > 0 ? $", {failedCount} failed" : string.Empty));

            FigmaImportTimer.Begin(TEXTURE_IMPORT_PHASE);
            ImportDownloadedTextures(writtenItems, settings, tiledImageRefs, repeatNodeIds);
        }

        private const string DOWNLOAD_PHASE = "Copy renders and image fills";
        private const string TEXTURE_IMPORT_PHASE = "Image import into Unity (AssetDatabase)";
        private const string DOWNLOAD_PROGRESS_TITLE = "Importing FigCraft Document";

        /// <summary>
        ///     Imports the written files as two AssetDatabase batches instead of a Refresh per file.
        ///     A new file has no TextureImporter until its first import, so the first batch imports
        ///     every file (new bytes, current settings) and the second applies the sprite settings,
        ///     reimporting only the files whose settings changed.
        /// </summary>
        private static void ImportDownloadedTextures(List<FigmaDownloadQueueItem> writtenItems, FigCraftBridgeSettings settings,
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
                        if (ApplySpriteImportSettings(textureImporter, settings, GetWrapMode(item, tiledImageRefs, repeatNodeIds),
                                item.FileType == FigmaDownloadQueueItem.FigmaFileType.ImageFill))
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

        /// <param name="clearBorder">
        ///     For an image fill: a border left on the importer was measured on the art this download
        ///     replaced. The 9-slice pass sets it again when a slice grid uses the fill.
        /// </param>
        /// <returns>True when the importer changed and needs a reimport.</returns>
        private static bool ApplySpriteImportSettings(TextureImporter importer, FigCraftBridgeSettings settings, TextureWrapMode wrapMode,
            bool clearBorder)
        {
            var borderChanged = clearBorder && importer.spriteBorder != Vector4.zero;
            if (borderChanged) importer.spriteBorder = Vector4.zero;
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
            return SpritePlatformOverride.Apply(importer, settings) || changed || borderChanged;
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