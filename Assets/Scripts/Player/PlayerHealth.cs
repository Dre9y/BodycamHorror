using System;
using BodycamHorror.Combat;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BodycamHorror.Player
{
    /// <summary>
    /// Hidden two-hit health. The first blow wounds the player (concussion: double vision, a ring in the ears)
    /// and starts a timer; another blow before it runs out kills. Left alone for the whole timer, the player
    /// recovers fully. No numbers on screen: the state is read only from the effects.
    /// Hits knock the bodycam, spike stress and make the flashlight flicker. At death the player is put back at
    /// the start (prototype).
    /// </summary>
    public class PlayerHealth : MonoBehaviour, IDamageable
    {
        [Tooltip("Blows it takes to die.")]
        [Min(1)] public int hitsToDie = 2;
        [Tooltip("Time (s) without being hit after which the wounds are gone.")]
        public float recoveryTime = 12f;
        [Tooltip("After a blow the player cannot be hurt for this long (s): time to see the double vision and get away. Also makes a multi-frame attack or a steam tick count as one blow.")]
        public float invulnerableTime = 2f;
        [Tooltip("Stress added per blow.")]
        public float stressPerHit = 0.3f;
        [Tooltip("Camera shake per point of the blow's damage (the hit's weight still reads in the camera).")]
        public float traumaPerDamage = 0.02f;

        [Header("References (found automatically)")]
        public BodycamPlayerController player;
        public BodycamCameraRig rig;
        public PlayerStress stress;
        public BodycamFlashlight flashlight;
        [Tooltip("The concussion look on the camera; added to the lens camera if missing.")]
        public ConcussionEffect concussion;
        [Tooltip("What the camera records at death (fall, the feed breaking up, NO SIGNAL); added if missing. Off: straight back to the start.")]
        public DeathCam deathCam;

        [Header("Debug")]
        [Tooltip("Editor / development builds: this key hits the player (from the front).")]
        public Key debugHitKey = Key.F6;
        [SerializeField] int hitsTaken;
        [SerializeField] float woundTimeLeft;

        /// <summary>Blows left before death.</summary>
        public int Health => Mathf.Max(0, hitsToDie - hitsTaken);
        public float Normalized => Health / (float)Mathf.Max(hitsToDie, 1);
        public bool IsDead => hitsTaken >= hitsToDie;
        public bool IsWounded => hitsTaken > 0 && !IsDead;
        /// <summary>Seconds until the wounds heal (0 when not wounded).</summary>
        public float WoundTimeLeft => woundTimeLeft;
        public float LastHitTime { get; private set; } = -100f;
        /// <summary>True for a short while after a blow: further hits do nothing.</summary>
        public bool IsInvulnerable => Time.time - LastHitTime < invulnerableTime;

        public event Action<DamageInfo> Damaged;
        public event Action Recovered;
        public event Action Died;

        Vector3 spawnPos;
        float spawnYaw;

        void Awake()
        {
            if (!player) player = GetComponent<BodycamPlayerController>();
            if (!rig) rig = GetComponentInChildren<BodycamCameraRig>();
            if (!stress) stress = GetComponent<PlayerStress>();
            if (!flashlight) flashlight = GetComponentInChildren<BodycamFlashlight>();
            if (!concussion) concussion = GetComponentInChildren<ConcussionEffect>(true);
            if (!concussion)
            {
                Camera cam = rig && rig.cam ? rig.cam : GetComponentInChildren<Camera>();
                if (cam) concussion = cam.gameObject.AddComponent<ConcussionEffect>();
            }
            if (concussion) concussion.health = this;
            if (!deathCam) deathCam = GetComponent<DeathCam>();
            if (!deathCam) deathCam = gameObject.AddComponent<DeathCam>();
            spawnPos = transform.position;
            spawnYaw = transform.eulerAngles.y;
        }

        public void TakeDamage(in DamageInfo info)
        {
            if (IsDead || info.amount <= 0f) return;
            if (IsInvulnerable) return;   // just hit: the same blow still landing, or a moment to get away
            LastHitTime = Time.time;
            hitsTaken++;
            woundTimeLeft = recoveryTime;

            if (rig)
            {
                rig.AddTrauma(info.amount * traumaPerDamage);
                Vector3 local = rig.transform.InverseTransformDirection(info.direction);
                // shove the view along the blow: pitch from front/back, roll from the side
                rig.AddKick(local * 0.6f, new Vector3(-local.z * 160f, local.x * 90f, -local.x * 220f));
            }
            if (stress) stress.Add(stressPerHit);
            if (flashlight) flashlight.Flicker(0.35f);

            lastBlowDirection = info.direction;
            Damaged?.Invoke(info);
            if (IsDead) Die();
        }

        /// <summary>Kills the player outright (debug).</summary>
        public void Kill()
        {
            if (IsDead) return;
            hitsTaken = hitsToDie;
            Die();
        }

        void Die()
        {
            Died?.Invoke();
            // stays dead (no more hits land) until the death cam hands back with Respawn
            if (deathCam && deathCam.isActiveAndEnabled) deathCam.Play(lastBlowDirection);
            else Respawn();
        }

        /// <summary>Back at the start, unhurt (prototype).</summary>
        public void Respawn()
        {
            Debug.Log("[PlayerHealth] Respawning at start.");
            if (player) player.Teleport(spawnPos, spawnYaw);
            hitsTaken = 0;
            woundTimeLeft = 0f;
            LastHitTime = -100f;
        }

        Vector3 lastBlowDirection;

        void Update()
        {
            if (IsWounded)
            {
                woundTimeLeft -= Time.deltaTime;
                if (woundTimeLeft <= 0f)
                {
                    woundTimeLeft = 0f;
                    hitsTaken = 0;
                    Recovered?.Invoke();
                }
            }

            var kb = Keyboard.current;
            if (Debug.isDebugBuild && kb != null && debugHitKey != Key.None && kb[debugHitKey].wasPressedThisFrame)
            {
                Vector3 fwd = rig ? rig.transform.forward : transform.forward;
                TakeDamage(new DamageInfo
                {
                    amount = 25f, point = transform.position + Vector3.up * 1.5f, normal = fwd, direction = -fwd,
                    source = gameObject, melee = true
                });
            }
        }
    }
}
