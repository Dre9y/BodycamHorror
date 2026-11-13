using BodycamHorror.Player;
using BodycamHorror.UI;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BodycamHorror.Audio
{
    /// <summary>
    /// What fear sounds like inside the head (PlayerStress): first a heartbeat that gets louder and
    /// faster as the fear grows, then, when it gets really bad, a ringing in the ears on top.
    /// The heartbeat is one recorded beat played once per pulse, so it speeds up without changing pitch.
    /// Both skip the body camera's microphone and the room: they are not out there to be recorded.
    /// Recordings: Resources/Audio/Fear/heartbeat and ear_ringing (replace the files to change them).
    /// Installs itself on the scene's player, so nothing has to be set up per scene.
    /// </summary>
    [DisallowMultipleComponent]
    public class PlayerFearAudio : MonoBehaviour
    {
        [Header("Heartbeat")]
        [Tooltip("Fear (0-1) at which the heartbeat is first heard.")]
        [Range(0f, 1f)] public float heartStart = 0.25f;
        [Tooltip("Fear at which the heartbeat is at full loudness.")]
        [Range(0f, 1f)] public float heartFull = 0.8f;
        [Range(0f, 1f)] public float heartVolume = 0.85f;
        [Tooltip("Beats per minute when the heartbeat is first heard.")]
        public float calmBpm = 78f;
        [Tooltip("Beats per minute at full fear.")]
        public float panicBpm = 150f;
        [Tooltip("Pitch of a beat at full fear: a touch tighter and harder.")]
        [Range(1f, 1.4f)] public float panicPitch = 1.12f;

        [Header("Ear ringing")]
        [Tooltip("Fear (0-1) at which the ringing starts. Keep it above Heart Start: the heart comes first.")]
        [Range(0f, 1f)] public float ringStart = 0.65f;
        [Tooltip("Fear at which the ringing is at full loudness.")]
        [Range(0f, 1f)] public float ringFull = 0.95f;
        [Range(0f, 1f)] public float ringVolume = 0.1f;
        [Tooltip("How fast the ringing comes in (per second).")]
        public float ringFadeIn = 0.6f;
        [Tooltip("How fast the ringing dies away once the fear drops (per second): it lingers.")]
        public float ringFadeOut = 0.15f;

        [Header("Recordings (loaded from Resources/Audio/Fear when empty)")]
        public AudioClip heartbeat;
        public AudioClip ringing;

        PlayerStress stress;
        AudioSource heart, ring;
        float beatTimer, ringLevel;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            Install();
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Install();

        /// <summary>Puts the fear sounds on the scene's player if it has none yet.</summary>
        public static void Install()
        {
            var fear = FindAnyObjectByType<PlayerStress>();
            if (fear && !fear.GetComponent<PlayerFearAudio>()) fear.gameObject.AddComponent<PlayerFearAudio>();
        }

        void Awake()
        {
            stress = GetComponent<PlayerStress>();
            if (!heartbeat) heartbeat = Resources.Load<AudioClip>("Audio/Fear/heartbeat");
            if (!ringing) ringing = Resources.Load<AudioClip>("Audio/Fear/ear_ringing");
            if (!heartbeat) Debug.LogWarning("PlayerFearAudio: no Resources/Audio/Fear/heartbeat.");
            if (!ringing) Debug.LogWarning("PlayerFearAudio: no Resources/Audio/Fear/ear_ringing.");

            heart = NewSource();
            ring = NewSource();
            ring.clip = ringing;
            ring.loop = true;
            ring.volume = 0f;
        }

        AudioSource NewSource()
        {
            var s = gameObject.AddComponent<AudioSource>();
            s.playOnAwake = false;
            s.spatialBlend = 0f;
            s.bypassListenerEffects = true;   // inside the head: not through the bodycam microphone
            s.bypassReverbZones = true;
            s.bypassEffects = true;
            s.priority = 8;
            return s;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f || !stress) return;
            float fear = stress.Value;

            // --- heartbeat: one beat per pulse
            float heart01 = Mathf.InverseLerp(heartStart, heartFull, fear);
            if (heart01 > 0f && heartbeat)
            {
                beatTimer -= dt;
                if (beatTimer <= 0f)
                {
                    float pulse01 = Mathf.InverseLerp(heartStart, 1f, fear);
                    heart.pitch = Mathf.Lerp(1f, panicPitch, pulse01);
                    // comes in quietly, but is never so faint that only half a beat is heard
                    heart.PlayOneShot(heartbeat, heartVolume * Mathf.Lerp(0.12f, 1f, heart01));
                    beatTimer += 60f / Mathf.Max(Mathf.Lerp(calmBpm, panicBpm, pulse01), 20f);
                    if (beatTimer < 0f) beatTimer = 0f;
                }
            }
            else beatTimer = 0f;   // the first beat lands the moment fear crosses the line

            // --- ringing: only at the top, in fast and out slowly
            float ringTarget = GameSettings.EarRinging ? Mathf.InverseLerp(ringStart, ringFull, fear) : 0f;
            if (!GameSettings.EarRinging) ringLevel = 0f;   // switched off in the settings: gone at once
            ringLevel = Mathf.MoveTowards(ringLevel, ringTarget, (ringTarget > ringLevel ? ringFadeIn : ringFadeOut) * dt);
            if (!ringing) return;
            ring.volume = ringVolume * ringLevel * ringLevel;
            if (ringLevel > 0.001f) { if (!ring.isPlaying) ring.Play(); }
            else if (ring.isPlaying) ring.Stop();
        }
    }
}
