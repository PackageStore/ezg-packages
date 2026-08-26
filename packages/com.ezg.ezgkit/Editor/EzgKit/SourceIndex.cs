#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Ezg.Editor.Shared.EzgKit
{
    /// <summary>
    ///     Cache text của mọi file <c>.cs</c> dưới <c>Assets/</c>, dùng chung cho mọi bộ kiểm của kit.
    ///     <para>
    ///         Vì sao: Readiness (đếm RestorePurchases), Social (link hardcode, webhook, bot token) và
    ///         Publisher (tham chiếu SDK, custom event) mỗi bộ tự <c>EnumerateFiles</c> + <c>ReadAllText</c>
    ///         1600 file — mỗi lần đổi tab 0,3–0,8 s, về Tổng quan (reload mọi page) là 2–3 s. Đọc một lần,
    ///         giữ trong RAM (~8 MB text game + SDK), mọi bộ kiểm quét trên RAM: còn vài chục ms.
    ///     </para>
    ///     <para>
    ///         <b>Vô hiệu tự động</b> khi có <c>.cs</c> được import / xoá / đổi tên
    ///         (<see cref="Watcher" /> — AssetPostprocessor chạy trên domain cũ ngay lúc import, nên kể cả
    ///         khi file mới làm compile đỏ, lần Reload kế tiếp vẫn thấy bản mới). Domain reload thì static
    ///         về null, đọc lại lần đầu dùng.
    ///     </para>
    ///     <para>
    ///         <see cref="Get{T}" /> cache thêm KẾT QUẢ dẫn xuất theo khoá (ví dụ "sdk.refs") — bộ kiểm chạy
    ///         regex một lần cho tới khi source đổi, không phải mỗi Reload.
    ///     </para>
    /// </summary>
    internal static class SourceIndex
    {
        #region Types

        internal sealed class SourceFile
        {
            /// <summary>Đường dẫn tuyệt đối (cho OpenScript / File.Exists).</summary>
            internal string Absolute;

            /// <summary>Đường dẫn tương đối gốc project, dấu <c>/</c> (cho hiển thị).</summary>
            internal string Relative;

            internal string Text;

            /// <summary>Nằm trong thư mục SDK bên thứ ba (<see cref="ThirdPartyDirs" />).</summary>
            internal bool IsThirdParty;

            /// <summary>Nằm trong một thư mục <c>Editor/</c>.</summary>
            internal bool IsEditor;
        }

        #endregion

        #region Fields

        /// <summary>Thư mục SDK bên thứ ba — code game không nằm đây; sample code của SDK hay bắt nhầm regex.</summary>
        internal static readonly string[] ThirdPartyDirs =
        {
            "/FacebookSDK/", "/MaxSdk/", "/Firebase/", "/GooglePlayPlugins/", "/ExternalDependencyManager/",
            "/GameAnalytics/", "/AppsFlyer/", "/Plugins/", "/Samples/",
        };

        private static List<SourceFile> _files;
        private static readonly Dictionary<string, object> _derived = new();

        /// <summary>Tăng mỗi lần vô hiệu — để ai giữ snapshot biết mình đã cũ.</summary>
        internal static int Version { get; private set; }

        #endregion

        #region API

        internal static IReadOnlyList<SourceFile> Files
        {
            get
            {
                if (_files == null) Build();
                return _files;
            }
        }

        /// <summary>Kết quả dẫn xuất từ source, cache theo <paramref name="key" /> tới lần source đổi.</summary>
        internal static T Get<T>(string key, Func<IReadOnlyList<SourceFile>, T> build)
        {
            if (_derived.TryGetValue(key, out var cached) && cached is T typed) return typed;
            var value = build(Files);
            _derived[key] = value;
            return value;
        }

        internal static void Invalidate()
        {
            _files = null;
            _derived.Clear();
            Version++;
        }

        internal static bool IsThirdPartyPath(string normalizedPath)
        {
            foreach (var dir in ThirdPartyDirs)
                if (normalizedPath.Contains(dir)) return true;
            return false;
        }

        #endregion

        #region Build

        private static void Build()
        {
            var files = new List<SourceFile>(1024);
            var root = Path.GetFullPath(Path.Combine(Application.dataPath, "..")).Replace('\\', '/');

            foreach (var file in Directory.EnumerateFiles(Application.dataPath, "*.cs", SearchOption.AllDirectories))
            {
                string text;
                try
                {
                    text = File.ReadAllText(file);
                }
                catch (Exception)
                {
                    continue;
                }

                var normalized = file.Replace('\\', '/');
                files.Add(new SourceFile
                {
                    Absolute = file,
                    Relative = normalized.StartsWith(root) ? normalized.Substring(root.Length).TrimStart('/') : normalized,
                    Text = text,
                    IsThirdParty = IsThirdPartyPath(normalized),
                    IsEditor = normalized.Contains("/Editor/"),
                });
            }

            _files = files;
        }

        #endregion

        #region Watcher

        /// <summary>Có .cs nào đổi là cache hết hạn. Chạy trên domain cũ ngay lúc import — không cần compile qua.</summary>
        private sealed class Watcher : AssetPostprocessor
        {
            private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
            {
                if (_files == null && _derived.Count == 0) return;
                if (AnyScript(imported) || AnyScript(deleted) || AnyScript(moved)) Invalidate();
            }

            private static bool AnyScript(string[] paths)
            {
                foreach (var path in paths)
                    if (path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)) return true;
                return false;
            }
        }

        #endregion
    }
}
#endif
