#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Ezg.FigmaFeatureImporter.Editor
{
    /// <summary>
    ///     Menu ASCII (gọi được qua MCP) cho luồng Figma → màn hình feature. Chỉ ba mục: dựng lại,
    ///     audit, mở settings. Sync / re-import offline / chạy post-processor nằm bên menu của bridge
    ///     (`Tools/EZG Technical Art/Figma Bridge`), không lặp lại ở đây.
    /// </summary>
    internal static class FigmaImportMenu
    {
        private const string ROOT = "Tools/EZG Technical Art/Figma Feature Importer/";

        /// <summary>Bản ship cho mọi entry enabled + variant tại chỗ cho mọi prefab trong Screens của bridge.</summary>
        [MenuItem(ROOT + "Rebuild Screens (ship + in place)", priority = 300)]
        private static void RebuildAll()
        {
            var ship = FigmaFeatureImporter.ImportAllEnabled();
            var inPlace = FigmaFeatureImporter.RebuildAllInPlace();
            var shipOk = ship.FindAll(r => r.ok).Count;
            var inPlaceOk = inPlace.FindAll(r => r.ok).Count;
            Debug.Log($"[FigmaImport] Rebuild: ship {shipOk}/{ship.Count} entry enabled · variant tại chỗ {inPlaceOk}/{inPlace.Count} màn trong thư mục Screens của bridge — xem report từng màn ở log phía trên.");
        }

        /// <summary>Audit variant tại chỗ (cấu trúc) + bản ship của entry enabled (kèm snapshot PNG).</summary>
        [MenuItem(ROOT + "Audit Screens", priority = 301)]
        private static void AuditAll()
        {
            foreach (var report in FigmaImportAudit.RunAllInPlace())
            {
                if (report.pass) Debug.Log($"[FigmaImport] Audit PASS (tại chỗ) {report.frame}\n{report}");
                else Debug.LogError($"[FigmaImport] Audit FAIL (tại chỗ) {report.frame}\n{report}");
            }
            foreach (var report in FigmaImportAudit.RunEnabled(snapshot: true))
            {
                if (report.pass) Debug.Log($"[FigmaImport] Audit PASS (ship) {report.frame}\n{report}");
                else Debug.LogWarning($"[FigmaImport] Audit FAIL (ship) {report.frame}\n{report}");
            }
        }

        [MenuItem(ROOT + "Select Import Settings", priority = 320)]
        private static void SelectSettings()
        {
            var settings = FigmaFeatureImportSettings.LoadOrCreate();
            Selection.activeObject = settings;
            EditorGUIUtility.PingObject(settings);
        }
    }
}
#endif
