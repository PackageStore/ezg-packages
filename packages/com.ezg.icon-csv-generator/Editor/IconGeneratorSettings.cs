#nullable enable

namespace Ezg.IconCsvGenerator.Editor
{
    using System.Collections.Generic;
    using UnityEditor;
    using UnityEngine;

    // ---------------------------------------------------------------------------
    // Global generation settings ScriptableObject for the Icon Generator tool.
    // One committed asset lives at:
    //   <anywhere under Assets/> — created via Assets ▸ Create ▸ EZG Technical Art ▸ Icon CSV Generator ▸ Settings
    //
    // The window reads this asset at open time (via IconSettingsLoader) and
    // writes it back whenever the artist changes a dropdown.
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Stores the global Gemini generation parameters that apply to every row
    /// plus the user-nameable list of CSV groups (each with its own CSV path,
    /// filter, filename pattern, prompt template, and reference images).
    ///
    /// Artist-facing: change values via the Icon Generator window — do NOT edit
    /// the asset YAML directly.
    /// </summary>
    [CreateAssetMenu(
        menuName = "EZG Technical Art/Icon CSV Generator/Settings",
        fileName = "IconGeneratorSettings")]
    internal sealed class IconGeneratorSettings : ScriptableObject
    {
        // ── Supported value arrays (ordered as presented in the dropdowns) ─────────

        /// <summary>Aspect ratio values accepted by the Gemini responseFormat.image.aspectRatio field.</summary>
        internal static readonly string[] ASPECT_RATIO_OPTIONS =
        {
            "1:1", "2:3", "3:2", "3:4", "4:3", "4:5", "5:4", "9:16", "16:9", "21:9",
        };

        /// <summary>Image size values accepted by the Gemini responseFormat.image.imageSize field.</summary>
        internal static readonly string[] IMAGE_SIZE_OPTIONS =
        {
            "512", "1K", "2K", "4K",
        };

        /// <summary>Max-concurrency dropdown values (2…10). Stored value = index + 2.</summary>
        internal static readonly string[] CONCURRENCY_OPTIONS =
        {
            "2", "3", "4", "5", "6", "7", "8", "9", "10",
        };

        // ── Serialized fields ─────────────────────────────────────────────────────

        /// <summary>Gemini aspect ratio parameter (responseFormat.image.aspectRatio). Default "1:1".</summary>
        [Tooltip("Aspect ratio sent to Gemini. Maps to responseFormat.image.aspectRatio.")]
        public string aspectRatio = "1:1";

        /// <summary>Gemini image size parameter (responseFormat.image.imageSize). Default "1K".</summary>
        [Tooltip("Image size sent to Gemini. Maps to responseFormat.image.imageSize. '1K' ≈ 1024 px.")]
        public string imageSize = "1K";

        /// <summary>Max icons generated at once (parallel Gemini requests). Range 2–10, default 2.</summary>
        [Tooltip("Max icons generated at once (parallel Gemini requests). Higher = faster, but more likely to hit Gemini rate limits.")]
        public int maxConcurrent = 2;

        /// <summary>
        /// User-nameable list of CSV groups. Each group drives an independent icon pipeline:
        /// its own CSV source, row filter, filename pattern, prompt template, and reference images.
        /// </summary>
        [Tooltip("List of CSV groups — each drives its own icon pipeline.")]
        public List<IconCsvGroup> groups = new();

        // ── Output / input paths (project-relative; edit here, never in source) ────

        /// <summary>Project-relative staging root where generated PSDs are written (per-group subfolder).</summary>
        [Tooltip("Project-relative folder where generated icon PSDs are written, under a per-group subfolder. Default: Assets/_Incoming")]
        public string incomingRoot = IconGenPaths.DefaultIncomingRoot;

        /// <summary>Project-relative root that holds per-group reference images.</summary>
        [Tooltip("Project-relative folder that holds per-group reference images. Default: Assets/Editor/IconReferenceImages")]
        public string referenceImagesRoot = IconGenPaths.DefaultReferenceImagesRoot;

        // ── Helpers ───────────────────────────────────────────────────────────────

        /// <summary>Returns the index of <see cref="aspectRatio"/> in <see cref="ASPECT_RATIO_OPTIONS"/>, or 0.</summary>
        public int AspectRatioIndex()
        {
            for (var i = 0; i < ASPECT_RATIO_OPTIONS.Length; i++)
            {
                if (ASPECT_RATIO_OPTIONS[i] == this.aspectRatio) return i;
            }
            return 0;
        }

        /// <summary>Returns the index of <see cref="imageSize"/> in <see cref="IMAGE_SIZE_OPTIONS"/>, or 1 ("1K").</summary>
        public int ImageSizeIndex()
        {
            for (var i = 0; i < IMAGE_SIZE_OPTIONS.Length; i++)
            {
                if (IMAGE_SIZE_OPTIONS[i] == this.imageSize) return i;
            }
            return 1; // default to "1K"
        }

        /// <summary>Returns the dropdown index for <see cref="maxConcurrent"/> (clamped 2–10).</summary>
        public int ConcurrencyIndex()
        {
            return Mathf.Clamp(this.maxConcurrent, 2, 10) - 2;
        }

        /// <summary>
        /// Applies the AR / size / concurrency dropdown selections, then marks the asset dirty and
        /// saves. No-ops if any index is out of range.
        /// </summary>
        public void ApplyDropdownSelection(int arIndex, int sizeIndex, int concurrencyIndex)
        {
            if (arIndex < 0 || arIndex >= ASPECT_RATIO_OPTIONS.Length) return;
            if (sizeIndex < 0 || sizeIndex >= IMAGE_SIZE_OPTIONS.Length) return;
            if (concurrencyIndex < 0 || concurrencyIndex >= CONCURRENCY_OPTIONS.Length) return;

            this.aspectRatio   = ASPECT_RATIO_OPTIONS[arIndex];
            this.imageSize     = IMAGE_SIZE_OPTIONS[sizeIndex];
            this.maxConcurrent = concurrencyIndex + 2;

            EditorUtility.SetDirty(this);
            AssetDatabase.SaveAssets();
        }
    }
}
