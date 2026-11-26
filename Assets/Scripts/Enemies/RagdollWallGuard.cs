using System.Collections.Generic;
using UnityEngine;

namespace BodycamHorror.Enemies
{
    /// <summary>
    /// Watches a ragdoll on its way down so no part of it passes through a wall: each physics step, a part that went
    /// through something solid since the last one is put back on the near side of it, its speed into it taken away.
    /// Once the body has come to rest (or after <see cref="maxTime"/>) it is frozen where it lies and the guard goes.
    /// </summary>
    public class RagdollWallGuard : MonoBehaviour
    {
        /// <summary>What counts as solid.</summary>
        public LayerMask mask = ~0;
        /// <summary>Colliders under this are the body itself (never a wall).</summary>
        public Transform owner;
        /// <summary>Frozen after this long (s) at most.</summary>
        public float maxTime = 8f;
        /// <summary>Frozen once every part has been this slow (m/s) for <see cref="restTime"/>.</summary>
        public float restSpeed = 0.08f;
        public float restTime = 1f;

        Rigidbody[] bodies;
        Vector3[] last;
        float started, restFor;

        void Start()
        {
            bodies = GetComponentsInChildren<Rigidbody>();
            last = new Vector3[bodies.Length];
            for (int i = 0; i < bodies.Length; i++) last[i] = bodies[i].position;
            started = Time.time;
            if (!owner) owner = transform;
        }

        void FixedUpdate()
        {
            if (bodies == null) return;
            bool resting = true;
            for (int i = 0; i < bodies.Length; i++)
            {
                Rigidbody rb = bodies[i];
                if (!rb || rb.isKinematic) continue;
                Vector3 now = rb.position;
                if (Physics.Linecast(last[i], now, out RaycastHit hit, mask, QueryTriggerInteraction.Ignore) && !hit.collider.transform.IsChildOf(owner))
                {
                    // through it: back on this side, and nothing left of the push into it
                    now = hit.point + hit.normal * 0.04f;
                    rb.position = now;
                    Vector3 v = rb.linearVelocity;
                    float into = Vector3.Dot(v, hit.normal);
                    if (into < 0f) rb.linearVelocity = v - hit.normal * into;
                }
                last[i] = now;
                if (rb.linearVelocity.sqrMagnitude > restSpeed * restSpeed) resting = false;
            }
            restFor = resting ? restFor + Time.fixedDeltaTime : 0f;
            if (restFor < restTime && Time.time - started < maxTime) return;
            // at rest: where it lies, for good (nothing shoves it into a wall later)
            foreach (Rigidbody rb in bodies)
                if (rb)
                {
                    rb.isKinematic = true;
                    rb.interpolation = RigidbodyInterpolation.None;
                }
            Destroy(this);
        }
    }
}
