using System.Collections.Generic;
using BodycamHorror.Enemies;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Events;
using UnityEngine.Playables;

namespace BodycamHorror.Cutscenes
{
    /// <summary>
    /// Final cutscene: the player in the old lift, an infected forces the closing doors, takes a bullet to
    /// the head, falls back out; the doors shut and the cab goes up. Everything that moves is one baked
    /// clip from Blender/Elevator_Finale.blend (Assets/Cutscenes/ElevatorFinale/ElevatorFinale.fbx,
    /// 420 frames at 30 fps), played here without an AnimatorController. The lights, the hands' tremor
    /// and the sound are timed here by frame to match the Blender scene (Blender/tools/elevator_finale_anim.py).
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public class ElevatorFinaleCutscene : MonoBehaviour
    {
        const float Fps = 30f;

        /// <summary>A sound started at a frame of the cutscene, optionally from the middle of its clip.</summary>
        [System.Serializable]
        public class SoundCue
        {
            public string name;
            public AudioClip clip;
            [Tooltip("Cutscene frame (30 fps) at which it starts.")]
            public int frame;
            [Tooltip("Where in the clip to start (s).")]
            public float from;
            [Tooltip("How much of the clip to play (s); 0 = to its end (or forever when looping).")]
            public float length;
            [Range(0f, 1f)] public float volume = 1f;
            public float fadeIn;
            public float fadeOut = 0.05f;
            public bool loop;
            [Tooltip("A gunshot: goes through the game's GunshotAcoustics (crack, body, the cab's slap-back) like a real shot.")]
            public bool gunshot;
        }

        [Tooltip("The baked take from ElevatorFinale.fbx.")]
        public AnimationClip clip;
        public bool playOnStart = true;
        [Tooltip("Playback speed of the whole cutscene (animation, hands, cues, lights). Sounds keep their own pitch.")]
        [Range(0.5f, 2f)] public float speed = 1.15f;

        [Header("Lights")]
        public Light cabLight;
        public Light landingLight;
        public Light muzzleLight;
        [Tooltip("Flash card at the muzzle of the visible gun: shown for two frames on the shot.")]
        public Renderer muzzleFlash;
        public Renderer landingBulb;
        public float cabIntensity = 2.2f;
        public float landingIntensity = 1.5f;
        public float muzzleIntensity = 14f;

        [Header("Player's hands")]
        [Tooltip("PistolArms model laid over the baked armature, with PistolArms.controller.")]
        public Animator arms;
        [Tooltip("Frame the gun is drawn (the hands come into view); before it they are hidden.")]
        public int drawFrame = 66;
        [Tooltip("The cutscene camera: the hands shake about the eye.")]
        public Transform viewCamera;
        [Tooltip("Tremor of the hands over the cutscene: degrees against frame.")]
        public AnimationCurve shake = new AnimationCurve(
            new Keyframe(0, 0.4f), new Keyframe(79, 1.4f), new Keyframe(124, 1.8f), new Keyframe(176, 2.2f),
            new Keyframe(186, 0.7f), new Keyframe(196, 0.6f), new Keyframe(210, 2.4f), new Keyframe(262, 1.2f),
            new Keyframe(420, 0.6f));
        [Tooltip("How fast the tremor is at full fright (Hz); calm breathing sway is much slower.")]
        public float tremorRate = 7f;
        [Tooltip("Kick of the shot: degrees the hands jump up, and how fast it settles (1/s).")]
        public float recoilKick = 7f;
        public float recoilSettle = 9f;
        [Tooltip("How much the gun turns after what it is watching (the thrashing paw, then the face): share of the angle.")]
        [Range(0f, 1f)] public float trackShare = 0.5f;
        [Tooltip("Most the gun turns off the camera's line (degrees).")]
        public float trackLimit = 12f;
        [Tooltip("How long the gun lags behind what it follows (s).")]
        public float trackLag = 0.22f;
        [Tooltip("The clip holds the gun a little low: degrees it is lifted onto what it follows.")]
        public float aimLift = 4f;
        [Tooltip("Where the gun sinks to once the doors are shut (yaw, pitch degrees off the camera's line).")]
        public Vector2 lowReady = new Vector2(-2f, 1f);
        [Tooltip("Flinches: (frame, metres the hands snap back towards the chest).")]
        public Vector2[] flinches =
        {
            new Vector2(47, 0.045f), new Vector2(72, 0.015f), new Vector2(98, 0.012f), new Vector2(119, 0.018f),
            new Vector2(138, 0.02f), new Vector2(152, 0.022f), new Vector2(176, 0.028f), new Vector2(238, 0.02f),
            new Vector2(302, 0.025f),
        };

        [Header("Infected: physics fall after the headshot")]
        [Tooltip("The cutscene's infected (Infected_Root of the FBX); hidden when the ragdoll takes over.")]
        public Transform infectedRoot;
        [Tooltip("Its skinned meshes (the FBX keeps them outside Infected_Root).")]
        public Renderer[] infectedRenderers;
        [Tooltip("The game's humanoid infected model (ZombieSlasher.fbx): posed like the cutscene's one and dropped as a ragdoll.")]
        public GameObject ragdollModel;
        public Material ragdollMaterial;
        [Tooltip("Frame the body goes limp (right after the head snaps back).")]
        public int ragdollFrame = 199;
        [Tooltip("Push of the bullet on the head (m/s) and the body's drift back out of the doorway (m/s).")]
        public float headPush = 4f;
        public float bodyDrift = 2.4f;

        [Header("Camera shake (the body camera on the chest)")]
        [Tooltip("Continuous shake: degrees against frame.")]
        public AnimationCurve cameraShake = new AnimationCurve(
            new Keyframe(0, 0.35f), new Keyframe(40, 0.5f), new Keyframe(47, 1.3f), new Keyframe(124, 1.1f),
            new Keyframe(176, 1.5f), new Keyframe(190, 0.6f), new Keyframe(200, 1.6f), new Keyframe(240, 1.0f),
            new Keyframe(300, 0.5f), new Keyframe(312, 1.0f), new Keyframe(420, 0.7f));
        [Tooltip("How fast the shake is (Hz).")]
        public float cameraShakeRate = 5f;
        [Tooltip("Jolts: (frame, degrees). Each kicks the camera and dies away over a few tenths of a second.")]
        public Vector2[] jolts =
        {
            new Vector2(47, 3f), new Vector2(72, 1.2f), new Vector2(98, 1f), new Vector2(119, 1.3f),
            new Vector2(138, 1.4f), new Vector2(152, 1.6f), new Vector2(176, 1.8f), new Vector2(197, 2.5f),
            new Vector2(238, 2.2f), new Vector2(302, 3f), new Vector2(306, 1.2f),
        };
        public float joltSettle = 7f;

        [Header("Sound")]
        public List<SoundCue> sounds = new List<SoundCue>();
        [Tooltip("The body camera microphone's auto-gain recovery during the cutscene (ms). The game's own is slow, " +
                 "so after the shot everything sounds smothered for half a second; here it springs straight back.")]
        public float micRelease = 60f;

        [Header("Beats (frames)")]
        public int shotFrame = 197;
        public int doorsShutFrame = 238;

        [Header("Ending")]
        [Tooltip("Black overlay faded in at the end; the sound fades with it.")]
        public CanvasGroup fade;
        public float fadeStart = 15.3f;
        public float fadeTime = 1.4f;

        public UnityEvent onShot;
        public UnityEvent onDoorsShut;
        public UnityEvent onFinished;

        PlayableGraph graph;
        BodycamHorror.Audio.BodycamMicrophone mic;
        float micReleaseBefore;
        float time;
        bool playing, shotDone, shutDone, finished;
        Color bulbEmission;
        Vector3 armsBasePosition;
        Quaternion armsBaseRotation;
        bool armsBaseSet;
        Vector2 aim, aimVelocity;   // where the gun points off the camera's line (yaw, pitch degrees), smoothed
        Transform pawBone, headBone, ragdollHead;
        GameObject ragdoll;
        bool ragdollDone;

        class Voice { public SoundCue cue; public AudioSource source; public float started; }
        readonly List<Voice> voices = new List<Voice>();
        int nextSound;

        // cab light: (frame, fraction of cabIntensity), stepped — the dips after the slam and on the jolt
        static readonly Vector2[] CabKeys =
        {
            new Vector2(0, 1f), new Vector2(240, 0.22f), new Vector2(242, 1f), new Vector2(302, 0.09f),
            new Vector2(305, 1f), new Vector2(308, 0.27f), new Vector2(311, 1f),
        };

        // the hands' clips, as laid out on the NLA track in Blender: gun holstered, drawn when the arm
        // comes through, one shot, holstered again once the doors are shut
        static readonly (int frame, string state)[] ArmBeats =
        {
            (66, "Take"), (79, "Idle"), (196, "Shoot"), (207, "Idle"),   // the gun stays out to the end
        };
        int nextArmBeat;
        Renderer[] armsRenderers;

        public bool Playing => playing;
        /// <summary>Current cutscene frame (30 fps, after <see cref="speed"/>).</summary>
        public float Frame => time * Fps;

        void Start()
        {
            if (fade) fade.alpha = 0f;
            if (muzzleLight) muzzleLight.intensity = 0f;
            if (landingBulb) bulbEmission = landingBulb.sharedMaterial.GetColor("_EmissionColor");
            if (playOnStart) Play();
        }

        public void Play()
        {
            if (graph.IsValid()) graph.Destroy();
            AnimationPlayableUtilities.PlayClip(GetComponent<Animator>(), clip, out graph);
            graph.GetRootPlayable(0).SetSpeed(speed);
            if (arms) arms.speed = speed;
            time = 0f;
            playing = true;
            shotDone = shutDone = finished = false;
            nextArmBeat = 0;
            nextSound = 0;
            aim = aimVelocity = Vector2.zero;
            ragdollDone = false;
            if (ragdoll) Destroy(ragdoll);
            if (infectedRoot)
            {
                foreach (var r in infectedRenderers) if (r) r.enabled = true;
                pawBone = FindBone(infectedRoot, "mixamorig:RightHand");
                headBone = FindBone(infectedRoot, "mixamorig:Head");
            }
            foreach (var v in voices) Destroy(v.source.gameObject);
            voices.Clear();
            sounds.Sort((a, b) => a.frame.CompareTo(b.frame));
            if (arms)
            {
                arms.Play("Hide", 0, 1f);
                if (armsRenderers == null)
                {
                    var shown = new List<Renderer>();
                    foreach (var r in arms.GetComponentsInChildren<Renderer>(true))
                        if (r.enabled && r != muzzleFlash) shown.Add(r);
                    armsRenderers = shown.ToArray();
                }
            }
        }

        void Update()
        {
            if (!playing) return;
            time += Time.deltaTime * speed;
            float frame = time * Fps;

            while (arms && nextArmBeat < ArmBeats.Length && frame >= ArmBeats[nextArmBeat].frame)
            {
                string state = ArmBeats[nextArmBeat++].state;
                if (state == "Shoot" || state == "Take") arms.Play(state, 0, 0f);
                else arms.CrossFadeInFixedTime(state, 0.1f, 0, 0f);
            }
            if (armsRenderers != null)
                foreach (var r in armsRenderers) r.enabled = frame >= drawFrame;
            if (muzzleFlash)
            {
                bool on = frame >= shotFrame && frame < shotFrame + 2;
                if (on && !muzzleFlash.enabled)
                    muzzleFlash.transform.Rotate(viewCamera ? viewCamera.forward : Vector3.forward, Random.Range(0f, 360f), Space.World);
                muzzleFlash.enabled = on;
                muzzleFlash.transform.localScale = Vector3.one * (frame < shotFrame + 1 ? 2.2f : 1.4f);
            }
            if (cabLight) cabLight.intensity = cabIntensity * Stepped(CabKeys, frame);
            if (landingLight)
            {
                // bulb on a bad contact: mostly on, now and then it sags for a few frames
                int slot = Mathf.FloorToInt(frame / 4f);
                float n = Mathf.PerlinNoise(slot * 0.37f, 3.1f);
                float k = n < 0.3f ? Mathf.Lerp(0.1f, 0.4f, n / 0.3f) : 1f;
                landingLight.intensity = landingIntensity * k;
                if (landingBulb) landingBulb.material.SetColor("_EmissionColor", bulbEmission * k);
            }
            if (muzzleLight)
                muzzleLight.intensity = frame >= shotFrame && frame < shotFrame + 1 ? muzzleIntensity
                                      : frame >= shotFrame + 1 && frame < shotFrame + 2 ? muzzleIntensity * 0.3f : 0f;

            if (!shotDone && frame >= shotFrame) { shotDone = true; onShot.Invoke(); }
            if (!ragdollDone && frame >= ragdollFrame) { ragdollDone = true; DropRagdoll(); }
            if (!shutDone && frame >= doorsShutFrame) { shutDone = true; onDoorsShut.Invoke(); }
            if (fade) fade.alpha = Mathf.Clamp01((time - fadeStart) / fadeTime);
            if (!mic && BodycamHorror.Audio.BodycamMicrophone.Instance)
            {
                mic = BodycamHorror.Audio.BodycamMicrophone.Instance;
                micReleaseBefore = mic.release;
                mic.release = micRelease;
                mic.Refresh();
            }
            UpdateSound(frame);
            if (!finished && clip && time >= Mathf.Max(clip.length, fadeStart + fadeTime))
            {
                finished = true;
                playing = false;
                foreach (var v in voices) v.source.Stop();
                onFinished.Invoke();
            }
        }

        // after the Animator: the clips pose the bones, the tremor moves the whole rig about the eye
        void LateUpdate()
        {
            if (playing && viewCamera) ShakeCamera(time * Fps);
            if (!arms || !viewCamera) return;
            Transform t = arms.transform;
            if (!armsBaseSet)
            {
                armsBasePosition = t.localPosition;
                armsBaseRotation = t.localRotation;
                armsBaseSet = true;
            }
            t.localPosition = armsBasePosition;
            t.localRotation = armsBaseRotation;
            if (!playing) return;

            float frame = time * Fps;
            Vector3 eye = viewCamera.position;

            // what the gun follows: the paw thrashing about inside, then the face in the gap, then the body
            // going down; once the doors are shut it sinks to a tired low ready
            Vector2 want = Vector2.zero;
            Transform watch = frame < 124 ? pawBone : frame < ragdollFrame ? headBone : ragdollHead;
            if (watch && frame >= 66 && frame < 250)
            {
                Vector3 local = viewCamera.InverseTransformDirection(watch.position - eye);
                float yawTo = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
                float pitchTo = Mathf.Atan2(local.y, new Vector2(local.x, local.z).magnitude) * Mathf.Rad2Deg;
                want = new Vector2(Mathf.Clamp(yawTo * trackShare, -trackLimit, trackLimit),
                                   Mathf.Clamp(pitchTo * trackShare + aimLift, -trackLimit, trackLimit * 0.6f));   // hardly up: the arms stay low
            }
            if (frame >= 250) want = lowReady;
            aim = Vector2.SmoothDamp(aim, want, ref aimVelocity, frame >= 250 ? 0.8f : trackLag, Mathf.Infinity, Time.deltaTime);

            // tremor: slow sway of breathing plus a fast shake that grows with the fright
            float amp = shake.Evaluate(frame);
            float fright = Mathf.InverseLerp(0.5f, 2.4f, amp);
            float slow = time * 0.6f, fast = time * Mathf.Lerp(2f, tremorRate, fright);
            float pitch = aim.y + (Noise(slow, 0.1f) * 0.6f + Noise(fast, 5.3f) * 0.4f * (0.4f + fright)) * amp;
            float yaw = aim.x + (Noise(slow, 2.7f) * 0.6f + Noise(fast, 8.9f) * 0.4f * (0.4f + fright)) * amp;
            float roll = Noise(fast * 0.7f, 11.1f) * amp * 0.5f - aim.x * 0.25f;   // cants a little into a turn
            float sinceShot = time - shotFrame / Fps;
            if (sinceShot >= 0f) pitch += recoilKick * Mathf.Exp(-sinceShot * recoilSettle) * Mathf.Min(1f, sinceShot * 40f);

            // flinches back towards the chest, breathing bob
            float pull = 0f;
            foreach (var f in flinches)
            {
                float since = (frame - f.x) / Fps;
                if (since >= 0f) pull += f.y * Mathf.Exp(-since * 5f) * Mathf.Min(1f, since * 30f);
            }
            t.position += -viewCamera.forward * pull
                        + viewCamera.up * (Mathf.Sin(time * 1.7f) * 0.006f - pull * 0.4f)
                        + viewCamera.right * Mathf.Sin(time * 0.85f) * 0.004f;
            t.RotateAround(eye, viewCamera.right, -pitch);
            t.RotateAround(eye, viewCamera.up, yaw);
            t.RotateAround(eye, viewCamera.forward, roll);
        }

        // after the Animator has posed the camera for this frame: rattle it about its own centre
        void ShakeCamera(float frame)
        {
            float amp = cameraShake.Evaluate(frame);
            float kick = 0f;
            foreach (var j in jolts)
            {
                float since = (frame - j.x) / Fps;
                if (since >= 0f) kick += j.y * Mathf.Exp(-since * joltSettle) * Mathf.Min(1f, since * 60f);
            }
            float t = time * cameraShakeRate;
            float pitch = Noise(t, 21.3f) * amp + Noise(time * 23f, 1.7f) * kick;
            float yaw = Noise(t, 33.9f) * amp * 0.8f + Noise(time * 19f, 4.2f) * kick * 0.7f;
            float roll = Noise(t * 0.8f, 47.5f) * amp * 0.6f + Noise(time * 17f, 9.4f) * kick * 0.5f;
            viewCamera.localRotation *= Quaternion.Euler(pitch, yaw, roll);
        }

        // ------------------------------------------------------------------ ragdoll

        /// <summary>Swaps the cutscene's infected for the game's humanoid one in the same pose and lets it fall.</summary>
        void DropRagdoll()
        {
            if (!infectedRoot || !ragdollModel || infectedRenderers == null || infectedRenderers.Length == 0) return;
            ragdoll = Instantiate(ragdollModel);
            ragdoll.name = "Infected_Ragdoll";
            int layer = infectedRenderers[0].gameObject.layer;   // stays in the dark
            foreach (var r in ragdoll.GetComponentsInChildren<Renderer>(true))
            {
                r.gameObject.layer = layer;
                if (ragdollMaterial) r.sharedMaterial = ragdollMaterial;
                if (r is SkinnedMeshRenderer skin) skin.updateWhenOffscreen = true;
            }
            var anim = ragdoll.GetComponent<Animator>();
            if (anim) { anim.runtimeAnimatorController = null; anim.enabled = false; }
            MatchPose(infectedRoot, ragdoll.transform);
            foreach (var r in infectedRenderers) if (r) r.enabled = false;

            var body = ragdoll.AddComponent<InfectedRagdoll>();
            if (!anim || !body.Build(anim, 70f)) { Debug.LogWarning("ElevatorFinale: ragdoll could not be built"); return; }
            // knocked back out of the doorway: the whole body drifts away, the head is thrown back hardest and
            // the torso turns over backwards, so it goes down on its back on the landing, not in a heap
            Vector3 away = Vector3.ProjectOnPlane(viewCamera.forward, Vector3.up).normalized;
            body.Activate(away * bodyDrift + Vector3.up * 0.4f);
            ragdollHead = anim.GetBoneTransform(HumanBodyBones.Head);
            if (ragdollHead) body.Impulse((away + Vector3.up * 0.25f) * headPush, ragdollHead.position);
            body.Topple(Vector3.Cross(Vector3.up, away), 6f);
        }

        static Transform FindBone(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t;
            return null;
        }

        /// <summary>Puts <paramref name="dst"/> in the pose of <paramref name="src"/>: same scale and hips, then every
        /// bone turned so it points where its namesake points. The two skeletons share bone names but not
        /// bone axes, so it goes by directions (twist is lost, which a falling body does not show).</summary>
        static void MatchPose(Transform src, Transform dst)
        {
            var from = new Dictionary<string, Transform>();
            foreach (var t in src.GetComponentsInChildren<Transform>(true)) from[t.name] = t;
            var to = new Dictionary<string, Transform>();
            foreach (var t in dst.GetComponentsInChildren<Transform>(true)) to[t.name] = t;
            Transform S(string n) => from.TryGetValue("mixamorig:" + n, out var t) ? t : null;
            Transform D(string n) => to.TryGetValue("mixamorig:" + n, out var t) ? t : null;
            if (!S("Hips") || !D("Hips") || !S("LeftLeg") || !D("LeftLeg") || !S("Spine") || !D("Spine")) return;

            float Leg(System.Func<string, Transform> f) => Vector3.Distance(f("LeftUpLeg").position, f("LeftLeg").position)
                                                         + Vector3.Distance(f("LeftLeg").position, f("LeftFoot").position);
            dst.localScale *= Leg(S) / Mathf.Max(0.001f, Leg(D));

            // hips: up the spine and across the thighs
            Quaternion Frame(System.Func<string, Transform> f) => Quaternion.LookRotation(
                f("RightUpLeg").position - f("LeftUpLeg").position, f("Spine").position - f("Hips").position);
            dst.rotation = Frame(S) * Quaternion.Inverse(Frame(D)) * dst.rotation;
            dst.position += S("Hips").position - D("Hips").position;

            foreach (var bone in dst.GetComponentsInChildren<Transform>(true))
            {
                if (bone == D("Hips") || !from.TryGetValue(bone.name, out var sb)) continue;
                Transform child = null;
                foreach (Transform c in bone)
                    if (from.ContainsKey(c.name) && (child == null || c.name.EndsWith("Neck"))) child = c;
                if (!child) continue;
                Vector3 a = child.position - bone.position, b = from[child.name].position - sb.position;
                if (a.sqrMagnitude < 1e-8f || b.sqrMagnitude < 1e-8f) continue;
                bone.rotation = Quaternion.FromToRotation(a, b) * bone.rotation;
            }
        }

        static float Noise(float t, float seed) => (Mathf.PerlinNoise(t, seed) - 0.5f) * 2f;

        void UpdateSound(float frame)
        {
            while (nextSound < sounds.Count && frame >= sounds[nextSound].frame)
            {
                var cue = sounds[nextSound++];
                if (!cue.clip) continue;
                if (cue.gunshot && BodycamHorror.Audio.GunshotAcoustics.Play(cue.clip, cue.volume, 1f, 1f)) continue;
                var go = new GameObject("Sound_" + (string.IsNullOrEmpty(cue.name) ? cue.clip.name : cue.name));
                go.transform.SetParent(transform, false);
                var src = go.AddComponent<AudioSource>();
                src.clip = cue.clip;
                src.loop = cue.loop;
                src.spatialBlend = 0f;
                src.playOnAwake = false;
                src.time = Mathf.Clamp(cue.from, 0f, cue.clip.length - 0.01f);
                src.volume = cue.fadeIn > 0f ? 0f : cue.volume;
                src.Play();
                voices.Add(new Voice { cue = cue, source = src, started = time });
            }

            float master = fade ? 1f - fade.alpha : 1f;
            for (int i = voices.Count - 1; i >= 0; i--)
            {
                var v = voices[i];
                float age = time - v.started;
                float gain = v.cue.fadeIn > 0f ? Mathf.Clamp01(age / v.cue.fadeIn) : 1f;
                if (v.cue.length > 0f)
                {
                    float left = v.cue.length - age;
                    if (left <= 0f) { Destroy(v.source.gameObject); voices.RemoveAt(i); continue; }
                    if (v.cue.fadeOut > 0f) gain *= Mathf.Clamp01(left / v.cue.fadeOut);
                }
                else if (!v.cue.loop && !v.source.isPlaying) { Destroy(v.source.gameObject); voices.RemoveAt(i); continue; }
                v.source.volume = v.cue.volume * gain * master;
            }
        }

        static float Stepped(Vector2[] keys, float frame)
        {
            float v = keys[0].y;
            foreach (var key in keys)
                if (frame >= key.x) v = key.y;
            return v;
        }

        void OnDestroy()
        {
            if (mic) { mic.release = micReleaseBefore; mic.Refresh(); }
            if (graph.IsValid()) graph.Destroy();
        }
    }
}
