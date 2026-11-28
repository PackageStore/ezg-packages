using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace UnityFigmaBridge.Editor.FigmaApi
{
    /// <summary>
    ///     A Figma file holds one node per id, but a document assembled from several snapshots (a
    ///     cached document spliced with newer screens and components) can hold two: a standalone
    ///     component that was later turned into a variant keeps its id, so the old copy and the
    ///     variant in the new component set both carry it. The build then mixed the two: the component
    ///     prefab instances use was the first one met (the old copy, without the variant's newer
    ///     layers), while the node lookup kept the last one.
    ///     <para>
    ///         <see cref="Resolve"/> runs before anything reads the document. Of components sharing an
    ///         id it keeps the variant inside a component set (the newer form; the REST document says
    ///         nothing about edit times), or the first one when none or several are variants, and
    ///         drops the others from the document, so instances, lookups and prefab numbering all see
    ///         the same node. The dropped copy's prefab is not written again. Any other repeated id is
    ///         reported and left alone. Every case is a Console warning and listed in <see cref="LastReport"/>.
    ///     </para>
    /// </summary>
    public static class DuplicateNodeIds
    {
        private static readonly List<string> s_LastReport = new();

        /// <summary>What the last <see cref="Resolve"/> found, one line per repeated id (empty when every id was unique).</summary>
        public static IReadOnlyList<string> LastReport => s_LastReport;

        private sealed class Use
        {
            public Node Node;
            public Node Parent;
            public string Path;
            public bool IsVariant => Node.type == NodeType.COMPONENT && Parent is { type: NodeType.COMPONENT_SET };
        }

        /// <summary>Keeps one component per repeated id and reports every repeated id. Returns the number of nodes dropped.</summary>
        public static int Resolve(FigmaFile file)
        {
            s_LastReport.Clear();
            if (file?.document == null) return 0;

            var dropped = 0;
            foreach (var pair in Collect(file.document))
            {
                var uses = pair.Value;
                if (uses.Count < 2) continue;
                if (uses.Any(u => u.Node.type != NodeType.COMPONENT)) continue; // reported by the pass below
                var keep = uses.FirstOrDefault(u => u.IsVariant) ?? uses[0];
                foreach (var use in uses.Where(u => u != keep))
                {
                    use.Parent.children = use.Parent.children.Where(c => c != use.Node).ToArray();
                    dropped++;
                }
                Report($"[FigmaBridge] Component id {pair.Key} is used by {uses.Count} nodes: " +
                       string.Join(", ", uses.Select(Describe)) +
                       $". Kept {Describe(keep)}; dropped the other{(uses.Count > 2 ? "s" : "")} from this build (their prefab is not " +
                       "written again). A Figma file has one node per id: the document was likely spliced from snapshots, remove the stale copy.");
            }

            // Ids still repeated (after the dropped copies took their layers with them) are left as they are
            foreach (var pair in Collect(file.document).Where(p => p.Value.Count > 1))
                Report($"[FigmaBridge] Node id {pair.Key} is used by {pair.Value.Count} nodes: " +
                       string.Join(", ", pair.Value.Select(Describe)) +
                       ". Which one a step uses is undefined; remove the duplicate from the document.");
            return dropped;
        }

        private static void Report(string message)
        {
            s_LastReport.Add(message);
            Debug.LogWarning(message);
        }

        private static string Describe(Use use) =>
            $"'{use.Path}' ({(use.IsVariant ? $"variant of '{use.Parent.name}'" : use.Node.type == NodeType.COMPONENT ? "standalone component" : use.Node.type.ToString())})";

        /// <summary>Every repeated id with its nodes; a first walk finds the repeats so only those get a path.</summary>
        private static Dictionary<string, List<Use>> Collect(Node document)
        {
            var seen = new HashSet<string>();
            var repeated = new HashSet<string>();
            Count(document, true, seen, repeated);
            var uses = new Dictionary<string, List<Use>>();
            if (repeated.Count > 0) Walk(document, null, new List<string>(), repeated, uses);
            return uses;
        }

        private static void Count(Node node, bool isDocument, HashSet<string> seen, HashSet<string> repeated)
        {
            if (node == null) return;
            if (!isDocument && !string.IsNullOrEmpty(node.id) && !seen.Add(node.id)) repeated.Add(node.id);
            if (node.children == null) return;
            foreach (var child in node.children) Count(child, false, seen, repeated);
        }

        private static void Walk(Node node, Node parent, List<string> path, HashSet<string> repeated, Dictionary<string, List<Use>> uses)
        {
            if (node == null) return;
            if (parent != null && !string.IsNullOrEmpty(node.id) && repeated.Contains(node.id))
            {
                if (!uses.TryGetValue(node.id, out var list)) uses[node.id] = list = new List<Use>(2);
                list.Add(new Use { Node = node, Parent = parent, Path = string.Join("/", path.Append(node.name)) });
            }
            if (node.children == null) return;
            if (parent != null) path.Add(node.name);
            foreach (var child in node.children) Walk(child, node, path, repeated, uses);
            if (parent != null) path.RemoveAt(path.Count - 1);
        }
    }
}
