using System;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Ezg.Package.Pooling
{
    /// <summary>
    ///     Central holder for objects spawned through <see cref="PoolService" />. When an object is despawned it
    ///     is re-parented here and deactivated, which triggers its <see cref="PoolingComponent" /> to return it
    ///     to the pool. Also provides delayed and immediate destruction helpers.
    /// </summary>
    /// <remarks>
    ///     Backed by the project <see cref="Singleton{T}" />, so this manager is persistent
    ///     (<c>DontDestroyOnLoad</c>) and the objects parented under it survive scene changes.
    /// </remarks>
    public class SpawnerManager : Singleton<SpawnerManager>
    {
        #region Public Methods

        /// <summary>
        ///     Immediately destroys the given GameObject.
        /// </summary>
        /// <param name="obj">The GameObject to destroy.</param>
        public void DestroyObject(GameObject obj)
        {
            if (obj != null) Destroy(obj);
        }

        /// <summary>
        ///     Invokes a callback after a delay, measured in scaled seconds. Uses UniTask instead of coroutines.
        /// </summary>
        /// <param name="seconds">The delay in seconds before invoking the callback. Values &lt;= 0 invoke immediately.</param>
        /// <param name="callback">The action to invoke after the delay.</param>
        public void DelayMethod(float seconds, Action callback)
        {
            DelayMethodAsync(seconds, callback).Forget();
        }

        #endregion

        #region Private Methods

        /// <summary>
        ///     Awaits the given delay then invokes the callback. Cancels safely if this manager is destroyed.
        /// </summary>
        /// <param name="seconds">The delay in seconds.</param>
        /// <param name="callback">The action to invoke.</param>
        private async UniTask DelayMethodAsync(float seconds, Action callback)
        {
            if (seconds > 0)
                await UniTask.Delay(TimeSpan.FromSeconds(seconds),
                    cancellationToken: this.GetCancellationTokenOnDestroy());

            callback?.Invoke();
        }

        #endregion
    }
}
