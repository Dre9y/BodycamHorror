using BodycamHorror.Audio;
using BodycamHorror.Combat;
using BodycamHorror.Environment;
using BodycamHorror.Player;
using BodycamHorror.Weapons;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace BodycamHorror.Enemies
{
    /// <summary>
    /// The crying head: a head that walks on fingers and tendrils. Until it notices the player it sits
    /// where it is and sobs, so it is heard long before it is seen. When it sees the player (or hears a
    /// shot, or someone running close by) the crying stops dead; it turns to face the player, shrieks,
    /// then skitters at them fast and low. Mostly it runs right up to the feet and snaps at the legs, again
    /// and again (Bite); now and then, from a few metres, it leaps at the face instead (rare, long cooldown).
    /// After a leap it needs a moment to get its legs under it again: that is when to shoot it. Loses interest after a while
    /// out of sight and starts crying again where it stopped.
    /// Physics body (Rigidbody + sphere), animated by a PlayableGraph (idle / move clips of the model).
    /// Built by CryingHeadBuilder (menu BodycamHorror/Crying Head/...).
    /// </summary>
    [RequireComponent(typeof(Rigidbody), typeof(SphereCollider))]
    public class CryingHead : MonoBehaviour, IDamageable
    {
        public enum State { Weeping, Noticing, Chase, Windup, Leap, Recover, Stagger, Dead, Bite }

        [Header("Body")]
        [Tooltip("The model (has the Animator). Turned upside down when it dies.")]
        public Transform model;
        public AnimationClip idleClip;
        public AnimationClip moveClip;
        [Tooltip("Ground speed (m/s) at which the move clip plays at its normal pace.")]
        public float strideSpeed = 1.65f;

        [Header("Health")]
        public float maxHealth = 70f;
        [Tooltip("A single hit at least this strong knocks it back and stuns it for a moment.")]
        public float staggerDamage = 18f;

        [Header("Senses")]
        public float sightRange = 16f;
        [Tooltip("Width of the field of view, degrees.")]
        public float sightAngle = 150f;
        [Tooltip("This close it notices you even from behind (walls still block it).")]
        public float closeRange = 2.5f;
        [Tooltip("Hears the player running this close.")]
        public float hearRunRange = 7f;
        [Tooltip("Hears a gunshot this close, walls or not.")]
        public float gunshotRange = 20f;
        [Tooltip("Seconds from the crying stopping to the shriek: it turns to look at you.")]
        public float noticeTime = 1.1f;
        [Tooltip("Out of sight this long and it gives up and cries again.")]
        public float loseTime = 7f;

        [Header("Chase")]
        public float speed = 5.4f;
        public float acceleration = 25f;
        public float turnSpeed = 600f;

        [Header("Bite (the usual attack: it runs up to the feet and bites at the legs)")]
        [Tooltip("This close to the player's feet (flat) it stops and bites.")]
        public float biteRange = 0.8f;
        [Tooltip("The bite lands if the feet are still this close when it snaps.")]
        public float biteReach = 1.1f;
        [Tooltip("It rears back this long before it snaps.")]
        public float biteWindup = 0.28f;
        [Tooltip("Seconds from the snap until it can move again.")]
        public float biteRecover = 0.45f;
        public float biteCooldown = 0.6f;
        public float biteDamage = 9f;
        [Tooltip("The little lunge forward as it snaps (m/s).")]
        public float biteLunge = 3f;

        [Header("Leap (the rare attack: a jump at the face)")]
        public float leapRange = 3.4f;
        public float leapMinRange = 1.1f;
        [Tooltip("In range with the leap ready, the chance it jumps each time it thinks about it; otherwise it keeps coming for the legs.")]
        [Range(0f, 1f)] public float leapChance = 0.3f;
        [Tooltip("Seconds between those thoughts.")]
        public float leapThinkInterval = 0.7f;
        [Tooltip("It crouches and trembles this long before it jumps.")]
        public float windupTime = 0.35f;
        [Tooltip("Horizontal speed of the leap (m/s).")]
        public float leapSpeed = 8f;
        public float leapCooldown = 7f;
        public float damage = 22f;
        [Tooltip("Within this distance of the player's head during a leap it lands its bite.")]
        public float hitRadius = 0.9f;
        [Tooltip("Seconds it needs to get its legs under it after a leap.")]
        public float recoverTime = 0.9f;

        [Header("Fear")]
        [Range(0f, 1f)] public float stressOnShriek = 0.35f;
        [Range(0f, 1f)] public float stressOnHit = 0.2f;

        [Header("Sound")]
        [Tooltip("Sobs, played one after another while it sits. Left empty: generated.")]
        public AudioClip[] sobs;
        [Tooltip("When it notices the player.")]
        public AudioClip shriek;
        [Range(0.3f, 3f)] public float shriekPitch = 1.55f;
        [Tooltip("Fingertip taps while it runs. Left empty: generated.")]
        public AudioClip[] skitters;
        [Range(0f, 1f)] public float sobVolume = 0.85f;
        public Vector2 sobPause = new Vector2(0.3f, 2.4f);
        [Range(0f, 1f)] public float skitterVolume = 0.4f;
        public LayerMask environmentMask = ~((1 << 2) | (1 << 8));   // not Ignore Raycast or Hitbox

        public State CurrentState => state;
        public bool IsDead => state == State.Dead;

        State state;
        float health, stateTimer, lastSeenTime = -100f, leapCooldownTimer, leapThinkTimer, biteCooldownTimer, sobTimer, skitterTimer, pathTimer, moveBlend;
        Vector3 lastSeenPos, leapDir, biteDir, modelBasePos;
        Quaternion modelBaseRot;
        bool leapHit, biteSnapped, deathFlipped;

        Rigidbody rb;
        SphereCollider body;
        AudioSource voice, feet;

        BodycamPlayerController player;
        BodycamCameraRig playerRig;
        PlayerStress playerStress;
        BodycamFlashlight playerLight;
        PlayerShooting playerShooting;
        IDamageable playerDamageable;

        PlayableGraph graph;
        AnimationMixerPlayable mixer;
        AnimationClipPlayable idlePlayable, movePlayable;

        AudioClip[] sobClips, skitterClips;
        NavMeshPath path;   // made in Awake: Unity does not allow it in a field initializer
        int pathCorner;
        static readonly RaycastHit[] Hits = new RaycastHit[12];

        // ------------------------------------------------------------------ setup

        void Awake()
        {
            rb = GetComponent<Rigidbody>();
            body = GetComponent<SphereCollider>();
            rb.freezeRotation = true;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            health = maxHealth;
            path = new NavMeshPath();

            // generated sounds live in fields of their own: written into the serialized lists they would
            // outlive the play session as missing references and silence it the next time
            sobClips = Usable(sobs);
            if (sobClips.Length == 0)
                sobClips = new[] { ProceduralAudio.CryingSob(1), ProceduralAudio.CryingSob(2), ProceduralAudio.CryingSob(3), ProceduralAudio.CryingSob(4) };
            skitterClips = Usable(skitters);
            if (skitterClips.Length == 0)
            {
                skitterClips = new AudioClip[6];
                for (int i = 0; i < skitterClips.Length; i++) skitterClips[i] = ProceduralAudio.Skitter(i);
            }

            voice = MakeSource("Voice", 1.5f, 24f);
            feet = MakeSource("Feet", 1f, 14f);

            if (model)
            {
                modelBasePos = model.localPosition;
                modelBaseRot = model.localRotation;
                BuildGraph();
            }
        }

        AudioSource MakeSource(string name, float min, float max)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = body.center;
            var s = go.AddComponent<AudioSource>();
            s.playOnAwake = false;
            s.spatialBlend = 1f;
            s.rolloffMode = AudioRolloffMode.Logarithmic;
            s.minDistance = min;
            s.maxDistance = max;
            s.dopplerLevel = 0f;
            SoundOcclusion.Attach(s, transform);
            return s;
        }

        void BuildGraph()
        {
            var animator = model.GetComponentInChildren<Animator>();
            if (!animator) animator = model.gameObject.AddComponent<Animator>();
            animator.applyRootMotion = false;
            graph = PlayableGraph.Create(name + "_Anim");
            graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
            var output = AnimationPlayableOutput.Create(graph, "Anim", animator);
            mixer = AnimationMixerPlayable.Create(graph, 2);
            if (idleClip) { idlePlayable = AnimationClipPlayable.Create(graph, idleClip); graph.Connect(idlePlayable, 0, mixer, 0); }
            if (moveClip) { movePlayable = AnimationClipPlayable.Create(graph, moveClip); graph.Connect(movePlayable, 0, mixer, 1); }
            if (idlePlayable.IsValid()) idlePlayable.SetTime(Random.Range(0f, idleClip.length));   // two of them never in step
            mixer.SetInputWeight(0, 1f);
            output.SetSourcePlayable(mixer);
            graph.Play();
        }

        void OnEnable() => FindPlayer();

        void OnDisable()
        {
            if (playerShooting) playerShooting.Fired -= OnGunshot;
        }

        void OnDestroy()
        {
            if (graph.IsValid()) graph.Destroy();
        }

        void FindPlayer()
        {
            if (player) return;
            player = FindAnyObjectByType<BodycamPlayerController>();
            if (!player) return;
            playerDamageable = player.GetComponent<IDamageable>();
            playerRig = player.GetComponentInChildren<BodycamCameraRig>();
            playerStress = player.GetComponent<PlayerStress>();
            playerLight = player.GetComponentInChildren<BodycamFlashlight>();
            playerShooting = player.GetComponentInChildren<PlayerShooting>();
            if (playerShooting) playerShooting.Fired += OnGunshot;
        }

        // ------------------------------------------------------------------ thinking

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            if (!player) FindPlayer();
            stateTimer += dt;
            leapCooldownTimer -= dt;
            leapThinkTimer -= dt;
            biteCooldownTimer -= dt;

            if (state == State.Dead) { UpdateDeath(dt); return; }

            bool sees = player && CanSeePlayer();
            if (sees) { lastSeenPos = player.transform.position; lastSeenTime = Time.time; }

            switch (state)
            {
                case State.Weeping:
                    Weep(dt);
                    if (sees || HearsPlayer()) Notice();
                    break;

                case State.Noticing:
                    if (stateTimer >= noticeTime) Shriek();
                    break;

                case State.Chase:
                    if (Time.time - lastSeenTime > loseTime) { SetState(State.Weeping); sobTimer = Random.Range(1.5f, 3f); break; }
                    float d = Flat(TargetPoint() - transform.position).magnitude;
                    if (sees && biteCooldownTimer <= 0f && d <= biteRange && Grounded()) { StartBite(); break; }
                    // the leap is the rare one: ready, in range, and then only now and then
                    if (sees && leapCooldownTimer <= 0f && leapThinkTimer <= 0f && d <= leapRange && d >= leapMinRange && Grounded())
                    {
                        leapThinkTimer = leapThinkInterval;
                        if (Random.value < leapChance) SetState(State.Windup);
                    }
                    break;

                case State.Bite:
                    if (!biteSnapped && stateTimer >= biteWindup) Snap();
                    if (stateTimer >= biteWindup + biteRecover) { biteCooldownTimer = biteCooldown; SetState(State.Chase); }
                    break;

                case State.Windup:
                    if (stateTimer >= windupTime) Leap();
                    break;

                case State.Leap:
                    if (!leapHit && player && Vector3.Distance(rb.position + Vector3.up * body.radius, TargetPoint()) < hitRadius) Bite();
                    if ((stateTimer > 0.25f && Grounded()) || stateTimer > 1.6f) SetState(State.Recover);
                    break;

                case State.Recover:
                    if (stateTimer >= recoverTime) SetState(State.Chase);
                    break;

                case State.Stagger:
                    if (stateTimer >= 0.45f) SetState(State.Chase);
                    break;
            }

            Animate(dt);
            Skitter(dt);
        }

        void SetState(State s)
        {
            state = s;
            stateTimer = 0f;
        }

        void Weep(float dt)
        {
            if (voice.isPlaying) return;
            sobTimer -= dt;
            if (sobTimer > 0f) return;
            voice.pitch = Random.Range(0.94f, 1.06f);
            voice.clip = sobClips[Random.Range(0, sobClips.Length)];
            voice.volume = sobVolume * Random.Range(0.8f, 1f);
            voice.Play();
            sobTimer = Random.Range(sobPause.x, sobPause.y);
        }

        /// <summary>The crying stops dead. It turns to look.</summary>
        void Notice()
        {
            if (!player) return;
            voice.Stop();
            lastSeenPos = player.transform.position;
            lastSeenTime = Time.time;
            SetState(State.Noticing);
        }

        void Shriek()
        {
            if (shriek)
            {
                voice.pitch = shriekPitch * Random.Range(0.95f, 1.05f);
                voice.PlayOneShot(shriek, 1f);
            }
            if (playerStress) playerStress.Add(stressOnShriek);
            if (playerLight) playerLight.Flicker(0.3f);
            leapCooldownTimer = Random.Range(0.6f, 3f);   // sometimes the first thing it does is jump, mostly not
            leapThinkTimer = 0f;
            SetState(State.Chase);
        }

        void OnGunshot()
        {
            if (state != State.Weeping || !player) return;
            if (Vector3.Distance(transform.position, player.transform.position) < gunshotRange) Notice();
        }

        bool HearsPlayer()
        {
            if (!player) return false;
            float d = Vector3.Distance(transform.position, player.transform.position);
            return d < hearRunRange && player.Speed > player.walkSpeed + 0.3f;
        }

        Vector3 Eye => rb.position + Vector3.up * (body.center.y + body.radius * 0.6f);

        /// <summary>The player's head: what it looks at and leaps for.</summary>
        Vector3 TargetPoint() => playerRig ? playerRig.transform.position : player.transform.position + Vector3.up * 1.5f;

        bool CanSeePlayer()
        {
            Vector3 eye = Eye, target = TargetPoint();
            Vector3 d = target - eye;
            float dist = d.magnitude;
            if (dist > sightRange) return false;
            bool alert = state != State.Weeping;
            if (!alert && dist > closeRange && Vector3.Angle(transform.forward, Flat(d)) > sightAngle * 0.5f) return false;
            return !Probe(eye, d / dist, dist, out _);
        }

        bool Grounded() =>
            Physics.SphereCast(rb.position + Vector3.up * (body.radius + 0.05f), body.radius * 0.8f, Vector3.down, out _,
                               0.3f, environmentMask, QueryTriggerInteraction.Ignore);

        // ------------------------------------------------------------------ moving

        void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;
            if (state == State.Dead) { SettleCorpse(dt); return; }
            if (!player) return;
            Vector3 v = rb.linearVelocity;
            Vector3 flat = Flat(v);
            Vector3 want = Vector3.zero;
            Vector3 face = transform.forward;

            switch (state)
            {
                case State.Noticing:
                case State.Windup:
                    face = Flat(TargetPoint() - rb.position);
                    break;
                case State.Bite:
                    // rears and turns to the legs, snaps forward a hand's width, then holds still
                    if (!biteSnapped) { face = Flat(player.transform.position - rb.position); biteDir = face.sqrMagnitude > 0.001f ? face.normalized : transform.forward; }
                    else face = biteDir;
                    want = biteSnapped && stateTimer < biteWindup + 0.12f ? biteDir * biteLunge : Vector3.zero;
                    break;
                case State.Chase:
                    Vector3 goal = Time.time - lastSeenTime < 0.3f ? player.transform.position : lastSeenPos;
                    Vector3 dir = Steer(goal);
                    float slow = Flat(goal - rb.position).magnitude < 0.6f ? 0f : 1f;   // at the last seen spot: stop and listen
                    want = dir * speed * slow;
                    if (dir.sqrMagnitude > 0.01f) face = dir;
                    break;
                case State.Leap:
                    face = leapDir;
                    break;
            }

            if (state != State.Leap && state != State.Stagger)
            {
                flat = Vector3.MoveTowards(flat, want, acceleration * dt);
                rb.linearVelocity = new Vector3(flat.x, v.y, flat.z);
            }
            if (face.sqrMagnitude > 0.001f)
                rb.MoveRotation(Quaternion.RotateTowards(rb.rotation, Quaternion.LookRotation(Flat(face).normalized), turnSpeed * dt));
        }

        /// <summary>Which way to run: along the NavMesh if the scene has one, otherwise straight on, around walls.</summary>
        Vector3 Steer(Vector3 goal)
        {
            Vector3 pos = rb.position;
            pathTimer -= Time.fixedDeltaTime;
            path ??= new NavMeshPath();
            if (pathTimer <= 0f)
            {
                pathTimer = 0.3f;
                pathCorner = 0;
                if (!NavMesh.SamplePosition(pos, out NavMeshHit a, 1f, NavMesh.AllAreas) ||
                    !NavMesh.SamplePosition(goal, out NavMeshHit b, 2f, NavMesh.AllAreas) ||
                    !NavMesh.CalculatePath(a.position, b.position, NavMesh.AllAreas, path))
                    path.ClearCorners();
                else pathCorner = 1;
            }
            Vector3 next = goal;
            if (path.corners.Length > 1)
            {
                while (pathCorner < path.corners.Length - 1 && Flat(path.corners[pathCorner] - pos).magnitude < 0.5f) pathCorner++;
                next = path.corners[Mathf.Min(pathCorner, path.corners.Length - 1)];
            }

            Vector3 dir = Flat(next - pos);
            if (dir.sqrMagnitude < 0.0001f) return Vector3.zero;
            dir.Normalize();

            // feel ahead: a wall in the way, try a little to either side
            Vector3 from = pos + Vector3.up * body.center.y;
            float reach = 0.6f + Flat(rb.linearVelocity).magnitude * 0.15f;
            if (!Blocked(from, dir, reach)) return dir;
            for (int a = 30; a <= 120; a += 30)
                foreach (int s in new[] { 1, -1 })
                {
                    Vector3 d = Quaternion.Euler(0f, a * s, 0f) * dir;
                    if (!Blocked(from, d, reach)) return d;
                }
            return dir;
        }

        bool Blocked(Vector3 from, Vector3 dir, float reach)
        {
            int n = Physics.SphereCastNonAlloc(from, body.radius * 0.7f, dir, Hits, reach, environmentMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var c = Hits[i].collider;
                if (c.attachedRigidbody == rb || Hits[i].normal.y > 0.6f) continue;   // itself, or a slope
                if (c.GetComponentInParent<BodycamPlayerController>()) continue;
                var door = Door.Of(c);
                if (door && Door.EnemyPassable(c)) { if (!door.IsOpen) door.Open(rb.position, state == State.Chase); continue; }
                return true;
            }
            return false;
        }

        void Leap()
        {
            Vector3 target = TargetPoint() - Vector3.up * 0.25f;
            Vector3 to = target - rb.position;
            Vector3 flat = Flat(to);
            float d = Mathf.Max(flat.magnitude, 0.5f);
            leapDir = flat / d;
            float time = d / leapSpeed;
            float vy = Mathf.Clamp((to.y - body.radius + 0.5f * -Physics.gravity.y * time * time) / time, 2f, 6.5f);
            rb.linearVelocity = leapDir * leapSpeed + Vector3.up * vy;
            leapHit = false;
            leapCooldownTimer = leapCooldown;
            if (shriek) { voice.pitch = shriekPitch * 1.25f; voice.PlayOneShot(shriek, 0.7f); }
            SetState(State.Leap);
        }

        void StartBite()
        {
            biteSnapped = false;
            Vector3 to = Flat(player.transform.position - rb.position);
            biteDir = to.sqrMagnitude > 0.001f ? to.normalized : transform.forward;
            SetState(State.Bite);
        }

        /// <summary>The ground attack: a snap at the legs. Small, quick, and it keeps coming.</summary>
        void Snap()
        {
            biteSnapped = true;
            feet.pitch = Random.Range(0.6f, 0.75f);
            feet.PlayOneShot(skitterClips[Random.Range(0, skitterClips.Length)], 1f);
            if (shriek) { voice.pitch = shriekPitch * Random.Range(1.6f, 1.8f); voice.PlayOneShot(shriek, 0.3f); }
            if (!player) return;
            Vector3 feetPos = player.transform.position;
            if (Flat(feetPos - rb.position).magnitude > biteReach) return;   // stepped back in time
            playerDamageable?.TakeDamage(new DamageInfo
            {
                amount = biteDamage, point = feetPos + Vector3.up * 0.3f, normal = -biteDir, direction = biteDir,
                collider = null, source = gameObject, melee = true
            });
            if (playerRig)
            {
                float side = Random.value < 0.5f ? -1f : 1f;
                playerRig.AddKick(new Vector3(side * 0.08f, -0.06f, -0.1f), new Vector3(12f, side * 10f, side * 8f));   // a jolt down at the legs
                playerRig.AddTrauma(0.3f);
            }
            if (playerStress) playerStress.Add(stressOnHit * 0.5f);
        }

        void Bite()
        {
            leapHit = true;
            Vector3 dir = leapDir;
            playerDamageable?.TakeDamage(new DamageInfo
            {
                amount = damage, point = TargetPoint(), normal = -dir, direction = dir,
                collider = null, source = gameObject, melee = true
            });
            if (playerRig)
            {
                float side = Random.value < 0.5f ? -1f : 1f;
                playerRig.AddKick(new Vector3(side * 0.4f, 0.2f, -0.7f), new Vector3(-45f, side * 70f, side * 110f));
                playerRig.AddTrauma(0.7f);
                playerRig.AddTremor(1f);
            }
            if (playerStress) playerStress.Add(stressOnHit);
            if (playerLight) playerLight.Flicker(0.4f);
            // knocked off the face: it falls back
            rb.linearVelocity = -dir * 3f + Vector3.up * 2.5f;
        }

        // ------------------------------------------------------------------ looks & sound

        void Animate(float dt)
        {
            if (!mixer.IsValid()) return;
            float ground = Flat(rb.linearVelocity).magnitude;
            float target = state == State.Leap || state == State.Windup || state == State.Bite ? 1f : Mathf.Clamp01(ground / 0.8f);
            moveBlend = Mathf.MoveTowards(moveBlend, target, dt * 5f);
            mixer.SetInputWeight(0, 1f - moveBlend);
            mixer.SetInputWeight(1, moveBlend);
            if (movePlayable.IsValid())
            {
                float pace = state switch
                {
                    State.Windup => 0f,                    // frozen, legs set
                    State.Leap => 2.2f,                    // legs thrashing in the air
                    State.Stagger => 1.6f,
                    State.Bite => biteSnapped ? 2.4f : 0.15f,   // set, then a flurry as it snaps
                    _ => Mathf.Clamp(ground / strideSpeed, 0.4f, 2.6f)
                };
                movePlayable.SetSpeed(pace);
            }
            if (idlePlayable.IsValid()) idlePlayable.SetSpeed(state == State.Noticing ? 0.25f : 1f);

            // a tremble as it sets itself for the leap
            if (model)
            {
                Vector3 shake = state == State.Windup ? Random.insideUnitSphere * 0.015f : Vector3.zero;
                float crouch = state == State.Windup ? -0.05f * Mathf.Clamp01(stateTimer / windupTime) : 0f;
                // the bite: rears back and up, then the head jabs forward and down
                Vector3 jab = Vector3.zero;
                if (state == State.Bite)
                {
                    if (!biteSnapped) { float k = Mathf.Clamp01(stateTimer / biteWindup); jab = new Vector3(0f, 0.04f, -0.06f) * k; }
                    else { float k = Mathf.Clamp01((stateTimer - biteWindup) / 0.08f) * (1f - Mathf.Clamp01((stateTimer - biteWindup - 0.12f) / 0.25f)); jab = new Vector3(0f, -0.03f, 0.1f) * k; }
                }
                model.localPosition = modelBasePos + shake + Vector3.up * crouch + jab;
            }
        }

        void Skitter(float dt)
        {
            float ground = Flat(rb.linearVelocity).magnitude;
            if (ground < 0.5f || !Grounded()) return;
            skitterTimer -= dt;
            if (skitterTimer > 0f) return;
            skitterTimer = Random.Range(0.05f, 0.11f) * Mathf.Clamp(strideSpeed / ground, 0.5f, 2f);
            feet.pitch = Random.Range(0.85f, 1.2f);
            feet.PlayOneShot(skitterClips[Random.Range(0, skitterClips.Length)], skitterVolume * Random.Range(0.6f, 1f));
        }

        // ------------------------------------------------------------------ damage

        public void TakeDamage(in DamageInfo info)
        {
            if (state == State.Dead) return;
            health -= info.amount;
            Vector3 push = Flat(info.direction).sqrMagnitude > 0.001f ? Flat(info.direction).normalized : -transform.forward;

            if (state == State.Weeping || state == State.Noticing)
            {
                // shot while crying: no turning to look, it comes at once
                voice.Stop();
                if (player) { lastSeenPos = player.transform.position; lastSeenTime = Time.time; }
                if (health > 0f) Shriek();
            }

            if (health <= 0f) { Die(push); return; }
            if (info.amount >= staggerDamage && state != State.Leap)
            {
                rb.linearVelocity = push * 3.2f + Vector3.up * 1.6f;
                SetState(State.Stagger);
            }
            else rb.AddForce(push * 0.8f, ForceMode.VelocityChange);
        }

        void Die(Vector3 push)
        {
            SetState(State.Dead);
            voice.Stop();
            if (shriek) { voice.pitch = shriekPitch * 0.7f; voice.PlayOneShot(shriek, 0.8f); }
            rb.freezeRotation = true;
            rb.linearVelocity = push * 1.6f + Vector3.up * 2.2f;
            // the slippery ball it runs on would let the corpse glide away across the tiles
            body.sharedMaterial = null;
            if (playerStress) playerStress.Add(0.05f);
        }

        /// <summary>It flips onto its back, the legs twitch for a while, then it is still.</summary>
        void UpdateDeath(float dt)
        {
            if (model)
            {
                float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(stateTimer / 0.35f));
                // turn over around the middle of the head, so the legs end up in the air, not in the floor
                Vector3 pivot = body.center;
                Quaternion flip = Quaternion.Slerp(Quaternion.identity, Quaternion.Euler(0f, 0f, 180f), k);
                model.localRotation = flip * modelBaseRot;
                model.localPosition = pivot + flip * (modelBasePos - pivot);
            }
            if (mixer.IsValid())
            {
                mixer.SetInputWeight(0, 0f);
                mixer.SetInputWeight(1, 1f);
                float twitch = Mathf.Clamp01(1f - stateTimer / 2.5f);
                if (movePlayable.IsValid()) movePlayable.SetSpeed(twitch * Random.Range(0f, 3f));
                if (stateTimer > 2.6f && graph.IsValid()) graph.Stop();
            }
            if (!deathFlipped && Grounded() && (stateTimer > 3f || (stateTimer > 0.6f && rb.linearVelocity.sqrMagnitude < 0.01f)))
            {
                deathFlipped = true;
                rb.linearVelocity = Vector3.zero;
                rb.isKinematic = true;   // settled: a corpse, not a ball
            }
        }

        /// <summary>Once it is down it drags to a stop on the floor within a short skid, never glides on.</summary>
        void SettleCorpse(float dt)
        {
            if (rb.isKinematic || stateTimer < 0.15f || !Grounded()) return;
            Vector3 v = rb.linearVelocity;
            Vector3 flat = Vector3.MoveTowards(Flat(v), Vector3.zero, 14f * dt);
            rb.linearVelocity = new Vector3(flat.x, v.y, flat.z);
        }

        // ------------------------------------------------------------------ helpers

        bool Probe(Vector3 origin, Vector3 dir, float dist, out RaycastHit best)
        {
            best = default;
            int n = Physics.RaycastNonAlloc(origin, dir, Hits, dist, environmentMask, QueryTriggerInteraction.Ignore);
            float bestD = float.MaxValue;
            bool found = false;
            for (int i = 0; i < n; i++)
            {
                var col = Hits[i].collider;
                if (col.attachedRigidbody == rb) continue;
                if (col.GetComponentInParent<BodycamPlayerController>()) continue;
                if (Hits[i].distance < bestD) { bestD = Hits[i].distance; best = Hits[i]; found = true; }
            }
            return found;
        }

        static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

        /// <summary>The clips of a list that are really there (a deleted recording leaves a hole).</summary>
        static AudioClip[] Usable(AudioClip[] clips) => clips == null ? new AudioClip[0] : System.Array.FindAll(clips, c => c);

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.3f, 0.3f, 0.5f);
            Gizmos.DrawWireSphere(transform.position, leapRange);
            Gizmos.color = new Color(1f, 0.8f, 0.3f, 0.3f);
            Gizmos.DrawWireSphere(transform.position, hearRunRange);
        }
    }
}
