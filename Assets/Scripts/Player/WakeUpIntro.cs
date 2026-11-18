using UnityEngine;
using UnityEngine.InputSystem;

namespace BodycamHorror.Player
{
    /// <summary>
    /// Opening cutscene: knocked out cold, the player comes round on the floor and gets up.
    /// A moment of black with a slow, loud heartbeat and ringing ears, the world muffled; the eyes snap open
    /// on a blurred, doubled ceiling and fall shut again; a few tries later they stay open, the
    /// head rolls to look round the room, the player props up, sits, gets a knee under and staggers to their
    /// feet; then control is handed back. Each heartbeat pulses the edges of the view darker.
    /// Everything is keyed on curves (seconds from the start) that can be reshaped in the Inspector.
    /// Works on the player's own view pivot, so the camera rig's shake and the lens all still apply.
    /// Hold Space or Enter to fast-forward.
    /// </summary>
    [DefaultExecutionOrder(-200)]   // after the controller's Update, before the rig, the lens and the body read the view
    public class WakeUpIntro : MonoBehaviour
    {
        [Header("Playback")]
        public bool playOnStart = true;
        [Tooltip("Hold Space / Enter to play this many times faster.")]
        public float skipSpeed = 6f;

        [Header("Pose of the view pivot (seconds -> value)")]
        [Tooltip("Eye height above the feet (m). The last key is set to the controller's standing eye height.")]
        public AnimationCurve eyeHeight = Smooth(0f, 0.2f, 5.9f, 0.2f, 6.6f, 0.28f, 7.5f, 0.5f, 8.35f, 0.82f, 9.2f, 0.86f,
                                                 10.3f, 1.05f, 11.45f, 1.6f, 11.9f, 1.53f, 12.5f, 1.58f);
        [Tooltip("Eyes forward (+) / back (-) of the feet (m): lying down the head is behind where the feet will stand.")]
        public AnimationCurve forward = Smooth(0f, -0.6f, 6.6f, -0.55f, 8.35f, -0.3f, 10.3f, -0.1f, 11.45f, 0f, 12.5f, 0f);
        [Tooltip("Look down (+) / up (-) in degrees.")]
        public AnimationCurve pitch = Smooth(0f, -82f, 5f, -80f, 6.6f, -60f, 7.5f, -25f, 8.35f, 18f, 9.2f, 28f,
                                             10.3f, 30f, 11.45f, 8f, 12.5f, 0f);
        [Tooltip("Look left (-) / right (+) in degrees.")]
        public AnimationCurve yaw = Smooth(0f, 0f, 6.75f, 0f, 8.35f, 8f, 9.2f, -4f, 10.3f, -10f, 11.45f, 3f, 12.5f, 0f);
        [Tooltip("Head tilt in degrees.")]
        public AnimationCurve roll = Smooth(0f, 0f, 7.5f, -6f, 8.35f, 4f, 9.2f, -2f, 10.3f, 7f, 11.45f, -4f, 11.9f, 2f, 12.5f, 0f);
        [Tooltip("Lying on the back: the head rolled to one side on the floor (deg, about the body's length).")]
        public AnimationCurve headTurn = Smooth(0f, 30f, 3.35f, 28f, 4f, 5f, 5f, -35f, 6.05f, -30f, 6.75f, -10f, 7.65f, 0f, 12.5f, 0f);

        [Header("Eyes")]
        [Tooltip("0 = shut, 1 = wide open.")]
        public AnimationCurve eyesOpen = Eased(0f, 0f, 1.1f, 0f, 1.4f, 0.4f, 1.95f, 0.05f, 2.75f, 0.12f, 3.4f, 0.6f, 3.9f, 0.45f,
                                               4.1f, 0.12f, 4.45f, 0.75f, 5.4f, 0.82f, 6.75f, 0.88f, 8f, 0.95f, 10.3f, 1f, 12.5f, 1f);
        [Tooltip("0 = sharp, 1 = everything past arm's length smeared.")]
        public AnimationCurve blur = Smooth(0f, 1f, 3.7f, 1f, 5.9f, 0.75f, 8.5f, 0.5f, 11.2f, 0.25f, 12.5f, 0f);

        [Header("Body")]
        [Tooltip("Breathing and the unsteadiness on top of the keyed pose.")]
        public float breathHeight = 0.012f;
        public float breathPitch = 0.8f;
        public float wobbleAngle = 1.6f;

        [Header("Moments")]
        [Tooltip("When the ear ringing and the double vision start (s).")]
        public float dazeAt = 0.15f;
        [Tooltip("The double vision comes back as the eyes properly open (s).")]
        public float redazeAt = 3.3f;
        [Range(0f, 1f)] public float redazeStrength = 0.8f;
        [Tooltip("The eyes snap open: a jolt, and the breathing starts (s).")]
        public float gaspAt = 1.15f;
        [Tooltip("Jolts of the body: sitting up, getting a knee under, standing (s). Each also scuffs on the floor.")]
        public float[] joltTimes = { 8.35f, 10.3f, 11.45f };
        public float[] joltTrauma = { 0.12f, 0.1f, 0.22f };

        [Header("Heartbeat (heard and seen)")]
        public AudioClip heartbeat;
        [Tooltip("Beats per minute over the scene.")]
        public AnimationCurve heartRate = Smooth(0f, 52f, 1.2f, 70f, 4f, 76f, 7f, 92f, 11.5f, 100f, 12.5f, 92f);
        public AnimationCurve heartVolume = Smooth(0f, 1f, 4f, 0.8f, 9f, 0.55f, 12.5f, 0.35f);
        [Tooltip("How far each beat pulls the edges of the view in (share of the eyelid travel).")]
        [Range(0f, 0.4f)] public float pulseAmount = 0.16f;
        public float pulseDecay = 7f;

        [Header("Breathing (seen, not heard)")]
        [Tooltip("Seconds per breath (in and out) over the scene; no breathing before the eyes first open.")]
        public AnimationCurve breathPeriod = Smooth(0f, 3f, 2f, 2.2f, 5f, 2.6f, 7.5f, 1.5f, 11.5f, 1.3f, 12.5f, 1.8f);

        [Header("Hearing")]
        [Tooltip("Low-pass cutoff (Hz) on everything heard: muffled on coming round, clearing as the head does.")]
        public AnimationCurve hearingCutoff = Smooth(0f, 500f, 1.2f, 900f, 5f, 3500f, 9f, 22000f, 12.5f, 22000f);

        [Header("References (found automatically)")]
        public BodycamPlayerController player;
        public BodycamCameraRig rig;
        public LensLook lens;
        public ConcussionEffect concussion;
        public PlayerBody body;
        public PlayerInteractor interactor;

        public bool IsPlaying { get; private set; }
        public float Duration { get; private set; }

        float t;
        float seed;
        bool dazed, redazed, gasped;
        int nextJolt;
        Renderer[] bodyRenderers;

        // sound
        AudioSource heartSource, bodySource;
        AudioLowPassFilter muffle;
        AudioClip[] scuffs;
        float nextBeat, pulse, breathPhase;

        void Awake()
        {
            if (!player) player = GetComponentInParent<BodycamPlayerController>();
            if (!player) player = FindAnyObjectByType<BodycamPlayerController>();
            if (player)
            {
                if (!rig) rig = player.GetComponentInChildren<BodycamCameraRig>(true);
                if (!lens) lens = player.GetComponentInChildren<LensLook>(true);
                if (!concussion) concussion = player.GetComponentInChildren<ConcussionEffect>(true);
                if (!body) body = player.GetComponentInChildren<PlayerBody>(true);
                if (!interactor) interactor = player.GetComponentInChildren<PlayerInteractor>(true);
            }
            seed = Random.value * 100f;
            if (playOnStart) Begin();
        }

        /// <summary>Starts (or restarts) the cutscene.</summary>
        public void Play()
        {
            enabled = true;
            Begin();
        }

        void Begin()
        {
            if (!player || !player.cameraPivot) { Debug.LogWarning("WakeUpIntro: no player / camera pivot", this); enabled = false; return; }

            // end on exactly the controller's own view so the hand-back does not jump
            SetLastKey(eyeHeight, player.StandingEyeHeight);
            SetLastKey(pitch, 0f);
            Duration = Mathf.Max(End(eyeHeight), End(forward), End(pitch), End(yaw), End(roll), End(headTurn), End(eyesOpen), End(blur));

            t = 0f;
            dazed = redazed = gasped = false;
            nextJolt = 0;
            nextBeat = 0.05f;
            pulse = 0f;
            breathPhase = 0f;
            IsPlaying = true;
            player.inputLocked = true;
            if (interactor) interactor.enabled = false;
            if (lens) { lens.eyelids = 1f; lens.daze = 1f; }   // black from the very first frame
            bodyRenderers = null;
            SetUpSound();
        }

        void LateUpdate()
        {
            if (!IsPlaying) return;

            float speed = 1f;
            var kb = Keyboard.current;
            if (kb != null && (kb.spaceKey.isPressed || kb.enterKey.isPressed)) speed = skipSpeed;
            float dt = Time.deltaTime * speed;
            t += dt;

            HideBody(true);
            Events();
            Sound(dt);

            if (t >= Duration) { Finish(); return; }

            // breathing and an unsteady head, stronger once up off the floor
            float up = Mathf.InverseLerp(0.2f, 1.5f, eyeHeight.Evaluate(t));
            float breath = gasped ? Mathf.Sin(breathPhase * 2f * Mathf.PI) : 0f;
            float wob = wobbleAngle * Mathf.Lerp(0.4f, 1f, up);
            float nx = (Mathf.PerlinNoise(seed, t * 0.6f) - 0.5f) * 2f * wob;
            float ny = (Mathf.PerlinNoise(t * 0.5f, seed + 7f) - 0.5f) * 2f * wob;
            float nz = (Mathf.PerlinNoise(seed + 13f, t * 0.4f) - 0.5f) * 2f * wob;

            Transform pivot = player.cameraPivot;
            pivot.localPosition = new Vector3(0f, eyeHeight.Evaluate(t) + breath * breathHeight, forward.Evaluate(t));
            pivot.localRotation = Quaternion.AngleAxis(headTurn.Evaluate(t), Vector3.forward)
                                * Quaternion.Euler(pitch.Evaluate(t) - breath * breathPitch + nx, yaw.Evaluate(t) + ny, roll.Evaluate(t) + nz);

            if (lens)
            {
                // the eyelids, pulled in a little further on every heartbeat
                float lids = 1f - Mathf.Clamp01(eyesOpen.Evaluate(t));
                lens.eyelids = Mathf.Clamp01(lids + (1f - lids) * pulse * pulseAmount);
                lens.daze = Mathf.Clamp01(blur.Evaluate(t));
            }
        }

        void Events()
        {
            if (!dazed && t >= dazeAt) { dazed = true; if (concussion) concussion.Daze(1f, true); }
            if (!redazed && t >= redazeAt) { redazed = true; if (concussion) concussion.Daze(redazeStrength, false); }
            if (!gasped && t >= gaspAt)
            {
                gasped = true;
                breathPhase = 0.25f;   // a sharp breath in as the eyes snap open
                if (rig) rig.AddTrauma(0.08f);
            }
            while (nextJolt < joltTimes.Length && t >= joltTimes[nextJolt])
            {
                if (rig) rig.AddTrauma(nextJolt < joltTrauma.Length ? joltTrauma[nextJolt] : 0.15f);
                // a hand or a shoe scuffing on the concrete, cloth dragging with it
                if (scuffs != null && scuffs.Length > 0) Play(bodySource, scuffs[Random.Range(0, scuffs.Length)], 0.45f, Random.Range(0.8f, 0.95f));
                nextJolt++;
            }
        }

        // ------------------------------------------------------------------ sound

        void SetUpSound()
        {
            Camera cam = player.GetComponentInChildren<Camera>();
            Transform host = cam ? cam.transform : player.transform;
            if (!heartSource)
            {
                // on a child of their own: the camera carries an OnAudioFilterRead (BodycamMicrophone), and a
                // source beside it would be run through that filter too
                var go = new GameObject("WakeUpAudio");
                go.transform.SetParent(host, false);
                heartSource = MakeSource(go);
                bodySource = MakeSource(go);
            }
            if (!heartbeat) heartbeat = Resources.Load<AudioClip>("Audio/Fear/heartbeat");
            if (scuffs == null) scuffs = Resources.LoadAll<AudioClip>("Audio/Footsteps");

            // everything heard goes through the ears: muffled at first (a filter on the listener takes the whole mix)
            var listener = FindAnyObjectByType<AudioListener>();
            if (listener && !muffle)
            {
                muffle = listener.GetComponent<AudioLowPassFilter>();
                if (!muffle) muffle = listener.gameObject.AddComponent<AudioLowPassFilter>();
            }
            if (muffle) { muffle.enabled = true; muffle.cutoffFrequency = hearingCutoff.Evaluate(0f); }
        }

        static AudioSource MakeSource(GameObject go)
        {
            var s = go.AddComponent<AudioSource>();
            s.playOnAwake = false;
            s.spatialBlend = 0f;   // inside the head
            s.priority = 8;
            return s;
        }

        void Sound(float dt)
        {
            if (muffle) muffle.cutoffFrequency = hearingCutoff.Evaluate(t);

            // the heartbeat: one recorded beat per pulse, so it speeds up without changing pitch
            pulse = Mathf.Max(0f, pulse - pulse * pulseDecay * dt);
            if (t >= nextBeat)
            {
                Play(heartSource, heartbeat, heartVolume.Evaluate(t), 1f);
                pulse = 1f;
                nextBeat = t + 60f / Mathf.Max(heartRate.Evaluate(t), 20f);
            }

            // breathing (moves the view only), from the eyes opening on, faster with the effort of getting up
            if (gasped) breathPhase += dt / Mathf.Max(breathPeriod.Evaluate(t), 0.3f);
        }

        static void Play(AudioSource source, AudioClip clip, float volume, float pitch)
        {
            if (!source || !clip) return;
            source.pitch = pitch;
            source.PlayOneShot(clip, volume);
        }

        // ------------------------------------------------------------------ end

        void Finish()
        {
            IsPlaying = false;
            player.SetLook(player.transform.eulerAngles.y, 0f);
            player.cameraPivot.localPosition = new Vector3(0f, player.StandingEyeHeight, 0f);
            Release();
            enabled = false;
        }

        void OnDisable()
        {
            // stopped half way (disabled, scene unloaded): never leave the player locked, blind or deaf
            if (!IsPlaying) return;
            IsPlaying = false;
            Release();
        }

        void Release()
        {
            if (player) player.inputLocked = false;
            if (interactor) interactor.enabled = true;
            if (lens) { lens.eyelids = 0f; lens.daze = 0f; }
            if (muffle) { muffle.cutoffFrequency = 22000f; muffle.enabled = false; }
            HideBody(false);
        }

        /// <summary>The body model stands at the feet and the first-person arms hang in front of the eyes: while lying and
        /// getting up both would be in the wrong pose, so neither is drawn.</summary>
        void HideBody(bool hide)
        {
            if (bodyRenderers == null)
            {
                var list = new System.Collections.Generic.List<Renderer>();
                if (body) list.AddRange(body.GetComponentsInChildren<Renderer>(true));
                if (player && player.cameraPivot) list.AddRange(player.cameraPivot.GetComponentsInChildren<Renderer>(true));
                bodyRenderers = list.ToArray();
            }
            foreach (var r in bodyRenderers) if (r) r.forceRenderingOff = hide;
        }

        // ------------------------------------------------------------------ curves

        static float End(AnimationCurve c) => c != null && c.length > 0 ? c[c.length - 1].time : 0f;

        static void SetLastKey(AnimationCurve c, float value)
        {
            if (c == null || c.length == 0) return;
            int i = c.length - 1;
            var k = c[i];
            k.value = value;
            c.MoveKey(i, k);
            if (i > 0) c.SmoothTangents(i, 0f);
        }

        /// <summary>Pairs of (time, value) through smooth tangents; flat at both ends.</summary>
        static AnimationCurve Smooth(params float[] tv)
        {
            var c = Build(tv);
            for (int i = 1; i < c.length - 1; i++) c.SmoothTangents(i, 0f);
            return c;
        }

        /// <summary>Pairs of (time, value), easing in and out of every key (eyelids settle at each one).</summary>
        static AnimationCurve Eased(params float[] tv) => Build(tv);

        static AnimationCurve Build(float[] tv)
        {
            var keys = new Keyframe[tv.Length / 2];
            for (int i = 0; i < keys.Length; i++) keys[i] = new Keyframe(tv[i * 2], tv[i * 2 + 1], 0f, 0f);
            return new AnimationCurve(keys);
        }
    }
}
