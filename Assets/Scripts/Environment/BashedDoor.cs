using System;
using System.Collections;
using BodycamHorror.AI;
using BodycamHorror.Audio;
using BodycamHorror.Player;
using UnityEngine;

namespace BodycamHorror.Environment
{
    /// <summary>
    /// A shut door something beats on from the other side: the first blow shakes it in its frame with a boom, the second
    /// tears it off and throws it into the room with a crash. Started by a script (<see cref="Begin"/>). The throw is not
    /// left to physics: the leaf flies along a set arc, tips forward and lands flat on the floor at <see cref="landOffset"/>,
    /// and lies there with its collider off, so it never props up, rolls or blocks the way. The moment it is torn off the
    /// NavMesh is baked again (RuntimeNavMesh), so what is behind it can walk through.
    /// </summary>
    public class BashedDoor : MonoBehaviour
    {
        [Tooltip("The leaf: a solid box with a collider. It flies off on the last blow.")]
        public Transform leaf;
        [Tooltip("Blows it takes; the last one tears it off.")]
        public int blows = 2;
        [Tooltip("Time between blows (s).")]
        public float gap = 1.4f;

        [Header("The throw (a set path, not physics)")]
        [Tooltip("Where the leaf's centre comes down, in the door's own space (+Z: out of the door's front, +X: its right).")]
        public Vector3 landOffset = new Vector3(0.3f, 0f, 2.1f);
        [Tooltip("Turned this far about the vertical as it lands (deg), so it does not lie square to the doorway.")]
        public float landYaw = 12f;
        [Tooltip("Time from torn off to on the floor (s).")]
        public float flightTime = 0.45f;
        [Tooltip("How high its bottom edge is lifted off the floor on the way (m).")]
        public float arcHeight = 0.3f;
        [Tooltip("The little jump it gives as it slaps down (m), and how long that takes (s).")]
        public float bounceHeight = 0.04f;
        public float bounceTime = 0.14f;

        [Header("Sound")]
        public AudioClip blowClip;
        [Range(0f, 1f)] public float blowVolume = 0.9f;
        public AudioClip burstClip;
        [Range(0f, 1f)] public float burstVolume = 1f;
        [Tooltip("The slap of it landing flat. Empty: the blow, quieter.")]
        public AudioClip landClip;
        [Range(0f, 1f)] public float landVolume = 0.7f;

        public event Action TornOff;
        public bool IsTorn { get; private set; }

        Vector3 restPos;
        Quaternion restRot;
        bool started;

        void Awake()
        {
            if (!leaf) leaf = transform.Find("Leaf");
            if (leaf) { restPos = leaf.localPosition; restRot = leaf.localRotation; }
        }

        public void Begin()
        {
            if (started || !leaf) return;
            started = true;
            StartCoroutine(Run());
        }

        IEnumerator Run()
        {
            for (int i = 1; i < blows; i++)
            {
                Blow(0.35f);
                yield return Shake(0.35f);
                yield return new WaitForSeconds(Mathf.Max(0f, gap - 0.35f));
            }
            TearOff();
        }

        void Blow(float trauma)
        {
            if (blowClip) SpatialSound.PlayAt(blowClip, leaf.position, blowVolume, 2f, 45f);
            Jolt(trauma);
        }

        /// <summary>The leaf jumps in its frame and settles back.</summary>
        IEnumerator Shake(float time)
        {
            Vector3 push = Vector3.forward * 0.05f;
            float t = 0f;
            while (t < time)
            {
                t += Time.deltaTime;
                float k = Mathf.Exp(-t * 9f) * Mathf.Cos(t * 55f);
                leaf.localPosition = restPos + push * k;
                leaf.localRotation = restRot * Quaternion.Euler(k * 2.5f, 0f, k * 1.2f);
                yield return null;
            }
            leaf.localPosition = restPos;
            leaf.localRotation = restRot;
        }

        void TearOff()
        {
            IsTorn = true;
            if (burstClip) SpatialSound.PlayAt(burstClip, leaf.position, burstVolume, 2.5f, 60f);
            if (blowClip) SpatialSound.PlayAt(blowClip, leaf.position, blowVolume, 2f, 45f);
            Jolt(0.6f);

            // nothing collides with it from here on: not on its way, not lying on the floor
            foreach (var c in leaf.GetComponentsInChildren<Collider>()) c.enabled = false;
            var body = leaf.GetComponent<Rigidbody>();
            if (body) { body.isKinematic = true; body.useGravity = false; }
            leaf.SetParent(null, true);
            TornOff?.Invoke();
            StartCoroutine(Fly());
            StartCoroutine(Rebake());   // its collider is already off: the way is open now, not once it lands
        }

        /// <summary>
        /// Off its hinges and down: it tips straight forward over its bottom front edge (no twist), the edge kicked out
        /// along the floor as it goes, so it leaves the frame at once and never dips into the floor; it lands flat on its
        /// face with its middle at <see cref="landOffset"/>, with a slap and a small bounce.
        /// </summary>
        IEnumerator Fly()
        {
            // the leaf's slab in the door's own space (x across, y up, z out into the room): the collider's box, or the mesh's
            Bounds slabBox;
            var slab = leaf.GetComponent<BoxCollider>();
            var mesh = leaf.GetComponent<MeshFilter>();
            if (slab) slabBox = new Bounds(slab.center, slab.size);
            else if (mesh && mesh.sharedMesh) slabBox = mesh.sharedMesh.bounds;
            else slabBox = new Bounds(Vector3.zero, new Vector3(1f, 2f, 0.1f));
            Vector3 min = Vector3.positiveInfinity, max = Vector3.negativeInfinity;
            for (int i = 0; i < 8; i++)
            {
                Vector3 c = slabBox.center + Vector3.Scale(slabBox.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                Vector3 d = transform.InverseTransformPoint(leaf.TransformPoint(c));
                min = Vector3.Min(min, d);
                max = Vector3.Max(max, d);
            }

            // it turns about its bottom front edge (under its middle): the leaf held in that edge's frame
            Quaternion upright = transform.rotation;
            Vector3 fromPivot = transform.TransformPoint(new Vector3((min.x + max.x) * 0.5f, min.y, max.z));
            Vector3 heldPos = Quaternion.Inverse(upright) * (leaf.position - fromPivot);
            Quaternion heldRot = Quaternion.Inverse(upright) * leaf.rotation;

            // flat on its face, turned landYaw: the edge goes where that puts the leaf's middle on landOffset
            Quaternion Pose(float tip, float yaw) => Quaternion.AngleAxis(yaw, Vector3.up) * upright * Quaternion.Euler(tip, 0f, 0f);
            Vector3 middle = new Vector3((min.x + max.x) * 0.5f, (min.y + max.y) * 0.5f, (min.z + max.z) * 0.5f);
            Vector3 middleHeld = Quaternion.Inverse(upright) * (transform.TransformPoint(middle) - fromPivot);
            Vector3 toPivot = transform.TransformPoint(landOffset) - Pose(90f, landYaw) * middleHeld;
            float floor = transform.position.y;
            Vector3 probe = transform.TransformPoint(landOffset);
            if (Physics.Raycast(probe + Vector3.up * 1.5f, Vector3.down, out RaycastHit hit, 4f, ~0, QueryTriggerInteraction.Ignore)) floor = hit.point.y;
            toPivot.y = floor + 0.002f;

            float time = Mathf.Max(0.05f, flightTime);
            for (float t = 0f; t < time; t += Time.deltaTime)
            {
                float k = t / time;
                float tip = 90f * Mathf.Lerp(k, k * k, 0.7f);    // a shove off the hinges, then gravity has it
                float slide = 1f - (1f - k) * (1f - k);          // the bottom kicked out fast, slowing as it comes down
                Vector3 pivot = Vector3.Lerp(fromPivot, toPivot, slide);
                pivot.y += arcHeight * 4f * k * (1f - k) * (1f - k);   // the bottom off the floor a little early on
                Quaternion pose = Pose(tip, landYaw * slide);
                leaf.SetPositionAndRotation(pivot + pose * heldPos, pose * heldRot);
                yield return null;
            }
            Quaternion toRot = Pose(90f, landYaw) * heldRot;
            Vector3 toPos = toPivot + Pose(90f, landYaw) * heldPos;
            leaf.SetPositionAndRotation(toPos, toRot);

            AudioClip slap = landClip ? landClip : blowClip;
            if (slap) SpatialSound.PlayAt(slap, toPos, landClip ? landVolume : landVolume * blowVolume, 2f, 40f);
            Jolt(0.25f);

            if (bounceHeight > 0f && bounceTime > 0f)
            {
                for (float t = 0f; t < bounceTime; t += Time.deltaTime)
                {
                    float k = t / bounceTime;
                    leaf.position = toPos + Vector3.up * (bounceHeight * 4f * k * (1f - k));
                    yield return null;
                }
                leaf.position = toPos;
            }
        }

        IEnumerator Rebake()
        {
            yield return null;   // the leaf is out of the frame and its collider off: the bake leaves it out
            var nav = FindAnyObjectByType<RuntimeNavMesh>();
            if (nav) { nav.Rebake(); SafeZone.Recarve(); }
        }

        void Jolt(float amount)
        {
            var player = FindAnyObjectByType<BodycamPlayerController>();
            if (!player) return;
            float near = 1f - Mathf.Clamp01(Vector3.Distance(player.transform.position, transform.position) / 20f);
            var rig = player.GetComponentInChildren<BodycamCameraRig>();
            if (rig) rig.AddTrauma(amount * (0.4f + 0.6f * near));
            var stress = player.GetComponent<PlayerStress>();
            if (stress) stress.Add(0.15f + 0.2f * near);
        }
    }
}
