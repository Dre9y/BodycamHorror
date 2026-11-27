using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace BodycamHorror.UI
{
    /// <summary>
    /// Player settings that outlive a session (PlayerPrefs) and are applied as soon as the game starts.
    /// Changed from the settings screen (SettingsScreen: in the main menu and in the pause menu).
    /// </summary>
    public static class GameSettings
    {
        const string VSyncKey = "settings.vsync";
        const string FpsCapKey = "settings.fpsCap";
        const string ShowFpsKey = "settings.showFps";
        const string EarRingingKey = "settings.earRinging";
        const string FlashlightColorKey = "settings.flashlightColor";
        const string ResolutionKey = "settings.resolution";
        const string DisplayModeKey = "settings.displayMode";
        const string VolumeKey = "settings.masterVolume";
        const string SensitivityKey = "settings.mouseSensitivity";
        const string InvertYKey = "settings.invertY";
        const string UpscalerKey = "settings.upscaler";
        const string RenderScaleKey = "settings.renderScale";
        const string LanguageKey = "settings.language";

        /// <summary>The menus' language (<see cref="Loc"/>). English by default.</summary>
        public static Loc.Language Language
        {
            get => language ??= (Loc.Language)UnityEngine.Mathf.Clamp(PlayerPrefs.GetInt(LanguageKey, (int)Loc.Language.English), 0, Loc.Names.Length - 1);   // read every frame: cached
            set { language = value; PlayerPrefs.SetInt(LanguageKey, (int)value); PlayerPrefs.Save(); }
        }
        static Loc.Language? language;

        // ------------------------------------------------------------------ graphics: render scale and upscaling

        /// <summary>Upscalers offered. STP: Unity's temporal one (also smooths edges at 100%); FSR 1.0: AMD's spatial
        /// one (sharp, works only below 100%); off: the picture is drawn at the chosen scale and simply stretched.</summary>
        public static readonly (string ru, string en, UpscalingFilterSelection filter)[] Upscalers =
        {
            ("Выкл", "Off", UpscalingFilterSelection.Auto),
            ("STP", "STP", UpscalingFilterSelection.STP),
            ("FSR 1.0", "FSR 1.0", UpscalingFilterSelection.FSR),
        };

        /// <summary>Render scales offered (the usual FSR steps: performance, balanced, quality, ultra quality, native).</summary>
        public static readonly (float scale, string ru, string en)[] RenderScales =
        {
            (0.5f, "Производительность", "Performance"), (0.59f, "Баланс", "Balanced"), (0.67f, "Качество", "Quality"),
            (0.77f, "Ультра", "Ultra"), (1f, "Родное", "Native"),
        };

        /// <summary>Index into <see cref="Upscalers"/>. Off by default: the picture as it was made.</summary>
        public static int UpscalerIndex
        {
            get => Mathf.Clamp(PlayerPrefs.GetInt(UpscalerKey, 0), 0, Upscalers.Length - 1);
            set { PlayerPrefs.SetInt(UpscalerKey, Mathf.Clamp(value, 0, Upscalers.Length - 1)); PlayerPrefs.Save(); ApplyRendering(); }
        }

        /// <summary>Index into <see cref="RenderScales"/>. Native by default.</summary>
        public static int RenderScaleIndex
        {
            get => Mathf.Clamp(PlayerPrefs.GetInt(RenderScaleKey, RenderScales.Length - 1), 0, RenderScales.Length - 1);
            set { PlayerPrefs.SetInt(RenderScaleKey, Mathf.Clamp(value, 0, RenderScales.Length - 1)); PlayerPrefs.Save(); ApplyRendering(); }
        }

        static UniversalRenderPipelineAsset Pipeline =>
            (QualitySettings.renderPipeline ? QualitySettings.renderPipeline : GraphicsSettings.defaultRenderPipeline) as UniversalRenderPipelineAsset;

        static void ApplyRendering()
        {
            var a = Pipeline;
            if (!a) return;
#if UNITY_EDITOR
            Remember(a);
#endif
            a.renderScale = RenderScales[RenderScaleIndex].scale;
            a.upscalingFilter = Upscalers[UpscalerIndex].filter;
        }

#if UNITY_EDITOR
        // In the editor the pipeline asset is a project file: what play mode sets on it is put back on leaving play
        // mode, so trying the settings never changes the project.
        static UniversalRenderPipelineAsset remembered;
        static float rememberedScale;
        static UpscalingFilterSelection rememberedFilter;

        static void Remember(UniversalRenderPipelineAsset a)
        {
            if (remembered == a) return;
            remembered = a;
            rememberedScale = a.renderScale;
            rememberedFilter = a.upscalingFilter;
            UnityEditor.EditorApplication.playModeStateChanged -= PutBack;
            UnityEditor.EditorApplication.playModeStateChanged += PutBack;
        }

        static void PutBack(UnityEditor.PlayModeStateChange change)
        {
            if (change != UnityEditor.PlayModeStateChange.EnteredEditMode || !remembered) return;
            remembered.renderScale = rememberedScale;
            remembered.upscalingFilter = rememberedFilter;
            remembered = null;
        }
#endif

        // ------------------------------------------------------------------ graphics: the screen

        /// <summary>The ways the game can fill the screen, in the order the settings list them.</summary>
        public static readonly (string ru, string en, FullScreenMode mode)[] DisplayModes =
        {
            ("Полный экран", "Fullscreen", FullScreenMode.ExclusiveFullScreen),
            ("Полноэкранное окно", "Borderless window", FullScreenMode.FullScreenWindow),
            ("В окне", "Windowed", FullScreenMode.Windowed),
        };

        static readonly Vector2Int[] CommonResolutions =
        {
            new Vector2Int(1280, 720), new Vector2Int(1366, 768), new Vector2Int(1600, 900), new Vector2Int(1920, 1080),
            new Vector2Int(2560, 1080), new Vector2Int(2560, 1440), new Vector2Int(3440, 1440), new Vector2Int(3840, 2160),
        };

        /// <summary>Resolutions offered: the usual ones that fit the monitor, and the monitor's own, smallest first.</summary>
        public static Vector2Int[] Resolutions
        {
            get
            {
                var native = Native;
                var list = new System.Collections.Generic.List<Vector2Int>();
                foreach (var r in CommonResolutions)
                    if (r.x <= native.x && r.y <= native.y) list.Add(r);
                if (!list.Contains(native)) list.Add(native);
                list.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
                return list.ToArray();
            }
        }

        static Vector2Int Native
        {
            get
            {
                // the desktop's resolution (in the editor Display.main is only the game view), or the biggest mode the
                // monitor reports, whichever is larger
                var cur = Screen.currentResolution;
                int w = cur.width, h = cur.height;
                foreach (var r in Screen.resolutions)
                    if (r.width * r.height > w * h) { w = r.width; h = r.height; }
                return new Vector2Int(w, h);
            }
        }

        /// <summary>The chosen resolution; until one is chosen, the one the game runs at.</summary>
        public static Vector2Int Resolution
        {
            get
            {
                string s = PlayerPrefs.GetString(ResolutionKey, "");
                var parts = s.Split('x');
                if (parts.Length == 2 && int.TryParse(parts[0], out int w) && int.TryParse(parts[1], out int h)) return new Vector2Int(w, h);
                return Screen.fullScreenMode == FullScreenMode.Windowed ? new Vector2Int(Screen.width, Screen.height) : Native;
            }
            set { PlayerPrefs.SetString(ResolutionKey, value.x + "x" + value.y); PlayerPrefs.Save(); ApplyScreen(); }
        }

        /// <summary>Index into <see cref="DisplayModes"/>.</summary>
        public static int DisplayModeIndex
        {
            get
            {
                int i = PlayerPrefs.GetInt(DisplayModeKey, -1);
                if (i >= 0 && i < DisplayModes.Length) return i;
                for (int k = 0; k < DisplayModes.Length; k++) if (DisplayModes[k].mode == Screen.fullScreenMode) return k;
                return 1;
            }
            set { PlayerPrefs.SetInt(DisplayModeKey, Mathf.Clamp(value, 0, DisplayModes.Length - 1)); PlayerPrefs.Save(); ApplyScreen(); }
        }

        /// <summary>Puts the chosen resolution and mode on the screen (only once the player has chosen something).</summary>
        static void ApplyScreen()
        {
            if (Application.isEditor) return;   // the game view sizes itself
            if (!PlayerPrefs.HasKey(ResolutionKey) && !PlayerPrefs.HasKey(DisplayModeKey)) return;
            var r = Resolution;
            Screen.SetResolution(r.x, r.y, DisplayModes[DisplayModeIndex].mode);
        }

        // ------------------------------------------------------------------ sound

        /// <summary>Everything the game plays, 0-1.</summary>
        public static float MasterVolume
        {
            get => PlayerPrefs.GetFloat(VolumeKey, 1f);
            set { PlayerPrefs.SetFloat(VolumeKey, Mathf.Clamp01(value)); PlayerPrefs.Save(); AudioListener.volume = MasterVolume; }
        }

        // ------------------------------------------------------------------ controls

        /// <summary>Look speed, as a multiple of the default (mouse and stick).</summary>
        public static float MouseSensitivity
        {
            get => sensitivity ??= Mathf.Clamp(PlayerPrefs.GetFloat(SensitivityKey, 1f), 0.1f, 3f);   // read every frame: cached
            set { sensitivity = Mathf.Clamp(value, 0.1f, 3f); PlayerPrefs.SetFloat(SensitivityKey, sensitivity.Value); PlayerPrefs.Save(); }
        }
        static float? sensitivity;

        /// <summary>Up on the mouse looks down.</summary>
        public static bool InvertY
        {
            get => invertY ??= PlayerPrefs.GetInt(InvertYKey, 0) != 0;   // read every frame: cached
            set { invertY = value; PlayerPrefs.SetInt(InvertYKey, value ? 1 : 0); PlayerPrefs.Save(); }
        }
        static bool? invertY;

        /// <summary>Flashlight colours offered on the settings page, in the order they cycle.</summary>
        public static readonly (string ru, string en, Color color)[] FlashlightColors =
        {
            ("Холодный синий", "Cold blue", new Color(0.70f, 0.83f, 1f)),
            ("Белый", "White", new Color(0.95f, 0.97f, 1f)),
            ("Тёплый", "Warm", new Color(1f, 0.94f, 0.82f)),
            ("Жёлтый", "Yellow", new Color(1f, 0.84f, 0.58f)),
            ("Зеленоватый", "Greenish", new Color(0.8f, 1f, 0.86f)),
        };

        /// <summary>Which of <see cref="FlashlightColors"/> the flashlight shines.</summary>
        public static int FlashlightColorIndex
        {
            get => flashlightColor ??= Mathf.Clamp(PlayerPrefs.GetInt(FlashlightColorKey, 0), 0, FlashlightColors.Length - 1);   // read every frame: cached
            set
            {
                flashlightColor = (value % FlashlightColors.Length + FlashlightColors.Length) % FlashlightColors.Length;
                PlayerPrefs.SetInt(FlashlightColorKey, flashlightColor.Value);
                PlayerPrefs.Save();
            }
        }
        static int? flashlightColor;
        public static Color FlashlightColor => FlashlightColors[FlashlightColorIndex].color;

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
            AudioListener.volume = MasterVolume;
            ApplyScreen();
            ApplyRendering();
        }
    }
}
