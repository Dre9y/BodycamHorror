using BodycamHorror.Player;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BodycamHorror.Audio
{
    /// <summary>
    /// The player's own footsteps: a recorded step every time a foot comes down in the camera's bob
    /// (BodycamCameraRig.Footstep), so sound and camera land together. Louder and heavier at a run,
    /// barely there crouched; a landing from the air is both feet at once. Recordings come from
    /// Resources/Audio/Footsteps (any clip there is a step); never the same one twice running.
    /// Installs itself on the scene's player, so nothing has to be set up per scene.
    /// </summary>
    [DisallowMultipleComponent]
    public class PlayerFootsteps : MonoBehaviour
    {
        [Header("Loudness")]
        [Range(0f, 1f)] public float walk = 0.32f;
        [Range(0f, 1f)] public float sprint = 0.6f;
        [Tooltip("Share of the walking loudness while crouched.")]
        [Range(0f, 1f)] public float crouch = 0.35f;
        [Tooltip("Random spread of loudness per step.")]
        [Range(0f, 0.5f)] public float volumeJitter = 0.15f;

        [Header("Pitch")]
        [Tooltip("Random spread of pitch per step: the same recordings never sound quite the same.")]
        [Range(0f, 0.2f)] public float pitchJitter = 0.06f;
        [Tooltip("Running pitches the steps down a touch: heavier, harder heel strikes.")]
        [Range(0.7f, 1f)] public float sprintPitch = 0.92f;

        [Header("Landing")]
        [Tooltip("Slowest fall (m/s) that lands with both feet audibly.")]
        public float landingMinImpact = 3f;
        [Range(0f, 1f)] public float landing = 0.75f;

        BodycamPlayerController player;
        BodycamCameraRig rig;
        AudioSource source;
        AudioClip[] clips;
        int last = -1, beforeLast = -1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            Install();
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Install();

        /// <summary>Puts footsteps on the scene's player if it has none yet.</summary>
        public static void Install()
        {
            var player = FindAnyObjectByType<BodycamPlayerController>();
            if (player && !player.GetComponent<PlayerFootsteps>()) player.gameObject.AddComponent<PlayerFootsteps>();
        }

        void Awake()
        {
            player = GetComponent<BodycamPlayerController>();
            rig = GetComponentInChildren<BodycamCameraRig>(true);
            clips = Resources.LoadAll<AudioClip>("Audio/Footsteps");
            source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;   // our own feet: right under us, through the body camera's microphone
            source.priority = 32;
            if (clips.Length == 0) Debug.LogWarning("PlayerFootsteps: no recordings in Resources/Audio/Footsteps.");
        }

        void OnEnable()
        {
            if (rig) rig.Footstep += OnFootstep;
            if (player) player.Landed += OnLanded;
        }

        void OnDisable()
        {
            if (rig) rig.Footstep -= OnFootstep;
            if (player) player.Landed -= OnLanded;
        }

        void OnFootstep(float sprint01)
        {
            float volume = Mathf.Lerp(walk, sprint, sprint01);
            if (player && player.IsCrouching) volume *= crouch;
            Step(volume, Mathf.Lerp(1f, sprintPitch, sprint01));
        }

        void OnLanded(float impact)
        {
            if (impact < landingMinImpact) return;
            float k = Mathf.InverseLerp(landingMinImpact, 12f, impact);
            float volume = landing * Mathf.Lerp(0.6f, 1f, k);
            // both feet, the second a hair after the first
            Step(volume, 0.9f);
            StartCoroutine(SecondFoot(volume * 0.8f));
        }

        System.Collections.IEnumerator SecondFoot(float volume)
        {
            yield return new WaitForSeconds(Random.Range(0.05f, 0.09f));
            Step(volume, 0.86f);
        }

        void Step(float volume, float pitch)
        {
            if (clips == null || clips.Length == 0 || volume <= 0.001f) return;
            source.pitch = pitch * (1f + Random.Range(-pitchJitter, pitchJitter));
            source.PlayOneShot(Next(), Mathf.Clamp01(volume * (1f + Random.Range(-volumeJitter, volumeJitter))));
        }

        /// <summary>A random recording, never one of the last two.</summary>
        AudioClip Next()
        {
            int i = Random.Range(0, clips.Length);
            if (clips.Length > 2)
                while (i == last || i == beforeLast) i = Random.Range(0, clips.Length);
            beforeLast = last;
            last = i;
            return clips[i];
        }
    }
}
