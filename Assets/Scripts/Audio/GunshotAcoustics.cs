using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace BodycamHorror.Audio
{
    /// <summary>
    /// Makes a gunshot sound like it was fired where it was fired. A few times a second it feels out
    /// the space around the listener with rays (how closed in it is, how far the walls are, what
    /// stands far off) and every shot is then built from layers shaped by that:
    ///   - the crack: the recording itself, at full level;
    ///   - the body: the same recording slowed down and muffled, the thump in the chest;
    ///   - the room: what the walls throw back. Tight corridors slap back at once, loud and bright;
    ///     rooms ring for a second; halls roll on for several; in the open there is next to none;
    ///   - echoes: the shot coming back off far walls and buildings, late, dull and quiet.
    /// Everything is kept dark and heavy: the shot is pitched down a little, and what comes back off
    /// the walls is dull, not bright.
    /// The same reading shapes the body camera microphone's own room sound, so every other sound
    /// the player hears sits in the same space as the shots (see BodycamMicrophone.SetSpace).
    /// Installs itself on the scene's AudioListener; guns call <see cref="Play"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public class GunshotAcoustics : MonoBehaviour
    {
        public static GunshotAcoustics Instance { get; private set; }

        [Header("Reading the space")]
        [Tooltip("What counts as walls. Not Ignore Raycast (the player).")]
        public LayerMask mask = ~(1 << 2);
        [Tooltip("Rays reach this far (m). Nothing within it in a direction = open air that way.")]
        public float reach = 60f;
        [Tooltip("Seconds between readings.")]
        public float interval = 0.25f;
        [Tooltip("How fast the reading follows the player from one space into another (1/s).")]
        public float response = 4f;

        [Header("Loudness")]
        [Tooltip("The recording itself.")]
        [Range(0f, 1f)] public float crack = 1f;
        [Tooltip("The slowed, muffled copy under it: weight.")]
        [Range(0f, 1f)] public float body = 0.9f;
        [Tooltip("How far down the body copy is pitched.")]
        [Range(0.3f, 1f)] public float bodyPitch = 0.5f;
        [Tooltip("The whole shot is played this much lower than recorded: bigger, slower, meaner.")]
        [Range(0.7f, 1f)] public float shotPitch = 0.93f;
        [Tooltip("What the surroundings throw back.")]
        [Range(0f, 1.5f)] public float room = 1f;
        [Tooltip("Late echoes off far walls.")]
        [Range(0f, 1f)] public float echoes = 0.6f;

        [Header("Debug")]
        [Tooltip("Shows the reading on screen (editor and development builds only).")]
        public Key debugKey = Key.F9;

        /// <summary>0 = open air, 1 = walls and a ceiling all round.</summary>
        public float Enclosure { get; private set; }
        /// <summary>Typical distance to the walls (m).</summary>
        public float Size { get; private set; } = 5f;
        /// <summary>The reading in a word.</summary>
        public string SpaceName =>
            Enclosure < 0.3f ? "open air" : Enclosure < 0.65f ? "street / yard"
            : Size < 2.6f ? "tight corridor" : Size < 6f ? "room" : Size < 14f ? "large room" : "hall";

        const float SpeedOfSound = 343f;
        const float TailSeconds = 4.5f;   // silence added after a clip so its reverb has time to die away
        const float DryHold = 0.07f;      // seconds of a recording kept as they are: the crack and the first of the boom
        const float DryFade = 0.08f;      // after that the recording's own room fades out this fast (s per 1/e)
        const int WetVoices = 3, EchoVoices = 4;
        const int HorizontalRays = 12;   // the first ring of Directions: level with the head

        static readonly Vector3[] Directions = BuildDirections();
        static readonly float[] Weights = new float[Directions.Length];

        readonly Dictionary<AudioClip, AudioClip> padded = new Dictionary<AudioClip, AudioClip>();
        readonly Dictionary<AudioClip, AudioClip> dryClips = new Dictionary<AudioClip, AudioClip>();
        static readonly RaycastHit[] RayHits = new RaycastHit[12];
        readonly float[] distances = new float[Directions.Length];
        readonly List<float> sorted = new List<float>(Directions.Length);
        readonly List<float> far = new List<float>(Directions.Length);
        readonly float[] echoDistance = new float[EchoVoices];

        AudioSource direct, bodySource;
        AudioSource[] wet, echo;
        AudioReverbFilter[] wetReverb;
        AudioLowPassFilter[] wetLowPass, echoLowPass;
        int nextWet, echoCount;
        float timer, targetEnclosure, targetSize = 5f, micRoom = -1f, micDecay = -1f;
        bool first = true, showDebug;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            Install();
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Install();

        /// <summary>Puts the acoustics on the scene's listener if it has none yet.</summary>
        public static void Install()
        {
            if (Instance) return;
            var listener = FindAnyObjectByType<AudioListener>();
            if (listener && !listener.GetComponent<GunshotAcoustics>()) listener.gameObject.AddComponent<GunshotAcoustics>();
        }

        static Vector3[] BuildDirections()
        {
            // a ring at head height, two rings looking up, and straight up: the floor says nothing about the space
            var list = new List<Vector3>();
            void Ring(int count, float elevation, float turn)
            {
                float y = Mathf.Sin(elevation * Mathf.Deg2Rad), r = Mathf.Cos(elevation * Mathf.Deg2Rad);
                for (int i = 0; i < count; i++)
                {
                    float a = (i / (float)count * 360f + turn) * Mathf.Deg2Rad;
                    list.Add(new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r));
                }
            }
            Ring(12, 0f, 0f);
            Ring(8, 35f, 22.5f);
            Ring(4, 65f, 0f);
            list.Add(Vector3.up);
            return list.ToArray();
        }

        void Awake()
        {
            Instance = this;
            for (int i = 0; i < Directions.Length; i++) Weights[i] = 1f + Directions[i].y * 2f;   // what is overhead counts most

            direct = Voice("Shot_Crack");
            bodySource = Voice("Shot_Body");
            bodySource.gameObject.AddComponent<AudioLowPassFilter>().cutoffFrequency = 700f;

            wet = new AudioSource[WetVoices];
            wetReverb = new AudioReverbFilter[WetVoices];
            wetLowPass = new AudioLowPassFilter[WetVoices];
            for (int i = 0; i < WetVoices; i++)
            {
                wet[i] = Voice("Shot_Room" + i);
                wetLowPass[i] = wet[i].gameObject.AddComponent<AudioLowPassFilter>();
                wetReverb[i] = wet[i].gameObject.AddComponent<AudioReverbFilter>();
                wetReverb[i].reverbPreset = AudioReverbPreset.User;
                wetReverb[i].dryLevel = -10000f;   // only what comes back off the walls
            }

            echo = new AudioSource[EchoVoices];
            echoLowPass = new AudioLowPassFilter[EchoVoices];
            for (int i = 0; i < EchoVoices; i++)
            {
                echo[i] = Voice("Shot_Echo" + i);
                echoLowPass[i] = echo[i].gameObject.AddComponent<AudioLowPassFilter>();
            }
        }

        AudioSource Voice(string voiceName)
        {
            var go = new GameObject(voiceName);
            go.transform.SetParent(transform, false);
            var source = go.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;   // the shooter's own gun: it is everywhere
            source.priority = 0;
            return source;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            foreach (var pair in padded) if (pair.Value && pair.Value != pair.Key) Destroy(pair.Value);
            foreach (var pair in dryClips) if (pair.Value && pair.Value != pair.Key) Destroy(pair.Value);
        }

        void Update()
        {
            timer -= Time.deltaTime;
            if (timer <= 0f)
            {
                timer = interval;
                Probe();
            }
            float k = first ? 1f : 1f - Mathf.Exp(-response * Time.deltaTime);
            first = false;
            Enclosure = Mathf.Lerp(Enclosure, targetEnclosure, k);
            Size = Mathf.Lerp(Size, targetSize, k);

            // everything else the microphone hears sits in the same space
            var mic = BodycamMicrophone.Instance;
            if (mic)
            {
                if (Mathf.Abs(Enclosure - micRoom) > 0.03f || Mathf.Abs(Size - micDecay) > Mathf.Max(0.15f, micDecay * 0.04f))
                {
                    micRoom = Enclosure;
                    micDecay = Size;
                    mic.SetSpace(Enclosure, Size);
                }
            }

            if ((Application.isEditor || Debug.isDebugBuild) && debugKey != Key.None
                && Keyboard.current != null && Keyboard.current[debugKey].wasPressedThisFrame) showDebug = !showDebug;
        }

        /// <summary>Feels out the space round the listener.</summary>
        void Probe()
        {
            Vector3 origin = transform.position;
            float hitWeight = 0f, totalWeight = 0f;
            sorted.Clear();
            far.Clear();
            for (int i = 0; i < Directions.Length; i++)
            {
                totalWeight += Weights[i];
                if (Wall(origin, Directions[i], out RaycastHit hit))
                {
                    distances[i] = hit.distance;
                    // a wall a long way off closes the space in less than one at arm's length
                    hitWeight += Weights[i] * Mathf.InverseLerp(reach, reach * 0.35f, hit.distance);
                    if (i < HorizontalRays) sorted.Add(hit.distance);
                    if (hit.distance > 9f && Directions[i].y < 0.5f) far.Add(hit.distance);
                }
                else
                {
                    distances[i] = -1f;
                    if (i < HorizontalRays) sorted.Add(reach);   // open that way: as far as it gets
                }
            }

            targetEnclosure = totalWeight > 0f ? hitWeight / totalWeight : 0f;
            if (sorted.Count > 0)
            {
                sorted.Sort();
                // the walls, not the ceiling, say how big a place is; and the nearer ones say it:
                // a long view down a corridor does not make it a hall
                targetSize = Mathf.Clamp(sorted[sorted.Count * 2 / 5], 0.8f, 40f);
            }
            else targetSize = 40f;

            // far surfaces worth an echo: a few, well apart in time
            far.Sort();
            echoCount = 0;
            for (int i = 0; i < far.Count && echoCount < EchoVoices; i++)
            {
                if (echoCount > 0 && far[i] < echoDistance[echoCount - 1] * 1.3f) continue;
                echoDistance[echoCount++] = far[i];
            }
        }

        /// <summary>Nearest thing in that direction that is part of the place: bodies, doors on hinges and
        /// loose props (anything with a Rigidbody) are not walls.</summary>
        bool Wall(Vector3 origin, Vector3 direction, out RaycastHit nearest)
        {
            nearest = default;
            int n = Physics.RaycastNonAlloc(origin, direction, RayHits, reach, mask, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                if (RayHits[i].distance >= best || RayHits[i].rigidbody) continue;
                best = RayHits[i].distance;
                nearest = RayHits[i];
            }
            return best < float.MaxValue;
        }

        /// <summary>Fires a shot through the space the listener is in. <paramref name="power"/>: 1 = a pistol, more for bigger guns.</summary>
        public static bool Play(AudioClip clip, float volume, float pitch, float power)
        {
            if (!Instance || !clip) return false;
            Instance.Fire(clip, volume, pitch, power);
            return true;
        }

        void Fire(AudioClip clip, float volume, float pitch, float power)
        {
            float e = Enclosure, size = Size;
            float size01 = Mathf.InverseLerp(1.5f, 25f, size);
            pitch *= shotPitch;
            Prepare(clip, out AudioClip dry, out AudioClip tailed);

            // the crack and the body: louder shut in, where there is nowhere for it to go
            direct.pitch = pitch;
            direct.PlayOneShot(dry, Mathf.Clamp01(volume * crack * Mathf.Lerp(0.85f, 1f, e)));
            bodySource.pitch = pitch * bodyPitch;
            bodySource.PlayOneShot(dry, Mathf.Clamp01(volume * body * power * Mathf.Lerp(0.7f, 1f, e)));

            // the room
            int v = nextWet;
            nextWet = (nextWet + 1) % WetVoices;
            AudioReverbFilter r = wetReverb[v];
            // closed in: the length follows the size. In the open: a long, thin, dull roll.
            r.decayTime = Mathf.Lerp(Mathf.Lerp(1.6f, 2.8f, size01), Mathf.Clamp(0.6f + 0.24f * size, 0.7f, 5f), e);
            r.decayHFRatio = Mathf.Lerp(0.35f, Mathf.Lerp(0.8f, 0.5f, size01), e);
            r.room = Mathf.Lerp(-2200f, -150f - 250f * size01, e);
            r.roomHF = Mathf.Lerp(-2500f, -900f, e);   // what comes back is dull
            r.roomLF = -100f;
            r.reflectionsLevel = Mathf.Clamp(Mathf.Lerp(-3000f, 600f - 60f * size, e), -3000f, 1000f);   // tight: a hard slap straight back
            r.reflectionsDelay = Mathf.Clamp(size / SpeedOfSound, 0.002f, 0.3f);
            r.reverbLevel = Mathf.Lerp(-900f, Mathf.Lerp(500f, 1100f, size01), e);   // the bigger the place, the more of the sound is its tail
            r.reverbDelay = Mathf.Clamp(2f * size / SpeedOfSound, 0.005f, 0.1f);
            r.diffusion = Mathf.Lerp(60f, 100f, e);
            r.density = Mathf.Lerp(40f, 100f, e);
            wetLowPass[v].cutoffFrequency = Mathf.Lerp(2200f, 5200f, e);
            wet[v].Stop();
            wet[v].clip = tailed;
            wet[v].pitch = pitch;
            wet[v].volume = Mathf.Clamp01(volume * room * power * Mathf.Lerp(0.55f, 1f, e));
            wet[v].Play();

            // echoes off whatever stands far away: each comes back after the trip there and back
            float open = 1f - 0.75f * e;
            for (int i = 0; i < echoCount; i++)
            {
                float d = echoDistance[i];
                float level = volume * echoes * power * open * Mathf.Clamp01(9f / d);
                if (level < 0.02f) continue;
                echo[i].Stop();
                echo[i].clip = dry;
                echo[i].pitch = pitch * 0.94f;
                echo[i].volume = Mathf.Clamp01(level);
                echoLowPass[i].cutoffFrequency = Mathf.Lerp(3200f, 900f, Mathf.InverseLerp(10f, 60f, d));   // air eats the highs
                echo[i].PlayScheduled(AudioSettings.dspTime + 2.0 * d / SpeedOfSound);
            }

        }

        /// <summary>Two versions of a recording, made once. Dry: the shot with the room it was recorded in
        /// faded out of it, so the only room heard is the one the player is standing in. Tailed: the
        /// dry one with silence after it, because a source's reverb is cut off the moment its clip ends.</summary>
        void Prepare(AudioClip clip, out AudioClip dry, out AudioClip tailed)
        {
            if (dryClips.TryGetValue(clip, out dry) && dry && padded.TryGetValue(clip, out tailed) && tailed) return;
            var data = new float[clip.samples * clip.channels];
            if (!clip.GetData(data, 0))
            {
                // not readable: played as recorded, and the room's answer is cut short. The shot still sounds.
                dryClips[clip] = dry = clip;
                padded[clip] = tailed = clip;
                return;
            }
            int channels = clip.channels, rate = clip.frequency;
            int hold = (int)(DryHold * rate);
            float perSample = Mathf.Exp(-1f / (DryFade * rate));
            float gain = 1f;
            for (int i = hold; i < clip.samples; i++)
            {
                gain *= perSample;
                for (int c = 0; c < channels; c++) data[i * channels + c] *= gain;
            }
            dry = AudioClip.Create(clip.name + "_dry", clip.samples, channels, rate, false);
            dry.SetData(data, 0);
            tailed = AudioClip.Create(clip.name + "_tail", clip.samples + Mathf.CeilToInt(TailSeconds * rate), channels, rate, false);
            tailed.SetData(data, 0);
            dryClips[clip] = dry;
            padded[clip] = tailed;
        }

        void OnGUI()
        {
            if (!showDebug || Event.current.type != EventType.Repaint) return;
            GUI.Label(new Rect(16f, 40f, 520f, 24f),
                $"Acoustics: {SpaceName}   closed in {Enclosure:F2}   walls at {Size:F1} m   echoes {echoCount}");
        }
    }
}
