using System;
using System.Collections.Generic;
using BodycamHorror.Combat;
using UnityEngine;
using UnityEngine.AI;
using Random = UnityEngine.Random;

namespace BodycamHorror.AI
{
    public enum MonsterState { Wander, Investigate, Search, Stalk, Hide, Peek, Charge, Attack, Retreat, Flee, Stagger, Dead }

    /// <summary>What the monster chose to do when the player got hostile.</summary>
    public enum MonsterResponse { None, Fight, Hide, Flee }

    /// <summary>
    /// Watcher, not a berserker. Once it finds the player it keeps them under observation from the
    /// edge of their view: picks watch spots behind cover at a comfortable distance, peeks around
    /// corners, ducks back when stared at and moves on to keep the player in sight as they move.
    /// It never attacks on a timer. A threat meter measures how hostile the player is to it (aiming at
    /// it, shooting near it, hitting it, closing in, chasing it); when the meter fills, or the player
    /// gets right on top of it, it decides between fighting, hiding and fleeing depending on how
    /// close the player is, whether it is cornered, hurt, aimed at, or the player is busy reloading.
    /// Only one monster attacks at a time.
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent), typeof(MonsterSenses))]
    public class MonsterBrain : MonoBehaviour, IDamageable
    {
        [Header("Health")]
        public float maxHealth = 200f;
        public float headshotMultiplier = 2.5f;
        [Tooltip("This much damage within half a second staggers it.")]
        public float staggerDamage = 60f;
        [Tooltip("Below this share of health it runs away to recover.")]
        [Range(0f, 1f)] public float fleeHealth = 0.35f;
        public float regenDelay = 6f;
        public float regenRate = 2.5f;
        [Tooltip("Respawn after death (s). 0 = stay dead.")]
        public float respawnDelay = 10f;

        [Header("Speeds (m/s)")]
        public float wanderSpeed = 1.6f;
        public float stalkSpeed = 3.2f;
        public float coverSpeed = 6.5f;
        public float fleeSpeed = 7.5f;
        public float chargeSpeed = 8.8f;
        public float peekSpeed = 1.5f;
        public float turnSpeed = 540f;

        [Header("Body")]
        public float standEyeHeight = 1.6f;
        public float crouchEyeHeight = 0.95f;
        [Tooltip("Height that must be hidden when crouched behind cover.")]
        public float hideHeight = 1.05f;

        [Header("Observation")]
        [Tooltip("Comfortable watching distance: closer makes it back off, further makes it follow.")]
        public Vector2 observeDistance = new Vector2(9f, 18f);
        public float preferredObserveDistance = 13f;
        [Tooltip("Preferred angle off the player's look direction for watch spots (deg). ~75 = edge of view.")]
        public float peripheryAngle = 75f;
        [Tooltip("Out of sight this long (s) while watching: move to a spot that sees the player again.")]
        public float lostSightRelocate = 4f;
        [Tooltip("Moves to a fresh watch spot every so often even if nothing happens (s).")]
        public Vector2 relocateEvery = new Vector2(9f, 16f);
        public Vector2 hideTime = new Vector2(1f, 2.5f);
        public Vector2 peekTime = new Vector2(1.5f, 3.5f);
        [Tooltip("Player looking within this angle at it while it peeks: it ducks back (deg).")]
        public float shyLookAngle = 18f;
        public Vector2 reactionTime = new Vector2(0.12f, 0.3f);

        [Header("Threat (how hostile the player is)")]
        [Tooltip("Threat at which it stops watching and responds (fight / hide / flee).")]
        [Range(0.1f, 1f)] public float threatThreshold = 0.6f;
        [Tooltip("Per second while the player aims at it (doubles at full aim-down-sights).")]
        public float aimThreat = 0.35f;
        [Tooltip("Per shot passing close by.")]
        public float shotNearThreat = 0.3f;
        [Tooltip("Per hit taken.")]
        public float hitThreat = 0.5f;
        [Tooltip("Inside this distance the player is in its personal space and the threat climbs.")]
        public float personalSpace = 7f;
        [Tooltip("Per second right at the monster (scales down to 0 at the personal space edge).")]
        public float closeThreat = 0.6f;
        [Tooltip("Per second when the player walks/runs straight at it (scaled by closing speed / 4 m/s).")]
        public float chaseThreat = 0.25f;
        [Tooltip("Player this close forces an immediate response, whatever the meter says.")]
        public float criticalDistance = 3.2f;
        [Tooltip("Calm time (s) before the threat starts to fall.")]
        public float calmDelay = 2f;
        public float threatDecay = 0.1f;
        [Tooltip("Minimum time between two responses (s).")]
        public float responseCooldown = 1.2f;

        [Header("Charge / Attack")]
        [Tooltip("Longest path (m) it will charge along.")]
        public float chargeMaxPath = 14f;
        [Tooltip("Gives up a charge when the player backs off this far and the threat has cooled.")]
        public float chargeGiveUpDistance = 11f;
        [Tooltip("Side-step distance when aimed at during a charge.")]
        public float zigzagWidth = 1.8f;
        public float damage = 28f;
        public float lungeRange = 2.6f;
        public float windupTime = 0.32f;
        public float lungeTime = 0.26f;
        public float lungeDistance = 2.4f;
        public float hitRadius = 1.5f;
        public float recoverTime = 0.5f;
        [Tooltip("Share of the threat left after it strikes (it made its point).")]
        [Range(0f, 1f)] public float threatAfterStrike = 0.35f;

        [Header("Debug")]
        public bool showDebugLabel = true;
        [Tooltip("Log every state change and response to the console.")]
        public bool logStateChanges;

        // ------------------------------------------------------------------ read by MonsterBody / UI

        public MonsterState State { get; private set; } = MonsterState.Wander;
        public float StateTime { get; private set; }
        /// <summary>0 = standing, 1 = fully crouched.</summary>
        public float Crouch { get; private set; }
        /// <summary>-1..1: leaning out to the left/right while peeking.</summary>
        public float Lean { get; private set; }
        /// <summary>0..1.</summary>
        public float JawOpen { get; private set; }
        /// <summary>0..1 during an attack: windup then lunge.</summary>
        public float AttackProgress { get; private set; }
        public Vector3 LookTarget { get; private set; }
        public bool HasLookTarget { get; private set; }
        public float Health => health;
        public float Health01 => health / maxHealth;
        public bool IsDead => State == MonsterState.Dead;
        /// <summary>0..1: how hostile it thinks the player is right now.</summary>
        public float Threat => threat;
        public MonsterResponse LastResponse { get; private set; }
        public Vector3 Velocity => agent ? agent.velocity : Vector3.zero;
        public Vector3 EyePosition => transform.position + Vector3.up * Mathf.Lerp(standEyeHeight, crouchEyeHeight, Crouch);
        public Vector3 ChestPosition => transform.position + Vector3.up * Mathf.Lerp(1.15f, 0.7f, Crouch);

        public event Action<DamageInfo, bool> Hurt;     // info, headshot
        public event Action Died;
        public event Action<AudioClip> Vocalized;
        public event Action<MonsterResponse> Responded;

        // ------------------------------------------------------------------ internals

        static MonsterBrain attacker;                    // attack token
        static readonly List<MonsterBrain> all = new List<MonsterBrain>();

        NavMeshAgent agent;
        MonsterSenses senses;
        MonsterBody body;
        AudioSource voice;
        NavMeshPath scratchPath;

        float health, recentDamage, lastDamageTime = -100f;
        float threat, lastHostileTime = -100f, lastSuppressSeen = -100f, nextResponseTime, nextRelocateTime;
        float stateTimer, crouchTarget, leanTarget, jawTarget, reaction, nextVoiceTime, repathTimer, shyTime;
        int hitsThisCharge, zigzagSign = 1;
        float zigzagTimer;
        bool arrivedAtSpot, attackHit, moveTargetSet, alarmed;
        CoverSpot cover;
        bool hasCover;
        Vector3 peekPoint, searchOrigin, spawnPosition;
        Quaternion spawnRotation;
        int peekSide, searchLeft;
        readonly List<CoverFinder.Burned> burned = new List<CoverFinder.Burned>();
        readonly List<Vector3> others = new List<Vector3>();

        void Awake()
        {
            agent = GetComponent<NavMeshAgent>();
            senses = GetComponent<MonsterSenses>();
            body = GetComponent<MonsterBody>();
            voice = GetComponent<AudioSource>();
            if (!voice)
            {
                voice = gameObject.AddComponent<AudioSource>();
                voice.spatialBlend = 1f;
                voice.minDistance = 2f;
                voice.maxDistance = 35f;
                voice.rolloffMode = AudioRolloffMode.Linear;
            }
            voice.playOnAwake = false;
            scratchPath = new NavMeshPath();
            health = maxHealth;
            agent.updateRotation = false;
            agent.autoBraking = true;
            agent.enabled = false;          // enabled once the runtime NavMesh exists
            spawnPosition = transform.position;
            spawnRotation = transform.rotation;
        }

        void OnEnable() => all.Add(this);

        void OnDisable()
        {
            all.Remove(this);
            ReleaseToken();
        }

        void Start() => PlaceOnNavMesh(transform.position);

        void PlaceOnNavMesh(Vector3 pos)
        {
            if (NavMesh.SamplePosition(pos, out NavMeshHit hit, 3f, NavMesh.AllAreas))
            {
                transform.position = hit.position;
                agent.enabled = true;
                agent.Warp(hit.position);
            }
            else Debug.LogWarning($"[MonsterBrain] {name}: no NavMesh near {pos}. Add a RuntimeNavMesh to the scene.", this);
        }

        // ------------------------------------------------------------------ main loop

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            StateTime += dt;
            stateTimer -= dt;
            recentDamage = Mathf.Max(0f, recentDamage - staggerDamage * 2f * dt);

            if (State == MonsterState.Dead)
            {
                if (respawnDelay > 0f && StateTime > respawnDelay) Respawn();
                return;
            }
            if (!agent.enabled || !agent.isOnNavMesh)
            {
                if (StateTime > 1f) { StateTime = 0f; PlaceOnNavMesh(transform.position); }
                return;
            }

            senses.Tick(dt, EyePosition, ChestPosition);
            HasLookTarget = false;

            UpdateThreat(dt);
            if (Time.time - lastDamageTime > regenDelay && health < maxHealth)
                health = Mathf.Min(maxHealth, health + regenRate * dt * (State == MonsterState.Hide ? 1.5f : 1f));

            if (State != MonsterState.Stagger && State != MonsterState.Attack) React();

            switch (State)
            {
                case MonsterState.Wander: UpdateWander(); break;
                case MonsterState.Investigate: UpdateInvestigate(); break;
                case MonsterState.Search: UpdateSearch(); break;
                case MonsterState.Stalk: UpdateStalk(dt); break;
                case MonsterState.Hide: UpdateHide(); break;
                case MonsterState.Peek: UpdatePeek(dt); break;
                case MonsterState.Charge: UpdateCharge(dt); break;
                case MonsterState.Attack: UpdateAttack(dt); break;
                case MonsterState.Retreat: case MonsterState.Flee: UpdateRetreat(); break;
                case MonsterState.Stagger: if (stateTimer <= 0f) Respond(true); break;
            }

            Crouch = Mathf.MoveTowards(Crouch, crouchTarget, dt * 3.5f);
            Lean = Mathf.MoveTowards(Lean, leanTarget, dt * 4f);
            JawOpen = Mathf.MoveTowards(JawOpen, jawTarget, dt * 8f);
            Face(dt);
        }

        float DistToPlayer => senses.Player ? Vector3.Distance(transform.position, senses.PlayerFeet) : 999f;

        /// <summary>Where we think the player's eyes are.</summary>
        Vector3 ThreatEye => Time.time - senses.LastSeenTime < 3f ? senses.PlayerEye : senses.LastKnownPosition + Vector3.up * 1.6f;

        // ------------------------------------------------------------------ threat meter

        void UpdateThreat(float dt)
        {
            bool hostile = false;
            if (senses.Player && senses.HasTarget)
            {
                // aimed at: worse the more the player is aiming down the sights
                if (senses.UnderAim)
                {
                    threat += aimThreat * (1f + senses.PlayerAimWeight) * dt;
                    hostile = true;
                }
                // shots passing close (hits are counted in TakeDamage)
                if (senses.LastSuppressedTime > lastSuppressSeen)
                {
                    lastSuppressSeen = senses.LastSuppressedTime;
                    if (Time.time - lastDamageTime > 0.05f) threat += shotNearThreat;
                    hostile = true;
                }
                // personal space and being chased
                float dist = DistToPlayer;
                if (dist < personalSpace)
                {
                    threat += (1f - dist / personalSpace) * closeThreat * dt;
                    hostile = true;
                }
                Vector3 away = transform.position - senses.PlayerFeet; away.y = 0f;
                float closing = Vector3.Dot(senses.Player.HorizontalVelocity, away.normalized);
                if (closing > 1.5f && dist < observeDistance.y && (senses.CanSeePlayer || senses.PlayerSeesUs))
                {
                    threat += chaseThreat * (closing / 4f) * dt;
                    hostile = true;
                }
            }
            if (hostile) lastHostileTime = Time.time;
            else if (Time.time - lastHostileTime > calmDelay) threat -= threatDecay * dt;
            threat = Mathf.Clamp01(threat);
        }

        /// <summary>Reactions that cut across states.</summary>
        void React()
        {
            if (!senses.HasTarget) return;
            float dist = DistToPlayer;

            switch (State)
            {
                case MonsterState.Wander: case MonsterState.Investigate: case MonsterState.Search:
                    // just found the player
                    Vocalize(MonsterSounds.Growl, 0.6f);
                    if (dist < criticalDistance) Respond(true);
                    else GoObserve();
                    return;
            }

            // badly hurt: run when the player is still pressing (not once it's safely tucked away)
            bool pressed = Time.time - lastHostileTime < 1f || dist < personalSpace;
            if (Health01 < fleeHealth && pressed && State != MonsterState.Flee && State != MonsterState.Charge &&
                (State != MonsterState.Hide || senses.PlayerSeesUs || dist < personalSpace))
            {
                Log("low health");
                if (TryFlee()) return;
            }

            bool canRespond = Time.time >= nextResponseTime;
            bool critical = dist < criticalDistance;
            // respond to hostility that is still going on, not to a meter that is merely cooling down
            bool hostileNow = threat >= threatThreshold && Time.time - lastHostileTime < 0.5f;
            switch (State)
            {
                case MonsterState.Stalk: case MonsterState.Hide: case MonsterState.Peek:
                    if (canRespond && (hostileNow || critical)) Respond(critical);
                    break;
                case MonsterState.Retreat: case MonsterState.Flee:
                    // caught while running: no way out but through
                    if (canRespond && critical && StateTime > 0.4f) Respond(true);
                    break;
            }
        }

        /// <summary>
        /// The player is hostile or too close: fight, hide or flee. Scores each option from the
        /// situation and tries them best-first; if nothing works it's cornered and fights.
        /// </summary>
        void Respond(bool forced)
        {
            nextResponseTime = Time.time + responseCooldown;
            float dist = DistToPlayer;
            bool aimed = senses.UnderAim;
            bool vulnerable = senses.PlayerVulnerable;
            bool hurtRecently = Time.time - lastDamageTime < 2f;

            float fight = 0f, hide = 0.5f, flee = 0f;
            // fight: when the player is right on top of it, busy, or not pointing the gun at it
            if (dist < criticalDistance) fight += 1.1f;
            else if (dist < personalSpace * 0.8f) fight += 0.45f;
            if (vulnerable) fight += 0.7f;
            if (!aimed) fight += 0.25f;
            fight += (Health01 - 0.5f) * 0.8f;
            if (hurtRecently && dist > personalSpace) fight -= 0.5f;
            // hide: being aimed at or shot at from a distance
            if (aimed) hide += 0.35f;
            hide += dist > personalSpace ? 0.3f : -0.35f;
            // flee: hurt, or the player keeps coming
            flee += (1f - Health01) * 1.2f;
            if (threat > 0.9f) flee += 0.3f;
            if (hurtRecently) flee += 0.25f;
            Vector3 away = transform.position - senses.PlayerFeet; away.y = 0f;
            if (senses.Player && Vector3.Dot(senses.Player.HorizontalVelocity, away.normalized) > 2f) flee += 0.3f;
            // a little unpredictability
            fight += Random.Range(-0.15f, 0.15f);
            hide += Random.Range(-0.15f, 0.15f);
            flee += Random.Range(-0.15f, 0.15f);

            var order = new List<(float score, MonsterResponse r)>
            {
                (fight, MonsterResponse.Fight), (hide, MonsterResponse.Hide), (flee, MonsterResponse.Flee),
            };
            order.Sort((a, b) => b.score.CompareTo(a.score));

            foreach (var (score, r) in order)
            {
                bool ok = r switch
                {
                    MonsterResponse.Fight => TryCharge(dist < criticalDistance + 1f),
                    MonsterResponse.Hide => TryHide(true),
                    _ => TryFlee(),
                };
                if (!ok) continue;
                SetResponse(r, $"fight {fight:0.00} hide {hide:0.00} flee {flee:0.00}{(forced ? " forced" : "")}");
                return;
            }
            // cornered: nowhere to go
            if (TryCharge(true)) { SetResponse(MonsterResponse.Fight, "cornered"); return; }
            GoStalk();
        }

        void SetResponse(MonsterResponse r, string why)
        {
            LastResponse = r;
            alarmed = r != MonsterResponse.Fight;
            Responded?.Invoke(r);
            Log($"RESPOND {r}: {why}");
        }

        // ------------------------------------------------------------------ calm states

        void UpdateWander()
        {
            crouchTarget = 0.15f; leanTarget = 0f; jawTarget = 0f;
            agent.speed = wanderSpeed;
            if (senses.Awareness > 0.35f && Time.time - senses.NoiseTime < 1f) { SetState(MonsterState.Investigate); return; }
            if (Arrived())
            {
                if (!arrivedAtSpot) { arrivedAtSpot = true; stateTimer = Random.Range(1f, 3.5f); }
                LookAround();
                if (stateTimer <= 0f && CoverFinder.RandomPoint(transform.position, 12f, out Vector3 p))
                {
                    agent.SetDestination(p);
                    arrivedAtSpot = false;
                }
            }
        }

        void UpdateInvestigate()
        {
            crouchTarget = 0.6f; leanTarget = 0f; jawTarget = 0.1f;
            agent.speed = stalkSpeed * 0.8f;
            if (StateTime < 0.05f || repathTimer <= 0f)
            {
                agent.SetDestination(senses.NoisePosition);
                repathTimer = 0.5f;
                Vocalize(MonsterSounds.Chitter, 0.15f);
            }
            repathTimer -= Time.deltaTime;
            Look(senses.NoisePosition + Vector3.up);
            if (Arrived() && StateTime > 0.5f) StartSearch(senses.NoisePosition);
            if (senses.Awareness < 0.1f) SetState(MonsterState.Wander);
        }

        void StartSearch(Vector3 origin)
        {
            searchOrigin = origin;
            searchLeft = 3;
            SetState(MonsterState.Search);
            arrivedAtSpot = true;
            stateTimer = 0.8f;
        }

        void UpdateSearch()
        {
            crouchTarget = 0.5f; leanTarget = 0f; jawTarget = 0.05f;
            agent.speed = stalkSpeed * 0.7f;
            if (Time.time - senses.NoiseTime < 0.2f && senses.Awareness > 0.4f) { SetState(MonsterState.Investigate); return; }
            if (!Arrived()) return;
            if (!arrivedAtSpot) { arrivedAtSpot = true; stateTimer = Random.Range(0.8f, 1.8f); }
            LookAround();
            if (stateTimer > 0f) return;
            if (searchLeft-- <= 0) { SetState(MonsterState.Wander); return; }
            // check spots where someone could be hiding from where we stand
            Vector3 eye = EyePosition;
            for (int i = 0; i < 6; i++)
            {
                if (!CoverFinder.RandomPoint(searchOrigin, 7f, out Vector3 p)) continue;
                if (i < 5 && Sight.Clear(eye, p + Vector3.up)) continue;   // prefer spots we can't see
                agent.SetDestination(p);
                arrivedAtSpot = false;
                return;
            }
        }

        void LookAround()
        {
            float a = Mathf.Sin(StateTime * 0.9f) * 70f;
            Look(transform.position + Quaternion.Euler(0f, a, 0f) * transform.forward * 5f + Vector3.up * standEyeHeight);
        }

        // ------------------------------------------------------------------ watching

        /// <summary>Starts (or restarts) keeping the player under observation.</summary>
        void GoObserve()
        {
            alarmed = false;
            nextRelocateTime = Time.time + Random.Range(relocateEvery.x, relocateEvery.y);
            GoStalk();
        }

        void GoStalk()
        {
            SetState(MonsterState.Stalk);
            repathTimer = 0f;
        }

        /// <summary>A watch spot: hidden, at a comfortable distance, at the edge of the player's view.</summary>
        bool FindWatchSpot(float maxRadius)
        {
            Vector3 player = senses.LastKnownPosition;
            float dist = DistToPlayer;
            var req = new CoverRequest
            {
                self = transform.position,
                threatEye = ThreatEye,
                threatForward = senses.PlayerForward,
                // sample around a point at the preferred distance from the player, on our side
                searchCenter = player + (transform.position - player).normalized * preferredObserveDistance,
                minRadius = 1.5f, maxRadius = maxRadius,
                desiredThreatDistance = preferredObserveDistance,
                viewAngle = peripheryAngle, viewAngleWeight = 0.8f,
                exposureWeight = 2f, travelWeight = 0.25f,
                hideHeight = hideHeight, minThreatDistance = observeDistance.x,
                requirePeekHeight = standEyeHeight * 0.9f,
            };
            if (dist > observeDistance.y * 1.5f) req.searchCenter = Vector3.Lerp(transform.position, player, 0.5f);
            return FindCover(req);
        }

        /// <summary>Moving to the next watch spot (following the player, backing off or circling).</summary>
        void UpdateStalk(float dt)
        {
            crouchTarget = 0.85f; leanTarget = 0f; jawTarget = 0.05f;
            agent.speed = senses.PlayerSeesUs ? coverSpeed : stalkSpeed;
            if (!senses.HasTarget) { StartSearch(senses.LastKnownPosition); return; }
            Look(ThreatEye);

            repathTimer -= dt;
            if (moveTargetSet && StateTime > 0.3f && Arrived() && hasCover && (transform.position - cover.position).sqrMagnitude < 1f)
            {
                EnterHide(Random.Range(hideTime.x, hideTime.y));
                return;
            }
            if (repathTimer > 0f && !Arrived()) return;
            repathTimer = 3f;

            if (FindWatchSpot(Mathf.Clamp(DistToPlayer * 0.6f, 6f, 12f)) && (cover.position - transform.position).sqrMagnitude > 1.5f)
            {
                agent.SetDestination(cover.position);
                moveTargetSet = true;
                return;
            }
            hasCover = false;
            moveTargetSet = false;
            // nothing to hide behind around there: keep a distance in the open, unseen if we can
            float dist = DistToPlayer;
            Vector3 player = senses.LastKnownPosition;
            Vector3 fromPlayer = (transform.position - player); fromPlayer.y = 0f;
            Vector3 want = player + fromPlayer.normalized * preferredObserveDistance;
            if (dist > observeDistance.y) want = senses.Predict(0.8f) + fromPlayer.normalized * preferredObserveDistance;
            if (NavMesh.SamplePosition(want, out NavMeshHit h, 3f, NavMesh.AllAreas)) agent.SetDestination(h.position);
            if (Arrived() && StateTime > 1f) EnterHide(Random.Range(hideTime.x, hideTime.y));
        }

        void EnterHide(float time)
        {
            SetState(MonsterState.Hide);
            arrivedAtSpot = true;
            stateTimer = time;
        }

        /// <summary>Hides behind the nearest cover (quick = only close spots). Used to break line of sight.</summary>
        bool TryHide(bool quick)
        {
            var req = new CoverRequest
            {
                self = transform.position,
                threatEye = ThreatEye,
                threatForward = senses.PlayerForward,
                searchCenter = transform.position,
                minRadius = 1.5f, maxRadius = quick ? 7f : 10f,
                desiredThreatDistance = Mathf.Max(DistToPlayer + 2f, preferredObserveDistance),
                viewAngle = 120f, viewAngleWeight = 0.3f,
                exposureWeight = 1.2f, travelWeight = 0.9f,
                hideHeight = hideHeight, minThreatDistance = Mathf.Min(criticalDistance + 1f, DistToPlayer),
            };
            if (!FindCover(req)) return false;
            SetState(MonsterState.Hide);
            agent.speed = coverSpeed;
            agent.SetDestination(cover.position);
            arrivedAtSpot = false;
            stateTimer = Random.Range(hideTime.x, hideTime.y) + 1.5f;
            Vocalize(MonsterSounds.Hurt, 0.25f);
            return true;
        }

        void UpdateHide()
        {
            crouchTarget = 1f; leanTarget = 0f; jawTarget = 0f;
            Look(ThreatEye);
            if (!Arrived())
            {
                agent.speed = coverSpeed;
                return;
            }
            if (!arrivedAtSpot) { arrivedAtSpot = true; stateTimer = Mathf.Max(stateTimer, Random.Range(hideTime.x, hideTime.y)); }
            if (!senses.HasTarget) { StartSearch(senses.LastKnownPosition); return; }

            float dist = DistToPlayer;
            // cover blown: the player has a line on us. Not hostile yet, so just slip away to another spot.
            if (senses.PlayerSeesUs && StateTime > 0.4f && threat < threatThreshold)
            {
                if (hasCover) Burn(cover.position, 8f);
                Log("cover blown");
                GoStalk();
                return;
            }

            if (stateTimer > 0f) return;
            // still shaken: stay down until the threat cools
            if (alarmed && threat > threatThreshold * 0.6f) { stateTimer = 0.5f; return; }
            // licking its wounds: stays down until it has some health back
            if (Health01 < fleeHealth + 0.15f) { stateTimer = 0.5f; return; }
            alarmed = false;

            // keep the comfortable distance: follow when the player walks off, back away when they come closer
            // (lost sight is handled by peeking: the peek tells whether this spot still sees the player)
            if (dist > observeDistance.y || dist < observeDistance.x || Time.time > nextRelocateTime)
            {
                nextRelocateTime = Time.time + Random.Range(relocateEvery.x, relocateEvery.y);
                GoStalk();
                return;
            }
            StartPeek();
        }

        void StartPeek()
        {
            if (CoverFinder.FindPeek(transform.position, ThreatEye, standEyeHeight * 0.9f, out peekPoint, out peekSide))
            {
                SetState(MonsterState.Peek);
                agent.speed = peekSpeed;
                agent.SetDestination(peekPoint);
                stateTimer = Random.Range(peekTime.x, peekTime.y);
                arrivedAtSpot = false;
                shyTime = 0f;
            }
            else GoStalk();
        }

        void UpdatePeek(float dt)
        {
            crouchTarget = peekSide == 0 ? 0.25f : 0.55f;
            jawTarget = 0.05f;
            Look(senses.CanSeePlayer ? senses.PlayerEye : ThreatEye);
            // lean towards the side we look around
            if (peekSide != 0)
            {
                Vector3 toThreat = ThreatEye - transform.position; toThreat.y = 0f;
                Vector3 sideDir = Vector3.Cross(Vector3.up, toThreat.normalized) * peekSide;
                leanTarget = Vector3.Dot(sideDir, transform.right) > 0f ? 1f : -1f;
            }
            else leanTarget = 0f;

            if (!Arrived()) { stateTimer = Mathf.Max(stateTimer, 0.4f); return; }
            if (!arrivedAtSpot)
            {
                arrivedAtSpot = true;
                stateTimer = Random.Range(peekTime.x, peekTime.y);   // the watch starts once it's at the corner
                Vocalize(MonsterSounds.Chitter, 0.3f);
            }

            // stared at: shy, ducks back (not a threat by itself, aiming is)
            bool stared = senses.PlayerSeesUs && senses.PlayerLookAngleTo(ChestPosition) < shyLookAngle;
            shyTime = stared ? shyTime + dt : 0f;
            if (shyTime > reaction)
            {
                leanTarget = 0f;
                Log("stared at, ducking");
                BackToCover(Random.Range(hideTime.x, hideTime.y));
                return;
            }

            if (stateTimer <= 0f)
            {
                leanTarget = 0f;
                // can't see the player from here any more: move to a spot that can
                if (!senses.CanSeePlayer && Time.time - senses.LastSeenTime > lostSightRelocate) GoStalk();
                else BackToCover(Random.Range(hideTime.x, hideTime.y));
            }
        }

        /// <summary>Shuffles back from the peek spot to the cover spot and hides there.</summary>
        void BackToCover(float time)
        {
            SetState(MonsterState.Hide);
            if (hasCover)
            {
                agent.speed = coverSpeed * 0.7f;
                agent.SetDestination(cover.position);
                arrivedAtSpot = false;
            }
            else arrivedAtSpot = true;
            stateTimer = time;
        }

        // ------------------------------------------------------------------ responses

        bool TryFlee()
        {
            var req = new CoverRequest
            {
                self = transform.position,
                threatEye = ThreatEye,
                threatForward = senses.PlayerForward,
                searchCenter = transform.position,
                minRadius = 6f, maxRadius = 22f,
                desiredThreatDistance = 24f,
                viewAngle = 180f, viewAngleWeight = 0.3f,
                exposureWeight = 1.2f, travelWeight = 0.1f,
                hideHeight = hideHeight, minThreatDistance = Mathf.Max(DistToPlayer + 4f, 10f),
            };
            if (!FindCover(req)) return false;
            SetState(MonsterState.Flee);
            agent.speed = fleeSpeed;
            agent.SetDestination(cover.position);
            Vocalize(MonsterSounds.Hurt, 0.6f);
            return true;
        }

        /// <summary>Charge the player. close = point blank, allowed even when another monster is attacking.</summary>
        bool TryCharge(bool close)
        {
            if (!senses.Player) return false;
            if (!agent.CalculatePath(senses.PlayerFeet, scratchPath) || scratchPath.status != NavMeshPathStatus.PathComplete) return false;
            if (PathLength(scratchPath) > chargeMaxPath) return false;
            // one attacker at a time, unless the player is right on top of us
            if (attacker && attacker != this && !attacker.IsDead && attacker.isActiveAndEnabled && !close) return false;
            attacker = this;
            SetState(MonsterState.Charge);
            agent.speed = chargeSpeed;
            agent.SetPath(scratchPath);
            hitsThisCharge = 0;
            zigzagTimer = 0f;
            Vocalize(MonsterSounds.Screech, 0f);
            return true;
        }

        void UpdateCharge(float dt)
        {
            crouchTarget = 0.35f; leanTarget = 0f; jawTarget = 0.7f;
            agent.speed = chargeSpeed;
            Vector3 target = senses.PlayerFeet;
            float dist = DistToPlayer;
            Look(senses.PlayerEye);

            if (!senses.HasTarget || StateTime > 6f) { ReleaseToken(); GoObserve(); return; }
            // the player backed off and calmed down: no need to finish it
            if (dist > chargeGiveUpDistance && threat < threatThreshold * 0.5f) { ReleaseToken(); Log("player backed off"); GoObserve(); return; }
            // getting shot to pieces on the way in
            if (hitsThisCharge >= 2 && Health01 < fleeHealth + 0.15f && dist > 5f && TryFlee()) return;

            if (dist < lungeRange && Sight.Clear(ChestPosition, senses.PlayerEye))
            {
                SetState(MonsterState.Attack);
                agent.ResetPath();
                attackHit = false;
                return;
            }

            repathTimer -= dt;
            if (repathTimer <= 0f)
            {
                repathTimer = 0.12f;
                // weave when it's staring down the barrel
                zigzagTimer -= 0.12f;
                Vector3 dest = target;
                if (senses.UnderAim && dist > 4.5f)
                {
                    if (zigzagTimer <= 0f) { zigzagSign = -zigzagSign; zigzagTimer = Random.Range(0.3f, 0.55f); }
                    Vector3 to = target - transform.position; to.y = 0f;
                    Vector3 side = Vector3.Cross(Vector3.up, to.normalized) * (zigzagSign * zigzagWidth);
                    if (NavMesh.SamplePosition(transform.position + to.normalized * 3f + side, out NavMeshHit h, 1f, NavMesh.AllAreas))
                        dest = h.position;
                }
                if (!agent.SetDestination(dest) || (agent.hasPath && agent.pathStatus == NavMeshPathStatus.PathPartial && dist > lungeRange))
                {
                    // player climbed out of reach: hide and keep watching from below
                    ReleaseToken();
                    if (!TryHide(false)) GoObserve();
                }
            }
        }

        void UpdateAttack(float dt)
        {
            crouchTarget = 0f; leanTarget = 0f;
            agent.speed = 0f;

            float t = StateTime;
            if (t < windupTime)
            {
                AttackProgress = t / windupTime * 0.5f;
                jawTarget = 1f;
                Look(senses.PlayerEye);
            }
            else if (t < windupTime + lungeTime)
            {
                AttackProgress = 0.5f + (t - windupTime) / lungeTime * 0.5f;
                Vector3 dir = transform.forward;
                agent.Move(dir * (lungeDistance / lungeTime) * dt);
                if (!attackHit && senses.Player)
                {
                    Vector3 pChest = senses.PlayerFeet + Vector3.up * 1.1f;
                    Vector3 reach = transform.position + Vector3.up * 1.1f + dir * 0.9f;
                    if (Vector3.Distance(reach, pChest) < hitRadius && Sight.Clear(ChestPosition, pChest))
                    {
                        attackHit = true;
                        var info = new DamageInfo
                        {
                            amount = damage, point = pChest, normal = -dir, direction = dir, source = gameObject,
                        };
                        if (senses.PlayerHealth) senses.PlayerHealth.TakeDamage(info);
                        else Debug.Log("[MonsterBrain] Hit the player (no PlayerHealth on the player).");
                    }
                }
            }
            else
            {
                AttackProgress = 1f;
                jawTarget = 0.2f;
                if (t > windupTime + lungeTime + recoverTime)
                {
                    AttackProgress = 0f;
                    ReleaseToken();
                    if (attackHit) threat *= threatAfterStrike;
                    nextResponseTime = Time.time + responseCooldown;
                    // still in its face and missed: go again, otherwise back off to cover and keep watching
                    if (!attackHit && DistToPlayer < lungeRange && Health01 > 0.5f && TryCharge(true)) return;
                    GoRetreat();
                }
            }
        }

        /// <summary>Hit and run: back to a cover a bit further away, then resume watching.</summary>
        void GoRetreat()
        {
            var req = new CoverRequest
            {
                self = transform.position,
                threatEye = ThreatEye,
                threatForward = senses.PlayerForward,
                searchCenter = transform.position,
                minRadius = 4f, maxRadius = 13f,
                desiredThreatDistance = preferredObserveDistance,
                viewAngle = 120f, viewAngleWeight = 0.3f,
                exposureWeight = 1.5f, travelWeight = 0.15f,
                hideHeight = hideHeight, minThreatDistance = 6f,
            };
            if (FindCover(req))
            {
                SetState(MonsterState.Retreat);
                agent.speed = coverSpeed;
                agent.SetDestination(cover.position);
            }
            else if (!TryHide(true)) GoObserve();
        }

        void UpdateRetreat()
        {
            bool flee = State == MonsterState.Flee;
            crouchTarget = flee ? 0.4f : 0.6f; leanTarget = 0f; jawTarget = 0.2f;
            agent.speed = flee ? fleeSpeed : coverSpeed;
            if (Arrived() && StateTime > 0.3f)
            {
                alarmed = true;
                EnterHide(Random.Range(hideTime.x, hideTime.y) + (flee ? 6f : 1.5f));
            }
            else if (StateTime > 9f) { if (!TryHide(false)) GoObserve(); }
        }

        // ------------------------------------------------------------------ damage

        public void TakeDamage(in DamageInfo info)
        {
            if (IsDead || info.amount <= 0f) return;
            bool headshot = body && body.IsHead(info.collider);
            float amount = info.amount * (headshot ? headshotMultiplier : 1f);
            health -= amount;
            recentDamage += amount;
            lastDamageTime = Time.time;
            lastHostileTime = Time.time;
            threat = Mathf.Clamp01(threat + hitThreat);
            if (State == MonsterState.Charge) hitsThisCharge++;
            senses.OnDamaged();
            Hurt?.Invoke(info, headshot);

            if (health <= 0f) { Die(); return; }
            Vocalize(MonsterSounds.Hurt, 0.2f);
            if (recentDamage > staggerDamage && State != MonsterState.Stagger)
            {
                recentDamage = 0f;
                SetState(MonsterState.Stagger);
                agent.ResetPath();
                stateTimer = 0.45f;
                if (attacker == this) ReleaseToken();
            }
        }

        void Die()
        {
            health = 0f;
            ReleaseToken();
            SetState(MonsterState.Dead);
            if (agent.isOnNavMesh) agent.ResetPath();
            agent.enabled = false;
            jawTarget = 1f;
            Vocalize(MonsterSounds.Death, 0f);
            Died?.Invoke();
        }

        void Respawn()
        {
            health = maxHealth;
            threat = 0f;
            alarmed = false;
            transform.SetPositionAndRotation(spawnPosition, spawnRotation);
            SetState(MonsterState.Wander);
            PlaceOnNavMesh(spawnPosition);
            if (body) body.ResetPose();
        }

        // ------------------------------------------------------------------ helpers

        void SetState(MonsterState s)
        {
            if (State == MonsterState.Charge && s != MonsterState.Attack) ReleaseToken();
            if (logStateChanges)
                Debug.Log($"[Monster {name}] {State} -> {s}  t={Time.time:0.0} dist={DistToPlayer:0.0} hp={health:0} threat={threat:0.00} " +
                          $"seen={senses.CanSeePlayer} seesUs={senses.PlayerSeesUs} aimed={senses.UnderAim}", this);
            State = s;
            StateTime = 0f;
            moveTargetSet = false;
            if (s == MonsterState.Stalk) repathTimer = 0f;
            reaction = Random.Range(reactionTime.x, reactionTime.y);
            if (s != MonsterState.Attack) AttackProgress = 0f;
        }

        void Log(string msg)
        {
            if (logStateChanges) Debug.Log($"[Monster {name}] {msg}  t={Time.time:0.0} dist={DistToPlayer:0.0} threat={threat:0.00}", this);
        }

        void ReleaseToken() { if (attacker == this) attacker = null; }

        bool FindCover(in CoverRequest req)
        {
            others.Clear();
            for (int i = 0; i < all.Count; i++)
                if (all[i] != this && all[i].hasCover && !all[i].IsDead) others.Add(all[i].cover.position);
            burned.RemoveAll(b => b.until < Time.time);
            if (CoverFinder.Find(req, burned, others, out CoverSpot c))
            {
                cover = c;
                hasCover = true;
                return true;
            }
            return false;
        }

        void Burn(Vector3 p, float seconds) => burned.Add(new CoverFinder.Burned { position = p, until = Time.time + seconds });

        bool Arrived() => agent.isOnNavMesh && !agent.pathPending && agent.remainingDistance <= agent.stoppingDistance + 0.2f;

        static float PathLength(NavMeshPath p)
        {
            var c = p.corners;
            float l = 0f;
            for (int i = 1; i < c.Length; i++) l += Vector3.Distance(c[i - 1], c[i]);
            return l;
        }

        void Look(Vector3 worldPoint)
        {
            LookTarget = worldPoint;
            HasLookTarget = true;
        }

        void Face(float dt)
        {
            Vector3 dir;
            Vector3 v = agent.enabled ? agent.velocity : Vector3.zero; v.y = 0f;
            bool moveFast = v.sqrMagnitude > 2.5f * 2.5f && State != MonsterState.Peek;
            if (State == MonsterState.Charge || State == MonsterState.Attack) dir = senses.PlayerFeet - transform.position;
            else if (HasLookTarget && !moveFast) dir = LookTarget - transform.position;
            else if (v.sqrMagnitude > 0.05f) dir = v;
            else return;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f) return;
            float speed = State == MonsterState.Attack && StateTime > windupTime ? 0f : turnSpeed;
            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(dir), speed * dt);
        }

        void Vocalize(AudioClip clip, float chance)
        {
            if (!clip || (chance > 0f && Random.value > chance) || Time.time < nextVoiceTime) return;
            nextVoiceTime = Time.time + clip.length * 0.8f;
            voice.pitch = Random.Range(0.9f, 1.1f);
            voice.PlayOneShot(clip);
            Vocalized?.Invoke(clip);
        }

        // ------------------------------------------------------------------ debug

        void OnDrawGizmosSelected()
        {
            if (!Application.isPlaying) return;
            if (hasCover)
            {
                Gizmos.color = Color.green;
                Gizmos.DrawWireSphere(cover.position + Vector3.up * 0.5f, 0.4f);
                Gizmos.DrawLine(cover.position + Vector3.up * 0.5f, cover.position + Vector3.up * 0.5f + cover.coverNormal);
            }
            if (State == MonsterState.Peek) { Gizmos.color = Color.yellow; Gizmos.DrawWireSphere(peekPoint + Vector3.up * 1.5f, 0.25f); }
            if (senses && senses.HasTarget)
            {
                Gizmos.color = Color.red;
                Gizmos.DrawWireCube(senses.LastKnownPosition + Vector3.up, new Vector3(0.5f, 2f, 0.5f));
                // comfort ring around the player
                Gizmos.color = new Color(1f, 0.6f, 0.1f, 0.5f);
                DrawRing(senses.LastKnownPosition, personalSpace);
                Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.4f);
                DrawRing(senses.LastKnownPosition, preferredObserveDistance);
            }
            Gizmos.color = new Color(1f, 0.3f, 0.3f, 0.6f);
            foreach (var b in burned) Gizmos.DrawWireCube(b.position + Vector3.up * 0.5f, Vector3.one * 0.6f);
        }

        static void DrawRing(Vector3 c, float r)
        {
            Vector3 prev = c + new Vector3(r, 0.1f, 0f);
            for (int i = 1; i <= 32; i++)
            {
                float a = i / 32f * Mathf.PI * 2f;
                Vector3 p = c + new Vector3(Mathf.Cos(a) * r, 0.1f, Mathf.Sin(a) * r);
                Gizmos.DrawLine(prev, p);
                prev = p;
            }
        }

        static Texture2D whiteTex;

        void OnGUI()
        {
            if (!showDebugLabel) return;
            Camera cam = Camera.main;
            if (!cam) return;
            Vector3 sp = cam.WorldToScreenPoint(transform.position + Vector3.up * 2.3f);
            if (sp.z < 0f || sp.z > 40f) return;
            string text = $"{State}  hp {health:0}  aw {senses.Awareness:0.00}" +
                          (senses.UnderAim ? "  AIMED" : "") + (attacker == this ? "  [TOKEN]" : "") +
                          (LastResponse != MonsterResponse.None ? $"  last: {LastResponse}" : "");
            float x = sp.x - 150f, y = Screen.height - sp.y - 10f;
            GUI.color = Color.black; GUI.Label(new Rect(x + 1, y + 1, 300f, 20f), text);
            GUI.color = State == MonsterState.Charge || State == MonsterState.Attack ? Color.red : Color.white;
            GUI.Label(new Rect(x, y, 300f, 20f), text);

            // threat bar with the response threshold marked
            if (!whiteTex) { whiteTex = new Texture2D(1, 1); whiteTex.SetPixel(0, 0, Color.white); whiteTex.Apply(); }
            var bar = new Rect(x, y + 20f, 140f, 7f);
            GUI.color = new Color(0f, 0f, 0f, 0.6f); GUI.DrawTexture(bar, whiteTex);
            GUI.color = Color.Lerp(new Color(1f, 0.85f, 0.2f), new Color(1f, 0.15f, 0.1f), threat / Mathf.Max(threatThreshold, 0.01f));
            GUI.DrawTexture(new Rect(bar.x, bar.y, bar.width * threat, bar.height), whiteTex);
            GUI.color = Color.white;
            GUI.DrawTexture(new Rect(bar.x + bar.width * threatThreshold - 1f, bar.y - 2f, 2f, bar.height + 4f), whiteTex);
            GUI.Label(new Rect(bar.xMax + 6f, bar.y - 7f, 120f, 20f), $"threat {threat:0.00}");
            GUI.color = Color.white;
        }
    }
}
