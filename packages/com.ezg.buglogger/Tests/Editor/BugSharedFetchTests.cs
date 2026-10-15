using System;
using Cysharp.Threading.Tasks;
using Ezg.BugLogger.UI;
using NUnit.Framework;

namespace Ezg.BugLogger.Tests
{
    /// <summary>
    ///     Lượt tải lựa chọn form dùng chung: prefetch (nút nổi bật lại) và form mở giữa chừng cùng chờ một request mà
    ///     không ném "Already continuation registered".
    /// </summary>
    public class BugSharedFetchTests
    {
        private int _calls;
        private UniTaskCompletionSource<int> _gate;

        [SetUp]
        public void SetUp()
        {
            _calls = 0;
            _gate = new UniTaskCompletionSource<int>();
        }

        [Test]
        public void Run_WhilePending_ManyWaitersShareOneRequest()
        {
            var fetch = new BugSharedFetch<int>(Factory);
            var first = 0;
            var second = 0;
            var third = 0;

            // Prefetch kiểu fire-and-forget + hai chỗ await thật — đúng chuỗi gây lỗi ở 0.2.3.
            fetch.Run().Forget();
            Assert.DoesNotThrow(() => fetch.Run().ContinueWith(v => first = v).Forget());
            Assert.DoesNotThrow(() => fetch.Run().ContinueWith(v => second = v).Forget());
            Assert.DoesNotThrow(() => fetch.Run().AttachExternalCancellation(default).ContinueWith(v => third = v).Forget());
            Assert.IsTrue(fetch.IsRunning);

            _gate.TrySetResult(7);

            Assert.AreEqual(1, _calls);
            Assert.AreEqual(7, first);
            Assert.AreEqual(7, second);
            Assert.AreEqual(7, third);
            Assert.IsFalse(fetch.IsRunning);
        }

        [Test]
        public void Run_AfterCompletion_StartsNewRequest()
        {
            var fetch = new BugSharedFetch<int>(Factory);
            fetch.Run().Forget();
            _gate.TrySetResult(1);

            _gate = new UniTaskCompletionSource<int>();
            var value = 0;
            fetch.Run().ContinueWith(v => value = v).Forget();
            _gate.TrySetResult(2);

            Assert.AreEqual(2, _calls);
            Assert.AreEqual(2, value);
        }

        [Test]
        public void Run_FactoryFails_EveryWaiterSeesErrorAndNextRunRetries()
        {
            var fetch = new BugSharedFetch<int>(Factory);
            Exception first = null;
            Exception second = null;
            Observe(fetch.Run(), e => first = e);
            Observe(fetch.Run(), e => second = e);

            _gate.TrySetException(new InvalidOperationException("boom"));

            Assert.IsInstanceOf<InvalidOperationException>(first);
            Assert.IsInstanceOf<InvalidOperationException>(second);
            Assert.IsFalse(fetch.IsRunning);

            _gate = new UniTaskCompletionSource<int>();
            fetch.Run().Forget();
            Assert.AreEqual(2, _calls);
            _gate.TrySetResult(0);
        }

        [Test]
        public void Run_FactoryCompletesSynchronously_IsNotLeftRunning()
        {
            var fetch = new BugSharedFetch<int>(() =>
            {
                _calls++;
                return UniTask.FromResult(3);
            });

            var value = 0;
            fetch.Run().ContinueWith(v => value = v).Forget();

            Assert.AreEqual(3, value);
            Assert.IsFalse(fetch.IsRunning);
        }

        private UniTask<int> Factory()
        {
            _calls++;
            return _gate.Task;
        }

        private static void Observe(UniTask<int> task, Action<Exception> onError)
        {
            ObserveAsync(task, onError).Forget();
        }

        private static async UniTaskVoid ObserveAsync(UniTask<int> task, Action<Exception> onError)
        {
            try
            {
                await task;
            }
            catch (Exception ex)
            {
                onError(ex);
            }
        }
    }
}
