using System.Collections.Generic;
using BodycamHorror.Combat;
using BodycamHorror.Player;
using BodycamHorror.Weapons;
using UnityEngine;

namespace BodycamHorror.Enemies
{
    /// <summary>
    /// Basic infected: not clever, dangerous in numbers. Physics body (Rigidbody capsule), so a pack
    /// really jostles: sprinters shove whoever is in front, and the shoved one can stumble or go down.
    /// Runs straight at the player, slides around tall walls, climbs obstacles slowly, trips over low
    /// ones at a run (some fall flat), swings at you up close. Headshots hurt more; heavy hits stagger
    /// or knock down. Damage to the player goes through <see cref="IDamageable"/> on the player.
    /// </summary>
    [RequireComponent(typeof(Rigidbody), typeof(CapsuleCollider))]
    public class Infected : MonoBehaviour, IDamageable, IPushable
    {
        public enum State { Idle, Chase, Attack, Climb, Stumble, Fallen, GettingUp, Dead, Scream }

        [Header("Health")]
        public float maxHealth = 100f;
        public float headshotMultiplier = 2.5f;
        [Tooltip("A single hit at least this strong makes it stagger back.")]
        public float staggerDamage = 30f;
        [Tooltip("A single hit at least this strong knocks it down.")]
        public float knockdownDamage = 60f;
        public float corpseLifetime = 40f;

        [Header("Senses")]
        public float aggroRange = 28f;
        [Tooltip("Always notices you this close, line of sight or not.")]
        public float closeAggroRange = 6f;
        public float gunshotAlertRange = 45f;
        [Tooltip("When one spots you, others this close join in.")]
        public float alertSpreadRange = 14f;

        [Header("Movement")]
        public float wanderSpeed = 0.8f;
        public Vector2 runSpeedRange = new Vector2(5.2f, 7f);
        public float acceleration = 10f;
        public float turnSpeed = 420f;
        [Tooltip("Obstacles up to this tall are just stepped over.")]
        public float stepHeight = 0.35f;

        [Header("Climbing")]
        public float maxClimbHeight = 2.3f;
        [Tooltip("Vertical climbing speed (m/s): slow on purpose.")]
        public float climbSpeed = 0.8f;
        public float climbOverTime = 0.5f;

        [Header("Tripping")]
        [Tooltip("At a run, obstacles up to this tall trip them up instead of being climbed.")]
        public float tripMaxHeight = 0.95f;
        public float tripMinSpeed = 2.5f;
        [Tooltip("Share of trips that end flat on the ground (the rest stumble over and keep going).")]
        [Range(0f, 1f)] public float tripFallChance = 0.5f;
        public float stumbleTime = 0.7f;
        public Vector2 fallenTime = new Vector2(1.5f, 3f);
        public float getUpTime = 0.8f;

        [Header("Crowd")]
        public float shoveRange = 0.95f;
        public float shoveMinSpeed = 3.8f;
        public float shoveImpulse = 2.6f;
        public float shoveCooldown = 0.7f;
        [Range(0f, 1f)] public float shovedStumbleChance = 0.3f;
        [Range(0f, 1f)] public float shovedFallChance = 0.12f;
        public float separationRadius = 0.85f;
        public float separationWeight = 0.6f;
        [Tooltip("Bunched up this close, they elbow each other now and then even without running.")]
        public float crowdRange = 0.8f;
        public Vector2 crowdShoveInterval = new Vector2(0.5f, 1.3f);
        public float crowdShoveImpulse = 1.5f;
        [Range(0f, 1f)] public float crowdStumbleChance = 0.15f;
        [Range(0f, 1f)] public float crowdFallChance = 0.04f;

        [Header("Pushed by the player")]
        [Tooltip("How long a melee shove throws them off balance (and breaks their swing).")]
        public float pushedStumbleTime = 0.45f;

        [Header("Attack")]
        public float attackRange = 1.45f;
        [Tooltip("Time to the blow landing (matches the attack clip played x1.8).")]
        public float attackWindup = 0.6f;
        public float attackRecover = 0.8f;
        [Tooltip("Animated models: chance to stop and scream when they first spot you (if not already close).")]
        [Range(0f, 1f)] public float screamChance = 0.35f;
        public float screamTime = 1.6f;
        public float attackDamage = 15f;
        public float attackCooldown = 0.9f;

        [Header("Physics")]
        public LayerMask environmentMask = ~(1 << 2);
        public float mass = 65f;

        [Header("Voice")]
        [Tooltip("Growls, shrieks and grunts (placeholder synth). Off for now.")]
        public bool voiceEnabled = false;

        public static readonly List<Infected> All = new List<Infected>();
        /// <summary>Raised when an infected dies.</summary>
        public static event System.Action<Infected> Died;

        public State CurrentState => state;
        public bool IsDead => state == State.Dead;
        public bool IsAlerted => alerted;
        /// <summary>Chasing / attacking / stumbling after the player (used for the arms-out pose).</summary>
        public bool IsHunting => alerted && (state == State.Chase || state == State.Stumble);
        public float Health => health;
        public float HorizontalSpeed => rb && !rb.isKinematic ? Flat(rb.linearVelocity).magnitude : 0f;
        public float ActionProgress { get; private set; }

        Rigidbody rb;
        CapsuleCollider capsule;
        InfectedBody body;
        AudioSource voice;
        PhysicsMaterial slippery, grippy;

        Transform player;
        BodycamPlayerController playerCtrl;
        IDamageable playerDamageable;
        BodycamCameraRig playerRig;
        PlayerStress playerStress;
        BodycamFlashlight playerLight;
        PlayerShooting playerShooting;

        State state = State.Idle;
        float health, stateTimer, runSpeed, senseTimer, attackCooldownTimer, shoveTimer, voiceTimer, crowdTimer;
        bool alerted, attackHitDone;
        Vector3 moveDir = Vector3.forward;
        float avoidTimer, avoidSide;
        Vector3 wanderTarget;
        float wanderTimer;

        // action data
        float actionDuration;
        Vector3 climbFrom, climbWall, climbDir;
        float climbTopY, climbUpTime;
        Vector3 getUpFromPos, getUpToPos;
        Quaternion getUpFromRot, getUpToRot;

        static readonly RaycastHit[] Hits = new RaycastHit[12];
        static readonly Dictionary<string, AudioClip> ClipCache = new Dictionary<string, AudioClip>();
        int voiceVariant;

        void Awake()
        {
            rb = GetComponent<Rigidbody>();
            capsule = GetComponent<CapsuleCollider>();
            body = GetComponent<InfectedBody>();
            if (!body) body = gameObject.AddComponent<InfectedBody>();
            if (!GetComponent<FleshSurface>()) gameObject.AddComponent<FleshSurface>();

            rb.mass = mass;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
            rb.linearDamping = 0f;
            rb.angularDamping = 2f;

            capsule.center = new Vector3(0f, 0.72f, 0f);
            capsule.height = 1.44f;
            capsule.radius = 0.27f;
            slippery = new PhysicsMaterial("InfectedSlippery")
            { dynamicFriction = 0f, staticFriction = 0f, bounciness = 0f, frictionCombine = PhysicsMaterialCombine.Minimum };
            grippy = new PhysicsMaterial("InfectedGrippy")
            { dynamicFriction = 0.8f, staticFriction = 0.9f, bounciness = 0f, frictionCombine = PhysicsMaterialCombine.Maximum };
            capsule.sharedMaterial = slippery;

            voice = gameObject.AddComponent<AudioSource>();
            voice.playOnAwake = false;
            voice.spatialBlend = 1f;
            voice.rolloffMode = AudioRolloffMode.Linear;
            voice.minDistance = 2f;
            voice.maxDistance = 35f;
            voiceVariant = Random.Range(0, 4);

            health = maxHealth;
            runSpeed = Random.Range(runSpeedRange.x, runSpeedRange.y);
            senseTimer = Random.value * 0.25f;
            voiceTimer = Random.Range(1f, 5f);
            wanderTarget = transform.position;
        }

        void OnEnable()
        {
            All.Add(this);
            FindPlayer();
            if (playerShooting) playerShooting.Fired += OnGunshot;
        }

        void OnDisable()
        {
            All.Remove(this);
            if (playerShooting) playerShooting.Fired -= OnGunshot;
        }

        void FindPlayer()
        {
            if (player) return;
            playerCtrl = FindAnyObjectByType<BodycamPlayerController>();
            if (!playerCtrl) return;
            player = playerCtrl.transform;
            playerDamageable = playerCtrl.GetComponent<IDamageable>();
            playerRig = playerCtrl.GetComponentInChildren<BodycamCameraRig>();
            playerStress = playerCtrl.GetComponent<PlayerStress>();
            playerLight = playerCtrl.GetComponentInChildren<BodycamFlashlight>();
            playerShooting = playerCtrl.GetComponentInChildren<PlayerShooting>();
        }

        // ------------------------------------------------------------------ senses & timers

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f || state == State.Dead) return;
            stateTimer += dt;
            attackCooldownTimer -= dt;
            shoveTimer -= dt;

            senseTimer -= dt;
            if (senseTimer <= 0f)
            {
                senseTimer = 0.25f;
                Sense();
            }

            crowdTimer -= dt;
            if (crowdTimer <= 0f)
            {
                crowdTimer = Random.Range(crowdShoveInterval.x, crowdShoveInterval.y);
                if (alerted && (state == State.Chase || state == State.Attack)) CrowdJostle();
            }

            switch (state)
            {
                case State.Attack: UpdateAttack(); break;
                case State.Stumble:
                    ActionProgress = Mathf.Clamp01(stateTimer / actionDuration);
                    if (stateTimer >= actionDuration) SetState(alerted ? State.Chase : State.Idle);
                    break;
                case State.Fallen:
                {
                    float settle = Ragdolled ? body.RagdollSpeed : rb.linearVelocity.magnitude;
                    if (stateTimer >= actionDuration && settle < 0.7f) StartGetUp();
                    else if (stateTimer >= actionDuration + 3f) StartGetUp();   // never stay down forever
                    break;
                }
                case State.Scream:
                    if (stateTimer >= screamTime) SetState(State.Chase);
                    break;
            }

            // voice
            voiceTimer -= dt;
            if (voiceTimer <= 0f)
            {
                voiceTimer = alerted ? Random.Range(1.8f, 4.5f) : Random.Range(5f, 11f);
                if (state != State.Fallen) Say("growl", alerted ? 0.9f : 0.5f, Random.Range(0.85f, 1.12f));
            }
        }

        void Sense()
        {
            if (!player) { FindPlayer(); if (!player) return; }
            if (alerted) return;
            float dist = Vector3.Distance(rb.position, player.position);
            if (dist < closeAggroRange || (dist < aggroRange && CanSeePlayer())) Alert(true, true);
        }

        bool CanSeePlayer()
        {
            Vector3 eye = rb.position + Vector3.up * 1.6f;
            Vector3 target = player.position + Vector3.up * 1.5f;
            Vector3 d = target - eye;
            return !ProbeWorld(eye, d.normalized, d.magnitude, out _);
        }

        /// <summary>Starts hunting the player; <paramref name="spread"/> wakes up others nearby.</summary>
        public void Alert(bool shriek, bool spread)
        {
            if (state == State.Dead) return;
            bool wasAlerted = alerted;
            alerted = true;
            if (state == State.Idle)
            {
                bool far = !player || Vector3.Distance(rb.position, player.position) > 7f;
                if (!wasAlerted && body && body.IsAnimated && far && Random.value < screamChance)
                {
                    SetState(State.Scream);   // stop and scream at the prey first
                    body.TriggerScream();
                }
                else SetState(State.Chase);
            }
            if (!wasAlerted && shriek && Random.value < 0.75f) Say("shriek", 1f, Random.Range(0.9f, 1.15f));
            if (!spread || wasAlerted) return;
            foreach (var other in All)
                if (other != this && !other.alerted && Vector3.Distance(other.rb.position, rb.position) < alertSpreadRange)
                    other.Alert(Random.value < 0.5f, false);
        }

        void OnGunshot()
        {
            if (!alerted && player && Vector3.Distance(rb.position, player.position) < gunshotAlertRange) Alert(true, false);
        }

        // ------------------------------------------------------------------ movement

        void FixedUpdate()
        {
            if (state == State.Dead) return;
            float dt = Time.fixedDeltaTime;
            switch (state)
            {
                case State.Idle: Wander(dt); break;
                case State.Chase: Chase(dt); break;
                case State.Attack: AttackMove(dt); break;
                case State.Stumble: StumbleMove(dt); break;
                case State.Climb: ClimbMove(dt); break;
                case State.GettingUp: GetUpMove(); break;
                case State.Scream:
                    if (player) Move(Flat(player.position - rb.position).normalized, 0f, dt);   // stand and face the prey
                    break;
            }
            if (!rb.isKinematic && (rb.constraints & RigidbodyConstraints.FreezeRotationX) != 0)
                rb.angularVelocity = Vector3.zero;   // only our own turning steers the yaw
        }

        void Wander(float dt)
        {
            wanderTimer -= dt;
            if (wanderTimer <= 0f || Flat(wanderTarget - rb.position).magnitude < 0.5f)
            {
                wanderTimer = Random.Range(3f, 7f);
                Vector2 r = Random.insideUnitCircle * 4f;
                wanderTarget = rb.position + new Vector3(r.x, 0f, r.y);
            }
            Vector3 to = Flat(wanderTarget - rb.position);
            bool stand = wanderTimer > 4.5f;   // spend part of the time just swaying on the spot
            Vector3 dir = to.sqrMagnitude > 0.01f ? to.normalized : transform.forward;
            dir = Avoid(dir, dt);
            Move(dir, stand ? 0f : wanderSpeed, dt);
            if (!stand) CheckObstacles(dir);
        }

        void Chase(float dt)
        {
            if (!player) { SetState(State.Idle); return; }
            Vector3 toP = Flat(player.position - rb.position);
            float dist = toP.magnitude;
            if (dist < attackRange && Mathf.Abs(player.position.y - rb.position.y) < 1.3f && attackCooldownTimer <= 0f)
            {
                StartAttack();
                return;
            }
            Vector3 dir = dist > 0.01f ? toP / dist : transform.forward;
            dir = Avoid(dir, dt);
            dir = (dir + Separation() * separationWeight).normalized;
            // close in carefully: no point running past the target
            float speed = runSpeed * Mathf.Clamp01(dist / 2.5f + 0.35f);
            Move(dir, speed, dt);
            CheckObstacles(dir);
            TryShove();
        }

        void Move(Vector3 dir, float speed, float dt)
        {
            moveDir = dir;
            Vector3 v = rb.linearVelocity;
            Vector3 h = Flat(v);
            bool grounded = IsGrounded();
            Vector3 change = Vector3.ClampMagnitude(dir * speed - h, acceleration * dt * (grounded ? 1f : 0.15f));
            rb.AddForce(change, ForceMode.VelocityChange);
            Face(dir, dt);
        }

        void Face(Vector3 dir, float dt)
        {
            if (dir.sqrMagnitude < 0.001f) return;
            Quaternion target = Quaternion.LookRotation(Flat(dir).normalized, Vector3.up);
            rb.MoveRotation(Quaternion.RotateTowards(rb.rotation, target, turnSpeed * dt));
        }

        bool IsGrounded() =>
            Physics.SphereCast(rb.position + Vector3.up * 0.35f, 0.2f, Vector3.down, out _, 0.25f, environmentMask, QueryTriggerInteraction.Ignore);

        /// <summary>Slides round walls too tall to climb (and low beams) by committing to one side for a moment.</summary>
        Vector3 Avoid(Vector3 dir, float dt)
        {
            if (avoidTimer > 0f)
            {
                avoidTimer -= dt;
                return Quaternion.Euler(0f, avoidSide * 70f, 0f) * dir;
            }
            if (!WallAhead(dir)) return dir;
            Vector3 left = Quaternion.Euler(0f, -60f, 0f) * dir, right = Quaternion.Euler(0f, 60f, 0f) * dir;
            float dl = FreeDistance(left), dr = FreeDistance(right);
            avoidSide = Mathf.Approximately(dl, dr) ? (Random.value < 0.5f ? -1f : 1f) : (dr > dl ? 1f : -1f);
            avoidTimer = 0.8f;
            return Quaternion.Euler(0f, avoidSide * 70f, 0f) * dir;
        }

        bool WallAhead(Vector3 dir)
        {
            Vector3 feet = rb.position;
            float reach = capsule.radius + 0.9f;
            bool knee = ProbeWorld(feet + Vector3.up * 0.22f, dir, reach, out RaycastHit k);
            bool head = ProbeWorld(feet + Vector3.up * 1.6f, dir, reach, out _);
            if (head && !knee) return true;                                     // beam / overhang
            if (!knee) return false;
            return ObstacleTop(k.point, dir, out _) > maxClimbHeight;          // too tall to climb
        }

        float FreeDistance(Vector3 dir) =>
            ProbeWorld(rb.position + Vector3.up * 0.9f, dir, 4f, out RaycastHit h) ? h.distance : 4f;

        Vector3 Separation()
        {
            Vector3 push = Vector3.zero;
            foreach (var other in All)
            {
                if (other == this || other.IsDead) continue;
                Vector3 d = Flat(rb.position - other.rb.position);
                float dd = d.magnitude;
                if (dd < 0.001f || dd > separationRadius) continue;
                push += d / dd * (separationRadius - dd) / separationRadius;
            }
            return push;
        }

        /// <summary>Steps over, trips on or climbs what is right in front.</summary>
        void CheckObstacles(Vector3 dir)
        {
            float speed = HorizontalSpeed;
            float reach = capsule.radius + 0.25f + speed * 0.06f;
            if (!ProbeWorld(rb.position + Vector3.up * 0.2f, dir, reach, out RaycastHit knee) || knee.normal.y > 0.6f) return;
            float h = ObstacleTop(knee.point, dir, out Vector3 top);
            if (h > maxClimbHeight) return;   // wall: Avoid() takes care of it
            if (h <= stepHeight)
            {
                if (IsGrounded())
                {
                    Vector3 v = rb.linearVelocity;
                    v.y = Mathf.Max(v.y, Mathf.Sqrt(2f * 9.81f * (h + 0.1f)));
                    rb.linearVelocity = v;
                }
                return;
            }
            float gap = knee.distance - capsule.radius;
            if (h <= tripMaxHeight && speed >= tripMinSpeed && gap < 0.3f) { Trip(h, dir); return; }
            if (gap < 0.3f) StartClimb(knee.point, top, dir);
        }

        /// <summary>Height of the obstacle top above the feet (999 if none found / too tall), probing just past the hit.</summary>
        float ObstacleTop(Vector3 hitPoint, Vector3 dir, out Vector3 top)
        {
            top = hitPoint;
            float feetY = rb.position.y;
            Vector3 origin = new Vector3(hitPoint.x, feetY + maxClimbHeight + 0.5f, hitPoint.z) + dir * 0.12f;
            if (Physics.CheckSphere(origin, 0.05f, environmentMask, QueryTriggerInteraction.Ignore)) return 999f;
            if (!ProbeWorld(origin, Vector3.down, maxClimbHeight + 0.5f, out RaycastHit down)) return 999f;
            top = down.point;
            return down.point.y - feetY;
        }

        /// <summary>Raycast against the world, ignoring this body and living infected (corpses count).</summary>
        bool ProbeWorld(Vector3 origin, Vector3 dir, float dist, out RaycastHit best)
        {
            best = default;
            int n = Physics.RaycastNonAlloc(origin, dir, Hits, dist, environmentMask, QueryTriggerInteraction.Ignore);
            float bestD = float.MaxValue;
            bool found = false;
            for (int i = 0; i < n; i++)
            {
                var col = Hits[i].collider;
                if (col.attachedRigidbody == rb) continue;
                var inf = col.GetComponentInParent<Infected>();
                if (inf && !inf.IsDead) continue;
                if (Hits[i].distance < bestD) { bestD = Hits[i].distance; best = Hits[i]; found = true; }
            }
            return found;
        }

        // ------------------------------------------------------------------ crowd

        void TryShove()
        {
            if (shoveTimer > 0f || HorizontalSpeed < shoveMinSpeed) return;
            foreach (var other in All)
            {
                if (other == this || other.IsDead || other.state == State.Fallen || other.state == State.Climb) continue;
                Vector3 d = Flat(other.rb.position - rb.position);
                float dd = d.magnitude;
                if (dd < 0.01f || dd > shoveRange || Vector3.Dot(d / dd, moveDir) < 0.5f) continue;
                Vector3 side = Vector3.Cross(Vector3.up, moveDir) * (Vector3.Dot(d, Vector3.Cross(Vector3.up, moveDir)) >= 0f ? 1f : -1f);
                other.GetShoved((moveDir * 0.6f + side * 0.8f).normalized * shoveImpulse);
                Vector3 v = rb.linearVelocity;
                rb.linearVelocity = new Vector3(v.x * 0.85f, v.y, v.z * 0.85f);   // shoving costs a little momentum
                shoveTimer = shoveCooldown;
                break;
            }
        }

        /// <summary>Bunched up: elbow the nearest one out of the way, a little towards the side.</summary>
        void CrowdJostle()
        {
            Infected nearest = null;
            float best = crowdRange;
            foreach (var other in All)
            {
                if (other == this || other.IsDead || other.state == State.Fallen || other.state == State.Climb
                    || other.state == State.GettingUp) continue;
                float d = Flat(other.rb.position - rb.position).magnitude;
                if (d < best) { best = d; nearest = other; }
            }
            if (!nearest) return;
            Vector3 away = Flat(nearest.rb.position - rb.position);
            away = away.sqrMagnitude > 0.0001f ? away.normalized : Flat(Random.insideUnitSphere).normalized;
            Vector3 side = Vector3.Cross(Vector3.up, away) * (Random.value < 0.5f ? -1f : 1f);
            nearest.GetShoved((away + side * 0.5f).normalized * crowdShoveImpulse, crowdStumbleChance, crowdFallChance);
            if (!rb.isKinematic) rb.AddForce(-away * (crowdShoveImpulse * 0.35f), ForceMode.VelocityChange);   // push-back on the shover
        }

        /// <summary>Barged by another infected.</summary>
        public void GetShoved(Vector3 impulse) => GetShoved(impulse, shovedStumbleChance, shovedFallChance);

        void GetShoved(Vector3 impulse, float stumbleChance, float fallChance)
        {
            if (state == State.Dead || state == State.Climb || state == State.GettingUp || rb.isKinematic) return;
            rb.AddForce(impulse, ForceMode.VelocityChange);
            float r = Random.value;
            Vector3 dir = Flat(impulse).normalized;
            if (r < fallChance) Fall(dir, 0.8f);
            else if (r < fallChance + stumbleChance && state != State.Fallen) Stumble(stumbleTime * 0.7f, 0f, dir);
        }

        /// <summary>Shoved by the player's melee: knocked back a step, off balance, swing broken.</summary>
        public void Push(Vector3 direction, float strength)
        {
            Vector3 dir = Flat(direction).sqrMagnitude > 0.0001f ? Flat(direction).normalized : -transform.forward;
            if (Ragdolled) { body.RagdollImpulse(dir * strength, rb.position + Vector3.up * 0.5f); return; }   // kick a downed body about
            if (state == State.Dead || (rb.isKinematic && state != State.Climb)) return;
            if (state == State.Climb) { rb.isKinematic = false; Fall(dir, 0.6f); return; }   // pushed off the ledge
            Alert(false, true);
            rb.AddForce(dir * strength + Vector3.up * 0.4f, ForceMode.VelocityChange);
            if (state == State.Fallen || state == State.GettingUp) return;
            Stumble(pushedStumbleTime, 0f, dir);
            attackCooldownTimer = Mathf.Max(attackCooldownTimer, 0.5f);
        }

        // ------------------------------------------------------------------ trip / stumble / fall / get up

        void Trip(float obstacleHeight, Vector3 dir)
        {
            if (Random.value < tripFallChance) Fall(dir, 1.1f);
            else Stumble(stumbleTime, obstacleHeight, dir);
        }

        void Stumble(float duration, float hopHeight, Vector3 dir)
        {
            SetState(State.Stumble);
            actionDuration = duration;
            moveDir = dir;
            Vector3 v = rb.linearVelocity;
            v.x *= 0.7f; v.z *= 0.7f;
            if (hopHeight > 0f) v.y = Mathf.Sqrt(2f * 9.81f * (hopHeight + 0.15f));   // feet dragged up and over it
            rb.linearVelocity = v;
        }

        void StumbleMove(float dt)
        {
            // keep lurching onwards at a fraction of the pace
            Vector3 dir = moveDir;
            if (alerted && player) dir = Vector3.Slerp(moveDir, Flat(player.position - rb.position).normalized, 0.3f);
            Move(dir, runSpeed * 0.45f, dt);
        }

        bool Ragdolled => body && body.RagdollActive;
        bool getUpFromRagdoll;

        void Fall(Vector3 dir, float strength)
        {
            if (state == State.Dead) return;
            dir = Flat(dir).sqrMagnitude > 0.001f ? Flat(dir).normalized : transform.forward;
            if (Ragdolled)
            {
                // already down: knock the body about some more and keep it down a little longer
                body.RagdollImpulse(dir * (1.5f * strength), rb.position + Vector3.up * 0.4f);
                actionDuration = Mathf.Max(actionDuration, stateTimer + 1f);
                return;
            }
            SetState(State.Fallen);
            actionDuration = Random.Range(fallenTime.x, fallenTime.y);
            if (body && body.HasRagdoll)
            {
                GoRagdoll(dir * (1.5f * strength) + Vector3.up * 0.6f, rb.position + Vector3.up * 1.1f,
                          Vector3.Cross(Vector3.up, dir), 4f * strength);
                return;
            }
            rb.isKinematic = false;
            rb.constraints = RigidbodyConstraints.None;
            capsule.sharedMaterial = grippy;
            rb.AddForce(dir * (1.5f * strength) + Vector3.up * 0.8f, ForceMode.VelocityChange);
            rb.AddTorque(Vector3.Cross(Vector3.up, dir) * (7f * strength), ForceMode.VelocityChange);   // topple along dir
        }

        /// <summary>Hands the body to the ragdoll; the root waits, kinematic and without its capsule.</summary>
        void GoRagdoll(Vector3 impulse, Vector3 point, Vector3 toppleAxis, float topple)
        {
            Vector3 v = rb.isKinematic ? Vector3.zero : rb.linearVelocity;
            rb.isKinematic = true;
            capsule.enabled = false;
            body.BeginRagdoll(v, impulse, point, toppleAxis, topple);
        }

        void StartGetUp()
        {
            if (Ragdolled)
            {
                SetState(State.GettingUp);
                getUpFromRagdoll = true;
                body.BeginGetUp(getUpTime, environmentMask, out Vector3 ground, out Quaternion facing);
                transform.SetPositionAndRotation(ground, facing);
                rb.position = ground;
                rb.rotation = facing;
                Say("grunt", 0.7f, Random.Range(0.9f, 1.1f));
                return;
            }
            getUpFromRagdoll = false;
            SetState(State.GettingUp);
            rb.isKinematic = true;
            getUpFromPos = rb.position;
            getUpFromRot = rb.rotation;
            float groundY = getUpFromPos.y;
            if (ProbeWorld(getUpFromPos + Vector3.up * 1.2f, Vector3.down, 3f, out RaycastHit g)) groundY = g.point.y;
            getUpToPos = new Vector3(getUpFromPos.x, groundY, getUpFromPos.z);
            Vector3 face = player && alerted ? Flat(player.position - getUpFromPos) : Flat(rb.rotation * Vector3.forward);
            getUpToRot = Quaternion.LookRotation(face.sqrMagnitude > 0.001f ? face.normalized : Vector3.forward, Vector3.up);
            Say("grunt", 0.7f, Random.Range(0.9f, 1.1f));
        }

        void GetUpMove()
        {
            float p = Mathf.Clamp01(stateTimer / getUpTime);
            ActionProgress = p;
            if (getUpFromRagdoll)
            {
                // the body blends itself up from the lying pose; the root just waits for it
                if (p < 1f) return;
                getUpFromRagdoll = false;
                capsule.enabled = true;
                capsule.sharedMaterial = slippery;
                rb.isKinematic = false;
                rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
                SetState(alerted ? State.Chase : State.Idle);
                return;
            }
            float e = p * p * (3f - 2f * p);
            rb.MovePosition(Vector3.Lerp(getUpFromPos, getUpToPos, e));
            rb.MoveRotation(Quaternion.Slerp(getUpFromRot, getUpToRot, e));
            if (p < 1f) return;
            rb.isKinematic = false;
            rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
            capsule.sharedMaterial = slippery;
            SetState(alerted ? State.Chase : State.Idle);
        }

        // ------------------------------------------------------------------ climbing

        void StartClimb(Vector3 wallPoint, Vector3 top, Vector3 dir)
        {
            SetState(State.Climb);
            rb.isKinematic = true;
            climbFrom = rb.position;
            climbWall = new Vector3(wallPoint.x, rb.position.y, wallPoint.z) - dir * (capsule.radius + 0.03f);
            climbDir = dir;
            climbTopY = top.y + 0.04f;
            climbUpTime = Mathf.Max(0.3f, (climbTopY - climbFrom.y) / climbSpeed) + 0.25f;
            actionDuration = climbUpTime + climbOverTime;
        }

        void ClimbMove(float dt)
        {
            float t = stateTimer;
            ActionProgress = Mathf.Clamp01(t / actionDuration);
            Vector3 pos;
            if (t < climbUpTime)
            {
                // hands on the edge, haul up slowly (a beat of reaching first)
                float u = Mathf.Clamp01((t - 0.25f) / (climbUpTime - 0.25f));
                pos = Vector3.Lerp(climbFrom, climbWall, Mathf.Clamp01(t / 0.25f));
                pos.y = Mathf.Lerp(climbFrom.y, climbTopY, u * u * (3f - 2f * u));
            }
            else
            {
                float o = Mathf.Clamp01((t - climbUpTime) / climbOverTime);
                pos = climbWall + climbDir * ((capsule.radius + 0.5f) * o);
                pos.y = climbTopY;
            }
            rb.MovePosition(pos);
            Face(climbDir, dt);
            if (t < actionDuration) return;
            rb.isKinematic = false;
            rb.linearVelocity = climbDir * 1.5f;
            SetState(alerted ? State.Chase : State.Idle);
        }

        // ------------------------------------------------------------------ attack

        void StartAttack()
        {
            SetState(State.Attack);
            actionDuration = attackWindup + attackRecover;
            attackHitDone = false;
            if (body) body.TriggerAttack();
            Say("grunt", 1f, Random.Range(0.95f, 1.2f));
        }

        void AttackMove(float dt)
        {
            Vector3 toP = player ? Flat(player.position - rb.position) : transform.forward;
            float p = stateTimer / actionDuration;
            // plant, then lunge into the swing
            float lunge = p > 0.3f && p < 0.55f ? 2.2f : 0f;
            Move(toP.sqrMagnitude > 0.001f ? toP.normalized : transform.forward, lunge, dt);
        }

        void UpdateAttack()
        {
            ActionProgress = Mathf.Clamp01(stateTimer / actionDuration);
            if (!attackHitDone && stateTimer >= attackWindup)
            {
                attackHitDone = true;
                if (player)
                {
                    Vector3 toP = player.position - rb.position;
                    bool inReach = Flat(toP).magnitude <= attackRange + 0.45f && Mathf.Abs(toP.y) < 1.4f
                                   && Vector3.Dot(Flat(toP).normalized, transform.forward) > 0.4f;
                    if (inReach) HitPlayer(Flat(toP).normalized);
                }
            }
            if (stateTimer >= actionDuration)
            {
                attackCooldownTimer = attackCooldown;
                SetState(State.Chase);
            }
        }

        void HitPlayer(Vector3 dir)
        {
            playerDamageable?.TakeDamage(new DamageInfo
            {
                amount = attackDamage, point = player.position + Vector3.up * 1.4f, normal = -dir, direction = dir,
                collider = null, source = gameObject, melee = true
            });
            if (playerRig)
            {
                float side = Random.value < 0.5f ? -1f : 1f;
                playerRig.AddKick(new Vector3(side * 0.6f, -0.3f, -0.5f), new Vector3(35f, side * 55f, side * 90f));
                playerRig.AddTrauma(0.55f);
                playerRig.AddTremor(0.9f);
            }
            if (playerStress) playerStress.Add(0.08f);
            if (playerLight) playerLight.Flicker(0.25f);
            Say("thud", 1f, Random.Range(0.85f, 1.05f));
        }

        // ------------------------------------------------------------------ damage

        public void TakeDamage(in DamageInfo info)
        {
            if (state == State.Dead) return;
            bool head = !info.melee && body && body.IsHeadCollider(info.collider);
            float dmg = info.amount * (head ? headshotMultiplier : 1f);
            health -= dmg;
            Alert(false, true);

            Vector3 push = Flat(info.direction).sqrMagnitude > 0.001f ? Flat(info.direction).normalized : -transform.forward;
            if (health <= 0f) { Die(info, push); return; }
            if (Ragdolled)
            {
                // shot while down: the body jerks with the hit
                body.RagdollImpulse(info.direction.normalized * Mathf.Clamp(dmg * 0.04f, 0.5f, 3f), info.point);
                return;
            }

            if (dmg >= knockdownDamage) Fall(push, 1.3f);
            else if (dmg >= staggerDamage)
            {
                if (state == State.Climb) { rb.isKinematic = false; Fall(push, 0.8f); }   // knocked off the wall
                else if (state != State.Fallen && state != State.GettingUp)
                {
                    rb.AddForce(push * 2.2f, ForceMode.VelocityChange);
                    Stumble(0.45f, 0f, push);
                }
            }
            else if (!rb.isKinematic) rb.AddForce(push * 0.7f, ForceMode.VelocityChange);
        }

        void Die(in DamageInfo info, Vector3 push)
        {
            bool wasRagdolled = Ragdolled;
            SetState(State.Dead);
            alerted = false;
            All.Remove(this);
            Vector3 at = info.point != Vector3.zero ? info.point : rb.position + Vector3.up;
            if (wasRagdolled) body.RagdollImpulse(push * 3f + Vector3.up * 0.5f, at);
            else if (body && body.HasRagdoll)
                GoRagdoll(push * 3f + Vector3.up * 0.6f, at, Vector3.Cross(Vector3.up, push), 2.5f);
            else
            {
                rb.isKinematic = false;
                rb.constraints = RigidbodyConstraints.None;
                capsule.sharedMaterial = grippy;
                rb.AddForceAtPosition(push * 3f + Vector3.up * 0.6f, at, ForceMode.VelocityChange);
                rb.AddTorque(Random.insideUnitSphere * 2f, ForceMode.VelocityChange);
            }
            voice.Stop();
            Say("growl", 0.8f, Random.Range(0.6f, 0.75f));   // last gurgle
            Died?.Invoke(this);
            Destroy(gameObject, corpseLifetime);
        }

        // ------------------------------------------------------------------ helpers

        void SetState(State s)
        {
            state = s;
            stateTimer = 0f;
            ActionProgress = 0f;
        }

        void Say(string kind, float volume, float pitch)
        {
            if (!voiceEnabled) return;
            string key = kind + voiceVariant;
            if (!ClipCache.TryGetValue(key, out AudioClip clip) || !clip)
            {
                int seed = 1000 + voiceVariant * 37;
                clip = kind switch
                {
                    "shriek" => ProceduralAudio.InfectedShriek(seed),
                    "grunt" => ProceduralAudio.InfectedGrunt(seed),
                    "thud" => ProceduralAudio.Thud(),
                    _ => ProceduralAudio.InfectedGrowl(seed),
                };
                ClipCache[key] = clip;
            }
            voice.pitch = pitch;
            voice.PlayOneShot(clip, volume);
        }

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
    }
}
