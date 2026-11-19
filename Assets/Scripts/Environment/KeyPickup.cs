using System;
using System.Collections.Generic;
using BodycamHorror.Player;
using UnityEngine;

namespace BodycamHorror.Environment
{
    /// <summary>
    /// A key lying about or hanging on a hook. The player looks at it and presses the use key (PlayerInteractor,
    /// E / D-pad down): it goes onto their KeyRing under <see cref="keyId"/>, the line "Ключ: <see cref="label"/>"
    /// shows, and this object is gone. A Door with the same <see cref="Door.keyId"/> then opens for them.
    /// Needs no collider.
    /// </summary>
    public class KeyPickup : MonoBehaviour
    {
        public static readonly List<KeyPickup> All = new List<KeyPickup>();
        static AudioClip generatedClip;
        /// <summary>A key was just taken by this player (raised before the pickup is destroyed).</summary>
        public static event Action<KeyPickup, GameObject> Taken;

        [Tooltip("What opens with it: a Door's Key Id.")]
        public string keyId = "flat1";
        [Tooltip("What the player is told they took.")]
        public string label = "кв. 1";
        [Tooltip("The key prompt floats this far above the key (m).")]
        public float promptHeight = 0.1f;
        [Tooltip("How close the player must be to take it (m, from the camera). 0 = PlayerInteractor's pickup reach.")]
        public float reach;
        [Tooltip("How directly they must look at it (deg). 0 = PlayerInteractor's pickup look angle.")]
        public float lookAngle;

        [Header("Glow (optional)")]
        [Tooltip("A small light that marks the key in the dark. It breathes a little.")]
        public Light glow;
        [Range(0f, 1f)] public float glowPulse = 0.3f;
        [Tooltip("Seconds per breath.")]
        public float glowPeriod = 2.2f;

        [Header("Sound (empty = generated)")]
        public AudioClip pickupClip;
        [Range(0f, 1f)] public float volume = 0.6f;

        public Vector3 PromptPoint => Centre + Vector3.up * promptHeight;
        /// <summary>Middle of the key as drawn.</summary>
        public Vector3 Centre => transform.TransformPoint(centreLocal);

        Vector3 centreLocal;
        float glowIntensity;

        void Update()
        {
            if (!glow || glowPeriod <= 0f) return;
            float breath = Mathf.Sin(Time.time * (Mathf.PI * 2f / glowPeriod));
            glow.intensity = glowIntensity * (1f + glowPulse * breath);
        }

        void Awake()
        {
            if (glow) glowIntensity = glow.intensity;
            bool any = false;
            Bounds b = default;
            foreach (var r in GetComponentsInChildren<Renderer>())
            {
                if (!any) { b = r.bounds; any = true; }
                else b.Encapsulate(r.bounds);
            }
            centreLocal = any ? transform.InverseTransformPoint(b.center) : Vector3.zero;
        }

        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);
        void Start() => PlayerInteractor.Ensure();

        public bool Take(GameObject player)
        {
            KeyRing ring = KeyRing.Of(player);
            if (!ring) return false;
            ring.Add(keyId, label);
            AudioClip clip = pickupClip ? pickupClip
                : generatedClip ? generatedClip : (generatedClip = BodycamHorror.Weapons.ProceduralAudio.DoubleClick(0.05f, 2600f, 0.45f));
            BodycamHorror.Audio.SpatialSound.PlayAt(clip, transform.position, volume, 1f, 100f);
            Taken?.Invoke(this, player);
            Destroy(gameObject);
            return true;
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.85f, 0.3f, 0.7f);
            Gizmos.DrawWireSphere(Application.isPlaying ? PromptPoint : transform.position, 0.03f);
        }
    }
}
