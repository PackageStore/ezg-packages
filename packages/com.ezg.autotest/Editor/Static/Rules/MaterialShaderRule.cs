using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace Ezg.AutoTest.Editor
{
    /// <summary>
    ///     Material hỏng: mất shader (magenta), shader lỗi biên dịch, shader chỉ chạy Built-in khi project dùng SRP,
    ///     blend opaque trên material trong suốt, và material gán cho UI Graphic mà shader không sample _MainTex
    ///     (sprite không hiện, ra mảng trắng) — các bẫy hay gặp sau khi chuyển Built-in → URP.
    /// </summary>
    public sealed class MaterialShaderRule : IStaticRule
    {
        const string ISSUE_CATEGORY = "Asset.Material";
        const string ERROR_SHADER = "Hidden/InternalErrorShader";
        const string MAIN_TEX = "_MainTex";
        const string SRP_SHADER_PREFIX = "Universal Render Pipeline/";
        const string PROP_SURFACE = "_Surface";
        const string PROP_SRC_BLEND = "_SrcBlend";
        const string PROP_DST_BLEND = "_DstBlend";
        const float SURFACE_TRANSPARENT = 1f;
        const float BLEND_ONE = 1f;
        const float BLEND_ZERO = 0f;
        const float FLOAT_EPSILON = 0.001f;
        const int MAX_LIST = 10;

        static readonly string[] BUILTIN_ONLY_EXACT = { "Standard", "Standard (Specular setup)" };
        static readonly string[] BUILTIN_ONLY_PREFIX = { "Legacy Shaders/", "Mobile/", "Particles/", "Nature/" };

        static readonly Regex SHADER_REF = new(@"m_Shader: \{fileID: (-?\d+)(?:, guid: ([0-9a-fA-F]{32}))?",
            RegexOptions.Compiled);

        static readonly FieldInfo GRAPHIC_MATERIAL_FIELD =
            typeof(Graphic).GetField("m_Material", BindingFlags.Instance | BindingFlags.NonPublic);

        public string Id => "broken-materials";
        public string Name => "Material hỏng (magenta / sai shader)";

        public string Description =>
            "Material mất shader hoặc shader lỗi (magenta), shader Built-in trên project URP, blend opaque trên " +
            "material trong suốt (glow thành khối trắng), material trên UI không sample sprite (ra mảng trắng).";

        public string Category => "Asset";
        public int Order => 30;

        public async Task Run(AutoTestContext ctx)
        {
            var cfg = ctx.Config.staticCheck;
            var sink = new AssetIssueSink(ctx, ISSUE_CATEGORY);
            var pipeline = GraphicsSettings.currentRenderPipeline != null
                ? GraphicsSettings.currentRenderPipeline
                : GraphicsSettings.defaultRenderPipeline;
            var usesSrp = pipeline != null;
            ctx.Log(usesSrp ? $"Render pipeline: {pipeline.GetType().Name} ({pipeline.name})" : "Render pipeline: Built-in");

            var materials = AssetRuleUtil.FindAssetsWithExtension("t:Material", cfg, ".mat");
            var brokenShaders = new Dictionary<Shader, List<string>>();
            var broken = 0;
            for (var i = 0; i < materials.Count; i++)
            {
                await StaticCheckUtil.Yield(ctx, i, materials.Count, "Quét material");
                var path = materials[i];
                try
                {
                    if (CheckMaterial(path, usesSrp, sink, brokenShaders)) broken++;
                }
                catch (Exception e)
                {
                    sink.ReportUnreadable(path, e);
                }
            }

            foreach (var pair in brokenShaders)
            {
                broken += pair.Value.Count;
                var shaderPath = AssetDatabase.GetAssetPath(pair.Key);
                sink.Report(Severity.Major, "Material dùng shader lỗi biên dịch",
                    $"Shader '{pair.Key.name}' đang có lỗi biên dịch → {pair.Value.Count} material hiển thị sai/magenta. " +
                    "Xem lỗi ở Inspector của shader (hoặc luật 'shaders')." + AssetRuleUtil.BulletList(pair.Value, MAX_LIST),
                    string.IsNullOrEmpty(shaderPath) ? pair.Value[0] : shaderPath, null, "Shader biên dịch không lỗi",
                    "Có lỗi biên dịch");
            }

            var prefabs = AssetRuleUtil.FindPrefabs(cfg);
            var uiHits = 0;
            var shaderCache = new Dictionary<Shader, string>();
            for (var i = 0; i < prefabs.Count; i++)
            {
                await StaticCheckUtil.Yield(ctx, i, prefabs.Count, "Quét material trên UI");
                try
                {
                    uiHits += CheckUiPrefab(prefabs[i], cfg, sink, shaderCache);
                }
                catch (Exception e)
                {
                    sink.ReportUnreadable(prefabs[i], e);
                }
            }

            ctx.Metric("Material đã quét", materials.Count);
            ctx.Metric("Material hỏng", broken);
            ctx.Metric("Prefab UI đã quét", prefabs.Count);
            ctx.Metric("Nhóm UI dùng material sai shader", uiHits);
            sink.Flush();
            EditorUtility.UnloadUnusedAssetsImmediate();
        }

        #region Material

        /// <summary>True nếu material có lỗi ghi riêng (không tính lỗi gộp theo shader).</summary>
        static bool CheckMaterial(string path, bool usesSrp, AssetIssueSink sink,
            Dictionary<Shader, List<string>> brokenShaders)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null) return false;
            var shader = mat.shader;

            if (shader == null || shader.name == ERROR_SHADER)
            {
                sink.Report(Severity.Critical, "Material bị magenta (mất shader)",
                    "Material không tìm thấy shader nên render màu hồng tím (magenta). " + DescribeShaderRef(path) +
                    " Gán lại shader đúng cho material.", path, null, "Shader hợp lệ",
                    shader == null ? "null" : ERROR_SHADER);
                return true;
            }

            var hasOwnIssue = false;
            if (ShaderUtil.ShaderHasError(shader))
            {
                if (!brokenShaders.TryGetValue(shader, out var list))
                {
                    list = new List<string>();
                    brokenShaders[shader] = list;
                }

                list.Add(path);
            }

            if (usesSrp && IsBuiltinOnlyShader(shader))
            {
                hasOwnIssue = true;
                sink.Report(Severity.Major, "Shader Built-in không chạy trên URP (magenta)",
                    $"Material dùng shader Built-in '{shader.name}' trong khi project chạy Scriptable Render Pipeline → " +
                    "render magenta/không hiện. Đổi sang shader tương đương: Standard/Mobile/Legacy → " +
                    "'Universal Render Pipeline/Lit' hoặc 'Simple Lit'/'Unlit'; Particles → " +
                    "'Universal Render Pipeline/Particles/Unlit'; material dùng trên UI → shader UI (vd 'UI/Default').",
                    path, null, "Shader tương thích SRP", shader.name);
            }

            if (HasOpaqueBlendOnTransparent(mat, shader))
            {
                hasOwnIssue = true;
                sink.Report(Severity.Major, "Blend opaque trên material trong suốt (glow thành khối trắng)",
                    "Material đặt Surface = Transparent nhưng _SrcBlend = One, _DstBlend = Zero (blend đục) — thường do " +
                    "trình nâng cấp URP map sai. Đặt lại Blend Mode trong Inspector: Additive (Src=SrcAlpha, Dst=One), " +
                    "Alpha (Src=SrcAlpha, Dst=OneMinusSrcAlpha), Multiply (Src=DstColor, Dst=Zero).",
                    path, null, "Blend trong suốt", "Src=One, Dst=Zero");
            }

            return hasOwnIssue;
        }

        static bool IsBuiltinOnlyShader(Shader shader)
        {
            // Shader cùng tên nhưng nằm trong project/package là shader tự viết → không phải built-in.
            var shaderPath = AssetDatabase.GetAssetPath(shader);
            if (shaderPath.StartsWith("Assets/", StringComparison.Ordinal) ||
                shaderPath.StartsWith("Packages/", StringComparison.Ordinal)) return false;
            var name = shader.name;
            foreach (var exact in BUILTIN_ONLY_EXACT)
                if (name == exact)
                    return true;
            foreach (var prefix in BUILTIN_ONLY_PREFIX)
                if (name.StartsWith(prefix, StringComparison.Ordinal))
                    return true;
            return false;
        }

        static bool HasOpaqueBlendOnTransparent(Material mat, Shader shader)
        {
            if (!shader.name.StartsWith(SRP_SHADER_PREFIX, StringComparison.Ordinal)) return false;
            if (!mat.HasProperty(PROP_SURFACE) || !mat.HasProperty(PROP_SRC_BLEND) || !mat.HasProperty(PROP_DST_BLEND))
                return false;
            return Approximately(mat.GetFloat(PROP_SURFACE), SURFACE_TRANSPARENT) &&
                   Approximately(mat.GetFloat(PROP_SRC_BLEND), BLEND_ONE) &&
                   Approximately(mat.GetFloat(PROP_DST_BLEND), BLEND_ZERO);
        }

        static bool Approximately(float a, float b)
        {
            return Math.Abs(a - b) < FLOAT_EPSILON;
        }

        /// <summary>Đọc dòng m_Shader trong file .mat để nói rõ shader bị xoá hay lỗi.</summary>
        static string DescribeShaderRef(string path)
        {
            try
            {
                var abs = StaticCheckUtil.AbsolutePath(path);
                if (!AssetRuleUtil.IsYamlFile(abs)) return "";
                var m = SHADER_REF.Match(File.ReadAllText(abs));
                if (!m.Success) return "";
                var guid = m.Groups[2].Success ? m.Groups[2].Value : null;
                if (string.IsNullOrEmpty(guid)) return $"(m_Shader fileID {m.Groups[1].Value}, không có guid.)";
                if (AssetRuleUtil.IsBuiltinGuid(guid))
                    return $"(Trỏ tới shader built-in fileID {m.Groups[1].Value} — có thể bị strip hoặc không còn trong bản Unity này.)";
                var shaderPath = AssetDatabase.GUIDToAssetPath(guid);
                return string.IsNullOrEmpty(shaderPath)
                    ? $"(Shader đã bị xoá khỏi project: guid {guid}.)"
                    : $"(Shader '{shaderPath}' vẫn còn nhưng không load được — kiểm tra lỗi biên dịch.)";
            }
            catch (Exception)
            {
                return "";
            }
        }

        #endregion

        #region UI Graphic

        sealed class UiGroup
        {
            public Material Material;
            public string Reason;
            public Severity Severity = Severity.Minor;
            public readonly List<string> Objects = new();
        }

        static int CheckUiPrefab(string path, StaticCheckConfig cfg, AssetIssueSink sink,
            Dictionary<Shader, string> shaderCache)
        {
            var root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (root == null || root.GetComponentInChildren<RectTransform>(true) == null) return 0;

            var groups = new Dictionary<Material, UiGroup>();
            var order = new List<UiGroup>();
            foreach (var g in root.GetComponentsInChildren<Graphic>(true))
            {
                // TextMeshPro dùng shader SDF riêng (có _MainTex) và getter material của nó có thể tạo instance.
                if (g == null || g is TMP_Text) continue;
                var mat = GetAssignedMaterial(g);
                if (mat == null) continue;
                var shader = mat.shader;
                if (shader == null) continue;
                if (!shaderCache.TryGetValue(shader, out var reason))
                {
                    reason = UiShaderProblem(shader);
                    shaderCache[shader] = reason;
                }

                if (reason == null || IsInheritedFromSource(g, mat, path, cfg)) continue;
                if (!groups.TryGetValue(mat, out var group))
                {
                    group = new UiGroup { Material = mat, Reason = reason };
                    groups[mat] = group;
                    order.Add(group);
                }

                if (NeedsTexture(g)) group.Severity = Severity.Major;
                group.Objects.Add(StaticCheckUtil.HierarchyPath(g.transform));
            }

            foreach (var group in order)
            {
                var matPath = AssetDatabase.GetAssetPath(group.Material);
                sink.Report(group.Severity, "Material trên UI không sample sprite (_MainTex) → ra mảng trắng",
                    $"Material '{(string.IsNullOrEmpty(matPath) ? group.Material.name : matPath)}' dùng shader " +
                    $"'{group.Material.shader.name}' {group.Reason}. UI Graphic đưa sprite/texture vào _MainTex nên hình " +
                    "không được vẽ, chỉ ra mảng màu phẳng. Đổi material sang shader UI (vd 'UI/Default' hoặc shader UI " +
                    "tuỳ biến có khai báo và sample _MainTex).\nObject dùng material này:" +
                    AssetRuleUtil.BulletList(group.Objects, MAX_LIST),
                    path, group.Objects[0], "Shader sample _MainTex", group.Reason);
            }

            return order.Count;
        }

        /// <summary>Material kế thừa nguyên từ prefab lồng / prefab gốc (trong phạm vi) → đã báo ở prefab nguồn.</summary>
        static bool IsInheritedFromSource(Graphic g, Material mat, string path, StaticCheckConfig cfg)
        {
            var source = PrefabUtility.GetCorrespondingObjectFromSource(g);
            if (source == null) return false;
            var sourcePath = AssetDatabase.GetAssetPath(source);
            if (string.IsNullOrEmpty(sourcePath) || sourcePath == path ||
                !sourcePath.EndsWith(AssetRuleUtil.PREFAB_EXTENSION, StringComparison.OrdinalIgnoreCase) ||
                !StaticCheckUtil.InScope(sourcePath, cfg)) return false;
            return GetAssignedMaterial(source) == mat;
        }

        /// <summary>Material gán tay trên Graphic (m_Material) — không lấy default material.</summary>
        static Material GetAssignedMaterial(Graphic g)
        {
            if (GRAPHIC_MATERIAL_FIELD != null) return GRAPHIC_MATERIAL_FIELD.GetValue(g) as Material;
            using (var so = new SerializedObject(g))
            {
                var prop = so.FindProperty("m_Material");
                return prop != null ? prop.objectReferenceValue as Material : null;
            }
        }

        /// <summary>Graphic thật sự cần texture (có sprite/texture/font) → lỗi hiển thị chắc chắn.</summary>
        static bool NeedsTexture(Graphic g)
        {
            switch (g)
            {
                case Image img: return img.sprite != null;
                case RawImage raw: return raw.texture != null;
                case Text _: return true;
                default: return false;
            }
        }

        /// <summary>null = shader dùng được cho UI; ngược lại là mô tả vấn đề.</summary>
        static string UiShaderProblem(Shader shader)
        {
            if (shader.name == ERROR_SHADER) return null; // đã báo "magenta" ở phần material
            var idx = shader.FindPropertyIndex(MAIN_TEX);
            if (idx < 0) return "không khai báo thuộc tính _MainTex";
            if ((shader.GetPropertyFlags(idx) & ShaderPropertyFlags.HideInInspector) == 0) return null;

            // Shader kiểu URP giữ _MainTex làm alias ẩn nhưng sample texture khác (vd _BaseMap).
            var count = shader.GetPropertyCount();
            for (var i = 0; i < count; i++)
                if (i != idx && (shader.GetPropertyFlags(i) & ShaderPropertyFlags.MainTexture) != 0)
                    return $"chỉ giữ _MainTex làm alias ẩn, texture thật được sample là {shader.GetPropertyName(i)}";
            return null;
        }

        #endregion
    }
}
