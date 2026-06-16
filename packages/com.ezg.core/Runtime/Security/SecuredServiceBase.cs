using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using Debug = UnityEngine.Debug;
using Random = UnityEngine.Random;

namespace Ezg.Core.Security
{
    /// <summary>
    ///     Base class that provides security features for static services.
    /// </summary>
    public abstract class SecuredServiceBase
    {
        #region Token Management

        private static readonly Dictionary<string, ServiceSecurityContext> _serviceContexts = new();

        protected const int TOKEN_LIFETIME_SECONDS = 300; // 5 phÃºt
        protected const int MIN_ACTION_INTERVAL_SECONDS = 2; // Chá»‘ng spam

        private class ServiceSecurityContext
        {
            public long LastActionTimestamp;
            public string SessionToken;
            public long TokenTimestamp;
        }

        #endregion

        #region Protected Security Methods

        /// <summary>
        ///     Initializes the security context for a service.
        /// </summary>
        protected static void InitializeSecurity(string serviceName)
        {
            if (!_serviceContexts.ContainsKey(serviceName))
                _serviceContexts[serviceName] = new ServiceSecurityContext();
            RegenerateToken(serviceName);
        }

        /// <summary>
        ///     Requests a token for performing an action.
        /// </summary>
        protected static string RequestActionToken(string serviceName, string[] allowedCallers)
        {
            if (!ValidateCaller(allowedCallers))
            {
                LogSuspiciousActivity(serviceName, "RequestActionToken - Invalid Caller");
                return null;
            }

            if (!_serviceContexts.ContainsKey(serviceName)) InitializeSecurity(serviceName);

            var context = _serviceContexts[serviceName];

            if (GetCurrentTimestamp() - context.TokenTimestamp > TOKEN_LIFETIME_SECONDS) RegenerateToken(serviceName);

            return context.SessionToken;
        }

        /// <summary>
        ///     Validates the token and security conditions for a secured action.
        /// </summary>
        protected static bool ValidateSecuredAction(
            string serviceName,
            string securityToken,
            string[] allowedCallers,
            bool checkTiming = true)
        {
            if (!ValidateToken(serviceName, securityToken))
            {
                LogSuspiciousActivity(serviceName, "ValidateSecuredAction - Invalid Token");
                return false;
            }

            if (!ValidateCaller(allowedCallers))
            {
                LogSuspiciousActivity(serviceName, "ValidateSecuredAction - Invalid Caller");
                return false;
            }

            if (checkTiming && !ValidateTiming(serviceName))
            {
                LogSuspiciousActivity(serviceName, "ValidateSecuredAction - Invalid Timing (Spam)");
                return false;
            }

            if (_serviceContexts.ContainsKey(serviceName))
                _serviceContexts[serviceName].LastActionTimestamp = GetCurrentTimestamp();

            return true;
        }

        /// <summary>
        ///     Invalidates the token after use.
        /// </summary>
        protected static void InvalidateToken(string serviceName)
        {
            RegenerateToken(serviceName);
        }

        #endregion

        #region Private Security Implementation

        private static void RegenerateToken(string serviceName)
        {
            if (!_serviceContexts.ContainsKey(serviceName))
                _serviceContexts[serviceName] = new ServiceSecurityContext();

            var context = _serviceContexts[serviceName];
            context.TokenTimestamp = GetCurrentTimestamp();

            var data =
                $"{serviceName}_{SystemInfo.deviceUniqueIdentifier}_{context.TokenTimestamp}_{Random.Range(0, 999999)}";
            context.SessionToken = ComputeHash(data);
        }

        private static bool ValidateToken(string serviceName, string token)
        {
            if (string.IsNullOrEmpty(token)) return false;

            if (!_serviceContexts.ContainsKey(serviceName)) return false;

            var context = _serviceContexts[serviceName];

            if (string.IsNullOrEmpty(context.SessionToken)) return false;

            if (token != context.SessionToken) return false;

            if (GetCurrentTimestamp() - context.TokenTimestamp > TOKEN_LIFETIME_SECONDS) return false;

            return true;
        }

        private static bool ValidateCaller(string[] allowedCallers)
        {
            if (allowedCallers == null || allowedCallers.Length == 0) return true;

            try
            {
                var stackTrace = new StackTrace();

                for (var i = 1; i < Math.Min(stackTrace.FrameCount, 6); i++)
                {
                    var frame = stackTrace.GetFrame(i);
                    var method = frame?.GetMethod();
                    if (method == null) continue;

                    var declaringType = method.DeclaringType;
                    if (declaringType == null) continue;

                    var typeName = declaringType.Name;

                    foreach (var allowed in allowedCallers)
                        if (typeName == allowed || typeName.StartsWith(allowed))
                            return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Security] ValidateCaller error: {ex.Message}");
                return false;
            }
        }

        private static bool ValidateTiming(string serviceName)
        {
            if (!_serviceContexts.ContainsKey(serviceName)) return true;

            var context = _serviceContexts[serviceName];

            if (context.LastActionTimestamp > 0)
            {
                var timeSinceLastAction = GetCurrentTimestamp() - context.LastActionTimestamp;
                if (timeSinceLastAction < MIN_ACTION_INTERVAL_SECONDS) return false;
            }

            return true;
        }

        private static string ComputeHash(string input)
        {
            using (var sha256 = SHA256.Create())
            {
                var bytes = Encoding.UTF8.GetBytes(input);
                var hash = sha256.ComputeHash(bytes);
                return Convert.ToBase64String(hash);
            }
        }

        private static long GetCurrentTimestamp()
        {
            return DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        }

        #endregion

        #region Logging & Monitoring

        protected static void LogSuspiciousActivity(string serviceName, string action)
        {
            Debug.LogWarning($"[SECURITY] {serviceName} - Suspicious activity: {action}");
        }

        protected static void LogSecurityEvent(string serviceName, string eventName,
            Dictionary<string, object> parameters = null)
        {
            Debug.Log($"[SECURITY] {serviceName} - {eventName}");
        }

        #endregion

        #region Utility Methods

        /// <summary>
        ///     Clears all security contexts, typically used during logout or reset flows.
        /// </summary>
        protected static void ClearAllSecurityContexts()
        {
            _serviceContexts.Clear();
        }

        /// <summary>
        ///     Clears the security context for a specific service.
        /// </summary>
        protected static void ClearSecurityContext(string serviceName)
        {
            if (_serviceContexts.ContainsKey(serviceName)) _serviceContexts.Remove(serviceName);
        }

        #endregion
    }
}