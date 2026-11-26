using BodycamHorror.Player;
using BodycamHorror.Weapons;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BodycamHorror.UI
{
    /// <summary>
    /// Tab opens a small tuning panel in the top left corner with a slider per value, dragged with the
    /// mouse. While it is up the cursor is free and the player's input is ignored (PauseMenu.IsOpen),
    /// but the game keeps running; Tab again closes it and gives the mouse back to the view.
    /// The values are multipliers on top of what is set in the inspector and outlive the session
    /// (PlayerPrefs). Read by PlayerShooting and BodycamPlayerController (walk and run speed are
    /// set in m/s, not as multipliers). Installs itself.
    /// The sliders are split into sections (tabs along the top); a section can also carry actions, such as
    /// handing the player the flashlight when they have none yet, or jumping to a place in the level (Level section).
    /// </summary>
    public class DebugMenu : MonoBehaviour
    {
        /// <summary>One slider: its range, step, the value it resets to and how it is shown.</summary>
        class Row
        {
            public Section section;
            public string name, key, format, unitPrefix, unitSuffix;
            public float min, max, step, reset, floor, value;
        }

        enum Section { Weapons, Flashlight, Camera, Movement, Level }
        static readonly string[] SectionNames = { "Оружие", "Фонарик", "Камера", "Движение", "Уровень" };

        /// <summary>Places to jump to (Level section): the scene object to find, and a feet point and facing in its own space.</summary>
        static readonly (string label, string anchor, Vector3 local, float yaw)[] Teleports =
        {
            // the bottom of the PvE landing, just in from the leg's door, looking up the landing to the iron door
            ("Телепорт: площадка с ключами", "PvE_Landing", new Vector3(2.25f, 0.05f, 1.4f), 0f),
            ("Телепорт: вход в кольцо", "BasementTransition", new Vector3(0f, 0.05f, -0.8f), 0f),
        };

        static readonly Row[] Rows =
        {
            new Row { section = Section.Weapons, name = "Отдача оружия", key = "debug.recoilScale", min = 0f, max = 3f, step = 0.05f, reset = 1.45f, format = "0.00", unitPrefix = "x" },
            new Row { section = Section.Weapons, name = "Тряска оружия от страха", key = "debug.fearTremorScale", min = 0f, max = 3f, step = 0.05f, reset = 1.65f, format = "0.00", unitPrefix = "x" },
            new Row { section = Section.Flashlight, name = "Фонарь: внутренний круг", key = "debug.flashlightSize", min = 0f, max = 3f, step = 0.05f, reset = 0.8f, floor = 0.2f, format = "0.00", unitPrefix = "x" },
            new Row { section = Section.Flashlight, name = "Фонарь: внешний круг", key = "debug.flashlightOuterSize", min = 0f, max = 3f, step = 0.05f, reset = 0.3f, floor = 0.1f, format = "0.00", unitPrefix = "x" },
            new Row { section = Section.Camera, name = "Дальность резкости", key = "debug.focusDistance", min = 2f, max = 60f, step = 0.5f, reset = 9f, format = "0.0", unitSuffix = " м" },
            new Row { section = Section.Camera, name = "Рыбий глаз", key = "debug.fisheye", min = 0f, max = 1f, step = 0.05f, reset = 0.5f, format = "0.00" },
            new Row { section = Section.Movement, name = "Скорость ходьбы", key = "debug.walkSpeed", min = 0.5f, max = 8f, step = 0.1f, reset = 2.7f, floor = 0.5f, format = "0.0", unitSuffix = " м/с" },
            new Row { section = Section.Movement, name = "Скорость бега", key = "debug.sprintSpeed", min = 1f, max = 15f, step = 0.1f, reset = 3.9f, floor = 1f, format = "0.0", unitSuffix = " м/с" },
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
        public static float FocusDistance => Rows[4].value;
        /// <summary>The player's walking speed (m/s); written into the controller every frame.</summary>
        public static float WalkSpeed => Rows[6].value;
        /// <summary>The player's running speed (m/s); written into the controller every frame.</summary>
        public static float SprintSpeed => Rows[7].value;
        /// <summary>Strength of the camera's fisheye bend (LensLook): 0 = flat, 1 = full equidistant fisheye.</summary>
        public static float Fisheye => Rows[5].value;
        /// <summary>Multiplier on the size of the flashlight's outer circle (the wide spill); 1 = as tuned.</summary>
        public static float FlashlightOuterSize => Rows[3].value;

        bool open;
        // a button's action, run in the next Update: from OnGUI (late in the frame, around rendering) a gun drawn
        // for the first time - its hands instantiated and bound right there - stayed undrawn
        System.Action pending;
        static Section shown;   // the open section, kept while the game runs
        GUIStyle head, line, value, hint, reset, tab, tabOn, action;
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
            if (pending != null) { var run = pending; pending = null; run(); }
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

            float pad = 18f * k, rowH = 44f * k, w = 700f * k, tabH = 40f * k, actionH = 48f * k;
            int count = 0;
            foreach (var r in Rows) if (r.section == shown) count++;
            // the level's actions: the light fear skip (in that level only), a button per teleport, the deathcam kill
            var lightFear = shown == Section.Level ? FindAnyObjectByType<Environment.LightFearEvent>() : null;
            int actions = shown == Section.Flashlight || shown == Section.Weapons ? 1
                        : shown == Section.Level ? Teleports.Length + 1 + (lightFear ? 1 : 0) : 0;
            var panel = new Rect(16f * k, 16f * k, w, pad * 2f + 44f * k + tabH + 14f * k + rowH * count + actionH * actions + 34f * k);
            Fill(panel, new Color(0.05f, 0.055f, 0.06f, 0.88f));

            float x = panel.x + pad, y = panel.y + pad, inner = w - pad * 2f;
            GUI.Label(new Rect(x, y, inner, 36f * k), "DEBUG", head);
            y += 44f * k;

            // the sections, as tabs
            float tw = (inner - 6f * k * (SectionNames.Length - 1)) / SectionNames.Length;
            for (int i = 0; i < SectionNames.Length; i++)
                if (GUI.Button(new Rect(x + i * (tw + 6f * k), y, tw, tabH), SectionNames[i], (Section)i == shown ? tabOn : tab)) shown = (Section)i;
            y += tabH + 14f * k;

            if (shown == Section.Flashlight)
            {
                var light = FindAnyObjectByType<BodycamFlashlight>(FindObjectsInactive.Include);
                bool has = light && light.owned;
                bool was = GUI.enabled;
                GUI.enabled = light && !has;
                string label = !light ? "Фонарика нет в сцене" : has ? "Фонарик уже есть" : "Выдать фонарик";
                if (GUI.Button(new Rect(x, y, inner, actionH - 8f * k), label, action)) pending = () => GiveFlashlight(light);
                GUI.enabled = was;
                y += actionH;
            }

            if (shown == Section.Weapons)
            {
                var shooting = FindAnyObjectByType<PlayerShooting>(FindObjectsInactive.Include);
                var pistol = shooting ? shooting.FindWeapon<Pistol>() : null;
                bool has = pistol && shooting.Owns(pistol);
                bool was = GUI.enabled;
                GUI.enabled = pistol && !has;
                string label = !pistol ? "Пистолета нет у игрока" : has ? "Пистолет уже есть" : "Выдать пистолет";
                if (GUI.Button(new Rect(x, y, inner, actionH - 8f * k), label, action)) pending = () => shooting.Give(pistol, true);
                GUI.enabled = was;
                y += actionH;
            }

            if (shown == Section.Level)
            {
                // the light monster's level: straight to the last part (the power on, the shotgun ahead in the way out)
                if (lightFear)
                {
                    if (GUI.Button(new Rect(x, y, inner, actionH - 8f * k), "Скип: перед подбором дробовика", action))
                    {
                        lightFear.SkipToShotgun();
                        ClosePanel();
                    }
                    y += actionH;
                }
                foreach (var tp in Teleports)
                {
                    var anchor = GameObject.Find(tp.anchor);
                    bool was = GUI.enabled;
                    GUI.enabled = anchor;
                    string label = anchor ? tp.label : tp.label + " (нет в сцене)";
                    if (GUI.Button(new Rect(x, y, inner, actionH - 8f * k), label, action)) { var t = tp; var a = anchor.transform; pending = () => TeleportTo(a, t.local, t.yaw); }
                    GUI.enabled = was;
                    y += actionH;
                }

                var health = FindAnyObjectByType<PlayerHealth>();
                bool wasOn = GUI.enabled;
                GUI.enabled = health && !health.IsDead;
                string killLabel = !health ? "Игрока нет в сцене" : health.IsDead ? "Игрок уже мёртв" : "Умереть (deathcam)";
                if (GUI.Button(new Rect(x, y, inner, actionH - 8f * k), killLabel, action)) pending = () => Kill(health);
                GUI.enabled = wasOn;
                y += actionH;
            }

            foreach (var r in Rows)
            {
                if (r.section != shown) continue;
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

        /// <summary>As if the weapon light had been picked up (WeaponPickup, Kind Pistol Light): the player owns
        /// the flashlight from now on, it fits under the pistol, and it is switched on.</summary>
        static void GiveFlashlight(BodycamFlashlight light)
        {
            if (!light) return;
            light.owned = true;
            var player = light.GetComponentInParent<BodycamPlayerController>();
            var shooting = player ? player.GetComponentInChildren<PlayerShooting>(true) : null;
            var pistol = shooting ? shooting.FindWeapon<Pistol>() : null;
            if (pistol) pistol.HasLight = true;
            light.SetOn(true);
        }

        /// <summary>Kills the player outright and closes the panel, so the death cam plays out in full view.</summary>
        void Kill(PlayerHealth health)
        {
            if (!health) return;
            // still lying on the floor in the wake-up intro: cut it short first (disabling it gives back input, eyes and ears)
            var intro = health.GetComponent<WakeUpIntro>();
            if (intro && intro.IsPlaying) intro.enabled = false;
            open = IsOpen = false;
            Save();
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            health.Kill();
        }

        /// <summary>Moves the player to a point given in <paramref name="anchor"/>'s space, facing its <paramref name="yaw"/>,
        /// and closes the panel so they can walk on at once.</summary>
        void TeleportTo(Transform anchor, Vector3 local, float yaw)
        {
            var player = FindAnyObjectByType<BodycamPlayerController>();
            if (!player) return;
            // still lying on the floor in the wake-up intro: cut it short (disabling it gives back input, eyes and ears)
            var intro = player.GetComponent<WakeUpIntro>();
            if (intro && intro.IsPlaying)
            {
                intro.enabled = false;
                if (player.cameraPivot) player.cameraPivot.localPosition = new Vector3(0f, player.StandingEyeHeight, 0f);
            }
            player.Teleport(anchor.TransformPoint(local), anchor.eulerAngles.y + yaw);
            ClosePanel();
        }

        /// <summary>Closes the panel and gives the mouse back to the view, so the player can go on at once.</summary>
        void ClosePanel()
        {
            open = IsOpen = false;
            Save();
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
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
            tab = new GUIStyle(GUI.skin.button) { fontSize = Mathf.RoundToInt(19f * k) };
            tab.normal.textColor = new Color(0.62f, 0.65f, 0.62f);
            tabOn = new GUIStyle(tab) { fontStyle = FontStyle.Bold };
            tabOn.normal.background = tabOn.active.background;
            tabOn.normal.textColor = tabOn.hover.textColor = new Color(1f, 0.82f, 0.4f);
            action = new GUIStyle(GUI.skin.button) { fontSize = Mathf.RoundToInt(19f * k), fontStyle = FontStyle.Bold };
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
