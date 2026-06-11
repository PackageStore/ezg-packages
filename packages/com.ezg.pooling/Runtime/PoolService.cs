using System;
using UnityEngine;

namespace Ezg.Package.Pooling
{
    /// <summary>
    ///     Abstract spawn/despawn facade over <see cref="PoolingManager" />. Implementations decide how prefabs
    ///     are resolved and where despawned objects are parked.
    /// </summary>
    public abstract class Pooling
    {
        #region Public Methods

        public abstract GameObject Spawn(string path, Transform parent = null);

        public abstract GameObject Spawn(GameObject prefab, Transform parent = null);

        public abstract void Despawn(GameObject prefab, float delay, Action callback = null, bool isBackParent = true);

        public abstract T Spawn<T>(string path) where T : Component;

        public abstract T Spawn<T>(T prefab) where T : Component;

        public abstract T Spawn<T>(GameObject prefab, Vector3 position) where T : Component;

        public abstract T Spawn<T>(T prefab, Transform parent) where T : Component;

        public abstract T Spawn<T>(T prefab, Vector3 position, Quaternion rotation, Transform parent = null)
            where T : Component;

        public abstract void Despawn<T>(T prefab, float delay, Action callback = null, bool isBackParent = true)
            where T : Component;

        public abstract void DespawnAll();

        #endregion
    }

    /// <summary>
    ///     Default <see cref="Pooling" /> implementation backed by <see cref="PoolingManager" /> for the pools and
    ///     <see cref="SpawnerManager" /> as the parking parent for despawned objects.
    /// </summary>
    public class DefaultPoolController : Pooling
    {
        #region Public Methods

        public override void Despawn(GameObject prefab, float delay, Action callback = null, bool isBackParent = true)
        {
            if (delay <= 0)
            {
                if (prefab == null) return;
                if (isBackParent) prefab.transform.SetParent(SpawnerManager.Instance.transform);
                prefab.SetActive(false);
                callback?.Invoke();
                return;
            }

            SpawnerManager.Instance.DelayMethod(delay, () =>
            {
                if (prefab == null) return;
                if (isBackParent) prefab.transform.SetParent(SpawnerManager.Instance.transform);
                prefab.SetActive(false);
                callback?.Invoke();
            });
        }

        public override void Despawn<T>(T prefab, float delay, Action callback = null, bool isBackParent = true)
        {
            if (delay <= 0)
            {
                if (prefab == null) return;

                prefab.gameObject.SetActive(false);
                if (isBackParent) prefab.transform.SetParent(SpawnerManager.Instance.transform);
                callback?.Invoke();
                return;
            }

            SpawnerManager.Instance.DelayMethod(delay, () =>
            {
                if (prefab == null) return;

                prefab.gameObject.SetActive(false);
                if (isBackParent) prefab.transform.SetParent(SpawnerManager.Instance.transform);
                callback?.Invoke();
            });
        }

        public override void DespawnAll()
        {
            // TODO: [Pooling] - Implement bulk despawn when a use case appears.
        }

        public override GameObject Spawn(string path, Transform parent = null)
        {
            var pref = PoolResources.Load<GameObject>(path);
            if (pref == null) return null;
            if (parent == null) parent = SpawnerManager.Instance.transform;
            return PoolingManager.Instance.Show(pref, Vector3.zero, Quaternion.identity, parent);
        }

        public override GameObject Spawn(GameObject prefab, Transform parent = null)
        {
            if (prefab == null) return null;
            if (parent == null) parent = SpawnerManager.Instance.transform;
            return PoolingManager.Instance.Show(prefab, Vector3.zero, Quaternion.identity, parent);
        }

        public override T Spawn<T>(string path)
        {
            return PoolingManager.Instance.Show<T>(PoolResources.Load<GameObject>(path), Vector3.zero, Quaternion.identity,
                SpawnerManager.Instance.transform);
        }

        public override T Spawn<T>(T prefab)
        {
            return PoolingManager.Instance.Show<T>(prefab.gameObject, Vector3.zero, Quaternion.identity,
                SpawnerManager.Instance.transform);
        }

        public override T Spawn<T>(GameObject prefab, Vector3 position)
        {
            return PoolingManager.Instance.Show<T>(prefab, position, Quaternion.identity,
                SpawnerManager.Instance.transform);
        }

        public override T Spawn<T>(T prefab, Transform parent)
        {
            if (prefab == null) return null;
            if (parent == null) parent = SpawnerManager.Instance.transform;
            return PoolingManager.Instance.Show<T>(prefab.gameObject, parent);
        }

        public override T Spawn<T>(T prefab, Vector3 position, Quaternion rotation, Transform parent = null)
        {
            if (parent == null) parent = SpawnerManager.Instance.transform;
            return PoolingManager.Instance.Show<T>(prefab.gameObject, position, rotation, parent);
        }

        #endregion
    }

    /// <summary>
    ///     Static facade for spawning and despawning pooled objects. Runs in parallel with direct
    ///     <see cref="PoolingManager" /> usage — both share the same underlying pools, so callers may use either
    ///     entry point interchangeably.
    /// </summary>
    public static class PoolService
    {
        #region Fields

        private static readonly Pooling Pooling = new DefaultPoolController();

        #endregion

        #region Public Methods

        /// <summary>
        ///     Releases all pooled objects. Reserved for a future bulk-despawn implementation.
        /// </summary>
        public static void ReleaseAll()
        {
            // Pooling.DespawnAll();
        }

        /// <summary>
        ///     Spawns a pooled instance from a prefab component.
        /// </summary>
        public static T Spawn<T>(T prefab) where T : Component
        {
            if (prefab == null) return null;

            return Pooling.Spawn(prefab);
        }

        /// <summary>
        ///     Loads a prefab of type <typeparamref name="T" /> by resource path and spawns a pooled instance.
        /// </summary>
        public static T Spawn<T>(string path) where T : Component
        {
            var prefab = PoolResources.Load<T>(path);
            if (prefab == null) return null;

            return Pooling.Spawn(prefab);
        }

        /// <summary>
        ///     Spawns a pooled instance from a prefab component under the given parent.
        /// </summary>
        public static T Spawn<T>(T prefab, Transform parent) where T : Component
        {
            if (prefab == null) return null;

            return Pooling.Spawn(prefab, parent);
        }

        /// <summary>
        ///     Spawns a pooled instance at the given position and rotation.
        /// </summary>
        public static T Spawn<T>(T prefab, Vector3 position, Quaternion rotation, Transform parent = null)
            where T : Component
        {
            if (prefab == null) return null;

            return Pooling.Spawn(prefab, position, rotation, parent);
        }

        /// <summary>
        ///     Spawns a pooled instance from a prefab GameObject at the given position, returning component <typeparamref name="T" />.
        /// </summary>
        public static T Spawn<T>(GameObject prefab, Vector3 position) where T : Component
        {
            if (prefab == null) return null;

            return Pooling.Spawn<T>(prefab, position);
        }

        /// <summary>
        ///     Loads a prefab by resource path and spawns a pooled GameObject under the given parent.
        /// </summary>
        public static GameObject SpawnGameObject(string path, Transform parent = null)
        {
            return Pooling.Spawn(path, parent);
        }

        /// <summary>
        ///     Spawns a pooled GameObject from a prefab under the given parent.
        /// </summary>
        public static GameObject Spawn(GameObject prefab, Transform parent = null)
        {
            if (prefab == null) return null;

            return Pooling.Spawn(prefab, parent);
        }

        /// <summary>
        ///     Despawns a GameObject back to its pool, optionally after a delay.
        /// </summary>
        public static void Despawn(GameObject t, float time = 0, Action callback = null, bool isBackParent = true)
        {
            Pooling.Despawn(t, time, callback, isBackParent);
        }

        /// <summary>
        ///     Fluent variant of <see cref="Despawn(GameObject,float,Action,bool)" /> that returns the GameObject.
        /// </summary>
        public static GameObject OnDespawn(this GameObject t, float time = 0, Action callback = null,
            bool isBackParent = true)
        {
            if (t == null) return t;

            Pooling.Despawn(t, time, callback, isBackParent);
            return t;
        }

        /// <summary>
        ///     Despawns a component's GameObject back to its pool, optionally after a delay.
        /// </summary>
        public static void Despawn<T>(T t, float time = 0, Action callback = null, bool isBackParent = true)
            where T : Component
        {
            if (t == null) return;

            Pooling.Despawn<T>(t, time, callback, isBackParent);
        }

        /// <summary>
        ///     Sets the local position of the GameObject and returns it for chaining.
        /// </summary>
        public static GameObject SetLocalPosition(this GameObject t, Vector3 pos)
        {
            if (t == null) return t;

            t.transform.localPosition = pos;
            return t;
        }

        /// <summary>
        ///     Sets the world position of the GameObject and returns it for chaining.
        /// </summary>
        public static GameObject SetPosition(this GameObject t, Vector3 pos)
        {
            if (t == null) return t;

            t.transform.position = pos;
            return t;
        }

        /// <summary>
        ///     Sets the parent of the GameObject and returns it for chaining.
        /// </summary>
        public static GameObject SetParent(this GameObject t, Transform parent)
        {
            if (t == null) return t;

            t.transform.SetParent(parent);
            return t;
        }

        /// <summary>
        ///     Sets the local euler angles of the GameObject and returns it for chaining.
        /// </summary>
        public static GameObject SetLocalEuler(this GameObject t, Vector3 euler)
        {
            if (t == null) return t;

            t.transform.localEulerAngles = euler;
            return t;
        }

        /// <summary>
        ///     Sets the world euler angles of the GameObject and returns it for chaining.
        /// </summary>
        public static GameObject SetEuler(this GameObject t, Vector3 euler)
        {
            if (t == null) return t;

            t.transform.eulerAngles = euler;
            return t;
        }

        #endregion
    }
}
