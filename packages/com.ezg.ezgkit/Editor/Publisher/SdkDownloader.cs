#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace Ezg.Editor.Shared.Publisher
{
    /// <summary>
    ///     Tự tải .unitypackage cho SDK chưa có trong cache — để "Chuyển sang X" là một cú bấm: có rồi
    ///     thì thôi, chưa có thì tải về rồi import. Chạy BẤT ĐỒNG BỘ qua <see cref="EditorApplication.update" />
    ///     (Firebase là zip ~1 GB — block Editor vài phút là không được), tuần tự từng job, progress bar
    ///     theo <see cref="UnityWebRequest.downloadProgress" />; xong hết thì gọi <c>onDone</c> để switcher
    ///     import.
    ///     <para>
    ///         Nguồn: GitHub Releases API (<c>releases/latest</c>, unauthenticated — 60 lượt/giờ, đủ) chọn
    ///         asset theo regex; asset zip thì giải nén entry .unitypackage bằng <see cref="ZipFile" />.
    ///         Firebase: zip theo version đang cài (hoặc bản mới nhất qua redirect), lấy đúng bộ product
    ///         (<see cref="SdkCatalog.FirebaseInstalled" />) — mỗi product một .unitypackage trong cache.
    ///     </para>
    ///     <para>
    ///         File tải về nằm chung chỗ với cache export (<see cref="SdkSwitcher.CacheDir" />), cùng
    ///         quy ước tên — nên lần sau không tải lại, và "về Ezg" dùng lại được. Tải dở → file tạm
    ///         <c>.part</c>, không bao giờ để lại .unitypackage hỏng trong cache.
    ///     </para>
    /// </summary>
    internal static class SdkDownloader
    {
        #region Types

        internal sealed class Job
        {
            internal SdkKind Kind;
            internal SdkCatalog.DownloadSource Source;

            /// <summary>Firebase: bộ product cần; version null = bản mới nhất.</summary>
            internal string[] FirebaseProducts;
            internal string FirebaseVersion;

            /// <summary>Kết quả: các .unitypackage đã nằm trong cache.</summary>
            internal readonly List<string> Files = new();
        }

        private enum Phase
        {
            Idle,
            Resolving,
            Downloading,
        }

        #endregion

        #region State

        private static readonly Queue<Job> _queue = new();
        private static Job _current;
        private static Phase _phase = Phase.Idle;
        private static UnityWebRequest _request;
        private static string _tempFile;
        private static Action<bool, string> _onDone;
        private static readonly List<string> _log = new();
        private static int _total;

        /// <summary>Dòng trạng thái cho page vẽ mỗi frame (rẻ — chỉ đọc field).</summary>
        internal static string Status { get; private set; }

        internal static bool IsBusy => _phase != Phase.Idle;

        #endregion

        #region Entry

        /// <summary>
        ///     Xếp hàng tải. <paramref name="onDone" />(ok, message) gọi đúng một lần khi xong hết hoặc lỗi
        ///     ở job nào đó (dừng luôn — import nửa bộ SDK còn tệ hơn không import).
        /// </summary>
        internal static void Start(List<Job> jobs, Action<bool, string> onDone)
        {
            if (IsBusy)
            {
                onDone?.Invoke(false, "Dang co luot tai khac chay.");
                return;
            }

            _queue.Clear();
            _log.Clear();
            foreach (var job in jobs) _queue.Enqueue(job);
            _total = _queue.Count;
            _onDone = onDone;
            if (_queue.Count == 0)
            {
                Finish(true, "Khong co gi de tai.");
                return;
            }

            EditorApplication.update += Tick;
            Next();
        }

        /// <summary>Có nguồn tải cho SDK này không (để plan quyết định "tải" hay "chặn").</summary>
        internal static bool CanDownload(SdkKind kind) => SdkCatalog.SpecOf(kind).Download != null;

        internal static Job MakeJob(SdkKind kind)
        {
            var source = SdkCatalog.SpecOf(kind).Download;
            if (source == null) return null;
            var job = new Job { Kind = kind, Source = source };
            if (source.IsFirebase) job.FirebaseProducts = SdkCatalog.FirebaseInstalled(out job.FirebaseVersion);
            return job;
        }

        #endregion

        #region Pipeline

        private static void Next()
        {
            if (_queue.Count == 0)
            {
                Finish(true, $"Da tai {_total} SDK: {string.Join(" · ", _log)}");
                return;
            }

            _current = _queue.Dequeue();
            var name = SdkCatalog.NameOf(_current.Kind);
            Directory.CreateDirectory(SdkSwitcher.CacheDir);

            if (_current.Source.IsFirebase)
            {
                var url = SdkCatalog.FirebaseZipUrl(_current.FirebaseVersion);
                Status = $"{name}: tải {(_current.FirebaseVersion ?? "bản mới nhất")} ({_current.FirebaseProducts.Length} product)…";
                BeginDownload(url, "firebase.zip");
                return;
            }

            _phase = Phase.Resolving;
            Status = $"{name}: hỏi GitHub Releases ({_current.Source.GitHubRepo})…";
            _request = UnityWebRequest.Get($"https://api.github.com/repos/{_current.Source.GitHubRepo}/releases/latest");
            _request.SetRequestHeader("User-Agent", "EzgKit");
            _request.SetRequestHeader("Accept", "application/vnd.github+json");
            _request.timeout = 30;
            _request.SendWebRequest();
        }

        private static void BeginDownload(string url, string tempName)
        {
            _phase = Phase.Downloading;
            _tempFile = Path.Combine(SdkSwitcher.CacheDir, tempName + ".part");
            if (File.Exists(_tempFile)) File.Delete(_tempFile);
            _request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbGET)
            {
                downloadHandler = new DownloadHandlerFile(_tempFile) { removeFileOnAbort = true },
                timeout = 0, // zip Firebase ~1 GB
            };
            _request.SetRequestHeader("User-Agent", "EzgKit");
            _request.SendWebRequest();
        }

        private static void Tick()
        {
            if (_request == null)
            {
                Cancel("Request bi mat giua chung.");
                return;
            }

            var name = SdkCatalog.NameOf(_current.Kind);
            var progress = _phase == Phase.Downloading ? _request.downloadProgress : 0f;
            var cancelled = EditorUtility.DisplayCancelableProgressBar("EzgKit - Tai SDK",
                $"[{_total - _queue.Count}/{_total}] {Status} {(progress > 0 ? $"{progress * 100:0}% · {_request.downloadedBytes / 1048576f:0.0} MB" : "")}",
                progress);
            if (cancelled)
            {
                Cancel("Nguoi dung huy.");
                return;
            }

            if (!_request.isDone) return;

            if (_request.result != UnityWebRequest.Result.Success)
            {
                Cancel($"{name}: {_request.error} ({_request.url})");
                return;
            }

            try
            {
                if (_phase == Phase.Resolving) OnResolved();
                else OnDownloaded();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                Cancel($"{name}: {exception.Message}");
            }
        }

        private static void OnResolved()
        {
            var release = JsonUtility.FromJson<GitHubRelease>(_request.downloadHandler.text);
            _request.Dispose();
            _request = null;

            if (release?.assets == null || release.assets.Length == 0)
                throw new InvalidOperationException($"release {release?.tag_name ?? "?"} không có asset — tải tay từ trang release.");

            var regex = new Regex(_current.Source.AssetPattern);
            GitHubAsset picked = null;
            foreach (var asset in release.assets)
                if (regex.IsMatch(asset.name))
                {
                    picked = asset;
                    break;
                }

            if (picked == null)
                throw new InvalidOperationException($"release {release.tag_name} không có asset khớp {_current.Source.AssetPattern}.");

            Status = $"{SdkCatalog.NameOf(_current.Kind)}: tải {picked.name} ({picked.size / 1048576f:0.0} MB, {release.tag_name})…";
            BeginDownload(picked.browser_download_url, picked.name);
        }

        private static void OnDownloaded()
        {
            _request.Dispose();
            _request = null;

            var name = SdkCatalog.NameOf(_current.Kind);
            if (_current.Source.IsFirebase)
            {
                ExtractFirebase(_tempFile, _current.FirebaseProducts, _current.Files);
                File.Delete(_tempFile);
                _log.Add($"{name} ({_current.Files.Count} product)");
            }
            else if (_current.Source.ZipEntryPattern != null)
            {
                var target = SdkSwitcher.CachePackagePath(_current.Kind);
                ExtractFirst(_tempFile, _current.Source.ZipEntryPattern, target);
                File.Delete(_tempFile);
                _current.Files.Add(target);
                _log.Add(name);
            }
            else
            {
                var target = SdkSwitcher.CachePackagePath(_current.Kind);
                if (File.Exists(target)) File.Delete(target);
                File.Move(_tempFile, target);
                _current.Files.Add(target);
                _log.Add(name);
            }

            _tempFile = null;
            _phase = Phase.Idle;
            Next();
        }

        /// <summary>Entry đầu tiên khớp regex → ghi ra <paramref name="target" />.</summary>
        private static void ExtractFirst(string zipPath, string entryPattern, string target)
        {
            var regex = new Regex(entryPattern);
            using var zip = ZipFile.OpenRead(zipPath);
            foreach (var entry in zip.Entries)
            {
                if (!regex.IsMatch(entry.FullName)) continue;
                if (File.Exists(target)) File.Delete(target);
                entry.ExtractToFile(target);
                return;
            }

            throw new InvalidOperationException($"zip không có entry khớp {entryPattern}.");
        }

        /// <summary>Zip Firebase: <c>firebase_unity_sdk/{Product}.unitypackage</c> cho từng product cần.</summary>
        private static void ExtractFirebase(string zipPath, string[] products, List<string> files)
        {
            var wanted = new HashSet<string>(products, StringComparer.OrdinalIgnoreCase);
            using var zip = ZipFile.OpenRead(zipPath);
            foreach (var entry in zip.Entries)
            {
                if (!entry.FullName.EndsWith(".unitypackage", StringComparison.OrdinalIgnoreCase)) continue;
                var product = Path.GetFileNameWithoutExtension(entry.Name);
                if (!wanted.Contains(product)) continue;

                var target = SdkSwitcher.CachePackagePath(SdkKind.Firebase, product);
                if (File.Exists(target)) File.Delete(target);
                entry.ExtractToFile(target);
                files.Add(target);
                wanted.Remove(product);
            }

            if (wanted.Count > 0)
                throw new InvalidOperationException($"zip Firebase thiếu product: {string.Join(", ", wanted)}.");
        }

        private static void Cancel(string message)
        {
            if (_request != null)
            {
                _request.Abort();
                _request.Dispose();
                _request = null;
            }

            if (_tempFile != null && File.Exists(_tempFile))
                try
                {
                    File.Delete(_tempFile);
                }
                catch (Exception)
                {
                    // file tạm — bỏ qua
                }

            Finish(false, message);
        }

        private static void Finish(bool ok, string message)
        {
            EditorApplication.update -= Tick;
            EditorUtility.ClearProgressBar();
            _phase = Phase.Idle;
            _current = null;
            _tempFile = null;
            _queue.Clear();
            Status = ok ? null : "Tải lỗi: " + message;
            var callback = _onDone;
            _onDone = null;
            callback?.Invoke(ok, message);
        }

        #endregion

        #region GitHub JSON

        [Serializable]
        private sealed class GitHubRelease
        {
            public string tag_name;
            public GitHubAsset[] assets;
        }

        [Serializable]
        private sealed class GitHubAsset
        {
            public string name;
            public long size;
            public string browser_download_url;
        }

        #endregion
    }
}
#endif
