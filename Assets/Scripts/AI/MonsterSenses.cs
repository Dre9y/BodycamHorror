using BodycamHorror.Player;
using BodycamHorror.Weapons;
using UnityEngine;

namespace BodycamHorror.AI
{
    /// <summary>
    /// Eyes, ears and memory of a monster, plus a read of what the player is doing right now
    /// (aiming at us, reloading, stunned, looking away). The brain only decides; this only perceives.
    /// </summary>
    public class MonsterSenses : MonoBehaviour
    {
        [Header("Sight")]
        public float sightRange = 28f;
        [Tooltip("Full field of view (deg).")]
        public float fieldOfView = 150f;
        [Tooltip("Within this distance the player is noticed at any angle (breathing, smell).")]
        public float closeSenseRange = 3f;
        [Tooltip("Seconds of plain view to go from unaware to fully engaged, at mid range.")]
        public float timeToNotice = 0.9f;
        [Tooltip("A lit flashlight makes the player this much easier to spot.")]
        public float flashlightVisibility = 1.8f;
        public float crouchVisibility = 0.5f;

        [Header("Hearing (radius in m)")]
        public float shotNoise = 45f;
        public float sprintNoise = 14f;
        public float runNoise = 8f;
        public float walkNoise = 4f;
        public float crouchNoise = 1.5f;
        public float landingNoise = 16f;

        [Header("Memory")]
        [Tooltip("After losing sight the monster still 'feels' where the player went for this long.")]
        public float intuitionTime = 1.2f;
        [Tooltip("Forget the player completely after this long out of sight.")]
        public float memoryTime = 14f;

        [Header("Threat read")]
        [Tooltip("Player counts as aiming at us when the view is within this angle (plus our angular size).")]
        public float aimAngle = 7f;
        [Tooltip("A shot passing this close counts as being shot at.")]
        public float suppressionRadius = 2f;

        // ------------------------------------------------------------------ state read by the brain

        public BodycamPlayerController Player { get; private set; }
        public PlayerHealth PlayerHealth { get; private set; }

        /// <summary>0 = unaware, 1 = fully engaged.</summary>
        public float Awareness { get; private set; }
        public bool CanSeePlayer { get; private set; }
        /// <summary>The player has line of sight to our upper body.</summary>
        public bool PlayerSeesUs { get; private set; }
        /// <summary>Player looks (roughly) straight at us and can see us.</summary>
        public bool UnderAim { get; private set; }
        public float UnderAimTime { get; private set; }
        public float LastSuppressedTime { get; private set; } = -100f;

        public Vector3 LastKnownPosition { get; private set; }
        public Vector3 LastKnownVelocity { get; private set; }
        public float LastSeenTime { get; private set; } = -100f;
        public bool HasTarget => Time.time - LastSeenTime < memoryTime && knowsTarget;

        public Vector3 NoisePosition { get; private set; }
        public float NoiseTime { get; private set; } = -100f;

        public Vector3 PlayerEye => Player ? Player.cameraPivot.position : Vector3.zero;
        public Vector3 PlayerFeet => Player ? Player.transform.position : Vector3.zero;
        public Vector3 PlayerForward => Player ? Player.cameraPivot.forward : Vector3.forward;

        MonsterBrain brain;
        PlayerShooting shooting;
        Pistol pistol;
        BodycamFlashlight flashlight;
        bool knowsTarget;
        float seenFor;

        /// <summary>Seconds of continuous sight of the player.</summary>
        public float SeenFor => seenFor;

        void Awake()
        {
            brain = GetComponent<MonsterBrain>();
            Player = FindAnyObjectByType<BodycamPlayerController>();
            if (Player)
            {
                PlayerHealth = Player.GetComponent<PlayerHealth>();
                shooting = Player.GetComponentInChildren<PlayerShooting>();
                pistol = Player.GetComponentInChildren<Pistol>();
                flashlight = Player.GetComponentInChildren<BodycamFlashlight>();
            }
        }

        void OnEnable()
        {
            if (pistol) pistol.Fired += OnPlayerFired;
            if (Player) Player.LandingResolved += OnPlayerLanded;
        }

        void OnDisable()
        {
            if (pistol) pistol.Fired -= OnPlayerFired;
            if (Player) Player.LandingResolved -= OnPlayerLanded;
        }

        // ------------------------------------------------------------------ player read

        /// <summary>Player is busy and can't shoot back well: reloading, stumbling, climbing, landing hard.</summary>
        public bool PlayerVulnerable
        {
            get
            {
                if (!Player) return false;
                if (shooting && shooting.IsReloading) return true;
                switch (Player.State)
                {
                    case MoveState.Mantling: case MoveState.Vaulting: case MoveState.Stumbling:
                    case MoveState.Bumped: case MoveState.HardLanding: case MoveState.Rolling:
                    case MoveState.HandLanding:
                        return true;
                }
                return pistol && pistol.Ammo <= 0;
            }
        }

        public bool PlayerFlashlightOn => flashlight && flashlight.IsOn;
        public float PlayerAimWeight => shooting ? shooting.AimWeight : 0f;

        /// <summary>Angle (deg) between where the player looks and the direction to a point.</summary>
        public float PlayerLookAngleTo(Vector3 point) => Player ? Vector3.Angle(PlayerForward, point - PlayerEye) : 180f;

        /// <summary>Where the player will probably be after t seconds.</summary>
        public Vector3 Predict(float t) => LastKnownPosition + LastKnownVelocity * t;

        // ------------------------------------------------------------------ perception

        public void Tick(float dt, Vector3 eye, Vector3 chest)
        {
            if (!Player) return;
            Vector3 pEye = PlayerEye;
            Vector3 pChest = Player.transform.position + Vector3.up * (Player.IsCrouching ? 0.6f : 1.1f);
            Vector3 toPlayer = pEye - eye;
            float dist = toPlayer.magnitude;

            // --- can we see the player?
            bool inRange = dist < sightRange * (PlayerFlashlightOn ? 1.25f : 1f);
            bool inCone = Vector3.Angle(transform.forward, toPlayer) < fieldOfView * 0.5f || dist < closeSenseRange;
            CanSeePlayer = inRange && inCone && (Sight.Clear(eye, pEye) || Sight.Clear(eye, pChest));

            // a flashlight pointed our way is seen from far away even outside the cone
            if (!CanSeePlayer && PlayerFlashlightOn && dist < sightRange * 1.6f &&
                PlayerLookAngleTo(eye) < 25f && Sight.Clear(eye, pEye))
                CanSeePlayer = true;

            if (CanSeePlayer)
            {
                float vis = 1f - Mathf.Clamp01(dist / (sightRange * 1.4f));
                vis *= Player.IsCrouching ? crouchVisibility : 1f;
                if (PlayerFlashlightOn) vis *= flashlightVisibility;
                if (Player.IsSprinting) vis *= 1.4f;
                if (dist < closeSenseRange) vis = 3f;
                Awareness = Mathf.Clamp01(Awareness + dt * vis / Mathf.Max(timeToNotice, 0.05f) * 1.6f);
                seenFor += dt;
                if (Awareness >= 0.99f) Know(true);
                else if (Awareness > 0.35f) Hear(Player.transform.position, 0f);
            }
            else
            {
                seenFor = 0f;
                // intuition: keep tracking for a moment after the player breaks line of sight
                if (knowsTarget && Time.time - LastSeenTime < intuitionTime) Know(false);
                float decay = HasTarget ? 0.03f : 0.12f;
                Awareness = Mathf.Max(0f, Awareness - decay * dt);
                if (Time.time - LastSeenTime > memoryTime) knowsTarget = false;
            }

            // --- movement noise
            float noise = 0f;
            if (Player.State == MoveState.Grounded || Player.State == MoveState.Sliding)
            {
                float v = Player.Speed;
                if (Player.IsSprinting) noise = sprintNoise;
                else if (Player.IsCrouching) noise = v > 0.5f ? crouchNoise : 0f;
                else if (v > Player.walkSpeed + 0.3f) noise = runNoise;
                else if (v > 0.5f) noise = walkNoise;
            }
            if (noise > 0f && dist < noise) Hear(Player.transform.position, 1f - dist / noise);

            // --- does the player see / aim at us?
            PlayerSeesUs = Sight.Clear(pEye, chest) || Sight.Clear(pEye, eye);
            float angularSize = Mathf.Atan2(0.6f, Mathf.Max(dist, 0.5f)) * Mathf.Rad2Deg;
            bool aimed = PlayerSeesUs && PlayerLookAngleTo(chest) < aimAngle + angularSize;
            UnderAim = aimed;
            UnderAimTime = aimed ? UnderAimTime + dt : 0f;
        }

        void Know(bool seen)
        {
            Vector3 v = Player.HorizontalVelocity;
            LastKnownVelocity = Vector3.Lerp(LastKnownVelocity, v, 0.5f);
            LastKnownPosition = Player.transform.position;
            knowsTarget = true;
            if (seen) { LastSeenTime = Time.time; Awareness = 1f; }
        }

        /// <summary>A noise at pos; loudness 0..1 raises suspicion, and 1 pins a rough position.</summary>
        public void Hear(Vector3 pos, float loudness)
        {
            float error = Vector3.Distance(transform.position, pos) * 0.12f * (1f - loudness);
            Vector2 e = Random.insideUnitCircle * error;
            NoisePosition = pos + new Vector3(e.x, 0f, e.y);
            NoiseTime = Time.time;
            Awareness = Mathf.Clamp01(Mathf.Max(Awareness, 0.4f + loudness * 0.5f));
        }

        /// <summary>We got hurt: we know exactly where it came from.</summary>
        public void OnDamaged()
        {
            if (!Player) return;
            Awareness = 1f;
            LastSeenTime = Time.time;
            Know(true);
            LastSuppressedTime = Time.time;
        }

        void OnPlayerFired(bool hit, RaycastHit info)
        {
            if (!Player) return;
            Vector3 origin = pistol.muzzle ? pistol.muzzle.position : PlayerEye;
            Vector3 end = hit ? info.point : origin + (pistol.muzzle ? pistol.muzzle.forward : PlayerForward) * pistol.range;
            Vector3 chest = transform.position + Vector3.up * 1f;
            if (Sight.DistanceToSegment(chest, origin, end) < suppressionRadius) LastSuppressedTime = Time.time;

            float d = Vector3.Distance(transform.position, Player.transform.position);
            if (d < shotNoise)
            {
                Hear(Player.transform.position, 1f - d / shotNoise * 0.5f);
                // a gunshot this close gives the position away
                if (d < shotNoise * 0.5f) { Know(false); LastSeenTime = Mathf.Max(LastSeenTime, Time.time - intuitionTime * 0.5f); }
            }
        }

        void OnPlayerLanded(LandingKind kind, float drop)
        {
            float d = Vector3.Distance(transform.position, Player.transform.position);
            float r = kind == LandingKind.Hard ? landingNoise : landingNoise * 0.5f;
            if (drop > 1f && d < r) Hear(Player.transform.position, 1f - d / r);
        }
    }
}
