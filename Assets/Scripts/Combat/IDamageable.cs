using UnityEngine;

namespace BodycamHorror.Combat
{
    public struct DamageInfo
    {
        public float amount;
        public Vector3 point;
        public Vector3 normal;
        /// <summary>Direction the bullet travelled.</summary>
        public Vector3 direction;
        public Collider collider;
        public GameObject source;
        /// <summary>A melee blow rather than a bullet (no headshot bonus, for example).</summary>
        public bool melee;
    }

    /// <summary>Anything that reacts to being shot. Looked up with GetComponentInParent on the hit collider.</summary>
    public interface IDamageable
    {
        void TakeDamage(in DamageInfo info);
    }
}
