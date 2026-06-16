using Cinemachine;
#if ODIN_INSPECTOR
using Sirenix.OdinInspector;
#endif
using UnityEngine;

namespace Ezg.Core.UI
{
    /// <summary>
    ///     Lock camera cho thư viện Cinemachine
    /// </summary>
    [ExecuteInEditMode]
    [SaveDuringPlay]
    [AddComponentMenu("")]
    public class CinemachineLockCamera : CinemachineExtension
    {
        #region Private Methods

        /// <summary>
        ///     Callback executed after the pipeline stage is completed. Applies locking constraints to coordinates.
        /// </summary>
        /// <param name="vcam">The virtual camera base instance.</param>
        /// <param name="stage">The current pipeline stage.</param>
        /// <param name="state">The mutable camera state.</param>
        /// <param name="deltaTime">The delta time for camera updates.</param>
        protected override void PostPipelineStageCallback(
            CinemachineVirtualCameraBase vcam,
            CinemachineCore.Stage stage, ref CameraState state, float deltaTime)
        {
            if (stage == CinemachineCore.Stage.Body)
            {
                var pos = state.RawPosition;
                if (IsLock_X) pos.x = m_XPosition;
                if (IsLock_Y) pos.y = m_YPosition;
                if (IsLock_Z) pos.z = m_ZPosition;
                state.RawPosition = pos;
            }
        }

        #endregion

        #region Fields

        public bool IsLock_X;

#if ODIN_INSPECTOR
        [ShowIf("IsLock_X")]
#endif
        public float m_XPosition;

        public bool IsLock_Y;

#if ODIN_INSPECTOR
        [ShowIf("IsLock_Y")]
#endif
        public float m_YPosition;

        public bool IsLock_Z;

#if ODIN_INSPECTOR
        [ShowIf("IsLock_Z")]
#endif
        public float m_ZPosition;

        #endregion
    }
}