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
    /// the corner and runs off along <see cref="fleePath"/> (or, with <see cref="duckTime"/> set, just snaps
    /// back out of sight). Nothing to fight; it is not there before or after.
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
        [Tooltip("Put it in place (hidden) when the level starts instead of when the pickup is taken, so it is " +
                 "never seen popping in, nor its shadow.")]
        public bool spawnAtStart;
        [Tooltip("How far (m) back behind the corner it waits before it leans out.")]
        public float hideDistance;
        [Tooltip("Seconds after it is set off (the door starts to open) before it leans out.")]
        public float peekDelay;
        [Tooltip("Lean from the waist: the legs and hips stay standing behind the corner and only the upper body " +
                 "bends out. Needs a humanoid model and Idle Speed 0 (it holds one pose from the idle clip).")]
        public bool bendAtWaist;
        [Tooltip("With Bend At Waist: how much of the tilt the neck takes back out (0 = the head tilts with the body, 1 = stays upright).")]
        [Range(0f, 1f)] public float headUpright = 0.5f;

        [Header("Being seen")]
        [Tooltip("How far into the view it must be: 0 = anywhere on screen, 0.35 = the middle part.")]
        [Range(0f, 0.45f)] public float viewMargin = 0.18f;
        [Tooltip("Colliders the eye sees through (window glass).")]
        public Collider[] seeThrough;
        [Tooltip("Seconds between being seen and the sting.")]
        public float reactionDelay = 0.2f;
        [Tooltip("Seconds it keeps staring after the sting before it ducks away.")]
        public float holdAfterSting = 0.45f;
        [Tooltip("Count Hold After Sting only while it is actually on screen (anywhere, nothing in between): the player " +
                 "gets that long to look at it even if they glance away and back.")]
        public bool holdOnlyWhileSeen = true;
        [Tooltip("With Hold Only While Seen: it goes anyway after this many seconds since the sting (s).")]
        public float maxHold = 1.5f;
        [Tooltip("Goes anyway if the player comes this close without looking (m).")]
        public float closeDistance = 2.2f;
        [Tooltip("Goes quietly if never seen for this long (s); 0 = waits for ever.")]
        public float giveUpAfter = 0f;

        [Header("Flight")]
        [Tooltip("Above 0: instead of running off, it snaps back behind the corner this fast (s) and is gone.")]
        public float duckTime;
        [Tooltip("How far (m) it pulls back behind the corner while ducking.")]
        public float duckDistance = 0.4f;
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
        [Tooltip("Speed of the idle while it watches. 0 = frozen still on Idle Time.")]
        public float idleSpeed = 0.6f;
        [Tooltip("Where in the idle clip it starts (s). Below 0 = anywhere.")]
        public float idleTime = -1f;
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

        [Header("Silence")]
        [Tooltip("Taking the pickup cuts every other sound in the level (ambience, lamp hum, drips...): only the " +
                 "player, the door and the monster are heard until it is over.")]
        public bool silenceWorld = true;
        [Tooltip("Seconds after the sting before the level's sounds come back.")]
        public float restoreAfterSting = 3f;

        [Header("The moment")]
        [Tooltip("A lamp near it that stutters while it goes (optional).")]
        public Light flickerLight;
        [Tooltip("Fear given to the player (0-1). 0.65 and up rings the ears, 0.8 is a full panic heartbeat.")]
        [Range(0f, 1f)] public float stress = 0.85f;

        [Header("Cold glow")]
        [Tooltip("A faint cold light on the monster's front from the moment it appears until the lamp bursts (or it is gone).")]
        public bool glow = true;
        public Color glowColor = new Color(0.45f, 0.64f, 1f);
        public float glowIntensity = 0.5f;
        public float glowRange = 1.6f;
        [Tooltip("Where the glow sits, relative to the monster (z = in front of it). It shines back onto the monster.")]
        public Vector3 glowOffset = new Vector3(0f, 1.9f, 0.45f);
        [Tooltip("Height on the monster it is aimed at (m).")]
        public float glowAimHeight = 1.35f;
        public float glowAngle = 80f;
        [Tooltip("Seconds it takes to come up (real time: it fades in behind the pickup's showcase too).")]
        public float glowFadeIn = 0.6f;

        [Header("The lamp over the pickup")]
        [Tooltip("Switched off with a click the moment the pickup is taken (optional).")]
        public Light pickupLamp;
        [Tooltip("Its glowing bulb: goes dark with it.")]
        public Renderer pickupLampGlow;
        [Tooltip("The switch. Empty = silent (the pickup's own sound covers it).")]
        public AudioClip switchClip;
        [Range(0f, 1f)] public float switchVolume = 0.8f;
        [Tooltip("Other lamps (their roots) that go out with it, e.g. the one behind the player.")]
        public Transform[] alsoOff;

        [Header("The lamp that bursts")]
        [Tooltip("A lamp (its root) that bursts, very loud, on the sting: its lights die and its glowing parts go dark (optional).")]
        public Transform burstLamp;
        [Tooltip("Its hum (an ambient sound object): switched off for good with it.")]
        public GameObject burstLampHum;
        public AudioClip burstClip;
        [Tooltip("Laid over the burst: glass dropping.")]
        public AudioClip burstLayer;
        [Range(0f, 1f)] public float burstVolume = 1f;
        [Tooltip("Seconds after the sting before it bursts: the player has seen the thing (and it has ducked away) first.")]
        public float burstDelay = 0.8f;

        enum Phase { Idle, Waiting, Watching, Reacting, Holding, Fleeing, Ducking, Done }

        Phase phase;
        Transform player;
        Camera eye;
        GameObject monster;
        Transform head;
        Animator animator;
        Transform[] spine;           // bent by hand when it leans from the waist (the animator is off then)
        Quaternion[] spineRest;
        Transform neck;
        Quaternion neckRest;
        AudioSource voice, feet, sting;
        PlayableGraph graph;
        float timer, peekTimer, stepTimer, lightIntensity, runTime, holdElapsed;
        int waypoint;
        static AudioClip generatedStinger;

        void OnEnable() => WeaponPickup.Taken += OnTaken;

        void OnDisable()
        {
            WeaponPickup.Taken -= OnTaken;
            Unsilence();
        }

        // ------------------------------------------------------------------ the silence

        readonly System.Collections.Generic.List<AudioSource> muted = new System.Collections.Generic.List<AudioSource>();
        readonly System.Collections.Generic.HashSet<AudioSource> spared = new System.Collections.Generic.HashSet<AudioSource>();
        bool silent;
        float silenceScan, restoreTimer = -1f;

        /// <summary>Everything in the level goes quiet; the click of the pickup itself is let finish.</summary>
        void Silence(Vector3 pickupAt)
        {
            silent = true;
            spared.Clear();
            foreach (var a in FindObjectsByType<AudioSource>(FindObjectsSortMode.None))
                if (a.isPlaying && !a.loop && (a.transform.position - pickupAt).sqrMagnitude < 0.25f) spared.Add(a);
            SilenceScan();
        }

        /// <summary>Mutes whatever plays now that is not the player, the door or the monster (run on and on,
        /// so sounds that start during the silence are cut too).</summary>
        void SilenceScan()
        {
            foreach (var a in FindObjectsByType<AudioSource>(FindObjectsSortMode.None))
            {
                if (a.mute || spared.Contains(a)) continue;
                Transform t = a.transform;
                if (t.IsChildOf(transform) || (monster && t.IsChildOf(monster.transform)) ||
                    (player && t.IsChildOf(player)) || (door && t.IsChildOf(door.transform))) continue;
                a.mute = true;
                muted.Add(a);
            }
        }

        void Unsilence()
        {
            silent = false;
            restoreTimer = -1f;
            foreach (var a in muted) if (a) a.mute = false;
            muted.Clear();
            spared.Clear();
        }

        void Start()
        {
            var controller = FindAnyObjectByType<BodycamPlayerController>();
            if (controller) player = controller.transform;
            if (flickerLight) lightIntensity = flickerLight.intensity;
            if (spawnAtStart)
            {
                Spawn();
                Pose(0f);   // waiting out of sight behind the corner
            }
        }

        void OnTaken(WeaponPickup taken, GameObject by)
        {
            if (phase != Phase.Idle || (pickup && taken != pickup)) return;
            if (by) player = by.transform;
            phase = Phase.Waiting;
            timer = appearDelay;
            if (silenceWorld) Silence(taken ? taken.transform.position : transform.position);
            SwitchOffPickupLamp();
        }

        float burstTimer = -1f;


        /// <summary>The lamp over the gun goes out with a click as it is taken.</summary>
        void SwitchOffPickupLamp()
        {
            if (alsoOff != null) foreach (Transform lamp in alsoOff) if (lamp) PutOut(lamp);
            if (!pickupLamp) return;
            foreach (var f in pickupLamp.GetComponents<LightFlicker>()) f.enabled = false;
            pickupLamp.enabled = false;
            if (pickupLampGlow) Darken(pickupLampGlow);
            // only a real recording: the pickup's own sound is what is heard as the gun is taken
            if (switchClip) PlayHere(switchClip, pickupLamp.transform.position, switchVolume, 1f, 15f);
        }

        /// <summary>The lamp bursts: a very loud crack of glass where it hangs (and flat in the ears), dark for good.</summary>
        void BurstLamp()
        {
            GlowOff();   // goes dark with the lamp
            if (!burstLamp) return;
            PutOut(burstLamp);
            if (burstLampHum) burstLampHum.SetActive(false);   // dead for good, not brought back when the silence ends
            Vector3 at = burstLamp.position;
            var lit = burstLamp.GetComponentInChildren<Light>(true);
            if (lit) at = lit.transform.position;
            if (burstClip)
            {
                PlayHere(burstClip, at, burstVolume, 14f, 90f).pitch = Random.Range(0.92f, 1.05f);
                sting.PlayOneShot(burstClip, burstVolume * 0.8f);
            }
            if (burstLayer) PlayHere(burstLayer, at, burstVolume, 8f, 70f).pitch = Random.Range(0.95f, 1.12f);
            var rig = player ? player.GetComponentInChildren<BodycamCameraRig>() : null;
            if (rig) rig.AddTrauma(0.25f);
        }

        /// <summary>A lamp goes dark: its lights off, its glowing parts unlit, its haze gone.</summary>
        static void PutOut(Transform lamp)
        {
            foreach (var f in lamp.GetComponentsInChildren<LightFlicker>(true)) f.enabled = false;
            foreach (var l in lamp.GetComponentsInChildren<Light>(true)) l.enabled = false;
            foreach (var r in lamp.GetComponentsInChildren<Renderer>(true)) Darken(r);
            foreach (var h in lamp.GetComponentsInChildren<LightHaze>(true))
            {
                foreach (var r in h.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
                h.enabled = false;
            }
        }

        static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

        static void Darken(Renderer r)
        {
            if (!r || !r.sharedMaterial || !r.sharedMaterial.HasProperty(EmissionId)) return;
            var block = new MaterialPropertyBlock();
            r.GetPropertyBlock(block);
            block.SetColor(EmissionId, Color.black);
            r.SetPropertyBlock(block);
        }

        /// <summary>A one-off sound placed in the room, under this object so the silence leaves it alone.</summary>
        AudioSource PlayHere(AudioClip clip, Vector3 at, float volume, float near, float far)
        {
            var go = new GameObject("Sound_" + clip.name);
            go.transform.SetParent(transform, false);
            go.transform.position = at;
            var s = go.AddComponent<AudioSource>();
            s.clip = clip;
            s.volume = volume;
            s.spatialBlend = 1f;
            s.rolloffMode = AudioRolloffMode.Logarithmic;
            s.minDistance = near;
            s.maxDistance = far;
            s.dopplerLevel = 0f;
            s.priority = 0;
            s.Play();
            Destroy(go, clip.length + 0.2f);
            return s;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (silent)
            {
                silenceScan -= dt;
                if (silenceScan <= 0f)
                {
                    silenceScan = 0.1f;
                    SilenceScan();
                }
                if (restoreTimer >= 0f && (restoreTimer -= dt) <= 0f) Unsilence();
            }
            GlowUpdate();
            // the scare is over (seen, ducked away): now the lamp bursts
            if (burstTimer >= 0f && (burstTimer -= dt) <= 0f)
            {
                burstTimer = -1f;
                BurstLamp();
            }
            // the pickup's showcase is up (the game is frozen behind it): what was to happen while the player looked
            // up from the gun happens now, out of sight, so it is already standing there in the open door when the
            // showcase goes away
            if (UI.ItemShowcase.Blocking && (phase == Phase.Waiting || phase == Phase.Watching))
            {
                if (phase == Phase.Waiting) Appear(true);
                if (monster) { peekTimer = peekTime; Pose(1f); }
                return;
            }
            switch (phase)
            {
                case Phase.Idle:
                    // waiting from the start: there only while the door hides it, so a player who wanders
                    // round there early does not find it standing about
                    if (monster) Show(!door || (!door.IsOpen && !PastDoor()));
                    break;
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
                    holdElapsed += dt;
                    if (!holdOnlyWhileSeen || Seen(0f)) timer -= dt;   // the stare only runs down while it is looked at
                    if (flickerLight) Flicker();
                    if (timer <= 0f || (holdOnlyWhileSeen && holdElapsed >= maxHold)) Flee();
                    break;
                case Phase.Fleeing:
                    Run(dt);
                    break;
                case Phase.Ducking:
                    Duck(dt);
                    break;
            }
        }

        // ------------------------------------------------------------------ appearing, peeking

        void Appear(bool unseen = false)
        {
            phase = Phase.Watching;
            timer = 0f;
            peekTimer = -peekDelay;
            if (!monster) Spawn();
            Show(true);
            GlowOn();
            if (door && !door.IsOpen)
            {
                if (unseen) door.OpenNow(monster.transform.position);   // opened behind the showcase: already open
                else door.Open(monster.transform.position);              // the creak is the cue to look up
            }
        }

        Light glowLight;
        float glowLevel;

        /// <summary>The faint cold light on its front starts to come up.</summary>
        void GlowOn()
        {
            if (!glow || !monster || glowLight) return;
            var go = new GameObject("ColdGlow");
            go.transform.SetParent(monster.transform, false);
            go.transform.localPosition = glowOffset;
            // a spot turned back onto it: the light falls on the monster only, never through the wall behind the glow
            Vector3 chest = new Vector3(0f, glowAimHeight, 0f) - glowOffset;
            if (chest.sqrMagnitude > 0.0001f) go.transform.localRotation = Quaternion.LookRotation(chest);
            glowLight = go.AddComponent<Light>();
            glowLight.type = LightType.Spot;
            glowLight.spotAngle = glowAngle;
            glowLight.innerSpotAngle = glowAngle * 0.4f;
            glowLight.color = glowColor;
            glowLight.range = glowRange;
            glowLight.shadows = LightShadows.None;
            glowLight.intensity = 0f;
            glowLevel = 0f;
        }

        void GlowUpdate()
        {
            if (!glowLight || !glowLight.enabled || glowLevel >= 1f) return;
            glowLevel = glowFadeIn > 0f ? Mathf.Min(1f, glowLevel + Time.unscaledDeltaTime / glowFadeIn) : 1f;
            glowLight.intensity = glowIntensity * glowLevel * glowLevel;
        }

        void GlowOff()
        {
            if (glowLight) glowLight.enabled = false;
        }

        bool shown = true;

        void Show(bool on)
        {
            if (on == shown || !monster) return;
            shown = on;
            foreach (var r in monster.GetComponentsInChildren<Renderer>(true)) r.enabled = on;
        }

        /// <summary>The player is on the far side of the door (the monster's side).</summary>
        bool PastDoor()
        {
            if (!player || !door) return false;
            float playerSide = door.transform.InverseTransformPoint(player.position).z;
            float monsterSide = door.transform.InverseTransformPoint(transform.position).z;
            return playerSide * monsterSide > 0f;
        }

        /// <summary>Makes the monster (model, voice, feet) where it stands hidden.</summary>
        void Spawn()
        {
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
                animator = model.GetComponentInChildren<Animator>();
                if (animator)
                {
                    animator.applyRootMotion = false;
                    animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                    head = animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Head) : null;
                }
                if (bendAtWaist && animator && animator.isHuman && idleClip && idleSpeed <= 0f)
                    HoldPose();   // one still pose, bent at the waist by hand
                else
                    Play(idleClip, idleSpeed, idleTime);   // slowed (or still): it is watching
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
        }

        /// <summary>Leans out from behind the corner, easing in and out (after Peek Delay).</summary>
        void Peek(float dt)
        {
            if (!monster) return;
            peekTimer += dt;
            float t = Mathf.Max(peekTimer, 0f);
            Pose(peekTime > 0f ? Mathf.SmoothStep(0f, 1f, t / peekTime) : 1f);
        }

        /// <summary>0 = standing hidden behind the corner (Hide Distance back), 1 = leaning all the way out.</summary>
        void Pose(float k)
        {
            if (!monster) return;
            Vector3 offset = new Vector3(peekOffset.x, 0f, peekOffset.z);
            Vector3 back = offset.sqrMagnitude > 0.0001f ? -offset.normalized : -transform.right;
            monster.transform.position = transform.position + back * (hideDistance * (1f - k)) + offset * k;
            if (spine != null)
            {
                monster.transform.rotation = transform.rotation;   // standing straight behind the corner
                Bend(peekLean * k);
                return;
            }
            Vector3 axis = offset.sqrMagnitude > 0.0001f ? Vector3.Cross(Vector3.up, offset.normalized) : transform.forward;
            monster.transform.rotation = Quaternion.AngleAxis(peekLean * k, axis) * transform.rotation;
        }

        /// <summary>Freezes the model in the idle clip's pose at Idle Time with the animator off, so the
        /// spine can be bent by hand without the animation putting it straight again.</summary>
        void HoldPose()
        {
            if (graph.IsValid()) graph.Destroy();
            idleClip.SampleAnimation(animator.gameObject, Mathf.Clamp(idleTime, 0f, idleClip.length));
            animator.enabled = false;
            var bones = new System.Collections.Generic.List<Transform>();
            foreach (var b in new[] { HumanBodyBones.Spine, HumanBodyBones.Chest, HumanBodyBones.UpperChest })
            {
                Transform t = animator.GetBoneTransform(b);
                if (t) bones.Add(t);
            }
            if (bones.Count == 0) return;
            spine = bones.ToArray();
            spineRest = new Quaternion[spine.Length];
            for (int i = 0; i < spine.Length; i++) spineRest[i] = spine[i].localRotation;
            neck = animator.GetBoneTransform(HumanBodyBones.Neck);
            if (neck) neckRest = neck.localRotation;
        }

        /// <summary>Tilts the upper body this many degrees out round the corner (towards Peek Offset),
        /// spread down the spine; the head keeps its own pose on top.</summary>
        void Bend(float degrees)
        {
            Vector3 offset = new Vector3(peekOffset.x, 0f, peekOffset.z);
            Vector3 axis = offset.sqrMagnitude > 0.0001f ? Vector3.Cross(Vector3.up, offset.normalized) : monster.transform.forward;
            Quaternion share = Quaternion.AngleAxis(degrees / spine.Length, axis);
            for (int i = 0; i < spine.Length; i++)
            {
                spine[i].localRotation = spineRest[i];
                spine[i].rotation = share * spine[i].rotation;   // the parent is already bent: each adds its share
            }
            if (neck)
            {
                // the neck takes part of the tilt back out, so the head stays nearer upright
                neck.localRotation = neckRest;
                neck.rotation = Quaternion.AngleAxis(-degrees * headUpright, axis) * neck.rotation;
            }
        }

        /// <summary>Snaps back behind the corner, fast, then is gone.</summary>
        void Duck(float dt)
        {
            if (!monster) { phase = Phase.Done; return; }
            timer += dt;
            float u = Mathf.Clamp01(timer / Mathf.Max(duckTime, 0.01f));
            float e = 1f - (1f - u) * (1f - u);   // quick out of the pose, settling at the end
            Vector3 offset = new Vector3(peekOffset.x, 0f, peekOffset.z);
            Vector3 back = offset.sqrMagnitude > 0.0001f ? -offset.normalized : -monster.transform.right;
            monster.transform.position = transform.position + offset * (1f - e) + back * (duckDistance * e);
            if (spine != null) Bend(peekLean * (1f - e));
            else
            {
                Vector3 axis = offset.sqrMagnitude > 0.0001f ? Vector3.Cross(Vector3.up, offset.normalized) : transform.forward;
                monster.transform.rotation = Quaternion.AngleAxis(peekLean * (1f - e), axis) * transform.rotation;
            }
            if (flickerLight) Flicker();
            if (u >= 1f) Vanish(4f);   // out of sight: let the scream ring out before it is removed
        }

        void Play(AnimationClip clip, float speed, float time = -1f)
        {
            if (graph.IsValid()) graph.Destroy();
            var animator = monster ? monster.GetComponentInChildren<Animator>() : null;
            if (!animator || !clip) return;
            var playable = AnimationPlayableUtilities.PlayClip(animator, clip, out graph);
            playable.SetSpeed(speed);
            playable.SetTime(time >= 0f ? Mathf.Min(time, clip.length) : Random.Range(0f, clip.length));
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

        bool Seen() => Seen(viewMargin);

        bool Seen(float margin)
        {
            if (!eye || !eye.isActiveAndEnabled) eye = Camera.main;
            if (!eye || !monster) return false;
            // the head or the shoulder, whichever shows round the corner; also the side of the head that
            // comes out first, so a head half round the jamb counts too
            Vector3 outward = new Vector3(peekOffset.x, 0f, peekOffset.z);
            outward = outward.sqrMagnitude > 0.0001f ? outward.normalized : Vector3.zero;
            return Visible(LookPoint, margin) || Visible(LookPoint + outward * 0.09f, margin) ||
                   Visible(monster.transform.position + monster.transform.up * 1.2f, margin);
        }

        bool Visible(Vector3 point, float margin)
        {
            Vector3 v = eye.WorldToViewportPoint(point);
            if (v.z <= 0f || v.x < margin || v.x > 1f - margin || v.y < margin * 0.5f || v.y > 1f - margin * 0.5f) return false;

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
            holdElapsed = 0f;
            if (silent) restoreTimer = Mathf.Max(restoreAfterSting, 0.01f);
            AudioClip clip = stinger ? stinger : generatedStinger ? generatedStinger : (generatedStinger = ProceduralAudio.ScareStinger());
            sting.PlayOneShot(clip, stingerVolume);
            if (scream)
            {
                voice.pitch = screamPitch;
                voice.PlayOneShot(scream, screamVolume);
            }
            var stressed = player ? player.GetComponentInChildren<PlayerStress>() : null;
            if (stressed) stressed.Add(stress);
            if (burstDelay <= 0f) BurstLamp();   // the lamp goes with the sting itself
            else burstTimer = burstDelay;
        }

        void Flee()
        {
            if (duckTime > 0f)
            {
                phase = Phase.Ducking;
                timer = 0f;
                return;
            }
            if (animator && !animator.enabled) animator.enabled = true;   // it was holding a pose
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
            GlowOff();
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
