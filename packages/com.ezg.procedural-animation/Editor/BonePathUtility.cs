using UnityEngine;

namespace Ezg.ProceduralAnimation.Editor
{
    public static class BonePathUtility
    {
        public const string RootPath = "";

        public static string GetRelativePath(Transform root, Transform target)
        {
            if (root == null || target == null)
            {
                return string.Empty;
            }

            if (root == target)
            {
                return RootPath;
            }

            string path = target.name;
            Transform current = target.parent;

            while (current != null && current != root)
            {
                path = current.name + "/" + path;
                current = current.parent;
            }

            return current == root ? path : string.Empty;
        }

        public static int GetDepth(string bonePath)
        {
            if (string.IsNullOrEmpty(bonePath))
            {
                return 0;
            }

            int depth = 0;
            for (int i = 0; i < bonePath.Length; i++)
            {
                if (bonePath[i] == '/')
                {
                    depth++;
                }
            }

            return depth + 1;
        }

        public static bool IsAssetFolderPath(string path)
        {
            return !string.IsNullOrWhiteSpace(path) && path.StartsWith("Assets");
        }
    }
}
