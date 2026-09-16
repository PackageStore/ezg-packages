using System;

namespace Ezg.UserSegment.Engine
{
    /// <summary>
    ///     Thời gian — §4.4: now = device_utc + offset; offset học từ server_time; monotonic chỉ đối chiếu trong foreground.
    /// </summary>
    public sealed class Clock
    {
        public const long OFFSET_JUMP_SUSPECT_S = 3600;
        public const double FOREGROUND_JUMP_SUSPECT_S = 60;
        public const long DAY_S = 86400;

        private readonly ITimeSource _src;
        private readonly EngineState _state;

        // Mốc đối chiếu trong khoảng foreground hiện tại
        private long _fgNowAnchor;
        private double _fgMonoAnchor;
        private bool _fgAnchored;

        /// <summary>Override cho debug / test: null = dùng đồng hồ thật.</summary>
        public long? NowOverride;

        public Clock(ITimeSource src, EngineState state)
        {
            _src = src;
            _state = state;
        }

        public static long UtcDay(long ts) => ts <= 0 ? 0 : ts / DAY_S;

        /// <summary>Giờ đã hiệu chỉnh; chạy ngược so với last_event_at → clamp + clock_suspect.</summary>
        public long Now()
        {
            if (NowOverride.HasValue) return NowOverride.Value;
            var now = _src.DeviceUtcNowSeconds() + _state.Clock.OffsetS;
            if (now < _state.Clock.LastEventAt)
            {
                _state.Context.ClockSuspect = true;
                now = _state.Clock.LastEventAt;
            }

            return now;
        }

        public long DeviceNow() => _src.DeviceUtcNowSeconds();

        /// <summary>Học offset tại mỗi response /config thành công.</summary>
        public void LearnOffset(long serverTime)
        {
            var newOffset = serverTime - _src.DeviceUtcNowSeconds();
            if (_state.Clock.HasOffset && Math.Abs(newOffset - _state.Clock.OffsetS) > OFFSET_JUMP_SUSPECT_S)
                _state.Context.ClockSuspect = true;
            _state.Clock.OffsetS = newOffset;
            _state.Clock.HasOffset = true;
            ResetForegroundAnchor();
        }

        /// <summary>Bắt đầu khoảng foreground mới (init / resume): đặt lại mốc đối chiếu.</summary>
        public void ResetForegroundAnchor()
        {
            _fgNowAnchor = Now();
            _fgMonoAnchor = _src.MonotonicSeconds();
            _fgAnchored = true;
        }

        /// <summary>Đối chiếu monotonic tại mỗi event trong foreground: nhảy tiến > 60 s → clock_suspect hết session.</summary>
        public void CheckForegroundJump(long now)
        {
            if (!_fgAnchored || NowOverride.HasValue) return;
            var dNow = now - _fgNowAnchor;
            var dMono = _src.MonotonicSeconds() - _fgMonoAnchor;
            if (dNow - dMono > FOREGROUND_JUMP_SUSPECT_S) _state.Context.ClockSuspect = true;
        }

        public void StopForeground() => _fgAnchored = false;
    }
}
