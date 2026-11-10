using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace GlassRoom
{
    public enum ControlMode
    {
        KeyboardMouse,
        Touchscreen
    }

    /// <summary>The current control mode. Every game starts in computer mode.</summary>
    public static class InputModeManager
    {
        static ControlMode s_Mode = ControlMode.KeyboardMouse;

        public static event Action<ControlMode> ModeChanged;

        public static ControlMode Mode
        {
            get => s_Mode;
            set
            {
                if (s_Mode == value)
                    return;
                s_Mode = value;
                ModeChanged?.Invoke(value);
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            s_Mode = ControlMode.KeyboardMouse;
            ModeChanged = null;
        }
    }

    /// <summary>
    /// Look sensitivity from the menu slider, which always starts in the middle. Each mode has its
    /// own range: computer 0.2× / 2.5× (middle) / 10×, phone 0.1× / 0.5× (middle) / 3×.
    /// </summary>
    public static class LookSettings
    {
        const float ComputerMin = 0.2f, ComputerMiddle = 2.5f, ComputerMax = 10f;
        const float PhoneMin = 0.1f, PhoneMiddle = 0.5f, PhoneMax = 3f;

        /// <summary>Slider position from 0 to 1; 0.5 is the middle.</summary>
        public static float SliderPosition { get; set; } = 0.5f;

        /// <summary>The multiplier applied to mouse or touch looking in the current mode.</summary>
        public static float Sensitivity
        {
            get
            {
                float t = Mathf.Clamp01(SliderPosition);
                bool phone = InputModeManager.Mode == ControlMode.Touchscreen;
                float min = phone ? PhoneMin : ComputerMin;
                float mid = phone ? PhoneMiddle : ComputerMiddle;
                float max = phone ? PhoneMax : ComputerMax;
                // Lower half: slowest up to the middle. Upper half: middle up to the top.
                return t < 0.5f ? Mathf.Lerp(min, mid, t * 2f) : Mathf.Lerp(mid, max, (t - 0.5f) * 2f);
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => SliderPosition = 0.5f;
    }

    /// <summary>Whether the key hints at the bottom of the screen are shown. Every game starts with them on.</summary>
    public static class HintSettings
    {
        public static bool Enabled { get; set; } = true;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Enabled = true;
    }

    /// <summary>
    /// Graphics: Full (sharp, every effect) or Low (for phones or anything that lags). Low uses the
    /// project's Mobile quality level (half resolution, no shadows), turns off the mirror floor
    /// and the glow around lights. Phones start on Low and computers on Full; the menu switches it any time.
    /// </summary>
    public static class GraphicsQuality
    {
        static bool s_Low;
        static bool s_Applied;

        public static event Action<bool> Changed;

        public static bool Low
        {
            get
            {
                if (!s_Applied) Set(Application.isMobilePlatform);
                return s_Low;
            }
            set => Set(value);
        }

        static void Set(bool low)
        {
            bool changed = s_Low != low || !s_Applied;
            s_Low = low;
            s_Applied = true;
            int level = LevelNamed(low ? "Mobile" : "PC");
            if (level >= 0 && QualitySettings.GetQualityLevel() != level)
                QualitySettings.SetQualityLevel(level, true);
            ApplyToScene();
            if (changed) Changed?.Invoke(low);
        }

        // The glow (bloom) around bright lights is off on Low. Uses each volume's own copy of its
        // settings so the saved project settings never change.
        static void ApplyToScene()
        {
            foreach (var volume in UnityEngine.Object.FindObjectsByType<Volume>())
                if (volume.HasInstantiatedProfile() || volume.sharedProfile != null)
                    if (volume.profile.TryGet<Bloom>(out var bloom)) bloom.active = !s_Low;
        }

        static int LevelNamed(string name)
        {
            var names = QualitySettings.names;
            for (int i = 0; i < names.Length; i++)
                if (names[i] == name) return i;
            return -1;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            s_Applied = false;
            Changed = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void ApplyStartingQuality()
        {
            // In the editor, put back the quality level it had once Play stops.
            int original = QualitySettings.GetQualityLevel();
            Application.quitting += () => QualitySettings.SetQualityLevel(original, true);
            Set(Application.isMobilePlatform);
            SceneManager.sceneLoaded += (_, _) => ApplyToScene();
        }
    }
}
