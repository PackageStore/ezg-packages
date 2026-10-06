#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace Ezg.Editor.Shared.EzgKit
{
    /// <summary>
    ///     Đường dẫn dùng chung của kit. Mọi thứ tính từ thư mục chứa <c>Assets/</c> (project root).
    /// </summary>
    internal static class ProjectPaths
    {
        internal static string Root => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

        /// <summary>Đường dẫn tuyệt đối từ đường dẫn tương đối project root (dấu <c>/</c>).</summary>
        internal static string Abs(string projectRelative) =>
            Path.GetFullPath(Path.Combine(Root, projectRelative ?? string.Empty));

        /// <summary>Đường dẫn tương đối project root, dấu <c>/</c>; nằm ngoài project thì trả nguyên.</summary>
        internal static string Rel(string absolute)
        {
            if (string.IsNullOrEmpty(absolute)) return absolute;
            var full = Path.GetFullPath(absolute).Replace('\\', '/');
            var root = Root.Replace('\\', '/').TrimEnd('/') + "/";
            return full.StartsWith(root, StringComparison.Ordinal) ? full.Substring(root.Length) : full;
        }

        internal static bool IsInsideProject(string absolute)
        {
            if (string.IsNullOrEmpty(absolute)) return false;
            var full = Path.GetFullPath(absolute).Replace('\\', '/');
            var root = Root.Replace('\\', '/').TrimEnd('/') + "/";
            return full.StartsWith(root, StringComparison.Ordinal);
        }

        internal static string StateFile => Abs("ProjectSettings/EzgKitSetup.json");

        internal static string ProfileFile => Abs(".claude/project-profile.json");

        internal static string ArtStyleFile => Abs(".claude/docs/ArtStyle.md");

        internal static string ArtStyleTemplate => Abs(".claude/docs/ArtStyle.template.md");

        internal static string ArtStyleBoardDir => Abs(".claude/docs/ArtStyle");

        internal static string ArtStyleBoardScript => Abs(".claude/scripts/art-style-board.py");

        internal static string BackupRoot => Abs("Library/EzgKit/Backups");
    }

    /// <summary>
    ///     Trạng thái setup của dự án — <c>ProjectSettings/EzgKitSetup.json</c>, được track git để cả team
    ///     (và Claude qua <c>/setup-project</c>) thấy cùng một bức tranh.
    ///     <code>
    ///     {
    ///       "schema": 1,
    ///       "pages":   { "&lt;pageId&gt;": { "marker": "done|deferred|na", "at": "ISO-8601", "note": "" } },
    ///       "answers": { "&lt;pageId&gt;": { …giá trị KHÔNG bí mật người dùng đã nhập… } },
    ///       "requests": [ "artstyle", … ]
    ///     }
    ///     </code>
    ///     <para>
    ///         <b>Không bao giờ chứa secret.</b> Key / token sống trong asset thật của chúng (AppSecretsConfig,
    ///         AdsConfig) hoặc EditorPrefs theo máy. File này chỉ ghi quyết định của người (để sau, không áp
    ///         dụng) và những câu trả lời vô hại để mở lại cửa sổ không phải gõ lại.
    ///     </para>
    /// </summary>
    internal static class EzgKitState
    {
        internal const string MARKER_DONE = "done";
        internal const string MARKER_DEFERRED = "deferred";
        internal const string MARKER_NA = "na";

        private static JsonObject _cache;
        private static DateTime _cacheStamp;

        #region Load / Save

        internal static JsonObject Load()
        {
            var path = ProjectPaths.StateFile;
            if (!File.Exists(path)) return _cache = NewState();

            var stamp = File.GetLastWriteTimeUtc(path);
            if (_cache != null && stamp == _cacheStamp) return _cache;

            var parsed = MiniJson.ParseObject(File.ReadAllText(path));
            if (parsed == null)
            {
                Debug.LogWarning($"[EzgKit] {ProjectPaths.Rel(path)} hỏng — coi như chưa có state.");
                parsed = NewState();
            }

            _cache = parsed;
            _cacheStamp = stamp;
            return _cache;
        }

        internal static void Save(JsonObject state)
        {
            state.Set("schema", 1);
            var path = ProjectPaths.StateFile;
            File.WriteAllText(path, MiniJson.Serialize(state) + "\n", new UTF8Encoding(false));
            _cache = state;
            _cacheStamp = File.GetLastWriteTimeUtc(path);
        }

        private static JsonObject NewState() =>
            new JsonObject()
                .Set("schema", 1)
                .Set("pages", new JsonObject())
                .Set("answers", new JsonObject())
                .Set("requests", new List<object>());

        #endregion

        #region Markers

        /// <summary>Marker của trang; null = chưa có quyết định nào.</summary>
        internal static string GetMarker(string pageId) =>
            Load().Obj("pages")?.Obj(pageId)?.Str("marker", null);

        internal static string GetMarkerTime(string pageId) =>
            Load().Obj("pages")?.Obj(pageId)?.Str("at", null);

        /// <summary>
        ///     Đặt marker. <c>null</c> / rỗng = xoá marker (trang quay về để detector quyết).
        ///     Chỉ nhận <see cref="MARKER_DONE" />, <see cref="MARKER_DEFERRED" />, <see cref="MARKER_NA" />.
        /// </summary>
        internal static void SetMarker(string pageId, string marker, string note = null)
        {
            if (string.IsNullOrEmpty(pageId)) return;
            var state = Load();
            var pages = state.ObjOrNew("pages");

            if (string.IsNullOrEmpty(marker) || marker == "clear" || marker == "none")
            {
                pages.Remove(pageId);
                Save(state);
                return;
            }

            if (marker != MARKER_DONE && marker != MARKER_DEFERRED && marker != MARKER_NA)
                throw new ArgumentException($"Marker không hợp lệ: '{marker}' (chỉ nhận done | deferred | na | clear).");

            pages.Set(pageId, new JsonObject()
                .Set("marker", marker)
                .Set("at", DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture))
                .Set("note", note ?? string.Empty));
            Save(state);
        }

        /// <summary>Áp marker lên trạng thái do detector tính: deferred / na luôn thắng.</summary>
        internal static SetupState Resolve(string pageId, SetupState detected)
        {
            var marker = GetMarker(pageId);
            return marker switch
            {
                MARKER_DEFERRED when detected != SetupState.Done => SetupState.Deferred,
                MARKER_NA => SetupState.NotApplicable,
                _ => detected,
            };
        }

        #endregion

        #region Answers

        internal static JsonObject Answers(string pageId) => Load().Obj("answers")?.Obj(pageId);

        internal static string Answer(string pageId, string key, string fallback = "") =>
            Answers(pageId)?.Str(key, fallback) ?? fallback;

        internal static void SetAnswer(string pageId, string key, object value)
        {
            var state = Load();
            state.ObjOrNew("answers").ObjOrNew(pageId).Set(key, value);
            Save(state);
        }

        #endregion

        #region Requests (việc nhờ Claude làm)

        internal static List<string> Requests()
        {
            var result = new List<string>();
            var list = Load().List("requests");
            if (list == null) return result;
            foreach (var item in list)
                if (item is string s && !string.IsNullOrEmpty(s) && !result.Contains(s))
                    result.Add(s);
            return result;
        }

        internal static void AddRequest(string id)
        {
            var state = Load();
            var list = state.List("requests") ?? new List<object>();
            if (!list.Contains(id)) list.Add(id);
            state.Set("requests", list);
            Save(state);
        }

        internal static void ClearRequest(string id)
        {
            var state = Load();
            var list = state.List("requests");
            if (list == null) return;
            list.RemoveAll(x => x as string == id);
            Save(state);
        }

        #endregion
    }

    /// <summary>
    ///     Bản lưu trước khi kit ghi đè một file text (GameConstant.cs, AndroidManifest.xml, ArtStyle.md,
    ///     project-profile.json). Đặt trong <c>Library/</c> (không vào git); mỗi lượt áp dụng một thư mục
    ///     theo giờ để còn lấy lại được khi ghi nhầm.
    /// </summary>
    internal static class EzgBackup
    {
        private static string _session;

        /// <summary>Bắt đầu một lượt ghi — các file backup trong lượt này chung một thư mục.</summary>
        internal static void BeginSession() =>
            _session = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);

        internal static string Save(string absolutePath)
        {
            try
            {
                if (string.IsNullOrEmpty(absolutePath) || !File.Exists(absolutePath)) return null;
                _session ??= DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
                var relative = ProjectPaths.IsInsideProject(absolutePath)
                    ? ProjectPaths.Rel(absolutePath)
                    : Path.GetFileName(absolutePath);
                var target = Path.Combine(ProjectPaths.BackupRoot, _session, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target) ?? ProjectPaths.BackupRoot);
                if (!File.Exists(target)) File.Copy(absolutePath, target);
                return target;
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[EzgKit] Không backup được {absolutePath}: {exception.Message}");
                return null;
            }
        }
    }
}
#endif
