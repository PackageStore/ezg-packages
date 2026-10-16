using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace Ezg.AutoTest.Editor
{
    /// <summary>
    ///     Quét tĩnh xem feature nào của game CÒN ĐƯỢC DÙNG (xem <see cref="LivenessConfig" />). Một feature "sống" khi có
    ///     ít nhất một trong: (1) tham chiếu code <c>&lt;Enum&gt;.&lt;Tên&gt;</c> trong <c>Assets/**/*.cs</c> — trừ file khai
    ///     báo enum và <c>codeRefExcludeFiles</c>; (2) giá trị số của enum nằm ở field YAML (prefab / scene / asset) có tên
    ///     khớp <c>serializedKeyPattern</c> — trừ prefab màn hình của chính nó (field "FeatureType" tự khai) và field khớp
    ///     <c>serializedKeyIgnorePattern</c>; (3) chuỗi <c>"Tên"</c> trong code (Enum.Parse, event "Open"+tên). Heuristic
    ///     ưu tiên KHÔNG bỏ sót: nghi ngờ thì coi là sống. Kết quả cache ở <c>Library/EzgAutoTest/liveness-cache.json</c>
    ///     theo hash (đường dẫn + kích thước + mtime của mọi .cs/.prefab/.unity/.asset + enum + tham số) nên chỉ quét lại
    ///     khi project đổi.
    /// </summary>
    public static class FeatureLivenessScanner
    {
        const string CACHE_RELATIVE_PATH = "Library/EzgAutoTest/liveness-cache.json";
        const string ASSETS_ROOT = "Assets";
        const double MEMO_SECONDS = 15d;
        const int MAX_SAMPLES = 4;
        const string YAML_MAGIC = "%YAML";
        const string SOURCE_SCAN = "scan";
        const string SOURCE_CACHE = "cache";

        static readonly string[] CODE_EXTENSIONS = { ".cs" };
        static readonly string[] YAML_EXTENSIONS = { ".prefab", ".unity", ".asset" };

        /// <summary>"  key: 123" (giá trị nguyên).</summary>
        static readonly Regex YAML_INT_FIELD = new(@"^\s*(?:-\s+)?([A-Za-z_][\w]*):\s*(-?\d+)\s*$",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);

        /// <summary>"  key:" (mở list / object).</summary>
        static readonly Regex YAML_OPEN_FIELD = new(@"^\s*([A-Za-z_][\w]*):\s*$",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);

        /// <summary>"  - 123" (phần tử list số).</summary>
        static readonly Regex YAML_INT_ITEM = new(@"^\s*-\s*(-?\d+)\s*$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

        static readonly Regex STRING_LITERAL = new("\"([A-Za-z_][A-Za-z0-9_]*)\"", RegexOptions.CultureInvariant | RegexOptions.Compiled);

        static Result _memo;
        static string _memoKey;
        static double _memoAt = double.NegativeInfinity;

        #region Kiểu dữ liệu

        /// <summary>Số tham chiếu của một feature.</summary>
        [Serializable]
        public sealed class FeatureInfo
        {
            public string name;
            public long value;
            public int codeRefs;
            public int serializedRefs;
            public int stringRefs;

            /// <summary>Vài nơi tham chiếu, dạng "file (field)" — để Feature.Dead / dev tra cứu.</summary>
            public List<string> samples = new();

            public bool HasAnyReference => codeRefs > 0 || serializedRefs > 0 || stringRefs > 0;
            public bool SerializedOnly => codeRefs == 0 && stringRefs == 0 && serializedRefs > 0;
        }

        /// <summary>Kết quả một lượt quét (hoặc đọc từ cache).</summary>
        public sealed class Result
        {
            public List<FeatureInfo> Features = new();
            public string SourceHash = "";
            public string ResolvedAt = "";
            public string Source = "";
            public int CodeFilesScanned;
            public int YamlFilesScanned;
            public double Milliseconds;
            public string EnumName = "";

            /// <summary>Lý do không quét được (adapter không có enum feature…), null = OK.</summary>
            public string Error;

            public IEnumerable<FeatureInfo> Dead => Features.Where(f => !f.HasAnyReference);
            public IEnumerable<FeatureInfo> SerializedOnly => Features.Where(f => f.SerializedOnly);

            public FeatureInfo Find(string name)
            {
                return Features.FirstOrDefault(f => string.Equals(f.name, name, StringComparison.OrdinalIgnoreCase));
            }
        }

        [Serializable]
        sealed class CacheFile
        {
            public string hash;
            public string resolvedAt;
            public string enumName;
            public int codeFiles;
            public int yamlFiles;
            public List<FeatureInfo> features = new();
        }

        #endregion

        #region Public

        /// <summary>
        ///     Điền <c>config.liveness.resolvedDead / resolvedSerializedOnly / resolvedAt / resolvedSource</c> nếu liveness
        ///     bật và chưa điền (hoặc <paramref name="force" />). Gọi trước khi chạy suite / bake config device / liệt kê
        ///     case. Không ném exception — lỗi thì để trống và cảnh báo (filter chỉ còn áp pattern + forceDead).
        /// </summary>
        public static Result EnsureResolved(AutoTestConfig config, IGameAdapter adapter = null, bool force = false)
        {
            var l = config?.liveness;
            if (l == null || !l.enabled) return null;
            if (!force && !string.IsNullOrEmpty(l.resolvedSource)) return _memo;
            try
            {
                var result = Resolve(config, adapter, force);
                Apply(result, l);
                if (result != null && result.Source == SOURCE_SCAN)
                    AutoTestLog.Info($"Liveness: quét {result.CodeFilesScanned} file .cs + {result.YamlFilesScanned} prefab/scene/asset " +
                                     $"trong {result.Milliseconds:0} ms — {l.resolvedDead.Count} feature chết, " +
                                     $"{l.resolvedSerializedOnly.Count} sống chỉ nhờ prefab/asset.");
                return result;
            }
            catch (Exception e)
            {
                AutoTestLog.Warn("Quét liveness feature lỗi — chỉ áp deadFeaturePatterns/forceDead: " + e.Message);
                return null;
            }
        }

        /// <summary>Quét (có cache + memo 15 s). <paramref name="adapter" /> null = tự tạo từ config.</summary>
        public static Result Resolve(AutoTestConfig config, IGameAdapter adapter = null, bool force = false)
        {
            config ??= new AutoTestConfig();
            adapter ??= AutoTestRegistry.CreateAdapter(config);
            var features = SafeFeatures(adapter, out var error);
            var enumName = EnumNameOf(features);
            var memoKey = MemoKey(config.liveness, enumName, features);
            if (!force && _memo != null && _memoKey == memoKey &&
                EditorApplication.timeSinceStartup - _memoAt < MEMO_SECONDS)
                return _memo;

            var watch = System.Diagnostics.Stopwatch.StartNew();
            var result = new Result { EnumName = enumName, Error = error };
            if (features.Count == 0)
            {
                result.Error ??= "Adapter không liệt kê được feature nào (enum Features chưa resolve?).";
                return Memo(result, memoKey);
            }

            var files = EnumerateFiles();
            result.SourceHash = Hash(files, config.liveness, enumName, features);
            var cache = force ? null : ReadCache();
            if (cache != null && cache.hash == result.SourceHash && cache.features != null && cache.features.Count > 0)
            {
                result.Features = cache.features;
                result.ResolvedAt = cache.resolvedAt;
                result.Source = SOURCE_CACHE;
                result.CodeFilesScanned = cache.codeFiles;
                result.YamlFilesScanned = cache.yamlFiles;
                result.Milliseconds = watch.Elapsed.TotalMilliseconds;
                return Memo(result, memoKey);
            }

            Scan(config.liveness, adapter, features, files, result);
            result.ResolvedAt = DateTimeOffset.Now.ToString("o", CultureInfo.InvariantCulture);
            result.Source = SOURCE_SCAN;
            result.Milliseconds = watch.Elapsed.TotalMilliseconds;
            WriteCache(result);
            return Memo(result, memoKey);
        }

        /// <summary>Ghi kết quả vào config (resolved*).</summary>
        public static void Apply(Result result, LivenessConfig l)
        {
            if (result == null || l == null) return;
            l.resolvedDead = result.Dead.Select(f => f.name).OrderBy(n => n, StringComparer.Ordinal).ToList();
            l.resolvedSerializedOnly = result.SerializedOnly.Select(f => f.name).OrderBy(n => n, StringComparer.Ordinal).ToList();
            l.resolvedAt = result.ResolvedAt ?? "";
            l.resolvedSource = string.IsNullOrEmpty(result.Source) ? (result.Error != null ? "error" : SOURCE_SCAN) : result.Source;
        }

        /// <summary>Xoá cache + memo (nút trong cửa sổ / khi dev muốn quét lại từ đầu).</summary>
        public static void ClearCache()
        {
            _memo = null;
            _memoKey = null;
            try
            {
                var path = CachePath();
                if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception e)
            {
                AutoTestLog.Warn("Không xoá được cache liveness: " + e.Message);
            }
        }

        #endregion

        #region Quét

        static void Scan(LivenessConfig l, IGameAdapter adapter, List<AutoTestFeature> features, List<FileEntry> files,
            Result result)
        {
            var byName = new Dictionary<string, FeatureInfo>(StringComparer.Ordinal);
            var byValue = new Dictionary<long, List<FeatureInfo>>();
            var ownPrefab = new Dictionary<string, FeatureInfo>(StringComparer.OrdinalIgnoreCase);
            var resolver = adapter as IFeaturePrefabResolver;
            foreach (var f in features)
            {
                var info = new FeatureInfo { name = f.Name, value = f.Value };
                byName[f.Name] = info;
                if (!byValue.TryGetValue(f.Value, out var list)) byValue[f.Value] = list = new List<FeatureInfo>();
                list.Add(info);
                result.Features.Add(info);
                if (resolver == null) continue;
                try
                {
                    var prefab = resolver.ResolvePrefabName(f);
                    if (!string.IsNullOrEmpty(prefab)) ownPrefab[prefab] = info;
                }
                catch (Exception)
                {
                    // Adapter không tính được tên prefab → không loại tự tham chiếu, vẫn an toàn (thiên về sống).
                }
            }

            var enumName = result.EnumName;
            var codeRef = string.IsNullOrEmpty(enumName)
                ? null
                : new Regex(@"\b" + Regex.Escape(enumName) + @"\.([A-Za-z_][A-Za-z0-9_]*)\b", RegexOptions.CultureInvariant);
            var enumDecl = string.IsNullOrEmpty(enumName)
                ? null
                : new Regex(@"\benum\s+" + Regex.Escape(enumName) + @"\b", RegexOptions.CultureInvariant);
            var excludeFiles = new HashSet<string>(l.codeRefExcludeFiles ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
            var keyPattern = SafeRegex(l.serializedKeyPattern, "serializedKeyPattern");
            var keyIgnore = SafeRegex(l.serializedKeyIgnorePattern, "serializedKeyIgnorePattern");

            foreach (var file in files)
            {
                if (file.IsCode)
                {
                    if (excludeFiles.Contains(Path.GetFileName(file.Path))) continue;
                    result.CodeFilesScanned++;
                    ScanCode(file, codeRef, enumDecl, byName, l.countStringLiterals);
                }
                else
                {
                    if (!IsYaml(file.Path)) continue;
                    result.YamlFilesScanned++;
                    ScanYaml(file, byValue, ownPrefab, keyPattern, keyIgnore);
                }
            }
        }

        static void ScanCode(FileEntry file, Regex codeRef, Regex enumDecl, Dictionary<string, FeatureInfo> byName,
            bool countStrings)
        {
            string text;
            try
            {
                text = File.ReadAllText(file.Path);
            }
            catch (Exception)
            {
                return;
            }

            // File khai báo enum: mọi "Tên = 1" đều khớp regex, không phải tham chiếu.
            if (enumDecl != null && enumDecl.IsMatch(text)) return;
            var label = AssetPath(file.Path);
            if (codeRef != null)
                foreach (Match m in codeRef.Matches(text))
                    if (byName.TryGetValue(m.Groups[1].Value, out var info))
                    {
                        info.codeRefs++;
                        AddSample(info, label + " (code)");
                    }

            if (!countStrings) return;
            foreach (Match m in STRING_LITERAL.Matches(text))
                if (byName.TryGetValue(m.Groups[1].Value, out var info))
                {
                    info.stringRefs++;
                    AddSample(info, label + " (chuỗi)");
                }
        }

        static void ScanYaml(FileEntry file, Dictionary<long, List<FeatureInfo>> byValue,
            Dictionary<string, FeatureInfo> ownPrefab, Regex keyPattern, Regex keyIgnore)
        {
            string[] lines;
            try
            {
                lines = File.ReadAllLines(file.Path);
            }
            catch (Exception)
            {
                return;
            }

            var baseName = Path.GetFileNameWithoutExtension(file.Path);
            ownPrefab.TryGetValue(baseName, out var self);
            var label = AssetPath(file.Path);
            string listKey = null;
            foreach (var raw in lines)
            {
                var m = YAML_INT_FIELD.Match(raw);
                if (m.Success)
                {
                    listKey = null;
                    var key = m.Groups[1].Value;
                    if (KeyMatches(key, keyPattern, keyIgnore)) Hit(byValue, self, label, key, m.Groups[2].Value);
                    continue;
                }

                m = YAML_OPEN_FIELD.Match(raw);
                if (m.Success)
                {
                    listKey = KeyMatches(m.Groups[1].Value, keyPattern, keyIgnore) ? m.Groups[1].Value : null;
                    continue;
                }

                if (listKey != null)
                {
                    m = YAML_INT_ITEM.Match(raw);
                    if (m.Success)
                    {
                        Hit(byValue, self, label, listKey + "[]", m.Groups[1].Value);
                        continue;
                    }

                    if (!raw.TrimStart().StartsWith("-", StringComparison.Ordinal)) listKey = null;
                }
            }
        }

        static void Hit(Dictionary<long, List<FeatureInfo>> byValue, FeatureInfo self, string label, string key, string number)
        {
            if (!long.TryParse(number, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)) return;
            if (!byValue.TryGetValue(value, out var list)) return;
            foreach (var info in list)
            {
                if (info == self) continue; // prefab màn hình tự khai FeatureType của nó
                info.serializedRefs++;
                AddSample(info, $"{label} ({key})");
            }
        }

        static bool KeyMatches(string key, Regex pattern, Regex ignore)
        {
            if (pattern != null && !pattern.IsMatch(key)) return false;
            return ignore == null || !ignore.IsMatch(key);
        }

        static void AddSample(FeatureInfo info, string sample)
        {
            if (info.samples.Count >= MAX_SAMPLES || info.samples.Contains(sample)) return;
            info.samples.Add(sample);
        }

        #endregion

        #region File / hash / cache

        struct FileEntry
        {
            public string Path;
            public long Size;
            public long Ticks;
            public bool IsCode;
        }

        static List<FileEntry> EnumerateFiles()
        {
            var root = Path.Combine(ProjectRoot(), ASSETS_ROOT);
            var result = new List<FileEntry>();
            if (!Directory.Exists(root)) return result;
            var code = new HashSet<string>(CODE_EXTENSIONS, StringComparer.OrdinalIgnoreCase);
            var yaml = new HashSet<string>(YAML_EXTENSIONS, StringComparer.OrdinalIgnoreCase);
            var stack = new Stack<DirectoryInfo>();
            stack.Push(new DirectoryInfo(root));
            while (stack.Count > 0)
            {
                var dir = stack.Pop();
                IEnumerable<FileSystemInfo> entries;
                try
                {
                    entries = dir.EnumerateFileSystemInfos();
                }
                catch (Exception)
                {
                    continue;
                }

                foreach (var entry in entries)
                {
                    if (AssetRuleUtil.IsIgnoredByUnity(entry.Name)) continue;
                    if (entry is DirectoryInfo sub)
                    {
                        stack.Push(sub);
                        continue;
                    }

                    var ext = Path.GetExtension(entry.Name);
                    var isCode = code.Contains(ext);
                    if (!isCode && !yaml.Contains(ext)) continue;
                    var fi = (FileInfo)entry;
                    result.Add(new FileEntry
                    {
                        Path = fi.FullName.Replace('\\', '/'),
                        Size = fi.Length,
                        Ticks = fi.LastWriteTimeUtc.Ticks,
                        IsCode = isCode
                    });
                }
            }

            result.Sort((a, b) => string.CompareOrdinal(a.Path, b.Path));
            return result;
        }

        static bool IsYaml(string absolutePath)
        {
            try
            {
                return AssetRuleUtil.IsYamlFile(absolutePath);
            }
            catch (Exception)
            {
                return false;
            }
        }

        static string Hash(List<FileEntry> files, LivenessConfig l, string enumName, List<AutoTestFeature> features)
        {
            using (var sha = SHA1.Create())
            {
                var sb = new StringBuilder();
                sb.Append("v1|").Append(enumName).Append('|');
                foreach (var f in features) sb.Append(f.Name).Append('=').Append(f.Value).Append(';');
                sb.Append('|').Append(string.Join(",", l.codeRefExcludeFiles ?? new List<string>()))
                    .Append('|').Append(l.serializedKeyPattern).Append('|').Append(l.serializedKeyIgnorePattern)
                    .Append('|').Append(l.countStringLiterals).Append('\n');
                foreach (var f in files)
                    sb.Append(f.Path).Append('|').Append(f.Size).Append('|').Append(f.Ticks).Append('\n');
                var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()));
                var hex = new StringBuilder(bytes.Length * 2);
                foreach (var b in bytes) hex.Append(b.ToString("x2", CultureInfo.InvariantCulture));
                return hex.ToString();
            }
        }

        static string CachePath()
        {
            return Path.Combine(ProjectRoot(), CACHE_RELATIVE_PATH).Replace('\\', '/');
        }

        static CacheFile ReadCache()
        {
            try
            {
                var path = CachePath();
                if (!File.Exists(path)) return null;
                return JsonUtility.FromJson<CacheFile>(File.ReadAllText(path));
            }
            catch (Exception)
            {
                return null;
            }
        }

        static void WriteCache(Result result)
        {
            try
            {
                var path = CachePath();
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                var cache = new CacheFile
                {
                    hash = result.SourceHash,
                    resolvedAt = result.ResolvedAt,
                    enumName = result.EnumName,
                    codeFiles = result.CodeFilesScanned,
                    yamlFiles = result.YamlFilesScanned,
                    features = result.Features
                };
                File.WriteAllText(path, JsonUtility.ToJson(cache, true));
            }
            catch (Exception e)
            {
                AutoTestLog.Warn("Không ghi được cache liveness: " + e.Message);
            }
        }

        static Result Memo(Result result, string key)
        {
            _memo = result;
            _memoKey = key;
            _memoAt = EditorApplication.timeSinceStartup;
            return result;
        }

        static string MemoKey(LivenessConfig l, string enumName, List<AutoTestFeature> features)
        {
            return enumName + "|" + features.Count + "|" + string.Join(",", l.codeRefExcludeFiles ?? new List<string>()) +
                   "|" + l.serializedKeyPattern + "|" + l.serializedKeyIgnorePattern + "|" + l.countStringLiterals;
        }

        #endregion

        #region Helpers

        static List<AutoTestFeature> SafeFeatures(IGameAdapter adapter, out string error)
        {
            error = null;
            if (adapter == null)
            {
                error = "Không tạo được adapter game.";
                return new List<AutoTestFeature>();
            }

            try
            {
                return adapter.GetFeatures()?.Where(f => f != null && !string.IsNullOrEmpty(f.Name)).ToList() ??
                       new List<AutoTestFeature>();
            }
            catch (Exception e)
            {
                error = $"Adapter {adapter.Name} lỗi khi liệt kê feature: {e.Message}";
                return new List<AutoTestFeature>();
            }
        }

        /// <summary>Tên ngắn của enum (vd "Features") lấy từ giá trị Raw — dùng cho regex tham chiếu code.</summary>
        static string EnumNameOf(List<AutoTestFeature> features)
        {
            foreach (var f in features)
                if (f.Raw != null)
                    return f.Raw.GetType().Name;
            return "";
        }

        static Regex SafeRegex(string pattern, string label)
        {
            if (string.IsNullOrWhiteSpace(pattern)) return null;
            try
            {
                return new Regex(pattern, RegexOptions.CultureInvariant);
            }
            catch (ArgumentException e)
            {
                AutoTestLog.Warn($"liveness.{label} sai cú pháp ({e.Message}) — bỏ qua điều kiện này.");
                return null;
            }
        }

        static string ProjectRoot()
        {
            return Path.GetDirectoryName(Application.dataPath)!.Replace('\\', '/');
        }

        static string AssetPath(string absolute)
        {
            var root = ProjectRoot() + "/";
            return absolute.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? absolute.Substring(root.Length) : absolute;
        }

        #endregion
    }
}
