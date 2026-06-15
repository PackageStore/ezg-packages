using Cysharp.Threading.Tasks;
using Firebase;
using Firebase.Auth;
using System;
using UnityEngine;
using UnityEngine.Events;

namespace Ezg.Core.Firebase
{
    /// <summary>
    ///     Static facade for Firebase Authentication and third-party login providers.
    /// </summary>
    public static class FirebaseLoginManager
    {
        #region Fields

        private static FirebaseUser _firebaseUser;

        #endregion

        #region Initialize

        static FirebaseLoginManager()
        {
            Auth = FirebaseAuth.DefaultInstance;
            _firebaseUser = FirebaseAuth.DefaultInstance.CurrentUser;
        }

        #endregion

        #region Public Methods

        internal static FirebaseAuth Auth { get; private set; }

        /// <summary>
        ///     Validates the current user's Firebase session by reloading the user.
        /// </summary>
        public static async UniTask<bool> ValidateCurrentUserSession(bool signOutIfInvalid = true)
        {
            RefreshCurrentUser();
            if (_firebaseUser == null)
            {
                Debug.Log("[LoginFlow][FirebaseLoginManager.ValidateCurrentUserSession] No cached Firebase user.");
                return false;
            }

            try
            {
                Debug.Log($"[LoginFlow][FirebaseLoginManager.ValidateCurrentUserSession] Reload Firebase user. userId={_firebaseUser.UserId}");
                await _firebaseUser.ReloadAsync();
                RefreshCurrentUser();
                var isValid = _firebaseUser != null;
                Debug.Log($"[LoginFlow][FirebaseLoginManager.ValidateCurrentUserSession] Reload finished. isValid={isValid}, userId={(_firebaseUser != null ? _firebaseUser.UserId : "null")}");
                return isValid;
            }
            catch (Exception ex)
            {
                var authError = TryExtractAuthError(ex);
                Debug.LogWarning(
                    "[LoginFlow][FirebaseLoginManager.ValidateCurrentUserSession] Reload failed. " +
                    $"userId={_firebaseUser.UserId}, authError={(authError.HasValue ? authError.Value.ToString() : "unknown")}, exception={ex}");

                if (signOutIfInvalid && IsInvalidSessionError(authError))
                {
                    Debug.LogWarning("[LoginFlow][FirebaseLoginManager.ValidateCurrentUserSession] Session is invalid. Sign out cached Firebase user.");
                    Auth.SignOut();
                    RefreshCurrentUser();
                }

                return false;
            }
        }

        /// <summary>
        ///     Gets the currently authenticated Firebase user.
        /// </summary>
        /// <returns>The FirebaseUser object if authenticated, otherwise null.</returns>
        public static FirebaseUser GetUserData()
        {
            return _firebaseUser;
        }

        /// <summary>
        ///     Initiates the Google sign-in flow.
        /// </summary>
        /// <param name="onSuccess">Callback executed on successful sign-in.</param>
        /// <param name="onFail">Callback executed on sign-in failure.</param>
        /// <param name="timeoutSeconds">The timeout duration in seconds.</param>
        /// <returns>A UniTask representing the asynchronous sign-in operation.</returns>
        public static UniTask SignInWithGoogle(UnityAction onSuccess, UnityAction onFail,
            float timeoutSeconds = -1f)
        {
            timeoutSeconds = ResolveTimeout(timeoutSeconds);
            Debug.Log($"[LoginFlow][FirebaseLoginManager] SignInWithGoogle called. timeoutSeconds={timeoutSeconds}");
            return FirebaseGoogleLoginProvider.SignIn(onSuccess, onFail, timeoutSeconds);
        }

        /// <summary>
        ///     Logs out the current Google user.
        /// </summary>
        /// <param name="successAction">Callback executed after successful logout.</param>
        public static void OnGoogleLogout(UnityAction successAction)
        {
            Debug.Log("[LoginFlow][FirebaseLoginManager] OnGoogleLogout called.");
            FirebaseGoogleLoginProvider.Logout(successAction);
        }

#if UNITY_IOS
        /// <summary>
        /// Initiates the Game Center sign-in flow.
        /// </summary>
        /// <param name="onSuccess">Callback executed on successful sign-in.</param>
        /// <param name="onFail">Callback executed on sign-in failure.</param>
        /// <param name="timeoutSeconds">The timeout duration in seconds.</param>
        /// <returns>A UniTask representing the asynchronous sign-in operation.</returns>
        public static UniTask SignInWithGameCenter(UnityAction onSuccess, UnityAction onFail, float timeoutSeconds = -1f)
        {
            timeoutSeconds = ResolveTimeout(timeoutSeconds);
            Debug.Log($"[LoginFlow][FirebaseLoginManager] SignInWithGameCenter called. timeoutSeconds={timeoutSeconds}, onSuccess={(onSuccess != null)}, onFail={(onFail != null)}");
            return FirebaseGameCenterLoginProvider.SignIn(onSuccess, onFail, timeoutSeconds);
        }

        public static void OnGameCenterLogout(UnityAction successAction)
        {
            Debug.Log("[LoginFlow][FirebaseLoginManager] OnGameCenterLogout called.");
            FirebaseGameCenterLoginProvider.Logout(successAction);
        }

        /// <summary>
        /// Initializes the Apple authentication manager.
        /// </summary>
        public static void InitAppleAuthManager()
        {
            Debug.Log("[LoginFlow][FirebaseLoginManager] InitAppleAuthManager called.");
            FirebaseAppleLoginProvider.Initialize();
        }

        /// <summary>
        /// Updates the Apple authentication manager tick cycle.
        /// </summary>
        public static void TickAppleAuthManager()
        {
            FirebaseAppleLoginProvider.Tick();
        }

        /// <summary>
        /// Checks if Apple authentication manager is initialized, and initializes it if not.
        /// </summary>
        public static void CheckInitApple()
        {
            Debug.Log("[LoginFlow][FirebaseLoginManager] CheckInitApple called.");
            FirebaseAppleLoginProvider.CheckInit();
        }

        /// <summary>
        /// Initiates the Apple sign-in flow.
        /// </summary>
        /// <param name="onSuccess">Callback executed on successful sign-in.</param>
        /// <param name="onFail">Callback executed on sign-in failure.</param>
        /// <param name="timeoutSeconds">The timeout duration in seconds.</param>
        /// <returns>A UniTask representing the asynchronous sign-in operation.</returns>
        public static UniTask SignInWithApple(UnityAction onSuccess, UnityAction onFail, float timeoutSeconds = -1f)
        {
            timeoutSeconds = ResolveTimeout(timeoutSeconds);
            Debug.Log($"[LoginFlow][FirebaseLoginManager] SignInWithApple called. timeoutSeconds={timeoutSeconds}, onSuccess={(onSuccess != null)}, onFail={(onFail != null)}");
            return FirebaseAppleLoginProvider.SignIn(onSuccess, onFail, timeoutSeconds);
        }

        /// <summary>
        /// Logs out the current Apple user.
        /// </summary>
        /// <param name="successAction">Callback executed after successful logout.</param>
        public static void OnAppleLogout(UnityAction successAction)
        {
            Debug.Log("[LoginFlow][FirebaseLoginManager] OnAppleLogout called.");
            FirebaseAppleLoginProvider.Logout(successAction);
        }
#endif

        #endregion

        #region Private Methods

        /// <summary>
        ///     Trả về timeout do caller truyền vào nếu hợp lệ (&gt; 0), ngược lại lấy từ <see cref="FirebaseConfig"/>.
        /// </summary>
        private static float ResolveTimeout(float timeoutSeconds)
        {
            return timeoutSeconds > 0f ? timeoutSeconds : FirebaseConfig.Instance.SignInTimeoutSeconds;
        }

        private static AuthError? TryExtractAuthError(Exception exception)
        {
            if (exception == null)
                return null;

            if (exception is AggregateException aggregateException)
            {
                foreach (var innerException in aggregateException.Flatten().InnerExceptions)
                {
                    var innerError = TryExtractAuthError(innerException);
                    if (innerError.HasValue)
                        return innerError;
                }
            }

            if (exception is FirebaseException firebaseException)
                return (AuthError)firebaseException.ErrorCode;

            return exception.InnerException != null
                ? TryExtractAuthError(exception.InnerException)
                : null;
        }

        private static bool IsInvalidSessionError(AuthError? authError)
        {
            return authError == AuthError.UserNotFound
                   || authError == AuthError.UserTokenExpired
                   || authError == AuthError.InvalidUserToken
                   || authError == AuthError.InvalidCredential
                   || authError == AuthError.RequiresRecentLogin;
        }

        /// <summary>
        ///     Refreshes the cached FirebaseAuth and FirebaseUser instances.
        /// </summary>
        internal static void RefreshCurrentUser()
        {
            Debug.Log("[LoginFlow][FirebaseLoginManager] RefreshCurrentUser.");
            Auth = FirebaseAuth.DefaultInstance;
            _firebaseUser = FirebaseAuth.DefaultInstance.CurrentUser;
            Debug.Log(
                $"[LoginFlow][FirebaseLoginManager] CurrentUser after refresh: {(_firebaseUser != null ? _firebaseUser.UserId : "null")}");
        }

        #endregion
    }
}