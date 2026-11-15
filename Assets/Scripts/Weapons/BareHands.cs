using BodycamHorror.Player;
using UnityEngine;
using UnityEngine.Rendering;

namespace BodycamHorror.Weapons
{
    /// <summary>
    /// The first-person arms with nothing in them: the melee slot. The pose brain
    /// (<see cref="PlaceholderArms"/>) says where each hand goes for the guard, punches and parkour;
    /// the arms reach for those points by IK and the fingers close into fists or open for a wall.
    /// </summary>
    [DefaultExecutionOrder(65)]   // after PlayerShooting (-100) and the free-hand pose (50)
    public class BareHands : MonoBehaviour
    {
        [Tooltip("Pose the arms are built in and rest in (the IK pulls them out of it).")]
        public AnimationClip idleClip;
        [Tooltip("Shoulders in weapon-holder space.")]
        public Vector3 leftShoulder = new Vector3(-0.17f, -0.25f, -0.06f);
        public Vector3 rightShoulder = new Vector3(0.17f, -0.25f, -0.06f);
        [Tooltip("How far the whole rig may move when a hand target is out of range (m).")]
        public float maxReachShift = 0.5f;
        public float curlSpeed = 14f;
        public float[] fistCurl = { 0.85f, 1f, 1f, 1f, 1f };
        public float[] openCurl = { 0.15f, 0.15f, 0.2f, 0.25f, 0.3f };

        [Header("Hanging (no melee: the arms hang at the sides and swing with the walk)")]
        [Tooltip("Hands hang this far in front of the shoulders (m): beside the thigh, as a relaxed arm does.")]
        public float hangForward = 0.04f;
        [Tooltip("And this far out to the side of them (m). 0 = the arms hang straight down, parallel to the torso.")]
        public float hangOut = 0.03f;
        [Tooltip("Shoulders moved this far out while hanging (m): the gun-holding rig has them narrow, inside the vest; out here the arms hang clear of the torso.")]
        public float hangShoulderOut = 0.12f;
        [Tooltip("Where the hand is aimed, in arm lengths from the shoulder. At 1 or more the arm hangs straight (the IK cannot reach further).")]
        [Range(0.7f, 1.1f)] public float hangDrop = 1.03f;
        [Tooltip("The first-person rig is sized for holding guns in front of the face; hanging at the sides it is scaled up by this to the body's arm length.")]
        public float hangArmScale = 1.3f;
        [Tooltip("Shoulders pulled back this far while hanging (m): the body stands behind the camera (PlayerBody.neckBehind), and the arms hang at its sides, in line with the torso, not in front of it. Looking down they show as they swing forward.")]
        public float hangShoulderBack = 0.18f;
        [Tooltip("Forward / back swing of each hand at a walk and at a sprint (m).")]
        public float swingWalk = 0.11f;
        public float swingSprint = 0.2f;
        public float hangBlendSpeed = 8f;
        [Tooltip("Finger curl while hanging (thumb first): relaxed, open, the fingers gently bent.")]
        public float[] hangCurl = { 0.5f, 0.15f, 0.18f, 0.22f, 0.26f };
        [Tooltip("The hanging hand turned in from thumb-forward (deg). This rig's palm sits ~45 deg off the thumb, so ~55 has the palm facing the torso.")]
        public float hangThumbTurn = 55f;

        BodycamCameraRig cameraRig;
        float hangW;

        PlayerShooting shooting;
        BodycamPlayerController player;
        PlaceholderArms pose;
        Transform leftTarget, rightTarget;
        RiggedArms otherArms;
        ArmRig rig;
        Vector3 rootBase;
        Quaternion rootRot = Quaternion.identity;
        Vector3 rootScale = Vector3.one;
        float armScale = 1f;   // how much the rig is scaled up right now (hanging)
        Transform clavLeft, clavRight;
        Vector3 clavLeftBase, clavRightBase;
        readonly float[] leftCurl = new float[5], rightCurl = new float[5];
        bool ready;

        /// <summary>Hooks the rig up to the player. Called once by whoever spawned it.</summary>
        public void Bind(BodycamPlayerController owner, PlayerShooting shooter, int layer)
        {
            player = owner;
            shooting = shooter;
            rootBase = transform.localPosition;
            rootRot = transform.localRotation;
            rootScale = transform.localScale;

            var animator = GetComponent<Animator>();
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            foreach (var t in GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
            foreach (var smr in GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                smr.updateWhenOffscreen = true;
                smr.shadowCastingMode = ShadowCastingMode.Off;
            }
            ViewModelLighting.MarkViewModel(gameObject);

            if (idleClip) idleClip.SampleAnimation(gameObject, 0f);
            rig = new ArmRig(transform);
            clavLeft = rig.Left.upper.parent != transform ? rig.Left.upper.parent : null;
            clavRight = rig.Right.upper.parent != transform ? rig.Right.upper.parent : null;
            if (clavLeft == clavRight) clavLeft = clavRight = null;   // a shared parent is not a collarbone
            if (clavLeft) clavLeftBase = clavLeft.localPosition;
            if (clavRight) clavRightBase = clavRight.localPosition;
            System.Array.Copy(openCurl, leftCurl, 5);
            System.Array.Copy(openCurl, rightCurl, 5);

            otherArms = owner ? owner.GetComponentInChildren<RiggedArms>(true) : null;
            pose = owner ? owner.GetComponentInChildren<PlaceholderArms>(true) : null;
            cameraRig = owner ? owner.GetComponentInChildren<BodycamCameraRig>(true) : null;
            ready = true;
            if (isActiveAndEnabled && otherArms) otherArms.RequestHidden(true);
        }

        void OnEnable()
        {
            if (ready && otherArms) otherArms.RequestHidden(true);
        }

        void OnDisable()
        {
            if (ready && otherArms) otherArms.RequestHidden(false);
        }

        void LateUpdate()
        {
            if (!ready || !pose) return;
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            if (!leftTarget) leftTarget = pose.transform.Find("LeftArm");     // built in the pose brain's Awake
            if (!rightTarget) rightTarget = pose.transform.Find("RightArm");
            if (!leftTarget || !rightTarget) return;

            Transform holder = transform.parent;
            transform.localPosition = rootBase;
            transform.localRotation = rootRot;
            bool parkour = player && !(player.State == MoveState.Grounded || player.State == MoveState.Airborne || player.State == MoveState.Sliding);

            // Without melee the arms hang at the sides. The rig is posed as if the camera looked straight ahead
            // (the look's pitch taken back out), so the hands stay down by the hips and show only looking down.
            bool hang = !shooting.meleeEnabled && !parkour && player && !player.IsBraceReady && player.cameraPivot;
            hangW = Mathf.MoveTowards(hangW, hang ? 1f : 0f, hangBlendSpeed * dt);
            float h = hangW * hangW * (3f - 2f * hangW);
            armScale = Mathf.Lerp(1f, hangArmScale, h);
            transform.localScale = rootScale * armScale;
            Quaternion unpitch = Quaternion.identity;
            Vector3 o = Vector3.zero;
            if (h > 0.001f)
            {
                Transform pivot = player.cameraPivot;
                unpitch = Quaternion.Slerp(Quaternion.identity, player.transform.rotation * Quaternion.Inverse(pivot.rotation), h);
                o = pivot.position;
                transform.SetPositionAndRotation(o + unpitch * (transform.position - o), unpitch * transform.rotation);
            }
            Vector3 Level(Vector3 p) => o + unpitch * (p - o);

            ArmRig.FreeTarget(rig.Left, leftTarget, out Vector3 leftPos, out Quaternion leftRot);
            ArmRig.FreeTarget(rig.Right, rightTarget, out Vector3 rightPos, out Quaternion rightRot);

            Vector3 back = player ? -player.transform.forward * (hangShoulderBack * h) : Vector3.zero;
            Vector3 outward = player ? player.transform.right * (hangShoulderOut * h) : Vector3.zero;
            // the collarbones go out with the shoulders, so the skin between them is not stretched
            if (clavLeft) { clavLeft.localPosition = clavLeftBase; clavLeft.position -= outward; }
            if (clavRight) { clavRight.localPosition = clavRightBase; clavRight.position += outward; }
            rig.Left.upper.position = Level(holder.TransformPoint(leftShoulder + shooting.ClimbLeftShoulderShift)) + back - outward;
            rig.Right.upper.position = Level(holder.TransformPoint(rightShoulder)) + back + outward;
            if (h > 0.001f)
            {
                HangTarget(rig.Left, -1f, out Vector3 hlPos, out Quaternion hlRot);
                HangTarget(rig.Right, 1f, out Vector3 hrPos, out Quaternion hrRot);
                leftPos = Vector3.Lerp(leftPos, hlPos, h); leftRot = Quaternion.Slerp(leftRot, hlRot, h);
                rightPos = Vector3.Lerp(rightPos, hrPos, h); rightRot = Quaternion.Slerp(rightRot, hrRot, h);
            }
            transform.position += holder.TransformVector(shooting.ClimbArmsShift);
            Vector3 reachDir = ArmRig.ReachDirection(holder);
            float shift = Mathf.Max(ArmRig.ReachShift(rig.Left, leftPos, reachDir), ArmRig.ReachShift(rig.Right, rightPos, reachDir));
            transform.position += reachDir * (Mathf.Min(shift, maxReachShift) * (1f - h));

            Vector3 elbowL = ArmRig.ElbowHint(rig.Left, holder), elbowR = ArmRig.ElbowHint(rig.Right, holder);
            if (h > 0.001f)
            {
                // hanging, the elbows point back and a little out
                Vector3 bodyBack = -player.transform.forward, bodyRight = player.transform.right;
                elbowL = Vector3.Lerp(elbowL, rig.Left.upper.position + bodyBack * 0.5f - bodyRight * 0.15f + Vector3.down * 0.3f, h);
                elbowR = Vector3.Lerp(elbowR, rig.Right.upper.position + bodyBack * 0.5f + bodyRight * 0.15f + Vector3.down * 0.3f, h);
            }
            ArmRig.Solve(rig.Left, leftPos, leftRot, elbowL);
            ArmRig.Solve(rig.Right, rightPos, rightRot, elbowR);

            bool fists = !parkour && !(player && player.IsBraceReady) && (shooting.GuardWeight > 0.3f || shooting.PunchExtend > 0.05f);
            float[] want = fists ? fistCurl : h > 0.5f ? hangCurl : openCurl;
            float k = 1f - Mathf.Exp(-curlSpeed * dt);
            for (int f = 0; f < 5; f++)
            {
                leftCurl[f] = Mathf.Lerp(leftCurl[f], want[f], k);
                rightCurl[f] = Mathf.Lerp(rightCurl[f], want[f], k);
            }
            ArmRig.SetCurl(rig.Left, leftCurl);
            ArmRig.SetCurl(rig.Right, rightCurl);
        }

        /// <summary>
        /// Where a hanging hand goes: below its shoulder at about the arm's length, a little forward and out,
        /// swinging forward and back against the legs with the camera's steps (the right arm forward as the left
        /// heel comes down), the back of the hand turned out.
        /// </summary>
        void HangTarget(ArmRig.Arm a, float side, out Vector3 position, out Quaternion rotation)
        {
            Quaternion yaw = player.transform.rotation;
            float phase = cameraRig ? cameraRig.StepPhase : 0f;
            float walking = cameraRig ? Mathf.Clamp01(cameraRig.BobAmount) : 0f;
            float amp = Mathf.Lerp(swingWalk, swingSprint, player.SprintBlend) * walking;
            float swing = Mathf.Cos(phase) * amp * side;
            Vector3 across = yaw * new Vector3(side * hangOut, 0f, hangForward + swing);
            float len = (a.upperLen + a.lowerLen) * armScale * hangDrop;   // the lengths were measured at the rig's own size
            float drop = Mathf.Sqrt(Mathf.Max(0.01f, len * len - across.sqrMagnitude));
            Vector3 shoulder = a.upper.position;
            Vector3 hand = shoulder + across + Vector3.down * drop;
            Vector3 along = (hand - shoulder).normalized;
            // the thumb forward, so the palm faces the thigh, turned in a little as a relaxed hand is (the palm a
            // touch back). For this rig a target's up points away from the thumb, hence the minus.
            Vector3 thumb = yaw * Quaternion.Euler(0f, -side * hangThumbTurn, 0f) * Vector3.forward;
            rotation = Quaternion.LookRotation(along, -thumb) * a.handFrame;
            position = hand + along * 0.02f - rotation * (a.palmCenter * armScale);
        }
    }
}
