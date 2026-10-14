using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Ezg.BugLogger.UI
{
    /// <summary>Đường cong easing cho <see cref="BugTweener" />.</summary>
    internal enum BugEase
    {
        Linear,
        OutCubic,
        InCubic,
        OutQuint,
        OutBack,
        InOutSine
    }

    /// <summary>
    ///     Bộ tween tối giản của UI Bug Logger — package không kéo thư viện tween của game theo (mỗi project một
    ///     bộ, một phiên bản). Chạy bằng thời gian thực (<see cref="Time.unscaledDeltaTime" />) vì form dừng game bằng
    ///     <c>timeScale = 0</c>. Mỗi tween gắn với (target, kênh): tween mới cùng kênh huỷ tween cũ; target bị huỷ thì
    ///     tween tự rơi.
    /// </summary>
    internal sealed class BugTweener : MonoBehaviour
    {
        #region Fields

        /// <summary>Kênh dùng chung — một target có thể chạy song song nhiều kênh khác nhau.</summary>
        public const int CHANNEL_ALPHA = 1;

        public const int CHANNEL_SCALE = 2;
        public const int CHANNEL_MOVE = 3;
        public const int CHANNEL_COLOR = 4;
        public const int CHANNEL_SHAKE = 5;
        public const int CHANNEL_LOOP = 6;
        public const int CHANNEL_ANY = -1;

        private const float BACK_OVERSHOOT = 1.70158f;

        // Khung hình bị khựng (load scene, GC) không được làm tween nhảy cóc tới cuối.
        private const float MAX_STEP = 1f / 20f;

        private sealed class Tween
        {
            public Action<float> Apply;
            public int Channel;
            public Action Complete;
            public bool Dead;
            public float Delay;
            public float Duration;
            public BugEase Ease;
            public float Elapsed;
            public bool PingPong;
            public Object Target;
        }

        private static BugTweener _instance;

        private readonly List<Tween> _tweens = new List<Tween>();
        private readonly Stack<Tween> _pool = new Stack<Tween>();

        #endregion

        #region Initialize

        private void Awake()
        {
            _instance = this;
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        private void Update()
        {
            var dt = Mathf.Min(Time.unscaledDeltaTime, MAX_STEP);

            // Apply/Complete có thể thêm hoặc huỷ tween ⇒ duyệt theo chỉ số, huỷ = đánh dấu, dọn sau vòng lặp.
            var count = _tweens.Count;
            for (var i = 0; i < count; i++)
            {
                var tween = _tweens[i];
                if (tween.Dead) continue;

                if (tween.Target == null)
                {
                    tween.Dead = true;
                    continue;
                }

                if (tween.Delay > 0f)
                {
                    tween.Delay -= dt;
                    continue;
                }

                tween.Elapsed += dt;
                var t = tween.Duration <= 0f ? 1f : Mathf.Clamp01(tween.Elapsed / tween.Duration);

                if (tween.PingPong)
                {
                    var cycle = tween.Duration <= 0f ? 0f : tween.Elapsed / tween.Duration % 2f;
                    var phase = cycle <= 1f ? cycle : 2f - cycle;
                    tween.Apply(Evaluate(tween.Ease, phase));
                    continue;
                }

                tween.Apply(Evaluate(tween.Ease, t));
                if (t < 1f) continue;

                tween.Dead = true;
                tween.Complete?.Invoke();
            }

            Sweep();
        }

        #endregion

        #region Public Methods

        /// <summary>
        ///     Chạy <paramref name="apply" />(0→1 đã ease) trong <paramref name="duration" /> giây thực. Không có runner
        ///     (overlay chưa dựng) thì áp giá trị cuối ngay.
        /// </summary>
        public static void To(Object target, int channel, float duration, Action<float> apply,
            BugEase ease = BugEase.OutCubic, Action complete = null, float delay = 0f)
        {
            Kill(target, channel);
            if (_instance == null)
            {
                apply(1f);
                complete?.Invoke();
                return;
            }

            _instance.Add(target, channel, duration, apply, ease, complete, delay, false);
        }

        /// <summary>Lặp qua lại 0→1→0 mãi tới khi <see cref="Kill" /> (skeleton nhấp nháy, nhịp chờ).</summary>
        public static void PingPong(Object target, int channel, float halfPeriod, Action<float> apply,
            BugEase ease = BugEase.InOutSine)
        {
            Kill(target, channel);
            if (_instance == null)
            {
                apply(0f);
                return;
            }

            _instance.Add(target, channel, halfPeriod, apply, ease, null, 0f, true);
        }

        /// <summary>Huỷ tween của target (một kênh hoặc <see cref="CHANNEL_ANY" />). Không gọi Complete.</summary>
        public static void Kill(Object target, int channel = CHANNEL_ANY)
        {
            if (_instance == null) return;

            var tweens = _instance._tweens;
            for (var i = 0; i < tweens.Count; i++)
            {
                var tween = tweens[i];
                if (tween.Dead || !ReferenceEquals(tween.Target, target)) continue;
                if (channel != CHANNEL_ANY && tween.Channel != channel) continue;

                tween.Dead = true;
            }
        }

        public static float Evaluate(BugEase ease, float t)
        {
            switch (ease)
            {
                case BugEase.OutCubic:
                {
                    var u = 1f - t;
                    return 1f - u * u * u;
                }
                case BugEase.InCubic:
                    return t * t * t;
                case BugEase.OutQuint:
                {
                    var u = 1f - t;
                    return 1f - u * u * u * u * u;
                }
                case BugEase.OutBack:
                {
                    var u = t - 1f;
                    return 1f + (BACK_OVERSHOOT + 1f) * u * u * u + BACK_OVERSHOOT * u * u;
                }
                case BugEase.InOutSine:
                    return 0.5f - 0.5f * Mathf.Cos(t * Mathf.PI);
                default:
                    return t;
            }
        }

        #endregion

        #region Private Methods

        private void Add(Object target, int channel, float duration, Action<float> apply, BugEase ease,
            Action complete, float delay, bool pingPong)
        {
            var tween = _pool.Count > 0 ? _pool.Pop() : new Tween();
            tween.Target = target;
            tween.Channel = channel;
            tween.Duration = duration;
            tween.Apply = apply;
            tween.Ease = ease;
            tween.Complete = complete;
            tween.Delay = delay;
            tween.Elapsed = 0f;
            tween.PingPong = pingPong;
            tween.Dead = false;
            _tweens.Add(tween);

            // Áp frame đầu ngay để không loé trạng thái cũ một khung hình.
            if (delay <= 0f) apply(Evaluate(ease, 0f));
        }

        private void Sweep()
        {
            for (var i = _tweens.Count - 1; i >= 0; i--)
            {
                var tween = _tweens[i];
                if (!tween.Dead) continue;

                _tweens.RemoveAt(i);
                tween.Apply = null;
                tween.Complete = null;
                tween.Target = null;
                _pool.Push(tween);
            }
        }

        #endregion
    }
}
