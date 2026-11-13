using UnityEngine;
using UnityEngine.InputSystem;

namespace BodycamHorror.UI
{
    /// <summary>
    /// Escape (or Start on a gamepad) pauses the game and opens this menu: Continue, Settings, Quit.
    /// While it is open time stands still, the sound is paused and the cursor is free; the player's own
    /// input is ignored (see <see cref="IsOpen"/>). Settings: V-Sync, and a frame cap for when it is off
    /// (<see cref="GameSettings"/>), the frame counter and the ear ringing. Drawn with IMGUI like the rest of the game's prompts and installed
    /// automatically, so nothing has to be set up per scene.
    /// </summary>
    [DefaultExecutionOrder(-1000)]   // before anything reads input this frame
    public class PauseMenu : MonoBehaviour
    {
        enum Page { Main, Settings }

        public static PauseMenu Instance { get; private set; }
        /// <summary>A menu is up (this one, or the debug panel with its free cursor): gameplay input must be ignored.</summary>
        public static bool IsOpen => IsPaused || DebugMenu.IsOpen;
        /// <summary>The pause menu itself is up: the game is paused.</summary>
        public static bool IsPaused => Instance && Instance.open;

        bool open;
        Page page;
        float timeScaleBefore = 1f;
        InputAction toggle;

        static Texture2D white;
        GUIStyle title, button, label, value, hint;
        float styleScale = -1f;

        static readonly Color Shade = new Color(0f, 0f, 0f, 0.72f);
        static readonly Color Panel = new Color(0.05f, 0.055f, 0.06f, 0.92f);
        static readonly Color Ink = new Color(0.9f, 0.92f, 0.88f, 1f);
        static readonly Color Dim = new Color(0.62f, 0.65f, 0.62f, 1f);
        static readonly Color Edge = new Color(0.9f, 0.92f, 0.88f, 0.25f);

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
            if (!toggle.WasPressedThisFrame()) return;
            if (!open) Open();
            else if (page != Page.Main) page = Page.Main;   // Escape steps back out of a sub-page first
            else Close();
        }

        public void Open()
        {
            if (open) return;
            open = true;
            page = Page.Main;
            timeScaleBefore = Time.timeScale;
            Time.timeScale = 0f;
            AudioListener.pause = true;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        public void Close()
        {
            if (!open) return;
            open = false;
            Time.timeScale = timeScaleBefore > 0f ? timeScaleBefore : 1f;
            AudioListener.pause = false;
            if (DebugMenu.IsOpen) return;   // its sliders still need the cursor
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
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
            if (!open) return;
            float k = Screen.height / 1080f;
            Styles(k);
            GUI.depth = -1000;

            Fill(new Rect(0f, 0f, Screen.width, Screen.height), Shade);
            float w = 660f * k, h = (page == Page.Main ? 420f : 580f) * k;
            var panel = new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h);
            Fill(panel, Panel);
            Frame(panel, Edge, Mathf.Max(1f, Mathf.Round(2f * k)));

            GUILayout.BeginArea(new Rect(panel.x + 48f * k, panel.y + 40f * k, panel.width - 96f * k, panel.height - 80f * k));
            if (page == Page.Main) MainPage(k);
            else SettingsPage(k);
            GUILayout.EndArea();
        }

        void MainPage(float k)
        {
            GUILayout.Label("ПАУЗА", title);
            GUILayout.Space(28f * k);
            if (Button("Продолжить", k)) Close();
            if (Button("Настройки", k)) page = Page.Settings;
            if (Button("Выйти из игры", k)) Quit();
        }

        void SettingsPage(float k)
        {
            GUILayout.Label("НАСТРОЙКИ", title);
            GUILayout.Space(28f * k);

            bool vsync = GameSettings.VSync;
            if (Row("Вертикальная синхронизация", vsync ? "Вкл" : "Выкл", true, k)) GameSettings.VSync = !vsync;

            int cap = GameSettings.FpsCap;
            string capText = cap <= 0 ? "Без ограничения" : cap + " к/с";
            if (Row("Ограничение кадров", vsync ? "по монитору" : capText, !vsync, k)) GameSettings.FpsCap = NextCap(cap);

            GUILayout.Label(vsync
                ? "Кадры идут с частотой монитора: без разрывов картинки."
                : "Без синхронизации картинка может рваться при поворотах.", hint);
            GUILayout.Space(14f * k);

            bool fps = GameSettings.ShowFps;
            if (Row("Счётчик кадров", fps ? "Вкл" : "Выкл", true, k)) GameSettings.ShowFps = !fps;

            bool ringing = GameSettings.EarRinging;
            if (Row("Писк в ушах при страхе", ringing ? "Вкл" : "Выкл", true, k)) GameSettings.EarRinging = !ringing;

            GUILayout.FlexibleSpace();
            if (Button("Назад", k)) page = Page.Main;
        }

        static int NextCap(int cap)
        {
            int i = System.Array.IndexOf(GameSettings.FpsCaps, cap);
            return GameSettings.FpsCaps[(i + 1) % GameSettings.FpsCaps.Length];
        }

        bool Button(string text, float k)
        {
            bool clicked = GUILayout.Button(text, button, GUILayout.Height(58f * k));
            GUILayout.Space(10f * k);
            return clicked;
        }

        /// <summary>A setting: its name on the left, its value on a button on the right (click to change).</summary>
        bool Row(string name, string current, bool enabled, float k)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(name, label, GUILayout.Height(52f * k));
            GUILayout.FlexibleSpace();
            bool was = GUI.enabled;
            GUI.enabled = enabled;
            bool clicked = GUILayout.Button(current, value, GUILayout.Width(190f * k), GUILayout.Height(52f * k));
            GUI.enabled = was;
            GUILayout.EndHorizontal();
            GUILayout.Space(8f * k);
            return clicked;
        }

        void Styles(float k)
        {
            if (Mathf.Approximately(k, styleScale) && title != null) return;
            styleScale = k;
            if (!white) { white = new Texture2D(1, 1); white.SetPixel(0, 0, Color.white); white.Apply(); }

            title = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(40f * k), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            title.normal.textColor = Ink;
            label = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(24f * k), alignment = TextAnchor.MiddleLeft, wordWrap = false };
            label.normal.textColor = Ink;
            hint = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(19f * k), wordWrap = true, alignment = TextAnchor.UpperLeft };
            hint.normal.textColor = Dim;

            button = new GUIStyle(GUI.skin.button) { fontSize = Mathf.RoundToInt(26f * k), alignment = TextAnchor.MiddleCenter };
            button.normal.background = Solid(new Color(1f, 1f, 1f, 0.06f));
            button.hover.background = Solid(new Color(1f, 1f, 1f, 0.16f));
            button.active.background = Solid(new Color(1f, 1f, 1f, 0.26f));
            button.normal.textColor = button.hover.textColor = button.active.textColor = Ink;
            button.border = new RectOffset(0, 0, 0, 0);

            value = new GUIStyle(button) { fontSize = Mathf.RoundToInt(22f * k) };
        }

        static Texture2D Solid(Color c)
        {
            var t = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
            t.SetPixel(0, 0, c);
            t.Apply();
            return t;
        }

        static void Fill(Rect r, Color c)
        {
            var was = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, white);
            GUI.color = was;
        }

        static void Frame(Rect r, Color c, float t)
        {
            Fill(new Rect(r.x, r.y, r.width, t), c);
            Fill(new Rect(r.x, r.yMax - t, r.width, t), c);
            Fill(new Rect(r.x, r.y, t, r.height), c);
            Fill(new Rect(r.xMax - t, r.y, t, r.height), c);
        }
    }
}
