using UnityEngine;

namespace BodycamHorror.UI
{
    /// <summary>
    /// The main menu's camera lies low on the floor; this lets it breathe a little (a slow, uneven sway of
    /// position and aim) and leans it a touch towards the mouse, so the still picture feels alive.
    /// </summary>
    public class MenuCameraDrift : MonoBehaviour
    {
        [Tooltip("How far it sways (m).")]
        public float swayDistance = 0.012f;
        [Tooltip("How much its aim wanders (degrees).")]
        public float swayAngle = 0.45f;
        [Tooltip("Seconds per sway, roughly.")]
        public float period = 7f;
        [Tooltip("How far it turns after the mouse at the screen's edge (degrees).")]
        public float mouseLean = 0.8f;

        Vector3 basePos;
        Quaternion baseRot;
        Vector2 lean;
        float seed;

        void Start()
        {
            basePos = transform.localPosition;
            baseRot = transform.localRotation;
            seed = Random.value * 100f;
        }

        void LateUpdate()
        {
            float t = (Time.unscaledTime + seed) / Mathf.Max(period, 0.1f);
            float n(float o) => Mathf.PerlinNoise(t, o) * 2f - 1f;

            Vector2 mouse = Vector2.zero;
            var m = UnityEngine.InputSystem.Mouse.current;
            if (m != null && Screen.width > 0)
            {
                Vector2 p = m.position.ReadValue();
                mouse = new Vector2(p.x / Screen.width - 0.5f, p.y / Screen.height - 0.5f) * 2f;
                mouse = Vector2.ClampMagnitude(mouse, 1f);
            }
            lean = Vector2.Lerp(lean, mouse, 1f - Mathf.Exp(-Time.unscaledDeltaTime * 1.5f));

            transform.localPosition = basePos + new Vector3(n(1.3f), n(7.1f) * 0.6f, 0f) * swayDistance;
            transform.localRotation = baseRot * Quaternion.Euler(
                n(3.7f) * swayAngle - lean.y * mouseLean * 0.6f,
                n(5.9f) * swayAngle + lean.x * mouseLean,
                n(9.2f) * swayAngle * 0.5f);
        }
    }
}
