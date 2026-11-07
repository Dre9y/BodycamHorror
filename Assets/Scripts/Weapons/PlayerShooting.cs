using System;
using BodycamHorror.Player;
using UnityEngine;
using UnityEngine.InputSystem;
using Random = UnityEngine.Random;

namespace BodycamHorror.Weapons
{
    /// <summary>
    /// Two-handed pistol handling for the bodycam view. Sits on a holder under CameraPivot and poses
    /// the pistol in pivot space: ready, aimed (RMB), lowered while sprinting, tucked against walls,
    /// carried in the right hand during parkour moves. Adds recoil, breathing and bob.
    /// The hands lead: <see cref="BodycamCameraRig"/> follows <see cref="AimOffset"/> a moment later.
    /// LMB / RT fire, RMB / LT aim, R / X reload.
    /// </summary>
    [DefaultExecutionOrder(-100)]   // read input before the controller re-locks the cursor; pose before the camera rig
    public class PlayerShooting : MonoBehaviour
    {
        [Header("References")]
        public BodycamPlayerController player;
        public BodycamCameraRig rig;
        public Pistol pistol;

        [Header("Poses (pivot space; pistol origin = right-hand grip)")]
        public Vector3 readyPosition = new Vector3(0.03f, -0.15f, 0.36f);
        public Vector3 aimPosition = new Vector3(0f, -0.105f, 0.31f);
        [Tooltip("Slight inward cant of the ready pose (deg), so the gun does not read as a flat silhouette.")]
        public float readyCant = -5f;
        [Tooltip("The barrel points at this distance straight ahead of the eye when settled.")]
        public float convergenceDistance = 20f;
        public Vector3 loweredPosition = new Vector3(0.10f, -0.34f, 0.31f);
        public Vector3 loweredEuler = new Vector3(42f, -18f, 14f);
        public Vector3 tuckPosition = new Vector3(0.09f, -0.13f, 0.2f);
        public Vector3 tuckEuler = new Vector3(-55f, -12f, 8f);

        [Header("Blending (1/s)")]
        public float aimSpeed = 11f;
        public float lowerSpeed = 7f;
        public float carrySpeed = 16f;
        public float tuckSpeed = 14f;
        public float twoHandSpeed = 13f;
        [Tooltip("After firing or aiming the gun stays up (no sprint lowering) for this long.")]
        public float raiseHoldTime = 0.8f;
        [Tooltip("A trigger pull this early is kept until the gun is ready.")]
        public float fireBufferTime = 0.3f;
        [Range(0.2f, 1f)] public float aimMoveSpeedScale = 0.7f;

        [Header("Wall tuck")]
        public float tuckProbeLength = 0.72f;
        public float tuckProbeRadius = 0.07f;

        [Header("Recoil")]
        public float kickPitch = 330f;
        public float kickYaw = 70f;
        public float kickRoll = 120f;
        public float kickBack = 1.5f;
        public float kickUp = 0.3f;
        [Range(0f, 1f)] public float aimRecoilScale = 0.6f;
        public float oneHandRecoilScale = 1.5f;
        public float recoilRotStiffness = 300f;
        public float recoilRotDamping = 24f;
        public float recoilPosStiffness = 380f;
        public float recoilPosDamping = 28f;
        [Header("Recoil in the camera itself (on top of following the gun)")]
        [Tooltip("Slow shake per shot.")]
        public float cameraTraumaPerShot = 0.14f;
        [Tooltip("Fast fine shudder per shot.")]
        public float cameraTremorPerShot = 0.55f;
        [Tooltip("Upward kick of the camera springs per shot (deg/s).")]
        public float cameraKickPitch = 110f;
        public float cameraKickRoll = 60f;
        [Tooltip("Backward shove of the camera per shot (m/s).")]
        public float cameraKickBack = 0.25f;

        [Header("Look inertia (hands swing ahead of sharp moves)")]
        [Tooltip("Degrees the gun leads the view per deg/s of look speed. Negative makes it trail instead.")]
        public float lookLead = 0.035f;
        [Tooltip("Extra swing from sudden changes of look speed (flicks and hard stops).")]
        public float lookJerkLead = 0.26f;
        public float maxLookLead = 16f;
        public float leadStiffness = 280f;
        public float leadDamping = 19f;
        [Tooltip("How far (m) the gun travels sideways per radian of lead.")]
        public float leadArm = 0.3f;
        [Range(0f, 1f)] public float aimLeadScale = 0.6f;

        [Header("Stress tremor (needs PlayerStress on the player)")]
        public PlayerStress stress;
        [Tooltip("Hand tremor at full stress (deg).")]
        public float stressTremorAngle = 1.6f;
        public float stressTremorPosition = 0.005f;
        public float stressTremorFrequency = 9f;
        [Tooltip("Share of the tremor the camera follows; the rest shows as hands shaking in frame.")]
        [Range(0f, 1f)] public float stressTremorCameraShare = 0.35f;
        [Tooltip("Breathing sway multiplier at full stress.")]
        public float stressBreathBoost = 2f;
        public float stressSpread = 1.5f;

        [Header("Spread (degrees)")]
        public float hipSpread = 0.7f;
        public float aimSpread = 0.15f;
        public float moveSpread = 1.4f;
        public float airSpread = 2.5f;
        public float oneHandSpread = 1f;

        [Header("Sway")]
        public float breathSway = 0.35f;
        public float aimBreathScale = 0.5f;
        public float bobPosition = 0.012f;
        public float strafeRoll = 1.5f;

        /// <summary>Weapon rotation away from the settled aim (recoil + breathing), degrees, pivot space.</summary>
        public Vector3 AimOffset { get; private set; }
        /// <summary>0..1: how much the camera should follow the gun right now (only while it is up).</summary>
        public float CameraFollowWeight { get; private set; }
        public float AimWeight => aimW;
        public float TwoHandWeight => twoHandW;
        public bool IsReloading => reloading;
        public float ReloadProgress => reloading ? Mathf.Clamp01(reloadTimer / reloadDuration) : 0f;
        public bool IsCarrying => carryW > 0.5f;
        public int Ammo => pistol ? pistol.Ammo : 0;

        public event Action Fired;

        InputAction fireAction, aimAction, reloadAction;
        bool firePressed, aimHeld, reloadPressed;
        float lastFireRequest = -10f, raiseTimer;

        float aimW, lowW, tuckW, carryW, twoHandW = 1f;
        Vector3 carryPos;
        Quaternion carryRot = Quaternion.identity;

        Vector3 recoilPos, recoilPosVel, recoilRot, recoilRotVel;
        Vector2 lead, leadVel, smoothLook, prevSmoothLook;
        float seed;

        bool reloading, reloadMagOut, reloadMagIn, reloadRacked, reloadFromEmpty;
        float reloadTimer, reloadDuration = 1f;

        void Awake()
        {
            if (!player) player = GetComponentInParent<BodycamPlayerController>();
            if (!rig && player) rig = player.GetComponentInChildren<BodycamCameraRig>();
            if (!pistol) pistol = GetComponentInChildren<Pistol>();
            if (!stress && player) stress = player.GetComponent<PlayerStress>();
            seed = Random.value * 100f;
            if (pistol) pistol.transform.SetLocalPositionAndRotation(readyPosition, AimAt(readyPosition));

            fireAction = new InputAction("Fire", InputActionType.Button, "<Mouse>/leftButton");
            fireAction.AddBinding("<Gamepad>/rightTrigger");
            aimAction = new InputAction("Aim", InputActionType.Button, "<Mouse>/rightButton");
            aimAction.AddBinding("<Gamepad>/leftTrigger");
            reloadAction = new InputAction("Reload", InputActionType.Button, "<Keyboard>/r");
            reloadAction.AddBinding("<Gamepad>/buttonWest");
        }

        void OnEnable()
        {
            fireAction.Enable(); aimAction.Enable(); reloadAction.Enable();
            if (player) { player.Landed += OnLanded; player.Impacted += OnImpacted; }
        }

        void OnDisable()
        {
            fireAction.Disable(); aimAction.Disable(); reloadAction.Disable();
            if (player)
            {
                player.Landed -= OnLanded;
                player.Impacted -= OnImpacted;
                player.sprintBlocked = false;
                player.moveSpeedScale = 1f;
            }
        }

        void OnDestroy()
        {
            fireAction?.Dispose(); aimAction?.Dispose(); reloadAction?.Dispose();
        }

        // ------------------------------------------------------------------ input

        void Update()
        {
            bool cursorFree = Cursor.lockState != CursorLockMode.Locked;
            bool mouseFire = Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
            // The first click into the game window only grabs the cursor.
            firePressed = fireAction.WasPressedThisFrame() && !(cursorFree && mouseFire);
            aimHeld = aimAction.IsPressed() && !(cursorFree && Mouse.current != null && Mouse.current.rightButton.isPressed);
            reloadPressed = reloadAction.WasPressedThisFrame();

            if (firePressed) { lastFireRequest = Time.time; raiseTimer = raiseHoldTime; }
            if (aimHeld) raiseTimer = raiseHoldTime;
            raiseTimer -= Time.deltaTime;
            if (reloadPressed) TryStartReload();

            if (player)
            {
                player.sprintBlocked = raiseTimer > 0f;
                player.moveSpeedScale = Mathf.Lerp(1f, aimMoveSpeedScale, aimW);
            }
        }

        // ------------------------------------------------------------------ pose & firing

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f || !pistol || !player) return;

            MoveState state = player.State;
            bool parkour = IsParkourState(state);

            UpdateReload(dt);

            // wall tuck: something right in front of the chest pushes the gun up and back
            float tuckTarget = 0f;
            if (Physics.SphereCast(transform.position - transform.up * 0.1f, tuckProbeRadius, transform.forward, out RaycastHit wall,
                    tuckProbeLength, pistol.hitMask, QueryTriggerInteraction.Ignore))
                tuckTarget = Mathf.InverseLerp(tuckProbeLength, tuckProbeLength * 0.45f, wall.distance);

            bool lowerTarget = player.IsSprinting && raiseTimer <= 0f;
            bool aimTarget = aimHeld && !parkour && !reloading && tuckTarget < 0.5f;
            // Brace ready: the support hand comes off the gun to meet the wall.
            bool twoHandTarget = !parkour && state != MoveState.Sliding && !player.IsBraceReady
                                 && !(reloading && InReloadHandPhase());

            aimW = Approach(aimW, aimTarget ? 1f : 0f, aimSpeed, dt);
            lowW = Approach(lowW, lowerTarget ? 1f : 0f, lowerSpeed, dt);
            tuckW = Approach(tuckW, tuckTarget, tuckSpeed, dt);
            carryW = Approach(carryW, parkour ? 1f : 0f, carrySpeed, dt);
            twoHandW = Approach(twoHandW, twoHandTarget ? 1f : 0f, twoHandSpeed, dt);

            // --- base pose
            Vector3 readyPos = Vector3.Lerp(readyPosition, aimPosition, aimW);
            Quaternion readyRot = AimAt(readyPos) * Quaternion.Euler(0f, 0f, readyCant * (1f - aimW));

            Vector3 pos = Vector3.Lerp(readyPos, loweredPosition, Smooth(lowW));
            Quaternion rot = Quaternion.Slerp(readyRot, Quaternion.Euler(loweredEuler), Smooth(lowW));
            pos = Vector3.Lerp(pos, tuckPosition, Smooth(tuckW));
            rot = Quaternion.Slerp(rot, Quaternion.Euler(tuckEuler), Smooth(tuckW));

            if (parkour) UpdateCarryPose(state, readyPos, readyRot);
            pos = Vector3.Lerp(pos, carryPos, Smooth(carryW));
            rot = Quaternion.Slerp(rot, carryRot, Smooth(carryW));

            // --- reload: tilt the gun so the mag well faces the left hand
            if (reloading)
            {
                float p = ReloadProgress;
                float tilt = Mathf.Clamp01(p / 0.15f) * Mathf.Clamp01((1f - p) / 0.18f);
                tilt = Smooth(tilt);
                pos += new Vector3(-0.045f, 0.035f, -0.06f) * tilt;
                rot *= Quaternion.Euler(-14f * tilt, 16f * tilt, -32f * tilt);
            }

            // --- body motion: bob and strafe roll (the camera has its own, so the camera does not follow these)
            float bob = rig ? rig.BobAmount : 0f;
            float phase = rig ? rig.StepPhase : 0f;
            float s = Mathf.Sin(phase);
            pos += new Vector3(s * bobPosition * 0.7f, -Mathf.Abs(s) * bobPosition, 0f) * bob * (1f - aimW * 0.5f);
            Vector3 localVel = player.transform.InverseTransformDirection(player.HorizontalVelocity);
            rot *= Quaternion.Euler(0f, 0f, -localVel.x * strafeRoll);

            float sdt = Mathf.Min(dt, 1f / 30f);   // clamped step keeps the stiff springs stable on frame spikes

            // --- look inertia: a sharp flick throws the hands ahead of the view, a hard stop lets them swing
            // back past centre. The camera does not follow this, so it reads as the hands moving on screen.
            Vector2 lookVel = player.LookVelocity;   // deg/s, x = yaw, y = pitch (down positive)
            smoothLook = Vector2.Lerp(smoothLook, lookVel, 1f - Mathf.Exp(-dt / 0.015f));
            Vector2 leadTarget = Vector2.ClampMagnitude(smoothLook * lookLead, maxLookLead);
            leadVel += (smoothLook - prevSmoothLook) * lookJerkLead;
            prevSmoothLook = smoothLook;
            leadVel += ((leadTarget - lead) * leadStiffness - leadVel * leadDamping) * sdt;
            lead += leadVel * sdt;
            lead = Vector2.ClampMagnitude(lead, maxLookLead);
            Vector2 leadNow = lead * Mathf.Lerp(1f, aimLeadScale, aimW) * (1f - 0.5f * carryW);
            pos += new Vector3(leadNow.x, -leadNow.y, 0f) * (Mathf.Deg2Rad * leadArm);
            rot = Quaternion.Euler(leadNow.y, leadNow.x, 0f) * rot * Quaternion.Euler(0f, 0f, -leadNow.x * 0.4f);

            // --- breathing sway (the camera follows this); heavier under stress
            float stress01 = stress ? stress.Value : 0f;
            float breath = breathSway * Mathf.Lerp(1f, aimBreathScale, aimW) * (1f + player.SprintBlend)
                           * (1f + stress01 * (stressBreathBoost - 1f));
            Vector3 sway = new Vector3(Noise(seed, Time.time * 0.35f), Noise(seed + 3f, Time.time * 0.28f), 0f) * breath;

            // --- stress tremor: fine, fast shake of the hands
            float tremor01 = stress ? stress.Tremor : 0f;
            Vector3 tremor = Vector3.zero, tremorPos = Vector3.zero;
            if (tremor01 > 0.001f)
            {
                float tf = Time.time * stressTremorFrequency;
                tremor = new Vector3(Noise(seed + 11f, tf) + 0.5f * Noise(seed + 12f, tf * 2.3f),
                                     Noise(seed + 13f, tf) + 0.5f * Noise(seed + 14f, tf * 2.3f),
                                     Noise(seed + 15f, tf) * 0.6f)
                         * (stressTremorAngle * tremor01 * Mathf.Lerp(1f, 0.75f, aimW));
                tremorPos = new Vector3(Noise(seed + 16f, tf * 1.3f), Noise(seed + 17f, tf * 1.3f), 0f)
                            * (stressTremorPosition * tremor01);
            }

            // --- recoil spring
            StepSpring(ref recoilPos, ref recoilPosVel, Vector3.zero, recoilPosStiffness, recoilPosDamping, sdt);
            StepSpring(ref recoilRot, ref recoilRotVel, Vector3.zero, recoilRotStiffness, recoilRotDamping, sdt);

            pistol.transform.localPosition = pos + recoilPos + tremorPos;
            pistol.transform.localRotation = rot * Quaternion.Euler(recoilRot + sway + tremor);

            AimOffset = recoilRot + sway + tremor * stressTremorCameraShare;
            CameraFollowWeight = (1f - carryW) * (1f - lowW) * (1f - tuckW);

            TryFire();
        }

        void TryFire()
        {
            if (Time.time - lastFireRequest > fireBufferTime) return;
            bool ready = carryW < 0.15f && lowW < 0.3f && tuckW < 0.45f && !reloading;
            if (!ready || !pistol.CanFire) return;

            lastFireRequest = -10f;
            if (pistol.Ammo <= 0) { pistol.DryFire(); return; }

            float speed01 = Mathf.Clamp01(player.Speed / Mathf.Max(player.sprintSpeed, 0.1f));
            float spread = Mathf.Lerp(hipSpread, aimSpread, aimW) + moveSpread * speed01
                           + (player.State == MoveState.Airborne ? airSpread : 0f)
                           + oneHandSpread * (1f - twoHandW)
                           + (stress ? stress.Tremor * stressSpread : 0f);

            if (!pistol.Fire(transform.position, spread, player.gameObject)) return;

            float k = Mathf.Lerp(1f, aimRecoilScale, aimW) * Mathf.Lerp(oneHandRecoilScale, 1f, twoHandW);
            recoilRotVel += new Vector3(-kickPitch * Random.Range(0.85f, 1.15f),
                                        Random.Range(-kickYaw, kickYaw),
                                        Random.Range(-kickRoll, kickRoll)) * k;
            recoilPosVel += new Vector3(Random.Range(-0.05f, 0.05f), kickUp, -kickBack) * k;
            if (rig)
            {
                rig.AddTrauma(cameraTraumaPerShot * k);
                rig.AddTremor(cameraTremorPerShot * k);
                rig.AddKick(new Vector3(0f, 0f, -cameraKickBack * k),
                            new Vector3(-cameraKickPitch * Random.Range(0.8f, 1.2f), 0f, Random.Range(-cameraKickRoll, cameraKickRoll)) * k);
            }
            Fired?.Invoke();
        }

        void UpdateCarryPose(MoveState state, Vector3 readyPos, Quaternion readyRot)
        {
            float p = player.ActionProgress;
            switch (state)
            {
                case MoveState.Mantling:
                {
                    // gun hand goes up onto the ledge with the other one, then pushes the body over
                    float reach = 1f - Mathf.Clamp01((p - 0.45f) / 0.55f);
                    carryPos = Vector3.Lerp(new Vector3(0.22f, -0.36f, 0.42f), new Vector3(0.24f, 0.0f, 0.6f), reach);
                    carryRot = Quaternion.Euler(Vector3.Lerp(new Vector3(55f, -8f, 0f), new Vector3(-10f, -12f, 70f), reach));
                    break;
                }
                case MoveState.Vaulting:
                    carryPos = new Vector3(0.40f, -0.2f, 0.3f);      // balance arm out, gun pointed down-forward
                    carryRot = Quaternion.Euler(35f, 15f, 30f);
                    break;
                case MoveState.Stumbling:
                    carryPos = new Vector3(0.3f, -0.3f, 0.55f);
                    carryRot = Quaternion.Euler(25f, -15f, 25f);
                    break;
                case MoveState.Bumped:
                {
                    // braced against the wall, coming back to ready as the stun wears off
                    float brace = 1f - p;
                    carryPos = Vector3.Lerp(readyPos, new Vector3(0.16f, -0.12f, 0.36f), brace);
                    carryRot = Quaternion.Slerp(readyRot, Quaternion.Euler(-30f, -10f, 10f), brace);
                    break;
                }
                case MoveState.BraceWall:
                    // gun hand flat against the wall, gun turned on its side
                    carryPos = new Vector3(0.2f, -0.08f, 0.4f);
                    carryRot = Quaternion.Euler(-15f, -20f, 75f);
                    break;
                case MoveState.HandLanding:
                    carryPos = new Vector3(0.22f, -0.5f, 0.34f);
                    carryRot = Quaternion.Euler(65f, -10f, 15f);
                    break;
                case MoveState.Rolling:
                    // tucked to the chest
                    carryPos = new Vector3(0.1f, -0.22f, 0.2f);
                    carryRot = Quaternion.Euler(-30f, -25f, 60f);
                    break;
                case MoveState.HardLanding:
                    carryPos = new Vector3(0.3f, -0.42f, 0.38f);
                    carryRot = Quaternion.Euler(50f, -20f, 35f);
                    break;
            }
        }

        static bool IsParkourState(MoveState s) =>
            s == MoveState.Mantling || s == MoveState.Vaulting || s == MoveState.Stumbling || s == MoveState.Bumped
            || s == MoveState.BraceWall || s == MoveState.HandLanding || s == MoveState.Rolling || s == MoveState.HardLanding;

        // ------------------------------------------------------------------ reload

        void TryStartReload()
        {
            if (reloading || !pistol || pistol.Ammo >= pistol.magazineSize) return;
            reloading = true;
            reloadFromEmpty = pistol.Ammo == 0;
            reloadDuration = pistol.reloadTime + (reloadFromEmpty ? pistol.emptyReloadExtra : 0f);
            reloadTimer = 0f;
            reloadMagOut = reloadMagIn = reloadRacked = false;
            lastFireRequest = -10f;
        }

        bool InReloadHandPhase()
        {
            float p = ReloadProgress;
            float magIn = MagInAt;
            return p > 0.12f && p < magIn + 0.04f;
        }

        float MagInAt => reloadFromEmpty ? 0.58f : 0.68f;

        void UpdateReload(float dt)
        {
            if (!reloading) return;
            reloadTimer += dt;
            float p = ReloadProgress;
            if (!reloadMagOut && p >= 0.16f) { reloadMagOut = true; pistol.PlayMagOut(); }
            if (!reloadMagIn && p >= MagInAt)
            {
                reloadMagIn = true;
                pistol.Ammo = pistol.magazineSize;
                pistol.PlayMagIn();
                recoilPosVel += new Vector3(0f, 0.35f, 0.2f);   // slap the mag home
                recoilRotVel += new Vector3(-40f, 0f, 30f);
            }
            if (reloadFromEmpty && !reloadRacked && p >= 0.84f)
            {
                reloadRacked = true;
                pistol.PlayRack();
                recoilPosVel += new Vector3(0f, 0f, -0.4f);
            }
            if (p >= 1f) reloading = false;
        }

        // ------------------------------------------------------------------ helpers

        void OnLanded(float impact)
        {
            if (impact < 2f) return;
            float k = Mathf.Clamp01((impact - 2f) / 12f);
            if (player.LastLandingKind == LandingKind.Hard) k = Mathf.Min(1f, k * 1.6f + 0.2f);
            else if (player.LastLandingKind != LandingKind.Normal) k *= 0.4f;
            recoilPosVel.y -= Mathf.Lerp(0.3f, 1.6f, k);
            recoilRotVel.x += Mathf.Lerp(15f, 140f, k);
            recoilRotVel.z += Random.Range(-1f, 1f) * Mathf.Lerp(10f, 120f, k);
        }

        void OnImpacted(ImpactKind kind, float speed)
        {
            // the hands get thrown about by trips and wall hits
            float k = Mathf.InverseLerp(3f, 9f, speed) * 0.6f + 0.4f;
            if (kind == ImpactKind.Brace) k *= 0.3f;
            else if (kind == ImpactKind.Scrape) k *= 0.2f;
            recoilRotVel += new Vector3(Random.Range(-1f, 1f) * 260f, Random.Range(-1f, 1f) * 200f, Random.Range(-1f, 1f) * 260f) * k;
            recoilPosVel += new Vector3(Random.Range(-0.6f, 0.6f), Random.Range(-0.8f, 0.4f), kind == ImpactKind.Wall ? -1.2f : 0.6f) * k;
        }

        Quaternion AimAt(Vector3 gripPos)
        {
            float sight = pistol ? pistol.SightHeight : 0.09f;
            Vector3 toTarget = new Vector3(0f, 0f, convergenceDistance) - (gripPos + Vector3.up * sight);
            return Quaternion.LookRotation(toTarget, Vector3.up);
        }

        static float Approach(float value, float target, float speed, float dt) =>
            Mathf.Lerp(value, target, 1f - Mathf.Exp(-speed * dt));

        static float Smooth(float t) => t * t * (3f - 2f * t);

        static void StepSpring(ref Vector3 value, ref Vector3 velocity, Vector3 target, float k, float d, float dt)
        {
            velocity += ((target - value) * k - velocity * d) * dt;
            value += velocity * dt;
        }

        static float Noise(float s, float t) => Mathf.PerlinNoise(s * 13.7f, t) * 2f - 1f;
    }
}
