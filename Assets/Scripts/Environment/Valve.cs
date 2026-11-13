using System.Collections.Generic;
using BodycamHorror.Player;
using BodycamHorror.Weapons;
using UnityEngine;
using UnityEngine.Events;

namespace BodycamHorror.Environment
{
    /// <summary>
    /// A hand wheel on a pipe. Stand close and look at it: the use key shows over the wheel
    /// (PlayerInteractor). Holding the key turns the wheel one way; after <see cref="turnTime"/>
    /// seconds of turning it is shut, a sound plays and <see cref="closed"/> fires. Letting go stops the
    /// wheel where it is, and it carries on from there. A shut valve cannot be used again.
    /// </summary>
    public class Valve : MonoBehaviour
    {
        public static readonly List<Valve> All = new List<Valve>();

        [Tooltip("The part that turns. Found by name (\"Wheel\") when empty.")]
        public Transform wheel;
        [Tooltip("Axis the wheel turns about, in the wheel's own space.")]
        public Vector3 axis = Vector3.forward;

        [Header("Turning")]
        [Tooltip("Seconds of turning it takes to shut the valve.")]
        public float turnTime = 7f;
        [Tooltip("Full turns of the wheel over that time.")]
        public float turns = 3f;
        [Tooltip("Seen from the front: clockwise shuts it.")]
        public bool clockwise = true;
        [Tooltip("On: the key has to be held the whole time. Off: one press and the wheel is turned to the end.")]
        public bool holdToTurn = true;
        [Tooltip("The wheel is heavy: it takes this long to get going and to stop (s).")]
        public float inertia = 0.22f;

        [Header("Prompt")]
        [Tooltip("Where the key prompt sits, in the wheel's own space.")]
        public Vector3 promptOffset = new Vector3(0f, 0f, 0.06f);

        [Header("Sound")]
        [Tooltip("Loops while the wheel turns, louder and a little higher the faster it goes. A generated creak when empty.")]
        public AudioClip turnClip;
        [Range(0f, 1f)] public float turnVolume = 0.3f;
        [Tooltip("The turning sound swells in and dies away over this long (s) instead of cutting.")]
        public float soundFade = 0.3f;
        [Tooltip("Played once when the valve is shut: a stand-in for whatever shutting it sets off.")]
        public AudioClip closedClip;
        [Range(0f, 1f)] public float closedVolume = 0.45f;

        [Header("Result")]
        [Tooltip("Fires once, when the valve has been turned all the way.")]
        public UnityEvent closed;

        /// <summary>0 = untouched, 1 = shut.</summary>
        public float Progress { get; private set; }
        public bool IsClosed { get; private set; }
        /// <summary>Can still be turned: shows the prompt.</summary>
        public bool CanUse => !IsClosed && isActiveAndEnabled;
        public Vector3 PromptPoint => wheel ? wheel.TransformPoint(promptOffset) : transform.position;

        AudioSource source;
        Audio.SoundOcclusion occlusion;
        Quaternion restRotation;
        float angle, angleVelocity, heldUntil = -1f, loudness;
        bool soundStarted;
        bool latched;
        static AudioClip generatedTurn, generatedClosed;

        void Awake()
        {
            if (!wheel) wheel = transform.Find("Wheel");
            if (!wheel) wheel = transform;
            restRotation = wheel.localRotation;

            var audioObject = new GameObject("ValveAudio");
            audioObject.transform.SetParent(transform, false);
            audioObject.transform.position = wheel.position;
            source = audioObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = true;
            source.spatialBlend = 1f;
            source.minDistance = 1.2f;
            source.maxDistance = 20f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.volume = 0f;
            occlusion = Audio.SoundOcclusion.Attach(source, transform);
            occlusion.Volume = 0f;
        }

        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);
        void Start() => PlayerInteractor.Ensure();

        /// <summary>Called by the player every frame the use key is down on this valve (pressed: this frame).</summary>
        public void Use(bool pressed)
        {
            if (!CanUse) return;
            if (holdToTurn) heldUntil = Time.time + 0.05f;
            else if (pressed) latched = true;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            bool turning = !IsClosed && (latched || Time.time <= heldUntil);
            if (turning)
            {
                Progress = Mathf.Min(1f, Progress + dt / Mathf.Max(turnTime, 0.05f));
                if (Progress >= 1f) Close();
            }

            // the wheel follows the progress with a little weight
            float target = Progress * turns * 360f * (clockwise ? -1f : 1f);
            angle = Mathf.SmoothDamp(angle, target, ref angleVelocity, inertia, Mathf.Infinity, dt);
            wheel.localRotation = restRotation * Quaternion.AngleAxis(angle, axis.sqrMagnitude > 0.0001f ? axis.normalized : Vector3.forward);

            // the sound follows the wheel itself: it swells as the wheel gets going, drops in pitch as it
            // slows and dies away with it, so nothing starts or stops with a click
            float speed01 = Mathf.Clamp01(Mathf.Abs(angleVelocity) / Mathf.Max(turns * 360f / Mathf.Max(turnTime, 0.05f), 1f));
            loudness = Mathf.MoveTowards(loudness, speed01, dt / Mathf.Max(soundFade, 0.01f));
            if (loudness > 0.002f)
            {
                if (!source.isPlaying)
                {
                    if (soundStarted) source.UnPause();
                    else
                    {
                        source.clip = turnClip ? turnClip : generatedTurn ? generatedTurn : (generatedTurn = ProceduralAudio.DoorCreak());
                        source.Play();
                        soundStarted = true;
                    }
                }
                occlusion.Volume = turnVolume * loudness * loudness;   // the occlusion owns the source's volume
                source.pitch = Mathf.Lerp(0.8f, 1f, speed01);
            }
            else
            {
                occlusion.Volume = 0f;
                if (source.isPlaying) source.Pause();
            }
        }

        void Close()
        {
            IsClosed = true;
            latched = false;
            AudioClip clip = closedClip ? closedClip : generatedClosed ? generatedClosed : (generatedClosed = ProceduralAudio.DoorShut());
            AudioSource.PlayClipAtPoint(clip, wheel.position, closedVolume);
            closed?.Invoke();
        }

        void OnDrawGizmosSelected()
        {
            if (!wheel) return;
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(wheel.TransformPoint(promptOffset), 0.03f);
            Gizmos.DrawRay(wheel.position, wheel.TransformDirection(axis.normalized) * 0.3f);
        }
    }
}
