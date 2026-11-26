using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BodycamHorror.Audio
{
    /// <summary>
    /// Places a sound around the listener's head more sharply than Unity's own panning. The source is made 2D and
    /// this filter renders it to the two ears itself:
    /// - level: the source's own rolloff (logarithmic / linear / custom curve, min / max distance) is kept;
    /// - side: the near ear gets it louder, the far one quieter, a fraction of a millisecond later and dulled, as the
    ///   head shadows it, so the side it comes from is unmistakable;
    /// - front / back: a sound behind the head loses its top and a little level, so behind sounds different from
    ///   in front even when the two would pan the same.
    /// Added automatically to every 3D AudioSource (see <see cref="SpatialSoundInstaller"/>); one-shots placed in the
    /// world should go through <see cref="PlayAt"/>, so they are placed from their first sample.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(AudioSource))]
    public class SpatialSound : MonoBehaviour
    {
        [Tooltip("How hard sounds are pushed to the side they come from (0 = centre, 1 = fully one ear).")]
        [Range(0f, 1f)] public float panStrength = 0.95f;
        [Tooltip("How much louder the near ear hears a sound fully to one side.")]
        [Range(0f, 1f)] public float nearBoost = 0.35f;
        [Tooltip("How much quieter the far ear hears it (1 = silent).")]
        [Range(0f, 1f)] public float farCut = 0.8f;
        [Tooltip("The far ear's cutoff (Hz) for a sound fully to one side: the head's shadow.")]
        public float shadowCutoff = 1800f;
        [Tooltip("The far ear hears a sound fully to one side this much later (ms). A real head: about 0.65.")]
        [Range(0f, 1.5f)] public float earDelay = 0.65f;
        [Tooltip("A sound right behind loses everything above this (Hz).")]
        public float rearCutoff = 2600f;
        [Tooltip("... and this much of its level.")]
        [Range(0f, 1f)] public float rearCut = 0.25f;

        const float Open = 20000f;
        const int Ring = 256;

        static AudioListener listener;

        AudioSource source;
        AudioRolloffMode rolloff;
        AnimationCurve customCurve;
        float minDistance, maxDistance;

        // set on the main thread, read on the audio thread
        volatile float tGain = 1f, tLeft = 1f, tRight = 1f, tDelayL, tDelayR, tShadowL = 1f, tShadowR = 1f, tRear = 1f;
        float gain = 1f, left = 1f, right = 1f, delayL, delayR, aShadowL = 1f, aShadowR = 1f, aRear = 1f;
        float lpL, lpR, rearL, rearR;
        readonly float[] ring = new float[Ring];
        int write;
        int sampleRate = 48000;
        bool ready;

        /// <summary>Plays a clip once at a point in the world, placed by the ears from its first sample.</summary>
        public static AudioSource PlayAt(AudioClip clip, Vector3 position, float volume = 1f, float minDistance = 1.5f, float maxDistance = 30f)
        {
            if (!clip) return null;
            var go = new GameObject("One shot " + clip.name);
            go.transform.position = position;
            var a = go.AddComponent<AudioSource>();
            a.clip = clip;
            a.volume = volume;
            a.spatialBlend = 1f;
            a.rolloffMode = AudioRolloffMode.Logarithmic;
            a.minDistance = minDistance;
            a.maxDistance = maxDistance;
            a.dopplerLevel = 0f;
            go.AddComponent<SpatialSound>();
            a.Play();
            Destroy(go, clip.length / Mathf.Max(0.01f, Mathf.Abs(a.pitch)) + 0.2f);
            return a;
        }

        void Awake()
        {
            source = GetComponent<AudioSource>();
            sampleRate = AudioSettings.outputSampleRate;
            // remember how the source was meant to fall off, then take the placing over
            rolloff = source.rolloffMode;
            minDistance = source.minDistance;
            maxDistance = source.maxDistance;
            if (rolloff == AudioRolloffMode.Custom) customCurve = source.GetCustomCurve(AudioSourceCurveType.CustomRolloff);
            source.spatialBlend = 0f;
            source.panStereo = 0f;
            source.spread = 0f;
            Place();
            gain = tGain; left = tLeft; right = tRight; delayL = tDelayL; delayR = tDelayR;
            ready = true;
        }

        void OnDestroy()
        {
            if (source) source.spatialBlend = 1f;
        }

        void LateUpdate() => Place();

        /// <summary>Works out, for this frame, what each ear should hear.</summary>
        void Place()
        {
            if (!listener || !listener.isActiveAndEnabled) listener = FindAnyObjectByType<AudioListener>();
            if (!listener) return;
            Transform head = listener.transform;
            Vector3 to = transform.position - head.position;
            float distance = to.magnitude;

            tGain = Rolloff(distance);

            Vector3 local = distance > 0.01f ? head.InverseTransformDirection(to / distance) : Vector3.forward;
            // a sound at the head itself (the player's own) stays in the middle
            float near = Mathf.InverseLerp(0.15f, 0.6f, distance);
            float side = Mathf.Clamp(local.x, -1f, 1f) * near;
            float pan = Mathf.Sign(side) * Mathf.Pow(Mathf.Abs(side), 0.6f) * panStrength;
            float p = Mathf.Abs(pan);
            float nearEar = 1f + nearBoost * p, farEar = 1f - farCut * p;
            tLeft = pan < 0f ? nearEar : farEar;
            tRight = pan < 0f ? farEar : nearEar;

            float delay = earDelay * 0.001f * sampleRate * Mathf.Abs(side);
            tDelayL = pan > 0f ? delay : 0f;   // the ear away from the sound hears it later
            tDelayR = pan < 0f ? delay : 0f;

            float shadow = Coefficient(Mathf.Lerp(Open, shadowCutoff, p));
            tShadowL = pan > 0f ? shadow : 1f;
            tShadowR = pan < 0f ? shadow : 1f;

            float back = Mathf.Clamp01(-local.z) * near;
            back = Mathf.Pow(back, 0.8f);
            tRear = Coefficient(Mathf.Lerp(Open, rearCutoff, back));
            tGain *= 1f - rearCut * back;
        }

        float Rolloff(float d)
        {
            float max = Mathf.Max(maxDistance, 0.01f);
            switch (rolloff)
            {
                case AudioRolloffMode.Custom:
                    return customCurve != null ? Mathf.Clamp01(customCurve.Evaluate(d / max)) : 1f;
                case AudioRolloffMode.Linear:
                    return Mathf.Clamp01(1f - Mathf.InverseLerp(minDistance, max, d));
                default:
                    if (d >= max) return 0f;
                    return Mathf.Clamp01(minDistance / Mathf.Max(d, minDistance));
            }
        }

        /// <summary>One-pole low-pass step for a cutoff (1 = open).</summary>
        float Coefficient(float cutoff)
        {
            if (cutoff >= Open - 1f) return 1f;
            return 1f - Mathf.Exp(-2f * Mathf.PI * cutoff / sampleRate);
        }

        void OnAudioFilterRead(float[] data, int channels)
        {
            if (!ready || channels < 2) return;
            // smoothed per sample: changes never click
            const float k = 0.002f;
            float g0 = tGain, l0 = tLeft, r0 = tRight, dl0 = tDelayL, dr0 = tDelayR, sl0 = tShadowL, sr0 = tShadowR, re0 = tRear;
            for (int i = 0; i < data.Length; i += channels)
            {
                gain += (g0 - gain) * k; left += (l0 - left) * k; right += (r0 - right) * k;
                delayL += (dl0 - delayL) * k; delayR += (dr0 - delayR) * k;
                aShadowL += (sl0 - aShadowL) * k; aShadowR += (sr0 - aShadowR) * k; aRear += (re0 - aRear) * k;

                float mono = 0f;
                for (int c = 0; c < channels; c++) mono += data[i + c];
                mono /= channels;
                ring[write] = mono;

                float inL = Read(delayL), inR = Read(delayR);
                lpL += (inL - lpL) * aShadowL;            // head shadow on the far ear
                lpR += (inR - lpR) * aShadowR;
                rearL += (lpL - rearL) * aRear;           // dulled from behind
                rearR += (lpR - rearR) * aRear;
                write = (write + 1) & (Ring - 1);

                float outL = rearL * left * gain, outR = rearR * right * gain;
                data[i] = outL;
                data[i + 1] = outR;
                for (int c = 2; c < channels; c++) data[i + c] = (outL + outR) * 0.5f;   // surround: centre / rears get both
            }
        }

        float Read(float delay)
        {
            float pos = write - delay;
            int a = Mathf.FloorToInt(pos);
            float f = pos - a;
            float s0 = ring[a & (Ring - 1)], s1 = ring[(a + 1) & (Ring - 1)];
            return s0 + (s1 - s0) * f;
        }
    }

    /// <summary>
    /// Gives every 3D sound in a scene a <see cref="SpatialSound"/>: what scenes and scripts already set up is placed
    /// by the ears without touching each of them. Sounds on the listener's own body (the player's steps, the gun)
    /// are left alone: they are at the head.
    /// </summary>
    public class SpatialSoundInstaller : MonoBehaviour
    {
        /// <summary>Off: Unity's own panning, for comparison.</summary>
        public static bool Enabled = true;
        float timer;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (FindAnyObjectByType<SpatialSoundInstaller>()) return;
            var go = new GameObject("SpatialSoundInstaller") { hideFlags = HideFlags.HideInHierarchy };
            DontDestroyOnLoad(go);
            go.AddComponent<SpatialSoundInstaller>();
            SceneManager.sceneLoaded += (s, m) => Scan();
        }

        void Update()
        {
            timer -= Time.unscaledDeltaTime;
            if (timer > 0f) return;
            timer = 0.1f;
            Scan();
        }

        static readonly List<AudioSource> sources = new List<AudioSource>();

        static void Scan()
        {
            if (!Enabled) return;
            var listener = FindAnyObjectByType<AudioListener>();
            Transform body = listener ? listener.transform.root : null;
            foreach (var a in FindObjectsByType<AudioSource>())
            {
                if (!a || a.spatialBlend < 0.5f || a.GetComponent<SpatialSound>()) continue;
                // the filter callback serves one source per object: two sources on one object would share it (and Unity
                // logs an error for every one); those keep Unity's own panning
                if (a.GetComponents<AudioSource>().Length > 1) continue;
                if (body && a.transform.IsChildOf(body)) continue;
                a.gameObject.AddComponent<SpatialSound>();
            }
        }
    }
}
