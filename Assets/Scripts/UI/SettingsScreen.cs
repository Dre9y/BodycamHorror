using UnityEngine;

namespace BodycamHorror.UI
{
    /// <summary>
    /// The settings, the same in the main menu and in the pause menu: a panel with four tabs.
    ///  - Общее: the frame counter, the ear ringing, the flashlight colour.
    ///  - Графика: screen resolution and display mode (each a drop-down list), V-Sync, the frame cap.
    ///  - Звук: the overall volume.
    ///  - Управление: look sensitivity, inverted look, and the keys.
    /// Drawn with IMGUI like the rest of the menus. Everything is stored and applied by <see cref="GameSettings"/>.
    /// </summary>
    public static class SettingsScreen
    {
        enum Tab { General, Graphics, Sound, Controls }
        enum List { None, Resolution, DisplayMode }

        static readonly string[] TabNames = { "Общее", "Графика", "Звук", "Управление" };

        static Tab tab = Tab.Graphics;
        static List open;
        static Rect resolutionAt, modeAt;   // the value buttons the lists hang from (area space)
        static Rect listRect;               // the open list itself, as last drawn


        /// <summary>The words' font (the menu passes its own). Kept for the pause menu.</summary>
        public static Font Font;

        static GUIStyle title, tabStyle, tabOn, label, value, hint, item, itemOn, button, slider, thumb;
        static float styleScale = -1f;
        static Font styleFont;
        static Texture2D white;

        static readonly Color Ink = new Color(0.9f, 0.92f, 0.88f, 1f);
        static readonly Color Dim = new Color(0.62f, 0.65f, 0.62f, 1f);
        static readonly Color Edge = new Color(0.9f, 0.92f, 0.88f, 0.25f);
        static readonly Color Panel = new Color(0.05f, 0.055f, 0.06f, 0.94f);
        static readonly Color ListBack = new Color(0.08f, 0.085f, 0.09f, 0.99f);

        /// <summary>The panel's size for a screen scale (k = screen height / 1080).</summary>
        public static Vector2 Size(float k) => new Vector2(880f, 800f) * k;

        /// <summary>Draws the panel in <paramref name="panel"/> (screen space). True when Назад was pressed.</summary>
        public static bool Draw(Rect panel, float k)
        {
            Styles(k);
            Fill(panel, Panel);
            Frame(panel, Edge, Mathf.Max(1f, Mathf.Round(2f * k)));

            var area = new Rect(panel.x + 48f * k, panel.y + 36f * k, panel.width - 96f * k, panel.height - 72f * k);
            GUILayout.BeginArea(area);

            // an open list takes the click before the rows under it can
            if (open != List.None && Event.current.type == EventType.MouseDown)
            {
                if (listRect.Contains(Event.current.mousePosition)) PickFromList(Event.current.mousePosition);
                open = List.None;
                Event.current.Use();
            }

            GUILayout.Label("НАСТРОЙКИ", title);
            GUILayout.Space(18f * k);
            Tabs(k);
            GUILayout.Space(22f * k);

            switch (tab)
            {
                case Tab.General: General(k); break;
                case Tab.Graphics: Graphics(k); break;
                case Tab.Sound: Sound(k); break;
                default: Controls(k); break;
            }

            GUILayout.FlexibleSpace();
            bool back = GUILayout.Button("Назад", button, GUILayout.Height(56f * k));
            if (open != List.None) DrawList(k);
            GUILayout.EndArea();
            if (back) open = List.None;
            return back;
        }

        /// <summary>Escape: an open list closes first. True when there was one.</summary>
        public static bool CloseList()
        {
            if (open == List.None) return false;
            open = List.None;
            return true;
        }

        static void Tabs(float k)
        {
            GUILayout.BeginHorizontal();
            for (int i = 0; i < TabNames.Length; i++)
            {
                bool on = (int)tab == i;
                if (GUILayout.Button(TabNames[i], on ? tabOn : tabStyle, GUILayout.Height(48f * k)) && !on)
                {
                    tab = (Tab)i;
                    open = List.None;
                }
                // the picked tab is underlined
                if (on && Event.current.type == EventType.Repaint)
                {
                    var r = GUILayoutUtility.GetLastRect();
                    Fill(new Rect(r.x + r.width * 0.15f, r.yMax - 3f * k, r.width * 0.7f, Mathf.Max(1f, 2f * k)), Ink);
                }
            }
            GUILayout.EndHorizontal();
            var line = GUILayoutUtility.GetLastRect();
            if (Event.current.type == EventType.Repaint) Fill(new Rect(line.x, line.yMax + 2f * k, line.width, 1f), Edge);
        }

        // ------------------------------------------------------------------ the tabs

        static void General(float k)
        {
            bool fps = GameSettings.ShowFps;
            if (Row("Счётчик кадров", fps ? "Вкл" : "Выкл", true, k)) GameSettings.ShowFps = !fps;
            bool ringing = GameSettings.EarRinging;
            if (Row("Писк в ушах при страхе", ringing ? "Вкл" : "Выкл", true, k)) GameSettings.EarRinging = !ringing;
            int light = GameSettings.FlashlightColorIndex;
            if (Row("Цвет фонарика", GameSettings.FlashlightColors[light].name, true, k)) GameSettings.FlashlightColorIndex = light + 1;
        }

        static void Graphics(float k)
        {
            var res = GameSettings.Resolution;
            if (Row("Разрешение экрана", res.x + " × " + res.y + "  ▾", true, k, ref resolutionAt)) Toggle(List.Resolution);
            int mode = GameSettings.DisplayModeIndex;
            if (Row("Режим экрана", GameSettings.DisplayModes[mode].name + "  ▾", true, k, ref modeAt)) Toggle(List.DisplayMode);

            int up = GameSettings.UpscalerIndex;
            if (Row("Апскейлер", GameSettings.Upscalers[up].name, true, k))
                GameSettings.UpscalerIndex = (up + 1) % GameSettings.Upscalers.Length;
            int sc = GameSettings.RenderScaleIndex;
            var scale = GameSettings.RenderScales[sc];
            int scStep = Stepper("Масштаб рендера", Mathf.RoundToInt(scale.scale * 100f) + "%  " + scale.name, k);
            if (scStep != 0) GameSettings.RenderScaleIndex = sc + scStep;

            bool vsync = GameSettings.VSync;
            if (Row("Вертикальная синхронизация", vsync ? "Вкл" : "Выкл", true, k)) GameSettings.VSync = !vsync;
            int cap = GameSettings.FpsCap;
            string capText = cap <= 0 ? "Без ограничения" : cap + " к/с";
            if (Row("Ограничение кадров", vsync ? "по монитору" : capText, !vsync, k)) GameSettings.FpsCap = NextCap(cap);

            GUILayout.Space(6f * k);
            GUILayout.Label(UpscalerHint(up, scale.scale), hint);
            if (Application.isEditor)
                GUILayout.Label("Разрешение и режим экрана меняются в собранной игре (в редакторе окно задаёт сам Unity).", hint);
        }

        static string UpscalerHint(int upscaler, float scale)
        {
            bool native = scale >= 0.999f;
            switch (GameSettings.Upscalers[upscaler].filter)
            {
                case UnityEngine.Rendering.Universal.UpscalingFilterSelection.STP:
                    return native ? "STP на 100%: сглаживает края, кадров не прибавит. Снизьте масштаб, чтобы игра шла быстрее."
                                  : "STP: картинка считается в меньшем размере и восстанавливается по нескольким кадрам. Быстрее, почти без потери чёткости.";
                case UnityEngine.Rendering.Universal.UpscalingFilterSelection.FSR:
                    return native ? "FSR 1.0 работает только при масштабе ниже 100%."
                                  : "FSR 1.0: картинка считается в меньшем размере и растягивается с подчёркиванием резкости.";
                default:
                    return native ? "Игра рисуется в полном разрешении."
                                  : "Без апскейлера уменьшенная картинка просто растягивается: будет мыльно. Выберите STP или FSR.";
            }
        }

        static void Sound(float k)
        {
            float v = GameSettings.MasterVolume;
            int step = Stepper("Общая громкость", Mathf.RoundToInt(v * 100f) + "%", k);
            if (step != 0) GameSettings.MasterVolume = Mathf.Round(v * 10f + step) / 10f;
        }

        static void Controls(float k)
        {
            float s = GameSettings.MouseSensitivity;
            float picked = Slider("Чувствительность обзора", s, 0.1f, 3f, s.ToString("0.00") + "×", k);
            picked = Mathf.Round(picked * 20f) / 20f;   // steps of 0.05
            if (!Mathf.Approximately(picked, s)) GameSettings.MouseSensitivity = picked;
            bool invert = GameSettings.InvertY;
            if (Row("Инверсия по вертикали", invert ? "Вкл" : "Выкл", true, k)) GameSettings.InvertY = !invert;
        }

        static int NextCap(int cap)
        {
            int i = System.Array.IndexOf(GameSettings.FpsCaps, cap);
            return GameSettings.FpsCaps[(i + 1) % GameSettings.FpsCaps.Length];
        }

        // ------------------------------------------------------------------ the drop-down lists

        static void Toggle(List list)
        {
            open = open == list ? List.None : list;
        }

        static string[] Options(out int current)
        {
            if (open == List.Resolution)
            {
                var all = GameSettings.Resolutions;
                var cur = GameSettings.Resolution;
                current = System.Array.IndexOf(all, cur);
                var names = new string[all.Length];
                for (int i = 0; i < all.Length; i++) names[i] = all[i].x + " × " + all[i].y;
                return names;
            }
            current = GameSettings.DisplayModeIndex;
            var modes = new string[GameSettings.DisplayModes.Length];
            for (int i = 0; i < modes.Length; i++) modes[i] = GameSettings.DisplayModes[i].name;
            return modes;
        }

        static void PickFromList(Vector2 at)
        {
            int count = Options(out _).Length;
            int i = Mathf.Clamp(Mathf.FloorToInt((at.y - listRect.y) / (listRect.height / Mathf.Max(1, count))), 0, count - 1);
            if (open == List.Resolution) GameSettings.Resolution = GameSettings.Resolutions[i];
            else GameSettings.DisplayModeIndex = i;
        }

        static void DrawList(float k)
        {
            var names = Options(out int current);
            float rowH = 44f * k;
            Rect at = open == List.Resolution ? resolutionAt : modeAt;
            listRect = new Rect(at.x, at.yMax + 2f * k, at.width, rowH * names.Length);
            if (Event.current.type != EventType.Repaint) return;
            Fill(listRect, ListBack);
            Frame(listRect, Edge, Mathf.Max(1f, k));
            Vector2 mouse = Event.current.mousePosition;
            for (int i = 0; i < names.Length; i++)
            {
                var r = new Rect(listRect.x, listRect.y + i * rowH, listRect.width, rowH);
                if (r.Contains(mouse)) Fill(r, new Color(1f, 1f, 1f, 0.12f));
                (i == current ? itemOn : item).Draw(r, names[i], false, false, false, false);
            }
        }

        // ------------------------------------------------------------------ rows

        /// <summary>A setting: its name on the left, its value on a button on the right (click to change).</summary>
        static bool Row(string name, string current, bool enabled, float k)
        {
            Rect unused = default;
            return Row(name, current, enabled, k, ref unused);
        }

        /// <summary>The same, giving back where its button is (for a list to hang from it).</summary>
        static bool Row(string name, string current, bool enabled, float k, ref Rect at)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(name, label, GUILayout.Height(50f * k));
            GUILayout.FlexibleSpace();
            bool was = GUI.enabled;
            GUI.enabled = enabled;
            bool clicked = GUILayout.Button(current, value, GUILayout.Width(300f * k), GUILayout.Height(50f * k));
            if (Event.current.type == EventType.Repaint) at = GUILayoutUtility.GetLastRect();
            GUI.enabled = was;
            GUILayout.EndHorizontal();
            GUILayout.Space(8f * k);
            return clicked;
        }

        /// <summary>A setting with a slider and its value beside it. Returns the value (dragged or as it was).</summary>
        static float Slider(string name, float current, float min, float max, string shown, float k)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(name, label, GUILayout.Height(50f * k));
            GUILayout.FlexibleSpace();
            GUILayout.BeginVertical(GUILayout.Width(220f * k), GUILayout.Height(50f * k));
            GUILayout.FlexibleSpace();
            float v = GUILayout.HorizontalSlider(current, min, max, slider, thumb, GUILayout.Width(220f * k));
            GUILayout.FlexibleSpace();
            GUILayout.EndVertical();
            GUILayout.Label(shown, itemOn, GUILayout.Width(80f * k), GUILayout.Height(50f * k));
            GUILayout.EndHorizontal();
            GUILayout.Space(8f * k);
            return v;
        }

        /// <summary>A setting with ◄ value ► : -1 / +1 when an arrow is clicked.</summary>
        static int Stepper(string name, string current, float k)
        {
            int step = 0;
            GUILayout.BeginHorizontal();
            GUILayout.Label(name, label, GUILayout.Height(50f * k));
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("◄", value, GUILayout.Width(56f * k), GUILayout.Height(50f * k))) step = -1;
            GUILayout.Label(current, itemOn, GUILayout.Width(188f * k), GUILayout.Height(50f * k));
            if (GUILayout.Button("►", value, GUILayout.Width(56f * k), GUILayout.Height(50f * k))) step = 1;
            GUILayout.EndHorizontal();
            GUILayout.Space(8f * k);
            return step;
        }

        // ------------------------------------------------------------------ looks

        static void Styles(float k)
        {
            if (Mathf.Approximately(k, styleScale) && styleFont == Font && title != null) return;
            styleScale = k;
            styleFont = Font;
            if (!white) { white = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave }; white.SetPixel(0, 0, Color.white); white.Apply(); }

            title = new GUIStyle(GUI.skin.label) { font = Font, fontSize = Mathf.RoundToInt(40f * k), fontStyle = Font ? FontStyle.Normal : FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            title.normal.textColor = Ink;
            label = new GUIStyle(GUI.skin.label) { font = Font, fontSize = Mathf.RoundToInt(25f * k), alignment = TextAnchor.MiddleLeft, wordWrap = false };
            label.normal.textColor = Ink;
            hint = new GUIStyle(GUI.skin.label) { font = Font, fontSize = Mathf.RoundToInt(19f * k), wordWrap = true, alignment = TextAnchor.UpperLeft };
            hint.normal.textColor = Dim;

            button = new GUIStyle(GUI.skin.button) { font = Font, fontSize = Mathf.RoundToInt(26f * k), alignment = TextAnchor.MiddleCenter };
            button.normal.background = Solid(new Color(1f, 1f, 1f, 0.06f));
            button.hover.background = Solid(new Color(1f, 1f, 1f, 0.16f));
            button.active.background = Solid(new Color(1f, 1f, 1f, 0.26f));
            button.normal.textColor = button.hover.textColor = button.active.textColor = Ink;
            button.border = new RectOffset(0, 0, 0, 0);
            value = new GUIStyle(button) { fontSize = Mathf.RoundToInt(22f * k) };

            tabStyle = new GUIStyle(GUI.skin.label) { font = Font, fontSize = Mathf.RoundToInt(26f * k), alignment = TextAnchor.MiddleCenter };
            tabStyle.normal.textColor = Dim;
            tabStyle.hover.textColor = Ink;
            tabOn = new GUIStyle(tabStyle);
            tabOn.normal.textColor = tabOn.hover.textColor = Ink;

            item = new GUIStyle(GUI.skin.label) { font = Font, fontSize = Mathf.RoundToInt(22f * k), alignment = TextAnchor.MiddleCenter };
            item.normal.textColor = Dim;
            itemOn = new GUIStyle(item);
            itemOn.normal.textColor = Ink;

            // the slider: a thin track, a narrow upright handle
            slider = new GUIStyle(GUI.skin.horizontalSlider)
            {
                fixedHeight = Mathf.Max(3f, 6f * k),
                border = new RectOffset(0, 0, 0, 0),
                padding = new RectOffset(0, 0, 0, 0),
                overflow = new RectOffset(0, 0, 0, 0),
                margin = new RectOffset(0, 0, 0, 0),
            };
            slider.normal.background = slider.hover.background = slider.active.background = Solid(new Color(1f, 1f, 1f, 0.18f));
            thumb = new GUIStyle(GUI.skin.horizontalSliderThumb)
            {
                fixedWidth = Mathf.Max(6f, 12f * k),
                fixedHeight = 0f,   // as tall as the track; drawn taller by the overflow, centred on it
                border = new RectOffset(0, 0, 0, 0),
                padding = new RectOffset(0, 0, 0, 0),
                margin = new RectOffset(0, 0, 0, 0),
                overflow = new RectOffset(0, 0, Mathf.RoundToInt(9f * k), Mathf.RoundToInt(9f * k)),
            };
            thumb.normal.background = Solid(new Color(0.82f, 0.84f, 0.8f, 1f));
            thumb.hover.background = thumb.active.background = Solid(Ink);
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
