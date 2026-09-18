#if UNITY_EDITOR
using UnityEngine;
using UnityFigmaBridge.Editor.PostProcess;

namespace Ezg.FigmaFeatureImporter.Editor
{
    /// <summary>
    ///     Điểm cắm vào com.ezg.figma-bridge: chạy sau mỗi Sync Document (và sau `Run Post-Processors
    ///     (no Sync)`). Với MỌI frame bridge vừa ghi: chụp bản raw, dựng bản ship nếu có entry
    ///     <c>enabled</c>, rồi biến chính prefab trong thư mục Screens của bridge thành variant của
    ///     <c>screenTemplatePath</c> (<see cref="FigmaFeatureImporter.ProcessBridgeScreen" />).
    ///     Bridge tìm hook này qua TypeCache — không cần đăng ký gì thêm.
    /// </summary>
    public sealed class FigmaFeatureImportHook : IFigmaImportPostProcessor
    {
        public int Order => 100;

        public void OnDocumentImported(FigmaImportContext context)
        {
            var settings = FigmaFeatureImportSettings.LoadOrCreate();
            var withEntry = 0;
            var inPlaceOk = 0;
            foreach (var screen in context.Screens)
            {
                var entry = settings.FindScreen(screen.FrameName);
                if (entry != null && entry.enabled) withEntry++;
                foreach (var report in FigmaFeatureImporter.ProcessBridgeScreen(screen.FrameName))
                    if (report.ok && report.outputPrefab == screen.PrefabPath) inPlaceOk++;
            }
            Debug.Log($"[FigmaImport] Hook: {context.Screens.Count} frame bridge vừa ghi → {inPlaceOk} variant tại chỗ, " +
                      $"{withEntry} màn có entry (bản ship)" +
                      (context.PostProcessOnly ? " (chạy lại không Sync)" : string.Empty) +
                      (context.Offline ? " [offline]" : string.Empty) + ".");
        }
    }
}
#endif
