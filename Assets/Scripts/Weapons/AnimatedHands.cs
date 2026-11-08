using BodycamHorror.Player;
using UnityEngine;
using UnityEngine.Rendering;

namespace BodycamHorror.Weapons
{
    /// <summary>
    /// The pistol rig. The clips (draw, holster, idle, shot, reload, run) were made for another pair of
    /// arms: that rig plays them with its arms hidden and its gun visible, and every frame the shared
    /// first-person arms take its hand positions and finger directions by IK. Every pose
    /// <see cref="PlayerShooting"/> gives the weapon (aim, recoil, sway, wall tuck, parkour carry) is
    /// applied to the gun and the hands. The left hand can also leave the gun for the pose brain's
    /// free-hand target (wall brace, jab).
    /// </summary>
    [DefaultExecutionOrder(65)]   // after PlayerShooting (-100) and the free-hand pose (50), before the weapon light (70)
    public class AnimatedHands : MonoBehaviour
    {
        [Header("Gun in the Idle clip (model space, measured from the meshes)")]
        [Tooltip("Point on the gun's symmetry plane used as the weapon origin.")]
        public Vector3 gunOrigin;
        [Tooltip("Rotation of the gun: sight line along +Z, up along +Y.")]
        public Vector3 gunEuler;
        [Tooltip("Where the bullets leave, in gun space.")]
        public Vector3 muzzleLocal;
        [Tooltip("Height of the sight line above the weapon origin.")]
        public float sightHeight = 0.075f;
        [Tooltip("Slide travel when locked back on an empty magazine (m).")]
        public float slideTravel = 0.0337f;

        [Header("Placement")]
        [Tooltip("Height of the rig's Head bone: this point goes on the camera.")]
        public float headHeight = 1.62f;
        [Tooltip("Nudges the whole rig relative to the camera.")]
        public Vector3 viewOffset;
        [Tooltip("Distance of the gun from the eye when aiming down the sights.")]
        public float aimDistance = 0.3f;

        [Header("Arms")]
        [Tooltip("Instance of the shared first-person arms.")]
        public Transform armsRoot;
        [Tooltip("Pose the arms are built in (any held pose of theirs).")]
        public AnimationClip armsIdleClip;
        [Tooltip("Shoulders in weapon-holder space.")]
        public Vector3 leftShoulder = new Vector3(-0.17f, -0.25f, -0.06f);
        public Vector3 rightShoulder = new Vector3(0.17f, -0.25f, -0.06f);

        [Header("Clips")]
        public AnimationClip idleClip;
        public AnimationClip reloadClip;
        public AnimationClip takeClip;
        public AnimationClip hideClip;
        [Tooltip("Reload: share of the clip at which the old magazine starts to come out.")]
        public float magOutAt = 0.24f;
        [Tooltip("Reload: share of the clip at which the new magazine seats.")]
        public float magInAt = 0.556f;
        [Tooltip("Reload: share of the clip at which the slide closes.")]
        public float slideCloseAt = 0.756f;

        [Header("IK")]
        [Tooltip("How far the arms as a whole may move when a hand target is out of range (m).")]
        public float maxReachShift = 0.5f;

        public float ReloadLength => reloadClip ? reloadClip.length : 1.6f;
        public float DrawLength => takeClip ? takeClip.length : 0.3f;
        public float HolsterLength => hideClip ? hideClip.length : 0.2f;
        public Vector3 GunPosition => gunOrigin + Vector3.down * headHeight + viewOffset;
        public Quaternion GunRotation => Quaternion.Euler(gunEuler);
        /// <summary>Roll of the gun in the Idle clip (degrees), kept as the ready pose's cant.</summary>
        public float IdleCant => Mathf.DeltaAngle(0f, gunEuler.z);
        /// <summary>Point on the gun that moves with the clips; things mounted on the gun ride on it.</summary>
        public Transform GunAnchor { get; private set; }

        /// <summary>A hand of the rig that plays the clips, and how it maps onto a hand of the shared arms.</summary>
        class SourceHand
        {
            public Transform hand;
            public Vector3 palmCenter;          // metres, hand rotation space
            public Quaternion toTarget;         // source hand rotation -> target hand rotation
            public Transform[] joints;          // five fingers, three joints each
        }

        static readonly string[] SourceFingers = { "Thumb", "Index", "Middle", "Ring", "Pinky" };

        Firearm gun;
        PlayerShooting shooting;
        BodycamPlayerController player;
        Transform holder, freeLeftHand;
        RiggedArms otherArms;
        PlaceholderArms pose;
        Animator animator;
        Transform gunBase, gunSlide, gunMag;
        Vector3 slideRest, armsBase;      // slide relative to the gun base bone, Idle pose
        ArmRig rig;
        SourceHand srcLeft, srcRight;
        float leftFree, leftOpen, slideLock;
        bool running, hiding, wasReloading, ready;

        /// <summary>Hooks the rig up to its weapon. Called once by the weapon that spawned it.</summary>
        public void Bind(Firearm weapon, BodycamPlayerController owner, PlayerShooting shooter)
        {
            gun = weapon;
            player = owner;
            shooting = shooter;
            holder = transform.parent;
            transform.localPosition = Vector3.down * headHeight + viewOffset;
            transform.localRotation = Quaternion.identity;
            transform.localScale = Vector3.one;

            animator = GetComponent<Animator>();
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            var armsAnimator = armsRoot.GetComponent<Animator>();
            if (armsAnimator) armsAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            foreach (var t in GetComponentsInChildren<Transform>(true)) t.gameObject.layer = weapon.gameObject.layer;
            foreach (var smr in GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                smr.updateWhenOffscreen = true;
                smr.shadowCastingMode = ShadowCastingMode.Off;
            }
            ViewModelLighting.MarkViewModel(gameObject);

            if (idleClip) idleClip.SampleAnimation(gameObject, 0f);
            if (armsIdleClip) armsIdleClip.SampleAnimation(armsRoot.gameObject, 0f);
            rig = new ArmRig(armsRoot);
            armsBase = armsRoot.localPosition;

            gunBase = SourceBone("Base");
            gunSlide = SourceBone("Up");
            gunMag = SourceBone("Mag");
            slideRest = Quaternion.Inverse(gunBase.rotation) * (gunSlide.position - gunBase.position);
            srcLeft = BuildSource(".L", rig.Left);
            srcRight = BuildSource(".R", rig.Right);

            GunAnchor = new GameObject("GunAnchor").transform;
            GunAnchor.SetParent(gunBase, false);
            GunAnchor.SetPositionAndRotation(holder.TransformPoint(GunPosition), holder.rotation * GunRotation);

            otherArms = owner ? owner.GetComponentInChildren<RiggedArms>(true) : null;
            pose = owner ? owner.GetComponentInChildren<PlaceholderArms>(true) : null;

            weapon.Fired += OnFired;
            ready = true;
            OnEnable();
        }

        Transform SourceBone(string boneName)
        {
            foreach (var t in GetComponentsInChildren<Transform>(true))
                if (t.name == boneName && !t.IsChildOf(armsRoot)) return t;
            Debug.LogWarning($"[AnimatedHands] Bone {boneName} not found.", this);
            return null;
        }

        SourceHand BuildSource(string side, ArmRig.Arm target)
        {
            var s = new SourceHand { hand = SourceBone("Hand" + side), joints = new Transform[15] };
            for (int f = 0; f < SourceFingers.Length; f++)
                for (int j = 0; j < 3; j++) s.joints[f * 3 + j] = SourceBone(SourceFingers[f] + "_" + j + side);

            Vector3 wrist = s.hand.position, middle = s.joints[6].position;
            Vector3 palm = ArmRig.PalmDirection(wrist, middle, s.joints[3].position, s.joints[12].position, s.joints[8].position);
            Quaternion sourceFrame = Quaternion.Inverse(Quaternion.LookRotation((middle - wrist).normalized, -palm)) * s.hand.rotation;
            s.toTarget = Quaternion.Inverse(sourceFrame) * target.handFrame;
            s.palmCenter = Quaternion.Inverse(s.hand.rotation) * (Vector3.Lerp(wrist, middle, 0.6f) + palm * 0.012f - wrist);
            return s;
        }

        void OnDestroy()
        {
            if (gun) gun.Fired -= OnFired;
        }

        void OnEnable()
        {
            if (!ready) return;
            running = hiding = wasReloading = false;
            leftFree = 0f;
            animator.Play("Take", 0, 0f);
            if (otherArms) otherArms.RequestHidden(true);
        }

        void OnDisable()
        {
            if (ready && otherArms) otherArms.RequestHidden(false);
        }

        void OnFired(bool hit)
        {
            if (!isActiveAndEnabled) return;
            // straight from the rest pose it can cut in on the frame of the shot; out of anything else it needs a blend
            var state = animator.GetCurrentAnimatorStateInfo(0);
            if (state.IsName("Idle") || state.IsName("Shoot")) animator.Play("Shoot", 0, 0f);
            else animator.CrossFadeInFixedTime("Shoot", 0.06f, 0, 0f);
            running = false;
        }

        // ------------------------------------------------------------------ per frame

        void LateUpdate()
        {
            if (!ready || !holder) return;
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            UpdateClips();
            armsRoot.localPosition = armsBase;

            // what the procedural side has done to the weapon, relative to where the clip holds it
            Quaternion dq = gun.transform.localRotation * Quaternion.Inverse(GunRotation);
            Vector3 dp = gun.transform.localPosition - dq * GunPosition;
            Quaternion worldTurn = holder.rotation * dq * Quaternion.Inverse(holder.rotation);
            Vector3 Move(Vector3 p) => holder.TransformPoint(dp + dq * holder.InverseTransformPoint(p));

            ArmRig.Arm left = rig.Left, right = rig.Right;
            Target(srcRight, right, worldTurn, Move, out Vector3 rightPos, out Quaternion rightRot);
            Target(srcLeft, left, worldTurn, Move, out Vector3 leftPos, out Quaternion leftRot);

            Vector3 basePos = Move(gunBase.position);
            Quaternion baseRot = worldTurn * gunBase.rotation;
            Vector3 slidePos = Move(gunSlide.position);
            Quaternion slideRot = worldTurn * gunSlide.rotation;
            Vector3 magPos = Move(gunMag.position);
            Quaternion magRot = worldTurn * gunMag.rotation;

            // the clip's reload starts on a locked-back slide: a mid-magazine reload keeps it closed
            bool reloading = shooting.IsReloading;
            if (reloading && !shooting.ReloadFromEmpty) slidePos = basePos + baseRot * slideRest;
            slideLock = Mathf.MoveTowards(slideLock, gun.Ammo <= 0 && !reloading ? 1f : 0f, dt / 0.1f);
            slidePos -= gun.transform.forward * (slideTravel * slideLock);

            // support hand leaves the gun for the free-hand pose (the reload clip already moves it)
            float freeTarget = reloading ? 0f : 1f - shooting.TwoHandWeight;
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
            Vector3 reachDir = ArmRig.ReachDirection(holder);
            float shift = Mathf.Max(ArmRig.ReachShift(left, leftPos, reachDir), ArmRig.ReachShift(right, rightPos, reachDir));
            armsRoot.position += reachDir * Mathf.Min(shift, maxReachShift);

            // a pose the right arm cannot get to: the gun stays in the hand and comes along
            Vector3 gunFix = ArmRig.ReachCorrection(right, rightPos);
            rightPos += gunFix;
            leftPos += gunFix * (1f - leftFree);
            gunBase.SetPositionAndRotation(basePos + gunFix, baseRot);
            gunSlide.SetPositionAndRotation(slidePos + gunFix, slideRot);
            gunMag.SetPositionAndRotation(magPos + gunFix, magRot);

            ArmRig.Solve(left, leftPos, leftRot, ArmRig.ElbowHint(left, holder));
            ArmRig.Solve(right, rightPos, rightRot, ArmRig.ElbowHint(right, holder));
            CopyFingers(srcRight, right, worldTurn);
            CopyFingers(srcLeft, left, worldTurn);

            // a hand flat on a wall or a ledge is open; the clips only ever hold it around the gun
            bool parkour = player && !(player.State == MoveState.Grounded || player.State == MoveState.Airborne || player.State == MoveState.Sliding);
            float openTarget = leftFree * (parkour || (player && player.IsBraceReady) ? 1f : 0f);
            leftOpen = Mathf.MoveTowards(leftOpen, openTarget, dt * 8f);
            if (leftOpen > 0.001f) ArmRig.BlendOpen(left, leftOpen);
        }

        static void Target(SourceHand s, ArmRig.Arm a, Quaternion worldTurn, System.Func<Vector3, Vector3> move,
                           out Vector3 position, out Quaternion rotation)
        {
            rotation = worldTurn * s.hand.rotation * s.toTarget;
            Vector3 palm = move(s.hand.position + s.hand.rotation * s.palmCenter);
            position = palm - rotation * a.palmCenter;
        }

        /// <summary>Points each finger segment of the arms the way the source's segment points.</summary>
        static void CopyFingers(SourceHand s, ArmRig.Arm a, Quaternion worldTurn)
        {
            for (int f = 0; f < ArmRig.FingerCount; f++)
            {
                for (int j = 0; j < ArmRig.JointsPerFinger; j++)
                {
                    int i = f * ArmRig.JointsPerFinger + j;
                    Transform joint = a.fingers[i];
                    // the source's outer joints have no bone past them: their own Y axis runs along them
                    Vector3 want = worldTurn * (j < ArmRig.JointsPerFinger - 1 ? s.joints[i + 1].position - s.joints[i].position : s.joints[i].up);
                    Vector3 end = j < ArmRig.JointsPerFinger - 1 ? a.fingers[i + 1].position : a.fingerEnds[f].position;
                    joint.rotation = Quaternion.FromToRotation(end - joint.position, want) * joint.rotation;
                }
            }
        }

        void UpdateClips()
        {
            bool reloading = shooting.IsReloading;
            if (reloading != wasReloading)
            {
                wasReloading = reloading;
                running = false;
                animator.CrossFadeInFixedTime(reloading ? "Reload" : "Idle", reloading ? 0.1f : 0.12f, 0, 0f);
            }

            if (shooting.IsPuttingAway != hiding)
            {
                hiding = shooting.IsPuttingAway;
                running = false;
                animator.CrossFadeInFixedTime(hiding ? "Hide" : "Idle", 0.08f, 0, 0f);
            }

            if (reloading || hiding || animator.GetCurrentAnimatorStateInfo(0).IsName("Take")) return;
            bool sprint = shooting.WantsLowered;
            if (sprint == running) return;
            running = sprint;
            animator.CrossFadeInFixedTime(sprint ? "RunStart" : "RunEnd", 0.12f, 0, 0f);
        }
    }
}
