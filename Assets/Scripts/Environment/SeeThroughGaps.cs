using UnityEngine;

namespace BodycamHorror.Environment
{
    /// <summary>
    /// For a prop whose collider is a rough box around a model with gaps in it (a transformer's fins, legs, a grille):
    /// the box still stops bodies, bullets and the NavMesh, but sight is judged by the model's real shape, so what the
    /// player can see through the gaps counts as seen. At start-up the meshes under it get an exact copy as colliders
    /// on Ignore Raycast (inside the box, so they never touch anything; ordinary casts and the NavMesh skip that layer).
    /// Ask through <see cref="Blocked"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public class SeeThroughGaps : MonoBehaviour
    {
        const int IgnoreRaycast = 2;
        static readonly RaycastHit[] hits = new RaycastHit[16];

        MeshCollider[] exact;

        void Awake()
        {
            var filters = GetComponentsInChildren<MeshFilter>(true);
            exact = new MeshCollider[filters.Length];
            for (int i = 0; i < filters.Length; i++)
            {
                if (!filters[i].sharedMesh) continue;
                var go = new GameObject("SightShape");
                go.layer = IgnoreRaycast;
                go.transform.SetParent(filters[i].transform, false);
                var mc = go.AddComponent<MeshCollider>();
                mc.sharedMesh = filters[i].sharedMesh;
                exact[i] = mc;
            }
        }

        bool Hides(Ray ray, float dist)
        {
            if (exact == null) return true;   // not set up yet: the box it is
            foreach (var mc in exact)
                if (mc && mc.Raycast(ray, out _, dist)) return true;
            return false;
        }

        /// <summary>
        /// Like <c>Physics.Linecast(a, b, mask)</c>, but colliders of a <see cref="SeeThroughGaps"/> prop only block where
        /// the model itself is in the way, not its gaps.
        /// </summary>
        public static bool Blocked(Vector3 a, Vector3 b, int mask, QueryTriggerInteraction triggers = QueryTriggerInteraction.Ignore)
        {
            Vector3 d = b - a;
            float dist = d.magnitude;
            if (dist < 1e-4f) return false;
            var ray = new Ray(a, d / dist);
            int n = Physics.RaycastNonAlloc(ray, hits, dist, mask, triggers);
            if (n == hits.Length) return Physics.Linecast(a, b, mask, triggers);   // too many to look through: the plain answer
            for (int i = 0; i < n; i++)
            {
                var gaps = hits[i].collider.GetComponentInParent<SeeThroughGaps>();
                if (!gaps || gaps.Hides(ray, dist)) return true;
            }
            return false;
        }
    }
}
