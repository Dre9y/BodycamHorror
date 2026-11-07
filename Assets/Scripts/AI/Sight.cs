using System.Collections.Generic;
using UnityEngine;

namespace BodycamHorror.AI
{
    /// <summary>
    /// Line-of-sight queries shared by the monster AI. Monster colliders are registered here and
    /// skipped, so a monster never hides behind (or is blinded by) its own body or another monster.
    /// The player lives on Ignore Raycast, so it is skipped by the mask.
    /// </summary>
    public static class Sight
    {
        public static int Mask = ~(1 << 2);

        static readonly HashSet<Collider> ignored = new HashSet<Collider>();
        static readonly RaycastHit[] hits = new RaycastHit[24];

        public static void Register(Collider c) { if (c) ignored.Add(c); }
        public static void Unregister(Collider c) { if (c) ignored.Remove(c); }

        /// <summary>True when nothing solid (other than monsters) is between a and b.</summary>
        public static bool Clear(Vector3 a, Vector3 b)
        {
            Vector3 d = b - a;
            float len = d.magnitude;
            if (len < 0.001f) return true;
            int n = Physics.RaycastNonAlloc(a, d / len, hits, len, Mask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
                if (!ignored.Contains(hits[i].collider)) return false;
            return true;
        }

        /// <summary>Distance to the first solid thing from a towards b (or the full distance).</summary>
        public static float Distance(Vector3 a, Vector3 b)
        {
            Vector3 d = b - a;
            float len = d.magnitude;
            if (len < 0.001f) return 0f;
            int n = Physics.RaycastNonAlloc(a, d / len, hits, len, Mask, QueryTriggerInteraction.Ignore);
            float best = len;
            for (int i = 0; i < n; i++)
                if (!ignored.Contains(hits[i].collider) && hits[i].distance < best) best = hits[i].distance;
            return best;
        }

        /// <summary>Closest distance from point p to the segment a-b.</summary>
        public static float DistanceToSegment(Vector3 p, Vector3 a, Vector3 b)
        {
            Vector3 ab = b - a;
            float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 0.0001f));
            return Vector3.Distance(p, a + ab * t);
        }
    }
}
