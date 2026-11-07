using BodycamHorror.Weapons;
using UnityEngine;

namespace BodycamHorror.Player
{
    /// <summary>
    /// Physical stress / adrenaline, 0..1. Climbs with sprinting, shooting and hard knocks; the longer
    /// the exertion goes on without a break, the faster it climbs. Recovers after a short calm.
    /// Drives hand tremor and heavier breathing; later also a hook for heartbeat, vision effects etc.
    /// </summary>
    [DefaultExecutionOrder(-150)]
    public class PlayerStress : MonoBehaviour
    {
        public BodycamPlayerController player;
        public PlayerShooting shooting;

        [Header("Build-up")]
        [Tooltip("Per second while sprinting.")]
        public float sprintRate = 0.025f;
        [Tooltip("Per second while moving faster than a walk (not sprinting).")]
        public float runRate = 0.012f;
        public float perShot = 0.018f;
        public float perHardLanding = 0.08f;
        public float perStumble = 0.12f;
        public float perWallBump = 0.1f;
        [Tooltip("Non-stop exertion for this long (s) makes stress climb streakMultiplier times faster.")]
        public float streakTime = 15f;
        public float streakMultiplier = 2f;

        [Header("Recovery")]
        [Tooltip("Calm time (s) before stress starts to fall.")]
        public float recoverDelay = 2.5f;
        public float recoverRate = 0.05f;
        [Tooltip("Recovery per second while standing still.")]
        public float restRecoverRate = 0.1f;

        [Header("Debug (read only)")]
        [SerializeField, Range(0f, 1f)] float current;
        [SerializeField] float streak;

        /// <summary>0..1.</summary>
        public float Value => current;
        /// <summary>0..1, eased so low stress shows almost nothing and high stress shows a lot.</summary>
        public float Tremor => current * current * (3f - 2f * current);

        float lastExertion = -100f;

        void Awake()
        {
            if (!player) player = GetComponent<BodycamPlayerController>();
            if (!shooting && player) shooting = player.GetComponentInChildren<PlayerShooting>();
        }

        void OnEnable()
        {
            if (player) { player.LandingResolved += OnLanded; player.Impacted += OnImpacted; }
            if (shooting) shooting.Fired += OnFired;
        }

        void OnDisable()
        {
            if (player) { player.LandingResolved -= OnLanded; player.Impacted -= OnImpacted; }
            if (shooting) shooting.Fired -= OnFired;
        }

        /// <summary>Adds stress (scaled by the current exertion streak) and counts as exertion.</summary>
        public void Add(float amount)
        {
            current = Mathf.Clamp01(current + amount * StreakFactor);
            lastExertion = Time.time;
        }

        float StreakFactor => Mathf.Lerp(1f, streakMultiplier, Mathf.Clamp01(streak / Mathf.Max(streakTime, 0.01f)));

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f || !player) return;

            float rate = 0f;
            if (player.IsSprinting) rate = sprintRate;
            else if (player.State == MoveState.Grounded && player.Speed > player.walkSpeed + 0.3f) rate = runRate;
            if (rate > 0f) Add(rate * dt);

            bool exerting = Time.time - lastExertion < 1f;
            if (exerting) streak += dt;
            else if (Time.time - lastExertion > recoverDelay)
            {
                streak = Mathf.Max(0f, streak - dt * 3f);
                bool resting = player.Speed < 0.3f && player.State == MoveState.Grounded;
                current = Mathf.Max(0f, current - (resting ? restRecoverRate : recoverRate) * dt);
            }
        }

        void OnFired() => Add(perShot);

        void OnLanded(LandingKind kind, float drop)
        {
            if (kind == LandingKind.Hard) Add(perHardLanding * (1f + player.LastLandingSeverity));
            else if (kind != LandingKind.Normal) Add(perHardLanding * 0.2f);   // a good landing is still effort
        }

        void OnImpacted(ImpactKind kind, float speed) => Add(kind switch
        {
            ImpactKind.Stumble => perStumble,
            ImpactKind.Wall => perWallBump,
            _ => perWallBump * 0.25f,
        });
    }
}
