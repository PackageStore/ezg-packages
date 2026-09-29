using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Ezg.AutoTest.Editor
{
    /// <summary>
    ///     Vệ sinh prefab UI (quét tĩnh, không vào Play): Image không sprite (ô trắng), Text/TMP không font, Button
    ///     không Target Graphic, Canvas gốc thiếu CanvasScaler. Gộp theo prefab + loại lỗi để report gọn.
    /// </summary>
    public sealed class UiPrefabHygieneRule : IStaticRule
    {
        const string ISSUE_CATEGORY = "Asset.UiHygiene";
        const float MIN_VISIBLE_ALPHA = 0.01f;
        const float NEAR_WHITE = 0.95f;
        const int MAX_OBJECTS_IN_MESSAGE = 15;
        const int MAX_PREFABS_IN_MESSAGE = 30;

        enum Problem
        {
            ImageNoSpriteWhite,
            ImageNoSpriteColored,
            TextNoFont,
            TmpNoFont,
            ButtonNoTarget
        }

        sealed class ProblemInfo
        {
            public Severity Severity;
            public string Title;
            public string Advice;
        }

        static readonly Dictionary<Problem, ProblemInfo> PROBLEMS = new()
        {
            {
                Problem.ImageNoSpriteWhite, new ProblemInfo
                {
                    Severity = Severity.Minor, Title = "Image không có sprite (ô trắng)",
                    Advice = "Image bật, màu trắng, không gán sprite → hiện ô trắng. Gán sprite, hoặc tắt Image/đặt alpha 0 " +
                             "nếu chỉ dùng làm vùng bấm. Nếu sprite được gán bằng code lúc chạy, thêm tên object vào " +
                             "danh sách bỏ qua (uiAudit.ignoredObjectPatterns)."
                }
            },
            {
                Problem.ImageNoSpriteColored, new ProblemInfo
                {
                    Severity = Severity.Info, Title = "Image không có sprite (khối màu đặc)",
                    Advice = "Image không gán sprite nên vẽ khối màu đặc. Bỏ qua nếu cố ý (nền mờ, panel màu); nếu lẽ ra " +
                             "phải có hình thì gán sprite."
                }
            },
            {
                Problem.TextNoFont, new ProblemInfo
                {
                    Severity = Severity.Major, Title = "Text không có font",
                    Advice = "Text (legacy) không gán font → chữ không hiện. Gán font của project."
                }
            },
            {
                Problem.TmpNoFont, new ProblemInfo
                {
                    Severity = Severity.Major, Title = "TextMeshPro không có font asset",
                    Advice = "TextMeshPro không gán Font Asset → lúc chạy rơi về font mặc định trong TMP Settings (sai " +
                             "font, dễ thiếu glyph tiếng Việt/ngôn ngữ khác). Gán Font Asset của project."
                }
            },
            {
                Problem.ButtonNoTarget, new ProblemInfo
                {
                    Severity = Severity.Minor, Title = "Button không có Target Graphic",
                    Advice = "Button dùng transition Color Tint/Sprite Swap nhưng không có Target Graphic → bấm không có " +
                             "phản hồi hình ảnh. Gán Image của nút vào Target Graphic."
                }
            }
        };

        public string Id => "ui-prefab-hygiene";
        public string Name => "Vệ sinh prefab UI";

        public string Description =>
            "Prefab UI: Image bật nhưng không sprite (ô trắng), Text/TextMeshPro không font, Button không Target " +
            "Graphic, Canvas gốc không có CanvasScaler.";

        public string Category => "Asset";
        public int Order => 70;

        public async Task Run(AutoTestContext ctx)
        {
            var cfg = ctx.Config.staticCheck;
            var sink = new AssetIssueSink(ctx, ISSUE_CATEGORY);
            var ignored = AssetRuleUtil.CompilePatterns(ctx.Config.uiAudit.ignoredObjectPatterns);
            var checkImages = ctx.Config.uiAudit.checkMissingSprite;
            var prefabs = AssetRuleUtil.FindPrefabs(cfg);
            var uiPrefabs = 0;
            var canvasNoScaler = new List<string>();
            for (var i = 0; i < prefabs.Count; i++)
            {
                await StaticCheckUtil.Yield(ctx, i, prefabs.Count, "Quét prefab UI");
                var path = prefabs[i];
                try
                {
                    if (ScanPrefab(path, cfg, ignored, checkImages, sink, canvasNoScaler)) uiPrefabs++;
                }
                catch (Exception e)
                {
                    sink.ReportUnreadable(path, e);
                }
            }

            if (canvasNoScaler.Count > 0)
                ctx.Report(Severity.Info, ISSUE_CATEGORY, "Canvas gốc không có CanvasScaler",
                    $"{canvasNoScaler.Count} prefab có Canvas (Screen Space) ở gốc nhưng không có CanvasScaler → nếu " +
                    "prefab được đặt làm canvas gốc, UI không co giãn theo độ phân giải máy. Bỏ qua nếu prefab luôn " +
                    "nằm dưới một canvas khác (canvas lồng không cần scaler):" +
                    AssetRuleUtil.BulletList(canvasNoScaler, MAX_PREFABS_IN_MESSAGE));

            ctx.Metric("Prefab đã quét", prefabs.Count);
            ctx.Metric("Prefab UI", uiPrefabs);
            sink.Flush();
            EditorUtility.UnloadUnusedAssetsImmediate();
        }

        /// <summary>True nếu là prefab UI (có RectTransform).</summary>
        static bool ScanPrefab(string path, StaticCheckConfig cfg, List<Regex> ignored, bool checkImages,
            AssetIssueSink sink, List<string> canvasNoScaler)
        {
            var root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (root == null || root.GetComponentInChildren<RectTransform>(true) == null) return false;

            var found = new Dictionary<Problem, List<string>>();

            if (checkImages)
                foreach (var img in root.GetComponentsInChildren<Image>(true))
                {
                    if (!IsBlankImage(img) || AssetRuleUtil.IsIgnoredObject(img.transform, ignored)) continue;
                    if (IsReportedAtSource(img, path, cfg, IsBlankImage)) continue;
                    var c = img.color;
                    var white = c.r >= NEAR_WHITE && c.g >= NEAR_WHITE && c.b >= NEAR_WHITE;
                    Add(found, white ? Problem.ImageNoSpriteWhite : Problem.ImageNoSpriteColored, img.transform);
                }

            foreach (var text in root.GetComponentsInChildren<Text>(true))
                if (HasNoFont(text) && !AssetRuleUtil.IsIgnoredObject(text.transform, ignored) &&
                    !IsReportedAtSource(text, path, cfg, HasNoFont))
                    Add(found, Problem.TextNoFont, text.transform);

            foreach (var tmp in root.GetComponentsInChildren<TMP_Text>(true))
                if (HasNoFont(tmp) && !AssetRuleUtil.IsIgnoredObject(tmp.transform, ignored) &&
                    !IsReportedAtSource(tmp, path, cfg, HasNoFont))
                    Add(found, Problem.TmpNoFont, tmp.transform);

            foreach (var button in root.GetComponentsInChildren<Button>(true))
                if (LacksTargetGraphic(button) && !AssetRuleUtil.IsIgnoredObject(button.transform, ignored) &&
                    !IsReportedAtSource(button, path, cfg, LacksTargetGraphic))
                    Add(found, Problem.ButtonNoTarget, button.transform);

            var canvas = root.GetComponent<Canvas>();
            if (canvas != null && canvas.renderMode != RenderMode.WorldSpace && root.GetComponent<CanvasScaler>() == null)
                canvasNoScaler.Add(path);

            foreach (var pair in found)
            {
                var info = PROBLEMS[pair.Key];
                var list = pair.Value;
                sink.Report(info.Severity, info.Title,
                    $"{list.Count} object. {info.Advice}" + AssetRuleUtil.BulletList(list, MAX_OBJECTS_IN_MESSAGE),
                    path, list[0], null, $"{list.Count} object");
            }

            return true;
        }

        #region Điều kiện lỗi

        /// <summary>Image bật, nhìn thấy được, không sprite, không phải graphic ẩn của Mask, sprite không phải Missing.</summary>
        static bool IsBlankImage(Image img)
        {
            if (img.sprite != null || !img.enabled || img.color.a <= MIN_VISIBLE_ALPHA) return false;
            return !IsHiddenMaskGraphic(img) && !IsMissingSpriteRef(img);
        }

        static bool HasNoFont(Text text)
        {
            return text.font == null;
        }

        static bool HasNoFont(TMP_Text text)
        {
            return text.font == null;
        }

        /// <summary>Chỉ transition Color Tint / Sprite Swap mới cần Target Graphic (Animation/None thì không).</summary>
        static bool LacksTargetGraphic(Button button)
        {
            var needsTarget = button.transition == Selectable.Transition.ColorTint ||
                              button.transition == Selectable.Transition.SpriteSwap;
            return needsTarget && button.targetGraphic == null;
        }

        /// <summary>
        ///     Lỗi kế thừa nguyên từ prefab lồng / prefab gốc của variant (cũng trong phạm vi quét) → đã báo ở prefab
        ///     nguồn, không lặp lại ở mọi variant.
        /// </summary>
        static bool IsReportedAtSource<T>(T comp, string path, StaticCheckConfig cfg, Func<T, bool> hasProblem)
            where T : Component
        {
            var source = PrefabUtility.GetCorrespondingObjectFromSource(comp);
            if (source == null) return false;
            var sourcePath = AssetDatabase.GetAssetPath(source);
            if (string.IsNullOrEmpty(sourcePath) || sourcePath == path ||
                !sourcePath.EndsWith(AssetRuleUtil.PREFAB_EXTENSION, StringComparison.OrdinalIgnoreCase) ||
                !StaticCheckUtil.InScope(sourcePath, cfg)) return false;
            return hasProblem(source);
        }

        static void Add(Dictionary<Problem, List<string>> found, Problem problem, Transform t)
        {
            if (!found.TryGetValue(problem, out var list))
            {
                list = new List<string>();
                found[problem] = list;
            }

            list.Add(StaticCheckUtil.HierarchyPath(t));
        }

        /// <summary>Image làm graphic cho Mask nhưng tắt Show Mask Graphic → không hiện, không phải lỗi.</summary>
        static bool IsHiddenMaskGraphic(Image img)
        {
            var mask = img.GetComponent<Mask>();
            return mask != null && !mask.showMaskGraphic;
        }

        /// <summary>Sprite trỏ tới asset đã xoá (Missing) — luật missing-references đã báo, không báo lại.</summary>
        static bool IsMissingSpriteRef(Image img)
        {
            using (var so = new SerializedObject(img))
            {
                var p = so.FindProperty("m_Sprite");
                return p != null && p.objectReferenceValue == null && p.objectReferenceInstanceIDValue != 0;
            }
        }

        #endregion
    }
}
