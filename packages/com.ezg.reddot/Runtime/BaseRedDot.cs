using TigerForge;
using UnityEngine;

namespace Ezg.Core.RedDot
{
    /// <summary>
    ///     Base class for red-dot indicators that listen to an event key and toggle a target object.
    ///     The event key is supplied by the consuming project (see the project-side subclass).
    /// </summary>
    public abstract class BaseRedDot : MonoBehaviour
    {
        #region Public Methods

        /// <summary>
        ///     Executes or recalculates the red-dot state.
        /// </summary>
        public virtual void Execute()
        {
        }

        #endregion

        #region Private Methods

        /// <summary>
        ///     Updates the active state of the target object based on the IsActive property.
        /// </summary>
        private void UpdateNotifState()
        {
            if (_notifObject != null)
                _notifObject.SetActive(IsActive);
        }

        #endregion

        #region Fields

        [SerializeField] private GameObject _notifObject;

        private bool _isActive;

        /// <summary>
        ///     Event key this red-dot listens on. Implemented by the project layer
        ///     (e.g. derived from a project-specific id enum).
        /// </summary>
        protected abstract string EventKey { get; }

        /// <summary>
        ///     Gets or sets the active state of the target object.
        /// </summary>
        public bool IsActive
        {
            get => _isActive;
            set
            {
                _isActive = value;
                UpdateNotifState();
            }
        }

        #endregion

        #region Initialize

        /// <summary>
        ///     Subscribes to the event when the script instance is loaded.
        /// </summary>
        protected virtual void Awake()
        {
            EventManager.StartListening(EventKey, Execute);
        }

        /// <summary>
        ///     Triggers execution when the object is enabled.
        /// </summary>
        protected virtual void OnEnable()
        {
            Execute();
        }

        /// <summary>
        ///     Initializes the state on start.
        /// </summary>
        public virtual void Start()
        {
            UpdateNotifState();
        }

        /// <summary>
        ///     Unsubscribes from the event when the script instance is destroyed.
        /// </summary>
        private void OnDestroy()
        {
            EventManager.StopListening(EventKey, Execute);
        }

        #endregion
    }
}
