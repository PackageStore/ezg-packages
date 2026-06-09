using UnityEngine;

public enum HapticTypes { Selection, Success, Warning, Failure, LightImpact, MediumImpact, HeavyImpact }
public class AndroidTaptic {

#region Fields
    public static long LightDuration = 20;
    public static long MediumDuration = 40;
    public static long HeavyDuration = 80;
    public static int LightAmplitude = 40;
    public static int MediumAmplitude = 120;
    public static int HeavyAmplitude = 255;
    private static int _sdkVersion = -1;
    private static long[] _successPattern = { 0, LightDuration, LightDuration, HeavyDuration };
    private static int[] _successPatternAmplitude = { 0, LightAmplitude, 0, HeavyAmplitude };
    private static long[] _warningPattern = { 0, HeavyDuration, LightDuration, MediumDuration };
    private static int[] _warningPatternAmplitude = { 0, HeavyAmplitude, 0, MediumAmplitude };
    private static long[] _failurePattern = { 0, MediumDuration, LightDuration, MediumDuration, LightDuration, HeavyDuration, LightDuration, LightDuration };
    private static int[] _failurePatternAmplitude = { 0, MediumAmplitude, 0, MediumAmplitude, 0, HeavyAmplitude, 0, LightAmplitude };

#if UNITY_ANDROID && !UNITY_EDITOR
    private static AndroidJavaClass UnityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
    private static AndroidJavaObject CurrentActivity = UnityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
    private static AndroidJavaObject AndroidVibrator = CurrentActivity.Call<AndroidJavaObject>("getSystemService", "vibrator");
    private static AndroidJavaClass VibrationEffectClass;
    private static AndroidJavaObject VibrationEffect;
    private static int DefaultAmplitude;
#else
    private static AndroidJavaClass UnityPlayer;
    private static AndroidJavaObject CurrentActivity;
    private static AndroidJavaObject AndroidVibrator = null;
    private static AndroidJavaClass VibrationEffectClass = null;
    private static AndroidJavaObject VibrationEffect;
    private static int DefaultAmplitude;
#endif
#endregion

#region Initialize
#endregion

#region Public Methods
    /// <summary>
    /// Triggers a default vibration on the Android device.
    /// </summary>
    public static void Vibrate() {
        AndroidVibrate(MediumDuration);
    }

    /// <summary>
    /// Triggers a haptic feedback of the specified type.
    /// </summary>
    /// <param name="type">The type of haptic feedback to trigger.</param>
    public static void Haptic(HapticTypes type) {
        try {
            switch (type) {
                case HapticTypes.Selection:
                    AndroidVibrate(LightDuration, LightAmplitude);
                    break;

                case HapticTypes.Success:
                    AndroidVibrate(_successPattern, _successPatternAmplitude, -1);
                    break;

                case HapticTypes.Warning:
                    AndroidVibrate(_warningPattern, _warningPatternAmplitude, -1);
                    break;

                case HapticTypes.Failure:
                    AndroidVibrate(_failurePattern, _failurePatternAmplitude, -1);
                    break;

                case HapticTypes.LightImpact:
                    AndroidVibrate(LightDuration, LightAmplitude);
                    break;

                case HapticTypes.MediumImpact:
                    AndroidVibrate(MediumDuration, MediumAmplitude);
                    break;

                case HapticTypes.HeavyImpact:
                    AndroidVibrate(HeavyDuration, HeavyAmplitude);
                    break;
            }
        } catch (System.NullReferenceException e) {
            Debug.Log(e.StackTrace);
        }
    }

    /// <summary>
    /// Triggers an Android vibration for the specified duration.
    /// </summary>
    /// <param name="milliseconds">The duration of the vibration in milliseconds.</param>
    public static void AndroidVibrate(long milliseconds) {
        if (AndroidVibrator != null) {
            AndroidVibrator.Call("vibrate", milliseconds);
        }
    }

    /// <summary>
    /// Triggers an Android vibration for the specified duration with a specific amplitude.
    /// </summary>
    /// <param name="milliseconds">The duration of the vibration in milliseconds.</param>
    /// <param name="amplitude">The amplitude of the vibration (1-255).</param>
    public static void AndroidVibrate(long milliseconds, int amplitude) {
        if ((AndroidSDKVersion() < 26)) {
            AndroidVibrate(milliseconds);
        } else {
            VibrationEffectClassInitialization();
            if (VibrationEffectClass != null) {
                VibrationEffect = VibrationEffectClass.CallStatic<AndroidJavaObject>("createOneShot", new object[] { milliseconds, amplitude });
                if (VibrationEffect != null && AndroidVibrator != null) {
                    AndroidVibrator.Call("vibrate", VibrationEffect);
                } else {
                    AndroidVibrate(milliseconds);
                }
            } else {
                AndroidVibrate(milliseconds);
            }
        }
    }

    /// <summary>
    /// Triggers an Android vibration pattern.
    /// </summary>
    /// <param name="pattern">The pattern of vibration durations.</param>
    /// <param name="repeat">The index in the pattern at which to repeat, or -1 for no repeat.</param>
    public static void AndroidVibrate(long[] pattern, int repeat) {
        if ((AndroidSDKVersion() < 26)) {
            if (AndroidVibrator != null) {
                AndroidVibrator.Call("vibrate", pattern, repeat);
            }
        } else {
            VibrationEffectClassInitialization();
            if (VibrationEffectClass != null) {
                VibrationEffect = VibrationEffectClass.CallStatic<AndroidJavaObject>("createWaveform", new object[] { pattern, repeat });
                if (VibrationEffect != null && AndroidVibrator != null) {
                    AndroidVibrator.Call("vibrate", VibrationEffect);
                }
            }
        }
    }

    /// <summary>
    /// Triggers an Android vibration pattern with varying amplitudes.
    /// </summary>
    /// <param name="pattern">The pattern of vibration durations.</param>
    /// <param name="amplitudes">The pattern of vibration amplitudes.</param>
    /// <param name="repeat">The index in the pattern at which to repeat, or -1 for no repeat.</param>
    public static void AndroidVibrate(long[] pattern, int[] amplitudes, int repeat) {
        if ((AndroidSDKVersion() < 26)) {
            if (AndroidVibrator != null) {
                AndroidVibrator.Call("vibrate", pattern, repeat);
            }
        } else {
            VibrationEffectClassInitialization();
            if (VibrationEffectClass != null) {
                VibrationEffect = VibrationEffectClass.CallStatic<AndroidJavaObject>("createWaveform", new object[] { pattern, amplitudes, repeat });
                if (VibrationEffect != null && AndroidVibrator != null) {
                    AndroidVibrator.Call("vibrate", VibrationEffect);
                }
            }
        }
    }

    /// <summary>
    /// Cancels any currently playing Android vibrations.
    /// </summary>
    public static void AndroidCancelVibrations() {
        AndroidVibrator.Call("cancel");
    }

    /// <summary>
    /// Gets the current Android SDK version.
    /// </summary>
    /// <returns>The API level of the Android SDK.</returns>
    public static int AndroidSDKVersion() {
        if (_sdkVersion == -1) {
            int apiLevel = int.Parse(SystemInfo.operatingSystem.Substring(SystemInfo.operatingSystem.IndexOf("-") + 1, 3));
            _sdkVersion = apiLevel;
            return apiLevel;
        } else {
            return _sdkVersion;
        }
    }
#endregion

#region Private Methods
    /// <summary>
    /// Triggers a default device vibration.
    /// </summary>
    void Vib() {
#if UNITY_IOS || UNITY_ANDROID
        Handheld.Vibrate();
#endif
    }

    /// <summary>
    /// Initializes the VibrationEffect class if it hasn't been initialized yet.
    /// </summary>
    private static void VibrationEffectClassInitialization() {
        if (VibrationEffectClass == null) { VibrationEffectClass = new AndroidJavaClass("android.os.VibrationEffect"); }
    }
#endregion

#region Event Handlers
#endregion

}