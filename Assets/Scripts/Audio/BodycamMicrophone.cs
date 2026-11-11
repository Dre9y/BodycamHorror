using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace BodycamHorror.Audio
{
    /// <summary>
    /// Makes everything the player hears sound as if recorded by the body camera's own microphone:
    /// a narrow band (no deep lows, no airy highs), automatic gain that ducks hard after a loud sound
    /// and creeps back, overload on gunshots, a little hiss and a narrowed stereo image. A short
    /// small-room reverb in front of it glues dry library recordings into the same space.
    /// Installs itself on the scene's AudioListener, so nothing has to be set up per scene.
    /// </summary>
    [DisallowMultipleComponent]
    public class BodycamMicrophone : MonoBehaviour
    {
        public static BodycamMicrophone Instance { get; private set; }

        [Tooltip("Processing off: the game sounds as it did before (also switched by the key below).")]
        public bool bypass;
        [Tooltip("Switches the processing on and off to compare (editor and development builds only).")]
        public Key toggleKey = Key.F8;
        [Range(0f, 1f)] public float mix = 1f;

        [Header("Band")]
        [Tooltip("Lows below this are cut (Hz): a small microphone has no bass.")]
        [Range(20f, 500f)] public float lowCut = 140f;
        [Tooltip("Highs above this are cut (Hz).")]
        [Range(2000f, 16000f)] public float highCut = 7500f;

        [Header("Auto gain")]
        [Tooltip("Sounds louder than this get turned down (dB).")]
        [Range(-40f, 0f)] public float threshold = -22f;
        [Tooltip("How hard loud sounds are turned down.")]
        [Range(1f, 20f)] public float ratio = 6f;
        [Tooltip("How fast the gain drops on a loud sound (ms). Slower lets more of a gunshot overload.")]
        [Range(0.5f, 50f)] public float attack = 5f;
        [Tooltip("How long the gain takes to come back after a loud sound (ms): the 'pumping' after a shot.")]
        [Range(50f, 3000f)] public float release = 700f;
        [Tooltip("Everything is raised by this (dB), so quiet sounds are close and loud ones squashed.")]
        [Range(0f, 18f)] public float makeup = 8f;

        [Header("Overload")]
        [Tooltip("Level at which the microphone clips (0-1). Lower = dirtier gunshots.")]
        [Range(0.2f, 1f)] public float ceiling = 0.75f;

        [Header("Character")]
        [Tooltip("Microphone hiss (dB). -90 and below is silent.")]
        [Range(-90f, -40f)] public float noise = -64f;
        [Tooltip("0 = mono like a real body camera, 1 = full stereo. Kept part-way so direction can still be heard.")]
        [Range(0f, 1f)] public float stereoWidth = 0.6f;

        [Header("Room")]
        [Tooltip("Short reverb of a small hard room, applied before the microphone. 0 = off.")]
        [Range(0f, 1f)] public float room = 0.55f;
        [Tooltip("Reverb tail length (s).")]
        [Range(0.1f, 3f)] public float roomDecay = 0.7f;

        const int MaxChannels = 8;

        AudioReverbFilter reverb;
        float labelTimer;

        // read on the audio thread
        volatile bool ready;
        float hpB0, hpB1, hpB2, hpA1, hpA2;
        float lpB0, lpB1, lpB2, lpA1, lpA2;
        float attackCoef, releaseCoef, thresholdLin, slope, makeupLin, noiseLin;
        float ceilingNow = 0.75f, widthNow = 0.6f, mixNow = 1f;

        // audio thread state
        readonly float[] hpX1 = new float[MaxChannels], hpX2 = new float[MaxChannels], hpY1 = new float[MaxChannels], hpY2 = new float[MaxChannels];
        readonly float[] lpX1 = new float[MaxChannels], lpX2 = new float[MaxChannels], lpY1 = new float[MaxChannels], lpY2 = new float[MaxChannels];
        readonly float[] frame = new float[MaxChannels];
        float envelope;
        uint rng = 0x2545F491u;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            Install();
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Install();

        /// <summary>Puts the microphone on the scene's listener if it has none yet.</summary>
        public static void Install()
        {
            if (Instance) return;
            var listener = FindAnyObjectByType<AudioListener>();
            if (!listener || listener.GetComponent<BodycamMicrophone>()) return;
            // filters run in component order: the room first, then the microphone that records it
            if (!listener.GetComponent<AudioReverbFilter>()) listener.gameObject.AddComponent<AudioReverbFilter>();
            listener.gameObject.AddComponent<BodycamMicrophone>();
        }

        void Awake()
        {
            Instance = this;
            reverb = GetComponent<AudioReverbFilter>();
        }

        void OnEnable() => Refresh();
        void OnValidate() { if (Application.isPlaying && isActiveAndEnabled) Refresh(); }
        void OnDisable() => ready = false;
        void OnDestroy() { if (Instance == this) Instance = null; }

        void Update()
        {
            if ((Application.isEditor || Debug.isDebugBuild) && toggleKey != Key.None
                && Keyboard.current != null && Keyboard.current[toggleKey].wasPressedThisFrame)
            {
                bypass = !bypass;
                labelTimer = 1.5f;
                Refresh();
            }
            if (labelTimer > 0f) labelTimer -= Time.unscaledDeltaTime;
        }

        /// <summary>Recomputes the filters from the inspector values (main thread).</summary>
        public void Refresh()
        {
            float rate = AudioSettings.outputSampleRate;
            if (rate <= 0f) return;

            Biquad(false, Mathf.Min(lowCut, rate * 0.45f), rate, out hpB0, out hpB1, out hpB2, out hpA1, out hpA2);
            Biquad(true, Mathf.Min(highCut, rate * 0.45f), rate, out lpB0, out lpB1, out lpB2, out lpA1, out lpA2);
            attackCoef = 1f - Mathf.Exp(-1f / (attack * 0.001f * rate));
            releaseCoef = 1f - Mathf.Exp(-1f / (release * 0.001f * rate));
            thresholdLin = Mathf.Pow(10f, threshold / 20f);
            slope = 1f / ratio - 1f;
            makeupLin = Mathf.Pow(10f, makeup / 20f);
            noiseLin = noise <= -89.5f ? 0f : Mathf.Pow(10f, noise / 20f);
            ceilingNow = ceiling;
            widthNow = stereoWidth;
            mixNow = bypass ? 0f : mix;
            ready = true;

            if (reverb)
            {
                reverb.enabled = !bypass && room > 0.001f;
                reverb.reverbPreset = AudioReverbPreset.User;
                reverb.dryLevel = 0f;
                reverb.room = Mathf.Lerp(-3000f, -300f, Mathf.Sqrt(room));   // mB
                reverb.roomHF = -700f;
                reverb.roomLF = -300f;
                reverb.decayTime = roomDecay;
                reverb.decayHFRatio = 0.55f;
                reverb.reflectionsLevel = -350f;
                reverb.reflectionsDelay = 0.006f;
                reverb.reverbLevel = 150f;
                reverb.reverbDelay = 0.012f;
                reverb.diffusion = 100f;
                reverb.density = 100f;
            }
        }

        // RBJ cookbook, Butterworth Q
        static void Biquad(bool lowPass, float freq, float rate, out float b0, out float b1, out float b2, out float a1, out float a2)
        {
            float w = 2f * Mathf.PI * freq / rate;
            float cos = Mathf.Cos(w), alpha = Mathf.Sin(w) / (2f * 0.7071f);
            float a0 = 1f + alpha;
            if (lowPass)
            {
                b1 = (1f - cos) / a0;
                b0 = b2 = b1 * 0.5f;
            }
            else
            {
                b1 = -(1f + cos) / a0;
                b0 = b2 = -b1 * 0.5f;
            }
            a1 = -2f * cos / a0;
            a2 = (1f - alpha) / a0;
        }

        void OnAudioFilterRead(float[] data, int channels)
        {
            if (!ready || channels < 1 || channels > MaxChannels) return;
            float wet = mixNow;
            if (wet <= 0.0001f) return;

            float width = widthNow, top = ceilingNow, hiss = noiseLin;
            float env = envelope;
            uint r = rng;

            for (int i = 0; i < data.Length; i += channels)
            {
                // narrow the stereo image
                if (channels == 2)
                {
                    float mid = (data[i] + data[i + 1]) * 0.5f, side = (data[i] - data[i + 1]) * 0.5f * width;
                    frame[0] = mid + side;
                    frame[1] = mid - side;
                }
                else for (int c = 0; c < channels; c++) frame[c] = data[i + c];

                // hiss + band limit
                r ^= r << 13; r ^= r >> 17; r ^= r << 5;
                float n = ((r & 0xFFFF) / 32768f - 1f) * hiss;
                float peak = 0f;
                for (int c = 0; c < channels; c++)
                {
                    float x = frame[c] + n;
                    float y = hpB0 * x + hpB1 * hpX1[c] + hpB2 * hpX2[c] - hpA1 * hpY1[c] - hpA2 * hpY2[c];
                    hpX2[c] = hpX1[c]; hpX1[c] = x; hpY2[c] = hpY1[c]; hpY1[c] = y;
                    x = y;
                    y = lpB0 * x + lpB1 * lpX1[c] + lpB2 * lpX2[c] - lpA1 * lpY1[c] - lpA2 * lpY2[c] + 1e-20f;
                    lpX2[c] = lpX1[c]; lpX1[c] = x; lpY2[c] = lpY1[c]; lpY1[c] = y;
                    frame[c] = y;
                    float a = y < 0f ? -y : y;
                    if (a > peak) peak = a;
                }

                // automatic gain: one envelope for all channels, so the image does not wander
                env += (peak - env) * (peak > env ? attackCoef : releaseCoef);
                float gain = makeupLin;
                if (env > thresholdLin) gain *= Mathf.Pow(env / thresholdLin, slope);

                // overload, then blend with the untouched signal
                for (int c = 0; c < channels; c++)
                {
                    float y = top * (float)System.Math.Tanh(frame[c] * gain / top);
                    data[i + c] += (y - data[i + c]) * wet;
                }
            }

            if (float.IsNaN(env) || float.IsInfinity(env))
            {
                env = 0f;
                for (int c = 0; c < MaxChannels; c++) hpX1[c] = hpX2[c] = hpY1[c] = hpY2[c] = lpX1[c] = lpX2[c] = lpY1[c] = lpY2[c] = 0f;
            }
            envelope = env;
            rng = r;
        }

        void OnGUI()
        {
            if (labelTimer <= 0f || Event.current.type != EventType.Repaint) return;
            GUI.Label(new Rect(16f, 16f, 400f, 24f), bypass ? "Bodycam mic: OFF" : "Bodycam mic: ON");
        }
    }
}
