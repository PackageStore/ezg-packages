using System;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using Firebase.Auth;
using Firebase.Extensions;
using UnityEngine;
using UnityEngine.Events;
#if GPG_LOGIN
using GooglePlayGames;
using GooglePlayGames.BasicApi;
#endif

namespace Ezg.Core.Firebase
{
    /// <summary>
    ///     Handles Firebase authentication using Google Play Games service.
    /// </summary>
    internal static class FirebaseGoogleLoginProvider
    {
        #region Fields

#if GPG_LOGIN
        private static UnityAction _onSuccess;
        private static UnityAction _onFail;
        private static UniTaskCompletionSource<bool> _signInCompletionSource;
#endif

        #endregion

        #region Public Methods

        /// <summary>
        ///     Initiates sign-in using Google Play Games authentication.
        /// </summary>
        /// <param name="onSuccess">Callback executed on successful sign-in.</param>
        /// <param name="onFail">Callback executed on sign-in failure.</param>
        /// <param name="timeoutSeconds">The timeout duration in seconds.</param>
        /// <returns>A UniTask representing the asynchronous sign-in operation.</returns>
        internal static async UniTask SignIn(UnityAction onSuccess, UnityAction onFail, float timeoutSeconds)
        {
#if GPG_LOGIN
            Debug.Log("[LoginFlow][FirebaseGoogleLoginProvider.SignIn] Start.");
            if (FirebaseAuth.DefaultInstance.CurrentUser != null)
            {
                Debug.Log(
                    $"[LoginFlow][FirebaseGoogleLoginProvider.SignIn] Already signed in as {FirebaseAuth.DefaultInstance.CurrentUser.DisplayName}.");
                onFail?.Invoke();
                return;
            }

            _onSuccess = onSuccess;
            _onFail = onFail;
            _signInCompletionSource = new UniTaskCompletionSource<bool>();

            Debug.Log(
                "[LoginFlow][FirebaseGoogleLoginProvider.SignIn] CurrentUser is null. Starting Play Games authentication.");
            PlayGamesPlatform.Instance.Authenticate(ProcessAuthentication);

            await FirebaseLoginProviderUtility.AwaitSignInWithTimeout(
                _signInCompletionSource,
                _onFail,
                timeoutSeconds,
                $"[LoginFlow][FirebaseGoogleLoginProvider.SignIn] Google Sign-In timeout after {timeoutSeconds} seconds");
#else
            Debug.LogWarning("[LoginFlow][FirebaseGoogleLoginProvider.SignIn] GPG_LOGIN is disabled. Sign-in will complete immediately.");
            await UniTask.CompletedTask;
#endif
        }

        /// <summary>
        ///     Logs out the Google user.
        /// </summary>
        /// <param name="successAction">Callback executed after successful logout.</param>
        internal static void Logout(UnityAction successAction)
        {
            FirebaseLoginProviderUtility.Logout(successAction);
        }

        #endregion

        #region Private Methods

#if GPG_LOGIN
        /// <summary>
        ///     Callback handling Google Play Games authentication status.
        /// </summary>
        /// <param name="status">The authentication status from Google Play Games.</param>
        private static void ProcessAuthentication(SignInStatus status)
        {
            Debug.Log($"[LoginFlow][FirebaseGoogleLoginProvider.ProcessAuthentication] Status={status}");
            if (status == SignInStatus.Success)
            {
                Debug.Log("[LoginFlow][FirebaseGoogleLoginProvider.ProcessAuthentication] Play Games sign-in success.");
                PlayGamesPlatform.Instance.RequestServerSideAccess(
                    false,
                    authCode =>
                    {
                        Debug.Log(
                            "[LoginFlow][FirebaseGoogleLoginProvider.ProcessAuthentication] Received server side access callback.");
                        if (string.IsNullOrEmpty(authCode))
                        {
                            Debug.LogWarning(
                                "[LoginFlow][FirebaseGoogleLoginProvider.ProcessAuthentication] Play Games server auth code is null or empty.");
                            TryFail();
                            return;
                        }

                        try
                        {
                            Debug.Log(
                                "[LoginFlow][FirebaseGoogleLoginProvider.ProcessAuthentication] Play Games auth code received.");
                            var credential = PlayGamesAuthProvider.GetCredential(authCode);
                            Debug.Log(
                                "[LoginFlow][FirebaseGoogleLoginProvider.ProcessAuthentication] Signing in to Firebase with credential.");
                            FirebaseLoginManager.Auth.SignInWithCredentialAsync(credential)
                                .ContinueWithOnMainThread(OnFirebaseLogin);
                        }
                        catch (Exception ex)
                        {
                            Debug.LogWarning(
                                "[LoginFlow][FirebaseGoogleLoginProvider.ProcessAuthentication] Failed to build Firebase credential from Play Games auth code: " +
                                ex);
                            TryFail();
                        }
                    });
                return;
            }

            Debug.LogError(
                $"[LoginFlow][FirebaseGoogleLoginProvider.ProcessAuthentication] Play Games sign-in failed. Status={status}");
            if (status == SignInStatus.Canceled)
                Debug.LogWarning(
                    "[LoginFlow][FirebaseGoogleLoginProvider.ProcessAuthentication] User canceled or not signed in to Google Play.");
            else if (status == SignInStatus.InternalError)
                Debug.LogWarning(
                    "[LoginFlow][FirebaseGoogleLoginProvider.ProcessAuthentication] Internal error - check SHA-1/OAuth configuration.");

            TryFail();
        }

        /// <summary>
        ///     Callback handling Firebase sign-in with Google Play Games credential.
        /// </summary>
        /// <param name="task">The Firebase auth task.</param>
        private static void OnFirebaseLogin(Task<FirebaseUser> task)
        {
            Debug.Log("[LoginFlow][FirebaseGoogleLoginProvider.OnFirebaseLogin] Firebase credential task completed.");
            FirebaseLoginProviderUtility.HandleFirebaseLoginResult(
                task,
                _onSuccess,
                _onFail,
                _signInCompletionSource,
                "[LoginFlow][FirebaseGoogleLoginProvider.OnFirebaseLogin] Firebase login canceled.",
                "[LoginFlow][FirebaseGoogleLoginProvider.OnFirebaseLogin] Firebase login failed",
                "[LoginFlow][FirebaseGoogleLoginProvider.OnFirebaseLogin] Firebase login success");
        }

        /// <summary>
        ///     Helper to trigger the login failure callback and resolve the completion source.
        /// </summary>
        private static void TryFail()
        {
            Debug.LogWarning("[LoginFlow][FirebaseGoogleLoginProvider.TryFail] Triggering login failure path.");
            if (_signInCompletionSource == null || _signInCompletionSource.TrySetResult(false))
                _onFail?.Invoke();
        }
#endif

        #endregion
    }
}