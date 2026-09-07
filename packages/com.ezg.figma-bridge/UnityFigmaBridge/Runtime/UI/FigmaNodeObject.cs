using UnityEngine;

namespace UnityFigmaBridge.Runtime.UI
{
    /// <summary>
    /// Temporary representative object for FIGMA nodes to allow them to be matched
    /// When the generation and subtitution process continues
    /// </summary>
    public class FigmaNodeObject : MonoBehaviour
    {
        // Reference to the full FIGMA node id
        public string NodeId;

        /// <summary>
        /// True when the node was replaced by a server-rendered bitmap. Lets the component pass
        /// recognise a substitution without inspecting which Image subclass sits on the object,
        /// which stops telling anything once every image is a plain Image.
        /// </summary>
        public bool ServerRendered;
    }
}
