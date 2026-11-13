using System.Runtime.InteropServices;
using UnityEngine;

namespace StillHere
{
    /// Gentle touch feedback on phones, so you feel what you touch.
    ///   iOS app:      the Taptic Engine (UIImpactFeedbackGenerator), via Plugins/iOS/StillHereHaptics.mm
    ///   Android app:  short vibrations with controlled strength (VibrationEffect)
    ///   Web:          navigator.vibrate on Android browsers; on iPhone Safari the page's best-effort haptic tick
    /// Gamepads keep their own rumble (InputHub.Rumble).
    public static class Haptics
    {
        public enum Feel { Tick = 0, Soft = 1, Firm = 2, Knock = 3, Heavy = 4, Purr = 5, Burn = 6 }

        public static bool enabled = true;
        static float last = -1;

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] static extern void SH_Haptic(int kind);
#elif UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")] static extern void SH_iOSHaptic(int kind);
#endif

        public static void Play(Feel f)
        {
            if (!enabled) return;
            float now = Time.unscaledTime;
            if (f != Feel.Purr && now - last < 0.05f) return;   // never a buzz, only distinct taps
            last = now;
#if UNITY_WEBGL && !UNITY_EDITOR
            try { SH_Haptic((int)f); } catch { }
#elif UNITY_IOS && !UNITY_EDITOR
            try { SH_iOSHaptic((int)f); } catch { }
#elif UNITY_ANDROID && !UNITY_EDITOR
            Android(f);
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        static AndroidJavaObject vibrator;
        static void Android(Feel f)
        {
            try
            {
                if (vibrator == null)
                {
                    using (var up = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                    using (var act = up.GetStatic<AndroidJavaObject>("currentActivity"))
                        vibrator = act.Call<AndroidJavaObject>("getSystemService", "vibrator");
                }
                long[] t; int[] a;
                switch (f)
                {
                    case Feel.Tick: t = new long[] { 0, 7 }; a = new[] { 0, 50 }; break;
                    case Feel.Soft: t = new long[] { 0, 12 }; a = new[] { 0, 80 }; break;
                    case Feel.Firm: t = new long[] { 0, 10 }; a = new[] { 0, 150 }; break;
                    case Feel.Knock: t = new long[] { 0, 24 }; a = new[] { 0, 180 }; break;
                    case Feel.Heavy: t = new long[] { 0, 60, 40, 90 }; a = new[] { 0, 220, 0, 160 }; break;
                    case Feel.Purr: t = new long[] { 0, 12, 28, 12, 28, 12, 28, 12, 28, 12 }; a = new[] { 0, 60, 0, 70, 0, 60, 0, 70, 0, 55 }; break;
                    default: t = new long[] { 0, 40, 30, 40 }; a = new[] { 0, 255, 0, 200 }; break;
                }
                using (var ve = new AndroidJavaClass("android.os.VibrationEffect"))
                using (var effect = ve.CallStatic<AndroidJavaObject>("createWaveform", t, a, -1))
                    vibrator.Call("vibrate", effect);
            }
            catch { }
        }
#endif
    }
}
