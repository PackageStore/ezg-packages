// Editor/CsvManager/CsvManagerWindow.Utils.cs
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Ezg.Package.CsvReader.Editor
{
    public partial class CsvManagerWindow
    {
        // NOTE (known debt): this window scans the host project's "Assets/_Project" tree
        // directly instead of the package's CsvPathUtility. It is intentionally
        // game-specific to the Merge Two project layout. In a project without an
        // "Assets/_Project" folder it simply lists nothing.
        private void RefreshAll()
        {
            _allEntries.Clear();
            _categories.Clear();

            string projectRoot = Path.Combine(Application.dataPath, "_Project");
            if (!Directory.Exists(projectRoot))
            {
                Repaint();
                return;
            }

            string[] csvFiles = Directory.GetFiles(projectRoot, "*.csv", SearchOption.AllDirectories);
            HashSet<string> seenCategories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (string fullPath in csvFiles)
            {
                // Convert absolute path to Unity asset path: "Assets/_Project/..."
                string assetPath = "Assets" + fullPath.Substring(Application.dataPath.Length).Replace('\\', '/');

                if (ShouldSkip(assetPath))
                    continue;

                CsvEntry entry = BuildEntry(assetPath);
                _allEntries.Add(entry);
                seenCategories.Add(entry.category);
            }

            List<string> sorted = new List<string>(seenCategories);
            sorted.Sort(StringComparer.OrdinalIgnoreCase);
            _categories.AddRange(sorted);

            // Keep selection valid; fall back to All if the category was removed
            if (_selectedCategory != null && !seenCategories.Contains(_selectedCategory))
                _selectedCategory = null;

            Repaint();
        }

        private static bool ShouldSkip(string assetPath)
            => assetPath.Contains("/LocalizationData/") || assetPath.Contains("/Visuals/");

        private static CsvEntry BuildEntry(string assetPath)
        {
            const string ROOT = "Assets/_Project/";
            string relative = assetPath.StartsWith(ROOT, StringComparison.OrdinalIgnoreCase)
                ? assetPath.Substring(ROOT.Length)
                : assetPath;

            string[] parts = relative.Replace('\\', '/').Split('/');

            return new CsvEntry
            {
                assetPath   = assetPath,
                name        = Path.GetFileNameWithoutExtension(assetPath),
                category    = DeriveCategory(parts),
                displayPath = relative
            };
        }

        // Path rules:
        //   Features/UI/<Name>/...          → <Name>
        //   Features/Systems/<Name>/...     → <Name>
        //   Features/Systems/CsvConfig/...  → "Systems"   (no feature subfolder)
        //   Core/...                        → "Core"
        //   else                            → "Other"
        private static string DeriveCategory(string[] parts)
        {
            if (parts.Length < 1) return "Other";

            if (string.Equals(parts[0], "Features", StringComparison.OrdinalIgnoreCase))
            {
                if (parts.Length >= 3)
                {
                    // parts[2] is "CsvConfig" when there is no feature subfolder under Systems
                    if (string.Equals(parts[2], "CsvConfig", StringComparison.OrdinalIgnoreCase) && parts.Length >= 2)
                        return parts[1]; // "Systems" or "UI"
                    return parts[2];    // Feature name
                }
                return parts.Length >= 2 ? parts[1] : "Features";
            }

            if (string.Equals(parts[0], "Core", StringComparison.OrdinalIgnoreCase))
                return "Core";

            return "Other";
        }
    }
}
#endif
