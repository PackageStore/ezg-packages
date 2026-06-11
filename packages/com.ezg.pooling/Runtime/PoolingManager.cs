using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Ezg.Package.Pooling
{
    /// <summary>
    ///     Manages all object pooling processes in the game.
    /// </summary>
    public class PoolingManager : Singleton<PoolingManager>
    {
        #region Public Methods

        /// <summary>
        ///     Pre-initializes a set number of pooled GameObjects.
        /// </summary>
        /// <param name="obj">The prefab to instantiate.</param>
        /// <param name="number">The quantity to pre-instantiate.</param>
        /// <param name="quater">The initial rotation.</param>
        /// <param name="parent">Optional parent transform.</param>
        public void PreInit(GameObject obj, int number, Quaternion quater, Transform parent = null)
        {
            _pools.Add(obj.name, new Queue<GameObject>());

            for (var i = 0; i < number; i++)
            {
                var objInstantiate = Instantiate(obj, _defaultPos, quater, parent);
                objInstantiate.name = obj.name;
                objInstantiate.SetActive(false);
                objInstantiate.transform.rotation = quater;
                if (parent != null)
                    objInstantiate.transform.SetParent(parent);

                objInstantiate.AddComponent<PoolingComponent>().ReturnPool += () => ReturnPool(obj, objInstantiate);
                ReturnPool(obj, objInstantiate, true);
            }
        }

        #endregion

        #region Private Methods

        /// <summary>
        ///     Instantiates and initializes a new GameObject for the pool.
        /// </summary>
        /// <param name="obj">The prefab to instantiate.</param>
        /// <param name="pos">Initial position.</param>
        /// <param name="quater">Initial rotation.</param>
        /// <param name="parent">Optional parent transform.</param>
        /// <param name="isShowObject">If true, activates the GameObject immediately.</param>
        /// <returns>The newly created GameObject.</returns>
        private GameObject InitData(GameObject obj, Vector3 pos, Quaternion quater, Transform parent = null,
            bool isShowObject = true)
        {
            var objInstantiate = Instantiate(obj, pos, quater, parent);
            objInstantiate.name = obj.name;
            objInstantiate.SetActive(isShowObject);
            objInstantiate.transform.position = pos;
            objInstantiate.transform.rotation = quater;
            if (parent != null)
                objInstantiate.transform.SetParent(parent);
            var poolingData = objInstantiate.GetComponent<PoolingComponent>();

            if (poolingData == null)
            {
                poolingData = objInstantiate.AddComponent<PoolingComponent>();
                poolingData.ReturnPool += () => ReturnPool(obj, objInstantiate);
                poolingData.Remove += () => { RemoveObject(obj); };
            }
            else
            {
                poolingData.ReturnPool = () => ReturnPool(obj, objInstantiate);
                poolingData.Remove += () => { RemoveObject(obj); };
            }

            return objInstantiate;
        }

        #endregion

        #region Private Methods

        /// <summary>
        ///     Instantiates and initializes a new generic model and component for the pool.
        /// </summary>
        /// <typeparam name="T">The component type associated with the pooled object.</typeparam>
        /// <param name="obj">The prefab GameObject.</param>
        /// <param name="pos">The target position.</param>
        /// <param name="quater">The target rotation.</param>
        /// <param name="parent">Optional parent transform.</param>
        /// <param name="isShowObject">If true, activates the GameObject immediately.</param>
        /// <returns>The newly created <see cref="PoolingDataModel{T}" /> wrapper.</returns>
        private PoolingDataModel<T> InitDataWithModel<T>(GameObject obj, Vector3 pos, Quaternion quater,
            Transform parent = null, bool isShowObject = true)
        {
            var objInstantiate = Instantiate(obj, pos, quater, parent);
            objInstantiate.name = obj.name;
            objInstantiate.SetActive(isShowObject);
            objInstantiate.transform.position = pos;
            objInstantiate.transform.rotation = quater;
            if (parent != null)
                objInstantiate.transform.SetParent(parent);

            var controller = objInstantiate.GetComponent<T>();
            var poolingData = objInstantiate.GetComponent<PoolingComponent>();

            var result = new PoolingDataModel<T>
            {
                GObject = objInstantiate,
                Controller = controller,
                IsNewCreate = true
            };


            if (poolingData == null)
            {
                poolingData = objInstantiate.AddComponent<PoolingComponent>();
                poolingData.ReturnPool += () => ReturnPool(obj, result);
                poolingData.Remove += () => { RemoveObject(obj); };
            }
            else
            {
                poolingData.ReturnPool = () => ReturnPool(obj, result);
                poolingData.Remove += () => { RemoveObject(obj); };
            }

            return result;
        }

        #endregion

        #region Fields

        /// <summary>
        ///     If true, removes the parent transform of objects when returning them to the pool.
        /// </summary>
        private readonly bool _removeParentWhenReturnPool = false;

        /// <summary>
        ///     Determines whether objects are allowed to return to their pool.
        /// </summary>
        private bool _allowReturnPool = true;

        /// <summary>
        ///     Stores the pooled GameObjects mapped by their prefab name.
        /// </summary>
        private readonly Dictionary<string, Queue<GameObject>> _pools = new();

        /// <summary>
        ///     Stores the pooled controllers and their associated models mapped by their prefab name.
        /// </summary>
        private readonly Dictionary<string, Queue<object>> _poolsT = new();

        /// <summary>
        ///     Default positioning for instantiated inactive pooled objects.
        /// </summary>
        private readonly Vector3 _defaultPos = new(-999999, -999999, 0);

        #endregion

        #region Public Methods

        /// <summary>
        ///     Retrieves or instantiates a pooled GameObject, positioning and rotating it accordingly.
        /// </summary>
        /// <param name="obj">The prefab GameObject.</param>
        /// <param name="pos">The target spawn position.</param>
        /// <param name="quater">The target spawn rotation.</param>
        /// <param name="parent">Optional parent transform.</param>
        /// <param name="isShowObject">If true, activates the GameObject immediately.</param>
        /// <returns>The pooled or newly instantiated GameObject.</returns>
        public GameObject Show(GameObject obj, Vector3 pos, Quaternion quater, Transform parent = null,
            bool isShowObject = true)
        {
            if (_pools.GetValueOrDefault(obj.name) == null) _pools.Add(obj.name, new Queue<GameObject>());

            if (_pools[obj.name].Any())
            {
                var objInPool = _pools[obj.name].Dequeue();
                if (objInPool != null)
                {
                    objInPool.transform.position = pos;
                    objInPool.transform.rotation = quater;
                    objInPool.SetActive(true);
                    if (parent != null)
                        objInPool.transform.SetParent(parent);
                    return objInPool;
                }
            }

            return InitData(obj, pos, quater, parent, isShowObject);
        }

        /// <summary>
        ///     Retrieves or instantiates a pooled GameObject with identity rotation.
        /// </summary>
        /// <param name="obj">The prefab GameObject.</param>
        /// <param name="pos">The target spawn position.</param>
        /// <param name="parent">Optional parent transform.</param>
        /// <param name="isShowObject">If true, activates the GameObject immediately.</param>
        /// <returns>The pooled or newly instantiated GameObject.</returns>
        public GameObject Show(GameObject obj, Vector3 pos, Transform parent = null, bool isShowObject = true)
        {
            return Show(obj, pos, Quaternion.identity, parent, isShowObject);
        }

        /// <summary>
        ///     Returns a GameObject back to its pool.
        /// </summary>
        /// <param name="mainObject">The original prefab GameObject.</param>
        /// <param name="obj">The instance GameObject being returned.</param>
        /// <param name="forceReturn">If true, bypasses the return checks.</param>
        public void ReturnPool(GameObject mainObject, GameObject obj, bool forceReturn = false)
        {
            if (_allowReturnPool || forceReturn) _pools[mainObject.name]?.Enqueue(obj);

            if (_removeParentWhenReturnPool) obj.transform.parent = null;
        }

        /// <summary>
        ///     Pre-initializes a set number of generic pooled components.
        /// </summary>
        /// <typeparam name="T">The component type associated with the pooled object.</typeparam>
        /// <param name="obj">The prefab GameObject.</param>
        /// <param name="number">The quantity to pre-instantiate.</param>
        /// <param name="quater">The initial rotation.</param>
        /// <param name="parent">Optional parent transform.</param>
        public void PreInit<T>(GameObject obj, int number, Quaternion quater, Transform parent = null)
        {
            if (_poolsT.ContainsKey(obj.name)) return;
            _poolsT.Add(obj.name, new Queue<object>());

            for (var i = 0; i < number; i++)
            {
                var objInstantiate = Instantiate(obj, _defaultPos, quater, parent);
                objInstantiate.name = obj.name;
                objInstantiate.SetActive(false);
                objInstantiate.transform.rotation = quater;
                if (parent != null)
                    objInstantiate.transform.SetParent(parent);

                var controller = objInstantiate.GetComponent<T>();

                var result = new PoolingDataModel<T>
                {
                    GObject = objInstantiate,
                    Controller = controller
                };

                var poolingData = objInstantiate.AddComponent<PoolingComponent>();
                poolingData.ReturnPool += () => ReturnPool(obj, result);
                poolingData.Remove += () => { RemoveObject(obj); };

                ReturnPool(obj, result, true);
            }
        }

        /// <summary>
        ///     Instantiates and initializes a new component instance of type <typeparamref name="T" /> for the pool.
        /// </summary>
        /// <typeparam name="T">The component type associated with the pooled object.</typeparam>
        /// <param name="obj">The prefab GameObject.</param>
        /// <param name="pos">The target position.</param>
        /// <param name="quater">The target rotation.</param>
        /// <param name="parent">Optional parent transform.</param>
        /// <param name="isShowObject">If true, activates the GameObject immediately.</param>
        /// <param name="isUI">If true, treats the component as UI and sets its local position on the RectTransform.</param>
        /// <returns>The component instance associated with the newly created pooled object.</returns>
        private T InitData<T>(GameObject obj, Vector3 pos, Quaternion quater, Transform parent = null,
            bool isShowObject = true, bool isUI = false)
        {
            var objInstantiate = Instantiate(obj, pos, quater, parent);
            objInstantiate.name = obj.name;
            objInstantiate.SetActive(isShowObject);
            if (isUI)
                objInstantiate.GetComponent<RectTransform>().localPosition = pos;
            else
                objInstantiate.transform.position = pos;

            objInstantiate.transform.rotation = quater;
            if (parent != null)
                objInstantiate.transform.SetParent(parent);

            var controller = objInstantiate.GetComponent<T>();

            var result = new PoolingDataModel<T>
            {
                GObject = objInstantiate,
                Controller = controller
            };

            var poolingData = objInstantiate.AddComponent<PoolingComponent>();
            poolingData.ReturnPool += () => ReturnPool(obj, result);
            poolingData.Remove += () => { RemoveObject(obj); };

            return controller;
        }

        #endregion

        #region Public Methods

        /// <summary>
        ///     Retrieves or instantiates a pooled component instance, positioning and rotating it accordingly.
        /// </summary>
        /// <typeparam name="T">The component type associated with the pooled object.</typeparam>
        /// <param name="obj">The prefab GameObject.</param>
        /// <param name="pos">The target position.</param>
        /// <param name="quater">The target rotation.</param>
        /// <param name="parent">Optional parent transform.</param>
        /// <param name="isShowObject">If true, activates the GameObject immediately.</param>
        /// <param name="isUI">If true, treats the component as UI and sets its local position on the RectTransform.</param>
        /// <returns>The pooled or newly instantiated component instance.</returns>
        public T Show<T>(GameObject obj, Vector3 pos, Quaternion quater, Transform parent = null,
            bool isShowObject = true, bool isUI = false)
        {
            if (_poolsT.GetValueOrDefault(obj.name) == null) _poolsT.Add(obj.name, new Queue<object>());

            if (_poolsT[obj.name].Any())
            {
                var objInPool = (PoolingDataModel<T>)_poolsT[obj.name].Dequeue();
                objInPool.GObject.SetActive(isShowObject);
                if (isUI)
                    objInPool.GObject.GetComponent<RectTransform>().localPosition = pos;
                else
                    objInPool.GObject.transform.position = pos;

                objInPool.GObject.transform.rotation = quater;
                if (parent != null)
                    objInPool.GObject.transform.SetParent(parent);
                objInPool.IsNewCreate = false;
                return objInPool.Controller;
            }

            return InitData<T>(obj, pos, Quaternion.identity, parent, isShowObject, isUI);
        }

        /// <summary>
        ///     Retrieves or instantiates a pooled component instance with identity rotation.
        /// </summary>
        /// <typeparam name="T">The component type associated with the pooled object.</typeparam>
        /// <param name="obj">The prefab GameObject.</param>
        /// <param name="pos">The target position.</param>
        /// <param name="parent">Optional parent transform.</param>
        /// <param name="isShowObject">If true, activates the GameObject immediately.</param>
        /// <param name="isUI">If true, treats the component as UI and sets its local position on the RectTransform.</param>
        /// <returns>The pooled or newly instantiated component instance.</returns>
        public T Show<T>(GameObject obj, Vector3 pos, Transform parent = null, bool isShowObject = true,
            bool isUI = false)
        {
            return Show<T>(obj, pos, Quaternion.identity, parent, isShowObject, isUI);
        }

        /// <summary>
        ///     Retrieves or instantiates a pooled component instance at zero position with identity rotation.
        /// </summary>
        /// <typeparam name="T">The component type associated with the pooled object.</typeparam>
        /// <param name="obj">The prefab GameObject.</param>
        /// <param name="parent">Optional parent transform.</param>
        /// <param name="isShowObject">If true, activates the GameObject immediately.</param>
        /// <param name="isUI">If true, treats the component as UI.</param>
        /// <returns>The pooled or newly instantiated component instance.</returns>
        public T Show<T>(GameObject obj, Transform parent = null, bool isShowObject = true, bool isUI = false)
        {
            return Show<T>(obj, Vector3.zero, Quaternion.identity, parent, isShowObject, isUI);
        }

        #endregion

        #region Public Methods

        /// <summary>
        ///     Retrieves a pooled object and its generic controller model.
        /// </summary>
        /// <typeparam name="T">The component type associated with the pooled object.</typeparam>
        /// <param name="obj">The prefab GameObject.</param>
        /// <param name="pos">The target position.</param>
        /// <param name="quater">The target rotation.</param>
        /// <param name="parent">Optional parent transform.</param>
        /// <param name="isShowObject">If true, activates the GameObject immediately.</param>
        /// <returns>The <see cref="PoolingDataModel{T}" /> containing the GameObject and controller component.</returns>
        public PoolingDataModel<T> ShowWithModel<T>(GameObject obj, Vector3 pos, Quaternion quater,
            Transform parent = null, bool isShowObject = true)
        {
            if (_poolsT.GetValueOrDefault(obj.name) == null) _poolsT.Add(obj.name, new Queue<object>());

            if (_poolsT[obj.name].Any())
            {
                var objInPool = (PoolingDataModel<T>)_poolsT[obj.name].Dequeue();
                objInPool.GObject.SetActive(isShowObject);
                objInPool.GObject.transform.position = pos;
                objInPool.GObject.transform.rotation = quater;
                if (parent != null)
                    objInPool.GObject.transform.SetParent(parent);
                objInPool.IsNewCreate = false;
                return objInPool;
            }

            return InitDataWithModel<T>(obj, pos, Quaternion.identity, parent, isShowObject);
        }

        /// <summary>
        ///     Retrieves a pooled object and its generic controller model with identity rotation.
        /// </summary>
        /// <typeparam name="T">The component type associated with the pooled object.</typeparam>
        /// <param name="obj">The prefab GameObject.</param>
        /// <param name="pos">The target position.</param>
        /// <param name="parent">Optional parent transform.</param>
        /// <param name="isShowObject">If true, activates the GameObject immediately.</param>
        /// <returns>The <see cref="PoolingDataModel{T}" /> containing the GameObject and controller component.</returns>
        public PoolingDataModel<T> ShowWithModel<T>(GameObject obj, Vector3 pos, Transform parent = null,
            bool isShowObject = true)
        {
            return ShowWithModel<T>(obj, pos, Quaternion.identity, parent, isShowObject);
        }

        /// <summary>
        ///     Returns a generic pooled component structure back to its pool.
        /// </summary>
        /// <typeparam name="T">The component type associated with the pooled object.</typeparam>
        /// <param name="mainObject">The original prefab GameObject.</param>
        /// <param name="data">The generic data model being returned.</param>
        /// <param name="forceReturn">If true, bypasses return checks.</param>
        public void ReturnPool<T>(GameObject mainObject, PoolingDataModel<T> data, bool forceReturn = false)
        {
            if (_allowReturnPool || forceReturn)
            {
                // Create pool if it doesn't exist
                if (!_poolsT.ContainsKey(mainObject.name))
                    _poolsT[mainObject.name] = new Queue<object>();

                _poolsT[mainObject.name].Enqueue(data);
            }

            if (_removeParentWhenReturnPool)
                data.GObject.transform.parent = null;
        }

        /// <summary>
        ///     Clears all cached generic pooled objects of type <typeparamref name="T" /> for a specific prefab.
        /// </summary>
        /// <typeparam name="T">The component type associated with the pooled object.</typeparam>
        /// <param name="obj">The prefab GameObject.</param>
        public void ClearPool<T>(GameObject obj)
        {
            if (_poolsT.ContainsKey(obj.name))
                _poolsT.Remove(obj.name);
        }

        /// <summary>
        ///     Clears all cached GameObjects and generic pooled objects for a specific prefab.
        /// </summary>
        /// <param name="obj">The prefab GameObject.</param>
        public void ClearPool(GameObject obj)
        {
            if (_pools.ContainsKey(obj.name))
                _pools.Remove(obj.name);
            if (_poolsT.ContainsKey(obj.name))
                _poolsT.Remove(obj.name);
        }

        /// <summary>
        ///     Clears all cached GameObjects and generic pooled objects by prefab name.
        /// </summary>
        /// <param name="objName">The name of the prefab.</param>
        public void ClearPool(string objName)
        {
            if (_pools.ContainsKey(objName))
                _pools.Remove(objName);
            if (_poolsT.ContainsKey(objName))
                _poolsT.Remove(objName);
        }

        /// <summary>
        ///     Removes a prefab's pool completely from the active pools dictionary.
        /// </summary>
        /// <param name="obj">The prefab GameObject to remove.</param>
        public void RemoveObject(GameObject obj)
        {
            // Remove entire pool of this prefab
            if (_pools.ContainsKey(obj.name)) _pools.Remove(obj.name);

            if (_poolsT.ContainsKey(obj.name)) _poolsT.Remove(obj.name);
        }

        #endregion

        #region Public Methods

        /// <summary>
        ///     Instantiates a new component instance of type <typeparamref name="T" /> directly without pooling.
        /// </summary>
        /// <typeparam name="T">The component type to retrieve from the instantiated object.</typeparam>
        /// <param name="obj">The prefab GameObject.</param>
        /// <param name="parent">Optional parent transform.</param>
        /// <param name="isShowObject">If true, activates the GameObject immediately.</param>
        /// <returns>The component instance of type <typeparamref name="T" />.</returns>
        public static T Instantiate<T>(GameObject obj, Transform parent = null, bool isShowObject = true)
        {
            var objInstantiate = Instantiate(obj, Vector3.zero, Quaternion.identity, parent);
            objInstantiate.name = obj.name;
            objInstantiate.SetActive(isShowObject);
            if (parent != null)
                objInstantiate.transform.SetParent(parent);
            return objInstantiate.GetComponent<T>();
        }

        /// <summary>
        ///     Instantiates a new component instance of type <typeparamref name="T" /> directly at a given position without
        ///     pooling.
        /// </summary>
        /// <typeparam name="T">The component type to retrieve from the instantiated object.</typeparam>
        /// <param name="obj">The prefab GameObject.</param>
        /// <param name="pos">The target position.</param>
        /// <param name="parent">Optional parent transform.</param>
        /// <param name="isShowObject">If true, activates the GameObject immediately.</param>
        /// <param name="isUI">If true, treats the component as UI and sets its local position on the RectTransform.</param>
        /// <returns>The component instance of type <typeparamref name="T" />.</returns>
        public static T Instantiate<T>(GameObject obj, Vector3 pos, Transform parent = null, bool isShowObject = true,
            bool isUI = false)
        {
            var objInstantiate = Instantiate(obj, pos, Quaternion.identity, parent);
            objInstantiate.name = obj.name;
            objInstantiate.SetActive(isShowObject);
            if (parent != null)
                objInstantiate.transform.SetParent(parent);

            if (isUI) objInstantiate.GetComponent<RectTransform>().localPosition = pos;

            return objInstantiate.GetComponent<T>();
        }

        /// <summary>
        ///     Clears all pooled objects from memory immediately.
        /// </summary>
        public void ClearCache()
        {
            _allowReturnPool = false;
            _pools.Clear();
            _poolsT.Clear();
        }

        /// <summary>
        ///     Enables objects to return to their pools, typically called after a scene transition finishes.
        /// </summary>
        public void EnableReturnPool()
        {
            _allowReturnPool = true;
        }

        #endregion

        #region Private Methods

        /// <summary>
        ///     Automatically clears the pools upon scene loading.
        /// </summary>
        /// <param name="scene">The loaded scene details.</param>
        /// <param name="mode">The load scene mode.</param>
        private void OnLoadScene(Scene scene, LoadSceneMode mode)
        {
            _pools.Clear();
            _poolsT.Clear();
        }

        /// <summary>
        ///     Disables pool returns when the application quits to prevent memory leaks or errors.
        /// </summary>
        private void OnApplicationQuit()
        {
            _allowReturnPool = false;
        }

        #endregion
    }
}