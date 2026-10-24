using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Ezg.Core.EditorTools
{
    /// <summary>
    ///     Keeps the "Recovering Scene Backups" dialog from ever showing on Editor startup.
    /// </summary>
    /// <remarks>
    ///     Unity writes <c>Temp/__Backupscenes</c> when entering Play Mode, restores the edit-mode scene from it on
    ///     exit, and then leaves the file there for as long as the scene stays dirty. If the Editor dies while that
    ///     file exists (crash, force-quit of a hung game), the next start asks to copy it into
    ///     <c>Assets/_Recovery/</c>. That check runs natively BEFORE any managed code loads, so it cannot be answered
    ///     from C#; the only fix is to make sure no backup is on disk when the Editor dies.
    ///     <list type="bullet">
    ///         <item>Entering Play Mode: Unity also loads the Play scenes FROM that backup, after the domain reload.
    ///         The guard therefore leaves it in place until <c>EnteredPlayMode</c> (scenes loaded); parking it during
    ///         the domain reload made every scene enter Play empty (0 roots, nothing ran).</item>
    ///         <item>Play Mode: the backup is parked under a name Unity does not look for, and moved back right
    ///         before Unity reads it on exit — scene restore after Play keeps working.</item>
    ///         <item>Edit Mode: the backup has already been consumed, so it is deleted.</item>
    ///         <item>Asset Import Worker processes are skipped: they share <c>Temp/</c> with the Editor, so their
    ///         "Edit Mode" cleanup used to delete the backup of a Play session in progress.</item>
    ///     </list>
    ///     Net effect after a crash: the same as answering "No" (unsaved scene edits are discarded, nothing lands in
    ///     <c>Assets/_Recovery/</c>). Define <c>EZG_KEEP_SCENE_BACKUPS</c> to opt out and get Unity's dialog back.
    /// </remarks>
    [InitializeOnLoad]
    internal static class SceneBackupGuard
    {
        #region Fields

        private const string BACKUP_DIR = "Temp/__Backupscenes";
        private const string PARKED_DIR = "Temp/__EzgParkedSceneBackups";

        #endregion

        #region Initialize

        static SceneBackupGuard()
        {
            // Asset Import Worker processes run [InitializeOnLoad] too, never play, and share the project's Temp/:
            // left in, every worker started during Play deleted the (parked) backup → the edit-mode scene came back
            // empty after Play and could be saved over the real scene.
            if (AssetDatabase.IsAssetImportWorkerProcess())
            {
                return;
            }

            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;

            // Domain reload on the way into Play Mode: Unity has just written the backup and is about to load the
            // Play scenes from it — do NOT touch it here. EnteredPlayMode (below) parks it once the scenes are loaded.
            if (!EditorApplication.isPlayingOrWillChangePlaymode)
            {
                // Edit Mode (Editor start or script reload): nothing needs a backup now.
                DeleteDirectory(BACKUP_DIR);
                DeleteDirectory(PARKED_DIR);
            }
        }

        #endregion

        #region Private

        /// <summary>
        ///     Moves the Play Mode backup out of the path Unity checks on startup.
        /// </summary>
        private static void Park()
        {
            if (!Directory.Exists(BACKUP_DIR))
            {
                return;
            }

            try
            {
                DeleteDirectory(PARKED_DIR);
                Directory.Move(BACKUP_DIR, PARKED_DIR);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[SceneBackupGuard] Could not park {BACKUP_DIR}: {e.Message}");
            }
        }

        /// <summary>
        ///     Puts the parked backup back so Unity can restore the edit-mode scene when Play Mode ends.
        /// </summary>
        private static void Unpark()
        {
            if (!Directory.Exists(PARKED_DIR))
            {
                return;
            }

            try
            {
                if (Directory.Exists(BACKUP_DIR))
                {
                    // Unity wrote a newer backup during Play: that one wins.
                    DeleteDirectory(PARKED_DIR);
                    return;
                }

                Directory.Move(PARKED_DIR, BACKUP_DIR);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[SceneBackupGuard] Could not restore {BACKUP_DIR}: {e.Message}");
            }
        }

        private static void DeleteDirectory(string path)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, true);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[SceneBackupGuard] Could not delete {path}: {e.Message}");
            }
        }

        #endregion

        #region Events

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            switch (state)
            {
                case PlayModeStateChange.EnteredPlayMode:
                    // The Play scenes have been loaded from the backup by now (with or without domain reload).
                    Park();
                    break;

                case PlayModeStateChange.ExitingPlayMode:
                    // Unity reads the backup right after this callback to restore the scene.
                    Unpark();
                    break;

                case PlayModeStateChange.EnteredEditMode:
                    // The edit-mode scene has been restored by now; Unity no longer needs the file.
                    DeleteDirectory(BACKUP_DIR);
                    break;
            }
        }

        #endregion
    }
}
