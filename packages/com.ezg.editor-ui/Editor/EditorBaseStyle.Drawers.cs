#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

public static partial class EditorBaseStyle
{
    public static void DrawPanelBackground(Rect rect)
    {
        Theme theme = Current;
        EditorGUI.DrawRect(rect, theme.Colors.SurfaceColor);
        EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 1f), theme.Colors.HighlightColor);
        DrawRectBorder(rect, theme.Colors.BorderSoftColor, 1f);
    }

    public static void DrawCardHeader(string title, string subtitle = null, Color? accent = null)
    {
        Theme theme = Current;
        float height = string.IsNullOrEmpty(subtitle) ? 26f : 36f;
        Rect rect = GUILayoutUtility.GetRect(10f, height, GUILayout.ExpandWidth(true));
        Color ac = accent.HasValue ? accent.Value : theme.Colors.AccentColor;

        Rect bar = new Rect(rect.x, rect.y + 2f, 5f, rect.height - 4f);
        EditorGUI.DrawRect(bar, ac);
        EditorGUI.DrawRect(new Rect(bar.x, bar.y, bar.width, bar.height * 0.35f),
            Color.Lerp(ac, Color.white, 0.25f));

        GUI.Label(new Rect(rect.x + 14f, rect.y, rect.width - 18f, 18f), title, theme.Styles.SectionTitleStyle);

        if (!string.IsNullOrEmpty(subtitle))
        {
            GUI.Label(new Rect(rect.x + 14f, rect.y + 18f, rect.width - 18f, 14f),
                subtitle, theme.Styles.SectionSubtitleStyle);
        }

        EditorGUI.DrawRect(new Rect(rect.x, rect.yMax + 2f, rect.width, 1f), theme.Colors.BorderSoftColor);
        GUILayout.Space(8f);
    }

    public static void DrawChip(string text, Color? color = null)
    {
        GUIContent content = new GUIContent(text);
        GUIStyle chipStyle = Current.Styles.ChipStyle;
        Vector2 size = chipStyle.CalcSize(content);
        Rect rect = GUILayoutUtility.GetRect(size.x + 14f, 20f,
            GUILayout.Width(size.x + 14f), GUILayout.Height(20f));
        DrawChip(rect, text, color);
    }

    public static void DrawChip(Rect rect, string text, Color? color = null)
    {
        Theme theme = Current;
        Color baseColor = color.HasValue ? color.Value : theme.Colors.SurfaceAltColor;
        Color fillColor;
        Color borderColor;
        Color textColor;

        if (!color.HasValue)
        {
            fillColor = baseColor;
            borderColor = theme.Colors.BorderSoftColor;
            textColor = theme.Colors.MutedTextColor;
        }
        else if (IsCloseTo(baseColor, theme.Colors.SuccessColor))
        {
            fillColor = theme.Colors.SuccessSoftColor;
            borderColor = theme.Colors.SuccessColor;
            textColor = theme.Colors.SuccessColor;
        }
        else if (IsCloseTo(baseColor, theme.Colors.WarningColor))
        {
            fillColor = theme.Colors.WarningSoftColor;
            borderColor = theme.Colors.WarningColor;
            textColor = theme.Colors.WarningColor;
        }
        else if (IsCloseTo(baseColor, theme.Colors.DangerColor))
        {
            fillColor = theme.Colors.DangerSoftColor;
            borderColor = theme.Colors.DangerColor;
            textColor = theme.Colors.DangerColor;
        }
        else if (IsCloseTo(baseColor, theme.Colors.AccentColor))
        {
            fillColor = theme.Colors.AccentSoftColor;
            borderColor = theme.Colors.AccentColor;
            textColor = theme.ProSkin ? Color.white : theme.Colors.AccentColor;
        }
        else
        {
            fillColor = baseColor;
            borderColor = theme.Colors.BorderSoftColor;
            textColor = theme.Colors.MutedTextColor;
        }

        EditorGUI.DrawRect(rect, fillColor);
        DrawRectBorder(rect, borderColor, 1f);

        GUIStyle style = new GUIStyle(theme.Styles.ChipStyle);
        SetTextColor(style, textColor);
        GUI.Label(rect, text, style);
    }

    public static string DrawSearchField(string value, string hint, GUIStyle searchStyle, GUIStyle hintStyle,
        params GUILayoutOption[] options)
    {
        GUIStyle resolvedSearchStyle = searchStyle ?? EditorStyles.toolbarSearchField;
        GUIStyle resolvedHintStyle = hintStyle ?? EditorStyles.miniLabel;
        Rect rect = GUILayoutUtility.GetRect(120f, 18f, resolvedSearchStyle, options);
        int controlId = GUIUtility.GetControlID(FocusType.Passive, rect);
        string newValue = EditorGUI.TextField(rect, value ?? string.Empty, resolvedSearchStyle);

        if (string.IsNullOrEmpty(newValue) &&
            Event.current.type == EventType.Repaint &&
            GUIUtility.keyboardControl != controlId)
        {
            GUI.Label(new Rect(rect.x + 18f, rect.y + 1f, rect.width - 20f, rect.height - 2f),
                hint, resolvedHintStyle);
        }

        return newValue;
    }

    public static void DrawRectBorder(Rect rect, Color color, float width)
    {
        Handles.BeginGUI();
        Color prev = Handles.color;
        Handles.color = color;

        Vector3 p0 = new Vector3(rect.x + 0.5f, rect.y + 0.5f);
        Vector3 p1 = new Vector3(rect.xMax - 0.5f, rect.y + 0.5f);
        Vector3 p2 = new Vector3(rect.xMax - 0.5f, rect.yMax - 0.5f);
        Vector3 p3 = new Vector3(rect.x + 0.5f, rect.yMax - 0.5f);
        Handles.DrawAAPolyLine(width, p0, p1, p2, p3, p0);

        Handles.color = prev;
        Handles.EndGUI();
    }
}
#endif
