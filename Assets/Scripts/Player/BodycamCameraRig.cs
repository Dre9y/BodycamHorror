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

        [Header("Shake")]
        public float traumaDecay = 1.3f;
        public float shakeAngle = 7f;
        public float shakePosition = 0.05f;
        public float shakeFrequency = 18f;

        [Header("Idle breathing")]
        public float breathAmount = 0.006f;
        public float breathAngle = 0.5f;
        public float breathRate = 0.3f;

        /// <summary>Footstep phase in radians (one full cycle = left + right step).</summary>
        public float StepPhase { get; private set; }
        /// <summary>0..1 bob strength this frame.</summary>
        public float BobAmount { get; private set; }

        Vector3 posValue, posVelocity, rotValue, rotVelocity;
        float trauma, stumbleSide = 1f, seed;
        Quaternion followRot;
        bool followInit;

        void Awake()
        {
            seed = Random.value * 100f;
            if (!cam) cam = GetComponentInChildren<Camera>();
            if (!player) player = GetComponentInParent<BodycamPlayerController>();
            if (!weapon && player) weapon = player.GetComponentInChildren<PlayerShooting>();
        }

        /// <summary>Adds camera shake (0..1, decays over time).</summary>
        public void AddTrauma(float amount) => trauma = Mathf.Clamp01(Mathf.Max(trauma, 0f) + amount);

        /// <summary>Kicks the camera springs: positional (m/s) and rotational (deg/s) velocity.</summary>
        public void AddKick(Vector3 positionVelocity, Vector3 rotationVelocity)
        {
            posVelocity += positionVelocity;
            rotVelocity += rotationVelocity;
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

            // Breathing (stronger after sprinting would be a nice later addition)
            float bt = Time.time * breathRate * Mathf.PI * 2f;
            targetPos.y += Mathf.Sin(bt) * breathAmount;
            targetRot.x += Noise(seed, Time.time * 0.3f) * breathAngle;
            targetRot.y += Noise(seed + 7f, Time.time * 0.25f) * breathAngle;

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
                    float a = Mathf.Sin(player.ActionProgress * Mathf.PI);
                    targetRot.x += a * 28f;
                    targetRot.z += a * 12f * stumbleSide;
                    targetPos.y -= a * 0.3f;
                    break;
                }
                case MoveState.Bumped:
                    targetRot.x -= 4f * (1f - player.ActionProgress);
                    break;
            }

            // Springs
            StepSpring(ref posValue, ref posVelocity, targetPos, posStiffness, posDamping, dt);
            StepSpring(ref rotValue, ref rotVelocity, targetRot, rotStiffness, rotDamping, dt);

            // Trauma shake
            trauma = Mathf.Max(0f, trauma - traumaDecay * dt);
            float shake = trauma * trauma;
            float t = Time.time * shakeFrequency;
            Vector3 shakeRot = new Vector3(Noise(seed + 1f, t), Noise(seed + 2f, t), Noise(seed + 3f, t)) * (shakeAngle * shake);
            Vector3 shakePos = new Vector3(Noise(seed + 4f, t), Noise(seed + 5f, t), 0f) * (shakePosition * shake);

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
            transform.localRotation = followLocal * Quaternion.Euler(rotValue + shakeRot);

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
            float k = Mathf.Clamp01((impact - 2f) / 12f);
            posVelocity.y -= Mathf.Lerp(0.5f, 3.5f, k);
            rotVelocity.x += Mathf.Lerp(20f, 160f, k);
            trauma = Mathf.Max(trauma, k * 0.6f);
        }

        void OnImpacted(ImpactKind kind, float speed)
        {
            float k = Mathf.InverseLerp(3f, 9f, speed);
            if (kind == ImpactKind.Wall)
            {
                posVelocity.z -= 1f + 2.5f * k;
                rotVelocity.x -= 120f * k;
                rotVelocity.z += (Random.value < 0.5f ? -1f : 1f) * 90f * k;
                trauma = Mathf.Max(trauma, 0.5f + 0.5f * k);
            }
            else
            {
                stumbleSide = Random.value < 0.5f ? -1f : 1f;
                rotVelocity.x += 150f * k;
                trauma = Mathf.Max(trauma, 0.45f + 0.3f * k);
            }
        }

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
