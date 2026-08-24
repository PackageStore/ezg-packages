#if UNITY_EDITOR
using UnityEditor;

namespace EZG.TechnicalArt.VisualRoadBuilder
{
    /// <summary>Injected context for all extracted subsystems — holds the four data owners.</summary>
    internal sealed class ToolContext
    {
        internal readonly RoadCanvasDoc Doc;
        internal readonly RoadPartLibrary Library;
        internal readonly ViewState View;
        internal readonly EditorWindow Host;

        internal ToolContext(RoadCanvasDoc doc, RoadPartLibrary library, ViewState view, EditorWindow host)
        {
            Doc = doc;
            Library = library;
            View = view;
            Host = host;
        }
    }
}
#endif
