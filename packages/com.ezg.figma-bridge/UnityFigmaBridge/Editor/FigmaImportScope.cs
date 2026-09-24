using System.Collections.Generic;
using System.Linq;
using UnityFigmaBridge.Editor.FigmaApi;
using UnityFigmaBridge.Editor.Settings;
using UnityFigmaBridge.Editor.Utils;

namespace UnityFigmaBridge.Editor
{
    /// <summary>
    /// What an import builds when only the selection is imported
    /// (<see cref="UnityFigmaBridgeSettings.ImportSelectionOnly"/>): the ticked screens and
    /// components, plus every component they reach through instances. A variant pulls in its whole
    /// component set, because an instance may switch to any variant. Everything outside keeps the
    /// prefabs and sprites of the last import.
    /// </summary>
    public sealed class FigmaImportScope
    {
        private readonly HashSet<string> m_Roots = new();
        private readonly HashSet<string> m_Nodes = new();
        private readonly HashSet<string> m_Pages = new();
        private readonly HashSet<string> m_Selected = new();

        /// <summary>Screens and component units (a set, or a component outside any set) built as prefabs.</summary>
        public IReadOnlyCollection<string> Roots => m_Roots;

        public bool IsRoot(string nodeId) => m_Roots.Contains(nodeId);

        /// <summary>A root that was not ticked, only reached through an instance.</summary>
        public bool IsDependency(string nodeId) => m_Roots.Contains(nodeId) && !m_Selected.Contains(nodeId);

        /// <summary>True for a node the import builds: inside a root, or a container above one.</summary>
        public bool Builds(string nodeId) => m_Nodes.Contains(nodeId);

        public bool HasRootOnPage(string pageId) => m_Pages.Contains(pageId);

        /// <summary>Parent and id lookups for one document, shared by every scope made from it.</summary>
        public sealed class DocumentIndex
        {
            internal readonly Dictionary<string, Node> Parents = new();
            internal readonly Dictionary<string, Node> Lookup = new();

            public DocumentIndex(FigmaFile file)
            {
                if (file?.document != null) IndexNodes(file.document, null, Parents, Lookup);
            }
        }

        public static FigmaImportScope Create(FigmaFile file, IEnumerable<string> selectedNodeIds) =>
            Create(new DocumentIndex(file), selectedNodeIds);

        public static FigmaImportScope Create(DocumentIndex index, IEnumerable<string> selectedNodeIds)
        {
            var scope = new FigmaImportScope();
            var parents = index.Parents;
            var lookup = index.Lookup;

            var pending = new Queue<Node>();
            foreach (var nodeId in selectedNodeIds)
            {
                if (!lookup.TryGetValue(nodeId, out var node) || !scope.m_Roots.Add(nodeId)) continue;
                scope.m_Selected.Add(nodeId);
                pending.Enqueue(node);
            }

            while (pending.Count > 0)
            {
                foreach (var node in Subtree(pending.Dequeue()))
                {
                    scope.m_Nodes.Add(node.id);
                    if (node.type != NodeType.INSTANCE || string.IsNullOrEmpty(node.componentId)) continue;
                    // A component from another library is not in the document; it builds inline as before
                    if (!lookup.TryGetValue(node.componentId, out var component)) continue;
                    var unit = parents.TryGetValue(component.id, out var parent) && parent?.type == NodeType.COMPONENT_SET
                        ? parent
                        : component;
                    if (scope.m_Roots.Add(unit.id)) pending.Enqueue(unit);
                }
            }

            foreach (var rootId in scope.m_Roots)
            {
                var ancestor = parents.TryGetValue(rootId, out var parent) ? parent : null;
                while (ancestor != null && ancestor.type != NodeType.CANVAS)
                {
                    scope.m_Nodes.Add(ancestor.id);
                    ancestor = parents.TryGetValue(ancestor.id, out parent) ? parent : null;
                }
                if (ancestor != null) scope.m_Pages.Add(ancestor.id);
            }
            return scope;
        }

        /// <summary>
        /// Ticked screens and components on the imported pages. A screen is ticked when it gets a
        /// prefab path (<see cref="FigmaPaths.GetPathForScreenPrefab"/>), so the screen list keeps
        /// its meaning.
        /// </summary>
        public static List<string> SelectedNodeIds(UnityFigmaBridgeSettings settings, List<Node> importedPages)
        {
            var pageIds = new HashSet<string>(importedPages.Select(page => page.id));
            var selected = importedPages
                .SelectMany(page => FigmaDataUtils.GetScreenNodes(page, settings.ScreensPageName))
                .Where(screen => FigmaPaths.GetPathForScreenPrefab(screen, 0) != null)
                .Select(screen => screen.id)
                .ToList();
            selected.AddRange(settings.ComponentSelections
                .Where(row => row.Include && pageIds.Contains(row.PageNodeId))
                .Select(row => row.NodeId));
            return selected;
        }

        private static void IndexNodes(Node node, Node parent, Dictionary<string, Node> parents, Dictionary<string, Node> lookup)
        {
            if (node == null) return;
            lookup[node.id] = node;
            parents[node.id] = parent;
            if (node.children == null) return;
            foreach (var child in node.children) IndexNodes(child, node, parents, lookup);
        }

        private static IEnumerable<Node> Subtree(Node root)
        {
            var stack = new Stack<Node>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                var node = stack.Pop();
                yield return node;
                if (node.children == null) continue;
                foreach (var child in node.children)
                    if (child != null) stack.Push(child);
            }
        }
    }
}
