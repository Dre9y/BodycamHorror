using System;
using System.Collections.Generic;
using BodycamHorror.Combat;
using UnityEngine;
using UnityEngine.AI;
using Random = UnityEngine.Random;

namespace BodycamHorror.AI
{
    public enum MonsterState { Wander, Investigate, Search, Stalk, Hide, Peek, Charge, Attack, Retreat, Stagger, Dead }

    /// <summary>
    /// Ambush hunter. Doesn't run at the gun: it stalks from cover to cover out of the player's view,
    /// peeks around corners, ducks back when aimed at, and charges when the player is busy (reloading,
    /// climbing, stumbling), looking away or walks right up to its hiding spot. Hit-and-run: after a
    /// strike it pulls back to cover. Badly hurt it retreats and licks its wounds before coming back.
    /// Only one monster attacks at a time; the others keep the pressure on and flank.
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent), typeof(MonsterSenses))]
    public class MonsterBrain : MonoBehaviour, IDamageable
    {
        [Header("Health")]
        public float maxHealth = 200f;
        public float headshotMultiplier = 2.5f;
        [Tooltip("This much damage within half a second staggers it.")]
        public float staggerDamage = 60f;
        [Tooltip("Below this share of health it retreats to recover.")]
        [Range(0f, 1f)] public float retreatHealth = 0.35f;
        public float regenDelay = 6f;
        public float regenRate = 5f;
        [Tooltip("Respawn after death (s). 0 = stay dead.")]
        public float respawnDelay = 10f;

        [Header("Speeds (m/s)")]
        public float wanderSpeed = 1.6f;
        public float stalkSpeed = 3.2f;
        public float coverSpeed = 6.5f;
        public float chargeSpeed = 8.8f;
        public float peekSpeed = 1.5f;
        public float turnSpeed = 540f;

        [Header("Body")]
        public float standEyeHeight = 1.6f;
        public float crouchEyeHeight = 0.95f;
        [Tooltip("Height that must be hidden when crouched behind cover.")]
        public float hideHeight = 1.05f;

        [Header("Tactics")]
        [Tooltip("Stalking distance at zero and at full aggression.")]
        public Vector2 stalkDistance = new Vector2(14f, 5f);
        [Tooltip("Aggression gained per second while hunting unhurt.")]
        public float aggressionGain = 0.035f;
        [Tooltip("Charge when the opportunity score passes this.")]
        public float opportunityThreshold = 0.75f;
        [Tooltip("Longest path (m) it will charge along.")]
        public float chargeMaxPath = 17f;
        [Tooltip("Player this close to its hiding spot gets pounced on.")]
        public float ambushDistance = 4f;
        public Vector2 reactionTime = new Vector2(0.12f, 0.3f);
        public Vector2 hideTime = new Vector2(0.8f, 2.4f);
        public Vector2 peekTime = new Vector2(0.7f, 2f);
        public float attackCooldown = 3.5f;
        [Tooltip("Side-step distance when aimed at during a charge.")]
        public float zigzagWidth = 1.8f;

        [Header("Attack")]
        public float damage = 28f;
        public float lungeRange = 2.6f;
        public float windupTime = 0.32f;
        public float lungeTime = 0.26f;
        public float lungeDistance = 2.4f;
        public float hitRadius = 1.5f;
        public float recoverTime = 0.5f;

        [Header("Debug")]
        public bool showDebugLabel = true;
        [Tooltip("Log every state change to the console.")]
        public bool logStateChanges;

        // ------------------------------------------------------------------ read by MonsterBody

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
        public Vector3 Velocity => agent ? agent.velocity : Vector3.zero;
        public Vector3 EyePosition => transform.position + Vector3.up * Mathf.Lerp(standEyeHeight, crouchEyeHeight, Crouch);
        public Vector3 ChestPosition => transform.position + Vector3.up * Mathf.Lerp(1.15f, 0.7f, Crouch);

        public event Action<DamageInfo, bool> Hurt;     // info, headshot
        public event Action Died;
        public event Action<AudioClip> Vocalized;

        // ------------------------------------------------------------------ internals

        static MonsterBrain attacker;                    // attack token
        static readonly List<Vector3> claimedCovers = new List<Vector3>();
        static readonly List<MonsterBrain> all = new List<MonsterBrain>();

        NavMeshAgent agent;
        MonsterSenses senses;
        MonsterBody body;
        AudioSource voice;
        NavMeshPath scratchPath;

        float health, aggression, recentDamage, lastDamageTime = -100f, nextAttackTime;
        float stateTimer, crouchTarget, leanTarget, jawTarget, reaction, nextVoiceTime, repathTimer;
        int hitsThisCharge, zigzagSign = 1;
        float zigzagTimer;
        bool arrivedAtSpot, attackHit, stalkTargetSet;
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

            if (senses.HasTarget && !Hurtish && State != MonsterState.Retreat)
                aggression = Mathf.Clamp01(aggression + aggressionGain * dt);
            if (Time.time - lastDamageTime > regenDelay && health < maxHealth)
                health = Mathf.Min(maxHealth, health + regenRate * dt * (State == MonsterState.Hide ? 2f : 1f));

            if (State != MonsterState.Stagger && State != MonsterState.Attack) React();

            switch (State)
            {
                case MonsterState.Wander: UpdateWander(); break;
                case MonsterState.Investigate: UpdateInvestigate(); break;
                case MonsterState.Search: UpdateSearch(); break;
                case MonsterState.Stalk: UpdateStalk(dt); break;
                case MonsterState.Hide: UpdateHide(); break;
                case MonsterState.Peek: UpdatePeek(); break;
                case MonsterState.Charge: UpdateCharge(dt); break;
                case MonsterState.Attack: UpdateAttack(dt); break;
                case MonsterState.Retreat: UpdateRetreat(); break;
                case MonsterState.Stagger: if (stateTimer <= 0f) AfterStagger(); break;
            }

            Crouch = Mathf.MoveTowards(Crouch, crouchTarget, dt * 3.5f);
            Lean = Mathf.MoveTowards(Lean, leanTarget, dt * 4f);
            JawOpen = Mathf.MoveTowards(JawOpen, jawTarget, dt * 8f);
            Face(dt);
        }

        bool Hurtish => Time.time - lastDamageTime < 2f;
        float DistToPlayer => senses.Player ? Vector3.Distance(transform.position, senses.PlayerFeet) : 999f;

        /// <summary>Where we think the player's eyes are.</summary>
        Vector3 ThreatEye => Time.time - senses.LastSeenTime < 3f ? senses.PlayerEye : senses.LastKnownPosition + Vector3.up * 1.6f;

        /// <summary>Reactions that cut across states: being aimed at, shot at, finding the player.</summary>
        void React()
        {
            if (!senses.HasTarget) return;

            switch (State)
            {
                case MonsterState.Wander: case MonsterState.Investigate: case MonsterState.Search:
                    // just found the player
                    Vocalize(MonsterSounds.Growl, 0.7f);
                    if (DistToPlayer < ambushDistance && TryCharge()) return;
                    GoHide(false);
                    return;
            }

            bool aimed = senses.UnderAimTime > reaction;
            bool suppressed = Time.time - senses.LastSuppressedTime < 0.35f;
            bool danger = aimed || suppressed;

            if (Health01 < retreatHealth && State != MonsterState.Retreat && State != MonsterState.Charge)
            {
                GoRetreat();
                return;
            }

            switch (State)
            {
                case MonsterState.Stalk:
                case MonsterState.Peek:
                    if (danger)
                    {
                        if (State == MonsterState.Peek && hasCover) Burn(cover.position, 6f);
                        if (DistToPlayer < ambushDistance && TryCharge()) return;
                        GoHide(State == MonsterState.Peek);
                    }
                    break;
                case MonsterState.Hide:
                    // the player has a line on our hiding spot: cover is blown
                    if (arrivedAtSpot && senses.PlayerSeesUs && StateTime > 0.3f &&(danger || senses.UnderAimTime > 0f || DistToPlayer < 9f))
                    {
                        if (DistToPlayer < ambushDistance + 2f && TryCharge()) return;
                        if (hasCover) Burn(cover.position, 8f);
                        GoHide(false);
                    }
                    break;
                case MonsterState.Charge:
                    if (Health01 < retreatHealth && hitsThisCharge >= 2 && DistToPlayer > 5f) GoRetreat();
                    break;
            }
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

        // ------------------------------------------------------------------ hunting

        void GoHide(bool quick)
        {
            Vector3 eye = ThreatEye;
            var req = new CoverRequest
            {
                self = transform.position,
                threatEye = eye,
                threatForward = senses.PlayerForward,
                searchCenter = transform.position,
                minRadius = 1.5f, maxRadius = quick ? 7f : 10f,
                desiredThreatDistance = Mathf.Max(DistToPlayer, Mathf.Lerp(stalkDistance.x, stalkDistance.y, aggression)),
                flankWeight = 0.3f, exposureWeight = 1.2f, travelWeight = 0.9f,
                hideHeight = hideHeight, minThreatDistance = 3f,
            };
            if (FindCover(req))
            {
                SetState(MonsterState.Hide);
                agent.speed = coverSpeed;
                agent.SetDestination(cover.position);
                arrivedAtSpot = false;
                stateTimer = Random.Range(hideTime.x, hideTime.y);
                if (quick) Vocalize(MonsterSounds.Hurt, 0.25f);
            }
            else if (senses.PlayerSeesUs && DistToPlayer < 10f && TryCharge()) { }
            else SetState(MonsterState.Stalk);
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

            // pounce on a player who walks up to our corner
            if (DistToPlayer < ambushDistance && TryCharge()) return;
            if (Opportunity() > opportunityThreshold + 0.15f && TryCharge()) return;

            bool recovering = Health01 < 0.6f && Time.time - lastDamageTime < 25f;
            if (stateTimer <= 0f && !recovering)
            {
                if (CoverFinder.FindPeek(transform.position, ThreatEye, standEyeHeight * 0.9f, out peekPoint, out peekSide))
                {
                    SetState(MonsterState.Peek);
                    agent.speed = peekSpeed;
                    agent.SetDestination(peekPoint);
                    stateTimer = Random.Range(peekTime.x, peekTime.y);
                    arrivedAtSpot = false;
                }
                else SetState(MonsterState.Stalk);
            }
        }

        void UpdatePeek()
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
            if (!arrivedAtSpot) { arrivedAtSpot = true; Vocalize(MonsterSounds.Chitter, 0.3f); }

            if (senses.CanSeePlayer && Opportunity() > opportunityThreshold && TryCharge()) return;

            if (stateTimer <= 0f)
            {
                leanTarget = 0f;
                if (!senses.CanSeePlayer || senses.PlayerLookAngleTo(ChestPosition) > 50f) GoStalk();
                else GoHide(false);
            }
        }

        void GoStalk()
        {
            SetState(MonsterState.Stalk);
            repathTimer = 0f;
        }

        void UpdateStalk(float dt)
        {
            crouchTarget = 0.85f; leanTarget = 0f; jawTarget = 0.05f;
            agent.speed = senses.PlayerSeesUs ? coverSpeed : stalkSpeed;
            if (!senses.HasTarget) { StartSearch(senses.LastKnownPosition); return; }
            Look(ThreatEye);

            if (Opportunity() > opportunityThreshold && TryCharge()) return;

            repathTimer -= dt;
            if (repathTimer > 0f && !Arrived()) return;
            if (stalkTargetSet && StateTime > 0.3f && Arrived() && hasCover && (transform.position - cover.position).sqrMagnitude < 1f)
            {
                // reached the next cover: settle in
                SetState(MonsterState.Hide);
                arrivedAtSpot = true;
                stateTimer = Random.Range(hideTime.x, hideTime.y);
                return;
            }
            repathTimer = 3f;

            // next cover: closer, preferably to the player's side or back
            float want = Mathf.Lerp(stalkDistance.x, stalkDistance.y, aggression);
            Vector3 player = senses.LastKnownPosition;
            var req = new CoverRequest
            {
                self = transform.position,
                threatEye = ThreatEye,
                threatForward = senses.PlayerForward,
                searchCenter = Vector3.Lerp(transform.position, player, 0.5f),
                minRadius = 2f, maxRadius = Mathf.Max(6f, Vector3.Distance(transform.position, player) * 0.7f),
                desiredThreatDistance = Mathf.Min(want, DistToPlayer - 1.5f),
                flankWeight = 0.8f, exposureWeight = 2f, travelWeight = 0.35f,
                hideHeight = hideHeight, minThreatDistance = 3f,
            };
            if (FindCover(req) && Vector3.Distance(cover.position, player) < DistToPlayer + 1f &&
                (cover.position - transform.position).sqrMagnitude > 1.5f)
            {
                agent.SetDestination(cover.position);
                stalkTargetSet = true;
                return;
            }
            // nothing to hide behind: creep straight in while unseen, otherwise back off
            hasCover = false;
            if (!senses.PlayerSeesUs) agent.SetDestination(senses.Predict(0.8f));
            else if (DistToPlayer < 9f && TryCharge()) { }
            else GoHide(false);
        }

        float Opportunity()
        {
            if (!senses.HasTarget || Time.time < nextAttackTime) return 0f;
            float o = 0f;
            float look = senses.PlayerLookAngleTo(ChestPosition);
            if (look > 100f) o += 0.6f; else if (look > 55f) o += 0.35f;
            if (senses.PlayerVulnerable) o += 0.65f;
            if (!senses.PlayerFlashlightOn) o += 0.15f;
            o += Mathf.Clamp01(1f - DistToPlayer / 15f) * 0.3f;
            o += aggression * 0.3f;
            o -= senses.PlayerAimWeight * 0.2f;
            if (Health01 < 0.5f) o -= 0.25f;
            if (Hurtish) o -= 0.3f;
            return o;
        }

        bool TryCharge()
        {
            if (!senses.Player || Time.time < nextAttackTime - (DistToPlayer < ambushDistance ? attackCooldown : 0f)) return false;
            if (!agent.CalculatePath(senses.PlayerFeet, scratchPath) || scratchPath.status != NavMeshPathStatus.PathComplete) return false;
            if (PathLength(scratchPath) > chargeMaxPath) return false;
            // one attacker at a time, unless the player is right on top of us
            if (attacker && attacker != this && !attacker.IsDead && attacker.isActiveAndEnabled && DistToPlayer > ambushDistance) return false;
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

            if (!senses.HasTarget || StateTime > 6f) { ReleaseToken(); GoHide(false); return; }

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
                    // player climbed out of reach: wait for them below
                    ReleaseToken();
                    GoHide(false);
                }
            }
        }

        void UpdateAttack(float dt)
        {
            crouchTarget = 0f; leanTarget = 0f;
            agent.speed = 0f;
            Vector3 toPlayer = senses.PlayerFeet - transform.position; toPlayer.y = 0f;

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
                    nextAttackTime = Time.time + attackCooldown * (attackHit ? 1f : 0.5f);
                    aggression = Mathf.Max(0f, aggression - (attackHit ? 0.45f : 0.2f));
                    ReleaseToken();
                    // hit and run; a miss at point blank tries again
                    if (!attackHit && DistToPlayer < lungeRange && Health01 > 0.5f) { nextAttackTime = Time.time + 0.6f; GoHide(true); }
                    else GoRetreat(attackHit ? 1f : 0.6f);
                }
            }
        }

        void GoRetreat(float distanceScale = 1f)
        {
            bool fleeing = Health01 < retreatHealth;
            var req = new CoverRequest
            {
                self = transform.position,
                threatEye = ThreatEye,
                threatForward = senses.PlayerForward,
                searchCenter = transform.position,
                minRadius = 4f * distanceScale, maxRadius = (fleeing ? 20f : 13f) * distanceScale,
                desiredThreatDistance = (fleeing ? 22f : 13f) * distanceScale,
                flankWeight = 0f, exposureWeight = 1.5f, travelWeight = 0.15f,
                hideHeight = hideHeight, minThreatDistance = 6f * distanceScale,
            };
            if (FindCover(req))
            {
                SetState(MonsterState.Retreat);
                agent.speed = coverSpeed;
                agent.SetDestination(cover.position);
            }
            else GoHide(true);
        }

        void UpdateRetreat()
        {
            crouchTarget = 0.6f; leanTarget = 0f; jawTarget = 0.2f;
            agent.speed = coverSpeed;
            if (Arrived() && StateTime > 0.3f)
            {
                SetState(MonsterState.Hide);
                arrivedAtSpot = true;
                stateTimer = Random.Range(hideTime.x, hideTime.y) + (Health01 < retreatHealth ? 6f : 1.5f);
            }
            else if (StateTime > 8f) GoHide(false);
        }

        void AfterStagger()
        {
            if (DistToPlayer < 3.5f && TryCharge()) return;
            if (Health01 < retreatHealth) GoRetreat();
            else GoHide(true);
        }

        // ------------------------------------------------------------------ damage

        public void TakeDamage(in DamageInfo info)
        {
            if (IsDead) return;
            bool headshot = body && body.IsHead(info.collider);
            float amount = info.amount * (headshot ? headshotMultiplier : 1f);
            health -= amount;
            recentDamage += amount;
            lastDamageTime = Time.time;
            aggression = Mathf.Max(0f, aggression - 0.15f);
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
            aggression = 0f;
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
                Debug.Log($"[Monster {name}] {State} -> {s}  t={Time.time:0.0} dist={DistToPlayer:0.0} hp={health:0} aggr={aggression:0.00} " +
                          $"seen={senses.CanSeePlayer} seesUs={senses.PlayerSeesUs} aimed={senses.UnderAim}", this);
            State = s;
            StateTime = 0f;
            stalkTargetSet = false;
            if (s == MonsterState.Stalk) repathTimer = 0f;
            reaction = Random.Range(reactionTime.x, reactionTime.y);
            if (s != MonsterState.Attack) AttackProgress = 0f;
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
            if (senses && senses.HasTarget) { Gizmos.color = Color.red; Gizmos.DrawWireCube(senses.LastKnownPosition + Vector3.up, new Vector3(0.5f, 2f, 0.5f)); }
            Gizmos.color = new Color(1f, 0.3f, 0.3f, 0.6f);
            foreach (var b in burned) Gizmos.DrawWireCube(b.position + Vector3.up * 0.5f, Vector3.one * 0.6f);
        }

        void OnGUI()
        {
            if (!showDebugLabel) return;
            Camera cam = Camera.main;
            if (!cam) return;
            Vector3 sp = cam.WorldToScreenPoint(transform.position + Vector3.up * 2.3f);
            if (sp.z < 0f || sp.z > 40f) return;
            string text = $"{State}  hp {health:0}  aw {senses.Awareness:0.00}  ag {aggression:0.00}" +
                          (senses.UnderAim ? "  AIMED" : "") + (attacker == this ? "  [TOKEN]" : "");
            var r = new Rect(sp.x - 150f, Screen.height - sp.y - 10f, 300f, 20f);
            GUI.color = Color.black; GUI.Label(new Rect(r.x + 1, r.y + 1, r.width, r.height), text);
            GUI.color = State == MonsterState.Charge || State == MonsterState.Attack ? Color.red : Color.white;
            GUI.Label(r, text);
        }
    }
}
