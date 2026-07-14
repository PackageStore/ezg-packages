using UnityEngine;

namespace Ezg.ProceduralAnimation.Editor
{
    internal static class InbetweenGeneratorContent
    {
        public static readonly GUIContent SkeletonRoot = new GUIContent(
            "Skeleton Root",
            "<b>Transform</b> gốc của bộ xương nhân vật. Kéo GameObject chứa các bone vào đây để hệ thống biết cấu trúc xương.");

        public static readonly GUIContent PresetFolder = new GUIContent(
            "Preset Folder",
            "Thư mục chứa các <b>FeelPresetAsset</b>. Kéo thả <b>folder</b> từ Project vào field này hoặc bấm Pick để chọn.\n<i>Ví dụ: Assets/Presets/Feel/</i>");

        public static readonly GUIContent PoseCaptureFolder = new GUIContent(
            "Pose Capture Folder",
            "Thư mục lưu các <b>PoseAsset</b> được capture từ Scene View.\n<i>Ví dụ: Assets/Poses/</i>");

        public static readonly GUIContent OutputFolder = new GUIContent(
            "Output Folder",
            "Thư mục xuất các <b>AnimationClip</b> được tạo ra từ batch export.\n<i>Ví dụ: Assets/Animations/Generated/</i>");

        public static readonly GUIContent AllConnectionsFoldout = new GUIContent(
            "All Connections",
            "Xem và chỉnh từng <b>connection</b> trong graph: bật/tắt, <b>Feel Preset</b>, và <b>Duration</b>. Đây không phải batch export.");

        public static readonly GUIContent BatchAnimationExportFoldout = new GUIContent(
            "Batch Animation Export",
            "Thiết lập và xuất hàng loạt các <b>AnimationClip</b> từ những path đã chọn.");

        public static readonly GUIContent PreviewFoldout = new GUIContent(
            "Preview",
            "Preview path animation trực tiếp trên nhân vật trong Scene. Mặc định dùng <b>Skeleton Root</b>, có thể tắt để override bằng target khác.");

        public static readonly GUIContent UseSkeletonRoot = new GUIContent(
            "Use Skeleton Root",
            "Bật để tự dùng <b>Skeleton Root</b> làm Preview Target. Tắt nếu muốn override bằng nhân vật hoặc transform khác.");

        public static readonly GUIContent PreviewTarget = new GUIContent(
            "Preview Target",
            "<b>Transform</b> của nhân vật sẽ được sample animation khi preview. Mặc định lấy từ <b>Skeleton Root</b>; tắt <b>Use Skeleton Root</b> để override.\n<i>Ví dụ: kéo _stickman_base vào đây.</i>");

        public static readonly GUIContent PlayPreview = new GUIContent(
            "▶ Play",
            "Để bật preview, hãy click trực tiếp vào <b>TÊN</b> path trong danh sách <b>Valid Paths</b>, không phải checkbox export. <i>Ví dụ: click vào dòng Stage A2_Stage B1_Stage C1.</i>");
    }
}
