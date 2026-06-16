using System.IO;
using UnityEngine;

namespace Ezg.Core.Security
{
    /// <summary>
    ///     Detects whether the device is rooted or running on an emulator.
    /// </summary>
    public class DetectDevice : MonoBehaviour
    {
        /// <summary>
        ///     Returns a diagnostic string that summarizes the current device and install state.
        /// </summary>
        /// <returns>A slash-separated string containing device and application information.</returns>
        public static string GetData()
        {
            var result = "";
            if (Application.platform == RuntimePlatform.Android)
            {
                var osBuild = new AndroidJavaClass("android.os.Build");
                var brand = osBuild.GetStatic<string>("BRAND");
                var fingerPrint = osBuild.GetStatic<string>("FINGERPRINT");
                var model = osBuild.GetStatic<string>("MODEL");
                var menufacturer = osBuild.GetStatic<string>("MANUFACTURER");
                var device = osBuild.GetStatic<string>("DEVICE");
                var product = osBuild.GetStatic<string>("PRODUCT");

                result += Application.installerName;
                result += "/";
                result += Application.installMode.ToString();
                result += "/";
                result += Application.buildGUID;
                result += "/";
                result += "Genuine :" + Application.genuine;
                result += "/";
                result += "Rooted : " + IsRooted();
                result += "/";
                result += "Emulator : " + IsEmulator();
                result += "/";
                result += "Model: " + model;
                result += "/";
                result += "Menufacturer : " + menufacturer;
                result += "/";
                result += "Device : " + device;
                result += "/";
                result += "Fingerprint : " + fingerPrint;
                result += "/";
                result += "Product : " + product;
            }
            else
            {
                result += Application.installerName;
                result += "/";
                result += Application.installMode.ToString();
                result += "/";
                result += Application.buildGUID;
                result += "/";
                result += "Genuine :" + Application.genuine;
                result += "/";
            }

            return result;
        }

        /// <summary>
        ///     Checks whether the current Android device appears to be rooted.
        /// </summary>
        /// <returns>True if a common root indicator is found; otherwise, false.</returns>
        public static bool IsRooted()
        {
            var isRoot = false;
            if (Application.platform == RuntimePlatform.Android)
            {
                if (IsRootedPrivate("/system/bin/su"))
                    isRoot = true;
                if (IsRootedPrivate("/system/xbin/su"))
                    isRoot = true;
                if (IsRootedPrivate("/system/app/SuperUser.apk"))
                    isRoot = true;
                if (IsRootedPrivate("/data/data/com.noshufou.android.su"))
                    isRoot = true;
                if (IsRootedPrivate("/sbin/su"))
                    isRoot = true;
            }

            return isRoot;
        }

        /// <summary>
        ///     Checks whether a root indicator file exists at the specified path.
        /// </summary>
        /// <param name="path">The file path to inspect.</param>
        /// <returns>True if the file exists; otherwise, false.</returns>
        public static bool IsRootedPrivate(string path)
        {
            return File.Exists(path);
        }

        /// <summary>
        ///     Checks whether the current runtime looks like an emulator.
        /// </summary>
        /// <returns>True if emulator characteristics are detected; otherwise, false.</returns>
        public static bool IsEmulator()
        {
            if (Application.platform == RuntimePlatform.Android)
            {
                AndroidJavaClass osBuild;
                osBuild = new AndroidJavaClass("android.os.Build");
                var fingerPrint = osBuild.GetStatic<string>("FINGERPRINT");
                var model = osBuild.GetStatic<string>("MODEL");
                var menufacturer = osBuild.GetStatic<string>("MANUFACTURER");
                var brand = osBuild.GetStatic<string>("BRAND");
                var device = osBuild.GetStatic<string>("DEVICE");
                var product = osBuild.GetStatic<string>("PRODUCT");

                return fingerPrint.Contains("generic")
                       || fingerPrint.Contains("unknown")
                       || model.Contains("google_sdk")
                       || model.Contains("Emulator")
                       || model.Contains("Android SDK built for x86")
                       || menufacturer.Contains("Genymotion")
                       || (brand.Contains("generic") && device.Contains("generic"))
                       || product.Equals("google_sdk")
                       || product.Equals("unknown");
            }

            return Application.platform == RuntimePlatform.OSXEditor;
        }
    }
}