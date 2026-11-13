using UnityEngine;

namespace BodycamHorror.UI
{
    /// <summary>
    /// Player settings that outlive a session (PlayerPrefs) and are applied as soon as the game starts.
    /// Changed from the pause menu's settings page.
    /// </summary>
    public static class GameSettings
    {
        const string VSyncKey = "settings.vsync";
        const string FpsCapKey = "settings.fpsCap";
        const string ShowFpsKey = "settings.showFps";
        const string EarRingingKey = "settings.earRinging";

        /// <summary>The frame counter in the top right corner (FpsCounter).</summary>
        public static bool ShowFps
        {
            get => showFps ??= PlayerPrefs.GetInt(ShowFpsKey, 1) != 0;   // read every frame by the counter: cached
            set { showFps = value; PlayerPrefs.SetInt(ShowFpsKey, value ? 1 : 0); PlayerPrefs.Save(); }
        }
        static bool? showFps;

        /// <summary>The ringing in the ears at high fear (PlayerFearAudio). Off for players it bothers.</summary>
        public static bool EarRinging
        {
            get => earRinging ??= PlayerPrefs.GetInt(EarRingingKey, 1) != 0;   // read every frame: cached
            set { earRinging = value; PlayerPrefs.SetInt(EarRingingKey, value ? 1 : 0); PlayerPrefs.Save(); }
        }
        static bool? earRinging;

        /// <summary>Frame caps offered while V-Sync is off; 0 = none.</summary>
        public static readonly int[] FpsCaps = { 0, 60, 120, 144, 165, 240 };

        /// <summary>Wait for the monitor's refresh before showing each frame: no tearing, a frame more latency.</summary>
        public static bool VSync
        {
            get => PlayerPrefs.GetInt(VSyncKey, 1) != 0;
            set { PlayerPrefs.SetInt(VSyncKey, value ? 1 : 0); PlayerPrefs.Save(); Apply(); }
        }

        /// <summary>Frames per second at most while V-Sync is off (0 = as many as the machine gives).</summary>
        public static int FpsCap
        {
            get => PlayerPrefs.GetInt(FpsCapKey, 144);
            set { PlayerPrefs.SetInt(FpsCapKey, Mathf.Max(0, value)); PlayerPrefs.Save(); Apply(); }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        public static void Apply()
        {
            bool vsync = VSync;
            QualitySettings.vSyncCount = vsync ? 1 : 0;
            // with V-Sync on the monitor sets the pace and targetFrameRate is ignored
            int cap = FpsCap;
            Application.targetFrameRate = vsync || cap <= 0 ? -1 : cap;
        }
    }
}
