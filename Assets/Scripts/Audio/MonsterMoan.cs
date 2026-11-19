using UnityEngine;

namespace BodycamHorror.Audio
{
    /// <summary>
    /// An occasional moan from a monster. Rare by design: each monster waits a long random while between moans,
    /// and all monsters share one slot, so a moan never starts while another one is still sounding
    /// (plus a quiet gap after it). A moan the player could not hear (too far away) is not played and does not
    /// take the slot.
    /// </summary>
    public class MonsterMoan : MonoBehaviour
    {
        const string DefaultClip = "Audio/Monster/creature_moan";

        [Tooltip("Left empty: Resources/" + DefaultClip + ".")]
        public AudioClip clip;
        [Range(0f, 1f)] public float volume = 0.85f;
        [Tooltip("Random pitch for each moan, so it is never quite the same.")]
        public Vector2 pitch = new Vector2(0.88f, 1.06f);
        [Tooltip("Seconds between this monster's moans.")]
        public Vector2 interval = new Vector2(25f, 50f);
        [Tooltip("Wait before the first moan.")]
        public Vector2 firstDelay = new Vector2(8f, 20f);
        [Tooltip("Silence after any monster's moan before another monster may moan (s).")]
        public float sharedGap = 6f;
        public float nearDistance = 2.5f;
        public float farDistance = 30f;
        [Tooltip("Where the voice comes from (the head); left empty: this object, raised by Height.")]
        public Transform mouth;
        public float height = 1.5f;

        /// <summary>Set by the monster: false while it should keep quiet (dead, hidden, mid-attack).</summary>
        public System.Func<bool> canMoan;
        /// <summary>Optional: checked only when a moan is about to start (e.g. not over its own crying); never cuts one short.</summary>
        public System.Func<bool> mayStart;

        static float sharedBusyUntil;
        static AudioClip defaultClip;
        static Transform listener;

        AudioSource source;
        float timer;

        void Start()
        {
            if (!clip) clip = defaultClip ? defaultClip : (defaultClip = Resources.Load<AudioClip>(DefaultClip));
            if (!clip) { enabled = false; return; }

            var go = new GameObject("Moan");
            go.transform.SetParent(mouth ? mouth : transform, false);
            if (!mouth) go.transform.localPosition = new Vector3(0f, height, 0f);
            source = go.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = false;
            source.spatialBlend = 1f;
            source.dopplerLevel = 0f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = nearDistance;
            source.maxDistance = farDistance;
            SoundOcclusion.Attach(source, transform, height);

            timer = Random.Range(firstDelay.x, firstDelay.y);
        }

        void Update()
        {
            if (source == null) return;
            if (canMoan != null && !canMoan())
            {
                Silence();
                return;
            }

            timer -= Time.deltaTime;
            if (timer > 0f) return;

            if (Time.time < sharedBusyUntil || (mayStart != null && !mayStart()) || !InEarshot())
            {
                timer = Random.Range(2f, 5f);   // try again a little later, not straight after the other one
                return;
            }

            source.pitch = Random.Range(pitch.x, pitch.y);
            source.PlayOneShot(clip, volume);
            sharedBusyUntil = Time.time + clip.length / source.pitch + sharedGap;
            timer = Random.Range(interval.x, interval.y);
        }

        bool InEarshot()
        {
            if (!listener)
            {
                var l = FindAnyObjectByType<AudioListener>();
                if (!l) return false;
                listener = l.transform;
            }
            return (listener.position - source.transform.position).sqrMagnitude < farDistance * farDistance * 0.8f;
        }

        void OnDisable() => Silence();

        /// <summary>Cuts a moan short and frees the shared slot (after a short pause).</summary>
        void Silence()
        {
            if (!source || !source.isPlaying) return;
            source.Stop();
            sharedBusyUntil = Mathf.Min(sharedBusyUntil, Time.time + 1.5f);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetShared() { sharedBusyUntil = 0f; listener = null; }
    }
}
