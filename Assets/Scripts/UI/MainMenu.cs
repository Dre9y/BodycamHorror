using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace BodycamHorror.UI
{
    /// <summary>
    /// The main menu: a black screen, the title in white a little above the middle, and under it
    /// Start Game and Exit. A music loop plays under it.
    /// Start Game: the words dissolve into the black; then the lore comes up in white in the middle, stays ten
    /// seconds and fades out again, the music fading out with it (Enter / Space / click skips it). The first
    /// level loads behind all this and opens on black too (the wake-up cutscene), so it follows straight on.
    /// Mouse, or keyboard / gamepad (up and down to pick, Enter / Space / A to choose).
    /// Drawn with IMGUI like the rest of the game's menus.
    /// </summary>
    [DefaultExecutionOrder(-1100)]   // before the pause menu reads Escape
    public class MainMenu : MonoBehaviour
    {
        /// <summary>The main menu is up: the pause menu and the bodycam's audio stay out of it.</summary>
        public static bool IsActive { get; private set; }

        [Header("Words")]
        public string title = "BLOCK 13";
        public string startLabel = "START GAME";
        public string exitLabel = "EXIT";
        [Tooltip("Where the title sits, as a share of the screen height from the top.")]
        [Range(0.2f, 0.6f)] public float titleY = 0.4f;

        [Header("Start")]
        [Tooltip("The scene the game starts in.")]
        public string firstScene = "Level";
        [Tooltip("Seconds for the words to dissolve into black.")]
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

        State state;
        int selected;
        float alpha = 1f;          // the words
        float loreAlpha;           // the lore card
        float skipAt = -1f;        // when the lore was skipped (time since start), or -1
        float appear;              // 0..1 as the menu first comes up
        float startTime, fadeIn;
        Vector2 lastMouse = new Vector2(-1f, -1f);
        const int ItemCount = 2;
        AudioSource source;
        AsyncOperation loading;
        InputAction up, down, choose;
        static Texture2D black;
        GUIStyle titleStyle, itemStyle, loreStyle;
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

            Time.timeScale = 1f;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        void OnEnable() { up.Enable(); down.Enable(); choose.Enable(); }
        void OnDisable() { up.Disable(); down.Disable(); choose.Disable(); }

        void OnDestroy()
        {
            IsActive = false;
            up?.Dispose(); down?.Dispose(); choose?.Dispose();
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            appear = Mathf.MoveTowards(appear, 1f, dt / 1.2f);

            if (state == State.Menu)
            {
                fadeIn += dt;
                if (source) source.volume = musicVolume * Mathf.Clamp01(fadeIn / Mathf.Max(musicFadeInTime, 0.01f));
                if (up.WasPressedThisFrame()) selected = (selected + ItemCount - 1) % ItemCount;
                if (down.WasPressedThisFrame()) selected = (selected + 1) % ItemCount;
                if (choose.WasPressedThisFrame()) Choose(selected);
                return;
            }

            // leaving: the words go and the music dies away, then the lore comes up out of the black, stays,
            // and goes again; then the level is let in (it opens on black too)
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

        void Choose(int item)
        {
            if (state != State.Menu) return;
            if (item == 0) StartGame();
            else Quit();
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
            if (!black)
            {
                black = new Texture2D(1, 1);
                black.SetPixel(0, 0, Color.black);
                black.Apply();
            }
            GUI.depth = -100;
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), black);
            MakeStyles();
            float h = Screen.height, w = Screen.width;

            if (loreAlpha > 0f)
            {
                // the lore card: white on black in the middle, in a column narrow enough to read
                float colW = Mathf.Min(w * 0.8f, h * 1.1f);
                GUI.color = new Color(1f, 1f, 1f, loreAlpha);
                GUI.Label(new Rect((w - colW) * 0.5f, h * 0.15f, colW, h * 0.7f), lore, loreStyle);
                GUI.color = Color.white;
            }
            if (alpha <= 0f) return;
            float a = alpha * Mathf.SmoothStep(0f, 1f, appear);

            // the title, a little above the middle
            var titleRect = new Rect(0f, h * titleY - h * 0.08f, w, h * 0.16f);
            GUI.color = new Color(1f, 1f, 1f, a);
            GUI.Label(titleRect, title, titleStyle);

            // the items under it
            string[] items = { startLabel, exitLabel };
            float itemH = h * 0.055f, top = h * (titleY + 0.17f);
            Vector2 mouse = Event.current.mousePosition;
            bool mouseMoved = Event.current.type == EventType.Repaint && (mouse - lastMouse).sqrMagnitude > 0.5f;
            if (Event.current.type == EventType.Repaint) lastMouse = mouse;
            for (int i = 0; i < items.Length; i++)
            {
                var r = new Rect(w * 0.5f - h * 0.25f, top + i * itemH * 1.25f, h * 0.5f, itemH);
                // the mouse picks only when it moves, so it does not fight the keys
                if (state == State.Menu && mouseMoved && r.Contains(mouse)) selected = i;
                bool on = selected == i && state == State.Menu;
                GUI.color = new Color(1f, 1f, 1f, a * (on ? 1f : 0.45f));
                GUI.Label(r, on ? "—  " + items[i] + "  —" : items[i], itemStyle);
                if (state == State.Menu && Event.current.type == EventType.MouseDown && Event.current.button == 0 && r.Contains(mouse))
                {
                    Event.current.Use();
                    Choose(i);
                }
            }
            GUI.color = Color.white;
        }

        void MakeStyles()
        {
            if (Mathf.Approximately(styleHeight, Screen.height) && titleStyle != null) return;
            styleHeight = Screen.height;
            titleStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = Mathf.RoundToInt(Screen.height * 0.1f),
                fontStyle = FontStyle.Bold,
            };
            titleStyle.normal.textColor = Color.white;
            itemStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = Mathf.RoundToInt(Screen.height * 0.032f),
            };
            itemStyle.normal.textColor = Color.white;
            loreStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = Mathf.RoundToInt(Screen.height * 0.03f),
                wordWrap = true,
            };
            loreStyle.normal.textColor = new Color(0.9f, 0.9f, 0.88f);
        }
    }
}
