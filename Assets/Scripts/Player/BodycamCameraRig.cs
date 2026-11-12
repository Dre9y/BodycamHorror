using BodycamHorror.Weapons;
using UnityEngine;

namespace BodycamHorror.Player
{
    /// <summary>
    /// Body-worn camera feel: footstep bob, heavy rotational lag behind the look, lean on strafe and
    /// braking, landing dips, impact shake, slide roll and FOV kick. Sits between the pitch pivot and
    /// the camera and only writes its own local offsets.
    /// </summary>
    public class BodycamCameraRig : MonoBehaviour
    {
        public BodycamPlayerController player;
        public Camera cam;

        [Header("FOV")]
        public float baseFov = 68f;
        public float sprintFov = 73f;
        public float slideFov = 78f;
        public float fovSharpness = 6f;

        [Header("Footstep bob")]
        [Tooltip("Meters travelled per left+right step cycle at walk / sprint.")]
        public float strideWalk = 3f;
        public float strideSprint = 5.6f;
        public float bobVertical = 0.05f;
        public float bobHorizontal = 0.04f;
        public float bobRoll = 1.8f;
        public float bobPitch = 1.2f;
        public float sprintBobMultiplier = 1.8f;
        [Tooltip("Share of each step spent dropping onto the foot; the rest is the slow climb to the next one.")]
        [Range(0.05f, 0.6f)] public float stepDropShare = 0.25f;

        [Header("Footfalls (a loaded body landing on each foot)")]
        [Tooltip("Downward jolt per footfall at a sprint (m/s).")]
        public float stepDropKick = 0.55f;
        [Tooltip("Head nod per footfall at a sprint (deg/s).")]
        public float stepPitchKick = 28f;
        [Tooltip("Roll towards the leading foot per footfall at a sprint (deg/s).")]
        public float stepRollKick = 10f;
        public float stepTremor = 0.12f;
        [Tooltip("Strength of a walking footfall compared with a sprinting one.")]
        [Range(0f, 1f)] public float walkStepWeight = 0.35f;
        [Tooltip("Share of the footfall left while aiming.")]
        [Range(0f, 1f)] public float aimStepWeight = 0.4f;

        [Header("Follow the hands")]
        [Tooltip("Optional. When set, the camera follows the gun's recoil and sway.")]
        public PlayerShooting weapon;
        [Tooltip("The hands lead and the camera catches up after roughly this long (s). Applies to mouse look too.")]
        public float handFollowLag = 0.03f;
        [Tooltip("How much of the gun's own motion (recoil, breathing) the camera takes on.")]
        [Range(0f, 1f)] public float handFollowAmount = 0.85f;
        [Tooltip("The camera never trails the hands by more than this (deg).")]
        public float maxFollowAngle = 6f;

        [Header("Look roll (heavy body-cam)")]
        public float lagRoll = 0.006f;
        public float maxLag = 6f;

        [Header("Aim")]
        public float aimFov = 60f;

        [Header("Lean")]
        public float strafeRoll = 1.2f;
        public float slideRoll = 7f;

        [Header("Springs")]
        public float posStiffness = 140f;
        public float posDamping = 16f;
        public float rotStiffness = 110f;
        public float rotDamping = 13f;
        [Tooltip("Stiff spring for AddKick (gun recoil): short, sharp jolts.")]
        public float kickStiffness = 420f;
        public float kickDamping = 30f;

        [Header("Shake")]
        public float traumaDecay = 1.3f;
        public float shakeAngle = 7f;
        public float shakePosition = 0.05f;
        public float shakeFrequency = 18f;

        [Header("Tremor (fast, fine shudder: shots, hard knocks)")]
        public float tremorDecay = 7f;
        public float tremorAngle = 0.9f;
        public float tremorPosition = 0.004f;
        public float tremorFrequency = 42f;

        [Header("Knocks (landings, trips, wall hits)")]
        [Tooltip("Scales every landing / stumble / wall-hit reaction at once.")]
        [Range(0f, 3f)] public float impactStrength = 1f;

        [Header("Idle breathing")]
        public float breathAmount = 0.006f;
        public float breathAngle = 0.5f;
        public float breathRate = 0.3f;
        [Tooltip("Optional. Stress makes breathing deeper and faster.")]
        public PlayerStress stress;
        public float stressBreathAmount = 2.5f;
        public float stressBreathRate = 2.2f;

        /// <summary>Footstep phase in radians (one full cycle = left + right step).</summary>
        public float StepPhase { get; private set; }
        /// <summary>A foot just came down, in step with the bob (0 walk .. 1 sprint). See PlayerFootsteps.</summary>
        public event System.Action<float> Footstep;
        /// <summary>0..1 bob strength this frame.</summary>
        public float BobAmount { get; private set; }
        /// <summary>How far steps, landings and leaning have moved the camera off the pivot.</summary>
        public Vector3 BodyPosition => posValue;
        /// <summary>How far steps, landings and leaning have turned the camera off the pivot.</summary>
        public Quaternion BodyRotation => Quaternion.Euler(rotValue);

        Vector3 posValue, posVelocity, rotValue, rotVelocity, kickValue, kickVelocity;
        float trauma, tremor, stumbleSide = 1f, seed, breathPhase;
        Quaternion followRot;
        bool followInit;
        int lastFootfall;

        void Awake()
        {
            seed = Random.value * 100f;
            if (!cam) cam = GetComponentInChildren<Camera>();
            if (!player) player = GetComponentInParent<BodycamPlayerController>();
            if (!weapon && player) weapon = player.GetComponentInChildren<PlayerShooting>();
            if (!stress && player) stress = player.GetComponent<PlayerStress>();
        }

        /// <summary>Adds camera shake (0..1, decays over time).</summary>
        public void AddTrauma(float amount) => trauma = Mathf.Clamp01(Mathf.Max(trauma, 0f) + amount);

        /// <summary>Adds a fast, fine shudder (decays within a fraction of a second).</summary>
        public void AddTremor(float amount) => tremor = Mathf.Min(1.5f, tremor + amount);

        /// <summary>Snappy kick (e.g. gun recoil) on a stiff spring: positional (m/s) and rotational (deg/s) velocity.</summary>
        public void AddKick(Vector3 positionVelocity, Vector3 rotationVelocity)
        {
            posVelocity += positionVelocity;
            kickVelocity += rotationVelocity;
        }

        void OnEnable()
        {
            if (!player) return;
            player.Landed += OnLanded;
            player.Impacted += OnImpacted;
            player.StateChanged += OnStateChanged;
        }

        void OnDisable()
        {
            if (!player) return;
            player.Landed -= OnLanded;
            player.Impacted -= OnImpacted;
            player.StateChanged -= OnStateChanged;
        }

        void LateUpdate()
        {
            float dt = Mathf.Min(Time.deltaTime, 1f / 30f);
            if (dt <= 0f || !player) return;

            MoveState state = player.State;
            Vector3 localVel = player.transform.InverseTransformDirection(player.HorizontalVelocity);
            float speed = player.Speed;
            float sprint01 = player.SprintBlend;

            Vector3 targetPos = Vector3.zero;
            Vector3 targetRot = Vector3.zero;

            // Footstep bob
            float bob = state == MoveState.Grounded ? Mathf.Clamp01(speed / player.walkSpeed) : 0f;
            bob *= Mathf.Lerp(1f, sprintBobMultiplier, sprint01);
            if (player.IsCrouching) bob *= 0.6f;
            BobAmount = bob;
            float stride = Mathf.Lerp(strideWalk, strideSprint, sprint01);
            StepPhase += speed * dt * (Mathf.PI * 2f / stride);
            float s = Mathf.Sin(StepPhase);
            // each step: the body drops quickly onto the foot, then climbs slowly towards the next one
            float stepT = Mathf.Repeat(StepPhase / Mathf.PI, 1f);
            float step = stepT < stepDropShare ? Smooth(stepT / stepDropShare) : 1f - Smooth((stepT - stepDropShare) / (1f - stepDropShare));
            Footfall(state, sprint01);
            targetPos.y -= step * bobVertical * bob;
            targetPos.x += s * bobHorizontal * bob;
            targetRot.z += s * bobRoll * bob;
            targetRot.x += step * bobPitch * bob;

            // Breathing: deeper and faster under stress
            float stress01 = stress ? stress.Value : 0f;
            float breathK = 1f + stress01 * (stressBreathAmount - 1f);
            breathPhase += breathRate * (1f + stress01 * (stressBreathRate - 1f)) * Mathf.PI * 2f * dt;
            targetPos.y += Mathf.Sin(breathPhase) * breathAmount * breathK;
            targetRot.x += (Noise(seed, Time.time * 0.3f) * breathAngle - Mathf.Sin(breathPhase) * 0.25f * stress01) * breathK;
            targetRot.y += Noise(seed + 7f, Time.time * 0.25f) * breathAngle * breathK;

            // Heavy camera rolls into turns (the trailing itself comes from following the hands below)
            Vector2 lv = player.LookVelocity;
            targetRot.z += Mathf.Clamp(-lv.x * lagRoll, -maxLag, maxLag);

            // Lean
            targetRot.z += -localVel.x * strafeRoll;

            switch (state)
            {
                case MoveState.Sliding:
                    targetRot.z += slideRoll;
                    trauma = Mathf.Max(trauma, 0.22f);   // ground rumble
                    break;
                case MoveState.Mantling:
                {
                    float a = Mathf.Sin(player.ActionProgress * Mathf.PI);
                    targetRot.x += a * 12f;
                    targetRot.z -= a * 3f;
                    break;
                }
                case MoveState.Vaulting:
                {
                    float a = Mathf.Sin(player.ActionProgress * Mathf.PI);
                    targetRot.z += a * 9f;
                    targetRot.x += a * 4f;
                    break;
                }
                case MoveState.Stumbling:
                {
                    // fast pitch forward, slower recovery
                    float p = player.ActionProgress;
                    float a = p < 0.2f ? Smooth(p / 0.2f) : 1f - Smooth((p - 0.2f) / 0.8f);
                    a *= impactStrength;
                    targetRot.x += a * 40f;
                    targetRot.z += a * 18f * stumbleSide;
                    targetRot.y += a * 6f * stumbleSide;
                    targetPos.y -= a * 0.45f;
                    targetPos.z += a * 0.1f;
                    break;
                }
                case MoveState.Bumped:
                    targetRot.x -= 4f * (1f - player.ActionProgress);
                    break;
                case MoveState.DiveBack:
                {
                    // thrown backwards: chin up, a touch of roll
                    float a = Mathf.Sin(Mathf.Min(player.ActionProgress, 1f) * Mathf.PI * 0.5f);
                    targetRot.x -= a * 14f;
                    targetRot.z += a * 4f;
                    break;
                }
                case MoveState.Prone:
                {
                    // lying on the back, chest-cam tilted up; shoulders work side to side while crawling
                    float crawl = Mathf.Clamp01(speed / 1.5f);
                    float c = Mathf.Sin(StepPhase * 1.6f);
                    targetRot.x -= 5f;
                    targetRot.z += c * 3.5f * crawl;
                    targetPos.x += c * 0.03f * crawl;
                    break;
                }
                case MoveState.BraceWall:
                {
                    // body compresses into the hands, then springs off
                    float a = Mathf.Sin(Mathf.Min(player.StateTime / 0.18f, 1f) * Mathf.PI * 0.5f);
                    targetRot.x += a * 7f;
                    targetPos.y -= a * 0.05f;
                    targetPos.z += a * 0.06f;
                    break;
                }
                case MoveState.HandLanding:
                {
                    // quick drop onto the hands, snap back up
                    float p = player.ActionProgress;
                    float a = p < 0.3f ? Smooth(p / 0.3f) : 1f - Smooth((p - 0.3f) / 0.7f);
                    targetRot.x += a * 24f;
                    targetRot.z += a * 4f * stumbleSide;
                    targetPos.y -= a * 0.1f;
                    break;
                }
                case MoveState.Rolling:
                    // the head-over-heels turn itself is on the pivot (RollAngle); add a little sideways wobble
                    targetRot.z += Mathf.Sin(player.ActionProgress * Mathf.PI) * 6f * stumbleSide;
                    trauma = Mathf.Max(trauma, 0.18f);
                    break;
                case MoveState.HardLanding:
                {
                    // slammed down, slow to straighten up
                    float p = player.ActionProgress;
                    float a = (p < 0.12f ? Smooth(p / 0.12f) : 1f - Smooth((p - 0.12f) / 0.88f)) * impactStrength;
                    float sev = 0.6f + 0.4f * player.LastLandingSeverity;
                    targetRot.x += a * 38f * sev;
                    targetRot.z += a * 14f * sev * stumbleSide;
                    targetPos.y -= a * 0.25f * sev;
                    break;
                }
            }

            // Springs
            StepSpring(ref posValue, ref posVelocity, targetPos, posStiffness, posDamping, dt);
            StepSpring(ref rotValue, ref rotVelocity, targetRot, rotStiffness, rotDamping, dt);
            StepSpring(ref kickValue, ref kickVelocity, Vector3.zero, kickStiffness, kickDamping, dt);

            // Trauma shake
            trauma = Mathf.Max(0f, trauma - traumaDecay * dt);
            float shake = trauma * trauma;
            float t = Time.time * shakeFrequency;
            Vector3 shakeRot = new Vector3(Noise(seed + 1f, t), Noise(seed + 2f, t), Noise(seed + 3f, t)) * (shakeAngle * shake);
            Vector3 shakePos = new Vector3(Noise(seed + 4f, t), Noise(seed + 5f, t), 0f) * (shakePosition * shake);

            // Tremor: fast fine shudder on top
            tremor *= Mathf.Exp(-tremorDecay * dt);
            if (tremor > 0.001f)
            {
                float tt = Time.time * tremorFrequency;
                shakeRot += new Vector3(Noise(seed + 21f, tt), Noise(seed + 22f, tt), Noise(seed + 23f, tt) * 1.5f) * (tremorAngle * tremor);
                shakePos += new Vector3(Noise(seed + 24f, tt), Noise(seed + 25f, tt), 0f) * (tremorPosition * tremor);
            }

            // Follow the hands: the pivot (mouse look) and the gun lead, the camera catches up in world
            // space a moment later, so on screen the gun swings ahead and the view settles onto it.
            Transform pivot = transform.parent;
            Quaternion pivotRot = pivot ? pivot.rotation : Quaternion.identity;
            Vector3 handOffset = weapon && weapon.isActiveAndEnabled
                ? weapon.AimOffset * (handFollowAmount * weapon.CameraFollowWeight) : Vector3.zero;
            Quaternion followTarget = pivotRot * Quaternion.Euler(handOffset);
            if (!followInit) { followRot = followTarget; followInit = true; }
            float followDt = Mathf.Min(Time.deltaTime, 0.1f);
            followRot = Quaternion.Slerp(followRot, followTarget, 1f - Mathf.Exp(-followDt / Mathf.Max(handFollowLag, 0.001f)));
            if (Quaternion.Angle(followRot, followTarget) > maxFollowAngle)
                followRot = Quaternion.RotateTowards(followTarget, followRot, maxFollowAngle);
            Quaternion followLocal = Quaternion.Inverse(pivotRot) * followRot;

            transform.localPosition = posValue + shakePos;
            transform.localRotation = followLocal * Quaternion.Euler(rotValue + kickValue + shakeRot);

            if (cam)
            {
                float targetFov = state == MoveState.Sliding || state == MoveState.Rolling ? slideFov : Mathf.Lerp(baseFov, sprintFov, sprint01);
                if (weapon && weapon.isActiveAndEnabled) targetFov = Mathf.Lerp(targetFov, aimFov, weapon.AimWeight);
                cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, targetFov, 1f - Mathf.Exp(-fovSharpness * dt));
            }
        }

        void OnLanded(float impact)
        {
            if (impact < 2f) return;
            float k = Mathf.Clamp01((impact - 2f) / 10f);
            float side = Random.value < 0.5f ? -1f : 1f;
            stumbleSide = side;
            // A tucked landing is absorbed by the hands / roll; an untucked one is a slam.
            float m = impactStrength * player.LastLandingKind switch
            {
                LandingKind.Hands => 0.4f,
                LandingKind.Roll => 0.2f,
                LandingKind.Hard => 1.4f,
                _ => 1f,
            };
            posVelocity.y -= Mathf.Lerp(0.8f, 5f, k) * m;
            rotVelocity.x += Mathf.Lerp(40f, 280f, k) * m;          // head snaps down
            rotVelocity.z += side * Mathf.Lerp(10f, 120f, k) * m;   // and twists off-axis
            trauma = Mathf.Max(trauma, Mathf.Clamp01(k * 0.9f * m));
            AddTremor(Mathf.Lerp(0.2f, 1.1f, k) * m);
        }

        void OnImpacted(ImpactKind kind, float speed)
        {
            float k = Mathf.InverseLerp(3f, 9f, speed);
            float m = impactStrength;
            float side = Random.value < 0.5f ? -1f : 1f;
            if (kind == ImpactKind.Scrape)
            {
                // shoulder brushes the wall: nudged away from it, a short rough shudder
                float wallSide = Mathf.Sign(player.transform.InverseTransformPoint(player.LastImpactPoint).x);
                float s = Mathf.InverseLerp(1f, 6f, speed) * m;
                posVelocity.x -= wallSide * (0.3f + 0.5f * s);
                rotVelocity.z += wallSide * (20f + 40f * s);
                rotVelocity.y -= wallSide * (8f + 15f * s);
                AddTremor(0.25f + 0.35f * s);
            }
            else if (kind == ImpactKind.Brace)
            {
                // hands took it: a firm thud, no stagger
                posVelocity.z -= (0.6f + 0.8f * k) * m;
                rotVelocity.x -= (20f + 30f * k) * m;
                trauma = Mathf.Max(trauma, 0.2f * m);
                AddTremor(0.35f * m);
            }
            else if (kind == ImpactKind.Wall)
            {
                posVelocity.z -= (1.5f + 3f * k) * m;
                rotVelocity.x -= (60f + 160f * k) * m;
                rotVelocity.z += side * (50f + 130f * k) * m;
                trauma = Mathf.Max(trauma, Mathf.Clamp01((0.55f + 0.45f * k) * m));
                AddTremor((0.6f + 0.6f * k) * m);
            }
            else
            {
                stumbleSide = side;
                float s = 0.5f + 0.5f * k;
                rotVelocity.x += 300f * s * m;
                rotVelocity.z += side * 180f * s * m;
                posVelocity.y -= 1.6f * s * m;
                posVelocity.z += 0.8f * s * m;
                trauma = Mathf.Max(trauma, Mathf.Clamp01((0.6f + 0.35f * k) * m));
                AddTremor(0.9f * m);
            }
        }

        static float Smooth(float t) { t = Mathf.Clamp01(t); return t * t * (3f - 2f * t); }

        /// <summary>Once per step, as the foot comes down: a jolt through the body into the camera.</summary>
        void Footfall(MoveState state, float sprint01)
        {
            int step = Mathf.FloorToInt(StepPhase / Mathf.PI);
            if (step == lastFootfall) return;
            lastFootfall = step;
            if (state != MoveState.Grounded || BobAmount < 0.2f) return;
            Footstep?.Invoke(sprint01);
            float k = Mathf.Lerp(walkStepWeight, 1f, sprint01);
            if (player.IsCrouching) k *= 0.6f;
            if (weapon && weapon.isActiveAndEnabled) k *= Mathf.Lerp(1f, aimStepWeight, weapon.AimWeight);
            posVelocity.y -= stepDropKick * k;
            rotVelocity.x += stepPitchKick * k;
            rotVelocity.z += Mathf.Sign(Mathf.Cos(StepPhase)) * stepRollKick * k;   // the way the body is about to sway
            AddTremor(stepTremor * k);
        }

        void OnStateChanged(MoveState from, MoveState to)
        {
            if (to == MoveState.Airborne && player.VerticalVelocity > 0f) rotVelocity.x -= 30f;   // push-off
            if (from == MoveState.Vaulting && to == MoveState.Grounded) { posVelocity.y -= 1.2f; trauma = Mathf.Max(trauma, 0.25f); }
            if (from == MoveState.Mantling && to == MoveState.Grounded) posVelocity.y -= 0.6f;
            if (to == MoveState.DiveBack) rotVelocity.x -= 60f;
            if (from == MoveState.DiveBack && to == MoveState.Prone)
            {
                // slammed onto the back
                posVelocity.y -= 2.2f * impactStrength;
                rotVelocity.x -= 70f * impactStrength;
                trauma = Mathf.Max(trauma, 0.4f * impactStrength);
                AddTremor(0.6f * impactStrength);
            }
        }

        static void StepSpring(ref Vector3 value, ref Vector3 velocity, Vector3 target, float k, float d, float dt)
        {
            velocity += ((target - value) * k - velocity * d) * dt;
            value += velocity * dt;
        }

        static float Noise(float s, float t) => Mathf.PerlinNoise(s * 13.7f, t) * 2f - 1f;
    }
}
