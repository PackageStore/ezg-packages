using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FigCraftBridge.Editor.FigmaApi;
using FigCraftBridge.Editor.Settings;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace FigCraftBridge.Editor.Source
{
    /// <summary>
    ///     Reads a FigCraft document through <c>figcraft-cli mcp</c>: the whole file in one <c>inspect_document</c>
    ///     call (converted to the importer's REST shape), each render through FigCraft's own <c>export</c>, and
    ///     image fills straight from the <c>.figcraft</c> archive (<c>images/&lt;sha256&gt;</c>).
    /// </summary>
    public sealed class FigCraftSource : IFigmaSource
    {
        static readonly TimeSpan DocumentTimeout = TimeSpan.FromMinutes(5);
        static readonly TimeSpan ExportTimeout = TimeSpan.FromSeconds(60);
        const int MaxDocumentBytes = 256 * 1024 * 1024;

        readonly FigCraftSourceKind _kind;
        readonly string _cliPath;
        readonly string _filePath;
        readonly int _appPort;
        FigCraftCli _cli;
        JToken _document;
        string _archivePath;
        Dictionary<string, long> _layerIds = new Dictionary<string, long>();
        Dictionary<string, Node> _nodes = new Dictionary<string, Node>();
        Dictionary<string, JObject> _restNodes = new Dictionary<string, JObject>();
        Dictionary<string, float[]> _outsets = new Dictionary<string, float[]>();
        JObject _restFile;

        public FigCraftSource(FigCraftBridgeSettings settings)
        {
            _kind = settings.Source;
            _cliPath = FigCraftCli.Locate(settings.FigCraftCliPath);
            _filePath = settings.FigCraftFileFullPath;
            _appPort = settings.AppPort;
        }

        public FigCraftSourceKind Kind => _kind;

        /// <summary>Document name FigCraft reported on the last read.</summary>
        public string DocumentName { get; private set; }

        public Task<FigmaFile> GetDocument(string fileId) => Task.Run(() =>
        {
            SourceTempFiles.Clear();
            var started = DateTime.UtcNow;
            var cli = Cli();
            var doc = (JObject)cli.Call("inspect_document", WithDocument(new JObject
            {
                ["allPages"] = true,
                ["depth"] = 1000,
                ["maxBytes"] = MaxDocumentBytes,
            }), DocumentTimeout);
            if (FindTruncated(doc["pages"]) is string cut)
                throw new FigCraftCliException($"FigCraft cut the layer tree at '{cut}': the document is larger than {MaxDocumentBytes / 1048576} MB of JSON.");

            _archivePath = _kind == FigCraftSourceKind.File ? _filePath : doc.Value<string>("path");
            DocumentName = doc.Value<string>("name") ?? Path.GetFileNameWithoutExtension(_archivePath ?? "Document");
            if (_kind == FigCraftSourceKind.App && doc.Value<bool?>("dirty") == true)
                Debug.LogWarning("[FigCraftBridge] The file in FigCraft has unsaved changes: layers come from the app, " +
                                 "image fills from the saved file. Save in FigCraft when an image changed.");

            var converted = FigCraftToRest.Convert(doc, DocumentName);
            _layerIds = converted.LayerIds;
            _restFile = converted.File;
            _restNodes = new Dictionary<string, JObject>();
            IndexRest(_restFile["document"] as JObject);
            _outsets = RenderBoundsFit.Load();
            foreach (var pair in _outsets)
                if (_restNodes.TryGetValue(pair.Key, out var restNode)) RenderBoundsFit.Apply(restNode, pair.Value);
            WriteCachedDocument();

            var figmaFile = FigmaApiUtils.ReadDocument(FigmaApiUtils.CachedDocumentPath);
            _nodes = new Dictionary<string, Node>();
            Index(figmaFile.document);
            Debug.Log($"[FigCraftBridge] Read '{DocumentName}' from {(_kind == FigCraftSourceKind.App ? $"the app (port {_appPort})" : _filePath)}: " +
                      $"{_layerIds.Count} layers in {(DateTime.UtcNow - started).TotalSeconds:0.0} s");
            return figmaFile;
        });

        public async Task<FigmaServerRenderData> GetServerRenderData(string fileId, IReadOnlyList<string> nodeIds, int scale,
            bool useAbsoluteBounds)
        {
            var images = new Dictionary<string, string>();
            var misfits = new List<(string nodeId, string fullPath, string boxPath)>();
            await Task.Run(() =>
            {
                foreach (var nodeId in nodeIds)
                {
                    if (!_layerIds.TryGetValue(nodeId, out var layerId))
                    {
                        Debug.LogWarning($"[FigCraftBridge] Render of {nodeId} skipped: no FigCraft layer has that id.");
                        images[nodeId] = null;
                        continue;
                    }
                    var path = SourceTempFiles.PathFor("renders", SourceTempFiles.SafeName(nodeId) + ".png");
                    try
                    {
                        ExportPng(layerId, path, scale, useAbsoluteBounds);
                        images[nodeId] = SourceTempFiles.Url(path);
                        if (useAbsoluteBounds || !SizeDiffers(nodeId, path, scale)) continue;
                        var boxPath = SourceTempFiles.PathFor("render-boxes", SourceTempFiles.SafeName(nodeId) + ".png");
                        ExportPng(layerId, boxPath, scale, true);
                        misfits.Add((nodeId, path, boxPath));
                    }
                    catch (Exception e)
                    {
                        images[nodeId] = null;
                        Debug.LogWarning($"[FigCraftBridge] Render of {nodeId} (layer {layerId}) failed: {e.Message}");
                    }
                }
            });

            // Texture decoding needs the main thread, where this continuation runs.
            var unplaced = new List<string>();
            foreach (var (nodeId, fullPath, boxPath) in misfits)
            {
                if (RenderBoundsFit.Fit(fullPath, boxPath, scale, out var outsets))
                {
                    _outsets[nodeId] = outsets;
                    if (_restNodes.TryGetValue(nodeId, out var restNode)) RenderBoundsFit.Apply(restNode, outsets);
                    if (_nodes.TryGetValue(nodeId, out var node) && node.absoluteBoundingBox != null)
                        node.absoluteRenderBounds = new Rectangle
                        {
                            x = node.absoluteBoundingBox.x - outsets[0],
                            y = node.absoluteBoundingBox.y - outsets[1],
                            width = node.absoluteBoundingBox.width + outsets[0] + outsets[2],
                            height = node.absoluteBoundingBox.height + outsets[1] + outsets[3],
                        };
                }
                else unplaced.Add(nodeId);
            }
            if (misfits.Count > 0)
            {
                RenderBoundsFit.Save(_outsets);
                WriteCachedDocument();
            }
            if (unplaced.Count > 0)
                Debug.LogWarning($"[FigCraftBridge] {unplaced.Count} render(s) could not be placed by their pixels and use the " +
                                 "render box computed from strokes and effects: " + string.Join(", ", unplaced));
            return new FigmaServerRenderData { err = null, images = images };
        }

        public Task<FigmaImageFillData> GetImageFillData(string fileId, IReadOnlyCollection<string> usedImageRefs) => Task.Run(() =>
        {
            var images = new Dictionary<string, string>();
            if (string.IsNullOrEmpty(_archivePath) || !File.Exists(_archivePath))
            {
                if (usedImageRefs.Count > 0)
                    Debug.LogWarning($"[FigCraftBridge] {usedImageRefs.Count} image fill(s) skipped: the document has no saved " +
                                     ".figcraft file to read them from. Save it in FigCraft, then Sync again.");
                return new FigmaImageFillData { error = false, status = 200, meta = new FigmaImageFillMetaData { images = images } };
            }
            var missing = new List<string>();
            using (var archive = ZipFile.OpenRead(_archivePath))
            {
                foreach (var imageRef in usedImageRefs.Distinct())
                {
                    var entry = archive.GetEntry("images/" + imageRef);
                    if (entry == null)
                    {
                        missing.Add(imageRef);
                        continue;
                    }
                    var path = SourceTempFiles.PathFor("fills", imageRef);
                    entry.ExtractToFile(path, true);
                    images[imageRef] = SourceTempFiles.Url(path);
                }
            }
            if (missing.Count > 0)
                Debug.LogWarning($"[FigCraftBridge] {missing.Count} image fill(s) are not in {_archivePath} (added after the last save?):\n  " +
                                 string.Join("\n  ", missing));
            return new FigmaImageFillData { error = false, status = 200, meta = new FigmaImageFillMetaData { images = images } };
        });

        /// <summary>FigCraft's own render of one node, as the visual check's reference.</summary>
        public void RenderReference(string nodeId, string path, int scale)
        {
            if (!_layerIds.TryGetValue(nodeId, out var layerId))
                throw new FigCraftCliException($"No FigCraft layer has the id {nodeId}.");
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
            ExportPng(layerId, Path.GetFullPath(path), scale, true);
        }

        /// <summary>FigCraft's render of one layer at its layout box, read from the file the settings name.</summary>
        public static void RenderLayer(FigCraftBridgeSettings settings, long layerId, string path, int scale = 1)
        {
            FigmaFileTargets.Resolve(settings);
            using var source = new FigCraftSource(settings);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".");
            source.ExportPng(layerId, Path.GetFullPath(path), scale, true);
        }

        void ExportPng(long layerId, string path, int scale, bool layoutBox)
        {
            Cli().Call("export", WithDocument(new JObject
            {
                ["ids"] = new JArray(layerId),
                ["format"] = "png",
                ["scale"] = scale,
                ["includeBoundingBox"] = layoutBox,
                ["colorProfile"] = "srgb",
                ["path"] = path,
            }), ExportTimeout);
            if (!File.Exists(path)) throw new FigCraftCliException($"FigCraft wrote no file at {path}.");
        }

        bool SizeDiffers(string nodeId, string path, int scale)
        {
            if (!_nodes.TryGetValue(nodeId, out var node)) return false;
            var box = NodeRenderBounds.Of(node);
            if (box == null || !TryReadPngSize(path, out var width, out var height)) return false;
            return Mathf.Abs(width - Mathf.CeilToInt(box.width * scale)) > 1 || Mathf.Abs(height - Mathf.CeilToInt(box.height * scale)) > 1;
        }

        void WriteCachedDocument()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FigmaApiUtils.CachedDocumentPath) ?? ".");
            using var writer = new StreamWriter(FigmaApiUtils.CachedDocumentPath, false, new UTF8Encoding(false));
            using var json = new JsonTextWriter(writer);
            _restFile.WriteTo(json);
        }

        void IndexRest(JObject node)
        {
            if (node == null) return;
            var id = node.Value<string>("id");
            if (id != null) _restNodes[id] = node;
            foreach (var child in (node["children"] as JArray ?? new JArray()).OfType<JObject>()) IndexRest(child);
        }

        static bool TryReadPngSize(string path, out int width, out int height)
        {
            width = height = 0;
            var header = new byte[24];
            using (var stream = File.OpenRead(path))
                if (stream.Read(header, 0, 24) != 24) return false;
            if (header[0] != 0x89 || header[1] != 0x50) return false;
            width = (header[16] << 24) | (header[17] << 16) | (header[18] << 8) | header[19];
            height = (header[20] << 24) | (header[21] << 16) | (header[22] << 8) | header[23];
            return true;
        }

        FigCraftCli Cli()
        {
            if (_cli != null) return _cli;
            if (_cliPath == null) throw new FigCraftCliException(FigCraftSourceText.NoCliBody(null));
            if (_kind == FigCraftSourceKind.App)
            {
                try
                {
                    _cli = FigCraftCli.Start(_cliPath, _appPort);
                    _cli.Call("inspect_document", new JObject { ["depth"] = 0 }, TimeSpan.FromSeconds(10));
                }
                catch (FigCraftCliException e)
                {
                    _cli?.Dispose();
                    _cli = null;
                    throw new FigCraftCliException(FigCraftSourceText.AppNotReachable(_appPort), e);
                }
                return _cli;
            }
            if (string.IsNullOrEmpty(_filePath) || !File.Exists(_filePath))
                throw new FigCraftCliException(FigCraftSourceText.NoFileBody(_filePath));
            _cli = FigCraftCli.Start(_cliPath, null);
            var opened = _cli.Call("open_file", new JObject { ["path"] = _filePath }, DocumentTimeout);
            _document = opened.SelectToken("document.id") ?? new JValue(_filePath);
            return _cli;
        }

        JObject WithDocument(JObject arguments)
        {
            if (_document != null) arguments["document"] = _document.DeepClone();
            return arguments;
        }

        static string FindTruncated(JToken nodes)
        {
            foreach (var node in (nodes as JArray ?? new JArray()).OfType<JObject>())
            {
                if (node["truncated"] != null && node["truncated"].Type != JTokenType.Null && node["truncated"].Type != JTokenType.Boolean
                    || node.Value<bool?>("truncated") == true)
                    return node.Value<string>("name") ?? node.Value<string>("id");
                if (FindTruncated(node["children"]) is string inner) return inner;
            }
            return null;
        }

        void Index(Node node)
        {
            if (node == null) return;
            _nodes[node.id] = node;
            if (node.children == null) return;
            foreach (var child in node.children) Index(child);
        }

        public void Dispose()
        {
            _cli?.Dispose();
            _cli = null;
        }
    }
}
