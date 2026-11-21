using System;
using System.IO;
using FigCraftBridge.Editor.Settings;

namespace FigCraftBridge.Editor.Source
{
    public readonly struct FigmaFileTarget
    {
        public readonly string Key;
        public readonly string Name;

        public FigmaFileTarget(string key, string name)
        {
            Key = key;
            Name = name;
        }
    }

    public sealed class FigmaFileTargetException : Exception
    {
        public FigmaFileTargetException(string userMessage, Exception inner = null) : base(userMessage, inner) { }
    }

    public static class FigmaFileTargets
    {
        /// <summary>The file a Sync reads: the .figcraft path, or the app's control port.</summary>
        public static FigmaFileTarget Resolve(FigCraftBridgeSettings settings)
        {
            if (FigCraftCli.Locate(settings.FigCraftCliPath) == null)
                throw new FigmaFileTargetException(FigCraftSourceText.NoCliBody(settings.FigCraftCliPath));
            if (settings.Source == FigCraftSourceKind.App)
                return new FigmaFileTarget($"app:{settings.AppPort}", "FigCraft app");
            var path = settings.FigCraftFileFullPath;
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                throw new FigmaFileTargetException(FigCraftSourceText.NoFileBody(settings.FigCraftFile));
            return new FigmaFileTarget(settings.FigCraftFile.Replace('\\', '/'), Path.GetFileNameWithoutExtension(path));
        }
    }
}
