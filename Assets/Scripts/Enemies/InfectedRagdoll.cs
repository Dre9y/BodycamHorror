using System.Collections.Generic;
using UnityEngine;

namespace BodycamHorror.Enemies
{
    /// <summary>
    /// Ragdoll built at runtime from a Humanoid skeleton: 11 bodies (pelvis, torso, head, upper and
    /// lower arms and legs) joined with CharacterJoints. Dormant (kinematic, colliders off) while the
    /// Animator drives the body; <see cref="Activate"/> hands the bones over to physics.
    /// </summary>
    public class InfectedRagdoll : MonoBehaviour
    {
        struct Part
        {
            public HumanBodyBones id;
            public Transform bone;
            public Rigidbody rb;
            public Collider col;
            public Collider[] extra;   // more hitboxes riding on this part (the stake past an arm's wrist)
        }

        /// <summary>A part's colliders: its own and the extra ones.</summary>
        static IEnumerable<Collider> Cols(Part p)
        {
            yield return p.col;
            if (p.extra != null) foreach (var c in p.extra) if (c) yield return c;
        }

        readonly List<Part> parts = new List<Part>();
        Animator animator;
        Rigidbody hipsBody;

        public bool IsActive { get; private set; }
        public bool IsBuilt => parts.Count > 0;
        public Collider HeadCollider { get; private set; }
        public Transform Hips => animator ? animator.GetBoneTransform(HumanBodyBones.Hips) : null;
        public Vector3 HipsVelocity => hipsBody ? hipsBody.linearVelocity : Vector3.zero;

        /// <summary>Builds the bodies and joints. <paramref name="ignore"/> colliders never touch the ragdoll.</summary>
        public bool Build(Animator anim, float totalMass, params Collider[] ignore)
        {
            animator = anim;
            if (!anim || !anim.isHuman) return false;
            Transform B(HumanBodyBones b) => anim.GetBoneTransform(b);

            Transform hips = B(HumanBodyBones.Hips), spine = B(HumanBodyBones.Spine), neck = B(HumanBodyBones.Neck), head = B(HumanBodyBones.Head);
            if (!hips || !spine || !head) return false;
            Transform chestTop = neck ? neck : head;
            Vector3 right = transform.right, fwd = transform.forward;

            // pelvis: a box between the thighs, up to the spine
            Transform lThigh = B(HumanBodyBones.LeftUpperLeg), rThigh = B(HumanBodyBones.RightUpperLeg);
            {
                var box = hips.gameObject.AddComponent<BoxCollider>();
                Vector3 l = hips.InverseTransformPoint(lThigh.position), r = hips.InverseTransformPoint(rThigh.position);
                Vector3 s = hips.InverseTransformPoint(spine.position);
                float width = (r - l).magnitude;
                box.center = (l + r) * 0.25f + s * 0.5f;
                box.size = AbsAxisSize(hips, width * 1.5f, (s - (l + r) * 0.5f).magnitude * 1.6f + width * 0.25f, width * 0.85f);
                AddPart(HumanBodyBones.Hips, hips, box, totalMass * 0.18f, null);
            }
            // torso: spine up to the neck (the chest bones ride along rigidly)
            AddCapsule(HumanBodyBones.Spine, spine, chestTop, 0.42f, totalMass * 0.24f, HumanBodyBones.Hips,
                       twist: 15f, swing1: 25f, swing2: 20f, right, fwd);
            // head
            {
                Transform top = null;
                foreach (var t in head.GetComponentsInChildren<Transform>()) if (t != head && t.name.Contains("HeadTop")) top = t;
                // a model's end bone can be anywhere (a broken export left one a metre off): the head is never
                // much bigger than the neck is long
                float neckLen = Vector3.Distance(chestTop.position, head.position);
                if (top && Vector3.Distance(head.position, top.position) > neckLen * 4f + 0.05f) top = null;
                float r = (top ? Vector3.Distance(head.position, top.position) : neckLen * 1.6f) * 0.55f;
                r = Mathf.Clamp(r, 0.06f, 0.16f);
                var sph = head.gameObject.AddComponent<SphereCollider>();
                sph.radius = r / Mathf.Max(head.lossyScale.x, 0.0001f);
                sph.center = head.InverseTransformPoint(top ? Vector3.Lerp(head.position, top.position, 0.5f) : head.position + transform.up * r);
                HeadCollider = sph;
                AddPart(HumanBodyBones.Head, head, sph, totalMass * 0.08f, Find(HumanBodyBones.Spine));
                Joint(head, Find(HumanBodyBones.Spine).rb, right, fwd, 20f, 30f, 25f);
            }
            // arms and legs (built from the T-pose: arms bend about the up axis, legs about the right axis)
            Vector3 up = transform.up;
            AddLimb(HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand, HumanBodyBones.Spine, 0.2f, totalMass * 0.03f, totalMass * 0.02f, up, fwd, false, 1f);
            AddLimb(HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand, HumanBodyBones.Spine, 0.2f, totalMass * 0.03f, totalMass * 0.02f, up, fwd, false, -1f);
            AddLimb(HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, HumanBodyBones.Hips, 0.22f, totalMass * 0.1f, totalMass * 0.05f, right, fwd, true, 1f);
            AddLimb(HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot, HumanBodyBones.Hips, 0.22f, totalMass * 0.1f, totalMass * 0.05f, right, fwd, true, 1f);

            hipsBody = Find(HumanBodyBones.Hips).rb;
            var mine = new List<Collider>();
            foreach (var p in parts) mine.AddRange(Cols(p));
            foreach (var m in mine)
                foreach (var c in ignore)
                    if (c) Physics.IgnoreCollision(m, c, true);
            // the parts overlap where they meet (shoulders in the torso, thighs in the pelvis): left to push each
            // other apart they fight the joints every step, and the body shivers on the floor
            for (int i = 0; i < mine.Count; i++)
                for (int k = i + 1; k < mine.Count; k++)
                    Physics.IgnoreCollision(mine[i], mine[k], true);
            SetDormant();
            return true;
        }

        void AddLimb(HumanBodyBones upper, HumanBodyBones lower, HumanBodyBones end, HumanBodyBones parent, float radiusK,
                     float upperMass, float lowerMass, Vector3 hingeAxis, Vector3 fwd, bool leg, float side)
        {
            Transform u = animator.GetBoneTransform(upper), l = animator.GetBoneTransform(lower), e = animator.GetBoneTransform(end);
            if (!u || !l || !e) return;
            // shoulders and hips swing freely; elbows and knees fold one way only
            AddCapsule(upper, u, l, radiusK, upperMass, parent, twist: leg ? 20f : 35f, swing1: leg ? 70f : 80f, swing2: leg ? 30f : 50f, hingeAxis, fwd);
            float low = leg ? -5f : (side > 0f ? -5f : -130f);
            float high = leg ? 120f : (side > 0f ? 130f : 5f);
            AddCapsule(lower, l, e, radiusK * 0.85f, lowerMass, upper, twist: 10f, swing1: 5f, swing2: 5f, hingeAxis, fwd,
                       hingeLow: low, hingeHigh: high);
            if (!leg) AddHandSegments(lower, l, e, Vector3.Distance(l.position, e.position) * radiusK * 0.85f);
        }

        /// <summary>
        /// Whatever the hand is (here a bone stake) gets hitboxes too: a thin capsule along each bone from the wrist
        /// to the tip, lined up with it exactly, tapering to the point. They belong to the forearm's part: a shot at the
        /// stake is a shot at the arm.
        /// </summary>
        void AddHandSegments(HumanBodyBones part, Transform forearm, Transform hand, float radius)
        {
            int index = parts.FindIndex(p => p.id == part);
            if (index < 0) return;
            var chain = new List<Transform> { hand };
            for (Transform t = hand; t.childCount > 0; )
            {
                // follow the longest branch (the stake), not a stray short child
                Transform next = null; float far = -1f;
                foreach (Transform c in t) { float d = Vector3.Distance(c.position, forearm.position); if (d > far) { far = d; next = c; } }
                if (Vector3.Distance(next.position, t.position) < 0.005f) break;
                chain.Add(next); t = next;
            }
            var extra = new List<Collider>();
            for (int i = 0; i < chain.Count - 1; i++)
            {
                Vector3 a = chain[i].position, b = chain[i + 1].position;
                float len = Vector3.Distance(a, b);
                float r = radius * Mathf.Lerp(1f, 0.45f, (i + 1f) / chain.Count);
                var go = new GameObject("Hitbox_" + chain[i].name);
                go.transform.SetParent(chain[i], true);
                go.transform.SetPositionAndRotation((a + b) * 0.5f, Quaternion.LookRotation(b - a, transform.up));
                go.transform.localScale = Vector3.one;
                var cap = go.AddComponent<CapsuleCollider>();
                float s = Mathf.Max(go.transform.lossyScale.x, 0.0001f);
                cap.direction = 2;
                cap.radius = r / s;
                cap.height = (len + r) / s;
                extra.Add(cap);
            }
            var p = parts[index];
            p.extra = extra.ToArray();
            parts[index] = p;
        }

        void AddCapsule(HumanBodyBones id, Transform bone, Transform child, float radiusK, float mass, HumanBodyBones parent,
                        float twist, float swing1, float swing2, Vector3 right, Vector3 fwd, float hingeLow = float.NaN, float hingeHigh = float.NaN)
        {
            var cap = bone.gameObject.AddComponent<CapsuleCollider>();
            Vector3 local = bone.InverseTransformPoint(child.position);
            int axis = MaxAxis(local);
            float len = local.magnitude;
            cap.direction = axis;
            cap.center = local * 0.5f;
            cap.radius = len * radiusK;
            cap.height = len + cap.radius;
            var parentPart = Find(parent);
            AddPart(id, bone, cap, mass, parentPart.rb ? parentPart : (Part?)null);
            if (!parentPart.rb) return;
            var j = Joint(bone, parentPart.rb, right, fwd, twist, swing1, swing2);
            if (!float.IsNaN(hingeLow))
            {
                j.lowTwistLimit = new SoftJointLimit { limit = hingeLow };
                j.highTwistLimit = new SoftJointLimit { limit = hingeHigh };
            }
        }

        CharacterJoint Joint(Transform bone, Rigidbody parent, Vector3 right, Vector3 fwd, float twist, float swing1, float swing2)
        {
            var j = bone.gameObject.AddComponent<CharacterJoint>();
            j.connectedBody = parent;
            // twist about the character's right axis = pitching the limb forward/back
            j.axis = bone.InverseTransformDirection(right).normalized;
            j.swingAxis = bone.InverseTransformDirection(fwd).normalized;
            j.lowTwistLimit = new SoftJointLimit { limit = -twist };
            j.highTwistLimit = new SoftJointLimit { limit = twist };
            j.swing1Limit = new SoftJointLimit { limit = swing1 };
            j.swing2Limit = new SoftJointLimit { limit = swing2 };
            j.enableProjection = true;
            j.projectionDistance = 0.02f;
            j.projectionAngle = 5f;
            j.enablePreprocessing = false;
            return j;
        }

        void AddPart(HumanBodyBones id, Transform bone, Collider col, float mass, Part? parent)
        {
            var rb = bone.gameObject.AddComponent<Rigidbody>();
            rb.mass = Mathf.Max(mass, 0.5f);
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            rb.linearDamping = 0.05f;
            rb.angularDamping = 1.5f;
            // light limbs on heavy joints chatter: more solver work, gentle depenetration, an earlier sleep
            rb.solverIterations = 16;
            rb.solverVelocityIterations = 6;
            rb.maxDepenetrationVelocity = 2f;
            rb.sleepThreshold = 0.05f;
            parts.Add(new Part { id = id, bone = bone, rb = rb, col = col });
        }

        Part Find(HumanBodyBones id)
        {
            foreach (var p in parts) if (p.id == id) return p;
            return default;
        }

        static int MaxAxis(Vector3 v)
        {
            Vector3 a = new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));
            return a.x > a.y && a.x > a.z ? 0 : a.y > a.z ? 1 : 2;
        }

        /// <summary>Box size in bone space from character-space width / height / depth.</summary>
        Vector3 AbsAxisSize(Transform bone, float width, float height, float depth)
        {
            Vector3 w = Abs(bone.InverseTransformVector(transform.right * width));
            Vector3 h = Abs(bone.InverseTransformVector(transform.up * height));
            Vector3 d = Abs(bone.InverseTransformVector(transform.forward * depth));
            return w + h + d;   // each lands (mostly) on one bone axis
        }

        static Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));

        /// <summary>Layer of the dormant bone colliders: shots and blows find them, physics ignores them.</summary>
        public static int HitboxLayer
        {
            get
            {
                if (hitboxLayer >= 0) return hitboxLayer;
                hitboxLayer = LayerMask.NameToLayer("Hitbox");
                if (hitboxLayer >= 0)
                    for (int i = 0; i < 32; i++) Physics.IgnoreLayerCollision(hitboxLayer, i, true);
                return hitboxLayer;
            }
        }
        static int hitboxLayer = -1;

        readonly HashSet<Collider> severedColliders = new HashSet<Collider>();

        void SetDormant()
        {
            int layer = HitboxLayer;
            foreach (var p in parts)
            {
                p.rb.isKinematic = true;
                p.rb.interpolation = RigidbodyInterpolation.None;
                p.rb.detectCollisions = true;
                foreach (var c in Cols(p))
                {
                    c.enabled = !severedColliders.Contains(c);
                    if (layer >= 0) c.gameObject.layer = layer;
                }
                if (layer >= 0) p.bone.gameObject.layer = layer;
            }
            IsActive = false;
        }

        /// <summary>Which body part a collider belongs to.</summary>
        public bool TryGetPart(Collider c, out HumanBodyBones part)
        {
            foreach (var p in parts)
                foreach (var pc in Cols(p))
                    if (pc == c) { part = p.id; return true; }
            part = default;
            return false;
        }

        /// <summary>The limb from <paramref name="bone"/> down is gone: nothing there to hit or to collide.</summary>
        public void DisableBelow(Transform bone)
        {
            foreach (var p in parts)
            {
                if (!p.bone.IsChildOf(bone)) continue;
                foreach (var c in Cols(p)) { severedColliders.Add(c); c.enabled = false; }
                p.rb.useGravity = false;
            }
        }

        /// <summary>Goes limp: the Animator lets go and every bone starts with <paramref name="velocity"/>.</summary>
        public void Activate(Vector3 velocity)
        {
            if (!IsBuilt) return;
            animator.enabled = false;
            foreach (var p in parts)
            {
                p.bone.gameObject.layer = 0;   // real body now: collides with the world
                foreach (var c in Cols(p)) { c.gameObject.layer = 0; c.enabled = !severedColliders.Contains(c); }
                p.rb.interpolation = RigidbodyInterpolation.Interpolate;
                p.rb.isKinematic = false;
                p.rb.linearVelocity = velocity;
                p.rb.angularVelocity = Vector3.zero;
            }
            IsActive = true;
        }

        /// <summary>Back to dormant (the caller re-enables the Animator).</summary>
        public void Deactivate() => SetDormant();

        /// <summary>Shoves the part nearest <paramref name="point"/> (and a little of the rest so it all goes over).</summary>
        public void Impulse(Vector3 velocityChange, Vector3 point)
        {
            if (!IsActive) return;
            Part nearest = default;
            float best = float.MaxValue;
            foreach (var p in parts)
            {
                if (!p.col.enabled) continue;   // severed
                float d = (p.col.ClosestPoint(point) - point).sqrMagnitude;
                if (d < best) { best = d; nearest = p; }
                p.rb.AddForce(velocityChange * 0.35f, ForceMode.VelocityChange);
            }
            if (nearest.rb) nearest.rb.AddForceAtPosition(velocityChange, point, ForceMode.VelocityChange);
        }

        /// <summary>Torque about the hips so a fall really topples.</summary>
        public void Topple(Vector3 axis, float speed)
        {
            if (hipsBody && IsActive) hipsBody.AddTorque(axis * speed, ForceMode.VelocityChange);
            var spine = Find(HumanBodyBones.Spine);
            if (spine.rb && IsActive) spine.rb.AddTorque(axis * speed, ForceMode.VelocityChange);
        }

        public bool Owns(Collider c)
        {
            return TryGetPart(c, out _);
        }
    }
}
