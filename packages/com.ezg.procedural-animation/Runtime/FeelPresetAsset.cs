using System.Collections.Generic;
using UnityEngine;

namespace Ezg.ProceduralAnimation
{
    [CreateAssetMenu(menuName = "Ezg/Procedural Animation/Feel Preset")]
    public class FeelPresetAsset : ScriptableObject
    {
        [Tooltip("<b>Đường cong chính</b> quyết định cảm giác chuyển động của toàn bộ animation. <i>VD: EaseInOut cho chuyển động mượt, Linear cho chuyển động đều.</i>")]
        public AnimationCurve mainCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [Min(0f), Tooltip("<b>Độ vọt quá</b> khi hoàn thành animation. Giá trị càng cao, hiệu ứng vọt qua đích càng mạnh. <i>VD: 0.2 cho cảm giác nảy nhẹ.</i>")]
        public float overshootAmount = 0f;
        [Range(0f, 1f), Tooltip("<b>Thời điểm bắt đầu vọt quá</b> trong chu kỳ animation (0..1). <i>VD: 0.75 nghĩa là vọt bắt đầu ở 75% thời gian.</i>")]
        public float overshootStartTime = 0.75f;

        [Min(0f), Tooltip("<b>Độ trễ toàn cục</b> áp dụng cho tất cả các bone, tạo hiệu ứng wave khi các bone di chuyển lần lượt. <i>VD: 0.1 để mỗi bone trễ hơn 0.1 giây.</i>")]
        public float globalBoneDelay = 0f;
        [Min(0f), Tooltip("<b>Độ trễ theo cấp con</b> trong hierarchy. Bone càng sâu sẽ càng trễ hơn bone cha. <i>VD: 0.05 nghĩa là mỗi cấp sâu hơn sẽ trễ thêm 0.05 giây.</i>")]
        public float childDepthDelay = 0f;

        [Tooltip("<b>Chế độ giật bước</b> (stepped/stop-motion). Khi bật, animation sẽ giật theo từng frame thay vì nội suy mượt. <i>VD: Dùng để tạo phong cách hoạt hình cổ điển.</i>")]
        public bool steppedMode = false;
        [Min(1), Tooltip("<b>Số khung hình mỗi giây</b> khi ở chế độ stepped. Giá trị càng thấp, chuyển động càng giật. <i>VD: 12 FPS cho phong cách stop-motion.</i>")]
        public int steppedFrameRate = 12;

        [Min(0f), Tooltip("<b>Cường độ nhiễu</b> thêm vào chuyển động để tạo cảm giác tự nhiên, không quá hoàn hảo. <i>VD: 0.05 cho rung nhẹ tự nhiên.</i>")]
        public float noiseAmount = 0f;
        [Tooltip("<b>Hạt giống nhiễu</b> để sinh nhiễu ổn định. Cùng một seed sẽ cho cùng một pattern nhiễu mỗi lần chạy. <i>VD: thay đổi seed để có pattern nhiễu khác.</i>")]
        public int noiseSeed = 12345;

        [Tooltip("<b>Quy tắc thời gian cho từng bone</b> riêng lẻ. Cho phép tùy chỉnh delay và timing cho từng bone cụ thể. <i>VD: bone tay phải trễ hơn bone tay trái.</i>")]
        public List<BoneTimingRule> boneTimingRules = new List<BoneTimingRule>();
    }
}
