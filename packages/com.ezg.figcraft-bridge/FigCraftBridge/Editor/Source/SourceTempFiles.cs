using System;
using System.IO;

namespace FigCraftBridge.Editor.Source
{
    /// Where the source stages renders, fills and the document before the download queue copies them.
    internal static class SourceTempFiles
    {
        public const string Root = "Temp/FigCraftBridge/source";

        public static void Clear()
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, true);
        }

        public static string PathFor(string folder, string name)
        {
            var directory = Path.Combine(Root, folder ?? string.Empty);
            Directory.CreateDirectory(directory);
            return Path.GetFullPath(Path.Combine(directory, name));
        }

        // The project path has spaces; only Uri escapes them correctly.
        public static string Url(string fullPath) => new Uri(fullPath).AbsoluteUri;

        public static string SafeName(string nodeId) => nodeId.Replace(':', '-').Replace(';', '-');
    }
}
