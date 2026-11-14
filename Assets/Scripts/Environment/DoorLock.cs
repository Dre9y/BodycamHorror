using BodycamHorror.Combat;
using BodycamHorror.Weapons;
using UnityEngine;
using UnityEngine.Events;

namespace BodycamHorror.Environment
{
    /// <summary>
    /// A padlock on a <see cref="Door"/>: while it hangs there the door is locked and only rattles when
    /// tried. It cannot be opened by hand - it has to be shot off. Bullets that hit it wear it down;
    /// when it gives, the padlock drops to the floor and the door can be opened like any other.
    /// Sits on the door's leaf with a collider around the padlock for the bullets to find.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class DoorLock : MonoBehaviour, IDamageable
    {
        [Tooltip("The door it locks. Found on a parent when empty.")]
        public Door door;
        [Tooltip("The part that falls off when the lock is shot through.")]
        public Transform padlock;

        [Tooltip("Damage it takes before it gives. A pistol bullet does 34, one shotgun pellet 13: at 1 any hit breaks it.")]
        public float health = 1f;
        [Tooltip("Fists and gun butts do nothing to it.")]
        public bool bulletsOnly = true;

        [Header("Sound (empty = generated)")]
        [Tooltip("A bullet hitting the lock without breaking it.")]
        public AudioClip hitClip;
        [Tooltip("The lock giving way.")]
        public AudioClip breakClip;
        [Range(0f, 1f)] public float volume = 0.8f;

        [Tooltip("Fires once, when the lock is shot off.")]
        public UnityEvent broken;

        public bool IsBroken { get; private set; }

        static AudioClip generatedHit, generatedBreak;

        void Awake()
        {
            if (!door) door = GetComponentInParent<Door>();
            if (door) door.locked = true;
        }

        public void TakeDamage(in DamageInfo info)
        {
            if (IsBroken || info.amount <= 0f || (bulletsOnly && info.melee)) return;
            health -= info.amount;
            if (health > 0f)
            {
                AudioClip ping = hitClip ? hitClip : generatedHit ? generatedHit : (generatedHit = ProceduralAudio.Click(0.035f, 3400f, 0.7f));
                AudioSource.PlayClipAtPoint(ping, transform.position, volume * 0.7f);
                return;
            }
            Break(info.direction);
        }

        void Break(Vector3 shotDirection)
        {
            IsBroken = true;
            if (door) door.locked = false;
            foreach (Collider c in GetComponents<Collider>()) c.enabled = false;   // nothing left to shoot at

            if (padlock)
            {
                // off the door and onto the floor, knocked the way the bullet went
                padlock.SetParent(null, true);
                var renderer = padlock.GetComponentInChildren<MeshRenderer>();
                var box = padlock.gameObject.AddComponent<BoxCollider>();
                if (renderer)
                {
                    Bounds b = renderer.localBounds;
                    box.center = padlock.InverseTransformPoint(renderer.transform.TransformPoint(b.center));
                    box.size = b.size;
                }
                else box.size = new Vector3(0.05f, 0.07f, 0.03f);
                var body = padlock.gameObject.AddComponent<Rigidbody>();
                body.mass = 0.4f;
                Vector3 push = shotDirection.sqrMagnitude > 0.01f ? shotDirection.normalized : -transform.forward;
                body.AddForce(push * 1.2f + Vector3.up * 0.6f, ForceMode.Impulse);
                body.AddTorque(Random.insideUnitSphere * 0.3f, ForceMode.Impulse);
            }

            AudioClip clip = breakClip ? breakClip : generatedBreak ? generatedBreak : (generatedBreak = ProceduralAudio.DoubleClick(0.06f, 2200f, 1f));
            AudioSource.PlayClipAtPoint(clip, transform.position, volume);
            broken?.Invoke();
        }
    }
}
