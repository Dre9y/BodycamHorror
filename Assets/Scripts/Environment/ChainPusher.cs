using System.Collections.Generic;
using UnityEngine;

namespace BodycamHorror.Environment
{
    /// <summary>A body that pushes hanging chains aside as it passes (an upright capsule from its feet).</summary>
    public class ChainPusher : MonoBehaviour
    {
        public static readonly List<ChainPusher> All = new List<ChainPusher>();
        public float radius = 0.35f;
        public float height = 1.8f;
        public Vector3 Velocity { get; private set; }
        Vector3 last;

        void OnEnable() { All.Add(this); last = transform.position; }
        void OnDisable() => All.Remove(this);

        void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;
            Velocity = dt > 0f ? (transform.position - last) / dt : Vector3.zero;
            last = transform.position;
        }
    }
}
