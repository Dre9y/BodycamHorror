using UnityEngine;
using UnityEngine.InputSystem;

namespace BodycamHorror.UI
{
    /// <summary>
    /// Tab opens a small tuning panel in the top left corner with a slider per value, dragged with the
    /// mouse. While it is up the cursor is free and the player's input is ignored (PauseMenu.IsOpen),
    /// but the game keeps running; Tab again closes it and gives the mouse back to the view.
    /// The values are multipliers on top of what is set in the inspector and outlive the session
    /// (PlayerPrefs). Read by PlayerShooting. Installs itself.
    /// </summary>
    public class DebugMenu : MonoBehaviour
    {
        const string RecoilKey = "debug.recoilScale";
        const string TremorKey = "debug.fearTremorScale";
        const string FlashlightKey = "debug.flashlightSize";
        const float Min = 0f, Max = 3f, Step = 0.05f;

        /// <summary>The panel is up and has the cursor.</summary>
        public static bool IsOpen { get; private set; }

        /// <summary>Multiplier on the recoil of every gun (weapon kick and camera kick); 1 = as tuned.</summary>
        public static float RecoilScale { get; private set; } = 1f;
        /// <summary>Multiplier on the hand tremor from fear; 1 = as tuned.</summary>
        public static float FearTremorScale { get; private set; } = 1f;
        /// <summary>Multiplier on the size of the flashlight's lit spot; 1 = as tuned.</summary>
        public static float FlashlightSize { get; private set; } = 1f;

        static readonly string[] Names = { "Отдача оружия", "Тряска оружия от страха", "Размер пятна фонаря" };

        bool open;
        GUIStyle head, line, value, hint, reset;
        float styleScale = -1f;
        static Texture2D white;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Load()
        {
            RecoilScale = Mathf.Clamp(PlayerPrefs.GetFloat(RecoilKey, 1f), Min, Max);
            FearTremorScale = Mathf.Clamp(PlayerPrefs.GetFloat(TremorKey, 1f), Min, Max);
            FlashlightSize = Mathf.Clamp(PlayerPrefs.GetFloat(FlashlightKey, 1f), Min, Max);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (FindAnyObjectByType<DebugMenu>()) return;
            var go = new GameObject("DebugMenu");
            DontDestroyOnLoad(go);
            go.AddComponent<DebugMenu>();
        }

        static float Get(int i) => i == 0 ? RecoilScale : i == 1 ? FearTremorScale : FlashlightSize;

        static void Set(int i, float v)
        {
            v = Mathf.Clamp(Mathf.Round(v * 100f) / 100f, Min, Max);
            if (i == 0) RecoilScale = v;
            else if (i == 1) FearTremorScale = v;
            else FlashlightSize = Mathf.Max(v, 0.2f);   // a beam of no width is no beam
        }

        static void Save()
        {
            PlayerPrefs.SetFloat(RecoilKey, RecoilScale);
            PlayerPrefs.SetFloat(TremorKey, FearTremorScale);
            PlayerPrefs.SetFloat(FlashlightKey, FlashlightSize);
            PlayerPrefs.Save();
        }

        void OnDisable() { Save(); IsOpen = false; }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb == null || PauseMenu.IsPaused || !kb.tabKey.wasPressedThisFrame) return;
            open = !open;
            IsOpen = open;
            if (!open) Save();
            Cursor.lockState = open ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = open;
        }

        void OnGUI()
        {
            if (!open || PauseMenu.IsPaused) return;
            float k = Screen.height / 1080f;
            Styles(k);
            GUI.depth = -900;

            float pad = 18f * k, rowH = 44f * k, w = 700f * k;
            var panel = new Rect(16f * k, 16f * k, w, pad * 2f + 44f * k + rowH * Names.Length + 34f * k);
            Fill(panel, new Color(0.05f, 0.055f, 0.06f, 0.88f));

            float x = panel.x + pad, y = panel.y + pad, inner = w - pad * 2f;
            GUI.Label(new Rect(x, y, inner, 36f * k), "DEBUG", head);
            y += 44f * k;

            for (int i = 0; i < Names.Length; i++)
            {
                float v = Get(i), h = rowH - 6f * k;
                GUI.Label(new Rect(x, y, 290f * k, h), Names[i], line);
                var bar = new Rect(x + 300f * k, y, 210f * k, h);
                // tick at 1: the value as tuned in the inspector
                Fill(new Rect(bar.x + bar.width * Mathf.InverseLerp(Min, Max, 1f), bar.y + 4f * k, Mathf.Max(1f, 2f * k), h - 8f * k),
                     new Color(1f, 1f, 1f, 0.35f));
                float picked = GUI.HorizontalSlider(new Rect(bar.x, bar.y + h * 0.5f - 6f * k, bar.width, 12f * k), v, Min, Max);
                if (!Mathf.Approximately(picked, v)) Set(i, Mathf.Round(picked / Step) * Step);
                GUI.Label(new Rect(bar.xMax + 14f * k, y, 70f * k, h), "x" + Get(i).ToString("0.00"), value);
                if (GUI.Button(new Rect(x + inner - 70f * k, y + 4f * k, 70f * k, h - 8f * k), "1.00", reset)) Set(i, 1f);
                y += rowH;
            }

            GUI.Label(new Rect(x, y + 4f * k, inner, 30f * k), "Тяни ползунок мышью. Кнопка 1.00 — вернуть как было. Tab — закрыть.", hint);
        }

        void Styles(float k)
        {
            if (Mathf.Approximately(k, styleScale) && head != null) return;
            styleScale = k;
            if (!white) { white = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave }; white.SetPixel(0, 0, Color.white); white.Apply(); }

            head = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(24f * k), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
            head.normal.textColor = new Color(0.9f, 0.92f, 0.88f);
            line = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(21f * k), alignment = TextAnchor.MiddleLeft, wordWrap = false };
            line.normal.textColor = new Color(0.9f, 0.92f, 0.88f);
            value = new GUIStyle(line) { fontStyle = FontStyle.Bold };
            value.normal.textColor = new Color(1f, 0.82f, 0.4f);
            reset = new GUIStyle(GUI.skin.button) { fontSize = Mathf.RoundToInt(17f * k) };
            hint = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(17f * k), alignment = TextAnchor.UpperLeft, wordWrap = true };
            hint.normal.textColor = new Color(0.62f, 0.65f, 0.62f);
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
