using BodycamHorror.AI;
using BodycamHorror.Enemies;
using BodycamHorror.Weapons;
using UnityEngine;

namespace BodycamHorror.Player
{
    /// <summary>
    /// Fear / adrenaline, 0..1. Climbs with sprinting, shooting and hard knocks; the longer
    /// the exertion goes on without a break, the faster it climbs. Recovers after a short calm.
    /// Monsters nearby and wounds hold it up: they set a floor the fear climbs to and cannot drop
    /// below while they last (see Threat). Anything else that should frighten calls Add().
    /// Drives hand tremor, heavier breathing, and the heartbeat and ear ringing (PlayerFearAudio).
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

        [Header("Fear: monsters nearby")]
        [Tooltip("A monster further than this (m) does not frighten.")]
        public float threatRange = 16f;
        [Tooltip("Share of the fear from a monster that is after the player but out of sight (behind a wall). " +
                 "One that is out of sight and not hunting does not frighten at all: fear must not give it away.")]
        [Range(0f, 1f)] public float unseenThreat = 0.4f;
        [Tooltip("Share of the fear from a monster in plain view that is not after the player (yet).")]
        [Range(0f, 1f)] public float idleThreat = 0.7f;
        [Tooltip("Fear that a hunting monster right in front of the player holds (0-1).")]
        [Range(0f, 1f)] public float threatCeiling = 0.9f;
        [Tooltip("How fast fear climbs to the level the threat holds (per second).")]
        public float threatRate = 0.25f;

        [Header("Fear: wounds")]
        [Tooltip("Fear held at the brink of death (0-1); nothing at full health. Needs PlayerHealth.")]
        [Range(0f, 1f)] public float lowHealthFear = 0.5f;

        [Header("Recovery")]
        [Tooltip("Calm time (s) before stress starts to fall.")]
        public float recoverDelay = 2.5f;
        public float recoverRate = 0.05f;
        [Tooltip("Recovery per second while standing still.")]
        public float restRecoverRate = 0.1f;

        [Header("Debug (read only)")]
        [SerializeField, Range(0f, 1f)] float current;
        [SerializeField] float streak;
        [SerializeField, Range(0f, 1f)] float threat;
        [SerializeField, Range(0f, 1f)] float floor;

        /// <summary>0..1.</summary>
        public float Value => current;
        /// <summary>0..1, eased so low stress shows almost nothing and high stress shows a lot.</summary>
        public float Tremor => current * current * (3f - 2f * current);

        /// <summary>0..1: how frightening the monsters around are right now (distance, in view, hunting).</summary>
        public float Threat => threat;

        float lastExertion = -100f;
        float threatTarget, threatTimer;
        PlayerHealth health;

        void Awake()
        {
            if (!player) player = GetComponent<BodycamPlayerController>();
            if (!shooting && player) shooting = player.GetComponentInChildren<PlayerShooting>();
            health = GetComponent<PlayerHealth>();
        }

        void OnEnable()
        {
            if (player) { player.LandingResolved += OnLanded; player.Impacted += OnImpacted; }
            if (shooting) { shooting.Fired += OnFired; shooting.Meleed += OnMeleed; }
        }

        void OnDisable()
        {
            if (player) { player.LandingResolved -= OnLanded; player.Impacted -= OnImpacted; }
            if (shooting) { shooting.Fired -= OnFired; shooting.Meleed -= OnMeleed; }
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

            // --- what holds the fear up: monsters around and wounds
            threatTimer -= dt;
            if (threatTimer <= 0f)
            {
                threatTimer = 0.15f;
                threatTarget = MeasureThreat();
            }
            threat = Mathf.MoveTowards(threat, threatTarget, dt * 2f);
            float wounds = health ? (1f - health.Normalized) * lowHealthFear : 0f;
            floor = 1f - (1f - threat * threatCeiling) * (1f - wounds);
            if (current < floor) current = Mathf.MoveTowards(current, floor, threatRate * dt);

            bool exerting = Time.time - lastExertion < 1f;
            if (exerting) streak += dt;
            else if (Time.time - lastExertion > recoverDelay)
            {
                streak = Mathf.Max(0f, streak - dt * 3f);
                if (current > floor)
                {
                    bool resting = player.Speed < 0.3f && player.State == MoveState.Grounded;
                    current = Mathf.Max(floor, current - (resting ? restRecoverRate : recoverRate) * dt);
                }
            }
        }

        /// <summary>The most frightening monster around, 0..1, plus a little for each one more.</summary>
        float MeasureThreat()
        {
            Vector3 eye = player.cameraPivot ? player.cameraPivot.position : player.transform.position + Vector3.up * 1.6f;
            float worst = 0f, sum = 0f;

            var infected = Infected.All;
            for (int i = 0; i < infected.Count; i++)
            {
                Infected e = infected[i];
                if (!e || e.IsDead) continue;
                float t = ThreatFrom(eye, e.transform, e.transform.position + Vector3.up * 1.1f, e.IsAlerted);
                sum += t;
                if (t > worst) worst = t;
            }

            var monsters = MonsterBrain.All;
            for (int i = 0; i < monsters.Count; i++)
            {
                MonsterBrain m = monsters[i];
                if (!m || m.IsDead) continue;
                bool hunting = m.State == MonsterState.Stalk || m.State == MonsterState.Peek
                               || m.State == MonsterState.Charge || m.State == MonsterState.Attack;
                float t = ThreatFrom(eye, m.transform, m.ChestPosition, hunting);
                sum += t;
                if (t > worst) worst = t;
            }

            return Mathf.Clamp01(worst + (sum - worst) * 0.25f);
        }

        float ThreatFrom(Vector3 eye, Transform monster, Vector3 chest, bool hunting)
        {
            float dist = Vector3.Distance(eye, chest);
            if (dist >= threatRange) return 0f;
            // nothing solid in between, or the first thing in the way is the monster itself
            bool seen = !Physics.Linecast(eye, chest, out RaycastHit hit, Sight.Mask, QueryTriggerInteraction.Ignore)
                        || hit.collider.transform.IsChildOf(monster);
            float weight = seen ? (hunting ? 1f : idleThreat) : (hunting ? unseenThreat : 0f);
            if (weight <= 0f) return 0f;
            float near = 1f - dist / threatRange;
            return weight * near * (2f - near);   // eases out: already strong at mid range
        }

        void OnFired() => Add(perShot);

        void OnMeleed(bool connected) => Add(perShot * (connected ? 1.2f : 0.6f));

        void OnLanded(LandingKind kind, float drop)
        {
            if (kind == LandingKind.Hard) Add(perHardLanding * (1f + player.LastLandingSeverity));
            else if (kind != LandingKind.Normal) Add(perHardLanding * 0.2f);   // a good landing is still effort
        }

        void OnImpacted(ImpactKind kind, float speed) => Add(kind switch
        {
            ImpactKind.Stumble => perStumble,
            ImpactKind.Wall => perWallBump,
            ImpactKind.Scrape => perWallBump * 0.1f,
            _ => perWallBump * 0.25f,
        });
    }
}
