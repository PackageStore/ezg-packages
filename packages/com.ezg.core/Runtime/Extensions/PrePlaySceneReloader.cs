#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace Ezg.Core.Extensions
{
    [InitializeOnLoad]
    public static class PrePlaySceneReloader
    {
        #region Initialize

        /// <summary>
        ///     Static constructor subscribing to play mode state changes in the editor.
        /// </summary>
        static PrePlaySceneReloader()
        {
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        #endregion

        #region Event Handlers

        /// <summary>
        ///     Event handler called when the play mode state in the editor changes.
        ///     Reopens the currently active scene when exiting edit mode to ensure a clean state.
        /// </summary>
        /// <param name="state">The play mode state transition change details.</param>
        private static void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingEditMode)
            {
                var scene = SceneManager.GetActiveScene();

                EditorSceneManager.OpenScene(scene.path);
            }
        }

        #endregion
    }
#endif
}