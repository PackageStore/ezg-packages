#if UNITY_EDITOR
using System;
using System.Reflection;
using System.Threading;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Ezg.VoodooSdk.Editor
{
    /// <summary>
    /// Ép Apple Unity Plug-ins hoàn tất khởi tạo trước khi build iOS bằng batchmode.
    ///
    /// <c>ApplePlugInEnvironment</c> chỉ gắn <c>EditorApplication.update</c> khi
    /// <c>!Application.isBatchMode</c>. Build bằng CLI thì state machine của nó kẹt ở
    /// <c>Initializing</c>: kết quả <c>Client.List()</c> không được xử lý, không package Apple nào
    /// được track, và <c>AppleCoreBuildStep</c> không copy native library nào vào Xcode project.
    /// Hậu quả là link fail hàng trăm symbol:
    ///
    ///     Undefined symbols: _NSString_Utf8String, _NSObject_As, _NSNumber_LongValue, ...
    ///     ld: symbol(s) not found for architecture arm64
    ///
    /// Rất chập chờn: build từ Editor GUI luôn ổn, và batchmode cũng "may mắn" chạy đúng nếu ngay
    /// trước đó có thay đổi script (domain reload đi qua nhánh OnPostprocessAllAssets). Vì vậy
    /// lỗi này hay bị quy nhầm cho nguyên nhân khác.
    ///
    /// Không có API public để ép, nên tự đẩy state machine bằng reflection. Package vendor không
    /// bị sửa, nâng cấp plug-in vẫn giữ nguyên hành vi.
    /// </summary>
    public class VoodooSdkApplePlugInPrimer : IPreprocessBuildWithReport
    {
        #region Fields

        /// <summary>Chạy sớm — native library phải sẵn sàng trước khi Unity sinh Xcode project.</summary>
        public int callbackOrder => -100;

        private const string EnvironmentTypeFullName = "Apple.Core.ApplePlugInEnvironment";
        private const string TypeName = EnvironmentTypeFullName + ", Apple.Core.Editor";
        private const int MaxTicks = 120;
        private const int TickDelayMs = 250;

        #endregion

        #region Events

        public void OnPreprocessBuild(BuildReport report)
        {
            if (report.summary.platform == BuildTarget.iOS)
                Prime();
        }

        #endregion

        #region Public

        /// <summary>Đẩy state machine của Apple Unity Plug-ins tới khi khởi tạo xong.</summary>
        public static void Prime()
        {
            if (!Application.isBatchMode)
                return; // GUI tick bình thường, không cần can thiệp.

            Type type = ResolveEnvironmentType();
            if (type == null)
                return; // Project không dùng Apple Unity Plug-ins.

            const BindingFlags Flags = BindingFlags.NonPublic | BindingFlags.Static;
            MethodInfo tick = type.GetMethod("OnEditorUpdate", Flags);
            FieldInfo stateField = type.GetField("_updateState", Flags);

            if (tick == null || stateField == null)
            {
                Debug.LogWarning($"[VoodooSdk] {TypeName} đổi cấu trúc nội bộ — không mồi được. " +
                                 "Nếu link iOS báo undefined _NSString_*/_NSObject_*, build lại từ Editor GUI.");
                return;
            }

            AssetDatabase.Refresh();
            object initializing = stateField.GetValue(null);

            for (int i = 0; i < MaxTicks; i++)
            {
                tick.Invoke(null, null);
                if (!Equals(stateField.GetValue(null), initializing))
                {
                    Debug.Log($"[VoodooSdk] Apple Unity Plug-ins khởi tạo xong sau {i + 1} tick.");
                    return;
                }
                Thread.Sleep(TickDelayMs);
            }

            Debug.LogWarning($"[VoodooSdk] Apple Unity Plug-ins vẫn ở '{initializing}' sau {MaxTicks} tick. " +
                             "Kiểm tra thư mục ApplePluginLibraries trong Xcode project sau khi build.");
        }

        #endregion

        #region Private

        /// <summary>
        /// Tìm <c>Apple.Core.ApplePlugInEnvironment</c> một cách chắc chắn.
        ///
        /// KHÔNG dùng riêng <see cref="Type.GetType(string)"/> với tên assembly dạng ngắn:
        /// nó chỉ tra trong các assembly ĐÃ nạp, nên tuỳ thời điểm mà lúc thấy lúc không —
        /// build batchmode từng báo "không có trong project" dù package vẫn ở đó, khiến native
        /// library không được copy và link iOS vỡ với undefined <c>_NSString_*</c>.
        ///
        /// <c>TypeCache</c> của Unity index sẵn mọi type nên không phụ thuộc trạng thái nạp.
        /// </summary>
        private static Type ResolveEnvironmentType()
        {
            Type type = Type.GetType(TypeName);
            if (type != null)
                return type;

            foreach (Type candidate in TypeCache.GetTypesDerivedFrom<AssetPostprocessor>())
            {
                if (candidate.FullName == EnvironmentTypeFullName)
                    return candidate;
            }

            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                type = assembly.GetType(EnvironmentTypeFullName, throwOnError: false);
                if (type != null)
                    return type;
            }

            return null;
        }

        #endregion
    }
}
#endif
