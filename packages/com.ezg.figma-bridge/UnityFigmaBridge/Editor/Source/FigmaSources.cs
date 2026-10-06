using System;
using UnityFigmaBridge.Editor.Settings;

namespace UnityFigmaBridge.Editor.Source
{
    public static class FigmaSources
    {
        public static IFigmaSource Create(UnityFigmaBridgeSettings settings, string accessToken)
        {
            switch (settings.Source)
            {
                case FigmaSourceKind.Rest: return new RestFigmaSource(accessToken);
                case FigmaSourceKind.Bridge: return new BridgeFigmaSource(settings.BridgePort);
                default: throw new ArgumentOutOfRangeException(nameof(settings), settings.Source, null);
            }
        }

        public static bool NeedsToken(UnityFigmaBridgeSettings settings)
        {
            switch (settings.Source)
            {
                case FigmaSourceKind.Rest: return true;
                case FigmaSourceKind.Bridge: return false;
                default: throw new ArgumentOutOfRangeException(nameof(settings), settings.Source, null);
            }
        }

        public static string Describe(UnityFigmaBridgeSettings settings)
        {
            switch (settings.Source)
            {
                case FigmaSourceKind.Rest: return "Rest";
                case FigmaSourceKind.Bridge: return $"Bridge (port {settings.BridgePort})";
                default: throw new ArgumentOutOfRangeException(nameof(settings), settings.Source, null);
            }
        }
    }
}
