using BodycamHorror.UI;
using UnityEngine;

namespace BodycamHorror.Cutscenes
{
    /// <summary>
    /// The titles after the elevator finale. Once the cab has gone black and the cutscene is over: a beat of black,
    /// then END / КОНЕЦ slams in with a tear of interference (the letters jump, split into red and blue, the frame
    /// crawls with noise and dropout lines) that settles to a faint unease; a moment later a question mark jumps
    /// onto the end of the word with a second, smaller tear: END? / КОНЕЦ?. The word follows the game's language.
    /// Drawn over everything on a black screen.
    /// </summary>
    public class EndTitles : MonoBehaviour
    {
        [Tooltip("Starts when this cutscene finishes. Empty: call Begin() yourself.")]
        public ElevatorFinaleCutscene cutscene;

        public string endRu = "КОНЕЦ";
        public string endEn = "END";
        public string question = "?";
        public Font endFont;
        public Color endColor = new Color(0.82f, 0.78f, 0.72f);

        [Header("Sound")]
        [Tooltip("Starts the moment END hits (its own hit at 0 s, the question mark's static at the question mark).")]
        public AudioClip sound;
        [Range(0f, 1f)] public float volume = 0.9f;

        [Header("Timing (s)")]
        [Tooltip("Black after the cutscene before END hits.")]
        public float blackBefore = 0.9f;
        [Tooltip("The hard interference as END appears, before it settles.")]
        public float rippleTime = 0.7f;
        [Tooltip("From END to the question mark jumping on.")]
        public float questionDelay = 2.2f;
        [Tooltip("The question mark's own, shorter tear.")]
        public float questionRippleTime = 0.45f;

        [Header("Look")]
        [Tooltip("Interference left once it has settled (0 = a clean title).")]
        [Range(0f, 0.5f)] public float settledRipple = 0.12f;
        [Range(0.05f, 0.3f)] public float endSize = 0.16f;

        const int Slices = 10;

        float started = -1f;
        bool soundStarted;
        AudioSource source;
        Texture2D noise;
        Color32[] noisePixels;
        GUIStyle endStyle;
        readonly float[] sliceShift = new float[Slices];

        void Start()
        {
            if (cutscene) cutscene.onFinished.AddListener(Begin);
        }

        void OnDestroy()
        {
            if (cutscene) cutscene.onFinished.RemoveListener(Begin);
            if (noise) Destroy(noise);
        }

        public void Begin()
        {
            if (started >= 0f) return;
            started = Time.unscaledTime;
            noise = new Texture2D(384, 216, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Repeat };
            noisePixels = new Color32[noise.width * noise.height];
        }

        float Since => started < 0f ? -1f : Time.unscaledTime - started;

        /// <summary>0..1 interference: a hard hit as END lands, falling off to a low simmer with the odd flare.</summary>
        float Ripple(float t)
        {
            float hit = t - blackBefore;
            if (hit < 0f) return 0f;
            float fall = Mathf.Clamp01(hit / Mathf.Max(rippleTime, 0.01f));
            float r = Mathf.Lerp(1f, settledRipple, fall * fall * (3f - 2f * fall));
            // the question mark lands with a second hit, not as hard
            float q = hit - questionDelay;
            if (q >= 0f)
            {
                float qf = Mathf.Clamp01(q / Mathf.Max(questionRippleTime, 0.01f));
                r = Mathf.Max(r, Mathf.Lerp(0.8f, settledRipple, qf * qf * (3f - 2f * qf)));
            }
            // now and then a brief flare, as if the signal slips
            if (fall >= 1f && Mathf.PerlinNoise(t * 1.7f, 0.37f) > 0.78f) r = Mathf.Max(r, 0.45f);
            return r;
        }

        void Update()
        {
            float t = Since;
            if (t < blackBefore) return;
            if (!soundStarted)
            {
                soundStarted = true;
                if (sound)
                {
                    source = gameObject.AddComponent<AudioSource>();
                    source.playOnAwake = false;
                    source.spatialBlend = 0f;
                    source.ignoreListenerPause = true;
                    source.clip = sound;
                    source.volume = volume;
                    source.Play();
                }
            }
            float r = Ripple(t);
            for (int i = 0; i < Slices; i++)
            {
                bool slip = Random.value < 0.15f + 0.7f * r;
                float want = slip ? Random.Range(-1f, 1f) * (0.01f + 0.12f * r) : 0f;
                sliceShift[i] = Mathf.Lerp(sliceShift[i], want, 0.6f);
            }
            byte strength = (byte)Mathf.Clamp(30 + 200 * r, 0, 255);
            for (int i = 0; i < noisePixels.Length; i++)
            {
                byte v = (byte)Random.Range(0, 256);
                noisePixels[i] = new Color32(v, v, v, (byte)(Random.value < 0.5f ? strength : 0));
            }
            for (int k = 0; k < (int)(r * 6f); k++)
            {
                int y = Random.Range(0, noise.height), row = y * noise.width;
                byte v = (byte)Random.Range(140, 256);
                for (int x = 0; x < noise.width; x++) noisePixels[row + x] = new Color32(v, v, v, (byte)(90 + 140 * r));
            }
            noise.SetPixels32(noisePixels);
            noise.Apply(false);
        }

        void OnGUI()
        {
            if (started < 0f || Event.current.type != EventType.Repaint) return;
            float t = Since;
            var screen = new Rect(0f, 0f, Screen.width, Screen.height);
            GUI.depth = -1000;
            GUI.color = Color.black;
            GUI.DrawTexture(screen, Texture2D.whiteTexture);
            GUI.color = Color.white;   // GUI.color tints text too: left black, every word came out black
            if (t < blackBefore) return;

            float r = Ripple(t);
            bool english = Loc.English;
            if (endStyle == null)
                endStyle = new GUIStyle { font = endFont, alignment = TextAnchor.MiddleLeft, clipping = TextClipping.Overflow };
            endStyle.fontSize = Mathf.RoundToInt(Screen.height * endSize);

            // END: appears at once, flashing bright on the hit, cut into slices that slip sideways. The line is laid
            // out as END? from the start, so the word does not jump aside when the question mark lands in its place
            var word = new GUIContent(english ? endEn : endRu);
            var mark = new GUIContent(question);
            float fullW = endStyle.CalcSize(new GUIContent(word.text + question)).x;
            float left = (Screen.width - fullW) * 0.5f;
            float markX = left + (fullW - endStyle.CalcSize(mark).x);
            float sinceMark = t - blackBefore - questionDelay;
            bool markShown = sinceMark >= 0f;

            float endH = Screen.height * endSize * 1.3f;
            var endRect = new Rect(0f, Screen.height * 0.42f - endH * 0.5f, Screen.width, endH);
            float flash = 1f + 1.4f * Mathf.Clamp01(1f - (t - blackBefore) / 0.12f);   // white-hot for a frame or two
            float markFlash = markShown ? 1f + 1.4f * Mathf.Clamp01(1f - sinceMark / 0.12f) : 1f;
            float flicker = Random.value < 0.06f + 0.4f * r ? Random.Range(0.25f, 0.8f) : 1f;
            float alpha = Mathf.Clamp01(flicker * (1f - 0.2f * r * Random.value));
            Vector2 shake = Random.insideUnitCircle * (Screen.height * 0.012f * r);
            float split = Screen.height * (0.002f + 0.02f * r);
            float sliceH = endRect.height / Slices;
            for (int i = 0; i < Slices; i++)
            {
                // each slice is the whole line, clipped to its band and shifted by its own amount
                var band = new Rect(0f, endRect.y + i * sliceH, Screen.width, sliceH + 0.5f);
                GUI.BeginGroup(band);
                float dx = sliceShift[i] * Screen.width + shake.x, y = -i * sliceH + shake.y;
                DrawPiece(word, left + dx, y, endRect.height, alpha, flash, split, r);
                if (markShown) DrawPiece(mark, markX + dx, y, endRect.height, alpha, markFlash, split, r);
                GUI.EndGroup();
            }

            // interference over the whole frame: grain, dropout lines, a band on the hit
            GUI.color = new Color(1f, 1f, 1f, 0.06f + 0.4f * r);
            GUI.DrawTextureWithTexCoords(screen, noise, new Rect(Random.value, Random.value, 1f, 1f));
            if (r > 0.3f)
            {
                GUI.color = new Color(1f, 1f, 1f, 0.12f * r);
                GUI.DrawTexture(new Rect(0f, Random.value * Screen.height, Screen.width, Screen.height * Random.Range(0.02f, 0.1f)), Texture2D.whiteTexture);
            }
            GUI.color = Color.white;
        }

        /// <summary>One piece of the line in its slice: a red and a blue ghost drifting off it, then the letters.</summary>
        void DrawPiece(GUIContent text, float x, float y, float height, float alpha, float flash, float split, float r)
        {
            float w = endStyle.CalcSize(text).x + 4f;
            if (r > 0.05f)
            {
                float ghost = alpha * Mathf.Clamp01(r * 1.5f) * 0.8f;
                endStyle.normal.textColor = new Color(0.9f, 0.05f, 0.05f, ghost);
                endStyle.Draw(new Rect(x - split, y, w, height), text, false, false, false, false);
                endStyle.normal.textColor = new Color(0.1f, 0.25f, 0.95f, ghost);
                endStyle.Draw(new Rect(x + split, y, w, height), text, false, false, false, false);
            }
            endStyle.normal.textColor = new Color(Mathf.Min(endColor.r * flash, 1f), Mathf.Min(endColor.g * flash, 1f), Mathf.Min(endColor.b * flash, 1f), alpha);
            endStyle.Draw(new Rect(x, y, w, height), text, false, false, false, false);
        }
    }
}
