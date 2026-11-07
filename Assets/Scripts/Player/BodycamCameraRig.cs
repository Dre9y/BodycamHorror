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
        public float baseFov = 88f;
        public float sprintFov = 95f;
        public float slideFov = 100f;
        public float fovSharpness = 6f;

        [Header("Footstep bob")]
        [Tooltip("Meters travelled per left+right step cycle at walk / sprint.")]
        public float strideWalk = 1.6f;
        public float strideSprint = 2.4f;
        public float bobVertical = 0.035f;
        public float bobHorizontal = 0.025f;
        public float bobRoll = 1.1f;
        public float bobPitch = 0.6f;
        public float sprintBobMultiplier = 1.8f;

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
        public float aimFov = 76f;

        [Header("Lean")]
        public float strafeRoll = 1.2f;
        public float brakePitch = 5f;
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
        /// <summary>0..1 bob strength this frame.</summary>
        public float BobAmount { get; private set; }

        Vector3 posValue, posVelocity, rotValue, rotVelocity, kickValue, kickVelocity;
        float trauma, tremor, stumbleSide = 1f, seed, breathPhase;
        Quaternion followRot;
        bool followInit;

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
            float step = Mathf.Abs(s);
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
            if (player.IsBraking) { targetRot.x += brakePitch; targetPos.y -= 0.04f; targetPos.z += 0.03f; }

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
                float targetFov = state == MoveState.Sliding ? slideFov : Mathf.Lerp(baseFov, sprintFov, sprint01);
                if (weapon && weapon.isActiveAndEnabled) targetFov = Mathf.Lerp(targetFov, aimFov, weapon.AimWeight);
                cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, targetFov, 1f - Mathf.Exp(-fovSharpness * dt));
            }
        }

        void OnLanded(float impact)
        {
            if (impact < 2f) return;
            float k = Mathf.Clamp01((impact - 2f) / 10f);
            float m = impactStrength;
            float side = Random.value < 0.5f ? -1f : 1f;
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
            if (kind == ImpactKind.Wall)
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

        void OnStateChanged(MoveState from, MoveState to)
        {
            if (to == MoveState.Airborne && player.VerticalVelocity > 0f) rotVelocity.x -= 30f;   // push-off
            if (from == MoveState.Vaulting && to == MoveState.Grounded) { posVelocity.y -= 1.2f; trauma = Mathf.Max(trauma, 0.25f); }
            if (from == MoveState.Mantling && to == MoveState.Grounded) posVelocity.y -= 0.6f;
        }

        static void StepSpring(ref Vector3 value, ref Vector3 velocity, Vector3 target, float k, float d, float dt)
        {
            velocity += ((target - value) * k - velocity * d) * dt;
            value += velocity * dt;
        }

        static float Noise(float s, float t) => Mathf.PerlinNoise(s * 13.7f, t) * 2f - 1f;
    }
}
