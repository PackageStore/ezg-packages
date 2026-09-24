using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using UnityFigmaBridge.Editor.FigmaApi;
using UnityFigmaBridge.Editor.Settings;
using UnityFigmaBridge.Editor.Utils;

namespace UnityFigmaBridge.Editor.Nodes
{
    /// <summary>
    /// A frame with Figma "Clip content" clips every child to its bounds and corner radius. The UGUI
    /// equivalent sits on the same GameObject: <see cref="RectMask2D"/> for square corners, otherwise a
    /// <see cref="Mask"/> whose graphic is a generated 9-sliced rounded rectangle.
    /// </summary>
    public static class ClipContentMask
    {
        private const float TexelsPerUnit = 2f;
        private const float ReferencePixelsPerUnit = 100f;
        private const int CenterTexels = 2;
        private const string MaskFolderName = "Masks";

        /// <param name="isScreenRoot">
        ///     Screen frames clip by default in Figma; in Unity the screen is stretched to the device,
        ///     so a clip there would cut art that bleeds past the design width.
        /// </param>
        public static void Apply(GameObject nodeGameObject, Node node, FigmaImportProcessData figmaImportProcessData,
            bool isScreenRoot)
        {
            if (!figmaImportProcessData.Settings.ClipContentAsMask) return;
            if (!node.clipsContent || node.isMask || isScreenRoot) return;
            if (node.type != NodeType.FRAME && node.type != NodeType.COMPONENT && node.type != NodeType.INSTANCE) return;
            if (node.overflowDirection != Node.OverflowDirection.NONE) return; // scroll frames get their RectMask2D from the layout pass

            var radii = CornerRadii(node);
            if (radii == Vector4.zero)
            {
                RemoveComponent<Mask>(nodeGameObject);
                UnityUiUtils.GetOrAddComponent<RectMask2D>(nodeGameObject);
                return;
            }

            RemoveComponent<RectMask2D>(nodeGameObject);
            var image = UnityUiUtils.GetOrAddComponent<Image>(nodeGameObject);
            var hasVisibleFill = node.fills != null && System.Array.Exists(node.fills, f => f != null && f.visible);
            var hasBitmapFill = node.fills != null && System.Array.Exists(node.fills, f => f != null && f.visible &&
                (f.type == Paint.PaintType.IMAGE || f.type == Paint.PaintType.PATTERN));
            if (hasBitmapFill)
            {
                Debug.LogWarning($"[ClipContentMask] '{node.name}' clips its content and has an image fill: the fill's alpha is the mask, its corner radius is not applied.", nodeGameObject);
            }
            else
            {
                image.sprite = RoundedRectSprite(radii, figmaImportProcessData.Settings);
                image.type = Image.Type.Sliced;
                image.pixelsPerUnitMultiplier = 1f;
                if (!hasVisibleFill)
                {
                    image.color = UnityEngine.Color.white;
                    image.raycastTarget = false;
                }
            }
            // A disabled graphic turns the mask off, so it stays enabled and is hidden through the Mask.
            image.enabled = true;
            var mask = UnityUiUtils.GetOrAddComponent<Mask>(nodeGameObject);
            mask.showMaskGraphic = hasVisibleFill;
        }

        /// <summary>Radii as (top left, top right, bottom right, bottom left), clamped to half the short side as Figma does.</summary>
        private static Vector4 CornerRadii(Node node)
        {
            var radii = node.rectangleCornerRadii != null && node.rectangleCornerRadii.Length == 4
                ? new Vector4(node.rectangleCornerRadii[0], node.rectangleCornerRadii[1], node.rectangleCornerRadii[2], node.rectangleCornerRadii[3])
                : Vector4.one * Mathf.Max(0f, node.cornerRadius);
            var limit = node.size != null ? Mathf.Min(node.size.x, node.size.y) * 0.5f : float.MaxValue;
            for (var i = 0; i < 4; i++) radii[i] = Mathf.Round(Mathf.Clamp(radii[i], 0f, limit));
            return radii;
        }

        private static Sprite RoundedRectSprite(Vector4 radii, UnityFigmaBridgeSettings bridgeSettings)
        {
            var name = radii.x == radii.y && radii.y == radii.z && radii.z == radii.w
                ? $"Mask-R{Format(radii.x)}"
                : $"Mask-R{Format(radii.x)}-{Format(radii.y)}-{Format(radii.z)}-{Format(radii.w)}";
            var folder = $"{FigmaPaths.FigmaImageFillFolder}/{MaskFolderName}";
            var path = $"{folder}/{name}.png";

            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite != null) return sprite;

            Directory.CreateDirectory(folder);
            var left = Mathf.CeilToInt(Mathf.Max(radii.x, radii.w) * TexelsPerUnit);
            var right = Mathf.CeilToInt(Mathf.Max(radii.y, radii.z) * TexelsPerUnit);
            var top = Mathf.CeilToInt(Mathf.Max(radii.x, radii.y) * TexelsPerUnit);
            var bottom = Mathf.CeilToInt(Mathf.Max(radii.w, radii.z) * TexelsPerUnit);
            File.WriteAllBytes(path, RoundedRectPng(left + right + CenterTexels, top + bottom + CenterTexels, radii * TexelsPerUnit));
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

            if (AssetImporter.GetAtPath(path) is TextureImporter importer)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.wrapMode = TextureWrapMode.Clamp;
                var settings = new TextureImporterSettings();
                importer.ReadTextureSettings(settings);
                settings.spriteMeshType = SpriteMeshType.FullRect;
                settings.spriteBorder = new Vector4(left, bottom, right, top);
                settings.spritePixelsPerUnit = ReferencePixelsPerUnit * TexelsPerUnit;
                importer.SetTextureSettings(settings);
                SpritePlatformOverride.Apply(importer, bridgeSettings);
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        /// <summary>White rounded rectangle filling the texture, alpha anti-aliased from a signed distance.</summary>
        private static byte[] RoundedRectPng(int width, int height, Vector4 radii)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            var pixels = new Color32[width * height];
            var halfSize = new Vector2(width, height) * 0.5f;
            for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                // Texture rows run bottom up; radii are top left, top right, bottom right, bottom left
                var point = new Vector2(x + 0.5f, y + 0.5f) - halfSize;
                var radius = point.y > 0f ? (point.x < 0f ? radii.x : radii.y) : (point.x < 0f ? radii.w : radii.z);
                var q = new Vector2(Mathf.Abs(point.x), Mathf.Abs(point.y)) - halfSize + Vector2.one * radius;
                var distance = new Vector2(Mathf.Max(q.x, 0f), Mathf.Max(q.y, 0f)).magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0f) - radius;
                var alpha = (byte)Mathf.RoundToInt(Mathf.Clamp01(0.5f - distance) * 255f);
                pixels[y * width + x] = new Color32(255, 255, 255, alpha);
            }
            texture.SetPixels32(pixels);
            texture.Apply();
            var png = texture.EncodeToPNG();
            Object.DestroyImmediate(texture);
            return png;
        }

        private static string Format(float value) => value.ToString("0.##", CultureInfo.InvariantCulture);

        private static void RemoveComponent<T>(GameObject gameObject) where T : UnityEngine.Component
        {
            var component = gameObject.GetComponent<T>();
            if (component != null) Object.DestroyImmediate(component);
        }
    }
}
