using UnityEngine;

namespace BodycamHorror.UI
{
    /// <summary>
    /// "Saving" in the bottom right corner: an eye filling up with blood, the words beside it.
    ///
    /// The eye is a 10 s clip (Resources/UI/EyeBloodLoading: 200 frames, 20 fps, in a 16-column atlas). It is not
    /// played on a clock: the frame follows the save's progress, so the eye is full exactly when the save is done,
    /// whatever the save took.
    ///  - Frames 0..<see cref="FillEnd"/> are the filling. The shown progress moves toward the real one (Report),
    ///    or, with no reports, toward a guess from the expected time; it never runs ahead of the work: without the
    ///    end it stops short of full and only creeps on, so a slow save never shows a full eye.
    ///  - Done early: the rest of the fill is played fast (at most <see cref="catchUpTime"/> s, never slower than the
    ///    clip's own speed), then the full eye plays on at its own speed for a moment (it twitches, blinks), then it all
    ///    fades. Done late: the same, just from further on.
    ///  - It stays up at least <see cref="minVisible"/> s so a quick save does not flash.
    /// Lives across scene loads. Use: SaveIndicator.Begin(); SaveIndicator.Report(0..1) (optional); SaveIndicator.End().
    /// </summary>
    public class SaveIndicator : MonoBehaviour
    {
        const int Frames = 200, Columns = 16, Rows = 13;
        const float ClipFps = 20f;
        /// <summary>The last frame of the filling; past it the eye is full and only moves.</summary>
        const int FillEnd = 110;
        /// <summary>The last frames go back to a white eye (the clip's loop seam): never shown.</summary>
        const int LastUsable = 188;
        /// <summary>Where the eye's middle is in a frame, from the top (measured over the clip).</summary>
        const float EyeCentre = 0.45f;
        /// <summary>Capitals sit low in their line box: lift the words this much (share of the screen height).</summary>
        const float TextNudge = 0f;

        [Tooltip("Shown progress stops here until the save says it is done (0-1).")]
        public float holdShortOf = 0.9f;
        [Tooltip("Longest the rest of the fill takes once the save is done (s).")]
        public float catchUpTime = 0.8f;
        [Tooltip("The full eye plays on this long before it fades (s).")]
        public float fullHold = 0.9f;
        public float fadeIn = 0.35f, fadeOut = 0.6f;
        [Tooltip("Shortest time on screen (s).")]
        public float minVisible = 2.2f;
        [Tooltip("Size of the eye, as a share of the screen height.")]
        public float eyeHeight = 0.085f;

        static SaveIndicator instance;
        Texture2D atlas;
        GUIStyle style;
        float styleHeight;

        /// <summary>The fill at the clip's own speed takes this long (s): pass it as expectedSeconds to play it as drawn.</summary>
        public const float NaturalFillSeconds = FillEnd / ClipFps;
        /// <summary>The full eye's own part of the clip (twitches, a blink) at its own speed (s).</summary>
        public const float NaturalFullSeconds = (LastUsable - FillEnd) / ClipFps;

        /// <summary>The words' font. The first one passed to Begin is kept for the saves after it.</summary>
        public static Font DefaultFont;

        string label;
        float expected, elapsed, shown, reported = -1f, fullTime, alpha, hold;
        bool active, done, rush;
        Font font;

        /// <summary>A save has started. <paramref name="expectedSeconds"/>: a guess, used only while no progress is
        /// reported. <paramref name="rush"/>: once done, play the rest of the fill fast (a real save mid-game); off,
        /// the clip keeps its own speed to the end (behind something long anyway, like the lore).
        /// <paramref name="fullHoldSeconds"/>: how long the full eye stays before it fades (below 0: the default).</summary>
        public static void Begin(string label = "СОХРАНЕНИЕ", float expectedSeconds = 10f, bool rush = true,
                                 Font font = null, float fullHoldSeconds = -1f)
        {
            var s = Instance;
            s.label = label;
            s.expected = Mathf.Max(expectedSeconds, 0.5f);
            s.elapsed = s.shown = s.fullTime = 0f;
            s.reported = -1f;
            s.done = false;
            s.rush = rush;
            s.hold = fullHoldSeconds >= 0f ? fullHoldSeconds : s.fullHold;
            if (font && !DefaultFont) DefaultFont = font;
            if (s.font != (font ? font : DefaultFont)) s.style = null;
            s.font = font ? font : DefaultFont;
            s.active = true;
        }

        /// <summary>How far the save is (0-1). Optional.</summary>
        public static void Report(float progress)
        {
            if (instance && instance.active) instance.reported = Mathf.Max(instance.reported, Mathf.Clamp01(progress));
        }

        /// <summary>The save is done: the eye fills up and it goes.</summary>
        public static void End()
        {
            if (instance && instance.active) instance.done = true;
        }

        public static bool IsShowing => instance && instance.active;

        static SaveIndicator Instance
        {
            get
            {
                if (instance) return instance;
                var go = new GameObject("SaveIndicator");
                DontDestroyOnLoad(go);
                instance = go.AddComponent<SaveIndicator>();
                return instance;
            }
        }

        void Awake() => atlas = Resources.Load<Texture2D>("UI/EyeBloodLoading");

        void Update()
        {
            if (!active) return;
            float dt = Time.unscaledDeltaTime;
            elapsed += dt;
            float clipRate = ClipFps / FillEnd;   // the fill at the clip's own speed (progress per second)

            if (!done)
            {
                // where the work is: reported, or guessed from the time it should take; never past holdShortOf
                float target = reported >= 0f ? reported : elapsed / expected;
                target = Mathf.Min(target, holdShortOf);
                // a jump in the reports is caught up quickly but not in one frame
                shown = Mathf.MoveTowards(shown, target, dt * Mathf.Max(clipRate, (target - shown) / 0.6f));
                // stuck short of full: the eye still creeps on, slower and slower, so it does not freeze
                if (shown >= holdShortOf - 0.001f) shown = Mathf.Min(shown + dt * 0.02f * (0.99f - shown), 0.99f);
            }
            else if (shown < 1f)
            {
                // done: the rest of the fill, fast if there is a lot left (unless told not to rush), never slower than the clip
                float rate = rush ? Mathf.Max(clipRate, (1f - shown) / Mathf.Max(catchUpTime, 0.05f)) : clipRate;
                shown = Mathf.MoveTowards(shown, 1f, dt * rate);
            }
            else fullTime += dt;

            float inA = Mathf.Clamp01(elapsed / Mathf.Max(fadeIn, 0.01f));
            float leaving = 0f;
            if (done && shown >= 1f)
            {
                // the full eye stays fullHold, and long enough for the whole thing to have been up minVisible
                float fullAt = elapsed - fullTime;
                leaving = Mathf.Max(0f, fullTime - Mathf.Max(hold, minVisible - fullAt));
            }
            float outA = 1f - Mathf.Clamp01(leaving / Mathf.Max(fadeOut, 0.01f));
            alpha = Mathf.Min(inA, outA);
            if (done && shown >= 1f && leaving >= fadeOut) active = false;
        }

        int Frame()
        {
            if (shown < 1f) return Mathf.Clamp(Mathf.RoundToInt(shown * FillEnd), 0, FillEnd);
            // full: the clip plays on at its own speed (twitches, a blink), held on its last usable frame
            return Mathf.Min(FillEnd + Mathf.RoundToInt(fullTime * ClipFps), LastUsable);
        }

        void OnGUI()
        {
            GUI.depth = -200;   // over the menus and the lore card (set on every event, or the order is not kept)
            if (!active || alpha <= 0f || Event.current.type != EventType.Repaint) return;
            float h = Screen.height, w = Screen.width;
            if (style == null || !Mathf.Approximately(styleHeight, h))
            {
                styleHeight = h;
                // the lore's face (Oswald, set by the menu), in capitals, a little smaller than the lore
                style = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleRight,
                    fontSize = Mathf.RoundToInt(h * 0.021f),
                    font = font,
                    wordWrap = false,
                    clipping = TextClipping.Overflow,
                };
                style.normal.textColor = Color.white;
            }

            float eyeH = h * eyeHeight;
            float eyeW = atlas ? eyeH * (atlas.width / (float)Columns) / (atlas.height / (float)Rows) : eyeH * 1.5f;
            float margin = h * 0.035f;
            var eye = new Rect(w - margin - eyeW, h - margin - eyeH, eyeW, eyeH);

            Color old = GUI.color;
            if (atlas)
            {
                int f = Frame();
                var uv = new Rect((f % Columns) / (float)Columns, 1f - (f / Columns + 1) / (float)Rows, 1f / Columns, 1f / Rows);
                GUI.color = new Color(1f, 1f, 1f, alpha);
                GUI.DrawTextureWithTexCoords(eye, atlas, uv, true);
            }
            // the words just left of the eye, on its middle line, dots running after them while it works; the dots
            // have a place of their own so the word does not shift as they come and go
            var dotsStyle = new GUIStyle(style) { alignment = TextAnchor.MiddleLeft };
            float dotsW = dotsStyle.CalcSize(new GUIContent("...")).x;
            // the eye sits a little above the middle of its frame (EyeCentre): the words go on the eye's own centre line,
            // nudged by TextNudge for where capitals sit in the line box
            float gap = h * 0.012f, lineH = h * 0.06f, top = eye.y + eye.height * EyeCentre - lineH * 0.5f + h * TextNudge;
            float breath = done ? 1f : 0.8f + 0.2f * Mathf.Sin(elapsed * 2.4f);
            GUI.color = new Color(0.8f, 0.78f, 0.74f, alpha * breath);
            float textRight = eye.x - gap - dotsW;
            GUI.Label(new Rect(textRight - h * 0.6f, top, h * 0.6f, lineH), label, style);
            int dots = done ? 3 : (int)(elapsed * 2.2f) % 4;
            GUI.Label(new Rect(textRight + h * 0.002f, top, dotsW + h * 0.01f, lineH), new string('.', dots), dotsStyle);
            GUI.color = old;
        }
    }
}
