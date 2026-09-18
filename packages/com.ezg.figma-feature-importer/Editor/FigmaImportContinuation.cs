#if UNITY_EDITOR
using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Ezg.FigmaFeatureImporter.Editor
{
    /// <summary>
    ///     Lượt 2 của codegen (quyết định D8 trong TechSpec/FigmaImport-TechSpec.md).
    ///
    ///     Import lần 1 sinh `&lt;X&gt;Controller.Figma.cs` (field [SerializeField] mới) — file đó phải
    ///     compile xong mới có field để wire, mà compile xong là domain reload, mất luôn stack đang
    ///     chạy. Nên importer ghi tên frame vào <see cref="SessionState" /> (sống qua reload, chết khi
    ///     đóng Editor) rồi gọi Refresh; sau reload <c>[InitializeOnLoadMethod]</c> đọc marker và
    ///     chạy <see cref="FigmaFeatureImporter.Import" /> lại cho từng frame — người dùng không phải
    ///     bấm gì thêm. Fallback thủ công: menu `Rebuild Screens (ship + in place)`.
    /// </summary>
    internal static class FigmaImportContinuation
    {
        private const string PENDING_KEY = "figma.pending";
        private const char SEPARATOR = ';';

        /// <summary>Ghi nhớ frame cần import lại sau reload (gộp với những frame đang chờ).</summary>
        internal static void Schedule(string frameName)
        {
            if (string.IsNullOrEmpty(frameName)) return;
            var pending = Read();
            if (pending.Contains(frameName)) return;
            pending = pending.Append(frameName).ToArray();
            SessionState.SetString(PENDING_KEY, string.Join(SEPARATOR.ToString(), pending));
        }

        internal static bool HasPending => Read().Length > 0;

        private static string[] Read()
        {
            var raw = SessionState.GetString(PENDING_KEY, string.Empty);
            return string.IsNullOrEmpty(raw)
                ? Array.Empty<string>()
                : raw.Split(SEPARATOR).Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();
        }

        [InitializeOnLoadMethod]
        private static void OnDomainReload()
        {
            if (!HasPending) return;
            EditorApplication.delayCall += RunPendingWhenIdle;
        }

        private static void RunPendingWhenIdle()
        {
            // Compile chưa xong (script sinh ra chưa thành assembly) → chờ thêm một nhịp.
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += RunPendingWhenIdle;
                return;
            }

            var frames = Read();
            SessionState.EraseString(PENDING_KEY);
            if (frames.Length == 0) return;

            Debug.Log($"[FigmaImport] Codegen đã compile — import lại {frames.Length} frame để wire field: " +
                      string.Join(", ", frames));
            foreach (var frame in frames)
            {
                try
                {
                    var report = FigmaFeatureImporter.Import(frame);
                    Debug.Log($"[FigmaImport] Lượt 2 '{frame}':\n{report}");
                }
                catch (Exception e)
                {
                    Debug.LogError($"[FigmaImport] Lượt 2 '{frame}' lỗi: {e}");
                }
            }
        }
    }
}
#endif
