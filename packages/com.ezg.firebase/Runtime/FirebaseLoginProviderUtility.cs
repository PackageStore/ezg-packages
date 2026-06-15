using System;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using Firebase.Auth;
using UnityEngine;
using UnityEngine.Events;

namespace Ezg.Core.Firebase
{
    /// <summary>
    ///     Utility class providing shared helper methods for Firebase authentication providers.
    /// </summary>
    internal static class FirebaseLoginProviderUtility
    {
        #region Public Methods

        /// <summary>
        ///     Handles the completion result of a Firebase sign-in task and invokes corresponding success or failure callbacks.
        /// </summary>
        /// <param name="task">The completed Firebase Auth task.</param>
        /// <param name="onSuccess">Callback executed upon successful login.</param>
        /// <param name="onFail">Callback executed if login is canceled or fails.</param>
        /// <param name="completionSource">The UniTaskCompletionSource to set the result on.</param>
        /// <param name="canceledLog">Log message printed if task is canceled.</param>
        /// <param name="failedLog">Log message printed if task is faulted.</param>
        /// <param name="successLog">Log message printed if task completes successfully.</param>
        internal static void HandleFirebaseLoginResult(
            Task<FirebaseUser> task,
            UnityAction onSuccess,
            UnityAction onFail,
            UniTaskCompletionSource<bool> completionSource,
            string canceledLog,
            string failedLog,
            string successLog)
        {
            Debug.Log(
                "[LoginFlow][FirebaseLoginProviderUtility.HandleFirebaseLoginResult] Evaluating Firebase auth task result.");
            if (task.IsCanceled)
            {
                Debug.Log(canceledLog);
                if (completionSource == null || completionSource.TrySetResult(false))
                    onFail?.Invoke();
                return;
            }

            if (task.IsFaulted)
            {
                Debug.Log(task.Exception == null ? failedLog : $"{failedLog}: {task.Exception}");
                if (completionSource == null || completionSource.TrySetResult(false))
                    onFail?.Invoke();
                return;
            }

            Debug.Log(successLog);
            if (completionSource == null || completionSource.TrySetResult(true))
            {
                Debug.Log(
                    "[LoginFlow][FirebaseLoginProviderUtility.HandleFirebaseLoginResult] Refresh current user and invoke success callback.");
                FirebaseLoginManager.RefreshCurrentUser();
                onSuccess?.Invoke();
            }
        }

        /// <summary>
        ///     Awaits the sign-in completion source with a specified timeout limit.
        /// </summary>
        /// <param name="completionSource">The sign-in task completion source.</param>
        /// <param name="onFail">Callback executed on timeout.</param>
        /// <param name="timeoutSeconds">Maximum time in seconds to wait for sign-in.</param>
        /// <param name="timeoutLog">Log message printed if the sign-in times out.</param>
        /// <returns>A UniTask representing the asynchronous operation.</returns>
        internal static async UniTask AwaitSignInWithTimeout(
            UniTaskCompletionSource<bool> completionSource,
            UnityAction onFail,
            float timeoutSeconds,
            string timeoutLog)
        {
            Debug.Log(
                "[LoginFlow][FirebaseLoginProviderUtility.AwaitSignInWithTimeout] Waiting for sign-in or timeout.");
            var timeoutTask = UniTask.Delay(TimeSpan.FromSeconds(timeoutSeconds));
            var signInTask = completionSource.Task;
            var (hasSignInCompleted, _) = await UniTask.WhenAny(signInTask, timeoutTask);

            if (!hasSignInCompleted)
            {
                Debug.LogWarning(timeoutLog);
                if (completionSource == null || completionSource.TrySetResult(false))
                    onFail?.Invoke();
                return;
            }

            Debug.Log(
                "[LoginFlow][FirebaseLoginProviderUtility.AwaitSignInWithTimeout] Sign-in completed before timeout.");
        }

        /// <summary>
        ///     Logs out the current Firebase user and refreshes the login manager state.
        /// </summary>
        /// <param name="successAction">Callback executed after successful logout.</param>
        internal static void Logout(UnityAction successAction)
        {
            Debug.Log("[LoginFlow][FirebaseLoginProviderUtility.Logout] Sign out current Firebase user.");
            FirebaseLoginManager.Auth.SignOut();
            FirebaseLoginManager.RefreshCurrentUser();
            successAction?.Invoke();
        }

        #endregion
    }
}