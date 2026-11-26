using UnityEngine;

namespace BodycamHorror.UI
{
    /// <summary>
    /// The settings, the same in the main menu and in the pause menu, laid out as the Figma mock-up "Сырой пролёт":
    /// a bodycam frame (REC, camera, timecode) over the scene, darkened on the left; there "НАСТРОЙКИ", the tab's name
    /// big with a red shift under it, the four tabs with a red trace under the picked one, the rows, Назад [ESC] and
    /// the tape label at the bottom.
    ///  - Общее: the frame counter, the ear ringing, the flashlight colour.
    ///  - Графика: screen resolution and display mode (each a drop-down list), upscaler, render scale, V-Sync, the frame cap.
    ///  - Звук: the overall volume.
    ///  - Управление: look sensitivity, inverted look.
    /// The mock-up is 1280×800; everything here is placed in its pixels and scaled by the screen height.
    /// Drawn with IMGUI like the rest of the menus. Everything is stored and applied by <see cref="GameSettings"/>.
    /// The textures (left shade, signal tear, row seam, cycle mark) are the mock-up's own, in Resources/SettingsUI.
    /// </summary>
    public static class SettingsScreen
    {
        enum Tab { General, Graphics, Sound, Controls }
        enum List { None, Resolution, DisplayMode }
        enum Mark { None, On, Off, Cycle, Drop }

        static readonly string[] TabNames = { "Общее", "Графика", "Звук", "Управление" };

        static Tab tab = Tab.General;
        static List open;
        static Rect resolutionAt, modeAt;   // the value areas the lists hang from (screen space)
        static Rect listRect;               // the open list itself, as last drawn
        static int hotRow;                  // the row with the red cursor: the last one the mouse was on
        static int rowIndex;                // counts the rows while drawing

        /// <summary>The words' font (the main menu passes Oswald). Kept for the pause menu.</summary>
        public static Font Font;

        // the mock-up's colours
        static readonly Color Ink = Rgb(209, 209, 185);        // title, names, picked tab, Назад, timecode
        static readonly Color ValueInk = Rgb(206, 209, 162);   // values, the on mark
        static readonly Color Dim = Rgb(163, 166, 154);        // other tabs, the OSD, [ESC], hints
        static readonly Color Red = Rgb(201, 83, 66);          // REC, the cursor, the tab trace
        static readonly Color RedShift = Rgb(119, 59, 52);     // the title's shifted copy
        static readonly Color RowBack = new Color(0.063f, 0.075f, 0.051f, 0.92f);
        static readonly Color RowHot = new Color(0.085f, 0.098f, 0.07f, 0.95f);
        static readonly Color ListBack = new Color(0.045f, 0.05f, 0.04f, 0.98f);

        static Texture2D white, dot, shade, tear, seam, cycle;
        static Font mono;
        static GUIStyle title, titleShift, timecode, kicker, tabStyle, label, labelSmall, value, osd, osdRed, key, hint, back, arrow, item, sign;
        static float styleU = -1f;
        static Font styleFont;

        static float u;          // screen pixels per mock-up pixel
        static Vector2 mouse;

        // the rows, in mock-up pixels
        const float RowX = 63f, RowW = 605f, RowValueX = 392f, RowMarkFromRight = 31f;
        static float rowY, rowH, rowPitch;

        /// <summary>Draws the settings over the whole screen. True when Назад was pressed.</summary>
        public static bool Draw()
        {
            u = Screen.height / 800f;
            Load();
            Styles();
            var e = Event.current;
            mouse = e.mousePosition;

            // an open list takes the click before the rows under it can
            if (open != List.None && e.type == EventType.MouseDown)
            {
                if (listRect.Contains(mouse)) PickFromList(mouse);
                open = List.None;
                e.Use();
            }

            float W = Screen.width, H = Screen.height;
            if (e.type == EventType.Repaint)
            {
                Fill(new Rect(0f, 0f, W, H), new Color(0f, 0f, 0f, 0.2f));
                if (shade) GUI.DrawTexture(new Rect(0f, 0f, W, H), shade, ScaleMode.StretchToFill);
            }

            Osd(W);
            GUI.Label(R(65f, 137f, 400f, 16f), "Н А С Т Р О Й К И", kicker);
            Title();
            Tabs();

            rowIndex = 0;
            rowY = 376f;
            bool roomy = tab == Tab.General;
            rowH = roomy ? 69f : 50f;
            rowPitch = rowH + (roomy ? 2.6f : 2f);
            float end;
            switch (tab)
            {
                case Tab.General: end = General(); break;
                case Tab.Graphics: end = Graphics(); break;
                case Tab.Sound: end = Sound(); break;
                default: end = Controls(); break;
            }

            // Назад sits where the mock-up has it, lower when the rows and hints need the room
            float backY = Mathf.Max(683.5f, end + 26f);
            bool pressed = Back(backY);
            float footY = Mathf.Min(backY + 72.5f, 786f);
            if (e.type == EventType.Repaint && tear)
                GUI.DrawTexture(new Rect(0f, (footY - 34.5f) * u, W, 5f * u), tear, ScaleMode.StretchToFill);
            GUI.Label(R(64f, footY - 8f, 60f, 16f), "CH 01", osd);
            GUI.Label(R(128f, footY - 8f, 20f, 16f), "/", osd);
            GUI.Label(R(158f, footY - 8f, 80f, 16f), "БК-04", osd);

            if (open != List.None) DrawList();
            if (pressed) open = List.None;
            return pressed;
        }

        /// <summary>Escape: an open list closes first. True when there was one.</summary>
        public static bool CloseList()
        {
            if (open == List.None) return false;
            open = List.None;
            return true;
        }

        // ------------------------------------------------------------------ the frame

        /// <summary>REC (blinking), the camera, the timecode on the right.</summary>
        static void Osd(float W)
        {
            bool on = Time.unscaledTime % 1.2f < 0.8f;
            if (Event.current.type == EventType.Repaint && on && dot)
            {
                var was = GUI.color;
                GUI.color = Red;
                GUI.DrawTexture(R(54.8f, 47.2f, 12f, 12f), dot);
                GUI.color = was;
            }
            GUI.Label(R(81f, 45f, 60f, 18f), "REC", osdRed);
            GUI.Label(R(147f, 45f, 80f, 18f), "КАМ-04", osd);

            double t = Time.realtimeSinceStartupAsDouble;
            int frames = (int)(t * 30.0);
            string tc = string.Format("{0:00}:{1:00}:{2:00}:{3:00}", frames / 108000 % 100, frames / 1800 % 60, frames / 30 % 60, frames % 30);
            GUI.Label(new Rect(W - 69f * u - 300f * u, 46f * u, 300f * u, 18f * u), tc, timecode);
        }

        /// <summary>The picked tab's name, big, with the red shifted copy under it and the seam over it.</summary>
        static void Title()
        {
            string t = TabNames[(int)tab].ToUpperInvariant();
            var r = R(61f, 160f, 900f, 120f);
            GUI.Label(new Rect(r.x - 2.5f * u, r.y + 2.5f * u, r.width, r.height), t, titleShift);
            GUI.Label(r, t, title);
            if (Event.current.type == EventType.Repaint && seam)
            {
                // the short seam across the top of the title
                GUI.DrawTexture(R(63f, 178f, 88f, 1.2f), seam, ScaleMode.StretchToFill);
            }
        }

        static void Tabs()
        {
            float x = 67.5f;
            for (int i = 0; i < TabNames.Length; i++)
            {
                bool on = (int)tab == i;
                float w = tabStyle.CalcSize(new GUIContent(TabNames[i])).x / u;
                var r = R(x, 309f, w, 28f);
                bool hover = r.Contains(mouse) && open == List.None;
                var was = GUI.color;
                GUI.color = on || hover ? Ink : Dim;
                GUI.Label(r, TabNames[i], tabStyle);
                GUI.color = was;
                if (on && Event.current.type == EventType.Repaint) Fill(R(x - 1.7f, 342.2f, w - 5f, 2.6f), Red);
                if (GUI.Button(r, GUIContent.none, GUIStyle.none) && !on)
                {
                    tab = (Tab)i;
                    open = List.None;
                    hotRow = 0;
                }
                x += w + 36f;
            }
        }

        static bool Back(float y)
        {
            var hit = R(60f, y - 18f, 180f, 36f);
            bool hover = hit.Contains(mouse) && open == List.None;
            var was = GUI.color;
            GUI.color = hover ? Red : Ink;
            GUI.Label(R(66f, y - 13f, 30f, 26f), "←", arrow);
            GUI.color = Ink;
            GUI.Label(R(107f, y - 15f, 80f, 30f), "Назад", back);
            GUI.color = was;
            GUI.Label(R(175.5f, y - 8f, 70f, 16f), "[ ESC ]", key);
            return GUI.Button(hit, GUIContent.none, GUIStyle.none);
        }

        // ------------------------------------------------------------------ the tabs (each gives back where its content ends)

        static float General()
        {
            bool fps = GameSettings.ShowFps;
            if (Row("Счётчик кадров", fps ? "Вкл" : "Выкл", fps ? Mark.On : Mark.Off, true, out _)) GameSettings.ShowFps = !fps;
            bool ringing = GameSettings.EarRinging;
            if (Row("Писк в ушах при страхе", ringing ? "Вкл" : "Выкл", ringing ? Mark.On : Mark.Off, true, out _)) GameSettings.EarRinging = !ringing;
            int light = GameSettings.FlashlightColorIndex;
            if (Row("Цвет фонарика", GameSettings.FlashlightColors[light].name, Mark.Cycle, true, out _)) GameSettings.FlashlightColorIndex = light + 1;
            return rowY;
        }

        static float Graphics()
        {
            var res = GameSettings.Resolution;
            if (Row("Разрешение экрана", res.x + " × " + res.y, Mark.Drop, true, out var at)) Toggle(List.Resolution);
            if (Event.current.type == EventType.Repaint) resolutionAt = at;
            int mode = GameSettings.DisplayModeIndex;
            if (Row("Режим экрана", GameSettings.DisplayModes[mode].name, Mark.Drop, true, out at)) Toggle(List.DisplayMode);
            if (Event.current.type == EventType.Repaint) modeAt = at;

            int up = GameSettings.UpscalerIndex;
            if (Row("Апскейлер", GameSettings.Upscalers[up].name, Mark.Cycle, true, out _))
                GameSettings.UpscalerIndex = (up + 1) % GameSettings.Upscalers.Length;
            int sc = GameSettings.RenderScaleIndex;
            var scale = GameSettings.RenderScales[sc];
            int scStep = Stepper("Масштаб рендера", Mathf.RoundToInt(scale.scale * 100f) + "% " + scale.name);
            if (scStep != 0) GameSettings.RenderScaleIndex = sc + scStep;

            bool vsync = GameSettings.VSync;
            if (Row("Вертикальная синхронизация", vsync ? "Вкл" : "Выкл", vsync ? Mark.On : Mark.Off, true, out _)) GameSettings.VSync = !vsync;
            int cap = GameSettings.FpsCap;
            string capText = cap <= 0 ? "Без ограничения" : cap + " к/с";
            if (Row("Ограничение кадров", vsync ? "По монитору" : capText, Mark.Cycle, !vsync, out _)) GameSettings.FpsCap = NextCap(cap);

            string text = UpscalerHint(up, scale.scale);
            if (Application.isEditor) text += "\nРазрешение и режим экрана меняются в собранной игре.";
            float h = hint.CalcHeight(new GUIContent(text), RowW * u) / u;
            GUI.Label(R(RowX, rowY + 2f, RowW, h), text, hint);
            return rowY + 2f + h;
        }

        static string UpscalerHint(int upscaler, float scale)
        {
            bool native = scale >= 0.999f;
            switch (GameSettings.Upscalers[upscaler].filter)
            {
                case UnityEngine.Rendering.Universal.UpscalingFilterSelection.STP:
                    return native ? "При 100% STP сглаживает края. Снизьте масштаб для большей частоты кадров."
                                  : "STP: картинка считается в меньшем размере и восстанавливается по нескольким кадрам.";
                case UnityEngine.Rendering.Universal.UpscalingFilterSelection.FSR:
                    return native ? "FSR 1.0 работает только при масштабе ниже 100%."
                                  : "FSR 1.0: картинка считается в меньшем размере и растягивается с подчёркиванием резкости.";
                default:
                    return native ? "Игра рисуется в полном разрешении."
                                  : "Без апскейлера уменьшенная картинка просто растягивается: будет мыльно. Выберите STP или FSR.";
            }
        }

        static float Sound()
        {
            float v = GameSettings.MasterVolume;
            int step = Stepper("Общая громкость", Mathf.RoundToInt(v * 100f) + "%");
            if (step != 0) GameSettings.MasterVolume = Mathf.Round(v * 10f + step) / 10f;
            return rowY;
        }

        static float Controls()
        {
            float s = GameSettings.MouseSensitivity;
            float picked = Slider("Чувствительность обзора", s, 0.1f, 3f, s.ToString("0.00") + "×");
            picked = Mathf.Round(picked * 20f) / 20f;   // steps of 0.05
            if (!Mathf.Approximately(picked, s)) GameSettings.MouseSensitivity = picked;
            bool invert = GameSettings.InvertY;
            if (Row("Инверсия по вертикали", invert ? "Вкл" : "Выкл", invert ? Mark.On : Mark.Off, true, out _)) GameSettings.InvertY = !invert;
            return rowY;
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

        static void DrawList()
        {
            var names = Options(out int current);
            float itemH = 30f * u;
            Rect at = open == List.Resolution ? resolutionAt : modeAt;
            listRect = new Rect(at.x - 12f * u, at.yMax - 6f * u, at.width + 12f * u, itemH * names.Length);
            if (Event.current.type != EventType.Repaint) return;
            Fill(listRect, ListBack);
            if (seam) GUI.DrawTexture(new Rect(listRect.x, listRect.y, listRect.width, Mathf.Max(1f, u)), seam, ScaleMode.StretchToFill);
            Fill(new Rect(listRect.x, listRect.y, Mathf.Max(1f, 2f * u), listRect.height), Red);
            for (int i = 0; i < names.Length; i++)
            {
                var r = new Rect(listRect.x, listRect.y + i * itemH, listRect.width, itemH);
                bool hover = r.Contains(mouse);
                if (hover) Fill(r, new Color(1f, 1f, 1f, 0.06f));
                var was = GUI.color;
                GUI.color = i == current || hover ? Color.white : new Color(1f, 1f, 1f, 0.6f);
                item.Draw(new Rect(r.x + 12f * u, r.y, r.width - 12f * u, r.height), names[i], false, false, false, false);
                GUI.color = was;
            }
        }

        // ------------------------------------------------------------------ rows

        /// <summary>A row's box: the dark band, the seam along its top, the red cursor when it is the hot one.
        /// Gives back the box (screen space).</summary>
        static Rect RowBox(bool enabled)
        {
            var r = R(RowX, rowY, RowW, rowH);
            int index = rowIndex++;
            rowY += rowPitch;
            if (open == List.None && r.Contains(mouse) && enabled) hotRow = index;
            bool hot = hotRow == index;
            if (Event.current.type == EventType.Repaint)
            {
                Fill(r, hot ? RowHot : RowBack);
                if (seam) GUI.DrawTexture(new Rect(r.x, r.y, r.width - 6f * u, Mathf.Max(1f, u)), seam, ScaleMode.StretchToFill);
                if (hot)
                {
                    float barH = rowH * 53f / 69f;
                    Fill(new Rect(r.x - 0.7f * u, r.y + (r.height - barH * u) * 0.5f, 3f * u, barH * u), Red);
                }
            }
            GUI.Label(new Rect(r.x + 18.6f * u, r.y, RowValueX * u - 30f * u, r.height), CurrentName, rowH > 60f ? label : labelSmall);
            return r;
        }

        static string CurrentName;

        /// <summary>A setting: its name, its value, a mark on the right. The whole row is the button.</summary>
        static bool Row(string name, string current, Mark mark, bool enabled, out Rect valueArea)
        {
            CurrentName = name;
            var r = RowBox(enabled);
            valueArea = new Rect(r.x + RowValueX * u, r.y, r.width - RowValueX * u, r.height);
            var was = GUI.color;
            GUI.color = enabled ? Color.white : new Color(1f, 1f, 1f, 0.45f);
            GUI.Label(new Rect(valueArea.x, r.y, valueArea.width - 50f * u, r.height), current, value);
            DrawMark(new Vector2(r.xMax - RowMarkFromRight * u, r.center.y), mark);
            GUI.color = was;
            return enabled && GUI.Button(r, GUIContent.none, GUIStyle.none);
        }

        /// <summary>A setting with − value + : -1 / +1 when a sign is clicked.</summary>
        static int Stepper(string name, string current)
        {
            CurrentName = name;
            var r = RowBox(true);
            float vx = r.x + RowValueX * u;
            var minus = new Rect(vx - 10f * u, r.y, 26f * u, r.height);
            var plus = new Rect(r.xMax - (RowMarkFromRight + 13f) * u, r.y, 26f * u, r.height);
            var was = GUI.color;
            GUI.color = minus.Contains(mouse) ? Red : ValueInk;
            GUI.Label(minus, "−", sign);
            GUI.color = plus.Contains(mouse) ? Red : ValueInk;
            GUI.Label(plus, "+", sign);
            GUI.color = was;
            GUI.Label(new Rect(vx + 24f * u, r.y, plus.x - vx - 24f * u, r.height), current, value);
            int step = 0;
            if (GUI.Button(minus, GUIContent.none, GUIStyle.none)) step = -1;
            if (GUI.Button(plus, GUIContent.none, GUIStyle.none)) step = 1;
            return step;
        }

        /// <summary>A setting with a thin slider and its value at the right. Returns the value (dragged or as it was).</summary>
        static float Slider(string name, float current, float min, float max, string shown)
        {
            CurrentName = name;
            var r = RowBox(true);
            var track = new Rect(r.x + (RowValueX - 26f) * u, r.center.y - 9f * u, 124f * u, 18f * u);
            float t = Mathf.InverseLerp(min, max, current);
            if (Event.current.type == EventType.Repaint)
            {
                Fill(new Rect(track.x, track.center.y - 0.5f * u, track.width, Mathf.Max(1f, u)), new Color(Dim.r, Dim.g, Dim.b, 0.7f));
                Fill(new Rect(track.x + track.width * t - 1f * u, track.center.y - 5f * u, Mathf.Max(1f, 2f * u), 10f * u), ValueInk);
            }
            var valueStyle = new GUIStyle(value) { alignment = TextAnchor.MiddleRight };
            GUI.Label(new Rect(track.xMax, r.y, r.xMax - (RowMarkFromRight - 6f) * u - track.xMax, r.height), shown, valueStyle);

            // dragging: the track (a little taller to catch the mouse)
            int id = GUIUtility.GetControlID(FocusType.Passive);
            var grab = new Rect(track.x - 6f * u, r.y, track.width + 12f * u, r.height);
            var e = Event.current;
            switch (e.GetTypeForControl(id))
            {
                case EventType.MouseDown:
                    if (grab.Contains(e.mousePosition) && e.button == 0 && open == List.None) { GUIUtility.hotControl = id; e.Use(); }
                    break;
                case EventType.MouseUp:
                    if (GUIUtility.hotControl == id) { GUIUtility.hotControl = 0; e.Use(); }
                    break;
            }
            if (GUIUtility.hotControl == id && (e.type == EventType.MouseDown || e.type == EventType.MouseDrag || e.type == EventType.Used))
                return Mathf.Lerp(min, max, Mathf.Clamp01((e.mousePosition.x - track.x) / track.width));
            return current;
        }

        static void DrawMark(Vector2 c, Mark mark)
        {
            if (Event.current.type != EventType.Repaint) return;
            switch (mark)
            {
                case Mark.On:
                    Fill(new Rect(c.x - 6f * u, c.y - 6f * u, 12f * u, 12f * u), ValueInk);
                    break;
                case Mark.Off:
                    Frame(new Rect(c.x - 6f * u, c.y - 6f * u, 12f * u, 12f * u), Dim, Mathf.Max(1f, 1.3f * u));
                    break;
                case Mark.Cycle:
                    if (cycle) GUI.DrawTexture(new Rect(c.x - 5f * u, c.y - 6.5f * u, 10f * u, 13f * u), cycle);
                    break;
                case Mark.Drop:
                    // a small chevron, two strokes
                    float s = 3.5f * u, th = Mathf.Max(1f, 1.3f * u);
                    var m = GUI.matrix;
                    GUIUtility.RotateAroundPivot(45f, c + new Vector2(-s * 0.5f, -s * 0.35f));
                    Fill(new Rect(c.x - s * 0.5f - s * 0.7f, c.y - s * 0.35f - th * 0.5f, s * 1.4f, th), Dim);
                    GUI.matrix = m;
                    GUIUtility.RotateAroundPivot(-45f, c + new Vector2(s * 0.5f, -s * 0.35f));
                    Fill(new Rect(c.x + s * 0.5f - s * 0.7f, c.y - s * 0.35f - th * 0.5f, s * 1.4f, th), Dim);
                    GUI.matrix = m;
                    break;
            }
        }

        // ------------------------------------------------------------------ looks

        static Rect R(float x, float y, float w, float h) => new Rect(x * u, y * u, w * u, h * u);

        static Color Rgb(int r, int g, int b) => new Color(r / 255f, g / 255f, b / 255f, 1f);

        static void Load()
        {
            if (!white) { white = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave }; white.SetPixel(0, 0, Color.white); white.Apply(); }
            if (!dot) dot = Circle(32);
            if (!shade) shade = Resources.Load<Texture2D>("SettingsUI/LeftShade");
            if (!tear) tear = Resources.Load<Texture2D>("SettingsUI/SignalTear");
            if (!seam) seam = Resources.Load<Texture2D>("SettingsUI/RowSeam");
            if (!cycle) cycle = Resources.Load<Texture2D>("SettingsUI/CycleMark");
            if (!mono) mono = Font.CreateDynamicFontFromOSFont(new[] { "Consolas", "Lucida Console", "Courier New" }, 16);
        }

        static Texture2D Circle(int size)
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear };
            float r = size * 0.5f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(r, r));
                    t.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(r - d)));
                }
            t.Apply();
            return t;
        }

        /// <summary>The font size whose capital letters stand <paramref name="cap"/> mock-up pixels tall.</summary>
        static int Cap(Font f, float cap, FontStyle style = FontStyle.Normal)
        {
            float ratio = 0.72f;
            if (f)
            {
                f.RequestCharactersInTexture("Н", 100, style);
                if (f.GetCharacterInfo('Н', out var ci, 100, style) && ci.glyphHeight > 0) ratio = ci.glyphHeight / 100f;
            }
            return Mathf.Max(6, Mathf.RoundToInt(cap * u / ratio));
        }

        static GUIStyle Text(Font f, float cap, Color c, TextAnchor a, FontStyle style = FontStyle.Normal)
        {
            var s = new GUIStyle(GUI.skin.label)
            {
                font = f, fontSize = Cap(f ? f : GUI.skin.font, cap, style), fontStyle = style, alignment = a,
                wordWrap = false, clipping = TextClipping.Overflow, padding = new RectOffset(0, 0, 0, 0), margin = new RectOffset(0, 0, 0, 0),
            };
            s.normal.textColor = s.hover.textColor = s.active.textColor = c;
            return s;
        }

        static void Styles()
        {
            if (Mathf.Approximately(u, styleU) && styleFont == Font && title != null) return;
            styleU = u;
            styleFont = Font;
            title = Text(Font, 81f, Ink, TextAnchor.MiddleLeft, FontStyle.Bold);
            titleShift = Text(Font, 81f, RedShift, TextAnchor.MiddleLeft, FontStyle.Bold);
            timecode = Text(mono, 10.5f, Ink, TextAnchor.MiddleRight);
            kicker = Text(mono, 10f, Dim, TextAnchor.MiddleLeft);
            tabStyle = Text(Font, 15f, Color.white, TextAnchor.MiddleLeft);
            label = Text(Font, 18.5f, Ink, TextAnchor.MiddleLeft);
            labelSmall = Text(Font, 14.5f, Ink, TextAnchor.MiddleLeft);
            value = Text(mono, 10f, ValueInk, TextAnchor.MiddleLeft);
            osd = Text(mono, 10.5f, Dim, TextAnchor.MiddleLeft);
            osdRed = Text(mono, 12.5f, Red, TextAnchor.MiddleLeft);
            key = Text(mono, 8.5f, Dim, TextAnchor.MiddleLeft);
            hint = Text(mono, 7.5f, Dim, TextAnchor.UpperLeft);
            hint.wordWrap = true;
            back = Text(Font, 17f, Color.white, TextAnchor.MiddleLeft);
            arrow = Text(mono, 16f, Color.white, TextAnchor.MiddleLeft);
            item = Text(mono, 10f, ValueInk, TextAnchor.MiddleLeft);
            sign = Text(mono, 12f, Color.white, TextAnchor.MiddleCenter);
        }

        static void Fill(Rect r, Color c)
        {
            var was = GUI.color;
            GUI.color = c * was;
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
