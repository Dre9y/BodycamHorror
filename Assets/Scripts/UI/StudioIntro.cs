using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace BodycamHorror.UI
{
    /// <summary>
    /// The developers' intro before the main menu. Black, the sound starts; the two photos come up out of the
    /// dark, dim, with the nicknames under them in the distressed font; for the length of the sound the picture
    /// flickers, tears sideways in strips, splits its colours and crawls with noise, harder on the sound's loud
    /// beats and towards the end; then everything sinks back to black and the menu (which fades in by itself) loads.
    /// The first time it cannot be skipped; once it has been seen to the end, any key or click cuts it short.
    /// </summary>
    public class StudioIntro : MonoBehaviour
    {
        [System.Serializable]
        public class Developer
        {
            public Texture2D photo;
            public string nickname;
        }

        public Developer[] developers =
        {
            new Developer { nickname = "Dregy" },
            new Developer { nickname = "LMM" },
        };
        public Font nicknameFont;
        public Color nicknameColor = new Color(0.72f, 0.66f, 0.6f);

        [Header("Sound")]
        public AudioClip sound;
        [Range(0f, 1f)] public float volume = 1f;
        [Tooltip("Length of the intro without a sound (s). With one, the sound's length.")]
        public float lengthWithoutSound = 19f;

        [Header("Timing (s)")]
        [Tooltip("Black, with the sound already playing, before the photos start to come up.")]
        public float blackBefore = 1.5f;
        public float photosFadeIn = 3f;
        [Tooltip("The photos sink back to black this long before the end.")]
        public float photosFadeOut = 2.5f;
        [Tooltip("Black after the photos are gone, before the menu loads.")]
        public float blackAfter = 1.2f;
        [Tooltip("A skip (only once the intro has been seen) fades out this fast.")]
        public float skipFade = 0.6f;

        [Header("Look")]
        [Tooltip("How bright the photos get at most: they stay dim, out of the dark.")]
        [Range(0.2f, 1.5f)] public float photoBrightness = 0.85f;
        [Tooltip("Photo size, share of the screen height.")]
        [Range(0.15f, 0.5f)] public float photoSize = 0.42f;
        [Tooltip("How hard the tearing, flicker and noise get (scaled up by the sound's loudness and towards the end).")]
        [Range(0f, 2f)] public float glitch = 1f;

        [Header("Then")]
        public string nextScene = "MainMenu";

        const string SeenKey = "intro_seen";
        const int Strips = 28;

        AudioSource source;
        AsyncOperation loading;
        Texture2D noise, white;
        Color32[] noisePixels;
        GUIStyle nameStyle;
        float startTime, length, skipAt = -1f, level, burst, burstUntil;
        bool skippable, done;
        readonly float[] samples = new float[256];
        readonly float[] stripShift = new float[Strips];

        void Awake()
        {
            Cursor.visible = false;
            skippable = PlayerPrefs.GetInt(SeenKey, 0) == 1;
            length = sound ? sound.length : lengthWithoutSound;

            source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            source.clip = sound;
            source.volume = volume;

            white = Texture2D.whiteTexture;
            noise = new Texture2D(384, 216, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Repeat };
            noisePixels = new Color32[noise.width * noise.height];
        }

        IEnumerator Start()
        {
            // the menu loads in the background, so it is there the moment the intro ends
            loading = SceneManager.LoadSceneAsync(nextScene);
            if (loading != null) loading.allowSceneActivation = false;
            yield return null;
            startTime = Time.unscaledTime;
            if (sound) source.Play();
        }

        void OnDestroy()
        {
            if (noise) Destroy(noise);
        }

        float Now => startTime > 0f ? Time.unscaledTime - startTime : 0f;

        void Update()
        {
            if (done || startTime <= 0f) return;
            float t = Now;

            if (skippable && skipAt < 0f && AnyPress()) skipAt = t;

            // the sound's loudness, quick to rise and slow to fall: the beats drive the flicker
            float rms = 0f;
            if (source.isPlaying)
            {
                source.GetOutputData(samples, 0);
                foreach (float s in samples) rms += s * s;
                rms = Mathf.Sqrt(rms / samples.Length);
            }
            float target = Mathf.Clamp01(rms * 4f);
            level = target > level ? Mathf.Lerp(level, target, 0.6f) : Mathf.MoveTowards(level, target, Time.unscaledDeltaTime * 1.5f);

            // now and then, and on a hard beat, a short burst of heavy tearing
            if (t > burstUntil && (Random.value < Time.unscaledDeltaTime * 0.6f || (target > 0.55f && Random.value < 0.15f)))
            {
                burstUntil = t + Random.Range(0.06f, 0.22f);
                burst = Random.Range(0.6f, 1f);
            }
            if (t > burstUntil) burst = 0f;

            float g = Intensity(t);
            for (int i = 0; i < Strips; i++)
            {
                // most strips sit still; a few slip sideways, more of them the harder it glitches
                bool slip = Random.value < 0.08f + 0.5f * g;
                float want = slip ? Random.Range(-1f, 1f) * (0.004f + 0.05f * g) : 0f;
                stripShift[i] = Mathf.Lerp(stripShift[i], want, 0.5f);
            }

            RefreshNoise(g);

            float end = skipAt >= 0f ? skipAt + skipFade : length;
            if (t >= end + (skipAt >= 0f ? 0.1f : 0f)) Finish();
        }

        /// <summary>0..1: how hard it glitches. Builds over the intro, rises with the sound, spikes in bursts.</summary>
        float Intensity(float t)
        {
            float build = Mathf.SmoothStep(0f, 1f, t / Mathf.Max(length, 0.01f));
            return Mathf.Clamp01((0.12f + 0.35f * build + 0.45f * level + burst) * glitch);
        }

        /// <summary>0..1: how far the photos are up out of the black.</summary>
        float PhotoLevel(float t)
        {
            float inA = Mathf.Clamp01((t - blackBefore) / Mathf.Max(photosFadeIn, 0.01f));
            float outStart = length - blackAfter - photosFadeOut;
            float outA = 1f - Mathf.Clamp01((t - outStart) / Mathf.Max(photosFadeOut, 0.01f));
            float a = Mathf.Min(inA, outA);
            if (skipAt >= 0f) a *= 1f - Mathf.Clamp01((t - skipAt) / Mathf.Max(skipFade, 0.01f));
            return Mathf.SmoothStep(0f, 1f, a);
        }

        void RefreshNoise(float g)
        {
            // fresh grain every frame: a little always, a lot in a burst
            byte strength = (byte)Mathf.Clamp(40 + 160 * g, 0, 255);
            for (int i = 0; i < noisePixels.Length; i++)
            {
                byte v = (byte)Random.Range(0, 256);
                noisePixels[i] = new Color32(v, v, v, (byte)(Random.value < 0.5f ? strength : 0));
            }
            // a few dropout lines: a whole row of the frame flashes
            for (int k = 0; k < (int)(g * 4f); k++)
            {
                int y = Random.Range(0, noise.height), row = y * noise.width;
                byte v = (byte)Random.Range(120, 256);
                for (int x = 0; x < noise.width; x++) noisePixels[row + x] = new Color32(v, v, v, (byte)(80 + 120 * g));
            }
            noise.SetPixels32(noisePixels);
            noise.Apply(false);
        }

        void OnGUI()
        {
            if (Event.current.type != EventType.Repaint) return;
            var screen = new Rect(0f, 0f, Screen.width, Screen.height);
            GUI.color = Color.black;
            GUI.DrawTexture(screen, white);
            if (startTime <= 0f) { GUI.color = Color.white; return; }

            float t = Now;
            float up = PhotoLevel(t);
            float g = Intensity(t);
            if (up > 0.001f)
            {
                // flicker: the picture dips and jumps with the beat, sometimes nearly out
                float flicker = 1f - 0.25f * g * Random.value;
                if (Random.value < 0.04f + 0.12f * g) flicker *= Random.Range(0.15f, 0.6f);
                flicker += level * 0.25f;
                float bright = photoBrightness * up * flicker;

                int n = developers.Length;
                float size = Screen.height * photoSize;
                float gap = size * 0.35f;
                float total = n * size + (n - 1) * gap;
                float x0 = (Screen.width - total) * 0.5f;
                float y0 = Screen.height * 0.5f - size * 0.62f;
                // the whole frame shakes a pixel or two on the beats
                Vector2 shake = Random.insideUnitCircle * (Screen.height * 0.004f * g);

                for (int i = 0; i < n; i++)
                {
                    var dev = developers[i];
                    var rect = new Rect(x0 + i * (size + gap) + shake.x, y0 + shake.y, size, size);
                    if (dev.photo) DrawTorn(dev.photo, rect, bright, g);
                    DrawName(dev.nickname, rect, up * Mathf.Clamp01(flicker), g);
                }
            }

            // grain and dropout lines over everything, and a slow rolling band like a worn tape
            float grain = (0.1f + 0.6f * g) * Mathf.Max(up, 0.25f);
            GUI.color = new Color(1f, 1f, 1f, grain * 0.22f);
            GUI.DrawTextureWithTexCoords(screen, noise, new Rect(Random.value, Random.value, 1f, 1f));
            float band = Mathf.Repeat(t * 0.18f, 1.3f) - 0.15f;
            GUI.color = new Color(1f, 1f, 1f, 0.035f * up);
            GUI.DrawTexture(new Rect(0f, band * Screen.height, Screen.width, Screen.height * 0.06f), white);
            GUI.color = Color.white;
        }

        /// <summary>The photo in horizontal strips, each slipped sideways by its own amount, with the colours split.</summary>
        void DrawTorn(Texture2D photo, Rect rect, float bright, float g)
        {
            float h = rect.height / Strips;
            float split = rect.width * (0.002f + 0.012f * g);
            for (int i = 0; i < Strips; i++)
            {
                float dx = stripShift[i] * rect.width;
                var strip = new Rect(rect.x + dx, rect.y + i * h, rect.width, h + 0.5f);
                // GUI rects run down the screen, UVs run up the texture
                var uv = new Rect(0f, 1f - (i + 1f) / Strips, 1f, 1f / Strips);
                GUI.color = new Color(bright, bright, bright, 1f);
                GUI.DrawTextureWithTexCoords(strip, photo, uv);
                if (g > 0.25f)
                {
                    // the red and the blue drift apart from the picture
                    float a = Mathf.Clamp01((g - 0.25f) * 0.6f) * bright;
                    GUI.color = new Color(bright, 0f, 0f, a);
                    GUI.DrawTextureWithTexCoords(new Rect(strip.x - split, strip.y, strip.width, strip.height), photo, uv);
                    GUI.color = new Color(0f, 0f, bright, a);
                    GUI.DrawTextureWithTexCoords(new Rect(strip.x + split, strip.y, strip.width, strip.height), photo, uv);
                }
            }
        }

        void DrawName(string nickname, Rect photo, float alpha, float g)
        {
            if (string.IsNullOrEmpty(nickname) || alpha <= 0.001f) return;
            if (nameStyle == null)
                nameStyle = new GUIStyle { font = nicknameFont, alignment = TextAnchor.UpperCenter };
            nameStyle.fontSize = Mathf.RoundToInt(Screen.height * 0.055f);
            var r = new Rect(photo.x - photo.width * 0.25f, photo.yMax + Screen.height * 0.035f, photo.width * 1.5f, Screen.height * 0.08f);
            var content = new GUIContent(nickname);
            // a red ghost slipping off the letters, then the letters, shaking a little
            Vector2 jitter = Random.insideUnitCircle * (Screen.height * 0.003f * g);
            nameStyle.normal.textColor = new Color(0.55f, 0.05f, 0.04f, alpha * (0.25f + 0.5f * g));
            GUI.color = Color.white;
            nameStyle.Draw(new Rect(r.x + jitter.x + Screen.height * 0.004f * g, r.y + jitter.y, r.width, r.height), content, false, false, false, false);
            nameStyle.normal.textColor = new Color(nicknameColor.r, nicknameColor.g, nicknameColor.b, alpha);
            nameStyle.Draw(new Rect(r.x + jitter.x, r.y + jitter.y, r.width, r.height), content, false, false, false, false);
        }

        static bool AnyPress()
        {
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            var pad = Gamepad.current;
            return (kb != null && kb.anyKey.wasPressedThisFrame)
                   || (mouse != null && (mouse.leftButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame))
                   || (pad != null && (pad.buttonSouth.wasPressedThisFrame || pad.startButton.wasPressedThisFrame));
        }

        void Finish()
        {
            if (done) return;
            done = true;
            // seen to the end (or skipped, which is only possible once it has been): next time it may be skipped
            PlayerPrefs.SetInt(SeenKey, 1);
            PlayerPrefs.Save();
            if (source) source.Stop();
            Cursor.visible = true;
            if (loading != null) loading.allowSceneActivation = true;
            else SceneManager.LoadScene(nextScene);
        }
    }
}
