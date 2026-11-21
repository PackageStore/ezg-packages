using FigCraftBridge.Editor.Settings;

namespace FigCraftBridge.Editor.Source
{
    public static class FigmaSources
    {
        public static IFigmaSource Create(FigCraftBridgeSettings settings) => new FigCraftSource(settings);

        public static string Describe(FigCraftBridgeSettings settings) => settings.Source == FigCraftSourceKind.App
            ? $"FigCraft app (control port {settings.AppPort})"
            : $"FigCraft file {settings.FigCraftFile}";
    }
}
