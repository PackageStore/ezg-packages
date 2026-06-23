// EZG Feature Hub — VisualElement phát Lottie trong Editor (edit mode).
// rlottie render từng frame ra Texture2D; tự drive bằng EditorApplication.timeSinceStartup
// (không dùng LottieAnimation.Update vì nó phụ thuộc Time.deltaTime — chỉ chạy ở play mode).
//   Loop : lặp vô hạn (spinner, brand).
//   Once : phát 0->cuối một lần (check, confetti).
//   Idle : đứng yên ở frame cuối (icon đã thành hình); gọi Play() để phát micro-animation khi hover.
//
// EZG_HAS_RLOTTIE: define do asmdef versionDefines bật khi project có com.gindemit.rlottie.
// Khi CHƯA có gói (vd vừa cài Feature Hub trên máy mới), file vẫn compile nhờ bản stub bên dưới
// — element chỉ là khung trống, không animation. Feature Hub tự thêm rlottie vào manifest khi load
// (FeatureHubRuntimeDependency); sau khi Unity resolve, define bật lên và icon động hoạt động.
using UnityEngine.UIElements;
#if EZG_HAS_RLOTTIE
using System;
using LottiePlugin;
using UnityEditor;
using UnityEngine;
#endif

namespace Ezg.FeatureHub.Editor
{
    public enum LottiePlay
    {
        Loop,
        Once,
        Idle,
    }

#if EZG_HAS_RLOTTIE
    public sealed class LottieElement : VisualElement
    {
        #region Fields

        private readonly string _json;
        private readonly uint _renderSize;
        private readonly LottiePlay _mode;
        private readonly Image _image;

        private LottieAnimation _animation;
        private double _startTime;
        private int _lastFrame = -1;
        private bool _ticking;
        private bool _loop;

        #endregion

        #region Initialize

        /// <param name="json">Nội dung Lottie JSON.</param>
        /// <param name="displaySize">Kích thước hiển thị (px) — element vuông.</param>
        /// <param name="mode">Loop / Once / Idle.</param>
        /// <param name="renderSize">Độ phân giải texture (mặc định = displaySize*2 cho sắc nét).</param>
        public LottieElement(string json, int displaySize, LottiePlay mode = LottiePlay.Loop, uint renderSize = 0)
        {
            _json = json;
            _mode = mode;
            _renderSize = renderSize == 0 ? (uint)Mathf.Max(32, displaySize * 2) : renderSize;

            style.width = displaySize;
            style.height = displaySize;
            style.flexShrink = 0;
            style.overflow = Overflow.Hidden; // texture không tràn ra ngoài khung

            // Image đặt absolute phủ kín khung -> không đẩy/đè layout xung quanh.
            _image = new Image { scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
            _image.style.position = Position.Absolute;
            _image.style.left = 0;
            _image.style.top = 0;
            _image.style.right = 0;
            _image.style.bottom = 0;
            // rlottie ghi buffer top-down còn Texture2D gốc bottom-left -> lật dọc cho đúng chiều.
            _image.style.scale = new Scale(new Vector3(1f, -1f, 1f));
            Add(_image);

            RegisterCallback<AttachToPanelEvent>(_ => OnAttach());
            RegisterCallback<DetachFromPanelEvent>(_ => OnDetach());
        }

        #endregion

        #region Public Methods

        /// <summary>Phát micro-animation 0->cuối một lần (dùng cho hover).</summary>
        public void Play()
        {
            StartOnce();
        }

        public void PlayOnce()
        {
            StartOnce();
        }

        public void PlayLoop()
        {
            _loop = true;
            _lastFrame = -1;
            _startTime = EditorApplication.timeSinceStartup;
            StartTicking();
        }

        #endregion

        #region Private Methods — lifecycle

        private void OnAttach()
        {
            CreateAnimation();
            switch (_mode)
            {
                case LottiePlay.Loop:
                    PlayLoop();
                    break;
                case LottiePlay.Once:
                    StartOnce();
                    break;
                default:
                    RenderRestFrame();
                    break;
            }
        }

        private void OnDetach()
        {
            StopTicking();
            DisposeAnimation();
        }

        private void CreateAnimation()
        {
            if (_animation != null || string.IsNullOrEmpty(_json))
                return;

            try
            {
                _animation = LottieAnimation.LoadFromJsonData(_json, string.Empty, _renderSize, _renderSize);
                _image.image = _animation.Texture;
                _animation.DrawOneFrame(0);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[FeatureHub] Lottie render lỗi: {e.Message}");
                _animation = null;
            }
        }

        private void DisposeAnimation()
        {
            if (_animation == null)
                return;

            _animation.Dispose();
            _animation = null;
            _image.image = null;
        }

        #endregion

        #region Private Methods — playback

        private void StartOnce()
        {
            if (_animation == null)
                return;
            _loop = false;
            _lastFrame = -1;
            _startTime = EditorApplication.timeSinceStartup;
            StartTicking();
        }

        private void RenderRestFrame()
        {
            if (_animation == null)
                return;
            int last = (int)Mathf.Max(0, _animation.TotalFramesCount - 1);
            DrawIfChanged(last);
        }

        private void StartTicking()
        {
            if (_ticking)
                return;
            _ticking = true;
            EditorApplication.update += Tick;
        }

        private void StopTicking()
        {
            if (!_ticking)
                return;
            _ticking = false;
            EditorApplication.update -= Tick;
        }

        private void Tick()
        {
            if (_animation == null)
            {
                StopTicking();
                return;
            }

            double fps = _animation.FrameRate > 0 ? _animation.FrameRate : 30.0;
            long total = _animation.TotalFramesCount > 0 ? _animation.TotalFramesCount : 1;
            double elapsedFrames = (EditorApplication.timeSinceStartup - _startTime) * fps;

            if (_loop)
            {
                DrawIfChanged((int)(elapsedFrames % total));
                return;
            }

            int frame = (int)elapsedFrames;
            if (frame >= total - 1)
            {
                DrawIfChanged((int)(total - 1)); // dừng ở frame cuối (rest state)
                StopTicking();
                return;
            }

            DrawIfChanged(frame);
        }

        private void DrawIfChanged(int frame)
        {
            if (frame == _lastFrame)
                return;
            _lastFrame = frame;

            try
            {
                _animation.DrawOneFrame(frame);
                _image.MarkDirtyRepaint();
            }
            catch (Exception)
            {
                StopTicking();
            }
        }

        #endregion
    }
#else
    // Stub khi project chưa có com.gindemit.rlottie: giữ nguyên public API để FeatureHubWindow
    // compile, hiển thị khung trống (graceful degrade). Animation bật lại tự động sau khi
    // FeatureHubRuntimeDependency thêm rlottie vào manifest và Unity resolve xong.
    public sealed class LottieElement : VisualElement
    {
        public LottieElement(string json, int displaySize, LottiePlay mode = LottiePlay.Loop, uint renderSize = 0)
        {
            style.width = displaySize;
            style.height = displaySize;
            style.flexShrink = 0;
        }

        public void Play() { }

        public void PlayOnce() { }

        public void PlayLoop() { }
    }
#endif
}
