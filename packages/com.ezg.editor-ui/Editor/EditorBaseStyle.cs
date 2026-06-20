#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

public static partial class EditorBaseStyle
{
    public sealed class Palette
    {
        public Color WindowBgColor;
        public Color SurfaceColor;
        public Color SurfaceAltColor;
        public Color SurfaceHoverColor;
        public Color SurfaceSunkenColor;
        public Color BorderColor;
        public Color BorderSoftColor;
        public Color HighlightColor;
        public Color AccentColor;
        public Color AccentSoftColor;
        public Color DangerColor;
        public Color DangerSoftColor;
        public Color SuccessColor;
        public Color SuccessSoftColor;
        public Color WarningColor;
        public Color WarningSoftColor;
        public Color MutedTextColor;
        public Color PrimaryTextColor;
    }

    public sealed class Styles
    {
        public GUIStyle PanelStyle;
        public GUIStyle CardStyle;
        public GUIStyle RecipeCardStyle;
        public GUIStyle CardBodyStyle;
        public GUIStyle SectionTitleStyle;
        public GUIStyle SectionSubtitleStyle;
        public GUIStyle SummaryValueStyle;
        public GUIStyle SummaryLabelStyle;
        public GUIStyle SummaryInlineValueStyle;
        public GUIStyle ChipStyle;
        public GUIStyle ToolbarSearchStyle;
        public GUIStyle DetailFoldoutStyle;
        public GUIStyle MutedMiniStyle;
    }

    public sealed class Theme
    {
        public bool ProSkin;
        public Palette Colors;
        public Styles Styles;
    }

    private static Theme _proTheme;
    private static Theme _personalTheme;

    public static Theme Current
    {
        get { return Get(EditorGUIUtility.isProSkin); }
    }

    public static Theme Get(bool proSkin)
    {
        if (proSkin)
        {
            if (_proTheme == null)
                _proTheme = BuildTheme(true);
            return _proTheme;
        }

        if (_personalTheme == null)
            _personalTheme = BuildTheme(false);
        return _personalTheme;
    }

    public static bool IsCloseTo(Color a, Color b)
    {
        return Mathf.Abs(a.r - b.r) < 0.01f &&
               Mathf.Abs(a.g - b.g) < 0.01f &&
               Mathf.Abs(a.b - b.b) < 0.01f;
    }

    private static Theme BuildTheme(bool proSkin)
    {
        Palette palette = CreatePalette(proSkin);
        Styles styles = CreateStyles(palette);

        return new Theme
        {
            ProSkin = proSkin,
            Colors = palette,
            Styles = styles
        };
    }

    private static Palette CreatePalette(bool proSkin)
    {
        Palette palette = new Palette();

        if (proSkin)
        {
            palette.WindowBgColor = new Color(0.11f, 0.12f, 0.14f, 1f);
            palette.SurfaceColor = new Color(0.16f, 0.17f, 0.19f, 1f);
            palette.SurfaceAltColor = new Color(0.20f, 0.22f, 0.25f, 1f);
            palette.SurfaceHoverColor = new Color(0.24f, 0.27f, 0.31f, 1f);
            palette.SurfaceSunkenColor = new Color(0.12f, 0.13f, 0.15f, 1f);
            palette.BorderColor = new Color(0.32f, 0.35f, 0.40f, 1f);
            palette.BorderSoftColor = new Color(0.24f, 0.27f, 0.31f, 1f);
            palette.HighlightColor = new Color(1f, 1f, 1f, 0.04f);
            palette.AccentColor = new Color(0.36f, 0.66f, 0.98f, 1f);
            palette.AccentSoftColor = new Color(0.22f, 0.38f, 0.62f, 0.55f);
            palette.DangerColor = new Color(0.88f, 0.42f, 0.42f, 1f);
            palette.DangerSoftColor = new Color(0.55f, 0.24f, 0.26f, 0.55f);
            palette.SuccessColor = new Color(0.40f, 0.78f, 0.50f, 1f);
            palette.SuccessSoftColor = new Color(0.20f, 0.42f, 0.28f, 0.55f);
            palette.WarningColor = new Color(0.96f, 0.72f, 0.32f, 1f);
            palette.WarningSoftColor = new Color(0.48f, 0.35f, 0.16f, 0.60f);
            palette.MutedTextColor = new Color(0.72f, 0.76f, 0.82f, 1f);
            palette.PrimaryTextColor = new Color(0.94f, 0.96f, 0.99f, 1f);
        }
        else
        {
            palette.WindowBgColor = new Color(0.92f, 0.94f, 0.97f, 1f);
            palette.SurfaceColor = new Color(0.98f, 0.98f, 0.99f, 1f);
            palette.SurfaceAltColor = new Color(0.92f, 0.94f, 0.97f, 1f);
            palette.SurfaceHoverColor = new Color(0.86f, 0.91f, 0.97f, 1f);
            palette.SurfaceSunkenColor = new Color(0.88f, 0.90f, 0.93f, 1f);
            palette.BorderColor = new Color(0.72f, 0.76f, 0.82f, 1f);
            palette.BorderSoftColor = new Color(0.82f, 0.86f, 0.90f, 1f);
            palette.HighlightColor = new Color(1f, 1f, 1f, 0.55f);
            palette.AccentColor = new Color(0.16f, 0.48f, 0.86f, 1f);
            palette.AccentSoftColor = new Color(0.62f, 0.78f, 0.96f, 0.65f);
            palette.DangerColor = new Color(0.78f, 0.24f, 0.24f, 1f);
            palette.DangerSoftColor = new Color(0.96f, 0.82f, 0.82f, 0.80f);
            palette.SuccessColor = new Color(0.18f, 0.58f, 0.30f, 1f);
            palette.SuccessSoftColor = new Color(0.80f, 0.93f, 0.82f, 0.85f);
            palette.WarningColor = new Color(0.82f, 0.54f, 0.10f, 1f);
            palette.WarningSoftColor = new Color(0.98f, 0.89f, 0.70f, 0.85f);
            palette.MutedTextColor = new Color(0.36f, 0.40f, 0.46f, 1f);
            palette.PrimaryTextColor = new Color(0.12f, 0.14f, 0.18f, 1f);
        }

        return palette;
    }

    private static Styles CreateStyles(Palette palette)
    {
        Styles styles = new Styles();

        styles.PanelStyle = new GUIStyle(GUIStyle.none)
        {
            padding = new RectOffset(12, 12, 12, 12),
            margin = new RectOffset(0, 0, 0, 0)
        };

        styles.CardStyle = new GUIStyle(EditorStyles.helpBox)
        {
            padding = new RectOffset(14, 14, 12, 14),
            margin = new RectOffset(0, 0, 0, 12)
        };

        styles.RecipeCardStyle = new GUIStyle(EditorStyles.helpBox)
        {
            padding = new RectOffset(10, 10, 8, 10),
            margin = new RectOffset(0, 0, 0, 6)
        };

        styles.CardBodyStyle = new GUIStyle
        {
            padding = new RectOffset(0, 0, 4, 0),
            margin = new RectOffset(0, 0, 0, 0)
        };

        styles.SectionTitleStyle = new GUIStyle(EditorStyles.boldLabel)
        {
            fontSize = 13,
            richText = false,
            normal = { textColor = palette.PrimaryTextColor }
        };

        styles.SectionSubtitleStyle = new GUIStyle(EditorStyles.miniLabel)
        {
            wordWrap = false,
            normal = { textColor = palette.MutedTextColor }
        };

        styles.SummaryValueStyle = new GUIStyle(EditorStyles.boldLabel)
        {
            fontSize = 13,
            alignment = TextAnchor.MiddleLeft,
            normal = { textColor = palette.PrimaryTextColor }
        };

        styles.SummaryLabelStyle = new GUIStyle(EditorStyles.miniLabel)
        {
            fontStyle = FontStyle.Bold,
            normal = { textColor = palette.MutedTextColor }
        };

        styles.SummaryInlineValueStyle = new GUIStyle(EditorStyles.label)
        {
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleRight,
            clipping = TextClipping.Clip,
            normal = { textColor = palette.PrimaryTextColor }
        };

        styles.ChipStyle = new GUIStyle(EditorStyles.miniLabel)
        {
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold,
            fontSize = 10,
            padding = new RectOffset(10, 10, 3, 3),
            normal = { textColor = palette.PrimaryTextColor }
        };

        styles.ToolbarSearchStyle = new GUIStyle(EditorStyles.toolbarSearchField);

        styles.DetailFoldoutStyle = new GUIStyle(EditorStyles.foldout)
        {
            fontStyle = FontStyle.Bold,
            fontSize = 12
        };
        SetTextColor(styles.DetailFoldoutStyle, palette.PrimaryTextColor);

        styles.MutedMiniStyle = new GUIStyle(EditorStyles.miniLabel)
        {
            normal = { textColor = palette.MutedTextColor }
        };

        return styles;
    }

    private static void SetTextColor(GUIStyle style, Color color)
    {
        if (style == null)
            return;

        style.normal.textColor = color;
        style.onNormal.textColor = color;
        style.focused.textColor = color;
        style.onFocused.textColor = color;
        style.active.textColor = color;
        style.onActive.textColor = color;
    }
}
#endif
