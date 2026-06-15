#if UNITY_IOS
using Apple.GameKit;
using Cysharp.Threading.Tasks;
using Firebase;
using Firebase.Auth;
using System;
using System.Text;
using UnityEngine;
using UnityEngine.Events;

namespace Ezg.Core.Firebase
{
    /// <summary>
    /// Handles Firebase authentication using iOS Game Center provider.
    /// </summary>
    internal static class FirebaseGameCenterLoginProvider
    {
        #region Fields

        private static readonly int MAX_CREDENTIAL_ATTEMPTS = FirebaseConfig.Instance.MaxCredentialAttempts;
        private static readonly int CREDENTIAL_RETRY_DELAY_MS = FirebaseConfig.Instance.CredentialRetryDelayMs;
        private static readonly float GAME_CENTER_AUTH_TIMEOUT_SECONDS = FirebaseConfig.Instance.GameCenterAuthTimeoutSeconds;
        private static readonly float PLAYER_AUTH_WAIT_TIMEOUT_SECONDS = FirebaseConfig.Instance.PlayerAuthWaitTimeoutSeconds;
        private static readonly int PLAYER_AUTH_POLL_DELAY_MS = FirebaseConfig.Instance.PlayerAuthPollDelayMs;
        private static readonly int POST_NATIVE_AUTH_SETTLE_DELAY_MS = FirebaseConfig.Instance.PostNativeAuthSettleDelayMs;
        private static readonly int POST_PROVIDER_READY_SETTLE_DELAY_MS = FirebaseConfig.Instance.PostProviderReadySettleDelayMs;

        private static UnityAction _onSuccess;
        private static UnityAction _onFail;
        private static UniTaskCompletionSource<bool> _signInCompletionSource;
        private static bool _isFinalized;
        private static string _currentStage;
        private static string _lastCredentialFailureDetail;
        private static float _signInStartedAtRealtime;

        #endregion

        #region Public Methods

        /// <summary>
        /// Initiates the Game Center sign-in flow.
        /// </summary>
        /// <param name="onSuccess">Callback executed on successful sign-in.</param>
        /// <param name="onFail">Callback executed on sign-in failure.</param>
        /// <param name="timeoutSeconds">The timeout duration in seconds.</param>
        /// <returns>A UniTask representing the asynchronous sign-in operation.</returns>
        internal static async UniTask SignIn(UnityAction onSuccess, UnityAction onFail, float timeoutSeconds)
        {
            Debug.Log($"[LoginFlow][FirebaseGameCenterLoginProvider.SignIn] Start. timeoutSeconds={timeoutSeconds}, onSuccess={(onSuccess != null)}, onFail={(onFail != null)}");
            _onSuccess = onSuccess;
            _onFail = onFail;
            _signInCompletionSource = new UniTaskCompletionSource<bool>();
            _isFinalized = false;
            _lastCredentialFailureDetail = null;
            _signInStartedAtRealtime = Time.realtimeSinceStartup;
            SetStage("initialized");
            Debug.Log("[LoginFlow][FirebaseGameCenterLoginProvider.SignIn] State initialized. Check existing Firebase session.");

            if (await TryUseExistingFirebaseSession())
            {
                Debug.Log("[LoginFlow][FirebaseGameCenterLoginProvider.SignIn] Existing Firebase session reused. Exit early.");
                return;
            }

            Debug.Log("[LoginFlow][FirebaseGameCenterLoginProvider.SignIn] No existing session. Start ExecuteSignInFlow and wait for timeout.");
            ExecuteSignInFlow().Forget();
            await AwaitSignInWithTimeout(timeoutSeconds);
            Debug.Log("[LoginFlow][FirebaseGameCenterLoginProvider.SignIn] AwaitSignInWithTimeout finished. " + BuildTrackingSummary());
        }

        /// <summary>
        /// Logs out the current user and refreshes FirebaseAuth state.
        /// </summary>
        /// <param name="successAction">Callback executed after successful logout.</param>
        internal static void Logout(UnityAction successAction)
        {
            FirebaseLoginProviderUtility.Logout(successAction);
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// Attempts to reuse the active Firebase user session if valid.
        /// </summary>
        /// <returns>A UniTask returning true if current Firebase session is reused, otherwise false.</returns>
        private static async UniTask<bool> TryUseExistingFirebaseSession()
        {
            SetStage("checking_existing_firebase_session");
            Debug.Log("[LoginFlow][FirebaseGameCenterLoginProvider.TryUseExistingFirebaseSession] Check FirebaseAuth.DefaultInstance.CurrentUser.");
            var hasValidSession = await FirebaseLoginManager.ValidateCurrentUserSession();
            if (!hasValidSession)
            {
                Debug.Log("[LoginFlow][FirebaseGameCenterLoginProvider.TryUseExistingFirebaseSession] No current Firebase user.");
                return false;
            }

            Debug.Log($"[LoginFlow][FirebaseGameCenterLoginProvider.TryUseExistingFirebaseSession] Firebase user already signed in. UserId={FirebaseAuth.DefaultInstance.CurrentUser.UserId}. Reusing current session.");
            FirebaseLoginManager.RefreshCurrentUser();
            SetStage("reused_existing_firebase_session");
            FinalizeSuccess();
            return true;
        }

        /// <summary>
        /// Core sign-in workflow sequence that authenticates with Game Center and Firebase.
        /// </summary>
        private static async UniTaskVoid ExecuteSignInFlow()
        {
            try
            {
                SetStage("execute_sign_in_flow_enter");
                Debug.Log("[LoginFlow][FirebaseGameCenterLoginProvider.ExecuteSignInFlow] Enter.");
                await UniTask.SwitchToMainThread();
                SetStage("main_thread_ready");
                Debug.Log("[LoginFlow][FirebaseGameCenterLoginProvider.ExecuteSignInFlow] Switched to main thread.");

                SetStage("authenticating_game_center_user");
                Debug.Log("[LoginFlow][FirebaseGameCenterLoginProvider.ExecuteSignInFlow] AuthenticateGameCenterUser...");
                var gameCenterAuthSuccess = await AuthenticateGameCenterUser();
                if (HasAlreadyFinalized("after AuthenticateGameCenterUser"))
                    return;
                Debug.Log($"[LoginFlow][FirebaseGameCenterLoginProvider.ExecuteSignInFlow] AuthenticateGameCenterUser result={gameCenterAuthSuccess}");
                if (!gameCenterAuthSuccess)
                {
                    HandleSignInFail("Game Center authentication failed or canceled.");
                    return;
                }

                SetStage("game_center_authenticated");
                Debug.Log($"[LoginFlow][FirebaseGameCenterLoginProvider.ExecuteSignInFlow] Native Game Center auth succeeded. Delay {POST_NATIVE_AUTH_SETTLE_DELAY_MS}ms to allow identity signature to settle.");
                await UniTask.Delay(POST_NATIVE_AUTH_SETTLE_DELAY_MS);
                if (HasAlreadyFinalized("after native auth settle delay"))
                    return;

                SetStage("waiting_for_firebase_game_center_provider");
                Debug.Log("[LoginFlow][FirebaseGameCenterLoginProvider.ExecuteSignInFlow] WaitForFirebaseGameCenterReady...");
                var playerAuthenticated = await WaitForFirebaseGameCenterReady();
                if (HasAlreadyFinalized("after WaitForFirebaseGameCenterReady"))
                    return;
                Debug.Log($"[LoginFlow][FirebaseGameCenterLoginProvider.ExecuteSignInFlow] WaitForFirebaseGameCenterReady result={playerAuthenticated}");
                if (!playerAuthenticated)
                {
                    HandleSignInFail("Game Center authenticated natively, but Firebase Game Center provider did not become ready before timeout.");
                    return;
                }

                SetStage("firebase_game_center_provider_ready");
                Debug.Log($"[LoginFlow][FirebaseGameCenterLoginProvider.ExecuteSignInFlow] Firebase Game Center provider ready. Delay {POST_PROVIDER_READY_SETTLE_DELAY_MS}ms before requesting credential.");
                await UniTask.Delay(POST_PROVIDER_READY_SETTLE_DELAY_MS);
                if (HasAlreadyFinalized("after provider ready settle delay"))
                    return;

                SetStage("requesting_game_center_credential");
                Debug.Log("[LoginFlow][FirebaseGameCenterLoginProvider.ExecuteSignInFlow] GetGameCenterCredentialWithRetry...");
                var credential = await GetGameCenterCredentialWithRetry();
                if (HasAlreadyFinalized("after GetGameCenterCredentialWithRetry"))
                    return;
                Debug.Log($"[LoginFlow][FirebaseGameCenterLoginProvider.ExecuteSignInFlow] Credential result={(credential != null ? credential.GetType().Name : "null")}");
                if (credential == null)
                {
                    HandleSignInFail("Failed to obtain Game Center credential for Firebase.");
                    return;
                }

                try
                {
                    SetStage("signing_in_to_firebase_with_game_center_credential");
                    Debug.Log("[LoginFlow][FirebaseGameCenterLoginProvider.ExecuteSignInFlow] Calling FirebaseAuth.SignInWithCredentialAsync.");
                    var firebaseTask = FirebaseLoginManager.Auth.SignInWithCredentialAsync(credential);
                    await firebaseTask;
                    if (HasAlreadyFinalized("after FirebaseAuth.SignInWithCredentialAsync"))
                        return;
                    Debug.Log($"[LoginFlow][FirebaseGameCenterLoginProvider.ExecuteSignInFlow] Firebase sign-in task finished. IsCanceled={firebaseTask.IsCanceled}, IsFaulted={firebaseTask.IsFaulted}");

                    if (firebaseTask.IsCanceled)
                    {
                        HandleSignInFail("Firebase sign-in with Game Center credential was canceled.");
                        return;
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[LoginFlow][FirebaseGameCenterLoginProvider.ExecuteSignInFlow] Firebase sign-in threw exception: {ex}");
                    HandleSignInFail(BuildFirebaseLoginFailureMessage(ex));
                    return;
                }

                SetStage("firebase_sign_in_succeeded");
                Debug.Log("[LoginFlow][FirebaseGameCenterLoginProvider.ExecuteSignInFlow] Firebase login success.");
                FirebaseLoginManager.RefreshCurrentUser();
                FinalizeSuccess();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[LoginFlow][FirebaseGameCenterLoginProvider.ExecuteSignInFlow] Unexpected exception: {FormatExceptionDetails(ex)}");
                HandleSignInFail($"Unexpected error during Game Center sign-in flow: {FormatExceptionDetails(ex)}");
            }
        }

        /// <summary>
        /// Authenticates with iOS native Game Center.
        /// </summary>
        /// <returns>A UniTask returning true if GKLocalPlayer is authenticated, otherwise false.</returns>
        private static async UniTask<bool> AuthenticateGameCenterUser()
        {
            try
            {
                SetStage("authenticate_game_center_user_enter");
                Debug.Log("[LoginFlow][FirebaseGameCenterLoginProvider.AuthenticateGameCenterUser] Enter.");
                if (IsGameCenterPlayerAuthenticated())
                {
                    Debug.Log("[LoginFlow][FirebaseGameCenterLoginProvider.AuthenticateGameCenterUser] GKLocalPlayer already authenticated.");
                    return true;
                }

                await UniTask.SwitchToMainThread();
                Debug.Log("[LoginFlow][FirebaseGameCenterLoginProvider.AuthenticateGameCenterUser] Calling GKLocalPlayer.Authenticate().");
                await GKLocalPlayer.Authenticate();
                Debug.Log($"[LoginFlow][FirebaseGameCenterLoginProvider.AuthenticateGameCenterUser] Authenticate returned. IsAuthenticated={IsGameCenterPlayerAuthenticated()}");
                if (IsGameCenterPlayerAuthenticated())
                    return true;

                Debug.LogWarning("[LoginFlow][FirebaseGameCenterLoginProvider.AuthenticateGameCenterUser] GKLocalPlayer.Authenticate finished but player is not authenticated.");
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[LoginFlow][FirebaseGameCenterLoginProvider.AuthenticateGameCenterUser] GKLocalPlayer.Authenticate failed: " + FormatExceptionDetails(ex) + ". " + BuildTrackingSummary());
            }

            var startedAt = Time.realtimeSinceStartup;
            Debug.Log($"[LoginFlow][FirebaseGameCenterLoginProvider.AuthenticateGameCenterUser] Polling until authenticated for up to {GAME_CENTER_AUTH_TIMEOUT_SECONDS}s.");
            while (Time.realtimeSinceStartup - startedAt < GAME_CENTER_AUTH_TIMEOUT_SECONDS)
            {
                if (IsGameCenterPlayerAuthenticated())
                {
                    Debug.Log("[LoginFlow][FirebaseGameCenterLoginProvider.AuthenticateGameCenterUser] GKLocalPlayer became authenticated during polling.");
                    return true;
                }

                await UniTask.Delay(PLAYER_AUTH_POLL_DELAY_MS);
            }

            Debug.LogWarning(
                $"[LoginFlow][FirebaseGameCenterLoginProvider.AuthenticateGameCenterUser] Game Center native auth timeout after {GAME_CENTER_AUTH_TIMEOUT_SECONDS}s. {BuildTrackingSummary()}");
            return IsGameCenterPlayerAuthenticated();
        }

        /// <summary>
        /// Polls and waits for Firebase Game Center provider initialization.
        /// </summary>
        /// <returns>A UniTask returning true if Firebase Game Center provider becomes ready, otherwise false.</returns>
        private static async UniTask<bool> WaitForFirebaseGameCenterReady()
        {
            SetStage("wait_for_firebase_game_center_provider_enter");
            Debug.Log("[LoginFlow][FirebaseGameCenterLoginProvider.WaitForFirebaseGameCenterReady] Enter.");
            var startedAt = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - startedAt < PLAYER_AUTH_WAIT_TIMEOUT_SECONDS)
            {
                if (IsFirebaseGameCenterPlayerAuthenticated())
                {
                    Debug.Log("[LoginFlow][FirebaseGameCenterLoginProvider.WaitForFirebaseGameCenterReady] Firebase Game Center provider is ready.");
                    return true;
                }

                await UniTask.Delay(PLAYER_AUTH_POLL_DELAY_MS);
            }

            Debug.LogWarning("[LoginFlow][FirebaseGameCenterLoginProvider.WaitForFirebaseGameCenterReady] Provider not ready before timeout.");
            return IsFirebaseGameCenterPlayerAuthenticated();
        }

        /// <summary>
        /// Retrieves Game Center credential for Firebase with multiple retries.
        /// </summary>
        /// <returns>A UniTask returning the acquired Credential, or null if failed.</returns>
        private static async UniTask<Credential> GetGameCenterCredentialWithRetry()
        {
            SetStage("get_game_center_credential_with_retry_enter");
            Debug.Log("[LoginFlow][FirebaseGameCenterLoginProvider.GetGameCenterCredentialWithRetry] Enter.");
            Exception lastException = null;

            for (var attempt = 1; attempt <= MAX_CREDENTIAL_ATTEMPTS; attempt++)
            {
                try
                {
                    await UniTask.SwitchToMainThread();
                    SetStage($"requesting_game_center_credential_attempt_{attempt}");
                    Debug.Log($"[LoginFlow][FirebaseGameCenterLoginProvider.GetGameCenterCredentialWithRetry] Attempt {attempt}/{MAX_CREDENTIAL_ATTEMPTS}.");
                    var attemptStartedAt = Time.realtimeSinceStartup;
                    var credential = await GameCenterAuthProvider.GetCredentialAsync();
                    var attemptElapsedMs = (Time.realtimeSinceStartup - attemptStartedAt) * 1000f;

                    if (credential != null)
                    {
                        _lastCredentialFailureDetail = null;
                        Debug.Log(
                            $"[LoginFlow][FirebaseGameCenterLoginProvider.GetGameCenterCredentialWithRetry] Credential acquired on attempt {attempt}. " +
                            $"Type={credential.GetType().Name}, elapsedMs={attemptElapsedMs:0}.");
                        return credential;
                    }

                    _lastCredentialFailureDetail =
                        $"Attempt {attempt}/{MAX_CREDENTIAL_ATTEMPTS} returned null credential after {attemptElapsedMs:0}ms.";
                    Debug.LogWarning(
                        "[LoginFlow][FirebaseGameCenterLoginProvider.GetGameCenterCredentialWithRetry] " +
                        _lastCredentialFailureDetail + " " + BuildTrackingSummary());
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    _lastCredentialFailureDetail =
                        $"Attempt {attempt}/{MAX_CREDENTIAL_ATTEMPTS} threw {FormatExceptionDetails(ex)}";
                    Debug.LogWarning(
                        "[LoginFlow][FirebaseGameCenterLoginProvider.GetGameCenterCredentialWithRetry] " +
                        _lastCredentialFailureDetail + " " + BuildTrackingSummary());
                }

                if (attempt < MAX_CREDENTIAL_ATTEMPTS)
                {
                    Debug.Log($"[LoginFlow][FirebaseGameCenterLoginProvider.GetGameCenterCredentialWithRetry] Delay {CREDENTIAL_RETRY_DELAY_MS}ms before retry.");
                    await UniTask.Delay(CREDENTIAL_RETRY_DELAY_MS);
                }
            }

            if (lastException != null)
            {
                Debug.LogWarning(
                    "[LoginFlow][FirebaseGameCenterLoginProvider.GetGameCenterCredentialWithRetry] " +
                    "Credential retrieval failed after retries. LastException=" + FormatExceptionDetails(lastException) +
                    " " + BuildTrackingSummary());
            }
            else
            {
                Debug.LogWarning(
                    "[LoginFlow][FirebaseGameCenterLoginProvider.GetGameCenterCredentialWithRetry] " +
                    "Credential retrieval failed after retries without exception. " + BuildTrackingSummary());
            }

            return null;
        }

        /// <summary>
        /// Builds a user-friendly failure message from a Firebase exception.
        /// </summary>
        /// <param name="exception">The exception to process.</param>
        /// <returns>A formatted failure message string.</returns>
        private static string BuildFirebaseLoginFailureMessage(Exception exception)
        {
            var firebaseException = FindFirebaseException(exception);
            if (firebaseException != null)
            {
                return "Firebase login failed. " +
                       $"ErrorCode={(AuthError)firebaseException.ErrorCode} ({firebaseException.ErrorCode}). " +
                       $"Exception: {FormatExceptionDetails(firebaseException)}";
            }

            return $"Firebase login failed. Exception: {FormatExceptionDetails(exception)}";
        }

        /// <summary>
        /// Traverses inner exceptions to locate a FirebaseException.
        /// </summary>
        /// <param name="exception">The root exception.</param>
        /// <returns>The found FirebaseException, or null if none is found.</returns>
        private static FirebaseException FindFirebaseException(Exception exception)
        {
            if (exception == null)
                return null;

            if (exception is FirebaseException directFirebaseException)
                return directFirebaseException;

            if (exception is AggregateException aggregateException)
            {
                foreach (var innerException in aggregateException.Flatten().InnerExceptions)
                {
                    if (innerException is FirebaseException innerFirebaseException)
                        return innerFirebaseException;
                }
            }

            var current = exception.InnerException;
            while (current != null)
            {
                if (current is FirebaseException innerFirebaseException)
                    return innerFirebaseException;

                current = current.InnerException;
            }

            return null;
        }

        /// <summary>
        /// Helper to summarize current authentication states.
        /// </summary>
        /// <returns>A string summary.</returns>
        private static string BuildAuthStateSummary()
        {
            return $"GKLocalPlayer.IsAuthenticated={IsGameCenterPlayerAuthenticated()}, " +
                   $"FirebaseGameCenter.IsPlayerAuthenticated={IsFirebaseGameCenterPlayerAuthenticated()}";
        }

        /// <summary>
        /// Builds a detailed tracking summary of the sign-in flow.
        /// </summary>
        /// <returns>A tracking summary string.</returns>
        private static string BuildTrackingSummary()
        {
            return $"Stage={_currentStage}, Elapsed={GetElapsedSeconds():0.00}s, {BuildAuthStateSummary()}, FirebaseCurrentUser={GetFirebaseCurrentUserId()}";
        }

        /// <summary>
        /// Checks if the sign-in operation has already finalized.
        /// </summary>
        /// <param name="continuationPoint">The stage point name.</param>
        /// <returns>True if finalized, otherwise false.</returns>
        private static bool HasAlreadyFinalized(string continuationPoint)
        {
            if (!_isFinalized)
                return false;

            Debug.LogWarning(
                "[LoginFlow][FirebaseGameCenterLoginProvider] Ignoring late continuation because sign-in is already finalized. " +
                $"continuationPoint={continuationPoint}. {BuildTrackingSummary()}");
            return true;
        }

        /// <summary>
        /// Calculates the elapsed time in seconds since the sign-in process started.
        /// </summary>
        /// <returns>The elapsed time in seconds.</returns>
        private static float GetElapsedSeconds()
        {
            return _signInStartedAtRealtime > 0f ? Time.realtimeSinceStartup - _signInStartedAtRealtime : 0f;
        }

        /// <summary>
        /// Gets the current Firebase user ID.
        /// </summary>
        /// <returns>The Firebase user ID string or "null".</returns>
        private static string GetFirebaseCurrentUserId()
        {
            try
            {
                return FirebaseAuth.DefaultInstance.CurrentUser != null
                    ? FirebaseAuth.DefaultInstance.CurrentUser.UserId
                    : "null";
            }
            catch (Exception ex)
            {
                return "error:" + ex.GetType().Name;
            }
        }

        /// <summary>
        /// Sets the current stage of the sign-in process.
        /// </summary>
        /// <param name="stage">The stage name.</param>
        private static void SetStage(string stage)
        {
            _currentStage = stage;
            Debug.Log("[LoginFlow][FirebaseGameCenterLoginProvider.Stage] " + BuildTrackingSummary());
        }

        /// <summary>
        /// Formats detailed exception information including inner exceptions.
        /// </summary>
        /// <param name="exception">The exception to format.</param>
        /// <returns>A detailed exception string.</returns>
        private static string FormatExceptionDetails(Exception exception)
        {
            if (exception == null)
                return "null";

            var builder = new StringBuilder();
            var aggregateException = exception as AggregateException;
            if (aggregateException != null)
            {
                aggregateException = aggregateException.Flatten();
                builder.Append(aggregateException.GetType().Name)
                    .Append(": ")
                    .Append(aggregateException.Message);

                for (var i = 0; i < aggregateException.InnerExceptions.Count; i++)
                {
                    var innerException = aggregateException.InnerExceptions[i];
                    builder.Append(" | Inner[")
                        .Append(i)
                        .Append("]=")
                        .Append(innerException.GetType().Name)
                        .Append(": ")
                        .Append(innerException.Message);

                    if (innerException is FirebaseException innerFirebaseException)
                    {
                        builder.Append(" ErrorCode=")
                            .Append(innerFirebaseException.ErrorCode);
                    }
                }

                return builder.ToString();
            }

            var current = exception;
            var depth = 0;
            while (current != null && depth < 5)
            {
                if (depth > 0)
                    builder.Append(" | Inner-> ");

                builder.Append(current.GetType().Name)
                    .Append(": ")
                    .Append(current.Message);

                if (current is FirebaseException firebaseException)
                {
                    builder.Append(" ErrorCode=")
                        .Append(firebaseException.ErrorCode);
                }

                current = current.InnerException;
                depth++;
            }

            return builder.ToString();
        }

        /// <summary>
        /// Checks native GKLocalPlayer authentication status.
        /// </summary>
        /// <returns>True if authenticated, otherwise false.</returns>
        private static bool IsGameCenterPlayerAuthenticated()
        {
            try
            {
                return GKLocalPlayer.Local.IsAuthenticated;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("Failed to read GKLocalPlayer authentication state: " + ex);
                return false;
            }
        }

        /// <summary>
        /// Checks Firebase Game Center provider player authentication status.
        /// </summary>
        /// <returns>True if authenticated, otherwise false.</returns>
        private static bool IsFirebaseGameCenterPlayerAuthenticated()
        {
            try
            {
                return GameCenterAuthProvider.IsPlayerAuthenticated();
            }
            catch (Exception ex)
            {
                Debug.LogWarning("Failed to read Firebase Game Center authentication state: " + ex);
                return false;
            }
        }

        /// <summary>
        /// Awaits sign-in completion or timeout.
        /// </summary>
        /// <param name="timeoutSeconds">The timeout duration in seconds.</param>
        /// <returns>A UniTask representing the asynchronous operation.</returns>
        private static async UniTask AwaitSignInWithTimeout(float timeoutSeconds)
        {
            var timeoutTask = UniTask.Delay(TimeSpan.FromSeconds(timeoutSeconds));
            var signInTask = _signInCompletionSource.Task;
            var (hasSignInCompleted, _) = await UniTask.WhenAny(signInTask, timeoutTask);

            if (!hasSignInCompleted)
            {
                HandleSignInFail($"Game Center Sign-In timeout after {timeoutSeconds} seconds");
            }
        }

        /// <summary>
        /// Handles Game Center sign-in flow failure.
        /// </summary>
        /// <param name="message">The failure description message.</param>
        private static void HandleSignInFail(string message)
        {
            if (_isFinalized)
            {
                Debug.LogWarning("[LoginFlow][FirebaseGameCenterLoginProvider.HandleSignInFail] Duplicate failure ignored. " + message + " " + BuildTrackingSummary());
                return;
            }

            var detail = string.IsNullOrEmpty(_lastCredentialFailureDetail)
                ? string.Empty
                : " LastCredentialFailure=" + _lastCredentialFailureDetail;
            Debug.LogWarning("[LoginFlow][FirebaseGameCenterLoginProvider.HandleSignInFail] " + message + detail + " " + BuildTrackingSummary());
            FinalizeFail();
        }

        /// <summary>
        /// Finalizes the sign-in flow with success status.
        /// </summary>
        private static void FinalizeSuccess()
        {
            if (_isFinalized)
            {
                Debug.Log("[LoginFlow][FirebaseGameCenterLoginProvider.FinalizeSuccess] Already finalized. Skip.");
                return;
            }

            SetStage("finalizing_success");
            Debug.Log("[LoginFlow][FirebaseGameCenterLoginProvider.FinalizeSuccess] Finalizing success.");
            _isFinalized = true;
            try
            {
                _onSuccess?.Invoke();
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[LoginFlow][FirebaseGameCenterLoginProvider.FinalizeSuccess] Success callback threw exception: " + FormatExceptionDetails(ex));
            }

            _signInCompletionSource?.TrySetResult(true);
        }

        /// <summary>
        /// Finalizes the sign-in flow with failure status.
        /// </summary>
        private static void FinalizeFail()
        {
            if (_isFinalized)
            {
                Debug.Log("[LoginFlow][FirebaseGameCenterLoginProvider.FinalizeFail] Already finalized. Skip.");
                return;
            }

            SetStage("finalizing_failure");
            Debug.Log("[LoginFlow][FirebaseGameCenterLoginProvider.FinalizeFail] Finalizing failure.");
            _isFinalized = true;
            try
            {
                _onFail?.Invoke();
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[LoginFlow][FirebaseGameCenterLoginProvider.FinalizeFail] Fail callback threw exception: " + FormatExceptionDetails(ex));
            }

            _signInCompletionSource?.TrySetResult(false);
        }

        #endregion
    }
}
#endif