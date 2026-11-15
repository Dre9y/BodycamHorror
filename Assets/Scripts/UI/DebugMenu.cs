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
        /// <summary>One slider: its range, step, the value it resets to and how it is shown.</summary>
        class Row
        {
            public string name, key, format, unitPrefix, unitSuffix;
            public float min, max, step, reset, floor, value;
        }

        static readonly Row[] Rows =
        {
            new Row { name = "Отдача оружия", key = "debug.recoilScale", min = 0f, max = 3f, step = 0.05f, reset = 1f, format = "0.00", unitPrefix = "x" },
            new Row { name = "Тряска оружия от страха", key = "debug.fearTremorScale", min = 0f, max = 3f, step = 0.05f, reset = 1f, format = "0.00", unitPrefix = "x" },
            new Row { name = "Размер пятна фонаря", key = "debug.flashlightSize", min = 0f, max = 3f, step = 0.05f, reset = 1f, floor = 0.2f, format = "0.00", unitPrefix = "x" },
            new Row { name = "Дальность резкости", key = "debug.focusDistance", min = 2f, max = 60f, step = 0.5f, reset = 15f, format = "0.0", unitSuffix = " м" },
        };

        static DebugMenu()
        {
            foreach (var r in Rows) r.value = r.reset;   // as tuned until the saved values are read
        }

        /// <summary>The panel is up and has the cursor.</summary>
        public static bool IsOpen { get; private set; }

        /// <summary>Multiplier on the recoil of every gun (weapon kick and camera kick); 1 = as tuned.</summary>
        public static float RecoilScale => Rows[0].value;
        /// <summary>Multiplier on the hand tremor from fear; 1 = as tuned.</summary>
        public static float FearTremorScale => Rows[1].value;
        /// <summary>Multiplier on the size of the flashlight's lit spot; 1 = as tuned.</summary>
        public static float FlashlightSize => Rows[2].value;
        /// <summary>The camera is sharp up to this far (m) and out of focus beyond it (LensLook).</summary>
        public static float FocusDistance => Rows[3].value;

        bool open;
        GUIStyle head, line, value, hint, reset;
        float styleScale = -1f;
        static Texture2D white;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Load()
        {
            foreach (var r in Rows) Set(r, PlayerPrefs.GetFloat(r.key, r.reset));
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (FindAnyObjectByType<DebugMenu>()) return;
            var go = new GameObject("DebugMenu");
            DontDestroyOnLoad(go);
            go.AddComponent<DebugMenu>();
        }

        static void Set(Row r, float v)
        {
            v = Mathf.Clamp(Mathf.Round(v / r.step) * r.step, r.min, r.max);
            r.value = Mathf.Max(v, r.floor);   // e.g. a flashlight beam of no width is no beam
        }

        static void Save()
        {
            foreach (var r in Rows) PlayerPrefs.SetFloat(r.key, r.value);
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
            var panel = new Rect(16f * k, 16f * k, w, pad * 2f + 44f * k + rowH * Rows.Length + 34f * k);
            Fill(panel, new Color(0.05f, 0.055f, 0.06f, 0.88f));

            float x = panel.x + pad, y = panel.y + pad, inner = w - pad * 2f;
            GUI.Label(new Rect(x, y, inner, 36f * k), "DEBUG", head);
            y += 44f * k;

            foreach (var r in Rows)
            {
                float v = r.value, h = rowH - 6f * k;
                GUI.Label(new Rect(x, y, 290f * k, h), r.name, line);
                var bar = new Rect(x + 300f * k, y, 210f * k, h);
                // tick at the default: the value as tuned
                Fill(new Rect(bar.x + bar.width * Mathf.InverseLerp(r.min, r.max, r.reset), bar.y + 4f * k, Mathf.Max(1f, 2f * k), h - 8f * k),
                     new Color(1f, 1f, 1f, 0.35f));
                float picked = GUI.HorizontalSlider(new Rect(bar.x, bar.y + h * 0.5f - 6f * k, bar.width, 12f * k), v, r.min, r.max);
                if (!Mathf.Approximately(picked, v)) Set(r, picked);
                GUI.Label(new Rect(bar.xMax + 14f * k, y, 90f * k, h), r.unitPrefix + r.value.ToString(r.format) + r.unitSuffix, value);
                if (GUI.Button(new Rect(x + inner - 70f * k, y + 4f * k, 70f * k, h - 8f * k), r.reset.ToString(r.format), reset)) Set(r, r.reset);
                y += rowH;
            }

            GUI.Label(new Rect(x, y + 4f * k, inner, 30f * k), "Тяни ползунок мышью. Кнопка справа — вернуть как было. Tab — закрыть.", hint);
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
