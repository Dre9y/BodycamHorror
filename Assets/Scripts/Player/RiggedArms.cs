using BodycamHorror.Weapons;
using UnityEngine;
using UnityEngine.Rendering;

namespace BodycamHorror.Player
{
    /// <summary>
    /// Skinned first-person arms driven by IK. <see cref="PlaceholderArms"/> stays the "pose brain"
    /// (where each hand should be for running, parkour, guard, punches, reloads); this puts a real
    /// rigged pair of arms on those targets: two-bone IK shoulder-elbow-wrist with elbow poles,
    /// a little shoulder reach when a target is out of range, forearm twist so the wrist doesn't
    /// candy-wrap, and finger curls for each grip. With a gun the hands wrap its grip and fore-end
    /// in weapon space, so they follow every recoil, sway, pump and reload the weapon does.
    /// Bone names follow the rig in Assets/arms (L_/R_ arm, elbow, wrist, thumb/point/middle/ring/pink 1-4).
    /// </summary>
    [DefaultExecutionOrder(60)]   // after PlaceholderArms (50) has worked out the hand targets
    public class RiggedArms : MonoBehaviour
    {
        [System.Serializable]
        public class GripPose
        {
            [Tooltip("The part the hand wraps (grip / fore-end / the other hand) as a capsule, in weapon space relative to the grip point.")]
            public Vector3 axisA, axisB;
            public float radius = 0.018f;
            [Range(0f, 1f), Tooltip("Where along the capsule the palm sits.")]
            public float along = 0.45f;
            [Tooltip("Direction from the wrist to the knuckles, in weapon space (across the capsule).")]
            public Vector3 forward = Vector3.forward;
            [Tooltip("Direction the back of the hand faces, in weapon space; the palm rests on the capsule on the opposite side.")]
            public Vector3 back = Vector3.right;
            [Tooltip("Extra shift of the hand, in weapon space.")]
            public Vector3 offset;
            [Tooltip("Max curl 0..1: thumb, index, middle, ring, pinky. Fingers stop earlier when they touch the capsule.")]
            public float[] curl = { 0.6f, 1f, 1f, 1f, 1f };
            [Tooltip("Where the thumb points along the weapon (weapon space). Zero = just curl it.")]
            public Vector3 thumbAim;
            [Tooltip("Push the thumb out of the capsule (off for a capsule that stands for the other hand).")]
            public bool thumbStaysOut = true;
            [Tooltip("Point the thumb tip goes to, weapon space relative to the grip point (overrides Thumb Aim). Zero = off.")]
            public Vector3 thumbPoint;
            [Tooltip("Trigger finger: where the pad of the index finger rests (the trigger face), weapon space relative to the grip point. Zero = index just wraps like the others.")]
            public Vector3 indexPoint;
            [Tooltip("Bend of the index finger's two outer joints while it rests on the trigger (0..1).")]
            public float indexBend = 0.35f;
        }

        [Header("References (found automatically)")]
        public PlaceholderArms poseSource;
        public PlayerShooting shooting;
        public BodycamPlayerController player;
        [Tooltip("Root of the imported arms model (bones are found under it by name).")]
        public Transform rigRoot;

        [Header("IK")]
        [Tooltip("Elbow hints in rig-root space (taken from the rig's *_arm_Pole when empty).")]
        public Vector3 leftPole = new Vector3(-0.34f, -0.1f, -0.08f);
        public Vector3 rightPole = new Vector3(0.36f, -0.16f, -0.14f);
        [Tooltip("How far a shoulder may reach forward when a hand target is out of range (m).")]
        public float maxShoulderReach = 0.2f;
        [Range(0f, 1f)] public float forearmTwist = 0.5f;
        public float poseSharpness = 18f;

        [Header("Gun grips (weapon space)")]
        public GripPose pistolRight = new GripPose
        {
            axisA = new Vector3(0f, 0.03f, -0.004f), axisB = new Vector3(0f, -0.045f, -0.024f), radius = 0.018f, along = 0.3f,
            forward = new Vector3(0f, 0.3f, 1f), back = new Vector3(1f, 0.1f, -0.15f), curl = new[] { 0.5f, 0.25f, 1f, 1f, 1f },
            offset = new Vector3(0f, -0.006f, -0.03f),
            thumbAim = new Vector3(-0.1f, -0.15f, 1f),
            thumbPoint = new Vector3(-0.02f, 0.046f, 0.055f),
            indexPoint = new Vector3(0.002f, 0.036f, 0.046f), indexBend = 0.2f,
        };
        public GripPose pistolLeft = new GripPose
        {
            // wraps the right hand's fingers on the grip
            axisA = new Vector3(0.036f, 0.045f, 0.002f), axisB = new Vector3(0.036f, -0.025f, -0.016f), radius = 0.034f, along = 0.3f,
            forward = new Vector3(0.15f, -0.45f, 1f), back = new Vector3(-1f, -0.15f, 0.1f), curl = new[] { 0.35f, 1f, 1f, 1f, 1f },
            thumbAim = new Vector3(-0.1f, -0.2f, 1f), thumbStaysOut = false,
        };
        public GripPose shotgunRight = new GripPose
        {
            axisA = new Vector3(0f, 0.03f, 0.022f), axisB = new Vector3(0f, -0.035f, -0.016f), radius = 0.017f, along = 0.35f,
            forward = new Vector3(0f, 0.3f, 1f), back = new Vector3(1f, 0.1f, -0.15f), curl = new[] { 0.5f, 0.25f, 1f, 1f, 1f },
            thumbAim = new Vector3(-0.8f, 0.1f, 0.45f),
            thumbPoint = new Vector3(-0.02f, 0.035f, 0.06f),
            indexPoint = new Vector3(0.003f, 0.007f, 0.098f), indexBend = 0.35f,
        };
        public GripPose shotgunLeft = new GripPose
        {
            // under the fore-end, fingers wrapping up over its right side
            axisA = new Vector3(0.034f, 0.022f, -0.08f), axisB = new Vector3(0.034f, 0.022f, 0.08f), radius = 0.021f, along = 0.3f,
            forward = new Vector3(0.6f, 0.25f, 0.75f), back = new Vector3(-0.6f, -0.8f, 0f), curl = new[] { 0.6f, 1f, 1f, 1f, 1f },
            thumbAim = new Vector3(0.1f, 0.3f, 1f),
        };

        [Header("Free-hand finger curls (thumb, index, middle, ring, pinky)")]
        public float[] fistCurl = { 0.85f, 1f, 1f, 1f, 1f };
        public float[] openCurl = { 0.15f, 0.15f, 0.2f, 0.25f, 0.3f };
        public float[] holdCurl = { 0.45f, 0.55f, 0.6f, 0.6f, 0.65f };   // magazine / shell in the hand
        [Tooltip("Index finger curl while the trigger is being pulled.")]
        public float triggerPullCurl = 0.55f;

        [Header("Finger joint angles at full curl (deg)")]
        public Vector3 fingerCurlAngles = new Vector3(75f, 95f, 65f);
        public Vector3 thumbCurlAngles = new Vector3(25f, 40f, 45f);

        static readonly string[] FingerNames = { "thumb", "point", "middle", "ring", "pink" };

        class Finger
        {
            public Transform[] joints = new Transform[3];
            public Quaternion[] bind = new Quaternion[3];
            public Vector3[] axis = new Vector3[3];
            public Transform tip;
            public float curl;
        }

        /// <summary>What a hand wraps this frame, in world space.</summary>
        struct Capsule
        {
            public Vector3 a, b; public float r; public bool on;
            public Vector3 thumb; public bool thumbOut; public Vector3 thumbPoint; public bool hasThumbPoint;
            public Vector3 index; public bool hasIndex; public float indexBend;
        }

        class Arm
        {
            public Transform upper, elbow, wrist;
            public Quaternion upperBind, elbowBind, wristBind;
            public Vector3 upperBindPos;
            public float upperLen, foreLen;
            public Quaternion handOffset;     // wrist rotation relative to the hand frame (fwd, back)
            public Vector3 palmCenter;        // middle of the palm surface, in wrist space
            public Finger[] fingers = new Finger[5];
            public Quaternion handRot; public Vector3 handPos; public bool hasPose;
            public bool ok;
        }

        Arm left, right;
        Renderer[] placeholderRenderers = new Renderer[0];
        Transform placeholderL, placeholderR;
        float lastShotTime = -10f;
        Firearm subscribed;

        void Awake() => Init();

        /// <summary>Finds the bones and records the bind pose. Runs again after a script reload in Play Mode.</summary>
        void Init()
        {
            if (!player) player = GetComponentInParent<BodycamPlayerController>();
            if (!poseSource && player) poseSource = player.GetComponentInChildren<PlaceholderArms>();
            if (!shooting && player) shooting = player.GetComponentInChildren<PlayerShooting>();
            if (!rigRoot && transform.childCount > 0) rigRoot = transform.GetChild(0);

            if (rigRoot)
            {
                var lp = Find(rigRoot, "L_arm_Pole"); if (lp) leftPole = rigRoot.InverseTransformPoint(lp.position);
                var rp = Find(rigRoot, "R_arm_Pole"); if (rp) rightPole = rigRoot.InverseTransformPoint(rp.position);
                left = BuildArm("L_");
                right = BuildArm("R_");
                foreach (var smr in rigRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    smr.updateWhenOffscreen = true;
                    smr.shadowCastingMode = ShadowCastingMode.Off;
                }
                ViewModelLighting.MarkViewModel(rigRoot.gameObject);   // lit like the guns, not by the flashlight beam
            }
            if (poseSource)
            {
                placeholderL = poseSource.transform.Find("LeftArm");
                placeholderR = poseSource.transform.Find("RightArm");
                placeholderRenderers = poseSource.GetComponentsInChildren<Renderer>(true);
            }
        }

        void OnEnable() { foreach (var r in placeholderRenderers) if (r) r.enabled = false; }

        void OnDisable()
        {
            foreach (var r in placeholderRenderers) if (r) r.enabled = true;
            if (subscribed) subscribed.Fired -= OnFired;
            subscribed = null;
        }

        void OnFired(bool hit) => lastShotTime = Time.time;

        // ------------------------------------------------------------------ setup

        Arm BuildArm(string p)
        {
            var a = new Arm
            {
                upper = Find(rigRoot, p + "arm"),
                elbow = Find(rigRoot, p + "elbow"),
                wrist = Find(rigRoot, p + "wrist"),
            };
            if (!a.upper || !a.elbow || !a.wrist)
            {
                Debug.LogWarning($"[RiggedArms] Bones {p}arm / {p}elbow / {p}wrist not found under {rigRoot.name}.", this);
                return a;
            }
            a.upperBind = a.upper.localRotation; a.elbowBind = a.elbow.localRotation; a.wristBind = a.wrist.localRotation;
            a.upperBindPos = a.upper.localPosition;
            a.upperLen = Vector3.Distance(a.upper.position, a.elbow.position);
            a.foreLen = Vector3.Distance(a.elbow.position, a.wrist.position);

            Transform mid = Find(rigRoot, p + "middle1"), idx = Find(rigRoot, p + "point1"), pink = Find(rigRoot, p + "pink1");
            Transform mid3 = Find(rigRoot, p + "middle3");
            Vector3 fwd = ((mid ? mid.position : a.wrist.position + a.wrist.forward * 0.1f) - a.wrist.position).normalized;
            Vector3 across = idx && pink ? (idx.position - pink.position).normalized : a.wrist.right;
            Vector3 palm = Vector3.Cross(fwd, across).normalized;
            // fingers in the bind pose already bend a little towards the palm: use that to pick the side
            if (mid && mid3)
            {
                Vector3 bend = Vector3.ProjectOnPlane(mid3.position - mid.position, fwd);
                if (Vector3.Dot(bend, palm) < 0f) palm = -palm;
            }
            Quaternion frame = Quaternion.LookRotation(fwd, -palm);
            a.handOffset = Quaternion.Inverse(frame) * a.wrist.rotation;
            Vector3 palmPoint = Vector3.Lerp(a.wrist.position, mid ? mid.position : a.wrist.position, 0.6f) + palm * 0.012f;
            a.palmCenter = a.wrist.InverseTransformPoint(palmPoint);

            for (int f = 0; f < 5; f++)
            {
                var fin = new Finger();
                for (int j = 0; j < 3; j++)
                {
                    var joint = Find(rigRoot, p + FingerNames[f] + (j + 1));
                    var child = Find(rigRoot, p + FingerNames[f] + (j + 2));
                    fin.joints[j] = joint;
                    if (!joint) continue;
                    fin.bind[j] = joint.localRotation;
                    Vector3 dir = child ? (child.position - joint.position).normalized : fwd;
                    Vector3 axisWorld = Vector3.Cross(dir, palm);
                    if (axisWorld.sqrMagnitude < 1e-6f) axisWorld = across;
                    fin.axis[j] = Quaternion.Inverse(joint.rotation) * axisWorld.normalized;
                }
                fin.tip = Find(rigRoot, p + FingerNames[f] + "4");
                a.fingers[f] = fin;
            }
            a.ok = true;
            return a;
        }

        /// <summary>Puts every skinned bone back where the mesh was bound (parents first).</summary>
        static void RestoreBindPose(Transform root)
        {
            foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var bones = smr.bones;
                var bind = smr.sharedMesh ? smr.sharedMesh.bindposes : null;
                if (bind == null || bind.Length != bones.Length) continue;
                var order = new int[bones.Length];
                for (int i = 0; i < order.Length; i++) order[i] = i;
                System.Array.Sort(order, (x, y) => Depth(bones[x]).CompareTo(Depth(bones[y])));
                foreach (int i in order)
                {
                    if (!bones[i]) continue;
                    Matrix4x4 m = smr.transform.localToWorldMatrix * bind[i].inverse;
                    bones[i].SetPositionAndRotation(m.GetColumn(3), m.rotation);
                }
            }
        }

        static int Depth(Transform t) { int d = 0; while (t) { t = t.parent; d++; } return d; }

        static Transform Find(Transform root, string n)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) if (t.name == n) return t;
            return null;
        }

        // ------------------------------------------------------------------ per frame

        void LateUpdate()
        {
            if (left == null || right == null)
            {
                // script reload in Play Mode drops the bone data: rebuild from the bind pose
                if (rigRoot) RestoreBindPose(rigRoot);
                Init();
                OnEnable();
            }
            if (left == null || right == null || !left.ok || !right.ok || !poseSource) return;
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            float k = 1f - Mathf.Exp(-poseSharpness * dt);

            Firearm gun = shooting && shooting.isActiveAndEnabled ? shooting.CurrentWeapon : null;
            if (gun != subscribed)
            {
                if (subscribed) subscribed.Fired -= OnFired;
                subscribed = gun;
                if (subscribed) subscribed.Fired += OnFired;
            }
            int punchHand = shooting ? shooting.PunchHand : 0;
            float punch = shooting ? shooting.PunchExtend : 0f;
            float guard = shooting ? shooting.GuardWeight : 0f;
            bool shotgun = gun is Shotgun;

            // free-hand targets straight from the placeholder pose: forearm direction = its forward, back of hand = its up
            FreeTarget(placeholderL, out Vector3 lPos, out Quaternion lRot);
            FreeTarget(placeholderR, out Vector3 rPos, out Quaternion rRot);
            float[] lCurl, rCurl;

            bool parkour = player && !(player.State == MoveState.Grounded || player.State == MoveState.Airborne || player.State == MoveState.Sliding);
            float[] freeCurl = parkour || (player && player.IsBraceReady) ? openCurl : (guard > 0.3f || punch > 0.05f ? fistCurl : openCurl);

            Capsule lCap = default, rCap = default;
            if (gun)
            {
                // right hand: always on the grip
                GripPose rg = shotgun ? shotgunRight : pistolRight;
                GripTarget(gun.transform, gun.transform.position, rg, out rPos, out rRot, out rCap);
                rCurl = (float[])rg.curl.Clone();
                bool pulling = Time.time - lastShotTime < 0.12f;
                if (pulling)
                {
                    rCurl[1] = triggerPullCurl;
                    if (rCap.hasIndex) { rCap.index -= gun.transform.forward * 0.007f; rCap.indexBend += 0.2f; }
                }

                // left hand: on the fore-end / support grip, blended to the placeholder's free pose
                GripPose lg = shotgun ? shotgunLeft : pistolLeft;
                GripTarget(gun.transform, gun.LeftGripPosition, lg, out Vector3 gripLPos, out Quaternion gripLRot, out lCap);
                float w = shooting.TwoHandWeight * (punchHand < 0 ? 1f - punch : 1f);
                bool reloadHand = shooting.IsReloading && w < 0.5f;
                lPos = Vector3.Lerp(lPos, gripLPos, w);
                lRot = Quaternion.Slerp(lRot, gripLRot, w);
                lCap.on = w > 0.6f;
                lCurl = Blend(reloadHand ? holdCurl : (punchHand < 0 && punch > 0.05f ? fistCurl : freeCurl), lg.curl, w);
            }
            else
            {
                lCurl = freeCurl;
                rCurl = freeCurl;
            }

            Pose(left, lPos, lRot, lCurl, lCap, leftPole, k, gun != null && shooting.TwoHandWeight > 0.95f);
            Pose(right, rPos, rRot, rCurl, rCap, rightPole, k, gun != null);
        }

        void FreeTarget(Transform hand, out Vector3 pos, out Quaternion rot)
        {
            if (!hand) { pos = transform.position; rot = transform.rotation; return; }
            pos = hand.position + hand.forward * 0.02f;
            rot = Quaternion.LookRotation(hand.forward, hand.up);
        }

        /// <summary>Palm on the capsule's surface (opposite the back of the hand), hand turned by the pose.</summary>
        static void GripTarget(Transform gun, Vector3 point, GripPose g, out Vector3 palmPos, out Quaternion rot, out Capsule cap)
        {
            Quaternion q = gun.rotation;
            Vector3 f = q * g.forward.normalized;
            Vector3 b = Vector3.ProjectOnPlane(q * g.back.normalized, f).normalized;
            rot = Quaternion.LookRotation(f, b);
            cap = new Capsule
            {
                a = point + q * g.axisA, b = point + q * g.axisB, r = g.radius, on = true,
                thumb = g.thumbAim.sqrMagnitude > 1e-6f ? q * g.thumbAim.normalized : Vector3.zero,
                thumbOut = g.thumbStaysOut,
                thumbPoint = point + q * g.thumbPoint, hasThumbPoint = g.thumbPoint.sqrMagnitude > 1e-8f,
                index = point + q * g.indexPoint, hasIndex = g.indexPoint.sqrMagnitude > 1e-8f, indexBend = g.indexBend,
            };
            Vector3 onAxis = Vector3.Lerp(cap.a, cap.b, g.along);
            palmPos = onAxis + b * g.radius + q * g.offset;
        }

        static float[] Blend(float[] a, float[] b, float t)
        {
            var r = new float[5];
            for (int i = 0; i < 5; i++) r[i] = Mathf.Lerp(a[i], b[i], t);
            return r;
        }

        /// <param name="rigid">Hand is locked to a gun: no smoothing, it must not lag behind the weapon.</param>
        void Pose(Arm a, Vector3 palmPos, Quaternion handRot, float[] curl, Capsule cap, Vector3 pole, float k, bool rigid)
        {
            if (!a.hasPose || rigid) { a.handPos = palmPos; a.handRot = handRot; a.hasPose = true; }
            else
            {
                a.handPos = Vector3.Lerp(a.handPos, palmPos, k);
                a.handRot = Quaternion.Slerp(a.handRot, handRot, k);
            }

            // reset to the bind pose, then solve
            a.upper.localPosition = a.upperBindPos;
            a.upper.localRotation = a.upperBind;
            a.elbow.localRotation = a.elbowBind;
            a.wrist.localRotation = a.wristBind;

            Quaternion wristRot = a.handRot * a.handOffset;
            Vector3 target = a.handPos - wristRot * Vector3.Scale(a.palmCenter, a.wrist.lossyScale);

            // shoulder reaches forward a little when the hand is out of range (punch, long stretch)
            Vector3 s = a.upper.position;
            float reach = a.upperLen + a.foreLen;
            float d = Vector3.Distance(s, target);
            if (d > reach * 0.97f)
            {
                a.upper.position = s + (target - s).normalized * Mathf.Min(d - reach * 0.97f, maxShoulderReach);
                s = a.upper.position;
            }

            Vector3 toT = target - s;
            float dist = Mathf.Clamp(toT.magnitude, 0.01f, reach * 0.999f);
            Vector3 dir = toT.normalized;
            Vector3 poleW = rigRoot.TransformPoint(pole);
            Vector3 bend = Vector3.ProjectOnPlane(poleW - s, dir);
            if (bend.sqrMagnitude < 1e-6f) bend = Vector3.ProjectOnPlane(-rigRoot.up, dir);
            bend.Normalize();
            float cosA = Mathf.Clamp((a.upperLen * a.upperLen + dist * dist - a.foreLen * a.foreLen) / (2f * a.upperLen * dist), -1f, 1f);
            float sinA = Mathf.Sqrt(1f - cosA * cosA);
            Vector3 elbowPos = s + dir * (a.upperLen * cosA) + bend * (a.upperLen * sinA);

            a.upper.rotation = Quaternion.FromToRotation(a.elbow.position - s, elbowPos - s) * a.upper.rotation;
            a.elbow.rotation = Quaternion.FromToRotation(a.wrist.position - a.elbow.position, target - a.elbow.position) * a.elbow.rotation;
            a.wrist.rotation = wristRot;

            // share the wrist roll with the forearm so the skin doesn't twist at the wrist
            if (forearmTwist > 0f)
            {
                Vector3 axis = (a.wrist.position - a.elbow.position).normalized;
                Quaternion local = Quaternion.Inverse(a.elbow.rotation) * a.wrist.rotation * Quaternion.Inverse(a.wristBind);
                Vector3 axisLocal = Quaternion.Inverse(a.elbow.rotation) * axis;
                Vector3 v = new Vector3(local.x, local.y, local.z);
                Vector3 proj = Vector3.Project(v, axisLocal);
                var twist = new Quaternion(proj.x, proj.y, proj.z, local.w);
                float mag = Mathf.Sqrt(twist.x * twist.x + twist.y * twist.y + twist.z * twist.z + twist.w * twist.w);
                if (mag > 1e-5f)
                {
                    twist = new Quaternion(twist.x / mag, twist.y / mag, twist.z / mag, twist.w / mag);
                    twist.ToAngleAxis(out float angle, out Vector3 ax);
                    if (angle > 180f) angle -= 360f;
                    a.elbow.localRotation = a.elbow.localRotation * Quaternion.AngleAxis(angle * forearmTwist, ax);
                    a.wrist.rotation = wristRot;
                }
            }

            // fingers: on a grip they close until they touch it, otherwise they take the given curl
            for (int f = 0; f < 5; f++)
            {
                Finger fin = a.fingers[f];
                if (fin == null || !fin.joints[0]) continue;
                float want = curl[f];
                if (cap.on) want = WrapCurl(fin, f, want, cap);
                fin.curl = rigid ? want : Mathf.Lerp(fin.curl, want, k);
                SetCurl(fin, f, fin.curl);
            }

            // trigger finger: bend the outer joints, then swing the finger from the knuckle onto the trigger
            Finger trig = a.fingers[1];
            if (cap.on && cap.hasIndex && trig != null && trig.joints[0] && trig.joints[2])
            {
                Transform k0 = trig.joints[0];
                SetCurl(trig, 1, 0f);
                float reachWanted = Vector3.Distance(k0.position, cap.index);
                // bend the two outer joints just enough that the pad lands on the trigger, never less than indexBend
                float bestBend = cap.indexBend, bestErr = float.MaxValue;
                for (int i = 0; i <= 16; i++)
                {
                    float bnd = Mathf.Lerp(cap.indexBend, 1.6f, i / 16f);
                    BendIndex(trig, bnd);
                    float err = Mathf.Abs(Vector3.Distance(k0.position, IndexPad(trig)) - reachWanted);
                    if (err < bestErr) { bestErr = err; bestBend = bnd; }
                }
                BendIndex(trig, bestBend);
                trig.curl = bestBend;
                k0.rotation = Quaternion.FromToRotation(IndexPad(trig) - k0.position, cap.index - k0.position) * k0.rotation;
            }

            Finger th = a.fingers[0];
            // fist: the thumb folds over the index and middle fingers
            Finger ix = a.fingers[1];
            if (!cap.on && curl[1] > 0.8f && th != null && th.joints[0] && th.tip && ix != null && ix.joints[1])
            {
                Transform t0 = th.joints[0];
                Vector3 over = Vector3.Lerp(ix.joints[1].position, a.fingers[2] != null && a.fingers[2].joints[1] ? a.fingers[2].joints[1].position : ix.joints[1].position, 0.4f);
                Quaternion aim = Quaternion.FromToRotation(th.tip.position - t0.position, over - t0.position) * t0.rotation;
                t0.rotation = Quaternion.Slerp(t0.rotation, aim, Mathf.InverseLerp(0.8f, 1f, curl[1]));
            }

            // thumb along the frame / fore-end instead of sticking out
            if (cap.on && (cap.thumb != Vector3.zero || cap.hasThumbPoint) && th != null && th.joints[0] && th.tip)
            {
                Transform t0 = th.joints[0];
                Vector3 cur = th.tip.position - t0.position;
                Vector3 want = cap.hasThumbPoint ? cap.thumbPoint - t0.position : cap.thumb;
                t0.rotation = Quaternion.FromToRotation(cur, want) * t0.rotation;
                // don't let it sink into what it holds
                for (int i = 0; i < 6 && cap.thumbOut && (Inside(th.tip, cap) || Inside(th.joints[2], cap)); i++)
                {
                    Vector3 outward = Vector3.ProjectOnPlane(th.tip.position - cap.a, cap.b - cap.a).normalized;
                    t0.rotation = Quaternion.AngleAxis(8f, Vector3.Cross(th.tip.position - t0.position, outward)) * t0.rotation;
                }
            }
        }

        void BendIndex(Finger fin, float bend)
        {
            fin.joints[1].localRotation = fin.bind[1] * Quaternion.AngleAxis(fingerCurlAngles.y * bend * 0.7f, fin.axis[1]);
            fin.joints[2].localRotation = fin.bind[2] * Quaternion.AngleAxis(fingerCurlAngles.z * bend * 0.7f, fin.axis[2]);
        }

        /// <summary>The pad of the index finger: just past its last joint.</summary>
        static Vector3 IndexPad(Finger fin) => fin.tip ? Vector3.Lerp(fin.joints[2].position, fin.tip.position, 0.45f) : fin.joints[2].position;

        void SetCurl(Finger fin, int f, float c)
        {
            Vector3 angles = f == 0 ? thumbCurlAngles : fingerCurlAngles;
            for (int j = 0; j < 3; j++)
            {
                if (!fin.joints[j]) continue;
                float ang = (j == 0 ? angles.x : j == 1 ? angles.y : angles.z) * c;
                fin.joints[j].localRotation = fin.bind[j] * Quaternion.AngleAxis(ang, fin.axis[j]);
            }
        }

        /// <summary>Largest curl up to max at which the finger's outer joints and tip stay outside the capsule.</summary>
        float WrapCurl(Finger fin, int f, float max, Capsule cap)
        {
            const int Steps = 10;
            float best = 0f;
            for (int i = 1; i <= Steps; i++)
            {
                float c = max * i / Steps;
                SetCurl(fin, f, c);
                if (Inside(fin.joints[1], cap) || Inside(fin.joints[2], cap) || Inside(fin.tip, cap)) break;
                best = c;
            }
            // touching almost at once: the finger starts at the edge of the grip (pinky below it) - close it
            // around rather than leaving it sticking out straight
            return best < max * 0.25f ? max * 0.8f : best;
        }

        static bool Inside(Transform t, Capsule cap)
        {
            if (!t) return false;
            Vector3 ab = cap.b - cap.a;
            float u = Mathf.Clamp01(Vector3.Dot(t.position - cap.a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-6f));
            return (t.position - (cap.a + ab * u)).sqrMagnitude < cap.r * cap.r;
        }

        void OnDrawGizmosSelected()
        {
            var sh = shooting;
            if (!sh) { var pl = GetComponentInParent<BodycamPlayerController>(); if (pl) sh = pl.GetComponentInChildren<PlayerShooting>(); }
            var gun = sh ? sh.CurrentWeapon : null;
            if (!gun) return;
            bool sg = gun is Shotgun;
            DrawCapsule(gun.transform, gun.transform.position, sg ? shotgunRight : pistolRight, Color.cyan);
            DrawCapsule(gun.transform, gun.LeftGripPosition, sg ? shotgunLeft : pistolLeft, Color.magenta);
        }

        static void DrawCapsule(Transform gun, Vector3 point, GripPose g, Color c)
        {
            Gizmos.color = c;
            Vector3 a = point + gun.rotation * g.axisA, b = point + gun.rotation * g.axisB;
            Gizmos.DrawLine(a, b);
            Gizmos.DrawWireSphere(a, g.radius);
            Gizmos.DrawWireSphere(b, g.radius);
        }
    }
}
