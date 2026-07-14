using Ezg.ProceduralAnimation;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Ezg.ProceduralAnimation.Editor
{
    public static class PosePreviewCaptureUtility
    {
        private const int PreviewSize = 256;

        public static Texture2D CaptureSceneViewPreview()
        {
            SceneView sceneView = SceneView.lastActiveSceneView;
            if (sceneView == null || sceneView.camera == null)
            {
                throw new System.InvalidOperationException("No active Scene View camera is available for preview capture.");
            }

            Camera camera = sceneView.camera;
            RenderTexture renderTexture = null;
            RenderTexture previousActive = RenderTexture.active;
            RenderTexture previousTarget = camera.targetTexture;

            try
            {
                renderTexture = new RenderTexture(PreviewSize, PreviewSize, 24, RenderTextureFormat.ARGB32)
                {
                    antiAliasing = 4
                };

                camera.targetTexture = renderTexture;
                camera.Render();

                RenderTexture.active = renderTexture;
                Texture2D captured = new Texture2D(PreviewSize, PreviewSize, TextureFormat.RGBA32, false);
                captured.ReadPixels(new Rect(0, 0, PreviewSize, PreviewSize), 0, 0);
                captured.Apply();
                return captured;
            }
            finally
            {
                RenderTexture.active = previousActive;
                camera.targetTexture = previousTarget;

                if (renderTexture != null)
                {
                    Object.DestroyImmediate(renderTexture);
                }
            }
        }

        public static Texture2D LoadPreviewForPose(PoseAsset pose)
        {
            if (!TryGetDefaultPreviewPath(pose, out string assetPath))
            {
                return null;
            }

            return AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
        }

        public static bool PreviewExistsForPose(PoseAsset pose, out string assetPath)
        {
            if (!TryGetDefaultPreviewPath(pose, out assetPath))
            {
                return false;
            }

            return AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath) != null;
        }

        public static Texture2D SavePreviewTextureForPose(Texture2D sourceTexture, PoseAsset pose, bool overwriteExisting)
        {
            if (!TryGetDefaultPreviewPath(pose, out string assetPath))
            {
                return null;
            }

            return SavePreviewTexture(sourceTexture, assetPath, overwriteExisting);
        }

        public static Texture2D SavePreviewTexture(Texture2D sourceTexture, string assetPath, bool overwriteExisting)
        {
            if (sourceTexture == null || string.IsNullOrEmpty(assetPath))
            {
                return null;
            }

            if (!overwriteExisting && AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath) != null)
            {
                return null;
            }

            string folder = Path.GetDirectoryName(assetPath)?.Replace("\\", "/");
            if (string.IsNullOrEmpty(folder) || !folder.StartsWith("Assets"))
            {
                return null;
            }

            AnimationClipWriter.EnsureAssetFolder(folder);

            Texture2D saved = new Texture2D(sourceTexture.width, sourceTexture.height, TextureFormat.RGBA32, false);
            saved.SetPixels(sourceTexture.GetPixels());
            saved.Apply();

            File.WriteAllBytes(assetPath, saved.EncodeToPNG());
            Object.DestroyImmediate(saved);

            AssetDatabase.ImportAsset(assetPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            return AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
        }

        public static string BuildDefaultPreviewPath(PoseAsset pose)
        {
            return TryGetDefaultPreviewPath(pose, out string assetPath) ? assetPath : string.Empty;
        }

        private static bool TryGetDefaultPreviewPath(PoseAsset pose, out string assetPath)
        {
            assetPath = string.Empty;
            if (pose == null)
            {
                return false;
            }

            string posePath = AssetDatabase.GetAssetPath(pose);
            if (string.IsNullOrEmpty(posePath))
            {
                return false;
            }

            string folder = Path.GetDirectoryName(posePath)?.Replace("\\", "/");
            string fileName = Path.GetFileNameWithoutExtension(posePath);
            if (string.IsNullOrEmpty(folder) || string.IsNullOrEmpty(fileName))
            {
                return false;
            }

            assetPath = $"{folder}/{MakeSafeFileName(fileName)}.png";
            return true;
        }

        private static string MakeSafeFileName(string name)
        {
            foreach (char invalid in System.IO.Path.GetInvalidFileNameChars())
            {
                name = name.Replace(invalid, '_');
            }

            return name.Trim();
        }
    }
}
