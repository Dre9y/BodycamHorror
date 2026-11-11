using System;
using System.Collections.Generic;
using BodycamHorror.Player;
using UnityEngine;

namespace BodycamHorror.Weapons
{
    /// <summary>
    /// A gun lying in the world. The player walks up, looks at it and presses the use key
    /// (PlayerInteractor, F / D-pad down): the gun goes into their hands (<see cref="PlayerShooting.Give"/>)
    /// and this object, with its model and its glow, is gone. Needs no collider.
    /// </summary>
    public class WeaponPickup : MonoBehaviour
    {
        public enum Kind { Pistol, Shotgun }

        public static readonly List<WeaponPickup> All = new List<WeaponPickup>();
        static AudioClip generatedClip;
        /// <summary>A pickup was just taken by this player (raised before the pickup is destroyed).</summary>
        public static event Action<WeaponPickup, GameObject> Taken;

        public Kind weapon = Kind.Pistol;
        [Tooltip("Draw it straight away.")]
        public bool equip = true;
        [Tooltip("Taking it switches the player's flashlight off.")]
        public bool flashlightOff;
        [Tooltip("The key prompt floats this far above the gun (m).")]
        public float promptHeight = 0.22f;

        [Header("Glow (optional)")]
        [Tooltip("A small light that marks the gun in the dark. It breathes a little.")]
        public Light glow;
        [Range(0f, 1f)] public float glowPulse = 0.18f;
        [Tooltip("Seconds per breath.")]
        public float glowPeriod = 2.6f;

        [Header("Sound (empty = generated)")]
        public AudioClip pickupClip;
        [Range(0f, 1f)] public float volume = 0.7f;

        /// <summary>Where the key prompt goes.</summary>
        public Vector3 PromptPoint => transform.position + Vector3.up * promptHeight;

        float glowIntensity;

        void Awake()
        {
            if (glow) glowIntensity = glow.intensity;
        }

        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);
        void Start() => PlayerInteractor.Ensure();

        void Update()
        {
            if (!glow || glowPeriod <= 0f) return;
            float breath = Mathf.Sin(Time.time * (Mathf.PI * 2f / glowPeriod));
            glow.intensity = glowIntensity * (1f + glowPulse * breath);
        }

        /// <summary>Gives the gun to this player. False if they cannot take it (no such gun on them, or they have it).</summary>
        public bool Take(GameObject player)
        {
            var shooting = player ? player.GetComponentInChildren<PlayerShooting>(true) : null;
            if (!shooting) return false;
            Firearm gun = weapon == Kind.Pistol ? shooting.FindWeapon<Pistol>() : (Firearm)shooting.FindWeapon<Shotgun>();
            if (!gun || !shooting.Give(gun, equip)) return false;

            AudioClip clip = pickupClip ? pickupClip
                : generatedClip ? generatedClip : (generatedClip = ProceduralAudio.DoubleClick(0.09f, 1500f, 0.6f));
            AudioSource.PlayClipAtPoint(clip, transform.position, volume);
            if (flashlightOff)
            {
                var flashlight = player.GetComponentInChildren<BodycamFlashlight>(true);
                if (flashlight && flashlight.IsOn) flashlight.SetOn(false, false);
            }
            Taken?.Invoke(this, player);
            Destroy(gameObject);
            return true;
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.4f, 0.9f, 1f, 0.6f);
            Gizmos.DrawWireSphere(PromptPoint, 0.05f);
        }
    }
}
