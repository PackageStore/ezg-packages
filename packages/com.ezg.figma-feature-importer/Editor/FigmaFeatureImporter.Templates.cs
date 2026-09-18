#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Ezg.FigmaFeatureImporter.Editor
{
    /// <summary>Bước 3–5 (§5.2): instantiate prefab thô, tên node, dropNodes, map component → template, buttonNodes.</summary>
    public static partial class FigmaFeatureImporter
    {
        /// <summary>Sprite plate phải phủ ít nhất phần này diện tích node mới được coi là "mặt" nút (loại icon nhỏ).</summary>
        private const float PLATE_MIN_AREA_RATIO = 0.6f;

        /// <summary>
        ///     Bước 3: instantiate prefab thô vào cha của thân, ghi lại mọi instance component Figma
        ///     (root của nested prefab instance → prefab component gốc) rồi unpack hoàn toàn. Sau unpack
        ///     là mất dấu component từ đâu.
        /// </summary>
        private static void InstantiateRaw(Ctx ctx, GameObject rawAsset)
        {
            var raw = (GameObject)PrefabUtility.InstantiatePrefab(rawAsset, ctx.BodyParent);
            raw.name = ctx.Entry.figmaScreen;
            var rawRt = raw.transform as RectTransform;
            ctx.FrameSize = rawRt != null ? rawRt.sizeDelta : new Vector2(1080f, 2400f);

            CollectInstances(ctx, raw);
            PrefabUtility.UnpackPrefabInstance(raw, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            ctx.Body = raw.transform;
            raw.transform.SetAsLastSibling();

            // Fill của chính frame là artboard của designer (thường trắng 1080×2400), không phải UI:
            // giữ lại là popup che kín game phía sau. Bỏ trừ khi entry cố ý keepFrameFill.
            if (!ctx.Entry.keepFrameFill)
            {
                var frameFill = raw.GetComponent<Image>();
                if (frameFill != null)
                {
                    DestroyNow(frameFill);
                    var renderer = raw.GetComponent<CanvasRenderer>();
                    if (renderer != null && raw.GetComponent<Graphic>() == null) DestroyNow(renderer);
                    ctx.Report.notes.Add("Bỏ fill nền của frame Figma (artboard). Đặt keepFrameFill=true nếu đó là nền thật.");
                }
            }
        }

        private static void CollectInstances(Ctx ctx, GameObject raw)
        {
            foreach (var t in raw.GetComponentsInChildren<Transform>(true))
            {
                if (t == raw.transform) continue;
                if (!PrefabUtility.IsAnyPrefabInstanceRoot(t.gameObject)) continue;

                var componentPath = OriginalPrefabPath(t.gameObject);
                if (string.IsNullOrEmpty(componentPath) || IsRawPath(ctx, componentPath)) continue;

                var chain = new List<string>();
                var parent = t.parent;
                while (parent != null && parent != raw.transform)
                {
                    if (PrefabUtility.IsAnyPrefabInstanceRoot(parent.gameObject))
                    {
                        var outerPath = OriginalPrefabPath(parent.gameObject);
                        if (!string.IsNullOrEmpty(outerPath) && !IsRawPath(ctx, outerPath)) chain.Insert(0, outerPath);
                    }
                    parent = parent.parent;
                }
                chain.Add(componentPath);

                ctx.Instances.Add(new InstanceInfo
                {
                    NodeName = t.name,
                    HierarchyPath = PathWithin(raw.transform, t),
                    ComponentPrefabPath = componentPath,
                    SourcePathChain = chain.ToArray(),
                    Node = t
                });
            }
        }

        /// <summary>Đường dẫn là chính prefab thô (bản bridge hay bản chụp trong RawScreens), không phải component.</summary>
        private static bool IsRawPath(Ctx ctx, string path) => path == ctx.RawPrefabPath || path == ctx.RawSourcePath;

        /// <summary>
        ///     Prefab GỐC mà instance này là bản của. Với instance lồng (B trong A) `GetCorrespondingObjectFromSource`
        ///     trả node B nằm trong prefab A (đường dẫn A); `...FromOriginalSource` mới ra prefab B.
        /// </summary>
        private static string OriginalPrefabPath(GameObject instanceRoot)
        {
            var original = PrefabUtility.GetCorrespondingObjectFromOriginalSource(instanceRoot);
            return original == null ? null : AssetDatabase.GetAssetPath(original);
        }

        /// <summary>
        ///     Bước 3b: node trùng tên trong cùng cha → `Name_1`, `Name_2` theo thứ tự anh em (khi bridge
        ///     chưa bật NumberDuplicateSiblings). Ghi vào report để designer đổi tên ở Figma.
        /// </summary>
        private static void NumberDuplicateSiblings(Ctx ctx)
        {
            foreach (var parent in ctx.Body.GetComponentsInChildren<Transform>(true))
            {
                if (IsInsideTemplate(ctx, parent) || ctx.TemplateRoots.Contains(parent)) continue;
                var seen = new Dictionary<string, int>();
                for (var i = 0; i < parent.childCount; i++)
                {
                    var child = parent.GetChild(i);
                    if (!seen.TryGetValue(child.name, out var count))
                    {
                        seen[child.name] = 0;
                        continue;
                    }
                    var baseName = child.name;
                    var newName = $"{baseName}_{++count}";
                    while (parent.Find(newName) != null) newName = $"{baseName}_{++count}";
                    seen[baseName] = count;
                    ctx.Report.renamedDuplicates.Add($"{PathWithin(ctx.Body, parent)}/{baseName} -> {newName}");
                    child.name = newName;
                }
            }
        }

        /// <summary>Bước 3b: áp `renameNodes` của entry (theo thứ tự khai).</summary>
        private static void ApplyRenames(Ctx ctx)
        {
            foreach (var rename in ctx.Entry.renameNodes ?? new List<FigmaNodeRename>())
            {
                if (rename == null || string.IsNullOrEmpty(rename.from) || string.IsNullOrEmpty(rename.to)) continue;
                var node = FindNode(ctx, rename.from);
                if (node == null)
                {
                    ctx.Report.Warn($"renameNodes: không thấy node '{rename.from}'.");
                    continue;
                }
                node.name = rename.to;
                var instance = ctx.Instances.FirstOrDefault(i => i.Node == node);
                if (instance != null) instance.NodeName = rename.to;
            }
        }

        /// <summary>Bước 4: bỏ lớp nền designer vẽ, nền demo… `background_button` của vỏ đã lo phần tối.</summary>
        private static void DropNodes(Ctx ctx)
        {
            foreach (var name in ctx.Entry.dropNodes ?? new List<string>())
            {
                if (string.IsNullOrEmpty(name)) continue;
                var found = false;
                Transform node;
                while ((node = FindNode(ctx, name)) != null)
                {
                    found = true;
                    DestroyNow(node.gameObject);
                }
                if (!found) ctx.Report.Warn($"dropNodes: không thấy node '{name}'.");
            }
            ctx.Instances.RemoveAll(i => i.Node == null);
        }

        /// <summary>
        ///     Bước 5: instance component có dòng map → thay bằng template project đúng cha/thứ tự/rect.
        ///     Duyệt từ ngoài vào trong; instance nằm trong một instance đã map thì bỏ qua (map khớp
        ///     prefix chuỗi nguồn — gặp map là không đi sâu).
        /// </summary>
        private static void MapTemplates(Ctx ctx)
        {
            var mappedPaths = new List<string>();
            foreach (var instance in ctx.Instances.OrderBy(i => i.HierarchyPath.Count(c => c == '/')).ToList())
            {
                if (instance.Node == null) continue;
                if (mappedPaths.Any(p => instance.HierarchyPath.StartsWith(p + "/", StringComparison.Ordinal))) continue;

                var map = ctx.Settings.FindTemplateMap(instance.ComponentPrefabPath);
                if (map == null) continue;

                var templateRoot = ReplaceWithTemplate(ctx, instance.Node, map, ctx.Settings.TemplatePath(map.template));
                if (templateRoot == null) continue;
                mappedPaths.Add(instance.HierarchyPath);
                ctx.Report.mappedTemplates.Add($"{instance.NodeName} <- {map.template}");
            }
            ctx.Instances.RemoveAll(i => i.Node == null);
        }

        /// <summary>
        ///     Bước 5b: node vẽ rời mà entry khai là nút → thay bằng `defaultButtonTemplate`, sprite của
        ///     node làm mặt. Đây là cách duy nhất để node rời có stack Button (D6).
        /// </summary>
        private static void MapButtonNodes(Ctx ctx)
        {
            foreach (var name in ctx.Entry.buttonNodes ?? new List<string>())
            {
                if (string.IsNullOrEmpty(name)) continue;
                var node = FindNode(ctx, name);
                if (node == null)
                {
                    ctx.Report.Warn($"buttonNodes: không thấy node '{name}'.");
                    continue;
                }
                if (ctx.TemplateRoots.Contains(node)) continue; // đã là template (instance component có map)

                var templatePath = ctx.Settings.TemplatePath(ctx.Settings.defaultButtonTemplate);
                var templateAsset = AssetDatabase.LoadAssetAtPath<GameObject>(templatePath);
                var map = new FigmaTemplateMap
                {
                    figmaComponent = "(buttonNodes)",
                    template = ctx.Settings.defaultButtonTemplate,
                    faceSlot = ctx.Settings.buttonFaceSlot,
                    // button_template thuần không có text_body — chỉ chép chữ khi template có slot
                    textSlot = !string.IsNullOrWhiteSpace(ctx.Settings.buttonTextSlot) && templateAsset != null &&
                               templateAsset.transform.Find(ctx.Settings.buttonTextSlot) != null
                        ? ctx.Settings.buttonTextSlot
                        : null
                };
                var templateRoot = ReplaceWithTemplate(ctx, node, map, templatePath);
                if (templateRoot != null) ctx.Report.mappedTemplates.Add($"{name} <- {map.template} (buttonNodes)");
            }
        }

        private static Transform ReplaceWithTemplate(Ctx ctx, Transform figmaNode, FigmaTemplateMap map, string templatePath)
        {
            var templateAsset = AssetDatabase.LoadAssetAtPath<GameObject>(templatePath);
            if (templateAsset == null)
            {
                ctx.Report.Warn($"Template '{templatePath}' không tồn tại (map {map.figmaComponent}).");
                return null;
            }

            var parent = figmaNode.parent;
            var siblingIndex = figmaNode.GetSiblingIndex();
            var name = figmaNode.name;

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(templateAsset, parent);
            instance.name = name;
            instance.transform.SetSiblingIndex(siblingIndex);
            instance.SetActive(figmaNode.gameObject.activeSelf);
            CopyRect(figmaNode as RectTransform, instance.transform as RectTransform);

            var templateTransform = instance.transform;

            if (!string.IsNullOrEmpty(map.textSlot))
            {
                var slot = templateTransform.Find(map.textSlot);
                var source = figmaNode.GetComponentInChildren<TMP_Text>(true);
                if (slot == null) ctx.Report.Warn($"{name}: textSlot '{map.textSlot}' không có trong {map.template}.");
                else if (source != null) SetSlotText(slot, source.text);
            }

            if (!string.IsNullOrEmpty(map.iconSlot))
            {
                var slot = templateTransform.Find(map.iconSlot);
                var icon = FindIcon(figmaNode);
                if (slot == null) ctx.Report.Warn($"{name}: iconSlot '{map.iconSlot}' không có trong {map.template}.");
                else if (icon != null && icon.sprite != null)
                {
                    var slotImage = slot.GetComponent<Image>();
                    if (slotImage != null)
                    {
                        slotImage.sprite = icon.sprite;
                        slotImage.enabled = true;
                        slot.gameObject.SetActive(true);
                    }
                }
            }

            if (!string.IsNullOrEmpty(map.faceSlot))
            {
                var slot = templateTransform.Find(map.faceSlot);
                var plate = FindPlate(figmaNode);
                if (slot == null) ctx.Report.Warn($"{name}: faceSlot '{map.faceSlot}' không có trong {map.template}.");
                else if (plate != null)
                {
                    var face = slot.GetComponent<Image>();
                    if (face != null)
                    {
                        face.sprite = plate.sprite;
                        face.type = plate.sprite.border != Vector4.zero ? Image.Type.Sliced : Image.Type.Simple;
                        face.color = Color.white;
                    }
                }
                else ctx.Report.notes.Add($"{name}: không có sprite plate → giữ art template {map.template}.");
            }

            if (!string.IsNullOrEmpty(map.childrenSlot))
            {
                var slot = templateTransform.Find(map.childrenSlot);
                if (slot == null) ctx.Report.Warn($"{name}: childrenSlot '{map.childrenSlot}' không có trong {map.template}.");
                else
                {
                    var children = new List<Transform>();
                    for (var i = 0; i < figmaNode.childCount; i++) children.Add(figmaNode.GetChild(i));
                    foreach (var child in children) child.SetParent(slot, false);
                }
            }

            foreach (var path in map.deactivate ?? new List<string>())
            {
                if (string.IsNullOrEmpty(path)) continue;
                var target = templateTransform.Find(path);
                if (target == null) ctx.Report.Warn($"{name}: deactivate '{path}' không có trong {map.template}.");
                else target.gameObject.SetActive(false);
            }

            if (map.isCloseButton)
            {
                var button = instance.GetComponent<Button>();
                if (button != null) ctx.CloseButtons.Add(button);
                else ctx.Report.Warn($"{name}: map isCloseButton nhưng {map.template} không có Button ở root.");
            }

            ctx.TemplateRoots.Add(templateTransform);
            // Instance con của node vừa thay không còn tồn tại
            foreach (var nested in ctx.Instances.Where(i => i.Node != null && i.Node != figmaNode && i.Node.IsChildOf(figmaNode)).ToList())
                nested.Node = null;
            DestroyNow(figmaNode.gameObject);
            return templateTransform;
        }

        private static void CopyRect(RectTransform source, RectTransform target)
        {
            if (source == null || target == null) return;
            target.anchorMin = source.anchorMin;
            target.anchorMax = source.anchorMax;
            target.pivot = source.pivot;
            target.anchoredPosition = source.anchoredPosition;
            target.sizeDelta = source.sizeDelta;
            target.localRotation = source.localRotation;
            target.localScale = source.localScale;
        }

        private static void SetSlotText(Transform slot, string content)
        {
            var tmp = slot.GetComponent<TMP_Text>();
            if (tmp != null)
            {
                tmp.text = content;
                return;
            }
            var legacy = slot.GetComponent<Text>();
            if (legacy != null) legacy.text = content;
        }

        /// <summary>Icon = Image có sprite dưới node tên bắt đầu bằng "Icon"/"icon"; thiếu thì Image có sprite nhỏ nhất.</summary>
        private static Image FindIcon(Transform figmaNode)
        {
            Image fallback = null;
            var fallbackArea = float.MaxValue;
            foreach (var image in figmaNode.GetComponentsInChildren<Image>(true))
            {
                if (image.sprite == null) continue;
                if (image.name.StartsWith("Icon", StringComparison.OrdinalIgnoreCase)) return image;
                var area = Area(image.rectTransform);
                if (area < fallbackArea)
                {
                    fallbackArea = area;
                    fallback = image;
                }
            }
            return fallback;
        }

        /// <summary>Mặt nút = Image có sprite lớn nhất phủ ≥ 60% diện tích node (icon nhỏ không tính).</summary>
        private static Image FindPlate(Transform figmaNode)
        {
            var nodeArea = Area(figmaNode as RectTransform);
            Image best = null;
            var bestArea = 0f;
            foreach (var image in figmaNode.GetComponentsInChildren<Image>(true))
            {
                if (image.sprite == null) continue;
                var area = Area(image.rectTransform);
                if (nodeArea > 0f && area < nodeArea * PLATE_MIN_AREA_RATIO) continue;
                if (area <= bestArea) continue;
                bestArea = area;
                best = image;
            }
            return best;
        }

        private static float Area(RectTransform rt)
        {
            if (rt == null) return 0f;
            var size = rt.sizeDelta;
            return Mathf.Abs(size.x * size.y);
        }
    }
}
#endif
