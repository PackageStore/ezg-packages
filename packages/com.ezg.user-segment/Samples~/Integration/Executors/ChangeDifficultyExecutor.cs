using Ezg.UserSegment;

namespace Ezg.Feature.System.UserSegment
{
    /// <summary>CHANGE_DIFFICULTY → <see cref="IDifficultyKnob" /> của project. Chỉ đăng ký khi project cung cấp knob.</summary>
    public sealed class ChangeDifficultyExecutor : IActionExecutor
    {
        private readonly IDifficultyKnob _knob;
        public ActionType ActionType => ActionType.CHANGE_DIFFICULTY;

        public ChangeDifficultyExecutor(IDifficultyKnob knob)
        {
            _knob = knob;
        }

        public void Execute(ActionRequest request)
        {
            request.ReportPresented();
            var applied = _knob.Apply(request.Params.Delta, request.Params.Scope);
            if (UnityEngine.Debug.isDebugBuild) UnityEngine.Debug.Log($"[UserSegment] ChangeDifficulty #{request.ExecutionId}: delta={request.Params.Delta} scope={request.Params.Scope} → {(applied ? "applied" : "no_unit_pending")}");
            request.ReportExecuted(applied ? "applied" : "no_unit_pending");
        }
    }
}
