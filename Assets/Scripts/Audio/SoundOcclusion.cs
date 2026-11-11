using UnityEngine;

namespace BodycamHorror.Audio
{
    /// <summary>
    /// Muffles a 3D sound when a wall or a shut door stands between it and the listener: the highs
    /// are filtered off and the level drops. A few rays to points around the listener's head give a
    /// partial result at a doorway or a corner, and the change is smoothed, so a door swinging open
    /// lets the sound "open up" instead of switching.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class SoundOcclusion : MonoBehaviour
    {
        [Tooltip("Colliders under this transform never block the sound (the thing making it).")]
        public Transform owner;
        [Tooltip("Rays start this far above the source (m): a creature's voice sits at its feet.")]
        public float height;
        public LayerMask mask = ~(1 << 2);   // not Ignore Raycast (the player)

        [Tooltip("Low-pass cutoff when fully blocked (Hz).")]
        public float blockedCutoff = 900f;
        [Tooltip("Volume when fully blocked (0-1).")]
        [Range(0f, 1f)] public float blockedVolume = 0.55f;
        [Tooltip("Rays go to points this far around the listener's head (m).")]
        public float spread = 0.35f;
        [Tooltip("Seconds between checks while the sound is playing.")]
        public float interval = 0.12f;
        [Tooltip("How fast the muffling follows a change (1/s).")]
        public float response = 9f;

        /// <summary>0 = clear line to the listener, 1 = fully behind something.</summary>
        public float Blocked { get; private set; }

        const float OpenCutoff = 22000f;

        static AudioListener listener;
        static Transform listenerBody;
        static readonly RaycastHit[] Hits = new RaycastHit[16];

        AudioSource source;
        AudioLowPassFilter filter;
        float baseVolume = 1f, target, timer;
        bool first = true;

        /// <summary>Adds occlusion to a source created in code.</summary>
        public static SoundOcclusion Attach(AudioSource source, Transform owner, float height = 0f)
        {
            if (!source) return null;
            var o = source.GetComponent<SoundOcclusion>();
            if (!o) o = source.gameObject.AddComponent<SoundOcclusion>();
            o.owner = owner;
            o.height = height;
            return o;
        }

        void Awake()
        {
            source = GetComponent<AudioSource>();
            baseVolume = source.volume;
            filter = GetComponent<AudioLowPassFilter>();
            if (!filter) filter = gameObject.AddComponent<AudioLowPassFilter>();
            filter.cutoffFrequency = OpenCutoff;
            filter.lowpassResonanceQ = 1f;
            filter.enabled = false;
            timer = Random.value * interval;   // a pack does not all cast on the same frame
        }

        void OnDisable()
        {
            if (source) source.volume = baseVolume;
            if (filter) filter.enabled = false;
            Blocked = target = 0f;
            first = true;
        }

        void Update()
        {
            timer -= Time.deltaTime;
            if (timer <= 0f)
            {
                bool playing = source.isPlaying;
                timer = playing ? interval : interval * 3f;
                Sample();
                // a sound that starts behind a wall must not begin clear and then duck
                if (first || !playing) Blocked = target;
                first = false;
            }

            Blocked = Mathf.MoveTowards(Blocked, target, response * Time.deltaTime * Mathf.Max(0.15f, Mathf.Abs(target - Blocked)));
            bool on = Blocked > 0.01f;
            if (filter.enabled != on) filter.enabled = on;
            if (on) filter.cutoffFrequency = OpenCutoff * Mathf.Pow(blockedCutoff / OpenCutoff, Blocked);
            source.volume = baseVolume * Mathf.Lerp(1f, blockedVolume, Blocked);
        }

        void Sample()
        {
            if (!listener || !listener.isActiveAndEnabled)
            {
                listener = FindAnyObjectByType<AudioListener>();
                listenerBody = null;
                if (listener)
                {
                    var controller = listener.GetComponentInParent<CharacterController>();
                    var body = listener.GetComponentInParent<Rigidbody>();
                    listenerBody = controller ? controller.transform : body ? body.transform : listener.transform;
                }
            }
            if (!listener) { target = 0f; return; }

            Vector3 from = transform.position + Vector3.up * height;
            Vector3 ear = listener.transform.position;
            Vector3 to = ear - from;
            float distance = to.magnitude;
            if (distance < 0.5f || distance > source.maxDistance) { target = 0f; return; }

            Vector3 side = Vector3.Cross(Vector3.up, to / distance);
            side = side.sqrMagnitude < 0.01f ? Vector3.right : side.normalized;
            Vector3 up = Vector3.up;

            int blocked = 0;
            if (Shut(from, ear)) blocked++;
            if (Shut(from, ear + side * spread)) blocked++;
            if (Shut(from, ear - side * spread)) blocked++;
            if (Shut(from, ear + up * spread)) blocked++;
            if (Shut(from, ear - up * spread)) blocked++;
            target = blocked / 5f;
        }

        bool Shut(Vector3 from, Vector3 to)
        {
            Vector3 d = to - from;
            float length = d.magnitude;
            if (length < 0.01f) return false;
            int count = Physics.RaycastNonAlloc(from, d / length, Hits, length, mask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                Collider c = Hits[i].collider;
                if (!c) continue;
                Transform t = c.transform;
                if (owner && t.IsChildOf(owner)) continue;
                if (listenerBody && t.IsChildOf(listenerBody)) continue;
                // bodies, ragdolls and loose props are not walls
                Rigidbody rb = c.attachedRigidbody;
                if (rb && !rb.isKinematic) continue;
                if (c is CharacterController) continue;
                return true;
            }
            return false;
        }
    }
}
