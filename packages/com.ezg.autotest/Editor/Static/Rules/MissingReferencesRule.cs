using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace Ezg.AutoTest.Editor
{
    /// <summary>
    ///     Tham chiếu hỏng: (b) prefab — field object reference hiện "Missing" (có instanceID nhưng không load được),
    ///     báo theo object; (a) mọi asset YAML — GUID trỏ tới asset không còn trong project, báo theo asset. Prefab
    ///     đã có lỗi theo object thì danh sách GUID của (a) được gộp vào lỗi đó thay vì tạo thêm dòng.
    /// </summary>
    public sealed class MissingReferencesRule : IStaticRule
    {
        const string ISSUE_CATEGORY = "Asset.MissingReference";
        const string TITLE_OBJECT = "Tham chiếu bị mất (Missing) trong component";
        const string TITLE_GUID = "Tham chiếu tới asset đã bị xoá";
        const string KEY_SCRIPT = "m_Script";
        const string KEY_OBJECT_REFERENCE = "objectReference";
        const string KEY_PROPERTY_PATH = "propertyPath:";
        const int MAX_GUIDS_IN_MESSAGE = 10;
        const int MAX_PROPS_IN_MESSAGE = 10;
        const int PROPERTY_PATH_LOOKBACK_LINES = 4;
        const int PARENT_KEY_LOOKBACK_LINES = 30;

        static readonly string[] YAML_EXTENSIONS =
        {
            ".prefab", ".unity", ".asset", ".mat", ".controller", ".overrideController", ".anim", ".spriteatlas",
            ".spriteatlasv2", ".playable", ".signal", ".mask", ".physicMaterial", ".physicsMaterial", ".guiskin",
            ".fontsettings", ".lighting"
        };

        /// <summary>Reference Unity luôn có dạng inline "{fileID: X, guid: Y, type: Z}" — không bắt field string tên "guid".</summary>
        static readonly Regex GUID_REF = new(@"fileID: -?\d+, guid: ([0-9a-fA-F]{32})", RegexOptions.Compiled);

        /// <summary>Mảng kiểu nguyên thuỷ không thể chứa object reference → không duyệt vào (mesh/curve data rất dài).</summary>
        static readonly HashSet<string> PRIMITIVE_ARRAY_TYPES = new(StringComparer.Ordinal)
        {
            "int", "unsigned int", "float", "double", "bool", "char", "string", "UInt8", "SInt8", "UInt16", "SInt16",
            "UInt32", "SInt32", "UInt64", "SInt64", "Vector2f", "Vector3f", "Vector4f", "Quaternionf", "ColorRGBA",
            "Matrix4x4f", "Keyframe", "BoneWeights4", "Rectf"
        };

        public string Id => "missing-references";
        public string Name => "Tham chiếu bị mất (Missing Reference)";

        public string Description =>
            "Field trong prefab hiện 'Missing' và GUID trong asset (prefab, scene, material, animator, SO…) trỏ tới " +
            "asset đã bị xoá — hình không hiện, nút không gán, NullReference lúc chạy.";

        public string Category => "Asset";
        public int Order => 20;

        public async Task Run(AutoTestContext ctx)
        {
            var cfg = ctx.Config.staticCheck;
            var sink = new AssetIssueSink(ctx, ISSUE_CATEGORY);
            var prefabIssues = new Dictionary<string, TestIssue>(StringComparer.OrdinalIgnoreCase);

            // (b) Chi tiết theo object trong prefab.
            var prefabs = AssetRuleUtil.FindPrefabs(cfg);
            var objectHits = 0;
            for (var i = 0; i < prefabs.Count; i++)
            {
                await StaticCheckUtil.Yield(ctx, i, prefabs.Count, "Quét tham chiếu prefab");
                try
                {
                    objectHits += ScanPrefabObjects(prefabs[i], cfg, sink, prefabIssues);
                }
                catch (Exception e)
                {
                    sink.ReportUnreadable(prefabs[i], e);
                }
            }

            // (a) GUID treo trong mọi asset YAML.
            var files = AssetRuleUtil.EnumerateScopeFiles(cfg, YAML_EXTENSIONS);
            var guids = new GuidResolver();
            var budget = new FrameBudget();
            var binarySkipped = 0;
            var assetsWithMissing = 0;
            for (var i = 0; i < files.Count; i++)
            {
                await budget.Tick(ctx, i, files.Count, "Quét GUID");
                var path = files[i].Path;
                try
                {
                    var result = ScanYaml(path, guids, sink, prefabIssues);
                    if (result < 0) binarySkipped++;
                    else if (result > 0) assetsWithMissing++;
                }
                catch (Exception e)
                {
                    sink.ReportUnreadable(path, e);
                }
            }

            ctx.Metric("Prefab đã quét", prefabs.Count);
            ctx.Metric("File YAML đã quét", files.Count - binarySkipped);
            ctx.Metric("File binary bỏ qua", binarySkipped);
            ctx.Metric("Object có tham chiếu Missing", objectHits);
            ctx.Metric("Asset có GUID treo", assetsWithMissing);
            sink.Flush();
            EditorUtility.UnloadUnusedAssetsImmediate();
        }

        #region (b) Prefab — SerializedProperty

        static int ScanPrefabObjects(string path, StaticCheckConfig cfg, AssetIssueSink sink,
            Dictionary<string, TestIssue> prefabIssues)
        {
            var root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (root == null) return 0;

            var perObject = new Dictionary<GameObject, List<string>>();
            var order = new List<GameObject>();
            foreach (var comp in root.GetComponentsInChildren<Component>(true))
            {
                // null = Missing Script (luật missing-scripts lo); Transform không có field reference hiện ra.
                if (comp == null || comp is Transform) continue;
                using (var so = new SerializedObject(comp))
                {
                    var it = so.GetIterator();
                    var enter = true;
                    while (it.NextVisible(enter))
                    {
                        enter = ShouldEnterChildren(it);
                        if (!IsMissingReference(it)) continue;
                        if (IsReportedAtSource(comp, it.propertyPath, path, cfg)) continue;
                        if (!perObject.TryGetValue(comp.gameObject, out var list))
                        {
                            list = new List<string>();
                            perObject[comp.gameObject] = list;
                            order.Add(comp.gameObject);
                        }

                        list.Add($"{comp.GetType().Name}.{it.propertyPath}");
                    }
                }
            }

            foreach (var go in order)
            {
                var list = perObject[go];
                var message =
                    $"{list.Count} field đang trỏ tới object/asset không còn tồn tại (hiện 'Missing' trong Inspector). " +
                    "Gán lại hoặc để trống có chủ đích (None)." + AssetRuleUtil.BulletList(list, MAX_PROPS_IN_MESSAGE);
                var issue = sink.Report(Severity.Major, TITLE_OBJECT, message, path,
                    StaticCheckUtil.HierarchyPath(go.transform), "Không field nào Missing", $"{list.Count} field Missing");
                if (issue != null && !prefabIssues.ContainsKey(path)) prefabIssues[path] = issue;
            }

            return order.Count;
        }

        static bool ShouldEnterChildren(SerializedProperty it)
        {
            if (it.propertyType == SerializedPropertyType.String) return false;
            return !(it.isArray && PRIMITIVE_ARRAY_TYPES.Contains(it.arrayElementType));
        }

        static bool IsMissingReference(SerializedProperty p)
        {
            return p.propertyType == SerializedPropertyType.ObjectReference && p.objectReferenceValue == null &&
                   p.objectReferenceInstanceIDValue != 0;
        }

        /// <summary>Field hỏng kế thừa nguyên từ prefab lồng / prefab gốc (trong phạm vi) → báo ở đó, không lặp.</summary>
        static bool IsReportedAtSource(Component comp, string propertyPath, string currentPath, StaticCheckConfig cfg)
        {
            var source = PrefabUtility.GetCorrespondingObjectFromSource(comp);
            if (source == null) return false;
            var sourcePath = AssetDatabase.GetAssetPath(source);
            if (string.IsNullOrEmpty(sourcePath) || sourcePath == currentPath ||
                !sourcePath.EndsWith(AssetRuleUtil.PREFAB_EXTENSION, StringComparison.OrdinalIgnoreCase) ||
                !StaticCheckUtil.InScope(sourcePath, cfg)) return false;
            using (var so = new SerializedObject(source))
            {
                var p = so.FindProperty(propertyPath);
                return p != null && IsMissingReference(p);
            }
        }

        #endregion

        #region (a) YAML — GUID treo

        /// <summary>-1 = file binary (bỏ qua), 0 = sạch, &gt;0 = số GUID treo.</summary>
        static int ScanYaml(string path, GuidResolver guids, AssetIssueSink sink,
            Dictionary<string, TestIssue> prefabIssues)
        {
            var abs = StaticCheckUtil.AbsolutePath(path);
            if (!AssetRuleUtil.IsYamlFile(abs)) return -1;

            var text = File.ReadAllText(abs);
            var ext = Path.GetExtension(path);
            var skipScriptKey = ext.Equals(".prefab", StringComparison.OrdinalIgnoreCase) ||
                                ext.Equals(".unity", StringComparison.OrdinalIgnoreCase);
            var missing = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var order = new List<string>();
            foreach (Match m in GUID_REF.Matches(text))
            {
                var g = m.Groups[1].Value;
                if (missing.ContainsKey(g) || !guids.IsMissing(g)) continue;
                var key = PropertyKeyAt(text, m.Index);
                // m_Script mất trong prefab/scene đã có luật missing-scripts báo theo object.
                if (skipScriptKey && key == KEY_SCRIPT) continue;
                missing[g] = key;
                order.Add(g);
            }

            if (order.Count == 0) return 0;

            var lines = new List<string>(order.Count);
            foreach (var g in order) lines.Add($"{missing[g]}: guid {g.ToLowerInvariant()}");
            var list = AssetRuleUtil.BulletList(lines, MAX_GUIDS_IN_MESSAGE);

            if (prefabIssues.TryGetValue(path, out var objectIssue))
            {
                objectIssue.message += "\n\nGUID treo trong file (asset đã bị xoá):" + list;
                return order.Count;
            }

            sink.Report(Severity.Major, TITLE_GUID,
                $"File tham chiếu {order.Count} asset không còn trong project (GUID không tồn tại). Khôi phục asset " +
                "bị xoá (kèm đúng file .meta) hoặc mở asset và gán lại tham chiếu." + list,
                path, null, "Mọi GUID đều tồn tại", $"{order.Count} GUID không tồn tại");
            return order.Count;
        }

        /// <summary>Tên property YAML chứa reference ở vị trí <paramref name="index" /> (text trước dấu ':' của dòng).</summary>
        static string PropertyKeyAt(string text, int index)
        {
            var lineStart = index > 0 ? text.LastIndexOf('\n', index - 1) + 1 : 0;
            var key = KeyOf(text.Substring(lineStart, index - lineStart));

            // Override trong prefab instance: "propertyPath: m_Sprite" nằm vài dòng phía trên "objectReference:".
            if (key == KEY_OBJECT_REFERENCE)
            {
                var propertyPath = FindPreviousValue(text, lineStart, KEY_PROPERTY_PATH, PROPERTY_PATH_LOOKBACK_LINES);
                return propertyPath != null ? propertyPath + " (override)" : key;
            }

            // Phần tử list "- {fileID…}" → lấy key cha gần nhất.
            if (string.IsNullOrEmpty(key))
            {
                var parent = FindParentKey(text, lineStart, PARENT_KEY_LOOKBACK_LINES);
                return parent != null ? parent + "[]" : "?";
            }

            return key;
        }

        static string KeyOf(string prefix)
        {
            var s = prefix.TrimStart();
            if (s.StartsWith("- ", StringComparison.Ordinal)) s = s.Substring(2).TrimStart();
            var colon = s.IndexOf(':');
            return colon > 0 ? s.Substring(0, colon).Trim() : "";
        }

        static string FindPreviousValue(string text, int lineStart, string prefix, int maxLines)
        {
            var end = lineStart - 1;
            for (var n = 0; n < maxLines && end > 0; n++)
            {
                var start = text.LastIndexOf('\n', end - 1) + 1;
                var line = text.Substring(start, end - start).Trim();
                if (line.StartsWith(prefix, StringComparison.Ordinal)) return line.Substring(prefix.Length).Trim();
                end = start - 1;
            }

            return null;
        }

        static string FindParentKey(string text, int lineStart, int maxLines)
        {
            var indent = IndentAt(text, lineStart);
            var end = lineStart - 1;
            for (var n = 0; n < maxLines && end > 0; n++)
            {
                var start = text.LastIndexOf('\n', end - 1) + 1;
                var raw = text.Substring(start, end - start).TrimEnd('\r');
                var lineIndent = IndentAt(text, start);
                var trimmed = raw.Trim();
                // Unity viết list cùng mức thụt với key cha: "  m_Materials:\n  - {fileID…}".
                if (lineIndent <= indent && trimmed.EndsWith(":", StringComparison.Ordinal) &&
                    !trimmed.StartsWith("-", StringComparison.Ordinal))
                    return trimmed.Substring(0, trimmed.Length - 1);
                end = start - 1;
            }

            return null;
        }

        static int IndentAt(string text, int lineStart)
        {
            var i = lineStart;
            while (i < text.Length && text[i] == ' ') i++;
            return i - lineStart;
        }

        #endregion
    }
}
