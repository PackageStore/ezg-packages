#if UNITY_IOS
using AppleAuth;
using AppleAuth.Enums;
using AppleAuth.Extensions;
using AppleAuth.Interfaces;
using AppleAuth.Native;
using Cysharp.Threading.Tasks;
using Firebase.Auth;
using Firebase.Extensions;
using System.Text;
using UnityEngine;
using UnityEngine.Events;

namespace Ezg.Core.Firebase
{
    /// <summary>
    /// Handles Firebase authentication using Apple sign-in provider.
    /// </summary>
    internal static class FirebaseAppleLoginProvider
    {
        #region Fields

        private static readonly string APPLE_USER_ID_KEY = FirebaseConfig.Instance.AppleUserIdPrefsKey;

        private static IAppleAuthManager _appleAuthManager;
        private static UniTaskCompletionSource<bool> _appleSignInCompletionSource;
        private static UnityAction _appleOnSuccess;
        private static UnityAction _appleOnFail;

        #endregion

        #region Initialize

        /// <summary>
        /// Initializes the Apple Authentication Manager if supported on the current platform.
        /// </summary>
        internal static void Initialize()
        {
            if (_appleAuthManager != null)
            {
                Debug.LogWarning("AppleAuthManager already initialized.");
                return;
            }

            if (!AppleAuthManager.IsCurrentPlatformSupported)
            {
                Debug.LogWarning("Sign In With Apple is not supported on this platform.");
                return;
            }

            var deserializer = new PayloadDeserializer();
            _appleAuthManager = new AppleAuthManager(deserializer);
            Debug.Log("AppleAuthManager initialized successfully.");
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Updates the Apple Authentication Manager tick loop.
        /// </summary>
        internal static void Tick()
        {
            _appleAuthManager?.Update();
        }

        /// <summary>
        /// Checks previous Apple login credentials state.
        /// </summary>
        internal static void CheckInit()
        {
            Debug.LogWarning("Checking Apple initialization.");
            if (_appleAuthManager == null)
            {
                Debug.LogWarning("_appleAuthManager is null. Call InitAppleAuthManager() first.");
                return;
            }

            _appleAuthManager.SetCredentialsRevokedCallback(result =>
            {
                Debug.LogWarning("Received revoked callback: " + result);
                PlayerPrefs.DeleteKey(APPLE_USER_ID_KEY);
            });

            if (PlayerPrefs.HasKey(APPLE_USER_ID_KEY))
            {
                var storedAppleUserId = PlayerPrefs.GetString(APPLE_USER_ID_KEY);
                CheckCredentialStatusForUserId(storedAppleUserId);
            }
        }

        /// <summary>
        /// Initiates the Apple sign-in flow.
        /// </summary>
        /// <param name="onSuccess">Callback executed on successful sign-in.</param>
        /// <param name="onFail">Callback executed on sign-in failure.</param>
        /// <param name="timeoutSeconds">The timeout duration in seconds.</param>
        /// <returns>A UniTask representing the asynchronous sign-in operation.</returns>
        internal static async UniTask SignIn(UnityAction onSuccess, UnityAction onFail, float timeoutSeconds)
        {
            if (FirebaseAuth.DefaultInstance.CurrentUser != null)
            {
                Debug.Log("User already signed in");
                onFail?.Invoke();
                return;
            }

            if (_appleAuthManager == null)
            {
                Debug.LogWarning("Apple Auth Manager is null. Call InitAppleAuthManager() first.");
                onFail?.Invoke();
                return;
            }

            _appleOnSuccess = onSuccess;
            _appleOnFail = onFail;
            _appleSignInCompletionSource = new UniTaskCompletionSource<bool>();

            var loginArgs = new AppleAuthLoginArgs(LoginOptions.IncludeEmail | LoginOptions.IncludeFullName);
            _appleAuthManager.LoginWithAppleId(
                loginArgs,
                credential =>
                {
                    PlayerPrefs.SetString(APPLE_USER_ID_KEY, credential.User);
                    SetupAppleDataSync(credential.User, credential);
                },
                error =>
                {
                    var authorizationErrorCode = error.GetAuthorizationErrorCode();
                    Debug.LogWarning("Sign in with Apple failed: " + authorizationErrorCode + " " + error);
                    _appleOnFail?.Invoke();
                    _appleSignInCompletionSource?.TrySetResult(false);
                });

            await FirebaseLoginProviderUtility.AwaitSignInWithTimeout(
                _appleSignInCompletionSource,
                _appleOnFail,
                timeoutSeconds,
                $"Apple Sign-In timeout after {timeoutSeconds} seconds");
        }

        /// <summary>
        /// Logs out the current Apple user from Firebase.
        /// </summary>
        /// <param name="successAction">Callback executed after successful logout.</param>
        internal static void Logout(UnityAction successAction)
        {
            FirebaseLoginProviderUtility.Logout(successAction);
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// Checks credential status for a given Apple user ID.
        /// </summary>
        /// <param name="appleUserId">The Apple user ID to check.</param>
        private static void CheckCredentialStatusForUserId(string appleUserId)
        {
            _appleAuthManager.GetCredentialState(
                appleUserId,
                state =>
                {
                    switch (state)
                    {
                        case CredentialState.Authorized:
                            return;

                        case CredentialState.Revoked:
                            Debug.LogWarning("Apple credential revoked.");
                            break;

                        case CredentialState.NotFound:
                            Debug.LogWarning("Apple credential not found.");
                            PlayerPrefs.DeleteKey(APPLE_USER_ID_KEY);
                            return;
                    }
                },
                error =>
                {
                    var authorizationErrorCode = error.GetAuthorizationErrorCode();
                    Debug.LogWarning("Error getting Apple credential state: " + authorizationErrorCode + " " + error);
                });
        }

        /// <summary>
        /// Extracts IdentityToken from credential and triggers Firebase sign-in.
        /// </summary>
        /// <param name="appleUserId">The Apple user ID.</param>
        /// <param name="receivedCredential">The credential received from Apple.</param>
        private static void SetupAppleDataSync(string appleUserId, ICredential receivedCredential)
        {
            if (receivedCredential == null)
            {
                Debug.Log("No credentials received for Apple user: " + appleUserId);
                _appleOnFail?.Invoke();
                _appleSignInCompletionSource?.TrySetResult(false);
                return;
            }

            if (receivedCredential is IAppleIDCredential appleID)
            {
                var identityToken = Encoding.UTF8.GetString(appleID.IdentityToken, 0, appleID.IdentityToken.Length);
                SignInAppleFirebaseSync(identityToken, Nonce.GenerateNonce(256));
            }
            else
            {
                _appleOnFail?.Invoke();
                _appleSignInCompletionSource?.TrySetResult(false);
                Debug.LogWarning("Credential is not an IAppleIDCredential.");
            }
        }

        /// <summary>
        /// Performs Firebase sign-in using Apple identity token.
        /// </summary>
        /// <param name="appleIdToken">The identity token from Apple.</param>
        /// <param name="rawNonce">The raw nonce generated for this login.</param>
        private static void SignInAppleFirebaseSync(string appleIdToken, string rawNonce)
        {
            Credential credential = OAuthProvider.GetCredential("apple.com", appleIdToken, rawNonce, null);
            FirebaseLoginManager.Auth.SignInWithCredentialAsync(credential)
                .ContinueWithOnMainThread(OnAppleFirebaseLogin);
        }

        /// <summary>
        /// Callback handling Apple Firebase login task result.
        /// </summary>
        /// <param name="task">The completed Firebase Auth task.</param>
        private static void OnAppleFirebaseLogin(System.Threading.Tasks.Task<FirebaseUser> task)
        {
            FirebaseLoginProviderUtility.HandleFirebaseLoginResult(
                task,
                _appleOnSuccess,
                _appleOnFail,
                _appleSignInCompletionSource,
                "Apple Firebase login canceled.",
                "Apple Firebase login failed",
                "Apple Firebase login success.");
        }

        #endregion
    }
}
#endif