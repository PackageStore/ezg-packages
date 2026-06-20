// Editor/CsvManager/CsvManagerWindow.Styles.cs
#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Ezg.Package.CsvReader.Editor
{
    public partial class CsvManagerWindow
    {
        private bool _stylesReady;
        private bool _stylesProSkin;

        private GUIStyle _panelStyle;
        private GUIStyle _cardStyle;
        private GUIStyle _sectionTitleStyle;
        private GUIStyle _mutedMiniStyle;
        private GUIStyle _toolbarSearchStyle;

        private Color _windowBgColor;
        private Color _surfaceColor;
        private Color _surfaceAltColor;
        private Color _surfaceHoverColor;
        private Color _borderColor;
        private Color _borderSoftColor;
        private Color _accentColor;
        private Color _accentSoftColor;
        private Color _primaryTextColor;
        private Color _mutedTextColor;

        private void EnsureStyles()
        {
            bool proSkin = EditorGUIUtility.isProSkin;
            if (_stylesReady && _stylesProSkin == proSkin) return;

            EditorBaseStyle.Theme  theme  = EditorBaseStyle.Get(proSkin);
            EditorBaseStyle.Palette colors = theme.Colors;
            EditorBaseStyle.Styles  styles = theme.Styles;

            _stylesReady   = true;
            _stylesProSkin = proSkin;

            _windowBgColor   = colors.WindowBgColor;
            _surfaceColor    = colors.SurfaceColor;
            _surfaceAltColor = colors.SurfaceAltColor;
            _surfaceHoverColor = colors.SurfaceHoverColor;
            _borderColor     = colors.BorderColor;
            _borderSoftColor = colors.BorderSoftColor;
            _accentColor     = colors.AccentColor;
            _accentSoftColor = colors.AccentSoftColor;
            _primaryTextColor = colors.PrimaryTextColor;
            _mutedTextColor  = colors.MutedTextColor;

            _panelStyle       = new GUIStyle(styles.PanelStyle);
            _cardStyle        = new GUIStyle(styles.CardStyle);
            _sectionTitleStyle = new GUIStyle(styles.SectionTitleStyle);
            _mutedMiniStyle   = new GUIStyle(styles.MutedMiniStyle);
            _toolbarSearchStyle = new GUIStyle(styles.ToolbarSearchStyle);
        }

        private string DrawSearchField(string value, string hint, params GUILayoutOption[] options)
            => EditorBaseStyle.DrawSearchField(value, hint, _toolbarSearchStyle, _mutedMiniStyle, options);

        private void DrawCardHeader(string title, string subtitle = null)
            => EditorBaseStyle.DrawCardHeader(title, subtitle);

        private void DrawChip(Rect rect, string text, Color? color = null)
            => EditorBaseStyle.DrawChip(rect, text, color);

        private void DrawPanelBackground(Rect rect)
            => EditorBaseStyle.DrawPanelBackground(rect);

        private void DrawRectBorder(Rect rect, Color color, float width)
            => EditorBaseStyle.DrawRectBorder(rect, color, width);

        private void DrawCategoryCardBackground(Rect rect, bool isSelected, bool isHovered, bool isAlt)
        {
            Color idleColor = _stylesProSkin
                ? new Color(0.18f, 0.19f, 0.22f, 1f)
                : new Color(0.97f, 0.97f, 0.99f, 1f);
            if (isAlt)
            {
                idleColor = _stylesProSkin
                    ? new Color(0.22f, 0.23f, 0.27f, 1f)
                    : new Color(0.91f, 0.93f, 0.96f, 1f);
            }

            Color selectedColor = Color.Lerp(_accentColor, _surfaceColor, _stylesProSkin ? 0.58f : 0.38f);
            Color bg = isSelected ? selectedColor : (isHovered ? _surfaceHoverColor : idleColor);

            if (isSelected || isHovered)
                EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 2f, rect.width, 3f),
                    new Color(0f, 0f, 0f, _stylesProSkin ? 0.22f : 0.08f));

            EditorGUI.DrawRect(rect, bg);

            float stripW = isSelected ? 5f : 3f;
            Rect strip = new Rect(rect.x, rect.y, stripW, rect.height);
            Color stripColor = isSelected
                ? _accentColor
                : (isHovered ? Color.Lerp(_borderColor, _accentColor, 0.55f) : _borderColor);
            EditorGUI.DrawRect(strip, stripColor);

            if (isSelected)
                EditorGUI.DrawRect(
                    new Rect(strip.x, strip.y, strip.width, strip.height * 0.35f),
                    Color.Lerp(_accentColor, Color.white, 0.32f));

            DrawRectBorder(rect, isSelected ? _accentColor : _borderSoftColor, isSelected ? 2f : 1f);
        }
    }
}
#endif
