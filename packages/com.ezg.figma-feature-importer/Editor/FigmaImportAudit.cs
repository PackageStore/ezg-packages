#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityFigmaBridge.Editor.Settings;
using UnityFigmaBridge.Editor.Utils;
using Object = UnityEngine.Object;

namespace Ezg.FigmaFeatureImporter.Editor
{
    /// <summary>Kết quả audit một màn; <see cref="ToString" /> trả JSON.</summary>
    [Serializable]
    public sealed class AuditReport
    {
        public string frame;
        public string prefab;
        public bool pass;
        public List<string> failures = new();
        public List<string> checks = new();
        public string snapshot;

        public override string ToString() => JsonUtility.ToJson(this, true);

        internal void Check(bool ok, string name, string detail = null)
        {
            var line = detail == null ? name : $"{name}: {detail}";
            if (ok) checks.Add(line);
            else failures.Add(line);
        }
    }

    /// <summary>
    ///     Hard-check §6 TechSpec dưới dạng code (thay cho grep tay trong figma-to-feature/SKILL.md
    ///     STEP 4). Gọi qua MCP: <c>FigmaImportAudit.Run("&lt;Frame&gt;").ToString()</c>.
    /// </summary>
    public static class FigmaImportAudit
    {
        /// <summary>Script FigmaImage của bridge 0.3.x (đã xoá ở 0.4.0) — còn tham chiếu là còn missing script.</summary>
        private const string FIGMA_IMAGE_GUID = "0186b0e08a08b6645958d1b546fb94ba";
        private const string RENDER_MODE_OVERRIDE = "propertyPath: m_RenderMode";

        /// <summary>Dùng khi settings chưa khai snapshotFolder.</summary>
        public const string DEFAULT_SNAPSHOT_DIR = ".claude/tmp";
        private const int SNAPSHOT_WIDTH = 1080;
        private const int SNAPSHOT_HEIGHT = 2400;

        /// <summary>Audit mọi entry enabled.</summary>
        public static List<AuditReport> RunEnabled(bool snapshot = false)
        {
            var settings = FigmaFeatureImportSettings.LoadOrCreate();
            var reports = new List<AuditReport>();
            foreach (var entry in settings.screens)
            {
                if (entry == null || !entry.enabled) continue;
                reports.Add(Run(entry.figmaScreen, snapshot));
            }
            return reports;
        }

        /// <summary>Audit bản ship của một frame theo entry của nó (<c>outputPrefab</c>).</summary>
        public static AuditReport Run(string frameName, bool snapshot = false)
        {
            var report = new AuditReport { frame = frameName };
            var settings = FigmaFeatureImportSettings.LoadOrCreate();
            var entry = settings.FindScreen(frameName);
            if (entry == null)
            {
                report.failures.Add($"Không có entry '{frameName}'.");
                return report;
            }
            report.prefab = entry.outputPrefab;

            if (string.IsNullOrEmpty(entry.outputPrefab) || !File.Exists(entry.outputPrefab))
            {
                report.failures.Add($"outputPrefab '{entry.outputPrefab}' không tồn tại.");
                return report;
            }

            Audit(report, settings, entry, frameName, entry.outputPrefab, allowFigmaSprites: false, snapshot);
            return report;
        }

        /// <summary>
        ///     Audit variant TẠI CHỖ (<c>Assets/Figma/Screens/&lt;Frame&gt;.prefab</c>): cùng hard-check
        ///     nhưng sprite được phép ở trong Assets/Figma; frame không entry kiểm theo
        ///     <see cref="FigmaFeatureImporter.DefaultEntry" /> (chỉ cấu trúc).
        /// </summary>
        public static AuditReport RunInPlace(string frameName, bool snapshot = false)
        {
            var report = new AuditReport { frame = frameName };
            var bridge = UnityFigmaBridgeSettingsProvider.FindUnityBridgeSettingsAsset();
            if (bridge == null)
            {
                report.failures.Add("Không thấy UnityFigmaBridgeSettings.asset.");
                return report;
            }
            FigmaPaths.Configure(bridge, "Document");
            var path = FigmaFeatureImporter.ScreenPrefabPath(frameName);
            if (path == null)
            {
                report.failures.Add($"Frame '{frameName}' đang ExcludeFromImport trong bridge settings và không có prefab trên đĩa.");
                return report;
            }
            report.prefab = path;
            if (!File.Exists(path))
            {
                report.failures.Add($"'{path}' không tồn tại — chưa Sync.");
                return report;
            }

            var settings = FigmaFeatureImportSettings.LoadOrCreate();
            var entry = settings.FindScreen(frameName) ?? FigmaFeatureImporter.DefaultEntry(settings, frameName);
            Audit(report, settings, entry, frameName, path, allowFigmaSprites: true, snapshot);
            return report;
        }

        /// <summary>Audit tại chỗ mọi prefab trong thư mục Screens của bridge.</summary>
        public static List<AuditReport> RunAllInPlace(bool snapshot = false)
        {
            var reports = new List<AuditReport>();
            var bridge = UnityFigmaBridgeSettingsProvider.FindUnityBridgeSettingsAsset();
            if (bridge == null) return reports;
            FigmaPaths.Configure(bridge, "Document");
            var folder = FigmaPaths.FigmaScreenPrefabFolder;
            if (!Directory.Exists(folder)) return reports;
            foreach (var file in Directory.GetFiles(folder, "*.prefab"))
                reports.Add(RunInPlace(Path.GetFileNameWithoutExtension(file), snapshot));
            return reports;
        }

        private static void Audit(AuditReport report, FigmaFeatureImportSettings settings, FigmaScreenEntry entry,
            string frameName, string prefabPath, bool allowFigmaSprites, bool snapshot)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
            {
                report.failures.Add("Không load được prefab.");
                return;
            }

            // Variant của screen_template
            var assetType = PrefabUtility.GetPrefabAssetType(prefab);
            report.Check(assetType == PrefabAssetType.Variant, "variant", assetType.ToString());
            var basePrefab = PrefabUtility.GetCorrespondingObjectFromSource(prefab);
            var basePath = basePrefab != null ? AssetDatabase.GetAssetPath(basePrefab) : null;
            report.Check(basePath == settings.screenTemplatePath, "base = " +
                Path.GetFileNameWithoutExtension(settings.screenTemplatePath), basePath ?? "null");

            // Không dính bridge / font cũ / override render mode (đọc thẳng YAML)
            var yaml = File.ReadAllText(prefabPath);
            report.Check(!yaml.Contains(FIGMA_IMAGE_GUID), "no FigmaImage", Count(yaml, FIGMA_IMAGE_GUID) + " ref");
            report.Check(!yaml.Contains(RENDER_MODE_OVERRIDE), "no m_RenderMode override");

            // Sprite không còn trỏ output thô của bridge
            var bridge = UnityFigmaBridgeSettingsProvider.FindUnityBridgeSettingsAsset();
            var figmaRoot = bridge != null ? FigmaPaths.Resolve(bridge, "Document", warnOnInvalid: false).Root : "Assets/Figma";
            figmaRoot = figmaRoot.TrimEnd('/') + "/";
            var figmaSprites = new List<string>();
            foreach (var image in prefab.GetComponentsInChildren<Image>(true))
            {
                if (image.sprite == null) continue;
                var path = AssetDatabase.GetAssetPath(image.sprite);
                if (path.StartsWith(figmaRoot, StringComparison.Ordinal)) figmaSprites.Add($"{image.name}: {path}");
            }
            if (allowFigmaSprites)
                report.checks.Add($"sprite under {figmaRoot}: {figmaSprites.Count} (variant tại chỗ — sprite ở nguyên trong Assets/Figma)");
            else
                report.Check(figmaSprites.Count == 0, "no sprite under " + figmaRoot, figmaSprites.Count == 0 ? null : string.Join("; ", figmaSprites));

            // Con root + đúng một nhánh bật
            var root = prefab.transform;
            var expectedRoots = new[] { settings.backgroundNode, settings.popupNode, settings.fullScreenNode };
            var rootOk = root.childCount >= 3;
            for (var i = 0; i < expectedRoots.Length && rootOk; i++) rootOk &= root.GetChild(i).name == expectedRoots[i];
            report.Check(rootOk, "root children order", rootOk ? null : Describe(root));
            var popup = root.Find(settings.popupNode);
            var full = root.Find(settings.fullScreenNode);
            var popupOn = popup != null && popup.gameObject.activeSelf;
            var fullOn = full != null && full.gameObject.activeSelf;
            report.Check(popupOn != fullOn, "exactly one branch active", $"popup={popupOn} full={fullOn}");
            var expectPopup = entry.layout == FigmaScreenLayout.Popup;
            report.Check(popupOn == expectPopup, "active branch matches layout", entry.layout.ToString());

            // Thân đúng vị trí
            var bodyParentPath = expectPopup
                ? $"{settings.popupNode}/{settings.popupBodyPath}"
                : $"{settings.fullScreenNode}/{settings.fullScreenContentNode}";
            var bodyParent = root.Find(bodyParentPath);
            var body = bodyParent != null ? bodyParent.Find(frameName) : null;
            report.Check(body != null, "body at " + bodyParentPath + "/" + frameName);

            // Controller: tìm theo TÊN lớp cơ sở trong settings (package không tham chiếu assembly game)
            MonoBehaviour controller = null;
            if (!string.IsNullOrWhiteSpace(entry.controllerType))
            {
                var wantedType = entry.controllerType.Split(',')[0].Trim();
                foreach (var behaviour in prefab.GetComponents<MonoBehaviour>())
                {
                    if (behaviour == null) continue;
                    if (behaviour.GetType().FullName != wantedType) continue;
                    controller = behaviour;
                    break;
                }
            }
            if (string.IsNullOrWhiteSpace(entry.controllerType))
            {
                report.checks.Add("controller: entry không khai controllerType (chỉ cấu trúc)");
            }
            else if (controller == null)
            {
                report.failures.Add($"controller: không có '{entry.controllerType.Split(',')[0].Trim()}' trên root");
            }
            else
            {
                var so = new SerializedObject(controller);
                var featureProperty = so.FindProperty("FeatureType");
                // Enum của game: so bằng TÊN member đang chọn, không ép kiểu enum nào.
                var actualFeature = featureProperty != null && featureProperty.propertyType == SerializedPropertyType.Enum &&
                                    featureProperty.enumValueIndex >= 0 &&
                                    featureProperty.enumValueIndex < featureProperty.enumNames.Length
                    ? featureProperty.enumNames[featureProperty.enumValueIndex]
                    : null;
                var expectedFeature = string.IsNullOrWhiteSpace(entry.featureType) ? "none" : entry.featureType.Trim();
                if (featureProperty != null && featureProperty.propertyType == SerializedPropertyType.Enum &&
                    Array.IndexOf(featureProperty.enumNames, expectedFeature) < 0)
                    report.failures.Add($"featureType '{expectedFeature}' không có trong enum của field FeatureType");
                report.Check(actualFeature == expectedFeature, "FeatureType",
                    $"{actualFeature ?? "null"} (expect {expectedFeature})");
                var mainUi = so.FindProperty("MainUI")?.objectReferenceValue as Transform;
                var activeBranch = popupOn ? popup : full;
                report.Check(mainUi != null && mainUi == activeBranch, "MainUI = active branch", mainUi != null ? mainUi.name : "null");
                var closeCount = so.FindProperty("_closeButtons")?.arraySize ?? 0;
                report.Check(!expectPopup || closeCount >= 1, "_closeButtons >= 1 (popup)", closeCount.ToString());
                report.Check(controller.GetType().AssemblyQualifiedName != null &&
                             controller.GetType().FullName == entry.controllerType.Split(',')[0].Trim(),
                    "controller type", controller.GetType().FullName);
            }

            // Font: mọi TMP = font khai trong settings (trống thì bỏ qua check)
            if (settings.font != null)
            {
                var wrongFonts = new List<string>();
                foreach (var text in prefab.GetComponentsInChildren<TMP_Text>(true))
                    if (text.font != settings.font)
                        wrongFonts.Add($"{text.name}: {(text.font != null ? text.font.name : "null")}");
                report.Check(wrongFonts.Count == 0, $"all TMP font = {settings.font.name}",
                    wrongFonts.Count == 0 ? null : string.Join("; ", wrongFonts));
            }
            else
            {
                report.checks.Add("font: settings chưa khai font — bỏ qua check font");
            }

            // Missing script
            var missing = 0;
            foreach (var t in prefab.GetComponentsInChildren<Transform>(true))
                missing += GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject);
            report.Check(missing == 0, "no missing script", missing.ToString());

            if (snapshot)
            {
                var snapshotDir = string.IsNullOrWhiteSpace(settings.snapshotFolder)
                    ? DEFAULT_SNAPSHOT_DIR
                    : settings.snapshotFolder;
                var png = Path.Combine(snapshotDir, frameName + ".png").Replace('\\', '/');
                report.snapshot = Snapshot(prefabPath, png);
                report.Check(report.snapshot != null, "snapshot", report.snapshot ?? "failed");
            }

            report.pass = report.failures.Count == 0;
        }

        /// <summary>
        ///     Chụp prefab 1080×2400 không cần Play: dựng trong preview scene với camera riêng
        ///     (ScreenSpaceCamera), không đụng scene đang mở. Trả đường dẫn PNG hoặc null.
        /// </summary>
        public static string Snapshot(string prefabPath, string pngPath) =>
            Snapshot(prefabPath, pngPath, SNAPSHOT_WIDTH, SNAPSHOT_HEIGHT);

        /// <summary>Chụp ở khổ tuỳ ý (vd 1080×1440 cho 4:3) — kiểm tỉ lệ nhanh không cần Game View.</summary>
        public static string Snapshot(string prefabPath, string pngPath, int width, int height)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null) return null;

            var scene = EditorSceneManager.NewPreviewScene();
            GameObject cameraObject = null;
            RenderTexture renderTexture = null;
            Texture2D texture = null;
            var previousActive = RenderTexture.active;
            try
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                cameraObject = new GameObject("__figmaSnapCam");
                SceneManager.MoveGameObjectToScene(cameraObject, scene);
                var camera = cameraObject.AddComponent<Camera>();
                camera.scene = scene;
                camera.orthographic = true;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.08f, 0.08f, 0.12f);
                camera.cullingMask = ~0;
                camera.nearClipPlane = 0.1f;
                camera.farClipPlane = 100f;

                renderTexture = new RenderTexture(width, height, 24);
                camera.targetTexture = renderTexture;

                var canvas = instance.GetComponent<Canvas>();
                if (canvas != null)
                {
                    canvas.renderMode = RenderMode.ScreenSpaceCamera;
                    canvas.worldCamera = camera;
                    canvas.planeDistance = 10f;
                }
                Canvas.ForceUpdateCanvases();
                camera.Render();

                RenderTexture.active = renderTexture;
                texture = new Texture2D(width, height, TextureFormat.RGB24, false);
                texture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                texture.Apply();

                var directory = Path.GetDirectoryName(pngPath);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                File.WriteAllBytes(pngPath, texture.EncodeToPNG());
                return pngPath;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[FigmaImport] Snapshot '{prefabPath}' lỗi: {e.Message}");
                return null;
            }
            finally
            {
                RenderTexture.active = previousActive;
                if (texture != null) Object.DestroyImmediate(texture);
                if (renderTexture != null) renderTexture.Release();
                if (cameraObject != null) Object.DestroyImmediate(cameraObject);
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        /// <summary>
        ///     Dấu vân cấu trúc của prefab: đường dẫn + component + mọi giá trị serialize, tham chiếu
        ///     object quy về "tên/kiểu" thay cho fileID. Hai lần import cùng đầu vào phải cho cùng dấu
        ///     vân dù Unity cấp fileID mới cho nhánh thân dựng lại (byte prefab vì thế không giống nhau).
        /// </summary>
        public static string StructuralFingerprint(string prefabPath)
        {
            var dump = StructuralDump(prefabPath);
            if (dump == null) return null;
            using var md5 = System.Security.Cryptography.MD5.Create();
            return BitConverter.ToString(md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes(dump))).Replace("-", string.Empty);
        }

        /// <summary>Bản text của <see cref="StructuralFingerprint" /> — diff hai bản để thấy đúng property đổi.</summary>
        public static string StructuralDump(string prefabPath)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null) return null;
            var sb = new System.Text.StringBuilder();
            foreach (var t in prefab.GetComponentsInChildren<Transform>(true))
            {
                var path = PathOf(prefab.transform, t);
                sb.Append(path).Append(" active=").Append(t.gameObject.activeSelf).Append('\n');
                foreach (var component in t.GetComponents<Component>())
                {
                    if (component == null)
                    {
                        sb.Append(path).Append(" <missing>\n");
                        continue;
                    }
                    sb.Append(path).Append(" :: ").Append(component.GetType().FullName).Append('\n');
                    var so = new SerializedObject(component);
                    var iterator = so.GetIterator();
                    var enterChildren = true;
                    while (iterator.Next(enterChildren))
                    {
                        // Không bước vào con của giá trị đã ghi (PPtr có m_FileID/m_PathID — chính là fileID
                        // đổi mỗi lần dựng lại); chỉ Generic chưa xử lý mới đi sâu.
                        enterChildren = false;
                        if (iterator.propertyPath == "m_Script" || iterator.propertyPath.StartsWith("m_CorrespondingSourceObject") ||
                            iterator.propertyPath.StartsWith("m_PrefabInstance") || iterator.propertyPath.StartsWith("m_PrefabAsset"))
                        {
                            enterChildren = false;
                            continue;
                        }
                        sb.Append("  ").Append(iterator.propertyPath).Append('=');
                        switch (iterator.propertyType)
                        {
                            case SerializedPropertyType.ObjectReference:
                                var reference = iterator.objectReferenceValue;
                                if (reference == null) sb.Append("null");
                                else
                                {
                                    var referencePath = AssetDatabase.GetAssetPath(reference);
                                    sb.Append(reference.GetType().Name).Append(':')
                                        .Append(string.IsNullOrEmpty(referencePath) ? reference.name : referencePath);
                                }
                                break;
                            case SerializedPropertyType.Integer: sb.Append(iterator.longValue); break;
                            case SerializedPropertyType.Boolean: sb.Append(iterator.boolValue); break;
                            case SerializedPropertyType.Float: sb.Append(iterator.doubleValue.ToString("R")); break;
                            case SerializedPropertyType.String: sb.Append(iterator.stringValue); break;
                            case SerializedPropertyType.Color: sb.Append(iterator.colorValue); break;
                            case SerializedPropertyType.Enum: sb.Append(iterator.intValue); break;
                            case SerializedPropertyType.Vector2: sb.Append(iterator.vector2Value); break;
                            case SerializedPropertyType.Vector3: sb.Append(iterator.vector3Value); break;
                            case SerializedPropertyType.Vector4: sb.Append(iterator.vector4Value); break;
                            case SerializedPropertyType.Rect: sb.Append(iterator.rectValue); break;
                            case SerializedPropertyType.Quaternion: sb.Append(iterator.quaternionValue); break;
                            case SerializedPropertyType.ArraySize: sb.Append(iterator.intValue); break;
                            default: enterChildren = iterator.hasChildren; break;
                        }
                        sb.Append('\n');
                    }
                }
            }
            return sb.ToString();
        }

        private static string PathOf(Transform root, Transform node)
        {
            var segments = new List<string>();
            for (var current = node; current != null && current != root; current = current.parent) segments.Add(current.name);
            segments.Add(root.name);
            segments.Reverse();
            return string.Join("/", segments);
        }

        private static int Count(string haystack, string needle)
        {
            var count = 0;
            var index = 0;
            while ((index = haystack.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += needle.Length;
            }
            return count;
        }

        private static string Describe(Transform root)
        {
            var names = new List<string>();
            for (var i = 0; i < root.childCount; i++) names.Add($"[{i}] {root.GetChild(i).name}");
            return string.Join(", ", names);
        }
    }
}
#endif
