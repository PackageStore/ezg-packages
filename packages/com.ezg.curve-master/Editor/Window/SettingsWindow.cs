using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace AnimationCurveManipulationTool {
    public class SettingsWindow : EditorWindow {

	    private string version = "v1.2.1";
        private string docsLink = "https://to.yan-k.tv/CMDocs";
        private string supportLink = "https://to.yan-k.tv/DCnekolab";

        public static void OpenWindow() {
            var window = GetWindow<SettingsWindow>();
            window.titleContent.text = "Curve Master Settings";
            window.minSize = new Vector2(282, 455f);
            window.maxSize = new Vector2(282, 455f);
#if UNITY_6000_0_OR_NEWER
            window.Show();
#else
            window.ShowAuxWindow();
#endif
        }

        private AnimationCurveManipulationWindow curveWindow;

        private Color selectedButtonColor = new Color(0.8f, 0.8f, 1f);
        private Color unselectedButtonColor = new Color(0.8f, 0.8f, 0.8f);

        private GUIStyle m_whiteText;
        private GUIStyle whiteText {
            get {
                if (m_whiteText == null) {
                    m_whiteText = new GUIStyle();
                    m_whiteText.normal.textColor = Color.white;
                    m_whiteText.alignment = TextAnchor.MiddleCenter;
                    m_whiteText.fontSize = 20;
                }
                return m_whiteText;
            }
        }

        private GUIStyle m_buttonFull;
        private GUIStyle m_buttonLeft;
        private GUIStyle m_buttonRight;

        private GUIStyle buttonFull {
            get {
                if (m_buttonFull == null) {
                    m_buttonFull = new GUIStyle(EditorStyles.miniButton);
                    m_buttonFull.fixedHeight = 35f;
                }
                return m_buttonFull;
            }
        }

        private GUIStyle buttonLeft {
            get {
                if (m_buttonLeft == null) {
                    m_buttonLeft = new GUIStyle(EditorStyles.miniButtonLeft);
                    m_buttonLeft.fixedHeight = 35f;
                }
                return m_buttonLeft;
            }
        }

        private GUIStyle buttonRight {
            get {
                if (m_buttonRight == null) {
                    m_buttonRight = new GUIStyle(EditorStyles.miniButtonRight);
                    m_buttonRight.fixedHeight = 35f;
                }
                return m_buttonRight;
            }
        }

        private GUIContent m_customOrderLabel = null;
        private GUIContent customOrderLabel {
            get {
                if (m_customOrderLabel == null) {
                    m_customOrderLabel = new GUIContent("Custom Order", "Custom Selection Order is used only for Offset and Offset+. By default, selection is ordered from top to bottom. By turning this on, it allows us to select in our preferred order using mouse click. But note that this might be expensive when many amounts of keyframes are being used.");
                }
                return m_customOrderLabel;
            }
        }

        private void OnGUI() {
            if (curveWindow == null) {
                if (ReferenceEquals(curveWindow, null)) {
                    curveWindow = GetWindow<AnimationCurveManipulationWindow>();
                }
                else {
                    Close();
                }
            }

            DrawUpperLogo();
            DrawTitleVersion();

            var config = AnimationCurveManipulationConfig.Get();
            DrawCurvePositionField(config);
            DrawButtonDisplayField(config);
            EditorGUILayout.Space();
            DrawSeparatorLine();
            EditorGUILayout.Space();

            DrawAutoApplyField(config);
            DrawCustomOrderField(config);
            EditorGUILayout.Space();
            DrawSeparatorLine();
            EditorGUILayout.Space();

            DrawPresetColorField(config);
            DrawPresetSizeField(config);

            GUILayout.Space(10);

            GUILayout.BeginHorizontal();
            GUILayout.Space(3f);
            bool resetPresets = false;
            if (GUILayout.Button("Reset Presets", buttonFull, GUILayout.Width(137 * 2f))) {
                resetPresets = EditorUtility.DisplayDialog("Reset Presets", "This will reset and clear ALL presets, are you sure?", "Reset", "Cancel");
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Docs", buttonLeft, GUILayout.Width(137))) {
                Application.OpenURL(docsLink);
            }
            if (GUILayout.Button("Support", buttonRight, GUILayout.Width(137))) {
                Application.OpenURL(supportLink);
            }
            GUILayout.EndHorizontal();

            var e = Event.current;
            if (e.type == EventType.ValidateCommand) {
                if (e.commandName == "UndoRedoPerformed") {
                    Repaint();
                }
            }

            if (resetPresets) {
                AnimationCurveManipulationConfig.ResetPresets();
            }
        }

        private void DrawUpperLogo() {
            var upperArea = EditorGUILayout.GetControlRect(GUILayout.Width(282), GUILayout.Height(157));
            var boxArea = new Rect(upperArea.x, upperArea.y + 2, 276, 153);
            var logoArea = new Rect(boxArea.x + 10, boxArea.y, boxArea.width - 20, 120);
            var textArea = new Rect(logoArea.x, logoArea.y + logoArea.height, logoArea.width, boxArea.height - logoArea.height);

            var guiColor = GUI.color;
            GUI.color = new Color(0.2f, 0.2f, 0.2f, 1);
            GUI.DrawTexture(boxArea, EditorGUIUtility.whiteTexture);
            GUI.color = guiColor;
            GUI.DrawTexture(logoArea, CurveMasterIcons.logo, ScaleMode.ScaleToFit);
            GUI.Label(textArea, "Curve Master", whiteText);
        }

        private void DrawTitleVersion() {
            EditorGUILayout.Space();
            GUILayout.Label("Curve Master", EditorStyles.boldLabel);
            var guiColor = GUI.color;
            GUI.color = new Color(0.5f, 0.5f, 0.5f, 1);
            GUILayout.Label(version, EditorStyles.miniLabel);
            DrawSeparatorLine();
            GUI.color = guiColor;
            EditorGUILayout.Space();
        }

        private void DrawCurvePositionField(AnimationCurveManipulationConfig _config) {
            GUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Curve Position", GUILayout.Width(100));
            var guiBacgroundColor = GUI.backgroundColor;
            GUI.backgroundColor = _config.curvePositionRight ? unselectedButtonColor : selectedButtonColor;
            if (GUILayout.Button("Left", EditorStyles.miniButtonLeft, GUILayout.Width(90))) {
                ChangeConfig(_config, () => _config.curvePositionRight = false);
                TryRepaintCurveWindow();
                GUI.FocusControl(null);
            }
            GUI.backgroundColor = _config.curvePositionRight ? selectedButtonColor : unselectedButtonColor;
            if (GUILayout.Button("Right", EditorStyles.miniButtonRight)) {
                ChangeConfig(_config, () => _config.curvePositionRight = true);
                TryRepaintCurveWindow();
                GUI.FocusControl(null);
            }
            GUI.backgroundColor = guiBacgroundColor;
            GUILayout.EndHorizontal();
        }

        private void DrawButtonDisplayField(AnimationCurveManipulationConfig _config) {
            GUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Button Display", GUILayout.Width(100));
            var guiBacgroundColor = GUI.backgroundColor;
            GUI.backgroundColor = _config.displayButtonIcon ? selectedButtonColor : unselectedButtonColor;
            if (GUILayout.Button("Icon", EditorStyles.miniButtonLeft, GUILayout.Width(90))) {
                ChangeConfig(_config, () => _config.displayButtonIcon = true);
                TryRepaintCurveWindow();
                GUI.FocusControl(null);
                Focus();
            }
            GUI.backgroundColor = _config.displayButtonIcon ? unselectedButtonColor : selectedButtonColor;
            if (GUILayout.Button("Text", EditorStyles.miniButtonRight)) {
                ChangeConfig(_config, () => _config.displayButtonIcon = false);
                TryRepaintCurveWindow();
                GUI.FocusControl(null);
                Focus();
            }
            GUI.backgroundColor = guiBacgroundColor;
            GUILayout.EndHorizontal();
        }

        private void DrawAutoApplyField(AnimationCurveManipulationConfig _config) {
            GUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Auto Apply", GUILayout.Width(100));
            var guiBacgroundColor = GUI.backgroundColor;
            GUI.backgroundColor = _config.autoApply ? selectedButtonColor : unselectedButtonColor;
            if (GUILayout.Button("On", EditorStyles.miniButtonLeft, GUILayout.Width(90))) {
                ChangeConfig(_config, () => _config.autoApply = true);
                GUI.FocusControl(null);
                Focus();
            }
            GUI.backgroundColor = _config.autoApply ? unselectedButtonColor : selectedButtonColor;
            if (GUILayout.Button("Off", EditorStyles.miniButtonRight)) {
                ChangeConfig(_config, () => _config.autoApply = false);
                GUI.FocusControl(null);
                Focus();
            }
            GUI.backgroundColor = guiBacgroundColor;
            GUILayout.EndHorizontal();
        }

        private void DrawCustomOrderField(AnimationCurveManipulationConfig _config) {
            GUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(customOrderLabel, GUILayout.Width(100));
            var guiBacgroundColor = GUI.backgroundColor;
            GUI.backgroundColor = _config.useCustomSelectionOrder ? selectedButtonColor : unselectedButtonColor;
            if (GUILayout.Button("On", EditorStyles.miniButtonLeft, GUILayout.Width(90))) {
                ChangeConfig(_config, () => _config.useCustomSelectionOrder = true);
                var curveWindow = GetWindow<AnimationCurveManipulationWindow>();
                if (curveWindow != null) {
                    curveWindow.EnableFrameUpdate();
                }
                GUI.FocusControl(null);
                Focus();
            }
            GUI.backgroundColor = _config.useCustomSelectionOrder ? unselectedButtonColor : selectedButtonColor;
            if (GUILayout.Button("Off", EditorStyles.miniButtonRight)) {
                ChangeConfig(_config, () => _config.useCustomSelectionOrder = false);
                var curveWindow = GetWindow<AnimationCurveManipulationWindow>();
                if (curveWindow != null) {
                    curveWindow.DisableFrameUpdate();
                }
                GUI.FocusControl(null);
                Focus();
            }
            GUI.backgroundColor = guiBacgroundColor;
            GUILayout.EndHorizontal();
        }

        private void DrawPresetColorField(AnimationCurveManipulationConfig _config) {
            GUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Preset Color", GUILayout.Width(100));
            EditorGUI.BeginChangeCheck();
            var newColor = EditorGUILayout.ColorField(GUIContent.none, _config.presetColor, GUILayout.Width(174f));
            if (EditorGUI.EndChangeCheck()) {
                ChangeConfig(_config, () => _config.presetColor = newColor);
                TryRepaintCurveWindow();
            }
            GUILayout.EndHorizontal();
        }

        private void DrawPresetSizeField(AnimationCurveManipulationConfig _config) {
            GUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Preset Size", GUILayout.Width(100));
            EditorGUI.BeginChangeCheck();
            var newSize = EditorGUILayout.Slider(_config.presetSize, AnimationCurveManipulationConfig.MinPresetSize, AnimationCurveManipulationConfig.MaxPresetSize, GUILayout.Width(174f));
            if (EditorGUI.EndChangeCheck()) {
                if (newSize < 1f) newSize = 1f;
                ChangeConfig(_config, () => _config.presetSize = newSize);
                TryRepaintCurveWindow();
            }
            GUILayout.EndHorizontal();
        }


        private void DrawSeparatorLine() {
            var guiColor = GUI.color;
            GUI.color = new Color(0.5f, 0.5f, 0.5f, 1);
            var lineRect = EditorGUILayout.GetControlRect(GUILayout.ExpandWidth(true), GUILayout.Height(1));
            GUI.DrawTexture(lineRect, EditorGUIUtility.whiteTexture);
            GUI.color = guiColor;
        }

        private void ChangeConfig(AnimationCurveManipulationConfig _config, System.Action _handler) {
            Undo.RecordObject(_config, "modify config");
            _handler.Invoke();
            EditorUtility.SetDirty(_config);
        }

        private void TryRepaintCurveWindow() {
            if (curveWindow != null) {
                curveWindow.Repaint();
            }
        }

    }
}
