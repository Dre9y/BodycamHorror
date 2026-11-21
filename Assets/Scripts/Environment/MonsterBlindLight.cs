using UnityEngine;

namespace BodycamHorror.Environment
{
    /// <summary>
    /// Marks a light the light monster (<see cref="Enemies.Manthing"/>) does not freeze in: it lights the room for the
    /// player, but only the flashlight and plain lamps hold the monster still (emergency lighting on the escape).
    /// </summary>
    [RequireComponent(typeof(Light))]
    public class MonsterBlindLight : MonoBehaviour { }
}
