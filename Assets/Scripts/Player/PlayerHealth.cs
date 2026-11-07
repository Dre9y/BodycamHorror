using System;
using BodycamHorror.Combat;
using UnityEngine;

namespace BodycamHorror.Player
{
    /// <summary>
    /// Player hit points. Hits knock the bodycam, spike stress and make the flashlight flicker.
    /// Regenerates slowly after a calm. At zero the player is put back at the start (prototype).
    /// </summary>
    public class PlayerHealth : MonoBehaviour, IDamageable
    {
        public float maxHealth = 100f;
        [Tooltip("Calm time (s) after a hit before health comes back.")]
        public float regenDelay = 6f;
        public float regenRate = 6f;
        [Tooltip("Stress added per point of damage.")]
        public float stressPerDamage = 0.012f;
        public float traumaPerDamage = 0.02f;

        [Header("References (found automatically)")]
        public BodycamPlayerController player;
        public BodycamCameraRig rig;
        public PlayerStress stress;
        public BodycamFlashlight flashlight;

        [Header("Debug (read only)")]
        [SerializeField] float current;

        public float Health => current;
        public float Normalized => current / Mathf.Max(maxHealth, 1f);
        public bool IsDead => current <= 0f;
        public float LastHitTime { get; private set; } = -100f;

        public event Action<DamageInfo> Damaged;
        public event Action Died;

        Vector3 spawnPos;
        float spawnYaw;

        void Awake()
        {
            current = maxHealth;
            if (!player) player = GetComponent<BodycamPlayerController>();
            if (!rig) rig = GetComponentInChildren<BodycamCameraRig>();
            if (!stress) stress = GetComponent<PlayerStress>();
            if (!flashlight) flashlight = GetComponentInChildren<BodycamFlashlight>();
            spawnPos = transform.position;
            spawnYaw = transform.eulerAngles.y;
        }

        public void TakeDamage(in DamageInfo info)
        {
            if (IsDead || info.amount <= 0f) return;
            current = Mathf.Max(0f, current - info.amount);
            LastHitTime = Time.time;

            if (rig)
            {
                rig.AddTrauma(info.amount * traumaPerDamage);
                Vector3 local = rig.transform.InverseTransformDirection(info.direction);
                // shove the view along the blow: pitch from front/back, roll from the side
                rig.AddKick(local * 0.6f, new Vector3(-local.z * 160f, local.x * 90f, -local.x * 220f));
            }
            if (stress) stress.Add(info.amount * stressPerDamage);
            if (flashlight) flashlight.Flicker(0.35f);

            Damaged?.Invoke(info);
            if (IsDead) Die();
        }

        void Die()
        {
            Died?.Invoke();
            Debug.Log("[PlayerHealth] Player died - respawning at start.");
            if (player) player.Teleport(spawnPos, spawnYaw);
            current = maxHealth;
        }

        void Update()
        {
            if (!IsDead && current < maxHealth && Time.time - LastHitTime > regenDelay)
                current = Mathf.Min(maxHealth, current + regenRate * Time.deltaTime);
        }
    }
}
