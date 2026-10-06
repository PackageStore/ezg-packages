using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityFigmaBridge.Editor.FigmaApi;

namespace UnityFigmaBridge.Editor.Source
{
    public enum FigmaSourceKind { Rest, Bridge }

    public interface IFigmaSource : IDisposable
    {
        FigmaSourceKind Kind { get; }

        /// Writes FigmaApiUtils.CachedDocumentPath, as the REST download does, then returns the decoded file.
        Task<FigmaFile> GetDocument(string fileId);

        /// Map node id -> URL the importer downloads (https for REST, file:// for Bridge). Null value = no render.
        Task<FigmaServerRenderData> GetServerRenderData(string fileId, IReadOnlyList<string> nodeIds, int scale,
            bool useAbsoluteBounds);

        /// meta.images: imageRef -> URL. REST ignores usedImageRefs and lists the whole file.
        Task<FigmaImageFillData> GetImageFillData(string fileId, IReadOnlyCollection<string> usedImageRefs);
    }
}
