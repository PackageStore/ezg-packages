using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityFigmaBridge.Editor.Bridge;
using UnityFigmaBridge.Editor.FigmaApi;

namespace UnityFigmaBridge.Editor.Source
{
    /// Reads the open Figma file through the EZG Tools plugin and hands it to the importer as REST DTOs with file:// URLs.
    public sealed class BridgeFigmaSource : IFigmaSource
    {
        /// Nodes per export request. Figma renders on one thread, so a large group only waits longer;
        /// the importer also sends batches of this size, so its progress bar moves per group.
        public const int ExportBatchSize = 10;
        const int ImagesChunk = 100;
        static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(3);
        static readonly TimeSpan SnapshotTimeout = TimeSpan.FromSeconds(120);
        static readonly TimeSpan BatchTimeout = TimeSpan.FromSeconds(60);
        static readonly TimeSpan ExportTimeoutPerNode = TimeSpan.FromSeconds(10);

        sealed class Session { public HubClient Client; public HubFile File; }

        readonly int _port;
        readonly object _gate = new object();
        Task<Session> _session;

        public BridgeFigmaSource(int port) { _port = port; }
        public FigmaSourceKind Kind => FigmaSourceKind.Bridge;

        public async Task<FigmaFile> GetDocument(string fileId)
        {
            BridgeTempFiles.Clear();
            var result = await Request(fileId, "doc.snapshot", new JObject(), SnapshotTimeout).ConfigureAwait(false);
            var text = (string)result["text"];
            BridgeTempFiles.Write(string.Empty, "document.json", new UTF8Encoding(false).GetBytes(text));
            File.Copy(Path.Combine(BridgeTempFiles.Root, "document.json"), FigmaApiUtils.CachedDocumentPath, true);
            var figmaFile = FigmaApiUtils.ReadDocument(FigmaApiUtils.CachedDocumentPath);
            await WarnMissingFonts(fileId, figmaFile).ConfigureAwait(false);
            return figmaFile;
        }

        public async Task<FigmaServerRenderData> GetServerRenderData(string fileId, IReadOnlyList<string> nodeIds,
            int scale, bool useAbsoluteBounds)
        {
            var images = new Dictionary<string, string>();
            foreach (var chunk in Chunks(nodeIds, ExportBatchSize))
            {
                // A heavy render can take seconds, and a timed-out export keeps running in the plugin
                // until its next yield, so the limit grows with the group.
                var timeout = BatchTimeout + TimeSpan.FromTicks(ExportTimeoutPerNode.Ticks * chunk.Count);
                var result = await Export(fileId, chunk, scale, useAbsoluteBounds, timeout).ConfigureAwait(false);
                foreach (var file in result["files"] as JArray ?? new JArray())
                {
                    var id = (string)file["nodeId"];
                    var base64 = (string)file["base64"];
                    if (id == null || base64 == null) continue;
                    images[id] = BridgeTempFiles.Write("renders", BridgeTempFiles.SafeName(id) + ".png",
                        Convert.FromBase64String(base64));
                }
                foreach (var failure in result["failed"] as JArray ?? new JArray())
                {
                    var id = (string)failure["nodeId"];
                    if (id == null) continue;
                    images[id] = null;
                    Debug.LogWarning($"[FigmaBridge] Render of {id} failed: {(string)failure["error"]}");
                }
            }
            foreach (var id in nodeIds.Where(id => !images.ContainsKey(id))) images[id] = null;
            return new FigmaServerRenderData { err = null, images = images };
        }

        public async Task<FigmaImageFillData> GetImageFillData(string fileId, IReadOnlyCollection<string> usedImageRefs)
        {
            var images = new Dictionary<string, string>();
            foreach (var chunk in Chunks(usedImageRefs.Distinct().ToList(), ImagesChunk))
            {
                var payload = new JObject { ["hashes"] = new JArray(chunk) };
                var result = await Request(fileId, "images.get", payload, BatchTimeout).ConfigureAwait(false);
                foreach (var image in result["images"] as JArray ?? new JArray())
                {
                    var hash = (string)image["hash"];
                    images[hash] = BridgeTempFiles.Write("fills", hash, Convert.FromBase64String((string)image["base64"]));
                }
                foreach (var hash in result["missing"] as JArray ?? new JArray())
                    Debug.LogWarning($"[FigmaBridge] Image fill missing: {(string)hash}");
                foreach (var failure in result["failed"] as JArray ?? new JArray())
                    Debug.LogWarning($"[FigmaBridge] Image fill failed: {(string)failure["hash"]} ({(string)failure["error"]})");
            }
            return new FigmaImageFillData { error = false, status = 200, meta = new FigmaImageFillMetaData { images = images } };
        }

        /// Blocks the caller; safe on the main thread because HubClient never needs it (plan 21).
        public byte[] RenderPngBlocking(string fileId, string nodeId, bool useAbsoluteBounds, TimeSpan timeout)
        {
            var result = Export(fileId, new[] { nodeId }, 1, useAbsoluteBounds, timeout).GetAwaiter().GetResult();
            var file = (result["files"] as JArray)?.FirstOrDefault(f => (string)f["nodeId"] == nodeId);
            var base64 = (string)file?["base64"];
            if (base64 != null) return Convert.FromBase64String(base64);
            var failure = (result["failed"] as JArray)?.FirstOrDefault(f => (string)f["nodeId"] == nodeId);
            throw new Exception($"Figma could not render {nodeId}: {(string)failure?["error"] ?? "no file returned"}");
        }

        public void Dispose()
        {
            Task<Session> session;
            lock (_gate) { session = _session; _session = null; }
            if (session != null && session.Status == TaskStatus.RanToCompletion) session.Result.Client.Dispose();
        }

        Task<JToken> Export(string fileId, IEnumerable<string> nodeIds, int scale, bool useAbsoluteBounds, TimeSpan timeout) =>
            Request(fileId, "export", new JObject
            {
                ["nodeIds"] = new JArray(nodeIds),
                ["format"] = "PNG",
                ["scale"] = scale,
                ["useAbsoluteBounds"] = useAbsoluteBounds,
            }, timeout);

        async Task WarnMissingFonts(string fileId, FigmaFile figmaFile)
        {
            try
            {
                var pageIds = (figmaFile.document?.children ?? new Node[0]).Select(page => page.id).ToList();
                if (pageIds.Count == 0) return;
                var payload = new JObject { ["textStyles"] = true, ["nodeIds"] = new JArray(pageIds) };
                var result = await Request(fileId, "fonts.check", payload, BatchTimeout).ConfigureAwait(false);
                foreach (var font in result["missing"] as JArray ?? new JArray())
                    Debug.LogWarning($"[FigmaBridge] Missing font {(string)font}");
            }
            catch (Exception e) { Debug.LogWarning($"[FigmaBridge] Font check skipped: {e.Message}"); }
        }

        static IEnumerable<List<string>> Chunks(IReadOnlyList<string> items, int size)
        {
            for (var start = 0; start < items.Count; start += size)
                yield return items.Skip(start).Take(size).ToList();
        }

        async Task<JToken> Request(string fileId, string op, JToken payload, TimeSpan timeout)
        {
            var session = await GetSession(fileId).ConfigureAwait(false);
            try { return await session.Client.Request(session.File.ConnectionId, op, payload, timeout).ConfigureAwait(false); }
            catch (HubRequestException e)
            {
                if (e.Kind == HubRequestKind.Closed) Forget(session);
                throw Translate(e);
            }
        }

        static Exception Translate(HubRequestException e)
        {
            if (e.Kind != HubRequestKind.Remote) return new FigmaApiRequestException(0, e.Message);
            if (e.RemoteCode == "EXPORT_STUCK")
                return new Exception("The EZG Tools export is stuck: close and reopen the EZG Tools plugin in Figma. " + e.Message);
            if (e.Message.Contains("unknown op"))
                return new Exception("The EZG Tools plugin is old: reopen it to update. " + e.Message);
            return new Exception(e.Message);
        }

        void Forget(Session session)
        {
            lock (_gate)
            {
                if (_session == null || _session.Status != TaskStatus.RanToCompletion || _session.Result != session) return;
                _session = null;
            }
            session.Client.Dispose();
        }

        Task<Session> GetSession(string fileId)
        {
            lock (_gate)
            {
                if (_session == null || _session.IsFaulted || _session.IsCanceled) _session = Open(fileId);
                return _session;
            }
        }

        async Task<Session> Open(string fileId)
        {
            HubClient client;
            try { client = await HubClient.Connect(_port, ConnectTimeout).ConfigureAwait(false); }
            catch (Exception e) when (!(e is OperationCanceledException))
            {
                throw new Exception(FigmaSourceText.BridgeNoHub(_port) + (string.IsNullOrWhiteSpace(e.Message) ? "" : " (" + e.Message + ")"));
            }
            var file = await client.WaitForFile(fileId, ConnectTimeout).ConfigureAwait(false);
            if (file == null)
            {
                var open = client.Files.Select(f => FigmaSourceText.DescribeFile(f.FileName, f.FileKey)).ToList();
                var list = open.Count == 0 ? "none" : string.Join(", ", open);
                var key = string.IsNullOrEmpty(fileId) ? "(no file key)" : fileId;
                client.Dispose();
                throw new Exception(FigmaSourceText.BridgeFileNotConnected(key, list));
            }
            if (client.IsUsable(file)) return new Session { Client = client, File = file };
            client.Dispose();
            throw new Exception("This Figma file runs an old EZG Tools plugin: update the EZG Tools plugin and reopen it.");
        }
    }
}
