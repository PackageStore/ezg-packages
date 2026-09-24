using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityFigmaBridge.Editor.FigmaApi;

namespace UnityFigmaBridge.Editor.Utils
{
    /// <summary>
    ///     Gives every image fill a readable, stable path derived from the Figma node that uses it,
    ///     instead of the <c>imageRef</c> content hash the API keys them by.
    ///
    ///     A fill is not owned by one node. Figma dedupes identical art, so one imageRef is reached
    ///     from many nodes, often on several screens and inside several components. The owner is
    ///     therefore resolved in a fixed order:
    ///
    ///     <list type="bullet">
    ///         <item>a COMPONENT or COMPONENT_SET ancestor, if any - a component prefab is shared
    ///         across screens, so its art cannot live under a single screen without the prefab
    ///         referencing an asset that a screen-only import would not produce;</item>
    ///         <item>otherwise the single screen that reaches it;</item>
    ///         <item>otherwise <c>Shared</c>, for art several screens reach with no component
    ///         between them.</item>
    ///     </list>
    ///
    ///     Names come from the nearest meaningful ancestor. Slice-grid cells are skipped, because
    ///     <c>slice_1_1</c> names a cell of a plate rather than the plate, and all nine cells share
    ///     one imageRef anyway.
    ///
    ///     Server renders get the same folder and naming, keyed by the rendered node id. Fills and
    ///     renders claim names from one set, so a render never overwrites a fill in the same folder.
    /// </summary>
    internal static class FigmaImageFillNamer
    {
        private const string SHARED_FOLDER = "Shared";
        private const string SCREENS_FOLDER = "Screens";
        private const string COMPONENTS_FOLDER = "Components";

        private static readonly Regex SliceName = new(@"^slice_(.+)_(.+)$", RegexOptions.Compiled);

        private static readonly Dictionary<string, string> s_NameByImageRef = new();
        private static readonly Dictionary<string, string> s_NameByRenderNodeId = new();
        private static readonly HashSet<string> s_Taken = new();

        private enum OwnerKind
        {
            Screen,
            Component
        }

        private readonly struct Usage
        {
            internal readonly OwnerKind Kind;
            internal readonly string Owner;
            internal readonly string Path;

            internal Usage(OwnerKind kind, string owner, string path)
            {
                Kind = kind;
                Owner = owner;
                Path = path;
            }
        }

        internal static void Clear()
        {
            s_NameByImageRef.Clear();
            s_NameByRenderNodeId.Clear();
            s_Taken.Clear();
        }

        internal static bool IsActive => s_NameByImageRef.Count > 0;

        /// <summary>
        ///     True when naming is active and nothing being imported reaches this fill, so
        ///     downloading it would only litter the folder with an unreferenced asset.
        /// </summary>
        internal static bool IsUnreachable(string imageRef) =>
            IsActive && !s_NameByImageRef.ContainsKey(imageRef);

        internal static bool TryGetRelativeName(string imageRef, out string relativeName) =>
            s_NameByImageRef.TryGetValue(imageRef, out relativeName);

        internal static bool TryGetRenderRelativeName(string nodeId, out string relativeName) =>
            s_NameByRenderNodeId.TryGetValue(nodeId, out relativeName);

        /// <param name="importedPages">
        ///     Only these pages are walked. A frame on a page nobody imports must never own an
        ///     asset, or art an imported screen needs ends up filed under a screen that does not
        ///     exist in the project.
        /// </param>
        internal static void Build(FigmaFile figmaFile, List<Node> importedPages)
        {
            Clear();
            if (figmaFile?.document == null) return;

            var usages = new Dictionary<string, List<Usage>>();
            foreach (var page in PagesToWalk(figmaFile, importedPages))
                Collect(page, null, null, new List<string>(), usages, ImageRefsOf, false);

            // Sorting by imageRef, then resolving each independently, keeps the output identical
            // between imports even if Figma reorders the document.
            foreach (var imageRef in usages.Keys.OrderBy(k => k))
            {
                var (folder, candidates) = Resolve(usages[imageRef]);
                s_NameByImageRef[imageRef] = Claim(folder, candidates, s_Taken);
            }
        }

        /// <summary>
        ///     Runs after <see cref="Build"/>, so fills keep the names they had before renders were
        ///     named. A render nothing imported owns (a pattern source in a plain frame) goes to
        ///     <c>Shared</c>; a node outside the walked pages keeps its node-id file name.
        /// </summary>
        internal static void BuildServerRenders(FigmaFile figmaFile, List<Node> importedPages,
            IEnumerable<string> renderNodeIds)
        {
            foreach (var name in s_NameByRenderNodeId.Values) s_Taken.Remove(name);
            s_NameByRenderNodeId.Clear();
            var wanted = new HashSet<string>(renderNodeIds);
            if (figmaFile?.document == null || wanted.Count == 0) return;

            var usages = new Dictionary<string, List<Usage>>();
            foreach (var page in PagesToWalk(figmaFile, importedPages))
                Collect(page, null, null, new List<string>(), usages,
                    node => wanted.Contains(node.id) ? new[] { node.id } : Array.Empty<string>(), true);

            foreach (var nodeId in usages.Keys.OrderBy(k => k, StringComparer.Ordinal))
            {
                var (folder, candidates) = Resolve(usages[nodeId]);
                s_NameByRenderNodeId[nodeId] = Claim(folder, candidates, s_Taken);
            }
        }

        private static IEnumerable<Node> PagesToWalk(FigmaFile figmaFile, List<Node> importedPages) =>
            importedPages != null && importedPages.Count > 0
                ? importedPages
                : figmaFile.document.children ?? new Node[] { };

        private static IEnumerable<string> ImageRefsOf(Node node) =>
            (node.fills ?? new Paint[] { }).Select(fill => fill?.imageRef).Where(imageRef => !string.IsNullOrEmpty(imageRef));

        /// <param name="keysOf">What this node contributes a usage for: its fills' imageRefs, or its own id when it is rendered.</param>
        /// <param name="keepUnowned">A render is drawn wherever it is reached, so it needs a name even with no owner.</param>
        private static void Collect(Node node, string screen, string component, List<string> path,
            Dictionary<string, List<Usage>> usages, Func<Node, IEnumerable<string>> keysOf, bool keepUnowned)
        {
            if (node == null) return;

            var nodePath = path;
            if (node.type != NodeType.CANVAS)
            {
                if (node.type == NodeType.FRAME && screen == null && component == null)
                {
                    // A screen the name table excludes produces no prefab, so it must not own art.
                    // Keep walking anyway: a component nested inside it still owns its own art, and
                    // that component may well be instanced by a screen that IS imported.
                    screen = FigmaPaths.GetPathForScreenPrefab(node, 0) == null ? null : node.name;
                }
                if ((node.type == NodeType.COMPONENT || node.type == NodeType.COMPONENT_SET) &&
                    component == null)
                    component = node.name;

                nodePath = new List<string>(path) { node.name };
            }
            else
            {
                screen = null;
                component = null;
                nodePath = new List<string>();
            }

            foreach (var key in keysOf(node))
            {
                if (component == null && screen == null && !keepUnowned) continue;

                var usage = component != null
                    ? new Usage(OwnerKind.Component, component, JoinPath(nodePath))
                    : new Usage(OwnerKind.Screen, screen, JoinPath(nodePath));

                if (!usages.TryGetValue(key, out var list))
                    usages[key] = list = new List<Usage>();
                list.Add(usage);
            }

            foreach (var child in node.children ?? new Node[] { })
                Collect(child, screen, component, nodePath, usages, keysOf, keepUnowned);
        }

        private static (string folder, List<string> candidates) Resolve(List<Usage> usages)
        {
            var components = usages.Where(u => u.Kind == OwnerKind.Component).ToList();
            if (components.Count > 0)
            {
                var owner = components.Select(u => u.Owner).OrderBy(o => o).First();
                var scoped = components.Where(u => u.Owner == owner).ToList();
                return ($"{COMPONENTS_FOLDER}/{Sanitise(owner)}", CandidateNames(scoped));
            }

            var screens = usages.Select(u => u.Owner).Where(o => o != null).Distinct().OrderBy(o => o).ToList();
            return screens.Count == 1
                ? ($"{SCREENS_FOLDER}/{Sanitise(screens[0])}", CandidateNames(usages))
                : (SHARED_FOLDER, CandidateNames(usages));
        }

        /// <summary>
        ///     Takes the first candidate name nobody has claimed in this folder. A counter is the
        ///     last resort, not the first, so names stay descriptive.
        /// </summary>
        private static string Claim(string folder, List<string> candidates, HashSet<string> taken)
        {
            foreach (var candidate in candidates)
                if (taken.Add($"{folder}/{candidate}"))
                    return $"{folder}/{candidate}";

            var baseName = candidates[^1];
            var suffix = 1;
            string next;
            do
            {
                next = $"{folder}/{baseName}_{suffix++}";
            } while (!taken.Add(next));

            return next;
        }

        /// <summary>
        ///     Returns candidate names for a fill, best first. Picking the shortest path keeps the
        ///     result independent of traversal order; the later candidates add ancestor segments,
        ///     which is how a wall of identically named <c>Icon</c> nodes becomes the variant name
        ///     that actually distinguishes them.
        /// </summary>
        private static List<string> CandidateNames(List<Usage> usages)
        {
            var best = usages
                .Select(u => u.Path)
                .Distinct()
                .OrderBy(p => p.Count(c => c == '/'))
                .ThenBy(p => p)
                .First();

            var segments = best.Split('/').Where(s => !string.IsNullOrWhiteSpace(s)).ToList();

            // Walk up past slice cells: all cells of one grid share this fill, and the plate above
            // them is what the art actually is.
            while (segments.Count > 1 && SliceName.IsMatch(segments[^1]))
                segments.RemoveAt(segments.Count - 1);

            if (segments.Count == 0) return new List<string> { "ImageFill" };

            var candidates = new List<string> { Sanitise(segments[^1]) };
            for (var extra = 1; extra < segments.Count && extra < 3; extra++)
            {
                var slice = segments.Skip(segments.Count - 1 - extra).Take(extra + 1);
                candidates.Add(string.Join("_", slice.Select(Sanitise)));
            }
            return candidates;
        }

        private static string JoinPath(List<string> segments) =>
            string.Join("/", segments.Where(s => !string.IsNullOrWhiteSpace(s)));

        // Same normalisation plan 15 applies to component prefab filenames, so a variant's sprite
        // and its prefab agree: "Type=Gold" -> "Type-Gold", "A, B" -> "A_B".
        private static string Sanitise(string value)
        {
            var name = string.IsNullOrWhiteSpace(value) ? "Unnamed" : value.Trim();
            name = name.Replace(", ", "_").Replace("=", "-");
            return FigmaPaths.MakeValidFileName(name);
        }
    }
}
