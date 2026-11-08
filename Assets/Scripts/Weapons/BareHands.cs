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

        PlayerShooting shooting;
        BodycamPlayerController player;
        PlaceholderArms pose;
        Transform leftTarget, rightTarget;
        RiggedArms otherArms;
        ArmRig rig;
        Vector3 rootBase;
        readonly float[] leftCurl = new float[5], rightCurl = new float[5];
        bool ready;

        /// <summary>Hooks the rig up to the player. Called once by whoever spawned it.</summary>
        public void Bind(BodycamPlayerController owner, PlayerShooting shooter, int layer)
        {
            player = owner;
            shooting = shooter;
            rootBase = transform.localPosition;

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
            System.Array.Copy(openCurl, leftCurl, 5);
            System.Array.Copy(openCurl, rightCurl, 5);

            otherArms = owner ? owner.GetComponentInChildren<RiggedArms>(true) : null;
            pose = owner ? owner.GetComponentInChildren<PlaceholderArms>(true) : null;
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
            ArmRig.FreeTarget(rig.Left, leftTarget, out Vector3 leftPos, out Quaternion leftRot);
            ArmRig.FreeTarget(rig.Right, rightTarget, out Vector3 rightPos, out Quaternion rightRot);

            rig.Left.upper.position = holder.TransformPoint(leftShoulder);
            rig.Right.upper.position = holder.TransformPoint(rightShoulder);
            Vector3 reachDir = ArmRig.ReachDirection(holder);
            float shift = Mathf.Max(ArmRig.ReachShift(rig.Left, leftPos, reachDir), ArmRig.ReachShift(rig.Right, rightPos, reachDir));
            transform.position += reachDir * Mathf.Min(shift, maxReachShift);

            ArmRig.Solve(rig.Left, leftPos, leftRot, ArmRig.ElbowHint(rig.Left, holder));
            ArmRig.Solve(rig.Right, rightPos, rightRot, ArmRig.ElbowHint(rig.Right, holder));

            bool parkour = player && !(player.State == MoveState.Grounded || player.State == MoveState.Airborne || player.State == MoveState.Sliding);
            bool fists = !parkour && !(player && player.IsBraceReady) && (shooting.GuardWeight > 0.3f || shooting.PunchExtend > 0.05f);
            float[] want = fists ? fistCurl : openCurl;
            float k = 1f - Mathf.Exp(-curlSpeed * dt);
            for (int f = 0; f < 5; f++)
            {
                leftCurl[f] = Mathf.Lerp(leftCurl[f], want[f], k);
                rightCurl[f] = Mathf.Lerp(rightCurl[f], want[f], k);
            }
            ArmRig.SetCurl(rig.Left, leftCurl);
            ArmRig.SetCurl(rig.Right, rightCurl);
        }
    }
}
