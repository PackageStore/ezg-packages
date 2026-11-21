namespace FigCraftBridge.Editor.Source
{
    public static class FigCraftSourceText
    {
        public static string SyncLabel(FigCraftSourceKind kind) =>
            kind == FigCraftSourceKind.App ? "Sync from FigCraft app" : "Sync from .figcraft file";

        public static string RefreshLabel(FigCraftSourceKind kind) =>
            kind == FigCraftSourceKind.App ? "Refresh from FigCraft app" : "Refresh from file";

        public const string OfflineTooltip =
            "Rebuild every output from Library/FigCraftBridge/FigCraftOutput.json and the sprites already on disk. FigCraft is not called.";

        public static string RequirementsFailed(FigCraftSourceKind kind) => kind == FigCraftSourceKind.App
            ? "Requirements not met: settings asset, figcraft-cli or the FigCraft app control port"
            : "Requirements not met: settings asset, figcraft-cli or the .figcraft file";

        public const string NoCliTitle = "figcraft-cli not found";
        public static string NoCliBody(string configured) => string.IsNullOrWhiteSpace(configured)
            ? "figcraft-cli was not found in ~/.local/bin, /opt/homebrew/bin, /usr/local/bin or PATH. Install FigCraft's CLI, or set FigCraft CLI Path in the FigCraft Bridge window."
            : $"FigCraft CLI Path '{configured}' does not exist. Fix it in the FigCraft Bridge window, or clear it to search the usual places.";

        public const string NoFileTitle = "Missing .figcraft file";
        public static string NoFileBody(string path) => string.IsNullOrWhiteSpace(path)
            ? "Pick the .figcraft file to import in the FigCraft Bridge window."
            : $"The .figcraft file '{path}' does not exist. Pick it again in the FigCraft Bridge window.";

        public static string AppNotReachable(int port) =>
            $"The FigCraft app does not answer on control port {port}. Start FigCraft with --control {port} (FigCraft.app --args --control {port}), open the file, then Sync again; or switch the source to File.";

        public static string DescribeFile(string name, string key) => string.IsNullOrEmpty(key) ? name : $"{name} ({key})";
    }
}
