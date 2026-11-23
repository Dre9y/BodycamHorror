using System.Collections;
using System.Collections.Generic;
using BodycamHorror.AI;
using BodycamHorror.Environment;
using BodycamHorror.Player;
using BodycamHorror.Weapons;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;

namespace BodycamHorror.Enemies
{
    /// <summary>Where a pose of the light monster fits.</summary>
    [System.Flags]
    public enum PoseFit
    {
        Open = 1,
        Corridor = 2,
        Doorway = 4,
        LowCeiling = 8,
        Peek = 16,
    }

    /// <summary>A pose the light monster is caught in: a moment of a clip, mixed with a moment of another, bent a little.</summary>
    [System.Serializable]
    public class FreezePose
    {
        public string name;
        public AnimationClip clip;
        [Range(0f, 1f)] public float time;
        [Tooltip("Optional second clip mixed in (e.g. the crawl into the hunched run: dropping onto its hands).")]
        public AnimationClip blendClip;
        [Range(0f, 1f)] public float blendTime;
        [Range(0f, 1f)] public float blend;
        [Tooltip("Where it may be used.")]
        public PoseFit fit = PoseFit.Open;
        [Tooltip("Leans out sideways this far (deg) towards the open side: for peeking round a corner.")]
        public float lean;
        [Tooltip("Turns its shoulders this far (deg) round the length of its back: for squeezing through a gap.")]
        public float twist;

        public FreezePose() { }

        public FreezePose(string name, AnimationClip clip, float time, PoseFit fit, AnimationClip blendClip = null, float blendTime = 0f,
                          float blend = 0f, float lean = 0f, float twist = 0f)
        {
            this.name = name; this.clip = clip; this.time = time; this.fit = fit;
            this.blendClip = blendClip; this.blendTime = blendTime; this.blend = blend; this.lean = lean; this.twist = twist;
        }
    }

    /// <summary>
    /// "Now you see me": a tall thing that only moves while nobody sees it. Seen means some part of it is on screen,
    /// with nothing in the way, and lit (by the flashlight, a lamp): in the dark it comes on even in plain view.
    /// Seen, it stands frozen in whatever pose it was caught in, its head turned to the player.
    ///
    /// It does not fit under the ceilings at its full height: it goes badly hunched, every so often dropping onto its
    /// hands and pushing itself up again, its head jerking about (the upright run and the crawl on all fours mixed,
    /// with a few bones bent on top). In a narrow corridor it goes on all fours with its shoulders drawn in, through a
    /// doorway it goes on all fours with its shoulders turned into the gap, and behind a corner it leans out to peek.
    /// A capsule along its back is kept out of the walls, and the spots it stands at have room for its body.
    ///
    /// With the NavMesh (the light monster's level):
    /// <list type="bullet">
    /// <item>Hunt: after the player. Far away it creeps (<see cref="stalkSpeed"/>), close up it charges (<see cref="runSpeed"/>).</item>
    /// <item>The player in a <see cref="SafeZone"/>: it never steps in. It comes to the edge and prowls round it, close by,
    /// only while the player is not looking at it.</item>
    /// <item>Stared at for <see cref="stareTime"/> seconds from inside a zone: it screams, very loud, and bolts into the
    /// dark; the flashlight dies for a couple of seconds so it is only heard going.</item>
    /// <item>From then on its steps are quiet. It hides behind a wall where the player cannot see it and waits, peeking
    /// out now and then; when the player leaves the light (or comes close, or waits it out) it hunts again.</item>
    /// </list>
    /// Without a NavMesh (the chain room) it simply comes straight on, faster the longer it chases.
    /// Reaching the player is the end: it is in the face, screaming, the picture goes black and the scene starts over.
    /// Waits, hidden, until <see cref="appearOn"/> bursts (or appears at once without one, or on <see cref="AppearNow"/>).
    /// Lights with a <see cref="MonsterBlindLight"/> do not count as light on it.
    /// </summary>
    public class Manthing : MonoBehaviour, PlayerStress.IFearSource, Combat.IDamageable
    {
        [Header("Body (built by the scene builder)")]
        public Animator animator;
        [Tooltip("Hunched, nearly upright run (\"standing run\"): the upright half of its gait.")]
        public AnimationClip runClip;
        [Tooltip("Crawl on all fours (\"walk\"): the half of its gait on its hands. Without it the run alone is played.")]
        public AnimationClip walkClip;
        [Tooltip("Gallop on all fours (\"run\"): on its hands when it charges.")]
        public AnimationClip crawlRunClip;
        [Tooltip("Crouched on all fours, looking about (\"search\"): standing low.")]
        public AnimationClip searchClip;
        [Tooltip("Standing hunched (\"idle standing\"): standing tall.")]
        public AnimationClip idleClip;
        [Tooltip("Only without the crawl clips (the old set-up): when seen it freezes in a random moment of one of these.")]
        public AnimationClip[] freezeClips;
        [Tooltip("The scream: in the player's face at the end, and when it is stared down from a safe zone.")]
        public AnimationClip screechClip;
        [Tooltip("Height of the sole above the lowest foot bone (m): keeps the feet on the floor.")]
        public float soleHeight = 0.03f;
        [Tooltip("Not used any more (the headroom is measured on the pose itself).")]
        public float poseHeight = 2.3f;
        [Tooltip("The poses it is caught in. Unseen it moves without any animation (it is dark, there is only its sound); " +
                 "the moment the light finds it, it stands in one of these that fits where it is (and fits in there). " +
                 "Empty: made from the clips above.")]
        public FreezePose[] poses;
        [Tooltip("Under a ceiling too low even for its hunch it shrinks to fit (only while unseen).")]
        public bool fitUnderCeiling = true;
        [Tooltip("Whatever the pose, the head is turned to the player the moment it freezes (and then holds still).")]
        public bool headFollowsPlayer = true;
        [Tooltip("Share of the turn the neck takes; the head does the rest.")]
        [Range(0f, 1f)] public float neckShare = 0.4f;

        [Header("Gait (hunched, dropping onto its hands)")]
        [Tooltip("How upright it goes in the open (0 = on all fours, 1 = the hunched run).")]
        [Range(0f, 1f)] public float uprightShare = 0.55f;
        [Tooltip("Every this many seconds it drops onto its hands ...")]
        public Vector2 handDropEvery = new Vector2(1.1f, 3f);
        [Tooltip("... for this long (s).")]
        public Vector2 handDropTime = new Vector2(0.35f, 0.8f);
        [Tooltip("Head height over the floor (at the model's own scale) upright and on all fours: how much ceiling each needs.")]
        public Vector2 headHeights = new Vector2(1.45f, 0.85f);
        [Tooltip("Ground covered by one cycle of the creep and of the charge (m, at the model's own scale).")]
        public Vector2 strides = new Vector2(0.8f, 1.9f);
        [Tooltip("The head snaps to a new tilt every so often while it moves (s) ...")]
        public Vector2 twitchEvery = new Vector2(1.2f, 3f);
        [Tooltip("How fast the head turns to a new tilt (deg/s): low = a slow, deliberate turn.")]
        public float twitchSpeed = 70f;
        [Tooltip("Standing at the edge of a safe zone (in its dim light) its head slowly rolls this far (deg) ...")]
        public float headSwayAngle = 22f;
        [Tooltip("... over this long (s) one way and back.")]
        public float headSwayPeriod = 5.5f;
        [Tooltip("Looked at by the player at the edge of the light, its body stops dead and only its head turns to them, this fast (deg/s).")]
        public float headWatchSpeed = 12f;
        [Tooltip("... by up to this much (deg).")]
        public float twitchAngle = 32f;

        [Header("Tight places")]
        [Tooltip("Narrower than this (m, wall to wall) is a corridor: on all fours, shoulders drawn in, head low.")]
        public float corridorWidth = 1.9f;
        [Tooltip("Narrower than this (m), or a Doorway marker: a door. On all fours, shoulders turned into the gap.")]
        public float doorWidth = 1.25f;
        [Tooltip("How far it turns its shoulders in a doorway (deg).")]
        public float doorTwist = 38f;
        [Tooltip("How far it leans out from behind a corner to peek (deg, spread down the spine).")]
        public float peekLean = 34f;
        [Tooltip("Radius of the capsule along its back that is kept out of the walls (m, at the model's own scale).")]
        public float bodyRadius = 0.2f;
        [Tooltip("Spots it stops at keep this much room (m) round the body, past what the NavMesh already keeps.")]
        public float standClearance = 0.3f;
        [Tooltip("After every pose: the back bent out of the walls, feet put down on the floor, hands laid on the wall or floor they are in or near.")]
        public bool fitBodyToRoom = true;

        [Header("Appearing")]
        public BurstingBulb appearOn;
        [Tooltip("Stays hidden until AppearNow() is called by a scripted event, instead of appearing at once.")]
        public bool appearOnCall;

        [Header("Hunting")]
        [Tooltip("Find the way by the NavMesh when the scene has one (RuntimeNavMesh); without one it heads straight for the player (or the doorway).")]
        public bool useNavMesh = true;
        [Tooltip("Far from the player it creeps at this speed (m/s) ...")]
        public float stalkSpeed = 1.5f;
        [Tooltip("... and within runDistance it charges at this one.")]
        public float runSpeed = 4.8f;
        [Tooltip("It starts charging inside the first distance (m) and goes back to creeping beyond the second.")]
        public Vector2 runDistance = new Vector2(8f, 11.5f);
        public float turnSpeed = 420f;
        [Tooltip("Not used any more (stalkSpeed and runSpeed).")]
        public float navSpeed = 3.3f;
        [Tooltip("The old way through doorways: shoots through at doorwaySpeed and cannot be frozen there. Off: it goes through " +
                 "on all fours, shoulders turned, and the light holds it there like anywhere else.")]
        public bool dashThroughDoorways;
        public float doorwaySpeed = 10f;

        [Header("No NavMesh (straight at the player)")]
        public float startSpeed = 2.2f;
        public float topSpeed = 5.2f;
        [Tooltip("Seconds of chasing (unseen) to get from the start speed to the top one.")]
        public float rampTime = 25f;
        [Tooltip("Where it heads when it cannot see the player (the door between the room and the corridor).")]
        public Vector3 doorway;

        [Header("Safe zones: prowling round the edge")]
        [Tooltip("How far outside the zone's edge it prowls (m).")]
        public Vector2 prowlStandoff = new Vector2(0.2f, 2.8f);
        [Tooltip("How far round the zone it goes in one move (deg either way).")]
        public float prowlArc = 110f;
        [Tooltip("Stands still between moves (s).")]
        public Vector2 prowlPause = new Vector2(0.8f, 2.6f);
        [Tooltip("Prowling speed (m/s).")]
        public float prowlSpeed = 1.3f;
        [Range(0f, 1f)] public float lurkBreathVolume = 0.85f;
        [Tooltip("Pause between breaths at the edge (s): heavy, quick breathing.")]
        public Vector2 lurkBreathGap = new Vector2(0.02f, 0.3f);
        [Tooltip("The heavy breathing carries this far (m).")]
        public float lurkBreathRange = 16f;

        [Header("Stared down (the flashlight on it, from a safe zone)")]
        [Tooltip("Lit by the flashlight from inside a safe zone this long (s) and it screams and runs.")]
        public float stareTime = 3f;
        [Tooltip("How near the middle of the screen it has to be to count as looked at (share of the screen from the centre).")]
        [Range(0.05f, 0.6f)] public float stareCone = 0.3f;
        [Tooltip("Flashlight light on it (same measure as litThreshold) for the stare to count.")]
        public float torchThreshold = 0.3f;
        [Tooltip("The scream, standing still, before the flashlight goes (s).")]
        public float screamHold = 1.15f;
        [Range(0f, 1f)] public float screamVolume = 1f;
        [Tooltip("The flashlight blinks this many times, then dies ...")]
        public int blackoutBlinks = 2;
        [Tooltip("... for this long (s); it bolts in the dark.")]
        public Vector2 blackoutTime = new Vector2(2f, 3f);
        [Tooltip("It bolts at this speed (m/s), loud ...")]
        public float retreatSpeed = 7.5f;
        [Tooltip("... to a dark spot out of sight this far from the player (m).")]
        public Vector2 retreatDistance = new Vector2(7f, 15f);
        [Tooltip("Stared down, it bolts to another room: a spot this far (m, walking) from the player, out of their sight.")]
        public Vector2 retreatWalk = new Vector2(14f, 32f);
        [Tooltip("Shot off, it runs to the far end of the hall or another room: this far (m, walking) from the player.")]
        public Vector2 fleeWalk = new Vector2(11f, 28f);
        [Tooltip("After either it stays put this long (s), giving the player a breather (unless they walk right up to it).")]
        public Vector2 restAfterRetreat = new Vector2(6f, 10f);
        [Tooltip("Not getting anywhere on its way for this long (s) and, out of the player's view, it is simply put where it was going.")]
        public float stuckTime = 1.5f;
        [Tooltip("It never runs off to wait within this far (m) of a safe zone's edge: the lights are where the player has to go.")]
        public float safeZoneAvoid = 8f;

        [Header("Turning to stone (the flashlight finds it)")]
        [Tooltip("Played on it each time the flashlight catches it. Empty: a crackle of stone made at start-up.")]
        public AudioClip petrifyClip;
        [Range(0f, 1f)] public float petrifyVolume = 0.9f;

        [Header("Enraged (the flashlight held on it, out of the light)")]
        [Tooltip("The flashlight on it this long (s) with the player outside a safe zone, and it snaps: screams, kills the light and comes.")]
        public float aggressionTime = 4.5f;
        [Tooltip("It stands screaming this long (s) before it comes.")]
        public float enrageWindup = 0.8f;
        [Tooltip("Coming at the player at this speed (m/s), the light does not stop it.")]
        public float enrageSpeed = 5f;
        [Tooltip("Enraged, its gallop jerks (a jump in the stride, or a hitch) every so often (s).")]
        public Vector2 jerkEvery = new Vector2(0.15f, 0.5f);
        [Tooltip("Shots do not stop it: it runs at the player all the way. Hit this many times by the time it gets to them, " +
                 "it breaks off, howling, and runs; fewer, and it is the scream in the face.")]
        public int hitsToSpare = 2;
        [Tooltip("It breaks off this far (m) outside the kill distance: right up to them, never into them.")]
        public float breakOffMargin = 0.6f;
        [Tooltip("Shot off, it runs in the dark: the flashlight dies for this long (s) as it turns.")]
        public float repelBlackout = 2f;
        [Tooltip("Its howls while it comes, one after another. Empty: none (only the scream when it snaps).")]
        public AudioClip[] enrageHowls;
        [Tooltip("The scream when it snaps and comes. Empty: the scream.")]
        public AudioClip enrageScream;
        [Tooltip("Pause between howls (s).")]
        public Vector2 howlEvery = new Vector2(0.9f, 1.8f);
        [Tooltip("Its roar, looped, from the moment it runs at the player until it breaks off. Empty: none.")]
        public AudioClip enrageLoop;
        [Range(0f, 1f)] public float enrageLoopVolume = 1f;
        [Tooltip("The roar comes up this fast as it starts running, and dies away this fast when it stops (s).")]
        public Vector2 enrageLoopFade = new Vector2(0.15f, 0.5f);
        [Tooltip("Its wail as it runs off out of its rage (shot off, the player in the light, or giving up), from its head "
               + "as it goes. Empty: the plain scream when shot off, nothing as it runs.")]
        public AudioClip retreatWail;
        [Range(0f, 1f)] public float retreatWailVolume = 1f;
        [Tooltip("Not reached in this long (s) and it breaks off anyway.")]
        public float enrageGiveUp = 14f;

        [Header("Rampage (the shotgun taken: it comes from across the hall, screaming, and nothing stops it)")]
        [Tooltip("The run it comes with (the Sniffer's hostile run, retargeted). Empty: its own gait.")]
        public AnimationClip rampageWalkClip;
        [Tooltip("It runs at this speed (m/s), never stopping: not lit, not stared down, not the light of a safe zone.")]
        public float rampageSpeed = 4.5f;
        [Tooltip("Ground covered by one cycle of the run (m, at the model's own scale): how fast the clip plays for the speed " +
                 "(shorter = the run plays faster).")]
        public float rampageStride = 1.2f;
        [Tooltip("The run's pace lurches faster and slower by up to this share either way.")]
        [Range(0f, 0.8f)] public float rampagePaceWobble = 0.3f;
        [Tooltip("Not used any more (a shot now makes it scream and blink away: see below).")]
        public float rampageStagger = 0.2f;
        [Tooltip("Shot this many times and it falls dead (each shot before that: a deafening scream, a blow to the picture, a " +
                 "blink, and it is somewhere else, out of sight).")]
        public int rampageHitsToKill = 3;
        [Tooltip("The deafening scream when it is shot. Empty: the rage scream, or the scream (placeholder).")]
        public AudioClip rampageHitScream;
        [Range(0f, 1f)] public float rampageHitScreamVolume = 1f;
        [Tooltip("How hard the picture is hit with the scream (camera trauma).")]
        [Range(0f, 1f)] public float rampageHitShake = 1f;
        [Tooltip("The blink: down to black this fast, held (it is moved while it is black), and back (s).")]
        public Vector3 rampageBlink = new Vector3(0.08f, 0.35f, 0.45f);
        [Tooltip("After a blink it is this far away (m, walking), out of the player's sight.")]
        public Vector2 rampageBlinkWalk = new Vector2(10f, 24f);
        [Tooltip("Its long moan as it falls dead. Empty: the wail as it runs off, slowed down (placeholder).")]
        public AudioClip deathMoan;
        [Range(0f, 1f)] public float deathMoanVolume = 1f;
        [Tooltip("It falls dead as a ragdoll this heavy (kg). Without a humanoid body: it tips over in this long (deathFallTime).")]
        public float corpseMass = 120f;
        [Tooltip("How long it takes to fall over (s), when it cannot go limp (no humanoid body).")]
        public float deathFallTime = 0.9f;
        [Tooltip("Its screams as it comes, one after another (the first the moment it appears). Empty: the rage scream, or the scream.")]
        public AudioClip[] rampageScreams;
        [Tooltip("Pause between screams (s).")]
        public Vector2 rampageScreamEvery = new Vector2(1.6f, 3.2f);
        [Range(0f, 1f)] public float rampageScreamVolume = 1f;

        [Header("The showdown (once, at a valve turned in the light)")]
        [Tooltip("Turned from inside a safe zone, this valve brings it: it rushes to the edge of the light and circles the " +
                 "player slowly, close (the flashlight does not stare it down now). The valve shut, it holds its breath, the " +
                 "fear and every sound fall away, and it waits for the player to turn and look at it; then it screams, the " +
                 "flashlight dies with a blow to the picture, and it is gone. After that it hunts as ever. Empty: no showdown.")]
        public Valve showdownValve;
        [Tooltip("It comes to the light this fast (m/s) ...")]
        public float showdownRushSpeed = 6.5f;
        [Tooltip("... and circles the player this slowly (m/s).")]
        public float showdownProwlSpeed = 0.6f;
        [Tooltip("How far outside the zone's edge it circles (m): closer than its usual prowl.")]
        public Vector2 showdownStandoff = new Vector2(0.3f, 1.4f);
        [Tooltip("Circling, a step every this long (s): slow, heavy footfalls ...")]
        public float showdownStepInterval = 1.15f;
        [Tooltip("... this loud (share of the full) ...")]
        [Range(0f, 1f)] public float showdownStepLoudness = 0.95f;
        [Tooltip("... and pitched this far down (1 = as recorded): the weight of it.")]
        [Range(0.4f, 1f)] public float showdownStepPitch = 0.68f;
        [Tooltip("Looked at this long (s) after the valve is shut, and it screams.")]
        public float showdownLook = 0.5f;
        [Tooltip("Every sound fades out over this long (s) as it holds its breath.")]
        public float showdownSilence = 1.2f;
        [Tooltip("The flashlight is dead this long after the scream (s).")]
        public float showdownBlackout = 2.5f;
        [Tooltip("The blow to the picture with the scream: down to black this fast, held, and back (s).")]
        public Vector3 showdownFade = new Vector3(0.07f, 0.3f, 0.8f);
        [Range(0f, 1f)] public float showdownFadeDepth = 0.85f;
        [Tooltip("How hard the picture shakes with the scream (camera trauma).")]
        [Range(0f, 1f)] public float showdownShake = 0.7f;

        [Header("Hiding, waiting")]
        [Tooltip("Peeks out from behind the wall every so often (s) ...")]
        public Vector2 peekEvery = new Vector2(2.5f, 6.5f);
        [Tooltip("... for this long (s).")]
        public Vector2 peekTime = new Vector2(1.4f, 3f);
        [Tooltip("Gives up waiting after this long with the player still in the light, and comes prowling again (s).")]
        public Vector2 ambushPatience = new Vector2(20f, 35f);
        [Tooltip("The player this close to its hiding place and it goes for them (m).")]
        public float pounceDistance = 5f;
        [Tooltip("After the player steps out of the light, a beat before it moves (s).")]
        public Vector2 ambushReact = new Vector2(0.4f, 1.4f);

        [Header("Seen")]
        [Tooltip("Light at a point (intensity / distance², cone and range included) for it to count as lit.")]
        public float litThreshold = 0.3f;
        [Tooltip("Grace after the last sighting before it moves again (s).")]
        public float unseenDelay = 0.06f;

        [Header("Stamping")]
        public AudioClip[] steps;
        [Tooltip("Time between steps (s) creeping and charging.")]
        public Vector2 stepInterval = new Vector2(0.5f, 0.2f);
        [Tooltip("Loudness of a step right next to the player (1 = full).")]
        [Range(0f, 1f)] public float stepLoudness = 1f;
        [Tooltip("Once it has been stared down it creeps this quietly (share of the full loudness); charging stays loud.")]
        [Range(0f, 1f)] public float stealthLoudness = 0.3f;
        [Tooltip("Within this distance (m) a step is at full loudness ...")]
        public float stepNear = 2.5f;
        [Tooltip("... and fades away to nothing by this distance (m).")]
        public float stepFar = 30f;
        [Tooltip("How steeply the steps fade with distance: higher = quiet sooner, so where it is is easy to hear.")]
        [Range(0.5f, 3f)] public float stepFalloff = 1.2f;
        [Tooltip("Far away the steps also lose their top (muffled), close up they are sharp. Cutoff (Hz) far and near.")]
        public Vector2 stepMuffle = new Vector2(900f, 22000f);

        [Header("Breathing (while it stands still)")]
        [Tooltip("Single breaths, played one after another in a random order with uneven gaps while it stands still.")]
        public AudioClip[] breaths;
        [Tooltip("Pause after a breath before the next (s): shortest and longest.")]
        public Vector2 breathGap = new Vector2(0.15f, 1.1f);
        [Range(0f, 1f)] public float breathVolume = 0.35f;
        [Tooltip("Hiding, it breathes this quietly.")]
        [Range(0f, 1f)] public float hidingBreathVolume = 0.12f;
        [Tooltip("Heard within this distance (m).")]
        public float breathRange = 9f;
        [Tooltip("Within this distance (m) every step jolts the camera a little.")]
        public float joltRange = 6f;

        [Header("Fear (from looking at it)")]
        [Tooltip("The most fear looking at it can bring (0-1).")]
        [Range(0f, 1f)] public float lookFearMax = 0.95f;
        [Tooltip("Looked at this long (s), the fear is about two thirds of the way up; twice as long, nearly all of it.")]
        public float lookFearTime = 3f;
        [Tooltip("Looking away, the time looked at runs back at this rate (1 = as fast as it built up).")]
        public float lookFearEbb = 1.2f;
        [Tooltip("How near the middle of the screen (share of the screen from the centre) counts as looking at it.")]
        [Range(0.05f, 0.7f)] public float lookFearCone = 0.35f;
        [Tooltip("Its steps quicken from this far away (m) ...")]
        public float fearFar = 16f;
        [Tooltip("... to the quickest at this distance.")]
        public float fearNear = 1.5f;

        [Header("The end")]
        public float killDistance = 1.15f;
        [Tooltip("Its scream: stared down from a safe zone, when it snaps, when it is shot off.")]
        public AudioClip scream;
        [Tooltip("The scream in the player's face at the end (the screamer). Empty: the scream.")]
        public AudioClip killScream;
        public float screamTime = 1.1f;
        public float fadeTime = 0.6f;

        enum Phase { Waiting, Active, Killing, Dead }
        Phase phase = Phase.Waiting;

        /// <summary>What it is doing (with the NavMesh).</summary>
        enum Mode { Hunt, Approach, Prowl, Scream, Retreat, Ambush, Enraged, ValveWatch, Hush, Rampage }
        Mode mode = Mode.Hunt;
        enum Gait { Still, Creep, Charge }

        BodycamPlayerController player;
        BodycamCameraRig rig;
        BodycamFlashlight torch;
        Camera cam;
        Transform model;
        int sightMask;
        float bodySize = 1f, fullScale = 1f;

        // ---- animation
        PlayableGraph graph;
        AnimationMixerPlayable mixer;
        readonly List<AnimationClipPlayable> clipPlayables = new List<AnimationClipPlayable>();
        readonly List<int> freezeInputs = new List<int>();
        int iRun = -1, iWalk = -1, iCrawlRun = -1, iSearch = -1, iIdle = -1, iScreech = -1, iRampage = -1;
        float[] weights = new float[0];
        int[] poseInputs = new int[0], poseBlendInputs = new int[0];
        int lastPose = -1;
        float twistSign = 1f;
        readonly List<int> poseOrder = new List<int>();
        Transform[] samplePoints, feet, hands, spineBones, limbPoints;
        Transform head, neck, hips, leftShoulder, rightShoulder;
        Vector3 headFace, headUp;   // the head's own forward and up (from the model's pose), in head space
        float gaitPhase, idleTime, upright = 0.8f, chargeMix;
        float handDropUntil, nextHandDrop;
        float corridorK, doorK, leanK, leanSide;
        Quaternion twitch = Quaternion.identity, twitchTarget = Quaternion.identity;
        float nextTwitch;
        bool LegacyBody => !walkClip;

        // ---- body vs walls
        CapsuleCollider probe;
        ManthingBodyFit bodyFit;
        readonly Collider[] overlaps = new Collider[16];

        // ---- seeing
        readonly List<Light> lights = new List<Light>();
        List<Light> torchLights;
        float torchLightsRefresh;
        float lightsRefresh, sightCheckAt;
        bool seen, onScreen;
        float lastSeen = -10f, stare;

        // ---- the way
        NavMeshPath path, probePath;
        Vector3[] corners = new Vector3[0];
        int corner;
        float repathAt, navCheckAt;
        Vector3 pathGoal;
        bool navReady;

        // ---- state
        SafeZone zone;
        Vector3 goal;
        bool goalSet, charging, stealthy;
        float modeUntil, pauseUntil, peekAt, peekUntil, reactAt = -1f, ambushTall = 0.5f;
        bool peeking, atPeek, held;
        float swayTime;
        float aggression, enrageGo, staggerUntil, nextHowl, petrifyAt;
        int hits;
        bool fleeing, torchWas;
        float nextJerk, hitchUntil;
        bool bodyHeld;
        Quaternion headNow;
        float lookTime;
        float restUntil, stuckFor, stuckCheckAt;
        Vector3 stuckFrom;
        AudioClip petrifySound;
        Vector3 cover, peekSpot;
        readonly List<CoverFinder.Burned> burned = new List<CoverFinder.Burned>();
        float chaseTime;

        // ---- the showdown
        bool showdownDone, rushed, silenced;
        float lookedFor, listenerWas = 1f;
        Coroutine silenceFade;

        // ---- sound
        AudioSource stepSource, breathSource, rageSource;
        AudioLowPassFilter stepFilter;
        int lastStep = -1, lastBreath = -1;
        float stepTimer, breathLevel, breathWait;
        float fade;

        public bool IsSeen => seen;
        public bool IsActive => phase != Phase.Waiting;

        void Start()
        {
            player = FindAnyObjectByType<BodycamPlayerController>();
            rig = player ? player.GetComponentInChildren<BodycamCameraRig>() : null;
            torch = player ? player.GetComponentInChildren<BodycamFlashlight>(true) : null;
            if (!torch) torch = FindAnyObjectByType<BodycamFlashlight>();
            sightMask = ~((1 << 2) | (1 << 8) | (1 << 9) | (1 << 10));   // not the player / itself, hitboxes, the body, chains
            model = animator ? animator.transform : null;
            BuildGraph();

            var moan = gameObject.AddComponent<Audio.MonsterMoan>();   // rare, never on top of another monster's
            moan.mouth = head;
            moan.height = 2f;
            moan.canMoan = () => phase == Phase.Active && !stealthy && mode != Mode.Ambush && mode != Mode.Hush;

            // the steps: fully placed in the room, loud close up and falling away steeply, so it can be found by ear
            stepSource = gameObject.AddComponent<AudioSource>();
            stepSource.playOnAwake = false;
            stepSource.spatialBlend = 1f;
            stepSource.dopplerLevel = 0f;
            stepSource.spread = 30f;
            stepSource.rolloffMode = AudioRolloffMode.Custom;
            stepSource.minDistance = stepNear;
            stepSource.maxDistance = stepFar;
            var curve = new AnimationCurve();
            for (int k = 0; k <= 12; k++)
            {
                float t = k / 12f;                                           // 0 = at the source, 1 = stepFar
                float d = Mathf.Lerp(0f, stepFar, t);
                float v = d <= stepNear ? 1f : Mathf.Pow(stepNear / d, stepFalloff) * (1f - t * t);
                curve.AddKey(t, v);
            }
            stepSource.SetCustomCurve(AudioSourceCurveType.CustomRolloff, curve);
            stepFilter = gameObject.AddComponent<AudioLowPassFilter>();
            stepFilter.cutoffFrequency = stepMuffle.y;

            // breathing: single breaths from the head, only while it stands still
            if (breaths != null && breaths.Length > 0)
            {
                var breathGo = new GameObject("Breath");
                breathGo.transform.SetParent(head ? head : transform, false);
                breathSource = breathGo.AddComponent<AudioSource>();
                breathSource.loop = false;
                breathSource.playOnAwake = false;
                breathSource.spatialBlend = 1f;
                breathSource.dopplerLevel = 0f;
                breathSource.rolloffMode = AudioRolloffMode.Linear;
                breathSource.minDistance = 1.5f;
                breathSource.maxDistance = breathRange;
                breathSource.volume = 0f;
            }

            // the roar while it comes (looped) and the wail as it runs off: from the head, heard well across the floor
            if (enrageLoop || retreatWail)
            {
                var rageGo = new GameObject("RageRoar");
                rageGo.transform.SetParent(head ? head : transform, false);
                rageSource = rageGo.AddComponent<AudioSource>();
                rageSource.clip = enrageLoop;
                rageSource.loop = true;
                rageSource.playOnAwake = false;
                rageSource.spatialBlend = 1f;
                rageSource.dopplerLevel = 0f;
                rageSource.rolloffMode = AudioRolloffMode.Logarithmic;
                rageSource.minDistance = 3f;
                rageSource.maxDistance = 60f;
                rageSource.volume = 0f;
            }

            var pusher = gameObject.GetComponent<ChainPusher>() ?? gameObject.AddComponent<ChainPusher>();
            float size = bodySize = model ? model.lossyScale.y : 1f;   // the model may be scaled up
            fullScale = model ? model.localScale.x : 1f;
            pusher.radius = 0.4f * size; pusher.height = 2.2f * size;

            // the capsule along its back, for keeping it out of the walls (a trigger: it pushes nothing by itself)
            var probeGo = new GameObject("BodyProbe");
            probeGo.layer = 2;
            probeGo.transform.SetParent(transform, false);
            probe = probeGo.AddComponent<CapsuleCollider>();
            probe.isTrigger = true;
            probe.direction = 1;
            if (animator && fitBodyToRoom) bodyFit = new ManthingBodyFit(animator, transform, sightMask);
            BuildHitboxes();
            path = new NavMeshPath();
            probePath = new NavMeshPath();

            nextHandDrop = Time.time + Random.Range(handDropEvery.x, handDropEvery.y);
            upright = uprightShare;

            if (appearOn && !appearOn.HasBurst) { SetVisible(false); appearOn.Burst += Appear; }
            else if (appearOnCall) SetVisible(false);
            else Appear();
        }

        /// <summary>
        /// Something for bullets to hit (its root is out of every ray): capsules on the Hitbox layer along the back, the
        /// head, the arms and legs, riding on the bones, so they follow whatever pose it is in.
        /// </summary>
        void BuildHitboxes()
        {
            if (!animator) return;
            int layer = LayerMask.NameToLayer("Hitbox");
            if (layer < 0) return;
            Transform B(HumanBodyBones b) => animator.GetBoneTransform(b);
            void Capsule(Transform a, Transform b, float radius)
            {
                if (!a || !b) return;
                var go = new GameObject("Hitbox_" + a.name) { layer = layer };
                go.transform.SetParent(a, false);
                go.transform.rotation = Quaternion.FromToRotation(Vector3.up, b.position - a.position);
                float s = Mathf.Max(go.transform.lossyScale.x, 1e-3f);   // sizes given in world metres
                float len = Vector3.Distance(a.position, b.position) / s, r = radius / s;
                var c = go.AddComponent<CapsuleCollider>();
                c.direction = 1;
                c.radius = r;
                c.height = len + 2f * r;
                c.center = Vector3.up * (len * 0.5f);
            }
            float k = bodySize;   // the model's scale: the radii below are for it at its own size
            Capsule(B(HumanBodyBones.Hips), B(HumanBodyBones.UpperChest) ? B(HumanBodyBones.UpperChest) : B(HumanBodyBones.Chest), 0.16f * k);
            Capsule(B(HumanBodyBones.Neck), B(HumanBodyBones.Head), 0.08f * k);
            if (head)
            {
                var go = new GameObject("Hitbox_Head") { layer = layer };
                go.transform.SetParent(head, false);
                var c = go.AddComponent<SphereCollider>();
                c.radius = 0.12f * k / Mathf.Max(go.transform.lossyScale.x, 1e-3f);
            }
            foreach (string side in new[] { "Left", "Right" })
            {
                HumanBodyBones Bone(string n) => (HumanBodyBones)System.Enum.Parse(typeof(HumanBodyBones), side + n);
                Capsule(B(Bone("UpperArm")), B(Bone("LowerArm")), 0.06f * k);
                Capsule(B(Bone("LowerArm")), B(Bone("Hand")), 0.05f * k);
                Capsule(B(Bone("UpperLeg")), B(Bone("LowerLeg")), 0.08f * k);
                Capsule(B(Bone("LowerLeg")), B(Bone("Foot")), 0.06f * k);
            }
        }

        void OnEnable() => PlayerStress.Register(this);

        void OnDisable() => PlayerStress.Unregister(this);

        void OnDestroy()
        {
            EndSilence();   // never leaves the game muted (the scene reloading, say)
            if (appearOn) appearOn.Burst -= Appear;
            if (graph.IsValid()) graph.Destroy();
        }

        void SetVisible(bool on)
        {
            foreach (var r in GetComponentsInChildren<Renderer>(true)) r.enabled = on;
            foreach (var c in GetComponentsInChildren<Collider>(true)) c.enabled = on;
        }

        /// <summary>Shows it where it stands and starts the hunt (for scripted events: <see cref="appearOnCall"/>).</summary>
        public void AppearNow() => Appear();

        void Appear()
        {
            if (phase != Phase.Waiting) return;
            SetVisible(true);
            phase = Phase.Active;
            mode = Mode.Hunt;
            FacePlayer();
            if (LegacyBody) FreezeRandom();
            else FreezeInPose();   // it is there, standing, the moment the light goes
        }

        /// <summary>The poses made from the clips: hunched, dropping onto its hands, on all fours, crouched, peeking.</summary>
        public static FreezePose[] DefaultPoses(AnimationClip run, AnimationClip walk, AnimationClip crawlRun, AnimationClip search, AnimationClip idle)
        {
            const PoseFit low = PoseFit.Corridor | PoseFit.Doorway | PoseFit.LowCeiling;
            var list = new List<FreezePose>();
            void P(FreezePose p) { if (p.clip) list.Add(p); }
            P(new FreezePose("Lunge", run, 0.25f, PoseFit.Open));
            P(new FreezePose("Reach", run, 0.62f, PoseFit.Open));
            P(new FreezePose("Hunched stand", idle, 0.1f, PoseFit.Open));
            P(new FreezePose("Hand drop", run, 0.1f, PoseFit.Open | PoseFit.LowCeiling, walk, 0.1f, 0.7f));
            P(new FreezePose("Hand drop, other hand", run, 0.55f, PoseFit.Open | PoseFit.LowCeiling, walk, 0.55f, 0.65f));
            P(new FreezePose("Crawl", walk, 0f, PoseFit.Open | low));
            P(new FreezePose("Crawl, mid-step", walk, 0.5f, low));
            P(new FreezePose("Crawl, squeezing", walk, 0.25f, PoseFit.Doorway | PoseFit.Corridor, twist: 35f));
            P(new FreezePose("Crouched, watching", search, 0.6f, PoseFit.Open | low));
            P(new FreezePose("Gallop", crawlRun, 0.25f, PoseFit.Open | PoseFit.Corridor));
            P(new FreezePose("Peek, tall", idle, 0.3f, PoseFit.Peek, lean: 32f));
            P(new FreezePose("Peek, low", search, 0.6f, PoseFit.Peek, lean: 28f));
            return list.ToArray();
        }

        // ================================================================== animation

        void BuildGraph()
        {
            if (!animator) return;
            // the head's face direction, read off the model's own pose (it looks ahead) before any clip moves it
            head = animator.GetBoneTransform(HumanBodyBones.Head);
            neck = animator.GetBoneTransform(HumanBodyBones.Neck);
            hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            leftShoulder = animator.GetBoneTransform(HumanBodyBones.LeftShoulder);
            rightShoulder = animator.GetBoneTransform(HumanBodyBones.RightShoulder);
            if (head)
            {
                headFace = Quaternion.Inverse(head.rotation) * animator.transform.forward;
                headUp = Quaternion.Inverse(head.rotation) * animator.transform.up;
            }
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            graph = PlayableGraph.Create("Manthing");
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            var clips = new List<AnimationClip>();
            int Add(AnimationClip c) { if (!c) return -1; int i = clips.IndexOf(c); if (i >= 0) return i; clips.Add(c); return clips.Count - 1; }
            iRun = Add(runClip);
            iWalk = Add(walkClip);
            iCrawlRun = Add(crawlRunClip);
            iSearch = Add(searchClip);
            iIdle = Add(idleClip);
            iScreech = Add(screechClip);
            iRampage = Add(rampageWalkClip);
            if (freezeClips != null) foreach (var c in freezeClips) { int i = Add(c); if (i >= 0) freezeInputs.Add(i); }
            if ((poses == null || poses.Length == 0) && walkClip) poses = DefaultPoses(runClip, walkClip, crawlRunClip, searchClip, idleClip);
            if (poses != null)
            {
                poseInputs = new int[poses.Length];
                poseBlendInputs = new int[poses.Length];
                for (int i = 0; i < poses.Length; i++)
                {
                    poseInputs[i] = poses[i] != null ? Add(poses[i].clip) : -1;
                    poseBlendInputs[i] = poses[i] != null ? Add(poses[i].blendClip) : -1;
                }
            }
            mixer = AnimationMixerPlayable.Create(graph, clips.Count);
            weights = new float[clips.Count];
            for (int i = 0; i < clips.Count; i++)
            {
                var p = AnimationClipPlayable.Create(graph, clips[i]);
                p.SetApplyFootIK(false);
                p.Pause();
                graph.Connect(p, 0, mixer, i);
                clipPlayables.Add(p);
            }
            AnimationPlayableOutput.Create(graph, "Body", animator).SetSourcePlayable(mixer);
            graph.Play();

            Transform[] Bones(params HumanBodyBones[] list)
            {
                var r = new List<Transform>();
                foreach (var b in list) { var t = animator.GetBoneTransform(b); if (t) r.Add(t); }
                return r.ToArray();
            }
            samplePoints = Bones(HumanBodyBones.Head, HumanBodyBones.Chest, HumanBodyBones.Hips, HumanBodyBones.LeftHand, HumanBodyBones.RightHand,
                                 HumanBodyBones.LeftLowerLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot);
            feet = Bones(HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot, HumanBodyBones.LeftToes, HumanBodyBones.RightToes);
            hands = Bones(HumanBodyBones.LeftHand, HumanBodyBones.RightHand);
            spineBones = Bones(HumanBodyBones.Spine, HumanBodyBones.Chest, HumanBodyBones.UpperChest);
            limbPoints = Bones(HumanBodyBones.LeftHand, HumanBodyBones.RightHand, HumanBodyBones.LeftLowerArm, HumanBodyBones.RightLowerArm, HumanBodyBones.Head,
                               HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot);
        }

        // ---- the mixer: a few clips at a time, each at its own moment

        void ClearPose() { for (int i = 0; i < weights.Length; i++) weights[i] = 0f; }

        void AddPose(int input, float weight, double time)
        {
            if (input < 0 || weight <= 0f) return;
            weights[input] += weight;
            clipPlayables[input].SetTime(time);
        }

        double At(int input, float phase01) => input < 0 ? 0.0 : phase01 * clipPlayables[input].GetAnimationClip().length;

        double Loop(int input, float time) => input < 0 ? 0.0 : time % clipPlayables[input].GetAnimationClip().length;

        void ApplyPose()
        {
            if (!graph.IsValid()) return;
            float sum = 0f;
            foreach (float w in weights) sum += w;
            if (sum <= 0f) return;
            for (int i = 0; i < weights.Length; i++) mixer.SetInputWeight(i, weights[i] / sum);
            graph.Evaluate(0f);
        }

        void Show(int input, double time)
        {
            if (input < 0) return;
            ClearPose();
            AddPose(input, 1f, time);
            ApplyPose();
            Ground();
        }

        /// <summary>On the move: the hunched run and the crawl (or the gallop) on all fours, mixed by how upright it is now.</summary>
        void PoseMove(Gait gait, float speed, float dt)
        {
            if (LegacyBody)
            {
                if (iRun < 0) return;
                gaitPhase = (gaitPhase + dt * Mathf.Lerp(0.9f, 1.4f, Mathf.InverseLerp(2f, 5.2f, speed)) / runClip.length) % 1f;
                Show(iRun, At(iRun, gaitPhase));
                return;
            }
            bodyHeld = false;
            if (mode == Mode.Rampage && iRampage >= 0)
            {
                // its own run, start to end, its pace lurching a little
                float wobble = 1f + rampagePaceWobble * (Mathf.PerlinNoise(Time.time * 2.3f, 3.7f) * 2f - 1f);
                gaitPhase = (gaitPhase + dt * speed * wobble / Mathf.Max(rampageStride * bodySize, 0.1f)) % 1f;
                ClearPose();
                AddPose(iRampage, 1f, At(iRampage, gaitPhase));
                ApplyPose();
                Procedural(dt, true);
                Ground();
                return;
            }
            float stride = Mathf.Lerp(strides.x, strides.y, chargeMix) * bodySize;
            float advance = dt * speed / Mathf.Max(stride, 0.1f);
            if (mode == Mode.Enraged) advance = Frantic(advance, dt);
            gaitPhase = (gaitPhase + advance) % 1f;
            chargeMix = Mathf.MoveTowards(chargeMix, gait == Gait.Charge ? 1f : 0f, dt * 2.5f);
            int low = iCrawlRun >= 0 ? iCrawlRun : iWalk;
            ClearPose();
            AddPose(iRun, upright, At(iRun, gaitPhase));
            AddPose(iWalk, (1f - upright) * (1f - chargeMix), At(iWalk, gaitPhase));
            AddPose(low, (1f - upright) * chargeMix, At(low, gaitPhase));
            ApplyPose();
            Procedural(dt, true);
            Ground();
        }

        /// <summary>Standing: crouched on all fours, looking about, or standing hunched; <paramref name="tall"/> mixes them.</summary>
        void PoseStill(float dt, float tall)
        {
            idleTime += dt;
            if (LegacyBody)
            {
                if (iIdle >= 0) Show(iIdle, Loop(iIdle, idleTime));
                else if (freezeInputs.Count > 0) Show(freezeInputs[0], 0.0);
                return;
            }
            ClearPose();
            AddPose(iIdle >= 0 ? iIdle : iRun, tall, iIdle >= 0 ? Loop(iIdle, idleTime) : 0.0);
            AddPose(iSearch >= 0 ? iSearch : iWalk, 1f - tall, iSearch >= 0 ? Loop(iSearch, idleTime) : 0.0);
            ApplyPose();
            Procedural(dt, false);
            Ground();
        }

        /// <summary>
        /// How upright it goes: hunched in the open, dropping onto its hands every so often (quick down, slower up, so it
        /// lurches), on all fours in a corridor or a doorway, and never taller than the ceiling over it lets it.
        /// </summary>
        /// <summary>
        /// Enraged: the gallop on all fours played in fits, as if frames were missing: the pace lurching faster and slower,
        /// now and then a jump ahead in the stride, now and then a hitch where it hangs for a moment.
        /// </summary>
        float Frantic(float advance, float dt)
        {
            if (Time.time < hitchUntil) return 0f;                                     // hung for a beat
            if (Time.time >= nextJerk)
            {
                nextJerk = Time.time + Random.Range(jerkEvery.x, jerkEvery.y);
                if (Random.value < 0.4f) hitchUntil = Time.time + Random.Range(0.05f, 0.12f);
                else return advance + Random.Range(0.08f, 0.22f);                        // a jump in the stride
            }
            return advance * Mathf.Lerp(0.5f, 1.9f, Mathf.PerlinNoise(Time.time * 3.1f, 7.3f));
        }

        void UpdateUpright(float dt, bool moving, float ceilingRoom)
        {
            float want = uprightShare;
            bool dropping = false;
            if (mode == Mode.Enraged) { want = 0f; moving = false; }   // on all fours, flat out
            if (mode == Mode.Rampage) moving = false;                  // its own walk: no dropping onto its hands
            if (moving)
            {
                if (Time.time >= nextHandDrop)
                {
                    handDropUntil = Time.time + Random.Range(handDropTime.x, handDropTime.y);
                    nextHandDrop = handDropUntil + Random.Range(handDropEvery.x, handDropEvery.y);
                    HandSlap();
                }
                dropping = Time.time < handDropUntil;
                if (dropping) want = 0.08f;
            }
            want = Mathf.Min(want, 1f - Mathf.Max(corridorK, doorK));
            // the ceiling: upright needs headHeights.x, on all fours headHeights.y (times its size)
            float scale = model ? model.localScale.x : 1f;
            float needUp = headHeights.x * scale + 0.25f, needLow = headHeights.y * scale + 0.25f;
            want = Mathf.Min(want, Mathf.Clamp01(Mathf.InverseLerp(needLow, needUp, ceilingRoom)));
            upright = Mathf.MoveTowards(upright, want, dt * (want < upright ? (dropping ? 5f : 3f) : 1.6f));
        }

        /// <summary>
        /// Bones bent over the clips: shoulders drawn in and head low in a corridor, shoulders turned into a doorway,
        /// a lean out from behind a corner, and the head jerking to odd tilts while it moves.
        /// </summary>
        void Procedural(float dt, bool moving)
        {
            if (!model) return;
            Vector3 fwd = transform.forward, right = transform.right;
            // a doorway: the shoulders turn into the gap (round the length of the back: up when upright, ahead on all fours),
            // one shoulder high, one low, spread down the spine
            if (doorK > 0.01f && spineBones.Length > 0)
            {
                Vector3 along = Vector3.Slerp(fwd, Vector3.up, upright);
                float each = doorTwist * doorK / spineBones.Length;
                foreach (Transform b in spineBones) b.rotation = Quaternion.AngleAxis(each, along) * b.rotation;
            }
            // a corridor: shoulders drawn up and in, as if squeezing between the walls
            if (corridorK > 0.01f)
            {
                if (leftShoulder) leftShoulder.rotation = Quaternion.AngleAxis(-14f * corridorK, fwd) * Quaternion.AngleAxis(-10f * corridorK, Vector3.up) * leftShoulder.rotation;
                if (rightShoulder) rightShoulder.rotation = Quaternion.AngleAxis(14f * corridorK, fwd) * Quaternion.AngleAxis(10f * corridorK, Vector3.up) * rightShoulder.rotation;
                if (neck) neck.rotation = Quaternion.AngleAxis(12f * corridorK, right) * neck.rotation;
            }
            // peeking: the whole back leans out to one side (side +1 = its right)
            if (leanK > 0.01f && spineBones.Length > 0)
            {
                float each = -peekLean * leanK * leanSide / spineBones.Length;
                foreach (Transform b in spineBones) b.rotation = Quaternion.AngleAxis(each, fwd) * b.rotation;
                if (neck) neck.rotation = Quaternion.AngleAxis(peekLean * 0.5f * leanK * leanSide, fwd) * neck.rotation;   // head back level-ish
            }
            // the head: snaps to a new crooked tilt every so often while it moves
            if (head)
            {
                if (moving && Time.time >= nextTwitch)
                {
                    nextTwitch = Time.time + (Frenzied ? Random.Range(0.12f, 0.45f) : Random.Range(twitchEvery.x, twitchEvery.y));
                    twitchTarget = Quaternion.Euler(Random.Range(-0.4f, 0.4f) * twitchAngle, Random.Range(-0.6f, 0.6f) * twitchAngle,
                                                    Random.Range(-1f, 1f) * twitchAngle);
                }
                if (!moving) twitchTarget = Quaternion.identity;
                float snap = Frenzied ? 900f : moving ? twitchSpeed : twitchSpeed * 0.5f;   // enraged: the head snaps about
                twitch = Quaternion.RotateTowards(twitch, twitchTarget, dt * snap);
                Quaternion frame = Quaternion.LookRotation(head.rotation * headFace, head.rotation * headUp);
                head.rotation = frame * twitch * Quaternion.Inverse(frame) * head.rotation;
            }
        }

        /// <summary>Feet on the floor whatever the pose (the model moves up or down under the body), and no hand under it.</summary>
        void Ground()
        {
            if (!model || feet == null || feet.Length == 0) return;
            float low = float.MaxValue;
            foreach (var t in feet) low = Mathf.Min(low, t.position.y);
            float floor = transform.position.y;
            float lift = floor + soleHeight - low;
            if (hands != null)
                foreach (var t in hands) lift = Mathf.Max(lift, floor + soleHeight * 1.5f - t.position.y);
            // the clips keep the hips where their own animation had them, off to the side or ahead by a metre or more:
            // the body goes back over its root (the point the NavMesh keeps clear of the walls)
            // (the middle of the back, so a long body on all fours sticks out as little in front as behind)
            Vector3 centre = hips ? hips.position : model.position;
            if (hips && spineBones != null && spineBones.Length > 0) centre = (hips.position + spineBones[spineBones.Length - 1].position) * 0.5f;
            model.position += Vector3.up * lift + Flat(transform.position - centre);
        }

        /// <summary>
        /// Caught by the light: stands in one of its poses, one that suits where it is (a corridor, a door frame, under a
        /// low ceiling, peeking round a corner, the open) and that fits in there best (the least of it in the walls),
        /// a different one from last time if it can. Its head turns to the player.
        /// </summary>
        void FreezeInPose()
        {
            if (poses == null || poses.Length == 0 || !graph.IsValid()) { LookAtPlayer(); return; }
            PoseFit where = Surroundings();
            poseOrder.Clear();
            for (int i = 0; i < poses.Length; i++)
                if (poses[i] != null && poseInputs[i] >= 0 && (poses[i].fit & where) != 0) poseOrder.Add(i);
            if (poseOrder.Count == 0)
                for (int i = 0; i < poses.Length; i++) if (poses[i] != null && poseInputs[i] >= 0) poseOrder.Add(i);
            if (poseOrder.Count == 0) { LookAtPlayer(); return; }
            for (int i = poseOrder.Count - 1; i > 0; i--) { int j = Random.Range(0, i + 1); (poseOrder[i], poseOrder[j]) = (poseOrder[j], poseOrder[i]); }
            if (poseOrder.Count > 1 && poseOrder[0] == lastPose) { poseOrder.RemoveAt(0); poseOrder.Add(lastPose); }

            if (where.HasFlag(PoseFit.Peek)) FaceTowards(player.transform.position, 1f, 1000f);
            twistSign = Random.value < 0.5f ? 1f : -1f;
            int best = poseOrder[0];
            float bestOverlap = float.MaxValue;
            // judged as it will stand: pushed out of the walls and fitted into the room
            Vector3 at = transform.position;
            int applied = -1;
            for (int k = 0; k < poseOrder.Count && k < 6; k++)
            {
                transform.position = at;
                ApplyFreezePose(applied = poseOrder[k]);
                ResolveBody();
                FitBody();
                float overlap = PoseOverlap();
                if (overlap < bestOverlap) { bestOverlap = overlap; best = poseOrder[k]; }
                if (overlap < 0.01f) break;
            }
            if (applied != best)
            {
                transform.position = at;
                ApplyFreezePose(best);
                ResolveBody();
                FitBody();
            }
            lastPose = best;
            LookAtPlayer();
            FitBody();   // the turned neck may have put the head into the wall
        }

        /// <summary>One pose, evaluated onto the body: its clip moment (mixed with the second one), the lean or twist, feet on the floor.</summary>
        void ApplyFreezePose(int index)
        {
            FreezePose p = poses[index];
            ClearPose();
            AddPose(poseInputs[index], 1f - p.blend, At(poseInputs[index], p.time));
            AddPose(poseBlendInputs[index], p.blend, At(poseBlendInputs[index], p.blendTime));
            ApplyPose();
            Vector3 fwd = transform.forward;
            if (Mathf.Abs(p.twist) > 0.01f && spineBones.Length > 0)
            {
                float each = p.twist / spineBones.Length * twistSign;
                foreach (Transform b in spineBones) b.rotation = Quaternion.AngleAxis(each, fwd) * b.rotation;
            }
            if (Mathf.Abs(p.lean) > 0.01f && spineBones.Length > 0)
            {
                float side = leanSide != 0f ? leanSide : twistSign;
                float each = -p.lean * side / spineBones.Length;
                foreach (Transform b in spineBones) b.rotation = Quaternion.AngleAxis(each, fwd) * b.rotation;
                if (neck) neck.rotation = Quaternion.AngleAxis(p.lean * 0.5f * side, fwd) * neck.rotation;
            }
            twitch = twitchTarget = Quaternion.identity;
            Ground();
        }

        /// <summary>What sort of place it stands in, for picking a pose.</summary>
        PoseFit Surroundings()
        {
            if (mode == Mode.Ambush && atPeek) return PoseFit.Peek;
            PoseFit f = 0;
            Vector3 chest = transform.position + Vector3.up * Mathf.Min(1.1f, headHeights.y * bodySize);
            float L = Physics.Raycast(chest, -transform.right, out RaycastHit hl, 3f, sightMask, QueryTriggerInteraction.Ignore) ? hl.distance : 3f;
            float R = Physics.Raycast(chest, transform.right, out RaycastHit hr, 3f, sightMask, QueryTriggerInteraction.Ignore) ? hr.distance : 3f;
            float width = L + R;
            if (Doorway.At(transform.position, 0.4f * bodySize) || width < doorWidth) f |= PoseFit.Doorway;
            else if (width < corridorWidth) f |= PoseFit.Corridor;
            float scale = model ? model.localScale.x : 1f;
            if (CeilingRoom() < headHeights.x * scale + 0.25f) f |= PoseFit.LowCeiling;
            return f == 0 ? PoseFit.Open : f;
        }

        /// <summary>
        /// How badly the posed body is in the walls: how deep its back (hips to head) is in anything solid, plus a share for
        /// every hand, forearm or the head inside something, plus the head over the ceiling.
        /// </summary>
        float PoseOverlap()
        {
            float total = 0f;
            if (probe && hips && head)
            {
                float r = bodyRadius * (model ? model.lossyScale.y : bodySize);
                Vector3 a = hips.position + Vector3.up * r, b = head.position;
                Vector3 axis = b - a;
                float len = axis.magnitude;
                if (len > 1e-3f)
                {
                    probe.transform.position = (a + b) * 0.5f;
                    probe.transform.rotation = Quaternion.FromToRotation(Vector3.up, axis / len);
                    probe.radius = r; probe.height = len + 2f * r; probe.center = Vector3.zero;
                    int n = Physics.OverlapCapsuleNonAlloc(a, b, r, overlaps, sightMask, QueryTriggerInteraction.Ignore);
                    for (int i = 0; i < n; i++)
                    {
                        Collider c = overlaps[i];
                        if (!c || c.transform.IsChildOf(transform)) continue;
                        if (Physics.ComputePenetration(probe, probe.transform.position, probe.transform.rotation,
                                                       c, c.transform.position, c.transform.rotation, out _, out float depth))
                            total += depth;
                    }
                }
            }
            if (limbPoints != null)
                foreach (Transform t in limbPoints)
                    if (t && Physics.CheckSphere(t.position, 0.02f, sightMask, QueryTriggerInteraction.Ignore))   // in it, not resting on it
                        total += 0.15f;
            if (head)
            {
                float room = CeilingRoom();
                float top = head.position.y + 0.18f * (model ? model.lossyScale.y : bodySize) - transform.position.y;
                if (top > room) total += top - room;
            }
            return total;
        }

        /// <summary>Old set-up only: stops dead in a random moment of a random pose.</summary>
        void FreezeRandom()
        {
            if (freezeInputs.Count == 0) return;
            int input = freezeInputs[Random.Range(0, freezeInputs.Count)];
            Show(input, Random.value * clipPlayables[input].GetAnimationClip().length);
            LookAtPlayer();
        }

        /// <summary>
        /// Turns the neck and head so the face points at the player's eyes, once: the pose is not evaluated again
        /// while it stands frozen, so the head holds where it was turned however the player moves.
        /// </summary>
        void LookAtPlayer()
        {
            if (!headFollowsPlayer || !head) return;
            if (!cam) cam = Camera.main;
            Vector3 eye = cam ? cam.transform.position : player ? player.transform.position + Vector3.up * 1.6f : head.position + transform.forward;
            Vector3 want = eye - head.position;
            if (want.sqrMagnitude < 1e-4f) return;
            want.Normalize();
            if (neck && neckShare > 0f)
            {
                Vector3 face = head.rotation * headFace;
                neck.rotation = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(face, want), neckShare) * neck.rotation;
            }
            // the head the rest of the way, kept upright as far as the turn allows
            Vector3 up = Vector3.ProjectOnPlane(Vector3.Lerp(head.rotation * headUp, Vector3.up, 0.6f), want);
            if (up.sqrMagnitude < 1e-4f) up = transform.up;
            head.rotation = Quaternion.LookRotation(want, up) * Quaternion.Inverse(Quaternion.LookRotation(headFace, headUp));
        }

        // ================================================================== per frame

        void Update()
        {
            if (phase == Phase.Waiting || !player) return;
            if (!cam) cam = Camera.main;
            float dt = Time.deltaTime;
            if (dt <= 0f || phase == Phase.Killing || phase == Phase.Dead) return;
            Roar(dt);
            CheckShowdown();

            // screaming, bolting, enraged, holding its breath for the showdown: the light does not hold it
            bool scripted = mode == Mode.Scream || mode == Mode.Retreat || mode == Mode.Enraged || mode == Mode.Hush || mode == Mode.Rampage;
            bool inDoorway = dashThroughDoorways && NavReady() && Doorway.At(transform.position);
            bool seenNow = !scripted && !inDoorway && CheckSeen();
            if (seenNow)
            {
                if (!seen) OnCaught();
                lastSeen = Time.time;
            }
            seen = !scripted && (seenNow || (!inDoorway && Time.time - lastSeen < unseenDelay));
            bool torchOn = seen && !LegacyBody && TorchOn(false);
            if (torchOn && !torchWas && Time.time >= petrifyAt) Petrify();   // the flashlight finds it: it turns to stone, audibly
            torchWas = torchOn;
            UpdateLookTime(dt);
            UpdateStare(dt);
            if (UpdateAggression(dt, torchOn)) return;
            Breathe(dt);
            if (seen)
            {
                // the one time it is seen alive: at the edge of the light, close to the player, it stands and stares,
                // its head slowly rolling, in the dim chemical light; the flashlight on it and it is stone, like anywhere else
                if (Animated && !LegacyBody)
                {
                    if (torchOn) held = true;   // stone, head and all, just as it was
                    else { held = false; HoldAndWatch(dt); }
                }
                return;
            }
            held = false;

            if (!NavReady()) { Direct(dt); return; }
            Tick(dt);
        }

        /// <summary>Played as a living thing only right by the player at a safe zone's edge; everywhere else it is a sound in the dark.</summary>
        bool Animated => LegacyBody || mode == Mode.Prowl || mode == Mode.Enraged || mode == Mode.ValveWatch || mode == Mode.Hush || mode == Mode.Rampage;

        /// <summary>Enraged or on its rampage: the head snaps about as it comes.</summary>
        bool Frenzied => mode == Mode.Enraged || mode == Mode.Rampage;

        /// <summary>The stone crackle on its body, the moment the flashlight catches it.</summary>
        void Petrify()
        {
            petrifyAt = Time.time + 0.6f;
            if (!petrifySound) petrifySound = petrifyClip ? petrifyClip : ProceduralAudio.Petrify(Random.Range(0, 1000));
            Vector3 at = spineBones != null && spineBones.Length > 0 ? spineBones[spineBones.Length - 1].position : transform.position + Vector3.up;
            Audio.SpatialSound.PlayAt(petrifySound, at, petrifyVolume, 2f, 25f);
        }

        /// <summary>
        /// The flashlight held on it with the player out in the open fills its rage; full, it snaps (see <see cref="BeginEnrage"/>).
        /// Returns true when it just snapped (nothing else this frame).
        /// </summary>
        bool UpdateAggression(float dt, bool torchOn)
        {
            if (LegacyBody || !NavReady() || (mode != Mode.Hunt && mode != Mode.Ambush) || SafeZone.At(player.transform.position))
            {
                aggression = Mathf.Max(0f, aggression - dt);
                return false;
            }
            aggression = torchOn ? aggression + dt : Mathf.Max(0f, aggression - dt * 0.5f);
            if (aggression < aggressionTime) return false;
            BeginEnrage();
            return true;
        }

        /// <summary>Caught in the light: a statue in one of its poses (or, at the edge of the light, it just looks up at them).</summary>
        void OnCaught()
        {
            if (LegacyBody) FreezeRandom();
            else if (Animated) LookAtPlayer();
            else FreezeInPose();
        }

        void Tick(float dt)
        {
            Vector3 playerPos = player.transform.position;
            float dist = Flat(playerPos - transform.position).magnitude;
            SafeZone inZone = SafeZone.At(playerPos);

            switch (mode)
            {
                case Mode.Hunt:
                {
                    if (inZone) { BeginApproach(inZone); break; }
                    if (dist <= killDistance) { StartCoroutine(Kill()); return; }
                    chaseTime += dt;
                    charging = charging ? dist < runDistance.y : dist < runDistance.x;
                    float speed = charging ? runSpeed : stalkSpeed;
                    Travel(playerPos, speed, charging ? Gait.Charge : Gait.Creep, dt);
                    if (!heldBack) Stamp(dt, dist, charging ? 1f : stealthy ? stealthLoudness : 0.75f, charging);   // waiting behind the corner: not a sound
                    break;
                }
                case Mode.Approach:
                {
                    if (!inZone) { BeginHunt(); break; }
                    if (inZone != zone) { BeginApproach(inZone); break; }
                    if (!goalSet && !PickProwlSpot(true)) { BeginProwl(); break; }
                    float left = Travel(goal, stalkSpeed, Gait.Creep, dt);
                    if (!heldBack) Stamp(dt, dist, stealthy ? stealthLoudness : 0.75f, false);
                    if (left < 0.25f) BeginProwl();
                    break;
                }
                case Mode.Prowl:
                {
                    if (!inZone) { BeginHunt(); break; }
                    if (inZone != zone) { BeginApproach(inZone); break; }
                    // round the light, close to the player: only while they are not looking its way, lit or not
                    bool watched = OnScreen();
                    if (Time.time < pauseUntil || watched || !goalSet)
                    {
                        if (watched) HoldAndWatch(dt);   // looked at: it stops dead as it was, only the head comes round
                        else StandAndStare(dt, 0.75f);
                        if (!watched && Time.time >= pauseUntil && !goalSet && !PickProwlSpot(false))
                            pauseUntil = Time.time + 0.5f;   // nowhere to go just now: keep standing
                        break;
                    }
                    float left = Travel(goal, prowlSpeed, Gait.Creep, dt);
                    Stamp(dt, dist, (stealthy ? stealthLoudness : 0.6f) * 0.8f, false);
                    if (left < 0.25f) { goalSet = false; pauseUntil = Time.time + Random.Range(prowlPause.x, prowlPause.y); }
                    break;
                }
                case Mode.Scream:
                {
                    // standing its ground, still as it was, screaming at them; then the flashlight blinks and dies
                    if (Time.time >= modeUntil) BeginRetreat();
                    break;
                }
                case Mode.Retreat:
                {
                    // it goes only once the light is dead (after the blinks)
                    if (torch && torch.InBlackout && !torch.IsDead && Time.time < modeUntil - 8f) break;
                    float left = Travel(goal, retreatSpeed, Gait.Charge, dt);
                    Stamp(dt, dist, 1f, true);   // heard all the way: the player cannot see it go
                    if (left < 0.3f || Time.time >= modeUntil)
                    {
                        // out of time on the way (out of sight): it is there anyway
                        if (left >= 0.3f && !OnScreen()) transform.position = NavPoint(goal);
                        BeginAmbush();
                    }
                    break;
                }
                case Mode.Ambush:
                    Ambush(dt, inZone, dist);
                    break;
                case Mode.ValveWatch:
                    ValveWatch(dt, inZone, dist);
                    break;
                case Mode.Hush:
                {
                    // out of the light before it is over: it snaps and comes, as it would at anyone out there
                    if (!inZone) { EndSilence(); showdownDone = true; BeginEnrage(); break; }
                    HoldAndWatch(dt);   // not a breath, not a step: only the head comes round to them
                    lookedFor = InEyes() ? lookedFor + dt : Mathf.Max(0f, lookedFor - dt);
                    if (lookedFor >= showdownLook) Showdown();
                    break;
                }
                case Mode.Enraged:
                {
                    if (inZone || Time.time >= modeUntil)
                    {
                        // into the light, or it took too long: it breaks off, and the flashlight comes back once it is gone
                        BeginRetreat(false);
                        if (torch) torch.EndBlackoutIn(repelBlackout, true);
                        break;
                    }
                    Howl();
                    if (Time.time < enrageGo)
                    {
                        FaceTowards(playerPos, dt, 1f);   // screaming at them
                        break;
                    }
                    // all the way to them: shot enough on the way, it breaks off right in front of them; else the end
                    if (hits >= Mathf.Max(1, hitsToSpare) && dist <= killDistance + breakOffMargin) { BreakOff(); break; }
                    if (dist <= killDistance) { StartCoroutine(Kill()); return; }
                    charging = true;
                    Travel(playerPos, enrageSpeed, Gait.Charge, dt);
                    Stamp(dt, dist, 1f, true);
                    break;
                }
                case Mode.Rampage:
                {
                    // the light, the flashlight, the safe zones: nothing holds it now; a shot makes it blink away
                    if (blinking) break;   // screaming, the picture going black: it stands where it was hit
                    if (dist <= killDistance) { StartCoroutine(Kill()); return; }
                    RampageScream();
                    if (Time.time < staggerUntil) { FaceTowards(playerPos, dt, 1f); break; }
                    charging = true;
                    Travel(playerPos, rampageSpeed, Gait.Charge, dt);
                    Stamp(dt, dist, 1f, true);
                    break;
                }
            }
        }

        /// <summary>
        /// Gone from the level (the generator started): not shown, not heard, not hunting, until it is brought back by
        /// <see cref="AttackFrom"/>.
        /// </summary>
        public void Vanish()
        {
            if (phase == Phase.Killing || phase == Phase.Dead) return;
            if (mode == Mode.Hush) EndSilence();
            if (mode == Mode.Enraged && torch) torch.EndBlackoutIn(0.3f, true);
            StopAllCoroutines();
            fade = 0f;
            blinking = false;
            phase = Phase.Waiting;
            mode = Mode.Hunt;
            seen = held = peeking = atPeek = goalSet = fleeing = charging = false;
            stare = aggression = lookTime = 0f;
            SetVisible(false);
            if (breathSource) breathSource.Stop();
            if (rageSource) { rageSource.Stop(); rageSource.volume = 0f; }
        }

        /// <summary>
        /// Back from nowhere at <paramref name="from"/> (the far side of the hall), screaming, and straight into its rampage:
        /// it runs at the player and nothing but a shot stops it, for a beat.
        /// </summary>
        public void AttackFrom(Vector3 from)
        {
            if (phase == Phase.Killing || phase == Phase.Dead) return;
            if (phase != Phase.Waiting) Vanish();
            transform.position = NavReady() ? NavPoint(from) : from;
            FacePlayer();
            Appear();
            BeginRampage();
        }

        /// <summary>
        /// On its rampage: it runs straight at the player, the run of <see cref="rampageWalkClip"/>, screaming, and neither
        /// the flashlight nor a safe zone stops it.
        /// </summary>
        public void BeginRampage()
        {
            if (phase == Phase.Killing || phase == Phase.Dead || mode == Mode.Rampage) return;
            if (phase == Phase.Waiting) Appear();
            if (mode == Mode.Hush) EndSilence();
            if (mode == Mode.Enraged && torch) torch.EndBlackoutIn(0.3f, true);   // the light comes back; it no longer matters
            mode = Mode.Rampage;
            showdownDone = true;
            held = peeking = atPeek = goalSet = fleeing = charging = false;
            leanK = 0f;
            stare = aggression = 0f;
            staggerUntil = 0f;
            gaitPhase = 0f;
            repathAt = 0f;
            hits = 0;
            blinking = false;
            enrageGo = 0f;          // the roar comes up at once
            nextRampageScream = 0f; // and the first scream with it
            if (breathSource) breathSource.Stop();
            if (rig) rig.AddTrauma(0.4f);
        }

        float nextRampageScream;
        bool blinking;

        /// <summary>
        /// Shot on its rampage: a deafening scream, the picture hit hard, and a blink. While the picture is black it is
        /// put somewhere else, far off and out of the player's sight, and comes on from there. The last hit: the blink,
        /// and it falls dead where it stood, with a long moan.
        /// </summary>
        IEnumerator RampageHit(bool last)
        {
            blinking = true;
            Vector3 mouth = head ? head.position : transform.position + Vector3.up * 2f;
            AudioClip clip = rampageHitScream ? rampageHitScream : enrageScream ? enrageScream : scream;
            if (clip)
            {
                // deafening: full loudness out to a long way, in the ears as much as in the room
                Audio.SpatialSound.PlayAt(clip, mouth, rampageHitScreamVolume, 30f, 120f);
                nextRampageScream = Time.time + clip.length + Random.Range(rampageScreamEvery.x, rampageScreamEvery.y);
            }
            if (rig) { rig.AddTrauma(rampageHitShake); rig.AddTremor(1f); }

            // the blink: down to black, held, back
            for (float t = 0f; t < rampageBlink.x; t += Time.deltaTime)
            {
                fade = t / Mathf.Max(rampageBlink.x, 0.01f);
                yield return null;
            }
            fade = 1f;
            if (last)
            {
                yield return new WaitForSeconds(rampageBlink.y);
                StartCoroutine(Die());
            }
            else
            {
                // somewhere else while the picture is black: far, and where the player cannot see it
                if (NavReady() && PickBlinkSpot(out Vector3 to)) transform.position = to;
                goalSet = false;
                FacePlayer();
                repathAt = 0f;
                stuckFor = 0f;
                yield return new WaitForSeconds(rampageBlink.y);
            }
            while (fade > 0f && phase != Phase.Killing)
            {
                fade = Mathf.MoveTowards(fade, 0f, Time.deltaTime / Mathf.Max(rampageBlink.z, 0.01f));
                yield return null;
            }
            blinking = false;
        }

        /// <summary>
        /// Where it goes in a blink: a spot on the NavMesh <see cref="rampageBlinkWalk"/> away from the player (walking),
        /// out of their sight (off the screen, or with a wall between), the farther the better.
        /// </summary>
        bool PickBlinkSpot(out Vector3 best)
        {
            best = transform.position;
            Vector3 from = NavPoint(player.transform.position);
            Vector3 eye = cam ? cam.transform.position : from + Vector3.up * 1.6f;
            var way = new NavMeshPath();
            float bestScore = float.MinValue;
            for (int i = 0; i < 120; i++)
            {
                Vector2 d = Random.insideUnitCircle * rampageBlinkWalk.y;
                if (!NavMesh.SamplePosition(from + new Vector3(d.x, 0f, d.y), out NavMeshHit hit, 2f, NavMesh.AllAreas)) continue;
                Vector3 q = hit.position;
                // somewhere it can run on from: reachable from where it stands
                if (!NavMesh.CalculatePath(NavPoint(transform.position), q, NavMesh.AllAreas, way) || way.status != NavMeshPathStatus.PathComplete) continue;
                // how far from the player: walking if there is a way (behind a shut gate, as the crow flies)
                float walk = NavMesh.CalculatePath(from, q, NavMesh.AllAreas, way) && way.status == NavMeshPathStatus.PathComplete
                    ? Length(way) : Flat(q - from).magnitude;
                if (walk < rampageBlinkWalk.x || walk > rampageBlinkWalk.y * 1.5f) continue;
                if (!HasRoom(q)) continue;
                // out of sight: none of it on the screen with a clear line to it
                bool seenThere = false;
                foreach (float h in new[] { 0.4f, 1.1f, 1.8f })
                {
                    Vector3 c = q + Vector3.up * h * bodySize * 0.6f;
                    Vector3 v = cam ? cam.WorldToViewportPoint(c) : new Vector3(0.5f, 0.5f, 1f);
                    bool inFrame = v.z > 0.05f && v.x > -0.1f && v.x < 1.1f && v.y > -0.1f && v.y < 1.1f;
                    if (inFrame && !Physics.Linecast(eye, c, sightMask, QueryTriggerInteraction.Ignore)) { seenThere = true; break; }
                }
                if (seenThere) continue;
                float score = walk + Random.value * 4f;
                if (score > bestScore) { bestScore = score; best = q; }
            }
            return bestScore > float.MinValue;
        }

        /// <summary>The last hit: a long moan, and it goes limp and falls where it stood (a ragdoll kept out of the walls).</summary>
        IEnumerator Die()
        {
            phase = Phase.Dead;
            if (rageSource) { rageSource.Stop(); rageSource.volume = 0f; }
            if (breathSource) breathSource.Stop();
            Vector3 mouth = head ? head.position : transform.position + Vector3.up * 1.5f;
            AudioClip moan = deathMoan ? deathMoan : retreatWail ? retreatWail : scream;
            if (moan)
            {
                var a = Audio.SpatialSound.PlayAt(moan, mouth, deathMoanVolume, 6f, 60f);
                if (a && !deathMoan) a.pitch = 0.7f;   // the placeholder slowed down into a long, falling moan
            }
            if (GoLimp())
            {
                yield return new WaitForSeconds(0.7f);
                if (rig) rig.AddTrauma(0.25f);   // the thud
                yield break;
            }
            // over onto its side, about the line it stands on, faster as it goes (it drops)
            Quaternion from = transform.rotation;
            Quaternion to = Quaternion.AngleAxis(Random.value < 0.5f ? 84f : -84f, transform.forward) * from;
            Vector3 at = transform.position;
            float floor = at.y;
            var bones = new List<Transform>();
            if (animator)
                for (var b = HumanBodyBones.Hips; b < HumanBodyBones.LastBone; b++)
                {
                    Transform t = animator.GetBoneTransform(b);
                    if (t) bones.Add(t);
                }
            for (float t = 0f; t <= deathFallTime; t += Time.deltaTime)
            {
                float k = Mathf.Clamp01(t / Mathf.Max(deathFallTime, 0.01f));
                transform.rotation = Quaternion.Slerp(from, to, k * k);
                // lying on the floor, not through it: the lowest bone kept just over it
                transform.position = at;
                float low = float.MaxValue;
                foreach (Transform b in bones) low = Mathf.Min(low, b.position.y);
                if (low < floor + soleHeight) transform.position = at + Vector3.up * (floor + soleHeight - low);
                yield return null;
            }
            if (rig) rig.AddTrauma(0.25f);   // the thud
        }

        /// <summary>
        /// Dead: the bones handed over to physics (a ragdoll built on its humanoid skeleton), first taken out of any wall
        /// the pose it died in reaches into, tipped over towards the open, and watched on the way down so nothing passes
        /// through a wall (<see cref="RagdollWallGuard"/>). False: no humanoid body to do it with.
        /// </summary>
        bool GoLimp()
        {
            if (!animator || !animator.isHuman || !model) return false;
            if (graph.IsValid()) graph.Stop();
            // the hitboxes, the probe: nothing of the living body is left to collide or to weigh on the bones
            foreach (var c in GetComponentsInChildren<Collider>(true)) c.enabled = false;
            var ragdoll = model.gameObject.AddComponent<InfectedRagdoll>();
            var ignore = player ? player.GetComponentsInChildren<Collider>(true) : new Collider[0];
            if (!ragdoll.Build(animator, corpseMass, ignore)) { Destroy(ragdoll); return false; }

            // over towards where there is most room, so it does not fold up against a wall
            Transform h = animator.GetBoneTransform(HumanBodyBones.Hips);
            Vector3 from = h ? h.position : transform.position + Vector3.up;
            Vector3 dir = transform.forward;
            float room = -1f;
            for (int i = 0; i < 8; i++)
            {
                Vector3 d = Quaternion.AngleAxis(i * 45f, Vector3.up) * transform.forward;
                float r = Physics.SphereCast(from, 0.2f * bodySize, d, out RaycastHit hit, 4f, sightMask, QueryTriggerInteraction.Ignore) ? hit.distance : 4f;
                r += i == 0 ? 0.3f : 0f;   // ahead, all else being equal: it was coming that way
                if (r > room) { room = r; dir = d; }
            }
            ClearOfWalls(ragdoll, dir);
            ragdoll.Activate(dir * 0.8f);
            ragdoll.Topple(Vector3.Cross(Vector3.up, dir), 1.2f);
            var guard = model.gameObject.AddComponent<RagdollWallGuard>();
            guard.mask = sightMask;
            guard.owner = transform;
            return true;
        }

        /// <summary>
        /// Before it goes limp: the pose it died in may reach into a wall (an arm through it, a leg in it). Left like that
        /// the physics shoots the limb out of the wrong side, or it stays stuck in it. Every joint that is behind a wall from
        /// the one it hangs off, and every part overlapping one, has the whole body moved out (a few passes).
        /// </summary>
        void ClearOfWalls(InfectedRagdoll ragdoll, Vector3 fallDir)
        {
            var mine = new List<Collider>();
            foreach (var c in model.GetComponentsInChildren<Collider>()) if (c.enabled && ragdoll.Owns(c)) mine.Add(c);
            var near = new Collider[16];
            HumanBodyBones[] joints =
            {
                HumanBodyBones.Hips, HumanBodyBones.Spine, HumanBodyBones.Chest, HumanBodyBones.UpperChest, HumanBodyBones.Neck, HumanBodyBones.Head,
                HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand,
                HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand,
                HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot,
                HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot,
            };
            var bones = new List<Transform>();
            var isHips = new List<bool>();
            foreach (var b in joints) { Transform t = animator.GetBoneTransform(b); if (t) { bones.Add(t); isHips.Add(b == HumanBodyBones.Hips); } }
            bool Ours(Collider c) => c.transform.IsChildOf(transform) || (c.attachedRigidbody && !c.attachedRigidbody.isKinematic);
            float margin = 0.15f * bodySize;
            var wallPush = new Dictionary<Collider, Vector3>();

            // a wall it reaches past: the whole body comes back off it, as far as the joint farthest beyond it
            void Wall(Collider c, Vector3 n, Vector3 onWall, float atLeast)
            {
                float depth = atLeast;
                foreach (Transform j in bones) depth = Mathf.Max(depth, Vector3.Dot(onWall - j.position, n));
                depth += margin;
                if (!wallPush.TryGetValue(c, out Vector3 was) || was.sqrMagnitude < depth * depth) wallPush[c] = n * depth;
            }

            Vector3 pivot = transform.position;
            Vector3 tiltAxis = Vector3.Cross(Vector3.up, fallDir);
            float tilted = 0f;
            for (int pass = 0; pass < 16; pass++)
            {
                Physics.SyncTransforms();
                wallPush.Clear();
                bool ceiling = false;
                float lift = 0f;
                // a joint round the far side of something from the one it hangs off (from the floor it stands on, for the hips)
                for (int i = 0; i < bones.Count; i++)
                {
                    Transform t = bones[i];
                    Vector3 a = isHips[i] ? pivot + Vector3.up * 0.3f : t.parent.position;
                    if (!Physics.Linecast(a, t.position, out RaycastHit hit, sightMask, QueryTriggerInteraction.Ignore) || Ours(hit.collider)) continue;
                    if (hit.normal.y < -0.6f) ceiling = true;
                    else if (hit.normal.y > 0.6f) lift = Mathf.Max(lift, hit.point.y - t.position.y + margin);
                    else Wall(hit.collider, Flat(hit.normal).normalized, hit.point, 0f);
                }
                // a part sunk into something
                foreach (Collider c in mine)
                {
                    Bounds bb = c.bounds;
                    int count = Physics.OverlapBoxNonAlloc(bb.center, bb.extents, near, Quaternion.identity, sightMask, QueryTriggerInteraction.Ignore);
                    for (int i = 0; i < count; i++)
                    {
                        Collider o = near[i];
                        if (!o || Ours(o)) continue;
                        if (!Physics.ComputePenetration(c, c.transform.position, c.transform.rotation, o, o.transform.position, o.transform.rotation,
                                                        out Vector3 d, out float depth)) continue;
                        if (d.y < -0.6f) ceiling = true;
                        else if (d.y > 0.6f) lift = Mathf.Max(lift, depth * d.y);
                        else
                        {
                            // always back to the side it stands on (a part more than half through would be pushed on
                            // out of the far side): the wall's face towards its feet, and the part off it
                            Vector3 stand = pivot + Vector3.up * Mathf.Clamp(c.bounds.center.y - pivot.y, 0.1f, 2.5f);
                            Vector3 face = o.ClosestPoint(stand);
                            Vector3 n = Flat(stand - face);
                            if (n.sqrMagnitude < 1e-4f) n = Flat(d);
                            n.Normalize();
                            float r = Mathf.Min(bb.extents.x, Mathf.Min(bb.extents.y, bb.extents.z));
                            Wall(o, n, face, Vector3.Dot(face - c.bounds.center, n) + r);
                        }
                    }
                }
                Vector3 push = Vector3.zero;
                foreach (var kv in wallPush) push += kv.Value;
                if (push.sqrMagnitude > 1e-4f)
                {
                    // out of the walls first
                    push = Vector3.ClampMagnitude(push, 1.5f * bodySize);
                    // never itself through a wall on the way: its feet stay on the side they were on
                    Vector3 foot = pivot + Vector3.up * 0.3f;
                    if (Physics.SphereCast(foot, 0.1f, push.normalized, out RaycastHit block, push.magnitude, sightMask, QueryTriggerInteraction.Ignore)
                        && !Ours(block.collider))
                        push = push.normalized * Mathf.Max(0f, block.distance - 0.05f);
                    if (push.sqrMagnitude < 1e-6f) break;   // cornered: as clear as it gets (the guard does the rest)
                    transform.position += push;
                    pivot += push;
                }
                else if (ceiling && tilted < 72f)
                {
                    // too tall for the room where it stands: tipped over (the way it is going to fall) until it is under it
                    transform.RotateAround(pivot, tiltAxis, 12f);
                    tilted += 12f;
                }
                else if (lift > 1e-3f) transform.position += Vector3.up * Mathf.Min(lift, 0.5f);   // up out of the floor
                else break;
            }
        }

        /// <summary>Its screams while it comes, one after another, from its head.</summary>
        void RampageScream()
        {
            if (Time.time < nextRampageScream) return;
            AudioClip clip = rampageScreams != null && rampageScreams.Length > 0 ? rampageScreams[Random.Range(0, rampageScreams.Length)]
                           : enrageScream ? enrageScream : scream;
            float gap = Random.Range(rampageScreamEvery.x, rampageScreamEvery.y);
            nextRampageScream = Time.time + (clip ? Mathf.Max(gap, clip.length * 0.8f) : gap);
            if (!clip) return;
            Vector3 mouth = head ? head.position : transform.position + Vector3.up * 2f;
            var a = Audio.SpatialSound.PlayAt(clip, mouth, rampageScreamVolume, 4f, 70f);
            if (a) a.pitch = Random.Range(0.92f, 1.08f);
        }

        /// <summary>
        /// The flashlight held on it out in the open too long: it screams, the flashlight dies, and it comes at the player,
        /// howling, the light no longer holding it, all the way to them. Shot <see cref="hitsToSpare"/> times by then it breaks
        /// off in front of them (<see cref="BreakOff"/>); else it is the end. The player reaching a safe zone breaks it off too.
        /// </summary>
        void BeginEnrage()
        {
            mode = Mode.Enraged;
            aggression = 0f;
            hits = 0;
            held = peeking = atPeek = goalSet = false;
            leanK = 0f;
            enrageGo = Time.time + enrageWindup;
            staggerUntil = 0f;
            modeUntil = Time.time + enrageGiveUp;
            nextHowl = Time.time + enrageWindup + Random.Range(howlEvery.x, howlEvery.y);
            repathAt = 0f;
            if (breathSource) breathSource.Stop();
            Vector3 mouth = head ? head.position : transform.position + Vector3.up * 2f;
            AudioClip snap = enrageScream ? enrageScream : scream;
            if (snap) Audio.SpatialSound.PlayAt(snap, mouth, 1f, 4f, 70f);
            nextHowl = Mathf.Max(nextHowl, Time.time + (snap ? snap.length * 0.9f : 0f));   // no howl on top of it
            if (rig) rig.AddTrauma(0.5f);
            // the light is dead until it is shot off (or breaks off): it is fought in the dark, by its howls and the muzzle flash
            if (torch) torch.Blackout(enrageGiveUp + 5f);
        }

        /// <summary>
        /// Up to the player with enough shot into it: it breaks off right in front of them and runs, howling (the light
        /// comes back once it has turned).
        /// </summary>
        void BreakOff()
        {
            Vector3 mouth = head ? head.position : transform.position + Vector3.up * 2f;
            if (scream && !retreatWail) Audio.SpatialSound.PlayAt(scream, mouth, 1f, 4f, 70f);   // else it wails as it goes
            if (rig) rig.AddTrauma(0.35f);
            BeginRetreat(false);
            if (torch) torch.EndBlackoutIn(repelBlackout, true);
        }

        /// <summary>The looped roar: up while it runs at the player (not while it stands screaming), away as soon as it stops.</summary>
        void Roar(float dt)
        {
            if (!rageSource) return;
            bool on = enrageLoop && phase == Phase.Active && (mode == Mode.Enraged || mode == Mode.Rampage) && Time.time >= enrageGo;
            // the wail runs out by itself while it flees (see Wail); anything else and it dies away
            bool wailing = retreatWail && rageSource.clip == retreatWail && phase == Phase.Active && mode == Mode.Retreat && fleeing;
            if (on && rageSource.clip != enrageLoop)
            {
                rageSource.Stop();
                rageSource.clip = enrageLoop;
                rageSource.loop = true;
                rageSource.pitch = 1f;
                rageSource.volume = 0f;
            }
            float target = on ? enrageLoopVolume : wailing ? retreatWailVolume : 0f;
            float fade = Mathf.Max(0.01f, target > rageSource.volume ? enrageLoopFade.x : enrageLoopFade.y);
            rageSource.volume = Mathf.MoveTowards(rageSource.volume, target, dt / fade);
            if (on && !rageSource.isPlaying)
            {
                rageSource.time = Random.Range(0f, enrageLoop.length * 0.9f);   // never the same start twice
                rageSource.Play();
            }
            else if (!on && !wailing && rageSource.isPlaying && rageSource.volume <= 0.001f) rageSource.Stop();
        }

        /// <summary>Beaten out of its rage: the roar breaks into a wail that goes with it as it runs.</summary>
        bool Wail()
        {
            if (!rageSource || !retreatWail) return false;
            rageSource.Stop();
            rageSource.clip = retreatWail;
            rageSource.loop = false;
            rageSource.pitch = Random.Range(0.96f, 1.04f);
            rageSource.volume = retreatWailVolume;
            rageSource.time = 0f;
            rageSource.Play();
            return true;
        }

        /// <summary>Howls, one after another, while it comes.</summary>
        void Howl()
        {
            if (Time.time < nextHowl) return;
            nextHowl = Time.time + Random.Range(howlEvery.x, howlEvery.y);
            if (enrageHowls == null || enrageHowls.Length == 0) return;   // none set: it comes silent but for its steps
            AudioClip clip = enrageHowls[Random.Range(0, enrageHowls.Length)];
            if (!clip) return;
            Vector3 mouth = head ? head.position : transform.position + Vector3.up * 2f;
            var a = Audio.SpatialSound.PlayAt(clip, mouth, Random.Range(0.85f, 1f), 3f, 60f);
            // short howls vary a little in pitch (never lower: the one-shot is timed for its own length); a long scream
            // keeps its own, and the next waits until it is nearly over
            if (a && clip.length < 1.5f) a.pitch = Random.Range(1f, 1.15f);
            nextHowl = Mathf.Max(nextHowl, Time.time + clip.length * 0.9f);
        }

        /// <summary>
        /// Shot. Enraged, every hit jolts it to a stop for a moment, and enough of them break it off: it runs, howling.
        /// Anywhere else a bullet only makes it angrier.
        /// </summary>
        public void TakeDamage(in Combat.DamageInfo info)
        {
            if (phase != Phase.Active) return;
            Vector3 mouth = head ? head.position : transform.position + Vector3.up * 2f;
            if (mode == Mode.Rampage)
            {
                // one shot, one hit (a shotgun's pellets all land in the same frame; the blink takes the rest)
                if (blinking) return;
                hits++;
                StartCoroutine(RampageHit(hits >= Mathf.Max(1, rampageHitsToKill)));
                return;
            }
            if (mode == Mode.Enraged)
            {
                // it keeps coming: a hit only counts (enough of them and it breaks off when it gets to the player)
                hits++;
                if (rig) rig.AddTrauma(0.15f);
                AudioClip clip = scream;   // a short yelp, not the long howl it comes with
                var a = clip ? Audio.SpatialSound.PlayAt(clip, mouth, 0.9f, 3f, 50f) : null;
                if (a) a.pitch = 1.2f;
                nextHowl = Mathf.Max(nextHowl, Time.time + 0.8f);
                return;
            }
            if (!LegacyBody && mode != Mode.Scream && mode != Mode.Retreat) aggression += aggressionTime * 0.35f;
        }

        // ================================================================== the showdown

        /// <summary>The player turning <see cref="showdownValve"/> from inside a safe zone: it comes (once it is over, never again).</summary>
        void CheckShowdown()
        {
            if (!showdownValve || showdownDone || LegacyBody || phase != Phase.Active || !NavReady()) return;
            if (mode != Mode.Hunt && mode != Mode.Approach && mode != Mode.Prowl && mode != Mode.Ambush && mode != Mode.Retreat) return;
            if (!showdownValve.IsTurning) return;
            SafeZone z = SafeZone.At(player.transform.position);
            if (!z) return;
            mode = Mode.ValveWatch;
            zone = z;
            goalSet = rushed = false;
            peeking = atPeek = fleeing = held = false;
            leanK = 0f;
            stare = aggression = 0f;
            charging = true;
            repathAt = 0f;
        }

        /// <summary>
        /// The valve turning: straight to the edge of the light, fast; then round the player, slowly, close, standing
        /// still whenever they look its way (the flashlight still turns it to stone, but does not drive it off).
        /// Out of the light the showdown is off and it hunts; the valve shut (and it at the edge) it holds its breath.
        /// </summary>
        void ValveWatch(float dt, SafeZone inZone, float dist)
        {
            if (!inZone) { BeginHunt(); return; }
            if (inZone != zone) { zone = inZone; goalSet = rushed = false; }
            if (!rushed)
            {
                if (!goalSet && !PickProwlSpot(true)) { rushed = true; pauseUntil = Time.time + 0.5f; return; }
                float l = Travel(goal, showdownRushSpeed, Gait.Charge, dt);
                Stamp(dt, dist, 1f, true);
                if (l < 0.25f)
                {
                    rushed = true;
                    goalSet = charging = false;
                    pauseUntil = Time.time + Random.Range(prowlPause.x, prowlPause.y);
                    breathWait = Random.Range(0.05f, 0.25f);
                }
                return;
            }
            if (showdownValve.IsClosed) { BeginHush(); return; }

            bool watched = OnScreen();
            if (Time.time < pauseUntil || watched || !goalSet)
            {
                if (watched) HoldAndWatch(dt);
                else StandAndStare(dt, 0.75f);
                if (!watched && Time.time >= pauseUntil && !goalSet && !PickProwlSpot(false)) pauseUntil = Time.time + 0.5f;
                return;
            }
            float left = Travel(goal, showdownProwlSpeed, Gait.Creep, dt);
            Stamp(dt, dist, showdownStepLoudness, false, showdownStepInterval, showdownStepPitch);
            if (left < 0.25f) { goalSet = false; pauseUntil = Time.time + Random.Range(prowlPause.x, prowlPause.y); }
        }

        /// <summary>The valve is shut: it stops dead and holds its breath; the fear and every sound go.</summary>
        void BeginHush()
        {
            mode = Mode.Hush;
            lookedFor = 0f;
            goalSet = charging = false;
            bodyHeld = false;   // the head turns on from wherever it is now
            if (breathSource) breathSource.Stop();
            breathLevel = 0f;
            PlayerStress.Hushed = true;
            if (!silenced)
            {
                silenced = true;
                listenerWas = AudioListener.volume;
            }
            if (silenceFade != null) StopCoroutine(silenceFade);
            silenceFade = StartCoroutine(FadeListener(0f, showdownSilence));
        }

        IEnumerator FadeListener(float to, float time)
        {
            float from = AudioListener.volume;
            for (float t = 0f; t < time; t += Time.deltaTime)
            {
                AudioListener.volume = Mathf.Lerp(from, to, t / Mathf.Max(time, 0.01f));
                yield return null;
            }
            AudioListener.volume = to;
            silenceFade = null;
        }

        /// <summary>Sound and fear back, at once.</summary>
        void EndSilence()
        {
            if (silenceFade != null) StopCoroutine(silenceFade);
            silenceFade = null;
            PlayerStress.Hushed = false;
            if (!silenced) return;
            silenced = false;
            AudioListener.volume = listenerWas;
        }

        /// <summary>
        /// Looked at long enough: the scream (the one it snaps with), the flashlight gone, the picture knocked dark and shaking
        /// as from a blast; in that dark it is away, fast, and from then on it hunts as ever.
        /// </summary>
        void Showdown()
        {
            showdownDone = true;
            EndSilence();
            Vector3 mouth = head ? head.position : transform.position + Vector3.up * 2f;
            AudioClip snap = enrageScream ? enrageScream : scream;
            if (snap) Audio.SpatialSound.PlayAt(snap, mouth, 1f, 4f, 70f);
            if (rig) { rig.AddTrauma(showdownShake); rig.AddTremor(1f); }
            if (torch) torch.Blackout(showdownBlackout);
            StartCoroutine(ShockFade());

            mode = Mode.Retreat;
            stealthy = true;
            fleeing = false;
            aggression = 0f;
            held = bodyHeld = false;
            if (!PickFarAway(retreatWalk, true) && !PickHideout()) PickRandomAway();
            stuckFor = 0f;
            modeUntil = Time.time + 10f;
            repathAt = 0f;
            charging = true;
        }

        IEnumerator ShockFade()
        {
            for (float t = 0f; t < showdownFade.x; t += Time.deltaTime)
            {
                fade = showdownFadeDepth * t / Mathf.Max(showdownFade.x, 0.01f);
                yield return null;
            }
            fade = showdownFadeDepth;
            yield return new WaitForSeconds(showdownFade.y);
            while (fade > 0f && phase != Phase.Killing)
            {
                fade = Mathf.MoveTowards(fade, 0f, showdownFadeDepth * Time.deltaTime / Mathf.Max(showdownFade.z, 0.01f));
                yield return null;
            }
        }

        // ================================================================== modes

        void BeginHunt()
        {
            mode = Mode.Hunt;
            zone = null;
            goalSet = peeking = atPeek = false;
            leanK = 0f;
            repathAt = 0f;
        }

        void BeginApproach(SafeZone z)
        {
            mode = Mode.Approach;
            zone = z;
            goalSet = false;
            stare = 0f;
            charging = false;
        }

        void BeginProwl()
        {
            mode = Mode.Prowl;
            goalSet = false;
            chaseTime = 0f;
            pauseUntil = Time.time + Random.Range(prowlPause.x, prowlPause.y);
            breathWait = Random.Range(0.05f, 0.25f);
        }

        /// <summary>Stared down from the light: it stands there and screams, very loud.</summary>
        void BeginScream()
        {
            mode = Mode.Scream;
            modeUntil = Time.time + screamHold;
            stare = 0f;
            if (breathSource) breathSource.Stop();
            Vector3 mouth = head ? head.position : transform.position + Vector3.up * 2f;
            if (scream) Audio.SpatialSound.PlayAt(scream, mouth, screamVolume, 4f, 70f);   // (min distance: loud well out from it)
            if (rig) rig.AddTrauma(0.45f);
        }

        /// <summary>
        /// Bolts for the dark: the flashlight dies for a couple of seconds, so all there is of it is the stamping going
        /// away fast. From now on it creeps quietly.
        /// </summary>
        /// <param name="killLight">Stared down: the flashlight blinks and dies, and it goes in the dark. False: beaten off
        /// (shot, or the player got into the light): it just runs, seen, howling.</param>
        void BeginRetreat(bool killLight = true)
        {
            mode = Mode.Retreat;
            stealthy = true;
            fleeing = !killLight;
            aggression = 0f;
            if (fleeing) Wail();   // out of its rage: wailing as it goes
            if (torch && killLight) torch.Blackout(Random.Range(blackoutTime.x, blackoutTime.y), blackoutBlinks);
            // a long way off: another room (or the far end of the hall), so the player gets a breather
            if (!PickFarAway(killLight ? retreatWalk : fleeWalk, killLight) && !PickHideout()) PickRandomAway();
            stuckFor = 0f;
            modeUntil = Time.time + 10f;   // never stuck bolting
            repathAt = 0f;
            charging = true;
        }

        void BeginAmbush()
        {
            mode = Mode.Ambush;
            fleeing = false;
            cover = transform.position;
            peeking = atPeek = false;
            peekAt = Time.time + Random.Range(peekEvery.x, peekEvery.y) * 0.6f;
            modeUntil = Time.time + Random.Range(ambushPatience.x, ambushPatience.y);
            reactAt = -1f;
            charging = false;
            goalSet = false;
            ambushTall = Random.value < 0.5f ? 0.1f : 0.85f;   // crouched low behind the wall, or standing tall behind it
            restUntil = Time.time + Random.Range(restAfterRetreat.x, restAfterRetreat.y);
        }

        /// <summary>
        /// Behind the wall, low, out of the light: waits, peeks out now and then. The player out of the light (after a beat),
        /// or close by: the hunt. Waited out: back to prowling round the light.
        /// </summary>
        void Ambush(float dt, SafeZone inZone, float dist)
        {
            if (dist < pounceDistance) { BeginHunt(); return; }
            if (!inZone)
            {
                if (reactAt < 0f) reactAt = Mathf.Max(Time.time + Random.Range(ambushReact.x, ambushReact.y), restUntil);
                if (Time.time >= reactAt) { BeginHunt(); return; }
            }
            else reactAt = -1f;
            if (inZone && Time.time >= modeUntil) { BeginApproach(inZone); return; }

            if (!peeking && Time.time >= peekAt) StartPeek();
            if (peeking && Time.time >= peekUntil)
            {
                peeking = false;
                peekAt = Time.time + Random.Range(peekEvery.x, peekEvery.y);
            }

            Vector3 target = peeking ? peekSpot : cover;
            Vector3 to = Flat(target - transform.position);
            float step = 0.9f * dt;
            if (to.magnitude > 0.02f)
            {
                Vector3 next = transform.position + Vector3.ClampMagnitude(to, step);
                if (NavMesh.SamplePosition(next, out NavMeshHit hit, 0.4f, NavMesh.AllAreas)) next = hit.position;
                transform.position = next;
            }
            FaceTowards(player.transform.position, dt, 0.4f);
            // no animation in the dark: caught out at the peek spot it stands in a peeking pose (FreezeInPose)
            atPeek = peeking && to.magnitude < 0.35f;
        }

        /// <summary>To the edge of the wall it hides behind, and a lean out to the side the player is on.</summary>
        void StartPeek()
        {
            if (!cam) return;
            if (!FindPeekSpot(out Vector3 spot, out int side))
            {
                peekAt = Time.time + Random.Range(peekEvery.x, peekEvery.y);
                return;
            }
            peeking = true;
            peekUntil = Time.time + Flat(spot - transform.position).magnitude / 0.9f + Random.Range(peekTime.x, peekTime.y);
            peekSpot = spot;
            leanSide = side;
        }

        /// <summary>
        /// The nearest spot to the side of its hiding place (across the line to the player) from where its head, at the
        /// height it is waiting at, would see the player's eyes; reachable in a straight shuffle. Side +1 = its right.
        /// </summary>
        bool FindPeekSpot(out Vector3 spot, out int side)
        {
            spot = cover; side = 0;
            Vector3 eye = cam.transform.position;
            Vector3 to = Flat(eye - cover);
            if (to.sqrMagnitude < 0.01f) return false;
            Vector3 right = Vector3.Cross(Vector3.up, to.normalized);
            float headH = ambushTall > 0.5f ? headHeights.x * 0.9f * bodySize : (headHeights.y + 0.2f) * bodySize;
            int first = Random.value < 0.5f ? 1 : -1;
            for (float off = 0.3f; off <= 2.7f; off += 0.3f)
            {
                for (int k = 0; k < 2; k++)
                {
                    int s = k == 0 ? first : -first;
                    Vector3 q = cover + right * (s * off);
                    if (NavMesh.Raycast(cover, q, out NavMeshHit hit, NavMesh.AllAreas))
                    {
                        if (Flat(hit.position - cover).magnitude < off - 0.35f) continue;   // a wall that way, not a corner
                        q = hit.position;
                    }
                    if (!Sight.Clear(q + Vector3.up * headH, eye)) continue;
                    // only so far: the lean puts the head out the rest of the way, the body stays behind the wall
                    Vector3 off2 = Flat(q - cover);
                    spot = cover + Vector3.ClampMagnitude(off2, Mathf.Max(0f, off2.magnitude - 0.55f * bodySize));
                    side = s;
                    return true;
                }
            }
            // a low cover: standing up is enough
            if (ambushTall < 0.5f && Sight.Clear(cover + Vector3.up * headHeights.x * 0.9f * bodySize, eye)) { ambushTall = 0.85f; spot = cover; return true; }
            // round a bend: the corner on the way to the player, a few steps off
            if (!NavMesh.CalculatePath(cover, NavPoint(player.transform.position), NavMesh.AllAreas, probePath)) return false;
            Vector3[] way = probePath.corners;
            float walked = 0f;
            for (int i = 1; i < way.Length && walked < 3.5f; i++)
            {
                Vector3 a = way[i - 1], seg = Flat(way[i] - a);
                float len = seg.magnitude;
                for (float t = 0.3f; t <= len && walked + t < 3.5f; t += 0.3f)
                {
                    Vector3 q = a + seg * (t / len);
                    if (!Sight.Clear(q + Vector3.up * headH, eye)) continue;
                    // a step short of the corner: the lean puts the head round it
                    float back = Mathf.Min(walked + t, 0.5f * bodySize);
                    Vector3 dir = seg / len;
                    spot = q - dir * back;
                    Vector3 r = Vector3.Cross(Vector3.up, Flat(eye - spot).normalized);
                    side = Vector3.Dot(q - spot, r) >= 0f ? 1 : -1;
                    return true;
                }
                walked += len;
            }
            return false;
        }

        // ---- stared at

        void UpdateStare(float dt)
        {
            bool fromLight = (mode == Mode.Prowl || mode == Mode.Approach) && zone && SafeZone.At(player.transform.position) == zone;
            if (!fromLight) { stare = 0f; return; }
            bool looked = LookedAt();
            stare = looked ? stare + dt : Mathf.Max(0f, stare - dt * 0.5f);
            if (stare >= stareTime) BeginScream();
        }

        /// <summary>
        /// The flashlight on it: switched on, some part of it near the middle of the screen with nothing in the way, and
        /// that part lit by the flashlight itself (the chemical light or a lamp on it does not count).
        /// </summary>
        bool LookedAt() => TorchOn(true);

        /// <summary>
        /// The flashlight's own light on some part of it that is on screen with nothing in the way;
        /// <paramref name="centre"/>: that part also near the middle of the screen (being stared at).
        /// </summary>
        bool TorchOn(bool centre)
        {
            if (!cam || samplePoints == null || !torch || !torch.IsOn || torch.InBlackout) return false;
            if (torchLights == null || Time.time >= torchLightsRefresh)
            {
                torchLightsRefresh = Time.time + 1f;
                torchLights = new List<Light>(torch.GetComponentsInChildren<Light>(true));
            }
            Vector3 eye = cam.transform.position;
            foreach (Transform t in samplePoints)
            {
                Vector3 v = cam.WorldToViewportPoint(t.position);
                if (v.z <= 0.05f) continue;
                if (centre ? new Vector2(v.x - 0.5f, v.y - 0.5f).magnitude > stareCone : v.x < -0.02f || v.x > 1.02f || v.y < -0.02f || v.y > 1.02f) continue;
                if (Physics.Linecast(eye, t.position, sightMask, QueryTriggerInteraction.Ignore)) continue;
                if (LightAt(t.position, torchLights, torchThreshold) >= torchThreshold) return true;
            }
            return false;
        }

        /// <summary>
        /// Looked at (without the flashlight) at the edge of the light: the body stops dead in whatever it was doing, not
        /// turning, not breathing the pose on; only the head comes round to the player, very slowly, rolling a little.
        /// </summary>
        void HoldAndWatch(float dt)
        {
            if (!head) return;
            if (!bodyHeld) { bodyHeld = true; headNow = head.rotation; }
            if (!cam) cam = Camera.main;
            Vector3 eye = cam ? cam.transform.position : player.transform.position + Vector3.up * 1.6f;
            Vector3 want = eye - head.position;
            if (want.sqrMagnitude > 1e-4f)
            {
                want.Normalize();
                Vector3 up = Vector3.ProjectOnPlane(Vector3.Lerp(headNow * headUp, Vector3.up, 0.6f), want);
                if (up.sqrMagnitude < 1e-4f) up = Vector3.up;
                Quaternion target = Quaternion.LookRotation(want, up) * Quaternion.Inverse(Quaternion.LookRotation(headFace, headUp));
                headNow = Quaternion.RotateTowards(headNow, target, headWatchSpeed * dt);
            }
            head.rotation = headNow;   // set afresh each frame (the pose is not evaluated): the sway does not pile up
            HeadSway(dt, 0.5f);
        }

        void StandAndStare(float dt, float tall)
        {
            bodyHeld = false;
            FaceTowards(player.transform.position, dt, 0.5f);
            corridorK = Mathf.MoveTowards(corridorK, 0f, dt * 2f);
            doorK = Mathf.MoveTowards(doorK, 0f, dt * 2f);
            leanK = Mathf.MoveTowards(leanK, 0f, dt * 3f);
            UpdateUpright(dt, false, CeilingRoom());
            PoseStill(dt, Mathf.Min(tall, upright));
            ResolveBody();
            FitBody();
            LookAtPlayer();
            HeadSway(dt);
        }

        /// <summary>
        /// Standing at the edge of the light: the head, turned to the player, slowly rolls over to one side and back and
        /// nods, never quite in time (two slow waves and a drift).
        /// </summary>
        void HeadSway(float dt, float amount = 1f)
        {
            if (!head || headSwayAngle <= 0f) return;
            swayTime += dt;
            float p = Mathf.Max(headSwayPeriod, 0.5f), a = headSwayAngle * amount;
            float roll = Mathf.Sin(swayTime * Mathf.PI * 2f / p) * a
                       + (Mathf.PerlinNoise(swayTime * 0.15f, 3.1f) - 0.5f) * a * 0.8f;
            float nod = Mathf.Sin(swayTime * Mathf.PI * 2f / (p * 1.63f) + 1.3f) * a * 0.35f;
            Vector3 face = head.rotation * headFace;
            Vector3 side = Vector3.Cross(Vector3.up, face);
            if (side.sqrMagnitude < 1e-4f) side = transform.right;
            head.rotation = Quaternion.AngleAxis(roll, face) * Quaternion.AngleAxis(nod, side.normalized) * head.rotation;
        }

        // ---- picking spots

        /// <summary>
        /// A spot just outside the zone, on the NavMesh, with room for its body, that it can walk to. The first: the nearest.
        /// Later ones: somewhere else round the zone (within <see cref="prowlArc"/>), preferably off the player's screen.
        /// </summary>
        bool PickProwlSpot(bool nearest)
        {
            if (!zone) return false;
            Vector3 c = zone.Centre, me = NavPoint(transform.position);
            float a0 = Mathf.Atan2(transform.position.z - c.z, transform.position.x - c.x);
            float bestScore = float.MinValue;
            bool found = false;
            int samples = nearest ? 24 : 18;
            for (int i = 0; i < samples; i++)
            {
                float a = nearest ? i / (float)samples * Mathf.PI * 2f : a0 + Random.Range(-prowlArc, prowlArc) * Mathf.Deg2Rad;
                Vector2 standoff = mode == Mode.ValveWatch ? showdownStandoff : prowlStandoff;
                float r = zone.radius + Random.Range(standoff.x, standoff.y);
                Vector3 p = c + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * r;
                if (!NavMesh.SamplePosition(p, out NavMeshHit hit, 1.2f, NavMesh.AllAreas)) continue;
                Vector3 q = hit.position;
                if (zone.Contains(q, 0.15f)) continue;
                if (Doorway.At(q, 0.3f)) continue;     // never stands in a door frame
                if (!HasRoom(q)) continue;
                if (!NavMesh.CalculatePath(me, q, NavMesh.AllAreas, probePath) || probePath.status != NavMeshPathStatus.PathComplete) continue;
                float length = Length(probePath);
                float score;
                if (nearest) score = -length;
                else
                {
                    float moved = Flat(q - transform.position).magnitude;
                    if (moved < 0.8f) continue;
                    if (length > moved * 2.5f + 3f) continue;   // not the long way round the building
                    score = Random.value * 2f - Mathf.Abs(moved - 3f) * 0.3f;
                    if (cam && !OnCamera(q + Vector3.up * bodySize)) score += 3f;   // round behind them
                }
                if (score > bestScore) { bestScore = score; goal = q; found = true; }
            }
            goalSet = found;
            if (found) repathAt = 0f;
            return found;
        }

        /// <summary>A dark place out of the player's sight, round a wall it can peek past, a good way off.</summary>
        bool PickHideout()
        {
            if (!cam) return false;
            if (Time.time >= lightsRefresh) RefreshLights();
            burned.RemoveAll(b => b.until < Time.time);
            Vector3 eye = cam.transform.position;
            for (int attempt = 0; attempt < 4; attempt++)
            {
                var req = new CoverRequest
                {
                    self = NavPoint(transform.position),
                    threatEye = eye,
                    threatForward = cam.transform.forward,
                    searchCenter = player.transform.position,
                    minRadius = retreatDistance.x,
                    maxRadius = retreatDistance.y,
                    desiredThreatDistance = Mathf.Lerp(retreatDistance.x, retreatDistance.y, 0.4f),
                    viewAngle = 70f,
                    viewAngleWeight = 0.3f,
                    exposureWeight = 0.2f,
                    travelWeight = 0.15f,
                    hideHeight = headHeights.y * bodySize,
                    minThreatDistance = retreatDistance.x * 0.8f,
                    requirePeekHeight = (headHeights.y + 0.2f) * bodySize,
                };
                if (!CoverFinder.Find(req, burned, null, out CoverSpot spot)) return false;
                bool dark = LightAt(spot.position + Vector3.up * bodySize * 0.6f) < litThreshold * 0.6f
                            && !PathPassesPlayer(spot.position) && !NearSafeZone(spot.position);
                if (dark && HasRoom(spot.position, 0.1f)) { goal = spot.position; goalSet = true; return true; }
                burned.Add(new CoverFinder.Burned { position = spot.position, until = Time.time + 20f });
            }
            return false;
        }

        /// <summary>
        /// Within <see cref="safeZoneAvoid"/> of a safe zone's edge: the lights are where the player has to go, so it never
        /// runs off to wait there (it only comes to them to prowl).
        /// </summary>
        bool NearSafeZone(Vector3 p)
        {
            foreach (SafeZone z in SafeZone.All)
                if (z && Mathf.Abs(p.y - z.Centre.y) < 3f && Flat(p - z.Centre).magnitude < z.radius + safeZoneAvoid) return true;
            return false;
        }

        /// <summary>
        /// Somewhere a long walk from the player (<paramref name="walk"/> metres along the floor, not as the crow flies):
        /// another room, or the far end of the hall. The farther, out of their sight and in the dark the better; never by a
        /// way that goes past them, never near a safe zone. <paramref name="mustHide"/>: out of their sight or nothing.
        /// </summary>
        bool PickFarAway(Vector2 walk, bool mustHide)
        {
            if (Time.time >= lightsRefresh) RefreshLights();
            Vector3 me = NavPoint(transform.position), from = NavPoint(player.transform.position);
            Vector3 eye = cam ? cam.transform.position : from + Vector3.up * 1.6f;
            var fromPlayer = new NavMeshPath();
            float bestScore = float.MinValue;
            for (int i = 0; i < 80; i++)
            {
                Vector2 d = Random.insideUnitCircle * walk.y;
                Vector3 p = (i % 3 == 0 ? me : from) + new Vector3(d.x, 0f, d.y);
                if (!NavMesh.SamplePosition(p, out NavMeshHit hit, 2.5f, NavMesh.AllAreas)) continue;
                Vector3 q = hit.position;
                if (NearSafeZone(q) || Doorway.At(q, 0.3f)) continue;   // never by the lights the player is making for
                if (!NavMesh.CalculatePath(from, q, NavMesh.AllAreas, fromPlayer) || fromPlayer.status != NavMeshPathStatus.PathComplete) continue;
                float walkFromPlayer = Length(fromPlayer);
                if (walkFromPlayer < walk.x || walkFromPlayer > walk.y * 1.4f) continue;
                bool hidden = CoverFinder.IsHidden(q, eye, headHeights.x * bodySize);
                if (mustHide && !hidden) continue;
                if (!HasRoom(q)) continue;
                if (PathPassesPlayer(q)) continue;
                float score = walkFromPlayer + (hidden ? 10f : 0f) + Random.value * 3f;
                if (LightAt(q + Vector3.up * bodySize * 0.6f) < litThreshold * 0.6f) score += 5f;
                if (score > bestScore) { bestScore = score; goal = q; }
            }
            if (bestScore == float.MinValue) return false;
            goalSet = true;
            return true;
        }

        /// <summary>
        /// On its way and not getting anywhere (caught on a corner, wedged in a door frame) for <see cref="stuckTime"/>:
        /// out of the player's view it is put where it was going (or, hunting them, a stretch further along its way).
        /// </summary>
        void CheckStuck(Vector3 target, float left)
        {
            if (heldBack) { stuckFor = 0f; stuckFrom = transform.position; return; }   // waiting out the light, not stuck
            if (left < 0.6f) { stuckFor = 0f; stuckFrom = transform.position; return; }
            if (Time.time < stuckCheckAt) return;
            const float every = 0.5f;
            stuckCheckAt = Time.time + every;
            stuckFor = Flat(transform.position - stuckFrom).magnitude < 0.2f ? stuckFor + every : 0f;
            stuckFrom = transform.position;
            if (stuckFor < stuckTime || OnScreen()) return;

            Vector3 to = NavPoint(target);
            if (mode == Mode.Hunt || mode == Mode.Enraged || mode == Mode.Rampage)
            {
                // not into the player's face: a few metres further along the way
                to = transform.position;
                float run = 3f;
                for (int i = corner; i < corners.Length && run > 0f; i++)
                {
                    Vector3 seg = corners[i] - to;
                    float len = Flat(seg).magnitude;
                    if (len >= run) { to += seg * (run / Mathf.Max(len, 1e-3f)); run = 0f; }
                    else { to = corners[i]; run -= len; }
                }
                if (Flat(to - player.transform.position).magnitude < killDistance + 1.5f) { stuckFor = 0f; return; }
            }
            transform.position = to;
            repathAt = 0f;
            stuckFor = 0f;
            stuckFrom = to;
        }

        /// <summary>
        /// Whether the way there is no good for getting away: no whole way there, or it goes right past the player
        /// (it never runs off through them). Leaves the way in probePath.
        /// </summary>
        bool PathPassesPlayer(Vector3 to)
        {
            if (!NavMesh.CalculatePath(NavPoint(transform.position), to, NavMesh.AllAreas, probePath) || probePath.status != NavMeshPathStatus.PathComplete)
                return true;
            Vector3 p = player.transform.position;
            if (SafeZone.At(p)) return false;   // in the light it cannot touch them anyway: skirting its edge is fine
            // never closer to them than it already is (shot at close range it starts right by them)
            float near = Mathf.Min(Mathf.Max(3f, killDistance + 1f), Flat(transform.position - p).magnitude * 0.9f);
            var c = probePath.corners;
            for (int i = 1; i < c.Length; i++)
                if (Sight.DistanceToSegment(Flat(p), Flat(c[i - 1]), Flat(c[i])) < near) return true;
            return false;
        }

        /// <summary>No proper hideout (a dead end): the farthest place from the player it can get to, the darker the better.</summary>
        void PickRandomAway()
        {
            Vector3 me = NavPoint(transform.position), from = player.transform.position;
            float bestScore = float.MinValue;
            goal = me;
            for (int i = 0; i < 24; i++)
            {
                Vector2 d = Random.insideUnitCircle * retreatDistance.y;
                Vector3 p = (i % 2 == 0 ? from : me) + new Vector3(d.x, 0f, d.y);
                if (!NavMesh.SamplePosition(p, out NavMeshHit hit, 2f, NavMesh.AllAreas)) continue;
                if (NearSafeZone(hit.position)) continue;
                if (PathPassesPlayer(hit.position)) continue;
                float score = Flat(hit.position - from).magnitude;
                if (LightAt(hit.position + Vector3.up * bodySize * 0.6f) < litThreshold * 0.6f) score += 4f;
                if (cam && CoverFinder.IsHidden(hit.position, cam.transform.position, headHeights.y * bodySize)) score += 6f;
                if (score > bestScore) { bestScore = score; goal = hit.position; }
            }
            // nothing that does not go past them: straight away from them, as far as the floor goes
            if (bestScore == float.MinValue)
            {
                Vector3 away = Flat(me - from).normalized;
                for (float d = retreatDistance.y; d >= 2f; d -= 2f)
                    if (NavMesh.SamplePosition(me + away * d, out NavMeshHit hit, 1.5f, NavMesh.AllAreas)
                        && !NavMesh.Raycast(me, hit.position, out _, NavMesh.AllAreas)) { goal = hit.position; break; }
            }
            goalSet = true;
        }

        /// <summary>
        /// Room for its body at a spot: nothing solid within the body's radius (plus <see cref="standClearance"/>) where it
        /// would stand. (Not the NavMesh edge: a safe zone's carved-out circle is an edge too.)
        /// </summary>
        bool HasRoom(Vector3 p, float extra = 0f)
        {
            float r = bodyRadius * bodySize + standClearance * 0.35f + extra;
            return !Physics.CheckCapsule(p + Vector3.up * (r + 0.35f), p + Vector3.up * (headHeights.y * bodySize), r, sightMask, QueryTriggerInteraction.Ignore);
        }

        // ================================================================== the way there

        bool NavReady()
        {
            if (!useNavMesh) return false;
            if (Time.time >= navCheckAt)
            {
                navCheckAt = Time.time + 1f;
                navReady = NavMesh.SamplePosition(transform.position, out _, 2.5f, NavMesh.AllAreas);
                if (navReady && path == null) { path = new NavMeshPath(); probePath = new NavMeshPath(); }
            }
            return navReady;
        }

        static Vector3 NavPoint(Vector3 p) => NavMesh.SamplePosition(p, out NavMeshHit hit, 2.5f, NavMesh.AllAreas) ? hit.position : p;

        static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

        static float Length(NavMeshPath p)
        {
            float sum = 0f;
            var c = p.corners;
            for (int i = 1; i < c.Length; i++) sum += Vector3.Distance(c[i - 1], c[i]);
            return sum;
        }

        /// <summary>
        /// One frame of going somewhere on foot: along the NavMesh way, sensing how tight it is round it (corridor,
        /// doorway, ceiling), posing the gait for it and keeping the body out of the walls. Returns the way left (m).
        /// </summary>
        float Travel(Vector3 target, float speed, Gait gait, float dt)
        {
            float room = CeilingRoom();
            SenseTightness(dt);
            UpdateUpright(dt, true, room);
            // squeezing through, it slows down a little
            float squeeze = Mathf.Max(corridorK * 0.25f, doorK * 0.4f);
            float left = MoveTo(target, speed * (1f - squeeze), dt);
            CheckStuck(target, left);
            if (!Animated) return left;   // in the dark it is only its sound: the body is posed when the light finds it
            PoseMove(gait, speed * (1f - squeeze), dt);
            ResolveBody();
            FitBody();
            FitHeadroom(dt);
            return left;
        }

        /// <summary>Walks the NavMesh way towards <paramref name="target"/>; returns how far is left of the way it found.</summary>
        float MoveTo(Vector3 target, float speed, float dt)
        {
            heldBack = false;
            if (dashThroughDoorways && Doorway.At(transform.position, 0.5f)) speed = Mathf.Max(speed, doorwaySpeed);
            if (Time.time >= repathAt || Flat(target - pathGoal).sqrMagnitude > 0.36f)
            {
                repathAt = Time.time + 0.3f;
                pathGoal = target;
                Vector3 to = NavMesh.SamplePosition(target, out NavMeshHit th, 3f, NavMesh.AllAreas) ? th.position : target;
                if (NavMesh.CalculatePath(NavPoint(transform.position), to, NavMesh.AllAreas, path) && path.corners.Length > 0)
                {
                    corners = path.corners;
                    corner = 1;
                }
                else { corners = new Vector3[0]; corner = 0; }
            }
            if (corner >= corners.Length) return 0f;

            Vector3 me = transform.position;
            float step = speed * dt;
            Vector3 heading = Vector3.zero;
            int cornerWas = corner;
            while (corner < corners.Length && step > 0f)
            {
                Vector3 c = corners[corner];
                Vector3 d = Flat(c - me);
                float dist = d.magnitude;
                if (dist > 1e-4f) heading = d / dist;
                if (dist <= step) { me = c; step -= dist; corner++; continue; }
                me += heading * step;
                me.y = Mathf.MoveTowards(me.y, c.y, speed * dt);
                step = 0f;
            }
            // hunting in the dark: never a step out into the light the player has on the corner (they would see it come
            // round it before it froze); it waits behind it until the light is gone
            heldBack = (mode == Mode.Hunt || mode == Mode.Approach) && !seen && WouldBeSeen(me + heading * (0.4f * bodySize) - transform.position);
            if (heldBack)
            {
                corner = cornerWas;   // not a step taken
                stuckFor = 0f;
                stuckFrom = transform.position;
                float wait = 0f;
                Vector3 from = transform.position;
                for (int i = corner; i < corners.Length; i++) { wait += Flat(corners[i] - from).magnitude; from = corners[i]; }
                return Mathf.Max(wait, 0.7f);
            }
            transform.position = me;
            if (heading.sqrMagnitude > 0.01f)
                transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(heading, Vector3.up), turnSpeed * dt);

            float left = 0f;
            Vector3 at = me;
            for (int i = corner; i < corners.Length; i++) { left += Flat(corners[i] - at).magnitude; at = corners[i]; }
            return left;
        }

        void FaceTowards(Vector3 p, float dt, float rate)
        {
            Vector3 to = Flat(p - transform.position);
            if (to.sqrMagnitude > 0.01f)
                transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(to, Vector3.up), turnSpeed * rate * dt);
        }

        /// <summary>How wide it is round it (wall to wall, across its heading): a corridor, a door frame, or the open.</summary>
        void SenseTightness(float dt)
        {
            Vector3 chest = transform.position + Vector3.up * Mathf.Min(1.1f, headHeights.y * bodySize);
            float L = Physics.Raycast(chest, -transform.right, out RaycastHit hl, 3f, sightMask, QueryTriggerInteraction.Ignore) ? hl.distance : 3f;
            float R = Physics.Raycast(chest, transform.right, out RaycastHit hr, 3f, sightMask, QueryTriggerInteraction.Ignore) ? hr.distance : 3f;
            // a little ahead too, so it is already low when its head gets there
            Vector3 ahead = chest + transform.forward * (0.6f * bodySize);
            float La = Physics.Raycast(ahead, -transform.right, out hl, 3f, sightMask, QueryTriggerInteraction.Ignore) ? hl.distance : 3f;
            float Ra = Physics.Raycast(ahead, transform.right, out hr, 3f, sightMask, QueryTriggerInteraction.Ignore) ? hr.distance : 3f;
            float width = Mathf.Min(L + R, La + Ra);
            bool door = Doorway.At(transform.position, 0.6f * bodySize) || width < doorWidth;
            float wantCorridor = width < corridorWidth ? Mathf.InverseLerp(corridorWidth, doorWidth, width) * 0.6f + 0.4f : 0f;
            corridorK = Mathf.MoveTowards(corridorK, door ? 0.3f : wantCorridor, dt * 2.5f);
            doorK = Mathf.MoveTowards(doorK, door ? 1f : 0f, dt * 3f);
        }

        /// <summary>Clear height over it (m): straight up from its feet and from over its head, the lower.</summary>
        float CeilingRoom()
        {
            float Up(Vector3 at) => Physics.Raycast(at + Vector3.up * 0.3f, Vector3.up, out RaycastHit hit, 20f, sightMask, QueryTriggerInteraction.Ignore)
                ? hit.distance + 0.3f : 20f;
            float room = Up(transform.position);
            if (head) room = Mathf.Min(room, Up(new Vector3(head.position.x, transform.position.y, head.position.z)));
            return room;
        }

        /// <summary>
        /// Keeps the body out of the walls: a capsule from the hips to the head, pushed out of whatever solid it overlaps
        /// (sideways only, and never off the NavMesh).
        /// </summary>
        bool ResolveBody()
        {
            if (!probe || !hips || !head || !probe.enabled) return false;
            float r = bodyRadius * (model ? model.lossyScale.y : bodySize);
            Vector3 a = hips.position, b = head.position;
            Vector3 axis = b - a;
            float len = axis.magnitude;
            if (len < 1e-3f) return false;
            probe.transform.position = (a + b) * 0.5f;
            probe.transform.rotation = Quaternion.FromToRotation(Vector3.up, axis / len);
            probe.radius = r;
            probe.height = len + 2f * r;
            probe.center = Vector3.zero;
            int n = Physics.OverlapCapsuleNonAlloc(a, b, r, overlaps, sightMask, QueryTriggerInteraction.Ignore);
            Vector3 push = Vector3.zero;
            for (int i = 0; i < n; i++)
            {
                Collider c = overlaps[i];
                if (!c || c.transform.IsChildOf(transform)) continue;
                if (c.attachedRigidbody && !c.attachedRigidbody.isKinematic) continue;   // loose props get shoved, not the monster
                if (Physics.ComputePenetration(probe, probe.transform.position, probe.transform.rotation,
                                               c, c.transform.position, c.transform.rotation, out Vector3 dir, out float depth))
                    push += Flat(dir) * depth;
            }
            if (push.sqrMagnitude < 1e-6f) return false;
            push = Vector3.ClampMagnitude(push, 0.25f);
            Vector3 from = transform.position, to = from + push;
            if (NavReady())
            {
                if (NavMesh.Raycast(from, to, out NavMeshHit hit, NavMesh.AllAreas)) to = hit.position;
                if (!NavMesh.SamplePosition(to, out NavMeshHit on, 0.3f, NavMesh.AllAreas)) return false;
                to.y = on.position.y;
            }
            transform.position = to;
            return true;
        }

        /// <summary>
        /// The pose fitted into the room without breaking it (<see cref="ManthingBodyFit"/>): the back bent out of the walls,
        /// the feet down on the floor, the hands flat on a wall or the floor they are in or near.
        /// </summary>
        void FitBody()
        {
            if (bodyFit == null || !model) return;
            bodyFit.Fit(model.lossyScale.y);
        }

        /// <summary>Under a ceiling too low even for its hunch it shrinks (only ever while unseen); with room it grows back.</summary>
        void FitHeadroom(float dt)
        {
            if (!fitUnderCeiling || !model || !head) return;
            float room = CeilingRoom();
            float top = head.position.y + 0.18f * model.lossyScale.y - transform.position.y;
            float now = model.localScale.x;
            // the pose scales with the model: the scale that just fits is in proportion
            float want = top > 0.1f ? now * (room - 0.1f) / top : fullScale;
            want = Mathf.Clamp(want, fullScale * 0.55f, fullScale);
            model.localScale = Vector3.one * Mathf.MoveTowards(now, want, dt * (want < now ? 3f : 1f));
        }

        /// <summary>No NavMesh: straight for the player (through the doorway when out of sight), sliding along walls.</summary>
        void Direct(float dt)
        {
            chaseTime += dt;
            float speed = Mathf.Lerp(startSpeed, topSpeed, Mathf.Clamp01(chaseTime / Mathf.Max(rampTime, 0.1f)));

            Vector3 me = transform.position;
            Vector3 target = player.transform.position;
            Vector3 chest = me + Vector3.up * 1.4f, playerChest = target + Vector3.up * 1.2f;
            if (Physics.Linecast(chest, playerChest, sightMask, QueryTriggerInteraction.Ignore))
                target = doorway;   // round the corner: through the door first
            Vector3 to = Flat(target - me);
            float dist = to.magnitude;

            Vector3 toPlayer = Flat(player.transform.position - me);
            if (toPlayer.magnitude <= killDistance) { StartCoroutine(Kill()); return; }

            if (dist > 0.05f)
            {
                Vector3 dir = to / dist;
                transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(dir, Vector3.up), turnSpeed * dt);
                Vector3 step = dir * Mathf.Min(speed * dt, dist);
                // do not walk into walls: slide along them
                if (Physics.SphereCast(me + Vector3.up * (1f * bodySize), 0.35f * bodySize, dir, out RaycastHit hit, step.magnitude + 0.05f, sightMask, QueryTriggerInteraction.Ignore))
                    step = Vector3.ProjectOnPlane(step, hit.normal);
                transform.position = me + new Vector3(step.x, 0f, step.z);
            }

            float room = CeilingRoom();
            SenseTightness(dt);
            UpdateUpright(dt, true, room);
            PoseMove(speed > (startSpeed + topSpeed) * 0.5f ? Gait.Charge : Gait.Creep, speed, dt);
            ResolveBody();
            FitBody();
            FitHeadroom(dt);
            Stamp(dt, toPlayer.magnitude, 1f, Mathf.Clamp01(chaseTime / Mathf.Max(rampTime, 0.1f)) > 0.5f);
        }

        // ================================================================== sound

        /// <summary>Breathes while it stands still: ragged and quiet when caught, hard and loud at the edge of the light, barely while hiding.</summary>
        void Breathe(float dt)
        {
            if (!breathSource) return;
            bool lurking = phase == Phase.Active && ((mode == Mode.Prowl || (mode == Mode.ValveWatch && rushed))
                                                     && (Time.time < pauseUntil || onScreen || !goalSet));
            bool hiding = phase == Phase.Active && mode == Mode.Ambush;
            bool still = seen || lurking || hiding;
            if (mode == Mode.Scream || mode == Mode.Retreat || mode == Mode.Hush) still = false;
            float want = !still ? 0f : hiding && !seen ? hidingBreathVolume : lurking ? lurkBreathVolume : breathVolume;
            breathLevel = Mathf.MoveTowards(breathLevel, want, dt * (!still ? 4f : lurking ? 2f : 0.8f));   // fades in, stops at once
            breathSource.volume = breathLevel;
            breathSource.maxDistance = lurking ? lurkBreathRange : breathRange;
            if (!still)
            {
                if (breathLevel <= 0.001f && breathSource.isPlaying) breathSource.Stop();
                breathWait = Random.Range(0.2f, 0.6f);   // when caught again, a beat of silence, then it breathes
                return;
            }
            if (breathSource.isPlaying) return;
            breathWait -= dt;
            if (breathWait > 0f) return;
            int i = Random.Range(0, breaths.Length);
            if (breaths.Length > 1 && i == lastBreath) i = (i + 1) % breaths.Length;
            lastBreath = i;
            breathSource.clip = breaths[i];
            breathSource.pitch = lurking ? Random.Range(0.8f, 0.9f) : Random.Range(0.9f, 1.03f);
            breathSource.Play();
            breathWait = lurking ? Random.Range(lurkBreathGap.x, lurkBreathGap.y) : Random.Range(breathGap.x, breathGap.y);
        }

        /// <summary>A step every so often: quick and heavy charging, slower creeping; <paramref name="loudness"/> of the full.
        /// <paramref name="pace"/> &gt; 0: a step every this long instead; <paramref name="pitch"/> scales the step's pitch (lower = heavier).</summary>
        void Stamp(float dt, float distance, float loudness, bool charge, float pace = 0f, float pitch = 1f)
        {
            if (steps == null || steps.Length == 0) return;
            if (stepFilter)
                stepFilter.cutoffFrequency = Mathf.Lerp(stepMuffle.x, stepMuffle.y, Mathf.Pow(1f - Mathf.InverseLerp(stepNear, stepFar, distance), 2f));
            stepTimer -= dt;
            if (stepTimer > 0f) return;
            float close = Mathf.InverseLerp(fearFar, fearNear, distance);
            float gap = pace > 0f ? pace : charge ? stepInterval.y : Mathf.Lerp(stepInterval.x, stepInterval.x * 0.8f, close);
            stepTimer = gap * Random.Range(0.85f, 1.2f);   // uneven: it does not walk like a person

            int i = Random.Range(0, steps.Length);
            if (steps.Length > 1 && i == lastStep) i = (i + 1) % steps.Length;
            lastStep = i;
            stepSource.pitch = Random.Range(0.85f, 1f) * pitch;
            stepSource.PlayOneShot(steps[i], stepLoudness * loudness);   // the distance does the rest
            if (rig && distance < joltRange && loudness > 0.5f) rig.AddTrauma(0.05f + 0.12f * (1f - distance / joltRange));
        }

        /// <summary>A palm slapped down on the floor as it drops onto its hands.</summary>
        void HandSlap()
        {
            if (steps == null || steps.Length == 0 || !stepSource) return;
            float loud = stealthy ? stealthLoudness : 0.7f;
            stepSource.pitch = Random.Range(1.1f, 1.25f);
            stepSource.PlayOneShot(steps[Random.Range(0, steps.Length)], stepLoudness * loud * 0.7f);
        }

        void FacePlayer()
        {
            if (!player) return;
            Vector3 to = Flat(player.transform.position - transform.position);
            if (to.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(to, Vector3.up);
        }

        // ================================================================== seeing

        /// <summary>Some part of it is on screen, with nothing in the way, and lit.</summary>
        bool CheckSeen()
        {
            if (!cam || samplePoints == null) return false;
            if (Time.time >= lightsRefresh) RefreshLights();
            Vector3 eye = cam.transform.position;
            foreach (Transform t in samplePoints)
            {
                Vector3 p = t.position;
                Vector3 v = cam.WorldToViewportPoint(p);
                if (v.z <= 0.05f || v.x < -0.02f || v.x > 1.02f || v.y < -0.02f || v.y > 1.02f) continue;
                if (Physics.Linecast(eye, p, sightMask, QueryTriggerInteraction.Ignore)) continue;
                if (LightAt(p) >= litThreshold) return true;
            }
            return false;
        }

        /// <summary>Held back this frame: the next step would have put it in the light in front of the player.</summary>
        bool heldBack;

        /// <summary>
        /// Whether, moved by <paramref name="offset"/>, some part of it would be on screen with nothing in the way and lit
        /// (a little more readily than <see cref="CheckSeen"/>: half lit is enough, so it stops short of the light's edge).
        /// </summary>
        bool WouldBeSeen(Vector3 offset)
        {
            if (!cam || samplePoints == null) return false;
            if (Time.time >= lightsRefresh) RefreshLights();
            Vector3 eye = cam.transform.position;
            foreach (Transform t in samplePoints)
            {
                Vector3 p = t.position + offset;
                Vector3 v = cam.WorldToViewportPoint(p);
                if (v.z <= 0.05f || v.x < -0.05f || v.x > 1.05f || v.y < -0.05f || v.y > 1.05f) continue;
                if (Physics.Linecast(eye, p, sightMask, QueryTriggerInteraction.Ignore)) continue;
                if (LightAt(p) >= litThreshold * 0.5f) return true;
            }
            return false;
        }

        /// <summary>Some part of it is on the player's screen with nothing in the way, lit or not (checked a few times a second).</summary>
        bool OnScreen()
        {
            if (Time.time < sightCheckAt) return onScreen;
            sightCheckAt = Time.time + 0.1f;
            onScreen = false;
            if (!cam || samplePoints == null) return false;
            Vector3 eye = cam.transform.position;
            foreach (Transform t in samplePoints)
            {
                Vector3 v = cam.WorldToViewportPoint(t.position);
                if (v.z <= 0.05f || v.x < -0.05f || v.x > 1.05f || v.y < -0.05f || v.y > 1.05f) continue;
                if (Physics.Linecast(eye, t.position, sightMask, QueryTriggerInteraction.Ignore)) continue;
                onScreen = true;
                break;
            }
            return onScreen;
        }

        /// <summary>Whether a point is inside the player's view (in front, on screen), walls aside.</summary>
        bool OnCamera(Vector3 p)
        {
            Vector3 v = cam.WorldToViewportPoint(p);
            return v.z > 0.05f && v.x > -0.1f && v.x < 1.1f && v.y > -0.1f && v.y < 1.1f;
        }

        void RefreshLights()
        {
            lightsRefresh = Time.time + 0.5f;
            lights.Clear();
            foreach (var l in FindObjectsByType<Light>()) if (!l.GetComponent<MonsterBlindLight>()) lights.Add(l);
        }

        float LightAt(Vector3 p) => LightAt(p, lights, litThreshold);

        /// <summary>Roughly how much of <paramref name="from"/>'s light falls on <paramref name="p"/> (only lights that light the world, not the hands).</summary>
        float LightAt(Vector3 p, List<Light> from, float litThreshold)
        {
            float sum = 0f;
            foreach (Light l in from)
            {
                if (!l || !l.isActiveAndEnabled || l.intensity <= 0f) continue;
                if ((l.renderingLayerMask & (int)ViewModelLighting.WorldLayer) == 0) continue;
                if (l.type == LightType.Directional) { sum += l.intensity * 0.2f; continue; }
                Vector3 d = p - l.transform.position;
                float dist = d.magnitude;
                if (dist > l.range || dist < 1e-3f) continue;
                float k = l.intensity / Mathf.Max(dist * dist, 0.25f);
                float fall = 1f - Mathf.Pow(dist / l.range, 4f);
                k *= fall * fall;
                if (l.type == LightType.Spot)
                {
                    float angle = Vector3.Angle(l.transform.forward, d);
                    float outer = l.spotAngle * 0.5f, inner = Mathf.Min(l.innerSpotAngle * 0.5f, outer - 0.1f);
                    if (angle > outer) continue;
                    k *= Mathf.InverseLerp(outer, inner, angle);
                }
                if (k < litThreshold * 0.25f) continue;
                if (l.shadows != LightShadows.None && Physics.Linecast(l.transform.position, p, sightMask, QueryTriggerInteraction.Ignore)) continue;
                sum += k;
                if (sum >= litThreshold) break;
            }
            return sum;
        }

        // ================================================================== fear

        /// <summary>
        /// Fear comes from looking at it, not from how near it is: the longer the player holds it in their eyes, the
        /// higher it climbs (and it ebbs once they look away). Its scream, its rage and the end are full fright.
        /// </summary>
        public float FearFor(Vector3 eye)
        {
            if (phase == Phase.Waiting || phase == Phase.Dead || mode == Mode.Hush) return 0f;
            if (phase == Phase.Killing || mode == Mode.Scream || mode == Mode.Enraged) return 1f;
            return lookFearMax * (1f - Mathf.Exp(-lookTime / Mathf.Max(lookFearTime, 0.1f)));
        }

        /// <summary>How long the player has been looking at it (grows while they do, ebbs while they do not).</summary>
        void UpdateLookTime(float dt)
        {
            bool looking = phase == Phase.Active && WatchedByPlayer();
            // (capped where the fear is near its top, so looking away brings it down without a long wait first)
            lookTime = looking ? Mathf.Min(lookTime + dt, lookFearTime * 2.5f) : Mathf.Max(0f, lookTime - dt * lookFearEbb);
        }

        /// <summary>
        /// The player has it in their eyes: some part of it near the middle of the screen, nothing in the way, and
        /// something to see it by (the flashlight or a lamp on it, or the chemical light at a safe zone's edge).
        /// </summary>
        bool WatchedByPlayer()
        {
            if (!seen && mode != Mode.Prowl && mode != Mode.ValveWatch && mode != Mode.Rampage) return false;
            return InEyes();
        }

        /// <summary>Some part of it near the middle of the screen with nothing in the way, lit or not.</summary>
        bool InEyes()
        {
            if (!cam || samplePoints == null) return false;
            Vector3 eye = cam.transform.position;
            foreach (Transform t in samplePoints)
            {
                Vector3 v = cam.WorldToViewportPoint(t.position);
                if (v.z <= 0.05f || new Vector2(v.x - 0.5f, v.y - 0.5f).magnitude > lookFearCone) continue;
                if (Physics.Linecast(eye, t.position, sightMask, QueryTriggerInteraction.Ignore)) continue;
                return true;
            }
            return false;
        }

        // ================================================================== the end

        IEnumerator Kill()
        {
            phase = Phase.Killing;
            if (breathSource) breathSource.Stop();
            if (rageSource) rageSource.Stop();   // the scream in the face cuts it off
            var shooting = player.GetComponentInChildren<PlayerShooting>();
            player.enabled = false;
            if (shooting) shooting.enabled = false;
            if (model) model.localScale = Vector3.one * fullScale;
            leanK = corridorK = doorK = 0f;

            // in the face, screaming
            Transform eye = cam ? cam.transform : player.transform;
            Vector3 flat = Vector3.ProjectOnPlane(eye.forward, Vector3.up).normalized;
            if (flat.sqrMagnitude < 0.01f) flat = player.transform.forward;
            transform.position = new Vector3(eye.position.x, player.transform.position.y, eye.position.z) + flat * 0.75f;
            transform.rotation = Quaternion.LookRotation(-flat, Vector3.up);
            AudioClip faceScream = killScream ? killScream : scream;
            if (faceScream) Audio.SpatialSound.PlayAt(faceScream, eye.position + flat * 0.5f, 1f, 1f, 100f);
            if (rig) rig.AddTrauma(1f);

            float t = 0f;
            Vector3 faceAt = eye.position + flat * 0.7f;
            // in the face for as long as it screams (the fade to black takes the last of it)
            float hold = Mathf.Max(screamTime, faceScream ? faceScream.length - fadeTime : 0f);
            while (t < hold)
            {
                t += Time.deltaTime;
                if (iScreech >= 0) Show(iScreech, 0.4f + t);
                // a body this tall: the head, not the chest, is what fills the view
                if (head) transform.position += faceAt - head.position;
                LookAtPlayer();
                yield return null;
            }
            while (fade < 1f)
            {
                fade = Mathf.MoveTowards(fade, 1f, Time.deltaTime / Mathf.Max(fadeTime, 0.01f));
                yield return null;
            }
            yield return new WaitForSeconds(0.5f);
            var scene = SceneManager.GetActiveScene();
            if (scene.buildIndex >= 0) SceneManager.LoadScene(scene.buildIndex);
            else SceneManager.LoadScene(scene.name);
        }

        static Texture2D black;

        void OnGUI()
        {
            if (fade <= 0f) return;
            if (!black) { black = new Texture2D(1, 1); black.SetPixel(0, 0, Color.black); black.Apply(); }
            var was = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, fade);
            GUI.depth = -1000;
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), black);
            GUI.color = was;
        }

        void OnDrawGizmosSelected()
        {
            if (!Application.isPlaying) return;
            Gizmos.color = Color.red;
            if (goalSet) Gizmos.DrawWireSphere(goal, 0.3f);
            Gizmos.color = Color.cyan;
            if (mode == Mode.Ambush) { Gizmos.DrawWireSphere(cover, 0.25f); Gizmos.DrawLine(cover, peekSpot); }
        }
    }
}
