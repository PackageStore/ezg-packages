namespace Ezg.Feature.System.UserSegment
{
    /// <summary>
    ///     Knob độ khó runtime của gameplay — template không có; project thật gán vào
    ///     <see cref="UserSegmentBootstrap.DifficultyKnob" /> TRƯỚC khi Init để executor CHANGE_DIFFICULTY được đăng ký.
    /// </summary>
    public interface IDifficultyKnob
    {
        /// <returns>true = đã áp cho unit kế tiếp; false = không có unit chờ (result no_unit_pending).</returns>
        bool Apply(int delta, string scope);
    }
}
