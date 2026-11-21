using System.Collections.Generic;
using UnityEngine;

namespace BodycamHorror.Environment
{
    /// <summary>
    /// Marks a doorway (a corridor's mouth, a door frame): the light monster (<see cref="Enemies.Manthing"/>) shoots
    /// through it and cannot be frozen inside it, so it is never caught standing half in the frame, clipping through the wall.
    /// A flat circle on the floor, centre here.
    /// </summary>
    public class Doorway : MonoBehaviour
    {
        public static readonly List<Doorway> All = new List<Doorway>();

        [Tooltip("Radius around the doorway's middle it covers (m).")]
        public float radius = 0.8f;

        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        /// <summary>Whether the point is in any doorway (widened by <paramref name="margin"/>).</summary>
        public static bool At(Vector3 p, float margin = 0f)
        {
            foreach (Doorway d in All)
            {
                if (!d) continue;
                Vector3 v = p - d.transform.position;
                if (Mathf.Abs(v.y) > 2f) continue;
                v.y = 0f;
                float r = d.radius + margin;
                if (v.sqrMagnitude <= r * r) return true;
            }
            return false;
        }

        void OnDrawGizmos()
        {
            Gizmos.color = new Color(1f, 0.6f, 0.1f, 0.6f);
            Gizmos.DrawWireSphere(transform.position + Vector3.up * 0.05f, radius);
        }
    }
}
