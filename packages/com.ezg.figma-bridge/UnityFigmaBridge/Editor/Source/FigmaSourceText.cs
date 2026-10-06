using System;

namespace UnityFigmaBridge.Editor.Source
{
    public static class FigmaSourceText
    {
        public static string SyncLabel(FigmaSourceKind kind)
        {
            switch (kind)
            {
                case FigmaSourceKind.Rest: return "Sync from Figma API";
                case FigmaSourceKind.Bridge: return "Sync from open Figma file";
                default: throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
            }
        }

        public static string RefreshLabel(FigmaSourceKind kind)
        {
            switch (kind)
            {
                case FigmaSourceKind.Rest: return "Refresh from Figma";
                case FigmaSourceKind.Bridge: return "Refresh from open file";
                default: throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
            }
        }

        public static string OfflineTooltip(FigmaSourceKind kind)
        {
            switch (kind)
            {
                case FigmaSourceKind.Rest: return "Rebuild every output from Assets/FigmaOutput.json and the sprites already on disk. No Figma API call.";
                case FigmaSourceKind.Bridge: return "Rebuild every output from Assets/FigmaOutput.json and the sprites already on disk. No Figma connection needed.";
                default: throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
            }
        }

        public static string RequirementsFailed(FigmaSourceKind kind)
        {
            switch (kind)
            {
                case FigmaSourceKind.Rest: return "Requirements not met: settings asset, document url or token";
                case FigmaSourceKind.Bridge: return "Requirements not met: settings asset or Bridge connection";
                default: throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
            }
        }

        public const string RestInvalidUrlTitle = "Missing Figma Document";
        public const string RestInvalidUrlBody = "Figma Document Url is not valid, please enter valid URL";

        public static string BridgeNoHub(int port) => $"No hub on port {port}. In Figma, open EZG Tools, go to the MCP tab and press the connect button. Check Bridge Port is the port shown there.";
        public static string BridgeFileNotConnected(string fileKey, string connectedList) => $"Figma file '{fileKey}' is not connected. Connected: {ListOrNone(connectedList)}. Open that file in Figma, run EZG Tools, go to the MCP tab and press the connect button.";
        public static string BridgeNoFile(int port) => $"The hub on port {port} has no Figma file connected. Open a file in Figma, run EZG Tools, go to the MCP tab and press the connect button.";
        public static string BridgeAmbiguous(string connectedList) => $"Several Figma files are open ({ListOrNone(connectedList)}). Pick one in the Figma Bridge window.";
        public static string BridgeUnsavedFile(string fileName) => $"'{(string.IsNullOrEmpty(fileName) ? "untitled" : fileName)}' is a draft with no file key. Save it in Figma first.";
        public const string BridgeFileChangedTitle = "Figma file changed";
        public static string BridgeFileChangedBody(string boundName, string openName) => $"This project is bound to '{boundName}', but the open file is '{openName}'. Importing overwrites the output and rebinds the project to the open file.";

        public static string DescribeFile(string name, string key) => string.IsNullOrEmpty(key) ? $"{name} (unsaved)" : $"{name} ({key})";

        static string ListOrNone(string list) => string.IsNullOrEmpty(list) ? "none" : list;
    }
}
