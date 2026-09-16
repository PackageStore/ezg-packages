using System;
using System.Threading.Tasks;

namespace Ezg.UserSegment.Engine
{
    /// <summary>
    ///     Ghi file ở một worker nền, luôn ghi bản mới nhất, bỏ bản cũ chưa kịp ghi — §4.5. Pause / quit gọi
    ///     <see cref="FlushSync" /> để ghi đồng bộ.
    /// </summary>
    public sealed class LatestWinsWriter
    {
        private readonly IStateStorage _storage;
        private readonly ISegLogger _log;
        private readonly object _lock = new object();
        private string _pendingKey;
        private string _pendingContent;
        private bool _running;

        public LatestWinsWriter(IStateStorage storage, ISegLogger log)
        {
            _storage = storage;
            _log = log;
        }

        public void Schedule(string key, string content)
        {
            lock (_lock)
            {
                _pendingKey = key;
                _pendingContent = content;
                if (_running) return;
                _running = true;
            }

            Task.Run(Worker);
        }

        private void Worker()
        {
            while (true)
            {
                string key, content;
                lock (_lock)
                {
                    if (_pendingContent == null)
                    {
                        _running = false;
                        return;
                    }

                    key = _pendingKey;
                    content = _pendingContent;
                    _pendingContent = null;
                }

                try
                {
                    _storage.WriteAtomic(key, content);
                }
                catch (Exception e)
                {
                    _log?.Log(LogLevel.Error, "[UserSegment] persist failed: " + e.Message);
                }
            }
        }

        /// <summary>Ghi đồng bộ ngay trên thread gọi (bỏ bản đang chờ vì content này mới hơn).</summary>
        public void FlushSync(string key, string content)
        {
            lock (_lock)
            {
                _pendingContent = null;
            }

            try
            {
                _storage.WriteAtomic(key, content);
            }
            catch (Exception e)
            {
                _log?.Log(LogLevel.Error, "[UserSegment] persist(sync) failed: " + e.Message);
            }
        }
    }
}
