using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BodycamHorror.Player
{
    public enum MoveState { Grounded, Airborne, Sliding, Mantling, Vaulting, Stumbling, Bumped }
    public enum ImpactKind { Wall, Stumble }

    /// <summary>
    /// First-person parkour controller with momentum: at speed the body can't turn on the spot,
    /// it has to brake first. Running into walls bumps you, running into low obstacles trips you.
    /// Moves: walk, sprint, crouch, jump, slide, vault, mantle (climb onto ledges).
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class BodycamPlayerController : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("Child that pitches up/down and carries the camera rig.")]
        public Transform cameraPivot;

        [Header("Look")]
        public float mouseSensitivity = 0.1f;
        public float gamepadLookSpeed = 160f;
        public float maxPitch = 85f;

        [Header("Speeds (m/s)")]
        public float walkSpeed = 3.2f;
        public float sprintSpeed = 7.5f;
        public float crouchSpeed = 1.8f;

        [Header("Inertia")]
        [Tooltip("Acceleration at walking pace.")]
        public float walkAcceleration = 10f;
        [Tooltip("Acceleration near top sprint speed: getting fully up to speed takes time.")]
        public float sprintAcceleration = 4.5f;
        [Tooltip("Deceleration when trying to turn sharply or reverse at speed.")]
        public float brakeDeceleration = 16f;
        [Tooltip("Deceleration with no input.")]
        public float stopFriction = 9f;
        [Tooltip("How fast the movement direction can turn at walking speed (deg/s).")]
        public float turnRateWalk = 600f;
        [Tooltip("How fast the movement direction can turn at full sprint (deg/s).")]
        public float turnRateSprint = 80f;
        [Tooltip("Above this angle between current motion and desired direction the body brakes instead of turning.")]
        public float sharpTurnAngle = 65f;
        [Range(0f, 1f)] public float airControl = 0.2f;

        [Header("Jump & Gravity")]
        public float jumpHeight = 1.1f;
        public float gravity = -22f;
        public float coyoteTime = 0.12f;
        public float jumpBufferTime = 0.15f;
        [Tooltip("Landing faster than this (m/s) costs speed.")]
        public float hardLandingSpeed = 11f;

        [Header("Body Heights")]
        public float standHeight = 1.8f;
        public float crouchHeight = 1.1f;
        public float slideHeight = 0.8f;
        public float eyeOffsetFromTop = 0.22f;
        public float heightChangeSpeed = 7f;

        [Header("Slide")]
        public float slideMinSpeed = 5f;
        public float slideBoost = 1.2f;
        public float slideFriction = 3.2f;
        public float slideMaxTime = 1.2f;
        public float slideTurnRate = 40f;

        [Header("Mantle (climb onto ledges)")]
        public float mantleMinHeight = 0.55f;
        public float mantleMaxHeight = 2.1f;
        [Tooltip("Max ledge height above the feet when grabbing in mid-air.")]
        public float airMantleMaxHeight = 1.7f;
        public float mantleReach = 0.7f;
        public float mantleBaseDuration = 0.35f;
        public float mantleDurationPerMeter = 0.22f;

        [Header("Vault (over low obstacles)")]
        public float vaultMinHeight = 0.4f;
        public float vaultMaxHeight = 1.25f;
        public float vaultMaxThickness = 1.0f;
        public float vaultMinSpeed = 3.5f;

        [Header("Impacts")]
        public float wallBumpMinSpeed = 4.5f;
        public float stumbleMinSpeed = 4f;
        [Tooltip("Obstacles up to this height (above the feet) trip you instead of stopping you.")]
        public float stumbleMaxObstacleHeight = 0.65f;
        public float bumpStunTime = 0.6f;
        public float stumbleTime = 0.8f;

        [Header("Collision")]
        [Tooltip("Geometry used for ledge/obstacle probes. The player itself sits on Ignore Raycast.")]
        public LayerMask environmentMask = ~(1 << 2);
        public float respawnBelowY = -30f;

        public event Action<float> Landed;                 // impact speed (m/s)
        public event Action<ImpactKind, float> Impacted;   // kind, speed into the obstacle
        public event Action<MoveState, MoveState> StateChanged;

        public MoveState State { get; private set; } = MoveState.Grounded;
        public Vector3 HorizontalVelocity => hVel;
        public float VerticalVelocity => vVel;
        public float Speed => hVel.magnitude;
        public float SprintBlend => Mathf.InverseLerp(walkSpeed, sprintSpeed, hVel.magnitude);
        public bool IsBraking { get; private set; }
        public bool IsCrouching => crouching;
        public Vector2 LookVelocity { get; private set; }  // deg/s: x = yaw, y = pitch (down positive)
        public float StateTime => stateTimer;
        public float ActionProgress { get; private set; }  // 0..1 during mantle/vault/stun
        /// <summary>What the player last ran into (for impact sounds, damage, etc.).</summary>
        public Collider LastImpactCollider { get; private set; }
        public Vector3 LastImpactPoint { get; private set; }

        CharacterController cc;
        InputAction moveAction, lookAction, lookStickAction, jumpAction, sprintAction, crouchAction;

        Vector3 hVel;
        float vVel;
        float yaw, pitch;
        float lastGroundedTime = -10f, lastJumpPressTime = -10f;
        float stateTimer, impactCooldown, stunDuration, pendingLift, standStepOffset;
        bool groundedLast = true, jumpConsumed, crouching, impactDuringMove;
        Vector3 spawnPosition; Quaternion spawnRotation;

        // scripted moves (mantle / vault)
        Vector3 moveFrom, moveCtrl, moveTo, exitVelocity;
        float moveDuration;

        Vector2 moveInput;
        bool sprintHeld, crouchHeld, crouchPressed, jumpHeld;

        void Awake()
        {
            cc = GetComponent<CharacterController>();
            standStepOffset = cc.stepOffset;
            cc.height = standHeight;
            cc.center = Vector3.up * (standHeight * 0.5f);
            yaw = transform.eulerAngles.y;
            spawnPosition = transform.position;
            spawnRotation = transform.rotation;
            CreateInput();
        }

        void CreateInput()
        {
            moveAction = new InputAction("Move", InputActionType.Value);
            moveAction.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w").With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a").With("Right", "<Keyboard>/d");
            moveAction.AddBinding("<Gamepad>/leftStick");

            lookAction = new InputAction("Look", InputActionType.Value, "<Mouse>/delta");
            lookStickAction = new InputAction("LookStick", InputActionType.Value, "<Gamepad>/rightStick");

            jumpAction = new InputAction("Jump", InputActionType.Button, "<Keyboard>/space");
            jumpAction.AddBinding("<Gamepad>/buttonSouth");

            sprintAction = new InputAction("Sprint", InputActionType.Button, "<Keyboard>/leftShift");
            sprintAction.AddBinding("<Gamepad>/leftStickPress");

            crouchAction = new InputAction("Crouch", InputActionType.Button, "<Keyboard>/leftCtrl");
            crouchAction.AddBinding("<Keyboard>/c");
            crouchAction.AddBinding("<Gamepad>/buttonEast");
        }

        void OnEnable()
        {
            moveAction.Enable(); lookAction.Enable(); lookStickAction.Enable();
            jumpAction.Enable(); sprintAction.Enable(); crouchAction.Enable();
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        void OnDisable()
        {
            moveAction.Disable(); lookAction.Disable(); lookStickAction.Disable();
            jumpAction.Disable(); sprintAction.Disable(); crouchAction.Disable();
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        void OnDestroy()
        {
            moveAction?.Dispose(); lookAction?.Dispose(); lookStickAction?.Dispose();
            jumpAction?.Dispose(); sprintAction?.Dispose(); crouchAction?.Dispose();
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            ReadInput();
            HandleCursor();
            Look(dt);

            stateTimer += dt;
            impactCooldown -= dt;
            IsBraking = false;

            switch (State)
            {
                case MoveState.Grounded:
                case MoveState.Airborne: UpdateLocomotion(dt); break;
                case MoveState.Sliding: UpdateSlide(dt); break;
                case MoveState.Mantling:
                case MoveState.Vaulting: UpdateScriptedMove(); break;
                case MoveState.Stumbling:
                case MoveState.Bumped: UpdateStunned(dt); break;
            }

            UpdateHeight(dt);

            if (transform.position.y < respawnBelowY) Respawn();
        }

        // ------------------------------------------------------------------ input & look

        void ReadInput()
        {
            moveInput = Vector2.ClampMagnitude(moveAction.ReadValue<Vector2>(), 1f);
            sprintHeld = sprintAction.IsPressed();
            crouchHeld = crouchAction.IsPressed();
            crouchPressed = crouchAction.WasPressedThisFrame();
            jumpHeld = jumpAction.IsPressed();
            if (jumpAction.WasPressedThisFrame()) lastJumpPressTime = Time.time;
        }

        void HandleCursor()
        {
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            else if (mouse != null && mouse.leftButton.wasPressedThisFrame && Cursor.lockState != CursorLockMode.Locked)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }

        void Look(float dt)
        {
            Vector2 mouse = Cursor.lockState == CursorLockMode.Locked ? lookAction.ReadValue<Vector2>() * mouseSensitivity : Vector2.zero;
            Vector2 d = mouse + lookStickAction.ReadValue<Vector2>() * (gamepadLookSpeed * dt);
            if (State == MoveState.Stumbling || State == MoveState.Bumped) d *= 0.35f;

            yaw += d.x;
            float newPitch = Mathf.Clamp(pitch - d.y, -maxPitch, maxPitch);
            LookVelocity = new Vector2(d.x, newPitch - pitch) / dt;
            pitch = newPitch;

            transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            if (cameraPivot) cameraPivot.localRotation = Quaternion.Euler(pitch, 0f, 0f);
        }

        // ------------------------------------------------------------------ locomotion

        void UpdateLocomotion(float dt)
        {
            bool grounded = groundedLast;
            Vector3 wish = transform.right * moveInput.x + transform.forward * moveInput.y;
            float inputMag = Mathf.Min(1f, wish.magnitude);
            Vector3 wishDir = inputMag > 0.01f ? wish.normalized : Vector3.zero;

            if (grounded)
            {
                if (crouchPressed && hVel.magnitude >= slideMinSpeed) { StartSlide(); return; }
                if (crouchHeld) crouching = true;
                else if (crouching && CanStandUp()) crouching = false;
            }

            bool sprinting = sprintHeld && moveInput.y > 0.3f && !crouching;
            float target = (crouching ? crouchSpeed : sprinting ? sprintSpeed : walkSpeed) * inputMag;
            Steer(wishDir, target, dt, grounded ? 1f : airControl, grounded);

            // jump / vault / mantle
            bool jumpBuffered = Time.time - lastJumpPressTime <= jumpBufferTime;
            bool canJump = !jumpConsumed && Time.time - lastGroundedTime <= coyoteTime;
            if (jumpBuffered)
            {
                if (TryStartVaultOrMantle(canJump)) { lastJumpPressTime = -10f; return; }
                if (canJump)
                {
                    lastJumpPressTime = -10f;
                    if (crouching && CanStandUp()) crouching = false;
                    Jump(1f);
                }
            }
            else if (!grounded && (jumpHeld || moveInput.y > 0.5f) && vVel < 2f)
            {
                // Grab ledges in the air while holding jump or pushing forward into them.
                if (TryStartVaultOrMantle(false)) return;
            }

            bool nowGrounded = MoveCharacter(dt);
            if (State == MoveState.Grounded && !nowGrounded) SetState(MoveState.Airborne);
            else if (State == MoveState.Airborne && nowGrounded) SetState(MoveState.Grounded);
        }

        void Jump(float strength)
        {
            vVel = Mathf.Sqrt(2f * jumpHeight * -gravity) * strength;
            jumpConsumed = true;
            groundedLast = false;
            SetState(MoveState.Airborne);
        }

        /// <summary>Momentum steering: the velocity vector turns at a limited rate, and sharp
        /// direction changes at speed brake first.</summary>
        void Steer(Vector3 wishDir, float targetSpeed, float dt, float control, bool grounded)
        {
            float speed = hVel.magnitude;

            if (targetSpeed < 0.05f)
            {
                if (grounded) hVel = Vector3.MoveTowards(hVel, Vector3.zero, stopFriction * dt);
                return;
            }

            if (speed < 0.3f)
            {
                hVel = Vector3.MoveTowards(hVel, wishDir * targetSpeed, walkAcceleration * control * dt);
                return;
            }

            Vector3 dir = hVel / speed;
            float angle = Vector3.Angle(dir, wishDir);
            float sprint01 = Mathf.InverseLerp(walkSpeed, sprintSpeed, speed);

            if (grounded && angle > sharpTurnAngle && speed > walkSpeed + 0.3f)
            {
                // Too fast to turn this hard: plant the feet and brake.
                IsBraking = true;
                speed = Mathf.MoveTowards(speed, 0f, brakeDeceleration * dt);
            }
            else
            {
                float turnRate = Mathf.Lerp(turnRateWalk, turnRateSprint, sprint01) * control;
                dir = Vector3.RotateTowards(dir, wishDir, turnRate * Mathf.Deg2Rad * dt, 0f);

                float accel;
                if (speed < targetSpeed) accel = Mathf.Lerp(walkAcceleration, sprintAcceleration, sprint01);
                else accel = grounded ? stopFriction : 0f;   // keep momentum in the air
                speed = Mathf.MoveTowards(speed, targetSpeed, accel * control * dt);
            }

            hVel = dir * speed;
        }

        /// <summary>Applies gravity, moves the controller and tracks landing. Returns grounded.</summary>
        bool MoveCharacter(float dt)
        {
            vVel += gravity * dt;
            float preVVel = vVel;
            impactDuringMove = false;
            CollisionFlags flags = cc.Move((hVel + Vector3.up * vVel) * dt);
            bool grounded = (flags & CollisionFlags.Below) != 0;

            if ((flags & CollisionFlags.Above) != 0 && vVel > 0f) vVel = 0f;

            if (impactDuringMove)
            {
                // A bump/trip started inside this Move: keep the velocities it set.
                groundedLast = grounded && State != MoveState.Stumbling;
                return grounded;
            }

            if (grounded)
            {
                if (!groundedLast)
                {
                    float impact = -preVVel;
                    if (impact > hardLandingSpeed) hVel *= 0.5f;
                    Landed?.Invoke(impact);
                }
                vVel = -4f;   // keep snapped to slopes and steps
                lastGroundedTime = Time.time;
                jumpConsumed = false;
            }

            groundedLast = grounded;
            return grounded;
        }

        // ------------------------------------------------------------------ slide

        void StartSlide()
        {
            SetState(MoveState.Sliding);
            crouching = true;
            float speed = hVel.magnitude;
            hVel = hVel / Mathf.Max(speed, 0.001f) * Mathf.Min(speed + slideBoost, sprintSpeed + 2f);
        }

        void UpdateSlide(float dt)
        {
            float speed = Mathf.MoveTowards(hVel.magnitude, 0f, slideFriction * dt);
            Vector3 dir = hVel.sqrMagnitude > 0.0001f ? hVel.normalized : transform.forward;
            Vector3 wish = transform.right * moveInput.x + transform.forward * moveInput.y;
            if (wish.sqrMagnitude > 0.01f)
                dir = Vector3.RotateTowards(dir, wish.normalized, slideTurnRate * Mathf.Deg2Rad * dt, 0f);
            hVel = dir * speed;

            if (Time.time - lastJumpPressTime <= jumpBufferTime && CanStandUp())
            {
                lastJumpPressTime = -10f;
                crouching = false;
                Jump(0.9f);   // slide-jump keeps the momentum
                MoveCharacter(dt);
                return;
            }

            bool grounded = MoveCharacter(dt);
            if (!grounded) { SetState(MoveState.Airborne); return; }

            bool released = !crouchHeld && stateTimer > 0.35f && CanStandUp();
            if (speed < crouchSpeed + 0.3f || stateTimer > slideMaxTime || released)
            {
                crouching = crouchHeld || !CanStandUp();
                SetState(MoveState.Grounded);
            }
        }

        // ------------------------------------------------------------------ vault & mantle

        bool TryStartVaultOrMantle(bool fromGround)
        {
            Vector3 feet = transform.position;
            Vector3 fwd = transform.forward;
            // At speed the button is pressed earlier, so look further ahead.
            float reach = cc.radius + mantleReach + Mathf.Max(0f, Vector3.Dot(hVel, fwd)) * 0.09f;

            RaycastHit wall = default;
            bool found = false;
            for (float y = 0.3f; y <= 1.55f && !found; y += 0.4f)
                found = Physics.Raycast(feet + Vector3.up * y, fwd, out wall, reach, environmentMask, QueryTriggerInteraction.Ignore)
                        && wall.normal.y < 0.3f;
            if (!found) return false;

            Vector3 wallN = Flat(wall.normal);
            if (wallN.sqrMagnitude < 0.0001f) return false;
            wallN.Normalize();
            Vector3 inward = -wallN;

            float maxH = fromGround ? mantleMaxHeight : airMantleMaxHeight;
            Vector3 topOrigin = new Vector3(wall.point.x, feet.y + maxH + 0.4f, wall.point.z) + inward * 0.2f;
            if (Physics.CheckSphere(topOrigin, 0.05f, environmentMask, QueryTriggerInteraction.Ignore)) return false; // too tall
            if (!Physics.Raycast(topOrigin, Vector3.down, out RaycastHit top, maxH + 0.4f, environmentMask, QueryTriggerInteraction.Ignore)
                || top.normal.y < 0.7f) return false;

            float height = top.point.y - feet.y;
            float speedIn = Vector3.Dot(hVel, inward);

            // Vault: low, thin obstacle taken at speed.
            if (fromGround && height >= vaultMinHeight && height <= vaultMaxHeight && speedIn >= vaultMinSpeed)
            {
                float probeY = top.point.y - 0.08f;
                Vector3 farOrigin = new Vector3(wall.point.x, probeY, wall.point.z) + inward * (vaultMaxThickness + 0.3f);
                if (!Physics.CheckSphere(farOrigin, 0.05f, environmentMask, QueryTriggerInteraction.Ignore)
                    && Physics.Raycast(farOrigin, wallN, out RaycastHit back, vaultMaxThickness + 0.3f, environmentMask, QueryTriggerInteraction.Ignore))
                {
                    Vector3 land = back.point + inward * (cc.radius + 0.4f);
                    Vector3 landOrigin = new Vector3(land.x, top.point.y + 0.2f, land.z);
                    if (Physics.Raycast(landOrigin, Vector3.down, out RaycastHit ground, height + 1.7f, environmentMask, QueryTriggerInteraction.Ignore)
                        && ground.normal.y > 0.7f && CapsuleFree(ground.point, standHeight))
                    {
                        StartVault(feet, top.point, ground.point, speedIn, inward);
                        return true;
                    }
                }
            }

            // Mantle: pull up onto the ledge.
            float minH = fromGround ? mantleMinHeight : 0.2f;
            if (height < minH || height > maxH) return false;

            Vector3 standXZ = top.point + inward * cc.radius;
            if (!Physics.Raycast(new Vector3(standXZ.x, top.point.y + 0.3f, standXZ.z), Vector3.down, out RaycastHit standHit, 0.6f,
                    environmentMask, QueryTriggerInteraction.Ignore) || standHit.normal.y < 0.7f) return false;
            Vector3 standPos = standHit.point;
            if (!CapsuleFree(standPos, crouchHeight)) return false;

            StartMantle(standPos);
            return true;
        }

        void StartMantle(Vector3 standPos)
        {
            SetState(MoveState.Mantling);
            cc.enabled = false;
            moveFrom = transform.position;
            moveCtrl = new Vector3(moveFrom.x, standPos.y + 0.25f, moveFrom.z);   // up first, then over
            moveTo = standPos;
            moveDuration = mantleBaseDuration + mantleDurationPerMeter * Mathf.Max(0f, standPos.y - moveFrom.y);
            exitVelocity = Flat(standPos - moveFrom).normalized * (walkSpeed * 0.6f);
            crouching = !CapsuleFree(standPos, standHeight);
            hVel = Vector3.zero;
            vVel = 0f;
        }

        void StartVault(Vector3 feet, Vector3 topPoint, Vector3 landPos, float speed, Vector3 inward)
        {
            SetState(MoveState.Vaulting);
            cc.enabled = false;
            moveFrom = feet;
            moveTo = landPos;
            // Quadratic Bezier peak = 0.5*ctrl + 0.25*(a+b); put the feet 0.12 m above the obstacle top.
            float peak = topPoint.y + 0.12f;
            Vector3 mid = (feet + landPos) * 0.5f;
            moveCtrl = new Vector3(mid.x, 2f * peak - (feet.y + landPos.y) * 0.5f, mid.z);
            moveDuration = Mathf.Clamp(Vector3.Distance(feet, landPos) / Mathf.Max(speed, 4f), 0.3f, 0.7f);
            exitVelocity = inward * (speed * 0.9f);
            hVel = exitVelocity;
            vVel = 0f;
            crouching = false;
        }

        void UpdateScriptedMove()
        {
            float t = Mathf.Clamp01(stateTimer / moveDuration);
            ActionProgress = t;
            float e = State == MoveState.Mantling ? t * t * (3f - 2f * t) : t;
            transform.position = Bezier(moveFrom, moveCtrl, moveTo, e);

            if (t >= 1f)
            {
                cc.enabled = true;
                hVel = exitVelocity;
                vVel = -4f;
                groundedLast = true;
                lastGroundedTime = Time.time;
                jumpConsumed = false;
                SetState(MoveState.Grounded);
            }
        }

        // ------------------------------------------------------------------ impacts

        void OnControllerColliderHit(ControllerColliderHit hit)
        {
            if (State == MoveState.Mantling || State == MoveState.Vaulting) return;
            if (State == MoveState.Stumbling && (pendingLift > 0f || stateTimer < 0.1f)) return;   // let the trip carry us over
            Vector3 n = hit.normal;
            if (n.y > 0.5f || n.y < -0.5f) return;   // floor or ceiling
            Vector3 flatN = Flat(n);
            if (flatN.sqrMagnitude < 0.0001f) return;
            flatN.Normalize();

            float into = -Vector3.Dot(hVel, flatN);
            if (into <= 0f) return;

            if (impactCooldown <= 0f && (State == MoveState.Grounded || State == MoveState.Airborne || State == MoveState.Sliding))
            {
                LastImpactCollider = hit.collider;
                LastImpactPoint = hit.point;
                float top = MeasureObstacleTop(hit.point, flatN);
                bool low = top <= stumbleMaxObstacleHeight && top > cc.stepOffset + 0.05f;

                if (low && State == MoveState.Grounded && into >= stumbleMinSpeed) { StartStumble(into, top); return; }
                if (!low && top > cc.stepOffset + 0.05f && into >= wallBumpMinSpeed) { StartBump(flatN, into); return; }
            }

            hVel += flatN * into;   // drop the into-wall part, keep sliding along it
        }

        float MeasureObstacleTop(Vector3 point, Vector3 flatN)
        {
            float feetY = transform.position.y;
            Vector3 origin = new Vector3(point.x, feetY + standHeight + 0.5f, point.z) - flatN * 0.08f;
            if (Physics.CheckSphere(origin, 0.02f, environmentMask, QueryTriggerInteraction.Ignore)) return 999f;
            if (Physics.Raycast(origin, Vector3.down, out RaycastHit h, standHeight + 0.6f, environmentMask, QueryTriggerInteraction.Ignore))
                return h.point.y - feetY;
            return 999f;
        }

        void StartStumble(float into, float obstacleHeight)
        {
            SetState(MoveState.Stumbling);
            stunDuration = stumbleTime;
            crouching = false;
            hVel *= 0.6f;
            vVel = 2.5f;
            pendingLift = obstacleHeight + 0.05f;   // feet are lifted over the edge next frame: tripped over it, not through it
            groundedLast = false;
            impactCooldown = 0.6f;
            impactDuringMove = true;
            Impacted?.Invoke(ImpactKind.Stumble, into);
        }

        void StartBump(Vector3 flatN, float into)
        {
            SetState(MoveState.Bumped);
            float k = Mathf.InverseLerp(wallBumpMinSpeed, sprintSpeed + 1f, into);
            stunDuration = Mathf.Lerp(0.25f, bumpStunTime, k);
            hVel += flatN * (into * 1.2f);   // stop, and bounce back a little
            impactCooldown = 0.6f;
            impactDuringMove = true;
            Impacted?.Invoke(ImpactKind.Wall, into);
        }

        void UpdateStunned(float dt)
        {
            ActionProgress = Mathf.Clamp01(stateTimer / Mathf.Max(stunDuration, 0.01f));
            if (pendingLift > 0f)
            {
                cc.Move(Vector3.up * pendingLift);
                pendingLift = 0f;
            }
            float friction = State == MoveState.Stumbling ? (groundedLast ? 7f : 1f) : 12f;
            hVel = Vector3.MoveTowards(hVel, Vector3.zero, friction * dt);
            bool grounded = MoveCharacter(dt);
            if (stateTimer >= stunDuration && grounded) SetState(MoveState.Grounded);
        }

        // ------------------------------------------------------------------ body height

        void UpdateHeight(float dt)
        {
            if (!cc.enabled) return;
            float target = State == MoveState.Sliding ? slideHeight : crouching ? crouchHeight : standHeight;
            float h = Mathf.MoveTowards(cc.height, target, heightChangeSpeed * dt);
            cc.height = h;
            cc.center = Vector3.up * (h * 0.5f);
            // The controller lifts itself by stepOffset on every move; keep it low when ducking
            // or the head clips things the body should pass under.
            cc.stepOffset = State == MoveState.Sliding ? 0.05f : crouching ? Mathf.Min(standStepOffset, 0.2f) : standStepOffset;
            if (cameraPivot) cameraPivot.localPosition = new Vector3(0f, h - eyeOffsetFromTop, 0f);
        }

        bool CanStandUp() => CapsuleFree(transform.position, standHeight, Mathf.Max(cc.height - cc.radius, cc.radius + 0.05f));

        bool CapsuleFree(Vector3 feet, float height, float bottomOffset = -1f)
        {
            float r = cc.radius;
            float bottom = bottomOffset >= 0f ? bottomOffset : r + 0.05f;
            Vector3 a = feet + Vector3.up * bottom;
            Vector3 b = feet + Vector3.up * Mathf.Max(bottom, height - r);
            return !Physics.CheckCapsule(a, b, r * 0.95f, environmentMask, QueryTriggerInteraction.Ignore);
        }

        // ------------------------------------------------------------------ helpers

        void SetState(MoveState s)
        {
            if (State == s) return;
            MoveState prev = State;
            State = s;
            stateTimer = 0f;
            ActionProgress = 0f;
            StateChanged?.Invoke(prev, s);
        }

        /// <summary>Teleports the player and sets the view direction, clearing all momentum.</summary>
        public void Teleport(Vector3 feetPosition, float newYaw, float newPitch = 0f)
        {
            cc.enabled = false;
            transform.position = feetPosition;
            cc.enabled = true;
            yaw = newYaw;
            pitch = Mathf.Clamp(newPitch, -maxPitch, maxPitch);
            transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            if (cameraPivot) cameraPivot.localRotation = Quaternion.Euler(pitch, 0f, 0f);
            hVel = Vector3.zero;
            vVel = 0f;
            SetState(MoveState.Airborne);
        }

        void Respawn()
        {
            cc.enabled = false;
            transform.SetPositionAndRotation(spawnPosition, spawnRotation);
            yaw = spawnRotation.eulerAngles.y;
            pitch = 0f;
            cc.enabled = true;
            hVel = Vector3.zero;
            vVel = 0f;
            SetState(MoveState.Airborne);
        }

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        static Vector3 Bezier(Vector3 a, Vector3 b, Vector3 c, float t)
        {
            float u = 1f - t;
            return u * u * a + 2f * u * t * b + t * t * c;
        }
    }
}
