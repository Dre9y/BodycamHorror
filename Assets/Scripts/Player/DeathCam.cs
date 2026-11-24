using UnityEngine;
using UnityEngine.InputSystem;

namespace BodycamHorror.Player
{
    /// <summary>
    /// What the body camera records when the player dies (test version). The body goes down and the camera
    /// with it: it drops to the floor, turning on its side towards whatever did it, and bounces once. Lying
    /// there it keeps recording, but the feed starts to break up: torn lines, broken blocks, the colours slipping,
    /// the sound crushed and stuttering (<see cref="DeathCamAudio"/>). The breaks come faster and harder until
    /// the picture goes under and the signal is gone: a black screen, NO SIGNAL and a hum. A key brings the
    /// player back at the start, the feed snapping back in through a last burst of noise.
    /// The picture is broken up inside the Lens Vignette pass (shader global _DeathCam).
    /// Added to the player by <see cref="PlayerHealth"/> if missing.
    /// </summary>
    [DefaultExecutionOrder(-200)]   // like WakeUpIntro: after the controller's Update, before the rig and the lens read the view
    public class DeathCam : MonoBehaviour
    {
        /// <summary>The player is dead and the death cam is running: gameplay input must be ignored.</summary>
        public static bool Active { get; private set; }

        [Header("Fall")]
        [Tooltip("Height of the camera above the feet once it lies on the floor (m).")]
        public float restHeight = 0.12f;
        [Tooltip("How far ahead of the feet the body falls (m).")]
        public float fallForward = 0.45f;
        [Tooltip("Random sideways spread of where it lands (m).")]
        public float fallSide = 0.25f;
        public float gravity = 16f;
        [Tooltip("Share of the fall speed it bounces back up with.")]
        [Range(0f, 0.6f)] public float bounce = 0.22f;
        [Tooltip("How far the camera ends up turned on its side (deg).")]
        public Vector2 rollRange = new Vector2(68f, 88f);
        [Tooltip("Look down (+) / up (-) once on the floor (deg).")]
        public Vector2 pitchRange = new Vector2(-8f, 12f);
        [Tooltip("How much it turns towards whatever landed the last blow (0 = not at all, 1 = straight at it).")]
        [Range(0f, 1f)] public float turnToKiller = 0.75f;
        public float maxTurn = 75f;

        [Header("Signal")]
        [Tooltip("Seconds from death until the signal is gone.")]
        public float signalLostAt = 3.6f;
        [Tooltip("Seconds before that of the feed falling apart completely.")]
        public float breakdownTime = 1.1f;
        [Tooltip("Strength of the breaks while lying there, at first and right before the breakdown.")]
        public Vector2 creepGlitch = new Vector2(0.03f, 0.3f);
        [Tooltip("Seconds between the sharp bursts (shorter as it gets worse).")]
        public Vector2 burstGap = new Vector2(0.25f, 0.9f);
        [Tooltip("How long a burst lasts (s).")]
        public Vector2 burstLength = new Vector2(0.05f, 0.22f);
        [Tooltip("How new glitch patterns come per second (a stepped, stuttery look).")]
        public Vector2 patternRate = new Vector2(8f, 24f);

        [Header("No signal")]
        [Tooltip("Seconds on the black screen before the prompt to continue shows.")]
        public float promptDelay = 1.2f;
        [Tooltip("Seconds of noise as the feed comes back after the respawn.")]
        public float reacquireTime = 0.6f;
        public string cameraName = "CAM 01";

        [Header("Debug")]
        [Tooltip("Editor / development builds: this key kills the player outright.")]
        public Key debugKillKey = Key.F7;

        [Header("References (found automatically)")]
        public BodycamPlayerController player;
        public PlayerHealth health;
        public BodycamCameraRig rig;
        public PlayerInteractor interactor;
        public PlayerBody body;

        enum Phase { Idle, Dying, NoSignal, Reacquire }

        static readonly int DeathCamId = Shader.PropertyToID("_DeathCam");

        Phase phase;
        float t;
        // fall
        Vector3 startPos, endPos;
        Quaternion startRot, endRot, restRot;
        Vector3 restPos;
        float height, fallSpeed, landedAt;
        bool landed;
        // glitch
        float spikeLeft, burstTimer, burstLeft, burstStrength, patternTimer, pattern;
        float glitch, staticNoise, blackout;
        // no signal
        string stamp;
        Renderer[] hidden;
        DeathCamAudio sound;
        GUIStyle big, small;
        Font mono;
        Texture2D black;

        void Awake()
        {
            if (!player) player = GetComponent<BodycamPlayerController>();
            if (!health) health = GetComponent<PlayerHealth>();
            if (!rig) rig = GetComponentInChildren<BodycamCameraRig>(true);
            if (!interactor) interactor = GetComponentInChildren<PlayerInteractor>(true);
            if (!body) body = GetComponentInChildren<PlayerBody>(true);
        }

        void OnDisable()
        {
            if (phase == Phase.Dying || phase == Phase.NoSignal) Release();
            phase = Phase.Idle;
            Active = false;
            Shader.SetGlobalVector(DeathCamId, Vector4.zero);
            if (sound) Quiet(sound);
        }

        /// <summary>Starts the death cam. <paramref name="blowDirection"/>: the way the last blow travelled (world).</summary>
        public void Play(Vector3 blowDirection)
        {
            if (!player || !player.cameraPivot) { if (health) health.Respawn(); return; }
            enabled = true;
            phase = Phase.Dying;
            Active = true;
            t = 0f;
            landed = false;
            spikeLeft = 0.15f;
            burstTimer = 0.5f;
            burstLeft = 0f;
            stamp = System.DateTime.Now.ToString("dd.MM.yyyy  HH:mm:ss");

            player.inputLocked = true;
            if (interactor) interactor.enabled = false;
            Hide(true);
            SetUpSound();

            // where the camera ends up: on the floor ahead of the feet, on its side, turned towards the killer
            Transform pivot = player.cameraPivot;
            startPos = pivot.localPosition;
            startRot = pivot.localRotation;
            float side = Random.value < 0.5f ? -1f : 1f;
            endPos = new Vector3(Random.Range(-fallSide, fallSide), restHeight, fallForward);
            Vector3 from = pivot.parent ? pivot.parent.TransformPoint(startPos) : startPos;
            Vector3 to = pivot.parent ? pivot.parent.TransformPoint(endPos) : endPos;
            // not through a wall: stop short of whatever is in the way
            if (Physics.SphereCast(from, 0.1f, to - from, out RaycastHit hit, (to - from).magnitude, ~0, QueryTriggerInteraction.Ignore)
                && !hit.transform.IsChildOf(player.transform))
            {
                Vector3 stop = hit.point + hit.normal * 0.15f;
                endPos = pivot.parent ? pivot.parent.InverseTransformPoint(stop) : stop;
                endPos.y = Mathf.Min(endPos.y, restHeight);
            }

            float yaw = 0f;
            Vector3 toKiller = Vector3.ProjectOnPlane(-blowDirection, Vector3.up);
            if (toKiller.sqrMagnitude > 1e-4f)
                yaw = Mathf.Clamp(Vector3.SignedAngle(player.transform.forward, toKiller, Vector3.up) * turnToKiller, -maxTurn, maxTurn);
            endRot = Quaternion.Euler(Random.Range(pitchRange.x, pitchRange.y), yaw, side * Random.Range(rollRange.x, rollRange.y));

            height = startPos.y;
            fallSpeed = 0f;
            if (rig) rig.AddTrauma(0.35f);
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (phase == Phase.Idle && Debug.isDebugBuild && kb != null && debugKillKey != Key.None
                && kb[debugKillKey].wasPressedThisFrame && health && !health.IsDead && !UI.PauseMenu.IsOpen)
                health.Kill();
        }

        void LateUpdate()
        {
            if (phase == Phase.Idle) return;
            float dt = Time.deltaTime;
            t += dt;

            switch (phase)
            {
                case Phase.Dying:
                    Fall(dt);
                    DyingSignal(dt);
                    if (t >= signalLostAt) LoseSignal();
                    break;
                case Phase.NoSignal:
                    // keep the view where it lies under the black (the controller would put it back up)
                    player.cameraPivot.localPosition = restPos;
                    player.cameraPivot.localRotation = restRot;
                    if (t >= promptDelay && ContinuePressed()) Respawn();
                    break;
                case Phase.Reacquire:
                {
                    float k = 1f - Mathf.Clamp01(t / Mathf.Max(reacquireTime, 0.01f));
                    glitch = k;
                    staticNoise = k * k * 0.6f;
                    blackout = 0f;
                    StepPattern(dt);
                    if (sound) { sound.crush = k; sound.gameLevel = 1f - k * k; sound.noiseLevel = k * 0.5f; sound.humLevel = 0f; }
                    if (k <= 0f)
                    {
                        phase = Phase.Idle;
                        glitch = staticNoise = 0f;
                        if (sound) Quiet(sound);
                    }
                    break;
                }
            }

            Shader.SetGlobalVector(DeathCamId, new Vector4(glitch, blackout, pattern, staticNoise));
        }

        // ------------------------------------------------------------------ the fall

        void Fall(float dt)
        {
            Transform pivot = player.cameraPivot;
            float floor = endPos.y;
            fallSpeed -= gravity * dt;
            height += fallSpeed * dt;
            if (height <= floor)
            {
                height = floor;
                if (fallSpeed < -1f)
                {
                    float hitSpeed = -fallSpeed;
                    fallSpeed = hitSpeed * bounce;
                    if (!landed)
                    {
                        landed = true;
                        landedAt = t;
                        spikeLeft = 0.14f;   // the jolt shakes the feed
                        if (rig) { rig.AddTrauma(0.6f); rig.AddTremor(1.2f); }
                        if (sound) sound.Thud();
                    }
                    else if (rig) rig.AddTrauma(0.15f);
                }
                else fallSpeed = 0f;
            }

            float drop = Mathf.Max(startPos.y - floor, 0.01f);
            float p = Mathf.Clamp01((startPos.y - Mathf.Max(height, floor)) / drop);
            if (landed) p = 1f;   // the bounce lifts it a little, it does not go back to standing
            float turn = p * p * (3f - 2f * p);

            Vector3 pos = Vector3.Lerp(startPos, endPos, Mathf.Sqrt(p));
            pos.y = landed ? height : Mathf.Lerp(startPos.y, floor, p);
            pivot.localPosition = pos;

            Quaternion rot = Quaternion.Slerp(startRot, endRot, turn);
            if (landed)
            {
                // rocks a little on its side and settles
                float s = t - landedAt;
                float wobble = Mathf.Exp(-5f * s) * Mathf.Sin(s * 17f) * 7f;
                rot *= Quaternion.Euler(wobble * 0.3f, 0f, wobble);
            }
            pivot.localRotation = rot;
        }

        // ------------------------------------------------------------------ the picture

        void DyingSignal(float dt)
        {
            float worstAt = signalLostAt - breakdownTime;
            float creep = Mathf.InverseLerp(0.4f, worstAt, t);
            float g = t > 0.4f ? Mathf.Lerp(creepGlitch.x, creepGlitch.y, creep) : 0f;

            if (spikeLeft > 0f) { spikeLeft -= dt; g = Mathf.Max(g, 0.8f); }

            if (t > 0.4f)
            {
                burstTimer -= dt;
                if (burstTimer <= 0f)
                {
                    burstLeft = Random.Range(burstLength.x, burstLength.y);
                    burstStrength = Random.Range(0.4f, 0.9f);
                    burstTimer = Random.Range(burstGap.x, burstGap.y) * Mathf.Lerp(1f, 0.35f, creep);
                }
                if (burstLeft > 0f) { burstLeft -= dt; g = Mathf.Max(g, burstStrength); }
            }

            // the breakdown: everything goes under
            float b = Mathf.InverseLerp(worstAt, signalLostAt, t);
            g = Mathf.Lerp(g, 1f, b * b);
            glitch = g;
            staticNoise = b * b * 0.75f;
            blackout = 0f;
            StepPattern(dt);

            if (sound)
            {
                sound.crush = Mathf.Clamp01(Mathf.Max(creep * 0.45f, g));
                sound.gameLevel = Mathf.Lerp(1f, 0.35f, b);
                sound.noiseLevel = Mathf.Max(b * 0.9f, burstLeft > 0f ? g * 0.25f : 0f);
                sound.humLevel = 0f;
            }
        }

        void StepPattern(float dt)
        {
            patternTimer -= dt;
            if (patternTimer > 0f) return;
            patternTimer = 1f / Random.Range(patternRate.x, patternRate.y);
            pattern = Random.Range(0f, 997f);
        }

        void LoseSignal()
        {
            phase = Phase.NoSignal;
            t = 0f;
            restPos = player.cameraPivot.localPosition;
            restRot = player.cameraPivot.localRotation;
            glitch = staticNoise = 0f;
            blackout = 1f;
            // cut: the game sound gone at once, only the dead line's hum and a faint hiss
            if (sound) { sound.crush = 0f; sound.gameLevel = 0f; sound.noiseLevel = 0.04f; sound.humLevel = 0.6f; }
        }

        static bool ContinuePressed()
        {
            var kb = Keyboard.current;
            if (kb != null && (kb.spaceKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame)) return true;
            var mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame) return true;
            var pad = Gamepad.current;
            return pad != null && (pad.buttonSouth.wasPressedThisFrame || pad.startButton.wasPressedThisFrame);
        }

        void Respawn()
        {
            Release();
            if (health) health.Respawn();
            phase = Phase.Reacquire;
            t = 0f;
            blackout = 0f;
        }

        void Release()
        {
            Active = false;
            if (player)
            {
                player.inputLocked = false;
                if (player.cameraPivot) { player.cameraPivot.localPosition = startPos; player.cameraPivot.localRotation = Quaternion.identity; }
            }
            if (interactor) interactor.enabled = true;
            Hide(false);
        }

        /// <summary>The first-person arms and the gun hang off the view, the body stands at the feet: on the floor
        /// both would be in the wrong place, so neither is drawn.</summary>
        void Hide(bool hide)
        {
            if (hidden == null)
            {
                var list = new System.Collections.Generic.List<Renderer>();
                if (body) list.AddRange(body.GetComponentsInChildren<Renderer>(true));
                if (player && player.cameraPivot) list.AddRange(player.cameraPivot.GetComponentsInChildren<Renderer>(true));
                hidden = list.ToArray();
            }
            foreach (var r in hidden) if (r) r.forceRenderingOff = hide;
            if (!hide) hidden = null;   // gather again next time: a new gun may have been picked up
        }

        // ------------------------------------------------------------------ sound

        void SetUpSound()
        {
            var listener = FindAnyObjectByType<AudioListener>();
            if (!listener) return;
            if (!sound || sound.gameObject != listener.gameObject)
            {
                sound = listener.GetComponent<DeathCamAudio>();
                if (!sound) sound = listener.gameObject.AddComponent<DeathCamAudio>();
            }
        }

        static void Quiet(DeathCamAudio s) { s.crush = 0f; s.gameLevel = 1f; s.noiseLevel = 0f; s.humLevel = 0f; }

        // ------------------------------------------------------------------ NO SIGNAL

        void OnGUI()
        {
            if (phase != Phase.NoSignal) return;
            float k = Screen.height / 1080f;
            if (!black) { black = new Texture2D(1, 1); black.SetPixel(0, 0, Color.black); black.Apply(); }
            if (big == null || Mathf.Abs(big.fontSize - Mathf.RoundToInt(64f * k)) > 0)
            {
                if (!mono) mono = Font.CreateDynamicFontFromOSFont(new[] { "Consolas", "Lucida Console", "Courier New" }, 32);
                big = new GUIStyle(GUI.skin.label) { font = mono, fontSize = Mathf.RoundToInt(64f * k), alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
                small = new GUIStyle(GUI.skin.label) { font = mono, fontSize = Mathf.RoundToInt(26f * k), alignment = TextAnchor.UpperLeft };
            }
            GUI.depth = -900;
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), black);

            var ink = new Color(0.88f, 0.9f, 0.86f, 1f);
            var dim = new Color(0.55f, 0.58f, 0.55f, 1f);
            float m = 48f * k;

            // blinking, like a monitor with nothing on the line
            if (Mathf.Repeat(t, 1.2f) < 0.85f)
            {
                big.normal.textColor = ink;
                GUI.Label(new Rect(0f, Screen.height * 0.5f - 60f * k, Screen.width, 90f * k), "NO SIGNAL", big);
            }
            small.alignment = TextAnchor.MiddleCenter;
            small.normal.textColor = dim;
            GUI.Label(new Rect(0f, Screen.height * 0.5f + 30f * k, Screen.width, 40f * k), cameraName + "  //  LINK LOST", small);

            small.alignment = TextAnchor.UpperLeft;
            small.normal.textColor = dim;
            GUI.Label(new Rect(m, m, 600f * k, 40f * k), cameraName, small);
            small.alignment = TextAnchor.UpperRight;
            GUI.Label(new Rect(Screen.width - m - 600f * k, m, 600f * k, 40f * k), stamp, small);

            if (t >= promptDelay)
            {
                small.alignment = TextAnchor.LowerCenter;
                float a = 0.5f + 0.5f * Mathf.Sin((t - promptDelay) * 3f);
                small.normal.textColor = new Color(dim.r, dim.g, dim.b, Mathf.Lerp(0.45f, 1f, a));
                GUI.Label(new Rect(0f, Screen.height - m - 40f * k, Screen.width, 40f * k), "[Пробел]  продолжить", small);
            }
        }
    }
}
