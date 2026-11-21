using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace BodycamHorror.Environment
{
    /// <summary>
    /// A corner or a crossing lit by chemical sticks: the light monster (<see cref="Enemies.Manthing"/>) never steps into it.
    /// The zone carves itself out of the NavMesh (a NavMeshObstacle), so a path to the player inside it ends at its edge,
    /// and the monster's way round it goes past it, not through. Flat circle on the floor: centre here, <see cref="radius"/> across.
    /// </summary>
    public class SafeZone : MonoBehaviour
    {
        public static readonly List<SafeZone> All = new List<SafeZone>();

        [Tooltip("Radius of the safe circle on the floor (m). The monster's body stays outside it.")]
        public float radius = 1.8f;
        [Tooltip("Height of the zone (m): the player counts as inside only between the floor and this.")]
        public float height = 3f;

        NavMeshObstacle obstacle;

        public Vector3 Centre => transform.position;

        void Awake()
        {
            obstacle = GetComponent<NavMeshObstacle>();
            if (!obstacle) obstacle = gameObject.AddComponent<NavMeshObstacle>();
            obstacle.shape = NavMeshObstacleShape.Capsule;
            obstacle.center = Vector3.up * (height * 0.5f);
            obstacle.radius = radius;
            obstacle.height = height;
            obstacle.carving = true;
            obstacle.carveOnlyStationary = true;
        }

        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        public bool Contains(Vector3 p, float margin = 0f)
        {
            Vector3 d = p - transform.position;
            if (d.y < -0.5f || d.y > height) return false;
            d.y = 0f;
            return d.sqrMagnitude <= (radius + margin) * (radius + margin);
        }

        /// <summary>The zone the point is in, or null.</summary>
        public static SafeZone At(Vector3 p, float margin = 0f)
        {
            foreach (SafeZone z in All) if (z && z.Contains(p, margin)) return z;
            return null;
        }

        /// <summary>After the NavMesh is baked again: carve the zones into the new mesh.</summary>
        public static void Recarve()
        {
            foreach (SafeZone z in All)
            {
                if (!z || !z.obstacle) continue;
                z.obstacle.enabled = false;
                z.obstacle.enabled = true;
            }
        }

        void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.3f, 1f, 0.4f, 0.5f);
            const int n = 32;
            Vector3 c = transform.position + Vector3.up * 0.05f, prev = c + Vector3.right * radius;
            for (int i = 1; i <= n; i++)
            {
                float a = i / (float)n * Mathf.PI * 2f;
                Vector3 p = c + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius;
                Gizmos.DrawLine(prev, p);
                prev = p;
            }
        }
    }
}
