using System.Collections.Generic;
using System.Linq;
using Ezg.Package.Singleton;

namespace Ezg.Core.Utils
{
    /// <summary>
    ///     Central registry that updates registered listeners every frame.
    /// </summary>
    public class UpdateManager : Singleton<UpdateManager>
    {
        #region Fields

        private static HashSet<IUpdateManager> _table = new();

        #endregion

        #region Methods

        /// <summary>
        ///     Registers an updater.
        /// </summary>
        /// <param name="obj">The updater to register.</param>
        public void Register(IUpdateManager obj)
        {
            if (obj != null)
                _table.Add(obj);
        }

        /// <summary>
        ///     Unregisters an updater.
        /// </summary>
        /// <param name="obj">The updater to remove.</param>
        public void Unregister(IUpdateManager obj)
        {
            if (obj != null)
                _table.Remove(obj);
        }

        /// <summary>
        ///     Clears all registered updaters when the manager is disabled.
        /// </summary>
        private void OnDisable()
        {
            _table = new HashSet<IUpdateManager>();
        }

        /// <summary>
        ///     Invokes update on all registered listeners.
        /// </summary>
        private void Update()
        {
            foreach (var item in _table.ToArray())
                item.UpdateMe();
        }

        #endregion
    }
}