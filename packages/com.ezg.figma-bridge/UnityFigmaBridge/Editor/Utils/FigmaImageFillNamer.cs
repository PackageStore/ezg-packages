using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
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
    ///
    ///     A fill whose art is already on disk under one of its names keeps that file, so new art
    ///     in the document never renames the fills that were there before it.
    /// </summary>
    internal static class FigmaImageFillNamer
    {
        private const string SHARED_FOLDER = "Shared";
        private const string SCREENS_FOLDER = "Screens";
        private const string COMPONENTS_FOLDER = "Components";

        private static readonly Regex SliceName = new(@"^slice_(.+)_(.+)$", RegexOptions.Compiled);

        private static readonly Dictionary<string, string> s_NameByImageRef = new();
        private static readonly Dictionary<string, string> s_NameByRenderNodeId = new();
        // The asset database and the macOS and Windows file systems ignore case: "Fill" and "fill"
        // are one file, and the second render written there replaced the first
        private static readonly HashSet<string> s_Taken = new(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> s_Reachable = new();

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
            internal readonly bool Reachable;

            internal Usage(OwnerKind kind, string owner, string path, bool reachable)
            {
                Kind = kind;
                Owner = owner;
                Path = path;
                Reachable = reachable;
            }
        }

        internal static void Clear()
        {
            s_NameByImageRef.Clear();
            s_NameByRenderNodeId.Clear();
            s_Taken.Clear();
            s_Reachable.Clear();
        }

        internal static bool IsActive => s_NameByImageRef.Count > 0;

        /// <summary>
        ///     True when naming is active and nothing being imported reaches this fill, so
        ///     downloading it would only litter the folder with an unreferenced asset.
        /// </summary>
        internal static bool IsUnreachable(string imageRef) =>
            IsActive && !s_Reachable.Contains(imageRef);

        internal static bool TryGetRelativeName(string imageRef, out string relativeName) =>
            s_NameByImageRef.TryGetValue(imageRef, out relativeName);

        internal static bool TryGetRenderRelativeName(string nodeId, out string relativeName) =>
            s_NameByRenderNodeId.TryGetValue(nodeId, out relativeName);

        /// <param name="importedPages">
        ///     Every page is walked and every listed screen owns its art, ticked or not, so a name
        ///     does not move when the ticks or the page selection change. These pages decide only
        ///     what is worth downloading: art a component uses, or a ticked screen on one of them.
        ///     Art only an unticked screen uses is never downloaded, so no folder is made for a
        ///     screen that is not in the project.
        /// </param>
        internal static void Build(FigmaFile figmaFile, List<Node> importedPages) =>
            Build(figmaFile, importedPages, FigmaPaths.FigmaImageFillFolder);

        /// <param name="imageFillFolder">
        ///     The document's image fill folder. Fills whose art is already in it keep their files;
        ///     null or empty names every fill as if the folder were empty.
        /// </param>
        internal static void Build(FigmaFile figmaFile, List<Node> importedPages, string imageFillFolder)
        {
            Clear();
            if (figmaFile?.document == null) return;

            var usages = new Dictionary<string, List<Usage>>();
            var importedPageIds = new HashSet<string>((importedPages ?? new List<Node>()).Select(page => page.id));
            foreach (var page in figmaFile.document.children ?? new Node[] { })
                Collect(page, false, false, null, false, null, new List<string>(), usages, ImageRefsOf, false, importedPageIds);

            // Sorting by imageRef, then resolving each independently, keeps the output identical
            // between imports even if Figma reorders the document.
            var fills = usages.Keys.OrderBy(k => k)
                .Select(imageRef =>
                {
                    var (folder, candidates) = Resolve(usages[imageRef]);
                    return (imageRef, folder, candidates);
                })
                .ToList();

            // Fills already on disk take their names before any new fill claims one
            var kept = KeepNamesOnDisk(fills, imageFillFolder, s_Taken);
            foreach (var (imageRef, folder, candidates) in fills)
            {
                s_NameByImageRef[imageRef] = kept.TryGetValue(imageRef, out var keptName)
                    ? keptName
                    : Claim(folder, candidates, s_Taken);
                if (usages[imageRef].Any(usage => usage.Reachable)) s_Reachable.Add(imageRef);
            }
        }

        /// <summary>
        ///     Claims run in imageRef order, so a new fill whose hash sorts early takes the first free
        ///     name of its family and pushes every later fill of that family down one name. Their
        ///     files keep the old art under names that now belong to other fills: each sprite GUID a
        ///     prefab holds changes its art, and a re-download only swaps the art between GUIDs. A fill
        ///     whose own art already sits in its folder under one of its names therefore keeps that
        ///     file. Figma's imageRef is the SHA-1 of the image bytes, so this needs no record of
        ///     earlier imports. Only files named like the fill are hashed.
        /// </summary>
        /// <returns>imageRef to the relative name it keeps; every kept name is added to <paramref name="taken"/>.</returns>
        private static Dictionary<string, string> KeepNamesOnDisk(
            List<(string imageRef, string folder, List<string> candidates)> fills, string imageFillFolder, HashSet<string> taken)
        {
            var kept = new Dictionary<string, string>();
            if (string.IsNullOrEmpty(imageFillFolder)) return kept;

            var namesByFolder = new Dictionary<string, string[]>();
            var hashByPath = new Dictionary<string, string>();
            using var sha1 = SHA1.Create();
            foreach (var (imageRef, folder, candidates) in fills)
            {
                if (!namesByFolder.TryGetValue(folder, out var names))
                {
                    var directory = $"{imageFillFolder}/{folder}";
                    namesByFolder[folder] = names = Directory.Exists(directory)
                        ? Directory.GetFiles(directory, "*.png").Select(Path.GetFileNameWithoutExtension).ToArray()
                        : Array.Empty<string>();
                }

                string keptName = null;
                var keptRank = long.MaxValue;
                foreach (var name in names)
                {
                    var rank = FamilyRank(name, candidates);
                    if (rank < 0 || rank >= keptRank || taken.Contains($"{folder}/{name}")) continue;

                    var path = $"{imageFillFolder}/{folder}/{name}.png";
                    if (!hashByPath.TryGetValue(path, out var hash)) hashByPath[path] = hash = HashOf(sha1, path);
                    if (!string.Equals(hash, imageRef, StringComparison.OrdinalIgnoreCase)) continue;

                    keptName = name;
                    keptRank = rank;
                }

                if (keptName == null) continue;
                var relativeName = $"{folder}/{keptName}";
                taken.Add(relativeName);
                kept[imageRef] = relativeName;
            }

            return kept;
        }

        /// <summary>
        ///     Where a file name sits among the names <see cref="Claim"/> could give a fill: its
        ///     candidates in order, then the counter forms of the last one. -1 for a name the fill
        ///     could never get, so a file is only kept by a fill it could have been named for.
        /// </summary>
        private static long FamilyRank(string name, List<string> candidates)
        {
            var index = candidates.IndexOf(name);
            if (index >= 0) return index;

            var counterPrefix = candidates[^1] + "_";
            if (name.Length <= counterPrefix.Length || !name.StartsWith(counterPrefix, StringComparison.Ordinal)) return -1;
            return int.TryParse(name.Substring(counterPrefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var counter) &&
                   counter > 0
                ? candidates.Count + (long)counter
                : -1;
        }

        /// <returns>Lower-case hex SHA-1 of the file, or null when it cannot be read.</returns>
        private static string HashOf(SHA1 sha1, string path)
        {
            try
            {
                using var stream = File.OpenRead(path);
                return BitConverter.ToString(sha1.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
            }
            catch (IOException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
        }

        /// <summary>
        ///     Runs after <see cref="Build"/>, so fills keep the names they had before renders were
        ///     named. Owners are resolved over every page, like fills. A render nothing owns (a
        ///     pattern source in an unlisted frame) goes to <c>Shared</c>.
        /// </summary>
        internal static void BuildServerRenders(FigmaFile figmaFile, IEnumerable<string> renderNodeIds)
        {
            foreach (var name in s_NameByRenderNodeId.Values) s_Taken.Remove(name);
            s_NameByRenderNodeId.Clear();
            var wanted = new HashSet<string>(renderNodeIds);
            if (figmaFile?.document == null || wanted.Count == 0) return;

            var usages = new Dictionary<string, List<Usage>>();
            foreach (var page in figmaFile.document.children ?? new Node[] { })
                Collect(page, false, false, null, false, null, new List<string>(), usages,
                    node => wanted.Contains(node.id) ? new[] { node.id } : Array.Empty<string>(), true, null);

            foreach (var nodeId in usages.Keys.OrderBy(k => k, StringComparer.Ordinal))
            {
                var (folder, candidates) = Resolve(usages[nodeId]);
                s_NameByRenderNodeId[nodeId] = Claim(folder, candidates, s_Taken);
            }
        }

        private static IEnumerable<string> ImageRefsOf(Node node) =>
            (node.fills ?? new Paint[] { }).Select(fill => fill?.imageRef).Where(imageRef => !string.IsNullOrEmpty(imageRef));

        /// <param name="keysOf">What this node contributes a usage for: its fills' imageRefs, or its own id when it is rendered.</param>
        /// <param name="keepUnowned">A render is drawn wherever it is reached, so it needs a name even with no owner.</param>
        /// <param name="importedPageIds">Pages whose ticked screens make art reachable; null when reach is not tracked.</param>
        private static void Collect(Node node, bool pageImported, bool onScreensPage, string screen, bool screenImported, string component,
            List<string> path, Dictionary<string, List<Usage>> usages, Func<Node, IEnumerable<string>> keysOf,
            bool keepUnowned, HashSet<string> importedPageIds)
        {
            if (node == null) return;

            var nodePath = path;
            if (node.type != NodeType.CANVAS)
            {
                // A component directly on the screens page is a screen, like a frame there
                var screenCandidate = node.type == NodeType.FRAME || (node.type == NodeType.COMPONENT && path.Count == 0);
                var isScreenRoot = false;
                if (screenCandidate && screen == null && component == null && onScreensPage)
                {
                    // Keep walking an unlisted frame: a component nested inside it still owns its own
                    // art, and that component may well be instanced by a screen that IS imported.
                    screen = FigmaPaths.IsListedScreen(node) ? node.name : null;
                    screenImported = screen != null && pageImported && FigmaPaths.GetPathForScreenPrefab(node, 0) != null;
                    isScreenRoot = screen != null;
                }
                if ((node.type == NodeType.COMPONENT || node.type == NodeType.COMPONENT_SET) &&
                    component == null && !isScreenRoot)
                    component = node.name;

                nodePath = new List<string>(path) { node.name };
            }
            else
            {
                pageImported = importedPageIds != null && importedPageIds.Contains(node.id);
                // A frame on any other page is a container for components, never a screen
                onScreensPage = FigmaDataUtils.IsScreensPage(node, FigmaPaths.ScreensPageName);
                screen = null;
                screenImported = false;
                component = null;
                nodePath = new List<string>();
            }

            foreach (var key in keysOf(node))
            {
                if (component == null && screen == null && !keepUnowned) continue;

                var usage = component != null
                    ? new Usage(OwnerKind.Component, component, JoinPath(nodePath), true)
                    : new Usage(OwnerKind.Screen, screen, JoinPath(nodePath), screenImported);

                if (!usages.TryGetValue(key, out var list))
                    usages[key] = list = new List<Usage>();
                list.Add(usage);
            }

            foreach (var child in node.children ?? new Node[] { })
                Collect(child, pageImported, onScreensPage, screen, screenImported, component, nodePath, usages, keysOf,
                    keepUnowned, importedPageIds);
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
