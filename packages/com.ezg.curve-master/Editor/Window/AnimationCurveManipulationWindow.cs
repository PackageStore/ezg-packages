using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace AnimationCurveManipulationTool {

	public class AnimationCurveManipulationWindow : EditorWindow {

		[MenuItem("Window/Animation/Curve Master")]
		private static void OpenWindow() {
			var window = GetWindow<AnimationCurveManipulationWindow>();
			window.titleContent.text = "Curve Master";
			window.Show();
		}

		[SerializeField] private AnimationCurveManipulationState manipulationState = new AnimationCurveManipulationState();

		[SerializeField] private CubicBezierEditor cubicBezierEditor = new CubicBezierEditor();
		[SerializeField] private int currentLibraryIndex;
		[SerializeField] private int currentPresetIndex;

		private int libraryIndexToRename = -1;
		private int presetIndexToRename = -1;

		private Vector2 tabsScrollPos;
		private Vector2 presetsScrollPos;
		[SerializeField] private int currentApplyMode = 1;

		private bool needsToUpdateInfo = false;
		private bool m_updateEachFrame = false;
		private AnimationCurveManipulationConfig config;

		private bool performImport = false;

		public bool updateEachFrame => m_updateEachFrame;

		private void OnEnable() {
			config = AnimationCurveManipulationConfig.Get();
			m_updateEachFrame = config != null && config.useCustomSelectionOrder;
			wantsMouseMove = m_updateEachFrame;
            
			if (m_updateEachFrame) {
				EditorApplication.update += OnUpdate;
			}
			cubicBezierEditor.onRepaintNeeded = Repaint;
			cubicBezierEditor.onRecordUndoRequest = () => {
				Undo.RecordObject(this, "modify curve value");
			};
			cubicBezierEditor.onValueChanged = () => {
				if (manipulationState.hasAnyCurvePair && !ReferenceEquals(config, null) && config.autoApply) {
					manipulationState.ApplyCurve(cubicBezierEditor.startTangent, cubicBezierEditor.endTangent, currentApplyMode);
				}
			};
		}

		private void OnDisable() {
			DisableFrameUpdate();
		}

		public void EnableFrameUpdate() {
			m_updateEachFrame = true;
			EditorApplication.update += OnUpdate;
		}

		public void DisableFrameUpdate() {
			if (!m_updateEachFrame) return;
			m_updateEachFrame = false;
			EditorApplication.update -= OnUpdate;
		}

		private void OnGUI() {
			performImport = false;

			if (needsToUpdateInfo) {
				if (mouseOverWindow == this) {
					manipulationState.UpdateInfo();
					needsToUpdateInfo = false;
				}
			}
			else if (mouseOverWindow != this) {
				needsToUpdateInfo = true;
			}

			manipulationState.selectedClip = null;
			bool curvePositionRight = !ReferenceEquals(config, null) && config.curvePositionRight;

			Rect leftSectionRect;
			Rect rightSectionRect;
			float leftSectionWidth = (position.width - 10) * 0.4f;

			if (curvePositionRight) {
				rightSectionRect = new Rect(2, 5, position.width - leftSectionWidth - 10, position.height - 10);
				leftSectionRect = new Rect(rightSectionRect.xMax + 5, 5, leftSectionWidth, position.height - 10);
			}
			else {
				leftSectionRect = new Rect(5, 5, leftSectionWidth, position.height - 10);
				rightSectionRect = new Rect(leftSectionRect.xMax + 5, 5, position.width - leftSectionRect.xMax - 10, position.height - 10);
			}

			GUI.Box(leftSectionRect, GUIContent.none, Styles.frameBoxStyle);
			//GUI.Box(rightSectionRect, GUIContent.none, Styles.frameBoxStyle);

			DrawLeftSection(leftSectionRect);
			DrawRightSection(rightSectionRect);

			var e = Event.current;
			if (e.type == EventType.ValidateCommand) {
				if (e.commandName == "UndoRedoPerformed") {
					Repaint();
				}
			}

			if (performImport) {
				var filePath = EditorUtility.OpenFilePanel("Import Library", Application.dataPath, "json");
				config.ImportLibrary(filePath);
			}
		}

		private void OnUpdate() {
			manipulationState.CheckKeySelection(this);
		}

		private void DrawLeftSection(Rect _drawArea) {
			GUILayout.BeginArea(new Rect(_drawArea.x + 5, _drawArea.y + 5, _drawArea.width - 10, _drawArea.height - 10));
			{
				var labelWidth = EditorGUIUtility.labelWidth;
				EditorGUIUtility.labelWidth = 80f;
				int currentTargetIndex = (int)manipulationState.editorTarget;
				int newTargetIndex = EditorGUILayout.Popup(currentTargetIndex, new string[] { "Animation | Timeline", "Particle System", "AnimationCurve Window" });
				EditorGUIUtility.labelWidth = labelWidth;
				if (newTargetIndex != currentTargetIndex) {
					Undo.RecordObject(this, "change editor target");
					manipulationState.editorTarget = (AnimationCurveManipulationState.EditorTarget)newTargetIndex;
					EditorUtility.SetDirty(this);
				}

				var curveRect = EditorGUILayout.GetControlRect(GUILayout.ExpandHeight(true));
				cubicBezierEditor.Draw(curveRect);

				EditorGUI.BeginChangeCheck();
				var newStringValue = EditorGUILayout.DelayedTextField(cubicBezierEditor.GetStringValue());
				if (EditorGUI.EndChangeCheck()) {
					Undo.RecordObject(this, "modify curve value by string");
					cubicBezierEditor.Parse(newStringValue);
					GUI.FocusControl(null);
				}

				bool guiEnabled = GUI.enabled;

				GUI.enabled = guiEnabled && manipulationState.hasAnyCurvePair;
				if (GUILayout.Button("Apply", GUILayout.Height(30))) {
					manipulationState.ApplyCurve(cubicBezierEditor.startTangent, cubicBezierEditor.endTangent, currentApplyMode);
				}
				GUI.enabled = guiEnabled;

				GUILayout.BeginHorizontal();
				{
					GUI.enabled = guiEnabled && manipulationState.hasAnyCurvePair;
					if (GUILayout.Button("Get Curve")) {
						GetCurve();
					}
					GUI.enabled = guiEnabled;

					GUILayout.FlexibleSpace();

					var guiColor = GUI.color;

					GUI.color = currentApplyMode == 0 ? Color.white : Color.grey;
					if (GUILayout.Button("In", GUILayout.Width(35))) {
						currentApplyMode = 0;
					}
					GUI.color = currentApplyMode == 1 ? Color.white : Color.grey;
					if (GUILayout.Button("In/Out", GUILayout.Width(50))) {
						currentApplyMode = 1;
					}
					GUI.color = currentApplyMode == 2 ? Color.white : Color.grey;
					if (GUILayout.Button("Out", GUILayout.Width(35))) {
						currentApplyMode = 2;
					}

					GUI.color = guiColor;
				}
				GUILayout.EndHorizontal();
				GUILayout.Space(5);

				GUILayout.BeginHorizontal();
				float settingsSize = EditorGUIUtility.singleLineHeight * 2;
				var debugArea = EditorGUILayout.GetControlRect(GUILayout.ExpandWidth(true), GUILayout.Height(settingsSize));
				var settingsArea = EditorGUILayout.GetControlRect(GUILayout.Width(settingsSize), GUILayout.Height(settingsSize));
				GUILayout.EndHorizontal();

				GUI.Box(debugArea, GUIContent.none);
				DrawDebugger(debugArea, manipulationState.curveCount, manipulationState.keyframeCount);
				if (GUI.Button(settingsArea, CurveMasterGUIContents.gearIcon)) {
					SettingsWindow.OpenWindow();
				}

				GUILayout.Space(5);

			}
			GUILayout.EndArea();
		}

		private void GetCurve() {
			var window = GetWindow<AnimationCurveManipulationWindow>();
			var selectedKeyframesGroup = KeyframeDataUtility.GetSelectedKeyframesGroup(manipulationState.editorTarget, 2);
			foreach (var kvp in selectedKeyframesGroup) {
				var selectedKeyframes = kvp.Value;
				if (selectedKeyframes.Length > 1) {
					Undo.RecordObject(window, "get curve");
					window.cubicBezierEditor.SetByKeyframeValues(selectedKeyframes[0], selectedKeyframes[1], window.currentApplyMode);
				}
				break;
			}
		}

		private void DrawDebugger(Rect _area, int _curveCount, int _keyframeCount) {
			var rect = new Rect(_area.x, _area.y, _area.width, EditorGUIUtility.singleLineHeight);
			var animationWindow = AnimationWindowBinding.Get();
			if (animationWindow == null) {
				GUI.Label(rect, "No Animation Window Opened", EditorStyles.miniLabel);
				return;
			}
			var animEditor = animationWindow.animEditor;
			if (animEditor == null) return;

			var state = animEditor.state;
			if (state == null) return;

			manipulationState.selectedClip = state.activeAnimationClip;
			if (manipulationState.selectedClip == null) {
				GUI.Label(rect, "No animation clip selected", EditorStyles.miniLabel);
			}
			else {
				GUI.Label(rect, "Clip: " + manipulationState.selectedClip.name, EditorStyles.miniLabel);
				rect.y += rect.height;

				if (_curveCount > 0) {
					GUI.Label(rect, "Selected Curves: " + _curveCount + " | Selected Keyframes: " + _keyframeCount, EditorStyles.miniLabel);
				}

			}

		}


		private void DrawRightSection(Rect _drawArea) {
			GUILayout.BeginArea(_drawArea);
			{
				var upperRect = EditorGUILayout.GetControlRect(GUILayout.ExpandWidth(true), GUILayout.Height(EditorGUIUtility.singleLineHeight * 4.5f));
				var exportLibraryRect = new Rect(upperRect.x + 0.7f * upperRect.width, upperRect.y, 0.3f * upperRect.width, upperRect.height);
				var savePresetRect = new Rect(upperRect.x, upperRect.y, upperRect.width - exportLibraryRect.width, upperRect.height);
				GUI.Box(savePresetRect, GUIContent.none, Styles.frameBoxStyle);
				GUI.Box(exportLibraryRect, GUIContent.none, Styles.frameBoxStyle);
				DrawKeyframeManipulationArea(savePresetRect);
				DrawExportLibraryArea(exportLibraryRect);

				GUILayout.Space(5);

				DrawTabs();

				var presetsRect = EditorGUILayout.GetControlRect(GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
				DrawPresetsArea(presetsRect);
			}
			GUILayout.EndArea();
		}

		private void DrawKeyframeManipulationArea(Rect _drawArea) {
			var guiColor = GUI.color;
			GUI.color = new Color(0.75f, 0.75f, 0.75f, 1);

			var currentRect = new Rect(_drawArea.x + 5, _drawArea.y + EditorGUIUtility.singleLineHeight * 0.6f, _drawArea.width - 10, EditorGUIUtility.singleLineHeight);
			GUI.Label(currentRect, "Keyframe Manipulation", EditorStyles.boldLabel);
			currentRect.y += currentRect.height;

			var lineRect = currentRect;
			lineRect.height = 1;
			GUI.DrawTexture(lineRect, EditorGUIUtility.whiteTexture);
			currentRect.y += 7;
			GUI.color = guiColor;

			var animationWindow = AnimationWindowBinding.Get();
			if (animationWindow == null) {
				GUI.Label(currentRect, "No Animation Window Opened", EditorStyles.centeredGreyMiniLabel);
				return;
			}
			var animEditor = animationWindow.animEditor;
			var curveEditor = animEditor.curveEditor;
			if (curveEditor == null) {
				GUI.Label(currentRect, "Click on \"Curve\" button on the Animation Window to initialize", EditorStyles.centeredGreyMiniLabel);
				return;
			}

			currentRect.height *= 2;
			currentRect.width /= 7;
			currentRect.width -= 2;

			var useIcons = !ReferenceEquals(config, null) && config.displayButtonIcon;
			if (useIcons) {
				DrawKeyframeIconButtons(currentRect);
			}
			else {
				DrawKeyframeTextButtons(currentRect);
			}
		}

		private void DrawKeyframeIconButtons(Rect _currentRect) {
			var guiEnabled = GUI.enabled;
			int selectedKeysCount = m_updateEachFrame? manipulationState.selectedKeysOrdered.Count : manipulationState.keyframeCount;
			GUI.enabled = guiEnabled && selectedKeysCount > 1 && manipulationState.editorTarget == AnimationCurveManipulationState.EditorTarget.Animation;
			if (GUI.Button(_currentRect, CurveMasterGUIContents.offsetIcon)) {
				manipulationState.OffsetKeyframesOnEachObject(this);
			}
			_currentRect.x += _currentRect.width + 2.5f;
			if (GUI.Button(_currentRect, CurveMasterGUIContents.offsetPlusIcon)) {
				manipulationState.OffsetKeyframesOnEachProperty(this);
			}
			_currentRect.x += _currentRect.width + 2.5f;
			GUI.enabled = guiEnabled && manipulationState.hasAnyCurvePair && manipulationState.editorTarget == AnimationCurveManipulationState.EditorTarget.Animation;
			if (GUI.Button(_currentRect, CurveMasterGUIContents.reverseIcon)) {
				manipulationState.ReverseKeyframes();
			}
			_currentRect.x += _currentRect.width + 2.5f;
			if (GUI.Button(_currentRect, CurveMasterGUIContents.mirrorIcon)) {
				manipulationState.MirrorKeyframes();
			}
			_currentRect.x += _currentRect.width + 2.5f;
			GUI.enabled = guiEnabled && selectedKeysCount > 0 && manipulationState.editorTarget == AnimationCurveManipulationState.EditorTarget.Animation;
			if (GUI.Button(_currentRect, CurveMasterGUIContents.alignIcon)) {
				manipulationState.AlignKeyframes(this);
			}
			_currentRect.x += _currentRect.width + 2.5f;
			GUI.enabled = guiEnabled && manipulationState.editorTarget == AnimationCurveManipulationState.EditorTarget.Animation && manipulationState.selectedClip != null;
			if (GUI.Button(_currentRect, CurveMasterGUIContents.setKeyIcon)) {
				manipulationState.SetKeys();
			}
			_currentRect.x += _currentRect.width + 2.5f;
			if (GUI.Button(_currentRect, CurveMasterGUIContents.addPropIcon)) {
				manipulationState.AddProperties();
			}
			GUI.enabled = guiEnabled;
		}

		private void DrawKeyframeTextButtons(Rect _currentRect) {
			var guiEnabled = GUI.enabled;
			int selectedKeysCount = manipulationState.selectedKeysOrdered.Count;
			GUI.enabled = guiEnabled && selectedKeysCount > 1 && manipulationState.editorTarget == AnimationCurveManipulationState.EditorTarget.Animation;
			if (GUI.Button(_currentRect, CurveMasterGUIContents.offsetLabel)) {
				manipulationState.OffsetKeyframesOnEachObject(this);
			}
			_currentRect.x += _currentRect.width + 2.5f;
			if (GUI.Button(_currentRect, CurveMasterGUIContents.offsetPlusLabel)) {
				manipulationState.OffsetKeyframesOnEachProperty(this);
			}
			_currentRect.x += _currentRect.width + 2.5f;
			GUI.enabled = guiEnabled && manipulationState.hasAnyCurvePair && manipulationState.editorTarget == AnimationCurveManipulationState.EditorTarget.Animation;
			if (GUI.Button(_currentRect, CurveMasterGUIContents.reverseLabel)) {
				manipulationState.ReverseKeyframes();
			}
			_currentRect.x += _currentRect.width + 2.5f;
			if (GUI.Button(_currentRect, CurveMasterGUIContents.mirrorLabel)) {
				manipulationState.MirrorKeyframes();
			}
			_currentRect.x += _currentRect.width + 2.5f;
			GUI.enabled = guiEnabled && selectedKeysCount > 0 && manipulationState.editorTarget == AnimationCurveManipulationState.EditorTarget.Animation;
			if (GUI.Button(_currentRect, CurveMasterGUIContents.alignLabel)) {
				manipulationState.AlignKeyframes(this);
			}
			_currentRect.x += _currentRect.width + 2.5f;
			GUI.enabled = guiEnabled && manipulationState.editorTarget == AnimationCurveManipulationState.EditorTarget.Animation && manipulationState.selectedClip != null;
			if (GUI.Button(_currentRect, CurveMasterGUIContents.setKeyLabel)) {
				manipulationState.SetKeys();
			}
			_currentRect.x += _currentRect.width + 2.5f;
			if (GUI.Button(_currentRect, CurveMasterGUIContents.addPropLabel)) {
				manipulationState.AddProperties();
			}
			GUI.enabled = guiEnabled;
		}

		private void DrawExportLibraryArea(Rect _drawArea) {
			var guiColor = GUI.color;
			GUI.color = new Color(0.75f, 0.75f, 0.75f, 1);

			var currentRect = new Rect(_drawArea.x + 5, _drawArea.y + EditorGUIUtility.singleLineHeight * 0.6f, _drawArea.width - 10, EditorGUIUtility.singleLineHeight);
			GUI.Label(currentRect, "Export Library", EditorStyles.boldLabel);
			currentRect.y += currentRect.height;

			var lineRect = currentRect;
			lineRect.height = 1;
			GUI.DrawTexture(lineRect, EditorGUIUtility.whiteTexture);
			currentRect.y += 7;
			GUI.color = guiColor;

			if (GUI.Button(currentRect, "Import")) {
				performImport = true;
			}
			currentRect.y += currentRect.height + 2;

			var guiEnabled = GUI.enabled;
			GUI.enabled = guiEnabled && currentLibraryIndex >= 0 && currentLibraryIndex < config.curveLibraries.Count;
			if (GUI.Button(currentRect, "Export")) {
				var currentLibrary = config.curveLibraries[currentLibraryIndex];
				var filePath = EditorUtility.SaveFilePanel("Export Library", Application.dataPath, currentLibrary.libraryName, "json");
				if (!string.IsNullOrEmpty(filePath)) {
					var json = currentLibrary.ToJSON();
					File.WriteAllText(filePath, json);
					AssetDatabase.Refresh();
				}
			}
			GUI.enabled = guiEnabled;
		}

		private void DrawTabs() {
			tabsScrollPos = GUILayout.BeginScrollView(tabsScrollPos, GUILayout.Height(30));
			GUILayout.BeginHorizontal();
			GUILayout.Space(5);

			if (config != null) {
				for (int i = 0; i < config.curveLibraries.Count; i++) {
					var library = config.curveLibraries[i];
					var buttonRect = EditorGUILayout.GetControlRect(false, 16, i == currentLibraryIndex ? Styles.tabSelected : Styles.tabUnselected);

					if (i == libraryIndexToRename) {
						HandleRenameLibrary(library, buttonRect, i);
					}
					else {
						var e = Event.current;
						if (e.type == EventType.MouseDown && e.button == 1) {
							if (buttonRect.Contains(e.mousePosition)) {
								var tabContextMenu = new GenericMenu();
								int index = i;
								tabContextMenu.AddItem(new GUIContent("Rename"), false, () => {
									libraryIndexToRename = index;
								});
								tabContextMenu.AddSeparator("");
								tabContextMenu.AddItem(new GUIContent("Remove"), false, () => {
									if (EditorUtility.DisplayDialog("Remove Library", "Are you sure you want to remove Curve Library: " + library.libraryName + "?", "Remove", "Cancel")) {
										Undo.SetCurrentGroupName("remove curve library");
										Undo.RecordObject(config, "remove curve library");
										config.curveLibraries.RemoveAt(index);
										EditorUtility.SetDirty(config);
										if (currentLibraryIndex >= config.curveLibraries.Count) {
											Undo.RecordObject(this, "remove curve library");
											currentLibraryIndex = config.curveLibraries.Count - 1;
											if (currentLibraryIndex <= 0) currentLibraryIndex = 0;
										}
									}
								});
								tabContextMenu.ShowAsContext();
								e.Use();
							}
						}
						if (GUI.Button(buttonRect, library.libraryName, i == currentLibraryIndex ? Styles.tabSelected : Styles.tabUnselected)) {
							currentLibraryIndex = i;
						}
					}
				}
				if (GUILayout.Button("  + Add Library  ", Styles.tabUnselected)) {
					Undo.RecordObject(config, "add curve library");
					config.curveLibraries.Add(new CurveLibrary());
					EditorUtility.SetDirty(config);
				}
			}

			GUILayout.Space(5);
			GUILayout.EndHorizontal();
			GUILayout.EndScrollView();
		}

		private void HandleRenameLibrary(CurveLibrary _library, Rect _buttonRect, int _index) {
			var e = Event.current;
			EditorGUI.BeginChangeCheck();
			GUI.SetNextControlName("RenameLibrary-" + _index);
			var newLibraryName = EditorGUI.DelayedTextField(_buttonRect, _library.libraryName);
			GUI.FocusControl("RenameLibrary-" + _index);

			bool finishRename = false;
			if (EditorGUI.EndChangeCheck()) {
				finishRename = true;
			}
			else if ((e.type == EventType.KeyUp || e.type == EventType.KeyDown) && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)) {
				finishRename = true;
			}
			else if (e.type == EventType.MouseDown && !_buttonRect.Contains(e.mousePosition)) {
				finishRename = true;
			}
			if (finishRename) {
				Undo.RecordObject(config, "rename curve library");
				_library.libraryName = newLibraryName;
				EditorUtility.SetDirty(config);
				libraryIndexToRename = -1;
				GUI.FocusControl(null);
				Repaint();
			}
		}

		private void DrawPresetsArea(Rect _drawArea) {
			if (ReferenceEquals(config, null)) return;

			if (currentLibraryIndex < 0 || currentLibraryIndex >= config.curveLibraries.Count) return;
			var currentLibrary = config.curveLibraries[currentLibraryIndex];

			int childCount = currentLibrary.curvePresets.Count + 1;
			float childSize = config == null? AnimationCurveManipulationConfig.DefaultPresetSize : config.presetSize;

			int cols = Mathf.FloorToInt(_drawArea.width / childSize);
			int rows = Mathf.CeilToInt(childCount / (float)cols);

			var backgroundRect = _drawArea;
			backgroundRect.width = cols * childSize;

			var viewArea = new Rect(_drawArea.x, _drawArea.y, cols * childSize, rows * childSize);
			presetsScrollPos = GUI.BeginScrollView(_drawArea, presetsScrollPos, viewArea);

			Vector2 startPointInv = new Vector2(0, 1);
			Vector2 endPointInv = new Vector2(1, 0);

			int currentIndex = 0;
			for (int row = 0; row < rows; row++) {
				if (currentIndex >= childCount) break;
				for (int col = 0; col < cols; col++) {
					if (currentIndex >= childCount) break;

					Rect childRect = new Rect(
						_drawArea.x + col * childSize,
						_drawArea.y + row * childSize,
						childSize,
						childSize
					);

					// Draw the child rect here
					if (currentIndex == childCount-1) {
						if (GUI.Button(childRect, GUIContent.none)) {
							Undo.RecordObject(config, "create new curve preset");
							var newPreset = new CurvePreset(cubicBezierEditor.GetValue());
							currentLibrary.curvePresets.Add(newPreset);
							EditorUtility.SetDirty(config);
						}
						GUI.Label(childRect, new GUIContent("+"), Styles.largeText);
					}
					else {
						var guiColor = GUI.color;
						GUI.color = currentPresetIndex == currentIndex ? Color.white : Color.grey;

						var currentPreset = currentLibrary.curvePresets[currentIndex];

						GUI.Box(childRect, GUIContent.none, Styles.boxBackground);
						Vector2 startTangentInv = new Vector2(currentPreset.curveValue[0], 1 - currentPreset.curveValue[1]);
						Vector2 endTangentInv = new Vector2(currentPreset.curveValue[2], 1 - currentPreset.curveValue[3]);

						var curveRect = new Rect(childRect.x + 14, childRect.y + 8, childRect.width - 28, childRect.height - 28);
						var nameRect = new Rect(childRect.x + 2, childRect.y + childRect.height - EditorGUIUtility.singleLineHeight, childRect.width - 4, EditorGUIUtility.singleLineHeight);

						Handles.BeginGUI();
						var handleColor = Handles.color;
						Handles.color = Color.white;
						Handles.DrawBezier(
							curveRect.position + startPointInv * curveRect.size,
							curveRect.position + endPointInv * curveRect.size,
							curveRect.position + startTangentInv * curveRect.size,
							curveRect.position + endTangentInv * curveRect.size,
							config == null ? AnimationCurveManipulationConfig.DefaultPresetColor : config.presetColor,
							null,
							2f
						);
						Handles.color = handleColor;
						Handles.EndGUI();
						GUI.color = guiColor;

						if (currentIndex == presetIndexToRename) {
							HandleRenamePreset(currentPreset, nameRect, currentIndex);
						}
						else {
							GUI.Box(nameRect, new GUIContent(currentPreset.presetName), EditorStyles.centeredGreyMiniLabel);
						}

						var e = Event.current;
						if (e.type == EventType.MouseDown) {
							if (e.button == 0) {
								if (e.clickCount == 1) {
									if (curveRect.Contains(e.mousePosition)) {
										Undo.RecordObject(this, "select curve preset");
										currentPresetIndex = currentIndex;
										cubicBezierEditor.SetValue(currentPreset.curveValue);
										e.Use();
									}
								}
								else if (e.clickCount == 2) {
									if (nameRect.Contains(e.mousePosition)) {
										presetIndexToRename = currentIndex;
										e.Use();
									}
								}
							}
							else if (e.button == 1) {
								if (childRect.Contains(e.mousePosition)) {
									var presetContextMenu = new GenericMenu();
									var preset = currentPreset;
									presetContextMenu.AddItem(new GUIContent("Save"), false, () => {
										Undo.RecordObject(config, "save curve preset");
										preset.curveValue = cubicBezierEditor.GetValue();
										EditorUtility.SetDirty(config);
									});
									var index = currentIndex;
									presetContextMenu.AddItem(new GUIContent("Rename"), false, () => {
										presetIndexToRename = index;
									});
									presetContextMenu.AddSeparator("");
									presetContextMenu.AddItem(new GUIContent("Remove"), false, () => {
										if (EditorUtility.DisplayDialog("Remove Preset", "Are you sure you want to remove Curve Preset: " + preset.presetName + "?", "Remove", "Cancel")) {
											Undo.RecordObject(config, "remove curve preset");
											currentLibrary.curvePresets.RemoveAt(index);
											EditorUtility.SetDirty(config);
										}
									});
									presetContextMenu.ShowAsContext();
								}
							}
						}
					}
					currentIndex++;

				}
			}

			GUI.EndScrollView();
		}

		private void HandleRenamePreset(CurvePreset _preset, Rect _rect, int _index) {
			var e = Event.current;
			EditorGUI.BeginChangeCheck();
			GUI.SetNextControlName("RenamePreset-" + _index);
			var newPresetName = EditorGUI.DelayedTextField(_rect, _preset.presetName);
			GUI.FocusControl("RenamePreset-" + _index);

			bool finishRename = false;
			if (EditorGUI.EndChangeCheck()) {
				finishRename = true;
			}
			else if ((e.type == EventType.KeyUp || e.type == EventType.KeyDown) && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)) {
				finishRename = true;
			}
			else if (e.type == EventType.MouseDown && !_rect.Contains(e.mousePosition)) {
				finishRename = true;
			}
			if (finishRename) {
				Undo.RecordObject(config, "rename curve preset");
				_preset.presetName = newPresetName;
				EditorUtility.SetDirty(config);
				presetIndexToRename = -1;
				GUI.FocusControl(null);
				Repaint();
			}
		}

	}

}