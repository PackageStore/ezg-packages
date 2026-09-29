using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace Ezg.AutoTest.Editor
{
    /// <summary>
    ///     Component "Missing Script" trong prefab (load asset, không instantiate) và scene (quét YAML text, KHÔNG
    ///     mở scene). Scene trong Build Settings luôn được quét kể cả khi nằm ngoài phạm vi.
    /// </summary>
    public sealed class MissingScriptsRule : IStaticRule
    {
        const string ISSUE_CATEGORY = "Asset.MissingScript";
        const string TITLE = "Object thiếu script (Missing Script)";
        const int MAX_GUIDS_IN_MESSAGE = 5;

        static readonly Regex SCRIPT_REF = new(@"m_Script: \{fileID: -?\d+, guid: ([0-9a-fA-F]{32})",
            RegexOptions.Compiled);

        public string Id => "missing-scripts";
        public string Name => "Missing Script (prefab + scene)";

        public string Description =>
            "Tìm component Missing Script trong prefab và scene (script đã xoá, đổi GUID hoặc class không compile). " +
            "Object mang Missing Script không chạy logic và làm lỗi khi build AssetBundle.";

        public string Category => "Asset";
        public int Order => 10;

        public async Task Run(AutoTestContext ctx)
        {
            var cfg = ctx.Config.staticCheck;
            var sink = new AssetIssueSink(ctx, ISSUE_CATEGORY);
            var objectsWithMissing = 0;

            var prefabs = AssetRuleUtil.FindPrefabs(cfg);
            for (var i = 0; i < prefabs.Count; i++)
            {
                await StaticCheckUtil.Yield(ctx, i, prefabs.Count, "Quét prefab");
                try
                {
                    objectsWithMissing += ScanPrefab(prefabs[i], cfg, sink);
                }
                catch (Exception e)
                {
                    sink.ReportUnreadable(prefabs[i], e);
                }
            }

            var scenes = AssetRuleUtil.CollectScenes(cfg);
            var guids = new GuidResolver();
            for (var i = 0; i < scenes.Count; i++)
            {
                await StaticCheckUtil.Yield(ctx, i, scenes.Count, "Quét scene");
                try
                {
                    objectsWithMissing += ScanScene(scenes[i], guids, sink);
                }
                catch (Exception e)
                {
                    sink.ReportUnreadable(scenes[i], e);
                }
            }

            ctx.Metric("Prefab đã quét", prefabs.Count);
            ctx.Metric("Scene đã quét", scenes.Count);
            ctx.Metric("Object thiếu script", objectsWithMissing);
            sink.Flush();
            EditorUtility.UnloadUnusedAssetsImmediate();
        }

        #region Prefab

        static int ScanPrefab(string path, StaticCheckConfig cfg, AssetIssueSink sink)
        {
            var root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (root == null)
            {
                sink.Report(Severity.Info, "Không đọc được asset", "AssetDatabase.LoadAssetAtPath trả null.", path);
                return 0;
            }

            var found = 0;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                var go = t.gameObject;
                var missing = CountMissing(go);
                if (missing == 0) continue;

                // Lỗi nằm sẵn trong prefab lồng / prefab gốc của variant (cũng trong phạm vi) → báo ở đó, không lặp lại.
                var source = PrefabUtility.GetCorrespondingObjectFromSource(go);
                var sourcePath = source != null ? AssetDatabase.GetAssetPath(source) : null;
                if (source != null && !string.IsNullOrEmpty(sourcePath) && sourcePath != path &&
                    StaticCheckUtil.InScope(sourcePath, cfg) && CountMissing(source) >= missing)
                    continue;

                found++;
                var message =
                    $"{missing} component trên object này trỏ tới script không còn tồn tại (script đã xoá, đổi GUID " +
                    "trong .meta hoặc class lỗi compile). Mở prefab, gán lại script hoặc xoá component Missing.";
                if (!string.IsNullOrEmpty(sourcePath) && sourcePath != path)
                    message += $"\nObject thuộc prefab lồng/prefab gốc: {sourcePath}";
                sink.Report(Severity.Critical, TITLE, message, path, StaticCheckUtil.HierarchyPath(t),
                    "0 component Missing Script", $"{missing} component");
            }

            return found;
        }

        static int CountMissing(GameObject go)
        {
            var missing = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(go);
            var nulls = 0;
            foreach (var c in go.GetComponents<Component>())
                if (c == null)
                    nulls++;
            return Math.Max(missing, nulls);
        }

        #endregion

        #region Scene (YAML)

        static int ScanScene(string path, GuidResolver guids, AssetIssueSink sink)
        {
            var abs = StaticCheckUtil.AbsolutePath(path);
            if (!AssetRuleUtil.IsYamlFile(abs))
            {
                sink.Report(Severity.Info, "Không đọc được asset",
                    "Scene lưu dạng binary (Asset Serialization không phải Force Text) — không quét text được.", path);
                return 0;
            }

            var text = File.ReadAllText(abs);
            var missingGuids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match m in SCRIPT_REF.Matches(text))
            {
                var g = m.Groups[1].Value;
                if (guids.IsMissing(g)) missingGuids.Add(g);
            }

            if (missingGuids.Count == 0) return 0;

            // Chỉ parse chi tiết khi thật sự có GUID mất (đa số scene sạch → bỏ qua bước tốn kém này).
            var yaml = SceneYaml.Parse(text);
            var perObject = new Dictionary<long, List<string>>();
            var order = new List<long>();
            foreach (var doc in yaml.Docs.Values)
            {
                if (doc.ScriptGuid == null || !missingGuids.Contains(doc.ScriptGuid)) continue;
                if (!perObject.TryGetValue(doc.GameObjectId, out var list))
                {
                    list = new List<string>();
                    perObject[doc.GameObjectId] = list;
                    order.Add(doc.GameObjectId);
                }

                list.Add(doc.ScriptGuid);
            }

            foreach (var goId in order)
            {
                var list = perObject[goId];
                var message =
                    $"{list.Count} component (MonoBehaviour) trong scene trỏ tới script không còn tồn tại. " +
                    "Mở scene, gán lại script hoặc xoá component Missing.\nGUID script mất:" +
                    AssetRuleUtil.BulletList(list, MAX_GUIDS_IN_MESSAGE);
                var hint = yaml.DescribePrefabSource(goId);
                if (hint != null) message += "\n" + hint;
                sink.Report(Severity.Critical, TITLE, message, path, yaml.PathOf(goId), "0 component Missing Script",
                    $"{list.Count} component");
            }

            return order.Count;
        }

        /// <summary>Parser YAML tối giản cho scene: chỉ đọc key cấp 1 cần để dựng tên/hierarchy object.</summary>
        sealed class SceneYaml
        {
            const string DOC_HEADER = "--- !u!";
            const int CLASS_TRANSFORM = 4;
            const int CLASS_RECT_TRANSFORM = 224;
            const int MAX_DEPTH = 128;

            static readonly Regex FILE_ID = new(@"fileID: (-?\d+)", RegexOptions.Compiled);
            static readonly Regex GUID = new(@"guid: ([0-9a-fA-F]{32})", RegexOptions.Compiled);

            public readonly Dictionary<long, Doc> Docs = new();
            readonly Dictionary<long, Doc> _transformOfGameObject = new();

            public sealed class Doc
            {
                public int ClassId;
                public long FileId;
                public string Name;
                public long GameObjectId;
                public long FatherId;
                public long PrefabInstanceId;
                public string ScriptGuid;
                public string SourcePrefabGuid;
            }

            public static SceneYaml Parse(string text)
            {
                var yaml = new SceneYaml();
                Doc current = null;
                using (var reader = new StringReader(text))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        if (line.StartsWith(DOC_HEADER, StringComparison.Ordinal))
                        {
                            current = ParseHeader(line);
                            if (current != null) yaml.Docs[current.FileId] = current;
                            continue;
                        }

                        // Chỉ key cấp 1 (thụt đúng 2 space) — tránh nhầm với m_Name trong m_Modifications…
                        if (current == null || line.Length < 4 || line[0] != ' ' || line[1] != ' ' || line[2] == ' ')
                            continue;
                        if (line.StartsWith("  m_Name: ", StringComparison.Ordinal))
                            current.Name = line.Substring("  m_Name: ".Length).Trim();
                        else if (line.StartsWith("  m_GameObject: ", StringComparison.Ordinal))
                            current.GameObjectId = ParseFileId(line);
                        else if (line.StartsWith("  m_Father: ", StringComparison.Ordinal))
                            current.FatherId = ParseFileId(line);
                        else if (line.StartsWith("  m_PrefabInstance: ", StringComparison.Ordinal))
                            current.PrefabInstanceId = ParseFileId(line);
                        else if (line.StartsWith("  m_Script: ", StringComparison.Ordinal))
                            current.ScriptGuid = ParseGuid(line);
                        else if (line.StartsWith("  m_SourcePrefab: ", StringComparison.Ordinal))
                            current.SourcePrefabGuid = ParseGuid(line);
                    }
                }

                foreach (var doc in yaml.Docs.Values)
                    if ((doc.ClassId == CLASS_TRANSFORM || doc.ClassId == CLASS_RECT_TRANSFORM) && doc.GameObjectId != 0)
                        yaml._transformOfGameObject[doc.GameObjectId] = doc;
                return yaml;
            }

            /// <summary>"Root/Child/Leaf" từ chuỗi m_Father; object thuộc prefab instance (stripped) thì ghi fileID.</summary>
            public string PathOf(long gameObjectId)
            {
                var parts = new List<string>();
                var goId = gameObjectId;
                for (var depth = 0; depth < MAX_DEPTH && goId != 0; depth++)
                {
                    parts.Add(NameOf(goId));
                    if (!_transformOfGameObject.TryGetValue(goId, out var tr) || tr.FatherId == 0) break;
                    if (!Docs.TryGetValue(tr.FatherId, out var father)) break;
                    goId = father.GameObjectId;
                }

                parts.Reverse();
                return string.Join("/", parts);
            }

            /// <summary>Object nằm trong prefab instance đặt vào scene → gợi ý prefab nguồn.</summary>
            public string DescribePrefabSource(long gameObjectId)
            {
                if (!Docs.TryGetValue(gameObjectId, out var go) || go.PrefabInstanceId == 0) return null;
                if (!Docs.TryGetValue(go.PrefabInstanceId, out var instance) ||
                    string.IsNullOrEmpty(instance.SourcePrefabGuid)) return null;
                var source = AssetDatabase.GUIDToAssetPath(instance.SourcePrefabGuid);
                return string.IsNullOrEmpty(source)
                    ? $"Object thuộc prefab instance có prefab nguồn đã mất (guid {instance.SourcePrefabGuid})."
                    : $"Object thuộc prefab instance của: {source}";
            }

            string NameOf(long gameObjectId)
            {
                if (Docs.TryGetValue(gameObjectId, out var go) && !string.IsNullOrEmpty(go.Name)) return go.Name;
                return string.Format(CultureInfo.InvariantCulture, "(object fileID {0})", gameObjectId);
            }

            static Doc ParseHeader(string line)
            {
                // "--- !u!114 &123456789" hoặc "--- !u!1 &123 stripped"
                var parts = line.Split(' ');
                if (parts.Length < 3 || !parts[1].StartsWith("!u!", StringComparison.Ordinal) ||
                    !parts[2].StartsWith("&", StringComparison.Ordinal)) return null;
                if (!int.TryParse(parts[1].Substring(3), NumberStyles.Integer, CultureInfo.InvariantCulture,
                        out var classId)) return null;
                if (!long.TryParse(parts[2].Substring(1), NumberStyles.Integer, CultureInfo.InvariantCulture,
                        out var fileId)) return null;
                return new Doc { ClassId = classId, FileId = fileId };
            }

            static long ParseFileId(string line)
            {
                var m = FILE_ID.Match(line);
                return m.Success &&
                       long.TryParse(m.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)
                    ? id
                    : 0;
            }

            static string ParseGuid(string line)
            {
                var m = GUID.Match(line);
                return m.Success ? m.Groups[1].Value.ToLowerInvariant() : null;
            }
        }

        #endregion
    }
}
