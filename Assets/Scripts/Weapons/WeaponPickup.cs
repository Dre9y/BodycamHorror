using System;
using System.Collections.Generic;
using BodycamHorror.Player;
using UnityEngine;

namespace BodycamHorror.Weapons
{
    /// <summary>
    /// A gun, or a weapon light for one, lying in the world. The player walks up, looks at it and presses
    /// the use key (PlayerInteractor, E / D-pad down): a gun goes into their hands (<see cref="PlayerShooting.Give"/>),
    /// a light is fitted to its gun (<see cref="Firearm.HasLight"/>, even one not picked up yet), and this
    /// object, with its model and its glow, is gone. Needs no collider.
    /// </summary>
    public class WeaponPickup : MonoBehaviour
    {
        public enum Kind { Pistol, Shotgun, PistolLight }

        public static readonly List<WeaponPickup> All = new List<WeaponPickup>();
        static AudioClip generatedClip;
        /// <summary>A pickup was just taken by this player (raised before the pickup is destroyed).</summary>
        public static event Action<WeaponPickup, GameObject> Taken;

        public Kind weapon = Kind.Pistol;
        [Tooltip("Draw it straight away (guns only).")]
        public bool equip = true;
        [Tooltip("Taking it switches the player's flashlight off.")]
        public bool flashlightOff;
        [Tooltip("The key prompt floats this far above the gun (m).")]
        public float promptHeight = 0.22f;
        [Tooltip("How close the player must be to take it (m, from the camera). 0 = PlayerInteractor's pickup reach.")]
        public float reach;
        [Tooltip("How directly they must look at it (deg). 0 = PlayerInteractor's pickup look angle.")]
        public float lookAngle;
        [Tooltip("The view must point at least this many degrees below the horizon to take it (0 = any): the player " +
                 "has to stand over it and look down at it.")]
        [Range(0f, 89f)] public float minLookDown;
        [Tooltip("A side it is easier to take it from (world direction from it towards the player, flattened). Zero: none.")]
        public Vector3 easySide;
        [Tooltip("From that side: this much more reach (m)...")]
        public float easySideReach = 0.5f;
        [Tooltip("...and this look angle (deg, 0 = unchanged).")]
        public float easySideLookAngle = 14f;

        [Header("Showcase (the screen on pickup)")]
        [Tooltip("Its pose to the camera (deg). Zero: turned to show its broad side.")]
        public Vector3 showcaseEuler;

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
        public Vector3 PromptPoint => Centre + Vector3.up * promptHeight;
        /// <summary>Middle of the gun as drawn (the model need not sit on this object's origin).</summary>
        public Vector3 Centre => transform.TransformPoint(centreLocal);

        float glowIntensity;
        Vector3 centreLocal;

        /// <summary>How much a player whose eye is at <paramref name="eye"/> stands on the easy side (0..1).</summary>
        public float EasySide(Vector3 eye)
        {
            Vector3 side = new Vector3(easySide.x, 0f, easySide.z);
            Vector3 to = eye - Centre; to.y = 0f;
            if (side.sqrMagnitude < 0.0001f || to.sqrMagnitude < 0.0001f) return 0f;
            return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.2f, 0.7f, Vector3.Dot(side.normalized, to.normalized)));
        }

        /// <summary>Reach and look angle for a player there (<paramref name="baseReach"/> / <paramref name="baseAngle"/>:
        /// the interactor's defaults, used where this has none of its own).</summary>
        public void Limits(Vector3 eye, float baseReach, float baseAngle, out float maxReach, out float maxAngle)
        {
            maxReach = reach > 0f ? reach : baseReach;
            maxAngle = lookAngle > 0f ? lookAngle : baseAngle;
            float k = EasySide(eye);
            if (k <= 0f) return;
            maxReach += easySideReach * k;
            if (easySideLookAngle > 0f) maxAngle = Mathf.Lerp(maxAngle, Mathf.Max(maxAngle, easySideLookAngle), k);
        }

        /// <summary>The view along <paramref name="forward"/> is tilted down far enough to take it.</summary>
        public bool LookingDown(Vector3 forward) =>
            minLookDown <= 0f || forward.normalized.y <= -Mathf.Sin(minLookDown * Mathf.Deg2Rad);

        void Awake()
        {
            if (glow) glowIntensity = glow.intensity;
            bool any = false;
            Bounds b = default;
            foreach (var r in GetComponentsInChildren<Renderer>())
            {
                if (r is ParticleSystemRenderer) continue;
                if (!any) { b = r.bounds; any = true; }
                else b.Encapsulate(r.bounds);
            }
            centreLocal = any ? transform.InverseTransformPoint(b.center) : Vector3.zero;
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
            Firearm gun = weapon == Kind.Shotgun ? shooting.FindWeapon<Shotgun>() : (Firearm)shooting.FindWeapon<Pistol>();
            if (weapon == Kind.PistolLight)
            {
                // the flashlight itself: the player has one from now on, and it fits under the pistol
                var light = player.GetComponentInChildren<BodycamFlashlight>(true);
                bool hadLight = light && light.owned, fitted = gun && gun.HasLight;
                if (hadLight && (fitted || !gun)) return false;
                if (light) light.owned = true;
                if (gun) gun.HasLight = true;
            }
            else if (!gun || !shooting.Give(gun, equip)) return false;

            AudioClip clip = pickupClip ? pickupClip
                : generatedClip ? generatedClip : (generatedClip = ProceduralAudio.DoubleClick(0.09f, 1500f, 0.6f));
            BodycamHorror.Audio.SpatialSound.PlayAt(clip, transform.position, volume, 1f, 100f);
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
