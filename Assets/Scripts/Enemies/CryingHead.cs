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
    /// Walls: mostly it runs on the floor (NavMesh), but between attacks it now and then picks a spot on a wall
    /// near the player (in reach of a leap, with a clear line to the face), runs to the foot of that wall, climbs
    /// up and across to the spot, clings there head-down watching, and leaps at the face from the wall. It drops
    /// off if it loses sight of you, if you walk out of reach, or if a hard hit knocks it off. Placed rotated
    /// with its legs against a wall, it starts there and sobs on the wall.
    /// On a wall: gravity is off and it is held to the surface along its own up (the wall's normal); off a
    /// wall everything works as before.
    /// Physics body (Rigidbody + sphere), animated by a PlayableGraph (idle / move clips of the model).
    /// Built by CryingHeadBuilder (menu BodycamHorror/Crying Head/...).
    /// </summary>
    [RequireComponent(typeof(Rigidbody), typeof(SphereCollider))]
    public class CryingHead : MonoBehaviour, IDamageable
    {
        public enum State { Weeping, Noticing, Chase, Windup, Leap, Recover, Stagger, Dead, Bite, ToWall, Climb, Perch }

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
        [Tooltip("It rears back (and trembles) this long before it snaps.")]
        public float biteWindup = 0.42f;
        [Tooltip("Seconds from the start of the snap to the teeth reaching the legs: the hit lands then, not as it starts.")]
        public float biteContact = 0.1f;
        [Tooltip("Seconds from the snap until it can move again.")]
        public float biteRecover = 0.45f;
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

        [Header("Prowl (between attacks it scuttles round the feet)")]
        [Tooltip("Seconds between attacks, bite or leap (random in this range). Meanwhile it prowls.")]
        public Vector2 attackPause = new Vector2(2.5f, 4.5f);
        [Tooltip("Extra pause after a bite that landed.")]
        public float pauseAfterHit = 1.5f;
        [Tooltip("How far from the feet it circles (m, random in this range).")]
        public Vector2 prowlRadius = new Vector2(0.9f, 1.9f);
        [Tooltip("How fast it circles round the player (degrees per second).")]
        public float prowlTurnRate = 120f;
        [Tooltip("Its speed while prowling, as a share of the chase speed.")]
        [Range(0.2f, 1f)] public float prowlSpeedShare = 0.7f;
        [Tooltip("Seconds between changes of mind: turning back the other way, closer or further.")]
        public Vector2 prowlSwitch = new Vector2(0.7f, 2f);
        [Tooltip("Chance at each change of mind that it darts right in under the feet (a feint, no bite).")]
        [Range(0f, 1f)] public float feintChance = 0.3f;

        [Header("Walls (it climbs up a wall near the player and leaps at the face from there)")]
        public bool climbsWalls = true;
        [Tooltip("Between attacks, the chance it goes for a wall each time it thinks about it.")]
        [Range(0f, 1f)] public float wallChance = 0.35f;
        [Tooltip("Seconds between those thoughts.")]
        public float wallThinkInterval = 1f;
        [Tooltip("Seconds after a wall attack (or a try) before it goes for a wall again.")]
        public float wallCooldown = 8f;
        [Tooltip("Only goes for a wall while the player is this far away (flat, metres).")]
        public Vector2 wallPlayerDistance = new Vector2(1.8f, 9f);
        [Tooltip("How far around itself it looks for a wall to climb.")]
        public float wallSearchRange = 4f;
        [Tooltip("How far along the wall (towards the player) it is willing to go from where it climbs on.")]
        public float wallSlide = 3f;
        [Tooltip("Height above the floor of the spot it clings to (random in this range).")]
        public Vector2 perchHeight = new Vector2(1.5f, 2.3f);
        [Tooltip("Speed on the wall (m/s).")]
        public float climbSpeed = 3.6f;
        [Tooltip("Seconds it clings and watches before the leap (random in this range).")]
        public Vector2 perchHold = new Vector2(0.5f, 1.6f);
        [Tooltip("From the wall it leaps at the face from at most this far (m, straight line).")]
        public float wallLeapRange = 5f;
        [Tooltip("Speed of the leap from a wall (m/s).")]
        public float wallLeapSpeed = 9f;
        [Tooltip("Out of sight this long on the wall and it drops off.")]
        public float perchLoseTime = 1.2f;

        [Header("Fear")]
        [Range(0f, 1f)] public float stressOnShriek = 0.35f;
        [Range(0f, 1f)] public float stressOnHit = 0.2f;

        [Header("Sound")]
        [Tooltip("Sobs, played one after another while it sits. Left empty: it sits silent.")]
        public AudioClip[] sobs;
        [Tooltip("Its attack cry: the leap and the bite. (Silent when it notices the player and when it dies.)")]
        public AudioClip shriek;
        [Range(0.3f, 3f)] public float shriekPitch = 1.55f;
        [Tooltip("Fingertip taps while it runs. Left empty: generated.")]
        public AudioClip[] skitters;
        [Range(0f, 1f)] public float sobVolume = 0.85f;
        public Vector2 sobPause = new Vector2(0.3f, 2.4f);
        [Range(0f, 1f)] public float skitterVolume = 0.4f;
        [Tooltip("A looping scurry of legs played while it runs, louder and quicker the faster it goes. " +
                 "Set: replaces the single taps. Empty: the taps.")]
        public AudioClip scurryLoop;
        [Range(0f, 1f)] public float scurryVolume = 0.6f;
        [Tooltip("How fast the scurry swells in and dies away (volume per second).")]
        public float scurryFade = 6f;
        public LayerMask environmentMask = ~((1 << 2) | (1 << 8));   // not Ignore Raycast or Hitbox

        public State CurrentState => state;
        public bool IsDead => state == State.Dead;

        State state;
        float health, stateTimer, lastSeenTime = -100f, leapCooldownTimer, leapThinkTimer, biteCooldownTimer, sobTimer, skitterTimer, pathTimer, moveBlend;
        Vector3 lastSeenPos, leapDir, biteDir, modelBasePos;
        Quaternion modelBaseRot;
        bool leapHit, biteSnapped, biteContacted, biteLanded, deathFlipped;
        bool prowling;
        float prowlAngle, prowlDir = 1f, prowlSwitchTimer, prowlRadiusNow, prowlRadiusTarget;

        // walls
        Vector3 surfaceUp = Vector3.up;       // the normal of what it stands on: up on the floor, the wall's normal on a wall
        Vector3 wallBase, wallNormal, perchPos;
        float wallThinkTimer, wallCooldownTimer, perchHoldTime;
        bool perchReached;
        Quaternion tilt = Quaternion.identity;   // eases the model over when the body snaps onto a wall

        /// <summary>Clinging to a wall (not standing on a floor).</summary>
        public bool OnWall => surfaceUp.y < 0.7f;

        Rigidbody rb;
        SphereCollider body;
        AudioSource voice, feet, scurry;
        SoundOcclusion scurryOcclusion;
        float scurryLevel;

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
            sobClips = Usable(sobs);   // no generated fallback: without sob recordings it sits silent
            skitterClips = Usable(skitters);
            if (skitterClips.Length == 0)
            {
                skitterClips = new AudioClip[6];
                for (int i = 0; i < skitterClips.Length; i++) skitterClips[i] = ProceduralAudio.Skitter(i);
            }

            voice = MakeSource("Voice", 1.5f, 24f);
            feet = MakeSource("Feet", 1f, 14f);
            if (scurryLoop)
            {
                scurry = MakeSource("Scurry", 1f, 14f);
                scurry.clip = scurryLoop;
                scurry.loop = true;
                scurryOcclusion = scurry.GetComponent<SoundOcclusion>();
                if (scurryOcclusion) scurryOcclusion.Volume = 0f;   // the occlusion owns the volume
                else scurry.volume = 0f;
            }

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

        void Start()
        {
            // placed with its legs against a wall: it starts on that wall
            if (climbsWalls && transform.up.y < 0.7f &&
                SurfaceProbe(transform.position + transform.up * (body.radius * 2f), -transform.up, body.radius * 2f + 0.5f, out RaycastHit hit) &&
                Mathf.Abs(hit.normal.y) < 0.5f)
                AttachToWall(hit, transform.forward);
        }

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
            wallThinkTimer -= dt;
            wallCooldownTimer -= dt;

            if (state == State.Dead) { Scurry(dt, 0f); UpdateDeath(dt); return; }

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
                    if (OnWall) { DropOff(); break; }   // never chases from a wall
                    if (Time.time - lastSeenTime > loseTime) { SetState(State.Weeping); sobTimer = Random.Range(1.5f, 3f); break; }
                    float d = Flat(TargetPoint() - transform.position).magnitude;
                    if (sees && biteCooldownTimer <= 0f && d <= biteRange && Grounded()) { StartBite(); break; }
                    if (sees && ThinksOfWall(d) && FindPerch()) { SetState(State.ToWall); break; }
                    // the leap is the rare one: ready, in range, and then only now and then
                    if (sees && biteCooldownTimer <= 0f && leapCooldownTimer <= 0f && leapThinkTimer <= 0f && d <= leapRange && d >= leapMinRange && Grounded())
                    {
                        leapThinkTimer = leapThinkInterval;
                        if (Random.value < leapChance) SetState(State.Windup);
                    }
                    break;

                case State.Bite:
                    if (!biteSnapped && stateTimer >= biteWindup) Snap();
                    if (biteSnapped && !biteContacted && stateTimer >= biteWindup + biteContact) BiteContact();
                    if (stateTimer >= biteWindup + biteRecover)
                    {
                        biteCooldownTimer = NextAttackPause() + (biteLanded ? pauseAfterHit : 0f);
                        SetState(State.Chase);
                    }
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

                case State.ToWall:
                    // running to the foot of the wall; FixedUpdate climbs on when it gets there
                    if (stateTimer > 3f) { wallCooldownTimer = wallCooldown * 0.5f; SetState(State.Chase); }
                    break;

                case State.Climb:
                    if (perchReached) { perchHoldTime = Random.Range(perchHold.x, Mathf.Max(perchHold.x, perchHold.y)); SetState(State.Perch); }
                    else if (stateTimer > 4f) DropOff();   // something on the wall in the way
                    break;

                case State.Perch:
                    if (Time.time - lastSeenTime > perchLoseTime) { DropOff(); break; }
                    if (stateTimer < perchHoldTime) break;
                    float reach = Vector3.Distance(BodyCenter, TargetPoint());
                    if (sees && reach <= wallLeapRange) SetState(State.Windup);                    // the jump at the face
                    else if (reach > wallLeapRange * 1.6f || stateTimer > perchHoldTime + 3f) DropOff();   // walked away: back to the floor
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
            if (sobClips.Length == 0 || voice.isPlaying) return;
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
            // no shriek on noticing: its voice is heard only when it attacks (leap, bite)
            if (playerStress) playerStress.Add(stressOnShriek);
            if (playerLight) playerLight.Flicker(0.3f);
            leapCooldownTimer = Random.Range(0.6f, 3f);   // sometimes the first thing it does is jump, mostly not
            leapThinkTimer = 0f;
            wallThinkTimer = wallThinkInterval;
            if (OnWall)
            {
                // it was crying on a wall: it stays there a moment, then comes off it at the face (or drops)
                perchReached = true;
                perchHoldTime = Random.Range(0.15f, 0.5f);
                SetState(State.Perch);
            }
            else SetState(State.Chase);
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

        Vector3 Eye => rb.position + surfaceUp * (body.center.y + body.radius * 0.6f);

        /// <summary>The middle of the ball, on a floor or on a wall.</summary>
        Vector3 BodyCenter => rb.position + surfaceUp * body.center.y;

        /// <summary>The player's head: what it looks at and leaps for.</summary>
        Vector3 TargetPoint() => playerRig ? playerRig.transform.position : player.transform.position + Vector3.up * 1.5f;

        bool CanSeePlayer()
        {
            Vector3 eye = Eye, target = TargetPoint();
            Vector3 d = target - eye;
            float dist = d.magnitude;
            if (dist > sightRange) return false;
            bool alert = state != State.Weeping;
            if (!alert && dist > closeRange && Vector3.Angle(transform.forward, OnWall ? d : Flat(d)) > sightAngle * 0.5f) return false;
            return !Probe(eye, d / dist, dist, out _);
        }

        /// <summary>Feet on something: the floor, or the wall it clings to.</summary>
        bool Grounded() =>
            Physics.SphereCast(rb.position + surfaceUp * (body.radius + 0.05f), body.radius * 0.8f, -surfaceUp, out _,
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

            if (OnWall) { WallMove(dt); return; }

            switch (state)
            {
                case State.Noticing:
                case State.Windup:
                    face = Flat(TargetPoint() - rb.position);
                    break;
                case State.ToWall:
                {
                    // close in, straight at it: the steering would shy away from the very wall it wants
                    Vector3 toBase = Flat(wallBase - rb.position);
                    Vector3 runDir = toBase.magnitude < 1.5f ? toBase.normalized : Steer(wallBase);
                    want = runDir * speed;
                    if (runDir.sqrMagnitude > 0.01f) face = runDir;
                    // at the foot of the wall (or bumping into it): on it goes
                    if (Flat(wallBase - rb.position).magnitude < 0.3f || Flat(v).sqrMagnitude > 1f)
                    {
                        if (SurfaceProbe(BodyCenter, -wallNormal, body.radius + 0.35f, out RaycastHit hit) && Mathf.Abs(hit.normal.y) < 0.5f)
                        {
                            AttachToWall(hit, Vector3.up);
                            perchReached = false;
                            SetState(State.Climb);
                            return;
                        }
                    }
                    break;
                }
                case State.Bite:
                    // rears and turns to the legs, snaps forward a hand's width, then holds still
                    if (!biteSnapped) { face = Flat(player.transform.position - rb.position); biteDir = face.sqrMagnitude > 0.001f ? face.normalized : transform.forward; }
                    else face = biteDir;
                    want = biteSnapped && stateTimer < biteWindup + biteContact + 0.04f ? biteDir * biteLunge : Vector3.zero;
                    break;
                case State.Chase:
                    bool fresh = Time.time - lastSeenTime < 0.3f;
                    Vector3 goal = fresh ? player.transform.position : lastSeenPos;
                    float pace = 1f;
                    // between attacks: scuttle round the feet instead of sitting on them
                    if (fresh && biteCooldownTimer > 0f && Flat(player.transform.position - rb.position).magnitude < prowlRadius.y * 2.5f)
                    {
                        goal = ProwlPoint(dt);
                        pace = prowlSpeedShare;
                    }
                    else prowling = false;
                    Vector3 dir = Steer(goal);
                    // at the last seen spot: stop and listen (prowling, the point keeps moving on)
                    float slow = Flat(goal - rb.position).magnitude < (prowling ? 0.15f : 0.6f) ? 0f : 1f;
                    want = dir * speed * pace * slow;
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
            TurnTo(face, dt);
        }

        /// <summary>Turns to face this way along what it stands on (its own up is the surface normal).</summary>
        void TurnTo(Vector3 face, float dt)
        {
            Vector3 f = Vector3.ProjectOnPlane(face, surfaceUp);
            if (f.sqrMagnitude < 0.0001f) f = Vector3.ProjectOnPlane(transform.forward, surfaceUp);
            if (f.sqrMagnitude < 0.0001f) f = Vector3.ProjectOnPlane(transform.up, surfaceUp);   // fell off a wall head-down
            if (f.sqrMagnitude < 0.0001f) return;
            rb.MoveRotation(Quaternion.RotateTowards(rb.rotation, Quaternion.LookRotation(f.normalized, surfaceUp), turnSpeed * dt));
        }

        // ------------------------------------------------------------------ walls

        /// <summary>Time to think about a wall? Only between attacks, from the floor, with the player in a sensible range.</summary>
        bool ThinksOfWall(float flatDistance)
        {
            if (!climbsWalls || wallCooldownTimer > 0f || wallThinkTimer > 0f) return false;
            wallThinkTimer = wallThinkInterval;
            if (biteCooldownTimer <= 0.5f) return false;   // about to attack from the floor anyway
            if (flatDistance < wallPlayerDistance.x || flatDistance > wallPlayerDistance.y || !Grounded()) return false;
            if (Random.value >= wallChance) return false;
            wallCooldownTimer = wallCooldown * 0.3f;   // a failed search does not repeat every frame either
            return true;
        }

        /// <summary>Looks around for a wall near itself with a spot on it, at perch height and slid along towards
        /// the player, that is real wall all the way (no doorway, no window), in leaping reach of the face and with a
        /// clear line to it. Sets wallBase (the foot of the wall to run to), wallNormal and perchPos.</summary>
        bool FindPerch()
        {
            if (!player) return false;
            Vector3 origin = rb.position + Vector3.up * body.center.y;
            Vector3 feetPos = player.transform.position, head = TargetPoint();
            float floorY = rb.position.y;
            float bestScore = float.MaxValue;
            bool found = false;
            const int Rays = 12;
            float spin = Random.Range(0f, 360f / Rays);
            for (int i = 0; i < Rays; i++)
            {
                Vector3 dir = Quaternion.Euler(0f, spin + i * 360f / Rays, 0f) * Vector3.forward;
                if (!SurfaceProbe(origin, dir, wallSearchRange, out RaycastHit hit) || Mathf.Abs(hit.normal.y) > 0.25f) continue;
                Vector3 n = Flat(hit.normal).normalized;
                Vector3 along = Vector3.Cross(Vector3.up, n);
                float slide = Mathf.Clamp(Vector3.Dot(Flat(feetPos - hit.point), along), -wallSlide, wallSlide);
                Vector3 spot = hit.point + along * slide;
                spot.y = floorY + Random.Range(perchHeight.x, Mathf.Max(perchHeight.x, perchHeight.y));

                // wall there, and along the way up and across to it
                if (!WallAt(spot, n, out RaycastHit perchHit)) continue;
                Vector3 climbOn = new Vector3(hit.point.x, floorY + 0.6f, hit.point.z);
                if (!WallAt(Vector3.Lerp(climbOn, spot, 0.5f), n, out _) || !WallAt(Vector3.Lerp(climbOn, spot, 0.85f), n, out _)) continue;

                // the face in reach, nothing in between
                Vector3 eye = perchHit.point + perchHit.normal * (body.radius * 2f);
                Vector3 to = head - eye;
                float dist = to.magnitude;
                if (dist > wallLeapRange || Flat(to).magnitude < 1f) continue;
                if (Probe(eye, to / dist, dist, out _)) continue;

                // best: about three metres from the player, and a wall close by
                float score = Mathf.Abs(Flat(spot - feetPos).magnitude - 3f) + hit.distance * 0.3f;
                if (score >= bestScore) continue;
                bestScore = score;
                found = true;
                wallNormal = n;
                wallBase = hit.point + n * (body.radius + 0.05f);
                wallBase.y = floorY;
                perchPos = perchHit.point + perchHit.normal * 0.01f;
            }
            return found;
        }

        /// <summary>A plain wall facing this way at this point.</summary>
        bool WallAt(Vector3 p, Vector3 n, out RaycastHit hit) =>
            SurfaceProbe(p + n * 0.4f, -n, 0.8f, out hit) && Mathf.Abs(hit.normal.y) < 0.3f && Vector3.Dot(hit.normal, n) > 0.8f;

        /// <summary>Gets onto the wall it touches: feet on the wall, back to the room, gravity off.</summary>
        void AttachToWall(RaycastHit hit, Vector3 look)
        {
            Quaternion before = rb.rotation;
            surfaceUp = hit.normal;
            rb.useGravity = false;
            Vector3 f = Vector3.ProjectOnPlane(look, surfaceUp);
            if (f.sqrMagnitude < 0.0001f) f = Vector3.ProjectOnPlane(Vector3.up, surfaceUp);
            Quaternion after = Quaternion.LookRotation(f.normalized, surfaceUp);
            rb.position = hit.point + hit.normal * 0.01f;
            rb.rotation = after;
            transform.SetPositionAndRotation(rb.position, after);
            rb.linearVelocity = Vector3.zero;
            tilt = Quaternion.Inverse(after) * before;   // the model swings over instead of snapping
        }

        /// <summary>Lets go of the wall: falls under gravity and rights itself in the air.</summary>
        void Detach()
        {
            if (!OnWall) return;
            surfaceUp = Vector3.up;
            rb.useGravity = true;
        }

        /// <summary>Lets go and drops back to the floor, a little away from the wall.</summary>
        void DropOff()
        {
            Vector3 n = surfaceUp;
            Detach();
            rb.linearVelocity = Flat(n).normalized * 1.3f;
            wallCooldownTimer = wallCooldown;
            SetState(State.Recover);
        }

        /// <summary>On a wall: held to it along its normal, moving along it (to its spot), facing the player while it waits.</summary>
        void WallMove(float dt)
        {
            // feel for the wall under the feet; gone (an edge, a doorway) and it falls
            if (!SurfaceProbe(BodyCenter, -surfaceUp, body.center.y + 0.45f, out RaycastHit hit)) { DropOff(); return; }
            if (hit.normal.y > 0.7f) { Detach(); SetState(State.Recover); return; }   // walked down onto a floor
            surfaceUp = Vector3.Slerp(surfaceUp, hit.normal, 10f * dt).normalized;

            Vector3 want = Vector3.zero, face = transform.forward;
            switch (state)
            {
                case State.Climb:
                {
                    Vector3 to = Vector3.ProjectOnPlane(perchPos - rb.position, surfaceUp);
                    float d = to.magnitude;
                    if (d < 0.12f) perchReached = true;
                    else
                    {
                        want = to / d * climbSpeed * Mathf.Clamp01(d / 0.3f);
                        face = to;
                    }
                    break;
                }
                case State.Perch:
                case State.Windup:
                case State.Noticing:
                    if (player) face = TargetPoint() - rb.position;   // head-down on the wall, looking at you
                    break;
            }

            // along the wall: eased like on the floor; into the wall: held at the surface
            Vector3 v = rb.linearVelocity;
            Vector3 along = Vector3.MoveTowards(Vector3.ProjectOnPlane(v, surfaceUp), want, acceleration * dt);
            float gap = hit.distance - body.center.y;   // the ball's surface to the wall
            float into = Mathf.Clamp(-gap * 12f, -3f, 1f);
            rb.linearVelocity = along + surfaceUp * into;
            TurnTo(face, dt);
        }

        float NextAttackPause() => Random.Range(attackPause.x, Mathf.Max(attackPause.x, attackPause.y));

        /// <summary>A point that circles the player's feet: it changes direction and distance now and then,
        /// and sometimes darts right in under the feet without biting.</summary>
        Vector3 ProwlPoint(float dt)
        {
            Vector3 c = player.transform.position;
            if (!prowling)
            {
                prowling = true;
                Vector3 off = Flat(rb.position - c);
                prowlAngle = Mathf.Atan2(off.x, off.z) * Mathf.Rad2Deg;   // start from where it is now
                prowlDir = Random.value < 0.5f ? -1f : 1f;
                prowlRadiusNow = Mathf.Clamp(off.magnitude, prowlRadius.x, prowlRadius.y);
                prowlRadiusTarget = Random.Range(prowlRadius.x, prowlRadius.y);
                prowlSwitchTimer = Random.Range(prowlSwitch.x, prowlSwitch.y);
            }
            prowlSwitchTimer -= dt;
            if (prowlSwitchTimer <= 0f)
            {
                prowlSwitchTimer = Random.Range(prowlSwitch.x, prowlSwitch.y);
                if (Random.value < 0.6f) prowlDir = -prowlDir;
                prowlRadiusTarget = Random.value < feintChance ? 0.45f : Random.Range(prowlRadius.x, prowlRadius.y);
            }
            prowlRadiusNow = Mathf.MoveTowards(prowlRadiusNow, prowlRadiusTarget, 2.5f * dt);
            prowlAngle += prowlDir * prowlTurnRate * dt;
            Vector3 p = c + Quaternion.Euler(0f, prowlAngle, 0f) * Vector3.forward * prowlRadiusNow;
            p.y = rb.position.y;
            return p;
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
            if (OnWall)
            {
                // off the wall at the face: a flatter, faster jump, from the middle of the ball, down if need be
                Vector3 from = BodyCenter;
                Detach();
                Vector3 off = target - from;
                Vector3 offFlat = Flat(off);
                float fd = Mathf.Max(offFlat.magnitude, 0.3f);
                leapDir = offFlat / fd;
                float ft = Mathf.Max(fd / wallLeapSpeed, 0.12f);
                float fvy = Mathf.Clamp((off.y + 0.5f * -Physics.gravity.y * ft * ft) / ft, -6f, 6.5f);
                rb.linearVelocity = leapDir * (fd / ft) + Vector3.up * fvy;
                wallCooldownTimer = wallCooldown;
            }
            else
            {
                Vector3 to = target - rb.position;
                Vector3 flat = Flat(to);
                float d = Mathf.Max(flat.magnitude, 0.5f);
                leapDir = flat / d;
                float time = d / leapSpeed;
                float vy = Mathf.Clamp((to.y - body.radius + 0.5f * -Physics.gravity.y * time * time) / time, 2f, 6.5f);
                rb.linearVelocity = leapDir * leapSpeed + Vector3.up * vy;
            }
            leapHit = false;
            leapCooldownTimer = leapCooldown;
            biteCooldownTimer = recoverTime + NextAttackPause();   // the leap counts as its attack too
            if (shriek) { voice.pitch = shriekPitch * 1.25f; voice.PlayOneShot(shriek, 0.7f); }
            SetState(State.Leap);
        }

        void StartBite()
        {
            biteSnapped = false;
            biteContacted = false;
            biteLanded = false;
            prowling = false;
            Vector3 to = Flat(player.transform.position - rb.position);
            biteDir = to.sqrMagnitude > 0.001f ? to.normalized : transform.forward;
            SetState(State.Bite);
        }

        /// <summary>The ground attack starts: the head jabs at the legs. The bite itself lands a moment later
        /// (BiteContact), when the teeth get there, so a step back in that moment still saves you.</summary>
        void Snap()
        {
            biteSnapped = true;
            feet.pitch = Random.Range(0.6f, 0.75f);
            feet.PlayOneShot(skitterClips[Random.Range(0, skitterClips.Length)], 1f);
            if (shriek) { voice.pitch = shriekPitch * Random.Range(1.6f, 1.8f); voice.PlayOneShot(shriek, 0.3f); }
        }

        /// <summary>The teeth reach the legs: the hit, if the feet are still there.</summary>
        void BiteContact()
        {
            biteContacted = true;
            if (!player) return;
            Vector3 feetPos = player.transform.position;
            if (Flat(feetPos - rb.position).magnitude > biteReach) return;   // stepped back in time
            biteLanded = true;
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
            tilt = Quaternion.RotateTowards(tilt, Quaternion.identity, 540f * dt);
            if (!mixer.IsValid()) return;
            float ground = SurfaceSpeed;
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
                // the bite: rears well back and up, trembling harder as it sets, then the head jabs forward and
                // down, reaching the legs right when the bite lands (biteContact), holds there, and pulls back
                Vector3 jab = Vector3.zero;
                if (state == State.Bite)
                {
                    if (!biteSnapped)
                    {
                        float k = Mathf.Clamp01(stateTimer / biteWindup);
                        k = 1f - (1f - k) * (1f - k);   // eases into the full rear
                        jab = new Vector3(0f, 0.09f, -0.13f) * k;
                        shake = Random.insideUnitSphere * (0.012f * k * k);
                    }
                    else
                    {
                        float since = stateTimer - biteWindup;
                        float reach = Mathf.Max(biteContact, 0.02f);
                        float k = since < reach
                            ? Mathf.Lerp(-1f, 1f, since / reach)                                      // from the rear to the full jab
                            : 1f - Mathf.Clamp01((since - reach - 0.08f) / 0.25f);                    // hold, then pull back
                        jab = k >= 0f ? new Vector3(0f, -0.04f, 0.14f) * k : new Vector3(0f, 0.09f, -0.13f) * -k;
                    }
                }
                // the swing onto a wall, about the middle of the ball
                Vector3 pivot = body.center;
                model.localRotation = tilt * modelBaseRot;
                model.localPosition = pivot + tilt * (modelBasePos + shake + Vector3.up * crouch + jab - pivot);
            }
        }

        /// <summary>Speed along what it stands on: the floor, or the wall.</summary>
        float SurfaceSpeed => Vector3.ProjectOnPlane(rb.linearVelocity, surfaceUp).magnitude;

        void Skitter(float dt)
        {
            float ground = SurfaceSpeed;
            if (scurry)
            {
                // the loop: in with the running, a little quicker and louder the faster it goes
                bool running = ground >= 0.5f && Grounded();
                Scurry(dt, running ? Mathf.Clamp01(ground / strideSpeed) : 0f);
                if (running) scurry.pitch = Mathf.Lerp(scurry.pitch, Mathf.Clamp(ground / strideSpeed, 0.85f, 1.3f), dt * 4f);
                return;
            }
            if (ground < 0.5f || !Grounded()) return;
            skitterTimer -= dt;
            if (skitterTimer > 0f) return;
            skitterTimer = Random.Range(0.05f, 0.11f) * Mathf.Clamp(strideSpeed / ground, 0.5f, 2f);
            feet.pitch = Random.Range(0.85f, 1.2f);
            feet.PlayOneShot(skitterClips[Random.Range(0, skitterClips.Length)], skitterVolume * Random.Range(0.6f, 1f));
        }

        /// <summary>Eases the scurry loop towards this much of its volume; stops it once silent.</summary>
        void Scurry(float dt, float target)
        {
            if (!scurry) return;
            scurryLevel = Mathf.MoveTowards(scurryLevel, target, scurryFade * dt);
            float v = scurryVolume * scurryLevel;
            if (scurryOcclusion) scurryOcclusion.Volume = v;
            else scurry.volume = v;
            if (scurryLevel > 0.001f)
            {
                if (!scurry.isPlaying)
                {
                    scurry.time = Random.Range(0f, scurry.clip.length);   // never starts on the same leg
                    scurry.Play();
                }
            }
            else if (scurry.isPlaying) scurry.Stop();
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
                if (OnWall)
                {
                    // knocked off the wall: away from it, whichever way the shot came
                    push = (push + Flat(surfaceUp).normalized).normalized;
                    Detach();
                    wallCooldownTimer = wallCooldown;
                }
                rb.linearVelocity = push * 3.2f + Vector3.up * 1.6f;
                SetState(State.Stagger);
            }
            else rb.AddForce(push * 0.8f, ForceMode.VelocityChange);
        }

        void Die(Vector3 push)
        {
            if (OnWall)
            {
                // drops off the wall dead, upright again so the flip onto its back reads right
                push = (push + Flat(surfaceUp).normalized).normalized;
                Vector3 f = Flat(transform.forward);
                if (f.sqrMagnitude < 0.001f) f = Flat(surfaceUp);
                Detach();
                Quaternion upright = Quaternion.LookRotation(f.normalized, Vector3.up);
                rb.rotation = upright;
                transform.rotation = upright;
                tilt = Quaternion.identity;
            }
            SetState(State.Dead);
            voice.Stop();
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

        /// <summary>Like Probe, but only what it can stand on: no bodies (other heads, the infected, props), no doors.</summary>
        bool SurfaceProbe(Vector3 origin, Vector3 dir, float dist, out RaycastHit best)
        {
            best = default;
            int n = Physics.RaycastNonAlloc(origin, dir, Hits, dist, environmentMask, QueryTriggerInteraction.Ignore);
            float bestD = float.MaxValue;
            bool found = false;
            for (int i = 0; i < n; i++)
            {
                var col = Hits[i].collider;
                if (col.attachedRigidbody) continue;
                if (col.GetComponentInParent<BodycamPlayerController>() || Door.Of(col) != null) continue;
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
            if (Application.isPlaying && (state == State.ToWall || state == State.Climb || state == State.Perch))
            {
                Gizmos.color = Color.magenta;
                Gizmos.DrawLine(wallBase, perchPos);
                Gizmos.DrawWireSphere(perchPos, 0.15f);
            }
        }
    }
}
