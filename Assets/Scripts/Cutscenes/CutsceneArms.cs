using BodycamHorror.Weapons;
using UnityEngine;

namespace BodycamHorror.Cutscenes
{
    /// <summary>
    /// The player's own first-person arms (BareArms: the Arms rig inside PistolArms.prefab) on the pistol in a
    /// cutscene, without a player or a weapon behind them. The PistolArms clips play on their hidden source
    /// rig; every frame the arms reach for its hands by IK and copy its fingers, the way
    /// <see cref="AnimatedHands"/> does in the game (same <see cref="ArmRig"/> maths, same settings, read off
    /// the prefab's AnimatedHands, which stays switched off). The shoulders are held to the camera.
    /// Runs after the cutscene has moved the rig for the frame.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public class CutsceneArms : MonoBehaviour
    {
        [Tooltip("The PistolArms prefab instance's AnimatedHands: where the settings and the arms are read from.")]
        public AnimatedHands source;
        [Tooltip("The camera the shoulders hang from (the game's weapon holder is the camera).")]
        public Transform holder;

        class SourceHand
        {
            public Transform hand;
            public Vector3 palmCenter;
            public Quaternion toTarget;
            public Transform[] joints;
        }

        static readonly string[] SourceFingers = { "Thumb", "Index", "Middle", "Ring", "Pinky" };

        Transform armsRoot, gunBase, gunSlide, gunMag;
        Vector3 armsBase, baseScale, slideScale, magScale;
        ArmRig rig;
        SourceHand srcLeft, srcRight;
        bool ready;

        void Start()
        {
            if (!source || !holder || !source.armsRoot) { enabled = false; return; }
            source.enabled = false;   // needs a player and a weapon; this stands in for it
            armsRoot = source.armsRoot;

            // build from the poses both rigs were made in, as the game does
            if (source.idleClip) source.idleClip.SampleAnimation(source.gameObject, 0f);
            if (source.armsIdleClip) source.armsIdleClip.SampleAnimation(armsRoot.gameObject, 0f);
            var armsAnimator = armsRoot.GetComponent<Animator>();
            if (armsAnimator) armsAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            rig = new ArmRig(armsRoot);
            armsBase = armsRoot.localPosition;

            gunBase = SourceBone("Base");
            gunSlide = SourceBone("Up");
            gunMag = SourceBone("Mag");
            baseScale = gunBase.localScale;
            slideScale = gunSlide.localScale;
            magScale = gunMag.localScale;
            srcLeft = BuildSource(".L", rig.Left);
            srcRight = BuildSource(".R", rig.Right);

            foreach (var smr in source.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                smr.updateWhenOffscreen = true;
            ready = true;
        }

        Transform SourceBone(string boneName)
        {
            foreach (var t in source.GetComponentsInChildren<Transform>(true))
                if (t.name == boneName && !t.IsChildOf(armsRoot)) return t;
            Debug.LogWarning($"[CutsceneArms] Bone {boneName} not found.", this);
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

        void LateUpdate()
        {
            if (!ready) return;
            armsRoot.localPosition = armsBase;
            ArmRig.Arm left = rig.Left, right = rig.Right;
            Target(srcRight, right, out Vector3 rightPos, out Quaternion rightRot);
            Target(srcLeft, left, out Vector3 leftPos, out Quaternion leftRot);
            Vector3 basePos = gunBase.position, slidePos = gunSlide.position, magPos = gunMag.position;
            Quaternion baseRot = gunBase.rotation, slideRot = gunSlide.rotation, magRot = gunMag.rotation;

            left.upper.position = holder.TransformPoint(source.leftShoulder);
            right.upper.position = holder.TransformPoint(source.rightShoulder);
            Vector3 reachDir = ArmRig.ReachDirection(holder);
            float shift = Mathf.Max(ArmRig.ReachShift(left, leftPos, reachDir), ArmRig.ReachShift(right, rightPos, reachDir));
            armsRoot.position += reachDir * Mathf.Min(shift, source.maxReachShift);

            // a pose the right arm cannot get to: the gun stays in the hand and comes along
            Vector3 gunFix = ArmRig.ReachCorrection(right, rightPos);
            rightPos += gunFix;
            leftPos += gunFix;

            // the gun at its own size, scaled about the firing palm
            Vector3 grip = srcRight.hand.position + srcRight.hand.rotation * srcRight.palmCenter;
            float k = source.gunScale;
            Vector3 AtSize(Vector3 p) => grip + (p - grip) * k + gunFix;
            gunBase.localScale = baseScale * k;
            gunSlide.localScale = slideScale * (gunSlide.IsChildOf(gunBase) ? 1f : k);
            gunMag.localScale = magScale * (gunMag.IsChildOf(gunBase) || gunMag.IsChildOf(gunSlide) ? 1f : k);
            gunBase.SetPositionAndRotation(AtSize(basePos), baseRot);
            gunSlide.SetPositionAndRotation(AtSize(slidePos), slideRot);
            gunMag.SetPositionAndRotation(AtSize(magPos), magRot);

            ArmRig.Solve(left, leftPos, leftRot, ArmRig.ElbowHint(left, holder));
            ArmRig.Solve(right, rightPos, rightRot, ArmRig.ElbowHint(right, holder));
            CopyFingers(srcRight, right);
            CopyFingers(srcLeft, left);
        }

        static void Target(SourceHand s, ArmRig.Arm a, out Vector3 position, out Quaternion rotation)
        {
            rotation = s.hand.rotation * s.toTarget;
            Vector3 palm = s.hand.position + s.hand.rotation * s.palmCenter;
            position = palm - rotation * a.palmCenter;
        }

        static void CopyFingers(SourceHand s, ArmRig.Arm a)
        {
            for (int f = 0; f < ArmRig.FingerCount; f++)
                for (int j = 0; j < ArmRig.JointsPerFinger; j++)
                {
                    int i = f * ArmRig.JointsPerFinger + j;
                    Transform joint = a.fingers[i];
                    Vector3 want = j < ArmRig.JointsPerFinger - 1 ? s.joints[i + 1].position - s.joints[i].position : s.joints[i].up;
                    Vector3 end = j < ArmRig.JointsPerFinger - 1 ? a.fingers[i + 1].position : a.fingerEnds[f].position;
                    joint.rotation = Quaternion.FromToRotation(end - joint.position, want) * joint.rotation;
                }
        }
    }
}
