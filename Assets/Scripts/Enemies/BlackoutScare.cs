using System.Collections;
using System.Collections.Generic;
using BodycamHorror.Audio;
using BodycamHorror.Environment;
using BodycamHorror.Player;
using BodycamHorror.Weapons;
using UnityEngine;

namespace BodycamHorror.Enemies
{
    /// <summary>
    /// The long corridor's scare. The player knocks the bottles over (<see cref="trap"/>); a moment later the lamps
    /// go out with a loud click. In the dark the head-banger is moved from the end wall to <see cref="runFrom"/>,
    /// behind the player, and set running: fast footsteps come up from behind. The lamps click back on, the end wall
    /// is empty, and the zombie is coming down the corridor at the player's back.
    /// The trap's own wake-up should be off (its Wake Radius 0): this scare decides what the zombie does.
    /// </summary>
    public class BlackoutScare : MonoBehaviour
    {
        [Header("What sets it off")]
        public NoiseTrap trap;
        [Tooltip("From the bottles going over to the lights going out (s).")]
        public float offDelay = 0.7f;

        [Header("The zombie")]
        [Tooltip("The head-banger's spawner: the zombie it spawned is the one that vanishes and comes from behind.")]
        public InfectedSpawner banger;
        [Tooltip("Where it comes from, on the floor, behind the player (the gizmo's red ball). Keep it in the corridor " +
                 "itself, with a clear straight run to the player: the zombie does not find its way round corners.")]
        public Vector3 runFrom;

        [Header("Lights")]
        [Tooltip("Lamps that go out together (whole lamp objects: lights, glowing parts and haze).")]
        public Transform[] lamps;
        [Tooltip("How long it stays dark (s).")]
        public float darkTime = 0.9f;
        [Tooltip("The switch's click. Empty: a generated one.")]
        public AudioClip clickClip;
        [Range(0f, 1f)] public float clickVolume = 1f;
        [Tooltip("Share of the click heard flat in the ears, wherever the player stands: it should be loud.")]
        [Range(0f, 1f)] public float clickInEars = 0.5f;

        [Header("Footsteps")]
        [Tooltip("A recording of running feet, looped on the zombie while it runs. Set: used instead of Steps.")]
        public AudioClip runLoop;
        [Tooltip("Where in the recording the loop starts (s): skips a lead-in.")]
        public float runLoopStart = 0f;
        [Tooltip("Run Loop: its level where the run starts, as a share of Step Volume; it rises to full as the zombie " +
                 "closes in (in place of the usual rolloff).")]
        [Range(0f, 1f)] public float farLevel = 0.12f;
        [Tooltip("Run Loop: how the level rises on the way in. 1 = evenly, higher = quiet for longer, then fast at the end.")]
        public float approachCurve = 2f;
        [Tooltip("Run Loop: the corridor's echo. Delay between repeats (ms) and how much each repeat keeps (0..1).")]
        public float echoDelay = 170f;
        [Range(0f, 1f)] public float echoDecay = 0.5f;
        [Tooltip("Run Loop: echo level far off and right behind: the echo is plain from afar, the feet drown it near.")]
        [Range(0f, 1f)] public float echoFar = 0.8f;
        [Range(0f, 1f)] public float echoNear = 0.35f;
        [Tooltip("Single footsteps, one every Step Interval, when there is no Run Loop.")]
        public AudioClip[] steps;
        [Range(0f, 1f)] public float stepVolume = 1f;
        [Tooltip("Seconds between steps at a run.")]
        public float stepInterval = 0.24f;
        public float stepPitch = 0.85f;
        [Tooltip("The footsteps stop this close to the player, or after this many seconds.")]
        public float stepsStopDistance = 2f;
        public float stepsMaxTime = 8f;

        [Header("Player")]
        [Range(0f, 1f)] public float stress = 0.7f;

        bool fired;
        AudioSource ears;
        static AudioClip generatedClick;

        struct Saved { public Renderer r; public MaterialPropertyBlock block; public bool hadBlock; }
        readonly List<Light> litLights = new List<Light>();
        readonly List<LightFlicker> runningFlickers = new List<LightFlicker>();
        readonly List<Renderer> shownHaze = new List<Renderer>();
        readonly List<LightHaze> runningHaze = new List<LightHaze>();
        readonly List<Saved> glows = new List<Saved>();

        static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

        void Awake()
        {
            ears = gameObject.AddComponent<AudioSource>();
            ears.playOnAwake = false;
            ears.spatialBlend = 0f;
            ScriptedSound.Mark(ears);
        }

        void Update()
        {
            if (fired || !trap || !trap.Sprung) return;
            fired = true;
            StartCoroutine(Run());
        }

        [ContextMenu("Fire")]
        public void Fire()
        {
            if (fired) return;
            fired = true;
            StartCoroutine(Run());
        }

        IEnumerator Run()
        {
            yield return new WaitForSeconds(offDelay);
            var playerCtl = FindAnyObjectByType<BodycamPlayerController>();
            Transform player = playerCtl ? playerCtl.transform : null;

            Click();
            LightsOut();

            Infected zombie = banger && banger.Alive.Count > 0 ? banger.Alive[0] : null;
            if (zombie && !zombie.IsDead)
            {
                MoveBehind(zombie, player);
                zombie.Alert(false, false);
                StartCoroutine(Footsteps(zombie, player));
            }
            var stressed = player ? player.GetComponentInChildren<PlayerStress>() : null;
            if (stressed) stressed.Add(stress);

            yield return new WaitForSeconds(darkTime);
            Click();
            LightsOn();
        }

        void MoveBehind(Infected zombie, Transform player)
        {
            Vector3 to = player ? player.position - runFrom : Vector3.forward;
            to.y = 0f;
            Quaternion face = to.sqrMagnitude > 0.01f ? Quaternion.LookRotation(to) : zombie.transform.rotation;
            var rb = zombie.GetComponent<Rigidbody>();
            if (rb)
            {
                rb.position = runFrom;
                rb.rotation = face;
                if (!rb.isKinematic) rb.linearVelocity = Vector3.zero;
            }
            zombie.transform.SetPositionAndRotation(runFrom, face);
            Physics.SyncTransforms();
        }

        IEnumerator Footsteps(Infected zombie, Transform player)
        {
            if (runLoop)
            {
                yield return RunLoop(zombie, player);
                yield break;
            }
            if (steps == null || steps.Length == 0) yield break;
            float until = Time.time + stepsMaxTime;
            int last = -1;
            while (zombie && !zombie.IsDead && Time.time < until)
            {
                if (player && Vector3.Distance(zombie.transform.position, player.position) < stepsStopDistance) yield break;
                int i = Random.Range(0, steps.Length);
                if (i == last && steps.Length > 1) i = (i + 1) % steps.Length;
                last = i;
                var src = SpatialSound.PlayAt(steps[i], zombie.transform.position, stepVolume * Random.Range(0.9f, 1f), 3f, 45f);
                if (src) src.pitch = stepPitch * Random.Range(0.93f, 1.07f);
                yield return new WaitForSeconds(stepInterval * Random.Range(0.9f, 1.1f));
            }
        }

        /// <summary>The running recording, looped at the zombie's feet; it fades with the zombie's speed and dies away
        /// once the zombie reaches the player, stops, dies or the time is up.</summary>
        IEnumerator RunLoop(Infected zombie, Transform player)
        {
            var go = new GameObject("RunFootsteps");   // its own object: the spatial filter serves one source per object
            go.transform.SetParent(zombie.transform, false);
            go.transform.localPosition = new Vector3(0f, 0.1f, 0f);
            var src = go.AddComponent<AudioSource>();
            src.clip = runLoop;
            src.loop = true;
            src.playOnAwake = false;
            src.spatialBlend = 1f;
            src.dopplerLevel = 0f;
            // the level is driven here by distance (Approach): the rolloff stays flat so the two do not stack
            src.rolloffMode = AudioRolloffMode.Custom;
            src.SetCustomCurve(AudioSourceCurveType.CustomRolloff, AnimationCurve.Constant(0f, 1f, 1f));
            src.minDistance = 1f;
            src.maxDistance = 100f;
            src.pitch = stepPitch;
            // the echo first, so the ears place the repeats with the feet
            var echo = go.AddComponent<AudioEchoFilter>();
            echo.delay = echoDelay;
            echo.decayRatio = echoDecay;
            echo.dryMix = 1f;
            echo.wetMix = echoFar;
            if (SpatialSoundInstaller.Enabled) go.AddComponent<SpatialSound>();

            float startDistance = player ? Vector3.Distance(zombie.transform.position, player.position) : 20f;
            float Approach()   // 0 where the run starts .. 1 at the player
            {
                if (!player) return 1f;
                float d = Vector3.Distance(zombie.transform.position, player.position);
                return Mathf.Pow(Mathf.Clamp01(Mathf.InverseLerp(startDistance, stepsStopDistance + 0.5f, d)), Mathf.Max(approachCurve, 0.01f));
            }
            float Loud() => stepVolume * Mathf.Lerp(farLevel, 1f, Approach());
            src.volume = Loud();
            src.time = Mathf.Clamp(runLoopStart, 0f, runLoop.length - 0.1f);
            src.Play();

            float until = Time.time + stepsMaxTime, level = 1f, started = Time.time;
            while (src && zombie && !zombie.IsDead && Time.time < until)
            {
                bool near = player && Vector3.Distance(zombie.transform.position, player.position) < stepsStopDistance;
                // the first moment it is still getting up to speed: keep the feet going
                bool running = Time.time - started < 0.6f || zombie.HorizontalSpeed > 1.5f;
                float target = near || !running ? 0f : 1f;
                level = Mathf.MoveTowards(level, target, Time.deltaTime / (target > level ? 0.1f : 0.35f));
                src.volume = Loud() * level;
                echo.wetMix = Mathf.Lerp(echoFar, echoNear, Approach());
                if (near && level <= 0f) break;
                yield return null;
            }
            while (src && src.volume > 0.001f)   // dies away
            {
                src.volume = Mathf.MoveTowards(src.volume, 0f, Time.deltaTime * stepVolume / 0.35f);
                yield return null;
            }
            if (go) Destroy(go, echoDelay * 0.001f * 8f);   // let the echo ring out
        }

        void Click()
        {
            AudioClip clip = clickClip ? clickClip
                : generatedClick ? generatedClick : (generatedClick = ProceduralAudio.Click(0.06f, 850f, 1f));
            Vector3 at = transform.position + Vector3.up * 2.4f;
            if (lamps != null)
                foreach (Transform lamp in lamps)
                    if (lamp) { at = lamp.position; break; }
            var src = SpatialSound.PlayAt(clip, at, clickVolume, 10f, 70f);
            if (src) src.pitch = Random.Range(0.95f, 1.05f);
            if (clickInEars > 0f) ears.PlayOneShot(clip, clickVolume * clickInEars);
        }

        void LightsOut()
        {
            litLights.Clear(); runningFlickers.Clear(); shownHaze.Clear(); runningHaze.Clear(); glows.Clear();
            if (lamps == null) return;
            foreach (Transform lamp in lamps)
            {
                if (!lamp) continue;
                foreach (var f in lamp.GetComponentsInChildren<LightFlicker>(true))
                    if (f.enabled) { f.enabled = false; runningFlickers.Add(f); }   // its OnDisable puts the glow back first
                foreach (var l in lamp.GetComponentsInChildren<Light>(true))
                    if (l.enabled) { l.enabled = false; litLights.Add(l); }
                foreach (var h in lamp.GetComponentsInChildren<LightHaze>(true))
                {
                    foreach (var r in h.GetComponentsInChildren<Renderer>(true))
                        if (r.enabled) { r.enabled = false; shownHaze.Add(r); }
                    if (h.enabled) { h.enabled = false; runningHaze.Add(h); }
                }
                foreach (var r in lamp.GetComponentsInChildren<Renderer>(true))
                {
                    if (!r.enabled || !r.sharedMaterial || !r.sharedMaterial.HasProperty(EmissionId)) continue;
                    var saved = new Saved { r = r, hadBlock = r.HasPropertyBlock(), block = new MaterialPropertyBlock() };
                    if (saved.hadBlock) r.GetPropertyBlock(saved.block);
                    glows.Add(saved);
                    var dark = new MaterialPropertyBlock();
                    r.GetPropertyBlock(dark);
                    dark.SetColor(EmissionId, Color.black);
                    r.SetPropertyBlock(dark);
                }
            }
        }

        void LightsOn()
        {
            foreach (var s in glows) if (s.r) s.r.SetPropertyBlock(s.hadBlock ? s.block : null);
            foreach (var l in litLights) if (l) l.enabled = true;
            foreach (var r in shownHaze) if (r) r.enabled = true;
            foreach (var h in runningHaze) if (h) h.enabled = true;
            foreach (var f in runningFlickers) if (f) f.enabled = true;
        }

        void OnDrawGizmos()
        {
            Gizmos.color = new Color(1f, 0.15f, 0.1f, 0.9f);
            Gizmos.DrawSphere(runFrom + Vector3.up * 0.1f, 0.2f);
            Gizmos.DrawWireCube(runFrom + Vector3.up * 0.9f, new Vector3(0.5f, 1.8f, 0.5f));
            if (trap)
            {
                Gizmos.color = new Color(1f, 0.15f, 0.1f, 0.4f);
                Gizmos.DrawLine(runFrom + Vector3.up * 0.9f, trap.transform.position + Vector3.up * 0.9f);
            }
            if (lamps == null) return;
            Gizmos.color = new Color(1f, 0.8f, 0.2f, 0.6f);
            foreach (Transform lamp in lamps) if (lamp) Gizmos.DrawLine(transform.position + Vector3.up * 2f, lamp.position);
        }
    }
}
