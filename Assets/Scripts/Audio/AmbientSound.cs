using UnityEngine;

namespace BodycamHorror.Audio
{
    /// <summary>
    /// A sound of the place. Two kinds:
    ///   - Loop: a bed or a hum that never stops (started at a random point, so two of the same are
    ///     never in step);
    ///   - Random: one-shots now and then (drips, creaks, scrapes, knocks): each time another recording,
    ///     after a random pause, a little louder or softer, higher or lower, from a random point within
    ///     <see cref="radius"/> of here, so the same few recordings never sound like a loop.
    /// 3D sounds are muffled through walls (SoundOcclusion). Everything goes through the body camera
    /// microphone like any other sound.
    /// </summary>
    public class AmbientSound : MonoBehaviour
    {
        public enum Mode { Loop, Random }

        public Mode mode = Mode.Random;
        public AudioClip[] clips;

        [Header("Level")]
        [Range(0f, 1f)] public float volume = 0.5f;
        [Tooltip("Random spread of loudness per one-shot (share of volume).")]
        [Range(0f, 1f)] public float volumeJitter = 0.3f;
        [Tooltip("Random pitch range per one-shot (and once for a loop).")]
        public Vector2 pitch = new Vector2(0.92f, 1.06f);

        [Header("Timing (Random)")]
        [Tooltip("Pause between one-shots (s), picked at random from this range.")]
        public Vector2 interval = new Vector2(4f, 12f);
        [Tooltip("One-shots come from anywhere within this distance of here (m). Height is kept within ±radius/3.")]
        public float radius;

        [Header("Space")]
        [Tooltip("0 = everywhere at once (a bed), 1 = from a point in the world.")]
        [Range(0f, 1f)] public float spatialBlend = 1f;
        public float minDistance = 1f;
        public float maxDistance = 25f;
        [Tooltip("Muffle it when a wall stands between it and the listener (3D only).")]
        public bool occlusion = true;

        AudioSource source;
        Transform emitter;
        float timer;
        int last = -1;

        void Awake()
        {
            // one-shots move about, so they get a child of their own to play from
            emitter = mode == Mode.Random && radius > 0f ? new GameObject("Emitter").transform : transform;
            if (emitter != transform) emitter.SetParent(transform, false);

            source = emitter.gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = spatialBlend;
            source.rolloffMode = AudioRolloffMode.Logarithmic;
            source.minDistance = minDistance;
            source.maxDistance = maxDistance;
            source.dopplerLevel = 0f;
            source.volume = mode == Mode.Loop ? volume : 1f;   // one-shots carry their own level
            if (occlusion && spatialBlend > 0.5f) SoundOcclusion.Attach(source, transform);
        }

        void Start()
        {
            if (clips == null || clips.Length == 0) { enabled = false; return; }
            if (mode == Mode.Loop)
            {
                source.clip = Pick();
                source.loop = true;
                source.pitch = Random.Range(pitch.x, pitch.y);
                source.time = Random.Range(0f, source.clip.length * 0.9f);
                source.Play();
                enabled = false;
            }
            else timer = Random.Range(0f, interval.y);   // not all of them on the first second
        }

        void Update()
        {
            timer -= Time.deltaTime;
            if (timer > 0f) return;
            timer = Random.Range(interval.x, interval.y);

            if (emitter != transform)
            {
                Vector3 p = Random.insideUnitSphere * radius;
                p.y *= 0.33f;
                emitter.localPosition = p;
            }
            source.pitch = Random.Range(pitch.x, pitch.y);
            source.PlayOneShot(Pick(), Mathf.Clamp01(volume * (1f + Random.Range(-volumeJitter, volumeJitter))));
        }

        /// <summary>A random recording, not the one just played.</summary>
        AudioClip Pick()
        {
            int i = Random.Range(0, clips.Length);
            if (clips.Length > 1 && i == last) i = (i + 1 + Random.Range(0, clips.Length - 1)) % clips.Length;
            last = i;
            return clips[i];
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.4f, 0.8f, 1f, 0.5f);
            Gizmos.DrawWireSphere(transform.position, Mathf.Max(radius, 0.2f));
        }
    }
}
