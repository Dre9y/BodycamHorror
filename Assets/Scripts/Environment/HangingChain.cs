using System.Collections.Generic;
using BodycamHorror.Combat;
using UnityEngine;

namespace BodycamHorror.Environment
{
    /// <summary>
    /// A chain hanging from the ceiling: a row of rigid segments on joints (built by the scene builder).
    /// Whatever brushes past it (a <see cref="ChainPusher"/>: the player, the manthing) shoves the segments it
    /// touches and sets them clanking; so does a bullet. The player's controller does not collide with the
    /// links (they would stop it dead); the push is applied here instead.
    /// </summary>
    public class HangingChain : MonoBehaviour, IDamageable
    {
        public Rigidbody[] segments;
        [Tooltip("How close (m) to a link a pusher's body counts as touching it.")]
        public float touchRadius = 0.12f;
        [Tooltip("How hard a body walking through shoves the links (m/s added per m/s of its speed).")]
        public float pushStrength = 0.9f;
        public AudioClip[] clanks;
        [Range(0f, 1f)] public float volume = 0.8f;
        [Tooltip("Swinging links knock together: a clink now and then while the chain moves this fast (m/s).")]
        public float clinkSpeed = 0.35f;
        [Tooltip("No link moves faster than this (m/s).")]
        public float maxSpeed = 4f;

        AudioSource source;
        float nextClank, nextClink;
        int lastClip = -1;

        void Awake()
        {
            source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 1f;
            source.rolloffMode = AudioRolloffMode.Logarithmic;
            source.minDistance = 1.2f;
            source.maxDistance = 22f;
            source.dopplerLevel = 0f;
            // the player walks through the links; it pushes them by hand below
            Physics.IgnoreLayerCollision(gameObject.layer, 2, true);
        }

        void FixedUpdate()
        {
            if (segments == null || segments.Length == 0) return;
            var pushers = ChainPusher.All;
            for (int p = 0; p < pushers.Count; p++)
            {
                ChainPusher pusher = pushers[p];
                if (!pusher || !pusher.isActiveAndEnabled) continue;
                Vector3 velocity = pusher.Velocity; velocity.y = 0f;
                Vector3 bottom = pusher.transform.position, top = bottom + Vector3.up * pusher.height;
                float hit = 0f;
                foreach (Rigidbody seg in segments)
                {
                    if (!seg) continue;
                    Vector3 c = seg.worldCenterOfMass;
                    Vector3 onBody = ClosestOnSegment(bottom, top, c);
                    Vector3 away = c - onBody; away.y = 0f;
                    float d = away.magnitude;
                    float reach = pusher.radius + touchRadius;
                    if (d > reach) continue;
                    // carried along at the body's pace (not faster), and out of its way
                    Vector3 want = velocity * pushStrength + (d > 1e-3f ? away / d : Vector3.zero) * (reach - d) * 4f;
                    Vector3 now = seg.linearVelocity; now.y = 0f;
                    Vector3 change = want - now;
                    if (Vector3.Dot(change, want) > 0f) seg.AddForce(Vector3.ClampMagnitude(change * 0.5f, 1.5f), ForceMode.VelocityChange);
                    hit = Mathf.Max(hit, velocity.magnitude + (reach - d) * 3f);
                }
                if (hit > 0.15f) Clank(Mathf.Clamp01(0.35f + hit * 0.25f));
            }

            // links knocking together while it swings (and a chain never flails faster than a hard shove)
            float swing = 0f;
            foreach (Rigidbody seg in segments)
            {
                if (!seg) continue;
                if (seg.linearVelocity.sqrMagnitude > maxSpeed * maxSpeed) seg.linearVelocity = seg.linearVelocity.normalized * maxSpeed;
                swing = Mathf.Max(swing, seg.linearVelocity.magnitude);
            }
            if (swing > clinkSpeed && Time.time >= nextClink)
            {
                nextClink = Time.time + Random.Range(0.18f, 0.5f) / Mathf.Clamp(swing, 0.5f, 3f);
                Play(Mathf.Clamp01(swing * 0.18f) * 0.6f);
            }
        }

        public void TakeDamage(in DamageInfo info)
        {
            Rigidbody nearest = null;
            float best = float.MaxValue;
            foreach (Rigidbody seg in segments)
            {
                if (!seg) continue;
                float d = (seg.worldCenterOfMass - info.point).sqrMagnitude;
                if (d < best) { best = d; nearest = seg; }
            }
            if (nearest) nearest.AddForceAtPosition(info.direction.normalized * Mathf.Clamp(info.amount * 0.15f, 1f, 6f), info.point, ForceMode.VelocityChange);
            Clank(1f);
        }

        void Clank(float loudness)
        {
            if (Time.time < nextClank) return;
            nextClank = Time.time + 0.22f;
            Play(loudness);
        }

        void Play(float loudness)
        {
            if (clanks == null || clanks.Length == 0 || loudness <= 0.01f) return;
            int i = Random.Range(0, clanks.Length);
            if (clanks.Length > 1 && i == lastClip) i = (i + 1) % clanks.Length;
            lastClip = i;
            source.pitch = Random.Range(0.9f, 1.12f);
            source.PlayOneShot(clanks[i], volume * loudness);
        }

        static Vector3 ClosestOnSegment(Vector3 a, Vector3 b, Vector3 p)
        {
            Vector3 ab = b - a;
            float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-6f));
            return a + ab * t;
        }
    }

}
