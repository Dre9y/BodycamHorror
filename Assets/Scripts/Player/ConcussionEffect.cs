using BodycamHorror.Combat;
using UnityEngine;

namespace BodycamHorror.Player
{
    /// <summary>
    /// What a blow to the head does to the eyes and ears. The picture splits in two: a faint, smeared second
    /// image drifts beside the real one, and the eyes keep trying to pull them back together. The split follows
    /// depth (like the eyes' own parallax): something close, an enemy in your face, doubles clearly while the
    /// far walls barely move, so it reads as things doubling rather than the screen being copied.
    /// The double vision is strongest right after the hit, fades over a few seconds and leaves a slight residue
    /// for as long as the player stays wounded. Optionally a thin ring in the ears.
    /// Drawn inside the Lens Focus full-screen pass (shader globals _LensDouble / _LensDoubleB).
    /// </summary>
    public class ConcussionEffect : MonoBehaviour
    {
        [Header("Double vision")]
        [Tooltip("How far the second image drifts at the peak, as a share of the screen height (for things up close).")]
        [Range(0f, 0.12f)] public float separation = 0.05f;
        [Tooltip("How strong the second image is at the peak (0.5 = as strong as the real one).")]
        [Range(0f, 0.5f)] public float ghostStrength = 0.5f;
        [Tooltip("Seconds for the double vision to fade from the peak to the residue.")]
        public float fadeTime = 8f;
        [Tooltip("Share of the effect left while the player stays wounded (0 = none).")]
        [Range(0f, 1f)] public float woundedResidue = 0.3f;
        [Tooltip("Seconds for the residue to clear once the wounds heal.")]
        public float healFadeTime = 2f;
        [Tooltip("Things this close (m) or closer double fully; further away the split shrinks.")]
        public float nearDistance = 2.5f;
        [Tooltip("Split left on the far distance, as a share of the near one (1 = the whole screen doubles evenly).")]
        [Range(0f, 1f)] public float farShare = 0.4f;
        [Tooltip("Smear of the second image along the split (0 = a crisp copy).")]
        [Range(0f, 1f)] public float ghostSmear = 0.3f;
        [Tooltip("How much the real image is pushed the other way (0 = only the ghost moves, 0.5 = both split evenly).")]
        [Range(0f, 0.5f)] public float mainShift = 0.3f;
        [Tooltip("How fast the eyes swim: the split breathes in and out and its tilt wanders.")]
        public float swimSpeed = 0.9f;
        [Tooltip("Largest tilt of the split away from horizontal (degrees).")]
        [Range(0f, 90f)] public float maxTilt = 18f;

        [Header("Ringing in the ears")]
        public bool ring = true;
        [Range(0f, 1f)] public float ringVolume = 0.07f;
        [Tooltip("Seconds for the ring to die away.")]
        public float ringTime = 4.5f;
        public float ringPitch = 6200f;

        [Header("References (found automatically)")]
        public PlayerHealth health;

        static readonly int DoubleId = Shader.PropertyToID("_LensDouble");
        static readonly int DoubleBId = Shader.PropertyToID("_LensDoubleB");

        float peak;          // 1 at the hit, fades to 0
        float residue;       // follows the wounded state
        float ringLevel;
        float seed;
        AudioSource ringSource;

        /// <summary>Current strength of the double vision (0-1).</summary>
        public float Strength { get; private set; }

        void Awake()
        {
            seed = Random.value * 100f;
            if (!health) health = GetComponentInParent<PlayerHealth>();
        }

        void OnEnable()
        {
            if (health) health.Damaged += OnDamaged;
        }

        void OnDisable()
        {
            if (health) health.Damaged -= OnDamaged;
            Shader.SetGlobalVector(DoubleId, Vector4.zero);
            if (ringSource) ringSource.Stop();
        }

        void Start()
        {
            // health may have been set by PlayerHealth after our OnEnable
            if (health) { health.Damaged -= OnDamaged; health.Damaged += OnDamaged; health.Died += OnDied; }
        }

        void OnDestroy()
        {
            if (health) { health.Damaged -= OnDamaged; health.Died -= OnDied; }
        }

        void OnDamaged(DamageInfo info)
        {
            if (health && health.IsDead) return;
            peak = 1f;
            seed = Random.value * 100f;   // a new swim each time
            if (ring && ringVolume > 0f)
            {
                EnsureRing();
                ringLevel = 1f;
                if (!ringSource.isPlaying) ringSource.Play();
            }
        }

        void OnDied() { peak = 0f; residue = 0f; ringLevel = 0f; }

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            peak = Mathf.Max(0f, peak - dt / Mathf.Max(fadeTime, 0.05f));
            bool wounded = health && health.IsWounded;
            residue = wounded ? woundedResidue : Mathf.MoveTowards(residue, 0f, woundedResidue * dt / Mathf.Max(healFadeTime, 0.05f));

            // ease out: a hard double at first, then a long tail
            float p = peak * peak * (3f - 2f * peak);
            Strength = Mathf.Max(p, residue);

            if (Strength < 0.001f) Shader.SetGlobalVector(DoubleId, Vector4.zero);
            else
            {
                float t = Time.time * swimSpeed + seed;
                // the eyes keep trying to fuse the images: the split breathes and sometimes nearly closes
                float breathe = 0.55f + 0.45f * Mathf.Sin(t * 2.3f) * Mathf.PerlinNoise(t * 0.7f, seed);
                breathe = Mathf.Lerp(breathe, 1f, peak * peak);   // no fusing at all in the first moment
                float tilt = (Mathf.PerlinNoise(seed, t * 0.5f) * 2f - 1f) * maxTilt * Mathf.Deg2Rad;
                float sep = separation * Strength * breathe;
                Shader.SetGlobalVector(DoubleId, new Vector4(sep, tilt, ghostStrength * Mathf.Sqrt(Strength), nearDistance));
                Shader.SetGlobalVector(DoubleBId, new Vector4(farShare, ghostSmear, mainShift, 0f));
            }

            if (ringSource)
            {
                ringLevel = Mathf.Max(0f, ringLevel - dt / Mathf.Max(ringTime, 0.05f));
                ringSource.volume = ringVolume * ringLevel * ringLevel;
                if (ringLevel <= 0f && ringSource.isPlaying) ringSource.Stop();
            }
        }

        void EnsureRing()
        {
            if (ringSource) return;
            // on a child of its own: the camera carries OnAudioFilterRead (BodycamMicrophone), and an AudioSource
            // beside it would get that filter too ("Only custom filters can be played" / multiple sources warning)
            var go = new GameObject("TinnitusSource");
            go.transform.SetParent(transform, false);
            ringSource = go.AddComponent<AudioSource>();
            ringSource.clip = MakeRing(ringPitch);
            ringSource.loop = true;
            ringSource.spatialBlend = 0f;
            ringSource.playOnAwake = false;
            ringSource.volume = 0f;
            ringSource.priority = 0;
        }

        /// <summary>A thin high tone with a slow beat against a near partial; loops seamlessly (whole cycles).</summary>
        static AudioClip MakeRing(float pitch)
        {
            const int rate = 44100;
            const float length = 2f;
            int n = (int)(rate * length);
            // round both partials to whole cycles over the clip so the loop has no click
            float f1 = Mathf.Round(pitch * length) / length;
            float f2 = Mathf.Round(pitch * 1.006f * length) / length;
            var d = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)rate;
                d[i] = (Mathf.Sin(2f * Mathf.PI * f1 * t) * 0.7f + Mathf.Sin(2f * Mathf.PI * f2 * t) * 0.3f) * 0.8f;
            }
            var clip = AudioClip.Create("Tinnitus", n, 1, rate, false);
            clip.SetData(d, 0);
            return clip;
        }
    }
}
