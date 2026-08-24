#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

namespace EZG.TechnicalArt.VisualRoadBuilder
{
    /// <summary>Cài skill Claude Code ship kèm package (<c>ClaudeSkill~/</c>) vào
    /// <c>&lt;project&gt;/.claude/skills/</c>. Claude Code chỉ quét <c>.claude/skills/</c> của project và
    /// <c>~/.claude/skills/</c>, KHÔNG quét <c>Packages/</c> hay <c>Library/PackageCache/</c>, nên phải copy
    /// ra ngoài. Folder nguồn có đuôi <c>~</c> nên Unity không import — đọc bằng <c>System.IO</c> trên
    /// <see cref="UnityEditor.PackageManager.PackageInfo.resolvedPath"/>, không qua AssetDatabase.</summary>
    internal static class ClaudeSkillInstaller
    {
        private const string MenuPath = "Tools/EZG Technical Art/Install Claude Skill";
        private const string SourceFolder = "ClaudeSkill~";
        private const string SkillName = "VisualRoadBuilder";

        [MenuItem(MenuPath)]
        private static void Install()
        {
            string source = ResolveSourcePath();
            if (source == null)
            {
                Debug.LogError($"[VisualRoadBuilder] Không thấy {SourceFolder}/{SkillName} trong package. "
                               + "Chạy menu này từ bản cài qua Package Manager.");
                return;
            }

            string dest = Path.Combine(
                Directory.GetParent(Application.dataPath).FullName, ".claude", "skills", SkillName);

            if (Directory.Exists(dest) && !EditorUtility.DisplayDialog(
                    "Install Claude Skill",
                    $"{dest}\n\nđã tồn tại. Ghi đè bằng bản trong package?\n"
                    + "Mọi thay đổi tay trong folder đó sẽ mất.",
                    "Ghi đè", "Huỷ"))
                return;

            if (Directory.Exists(dest)) Directory.Delete(dest, true);
            CopyTree(new DirectoryInfo(source), new DirectoryInfo(dest));

            Debug.Log($"[VisualRoadBuilder] Đã cài skill vào {dest}. "
                      + $"Mở lại session Claude Code để /{SkillName} xuất hiện.");
            EditorUtility.RevealInFinder(dest);
        }

        private static string ResolveSourcePath()
        {
            var pkg = UnityEditor.PackageManager.PackageInfo.FindForAssembly(
                typeof(ClaudeSkillInstaller).Assembly);
            if (pkg == null || string.IsNullOrEmpty(pkg.resolvedPath)) return null;
            string path = Path.Combine(pkg.resolvedPath, SourceFolder, SkillName);
            return Directory.Exists(path) ? path : null;
        }

        private static void CopyTree(DirectoryInfo from, DirectoryInfo to)
        {
            to.Create();
            foreach (FileInfo file in from.GetFiles())
                file.CopyTo(Path.Combine(to.FullName, file.Name), true);
            foreach (DirectoryInfo dir in from.GetDirectories())
                CopyTree(dir, new DirectoryInfo(Path.Combine(to.FullName, dir.Name)));
        }
    }
}
#endif
