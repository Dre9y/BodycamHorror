using UnityEngine;

namespace BodycamHorror.Combat
{
    /// <summary>
    /// Marks a body as flesh: bullets and blows leave a blood spray here instead of a bullet hole and
    /// grey dust. Looked up with GetComponentInParent on the hit collider.
    /// </summary>
    public class FleshSurface : MonoBehaviour
    {
        public Color bloodColor = new Color(0.32f, 0.04f, 0.03f, 0.9f);
        [Tooltip("Scales the amount of spray per hit.")]
        public float sprayAmount = 1f;
    }
}
