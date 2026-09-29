using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

namespace Ezg.AutoTest.Editor
{
    /// <summary>
    ///     Sinh khung cho phần việc riêng của từng project: thư mục AutoTests + asmdef, file kịch bản mới (có
    ///     comment "// AI:" hướng dẫn agent triển khai), project hooks, adapter cho project không theo template
    ///     EZG, prompt dán cho Claude Code và cài skill AI vào .claude/skills. Mọi hàm idempotent — không bao
    ///     giờ ghi đè file đã có.
    /// </summary>
    public static class AutoTestScaffolder
    {
        #region Hằng số

        /// <summary>Tên skill AI (thư mục trong .claude/skills).</summary>
        public const string SKILL_NAME = "ezg-autotest-scenario";

        /// <summary>Tài liệu agent phải đọc trước khi viết kịch bản.</summary>
        public const string GUIDE_PATH = "Packages/com.ezg.autotest/Documentation~/AI-SCENARIO-GUIDE.md";

        const string PACKAGE_NAME = "com.ezg.autotest";
        const string SKILL_SOURCE_RELATIVE = "Documentation~/AI/" + SKILL_NAME + "/SKILL.md";
        const string GUIDE_RELATIVE = "Documentation~/AI-SCENARIO-GUIDE.md";
        const string PROJECT_ROOT_FOLDER = "Assets/_Project";
        const string PROJECT_AUTOTESTS_FOLDER = "Assets/_Project/AutoTests";
        const string FALLBACK_AUTOTESTS_FOLDER = "Assets/AutoTests";
        const string SCENARIOS_SUBFOLDER = "Scenarios";
        const string DEFAULT_CATEGORY = "Gameplay";
        const string DEFAULT_SCENARIO_NAME = "New";
        const string DEFAULT_PRODUCT_NAME = "Game";
        const string SCENARIO_SUFFIX = "Scenario";
        const string AUTOTESTS_SUFFIX = ".AutoTests";
        const string HOOKS_SUFFIX = "AutoTestHooks";
        const string ADAPTER_SUFFIX = "GameAdapter";
        const string ASSEMBLY_CSHARP = "Assembly-CSharp";
        const string AUTOTEST_ASSEMBLY = "Ezg.AutoTest";
        const string UGUI_ASSEMBLY = "UnityEngine.UI";
        const string TMP_ASSEMBLY = "Unity.TextMeshPro";
        const string DEFINE_CONSTRAINT = "UNITY_EDITOR || EZG_AUTOTEST";
        const string UI_MANAGER_TYPE = "UIManager";
        const string FEATURE_BASE_TYPE = "FeatureBaseController";
        const string GUID_REFERENCE_PREFIX = "GUID:";
        const int FEATURE_CONTROLLER_WEIGHT = 2;
        const int MAX_UNIQUE_SUFFIX = 99;

        /// <summary>Đoạn đường dẫn asmdef KHÔNG phải code game (third-party, sample, editor, test).</summary>
        static readonly string[] EXCLUDED_ASMDEF_PATH_PARTS =
        {
            "/Plugins/", "/ThirdParty/", "/3rdParty/", "/Third Party/", "/Samples/", "/Editor/", "/Tests/",
            "/Test/", "/AutoTests/"
        };

        #endregion

        #region Menu

        [MenuItem("Assets/Create/EZG/Auto Test Scenario", false, 81)]
        static void CreateScenarioMenu()
        {
            AutoTestNewScenarioWindow.Open(GuessCategoryFromSelection());
        }

        /// <summary>Chuột phải trong Scenarios/&lt;Nhóm&gt;/ ⇒ điền sẵn nhóm đó.</summary>
        static string GuessCategoryFromSelection()
        {
            var obj = Selection.activeObject;
            if (obj == null) return null;
            var path = AssetDatabase.GetAssetPath(obj);
            if (string.IsNullOrEmpty(path)) return null;
            var marker = "/" + SCENARIOS_SUBFOLDER + "/";
            var idx = path.IndexOf(marker, StringComparison.Ordinal);
            if (idx < 0) return null;
            var rest = path.Substring(idx + marker.Length);
            var slash = rest.IndexOf('/');
            var segment = slash < 0 ? rest : rest.Substring(0, slash);
            return segment.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ? null : segment;
        }

        #endregion

        #region Phát hiện assembly game

        /// <summary>
        ///     Tên asmdef chứa code game (dưới Assets/, không phải Plugins/ThirdParty/Samples/Editor/test): ưu tiên
        ///     assembly có type tên "UIManager", sau đó assembly có nhiều MonoBehaviour/FeatureBaseController nhất.
        ///     Trả null khi game nằm trong Assembly-CSharp (không có asmdef).
        /// </summary>
        public static string DetectGameAssembly()
        {
            var candidates = new List<string>();
            foreach (var asm in CompilationPipeline.GetAssemblies(AssembliesType.Player))
            {
                if (asm == null || string.IsNullOrEmpty(asm.name)) continue;
                if (IsExcludedAssemblyName(asm.name)) continue;
                var asmdefPath = CompilationPipeline.GetAssemblyDefinitionFilePathFromAssemblyName(asm.name);
                if (!IsGameAsmdefPath(asmdefPath)) continue;
                if (!candidates.Contains(asm.name)) candidates.Add(asm.name);
            }

            if (candidates.Count == 0) return null;

            var scores = ComputeAssemblyScores();
            var best = candidates
                .OrderByDescending(n => AssemblyHasType(n, UI_MANAGER_TYPE))
                .ThenByDescending(n => ScoreOf(scores, n))
                .ThenBy(n => n, StringComparer.Ordinal)
                .First();

            var bestHasUi = AssemblyHasType(best, UI_MANAGER_TYPE);
            if (bestHasUi) return best;

            // Không asmdef nào có UIManager: nếu Assembly-CSharp có (hoặc nhiều code hơn) ⇒ game ở Assembly-CSharp.
            if (AssemblyHasType(ASSEMBLY_CSHARP, UI_MANAGER_TYPE)) return null;
            var bestScore = ScoreOf(scores, best);
            if (bestScore == 0) return null;
            return ScoreOf(scores, ASSEMBLY_CSHARP) > bestScore ? null : best;
        }

        static bool IsExcludedAssemblyName(string name)
        {
            if (name.StartsWith(ASSEMBLY_CSHARP, StringComparison.Ordinal)) return true;
            if (name.StartsWith("Unity.", StringComparison.Ordinal) ||
                name.StartsWith("UnityEngine.", StringComparison.Ordinal))
                return true;
            // Segment kết thúc bằng "Test"/"Tests" (phân biệt hoa thường để "Contest" không bị loại).
            foreach (var segment in name.Split('.'))
                if (segment.EndsWith("Test", StringComparison.Ordinal) ||
                    segment.EndsWith("Tests", StringComparison.Ordinal))
                    return true;
            return false;
        }

        static bool IsGameAsmdefPath(string asmdefPath)
        {
            if (string.IsNullOrEmpty(asmdefPath)) return false;
            var p = "/" + asmdefPath.Replace('\\', '/');
            if (!p.StartsWith("/Assets/", StringComparison.Ordinal)) return false;
            foreach (var part in EXCLUDED_ASMDEF_PATH_PARTS)
                if (p.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0)
                    return false;
            return true;
        }

        /// <summary>Điểm mỗi assembly = số MonoBehaviour + trọng số × số FeatureBaseController.</summary>
        static Dictionary<string, int> ComputeAssemblyScores()
        {
            var scores = new Dictionary<string, int>();
            foreach (var t in TypeCache.GetTypesDerivedFrom<MonoBehaviour>())
                AddScore(scores, t, 1);

            var featureBase = AutoTestRegistry.FindType(FEATURE_BASE_TYPE);
            if (featureBase != null)
                foreach (var t in TypeCache.GetTypesDerivedFrom(featureBase))
                    AddScore(scores, t, FEATURE_CONTROLLER_WEIGHT);
            return scores;
        }

        static void AddScore(Dictionary<string, int> scores, Type t, int amount)
        {
            if (t == null || t.IsAbstract) return;
            var name = t.Assembly.GetName().Name;
            scores[name] = ScoreOf(scores, name) + amount;
        }

        static int ScoreOf(Dictionary<string, int> scores, string assemblyName)
        {
            return scores.TryGetValue(assemblyName, out var v) ? v : 0;
        }

        static bool AssemblyHasType(string assemblyName, string typeName)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (asm.IsDynamic || asm.GetName().Name != assemblyName) continue;
                Type[] types;
                try
                {
                    types = asm.GetTypes();
                }
                catch (System.Reflection.ReflectionTypeLoadException e)
                {
                    types = e.Types.Where(t => t != null).ToArray();
                }
                catch
                {
                    return false;
                }

                foreach (var t in types)
                    if (t != null && t.Name == typeName)
                        return true;
                return false;
            }

            return false;
        }

        #endregion

        #region Thư mục + asmdef

        /// <summary>Đường dẫn (asset path) thư mục AutoTests — không tạo gì.</summary>
        public static string GetAutoTestsFolderPath()
        {
            if (AssetDatabase.IsValidFolder(PROJECT_AUTOTESTS_FOLDER)) return PROJECT_AUTOTESTS_FOLDER;
            if (AssetDatabase.IsValidFolder(FALLBACK_AUTOTESTS_FOLDER)) return FALLBACK_AUTOTESTS_FOLDER;
            return AssetDatabase.IsValidFolder(PROJECT_ROOT_FOLDER) ? PROJECT_AUTOTESTS_FOLDER : FALLBACK_AUTOTESTS_FOLDER;
        }

        /// <summary>
        ///     Tạo Assets/_Project/AutoTests/ (hoặc Assets/AutoTests/ nếu không có Assets/_Project) và — khi game
        ///     nằm trong asmdef — asmdef "&lt;Product&gt;.AutoTests" tham chiếu Ezg.AutoTest + asmdef game + mọi
        ///     reference của asmdef game. Không ghi đè asmdef đã có. Trả asset path của thư mục.
        /// </summary>
        public static string EnsureAutoTestsFolder()
        {
            var folder = GetAutoTestsFolderPath();
            var abs = ToAbsolute(folder);
            var changed = false;
            if (!Directory.Exists(abs))
            {
                Directory.CreateDirectory(abs);
                changed = true;
            }

            var gameAssembly = DetectGameAssembly();
            if (!string.IsNullOrEmpty(gameAssembly) && !HasAsmdef(abs))
            {
                var asmdefName = GetProductNameSanitized() + AUTOTESTS_SUFFIX;
                // Đã có assembly trùng tên ở nơi khác (user dời thư mục) ⇒ không tạo bản thứ hai.
                if (string.IsNullOrEmpty(CompilationPipeline.GetAssemblyDefinitionFilePathFromAssemblyName(asmdefName)))
                {
                    var json = BuildAsmdefJson(asmdefName, gameAssembly);
                    WriteText(Path.Combine(abs, asmdefName + ".asmdef"), json);
                    changed = true;
                }
            }

            if (changed) AssetDatabase.Refresh();
            return folder;
        }

        static bool HasAsmdef(string absFolder)
        {
            return Directory.Exists(absFolder) &&
                   Directory.EnumerateFiles(absFolder, "*.asmdef", SearchOption.TopDirectoryOnly).Any();
        }

        [Serializable]
        sealed class AsmdefData
        {
            // Gán null tường minh: JsonUtility điền giá trị, tránh cảnh báo CS0649.
            public string name = null;
            public string[] references = null;
        }

        static string BuildAsmdefJson(string asmdefName, string gameAssembly)
        {
            var refs = new List<string>();
            var names = new HashSet<string>(StringComparer.Ordinal);

            void AddRef(string reference)
            {
                if (string.IsNullOrWhiteSpace(reference) || refs.Contains(reference)) return;
                var resolved = ResolveReferenceName(reference);
                if (resolved != null && !names.Add(resolved)) return; // cùng assembly, khác cách ghi (tên/GUID)
                refs.Add(reference);
            }

            AddRef(AUTOTEST_ASSEMBLY);
            AddRef(gameAssembly);
            var gameAsmdefPath = CompilationPipeline.GetAssemblyDefinitionFilePathFromAssemblyName(gameAssembly);
            var gameAsmdef = ReadAsmdef(gameAsmdefPath);
            if (gameAsmdef?.references != null)
                foreach (var r in gameAsmdef.references)
                    AddRef(r);
            AddRef(UGUI_ASSEMBLY);
            AddRef(TMP_ASSEMBLY);

            var sb = new StringBuilder();
            sb.Append("{\n");
            sb.Append("    \"name\": ").Append(JsonString(asmdefName)).Append(",\n");
            sb.Append("    \"rootNamespace\": \"\",\n");
            sb.Append("    \"references\": [\n");
            for (var i = 0; i < refs.Count; i++)
                sb.Append("        ").Append(JsonString(refs[i])).Append(i < refs.Count - 1 ? ",\n" : "\n");
            sb.Append("    ],\n");
            sb.Append("    \"includePlatforms\": [],\n");
            sb.Append("    \"excludePlatforms\": [],\n");
            sb.Append("    \"allowUnsafeCode\": false,\n");
            sb.Append("    \"overrideReferences\": false,\n");
            sb.Append("    \"precompiledReferences\": [],\n");
            sb.Append("    \"autoReferenced\": false,\n");
            sb.Append("    \"defineConstraints\": [\n");
            sb.Append("        ").Append(JsonString(DEFINE_CONSTRAINT)).Append('\n');
            sb.Append("    ],\n");
            sb.Append("    \"versionDefines\": [],\n");
            sb.Append("    \"noEngineReferences\": false\n");
            sb.Append("}\n");
            return sb.ToString();
        }

        static AsmdefData ReadAsmdef(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath)) return null;
            try
            {
                var abs = ToAbsolute(assetPath);
                return File.Exists(abs) ? JsonUtility.FromJson<AsmdefData>(File.ReadAllText(abs)) : null;
            }
            catch (Exception e)
            {
                AutoTestLog.Warn($"Không đọc được asmdef '{assetPath}': {e.Message}");
                return null;
            }
        }

        /// <summary>Tên assembly của một reference asmdef (dạng tên hoặc "GUID:xxx") — chỉ để lọc trùng.</summary>
        static string ResolveReferenceName(string reference)
        {
            if (!reference.StartsWith(GUID_REFERENCE_PREFIX, StringComparison.OrdinalIgnoreCase)) return reference;
            var path = AssetDatabase.GUIDToAssetPath(reference.Substring(GUID_REFERENCE_PREFIX.Length));
            return ReadAsmdef(path)?.name;
        }

        #endregion

        #region Kịch bản

        /// <summary>Asset path file kịch bản sẽ được tạo cho tên/nhóm này (không tạo gì).</summary>
        public static string GetScenarioAssetPath(string name, string category)
        {
            var className = ToScenarioClassName(name);
            var categoryFolder = ToPascalIdentifier(NormalizeCategory(category), DEFAULT_CATEGORY);
            return GetAutoTestsFolderPath() + "/" + SCENARIOS_SUBFOLDER + "/" + categoryFolder + "/" + className + ".cs";
        }

        /// <summary>Tên class kịch bản (PascalCase, bỏ dấu, hậu tố "Scenario").</summary>
        public static string ToScenarioClassName(string name)
        {
            var className = ToPascalIdentifier(name, DEFAULT_SCENARIO_NAME);
            if (!className.EndsWith(SCENARIO_SUFFIX, StringComparison.Ordinal)) className += SCENARIO_SUFFIX;
            return className;
        }

        /// <summary>
        ///     Tạo &lt;AutoTests&gt;/Scenarios/&lt;Nhóm&gt;/&lt;Tên&gt;Scenario.cs từ template (Run bắt đầu bằng
        ///     ctx.Skip cho tới khi được triển khai). File đã tồn tại ⇒ trả lại đường dẫn cũ, không ghi đè.
        /// </summary>
        public static string CreateScenario(string name, string category, string description)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Tên kịch bản không được trống.", nameof(name));

            var existingPath = GetScenarioAssetPath(name, category);
            if (File.Exists(ToAbsolute(existingPath))) return existingPath;

            var folder = EnsureAutoTestsFolder();
            var displayName = name.Trim();
            var categoryName = NormalizeCategory(category);
            var desc = string.IsNullOrWhiteSpace(description) ? "" : description.Trim();
            var ns = GetNamespace();
            var dir = folder + "/" + SCENARIOS_SUBFOLDER + "/" + ToPascalIdentifier(categoryName, DEFAULT_CATEGORY);

            var baseClassName = ToScenarioClassName(name);
            var className = baseClassName;
            var assetPath = dir + "/" + className + ".cs";
            if (File.Exists(ToAbsolute(assetPath))) return assetPath;

            // Trùng class cùng namespace ở file khác (vd nhóm khác) ⇒ thêm số để không lỗi compile.
            for (var i = 2; ScenarioTypeExists(ns + "." + className) && i <= MAX_UNIQUE_SUFFIX; i++)
            {
                className = baseClassName + i.ToString(CultureInfo.InvariantCulture);
                assetPath = dir + "/" + className + ".cs";
                if (File.Exists(ToAbsolute(assetPath))) return assetPath;
            }

            var content = SCENARIO_TEMPLATE
                .Replace("{{GUIDE}}", GUIDE_PATH)
                .Replace("{{SKILL}}", SKILL_NAME)
                .Replace("{{NAMESPACE}}", ns)
                .Replace("{{CLASS}}", className)
                .Replace("{{SUMMARY}}", EscapeXml(string.IsNullOrEmpty(desc) ? displayName : desc))
                .Replace("{{NAME}}", EscapeCs(displayName))
                .Replace("{{CATEGORY}}", EscapeCs(categoryName))
                .Replace("{{DESCRIPTION}}", EscapeCs(desc));

            Directory.CreateDirectory(ToAbsolute(dir));
            WriteText(ToAbsolute(assetPath), content);
            AssetDatabase.Refresh();
            return assetPath;
        }

        static bool ScenarioTypeExists(string fullName)
        {
            foreach (var t in TypeCache.GetTypesDerivedFrom<AutoTestScenario>())
                if (t.FullName == fullName)
                    return true;
            return false;
        }

        static string NormalizeCategory(string category)
        {
            return string.IsNullOrWhiteSpace(category) ? DEFAULT_CATEGORY : category.Trim();
        }

        #endregion

        #region Hooks + Adapter

        /// <summary>
        ///     Tạo &lt;AutoTests&gt;/&lt;Product&gt;AutoTestHooks.cs khi project CHƯA có class nào implement
        ///     <see cref="IAutoTestProjectHooks" />. Đã có ⇒ trả đường dẫn script của class đó (null nếu không
        ///     định vị được file).
        /// </summary>
        public static string CreateProjectHooks()
        {
            var existingType = FindExistingHooksType();
            if (existingType != null) return FindScriptPath(existingType);

            var folder = EnsureAutoTestsFolder();
            var className = GetProductNameSanitized() + HOOKS_SUFFIX;
            var assetPath = folder + "/" + className + ".cs";
            if (File.Exists(ToAbsolute(assetPath))) return assetPath;

            var content = HOOKS_TEMPLATE
                .Replace("{{GUIDE}}", GUIDE_PATH)
                .Replace("{{NAMESPACE}}", GetNamespace())
                .Replace("{{CLASS}}", className)
                .Replace("{{PRODUCT}}", EscapeXml(GetProductDisplayName()));
            WriteText(ToAbsolute(assetPath), content);
            AssetDatabase.Refresh();
            return assetPath;
        }

        /// <summary>
        ///     Tạo &lt;AutoTests&gt;/&lt;Product&gt;GameAdapter.cs — adapter (kế thừa GenericGameAdapter, Priority
        ///     200) cho project KHÔNG theo template EZG. Initialize trả false cho tới khi được triển khai để không
        ///     che mất adapter khác. File đã có ⇒ trả lại đường dẫn cũ.
        /// </summary>
        public static string CreateAdapterTemplate()
        {
            var folder = EnsureAutoTestsFolder();
            var className = GetProductNameSanitized() + ADAPTER_SUFFIX;
            var assetPath = folder + "/" + className + ".cs";
            if (File.Exists(ToAbsolute(assetPath))) return assetPath;

            var content = ADAPTER_TEMPLATE
                .Replace("{{GUIDE}}", GUIDE_PATH)
                .Replace("{{NAMESPACE}}", GetNamespace())
                .Replace("{{CLASS}}", className)
                .Replace("{{PRODUCT}}", EscapeCs(GetProductDisplayName()));
            WriteText(ToAbsolute(assetPath), content);
            AssetDatabase.Refresh();
            return assetPath;
        }

        static Type FindExistingHooksType()
        {
            foreach (var t in TypeCache.GetTypesDerivedFrom<IAutoTestProjectHooks>())
                if (t.IsClass && !t.IsAbstract && !t.IsGenericTypeDefinition)
                    return t;
            return null;
        }

        /// <summary>File .cs khai báo type (MonoScript.GetClass, fallback theo tên file).</summary>
        static string FindScriptPath(Type type)
        {
            string byFileName = null;
            foreach (var guid in AssetDatabase.FindAssets(type.Name + " t:MonoScript"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var script = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
                if (script != null && script.GetClass() == type) return path;
                if (byFileName == null && Path.GetFileNameWithoutExtension(path) == type.Name) byFileName = path;
            }

            return byFileName;
        }

        #endregion

        #region Prompt + skill AI

        /// <summary>Prompt tiếng Việt dán thẳng cho Claude Code để triển khai kịch bản vừa tạo.</summary>
        public static string BuildAiPrompt(string scenarioAssetPath, string featureHint)
        {
            var hint = string.IsNullOrWhiteSpace(featureHint)
                ? Path.GetFileNameWithoutExtension(scenarioAssetPath ?? "")
                : featureHint.Trim();
            var sb = new StringBuilder();
            sb.Append("Triển khai kịch bản auto test tại `").Append(scenarioAssetPath).Append("` cho tính năng: ")
                .Append(hint).Append(".\n\n");
            sb.Append("1. Đọc `").Append(GetGuideDiskPath()).Append("` trước (hoặc gọi skill `").Append(SKILL_NAME)
                .Append("` nếu project đã cài trong .claude/skills).\n");
            sb.Append("2. Đọc code tính năng liên quan (dùng codegraph nếu project có `.codegraph/`, không thì Grep/Read): ")
                .Append("controller màn hình, service, player data, config CSV, tên object/nút trong prefab.\n");
            sb.Append("3. Viết các bước bằng `ctx.Step(...)`, tên bước là hành động người chơi bằng tiếng Việt ")
                .Append("(\"Mở shop\", \"Bấm nút Mua gói 100 kim cương\"…) để QA đọc như các bước tái hiện. ")
                .Append("Chờ UI bằng `ctx.Ui.WaitFor` / `ctx.WaitUntil` (không sleep cố định dài), kiểm tra bằng ")
                .Append("`ctx.Check` / `ctx.Assert` với severity phù hợp, chụp màn hình ở điểm quan trọng, ")
                .Append("khôi phục mọi state đã đổi trong `TearDown`. Xoá dòng `ctx.Skip(\"Kịch bản chưa được triển khai\")` khi xong.\n");
            sb.Append("4. Chạy compile-check qua Unity MCP (Assets/Refresh → chờ compile xong → unity_get_compilation_errors) và sửa hết lỗi.\n");
            sb.Append("5. Chạy kịch bản trong cửa sổ EZG > Auto Test System (suite \"Kịch bản riêng\") hoặc qua ")
                .Append("`Ezg.AutoTest.Editor.AutoTestRunner` như hướng dẫn trong guide, đọc report (summary.md, report.json, ảnh chụp) ")
                .Append("rồi sửa tới khi pass — hoặc tới khi xác định được bug thật của game: khi đó KHÔNG nới lỏng kiểm tra ")
                .Append("cho pass, mà báo lại bug kèm các bước tái hiện, expected/actual và ảnh chụp.\n");
            sb.Append("6. Không git add/commit trừ khi được yêu cầu.\n");
            return sb.ToString();
        }

        /// <summary>
        ///     Đường dẫn guide AI trên đĩa, tương đối với project nếu nằm trong project — package embedded:
        ///     "Packages/com.ezg.autotest/…"; package từ registry: "Library/PackageCache/com.ezg.autotest@…/…".
        /// </summary>
        public static string GetGuideDiskPath()
        {
            var abs = Path.Combine(GetPackageRoot(), GUIDE_RELATIVE).Replace('\\', '/');
            if (!File.Exists(abs)) return GUIDE_PATH;
            var root = GetProjectRoot() + "/";
            return abs.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? abs.Substring(root.Length) : abs;
        }

        /// <summary>
        ///     Copy skill AI từ package sang &lt;project&gt;/.claude/skills/ezg-autotest-scenario/SKILL.md (chỉ ghi
        ///     khi nội dung khác). true = đã cài/đã đúng bản, <paramref name="path" /> = file đích; false = không
        ///     thấy file nguồn trong package, <paramref name="path" /> = đường dẫn nguồn đã tìm.
        /// </summary>
        public static bool InstallAiSkill(out string path)
        {
            var source = Path.Combine(GetPackageRoot(), SKILL_SOURCE_RELATIVE).Replace('\\', '/');
            if (!File.Exists(source))
            {
                path = source;
                AutoTestLog.Warn("Không tìm thấy skill AI trong package: " + source);
                return false;
            }

            var target = Path.Combine(GetProjectRoot(), ".claude", "skills", SKILL_NAME, "SKILL.md").Replace('\\', '/');
            path = target;
            var content = File.ReadAllText(source);
            if (File.Exists(target) && File.ReadAllText(target) == content) return true;

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            WriteText(target, content);
            AutoTestLog.Info("Đã cài skill AI: " + target);
            return true;
        }

        static string GetPackageRoot()
        {
            var info = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(AutoTestScaffolder).Assembly);
            if (info != null && !string.IsNullOrEmpty(info.resolvedPath)) return info.resolvedPath;
            return Path.GetFullPath(Path.Combine(GetProjectRoot(), "Packages", PACKAGE_NAME));
        }

        #endregion

        #region Tiện ích

        /// <summary>Tên sản phẩm dạng identifier C# (bỏ dấu, PascalCase) — vd "My Idle Game" → "MyIdleGame".</summary>
        public static string GetProductNameSanitized()
        {
            return ToPascalIdentifier(PlayerSettings.productName, DEFAULT_PRODUCT_NAME);
        }

        /// <summary>Namespace cho code sinh ra: "&lt;Product&gt;.AutoTests".</summary>
        public static string GetNamespace()
        {
            return GetProductNameSanitized() + AUTOTESTS_SUFFIX;
        }

        static string GetProductDisplayName()
        {
            var product = PlayerSettings.productName;
            return string.IsNullOrWhiteSpace(product) ? DEFAULT_PRODUCT_NAME : product.Trim();
        }

        /// <summary>
        ///     Chuỗi bất kỳ (kể cả tiếng Việt có dấu) → identifier PascalCase ASCII. Rỗng ⇒
        ///     <paramref name="fallback" />; bắt đầu bằng số ⇒ thêm <paramref name="fallback" /> phía trước.
        /// </summary>
        public static string ToPascalIdentifier(string input, string fallback)
        {
            if (string.IsNullOrWhiteSpace(input)) return fallback;
            var plain = RemoveDiacritics(input);
            var sb = new StringBuilder(plain.Length);
            var upperNext = true;
            foreach (var ch in plain)
            {
                if (ch < 128 && char.IsLetterOrDigit(ch))
                {
                    sb.Append(upperNext ? char.ToUpperInvariant(ch) : ch);
                    upperNext = false;
                }
                else
                {
                    upperNext = true;
                }
            }

            if (sb.Length == 0) return fallback;
            if (char.IsDigit(sb[0])) sb.Insert(0, fallback);
            return sb.ToString();
        }

        static string RemoveDiacritics(string s)
        {
            var normalized = s.Replace('đ', 'd').Replace('Đ', 'D').Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(normalized.Length);
            foreach (var ch in normalized)
                if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                    sb.Append(ch);
            return sb.ToString().Normalize(NormalizationForm.FormC);
        }

        /// <summary>Escape để đặt trong chuỗi C# thường ("...").</summary>
        static string EscapeCs(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "").Replace("\n", "\\n")
                .Replace("\t", "\\t");
        }

        /// <summary>Escape để đặt trong XML doc comment một dòng.</summary>
        static string EscapeXml(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\r", "")
                .Replace("\n", " ");
        }

        static string JsonString(string s)
        {
            var sb = new StringBuilder(s.Length + 2);
            sb.Append('"');
            foreach (var ch in s)
                switch (ch)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (ch < ' ') sb.Append("\\u").Append(((int)ch).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(ch);
                        break;
                }

            sb.Append('"');
            return sb.ToString();
        }

        static string GetProjectRoot()
        {
            return Path.GetDirectoryName(Application.dataPath)!.Replace('\\', '/');
        }

        static string ToAbsolute(string assetPath)
        {
            return Path.Combine(GetProjectRoot(), assetPath).Replace('\\', '/');
        }

        static void WriteText(string absolutePath, string content)
        {
            File.WriteAllText(absolutePath, content, new UTF8Encoding(false));
        }

        #endregion

        #region Template

        const string SCENARIO_TEMPLATE = @"#if UNITY_EDITOR || EZG_AUTOTEST
using System.Threading.Tasks;
using UnityEngine;

// AI: ======================================================================================
// AI: KỊCH BẢN AUTO TEST — khung sinh bởi EZG Auto Test System. Agent triển khai theo các comment
// AI: ""// AI:"" rồi XOÁ chúng khi xong (giữ lại comment tiếng Việt giải thích nghiệp vụ).
// AI: ĐỌC TRƯỚC: {{GUIDE}}
// AI:   (package cài từ registry ⇒ file nằm ở Library/PackageCache/com.ezg.autotest@<version>/Documentation~/;
// AI:   hoặc gọi skill ""{{SKILL}}"" nếu project đã cài trong .claude/skills).
// AI: File nằm trong assembly AutoTests của project ⇒ gọi thẳng được code game (Service tĩnh,
// AI:   PlayerDataManager, DataManager, hàm Cheat_*…) — thêm using namespace của game nếu cần.
// AI: Luật chính:
// AI:   • Một kịch bản = MỘT luồng người chơi, chạy < 2 phút (TimeoutSeconds = 120).
// AI:   • Tên bước ctx.Step(...) = hành động người chơi bằng tiếng Việt ⇒ QA đọc như ""các bước tái hiện"".
// AI:   • Chờ bằng ctx.Ui.WaitFor / ctx.WaitUntil — KHÔNG ctx.WaitSeconds dài cố định.
// AI:   • Kiểm tra mềm: ctx.Check (ghi lỗi, chạy tiếp). Kiểm tra cứng: ctx.Assert / ctx.AreEqual (dừng case).
// AI:   • Mọi thứ SetUp/Run thay đổi (tiền, unlock, cờ tutorial, màn đang mở) phải trả lại trong TearDown.
// AI:   • Không DateTime.Now (dùng TimeManager của game); vòng lặp tự viết phải await (có ctx.Token).
// AI: ======================================================================================

namespace {{NAMESPACE}}
{
    // using đặt TRONG namespace để type của package thắng type global trùng tên của game (vd enum AutoTestFeature).
    using Ezg.AutoTest;

    /// <summary>{{SUMMARY}}</summary>
    [AutoTestScenario(""{{NAME}}"", Category = ""{{CATEGORY}}"", Description = ""{{DESCRIPTION}}"",
        TimeoutSeconds = 120)]
    public sealed class {{CLASS}} : AutoTestScenario
    {
        // AI: Lưu state gốc ở đây trong SetUp để TearDown khôi phục, vd:
        // AI:   double _goldBefore;

        public override async Task SetUp(AutoTestContext ctx)
        {
            // Chờ game boot xong + đóng popup đầu game (gọi nhiều lần vẫn an toàn).
            await GameFlow.EnsureReady(ctx);

            // AI: Chuẩn bị điều kiện cho luồng test (Arrange):
            // AI:   - Thêm tiền: Service/cheat của game, hoặc ctx.Game.AddCurrency(...) khi adapter có Economy.
            // AI:   - Mở khoá feature / nhảy level / bỏ tutorial bằng Service hoặc hàm Cheat_* của game.
            // AI:   - Thiếu điều kiện chạy (adapter thiếu capability, game chưa có tính năng…) ⇒ ctx.Skip(""lý do"").
        }

        public override async Task Run(AutoTestContext ctx)
        {
            // AI: XOÁ dòng Skip bên dưới khi đã triển khai xong kịch bản.
            ctx.Skip(""Kịch bản chưa được triển khai"");

            // AI: Mẫu một bước (Act + Assert). Đổi tên object/nút theo prefab THẬT của game (đọc prefab/controller).
            using (ctx.Step(""Bấm nút mở tính năng trên HUD""))
            {
                // Chờ nút xuất hiện và hiển thị (tối đa 5 giây) — không dùng sleep cố định.
                var openButton = await ctx.Ui.WaitFor(""btn_open_feature"", 5f);
                ctx.Assert(openButton != null, ""Không thấy nút mở tính năng trên HUD"", Severity.Critical);

                var clicked = await ctx.Ui.Click(openButton);
                ctx.Check(clicked, ""Nút mở tính năng không bấm được"", ""Nút bị che hoặc không interactable"");
            }

            using (ctx.Step(""Kiểm tra màn hình tính năng đã mở""))
            {
                var panel = await ctx.Ui.WaitFor(""screen_example"", 10f);
                ctx.Check(panel != null, ""Màn hình tính năng không mở sau khi bấm nút"",
                    severity: Severity.Major, expected: ""screen_example hiển thị"", actual: ""không thấy"");
                await ctx.Screenshot(""man_hinh_tinh_nang"");
            }

            // AI: Thêm các bước tiếp theo của luồng. Đo thời gian bằng ctx.Metric khi cần, vd:
            // AI:   ctx.Metric(""Thời gian mở màn"", openMs, ""ms"", max: 1500, severity: Severity.Minor);
        }

        public override async Task TearDown(AutoTestContext ctx)
        {
            // AI: Khôi phục mọi state đã đổi trong SetUp/Run (tiền, unlock, cờ tutorial…) — dùng giá trị đã lưu.

            // Đóng các màn đã mở để kịch bản sau bắt đầu sạch (chỉ khi game đã boot — tránh đóng nhầm HUD).
            if (GameFlow.IsReady(ctx.Session)) await GameFlow.ReturnToBaseline(ctx);
        }
    }
}
#endif
";

        const string HOOKS_TEMPLATE = @"#if UNITY_EDITOR || EZG_AUTOTEST
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

// AI: ======================================================================================
// AI: PROJECT HOOKS — điểm móc của project vào EZG Auto Test System. Runner tự tìm class này (chỉ được
// AI: có MỘT class hooks không abstract trong project). Override phần cần, xoá comment ""// AI:"" khi xong.
// AI: Hướng dẫn: {{GUIDE}} (mục ""Project hooks"").
// AI: Chỉ gọi hàm có sẵn của game (Service/cheat) — KHÔNG sửa PlayerPrefs trực tiếp, KHÔNG gọi
// AI:   UIManager.Instance trước khi game sẵn sàng (Instance tự tạo bản rỗng khi chưa khởi tạo).
// AI: ======================================================================================

namespace {{NAMESPACE}}
{
    // using đặt TRONG namespace để type của package thắng type global trùng tên của game (vd enum AutoTestFeature).
    using Ezg.AutoTest;

    /// <summary>
    ///     Điểm móc của {{PRODUCT}} vào EZG Auto Test System: bỏ tutorial, báo game sẵn sàng, data mẫu khi mở
    ///     màn, đóng popup chặn, loại feature khỏi smoke. Runner tự tìm class này — không cần đăng ký.
    /// </summary>
    public sealed class {{CLASS}} : AutoTestProjectHooks
    {
        /// <summary>Sau khi game boot xong, trước case đầu tiên của mỗi phiên Play.</summary>
        public override Task OnSessionStarted(AutoTestContext ctx)
        {
            // AI: Đưa game về trạng thái ""người chơi đã qua tutorial"" để smoke/button sweep/kịch bản không bị
            // AI:   bước hướng dẫn chặn. Ví dụ (đổi theo service thật của game):
            // AI:     MyTutorialService.DoneAll();
            // AI:     ctx.Log(""Đã bỏ qua toàn bộ tutorial"");
            // AI: Việc cần await (chờ scene/animation) ⇒ đổi thành async Task và dùng ctx.WaitUntil.
            return Task.CompletedTask;
        }

        /// <summary>null = để adapter tự quyết (template EZG: UIManager sẵn sàng + có màn nhóm Main).</summary>
        public override bool? IsGameReady()
        {
            // AI: Chỉ override khi adapter đoán sai (vd game còn màn loading riêng sau khi UIManager sẵn sàng):
            // AI:   return MyLoadingService.IsDone && !MySceneLoader.IsLoading;
            return null;
        }

        /// <summary>Data mẫu khi mở màn cần data (null = không truyền).</summary>
        public override object GetFeatureData(string featureName)
        {
            // AI: Màn nào mở mà thiếu data sẽ NullReference trong smoke ⇒ trả data mẫu hợp lệ ở đây:
            // AI:   switch (featureName)
            // AI:   {
            // AI:       case ""RewardPopup"": return new MyRewardPopupData(/* phần thưởng mẫu */);
            // AI:       case ""StationInfo"": return MyStationService.GetFirstUnlockedStation();
            // AI:   }
            // AI: Màn không thể có data mẫu hợp lệ ⇒ đưa vào ExtraExcludedFeatures thay vì trả data sai.
            return null;
        }

        /// <summary>Đóng popup chặn (rating, offer, daily…) giữa các case.</summary>
        public override Task DismissBlockingPopups(AutoTestContext ctx)
        {
            // AI: Popup tự bật giữa chừng (offer theo giờ, level up, rating…) mà settings
            // AI:   smoke.dismissAtStartFeatures chưa bắt được ⇒ đóng ở đây, vd:
            // AI:     MyPopupQueueService.ClearQueue();
            return Task.CompletedTask;
        }

        /// <summary>Tên feature (đúng tên enum) bỏ qua thêm trong smoke / UI audit / button sweep.</summary>
        public override IEnumerable<string> ExtraExcludedFeatures()
        {
            // AI: Liệt kê màn không mở độc lập được (bước tutorial, màn cần scene riêng, màn debug…), vd:
            // AI:   return new[] { ""StationInfo"", ""BattleResult"" };
            return Array.Empty<string>();
        }
    }
}
#endif
";

        const string ADAPTER_TEMPLATE = @"#if UNITY_EDITOR || EZG_AUTOTEST
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

// AI: ======================================================================================
// AI: GAME ADAPTER — CHỈ dùng cho project KHÔNG theo template EZG (không có UIManager.Show(GameEnums.Features),
// AI: PlayerResource/EnumBase.MoneyTypes, DataPlayer…). Project theo template EZG đã có EzgTemplateAdapter sẵn
// AI: trong package — XOÁ file này nếu lỡ tạo.
// AI: Hướng dẫn: {{GUIDE}} (mục ""Viết adapter cho project không theo template EZG"").
// AI: Cách làm: triển khai từng nhóm (vòng đời → màn hình → economy → dữ liệu → config), mỗi nhóm xong thì bật
// AI:   flag tương ứng trong Capabilities. Suite tự bỏ qua case cần capability chưa bật — không cần làm hết một lúc.
// AI: Adapter được tạo ở cả Edit mode (sandbox gọi GetSaveKeys) ⇒ Initialize/GetSaveKeys KHÔNG được chạm
// AI:   object runtime (singleton, scene); chỉ resolve type/tham chiếu tĩnh.
// AI: ======================================================================================

namespace {{NAMESPACE}}
{
    // using đặt TRONG namespace để type của package thắng type global trùng tên của game (vd enum AutoTestFeature).
    using Ezg.AutoTest;

    /// <summary>Adapter nối EZG Auto Test System với code của {{PRODUCT}}.</summary>
    public sealed class {{CLASS}} : GenericGameAdapter
    {
        public override string Name => ""{{PRODUCT}} Adapter"";

        /// <summary>Cao hơn EzgTemplateAdapter (50) và GenericGameAdapter (0) ⇒ được chọn trước khi Initialize trả true.</summary>
        public override int Priority => 200;

        // AI: TODO — bật dần khi đã triển khai, vd:
        // AI:   AdapterCapabilities.ReadyState | AdapterCapabilities.Features | AdapterCapabilities.Economy
        // AI:   | AdapterCapabilities.PlayerData | AdapterCapabilities.Config | AdapterCapabilities.Rewards
        // AI:   | AdapterCapabilities.PurchaseOffline
        public override AdapterCapabilities Capabilities => AdapterCapabilities.ReadyState;

        public override bool Initialize(AutoTestConfig config)
        {
            _config = config;
            _diagnostics.Clear();

            // AI: TODO — kiểm tra type cốt lõi của game có tồn tại (vd typeof(MyScreenManager)), cache danh sách
            // AI:   màn hình/tiền tệ, ghi _diagnostics.Add(""..."") mô tả tìm thấy gì / thiếu gì (hiện trong report).
            // AI: Trả false ⇒ runner bỏ qua adapter này và dùng adapter Priority thấp hơn. ĐỔI THÀNH true khi đã
            // AI:   triển khai ít nhất IsGameReady (và các phần đã bật trong Capabilities).
            _diagnostics.Add(""{{CLASS}}: chưa triển khai — Initialize trả false để runner dùng adapter khác."");
            return false;
        }

        #region Vòng đời

        /// <summary>Game đã boot xong, người chơi thao tác được chưa.</summary>
        public override bool IsGameReady()
        {
            // AI: TODO — true khi: đã rời scene boot/loading, UI chính đã hiện, không có màn loading/khoá input.
            // AI:   KHÔNG tự tạo singleton khi kiểm tra (dùng Object.FindFirstObjectByType thay vì X.Instance).
            // AI:   Base: rời scene boot + có EventSystem + scene đứng yên settleSecondsAfterBoot giây.
            return base.IsGameReady();
        }

        /// <summary>Mô tả trạng thái hiện tại — hiện trong thông báo khi boot quá thời gian.</summary>
        public override string DescribeState()
        {
            // AI: TODO — thêm thông tin giúp debug boot treo: scene, cờ loading, màn đang mở…
            return base.DescribeState();
        }

        #endregion

        #region Màn hình (capability Features)

        /// <summary>Mọi màn hình/feature của game.</summary>
        public override IReadOnlyList<AutoTestFeature> GetFeatures()
        {
            // AI: TODO — liệt kê từ enum/bảng màn hình của game, cache trong Initialize:
            // AI:   new AutoTestFeature { Name = ""Shop"", Value = (long)MyScreenId.Shop, Raw = MyScreenId.Shop }
            return base.GetFeatures();
        }

        /// <summary>Mở màn hình như game thật mở; trả GameObject gốc hoặc null nếu không mở được.</summary>
        public override Task<GameObject> ShowFeature(AutoTestFeature feature, object data, float timeoutSeconds,
            CancellationToken ct)
        {
            // AI: TODO — gọi UI manager của game (truyền data nếu khác null), chờ tới khi mở xong (tối đa
            // AI:   timeoutSeconds, tôn trọng ct), trả root của màn. Ví dụ (đổi thành async Task<GameObject>):
            // AI:     MyScreenManager.Open((MyScreenId)feature.Raw, data);
            // AI:     await AutoTestClock.Until(() => MyScreenManager.IsOpen((MyScreenId)feature.Raw), timeoutSeconds, ct);
            // AI:     return MyScreenManager.GetRoot((MyScreenId)feature.Raw);
            return base.ShowFeature(feature, data, timeoutSeconds, ct);
        }

        public override void CloseFeature(AutoTestFeature feature)
        {
            // AI: TODO — đóng màn qua UI manager của game (không SetActive(false) thủ công).
            base.CloseFeature(feature);
        }

        public override bool IsFeatureShowing(AutoTestFeature feature)
        {
            // AI: TODO — màn đang mở (đang hiển thị) không.
            return base.IsFeatureShowing(feature);
        }

        public override GameObject GetFeatureObject(AutoTestFeature feature)
        {
            // AI: TODO — GameObject gốc của màn đang mở, null nếu chưa mở.
            return base.GetFeatureObject(feature);
        }

        /// <summary>Các màn đang mở (thứ tự mở trước → sau). Sau boot, danh sách này là ""màn nền"" không bị đóng.</summary>
        public override IReadOnlyList<AutoTestFeature> GetOpenFeatures()
        {
            // AI: TODO — danh sách màn đang mở, dùng đúng object AutoTestFeature trả từ GetFeatures().
            return base.GetOpenFeatures();
        }

        #endregion

        #region Economy (capability Economy / Rewards / PurchaseOffline)

        public override IReadOnlyList<AutoTestCurrency> GetCurrencies()
        {
            // AI: TODO — tiền tệ THẬT (bỏ loại giả như Ads/Cash/IAP), cache trong Initialize:
            // AI:   new AutoTestCurrency { Name = ""Gold"", Value = (long)MyMoney.Gold, Raw = MyMoney.Gold }
            return base.GetCurrencies();
        }

        public override double GetBalance(AutoTestCurrency currency)
        {
            // AI: TODO — số dư hiện tại.
            return base.GetBalance(currency);
        }

        public override void AddCurrency(AutoTestCurrency currency, double amount)
        {
            // AI: TODO — cộng tiền qua API của game, KHÔNG chạy animation/popup.
            base.AddCurrency(currency, amount);
        }

        /// <summary>Trừ tiền; false khi game từ chối (không đủ tiền) — khi đó số dư KHÔNG được đổi.</summary>
        public override bool RemoveCurrency(AutoTestCurrency currency, double amount)
        {
            // AI: TODO — trừ qua API của game. API trả void ⇒ so số dư trước/sau để biết thành công.
            return base.RemoveCurrency(currency, amount);
        }

        public override bool IsEnough(AutoTestCurrency currency, double amount)
        {
            // AI: TODO — gọi đúng hàm kiểm tra đủ tiền mà UI của game dùng.
            return base.IsEnough(currency, amount);
        }

        public override void SetCurrency(AutoTestCurrency currency, double amount)
        {
            // AI: TODO — đặt số dư (dùng để khôi phục sau test). Game không có hàm Set ⇒ Add/Remove phần chênh.
            base.SetCurrency(currency, amount);
        }

        /// <summary>Kiểu số lưu số dư (int/long/double…) — suite economy dùng để test tràn số.</summary>
        public override Type BalanceType(AutoTestCurrency currency)
        {
            // AI: TODO — vd return typeof(long);
            return base.BalanceType(currency);
        }

        /// <summary>Phát thưởng qua hệ thống reward của game (không popup). false = không hỗ trợ.</summary>
        public override bool GrantReward(AutoTestCurrency currency, double amount)
        {
            // AI: TODO — gọi service phát thưởng của game (tắt popup/animation) rồi return true.
            return base.GrantReward(currency, amount);
        }

        /// <summary>Mua bằng tiền mềm. null = không hỗ trợ; true/false = giao dịch thành công hay không.</summary>
        public override Task<bool?> PurchaseOffline(AutoTestCurrency currency, double cost, float timeoutSeconds,
            CancellationToken ct)
        {
            // AI: TODO — gọi luồng mua bằng tiền mềm của game (tắt popup ""không đủ tiền"", không mở shop).
            return base.PurchaseOffline(currency, cost, timeoutSeconds, ct);
        }

        #endregion

        #region Dữ liệu (capability PlayerData)

        public override void SaveAll()
        {
            // AI: TODO — lưu toàn bộ dữ liệu người chơi xuống storage.
            base.SaveAll();
        }

        /// <summary>
        ///     Key PlayerPrefs chứa dữ liệu người chơi — sandbox sao lưu/khôi phục các key này quanh suite có sửa
        ///     dữ liệu. Gọi ở EDIT MODE ⇒ chỉ tính từ type/hằng số, không chạm object runtime.
        /// </summary>
        public override IReadOnlyList<string> GetSaveKeys()
        {
            // AI: TODO — QUAN TRỌNG cho an toàn dữ liệu: trả đủ mọi key save của game. Thiếu key ⇒ test có thể
            // AI:   ghi đè save thật của dev. Ví dụ:
            // AI:     var keys = new List<string>(base.GetSaveKeys()); // extraSaveKeys trong settings
            // AI:     keys.Add(MySaveSystem.SAVE_KEY);
            // AI:     return keys;
            return base.GetSaveKeys();
        }

        /// <summary>Đọc lại dữ liệu người chơi từ storage (test save/load). false = không hỗ trợ.</summary>
        public override bool ReloadPlayerData()
        {
            // AI: TODO — gọi hàm load lại của save system rồi return true.
            return base.ReloadPlayerData();
        }

        #endregion

        #region Config (capability Config)

        /// <summary>Các bảng config đã load (CSV → ScriptableObject…) — suite economy kiểm tra số âm/id trùng.</summary>
        public override IReadOnlyList<AutoTestConfigTable> GetConfigCollections()
        {
            // AI: TODO — vd new AutoTestConfigTable { Name = ""ShopPack"", Asset = so, ItemType = typeof(ShopPackRow), Items = so.rows }
            return base.GetConfigCollections();
        }

        #endregion
    }
}
#endif
";

        #endregion
    }
}
