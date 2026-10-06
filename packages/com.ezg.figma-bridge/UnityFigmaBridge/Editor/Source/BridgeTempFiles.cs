using System;
using System.IO;

namespace UnityFigmaBridge.Editor.Source
{
    /// Where the Bridge source stages renders, fills and the snapshot before the download queue copies them.
    internal static class BridgeTempFiles
    {
        public const string Root = "Temp/FigmaBridge/bridge";

        public static void Clear()
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, true);
        }

        public static string Write(string folder, string name, byte[] bytes)
        {
            var directory = Path.Combine(Root, folder ?? string.Empty);
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, name);
            File.WriteAllBytes(path, bytes);
            // The project path has spaces; only Uri escapes them correctly.
            return new Uri(Path.GetFullPath(path)).AbsoluteUri;
        }

        public static string SafeName(string nodeId) => nodeId.Replace(':', '-').Replace(';', '-');
    }
}
