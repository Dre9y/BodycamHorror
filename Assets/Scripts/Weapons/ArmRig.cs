using System.Collections.Generic;
using UnityEngine;

namespace BodycamHorror.Weapons
{
    /// <summary>
    /// The arm bones of the first-person rig in Assets/Shotgun (UpArm_L, Forearm_L, Hand_L, BoneTwist_03.L,
    /// Bone_L.005...) and the IK that places them. Each hand is its own bone, not a child of the forearm,
    /// so the IK brings the forearm to the hand. Build it while the rig is in a held pose (any clip frame):
    /// the hand frames come from the knuckles.
    /// </summary>
    public class ArmRig
    {
        public class Arm
        {
            public Transform upper, lower, hand, forearmEnd;
            /// <summary>Forearm twist bones, elbow to wrist.</summary>
            public Transform[] twists;
            /// <summary>Twist bones and the hand relative to the forearm in the pose the rig was built in.
            /// Clips leave constant bones alone, so last frame's solved values are not a usable starting point.</summary>
            public Quaternion[] twistRel;
            public Quaternion handRel;
            public float upperLen, lowerLen;
            /// <summary>-1 for the left arm, +1 for the right.</summary>
            public float side;
            /// <summary>Hand rotation relative to a frame of (fingers forward, back of the hand up).</summary>
            public Quaternion handFrame;
            /// <summary>Middle of the palm in metres, relative to the hand position and rotation.</summary>
            public Vector3 palmCenter;
            /// <summary>Thumb, index, middle, ring, pinky: three joints each, in that order.</summary>
            public Transform[] fingers;
            /// <summary>The end point of each finger.</summary>
            public Transform[] fingerEnds;
            public Quaternion[] fingersOpen;
            public Vector3[] curlAxes;
        }

        public const int FingerCount = 5;
        public const int JointsPerFinger = 3;
        static readonly int[] FingerBase = { 20, 5, 9, 13, 17 };   // Bone_L.0NN: thumb, index, middle, ring, pinky

        static readonly Vector3 FingerCurlAngles = new Vector3(75f, 95f, 65f);
        static readonly Vector3 ThumbCurlAngles = new Vector3(25f, 40f, 45f);

        public readonly Arm Left, Right;
        readonly Dictionary<string, Transform> bones = new Dictionary<string, Transform>();
        readonly SkinnedMeshRenderer skin;

        public ArmRig(Transform root)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (!t.GetComponent<SkinnedMeshRenderer>()) bones[t.name] = t;
            foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (smr.name == "mesh") skin = smr;
            Left = BuildArm("L", -1f);
            Right = BuildArm("R", 1f);
        }

        public Transform Bone(string boneName)
        {
            if (bones.TryGetValue(boneName, out Transform t)) return t;
            Debug.LogWarning($"[ArmRig] Bone {boneName} not found.");
            return null;
        }

        Transform FingerBone(string side, int number, string suffix = "") => Bone($"Bone_{side}.{number:000}{suffix}");

        Arm BuildArm(string side, float sign)
        {
            var a = new Arm
            {
                side = sign,
                // the control bone, not Hand_x: the clips rewrite it every frame, while Hand_x sits constant under it
                upper = Bone("UpArm_" + side), lower = Bone("Forearm_" + side), hand = Bone("IK_Hand_Cntrl_" + side),
                forearmEnd = Bone($"BoneTwist_01.{side}_end"),
                twists = new[] { Bone($"BoneTwist_03.{side}"), Bone($"BoneTwist_02.{side}"), Bone($"BoneTwist_01.{side}") },
                twistRel = new Quaternion[3],
                fingers = new Transform[FingerCount * JointsPerFinger],
                fingerEnds = new Transform[FingerCount],
                fingersOpen = new Quaternion[FingerCount * JointsPerFinger],
                curlAxes = new Vector3[FingerCount * JointsPerFinger],
            };
            a.upperLen = Vector3.Distance(a.upper.position, a.lower.position);
            a.lowerLen = Vector3.Distance(a.lower.position, a.forearmEnd.position);
            for (int i = 0; i < a.twists.Length; i++) a.twistRel[i] = Quaternion.Inverse(a.lower.rotation) * a.twists[i].rotation;
            a.handRel = Quaternion.Inverse(a.lower.rotation) * a.hand.rotation;

            for (int f = 0; f < FingerCount; f++)
            {
                for (int j = 0; j < JointsPerFinger; j++) a.fingers[f * JointsPerFinger + j] = FingerBone(side, FingerBase[f] + j);
                a.fingerEnds[f] = FingerBone(side, FingerBase[f] + JointsPerFinger - 1, "_end");
            }

            // hand frame from the knuckles: fingers forward, back of the hand up (opposite the way the fingers bend)
            Transform mid = a.fingers[2 * JointsPerFinger], midTip = a.fingers[2 * JointsPerFinger + 2];
            Transform idx = a.fingers[1 * JointsPerFinger], pinky = a.fingers[4 * JointsPerFinger];
            Vector3 palm = PalmDirection(a.hand.position, mid.position, idx.position, pinky.position, midTip.position);
            Vector3 fwd = (mid.position - a.hand.position).normalized;
            a.handFrame = Quaternion.Inverse(Quaternion.LookRotation(fwd, -palm)) * a.hand.rotation;
            a.palmCenter = Quaternion.Inverse(a.hand.rotation) * (Vector3.Lerp(a.hand.position, mid.position, 0.6f) + palm * 0.012f - a.hand.position);

            ReadBindPose(a, side);
            return a;
        }

        /// <summary>The direction the palm faces: across the knuckles, on the side the fingers curl towards.</summary>
        public static Vector3 PalmDirection(Vector3 wrist, Vector3 middle, Vector3 index, Vector3 pinky, Vector3 middleTip)
        {
            Vector3 fwd = (middle - wrist).normalized;
            Vector3 palm = Vector3.Cross(fwd, (index - pinky).normalized).normalized;
            return Vector3.Dot(Vector3.ProjectOnPlane(middleTip - middle, fwd), palm) < 0f ? -palm : palm;
        }

        /// <summary>Open-hand rotations and curl axes, from where the mesh was bound.</summary>
        void ReadBindPose(Arm a, string side)
        {
            for (int i = 0; i < a.fingers.Length; i++) a.fingersOpen[i] = a.fingers[i].localRotation;
            if (!skin) return;

            Transform[] skinBones = skin.bones;
            Matrix4x4[] bind = skin.sharedMesh.bindposes;
            Matrix4x4 World(Transform t) => bind[System.Array.IndexOf(skinBones, t)].inverse;
            Vector3 Pos(Transform t) => World(t).GetColumn(3);

            Vector3 palm = PalmDirection(Pos(a.hand), Pos(a.fingers[6]), Pos(a.fingers[3]), Pos(a.fingers[12]), Pos(a.fingers[8]));
            for (int f = 0; f < FingerCount; f++)
            {
                for (int j = 0; j < JointsPerFinger; j++)
                {
                    int i = f * JointsPerFinger + j;
                    Matrix4x4 m = World(a.fingers[i]);
                    a.fingersOpen[i] = (bind[System.Array.IndexOf(skinBones, a.fingers[i].parent)] * m).rotation;
                    // the outermost joint has no joint past it in the bind data: it carries on the way the one before went
                    int from = j < JointsPerFinger - 1 ? i : i - 1;
                    Vector3 dir = (Pos(a.fingers[from + 1]) - Pos(a.fingers[from])).normalized;
                    a.curlAxes[i] = (Quaternion.Inverse(m.rotation) * Vector3.Cross(dir, palm)).normalized;
                }
            }
        }

        /// <summary>Where a hand goes to sit on a free-hand target from the pose brain (forearm along its
        /// forward, back of the hand along its up).</summary>
        public static void FreeTarget(Arm a, Transform target, out Vector3 position, out Quaternion rotation)
        {
            rotation = Quaternion.LookRotation(target.forward, target.up) * a.handFrame;
            position = target.position + target.forward * 0.02f - rotation * a.palmCenter;
        }

        /// <summary>The way the whole rig moves to reach further: forward and down, so the open ends of
        /// the sleeves at the shoulders stay below the frame and the arms come up from under it.</summary>
        public static Vector3 ReachDirection(Transform holder) => (holder.forward * 0.8f - holder.up * 0.6f).normalized;

        /// <summary>How far the whole rig has to move along <paramref name="direction"/> for this arm to reach
        /// its target. The rig moves as one piece: pulling a shoulder alone tears the skin around it.</summary>
        public static float ReachShift(Arm a, Vector3 target, Vector3 direction)
        {
            float reach = (a.upperLen + a.lowerLen) * 0.999f;
            Vector3 to = target - a.upper.position;
            if (to.sqrMagnitude <= reach * reach) return 0f;
            float along = Vector3.Dot(to, direction);
            float disc = along * along - (to.sqrMagnitude - reach * reach);
            return Mathf.Max(0f, disc > 0f ? along - Mathf.Sqrt(disc) : along);
        }

        /// <summary>What to add to a target this arm cannot reach so it ends up at the end of the arm.
        /// Applied to the gun as well, a pose the arm cannot get to takes the gun with the hand.</summary>
        public static Vector3 ReachCorrection(Arm a, Vector3 target)
        {
            float reach = (a.upperLen + a.lowerLen) * 0.999f;
            Vector3 to = target - a.upper.position;
            return to.sqrMagnitude <= reach * reach ? Vector3.zero : to.normalized * reach - to;
        }

        /// <summary>A point the elbow should bend towards: down and away from the body.</summary>
        public static Vector3 ElbowHint(Arm a, Transform holder) =>
            a.upper.position + holder.right * (a.side * 0.5f) - holder.up;

        /// <summary>Two-bone IK that bends the arm towards <paramref name="elbowHint"/>, puts the hand on
        /// the target and shares the turn of the wrist out along the forearm twist bones.</summary>
        public static void Solve(Arm a, Vector3 target, Quaternion targetRot, Vector3 elbowHint)
        {
            float reach = a.upperLen + a.lowerLen;
            Vector3 shoulder = a.upper.position;
            Vector3 toTarget = target - shoulder;
            float d = Mathf.Clamp(toTarget.magnitude, Mathf.Abs(a.upperLen - a.lowerLen) + 0.001f, reach * 0.999f);
            Vector3 dir = toTarget.normalized;
            // out of reach: the hand stops at the end of the arm. Left on the target it would tear away from the sleeve
            if (toTarget.magnitude > d) target = shoulder + dir * d;
            Vector3 bend = Vector3.ProjectOnPlane(elbowHint - shoulder, dir);
            bend = bend.sqrMagnitude > 1e-8f ? bend.normalized : Vector3.down;

            float along = (d * d + a.upperLen * a.upperLen - a.lowerLen * a.lowerLen) / (2f * d);
            float height = Mathf.Sqrt(Mathf.Max(a.upperLen * a.upperLen - along * along, 0f));
            Vector3 elbow = shoulder + dir * along + bend * height;

            a.upper.rotation = Quaternion.FromToRotation(a.lower.position - shoulder, elbow - shoulder) * a.upper.rotation;
            a.lower.rotation = Quaternion.FromToRotation(a.forearmEnd.position - a.lower.position, target - a.lower.position) * a.lower.rotation;
            a.hand.SetPositionAndRotation(target, targetRot);

            // hand turned about the forearm: share that twist out along the forearm bones
            Quaternion delta = (Quaternion.Inverse(a.lower.rotation) * targetRot) * Quaternion.Inverse(a.handRel);
            Vector3 axis = Quaternion.Inverse(a.lower.rotation) * (a.forearmEnd.position - a.lower.position).normalized;
            Vector3 v = Vector3.Project(new Vector3(delta.x, delta.y, delta.z), axis);
            Quaternion twist = new Quaternion(v.x, v.y, v.z, delta.w);
            float m = Mathf.Sqrt(twist.x * twist.x + twist.y * twist.y + twist.z * twist.z + twist.w * twist.w);
            twist = m > 1e-5f ? new Quaternion(twist.x / m, twist.y / m, twist.z / m, twist.w / m) : Quaternion.identity;
            for (int i = 0; i < a.twists.Length; i++)
                a.twists[i].rotation = a.lower.rotation * Quaternion.Slerp(Quaternion.identity, twist, (i + 1f) / a.twists.Length) * a.twistRel[i];
        }

        /// <summary>Blends the fingers towards a flat open hand (the clips only ever hold them around a gun).</summary>
        public static void BlendOpen(Arm a, float weight)
        {
            for (int i = 0; i < a.fingers.Length; i++)
                a.fingers[i].localRotation = Quaternion.Slerp(a.fingers[i].localRotation, a.fingersOpen[i], weight);
        }

        /// <summary>Poses the fingers from curl amounts (0 open, 1 closed): thumb, index, middle, ring, pinky.</summary>
        public static void SetCurl(Arm a, float[] curl)
        {
            for (int f = 0; f < FingerCount; f++)
            {
                Vector3 angles = f == 0 ? ThumbCurlAngles : FingerCurlAngles;
                for (int j = 0; j < JointsPerFinger; j++)
                {
                    int i = f * JointsPerFinger + j;
                    a.fingers[i].localRotation = a.fingersOpen[i] * Quaternion.AngleAxis(curl[f] * angles[j], a.curlAxes[i]);
                }
            }
        }
    }
}
