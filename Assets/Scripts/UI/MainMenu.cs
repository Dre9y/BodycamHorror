using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace BodycamHorror.UI
{
    /// <summary>
    /// The main menu, drawn over a 3D shot (a pistol on the wet floor of a dark corridor, a figure far down it).
    /// The scene comes up out of black; on the right the title, and under it Play, Settings and Exit.
    /// Settings: the same options as the pause menu's settings page (<see cref="GameSettings"/>).
    /// Play: the words and the picture go down to black; then the lore comes up in white in the middle, stays
    /// ten seconds and fades out again, the music fading out with it (Enter / Space / click skips it). The first
    /// level loads behind all this and opens on black too (the wake-up cutscene), so it follows straight on.
    /// Mouse, or keyboard / gamepad (up and down to pick, Enter / Space / A to choose, Escape / B back).
    /// Drawn with IMGUI like the rest of the game's menus.
    /// </summary>
    [DefaultExecutionOrder(-1100)]   // before the pause menu reads Escape
    public class MainMenu : MonoBehaviour
    {
        /// <summary>The main menu is up: the pause menu and the bodycam's audio stay out of it.</summary>
        public static bool IsActive { get; private set; }

        [Header("Words")]
        public string title = "BLOCK 13";
        public string startLabel = "ИГРАТЬ";
        public string settingsLabel = "НАСТРОЙКИ";
        public string exitLabel = "ВЫХОД";
        public string backLabel = "НАЗАД";
        public Font titleFont;
        public Font itemFont;
        [Tooltip("Left edge of the title and items, as a share of the screen width.")]
        [Range(0.4f, 0.85f)] public float columnX = 0.64f;
        [Tooltip("Where the title sits, as a share of the screen height from the top.")]
        [Range(0.05f, 0.4f)] public float titleY = 0.16f;
        [Tooltip("Where the first item sits, as a share of the screen height from the top.")]
        [Range(0.2f, 0.7f)] public float itemsY = 0.33f;
        [Tooltip("Gap between items, as a share of the screen height.")]
        [Range(0.05f, 0.25f)] public float itemGap = 0.155f;

        [Header("Start")]
        [Tooltip("The scene the game starts in.")]
        public string firstScene = "Level";
        [Tooltip("Seconds for the picture to come up out of black when the menu opens.")]
        public float sceneFadeIn = 3f;
        [Tooltip("Seconds for the words and the picture to go down to black.")]
        public float textFadeTime = 1.6f;
        [Tooltip("Seconds for the music to die away when there is no lore (with lore it plays on under it and goes with it).")]
        public float musicFadeTime = 2.2f;

        [Header("Lore (after the words have gone, before the level)")]
        [TextArea(6, 14)]
        public string lore =
            "Блок 13.\n" +
            "Девятиэтажная панелька на окраине города.\n\n" +
            "Уже неделю оттуда не перестают звонить: за стенами кричат, " +
            "свет гаснет этаж за этажом, жильцы больше не открывают двери.\n\n" +
            "Ты — тот самый полицейский, которого отправили разобраться.\n\n" +
            "Ты помнишь лестничную клетку. Что-то шевельнулось в темноте. Удар по голове.\n\n" +
            "А потом — ничего.";
        [Tooltip("Black between the menu's words going and the lore appearing (s).")]
        public float loreGap = 0.6f;
        public float loreFadeIn = 1.5f;
        [Tooltip("Seconds the lore stays fully up.")]
        public float loreHold = 10f;
        public float loreFadeOut = 1.5f;
        [Tooltip("Black after the lore before the level comes in (s).")]
        public float loreAfter = 0.4f;

        [Header("Music")]
        public AudioClip music;
        [Range(0f, 1f)] public float musicVolume = 0.8f;
        [Tooltip("Seconds for the music to come up when the menu opens.")]
        public float musicFadeInTime = 2f;

        enum State { Menu, Starting, Leaving }
        enum Page { Main, Settings }

        State state;
        Page page;
        int selected;
        float alpha = 1f;          // the words
        float loreAlpha;           // the lore card
        float skipAt = -1f;        // when the lore was skipped (time since start), or -1
        float opened;              // seconds since the menu opened
        float startTime, fadeIn;
        float[] hover = new float[8];   // 0..1 per row: how lit it is (eases after the selection)
        Vector2 lastMouse = new Vector2(-1f, -1f);
        AudioSource source;
        AsyncOperation loading;
        InputAction up, down, choose, back;
        static Texture2D white;
        GUIStyle titleStyle, itemStyle, rowStyle, valueStyle, loreStyle;
        float styleHeight = -1f;

        void Awake()
        {
            IsActive = true;
            source = GetComponent<AudioSource>();
            if (!source) source = gameObject.AddComponent<AudioSource>();
            source.clip = music;
            source.loop = true;
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            source.volume = 0f;
            if (music) source.Play();

            up = new InputAction("MenuUp", InputActionType.Button, "<Keyboard>/upArrow");
            up.AddBinding("<Keyboard>/w");
            up.AddBinding("<Gamepad>/dpad/up");
            up.AddBinding("<Gamepad>/leftStick/up");
            down = new InputAction("MenuDown", InputActionType.Button, "<Keyboard>/downArrow");
            down.AddBinding("<Keyboard>/s");
            down.AddBinding("<Gamepad>/dpad/down");
            down.AddBinding("<Gamepad>/leftStick/down");
            choose = new InputAction("MenuChoose", InputActionType.Button, "<Keyboard>/enter");
            choose.AddBinding("<Keyboard>/numpadEnter");
            choose.AddBinding("<Keyboard>/space");
            choose.AddBinding("<Gamepad>/buttonSouth");
            back = new InputAction("MenuBack", InputActionType.Button, "<Keyboard>/escape");
            back.AddBinding("<Keyboard>/backspace");
            back.AddBinding("<Gamepad>/buttonEast");

            Time.timeScale = 1f;
            AudioListener.pause = false;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        void OnEnable() { up.Enable(); down.Enable(); choose.Enable(); back.Enable(); }
        void OnDisable() { up.Disable(); down.Disable(); choose.Disable(); back.Disable(); }

        void OnDestroy()
        {
            IsActive = false;
            up?.Dispose(); down?.Dispose(); choose?.Dispose(); back?.Dispose();
        }

        int RowCount => page == Page.Main ? 3 : 6;

        void Update()
        {
            IsActive = true;   // again each frame: a script reload in the editor clears statics under a live menu
            float dt = Time.unscaledDeltaTime;
            opened += dt;
            for (int i = 0; i < hover.Length; i++)
                hover[i] = Mathf.MoveTowards(hover[i], i == selected && state == State.Menu ? 1f : 0f, dt / 0.18f);

            if (state == State.Menu)
            {
                fadeIn += dt;
                if (source) source.volume = musicVolume * Mathf.Clamp01(fadeIn / Mathf.Max(musicFadeInTime, 0.01f));
                if (up.WasPressedThisFrame()) selected = (selected + RowCount - 1) % RowCount;
                if (down.WasPressedThisFrame()) selected = (selected + 1) % RowCount;
                if (choose.WasPressedThisFrame()) Choose(selected);
                if (back.WasPressedThisFrame() && page == Page.Settings) OpenPage(Page.Main, 1);
                return;
            }

            // leaving: the words and the picture go and the music dies away, then the lore comes up out of the
            // black, stays, and goes again; then the level is let in (it opens on black too)
            float since = Time.unscaledTime - startTime;
            alpha = 1f - Mathf.Clamp01(since / Mathf.Max(textFadeTime, 0.01f));
            bool hasLore = !string.IsNullOrEmpty(lore);

            float loreStart = textFadeTime + loreGap;
            float loreOutStart = loreStart + loreFadeIn + loreHold;
            // Enter / Space / click skips the rest of the lore: it fades out from where it is
            if (state == State.Starting && skipAt < 0f && since > loreStart && since < loreOutStart
                && (choose.WasPressedThisFrame() || (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)))
                skipAt = since;
            float loreEnd, m;   // m: how much of the music is left
            if (!hasLore)
            {
                loreAlpha = 0f;
                loreEnd = textFadeTime;
                m = 1f - Mathf.Clamp01(since / Mathf.Max(musicFadeTime, 0.01f));
            }
            else if (skipAt >= 0f)
            {
                float from = Mathf.Clamp01((skipAt - loreStart) / Mathf.Max(loreFadeIn, 0.01f));
                float outTime = Mathf.Max(loreFadeOut * Mathf.Max(from, 0.5f), 0.01f);
                float gone = Mathf.Clamp01((since - skipAt) / outTime);
                loreAlpha = from * (1f - gone);
                m = 1f - gone;
                loreEnd = skipAt + outTime;
            }
            else
            {
                // the music plays on under the lore and goes out with it
                float inA = Mathf.Clamp01((since - loreStart) / Mathf.Max(loreFadeIn, 0.01f));
                float outA = 1f - Mathf.Clamp01((since - loreOutStart) / Mathf.Max(loreFadeOut, 0.01f));
                loreAlpha = Mathf.Min(inA, outA);
                m = outA;
                loreEnd = loreOutStart + loreFadeOut;
            }
            if (source) source.volume = musicVolume * m * m;   // quicker at first, a quiet tail

            bool done = hasLore ? since >= loreEnd + loreAfter : since >= Mathf.Max(loreEnd, musicFadeTime);
            if (state == State.Starting && done)
            {
                if (source) source.Stop();
                if (loading != null) loading.allowSceneActivation = true;
                else SceneManager.LoadScene(firstScene);
                state = State.Leaving;
            }
        }

        void OpenPage(Page p, int select)
        {
            page = p;
            selected = select;
        }

        void Choose(int row)
        {
            if (state != State.Menu) return;
            if (page == Page.Main)
            {
                if (row == 0) StartGame();
                else if (row == 1) OpenPage(Page.Settings, 0);
                else Quit();
                return;
            }
            switch (row)
            {
                case 0: GameSettings.VSync = !GameSettings.VSync; break;
                case 1: if (!GameSettings.VSync) GameSettings.FpsCap = NextCap(GameSettings.FpsCap); break;
                case 2: GameSettings.ShowFps = !GameSettings.ShowFps; break;
                case 3: GameSettings.EarRinging = !GameSettings.EarRinging; break;
                case 4: GameSettings.FlashlightColorIndex = GameSettings.FlashlightColorIndex + 1; break;
                default: OpenPage(Page.Main, 1); break;
            }
        }

        static int NextCap(int cap)
        {
            int i = System.Array.IndexOf(GameSettings.FpsCaps, cap);
            return GameSettings.FpsCaps[(i + 1) % GameSettings.FpsCaps.Length];
        }

        void StartGame()
        {
            state = State.Starting;
            startTime = Time.unscaledTime;
            Cursor.visible = false;
            // load behind the fade so the cut into the level is instant
            if (Application.CanStreamedLevelBeLoaded(firstScene))
            {
                loading = SceneManager.LoadSceneAsync(firstScene);
                if (loading != null) loading.allowSceneActivation = false;
            }
            else Debug.LogWarning("MainMenu: scene '" + firstScene + "' is not in the build settings.");
        }

        static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        // ------------------------------------------------------------------ drawing

        void OnGUI()
        {
            if (!white)
            {
                white = new Texture2D(1, 1);
                white.SetPixel(0, 0, Color.white);
                white.Apply();
            }
            GUI.depth = -100;
            MakeStyles();
            float h = Screen.height, w = Screen.width;

            // the picture: up out of black when the menu opens, down to black when the game starts
            float picture = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(opened / Mathf.Max(sceneFadeIn, 0.01f))) * alpha;
            if (picture < 1f) Fill(new Rect(0, 0, w, h), new Color(0f, 0f, 0f, 1f - picture));

            if (loreAlpha > 0f)
            {
                // the lore card: white on black in the middle, in a column narrow enough to read
                float colW = Mathf.Min(w * 0.8f, h * 1.1f);
                GUI.color = new Color(1f, 1f, 1f, loreAlpha);
                GUI.Label(new Rect((w - colW) * 0.5f, h * 0.15f, colW, h * 0.7f), lore, loreStyle);
                GUI.color = Color.white;
            }
            if (alpha <= 0f) return;
            // the words come in after the picture has started to show
            float a = alpha * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((opened - sceneFadeIn * 0.45f) / 1.4f));
            if (a <= 0f) return;

            float x = w * columnX, colWidth = w * 0.95f - x;

            // the title: a faint unsteady glow, like the lamp it sits beside
            float flicker = 0.9f + 0.1f * Mathf.PerlinNoise(Time.unscaledTime * 2.3f, 0.5f);
            if (Random.value < 0.006f) flicker *= 0.55f;
            // one line, made smaller if the column is too narrow for it
            titleStyle.fontSize = Mathf.RoundToInt(Screen.height * 0.115f);
            float tw = titleStyle.CalcSize(new GUIContent(title)).x;
            if (tw > colWidth) titleStyle.fontSize = Mathf.Max(8, Mathf.FloorToInt(titleStyle.fontSize * colWidth / tw));
            var titleRect = new Rect(x - h * 0.01f, h * titleY - h * 0.09f, colWidth + h * 0.02f, h * 0.18f);
            GUI.color = new Color(0f, 0f, 0f, a * 0.55f);
            GUI.Label(new Rect(titleRect.x + h * 0.004f, titleRect.y + h * 0.006f, titleRect.width, titleRect.height), title, titleStyle);
            GUI.color = new Color(0.86f, 0.85f, 0.8f, a * flicker);
            GUI.Label(titleRect, title, titleStyle);

            Vector2 mouse = Event.current.mousePosition;
            bool mouseMoved = Event.current.type == EventType.Repaint && (mouse - lastMouse).sqrMagnitude > 0.5f;
            if (Event.current.type == EventType.Repaint) lastMouse = mouse;

            if (page == Page.Main)
            {
                string[] items = { startLabel, settingsLabel, exitLabel };
                for (int i = 0; i < items.Length; i++)
                {
                    float y = h * (itemsY + i * itemGap);
                    var r = new Rect(x, y - h * 0.045f, colWidth, h * 0.09f);
                    if (Item(r, items[i], null, i, a, mouseMoved, mouse)) Choose(i);
                }
            }
            else
            {
                bool vsync = GameSettings.VSync;
                int cap = GameSettings.FpsCap;
                string[] labels = { "Вертикальная синхронизация", "Ограничение кадров", "Счётчик кадров", "Писк в ушах при страхе", "Цвет фонарика", backLabel };
                string[] values =
                {
                    vsync ? "Вкл" : "Выкл",
                    vsync ? "по монитору" : cap <= 0 ? "нет" : cap.ToString(),
                    GameSettings.ShowFps ? "Вкл" : "Выкл",
                    GameSettings.EarRinging ? "Вкл" : "Выкл",
                    GameSettings.FlashlightColors[GameSettings.FlashlightColorIndex].name,
                    null,
                };
                float rowH = h * 0.068f, top = h * itemsY - h * 0.02f;
                for (int i = 0; i < labels.Length; i++)
                {
                    float y = top + i * rowH + (i == labels.Length - 1 ? rowH * 0.6f : 0f);
                    var r = new Rect(x, y - rowH * 0.5f, colWidth, rowH);
                    bool enabled = !(i == 1 && vsync);
                    if (Item(r, labels[i], values[i], i, a * (enabled ? 1f : 0.5f), mouseMoved, mouse) && enabled) Choose(i);
                }
            }
            GUI.color = Color.white;
        }

        /// <summary>One row: dim, lit when picked, with a thin rough line drawn under it as it lights.
        /// Returns whether it was clicked.</summary>
        bool Item(Rect r, string label, string value, int i, float a, bool mouseMoved, Vector2 mouse)
        {
            bool main = value == null && page == Page.Main;
            // the mouse picks only when it moves, so it does not fight the keys
            if (state == State.Menu && mouseMoved && r.Contains(mouse)) selected = i;
            float lit = Mathf.SmoothStep(0f, 1f, hover[i]);
            GUIStyle style = main ? itemStyle : rowStyle;
            float shift = lit * r.height * 0.18f;   // the picked one steps out a little to the right

            GUI.color = new Color(0.82f, 0.82f, 0.78f, a * Mathf.Lerp(0.42f, 1f, lit));
            GUI.Label(new Rect(r.x + shift, r.y, r.width - shift, r.height), label, style);
            if (value != null)
            {
                GUI.color = new Color(0.82f, 0.82f, 0.78f, a * Mathf.Lerp(0.35f, 0.85f, lit));
                GUI.Label(new Rect(r.x, r.y, r.width, r.height), value, valueStyle);
            }

            // the line under it: a hand-drawn stroke with a step in it, drawn out from the left as it lights
            if (lit > 0.01f && Event.current.type == EventType.Repaint)
            {
                float baseY = r.yMax - r.height * (main ? 0.12f : 0.14f);
                float len = r.width * (main ? 0.78f : 1f) * lit;
                float th = Mathf.Max(1f, Screen.height / 540f);
                Color c = new Color(0.86f, 0.86f, 0.82f, a * 0.85f * lit);
                const int segs = 26;
                for (int s = 0; s < segs; s++)
                {
                    float u0 = s / (float)segs, u1 = (s + 1) / (float)segs;
                    if (u0 * r.width * (main ? 0.78f : 1f) > len) break;
                    float yOff = Wobble(u0, i) * r.height * 0.05f;
                    Fill(new Rect(r.x + u0 * r.width * (main ? 0.78f : 1f), baseY + yOff,
                                  (u1 - u0) * r.width * (main ? 0.78f : 1f) + 1f, th), c);
                }
            }

            if (state == State.Menu && Event.current.type == EventType.MouseDown && Event.current.button == 0 && r.Contains(mouse))
            {
                Event.current.Use();
                selected = i;
                return true;
            }
            return false;
        }

        /// <summary>The stroke's shape: high at the start, a step down past a third of the way, a little shake.</summary>
        static float Wobble(float u, int row)
        {
            float step = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.36f, 0.44f, u)) * (row % 2 == 0 ? 1f : 0.7f);
            float shake = Mathf.PerlinNoise(u * 9f, row * 3.1f) - 0.5f;
            return -1f + step * 2f + shake * 0.5f;
        }

        static void Fill(Rect r, Color c)
        {
            Color old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, white);
            GUI.color = old;
        }

        void MakeStyles()
        {
            if (Mathf.Approximately(styleHeight, Screen.height) && titleStyle != null) return;
            styleHeight = Screen.height;
            titleStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleLeft,
                fontSize = Mathf.RoundToInt(Screen.height * 0.115f),
                font = titleFont,
                wordWrap = false,
                clipping = TextClipping.Overflow,
            };
            titleStyle.normal.textColor = Color.white;
            itemStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleLeft,
                fontSize = Mathf.RoundToInt(Screen.height * 0.046f),
                font = itemFont,
                wordWrap = false,
                clipping = TextClipping.Overflow,
            };
            itemStyle.normal.textColor = Color.white;
            rowStyle = new GUIStyle(itemStyle) { fontSize = Mathf.RoundToInt(Screen.height * 0.027f) };
            valueStyle = new GUIStyle(rowStyle) { alignment = TextAnchor.MiddleRight };
            loreStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = Mathf.RoundToInt(Screen.height * 0.03f),
                font = itemFont,
                wordWrap = true,
            };
            loreStyle.normal.textColor = new Color(0.9f, 0.9f, 0.88f);
        }
    }
}
