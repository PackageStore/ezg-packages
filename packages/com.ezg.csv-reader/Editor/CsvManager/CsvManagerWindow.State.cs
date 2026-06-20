// Editor/CsvManager/CsvManagerWindow.State.cs
#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace Ezg.Package.CsvReader.Editor
{
    public partial class CsvManagerWindow
    {
        private struct CsvEntry
        {
            public string assetPath;   // "Assets/_Project/Features/UI/GamePlay/CsvConfig/ItemMerge.csv"
            public string name;        // "ItemMerge"
            public string category;    // "GamePlay"
            public string displayPath; // "Features/UI/GamePlay/CsvConfig/ItemMerge.csv"
        }

        private readonly List<CsvEntry> _allEntries  = new List<CsvEntry>();
        private readonly List<string>   _categories  = new List<string>();

        private string  _selectedCategory = null; // null = All
        private string  _searchQuery      = "";
        private Vector2 _leftScroll;
        private Vector2 _rightScroll;
    }
}
#endif
