#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using UnityFigmaBridge.Editor.Utils;
using UnityFigmaBridge.Editor.PostProcess;

namespace Ezg.FigmaFeatureImporter.Editor
{
    /// <summary>Bước 6 và 8 (§5.2): FigmaImage → Image (fallback bridge 0.2.x), sprite rời khỏi Assets/Figma.</summary>
    public static partial class FigmaFeatureImporter
    {

        /// <summary>hash MD5 nội dung PNG → đường dẫn asset trong Features (tái dùng ảnh trùng byte).</summary>
        private static Dictionary<string, string> s_FeaturePngByHash;

        /// <summary>
        ///     Bước 6: bridge 0.2.x gắn FigmaImage (shader riêng) cho mọi fill. Thay bằng Image thuần
        ///     đọc sprite + màu qua SerializedObject (không tham chiếu asm runtime của bridge). Node
        ///     chỉ có viền/bo góc mà không sprite → `needsPngExport`. Với bridge 0.3.0 PlainImages đã
        ///     làm việc này; danh sách shape-only lấy từ sidecar.
        /// </summary>
        private const int FIGMA_IMAGE_SCALE_MODE_TILE = 3;

        private static void ConvertFigmaImages(Ctx ctx)
        {
            foreach (var t in ctx.Body.GetComponentsInChildren<Transform>(true))
            {
                if (IsInsideTemplate(ctx, t)) continue;
                foreach (var component in t.GetComponents<Component>())
                {
                    if (component == null || component.GetType().Name != "FigmaImage") continue;

                    var so = new SerializedObject(component);
                    var sprite = so.FindProperty("m_Sprite")?.objectReferenceValue as Sprite;
                    var fillColor = so.FindProperty("m_FillColor")?.colorValue ?? Color.white;
                    var strokeWidth = so.FindProperty("m_StrokeWidth")?.floatValue ?? 0f;
                    var cornerRadius = so.FindProperty("m_CornerRadius")?.vector4Value ?? Vector4.zero;
                    var raycast = so.FindProperty("m_RaycastTarget")?.boolValue ?? true;
                    // FigmaImage.ImageScaleMode: Fill=0, Fit=1, Stretch=2, Tile=3 (fill TILE hoặc PATTERN của Figma)
                    var isTiled = (so.FindProperty("m_ImageScaleMode")?.enumValueIndex ?? 0) == FIGMA_IMAGE_SCALE_MODE_TILE;
                    var tileScale = so.FindProperty("m_ImageScaleFactor")?.floatValue ?? 1f;
                    var wasEnabled = component is Behaviour behaviour && behaviour.enabled;

                    var go = component.gameObject;
                    DestroyNow(component);
                    var image = go.GetComponent<Image>();
                    if (image == null) image = go.AddComponent<Image>();
                    image.sprite = sprite;
                    image.color = fillColor;
                    image.raycastTarget = raycast;
                    image.enabled = wasEnabled;
                    if (sprite != null && isTiled)
                    {
                        // FigmaImage vẽ tile = sprite × factor; Image.Tiled vẽ tile = sprite / multiplier
                        image.type = Image.Type.Tiled;
                        image.pixelsPerUnitMultiplier = tileScale > 0f ? 1f / tileScale : 1f;
                    }
                    else image.type = sprite != null && sprite.border != Vector4.zero ? Image.Type.Sliced : Image.Type.Simple;

                    if (sprite == null && (strokeWidth > 0f || cornerRadius != Vector4.zero))
                        ctx.Report.needsPngExport.Add(PathWithin(ctx.Body, t));
                    break;
                }
            }

            var sidecar = FigmaInstanceSidecar.Read(ctx.RawPrefabPath);
            if (sidecar?.shapeOnlyNodes != null)
            {
                foreach (var node in sidecar.shapeOnlyNodes)
                {
                    if (node.HasSprite) continue; // chỉ mất bo góc/viền trên ảnh thật — vẫn có hình
                    var path = node.HierarchyPath;
                    // Node nằm trong instance đã thay bằng template project (hoặc đã drop) thì không còn
                    if (FindByHierarchyPath(ctx.Body, path) == null) continue;
                    if (!ctx.Report.needsPngExport.Contains(path)) ctx.Report.needsPngExport.Add(path);
                }
            }
        }

        /// <summary>
        ///     Bước 8: mọi sprite còn trỏ vào output thô của bridge → ảnh trùng byte đã có trong
        ///     Features thì tái dùng, không thì copy vào `spriteFolder` của màn và set import (Sprite,
        ///     không mipmap, border chép từ nguồn, FullRect khi có border). Màn thật không tham chiếu
        ///     Assets/Figma.
        /// </summary>
        private static void RelocateSprites(Ctx ctx)
        {
            var figmaRoot = FigmaPaths.FigmaAssetsRootFolder.Replace('\\', '/').TrimEnd('/') + "/";
            var resolved = new Dictionary<string, Sprite>();

            foreach (var image in ctx.Body.GetComponentsInChildren<Image>(true))
            {
                if (image.sprite == null) continue;
                var sourcePath = AssetDatabase.GetAssetPath(image.sprite);
                if (string.IsNullOrEmpty(sourcePath) || !sourcePath.StartsWith(figmaRoot, StringComparison.Ordinal)) continue;

                if (!resolved.TryGetValue(sourcePath, out var replacement))
                {
                    replacement = ResolveProjectSprite(ctx, sourcePath);
                    resolved[sourcePath] = replacement;
                }

                if (replacement != null) image.sprite = replacement;
                else ctx.Report.Warn($"Không chuyển được sprite '{sourcePath}' khỏi Assets/Figma.");
            }
        }

        private static Sprite ResolveProjectSprite(Ctx ctx, string sourcePath)
        {
            var hash = FileHash(sourcePath);
            if (hash == null) return null;

            EnsureFeatureHashCache(ctx.Settings.spriteReuseRoot);
            if (s_FeaturePngByHash.TryGetValue(hash, out var existingPath))
            {
                var existing = AssetDatabase.LoadAssetAtPath<Sprite>(existingPath);
                if (existing != null)
                {
                    ctx.Report.spritesReused++;
                    return existing;
                }
            }

            var folder = ctx.Entry.spriteFolder;
            if (string.IsNullOrWhiteSpace(folder))
            {
                ctx.Report.Warn("spriteFolder trống — sprite vẫn trỏ Assets/Figma.");
                return null;
            }
            folder = folder.Replace('\\', '/').TrimEnd('/');
            Directory.CreateDirectory(folder);

            var fileName = Path.GetFileName(sourcePath);
            var destination = $"{folder}/{fileName}";
            if (File.Exists(destination) && FileHash(destination) != hash)
                destination = $"{folder}/{Path.GetFileNameWithoutExtension(fileName)}_{hash.Substring(0, 6)}{Path.GetExtension(fileName)}";

            if (!File.Exists(destination))
            {
                File.Copy(sourcePath, destination);
                AssetDatabase.ImportAsset(destination, ImportAssetOptions.ForceSynchronousImport);
                ctx.Report.spritesCopied++;
            }

            ConfigureSpriteImporter(destination, ReadBorder(sourcePath), ReadWrapMode(sourcePath));
            s_FeaturePngByHash[hash] = destination;
            return AssetDatabase.LoadAssetAtPath<Sprite>(destination);
        }

        /// <summary>Wrap mode của nguồn: bridge đặt Repeat cho fill TILE / PATTERN (Image.Tiled cần Repeat), còn lại Clamp.</summary>
        private static TextureWrapMode ReadWrapMode(string path)
        {
            if (AssetImporter.GetAtPath(path) is not TextureImporter importer) return TextureWrapMode.Clamp;
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            return settings.wrapMode == TextureWrapMode.Repeat ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
        }

        private static Vector4 ReadBorder(string path)
        {
            if (AssetImporter.GetAtPath(path) is not TextureImporter importer) return Vector4.zero;
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            return settings.spriteBorder;
        }

        /// <summary>
        ///     Set qua TextureImporterSettings (không trộn với property trực tiếp — SetTextureSettings ghi
        ///     đè bằng giá trị đã đọc). Idempotent: không reimport khi đã đúng.
        /// </summary>
        private static void ConfigureSpriteImporter(string path, Vector4 border, TextureWrapMode wrapMode)
        {
            if (AssetImporter.GetAtPath(path) is not TextureImporter importer) return;
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);

            var meshType = border != Vector4.zero ? SpriteMeshType.FullRect : settings.spriteMeshType;
            var wanted = settings.textureType != TextureImporterType.Sprite
                         || settings.spriteMode != (int)SpriteImportMode.Single
                         || settings.spriteBorder != border
                         || settings.spriteMeshType != meshType
                         || !settings.alphaIsTransparency
                         || settings.mipmapEnabled
                         || settings.wrapMode != wrapMode;
            if (!wanted) return;

            settings.textureType = TextureImporterType.Sprite;
            settings.spriteMode = (int)SpriteImportMode.Single;
            settings.npotScale = TextureImporterNPOTScale.None; // sprite không cho ToNearest — Unity reset + warn
            settings.spriteBorder = border;
            settings.spriteMeshType = meshType;
            settings.alphaIsTransparency = true;
            settings.mipmapEnabled = false;
            settings.wrapMode = wrapMode;
            importer.SetTextureSettings(settings);
            importer.SaveAndReimport();
        }

        private static void EnsureFeatureHashCache(string reuseRoot)
        {
            if (s_FeaturePngByHash != null) return;
            s_FeaturePngByHash = new Dictionary<string, string>();
            if (string.IsNullOrWhiteSpace(reuseRoot) || !Directory.Exists(reuseRoot)) return;
            foreach (var file in Directory.GetFiles(reuseRoot, "*.png", SearchOption.AllDirectories))
            {
                var path = file.Replace('\\', '/');
                var hash = FileHash(path);
                if (hash != null && !s_FeaturePngByHash.ContainsKey(hash)) s_FeaturePngByHash[hash] = path;
            }
        }

        /// <summary>Quên cache hash (gọi khi Features đổi nhiều ảnh trong cùng phiên).</summary>
        internal static void ResetSpriteCache() => s_FeaturePngByHash = null;

        private static string FileHash(string path)
        {
            try
            {
                using var md5 = MD5.Create();
                using var stream = File.OpenRead(path);
                return BitConverter.ToString(md5.ComputeHash(stream)).Replace("-", string.Empty).ToLowerInvariant();
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
#endif
