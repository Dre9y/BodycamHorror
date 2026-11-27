using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BodycamHorror.UI
{
    /// <summary>
    /// Escape (or Start on a gamepad) pauses the game and opens this menu: Continue, Settings, Quit.
    /// While it is open time stands still, the sound is paused and the cursor is free; the player's own
    /// input is ignored (see <see cref="IsOpen"/>). Settings: the same screen as the main menu's
    /// (<see cref="SettingsScreen"/>). Laid out as the mock-up SourceArt/PauseMenu.svg (1280×800): the bodycam
    /// OSD, the left shade, "ПАУЗА" with its red shift and the three buttons, placed in its pixels and scaled by
    /// the screen height; the pieces are the mock-up's own, in Resources/PauseMenuUI (in English the title and the
    /// OSD are set in type instead, the pictures being Russian). Mouse, or up/down + Enter
    /// (d-pad + A on a gamepad). Drawn with IMGUI like the rest of the game's menus and installed automatically,
    /// so nothing has to be set up per scene.
    /// </summary>
    [DefaultExecutionOrder(-1000)]   // before anything reads input this frame
    public class PauseMenu : MonoBehaviour
    {
        enum Page { Main, Settings }

        public static PauseMenu Instance { get; private set; }
        /// <summary>A menu is up (this one, the debug panel with its free cursor, or a picked-up item on show): gameplay input must be ignored.</summary>
        public static bool IsOpen => IsPaused || DebugMenu.IsOpen || ItemShowcase.Blocking || Player.DeathCam.Active;
        /// <summary>The pause menu itself is up: the game is paused.</summary>
        public static bool IsPaused => Instance && Instance.open;

        bool open;
        Page page;
        float timeScaleBefore = 1f;
        InputAction toggle;
        // Animators stopped for the pause. With time at 0 the scripts that pose the arms (BareHands, RiggedArms,
        // the gun hands...) skip their frame, but an Animator still writes its clip's raw pose every frame: the
        // hands would fall out of their IK pose. Stopped, the bones stay exactly as last posed.
        readonly List<Animator> frozen = new List<Animator>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Instance) return;
            var go = new GameObject("PauseMenu");
            DontDestroyOnLoad(go);
            go.AddComponent<PauseMenu>();
        }

        void Awake()
        {
            Instance = this;
            toggle = new InputAction("Pause", InputActionType.Button, "<Keyboard>/escape");
            toggle.AddBinding("<Gamepad>/start");
        }

        void OnEnable() => toggle.Enable();
        void OnDisable() => toggle.Disable();

        void OnDestroy()
        {
            if (open) Close();
            toggle?.Dispose();
            if (Instance == this) Instance = null;
        }

        void Update()
        {
            if (!toggle.WasPressedThisFrame() || MainMenu.IsActive) { Navigate(); return; }   // no pausing the main menu
            if (!open && ItemShowcase.Blocking) return;   // Escape puts the item away, it does not pause
            if (!open && Player.DeathCam.Active) return;  // dead: the death cam plays out (it mutes and blacks out on its own)
            if (!open) Open();
            else if (page != Page.Main) { if (!SettingsScreen.CloseList()) page = Page.Main; }   // Escape steps back out: an open list, then the sub-page
            else Close();
        }

        public void Open()
        {
            if (open) return;
            open = true;
            page = Page.Main;
            focus = 0;
            timeScaleBefore = Time.timeScale;
            Time.timeScale = 0f;
            AudioListener.pause = true;
            Freeze();
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        public void Close()
        {
            if (!open) return;
            open = false;
            Time.timeScale = timeScaleBefore > 0f ? timeScaleBefore : 1f;
            AudioListener.pause = false;
            Thaw();
            if (DebugMenu.IsOpen) return;   // its sliders still need the cursor
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        void Freeze()
        {
            frozen.Clear();
            foreach (Animator a in FindObjectsByType<Animator>(FindObjectsSortMode.None))
            {
                if (!a.isActiveAndEnabled || a.updateMode == AnimatorUpdateMode.UnscaledTime) continue;
                a.keepAnimatorStateOnDisable = true;   // carry on mid-clip afterwards, not from the start
                a.enabled = false;
                frozen.Add(a);
            }
        }

        void Thaw()
        {
            foreach (Animator a in frozen) if (a) a.enabled = true;
            frozen.Clear();
        }

        static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        /// <summary>Up/down (arrows, W/S, d-pad, stick) move the focus; Enter or A press it.</summary>
        void Navigate()
        {
            if (!open || page != Page.Main) return;
            var kb = Keyboard.current;
            var pad = Gamepad.current;
            bool up = (kb != null && (kb.upArrowKey.wasPressedThisFrame || kb.wKey.wasPressedThisFrame))
                      || (pad != null && (pad.dpad.up.wasPressedThisFrame || pad.leftStick.up.wasPressedThisFrame));
            bool down = (kb != null && (kb.downArrowKey.wasPressedThisFrame || kb.sKey.wasPressedThisFrame))
                        || (pad != null && (pad.dpad.down.wasPressedThisFrame || pad.leftStick.down.wasPressedThisFrame));
            bool press = (kb != null && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame))
                         || (pad != null && pad.buttonSouth.wasPressedThisFrame);
            if (up) focus = (focus + Items.Length - 1) % Items.Length;
            if (down) focus = (focus + 1) % Items.Length;
            if (press) Press(focus);
        }

        void Press(int i)
        {
            switch (i)
            {
                case 0: Close(); break;
                case 1: page = Page.Settings; break;
                default: Quit(); break;
            }
        }

        // ------------------------------------------------------------------ drawing

        static string[] Items => Loc.English ? ItemsEn : ItemsRu;
        static readonly string[] ItemsRu = { "Продолжить", "Настройки", "Выйти из игры" };
        static readonly string[] ItemsEn = { "Resume", "Settings", "Quit game" };
        // the buttons, in mock-up pixels
        const float ItemX = 64f, ItemY = 350f, ItemW = 550f, ItemH = 68f, ItemPitch = 73f, LabelX = 88f;

        // the mock-up's colours
        static readonly Color Ink = Rgb(209, 209, 185);      // the focused label, the timecode
        static readonly Color IdleInk = Rgb(182, 186, 169);  // the other labels
        static readonly Color KeyInk = Rgb(181, 185, 159);   // [ ESC ], ›
        static readonly Color Red = Rgb(201, 83, 66);        // REC, the pause bars
        static readonly Color RedShift = Rgb(119, 59, 52);   // the title's shifted copy
        static readonly Color OsdInk = Rgb(163, 166, 154);   // КАМ-04, CH 01 / БК-04

        static Texture2D white, shade, title, osdCamera, osdChannel, buttonIdle, buttonFocused;
        static Font oswald, mono;
        GUIStyle label, labelIdle, key, timecode, bigTitle, bigTitleShift, osd, osdRed;
        float styleU = -1f;
        float u;            // screen pixels per mock-up pixel
        int focus;          // the highlighted button: the mouse's or the keys'
        Vector2 lastMouse;

        void OnGUI()
        {
            if (!open) return;
            GUI.depth = -1000;
            Load();
            if (!SettingsScreen.Font) SettingsScreen.Font = oswald;   // the level was started directly, past the main menu
            var e = Event.current;
            float W = Screen.width, H = Screen.height;

            if (page == Page.Settings)
            {
                if (e.type == EventType.Repaint) Fill(new Rect(0f, 0f, W, H), new Color(0f, 0f, 0f, 0.35f));
                if (SettingsScreen.Draw()) page = Page.Main;
                return;
            }

            u = H / 800f;
            Styles();

            if (e.type == EventType.Repaint)
            {
                Fill(new Rect(0f, 0f, W, H), new Color(0f, 0f, 0f, 0.25f));   // the scene dims a little everywhere
                Tex(new Rect(0f, 0f, W, H), shade);                            // and a lot on the left, under the menu
                if (!Loc.English)
                {
                    // the mock-up's own pieces, Russian
                    Tex(R(48f, 36f, 175f, 34f), osdCamera);                    // ■ REC  КАМ-04
                    Tex(R(59f, 737f, 190f, 32f), osdChannel);                  // CH 01 / БК-04
                    Tex(R(56f, 180f, 302f, 155f), title);                      // ❚❚ ПАУЗА
                }
                else
                {
                    // the same, set in type
                    Fill(R(53f, 45f, 11f, 11f), Red);
                    GUI.Label(R(77f, 41f, 60f, 20f), "REC", osdRed);
                    GUI.Label(R(147f, 41f, 100f, 20f), "CAM-04", osd);
                    GUI.Label(R(64f, 743f, 300f, 20f), "CH 01   /   BC-04", osd);
                    Fill(R(67f, 187f, 5f, 16f), Red);
                    Fill(R(76f, 187f, 5f, 16f), Red);
                    GUI.Label(R(61f, 205f, 700f, 120f), "PAUSE", bigTitleShift);
                    GUI.Label(R(64f, 204f, 700f, 120f), "PAUSE", bigTitle);
                }
            }

            int frames = (int)(Time.realtimeSinceStartupAsDouble * 30.0);
            string tc = string.Format("{0:00}:{1:00}:{2:00}:{3:00}", frames / 108000 % 100, frames / 1800 % 60, frames / 30 % 60, frames % 30);
            GUI.Label(new Rect(W - 73f * u - 300f * u, 41f * u, 300f * u, 20f * u), tc, timecode);

            // the mouse takes the focus only when it moves, so it does not fight the keys
            bool moved = (e.mousePosition - lastMouse).sqrMagnitude > 0.25f;
            if (e.type == EventType.MouseMove || e.type == EventType.Repaint) lastMouse = e.mousePosition;
            for (int i = 0; i < Items.Length; i++)
            {
                float y = ItemY + i * ItemPitch;
                var r = R(ItemX, y, ItemW, ItemH);
                if (moved && r.Contains(e.mousePosition)) focus = i;
                bool on = focus == i;
                if (e.type == EventType.Repaint)
                {
                    Tex(r, on ? buttonFocused : buttonIdle);
                    GUI.Label(R(LabelX, y, 400f, ItemH), Items[i], on ? label : labelIdle);
                    if (i == 0) GUI.Label(R(400f, y, 193f, ItemH), "[ ESC ]", key);
                    else if (i == 1) GUI.Label(R(400f, y, 184f, ItemH), "›", key);
                }
                if (GUI.Button(r, GUIContent.none, GUIStyle.none)) { focus = i; Press(i); }
            }
        }

        Rect R(float x, float y, float w, float h) => new Rect(x * u, y * u, w * u, h * u);

        static Color Rgb(int r, int g, int b) => new Color(r / 255f, g / 255f, b / 255f, 1f);

        static void Load()
        {
            if (!white) { white = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave }; white.SetPixel(0, 0, Color.white); white.Apply(); }
            if (!shade) shade = Resources.Load<Texture2D>("PauseMenuUI/LeftShade");
            if (!title) title = Resources.Load<Texture2D>("PauseMenuUI/PauseTitle");
            if (!osdCamera) osdCamera = Resources.Load<Texture2D>("PauseMenuUI/CameraOSD");
            if (!osdChannel) osdChannel = Resources.Load<Texture2D>("PauseMenuUI/ChannelOSD");
            if (!buttonIdle) buttonIdle = Resources.Load<Texture2D>("PauseMenuUI/ButtonIdle");
            if (!buttonFocused) buttonFocused = Resources.Load<Texture2D>("PauseMenuUI/ButtonFocused");
            if (!oswald) oswald = Resources.Load<Font>("Fonts/Oswald");
            if (!mono) mono = Font.CreateDynamicFontFromOSFont(new[] { "Consolas", "Lucida Console", "Courier New" }, 16);
        }

        void Styles()
        {
            if (Mathf.Approximately(u, styleU) && label != null) return;
            styleU = u;
            label = Text(oswald, 25f, Ink, TextAnchor.MiddleLeft);
            labelIdle = Text(oswald, 25f, IdleInk, TextAnchor.MiddleLeft);
            key = Text(mono, 9.5f, KeyInk, TextAnchor.MiddleRight);
            timecode = Text(mono, 11f, Ink, TextAnchor.MiddleRight);
            bigTitle = Text(oswald, 89f, Ink, TextAnchor.MiddleLeft);
            bigTitleShift = Text(oswald, 89f, RedShift, TextAnchor.MiddleLeft);
            osd = Text(mono, 10f, OsdInk, TextAnchor.MiddleLeft);
            osdRed = Text(mono, 12.5f, Red, TextAnchor.MiddleLeft);
        }

        /// <summary>The font size whose capital letters stand <paramref name="cap"/> mock-up pixels tall.</summary>
        int Cap(Font f, float cap)
        {
            float ratio = 0.72f;
            if (f)
            {
                f.RequestCharactersInTexture("Н", 100);
                if (f.GetCharacterInfo('Н', out var ci, 100) && ci.glyphHeight > 0) ratio = ci.glyphHeight / 100f;
            }
            return Mathf.Max(6, Mathf.RoundToInt(cap * u / ratio));
        }

        GUIStyle Text(Font f, float cap, Color c, TextAnchor a)
        {
            var s = new GUIStyle(GUI.skin.label)
            {
                font = f, fontSize = Cap(f ? f : GUI.skin.font, cap), alignment = a, wordWrap = false,
                clipping = TextClipping.Overflow, padding = new RectOffset(0, 0, 0, 0), margin = new RectOffset(0, 0, 0, 0),
            };
            s.normal.textColor = s.hover.textColor = s.active.textColor = c;
            return s;
        }

        static void Tex(Rect r, Texture2D t)
        {
            if (t) GUI.DrawTexture(r, t, ScaleMode.StretchToFill);
        }

        static void Fill(Rect r, Color c)
        {
            var was = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, white);
            GUI.color = was;
        }
    }
}
