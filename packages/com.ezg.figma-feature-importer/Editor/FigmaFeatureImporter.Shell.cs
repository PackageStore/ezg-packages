#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace Ezg.FigmaFeatureImporter.Editor
{
    /// <summary>Bước 1–2, 9–10 (§5.2): vỏ variant, idempotent, dọn component thừa, neo thân.</summary>
    public static partial class FigmaFeatureImporter
    {
        /// <summary>
        ///     Tạo variant `screen_template` tại <paramref name="outputPrefab" /> nếu chưa có. Dựng trong
        ///     preview scene để không làm bẩn scene đang mở. Đã có thì chỉ kiểm là variant.
        /// </summary>
        private static bool EnsureVariant(FigmaFeatureImportSettings settings, string outputPrefab, ImportReport report)
        {
            if (File.Exists(outputPrefab))
            {
                var existing = AssetDatabase.LoadAssetAtPath<GameObject>(outputPrefab);
                if (existing == null)
                {
                    report.Warn($"'{outputPrefab}' tồn tại nhưng không load được như prefab.");
                    return false;
                }
                if (PrefabUtility.GetPrefabAssetType(existing) != PrefabAssetType.Variant)
                    report.Warn($"'{outputPrefab}' không phải variant — vỏ không phải screen_template, importer vẫn chạy nhưng audit sẽ fail.");
                return true;
            }

            var template = AssetDatabase.LoadAssetAtPath<GameObject>(settings.screenTemplatePath);
            if (template == null)
            {
                report.Warn($"Không thấy template {settings.screenTemplatePath}.");
                return false;
            }

            var directory = Path.GetDirectoryName(outputPrefab);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            var scene = EditorSceneManager.NewPreviewScene();
            try
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(template, scene);
                instance.name = Path.GetFileNameWithoutExtension(outputPrefab);
                PrefabUtility.SaveAsPrefabAsset(instance, outputPrefab, out var success);
                if (!success) report.Warn($"Không tạo được variant tại '{outputPrefab}'.");
                else report.notes.Add($"Tạo variant mới {outputPrefab} từ {System.IO.Path.GetFileNameWithoutExtension(settings.screenTemplatePath)}.");
                return success;
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        /// <summary>
        ///     Bước 1: bật đúng nhánh (popup / full screen), xác định cha của thân, áp các override vỏ
        ///     mà create-ui quy định (replaceTemplateFrame, keepFullScreenChrome, fullScreenBackdrop,
        ///     backgroundAlpha). Override nằm trên variant, template gốc không đổi.
        /// </summary>
        private static void ConfigureShell(Ctx ctx)
        {
            var settings = ctx.Settings;
            var root = ctx.Root.transform;
            var background = root.Find(settings.backgroundNode);
            var popup = root.Find(settings.popupNode);
            var full = root.Find(settings.fullScreenNode);
            if (popup == null || full == null)
                throw new System.InvalidOperationException(
                    $"Vỏ không có '{settings.popupNode}' / '{settings.fullScreenNode}' — vỏ " +
                    $"'{settings.screenTemplatePath}' đã đổi, hoặc tên nhánh trong settings sai.");

            var entry = ctx.Entry;
            var isPopup = entry.layout == FigmaScreenLayout.Popup;
            popup.gameObject.SetActive(isPopup);
            full.gameObject.SetActive(!isPopup);
            ctx.ActiveBranch = isPopup ? popup : full;

            if (isPopup)
            {
                // Chuỗi node từ nhánh popup tới node nhận thân; phần tử cuối là thân.
                var chain = ResolveChain(popup, settings.popupBodyPath);
                if (chain.Count == 0)
                    throw new System.InvalidOperationException(
                        $"Không thấy {settings.popupNode}/{settings.popupBodyPath}.");
                var body = chain[chain.Count - 1];
                ctx.BodyParent = body;

                if (entry.replaceTemplateFrame)
                {
                    foreach (var frameNode in settings.popupFrameNodes ?? new List<string>())
                        SetActive(FindByPath(popup, frameNode), false);
                    // Ảnh nền của node ngay trên thân (khung template) — Figma đã vẽ khung riêng.
                    if (chain.Count >= 2)
                    {
                        var contentImage = chain[chain.Count - 2].GetComponent<Image>();
                        if (contentImage != null) contentImage.enabled = false;
                    }
                    foreach (var link in chain)
                    {
                        Stretch(link as RectTransform);
                        DisableLayoutDrivers(link);
                    }
                }
            }
            else
            {
                var content = FindByPath(full, settings.fullScreenContentNode);
                if (content == null)
                    throw new System.InvalidOperationException(
                        $"Không thấy {settings.fullScreenNode}/{settings.fullScreenContentNode}.");
                ctx.BodyParent = content;

                if (!entry.keepFullScreenChrome)
                {
                    foreach (var chrome in settings.fullScreenChromeNodes ?? new List<string>())
                        SetActive(FindByPath(full, chrome), false);
                    Stretch(content as RectTransform);
                }

                var backdrop = full.GetComponent<Image>();
                if (backdrop != null) backdrop.enabled = entry.fullScreenBackdrop;
            }

            if (background != null && entry.backgroundAlpha <= 0f)
            {
                var image = background.GetComponent<Image>();
                if (image != null)
                {
                    var color = image.color;
                    color.a = 0f;
                    image.color = color;
                }
            }
        }

        /// <summary>
        ///     Bước 2 (idempotent): xoá mọi nhánh thân cũ tên = frame trên CẢ prefab (đổi layout
        ///     Popup↔FullScreen thì thân cũ nằm bên nhánh kia). Node của vỏ là phần của prefab
        ///     instance gốc nên không bao giờ bị xoá nhầm.
        /// </summary>
        private static void RemoveOldBody(Ctx ctx, string frameName)
        {
            var stale = new List<GameObject>();
            foreach (var t in ctx.Root.GetComponentsInChildren<Transform>(true))
            {
                if (t.name != frameName || t == ctx.Root.transform) continue;
                if (PrefabUtility.IsPartOfPrefabInstance(t.gameObject)) continue;
                stale.Add(t.gameObject);
            }
            foreach (var go in stale) DestroyNow(go);
        }

        /// <summary>
        ///     Bước 9: gỡ LayoutElement bridge gắn cho mọi node (trừ con của LayoutGroup),
        ///     ContentSizeFitter sót, marker bridge, Button bridge tự gắn theo tên (D6). Không đụng
        ///     ruột template project.
        /// </summary>
        private static void Cleanup(Ctx ctx)
        {
            var removedButtons = 0;
            foreach (var t in ctx.Body.GetComponentsInChildren<Transform>(true))
            {
                if (PrefabUtility.IsPartOfPrefabInstance(t.gameObject)) continue; // ruột template
                if (ctx.TemplateRoots.Contains(t)) continue;

                foreach (var component in t.GetComponents<Component>())
                {
                    if (component == null) continue;
                    switch (component)
                    {
                        case LayoutElement layoutElement:
                            var parentHasGroup = t.parent != null && t.parent.GetComponent<LayoutGroup>() != null;
                            if (!parentHasGroup) DestroyNow(layoutElement);
                            break;
                        case ContentSizeFitter fitter:
                            DestroyNow(fitter);
                            break;
                        case Button button:
                            DestroyNow(button);
                            removedButtons++;
                            break;
                        default:
                            var typeName = component.GetType().Name;
                            if (typeName == "FigmaNodeObject" || typeName == "FigmaComponentNodeMarker" ||
                                typeName == "FigmaPrototypeFlowButton")
                                DestroyNow(component);
                            break;
                    }
                }
            }
            if (removedButtons > 0)
                ctx.Report.notes.Add($"Gỡ {removedButtons} Button bridge gắn theo tên (nút phải là template — khai buttonNodes).");
        }

        /// <summary>
        ///     Bước 10: thân = khổ frame neo tâm (CenterFixed, nếp PsdCanvas.Middle) hoặc trải theo
        ///     canvas (Stretch). Popup không replaceTemplateFrame thì thân đứng ngoài dòng layout.
        /// </summary>
        private static void AnchorBody(Ctx ctx)
        {
            var rt = ctx.Body as RectTransform;
            if (rt == null) return;
            rt.localRotation = Quaternion.identity;
            rt.localScale = Vector3.one;
            if (ctx.Entry.bodyAnchor == FigmaBodyAnchor.Stretch)
            {
                Stretch(rt);
            }
            else
            {
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = ctx.FrameSize;
                rt.anchoredPosition = Vector2.zero;
            }

            if (ctx.Entry.layout == FigmaScreenLayout.Popup && !ctx.Entry.replaceTemplateFrame)
            {
                var layoutElement = ctx.Body.GetComponent<LayoutElement>();
                if (layoutElement == null) layoutElement = ctx.Body.gameObject.AddComponent<LayoutElement>();
                layoutElement.ignoreLayout = true;
            }
        }

        private static void Stretch(RectTransform rt)
        {
            if (rt == null) return;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        private static void DisableLayoutDrivers(Transform t)
        {
            foreach (var group in t.GetComponents<LayoutGroup>()) group.enabled = false;
            foreach (var fitter in t.GetComponents<ContentSizeFitter>()) fitter.enabled = false;
            foreach (var element in t.GetComponents<LayoutElement>()) element.enabled = false;
        }

        private static void SetActive(Transform t, bool active)
        {
            if (t != null) t.gameObject.SetActive(active);
        }

        /// <summary>Node theo đường dẫn '/' dưới <paramref name="root" /> (rỗng = chính root).</summary>
        private static Transform FindByPath(Transform root, string path)
        {
            if (root == null || string.IsNullOrWhiteSpace(path)) return null;
            var current = root;
            foreach (var segment in path.Split('/'))
            {
                if (string.IsNullOrEmpty(segment)) continue;
                current = current.Find(segment);
                if (current == null) return null;
            }
            return current;
        }

        /// <summary>Mọi node trên đường đi tới <paramref name="path" /> (rỗng nếu đứt giữa chừng).</summary>
        private static List<Transform> ResolveChain(Transform root, string path)
        {
            var chain = new List<Transform>();
            if (root == null || string.IsNullOrWhiteSpace(path)) return chain;
            var current = root;
            foreach (var segment in path.Split('/'))
            {
                if (string.IsNullOrEmpty(segment)) continue;
                current = current.Find(segment);
                if (current == null) return new List<Transform>();
                chain.Add(current);
            }
            return chain;
        }
    }
}
#endif
