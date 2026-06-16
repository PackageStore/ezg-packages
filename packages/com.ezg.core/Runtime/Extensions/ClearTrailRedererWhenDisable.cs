using System.Linq;
using UnityEngine;

namespace Ezg.Core.Extensions
{
    public class ClearTrailRedererWhenDisable : MonoBehaviour
    {
        #region Fields

        public TrailRenderer[] _trails;

        #endregion

        #region Initialize

        /// <summary>
        ///     Filters out any null elements in the trails array during initialization.
        /// </summary>
        private void Awake()
        {
            if (_trails != null)
                _trails = _trails?.Where(x => x != null).ToArray();
        }

        /// <summary>
        ///     Clears all registered trail renderers when the GameObject becomes disabled or inactive.
        /// </summary>
        private void OnDisable()
        {
            foreach (var t in _trails) t.Clear();
        }

        #endregion
    }
}