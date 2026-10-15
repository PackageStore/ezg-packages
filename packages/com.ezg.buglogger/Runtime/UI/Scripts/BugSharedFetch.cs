using System;
using Cysharp.Threading.Tasks;

namespace Ezg.BugLogger.UI
{
    /// <summary>
    ///     Một lượt tải chạy chung cho mọi chỗ cần (prefetch lúc hiện nút nổi, form mở giữa chừng): đang chạy thì trả
    ///     lại lượt đó, xong rồi thì lần gọi sau chạy lượt mới. Await bao nhiêu chỗ cùng lúc cũng được — UniTask thường
    ///     (kể cả <c>Preserve()</c> khi còn chạy) chỉ cho MỘT chỗ chờ, chỗ thứ hai ném
    ///     "Already continuation registered".
    /// </summary>
    internal sealed class BugSharedFetch<T>
    {
        #region Fields

        private readonly Func<UniTask<T>> _factory;
        private UniTaskCompletionSource<T> _pending;

        #endregion

        #region Properties

        /// <summary>Đang có lượt tải chưa xong.</summary>
        public bool IsRunning => _pending != null;

        #endregion

        #region Initialize

        public BugSharedFetch(Func<UniTask<T>> factory)
        {
            _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        }

        #endregion

        #region Public Methods

        /// <summary>Lượt đang chạy, hoặc bắt đầu lượt mới. Kết quả await được ở nhiều chỗ.</summary>
        public UniTask<T> Run()
        {
            if (_pending != null) return _pending.Task;

            var source = new UniTaskCompletionSource<T>();
            _pending = source;
            RunAsync(source).Forget();
            return source.Task;
        }

        /// <summary>Bỏ lượt đang chạy (đổi phiên Play khi tắt Reload Domain). Chỗ đang chờ vẫn nhận kết quả cũ.</summary>
        public void Reset()
        {
            _pending = null;
        }

        #endregion

        #region Private Methods

        private async UniTaskVoid RunAsync(UniTaskCompletionSource<T> source)
        {
            T result;
            try
            {
                result = await _factory();
            }
            catch (Exception ex)
            {
                Release(source);
                source.TrySetException(ex);
                return;
            }

            // Nhả trước khi báo kết quả: chỗ chờ gọi Run() ngay trong continuation thì được lượt mới.
            Release(source);
            source.TrySetResult(result);
        }

        private void Release(UniTaskCompletionSource<T> source)
        {
            if (_pending == source) _pending = null;
        }

        #endregion
    }
}
