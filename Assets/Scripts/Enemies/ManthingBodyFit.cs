using UnityEngine;

namespace BodycamHorror.Enemies
{
    /// <summary>
    /// Fits a posed humanoid body into the room it stands in, without breaking the pose: the back is bent away from
    /// any wall the chest or head is in, the feet are put down on the floor, and a hand that is in a wall or the floor,
    /// or close enough to one to lean on, is put flat on it (the elbow bending to suit). Run once on a pose after it
    /// is evaluated (and after the body is pushed out of the walls as a whole); the next evaluation overwrites it.
    /// </summary>
    public class ManthingBodyFit
    {
        readonly Transform root;
        readonly int mask;
        readonly Transform hips, head;
        readonly Transform[] spine;                       // spine, chest, upper chest, neck: the bones the back bends with
        readonly Limb leftArm, rightArm, leftLeg, rightLeg;
        readonly SphereCollider probe;
        readonly Collider[] hits = new Collider[12];

        /// <summary>How far (m, at the model's own scale) a hand reaches out to lean on a surface beyond where the pose has it.</summary>
        public float handReach = 0.35f;
        /// <summary>A hand this low over the floor (m, at the model's own scale) is put down on it.</summary>
        public float handToFloor = 0.3f;
        /// <summary>A foot this far up off the floor (m, at the model's own scale) or less is put down on it.</summary>
        public float footToFloor = 0.6f;

        class Limb
        {
            public Transform upper, lower, end, tip;   // tip: a finger or toe, for the direction along the surface
            public bool Valid => upper && lower && end;
        }

        public ManthingBodyFit(Animator animator, Transform root, int mask)
        {
            this.root = root;
            this.mask = mask;
            Transform B(HumanBodyBones b) => animator.GetBoneTransform(b);
            hips = B(HumanBodyBones.Hips);
            head = B(HumanBodyBones.Head);
            var list = new System.Collections.Generic.List<Transform>();
            foreach (var b in new[] { HumanBodyBones.Spine, HumanBodyBones.Chest, HumanBodyBones.UpperChest, HumanBodyBones.Neck })
                if (B(b)) list.Add(B(b));
            spine = list.ToArray();
            leftArm = new Limb { upper = B(HumanBodyBones.LeftUpperArm), lower = B(HumanBodyBones.LeftLowerArm), end = B(HumanBodyBones.LeftHand), tip = B(HumanBodyBones.LeftMiddleProximal) };
            rightArm = new Limb { upper = B(HumanBodyBones.RightUpperArm), lower = B(HumanBodyBones.RightLowerArm), end = B(HumanBodyBones.RightHand), tip = B(HumanBodyBones.RightMiddleProximal) };
            leftLeg = new Limb { upper = B(HumanBodyBones.LeftUpperLeg), lower = B(HumanBodyBones.LeftLowerLeg), end = B(HumanBodyBones.LeftFoot), tip = B(HumanBodyBones.LeftToes) };
            rightLeg = new Limb { upper = B(HumanBodyBones.RightUpperLeg), lower = B(HumanBodyBones.RightLowerLeg), end = B(HumanBodyBones.RightFoot), tip = B(HumanBodyBones.RightToes) };

            var go = new GameObject("FitProbe") { layer = 2 };
            go.transform.SetParent(root, false);
            probe = go.AddComponent<SphereCollider>();
            probe.isTrigger = true;
        }

        /// <summary>Fits the current pose. <paramref name="scale"/>: the model's scale (its own metres to the world's).</summary>
        public void Fit(float scale)
        {
            if (!hips || !head) return;
            probe.enabled = true;
            BendBackOut(scale);
            float floor = root.position.y;
            Foot(leftLeg, floor, scale);
            Foot(rightLeg, floor, scale);
            Hand(leftArm, floor, scale);
            Hand(rightArm, floor, scale);
        }

        // ------------------------------------------------------------------ the back

        /// <summary>The head and chest out of the walls: the back bends away, spread over its bones, a few passes.</summary>
        void BendBackOut(float scale)
        {
            if (spine.Length == 0) return;
            Transform chest = spine.Length > 2 ? spine[2] : spine[spine.Length - 1];
            for (int pass = 0; pass < 3; pass++)
            {
                Vector3 push = Penetration(head.position, 0.14f * scale);
                Transform effector = head;
                if (push.sqrMagnitude < 1e-6f) { push = Penetration(chest.position, 0.2f * scale); effector = chest; }
                if (push.sqrMagnitude < 1e-6f) return;
                push *= 1.1f;   // a little clear of the wall, not grazing it
                // each bone below the effector takes a share of the move
                int count = 0;
                foreach (Transform b in spine) if (effector.IsChildOf(b) && b != effector) count++;
                if (count == 0) return;
                foreach (Transform b in spine)
                {
                    if (b == effector || !effector.IsChildOf(b)) continue;
                    Vector3 from = effector.position - b.position;
                    Vector3 to = from + push / count;
                    if (from.sqrMagnitude < 1e-6f) continue;
                    b.rotation = Quaternion.FromToRotation(from, to) * b.rotation;
                }
            }
        }

        // ------------------------------------------------------------------ feet

        /// <summary>A foot up in the air (not too far) is put down on the floor; one in the floor is lifted onto it.</summary>
        void Foot(Limb leg, float floor, float scale)
        {
            if (!leg.Valid) return;
            Vector3 foot = leg.end.position;
            float low = leg.tip ? Mathf.Min(foot.y, leg.tip.position.y) : foot.y;
            if (Physics.Raycast(foot + Vector3.up * 0.5f * scale, Vector3.down, out RaycastHit hit, 2f * scale, mask, QueryTriggerInteraction.Ignore))
                floor = hit.point.y;
            float sole = 0.03f * scale;
            float up = low - (floor + sole);
            // in a wall: out of it sideways
            Vector3 side = Penetration(foot + Vector3.up * 0.08f * scale, 0.07f * scale);
            side.y = 0f;
            if (side.sqrMagnitude > 1e-6f) side *= 1.2f;
            bool lower = up <= footToFloor * scale && Mathf.Abs(up) >= 0.01f;   // well up: a step in the air, left as it is
            if (!lower && side.sqrMagnitude < 1e-6f) return;
            Vector3 knee = leg.lower.position + root.forward * 0.1f * scale + side;
            Reach(leg, foot - Vector3.up * (lower ? up : 0f) + side, knee);
        }

        // ------------------------------------------------------------------ hands

        /// <summary>
        /// A hand in a wall, or within reach of one along the way it points, goes flat on the wall; a hand in the floor or
        /// low over it goes flat on the floor. A hand in the open is left as the pose has it.
        /// </summary>
        void Hand(Limb arm, float floor, float scale)
        {
            if (!arm.Valid) return;
            Vector3 shoulder = arm.upper.position, hand = arm.end.position;
            float length = Vector3.Distance(shoulder, arm.lower.position) + Vector3.Distance(arm.lower.position, hand);
            Vector3 along = hand - shoulder;
            float reach = along.magnitude;
            if (reach < 1e-3f) return;
            float palm = 0.025f * scale;

            // a wall (or anything) between the shoulder and a little past the hand: the hand goes on it
            if (Physics.Raycast(shoulder, along / reach, out RaycastHit hit, Mathf.Min(length, reach + handReach * scale), mask, QueryTriggerInteraction.Ignore))
            {
                Place(arm, hit.point + hit.normal * palm, hit.normal);
                Elbow(arm, scale);
                return;
            }
            // the floor: in it, or just over it
            if (Physics.Raycast(hand + Vector3.up * 0.4f * scale, Vector3.down, out hit, (0.4f + handToFloor) * scale, mask, QueryTriggerInteraction.Ignore)
                && Vector3.Dot(hit.normal, Vector3.up) > 0.6f)
            {
                Place(arm, hit.point + hit.normal * palm, hit.normal);
                Elbow(arm, scale);
                return;
            }
            // still in something (a pipe, a corner the rays missed): out of it the shortest way
            Vector3 push = Penetration(hand, 0.05f * scale);
            if (push.sqrMagnitude > 1e-6f) Place(arm, hand + push * 1.1f, push.normalized);
            Elbow(arm, scale);
        }

        /// <summary>An elbow in the wall: the arm bends the other way round (out from the wall), the hand staying where it is.</summary>
        void Elbow(Limb arm, float scale)
        {
            for (int pass = 0; pass < 2; pass++)
            {
                Vector3 push = Penetration(arm.lower.position, 0.06f * scale);
                if (push.sqrMagnitude < 1e-6f) return;
                Quaternion hand = arm.end.rotation;
                Reach(arm, arm.end.position, arm.lower.position + push.normalized * 0.5f * scale);
                arm.end.rotation = hand;
            }
        }

        /// <summary>The hand to a point on a surface, the elbow bending as it already bends, the fingers laid along the surface.</summary>
        void Place(Limb arm, Vector3 target, Vector3 normal)
        {
            Reach(arm, target, arm.lower.position);
            if (!arm.tip) return;
            Vector3 fingers = arm.tip.position - arm.end.position;
            Vector3 flat = Vector3.ProjectOnPlane(fingers, normal);
            if (fingers.sqrMagnitude > 1e-6f && flat.sqrMagnitude > 1e-6f)
                arm.end.rotation = Quaternion.FromToRotation(fingers, flat) * arm.end.rotation;
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>
        /// Two-bone reach: the end of the limb to <paramref name="target"/> (or as far towards it as the limb is long),
        /// the middle joint bent towards <paramref name="hint"/>.
        /// </summary>
        static void Reach(Limb limb, Vector3 target, Vector3 hint)
        {
            Vector3 a = limb.upper.position;
            float l1 = Vector3.Distance(a, limb.lower.position), l2 = Vector3.Distance(limb.lower.position, limb.end.position);
            Vector3 toT = target - a;
            float d = Mathf.Clamp(toT.magnitude, Mathf.Abs(l1 - l2) + 1e-3f, l1 + l2 - 1e-3f);
            if (toT.sqrMagnitude < 1e-8f) return;
            Vector3 dir = toT.normalized;
            Vector3 pole = Vector3.ProjectOnPlane(hint - a, dir);
            if (pole.sqrMagnitude < 1e-8f) pole = Vector3.ProjectOnPlane(Vector3.up, dir);
            if (pole.sqrMagnitude < 1e-8f) pole = Vector3.ProjectOnPlane(Vector3.forward, dir);
            pole.Normalize();
            float x = (l1 * l1 - l2 * l2 + d * d) / (2f * d);
            float h = Mathf.Sqrt(Mathf.Max(0f, l1 * l1 - x * x));
            Vector3 mid = a + dir * x + pole * h;
            limb.upper.rotation = Quaternion.FromToRotation(limb.lower.position - a, mid - a) * limb.upper.rotation;
            Vector3 b = limb.lower.position;
            limb.lower.rotation = Quaternion.FromToRotation(limb.end.position - b, a + dir * d - b) * limb.lower.rotation;
        }

        /// <summary>How far, and which way, a ball at <paramref name="p"/> has to move to be out of everything solid.</summary>
        Vector3 Penetration(Vector3 p, float radius)
        {
            probe.radius = radius;
            probe.transform.position = p;
            int n = Physics.OverlapSphereNonAlloc(p, radius, hits, mask, QueryTriggerInteraction.Ignore);
            Vector3 push = Vector3.zero;
            for (int i = 0; i < n; i++)
            {
                Collider c = hits[i];
                if (!c || c.transform.IsChildOf(root)) continue;
                if (c.attachedRigidbody && !c.attachedRigidbody.isKinematic) continue;
                if (Physics.ComputePenetration(probe, p, Quaternion.identity, c, c.transform.position, c.transform.rotation, out Vector3 dir, out float depth))
                    push += dir * depth;
            }
            return push;
        }
    }
}
