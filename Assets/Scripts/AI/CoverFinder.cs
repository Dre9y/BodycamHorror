using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace BodycamHorror.AI
{
    /// <summary>What a cover search wants: where the threat looks from and how to weigh candidates.</summary>
    public struct CoverRequest
    {
        public Vector3 self;            // monster feet
        public Vector3 threatEye;       // player eye
        public Vector3 threatForward;   // player look direction
        public Vector3 searchCenter;    // around what to sample
        public float minRadius, maxRadius;
        /// <summary>Best distance to the threat; candidates are scored by how close they are to it.</summary>
        public float desiredThreatDistance;
        /// <summary>0..1: how much to prefer spots out of the threat's field of view (flanking).</summary>
        public float flankWeight;
        /// <summary>0..1: how much to punish exposed path segments on the way there.</summary>
        public float exposureWeight;
        /// <summary>Weight of the path length from the monster.</summary>
        public float travelWeight;
        /// <summary>Required hiding height above the feet (crouched chest/head).</summary>
        public float hideHeight;
        /// <summary>Don't pick spots closer than this to the threat.</summary>
        public float minThreatDistance;
    }

    public struct CoverSpot
    {
        public Vector3 position;
        /// <summary>Points from the spot towards whatever blocks the threat's view.</summary>
        public Vector3 coverNormal;
        public float score;
        public bool hugging;            // a wall/crate is within arm's reach between us and the threat
    }

    /// <summary>
    /// Finds hiding spots and peek positions on the NavMesh. Candidates come from rings of samples
    /// snapped to the closest NavMesh edge (edges sit next to walls and crates, i.e. next to cover).
    /// </summary>
    public static class CoverFinder
    {
        public struct Burned { public Vector3 position; public float until; }

        static readonly List<CoverSpot> candidates = new List<CoverSpot>(96);
        static readonly Vector3[] corners = new Vector3[32];
        static NavMeshPath path;

        public static bool IsHidden(Vector3 feet, Vector3 eye, float hideHeight)
        {
            // chest and top of the crouched body must both be blocked
            return !Sight.Clear(eye, feet + Vector3.up * hideHeight) &&
                   !Sight.Clear(eye, feet + Vector3.up * (hideHeight * 0.5f));
        }

        public static bool Find(in CoverRequest req, List<Burned> burned, IList<Vector3> avoid, out CoverSpot best)
        {
            path ??= new NavMeshPath();
            candidates.Clear();
            best = default;

            const int rings = 4, perRing = 14;
            float angleOffset = Random.value * 360f;
            for (int r = 0; r < rings; r++)
            {
                float radius = Mathf.Lerp(req.minRadius, req.maxRadius, (r + 0.5f) / rings);
                for (int i = 0; i < perRing; i++)
                {
                    float a = (angleOffset + (i + (r & 1) * 0.5f) * 360f / perRing) * Mathf.Deg2Rad;
                    Vector3 p = req.searchCenter + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius;
                    if (!NavMesh.SamplePosition(p, out NavMeshHit hit, 2f, NavMesh.AllAreas)) continue;
                    Vector3 spot = hit.position;
                    if (NavMesh.FindClosestEdge(spot, out NavMeshHit edge, NavMesh.AllAreas) && edge.distance < 2.5f)
                        spot = edge.position;
                    Evaluate(req, spot, burned, avoid);
                }
            }

            if (candidates.Count == 0) return false;
            candidates.Sort((x, y) => y.score.CompareTo(x.score));

            // the expensive part (paths + exposure) only for the best few
            float bestScore = float.NegativeInfinity;
            int checks = Mathf.Min(6, candidates.Count);
            for (int i = 0; i < checks; i++)
            {
                CoverSpot c = candidates[i];
                if (!NavMesh.CalculatePath(req.self, c.position, NavMesh.AllAreas, path) ||
                    path.status != NavMeshPathStatus.PathComplete) continue;
                int n = path.GetCornersNonAlloc(corners);
                float length = 0f, exposed = 0f;
                for (int k = 1; k < n; k++)
                {
                    float seg = Vector3.Distance(corners[k - 1], corners[k]);
                    length += seg;
                    // sample the segment: count meters seen by the threat
                    int samples = Mathf.Clamp(Mathf.CeilToInt(seg / 2f), 1, 6);
                    for (int s = 1; s <= samples; s++)
                    {
                        Vector3 q = Vector3.Lerp(corners[k - 1], corners[k], s / (float)samples);
                        if (Sight.Clear(req.threatEye, q + Vector3.up * req.hideHeight)) exposed += seg / samples;
                    }
                }
                c.score -= length * req.travelWeight + exposed * req.exposureWeight * 0.6f;
                if (c.score > bestScore) { bestScore = c.score; best = c; }
            }
            return bestScore > float.NegativeInfinity;
        }

        static void Evaluate(in CoverRequest req, Vector3 spot, List<Burned> burned, IList<Vector3> avoid)
        {
            Vector3 toThreat = req.threatEye - spot; toThreat.y = 0f;
            float threatDist = toThreat.magnitude;
            if (threatDist < req.minThreatDistance) return;

            if (burned != null)
                for (int i = 0; i < burned.Count; i++)
                    if (burned[i].until > Time.time && (burned[i].position - spot).sqrMagnitude < 2.25f) return;
            if (avoid != null)
                for (int i = 0; i < avoid.Count; i++)
                    if ((avoid[i] - spot).sqrMagnitude < 4f) return;

            for (int i = 0; i < candidates.Count; i++)
                if ((candidates[i].position - spot).sqrMagnitude < 0.5f) return;

            if (!IsHidden(spot, req.threatEye, req.hideHeight)) return;

            Vector3 dir = toThreat / Mathf.Max(threatDist, 0.01f);
            float coverDist = Sight.Distance(spot + Vector3.up * req.hideHeight * 0.7f,
                                             spot + Vector3.up * req.hideHeight * 0.7f + dir * 3f);
            bool hugging = coverDist < 1.6f;

            float score = 10f;
            score -= Mathf.Abs(threatDist - req.desiredThreatDistance) * 0.8f;
            if (hugging) score += 4f;
            // flank: spots the threat isn't looking towards
            Vector3 fwd = req.threatForward; fwd.y = 0f;
            if (fwd.sqrMagnitude > 0.001f)
            {
                float facing = Vector3.Dot(fwd.normalized, -dir);   // 1 = right in front of the player
                score -= facing * 5f * req.flankWeight;
            }

            candidates.Add(new CoverSpot { position = spot, coverNormal = dir, score = score, hugging = hugging });
        }

        /// <summary>
        /// Finds a spot just around the corner of a cover where the head can see the threat.
        /// side is +1/-1 relative to the direction to the threat (right/left); 0 = rises over a low cover.
        /// </summary>
        public static bool FindPeek(Vector3 cover, Vector3 threatEye, float headHeight, out Vector3 peek, out int side)
        {
            Vector3 toThreat = threatEye - cover; toThreat.y = 0f;
            Vector3 right = Vector3.Cross(Vector3.up, toThreat.normalized);
            int first = Random.value < 0.5f ? 1 : -1;
            for (float off = 0.45f; off <= 2.3f; off += 0.35f)
            {
                for (int k = 0; k < 2; k++)
                {
                    int s = k == 0 ? first : -first;
                    Vector3 q = cover + right * (s * off);
                    if (!NavMesh.SamplePosition(q, out NavMeshHit hit, 0.5f, NavMesh.AllAreas)) continue;
                    // must be reachable in a straight shuffle from the cover spot
                    if (NavMesh.Raycast(cover, hit.position, out _, NavMesh.AllAreas)) continue;
                    if (!Sight.Clear(hit.position + Vector3.up * headHeight, threatEye)) continue;
                    peek = hit.position; side = s;
                    return true;
                }
            }
            // low cover: standing up is enough
            if (Sight.Clear(cover + Vector3.up * (headHeight + 0.5f), threatEye))
            {
                peek = cover; side = 0;
                return true;
            }
            peek = cover; side = 0;
            return false;
        }

        /// <summary>Random reachable NavMesh point around center.</summary>
        public static bool RandomPoint(Vector3 center, float radius, out Vector3 point)
        {
            for (int i = 0; i < 8; i++)
            {
                Vector2 r = Random.insideUnitCircle * radius;
                if (NavMesh.SamplePosition(center + new Vector3(r.x, 0f, r.y), out NavMeshHit hit, 2f, NavMesh.AllAreas))
                {
                    point = hit.position;
                    return true;
                }
            }
            point = center;
            return false;
        }
    }
}
