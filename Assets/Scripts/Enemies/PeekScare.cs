using BodycamHorror.Environment;
using BodycamHorror.Player;
using BodycamHorror.Weapons;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using Random = UnityEngine.Random;

namespace BodycamHorror.Enemies
{
    /// <summary>
    /// A scare hung on a pickup: the moment the gun is taken, a monster is there behind a corner (a door
    /// jamb), out of sight, and slowly leans out from behind it to look at the player. It waits until the
    /// player actually sees it — in the middle part of the view, nothing solid in between — then, after a
    /// beat, a loud sting and its scream hit at once, it holds the stare for a moment, ducks back behind
    /// the corner and runs off along <see cref="fleePath"/>. Nothing to fight; it is not there before or after.
    /// This transform: where it stands hidden, on the floor, facing the way it will look out.
    /// </summary>
    public class PeekScare : MonoBehaviour
    {
        [Tooltip("The pickup that sets it off. Empty = any pickup in the scene.")]
        public WeaponPickup pickup;
        [Tooltip("Seconds after the pickup before it starts to lean out (the player is still looking down).")]
        public float appearDelay = 0.4f;
        [Tooltip("A door it peeks round: opened (creaking) as it comes, if it is shut. Optional.")]
        public Door door;

        [Header("Peeking out")]
        [Tooltip("World offset it slides out by, from behind the corner into view.")]
        public Vector3 peekOffset = new Vector3(0f, 0f, -0.35f);
        [Tooltip("Degrees it tilts its body out with it, head first.")]
        public float peekLean = 16f;
        [Tooltip("Seconds to lean all the way out: slow is worse.")]
        public float peekTime = 1.6f;

        [Header("Being seen")]
        [Tooltip("How far into the view it must be: 0 = anywhere on screen, 0.35 = the middle part.")]
        [Range(0f, 0.45f)] public float viewMargin = 0.18f;
        [Tooltip("Colliders the eye sees through (window glass).")]
        public Collider[] seeThrough;
        [Tooltip("Seconds between being seen and the sting.")]
        public float reactionDelay = 0.2f;
        [Tooltip("Seconds it keeps staring after the sting before it ducks away.")]
        public float holdAfterSting = 0.45f;
        [Tooltip("Goes anyway if the player comes this close without looking (m).")]
        public float closeDistance = 2.2f;
        [Tooltip("Goes quietly if never seen for this long (s); 0 = waits for ever.")]
        public float giveUpAfter = 0f;

        [Header("Flight")]
        [Tooltip("Floor points it runs through, in order; the last is where it vanishes.")]
        public Vector3[] fleePath;
        public float speed = 7.5f;
        [Tooltip("Degrees a second it turns while running.")]
        public float turnSpeed = 900f;
        [Tooltip("Seconds to get up to speed.")]
        public float acceleration = 0.2f;
        [Tooltip("Doors on the way are thrown open when it comes this close (m).")]
        public float doorReach = 1.6f;

        [Header("Monster")]
        public GameObject modelPrefab;
        public Material modelMaterial;
        public float modelScale = 0.7f;
        public AnimationClip idleClip;
        public AnimationClip runClip;
        public float runAnimationSpeed = 1.2f;

        [Header("Sound")]
        [Tooltip("The jump-scare sting, played flat and loud. Empty = a generated one.")]
        public AudioClip stinger;
        [Range(0f, 1f)] public float stingerVolume = 1f;
        public AudioClip scream;
        [Range(0f, 1f)] public float screamVolume = 1f;
        [Tooltip("Lower = a deeper, bigger voice.")]
        public float screamPitch = 0.85f;
        public AudioClip[] steps;
        [Range(0f, 1f)] public float stepVolume = 0.9f;
        public float stepInterval = 0.26f;
        public float stepPitch = 0.72f;

        [Header("The moment")]
        [Tooltip("A lamp near it that stutters while it goes (optional).")]
        public Light flickerLight;
        [Tooltip("Fear given to the player (0-1). 0.65 and up rings the ears, 0.8 is a full panic heartbeat.")]
        [Range(0f, 1f)] public float stress = 0.85f;

        enum Phase { Idle, Waiting, Watching, Reacting, Holding, Fleeing, Done }

        Phase phase;
        Transform player;
        Camera eye;
        GameObject monster;
        Transform head;
        AudioSource voice, feet, sting;
        PlayableGraph graph;
        float timer, peekTimer, stepTimer, lightIntensity, runTime;
        int waypoint;
        static AudioClip generatedStinger;

        void OnEnable() => WeaponPickup.Taken += OnTaken;
        void OnDisable() => WeaponPickup.Taken -= OnTaken;

        void Start()
        {
            var controller = FindAnyObjectByType<BodycamPlayerController>();
            if (controller) player = controller.transform;
            if (flickerLight) lightIntensity = flickerLight.intensity;
        }

        void OnTaken(WeaponPickup taken, GameObject by)
        {
            if (phase != Phase.Idle || (pickup && taken != pickup)) return;
            if (by) player = by.transform;
            phase = Phase.Waiting;
            timer = appearDelay;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            switch (phase)
            {
                case Phase.Waiting:
                    timer -= dt;
                    if (timer <= 0f) Appear();
                    break;
                case Phase.Watching:
                    Peek(dt);
                    timer += dt;
                    if (Seen() || Close()) { phase = Phase.Reacting; timer = reactionDelay; }
                    else if (giveUpAfter > 0f && timer > giveUpAfter) Vanish(0f);
                    break;
                case Phase.Reacting:
                    Peek(dt);
                    timer -= dt;
                    if (timer <= 0f) Sting();
                    break;
                case Phase.Holding:
                    timer -= dt;
                    if (flickerLight) Flicker();
                    if (timer <= 0f) Flee();
                    break;
                case Phase.Fleeing:
                    Run(dt);
                    break;
            }
        }

        // ------------------------------------------------------------------ appearing, peeking

        void Appear()
        {
            phase = Phase.Watching;
            timer = 0f;
            peekTimer = 0f;
            monster = new GameObject("PeekMonster");
            monster.transform.SetPositionAndRotation(transform.position, transform.rotation);

            if (modelPrefab)
            {
                var model = Instantiate(modelPrefab, monster.transform, false);
                model.transform.localScale = Vector3.one * modelScale;
                foreach (var c in model.GetComponentsInChildren<Collider>(true)) Destroy(c);
                foreach (var r in model.GetComponentsInChildren<Renderer>(true))
                {
                    if (modelMaterial) r.sharedMaterial = modelMaterial;
                    if (r is SkinnedMeshRenderer smr) smr.updateWhenOffscreen = true;
                }
                var animator = model.GetComponentInChildren<Animator>();
                if (animator)
                {
                    animator.applyRootMotion = false;
                    animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                    head = animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Head) : null;
                }
                Play(idleClip, 0.6f);   // slowed: it is holding still, watching
            }
            else
            {
                // no model: a dark figure-sized block, so the moment can still be tried out
                var block = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                Destroy(block.GetComponent<Collider>());
                block.transform.SetParent(monster.transform, false);
                block.transform.localPosition = Vector3.up * 1f;
                block.transform.localScale = new Vector3(0.6f, 1f, 0.45f);
            }

            voice = Source("Voice", 6f, 45f);
            feet = Source("Feet", 2f, 30f);
            sting = gameObject.AddComponent<AudioSource>();   // flat: it is in the player's head, not in the room
            sting.playOnAwake = false;
            sting.spatialBlend = 0f;
            sting.priority = 0;
            sting.bypassReverbZones = true;

            if (door && !door.IsOpen) door.Open(monster.transform.position);   // the creak is the cue to look up
        }

        /// <summary>Leans out from behind the corner, slowly, easing in and out.</summary>
        void Peek(float dt)
        {
            if (!monster) return;
            peekTimer += dt;
            float k = peekTime > 0f ? Mathf.SmoothStep(0f, 1f, peekTimer / peekTime) : 1f;
            Vector3 offset = new Vector3(peekOffset.x, 0f, peekOffset.z);
            monster.transform.position = transform.position + offset * k;
            Vector3 axis = offset.sqrMagnitude > 0.0001f ? Vector3.Cross(Vector3.up, offset.normalized) : transform.forward;
            monster.transform.rotation = Quaternion.AngleAxis(peekLean * k, axis) * transform.rotation;
        }

        void Play(AnimationClip clip, float speed)
        {
            if (graph.IsValid()) graph.Destroy();
            var animator = monster ? monster.GetComponentInChildren<Animator>() : null;
            if (!animator || !clip) return;
            var playable = AnimationPlayableUtilities.PlayClip(animator, clip, out graph);
            playable.SetSpeed(speed);
            playable.SetTime(Random.Range(0f, clip.length));
        }

        AudioSource Source(string name, float near, float far)
        {
            var go = new GameObject(name);
            go.transform.SetParent(monster.transform, false);
            go.transform.localPosition = Vector3.up * 1.4f;
            var s = go.AddComponent<AudioSource>();
            s.playOnAwake = false;
            s.spatialBlend = 1f;
            s.rolloffMode = AudioRolloffMode.Logarithmic;
            s.minDistance = near;
            s.maxDistance = far;
            s.dopplerLevel = 0.3f;
            s.priority = 0;
            return s;
        }

        // ------------------------------------------------------------------ being seen

        Vector3 LookPoint => head ? head.position : monster.transform.position + monster.transform.up * 1.5f;

        bool Seen()
        {
            if (!eye || !eye.isActiveAndEnabled) eye = Camera.main;
            if (!eye || !monster) return false;
            // the head or the shoulder, whichever shows round the corner
            return Visible(LookPoint) || Visible(monster.transform.position + monster.transform.up * 1.2f);
        }

        bool Visible(Vector3 point)
        {
            Vector3 v = eye.WorldToViewportPoint(point);
            if (v.z <= 0f || v.x < viewMargin || v.x > 1f - viewMargin || v.y < viewMargin * 0.5f || v.y > 1f - viewMargin * 0.5f) return false;

            Vector3 from = eye.transform.position, dir = point - from;
            float distance = dir.magnitude;
            var hits = Physics.RaycastAll(from, dir / distance, distance - 0.12f, ~0, QueryTriggerInteraction.Ignore);
            foreach (RaycastHit hit in hits)
            {
                if (player && hit.collider.transform.IsChildOf(player)) continue;
                if (monster && hit.collider.transform.IsChildOf(monster.transform)) continue;
                if (seeThrough != null && System.Array.IndexOf(seeThrough, hit.collider) >= 0) continue;
                return false;   // a wall, a door, furniture in between
            }
            return true;
        }

        bool Close() => player && monster && closeDistance > 0f &&
                        (player.position - monster.transform.position).sqrMagnitude < closeDistance * closeDistance;

        // ------------------------------------------------------------------ the sting, the flight

        void Sting()
        {
            phase = Phase.Holding;
            timer = holdAfterSting;
            AudioClip clip = stinger ? stinger : generatedStinger ? generatedStinger : (generatedStinger = ProceduralAudio.ScareStinger());
            sting.PlayOneShot(clip, stingerVolume);
            if (scream)
            {
                voice.pitch = screamPitch;
                voice.PlayOneShot(scream, screamVolume);
            }
            var stressed = player ? player.GetComponentInChildren<PlayerStress>() : null;
            if (stressed) stressed.Add(stress);
        }

        void Flee()
        {
            phase = Phase.Fleeing;
            waypoint = 0;
            runTime = 0f;
            stepTimer = 0.05f;
            Play(runClip, runAnimationSpeed);
            if (fleePath == null || fleePath.Length == 0) Vanish(3f);
        }

        void Run(float dt)
        {
            if (!monster) { phase = Phase.Done; return; }
            runTime += dt;
            Vector3 target = fleePath[waypoint];
            Vector3 p = monster.transform.position;
            Vector3 to = target - p;
            to.y = 0f;

            if (to.sqrMagnitude > 0.0004f)   // also stands it up straight again out of the lean
            {
                var want = Quaternion.LookRotation(to.normalized);
                monster.transform.rotation = Quaternion.RotateTowards(monster.transform.rotation, want, turnSpeed * dt);
            }
            float pace = speed * (acceleration > 0f ? Mathf.Clamp01(runTime / acceleration) : 1f);
            monster.transform.position = Vector3.MoveTowards(p, new Vector3(target.x, p.y, target.z), pace * dt);

            foreach (Door d in Door.All)   // a shut door in the way is thrown open
                if (d && !d.IsOpen && (d.Centre - monster.transform.position).sqrMagnitude < doorReach * doorReach)
                    d.Open(monster.transform.position, true);

            stepTimer -= dt;
            if (stepTimer <= 0f && steps != null && steps.Length > 0)
            {
                stepTimer = stepInterval * Random.Range(0.9f, 1.1f);
                var clip = steps[Random.Range(0, steps.Length)];
                feet.pitch = stepPitch * Random.Range(0.94f, 1.06f);
                if (clip) feet.PlayOneShot(clip, stepVolume);
            }

            if (flickerLight) Flicker();

            if (new Vector2(to.x, to.z).sqrMagnitude < 0.01f && ++waypoint >= fleePath.Length)
                Vanish(3f);   // round the corner: let the scream fade out in the dark before removing it
        }

        void Flicker() =>
            flickerLight.intensity = lightIntensity * (Random.value < 0.35f ? Random.Range(0f, 0.25f) : Random.Range(0.6f, 1.1f));

        void Vanish(float soundTail)
        {
            phase = Phase.Done;
            if (flickerLight) flickerLight.intensity = lightIntensity;
            if (graph.IsValid()) graph.Destroy();
            if (!monster) return;
            foreach (var r in monster.GetComponentsInChildren<Renderer>()) r.enabled = false;
            Destroy(monster, soundTail);
        }

        void OnDestroy()
        {
            if (graph.IsValid()) graph.Destroy();
        }

        void OnDrawGizmos()
        {
            Gizmos.color = new Color(1f, 0.2f, 0.1f, 0.9f);
            Gizmos.DrawWireSphere(transform.position + Vector3.up * 0.9f, 0.3f);
            Gizmos.DrawWireSphere(transform.position + peekOffset + Vector3.up * 1.5f, 0.15f);   // where the head comes out
            Gizmos.DrawLine(transform.position, transform.position + transform.forward * 0.8f);
            if (fleePath == null) return;
            Vector3 last = transform.position;
            foreach (Vector3 p in fleePath)
            {
                Gizmos.DrawLine(last, p);
                Gizmos.DrawWireSphere(p, 0.15f);
                last = p;
            }
        }
    }
}
