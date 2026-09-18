#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using UnityFigmaBridge.Editor.FigmaApi;
using UnityFigmaBridge.Editor.Settings;
using UnityFigmaBridge.Editor.Utils;
using Object = UnityEngine.Object;

namespace Ezg.FigmaFeatureImporter.Editor
{
    /// <summary>Kết quả một lần import; <see cref="ToString" /> trả JSON để đọc qua MCP.</summary>
    [Serializable]
    public sealed class ImportReport
    {
        public string frame;
        public string outputPrefab;
        public bool ok;
        public int fieldsWired;
        public int spritesReused;
        public int spritesCopied;
        public string generatedFile;
        public List<string> warnings = new();
        public List<string> unresolvedBindings = new();
        public List<string> needsPngExport = new();
        public List<string> unnamedTexts = new();
        public List<string> renamedDuplicates = new();
        public List<string> mappedTemplates = new();
        public List<string> notes = new();

        public override string ToString() => JsonUtility.ToJson(this, true);

        internal void Warn(string message)
        {
            warnings.Add(message);
            Debug.LogWarning($"[FigmaImport] {frame}: {message}");
        }
    }

    /// <summary>
    ///     Importer generic Figma → màn hình feature (TechSpec/FigmaImport-TechSpec.md §5). Đầu vào là
    ///     prefab thô do bridge ghi; đầu ra là variant `screen_template`. Hai đầu ra:
    ///     <list type="bullet">
    ///       <item><b>Tại chỗ</b> (<see cref="ImportInPlace" />, mọi frame): chính
    ///       <c>Assets/Figma/Screens/&lt;Frame&gt;.prefab</c> trở thành variant — mở ra là dùng được cho
    ///       planning/backlog. Bản thô của bridge được chụp sang <c>rawScreensFolder</c> trước
    ///       khi ghi đè để lần chạy sau (menu, Run Post-Processors) vẫn dựng lại được từ nguồn sạch.
    ///       Frame không có entry dùng <see cref="DefaultEntry" /> (Popup nếu prefab thô có node theo quy ước
    ///       `Bg_Dim`, không thì FullScreen; thân CenterFixed, không controller).</item>
    ///       <item><b>Bản ship</b> (<see cref="Import" />, frame có entry <c>enabled</c>): variant tại
    ///       <c>outputPrefab</c> trong Features, sprite rời khỏi Assets/Figma, controller + codegen.</item>
    ///     </list>
    ///     Mọi khác biệt giữa các màn là DATA trong <see cref="FigmaFeatureImportSettings" />, không phải
    ///     code theo màn.
    ///
    ///     Thứ tự dựng (một file partial cho mỗi nhóm bước):
    ///     Shell (vỏ variant, idempotent, thân) → Templates (instance component → template project,
    ///     buttonNodes) → Sprites (FigmaImage → Image, sprite rời khỏi Assets/Figma) → Text (font,
    ///     rect cố định) → Controller (add, FeatureType, MainUI, _closeButtons, auto-wire, codegen).
    /// </summary>
    public static partial class FigmaFeatureImporter
    {
        private const string LOG = "[FigmaImport]";

        /// <summary>Trạng thái của một lần import, truyền giữa các bước.</summary>
        private sealed class Ctx
        {
            public FigmaFeatureImportSettings Settings;
            public FigmaScreenEntry Entry;
            public ImportReport Report;
            public UnityFigmaBridgeSettings Bridge;
            /// <summary>Đường dẫn màn của bridge (Assets/Figma/Screens/&lt;Frame&gt;.prefab) — sidecar nằm cạnh.</summary>
            public string RawPrefabPath;
            /// <summary>Prefab thô thật sự được instantiate (bản chụp trong <c>rawScreensFolder</c>).</summary>
            public string RawSourcePath;
            public GameObject Root;
            public Transform ActiveBranch;
            public Transform BodyParent;
            public Transform Body;
            public Vector2 FrameSize;
            public readonly List<InstanceInfo> Instances = new();
            public readonly List<Button> CloseButtons = new();
            public readonly HashSet<Transform> TemplateRoots = new();
            public readonly HashSet<Transform> ExplicitlyBound = new();
        }

        /// <summary>Một instance component Figma trong prefab thô, ghi TRƯỚC khi unpack.</summary>
        internal sealed class InstanceInfo
        {
            public string NodeName;
            public string HierarchyPath;
            public string ComponentPrefabPath;
            public string[] SourcePathChain;
            public Transform Node;
        }

        /// <summary>Import mọi entry <c>enabled</c>.</summary>
        public static List<ImportReport> ImportAllEnabled()
        {
            var settings = FigmaFeatureImportSettings.LoadOrCreate();
            var reports = new List<ImportReport>();
            foreach (var entry in settings.screens)
            {
                if (entry == null || !entry.enabled || string.IsNullOrEmpty(entry.figmaScreen)) continue;
                reports.Add(Import(entry.figmaScreen));
            }
            return reports;
        }

        /// <summary>
        ///     Bản ship: dựng lại màn có entry từ prefab thô của frame <paramref name="frameName" /> vào
        ///     <c>outputPrefab</c> (variant `screen_template`, sprite rời Assets/Figma, controller, codegen).
        /// </summary>
        public static ImportReport Import(string frameName)
        {
            var report = new ImportReport { frame = frameName };
            if (!Preflight(frameName, report, out var settings, out var bridge, out var rawPath)) return report;

            var entry = settings.FindScreen(frameName);
            if (entry == null)
            {
                report.Warn($"Không có entry '{frameName}' trong {settings.AssetPath()}.");
                return report;
            }
            if (string.IsNullOrEmpty(entry.outputPrefab) || !entry.outputPrefab.EndsWith(".prefab", StringComparison.Ordinal))
            {
                report.Warn("outputPrefab trống hoặc không phải .prefab.");
                return report;
            }
            report.outputPrefab = entry.outputPrefab;

            var rawAsset = ResolveRawSource(settings, rawPath, report, out var rawSourcePath);
            if (rawAsset == null) return report;

            if (!EnsureVariant(settings, entry.outputPrefab, report)) return report;

            var ctx = new Ctx
            {
                Settings = settings,
                Entry = entry,
                Report = report,
                Bridge = bridge,
                RawPrefabPath = rawPath,
                RawSourcePath = rawSourcePath
            };

            var codegenWritten = false;
            ctx.Root = PrefabUtility.LoadPrefabContents(entry.outputPrefab);
            try
            {
                ctx.Root.name = Path.GetFileNameWithoutExtension(entry.outputPrefab);
                codegenWritten = RunPipeline(ctx, rawAsset, relocateSprites: true, codegen: true);
                Save(ctx, entry.outputPrefab);
            }
            catch (Exception e)
            {
                report.ok = false;
                report.Warn($"Exception: {e}");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(ctx.Root);
            }

            if (codegenWritten)
            {
                // D8: file partial mới phải compile xong mới có field để wire → hẹn lượt 2 sau reload.
                FigmaImportContinuation.Schedule(frameName);
                report.notes.Add("Đã sinh partial mới — sau khi compile importer tự chạy lại để wire field.");
                AssetDatabase.ImportAsset(report.generatedFile);
                AssetDatabase.Refresh();
            }

            Debug.Log($"{LOG} {frameName} → {entry.outputPrefab}\n{report}");
            return report;
        }

        /// <summary>
        ///     Tại chỗ: biến chính <c>Assets/Figma/Screens/&lt;Frame&gt;.prefab</c> thành variant
        ///     `screen_template`. Có entry thì theo entry (kể cả controller), không thì
        ///     <see cref="DefaultEntry" />. Sprite giữ nguyên trong Assets/Figma, không codegen. Bản thô
        ///     được chụp sang <c>rawScreensFolder</c> nên chạy lại bao nhiêu lần cũng được.
        /// </summary>
        public static ImportReport ImportInPlace(string frameName)
        {
            var report = new ImportReport { frame = frameName };
            if (!Preflight(frameName, report, out var settings, out var bridge, out var rawPath)) return report;
            report.outputPrefab = rawPath;

            var rawAsset = ResolveRawSource(settings, rawPath, report, out var rawSourcePath);
            if (rawAsset == null) return report;

            var entry = settings.FindScreen(frameName);
            if (entry == null)
            {
                entry = DefaultEntry(settings, frameName, rawAsset);
                var marker = FindPopupMarker(settings, rawAsset);
                report.notes.Add(marker != null
                    ? $"Không có entry — nhận POPUP theo node '{marker}' (quy ước Bg_Dim), thân CenterFixed, không controller. Thêm entry để tinh chỉnh."
                    : "Không có entry, không thấy node Bg_Dim/Bg_Popup*/Container_Popup — dựng FULL SCREEN, thân CenterFixed 1080×2400, không controller. Là popup thì thêm lớp tối 'Bg_Dim' trong Figma (hoặc khai entry).");
            }

            var template = AssetDatabase.LoadAssetAtPath<GameObject>(settings.screenTemplatePath);
            if (template == null)
            {
                report.Warn($"Không thấy vỏ {settings.screenTemplatePath} — sửa screenTemplatePath trong settings.");
                return report;
            }

            var ctx = new Ctx
            {
                Settings = settings,
                Entry = entry,
                Report = report,
                Bridge = bridge,
                RawPrefabPath = rawPath,
                RawSourcePath = rawSourcePath
            };

            // Vỏ dựng trong preview scene: instance của screen_template + thân → SaveAsPrefabAsset ra variant,
            // ghi đè đúng đường dẫn bridge (giữ GUID nên Pages/<Page>.prefab vẫn trỏ tới màn này).
            var scene = EditorSceneManager.NewPreviewScene();
            try
            {
                ctx.Root = (GameObject)PrefabUtility.InstantiatePrefab(template, scene);
                ctx.Root.name = Path.GetFileNameWithoutExtension(rawPath);
                RunPipeline(ctx, rawAsset, relocateSprites: false, codegen: false);
                Save(ctx, rawPath);
            }
            catch (Exception e)
            {
                report.ok = false;
                report.Warn($"Exception: {e}");
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
            }

            Debug.Log($"{LOG} {frameName} → {rawPath} (variant tại chỗ)\n{report}");
            return report;
        }

        /// <summary>
        ///     Một frame bridge vừa ghi: chụp raw, dựng bản ship nếu có entry enabled, rồi variant tại chỗ.
        ///     Hook và menu đều đi qua đây.
        /// </summary>
        public static List<ImportReport> ProcessBridgeScreen(string frameName)
        {
            var reports = new List<ImportReport>();
            var entry = FigmaFeatureImportSettings.LoadOrCreate().FindScreen(frameName);
            if (entry != null && entry.enabled) reports.Add(Import(frameName));
            reports.Add(ImportInPlace(frameName));
            return reports;
        }

        /// <summary>Dựng lại variant tại chỗ cho mọi màn đang có trong thư mục Screens của bridge.</summary>
        public static List<ImportReport> RebuildAllInPlace()
        {
            var reports = new List<ImportReport>();
            var bridge = UnityFigmaBridgeSettingsProvider.FindUnityBridgeSettingsAsset();
            if (bridge == null)
            {
                Debug.LogWarning($"{LOG} Không thấy UnityFigmaBridgeSettings.asset — bridge chưa cấu hình.");
                return reports;
            }
            FigmaPaths.Configure(bridge, ResolveDocumentName(bridge));
            var folder = FigmaPaths.FigmaScreenPrefabFolder;
            if (!Directory.Exists(folder)) return reports;

            foreach (var file in Directory.GetFiles(folder, "*.prefab"))
            {
                var prefabPath = file.Replace('\\', '/');
                var frameName = Path.GetFileNameWithoutExtension(prefabPath);
                var sidecar = UnityFigmaBridge.Editor.PostProcess.FigmaInstanceSidecar.Read(prefabPath);
                if (!string.IsNullOrEmpty(sidecar?.screen)) frameName = sidecar.screen;
                reports.Add(ImportInPlace(frameName));
            }
            return reports;
        }

        /// <summary>
        ///     Quy ước với designer (chốt 2026-09-08): popup luôn có lớp tối tên <c>Bg_Dim</c>. Khung popup
        ///     đặt tên <c>Bg_Popup*</c> / <c>Container_Popup</c> cũng được tính. Không có gì → full screen.
        ///     Hình học không dùng được: designer vẽ cả <c>Bg_Screen</c> phủ hết artboard sau popup.
        /// </summary>
        private const string DEFAULT_POPUP_MARKER = "^(Bg_Dim|Bg_Popup|Container_Popup)";
        private static string s_PopupMarkerPattern;
        private static Regex s_PopupMarker;

        /// <summary>Regex nhận popup, biên dịch lại khi settings đổi pattern.</summary>
        private static Regex PopupMarker(FigmaFeatureImportSettings settings)
        {
            var pattern = settings != null && !string.IsNullOrWhiteSpace(settings.popupMarkerPattern)
                ? settings.popupMarkerPattern
                : DEFAULT_POPUP_MARKER;
            if (s_PopupMarker != null && s_PopupMarkerPattern == pattern) return s_PopupMarker;
            try
            {
                s_PopupMarker = new Regex(pattern, RegexOptions.CultureInvariant);
            }
            catch (ArgumentException e)
            {
                Debug.LogWarning($"{LOG} popupMarkerPattern '{pattern}' không hợp lệ ({e.Message}) — dùng mặc định.");
                pattern = DEFAULT_POPUP_MARKER;
                s_PopupMarker = new Regex(pattern, RegexOptions.CultureInvariant);
            }
            s_PopupMarkerPattern = pattern;
            return s_PopupMarker;
        }

        /// <summary>
        ///     Entry ngầm định cho frame chưa khai. Layout đọc từ prefab thô theo quy ước
        ///     <c>popupMarkerPattern</c>: có node khớp → Popup (Figma đã vẽ khung riêng nên
        ///     <c>replaceTemplateFrame</c>), không → FullScreen. Thân giữ đúng khổ frame neo tâm
        ///     (CenterFixed) để hình học trùng bản raw ở mọi canvas — node Figma neo LEFT/TOP nên Stretch
        ///     chỉ làm thân rộng ra mà nội dung dồn trái (Pilot §7). Không chrome template, không
        ///     controller. Màn cần bám mép canvas thì khai entry với Stretch.
        /// </summary>
        /// <param name="raw">Prefab thô; null → tự load <c>RawScreens/&lt;frame&gt;.prefab</c> (audit gọi không có raw).</param>
        internal static FigmaScreenEntry DefaultEntry(FigmaFeatureImportSettings settings, string frameName,
            GameObject raw = null)
        {
            raw ??= AssetDatabase.LoadAssetAtPath<GameObject>($"{RawScreensFolder(settings)}/{frameName}.prefab");
            var isPopup = FindPopupMarker(settings, raw) != null;
            return new FigmaScreenEntry
            {
                figmaScreen = frameName,
                enabled = true,
                layout = isPopup ? FigmaScreenLayout.Popup : FigmaScreenLayout.FullScreen,
                bodyAnchor = FigmaBodyAnchor.CenterFixed,
                replaceTemplateFrame = isPopup,
                keepFullScreenChrome = false,
                fullScreenBackdrop = false,
                keepFrameFill = false,
                controllerType = null,
                featureType = null
            };
        }

        /// <summary>Tên node đầu tiên trong prefab thô khớp <c>popupMarkerPattern</c>; null = không phải popup.</summary>
        internal static string FindPopupMarker(FigmaFeatureImportSettings settings, GameObject raw)
        {
            if (raw == null) return null;
            var marker = PopupMarker(settings);
            foreach (var t in raw.GetComponentsInChildren<Transform>(true))
                if (t != raw.transform && marker.IsMatch(t.name)) return t.name;
            return null;
        }

        /// <summary>Kiểm Play mode, settings, bridge; trả đường dẫn màn của bridge (null nếu frame bị exclude).</summary>
        private static bool Preflight(string frameName, ImportReport report, out FigmaFeatureImportSettings settings,
            out UnityFigmaBridgeSettings bridge, out string rawPath)
        {
            settings = null;
            bridge = null;
            rawPath = null;
            if (Application.isPlaying)
            {
                report.Warn("Đang Play mode — importer từ chối chạy (sửa prefab lúc Play là mất khi thoát).");
                return false;
            }

            settings = FigmaFeatureImportSettings.LoadOrCreate();
            bridge = UnityFigmaBridgeSettingsProvider.FindUnityBridgeSettingsAsset();
            if (bridge == null)
            {
                report.Warn("Không thấy UnityFigmaBridgeSettings.asset — bridge chưa cấu hình.");
                return false;
            }
            FigmaPaths.Configure(bridge, ResolveDocumentName(bridge));

            rawPath = ScreenPrefabPath(frameName);
            if (rawPath == null)
            {
                report.Warn($"Frame '{frameName}' đang ExcludeFromImport trong bridge settings và chưa có prefab trên đĩa — bật lại rồi Sync.");
                return false;
            }
            return true;
        }

        /// <summary>
        ///     Đường dẫn màn của bridge. Frame đang ExcludeFromImport nhưng prefab còn trên đĩa (Sync trước
        ///     đó) vẫn dựng lại được — chỉ null khi không có gì để dựng. Gọi sau <see cref="FigmaPaths.Configure" />.
        /// </summary>
        internal static string ScreenPrefabPath(string frameName)
        {
            var path = FigmaPaths.GetPathForScreenPrefab(new Node { name = frameName, type = NodeType.FRAME }, 0);
            if (path != null) return path;
            var onDisk = $"{FigmaPaths.FigmaScreenPrefabFolder}/{frameName}.prefab";
            return File.Exists(onDisk) ? onDisk : null;
        }

        /// <summary>Prefab tại đường dẫn bridge đã là variant `screen_template` (đã qua <see cref="ImportInPlace" />).</summary>
        internal static bool IsScreenTemplateVariant(FigmaFeatureImportSettings settings, GameObject asset)
        {
            if (asset == null || PrefabUtility.GetPrefabAssetType(asset) != PrefabAssetType.Variant) return false;
            var source = PrefabUtility.GetCorrespondingObjectFromSource(asset);
            return source != null && AssetDatabase.GetAssetPath(source) == settings.screenTemplatePath;
        }

        /// <summary>
        ///     Nguồn thô để dựng: nếu <paramref name="rawPath" /> còn là prefab thô của bridge (vừa Sync) thì
        ///     chụp sang <c>rawScreensFolder</c> và dùng bản chụp (để ghi đè tại chỗ không tự tham
        ///     chiếu); nếu đã là variant thì dùng bản chụp có sẵn. Không có cả hai → cần Sync.
        /// </summary>
        private static GameObject ResolveRawSource(FigmaFeatureImportSettings settings, string rawPath,
            ImportReport report, out string rawSourcePath)
        {
            var rawRoot = RawScreensFolder(settings);
            rawSourcePath = $"{rawRoot}/{Path.GetFileName(rawPath)}";
            var main = AssetDatabase.LoadAssetAtPath<GameObject>(rawPath);

            if (main != null && !IsScreenTemplateVariant(settings, main))
            {
                EnsureAssetFolder(rawRoot);
                if (File.Exists(rawSourcePath)) AssetDatabase.DeleteAsset(rawSourcePath);
                if (!AssetDatabase.CopyAsset(rawPath, rawSourcePath))
                {
                    report.Warn($"Không chụp được bản raw '{rawPath}' → '{rawSourcePath}'.");
                    return null;
                }
                AssetDatabase.ImportAsset(rawSourcePath, ImportAssetOptions.ForceSynchronousImport);
                report.notes.Add($"Chụp bản raw của bridge sang {rawSourcePath}.");
            }
            else if (!File.Exists(rawSourcePath))
            {
                report.Warn(main == null
                    ? $"Chưa có prefab thô '{rawPath}' — chạy Figma Bridge Sync (hoặc Re-import from cache) trước."
                    : $"'{rawPath}' đã là variant nhưng không còn bản raw ở {rawRoot} — Sync / Re-import from cache để dựng lại.");
                return null;
            }

            var rawAsset = AssetDatabase.LoadAssetAtPath<GameObject>(rawSourcePath);
            if (rawAsset == null) report.Warn($"Không load được bản raw '{rawSourcePath}'.");
            return rawAsset;
        }

        /// <summary>Chuỗi bước §5.2 dùng chung cho hai đầu ra. Trả true nếu codegen vừa ghi file mới.</summary>
        private static bool RunPipeline(Ctx ctx, GameObject rawAsset, bool relocateSprites, bool codegen)
        {
            ConfigureShell(ctx);
            RemoveOldBody(ctx, ctx.Entry.figmaScreen);
            InstantiateRaw(ctx, rawAsset);
            NumberDuplicateSiblings(ctx);
            ApplyRenames(ctx);
            DropNodes(ctx);
            MapTemplates(ctx);
            MapButtonNodes(ctx);
            ConvertFigmaImages(ctx);
            FixTexts(ctx);
            if (relocateSprites) RelocateSprites(ctx);
            Cleanup(ctx);
            AnchorBody(ctx);
            WireController(ctx);
            var codegenWritten = codegen && GenerateBindings(ctx);

            var removed = RemoveMissingScripts(ctx.Root);
            if (removed > 0) ctx.Report.notes.Add($"Gỡ {removed} component mất script trước khi lưu.");
            return codegenWritten;
        }

        private static void Save(Ctx ctx, string path)
        {
            PrefabUtility.SaveAsPrefabAsset(ctx.Root, path, out var saved);
            ctx.Report.ok = saved;
            if (!saved) ctx.Report.Warn($"SaveAsPrefabAsset('{path}') thất bại.");
        }

        /// <summary>Thư mục chụp bản thô; trống thì về mặc định để không ghi thẳng vào Assets/.</summary>
        internal static string RawScreensFolder(FigmaFeatureImportSettings settings)
        {
            var folder = settings != null ? settings.rawScreensFolder : null;
            if (string.IsNullOrWhiteSpace(folder)) return "Assets/Figma/RawScreens";
            return folder.Replace('\\', '/').TrimEnd('/');
        }

        private static void EnsureAssetFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var segments = path.Replace('\\', '/').Split('/');
            var current = segments[0];
            for (var i = 1; i < segments.Length; i++)
            {
                var next = $"{current}/{segments[i]}";
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, segments[i]);
                current = next;
            }
        }

        /// <summary>
        ///     Tên tài liệu Figma chỉ ảnh hưởng thư mục ImageFills; importer đọc sprite từ prefab thô
        ///     nên chỉ cần đúng root. Lấy từ thư mục con duy nhất dưới ImageFillParent khi có.
        /// </summary>
        private static string ResolveDocumentName(UnityFigmaBridgeSettings bridge)
        {
            var folders = FigmaPaths.Resolve(bridge, "Document", warnOnInvalid: false);
            if (Directory.Exists(folders.ImageFillParent))
            {
                var subFolders = Directory.GetDirectories(folders.ImageFillParent);
                if (subFolders.Length == 1) return Path.GetFileName(subFolders[0]);
            }
            return "Document";
        }

        private static int RemoveMissingScripts(GameObject root)
        {
            var removed = 0;
            foreach (var child in root.GetComponentsInChildren<Transform>(true))
                removed += GameObjectUtility.RemoveMonoBehavioursWithMissingScript(child.gameObject);
            return removed;
        }

        /// <summary>Đường dẫn '/' từ thân tới node (thân là phần tử đầu).</summary>
        private static string PathWithin(Transform root, Transform node)
        {
            var segments = new List<string>();
            var current = node;
            while (current != null && current != root)
            {
                segments.Add(current.name);
                current = current.parent;
            }
            segments.Add(root.name);
            segments.Reverse();
            return string.Join("/", segments);
        }

        /// <summary>Node nằm trong ruột một template project đã instantiate (không phải root của nó).</summary>
        private static bool IsInsideTemplate(Ctx ctx, Transform node)
        {
            var current = node.parent;
            while (current != null && current != ctx.Body)
            {
                if (ctx.TemplateRoots.Contains(current)) return true;
                current = current.parent;
            }
            return false;
        }

        /// <summary>Tìm node theo tên trong thân (theo thứ tự duyệt), bỏ qua ruột template.</summary>
        private static Transform FindNode(Ctx ctx, string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            foreach (var t in ctx.Body.GetComponentsInChildren<Transform>(true))
            {
                if (t.name != name) continue;
                if (IsInsideTemplate(ctx, t)) continue;
                return t;
            }
            return null;
        }

        private static void DestroyNow(Object target)
        {
            if (target != null) Object.DestroyImmediate(target);
        }

        /// <summary>Node theo đường dẫn '/' bắt đầu bằng tên thân (định dạng HierarchyPath của bridge/sidecar).</summary>
        private static Transform FindByHierarchyPath(Transform body, string hierarchyPath)
        {
            if (body == null || string.IsNullOrEmpty(hierarchyPath)) return null;
            var segments = hierarchyPath.Split('/');
            if (segments.Length == 0 || segments[0] != body.name) return null;
            var current = body;
            for (var i = 1; i < segments.Length && current != null; i++) current = current.Find(segments[i]);
            return current;
        }
    }
}
#endif
