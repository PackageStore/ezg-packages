#nullable enable

namespace Ezg.IconCsvGenerator.Editor
{
    using System;
    using UnityEditor;
    using UnityEngine;

    /// <summary>
    /// Forces crisp, uncompressed "Editor GUI" import settings on the lucide button-icon PNGs
    /// (see <see cref="LucideButtonIcons.IconsFolder"/>) so they render sharp in the toolbar
    /// without anyone configuring each texture by hand. Scoped strictly to that folder — it
    /// never touches game art.
    /// </summary>
    internal sealed class ButtonIconPostprocessor : AssetPostprocessor
    {
        private void OnPreprocessTexture()
        {
            if (!this.assetPath.StartsWith(LucideButtonIcons.IconsFolder + "/", StringComparison.Ordinal))
                return;

            var importer = (TextureImporter)this.assetImporter;
            importer.textureType         = TextureImporterType.GUI;       // "Editor GUI and Legacy GUI"
            importer.mipmapEnabled       = false;
            importer.alphaIsTransparency = true;
            importer.npotScale           = TextureImporterNPOTScale.None;
            importer.wrapMode            = TextureWrapMode.Clamp;
            importer.filterMode          = FilterMode.Bilinear;
            importer.textureCompression  = TextureImporterCompression.Uncompressed;
            importer.maxTextureSize      = 12;
        }
    }
}
