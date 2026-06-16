using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Ezg.Feature.LocalNotification
{
    /// <summary>
    ///     Bridge class for interfacing with native Android notifications and handling texture conversion.
    /// </summary>
    public class NotificationNativeBridge
    {
        #region Initialize

        static NotificationNativeBridge()
        {
            if (Application.platform == RuntimePlatform.Android)
                try
                {
                    _javaClass = new AndroidJavaClass(JavaClassName);
                    using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                    using (var context = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
                    {
                        _javaClass.CallStatic("init", context);
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError($"[NotificationNativeBridge] Error initializing: {e.Message}");
                }
        }

        #endregion

        #region Public Methods

        /// <summary>
        ///     Show a native Android notification.
        /// </summary>
        /// <param name="title">Title of the notification</param>
        /// <param name="message">Content text</param>
        /// <param name="smallIconName">The name of the icon in android resources (e.g. "app_icon")</param>
        /// <param name="largeIcon">Optional Texture2D to show as a large icon/big picture</param>
        public static void ShowNotification(string title, string message, string smallIconName = "app_icon",
            Texture2D largeIcon = null)
        {
            if (Application.platform != RuntimePlatform.Android)
            {
                Debug.Log($"[NotificationNativeBridge] Mock Notification: {title} - {message}");
                return;
            }

            if (_javaClass == null)
            {
                Debug.LogError("[NotificationNativeBridge] Java class not initialized.");
                return;
            }

            try
            {
                byte[] largeIconBytes = null;
                if (largeIcon != null) largeIconBytes = GetReadableTextureBytes(largeIcon);

                using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var context = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
                {
                    _javaClass.CallStatic("showNotification", context, title, message, smallIconName, largeIconBytes);
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[NotificationNativeBridge] Error showing notification: {e.Message}");
            }
        }

        #endregion

        #region Private Methods

        /// <summary>
        ///     Converts a Texture2D into a byte array by rendering it through a temporary RenderTexture to handle unreadable or
        ///     compressed textures.
        /// </summary>
        /// <param name="source">The source Texture2D.</param>
        /// <returns>A byte array of the encoded PNG texture.</returns>
        private static byte[] GetReadableTextureBytes(Texture2D source)
        {
            if (source == null) return null;

            // Use RenderTexture to read back ANY texture (readable or not, compressed or not)
            var tmp = RenderTexture.GetTemporary(
                source.width,
                source.height,
                0,
                RenderTextureFormat.Default,
                RenderTextureReadWrite.Linear);

            Graphics.Blit(source, tmp);
            var previous = RenderTexture.active;
            RenderTexture.active = tmp;

            var readableText = new Texture2D(source.width, source.height);
            readableText.ReadPixels(new Rect(0, 0, tmp.width, tmp.height), 0, 0);
            readableText.Apply();

            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(tmp);

            var bytes = readableText.EncodeToPNG();
            Object.Destroy(readableText);

            return bytes;
        }

        #endregion

        #region Fields

        private const string JavaClassName = "com.mynoti.lib.NotificationHelper";
        private static readonly AndroidJavaClass _javaClass;

        #endregion
    }
}