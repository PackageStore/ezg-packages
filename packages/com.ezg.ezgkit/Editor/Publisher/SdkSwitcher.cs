#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Ezg.Editor.Shared.EzgKit;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.PackageManager;
using UnityEngine;

namespace Ezg.Editor.Shared.Publisher
{
    /// <summary>Kế hoạch chuyển bộ SDK — dựng trước, hiện cho người dùng, rồi mới thi hành.</summary>
    internal sealed class SwitchPlan
    {
        internal sealed class Step
        {
            internal SdkKind Kind;
            internal string Text;

            /// <summary>Nguồn cài cho SDK trong Assets/: các .unitypackage (cache / file người dùng kéo vào / vừa tải). Firebase nhiều file (mỗi product một).</summary>
            internal readonly List<string> PackageFiles = new();

            /// <summary>Chưa có file — switcher tải về cache trước khi Execute (<see cref="SdkDownloader" />).</summary>
            internal bool NeedsDownload;

            /// <summary>Spec UPM sẽ Add (git URL / version / file:).</summary>
            internal string UpmAdd;
        }

        internal readonly List<Step> Install = new();
        internal readonly List<Step> Remove = new();
        internal readonly List<Step> Keep = new();

        /// <summary>Không làm được: thiếu file cài, hoặc gỡ là vỡ compile. Kèm lý do + cách gỡ chặn.</summary>
        internal readonly List<Step> Blocked = new();

        /// <summary>Người dùng bỏ tick "Import" / "Gỡ" — làm được nhưng không làm theo lựa chọn.</summary>
        internal readonly List<Step> Skipped = new();

        internal readonly List<string> Ids = new();
        internal readonly List<string> Defines = new();
        internal string IdError;

        internal bool HasWork => Install.Count > 0 || Remove.Count > 0 || Ids.Count > 0 || Defines.Count > 0;

        internal bool IsBlocked(SdkKind kind)
        {
            foreach (var step in Blocked) if (step.Kind == kind) return true;
            return false;
        }

        internal List<SdkDownloader.Job> DownloadJobs()
        {
            var jobs = new List<SdkDownloader.Job>();
            foreach (var step in Install)
                if (step.NeedsDownload)
                {
                    var job = SdkDownloader.MakeJob(step.Kind);
                    if (job != null) jobs.Add(job);
                }

            return jobs;
        }

        /// <summary>Một dòng tóm tắt cho từng SDK — page in trên card của SDK đó.</summary>
        internal string ActionOf(SdkKind kind)
        {
            foreach (var step in Install) if (step.Kind == kind) return "Sẽ cài: " + step.Text;
            foreach (var step in Remove) if (step.Kind == kind) return "Sẽ gỡ: " + step.Text;
            foreach (var step in Blocked) if (step.Kind == kind) return "Chặn: " + step.Text;
            foreach (var step in Skipped) if (step.Kind == kind) return "Bỏ qua: " + step.Text;
            return null;
        }

        internal string Summary()
        {
            var sb = new StringBuilder();
            Section(sb, "CAI THEM", Install);
            Section(sb, "GO", Remove);
            Section(sb, "CHAN (khong lam)", Blocked);
            Section(sb, "BO QUA (khong tick)", Skipped);
            if (Ids.Count > 0) sb.Append("GHI ID:\n  - ").Append(string.Join("\n  - ", Ids)).Append('\n');
            if (Defines.Count > 0) sb.Append("DEFINE:\n  - ").Append(string.Join("\n  - ", Defines)).Append('\n');
            return sb.ToString();
        }

        private static void Section(StringBuilder sb, string title, List<Step> steps)
        {
            if (steps.Count == 0) return;
            sb.Append(title).Append(":\n");
            foreach (var step in steps) sb.Append("  - ").Append(SdkCatalog.NameOf(step.Kind)).Append(": ").Append(step.Text).Append('\n');
        }
    }

    /// <summary>
    ///     Nút "Chuyển sang {publisher}": biến project từ bộ SDK đang có sang bộ SDK publisher đòi — cài
    ///     cái thiếu, gỡ cái thừa, ghi ID, gắn define — trong một lần bấm, có preview trước.
    ///     <para>
    ///         <b>Ba luật an toàn:</b>
    ///         <list type="number">
    ///             <item>
    ///                 <b>Export trước khi gỡ.</b> SDK trong Assets/ (Firebase 367MB, FacebookSDK, MaxSdk…) được
    ///                 <see cref="AssetDatabase.ExportPackage" /> vào cache theo máy
    ///                 (<see cref="CacheDir" />) rồi mới xoá; spec UPM gỡ đi được ghi vào <c>upm.json</c> cùng
    ///                 chỗ. Bấm về Ezg là cài lại từ đúng bản vừa gỡ — không phải tải lại từ internet, không
    ///                 lệch version. Không có cache thì <see cref="SdkDownloader" /> tự tải (GitHub Releases /
    ///                 Google) về cache rồi import; UPM thì spec mặc định trong <see cref="SdkCatalog.SpecOf" />.
    ///                 Chỉ SDK không có nguồn tải (Google Play plugins) mới cần kéo file tay.
    ///             </item>
    ///             <item>
    ///                 <b>Chặn gỡ SDK mà code game còn gọi thẳng.</b> Gỡ Firebase khi GameInitialize còn
    ///                 <c>Firebase.Analytics…</c> là vỡ compile, Editor không mở được tool nữa. Switcher quét
    ///                 <see cref="SdkCatalog.CodeReferences" />, còn khớp thì để lại SDK, báo file. Muốn switch
    ///                 sạch: bọc lời gọi trong <c>#if EZG_SDK_*</c> (define do switcher gắn theo bộ SDK).
    ///             </item>
    ///             <item>
    ///                 <b>Không bao giờ chạy trong lượt vẽ.</b> Gọi qua <c>ReadinessActions.Defer</c>; xoá
    ///                 asset / import package / <see cref="Client.AddAndRemove" /> đều kéo theo reimport +
    ///                 domain reload — chạy giữa OnGUI là vỡ cửa sổ.
    ///             </item>
    ///         </list>
    ///     </para>
    /// </summary>
    internal static class SdkSwitcher
    {
        #region Cache

        private const string UPM_RECORD = "upm.json";

        /// <summary>Cache theo máy + theo game (SDK folder của hai game có thể khác config bên trong).</summary>
        internal static string CacheDir
        {
            get
            {
                var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var game = Regex.Replace(PlayerSettings.productName ?? "game", "[^A-Za-z0-9_-]+", "_");
                return Path.Combine(root, "Ezg", "SdkCache", game);
            }
        }

        /// <summary>Đường dẫn cache của một SDK: <c>{kind}.unitypackage</c>, hoặc <c>{kind}.{product}.unitypackage</c> (Firebase).</summary>
        internal static string CachePackagePath(SdkKind kind, string product = null) =>
            Path.Combine(CacheDir, product == null ? kind + ".unitypackage" : $"{kind}.{product}.unitypackage");

        /// <summary>
        ///     File cache của SDK. Bản export nguyên khối (<c>{kind}.unitypackage</c>, do lần gỡ trước ghi) ưu
        ///     tiên; không có thì các file theo product (Firebase tải từ Google). Rỗng = chưa có gì.
        /// </summary>
        internal static List<string> CachedPackages(SdkKind kind)
        {
            var files = new List<string>();
            var whole = CachePackagePath(kind);
            if (File.Exists(whole))
            {
                files.Add(whole);
                return files;
            }

            if (!Directory.Exists(CacheDir)) return files;
            foreach (var file in Directory.EnumerateFiles(CacheDir, kind + ".*.unitypackage")) files.Add(file);
            files.Sort(StringComparer.Ordinal);
            return files;
        }

        [Serializable]
        private sealed class UpmRecord
        {
            public List<string> names = new();
            public List<string> specs = new();

            internal string Get(string name)
            {
                var index = names.IndexOf(name);
                return index < 0 ? null : specs[index];
            }

            internal void Set(string name, string spec)
            {
                var index = names.IndexOf(name);
                if (index < 0)
                {
                    names.Add(name);
                    specs.Add(spec);
                }
                else specs[index] = spec;
            }
        }

        private static UpmRecord LoadUpmRecord()
        {
            var path = Path.Combine(CacheDir, UPM_RECORD);
            if (!File.Exists(path)) return new UpmRecord();
            try
            {
                return JsonUtility.FromJson<UpmRecord>(File.ReadAllText(path)) ?? new UpmRecord();
            }
            catch (Exception)
            {
                return new UpmRecord();
            }
        }

        private static void SaveUpmRecord(UpmRecord record)
        {
            Directory.CreateDirectory(CacheDir);
            File.WriteAllText(Path.Combine(CacheDir, UPM_RECORD), JsonUtility.ToJson(record, true), new UTF8Encoding(false));
        }

        #endregion

        #region Plan

        /// <summary>
        ///     Dựng kế hoạch. Chỉ ĐỌC. <paramref name="manualPackages" /> = file .unitypackage người dùng
        ///     kéo vào cho SDK chưa có nguồn (ưu tiên hơn cache).
        /// </summary>
        internal static SwitchPlan BuildPlan(IPublisherProfile profile, List<SdkReport> reports,
            Dictionary<SdkKind, string> manualPackages, HashSet<SdkKind> excluded = null)
        {
            var plan = new SwitchPlan();
            excluded ??= new HashSet<SdkKind>();
            if (profile.RequiredSdks.Length == 0) return plan; // chưa có tài liệu → không cài/gỡ gì

            var record = LoadUpmRecord();
            var installedAfter = new HashSet<SdkKind>();

            // Quét tham chiếu code MỘT lượt cho mọi SDK thừa (đọc 1600 file .cs một lần, không phải mỗi SDK một lần).
            var extraKinds = new List<SdkKind>();
            foreach (var report in reports)
                if (!report.Required && report.Installed) extraKinds.Add(report.Kind);
            var references = SdkCatalog.CodeReferences(extraKinds, 3);

            foreach (var report in reports)
            {
                var spec = SdkCatalog.SpecOf(report.Kind);

                if (report.Required && report.Installed)
                {
                    installedAfter.Add(report.Kind);
                    plan.Keep.Add(new SwitchPlan.Step { Kind = report.Kind, Text = "giữ" });
                    continue;
                }

                if (report.Required)
                {
                    if (excluded.Contains(report.Kind))
                    {
                        plan.Skipped.Add(new SwitchPlan.Step { Kind = report.Kind, Text = "không import (bỏ tick) — publisher vẫn đòi SDK này" });
                        continue;
                    }

                    // Cần cài: UPM lấy spec đã nhớ → mặc định; Assets lấy file người dùng → cache → chặn.
                    var step = new SwitchPlan.Step { Kind = report.Kind };
                    var parts = new List<string>();
                    if (spec.UpmName != null)
                    {
                        step.UpmAdd = record.Get(spec.UpmName) ?? spec.UpmDefaultSpec;
                        parts.Add($"UPM {spec.UpmName} ({step.UpmAdd})");
                    }

                    if (spec.HasAssets)
                    {
                        // Nguồn theo thứ tự: file người dùng kéo vào → cache → tự tải (GitHub Releases / Google) → chặn.
                        if (manualPackages != null && manualPackages.TryGetValue(report.Kind, out var manual)
                            && !string.IsNullOrEmpty(manual) && File.Exists(manual))
                        {
                            step.PackageFiles.Add(manual);
                            parts.Add("import " + Path.GetFileName(manual) + " (file kéo vào)");
                        }
                        else
                        {
                            var cached = CachedPackages(report.Kind);
                            if (cached.Count > 0)
                            {
                                step.PackageFiles.AddRange(cached);
                                parts.Add(cached.Count == 1
                                    ? "import " + Path.GetFileName(cached[0]) + " (cache)"
                                    : $"import {cached.Count} package từ cache");
                            }
                            else if (SdkDownloader.CanDownload(report.Kind))
                            {
                                step.NeedsDownload = true;
                                var download = spec.Download;
                                parts.Add(download.IsFirebase
                                    ? $"tải Firebase Unity SDK từ Google ({SdkCatalog.FirebaseInstalled(out var ver).Length} product, {ver ?? "bản mới nhất"}) rồi import"
                                    : $"tải từ GitHub Releases {download.GitHubRepo} rồi import");
                            }
                            else
                            {
                                plan.Blocked.Add(new SwitchPlan.Step
                                {
                                    Kind = report.Kind,
                                    Text = "SDK này không có nguồn tải tự động — tải .unitypackage từ trang release rồi kéo vào ô trên card"
                                           + (spec.ReleasePageUrl != null ? $" ({spec.ReleasePageUrl})" : "") + ".",
                                });
                                continue;
                            }
                        }
                    }

                    step.Text = string.Join(" + ", parts);
                    plan.Install.Add(step);
                    installedAfter.Add(report.Kind);
                    continue;
                }

                // Thừa: gỡ nếu code game không còn gọi thẳng — và người dùng còn tick "Gỡ".
                var (total, refs) = references.TryGetValue(report.Kind, out var found) ? found : (0, new List<string>());
                if (total == 0 && excluded.Contains(report.Kind))
                {
                    installedAfter.Add(report.Kind);
                    plan.Skipped.Add(new SwitchPlan.Step { Kind = report.Kind, Text = "giữ lại (bỏ tick gỡ)" });
                    continue;
                }

                if (total > 0)
                {
                    installedAfter.Add(report.Kind);
                    plan.Blocked.Add(new SwitchPlan.Step
                    {
                        Kind = report.Kind,
                        Text = $"code game còn gọi thẳng SDK ({total} file: {string.Join(", ", refs)}) — gỡ là vỡ compile. "
                               + $"Bọc trong #if {spec.Define} rồi chuyển lại.",
                    });
                    continue;
                }

                var what = new List<string>();
                if (spec.HasAssets) what.Add("export → cache rồi xoá " + string.Join(", ", spec.AssetFolders));
                if (spec.UpmName != null) what.Add("UPM remove " + spec.UpmName + (spec.UpmAlso.Length > 0 ? " + " + string.Join(", ", spec.UpmAlso) : ""));
                plan.Remove.Add(new SwitchPlan.Step { Kind = report.Kind, Text = string.Join("; ", what) });
            }

            // ID publisher cấp mà catalog ghi được.
            if (PublisherSdkApplier.Apply(profile, reports, true, out var idChanges, out var idError))
                foreach (var change in idChanges)
                    if (!change.Contains("giu nguyen")) plan.Ids.Add(change);
            else plan.IdError = idError;

            // Define theo bộ SDK sau khi chuyển: có SDK → có define; không → gỡ define.
            var current = PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.Android).Split(';', StringSplitOptions.RemoveEmptyEntries);
            var currentSet = new HashSet<string>(current);
            foreach (SdkKind kind in Enum.GetValues(typeof(SdkKind)))
            {
                var define = SdkCatalog.SpecOf(kind).Define;
                if (define == null) continue;
                var want = installedAfter.Contains(kind);
                if (want && !currentSet.Contains(define)) plan.Defines.Add("+ " + define);
                else if (!want && currentSet.Contains(define)) plan.Defines.Add("− " + define);
            }

            return plan;
        }

        #endregion

        #region Execute

        /// <summary>
        ///     Thi hành kế hoạch. Thứ tự: export cache → xoá asset → import package → define → ID → state →
        ///     UPM add/remove (cuối cùng vì nó kích resolve + domain reload). Mỗi bước ghi log; lỗi ở bước
        ///     nào thì dừng ở bước đó và nói rõ đã làm tới đâu.
        /// </summary>
        internal static bool Execute(IPublisherProfile profile, List<SdkReport> reports, SwitchPlan plan,
            out List<string> log, out string error)
        {
            log = new List<string>();
            error = null;
            var record = LoadUpmRecord();
            var upmAdd = new List<string>();
            var upmRemove = new List<string>();

            try
            {
                // 1. Export + xoá SDK thừa trong Assets/.
                var toDelete = new List<string>();
                foreach (var step in plan.Remove)
                {
                    var spec = SdkCatalog.SpecOf(step.Kind);
                    if (spec.HasAssets)
                    {
                        var existing = new List<string>();
                        foreach (var folder in spec.AssetFolders)
                            if (AssetDatabase.IsValidFolder(folder) || File.Exists(Path.Combine(ProjectRoot(), folder)))
                                existing.Add(folder);

                        if (existing.Count > 0)
                        {
                            Directory.CreateDirectory(CacheDir);
                            var file = CachePackagePath(step.Kind);
                            EditorUtility.DisplayProgressBar("EzgKit - Chuyen SDK", $"Export {SdkCatalog.NameOf(step.Kind)} vao cache…", 0.2f);
                            AssetDatabase.ExportPackage(existing.ToArray(), file, ExportPackageOptions.Recurse);
                            log.Add($"cache: {SdkCatalog.NameOf(step.Kind)} → {file}");
                            toDelete.AddRange(existing);
                        }
                    }

                    if (spec.UpmName != null)
                    {
                        var current = SdkCatalog.UpmSpec(spec.UpmName);
                        if (current != null) record.Set(spec.UpmName, current);
                        upmRemove.Add(spec.UpmName);
                        foreach (var also in spec.UpmAlso)
                        {
                            var alsoSpec = SdkCatalog.UpmSpec(also);
                            if (alsoSpec == null) continue;
                            record.Set(also, alsoSpec);
                            upmRemove.Add(also);
                        }
                    }
                }

                if (toDelete.Count > 0)
                {
                    EditorUtility.DisplayProgressBar("EzgKit - Chuyen SDK", "Xoa thu muc SDK thua…", 0.4f);
                    var failed = new List<string>();
                    AssetDatabase.DeleteAssets(toDelete.ToArray(), failed);
                    foreach (var path in toDelete)
                        log.Add((failed.Contains(path) ? "KHONG xoa duoc: " : "xoa: ") + path);
                }

                SaveUpmRecord(record);

                // 2. Cài SDK thiếu.
                foreach (var step in plan.Install)
                {
                    var spec = SdkCatalog.SpecOf(step.Kind);
                    // Vừa tải xong thì file nằm trong cache — lấy lại từ đó.
                    var files = step.NeedsDownload ? CachedPackages(step.Kind) : step.PackageFiles;
                    if (spec.HasAssets && files.Count == 0)
                    {
                        error = $"{SdkCatalog.NameOf(step.Kind)}: khong co .unitypackage de import (tai chua xong?).";
                        return false;
                    }

                    foreach (var file in files)
                    {
                        EditorUtility.DisplayProgressBar("EzgKit - Chuyen SDK", $"Import {Path.GetFileName(file)}…", 0.6f);
                        AssetDatabase.ImportPackage(file, false);
                        log.Add($"import: {Path.GetFileName(file)}");
                    }

                    if (spec.UpmName != null && step.UpmAdd != null)
                    {
                        upmAdd.Add(UpmAddArgument(spec.UpmName, step.UpmAdd));
                        foreach (var also in spec.UpmAlso)
                        {
                            var alsoSpec = record.Get(also);
                            if (alsoSpec != null) upmAdd.Add(UpmAddArgument(also, alsoSpec));
                        }
                    }
                }

                // 3. Define theo bộ SDK mới.
                ApplyDefines(plan, log);

                // 4. ID publisher cấp + state.
                if (plan.Ids.Count > 0 || plan.IdError == null)
                {
                    if (PublisherSdkApplier.Apply(profile, reports, false, out var idChanges, out var idError))
                        log.AddRange(idChanges);
                    else if (idError != null) log.Add("ID: " + idError);
                }

                var state = PublisherState.Load();
                state.activePublisher = profile.Id;
                state.appliedAtUtc = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm 'UTC'");
                state.Save();

                // 5. UPM cuối cùng — resolve + reload.
                if (upmAdd.Count > 0 || upmRemove.Count > 0)
                {
                    EditorUtility.DisplayProgressBar("EzgKit - Chuyen SDK", "Package Manager add/remove…", 0.9f);
                    Client.AddAndRemove(upmAdd.ToArray(), upmRemove.ToArray());
                    log.Add($"UPM: add [{string.Join(", ", upmAdd)}] remove [{string.Join(", ", upmRemove)}] — Package Manager đang resolve.");
                }

                AssetDatabase.Refresh();
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                error = exception.Message;
                return false;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        /// <summary>Đối số cho <see cref="Client.AddAndRemove" />: git URL / file: dùng thẳng, version thì <c>name@version</c>.</summary>
        private static string UpmAddArgument(string name, string spec)
        {
            if (spec.StartsWith("http") || spec.StartsWith("git") || spec.StartsWith("file:") || spec.StartsWith("ssh"))
                return spec;
            return name + "@" + spec;
        }

        private static void ApplyDefines(SwitchPlan plan, List<string> log)
        {
            if (plan.Defines.Count == 0) return;
            foreach (var target in new[] { NamedBuildTarget.Android, NamedBuildTarget.iOS })
            {
                var set = new List<string>(PlayerSettings.GetScriptingDefineSymbols(target).Split(';', StringSplitOptions.RemoveEmptyEntries));
                foreach (var change in plan.Defines)
                {
                    var define = change.Substring(2);
                    if (change.StartsWith("+")) { if (!set.Contains(define)) set.Add(define); }
                    else set.Remove(define);
                }

                PlayerSettings.SetScriptingDefineSymbols(target, string.Join(";", set));
            }

            log.Add("define: " + string.Join(", ", plan.Defines));
        }

        private static string ProjectRoot() => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

        #endregion
    }
}
#endif
