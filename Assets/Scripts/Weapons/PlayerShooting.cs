using System;
using BodycamHorror.Combat;
using BodycamHorror.Player;
using UnityEngine;
using UnityEngine.InputSystem;
using Random = UnityEngine.Random;

namespace BodycamHorror.Weapons
{
    /// <summary>
    /// What the hands do for the bodycam view. Sits on a holder under CameraPivot and poses the current
    /// gun in pivot space (ready, aimed, lowered while sprinting, tucked against walls, carried during
    /// parkour), adds recoil, breathing, bob and look inertia, switches weapons and does melee.
    /// The hands lead: <see cref="BodycamCameraRig"/> follows <see cref="AimOffset"/> a moment later.
    /// Slots: 1..N = <see cref="weapons"/>, N+1 = bare hands. With <see cref="startUnarmed"/> none of them is
    /// there until picked up (<see cref="Give"/>). LMB / RT fire (punch with bare hands),
    /// RMB / LT aim, R / X reload, V / R3 melee, 1-3 / wheel / Y switch.
    /// </summary>
    [DefaultExecutionOrder(-100)]   // read input before the controller re-locks the cursor; pose before the camera rig
    public class PlayerShooting : MonoBehaviour
    {
        enum SwitchPhase { None, Lowering, Raising }
        enum ReloadPhase { None, Enter, Loading, Exit }
        enum MeleeKind { None, PunchLeft, PunchRight, Jab, Bash }

        [Header("References")]
        public BodycamPlayerController player;
        public BodycamCameraRig rig;
        [Tooltip("Guns in slot order (keys 1, 2, ...). The slot after the last gun is bare hands.")]
        public Firearm[] weapons = new Firearm[0];
        public int startSlot = 0;
        [Tooltip("Start with nothing at all: no guns and no fists either. Each gun has to be picked up (WeaponPickup, F); the fists come with the first one.")]
        public bool startUnarmed;

        [Header("Shared poses (pivot space; gun origin = right-hand grip)")]
        [Tooltip("The barrel points at this distance straight ahead of the eye when settled.")]
        public float convergenceDistance = 20f;
        public Vector3 loweredPosition = new Vector3(0.10f, -0.34f, 0.31f);
        public Vector3 loweredEuler = new Vector3(42f, -18f, 14f);
        public Vector3 tuckPosition = new Vector3(0.09f, -0.13f, 0.2f);
        public Vector3 tuckEuler = new Vector3(-55f, -12f, 8f);
        [Tooltip("Where a gun goes while it is being put away / drawn.")]
        public Vector3 holsterPosition = new Vector3(0.14f, -0.55f, 0.12f);
        public Vector3 holsterEuler = new Vector3(70f, -25f, 35f);

        [Header("Blending (1/s)")]
        public float aimSpeed = 11f;
        public float lowerSpeed = 7f;
        public float carrySpeed = 16f;
        [Tooltip("Moves the arms while climbing a ledge (weapon-holder space), so they reach for it from the shoulders and not from under the frame.")]
        public Vector3 climbArmsOffset = new Vector3(0f, 0.17f, 0.1f);
        [Tooltip("Moves just the left shoulder while climbing, so the elbow has room to bend.")]
        public Vector3 climbLeftShoulderOffset = new Vector3(-0.06f, 0f, 0f);
        public float tuckSpeed = 14f;
        public float twoHandSpeed = 13f;
        public float guardSpeed = 10f;
        [Tooltip("After firing, aiming or a melee strike the hands stay up (no sprint lowering) for this long.")]
        public float raiseHoldTime = 0.8f;
        [Tooltip("A trigger pull this early is kept until the gun is ready.")]
        public float fireBufferTime = 0.3f;
        [Range(0.2f, 1f)] public float aimMoveSpeedScale = 0.7f;

        [Header("Bare hands")]
        [Tooltip("The first-person arms for the melee slot. Empty = the old procedural arms.")]
        public BareHands bareHandsPrefab;

        [Header("Switching")]
        public float switchLowerTime = 0.2f;
        public float switchRaiseTime = 0.28f;

        [Header("Wall tuck")]
        public float tuckProbeRadius = 0.07f;

        [Header("Recoil springs")]
        [Range(0f, 1f)] public float aimRecoilScale = 0.6f;
        public float oneHandRecoilScale = 1.5f;
        public float recoilRotStiffness = 300f;
        public float recoilRotDamping = 24f;
        public float recoilPosStiffness = 380f;
        public float recoilPosDamping = 28f;

        [Header("Melee")]
        public float meleeRange = 1.7f;
        public float meleeRadius = 0.22f;
        public LayerMask meleeMask = ~(1 << 2);
        [Tooltip("Bare-hand punch damage and push.")]
        public float punchDamage = 25f;
        public float punchForce = 4f;
        public float punchTime = 0.34f;
        [Tooltip("Left-hand jab while holding a one-handed-melee gun (pistol).")]
        public float jabTime = 0.38f;
        [Tooltip("Strike with the gun itself (shotgun).")]
        public float bashTime = 0.46f;
        [Tooltip("Point of the swing (0..1) where it connects.")]
        [Range(0.1f, 0.9f)] public float meleeHitAt = 0.38f;
        [Tooltip("Used for melee dust when no gun is around to lend its material.")]
        public Material impactMaterial;
        [Tooltip("Strikes in a row before the arms need a breather.")]
        public int meleeCharges = 2;
        [Tooltip("Seconds without striking to get all strikes back (and the lock-out once they are used up).")]
        public float meleeRechargeTime = 3f;
        [Tooltip("Push speed (m/s) given to whatever the strike catches.")]
        public float meleePushStrength = 3.2f;
        [Tooltip("Strikes shove everything pushable in this cone in front (deg, full width).")]
        public float meleePushArc = 100f;

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

        [Header("Free aim (aiming down the sights)")]
        [Tooltip("While aiming, the look input first swings the gun this far up or down from the middle of the view (degrees); "
                 + "the view only starts to turn once the gun is at that edge. The shots go where the gun points. 0 = the gun stays centred.")]
        public float freeAimRadius = 6f;
        [Tooltip("The same sideways: wider, so the gun can sweep across a doorway before the view starts to turn.")]
        public float freeAimHorizontalRadius = 12f;

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

        [Header("Spread on top of the gun's own (degrees)")]
        public float moveSpread = 1.4f;
        public float airSpread = 2.5f;
        public float oneHandSpread = 1f;

        [Header("Sway")]
        public float breathSway = 0.35f;
        public float aimBreathScale = 0.5f;
        public float bobPosition = 0.012f;
        public float strafeRoll = 1.5f;
        [Tooltip("Share of the camera's step motion the hands follow, so they do not jump up the frame on every footfall.")]
        [Range(0f, 1f)] public float hipFollowBody = 0.85f;
        [Tooltip("The same when aiming: the sights stay put in the frame.")]
        [Range(0f, 1f)] public float aimFollowBody = 0.9f;
        [Tooltip("How far the gun sinks when walking while aiming, to keep it out of the view (m).")]
        public float aimWalkDrop = 0.012f;

        /// <summary>Weapon rotation away from the settled aim (recoil + breathing), degrees, pivot space.</summary>
        public Vector3 AimOffset { get; private set; }
        /// <summary>0..1: how much the camera should follow the gun right now (only while it is up).</summary>
        public float CameraFollowWeight { get; private set; }
        /// <summary>How far the arms are moved right now because of climbing (weapon-holder space).</summary>
        public Vector3 ClimbArmsShift => climbArmsOffset * Smooth(climbW) + StumbleArmsShift;

        /// <summary>A trip throws the camera down past the shoulders: they go with it, or the upper arms fill the frame.</summary>
        Vector3 StumbleArmsShift
        {
            get
            {
                if (!rig || stumbleW <= 0f) return Vector3.zero;
                Vector3 shoulders = new Vector3(0f, -0.25f, -0.06f);
                return (rig.BodyPosition + rig.BodyRotation * shoulders - shoulders) * Smooth(stumbleW);
            }
        }
        /// <summary>How far the left shoulder is moved right now because of climbing.</summary>
        public Vector3 ClimbLeftShoulderShift => climbLeftShoulderOffset * Smooth(climbW);
        /// <summary>0..1: how far into the climbing pose the arms are.</summary>
        public float ClimbWeight => Smooth(climbW);
        public float AimWeight => aimW;
        public float TwoHandWeight => twoHandW;
        public bool IsReloading => reloadPhase != ReloadPhase.None;
        public bool ReloadFromEmpty => reloadFromEmpty;
        /// <summary>0 not reloading, 1 getting into position, 2 loading, 3 finishing.</summary>
        public int ReloadStage => reloadPhase == ReloadPhase.None ? 0 : reloadPhase == ReloadPhase.Enter ? 1 : reloadPhase == ReloadPhase.Loading ? 2 : 3;
        /// <summary>Seconds into the current reload stage (for loading, into the current shell).</summary>
        public float ReloadStageTime => reloadPhase == ReloadPhase.Loading ? shellTimer : reloadTimer;
        /// <summary>Shells fully loaded since this reload began.</summary>
        public int ShellsLoaded => shellsLoaded;
        /// <summary>The gun is being put away right now (the draw of the next one has not started).</summary>
        public bool IsPuttingAway => switchPhase == SwitchPhase.Lowering;
        /// <summary>Sprinting with the gun not needed: it should be carried low.</summary>
        public bool WantsLowered => player && player.IsSprinting && raiseTimer <= 0f;
        public float ReloadProgress => reloadPhase == ReloadPhase.Loading && Current && Current.reloadStyle == Firearm.ReloadStyle.Magazine
            ? Mathf.Clamp01(reloadTimer / reloadDuration) : 0f;
        public bool IsCarrying => carryW > 0.5f;
        /// <summary>The gun in the hands right now (null for bare hands or between guns).</summary>
        public Firearm CurrentWeapon => shown;
        public int Slot => slot;
        public int SlotCount => (weapons?.Length ?? 0) + 1;
        public int Ammo => shown ? shown.Ammo : 0;
        public bool IsSwitching => switchPhase != SwitchPhase.None;
        /// <summary>Bare hands: how far up the fists are (0 = relaxed / running, 1 = guard).</summary>
        public float GuardWeight => shown || !HasFists ? 0f : guardW * (1f - switchW);
        /// <summary>Hand throwing a strike right now: -1 left, +1 right, 0 none.</summary>
        public int PunchHand => meleeKind switch { MeleeKind.PunchLeft => -1, MeleeKind.Jab => -1, MeleeKind.PunchRight => 1, _ => 0 };
        /// <summary>0..1 extension of the current punch / jab.</summary>
        public float PunchExtend => PunchHand != 0 ? MeleeCurve() : 0f;
        /// <summary>Melee strikes left before the recharge.</summary>
        public int MeleeChargesLeft => meleeChargesLeft;

        public event Action Fired;
        /// <summary>A melee strike was thrown (true if it connected).</summary>
        public event Action<bool> Meleed;

        /// <summary>Nothing picked up yet: no gun, and the fists are not there to use either.</summary>
        public bool IsUnarmed => !shown && !HasFists;
        /// <summary>The bare-hands slot is there to use.</summary>
        public bool HasFists => owned == null || owned[owned.Length - 1];

        Firearm Current => slot < (weapons?.Length ?? 0) ? weapons[slot] : null;

        bool[] owned;   // per slot, the bare-hands slot last

        InputAction fireAction, aimAction, reloadAction, meleeAction, nextAction, scrollAction;
        InputAction[] slotActions;
        bool aimHeld;
        float lastFireRequest = -10f, raiseTimer, lastScrollTime = -10f;

        float aimW, lowW, tuckW, carryW, climbW, stumbleW, twoHandW = 1f, guardW, switchW;
        Vector3 carryPos;
        Quaternion carryRot = Quaternion.identity;

        Vector3 recoilPos, recoilPosVel, recoilRot, recoilRotVel;
        Vector2 lead, leadVel, smoothLook, prevSmoothLook;
        float seed;

        int slot, pendingSlot = -1;
        Firearm shown;
        SwitchPhase switchPhase;
        float switchTimer;

        ReloadPhase reloadPhase;
        float reloadTimer, reloadDuration = 1f, shellTimer;
        bool reloadMagOut, reloadMagIn, reloadRacked, reloadFromEmpty, shellInserted, abortReload;
        int shellsLoaded;
        float reloadPoseW;

        BareHands bareHands;
        MeleeKind meleeKind;
        float meleeTimer, meleeDuration;
        bool meleeHitDone;
        int nextPunchRight = 1;
        int meleeChargesLeft = 2;
        float meleeRechargeTimer;
        AudioSource meleeAudio;
        AudioClip whooshClip, thudClip;

        static readonly Vector3 BeltPos = new Vector3(-0.12f, -0.47f, 0.2f);
        static readonly Vector3 BeltEuler = new Vector3(55f, 25f, 0f);

        void Awake()
        {
            if (!player) player = GetComponentInParent<BodycamPlayerController>();
            if (!rig && player) rig = player.GetComponentInChildren<BodycamCameraRig>();
            if (!stress && player) stress = player.GetComponent<PlayerStress>();
            if (weapons == null || weapons.Length == 0) weapons = GetComponentsInChildren<Firearm>(true);
            if (!impactMaterial)
                foreach (var w in weapons) if (w && w.impactMaterial) { impactMaterial = w.impactMaterial; break; }
            seed = Random.value * 100f;
            meleeChargesLeft = meleeCharges;

            owned = new bool[SlotCount];
            for (int i = 0; i < owned.Length; i++) owned[i] = !startUnarmed;
            // unarmed: sit in the bare-hands slot with the hands themselves hidden
            slot = startUnarmed ? SlotCount - 1 : Mathf.Clamp(startSlot, 0, SlotCount - 1);
            for (int i = 0; i < weapons.Length; i++) if (weapons[i]) weapons[i].gameObject.SetActive(i == slot);
            shown = Current;
            if (shown) shown.transform.SetLocalPositionAndRotation(shown.readyPosition, AimAt(shown.readyPosition, shown.sightHeight));
            if (bareHandsPrefab)
            {
                bareHands = Instantiate(bareHandsPrefab, transform, false);
                bareHands.name = bareHandsPrefab.name;
                bareHands.gameObject.SetActive(!shown && HasFists);
                bareHands.Bind(player, this, gameObject.layer);
            }

            fireAction = new InputAction("Fire", InputActionType.Button, "<Mouse>/leftButton");
            fireAction.AddBinding("<Gamepad>/rightTrigger");
            aimAction = new InputAction("Aim", InputActionType.Button, "<Mouse>/rightButton");
            aimAction.AddBinding("<Gamepad>/leftTrigger");
            reloadAction = new InputAction("Reload", InputActionType.Button, "<Keyboard>/r");
            reloadAction.AddBinding("<Gamepad>/buttonWest");
            meleeAction = new InputAction("Melee", InputActionType.Button, "<Keyboard>/v");
            meleeAction.AddBinding("<Gamepad>/rightStickPress");
            nextAction = new InputAction("NextWeapon", InputActionType.Button, "<Gamepad>/buttonNorth");
            scrollAction = new InputAction("WeaponScroll", InputActionType.Value, "<Mouse>/scroll/y");
            slotActions = new InputAction[Mathf.Min(SlotCount, 9)];
            for (int i = 0; i < slotActions.Length; i++)
                slotActions[i] = new InputAction("Slot" + (i + 1), InputActionType.Button, "<Keyboard>/" + (i + 1));
        }

        void OnEnable()
        {
            fireAction.Enable(); aimAction.Enable(); reloadAction.Enable(); meleeAction.Enable(); nextAction.Enable(); scrollAction.Enable();
            foreach (var a in slotActions) a.Enable();
            if (player) { player.Landed += OnLanded; player.Impacted += OnImpacted; }
        }

        void OnDisable()
        {
            fireAction.Disable(); aimAction.Disable(); reloadAction.Disable(); meleeAction.Disable(); nextAction.Disable(); scrollAction.Disable();
            foreach (var a in slotActions) a.Disable();
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
            fireAction?.Dispose(); aimAction?.Dispose(); reloadAction?.Dispose(); meleeAction?.Dispose();
            nextAction?.Dispose(); scrollAction?.Dispose();
            if (slotActions != null) foreach (var a in slotActions) a?.Dispose();
        }

        // ------------------------------------------------------------------ input

        void Update()
        {
            if (UI.PauseMenu.IsOpen) return;   // paused: no firing, aiming or switching
            bool cursorFree = Cursor.lockState != CursorLockMode.Locked;
            bool mouseFire = Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
            // The first click into the game window only grabs the cursor.
            bool firePressed = fireAction.WasPressedThisFrame() && !(cursorFree && mouseFire);
            aimHeld = aimAction.IsPressed() && !(cursorFree && Mouse.current != null && Mouse.current.rightButton.isPressed);

            if (firePressed)
            {
                raiseTimer = raiseHoldTime;
                if (!Current) TryStartMelee();      // bare hands: the trigger throws punches
                else if (IsReloading) InterruptReload();
                else lastFireRequest = Time.time;
            }
            if (aimHeld) raiseTimer = raiseHoldTime;
            raiseTimer -= Time.deltaTime;
            if (meleeChargesLeft < meleeCharges)
            {
                meleeRechargeTimer -= Time.deltaTime;
                if (meleeRechargeTimer <= 0f) meleeChargesLeft = meleeCharges;
            }
            if (reloadAction.WasPressedThisFrame()) TryStartReload();
            if (meleeAction.WasPressedThisFrame()) { raiseTimer = raiseHoldTime; TryStartMelee(); }

            for (int i = 0; i < slotActions.Length; i++)
                if (slotActions[i].WasPressedThisFrame()) RequestSlot(i);
            if (nextAction.WasPressedThisFrame()) RequestSlot(StepSlot(TargetSlot, 1));
            float scroll = cursorFree ? 0f : scrollAction.ReadValue<float>();
            if (Mathf.Abs(scroll) > 0.01f && Time.time - lastScrollTime > 0.12f)
            {
                lastScrollTime = Time.time;
                RequestSlot(StepSlot(TargetSlot, scroll < 0f ? 1 : -1));
            }

            if (player)
            {
                player.sprintBlocked = raiseTimer > 0f;
                player.moveSpeedScale = Mathf.Lerp(1f, aimMoveSpeedScale, aimW);
            }
        }

        int TargetSlot => pendingSlot >= 0 ? pendingSlot : slot;

        /// <summary>The next slot that way the player actually has (the same one if there is no other).</summary>
        int StepSlot(int from, int direction)
        {
            int n = SlotCount;
            for (int i = 1; i < n; i++)
            {
                int s = ((from + direction * i) % n + n) % n;
                if (owned[s]) return s;
            }
            return from;
        }

        // ------------------------------------------------------------------ picking up

        /// <summary>Whether the player has this gun.</summary>
        public bool Owns(Firearm weapon)
        {
            int i = weapon ? Array.IndexOf(weapons, weapon) : -1;
            return i >= 0 && owned[i];
        }

        /// <summary>The gun of this type among the ones the player can carry (owned or not).</summary>
        public T FindWeapon<T>() where T : Firearm
        {
            foreach (Firearm w in weapons) if (w is T found) return found;
            return null;
        }

        /// <summary>Hands the player one of the guns in <see cref="weapons"/> (and the use of their fists).
        /// False if it is not one of them or they have it already.</summary>
        public bool Give(Firearm weapon, bool equip = true)
        {
            int i = weapon ? Array.IndexOf(weapons, weapon) : -1;
            if (i < 0 || owned[i]) return false;
            bool wasUnarmed = IsUnarmed;
            owned[i] = true;
            owned[owned.Length - 1] = true;
            if (equip)
            {
                RequestSlot(i);
                // empty hands have nothing to put away first: straight to the draw
                if (wasUnarmed && switchPhase == SwitchPhase.Lowering) switchTimer = LowerTime;
            }
            return true;
        }

        // ------------------------------------------------------------------ switching

        /// <summary>Puts the current gun away and draws the one in <paramref name="newSlot"/>.</summary>
        public void RequestSlot(int newSlot)
        {
            newSlot = Mathf.Clamp(newSlot, 0, SlotCount - 1);
            if (newSlot == TargetSlot || !owned[newSlot]) return;
            pendingSlot = newSlot;
            CancelReload();
            if (switchPhase == SwitchPhase.None || switchPhase == SwitchPhase.Raising)
            {
                // reverse a half-finished draw from where it is
                switchTimer = switchPhase == SwitchPhase.Raising ? switchW * LowerTime : 0f;
                switchPhase = SwitchPhase.Lowering;
            }
        }

        float LowerTime => shown && shown.ClipsDriveSwitch ? shown.HolsterTime : switchLowerTime;
        float RaiseTime => shown && shown.ClipsDriveSwitch ? shown.DrawTime : switchRaiseTime;

        void UpdateSwitch(float dt)
        {
            if (switchPhase == SwitchPhase.Lowering)
            {
                switchTimer += dt;
                switchW = Smooth(Mathf.Clamp01(switchTimer / LowerTime));
                if (switchTimer < LowerTime || meleeKind != MeleeKind.None) return;
                if (shown) shown.gameObject.SetActive(false);
                slot = pendingSlot;
                pendingSlot = -1;
                shown = Current;
                if (shown)
                {
                    shown.gameObject.SetActive(true);
                    if (!shown.TryGetHolsterPose(out Vector3 drawPos, out Vector3 drawEuler)) { drawPos = holsterPosition; drawEuler = holsterEuler; }
                    shown.transform.SetLocalPositionAndRotation(drawPos, Quaternion.Euler(drawEuler));
                }
                switchPhase = SwitchPhase.Raising;
                switchTimer = 0f;
            }
            else if (switchPhase == SwitchPhase.Raising)
            {
                switchTimer += dt;
                switchW = 1f - Smooth(Mathf.Clamp01(switchTimer / RaiseTime));
                if (switchTimer >= RaiseTime) { switchPhase = SwitchPhase.None; switchW = 0f; }
            }
        }

        // ------------------------------------------------------------------ pose & firing

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f || !player) return;

            MoveState state = player.State;
            bool parkour = IsParkourState(state);
            Firearm w = shown;

            UpdateSwitch(dt);
            if (IsReloading && BlocksReload(state)) CancelReload();
            UpdateReload(dt);
            UpdateMelee(dt);
            bool fistsOut = !shown && HasFists;
            if (bareHands && bareHands.gameObject.activeSelf != fistsOut) bareHands.gameObject.SetActive(fistsOut);

            // wall tuck: something right in front of the chest pushes the gun up and back
            float tuckTarget = 0f;
            if (w && ProbeTuck(w, out float wallDistance))
                tuckTarget = Mathf.InverseLerp(w.tuckDistance, w.tuckDistance * 0.45f, wallDistance);

            bool meleeing = meleeKind != MeleeKind.None;
            bool lowerTarget = player.IsSprinting && raiseTimer <= 0f;
            bool aimTarget = w && aimHeld && !parkour && !IsReloading && !meleeing && tuckTarget < 0.5f && !IsSwitching;
            // Brace ready: the support hand comes off the gun to meet the wall.
            bool twoHandTarget = w && !parkour && state != MoveState.Sliding && !player.IsBraceReady
                                 && !ReloadHandIsBusy() && meleeKind != MeleeKind.Jab;

            aimW = Approach(aimW, aimTarget ? 1f : 0f, aimSpeed, dt);
            lowW = Approach(lowW, lowerTarget ? 1f : 0f, lowerSpeed, dt);
            tuckW = Approach(tuckW, tuckTarget, tuckSpeed, dt);
            carryW = Approach(carryW, parkour ? 1f : 0f, carrySpeed, dt);
            climbW = Approach(climbW, state == MoveState.Mantling ? 1f : 0f, carrySpeed, dt);
            stumbleW = Approach(stumbleW, state == MoveState.Stumbling ? 1f : 0f, carrySpeed, dt);
            twoHandW = Approach(twoHandW, twoHandTarget ? 1f : 0f, twoHandSpeed, dt);
            guardW = Approach(guardW, !w && !parkour && !lowerTarget ? 1f : 0f, guardSpeed, dt);
            // coming out of the aim the circle closes and the view turns to where the gun was pointing
            player.FreeLookRadius = new Vector2(freeAimHorizontalRadius, freeAimRadius) * Smooth(aimW);

            float sdt = Mathf.Min(dt, 1f / 30f);   // clamped step keeps the stiff springs stable on frame spikes
            UpdateLookLead(dt, sdt);

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

            StepSpring(ref recoilPos, ref recoilPosVel, Vector3.zero, recoilPosStiffness, recoilPosDamping, sdt);
            StepSpring(ref recoilRot, ref recoilRotVel, Vector3.zero, recoilRotStiffness, recoilRotDamping, sdt);

            AimOffset = recoilRot + sway + tremor * stressTremorCameraShare;
            CameraFollowWeight = (1f - carryW) * (1f - lowW) * (1f - tuckW) * (1f - switchW);

            if (!w) return;   // bare hands: the arms pose themselves from GuardWeight / PunchHand

            // --- base pose
            Vector3 readyPos = Vector3.Lerp(w.readyPosition, w.aimPosition, aimW);
            Quaternion readyRot = AimAt(readyPos, w.sightHeight) * Quaternion.Euler(0f, 0f, w.readyCant * (1f - aimW));

            float lowPose = w.ClipsDriveRun ? 0f : Smooth(lowW);   // run clips carry it themselves
            if (!w.TryGetLoweredPose(out Vector3 lowPos, out Vector3 lowEuler)) { lowPos = loweredPosition; lowEuler = loweredEuler; }
            Vector3 pos = Vector3.Lerp(readyPos, lowPos, lowPose);
            Quaternion rot = Quaternion.Slerp(readyRot, Quaternion.Euler(lowEuler), lowPose);
            pos = Vector3.Lerp(pos, tuckPosition, Smooth(tuckW));
            rot = Quaternion.Slerp(rot, Quaternion.Euler(tuckEuler), Smooth(tuckW));

            if (parkour) UpdateCarryPose(state, readyPos, readyRot);
            pos = Vector3.Lerp(pos, carryPos, Smooth(carryW));
            rot = Quaternion.Slerp(rot, carryRot, Smooth(carryW));

            // --- reload: guns that would load out of frame come up into view
            bool hasReloadPose = w.TryGetReloadPose(out Vector3 reloadPos, out Vector3 reloadEuler);
            reloadPoseW = Approach(reloadPoseW, IsReloading && hasReloadPose ? 1f : 0f, 8f, dt);
            if (hasReloadPose && reloadPoseW > 0.001f)
            {
                pos = Vector3.Lerp(pos, reloadPos, Smooth(reloadPoseW));
                rot = Quaternion.Slerp(rot, Quaternion.Euler(reloadEuler), Smooth(reloadPoseW));
            }

            // --- reload: tilt the gun towards the free hand
            if (IsReloading && !w.ClipsDriveReload)
            {
                if (w.reloadStyle == Firearm.ReloadStyle.Magazine)
                {
                    float p = ReloadProgress;
                    float tilt = Smooth(Mathf.Clamp01(p / 0.15f) * Mathf.Clamp01((1f - p) / 0.18f));
                    pos += new Vector3(-0.045f, 0.035f, -0.06f) * tilt;
                    rot *= Quaternion.Euler(-14f * tilt, 16f * tilt, -32f * tilt);
                }
                else
                {
                    // roll the loading port up towards the left hand
                    float tilt = ShellTilt();
                    pos += new Vector3(-0.03f, 0.035f, -0.05f) * tilt;
                    rot *= Quaternion.Euler(-8f * tilt, 12f * tilt, 38f * tilt);
                }
            }

            // --- melee with the gun in hand
            if (meleeing)
            {
                float m = MeleeCurve();
                if (meleeKind == MeleeKind.Bash)
                {
                    // shove the gun forward, canted, like driving the muzzle / receiver into someone
                    pos += new Vector3(-0.04f, 0.07f, 0.22f) * m;
                    rot *= Quaternion.Euler(-22f * m, 18f * m, -38f * m);
                }
                else pos += new Vector3(0.03f, -0.03f, -0.08f) * m;   // jab: gun pulled in while the left hand strikes
            }

            // --- the gun's own motion (pump stroke...)
            w.GetHandlingOffset(out Vector3 hPos, out Vector3 hEuler);
            pos += hPos;
            rot *= Quaternion.Euler(hEuler);

            // --- putting away / drawing
            if (switchW > 0f && !w.ClipsDriveSwitch)
            {
                if (!w.TryGetHolsterPose(out Vector3 awayPos, out Vector3 awayEuler)) { awayPos = holsterPosition; awayEuler = holsterEuler; }
                pos = Vector3.Lerp(pos, awayPos, switchW);
                rot = Quaternion.Slerp(rot, Quaternion.Euler(awayEuler), switchW);
            }

            // --- body motion: bob and strafe roll (the camera has its own, so the camera does not follow these)
            float bob = rig ? rig.BobAmount : 0f;
            float phase = rig ? rig.StepPhase : 0f;
            float s = Mathf.Sin(phase);
            pos += new Vector3(s * bobPosition * 0.7f, -Mathf.Abs(s) * bobPosition, 0f) * bob * (1f - aimW * 0.5f);
            Vector3 localVel = player.transform.InverseTransformDirection(player.HorizontalVelocity);
            rot *= Quaternion.Euler(0f, 0f, -localVel.x * strafeRoll);

            // --- look inertia
            Vector2 leadNow = lead * Mathf.Lerp(1f, aimLeadScale, aimW) * (1f - 0.5f * carryW);
            pos += new Vector3(leadNow.x, -leadNow.y, 0f) * (Mathf.Deg2Rad * leadArm);
            rot = Quaternion.Euler(leadNow.y, leadNow.x, 0f) * rot * Quaternion.Euler(0f, 0f, -leadNow.x * 0.4f);

            // --- free aim: the whole pose swings about the eye, so the sights stay lined up wherever the gun is in the frame
            Vector2 free = player.FreeLook;
            if (free != Vector2.zero)
            {
                Quaternion swing = Quaternion.Euler(free.y, free.x, 0f);
                pos = swing * pos;
                rot = swing * rot;
            }

            // --- the camera drops on every step while the holder does not, which would jump the hands
            // up the frame, so they go down with it (parkour poses have their own motion)
            float follow = state == MoveState.Stumbling ? 1f : Mathf.Lerp(hipFollowBody, aimFollowBody, aimW) * (1f - carryW);
            if (rig && follow > 0.001f)
            {
                Quaternion bodyTilt = Quaternion.Slerp(Quaternion.identity, rig.BodyRotation, follow);
                pos = rig.BodyPosition * follow + bodyTilt * pos;
                rot = bodyTilt * rot;
            }
            pos.y -= aimWalkDrop * Mathf.Clamp01(bob) * aimW;

            w.transform.localPosition = pos + recoilPos + tremorPos;
            w.transform.localRotation = rot * Quaternion.Euler(recoilRot + sway + tremor);

            TryFire(w);
        }

        static readonly RaycastHit[] TuckHits = new RaycastHit[8];

        /// <summary>Nearest solid thing right in front of the chest. Bodies do not count: the gun has to
        /// stay up and fire when enemies are in your face.</summary>
        bool ProbeTuck(Firearm w, out float distance)
        {
            distance = float.MaxValue;
            int n = Physics.SphereCastNonAlloc(transform.position - transform.up * 0.1f, tuckProbeRadius, transform.forward, TuckHits,
                                               w.tuckDistance, w.hitMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                if (TuckHits[i].distance >= distance || TuckHits[i].collider.GetComponentInParent<FleshSurface>()) continue;
                distance = TuckHits[i].distance;
            }
            return distance < float.MaxValue;
        }

        void UpdateLookLead(float dt, float sdt)
        {
            // A sharp flick throws the hands ahead of the view, a hard stop lets them swing back past
            // centre. The camera does not follow this, so it reads as the hands moving on screen.
            Vector2 lookVel = player.LookVelocity;   // deg/s, x = yaw, y = pitch (down positive)
            smoothLook = Vector2.Lerp(smoothLook, lookVel, 1f - Mathf.Exp(-dt / 0.015f));
            Vector2 leadTarget = Vector2.ClampMagnitude(smoothLook * lookLead, maxLookLead);
            leadVel += (smoothLook - prevSmoothLook) * lookJerkLead;
            prevSmoothLook = smoothLook;
            leadVel += ((leadTarget - lead) * leadStiffness - leadVel * leadDamping) * sdt;
            lead += leadVel * sdt;
            lead = Vector2.ClampMagnitude(lead, maxLookLead);
        }

        void TryFire(Firearm w)
        {
            if (Time.time - lastFireRequest > fireBufferTime) return;
            bool ready = carryW < 0.15f && lowW < 0.3f && tuckW < 0.45f && !IsReloading && !IsSwitching
                         && meleeKind == MeleeKind.None;
            if (!ready || !w.CanFire) return;

            lastFireRequest = -10f;
            if (w.Ammo <= 0) { w.DryFire(); return; }

            float speed01 = Mathf.Clamp01(player.Speed / Mathf.Max(player.sprintSpeed, 0.1f));
            float spread = Mathf.Lerp(w.hipSpread, w.aimSpread, aimW) + moveSpread * speed01
                           + (player.State == MoveState.Airborne || player.State == MoveState.DiveBack ? airSpread : 0f)
                           + oneHandSpread * (1f - twoHandW)
                           + (stress ? stress.Tremor * stressSpread : 0f);

            if (!w.Fire(transform.position, spread, player.gameObject)) return;

            float k = Mathf.Lerp(1f, aimRecoilScale, aimW) * Mathf.Lerp(oneHandRecoilScale, 1f, twoHandW);
            recoilRotVel += new Vector3(-w.kickPitch * Random.Range(0.85f, 1.15f),
                                        Random.Range(-w.kickYaw, w.kickYaw),
                                        Random.Range(-w.kickRoll, w.kickRoll)) * k;
            recoilPosVel += new Vector3(Random.Range(-0.05f, 0.05f), w.kickUp, -w.kickBack) * k;
            if (rig)
            {
                rig.AddTrauma(w.cameraTrauma * k);
                rig.AddTremor(w.cameraTremor * k);
                rig.AddKick(new Vector3(0f, 0f, -w.cameraKickBack * k),
                            new Vector3(-w.cameraKickPitch * Random.Range(0.8f, 1.2f), 0f, Random.Range(-w.cameraKickRoll, w.cameraKickRoll)) * k);
            }
            Fired?.Invoke();
        }

        void UpdateCarryPose(MoveState state, Vector3 readyPos, Quaternion readyRot)
        {
            float p = player.ActionProgress;
            if (shown && shown.TryGetCarryPose(state, p, readyPos, readyRot, out carryPos, out carryRot)) return;
            switch (state)
            {
                case MoveState.Mantling:
                {
                    // gun hand goes up onto the ledge with the other one, then pushes the body over
                    float reach = 1f - Mathf.Clamp01((p - 0.45f) / 0.55f);
                    carryPos = Vector3.Lerp(new Vector3(0.22f, -0.36f, 0.42f), new Vector3(0.24f, 0.0f, 0.48f), reach);
                    carryRot = Quaternion.Euler(Vector3.Lerp(new Vector3(55f, -8f, 0f), new Vector3(-10f, -12f, 70f), reach));
                    break;
                }
                case MoveState.Vaulting:
                    carryPos = new Vector3(0.40f, -0.2f, 0.3f);      // balance arm out, gun pointed down-forward
                    carryRot = Quaternion.Euler(35f, 15f, 30f);
                    break;
                case MoveState.Stumbling:
                    carryPos = new Vector3(0.22f, -0.24f, 0.4f);
                    carryRot = Quaternion.Euler(35f, -12f, 18f);
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

        /// <summary>Moves where nobody could keep feeding a gun: the reload stops and cannot start.</summary>
        static bool BlocksReload(MoveState s) =>
            IsParkourState(s) || s == MoveState.Sliding || s == MoveState.DiveBack || s == MoveState.ProneStandUp;

        static bool IsParkourState(MoveState s) =>
            s == MoveState.Mantling || s == MoveState.Vaulting || s == MoveState.Stumbling || s == MoveState.Bumped
            || s == MoveState.BraceWall || s == MoveState.HandLanding || s == MoveState.Rolling || s == MoveState.HardLanding;

        // ------------------------------------------------------------------ melee

        void TryStartMelee()
        {
            if (meleeKind != MeleeKind.None || carryW > 0.5f) return;
            if (IsUnarmed) return;   // nothing picked up yet: no strikes either
            if (switchPhase == SwitchPhase.Lowering) return;
            if (meleeChargesLeft <= 0) return;   // out of breath: wait for the recharge
            meleeChargesLeft--;
            meleeRechargeTimer = meleeRechargeTime;
            CancelReload();

            Firearm w = shown;
            if (!w)
            {
                meleeKind = nextPunchRight > 0 ? MeleeKind.PunchRight : MeleeKind.PunchLeft;
                nextPunchRight = -nextPunchRight;
                meleeDuration = punchTime;
            }
            else if (w.meleeWithWeapon) { meleeKind = MeleeKind.Bash; meleeDuration = bashTime; }
            else { meleeKind = MeleeKind.Jab; meleeDuration = jabTime; }

            meleeTimer = 0f;
            meleeHitDone = false;
            lastFireRequest = -10f;
            PlayMelee(whooshClip ? whooshClip : (whooshClip = ProceduralAudio.Whoosh()), Random.Range(0.9f, 1.15f), 0.7f);
        }

        void UpdateMelee(float dt)
        {
            if (meleeKind == MeleeKind.None) return;
            meleeTimer += dt;
            if (!meleeHitDone && meleeTimer >= meleeDuration * meleeHitAt)
            {
                meleeHitDone = true;
                DoMeleeHit();
            }
            if (meleeTimer >= meleeDuration) meleeKind = MeleeKind.None;
        }

        /// <summary>0 -> 1 (contact) -> 0 over the swing.</summary>
        float MeleeCurve()
        {
            if (meleeKind == MeleeKind.None || meleeDuration <= 0f) return 0f;
            float p = Mathf.Clamp01(meleeTimer / meleeDuration);
            return p < meleeHitAt ? Smooth(p / meleeHitAt) : 1f - Smooth((p - meleeHitAt) / (1f - meleeHitAt));
        }

        void DoMeleeHit()
        {
            Firearm w = shown;
            bool withWeapon = meleeKind == MeleeKind.Bash || meleeKind == MeleeKind.Jab;
            float damage = withWeapon && w ? w.meleeDamage : punchDamage;
            float force = withWeapon && w ? w.meleeForce : punchForce;
            Vector3 dir = transform.forward;

            Physics.SyncTransforms();   // bone hitboxes follow the animation
            // Start a little behind the eye (still inside the body): a sphere cast misses whatever it begins inside.
            const float backStep = 0.3f;
            bool hit = Physics.SphereCast(transform.position - dir * backStep, meleeRadius, dir, out RaycastHit info, meleeRange + backStep,
                                          meleeMask, QueryTriggerInteraction.Ignore);
            if (hit)
            {
                info.collider.GetComponentInParent<IDamageable>()?.TakeDamage(new DamageInfo
                {
                    amount = damage, point = info.point, normal = info.normal, direction = dir,
                    collider = info.collider, source = player ? player.gameObject : gameObject, melee = true
                });
                if (info.rigidbody && !info.rigidbody.isKinematic)
                    info.rigidbody.AddForceAtPosition(dir * force, info.point, ForceMode.Impulse);
                var flesh = info.collider.GetComponentInParent<FleshSurface>();
                if (flesh) ImpactEffects.Get().Blood(info.point, info.normal, dir, impactMaterial, flesh.bloodColor, 0.4f);
                else ImpactEffects.Get().Burst(info.point, info.normal, dir, impactMaterial, 0.5f, false);
                PlayMelee(thudClip ? thudClip : (thudClip = ProceduralAudio.Thud()), Random.Range(0.85f, 1.1f), 1f);

                // the blow stops dead: jolt back through the arms and the camera
                float k = meleeKind == MeleeKind.Bash ? 1.3f : 1f;
                recoilPosVel += new Vector3(0f, 0.2f, -1.2f) * k;
                recoilRotVel += new Vector3(-60f, Random.Range(-40f, 40f), Random.Range(-60f, 60f)) * k;
                if (rig)
                {
                    rig.AddKick(new Vector3(0f, 0f, -0.45f * k), new Vector3(-25f * k, Random.Range(-15f, 15f), Random.Range(-30f, 30f)));
                    rig.AddTremor(0.7f * k);
                    rig.AddTrauma(0.22f * k);
                }
            }
            bool pushed = PushInFront(dir);
            Meleed?.Invoke(hit || pushed);
        }

        static readonly Collider[] PushBuffer = new Collider[24];
        readonly System.Collections.Generic.HashSet<IPushable> pushedThisSwing = new System.Collections.Generic.HashSet<IPushable>();

        /// <summary>Shoves everything pushable in a cone in front back a step.</summary>
        bool PushInFront(Vector3 dir)
        {
            Vector3 eye = transform.position;
            Vector3 flatDir = new Vector3(dir.x, 0f, dir.z).normalized;
            int n = Physics.OverlapSphereNonAlloc(eye + dir * (meleeRange * 0.5f), meleeRange * 0.6f, PushBuffer,
                                                  meleeMask, QueryTriggerInteraction.Ignore);
            pushedThisSwing.Clear();
            for (int i = 0; i < n; i++)
            {
                var target = PushBuffer[i].GetComponentInParent<IPushable>();
                if (target == null || pushedThisSwing.Contains(target)) continue;
                Vector3 to = PushBuffer[i].bounds.center - eye;
                Vector3 flatTo = new Vector3(to.x, 0f, to.z);
                if (flatTo.magnitude > meleeRange + 0.3f) continue;
                if (flatTo.sqrMagnitude > 0.0001f && Vector3.Angle(flatDir, flatTo) > meleePushArc * 0.5f) continue;
                pushedThisSwing.Add(target);
                // push away from the player, leaning into the strike direction
                Vector3 pushDir = (flatTo.normalized + flatDir).normalized;
                target.Push(pushDir, meleePushStrength);
            }
            return pushedThisSwing.Count > 0;
        }

        void PlayMelee(AudioClip clip, float pitch, float vol)
        {
            if (!meleeAudio)
            {
                meleeAudio = gameObject.AddComponent<AudioSource>();
                meleeAudio.playOnAwake = false;
                meleeAudio.spatialBlend = 0f;
            }
            meleeAudio.pitch = pitch;
            meleeAudio.PlayOneShot(clip, vol);
        }

        // ------------------------------------------------------------------ reload

        void TryStartReload()
        {
            Firearm w = shown;
            if (IsReloading || !w || IsSwitching || meleeKind != MeleeKind.None || w.Ammo >= w.magazineSize) return;
            if (player && BlocksReload(player.State)) return;
            reloadFromEmpty = w.Ammo == 0;
            reloadTimer = 0f;
            lastFireRequest = -10f;
            abortReload = false;
            shellsLoaded = 0;
            if (w.reloadStyle == Firearm.ReloadStyle.Magazine)
            {
                reloadPhase = ReloadPhase.Loading;
                reloadDuration = w.reloadTime + (reloadFromEmpty ? w.emptyReloadExtra : 0f);
                reloadMagOut = reloadMagIn = reloadRacked = false;
            }
            else reloadPhase = ReloadPhase.Enter;
        }

        void CancelReload() => reloadPhase = ReloadPhase.None;

        /// <summary>The trigger during a reload stops it and does not shoot: a magazine stays as it is,
        /// shell by shell finishes the shell in hand and comes back to the ready.</summary>
        void InterruptReload()
        {
            if (shown && shown.reloadStyle == Firearm.ReloadStyle.PerShell) abortReload = true;
            else CancelReload();
        }

        /// <summary>Whether the left hand is off the gun for the reload right now.</summary>
        bool ReloadHandIsBusy()
        {
            if (!IsReloading || !shown) return false;
            if (shown.reloadStyle == Firearm.ReloadStyle.Magazine)
            {
                float p = ReloadProgress;
                return p > 0.12f && p < MagInAt + 0.04f;
            }
            return reloadPhase == ReloadPhase.Enter || reloadPhase == ReloadPhase.Loading;
        }

        /// <summary>
        /// Where the free left hand should be during a reload: belt for a fresh magazine, belt and
        /// loading port in turn for shells. World position, rotation as pivot-space euler.
        /// </summary>
        public bool TryGetReloadHandPose(out Vector3 worldPosition, out Vector3 pivotEuler)
        {
            worldPosition = default;
            pivotEuler = default;
            if (!ReloadHandIsBusy()) return false;
            bool atPort = shown.reloadStyle == Firearm.ReloadStyle.PerShell && reloadPhase == ReloadPhase.Loading
                          && shellTimer / Mathf.Max(shown.shellInsertTime, 0.01f) > 0.4f;
            if (atPort)
            {
                worldPosition = shown.transform.TransformPoint(new Vector3(-0.03f, -0.01f, 0.07f));
                pivotEuler = new Vector3(-15f, 35f, -50f);
            }
            else
            {
                worldPosition = transform.TransformPoint(BeltPos);
                pivotEuler = BeltEuler;
            }
            return true;
        }

        float MagInAt => shown && shown.ReloadMagInAt > 0f ? shown.ReloadMagInAt : reloadFromEmpty ? 0.58f : 0.68f;

        float ShellTilt()
        {
            float half = Mathf.Max(shown ? shown.reloadTime * 0.5f : 0.3f, 0.01f);
            return reloadPhase switch
            {
                ReloadPhase.Enter => Smooth(Mathf.Clamp01(reloadTimer / half)),
                ReloadPhase.Loading => 1f,
                ReloadPhase.Exit => 1f - Smooth(Mathf.Clamp01(reloadTimer / half)),
                _ => 0f,
            };
        }

        void UpdateReload(float dt)
        {
            Firearm w = shown;
            if (!IsReloading) return;
            if (!w) { CancelReload(); return; }
            reloadTimer += dt;

            if (w.reloadStyle == Firearm.ReloadStyle.Magazine)
            {
                float p = ReloadProgress;
                if (!reloadMagOut && p >= w.ReloadMagOutAt) { reloadMagOut = true; w.Play(Firearm.Sfx.MagOut); }
                if (!reloadMagIn && p >= MagInAt)
                {
                    reloadMagIn = true;
                    w.Ammo = w.magazineSize;
                    w.Play(Firearm.Sfx.MagIn);
                    recoilPosVel += new Vector3(0f, 0.35f, 0.2f);   // slap the mag home
                    recoilRotVel += new Vector3(-40f, 0f, 30f);
                }
                if (reloadFromEmpty && !reloadRacked && p >= w.ReloadRackAt)
                {
                    reloadRacked = true;
                    w.Rack();
                    recoilPosVel += new Vector3(0f, 0f, -0.4f);
                }
                if (p >= 1f) CancelReload();
                return;
            }

            // shell by shell
            float half = Mathf.Max(w.reloadTime * 0.5f, 0.01f);
            float enter = w.ReloadEnterTime >= 0f ? w.ReloadEnterTime : half;
            float exit = w.ReloadExitTime >= 0f ? w.ReloadExitTime : half;
            switch (reloadPhase)
            {
                case ReloadPhase.Enter:
                    if (abortReload) { EnterExit(); break; }
                    if (reloadTimer >= enter) { reloadPhase = ReloadPhase.Loading; shellTimer = 0f; shellInserted = false; }
                    break;
                case ReloadPhase.Loading:
                    shellTimer += dt;
                    if (abortReload && !shellInserted) { EnterExit(); break; }
                    if (!shellInserted && shellTimer >= w.shellInsertTime * w.ShellInsertShare)
                    {
                        shellInserted = true;
                        w.Ammo = Mathf.Min(w.Ammo + 1, w.magazineSize);
                        w.Play(Firearm.Sfx.ShellIn, Random.Range(0.95f, 1.05f));
                        recoilPosVel += new Vector3(0f, 0.15f, 0.12f);   // thumb pushes the shell home
                        recoilRotVel += new Vector3(-15f, 0f, 10f);
                    }
                    if (shellTimer >= w.shellInsertTime)
                    {
                        shellsLoaded++;
                        if (w.Ammo >= w.magazineSize || abortReload) EnterExit();
                        else { shellTimer = 0f; shellInserted = false; }
                    }
                    break;
                case ReloadPhase.Exit:
                    if (reloadFromEmpty && !reloadRacked && reloadTimer >= exit)
                    {
                        reloadRacked = true;
                        w.Rack();   // chamber the first shell
                    }
                    if (reloadTimer >= exit + (reloadFromEmpty ? w.emptyReloadExtra : 0f)) CancelReload();
                    break;
            }
        }

        void EnterExit()
        {
            reloadPhase = ReloadPhase.Exit;
            reloadTimer = 0f;
            reloadRacked = false;
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

        Quaternion AimAt(Vector3 gripPos, float sightHeight)
        {
            Vector3 toTarget = new Vector3(0f, 0f, convergenceDistance) - (gripPos + Vector3.up * sightHeight);
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
