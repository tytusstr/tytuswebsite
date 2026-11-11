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

    /// <summary>
    /// Remembers the menu settings (controls, look sensitivity, graphics, hints) on this device,
    /// so they're the same the next time the game or website is opened. In a browser they're
    /// kept in that browser's storage for this website.
    /// </summary>
    static class SavedSettings
    {
        public const string Mode = "GlassRoom.Mode";
        public const string Sensitivity = "GlassRoom.Sensitivity";
        public const string LowGraphics = "GlassRoom.LowGraphics";
        public const string Hints = "GlassRoom.Hints";
        public const string Mouse = "GlassRoom.Mouse";
        public const string FieldOfView = "GlassRoom.FieldOfView";

        public static void SetInt(string key, int value)
        {
            if (PlayerPrefs.HasKey(key) && PlayerPrefs.GetInt(key) == value) return;
            PlayerPrefs.SetInt(key, value);
            PlayerPrefs.Save();
        }

        public static void SetFloat(string key, float value)
        {
            if (PlayerPrefs.HasKey(key) && Mathf.Approximately(PlayerPrefs.GetFloat(key), value)) return;
            PlayerPrefs.SetFloat(key, value);
            PlayerPrefs.Save();
        }

        // Saved settings are put back before the scene loads (graphics: see GraphicsQuality).
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Load()
        {
            if (PlayerPrefs.HasKey(Mode))
                InputModeManager.Mode = PlayerPrefs.GetInt(Mode) == 1 ? ControlMode.Touchscreen : ControlMode.KeyboardMouse;
            if (PlayerPrefs.HasKey(Sensitivity))
                LookSettings.SliderPosition = Mathf.Clamp01(PlayerPrefs.GetFloat(Sensitivity));
            if (PlayerPrefs.HasKey(Hints))
                HintSettings.Enabled = PlayerPrefs.GetInt(Hints) != 0;
            if (PlayerPrefs.HasKey(FieldOfView))
                FovSettings.SliderPosition = Mathf.Clamp01(PlayerPrefs.GetFloat(FieldOfView));
            if (PlayerPrefs.HasKey(Mouse))
                MouseSettings.Enabled = PlayerPrefs.GetInt(Mouse) != 0;
        }
    }

    /// <summary>
    /// The current control mode. The first game starts in computer mode; after that, whichever
    /// mode was last chosen.
    /// </summary>
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
                SavedSettings.SetInt(SavedSettings.Mode, value == ControlMode.Touchscreen ? 1 : 0);
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
    /// Look sensitivity from the menu slider, which starts in the middle until it's moved (then it's remembered). Each mode has its
    /// own range: computer 0.2× / 1.5× (middle) / 10×, phone 0.1× / 0.5× (middle) / 3×.
    /// </summary>
    public static class LookSettings
    {
        const float ComputerMin = 0.2f, ComputerMiddle = 1.5f, ComputerMax = 10f;
        const float PhoneMin = 0.1f, PhoneMiddle = 0.5f, PhoneMax = 3f;

        /// <summary>Slider position from 0 to 1; 0.5 is the middle.</summary>
        public static float SliderPosition
        {
            get => s_Slider;
            set
            {
                s_Slider = value;
                SavedSettings.SetFloat(SavedSettings.Sensitivity, value);
            }
        }
        static float s_Slider = 0.5f;

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
        static void ResetStatics() => s_Slider = 0.5f;
    }

    /// <summary>
    /// Field of view from the menu slider: how wide you see, in degrees. It starts in the middle
    /// (68°, the original view) the first time, then whatever was last chosen is remembered.
    /// </summary>
    public static class FovSettings
    {
        const float Min = 50f, Middle = 68f, Max = 95f;
        static float s_Slider = 0.5f;

        /// <summary>Slider position from 0 to 1; 0.5 is the middle.</summary>
        public static float SliderPosition
        {
            get => s_Slider;
            set
            {
                s_Slider = value;
                SavedSettings.SetFloat(SavedSettings.FieldOfView, value);
            }
        }

        /// <summary>The camera's vertical field of view in degrees.</summary>
        public static float FieldOfView
        {
            get
            {
                float t = Mathf.Clamp01(s_Slider);
                return t < 0.5f ? Mathf.Lerp(Min, Middle, t * 2f) : Mathf.Lerp(Middle, Max, (t - 0.5f) * 2f);
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => s_Slider = 0.5f;
    }

    /// <summary>Whether the key hints at the bottom of the screen are shown. On until switched off (then remembered).</summary>
    public static class HintSettings
    {
        static bool s_Enabled = true;

        public static bool Enabled
        {
            get => s_Enabled;
            set
            {
                s_Enabled = value;
                SavedSettings.SetInt(SavedSettings.Hints, value ? 1 : 0);
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => s_Enabled = true;
    }

    /// <summary>
    /// The "Mouse?" switch in computer mode: whether the player has a mouse with a right button.
    /// Off (the default, for laptop trackpads) the hints say "Click" and N skips songs; on, they
    /// mention left and right click. Remembered once changed.
    /// </summary>
    public static class MouseSettings
    {
        static bool s_Enabled;

        public static bool Enabled
        {
            get => s_Enabled;
            set
            {
                s_Enabled = value;
                SavedSettings.SetInt(SavedSettings.Mouse, value ? 1 : 0);
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => s_Enabled = false;
    }

    /// <summary>
    /// Graphics: Full (sharp, every effect) or Low (for phones or anything that lags). Low uses the
    /// project's Mobile quality level (half resolution, no shadows), turns off the mirror floor
    /// and the glow around lights. The first game starts on Full; after that, whichever was last chosen.
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
                if (!s_Applied) Set(SavedLow);
                return s_Low;
            }
            set => Set(value);
        }

        static void Set(bool low)
        {
            bool changed = s_Low != low || !s_Applied;
            s_Low = low;
            s_Applied = true;
            if (changed) SavedSettings.SetInt(SavedSettings.LowGraphics, low ? 1 : 0);
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

        // Full unless Low was chosen last time.
        static bool SavedLow => PlayerPrefs.GetInt(SavedSettings.LowGraphics, 0) != 0;

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
            Set(SavedLow);
            SceneManager.sceneLoaded += (_, _) => ApplyToScene();
        }
    }
}
