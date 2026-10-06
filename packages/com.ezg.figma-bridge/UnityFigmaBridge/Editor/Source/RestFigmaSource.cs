using System.Collections.Generic;
using System.Threading.Tasks;
using UnityFigmaBridge.Editor.FigmaApi;

namespace UnityFigmaBridge.Editor.Source
{
    public sealed class RestFigmaSource : IFigmaSource
    {
        private readonly string _accessToken;

        public RestFigmaSource(string accessToken) => _accessToken = accessToken;

        public FigmaSourceKind Kind => FigmaSourceKind.Rest;

        public Task<FigmaFile> GetDocument(string fileId) =>
            FigmaApiUtils.GetFigmaDocument(fileId, _accessToken, true);

        public Task<FigmaServerRenderData> GetServerRenderData(string fileId, IReadOnlyList<string> nodeIds, int scale,
            bool useAbsoluteBounds) =>
            FigmaApiUtils.GetFigmaServerRenderData(fileId, _accessToken, nodeIds, scale, useAbsoluteBounds);

        public Task<FigmaImageFillData> GetImageFillData(string fileId, IReadOnlyCollection<string> usedImageRefs) =>
            FigmaApiUtils.GetDocumentImageFillData(fileId, _accessToken);

        public void Dispose() { }
    }
}
