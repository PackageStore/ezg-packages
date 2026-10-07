using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Ezg.GameVfx
{
    /// <summary>
    /// Showcase driver for VFX built by the game-vfx skill: lays every slot out, replays one-shots on
    /// an interval, plays looping effects once, and moves projectile slots on a circle so their trail shows.
    /// </summary>
    public class GameVfxShowcaseLoop : MonoBehaviour
    {
        #region Fields

        private const float DEFAULT_INTERVAL = 1.6f;
        private const float ORBIT_SPEED = 2.4f;

        [Serializable]
        private class Slot
        {
            public ParticleSystem Prefab;
            public Vector3 Position;
            public float Interval = DEFAULT_INTERVAL;   // <= 0: play once (looping effects)
            public float OrbitRadius;                   // > 0: move on a circle (projectile trails)
        }

        [SerializeField] private Slot[] _slots = Array.Empty<Slot>();

        #endregion

        #region Initialize

        private void Start()
        {
            var token = this.GetCancellationTokenOnDestroy();
            foreach (var slot in _slots)
            {
                if (slot.Prefab == null)
                {
                    continue;
                }
                var fx = Instantiate(slot.Prefab, transform.position + slot.Position, slot.Prefab.transform.rotation, transform);
                if (slot.OrbitRadius > 0f)
                {
                    OrbitAsync(fx.transform, transform.position + slot.Position, slot.OrbitRadius, token).Forget();
                }
                if (slot.Interval > 0f)
                {
                    ReplayAsync(fx, slot.Interval, token).Forget();
                }
                else
                {
                    fx.Play(true);
                }
            }
        }

        #endregion

        #region Private

        private static async UniTaskVoid ReplayAsync(ParticleSystem fx, float interval, CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                fx.gameObject.SetActive(true);
                fx.Clear(true);
                fx.Play(true);
                await UniTask.Delay(TimeSpan.FromSeconds(interval), cancellationToken: token).SuppressCancellationThrow();
            }
        }

        private static async UniTaskVoid OrbitAsync(Transform target, Vector3 centre, float radius, CancellationToken token)
        {
            float angle = 0f;
            while (!token.IsCancellationRequested && target != null)
            {
                angle += ORBIT_SPEED * Time.deltaTime;
                target.position = centre + new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * radius;
                target.rotation = Quaternion.Euler(0f, 0f, angle * Mathf.Rad2Deg + 90f);
                await UniTask.Yield(PlayerLoopTiming.Update, token).SuppressCancellationThrow();
            }
        }

        #endregion
    }
}
