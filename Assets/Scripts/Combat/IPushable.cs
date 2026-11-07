using UnityEngine;

namespace BodycamHorror.Combat
{
    /// <summary>Something a melee shove can push back. Looked up with GetComponentInParent.</summary>
    public interface IPushable
    {
        /// <param name="direction">Flat push direction (away from the pusher).</param>
        /// <param name="strength">Push speed in m/s.</param>
        void Push(Vector3 direction, float strength);
    }
}
