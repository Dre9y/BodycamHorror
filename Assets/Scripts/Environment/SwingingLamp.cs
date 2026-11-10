using UnityEngine;

namespace BodycamHorror.Environment
{
    /// <summary>
    /// Swings a hanging lamp like a pendulum. Goes on the pivot at the ceiling end of the cord; the
    /// shade, bulb and light hang below it as children. The pace follows from the cord length, the
    /// two axes run slightly out of step so the lamp traces a slow ellipse, and the swing breathes
    /// in and out a little, as if a draught kept nudging it.
    /// </summary>
    public class SwingingLamp : MonoBehaviour
    {
        [Tooltip("Pivot to bulb (m): sets how fast it swings.")]
        public float cordLength = 0.72f;
        [Tooltip("Swing to each side, degrees: x = along the local X axis, y = along the local Z axis.")]
        public Vector2 amplitude = new Vector2(16f, 5f);
        [Tooltip("How much the swing grows and dies down over time (0 = steady).")]
        [Range(0f, 1f)] public float gustiness = 0.35f;
        [Tooltip("Seconds over which the swing strength drifts.")]
        public float gustPeriod = 7f;
        [Tooltip("Where in the swing it starts, 0..1; negative = random.")]
        public float startPhase = -1f;

        Quaternion rest;
        float phase, seed;

        void Awake()
        {
            rest = transform.localRotation;
            seed = Random.value * 100f;
            phase = (startPhase < 0f ? Random.value : startPhase) * Mathf.PI * 2f;
        }

        void OnDisable() => transform.localRotation = rest;

        void Update()
        {
            float omega = Mathf.Sqrt(9.81f / Mathf.Max(cordLength, 0.05f));
            phase += omega * Time.deltaTime;

            float gust = 1f - gustiness + gustiness * 2f * Mathf.PerlinNoise(seed, Time.time / Mathf.Max(gustPeriod, 0.1f));
            float alongX = amplitude.x * gust * Mathf.Sin(phase);
            float alongZ = amplitude.y * gust * Mathf.Sin(phase * 0.94f + 1.3f);
            // swinging along X is a turn about Z, and the other way round
            transform.localRotation = rest * Quaternion.Euler(alongZ, 0f, -alongX);
        }
    }
}
