using System;
using System.Collections.Generic;
using System.Linq;
using UnityFigmaBridge.Editor.FigmaApi;
using UnityFigmaBridge.Editor.Settings;

namespace UnityFigmaBridge.Editor.Source
{
    public readonly struct BridgePage
    {
        public readonly string Id;
        public readonly string Name;

        public BridgePage(string id, string name)
        {
            Id = id;
            Name = name;
        }
    }

    /// <summary>Which pages a Bridge read asks the plugin for, decided before any page is read.</summary>
    public sealed class BridgePageSelection
    {
        /// <summary>Read every page, as before 0.8.33.</summary>
        public bool EveryPage;

        /// <summary>Also start from every page that holds a component, so the component list can be rebuilt.</summary>
        public bool ComponentPages;

        /// <summary>The pages the import starts from; the plugin adds the pages of the mains their instances use.</summary>
        public Func<IReadOnlyList<BridgePage>, IReadOnlyCollection<string>> Pick;

        public static readonly BridgePageSelection All = new BridgePageSelection { EveryPage = true };
    }

    /// <summary>The page rules of an import, from the settings. The plugin op doc.pages adds dependencies.</summary>
    public static class BridgePagePlan
    {
        /// <param name="rebuildLists">The screen and component lists will be rebuilt from this read.</param>
        public static BridgePageSelection ForImport(UnityFigmaBridgeSettings settings, bool rebuildLists)
        {
            if (!settings.ReadOnlyUsedPages) return BridgePageSelection.All;
            return new BridgePageSelection
            {
                ComponentPages = rebuildLists,
                Pick = pages => rebuildLists
                    ? ImportPages(settings, pages).Union(ScreensPages(settings, pages)).ToList()
                    : ImportPages(settings, pages),
            };
        }

        /// <summary>The pages the page, screen and component lists are built from.</summary>
        public static BridgePageSelection ForLists(UnityFigmaBridgeSettings settings)
        {
            if (!settings.ReadOnlyUsedPages) return BridgePageSelection.All;
            return new BridgePageSelection { ComponentPages = true, Pick = pages => ScreensPages(settings, pages) };
        }

        /// <summary>
        /// The selected pages (all with Only Import Selected Pages off). With Import Selection Only on,
        /// only the screens page and the pages of ticked components among them: nothing else builds.
        /// Empty when the page list no longer matches the file, so the importer reports that unread.
        /// </summary>
        public static IReadOnlyCollection<string> ImportPages(UnityFigmaBridgeSettings settings, IReadOnlyList<BridgePage> pages)
        {
            var allowed = new HashSet<string>(pages.Select(page => page.Id));
            if (settings.OnlyImportSelectedPages)
            {
                if (!allowed.SetEquals(settings.PageDataList.Select(page => page.NodeId))) return Array.Empty<string>();
                allowed.IntersectWith(settings.PageDataList.Where(page => page.Selected).Select(page => page.NodeId));
            }
            if (!settings.ImportSelectionOnly) return Ordered(pages, allowed);

            var used = new HashSet<string>(ScreensPages(settings, pages));
            used.UnionWith(settings.ComponentSelections.Where(row => row.Include).Select(row => row.PageNodeId ?? ""));
            allowed.IntersectWith(used);
            return Ordered(pages, allowed);
        }

        static IReadOnlyCollection<string> ScreensPages(UnityFigmaBridgeSettings settings, IReadOnlyList<BridgePage> pages) =>
            pages.Where(page => FigmaDataUtils.PageNameMatches(page.Name, settings.ScreensPageName)).Select(page => page.Id).ToList();

        static IReadOnlyCollection<string> Ordered(IReadOnlyList<BridgePage> pages, HashSet<string> ids) =>
            pages.Where(page => ids.Contains(page.Id)).Select(page => page.Id).ToList();
    }
}
