using BodycamHorror.Player;
using UnityEngine;
using UnityEngine.Rendering;

namespace BodycamHorror.Weapons
{
    /// <summary>
    /// The shotgun rig: gun and arms play the same clips. Procedural weapon poses from
    /// <see cref="PlayerShooting"/> move the gun and hands, and the arms reach for them by IK. The reload
    /// clip is scrubbed by the shell-by-shell logic: one pass of it is looped for every shell, the
    /// opening and closing parts are played once.
    /// </summary>
    [DefaultExecutionOrder(65)]   // after PlayerShooting (-100) and the free-hand pose (50), before the weapon light (70)
    public class ShotgunHands : MonoBehaviour
    {
        [Header("Rig parts")]
        [Tooltip("The rig that plays the clips.")]
        public Transform sourceRoot;
        public Animator sourceAnimator;
        public AnimationClip sourceIdleClip;

        [Header("Gun in the Idle clip (weapon-holder space, measured from the meshes)")]
        public Vector3 gunPosition;
        [Tooltip("Rotation of the gun: sight line along +Z, up along +Y.")]
        public Vector3 gunEuler;
        [Tooltip("Where the pellets leave, in gun space.")]
        public Vector3 muzzleLocal;
        [Tooltip("Height of the sight line above the weapon origin.")]
        public float sightHeight = 0.06f;

        [Header("Placement")]
        [Tooltip("Size of the gun against the arms, grown around the right palm so the grip stays where it is.")]
        public float gunScale = 1.25f;
        [Tooltip("Where the gun is carried relative to the clips' pose: low and to the right, butt in the shoulder.")]
        public Vector3 readyOffset = new Vector3(0.0676f, -0.1253f, -0.144f);
        [Tooltip("Distance of the gun from the eye when aiming.")]
        public float aimDistance = 0.25f;
        [Tooltip("How far below the aim point the gun is held when aiming, so the target stays in view.")]
        public float aimDrop = 0.08f;
        [Tooltip("How far right of the aim point the gun is held when aiming.")]
        public float aimSide = 0.03f;
        [Tooltip("Carried low on the run (weapon-holder space).")]
        public Vector3 sprintPosition = new Vector3(0.14f, -0.27f, 0.09f);
        public Vector3 sprintEuler = new Vector3(20f, -14f, 10f);
        [Tooltip("Put away: low and out of frame, without tipping the butt up into the lens.")]
        public Vector3 holsterPosition = new Vector3(0.2f, -0.6f, 0.1f);
        public Vector3 holsterEuler = new Vector3(18f, -12f, 12f);
        [Tooltip("Lifted towards the middle of the view while loading, so the hand and the shells are in frame.")]
        public Vector3 reloadPosition = new Vector3(0.08f, -0.17f, 0.26f);
        public Vector3 reloadEuler = new Vector3(-8f, -10f, 18f);
        [Tooltip("Moves the left hand on the pump, in gun space.")]
        public Vector3 leftHandOffset = new Vector3(0f, 0.016f, 0f);
        [Tooltip("Moves the arms when aiming (weapon-holder space), so the left forearm comes to the fore-end from the side and not from under it.")]
        public Vector3 aimArmsOffset = new Vector3(-0.1f, 0.16f, 0.03f);
        [Tooltip("Shoulders in weapon-holder space.")]
        public Vector3 leftShoulder = new Vector3(-0.13f, -0.3f, 0.05f);
        public Vector3 rightShoulder = new Vector3(0.24f, -0.3f, -0.18f);

        [Header("Reload clip timeline (s)")]
        [Tooltip("End of the opening: the support hand is about to leave the pump.")]
        public float enterEnd = 0.07f;
        [Tooltip("End of the first shell's pass.")]
        public float firstShellEnd = 1.38f;
        [Tooltip("Length of one pass: hand to the belt, shell in, hand back.")]
        public float shellCycle = 1.31f;
        [Tooltip("Where the closing part starts.")]
        public float exitStart = 5.31f;
        [Tooltip("From the closing part's start until the pump is racked.")]
        public float exitToRack = 0.62f;
        [Tooltip("Clip length.")]
        public float reloadLength = 6.82f;
        [Tooltip("Reload playback speed against the clip.")]
        public float reloadRate = 1.3f;
        [Tooltip("Share of a pass at which the shell goes in.")]
        [Range(0f, 1f)] public float shellInsertShare = 0.9f;

        [Header("Firing")]
        [Tooltip("Time from the shot until the pump is finished and it can fire again.")]
        public float shotRecovery = 0.75f;
        [Tooltip("Time from the shot until the pump starts.")]
        public float pumpDelay = 0.33f;
        public float pumpTime = 0.3f;

        [Header("Moving")]
        [Tooltip("Above this speed the walk clip plays.")]
        public float walkSpeed = 0.6f;

        [Header("IK")]
        [Tooltip("How far the whole rig may move when a hand target is out of range (m).")]
        public float maxReachShift = 0.55f;

        /// <summary>Where the clips hold the gun; the procedural poses are measured against this.</summary>
        public Vector3 GunPosition => gunPosition;
        /// <summary>Where the gun sits at the ready.</summary>
        public Vector3 ReadyPosition => gunPosition + readyOffset;
        /// <summary>Where the gun sits when aiming.</summary>
        public Vector3 AimPosition => new Vector3(aimSide, -SightHeight - aimDrop, aimDistance);
        public float SightHeight => sightHeight * gunScale;
        public Vector3 MuzzleLocal => muzzleLocal * gunScale;
        public Quaternion GunRotation => Quaternion.Euler(gunEuler);
        public float IdleCant => Mathf.DeltaAngle(0f, gunEuler.z);
        public Transform GunAnchor { get; private set; }
        public float EnterTime => enterEnd / reloadRate;
        public float ShellTime => shellCycle / reloadRate;
        public float ExitTime => exitToRack / reloadRate;
        public float ExitExtraTime => (reloadLength - exitStart - exitToRack) / reloadRate;

        Firearm gun;
        PlayerShooting shooting;
        BodycamPlayerController player;
        Transform holder, freeLeftHand, gunBody, shell, pump;
        RiggedArms otherArms;
        PlaceholderArms pose;
        ArmRig rig;
        Vector3 rootBase, bodyScale, shellScale, pumpPalm;
        Quaternion pumpHand;
        float pumpHold = 1f, shotUntil;
        SkinnedMeshRenderer shellRenderer;
        float leftFree, leftOpen;
        bool walking, wasReloading, ready;

        /// <summary>Hooks the rig up to its weapon. Called once by the weapon that spawned it.</summary>
        public void Bind(Firearm weapon, BodycamPlayerController owner, PlayerShooting shooter)
        {
            gun = weapon;
            player = owner;
            shooting = shooter;
            holder = transform.parent;

            sourceAnimator.applyRootMotion = false;
            sourceAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            foreach (var t in GetComponentsInChildren<Transform>(true)) t.gameObject.layer = weapon.gameObject.layer;
            foreach (var smr in GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                smr.updateWhenOffscreen = true;
                smr.shadowCastingMode = ShadowCastingMode.Off;
            }
            ViewModelLighting.MarkViewModel(gameObject);

            if (sourceIdleClip) sourceIdleClip.SampleAnimation(sourceRoot.gameObject, 0f);
            rig = new ArmRig(sourceRoot);
            rootBase = sourceRoot.localPosition;

            gunBody = SourceBone("Body");
            shell = SourceBone("12ge_low");
            bodyScale = gunBody.localScale;
            shellScale = shell.localScale;

            // the grip on the fore-end, taken from the rest pose
            pump = SourceBone("Slide");
            ArmRig.Arm support = rig.Left;
            Quaternion toPump = Quaternion.Inverse(pump.rotation);
            pumpHand = toPump * support.hand.rotation;
            pumpPalm = toPump * (support.hand.position + support.hand.rotation * support.palmCenter - pump.position);

            foreach (var smr in sourceRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (smr.name == "12ge_low") shellRenderer = smr;

            // the body bone carries the file's x100 scale: keep the mount point itself at scale 1
            GunAnchor = new GameObject("GunAnchor").transform;
            GunAnchor.SetParent(gunBody, false);
            GunAnchor.localScale = Vector3.one / gunBody.lossyScale.x;
            GunAnchor.SetPositionAndRotation(holder.TransformPoint(GunPosition), holder.rotation * GunRotation);

            otherArms = owner ? owner.GetComponentInChildren<RiggedArms>(true) : null;
            pose = owner ? owner.GetComponentInChildren<PlaceholderArms>(true) : null;

            weapon.Fired += OnFired;
            ready = true;
            OnEnable();
        }

        Transform SourceBone(string boneName)
        {
            foreach (var t in sourceRoot.GetComponentsInChildren<Transform>(true))
                if (t.name == boneName && !t.GetComponent<SkinnedMeshRenderer>()) return t;
            Debug.LogWarning($"[ShotgunHands] Bone {boneName} not found.", this);
            return null;
        }

        void OnDestroy()
        {
            if (gun) gun.Fired -= OnFired;
        }

        void OnEnable()
        {
            if (!ready) return;
            walking = wasReloading = false;
            leftFree = 0f;
            pumpHold = 1f;
            sourceAnimator.Play("Idle", 0, 0f);
            if (otherArms) otherArms.RequestHidden(true);
        }

        void OnDisable()
        {
            if (ready && otherArms) otherArms.RequestHidden(false);
        }

        void OnFired(bool hit)
        {
            if (!isActiveAndEnabled) return;
            sourceAnimator.CrossFadeInFixedTime("Shot", 0.03f, 0, 0f);
            walking = false;
            shotUntil = Time.time + shotRecovery;
        }

        // ------------------------------------------------------------------ per frame

        void LateUpdate()
        {
            if (!ready || !holder) return;
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            UpdateClips();
            sourceRoot.localPosition = rootBase;
            gunBody.localScale = bodyScale;   // the bones under it are read at the size the clips were made for

            Quaternion dq = gun.transform.localRotation * Quaternion.Inverse(GunRotation);
            Vector3 dp = gun.transform.localPosition - dq * GunPosition;
            Quaternion worldTurn = holder.rotation * dq * Quaternion.Inverse(holder.rotation);
            Vector3 Move(Vector3 p) => holder.TransformPoint(dp + dq * holder.InverseTransformPoint(p));

            ArmRig.Arm left = rig.Left, right = rig.Right;
            Vector3 grip = Move(right.hand.position + right.hand.rotation * right.palmCenter);
            Vector3 Grow(Vector3 p) => grip + (p - grip) * gunScale;

            // everything worked out in world space first: the rig may shift under it for reach
            Vector3 bodyPos = Grow(Move(gunBody.position)), shellPos = Grow(Move(shell.position));
            Quaternion bodyRot = worldTurn * gunBody.rotation, shellRot = worldTurn * shell.rotation;

            Vector3 rightPos = Move(right.hand.position), leftPos = Move(left.hand.position);
            Quaternion rightRot = worldTurn * right.hand.rotation, leftRot = worldTurn * left.hand.rotation;
            // the support hand stays on the bigger gun: its palm moves with the point it was holding
            Vector3 leftPalm = Move(left.hand.position + left.hand.rotation * left.palmCenter);
            leftPos += Grow(leftPalm) - leftPalm;

            // outside the reload the palm rides the fore-end: the walk clips sway the hand on their own.
            // At the end of a reload it is back there in time for the racking stroke
            bool onPump = !shooting.IsReloading || (shooting.ReloadStage == 3 && shooting.ReloadStageTime >= ExitTime * 0.8f);
            pumpHold = Mathf.MoveTowards(pumpHold, onPump ? 1f : 0f, dt * 8f);
            if (pumpHold > 0.001f)
            {
                Quaternion pumpRot = worldTurn * pump.rotation;
                Quaternion heldRot = pumpRot * pumpHand;
                Vector3 heldPos = Grow(Move(pump.position)) + pumpRot * (pumpPalm * gunScale) - heldRot * left.palmCenter;
                leftPos = Vector3.Lerp(leftPos, heldPos, pumpHold);
                leftRot = Quaternion.Slerp(leftRot, heldRot, pumpHold);
            }
            leftPos += gun.transform.TransformDirection(leftHandOffset);

            float freeTarget = shooting.IsReloading ? 0f : 1f - shooting.TwoHandWeight;
            leftFree = Mathf.MoveTowards(leftFree, freeTarget, dt * 8f);
            if (!freeLeftHand && pose) freeLeftHand = pose.transform.Find("LeftArm");   // built in the pose brain's Awake
            if (leftFree > 0.001f && freeLeftHand)
            {
                ArmRig.FreeTarget(left, freeLeftHand, out Vector3 freePos, out Quaternion freeRot);
                leftPos = Vector3.Lerp(leftPos, freePos, leftFree);
                leftRot = Quaternion.Slerp(leftRot, freeRot, leftFree);
            }

            left.upper.position = holder.TransformPoint(leftShoulder);
            right.upper.position = holder.TransformPoint(rightShoulder);
            sourceRoot.position += holder.TransformVector(aimArmsOffset) * shooting.AimWeight;
            Vector3 reachDir = ArmRig.ReachDirection(holder);
            float shift = Mathf.Max(ArmRig.ReachShift(left, leftPos, reachDir), ArmRig.ReachShift(right, rightPos, reachDir));
            sourceRoot.position += reachDir * Mathf.Min(shift, maxReachShift);
            // forward and down is not always enough for the fore-end: the last centimetres go straight at it,
            // or the stretched arm would stop short and the palm would slide on the gun as it bobs
            sourceRoot.position -= ArmRig.ReachCorrection(left, leftPos) * (1f - leftFree);

            // a pose the right arm cannot get to: the gun stays in the hand and comes along
            Vector3 gunFix = ArmRig.ReachCorrection(right, rightPos);
            bodyPos += gunFix; shellPos += gunFix; rightPos += gunFix;
            leftPos += gunFix * (1f - leftFree);

            gunBody.SetPositionAndRotation(bodyPos, bodyRot);
            shell.SetPositionAndRotation(shellPos, shellRot);
            gunBody.localScale = bodyScale * gunScale;
            shell.localScale = shellScale * gunScale;
            GunAnchor.localScale = Vector3.one / gunBody.lossyScale.x;
            ArmRig.Solve(left, leftPos, leftRot, ArmRig.ElbowHint(left, holder));
            ArmRig.Solve(right, rightPos, rightRot, ArmRig.ElbowHint(right, holder));

            bool parkour = player && !(player.State == MoveState.Grounded || player.State == MoveState.Airborne || player.State == MoveState.Sliding);
            float openTarget = leftFree * (parkour || (player && player.IsBraceReady) ? 1f : 0f);
            leftOpen = Mathf.MoveTowards(leftOpen, openTarget, dt * 8f);
            if (leftOpen > 0.001f) ArmRig.BlendOpen(left, leftOpen);
        }

        /// <summary>How the gun is carried through parkour moves: low and across the body, never tipped
        /// so far that the long butt swings up into the lens or the right arm has to reach for it.</summary>
        public void GetCarryPose(MoveState state, float progress, Vector3 readyPosition, Quaternion readyRotation,
                                 out Vector3 position, out Quaternion rotation)
        {
            Vector3 p = new Vector3(0.24f, -0.36f, 0.18f), e = new Vector3(25f, -15f, 25f);
            switch (state)
            {
                case MoveState.Mantling: p = new Vector3(0.2f, -0.4f, 0.16f); e = new Vector3(35f, -30f, 35f); break;
                case MoveState.Vaulting: p = new Vector3(0.26f, -0.36f, 0.16f); e = new Vector3(30f, -20f, 40f); break;
                case MoveState.BraceWall: p = new Vector3(0.22f, -0.34f, 0.16f); e = new Vector3(20f, -20f, 35f); break;
                case MoveState.HandLanding: p = new Vector3(0.22f, -0.42f, 0.14f); e = new Vector3(35f, -15f, 25f); break;
                case MoveState.Rolling: p = new Vector3(0.14f, -0.38f, 0.12f); e = new Vector3(30f, -35f, 50f); break;
                case MoveState.HardLanding: p = new Vector3(0.26f, -0.42f, 0.16f); e = new Vector3(35f, -20f, 30f); break;
            }
            position = p;
            rotation = Quaternion.Euler(e);
            if (state != MoveState.Bumped) return;
            // braced against the wall, coming back to ready as the stun wears off
            position = Vector3.Lerp(readyPosition, new Vector3(0.2f, -0.32f, 0.16f), 1f - progress);
            rotation = Quaternion.Slerp(readyRotation, Quaternion.Euler(15f, -15f, 20f), 1f - progress);
        }

        void UpdateClips()
        {
            int stage = shooting.ReloadStage;
            bool reloading = stage != 0;
            if (reloading != wasReloading)
            {
                wasReloading = reloading;
                walking = false;
                sourceAnimator.CrossFadeInFixedTime(reloading ? "Reload" : "Idle", reloading ? 0.08f : 0.16f, 0, 0f);
            }
            if (shellRenderer) shellRenderer.enabled = stage == 2;   // the clip keeps the shell around all the time
            if (reloading)
            {
                sourceAnimator.SetFloat("ReloadTime", ReloadClipTime(stage) / reloadLength);
                return;
            }

            // the shot and its pump: nothing may cut in, and the Animator only reports the state after the blend
            if (Time.time < shotUntil) return;
            var state = sourceAnimator.GetCurrentAnimatorStateInfo(0);
            if (state.IsName("Shot") && state.normalizedTime < 1f) return;
            bool moving = player && player.State == MoveState.Grounded && player.Speed > walkSpeed;
            if (moving == walking) return;
            walking = moving;
            sourceAnimator.CrossFadeInFixedTime(moving ? "Walk" : "Idle", 0.15f, 0, 0f);
        }

        float ReloadClipTime(int stage)
        {
            float t = shooting.ReloadStageTime * reloadRate;
            switch (stage)
            {
                case 1: return Mathf.Min(t, enterEnd);
                case 2: return (shooting.ShellsLoaded == 0 ? enterEnd : firstShellEnd) + Mathf.Min(t, shellCycle);
                default: return Mathf.Min(exitStart + t, reloadLength);
            }
        }
    }
}
