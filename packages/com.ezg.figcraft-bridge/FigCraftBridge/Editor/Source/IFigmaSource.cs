using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FigCraftBridge.Editor.FigmaApi;

namespace FigCraftBridge.Editor.Source
{
    /// <summary>File: a saved .figcraft read headless by figcraft-cli. App: the file in front in the FigCraft app.</summary>
    public enum FigCraftSourceKind { File, App }

    public interface IFigmaSource : IDisposable
    {
        FigCraftSourceKind Kind { get; }

        /// Writes FigmaApiUtils.CachedDocumentPath, then returns the decoded file.
        Task<FigmaFile> GetDocument(string fileId);

        /// Map node id -> file:// URL of its render. Null value = no render.
        Task<FigmaServerRenderData> GetServerRenderData(string fileId, IReadOnlyList<string> nodeIds, int scale,
            bool useAbsoluteBounds);

        /// meta.images: imageRef -> file:// URL.
        Task<FigmaImageFillData> GetImageFillData(string fileId, IReadOnlyCollection<string> usedImageRefs);
    }
}
