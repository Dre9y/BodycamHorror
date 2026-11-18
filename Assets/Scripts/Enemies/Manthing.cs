using System.Collections;
using System.Collections.Generic;
using BodycamHorror.Environment;
using BodycamHorror.Player;
using BodycamHorror.Weapons;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;

namespace BodycamHorror.Enemies
{
    /// <summary>
    /// "Now you see me": a tall thing that only moves while nobody sees it. Seen means some part of it is on screen,
    /// with nothing in the way, and lit (by the flashlight, a lamp): in the dark it comes on even in plain view.
    /// Seen, it stands frozen in one of its poses (a moment of a run, a stoop, an upright stare), a new one every time.
    /// Unseen, it closes in, faster the longer the chase goes on, and its stamping gets louder and quicker as it nears;
    /// the player's fear (heartbeat, shaking hands) climbs with it. Reaching the player is the end: it is in the face,
    /// screaming, the picture goes black and the scene starts over.
    /// Waits, hidden, until <see cref="appearOn"/> bursts (or appears at once without one).
    /// </summary>
    public class Manthing : MonoBehaviour, PlayerStress.IFearSource
    {
        [Header("Body (built by the scene builder)")]
        public Animator animator;
        [Tooltip("Played, unseen, while it moves.")]
        public AnimationClip runClip;
        [Tooltip("The poses it freezes in: a random moment of a random one of these each time it is seen.")]
        public AnimationClip[] freezeClips;
        [Tooltip("In the player's face at the end.")]
        public AnimationClip screechClip;
        [Tooltip("Height of the sole above the lowest foot bone (m): keeps the feet on the floor.")]
        public float soleHeight = 0.03f;
        [Tooltip("Height of its tallest pose at the model's own scale (m). With the model's scale it sets how much headroom it needs.")]
        public float poseHeight = 2.3f;
        [Tooltip("Under a lower ceiling (a corridor) it shrinks to fit, never bigger than the scale it was placed at; only while unseen.")]
        public bool fitUnderCeiling = true;
        [Tooltip("Whatever the pose, the head is turned to the player the moment it freezes (and then holds still).")]
        public bool headFollowsPlayer = true;
        [Tooltip("Share of the turn the neck takes; the head does the rest.")]
        [Range(0f, 1f)] public float neckShare = 0.4f;

        [Header("Appearing")]
        public BurstingBulb appearOn;

        [Header("Moving")]
        public float startSpeed = 2.2f;
        public float topSpeed = 5.2f;
        [Tooltip("Seconds of chasing (unseen) to get from the start speed to the top one.")]
        public float rampTime = 25f;
        [Tooltip("Where it heads when it cannot see the player (the door between the room and the corridor).")]
        public Vector3 doorway;
        public float turnSpeed = 540f;

        [Header("Seen")]
        [Tooltip("Light at a point (intensity / distance², cone and range included) for it to count as lit.")]
        public float litThreshold = 0.3f;
        [Tooltip("Grace after the last sighting before it moves again (s).")]
        public float unseenDelay = 0.06f;

        [Header("Stamping")]
        public AudioClip[] steps;
        [Tooltip("Time between steps (s) far away and close up.")]
        public Vector2 stepInterval = new Vector2(0.4f, 0.19f);
        [Tooltip("Loudness of a step right next to the player (1 = full).")]
        [Range(0f, 1f)] public float stepLoudness = 1f;
        [Tooltip("Within this distance (m) a step is at full loudness ...")]
        public float stepNear = 2.5f;
        [Tooltip("... and fades away to nothing by this distance (m).")]
        public float stepFar = 30f;
        [Tooltip("How steeply the steps fade with distance: higher = quiet sooner, so where it is is easy to hear.")]
        [Range(0.5f, 3f)] public float stepFalloff = 1.2f;
        [Tooltip("Far away the steps also lose their top (muffled), close up they are sharp. Cutoff (Hz) far and near.")]
        public Vector2 stepMuffle = new Vector2(900f, 22000f);

        [Header("Breathing (while it stands frozen)")]
        [Tooltip("Single breaths, played one after another in a random order with uneven gaps while it stands still.")]
        public AudioClip[] breaths;
        [Tooltip("Pause after a breath before the next (s): shortest and longest.")]
        public Vector2 breathGap = new Vector2(0.15f, 1.1f);
        [Range(0f, 1f)] public float breathVolume = 0.35f;
        [Tooltip("Heard within this distance (m).")]
        public float breathRange = 9f;
        [Tooltip("Within this distance (m) every step jolts the camera a little.")]
        public float joltRange = 6f;

        [Header("Fear")]
        [Tooltip("Fear it holds from this far away (m) ...")]
        public float fearFar = 16f;
        [Tooltip("... climbing to the most at this distance.")]
        public float fearNear = 1.5f;

        [Header("The end")]
        public float killDistance = 1.15f;
        public AudioClip scream;
        public float screamTime = 1.1f;
        public float fadeTime = 0.6f;

        enum Phase { Waiting, Hunting, Killing }
        Phase phase = Phase.Waiting;

        BodycamPlayerController player;
        BodycamCameraRig rig;
        Camera cam;
        Transform model;
        PlayableGraph graph;
        AnimationMixerPlayable mixer;
        readonly List<AnimationClipPlayable> clipPlayables = new List<AnimationClipPlayable>();
        int runInput = -1, screechInput = -1;
        readonly List<int> freezeInputs = new List<int>();
        Transform[] samplePoints;
        Transform[] feet;
        Transform head, neck;
        Vector3 headFace, headUp;   // the head's own forward and up (from the model's pose), in head space
        readonly List<Light> lights = new List<Light>();
        float lightsRefresh;
        AudioSource stepSource, breathSource;
        AudioLowPassFilter stepFilter;
        int lastStep = -1;
        float breathLevel, breathWait;
        int lastBreath = -1;

        bool seen;
        float lastSeen = -10f, chaseTime, stepTimer, runTime, fade;
        int sightMask;
        float bodySize = 1f, fullScale = 1f;

        public bool IsSeen => seen;
        public bool IsActive => phase != Phase.Waiting;

        void Start()
        {
            player = FindAnyObjectByType<BodycamPlayerController>();
            rig = player ? player.GetComponentInChildren<BodycamCameraRig>() : null;
            sightMask = ~((1 << 2) | (1 << 8) | (1 << 9) | (1 << 10));   // not the player / itself, hitboxes, the body, chains
            model = animator ? animator.transform : null;
            BuildGraph();

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

            var pusher = gameObject.GetComponent<ChainPusher>() ?? gameObject.AddComponent<ChainPusher>();
            float size = bodySize = model ? model.lossyScale.y : 1f;   // the model may be scaled up
            fullScale = model ? model.localScale.x : 1f;
            pusher.radius = 0.4f * size; pusher.height = 2.2f * size;

            if (appearOn && !appearOn.HasBurst) { SetVisible(false); appearOn.Burst += Appear; }
            else Appear();
        }

        void OnEnable() => PlayerStress.Register(this);

        void OnDisable() => PlayerStress.Unregister(this);

        void OnDestroy()
        {
            if (appearOn) appearOn.Burst -= Appear;
            if (graph.IsValid()) graph.Destroy();
        }

        void SetVisible(bool on)
        {
            foreach (var r in GetComponentsInChildren<Renderer>(true)) r.enabled = on;
            foreach (var c in GetComponentsInChildren<Collider>(true)) c.enabled = on;
        }

        void Appear()
        {
            if (phase != Phase.Waiting) return;
            SetVisible(true);
            phase = Phase.Hunting;
            FacePlayer();
            Freeze();   // it is there, standing, the moment the light goes
        }

        // ------------------------------------------------------------------ animation

        void BuildGraph()
        {
            if (!animator) return;
            // the head's face direction, read off the model's own pose (it looks ahead) before any clip moves it
            head = animator.GetBoneTransform(HumanBodyBones.Head);
            neck = animator.GetBoneTransform(HumanBodyBones.Neck);
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
            if (runClip) { runInput = clips.Count; clips.Add(runClip); }
            if (freezeClips != null) foreach (var c in freezeClips) if (c) { freezeInputs.Add(clips.Count); clips.Add(c); }
            if (screechClip) { screechInput = clips.Count; clips.Add(screechClip); }
            mixer = AnimationMixerPlayable.Create(graph, clips.Count);
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

            var list = new List<Transform>();
            foreach (var b in new[] { HumanBodyBones.Head, HumanBodyBones.Chest, HumanBodyBones.Hips, HumanBodyBones.LeftHand, HumanBodyBones.RightHand,
                                      HumanBodyBones.LeftLowerLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot })
            {
                var t = animator.GetBoneTransform(b);
                if (t) list.Add(t);
            }
            samplePoints = list.ToArray();
            var f = new List<Transform>();
            foreach (var b in new[] { HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot, HumanBodyBones.LeftToes, HumanBodyBones.RightToes })
            {
                var t = animator.GetBoneTransform(b);
                if (t) f.Add(t);
            }
            feet = f.ToArray();
        }

        void Show(int input, double time)
        {
            if (!graph.IsValid() || input < 0) return;
            for (int i = 0; i < clipPlayables.Count; i++) mixer.SetInputWeight(i, i == input ? 1f : 0f);
            clipPlayables[input].SetTime(time);
            graph.Evaluate(0f);
            Ground();
        }

        /// <summary>Feet on the floor whatever the pose: the model moves up or down under the body.</summary>
        void Ground()
        {
            if (!model || feet == null || feet.Length == 0) return;
            float low = float.MaxValue;
            foreach (var t in feet) low = Mathf.Min(low, t.position.y);
            model.position += Vector3.up * (transform.position.y + soleHeight - low);
        }

        /// <summary>Stops dead in a random moment of a random pose.</summary>
        void Freeze()
        {
            if (freezeInputs.Count == 0) return;
            int input = freezeInputs[Random.Range(0, freezeInputs.Count)];
            AnimationClip c = clipPlayables[input].GetAnimationClip();
            Show(input, Random.value * c.length);
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

        // ------------------------------------------------------------------ per frame

        void Update()
        {
            if (phase == Phase.Waiting || !player) return;
            if (!cam) cam = Camera.main;
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            if (phase == Phase.Killing) return;

            bool seenNow = CheckSeen();
            if (seenNow)
            {
                if (!seen) Freeze();   // caught: whatever it was doing, it is a statue now
                lastSeen = Time.time;
            }
            seen = seenNow || Time.time - lastSeen < unseenDelay;
            Breathe(dt);
            if (seen) return;

            Hunt(dt);
        }

        void Hunt(float dt)
        {
            chaseTime += dt;
            float speed = Mathf.Lerp(startSpeed, topSpeed, Mathf.Clamp01(chaseTime / Mathf.Max(rampTime, 0.1f)));

            Vector3 me = transform.position;
            Vector3 target = player.transform.position;
            Vector3 chest = me + Vector3.up * 1.4f, playerChest = target + Vector3.up * 1.2f;
            if (Physics.Linecast(chest, playerChest, sightMask, QueryTriggerInteraction.Ignore))
                target = doorway;   // round the corner: through the door first
            Vector3 to = target - me; to.y = 0f;
            float dist = to.magnitude;

            Vector3 toPlayer = player.transform.position - me; toPlayer.y = 0f;
            if (toPlayer.magnitude <= killDistance) { StartCoroutine(Kill()); return; }

            if (dist > 0.05f)
            {
                Vector3 dir = to / dist;
                Quaternion want = Quaternion.LookRotation(dir, Vector3.up);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, want, turnSpeed * dt);
                Vector3 step = dir * Mathf.Min(speed * dt, dist);
                // do not walk into walls: slide along them
                if (Physics.SphereCast(me + Vector3.up * (1f * bodySize), 0.35f * bodySize, dir, out RaycastHit hit, step.magnitude + 0.05f, sightMask, QueryTriggerInteraction.Ignore))
                    step = Vector3.ProjectOnPlane(step, hit.normal);
                transform.position = me + new Vector3(step.x, 0f, step.z);
            }

            FitHeadroom(dt);

            // the run, played while nobody looks
            if (runInput >= 0)
            {
                runTime += dt * Mathf.Lerp(0.9f, 1.4f, Mathf.InverseLerp(startSpeed, topSpeed, speed));
                Show(runInput, runTime % runClip.length);
            }
            Stamp(dt, toPlayer.magnitude);
        }

        /// <summary>Grows or shrinks (only ever while unseen) to stand under whatever ceiling is over it.</summary>
        void FitHeadroom(float dt)
        {
            if (!fitUnderCeiling || !model) return;
            float room = Physics.Raycast(transform.position + Vector3.up * 0.3f, Vector3.up, out RaycastHit hit, 20f, sightMask, QueryTriggerInteraction.Ignore)
                ? hit.distance + 0.3f : 20f;
            float want = Mathf.Min(fullScale, (room - 0.12f) / Mathf.Max(poseHeight, 0.1f));
            float now = model.localScale.x;
            now = Mathf.MoveTowards(now, want, dt * 2.5f);
            model.localScale = Vector3.one * now;
        }

        /// <summary>Breathes, ragged and quiet, while it stands still; holds its breath when it moves.</summary>
        void Breathe(float dt)
        {
            if (!breathSource) return;
            float want = seen ? breathVolume : 0f;
            breathLevel = Mathf.MoveTowards(breathLevel, want, dt * (seen ? 0.8f : 4f));   // fades in slowly, stops at once
            breathSource.volume = breathLevel;
            if (!seen)
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
            breathSource.pitch = Random.Range(0.9f, 1.03f);
            breathSource.Play();
            breathWait = Random.Range(breathGap.x, breathGap.y);
        }

        void Stamp(float dt, float distance)
        {
            if (steps == null || steps.Length == 0) return;
            if (stepFilter)
                stepFilter.cutoffFrequency = Mathf.Lerp(stepMuffle.x, stepMuffle.y, Mathf.Pow(1f - Mathf.InverseLerp(stepNear, stepFar, distance), 2f));
            float close = Mathf.InverseLerp(fearFar, fearNear, distance);
            float urgency = Mathf.Clamp01(chaseTime / Mathf.Max(rampTime, 0.1f));
            stepTimer -= dt;
            if (stepTimer > 0f) return;
            stepTimer = Mathf.Lerp(stepInterval.x, stepInterval.y, Mathf.Max(close, urgency * 0.6f)) * Random.Range(0.9f, 1.1f);

            int i = Random.Range(0, steps.Length);
            if (steps.Length > 1 && i == lastStep) i = (i + 1) % steps.Length;
            lastStep = i;
            stepSource.pitch = Random.Range(0.85f, 1f);
            stepSource.PlayOneShot(steps[i], stepLoudness);   // the distance does the rest
            if (rig && distance < joltRange) rig.AddTrauma(0.05f + 0.12f * (1f - distance / joltRange));
        }

        void FacePlayer()
        {
            if (!player) return;
            Vector3 to = player.transform.position - transform.position; to.y = 0f;
            if (to.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(to, Vector3.up);
        }

        // ------------------------------------------------------------------ seeing

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

        void RefreshLights()
        {
            lightsRefresh = Time.time + 0.5f;
            lights.Clear();
            foreach (var l in FindObjectsByType<Light>()) lights.Add(l);
        }

        /// <summary>Roughly how much light falls on <paramref name="p"/> (only lights that light the world, not the hands).</summary>
        float LightAt(Vector3 p)
        {
            float sum = 0f;
            foreach (Light l in lights)
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

        // ------------------------------------------------------------------ fear

        public float FearFor(Vector3 eye)
        {
            if (phase == Phase.Waiting) return 0f;
            if (phase == Phase.Killing) return 1f;
            float d = Vector3.Distance(eye, transform.position + Vector3.up * 1.4f);
            float close = Mathf.InverseLerp(fearFar, fearNear, d);
            float urgency = Mathf.Clamp01(chaseTime / Mathf.Max(rampTime, 0.1f));
            // being in the dark room with it is frightening already; closer, and the longer it has been coming, more so
            return Mathf.Clamp01(0.35f + 0.55f * close + 0.15f * urgency);
        }

        // ------------------------------------------------------------------ the end

        IEnumerator Kill()
        {
            phase = Phase.Killing;
            if (breathSource) breathSource.Stop();
            var shooting = player.GetComponentInChildren<PlayerShooting>();
            player.enabled = false;
            if (shooting) shooting.enabled = false;

            // in the face, screaming
            Transform eye = cam ? cam.transform : player.transform;
            Vector3 flat = Vector3.ProjectOnPlane(eye.forward, Vector3.up).normalized;
            if (flat.sqrMagnitude < 0.01f) flat = player.transform.forward;
            transform.position = new Vector3(eye.position.x, player.transform.position.y, eye.position.z) + flat * 0.75f;
            transform.rotation = Quaternion.LookRotation(-flat, Vector3.up);
            if (scream) BodycamHorror.Audio.SpatialSound.PlayAt(scream, eye.position + flat * 0.5f, 1f, 1f, 100f);
            if (rig) rig.AddTrauma(1f);

            float t = 0f;
            Vector3 faceAt = eye.position + flat * 0.7f;
            while (t < screamTime)
            {
                t += Time.deltaTime;
                if (screechInput >= 0) Show(screechInput, 0.4f + t);
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
    }
}
