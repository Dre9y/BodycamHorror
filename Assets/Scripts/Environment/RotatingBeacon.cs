using UnityEngine;

namespace BodycamHorror.Environment
{
    /// <summary>
    /// A rotating warning beacon: the reflector in the dome (<see cref="rotor"/>) turns round the lamp's axis and a dim
    /// red beam (<see cref="beam"/>, a spot light on the reflector) sweeps the walls with it, over a faint steady glow
    /// in the dome. Each beacon starts at its own angle, so a row of them never turns in step.
    /// </summary>
    public class RotatingBeacon : MonoBehaviour
    {
        [Tooltip("The reflector: turned about the lamp's own up axis (the dome's axis).")]
        public Transform rotor;
        [Tooltip("The beam: a spot light riding on the reflector.")]
        public Light beam;
        [Tooltip("The steady glow in the dome.")]
        public Light glow;
        [Tooltip("Turns a second.")]
        public float turnsPerSecond = 1.1f;
        [Tooltip("The beam leans this far towards the floor from level (deg).")]
        public float beamTilt = 18f;

        float phase;
        Quaternion rotorRest;

        void Awake()
        {
            phase = Random.Range(0f, 360f);
            if (rotor) rotorRest = rotor.localRotation;
            if (beam && rotor)
            {
                // out of the reflector's mouth (its local -X: the model's +X mirrored on import), leaning to the floor
                Vector3 down = rotor.InverseTransformDirection(Vector3.down);
                Vector3 mouth = Vector3.left;
                Vector3 lean = Vector3.ProjectOnPlane(down, mouth).normalized;
                Vector3 dir = (mouth * Mathf.Cos(beamTilt * Mathf.Deg2Rad) + lean * Mathf.Sin(beamTilt * Mathf.Deg2Rad)).normalized;
                beam.transform.localRotation = Quaternion.LookRotation(dir, Vector3.up);
            }
        }

        void Update()
        {
            if (!rotor) return;
            float a = phase + Time.time * turnsPerSecond * 360f;
            rotor.localRotation = rotorRest * Quaternion.AngleAxis(a, Vector3.up);
        }
    }
}
