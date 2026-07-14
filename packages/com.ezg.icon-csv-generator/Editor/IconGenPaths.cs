#nullable enable

namespace Ezg.IconCsvGenerator.Editor
{
    /// <summary>
    /// Project-agnostic resolver for the two roots the tool writes to and reads from.
    /// Defaults apply until the window pushes the active <see cref="IconGeneratorSettings"/>
    /// values via <see cref="ApplyFrom"/>, so a consumer only ever edits these paths in the
    /// settings asset — never in package source.
    /// </summary>
    internal static class IconGenPaths
    {
        internal const string DefaultIncomingRoot        = "Assets/_Incoming";
        internal const string DefaultReferenceImagesRoot = "Assets/Editor/IconReferenceImages";

        /// <summary>Project-relative staging root where generated PSDs are written (under a per-group subfolder).</summary>
        internal static string IncomingRoot { get; private set; } = DefaultIncomingRoot;

        /// <summary>Project-relative root holding per-group reference images.</summary>
        internal static string ReferenceImagesRoot { get; private set; } = DefaultReferenceImagesRoot;

        /// <summary>
        /// Adopts the roots configured on the active settings asset. Blank fields keep the current
        /// value; trailing slashes are trimmed so callers can always append "/subfolder".
        /// </summary>
        internal static void ApplyFrom(IconGeneratorSettings? settings)
        {
            if (settings == null) return;
            if (!string.IsNullOrWhiteSpace(settings.incomingRoot))
                IncomingRoot = settings.incomingRoot.TrimEnd('/');
            if (!string.IsNullOrWhiteSpace(settings.referenceImagesRoot))
                ReferenceImagesRoot = settings.referenceImagesRoot.TrimEnd('/');
        }
    }
}
