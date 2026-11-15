using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.Rendering;

namespace BodycamHorror.Player
{
    /// <summary>
    /// The player's own body, seen when looking down and when flat on the back after a back dive.
    /// A humanoid model (Mixamo rig) stands at the player's feet; its head and arms are folded away (the
    /// first-person arms and the camera take their place) and it is set back a little, so looking down shows
    /// chest, belt and legs rather than the inside of the neck.
    /// The clips are mixed with Playables from the controller's state, so no Animator Controller asset is
    /// needed: directional walking and running, crouching, falling, sliding. Any slot left empty falls
    /// back to the nearest one there is. Lying on the back is done by laying the whole body down.
    /// </summary>
    [DefaultExecutionOrder(80)]   // after the controller and the camera rig have placed the view this frame
    [RequireComponent(typeof(BodycamPlayerController))]
    public class PlayerBody : MonoBehaviour
    {
        [Header("Model")]
        [Tooltip("Humanoid model (Mixamo rig). Its own Animator is used; any controller on it is replaced.")]
        public GameObject modelPrefab;
        [Tooltip("Material for every renderer of the model. Empty = the model's own.")]
        public Material material;
        [Tooltip("Scale the model so its eyes are at the camera's standing height.")]
        public bool fitToEyeHeight = true;
        [Tooltip("Scale used when fitToEyeHeight is off.")]
        public float modelScale = 1f;

        [Header("Where it sits")]
        [Tooltip("The neck sits this far behind the camera pivot (m), so looking down shows the chest and not the inside of the collar.")]
        public float neckBehind = 0.26f;
        [Tooltip("And this far below it (m).")]
        public float neckBelow = 0.12f;
        [Tooltip("Lying on the back the hips rest this high above the bottom of the controller (m).")]
        public float lyingHipHeight = 0.12f;
        [Tooltip("Bones folded to nothing: the head (the camera is there) and the shoulders with the arms (the first-person arms are there).")]
        public HumanBodyBones[] hiddenBones = { HumanBodyBones.Head, HumanBodyBones.LeftShoulder, HumanBodyBones.RightShoulder };
        public bool castShadows = true;
        [Tooltip("Hidden/BodycamHorror/ViewModelStencil: marks the hands so the body is drawn under them. Referenced here so builds include it.")]
        public Shader stencilShader;

        [Header("Clips (Mixamo, In Place)")]
        public AnimationClip idle;
        public AnimationClip walkForward;
        public AnimationClip walkBackward;
        public AnimationClip strafeLeft;
        public AnimationClip strafeRight;
        public AnimationClip run;
        public AnimationClip crouchIdle;
        public AnimationClip crouchWalk;
        public AnimationClip fall;
        public AnimationClip slide;
        [Tooltip("Played lying on the back after a back dive. Empty = the idle, laid down.")]
        public AnimationClip lying;
        [Tooltip("A back dive puts the camera on the floor in about this long (s); the lying clip is sped up to have the body down by then, so the camera never ends up inside it.")]
        public float diveLandTime = 0.45f;

        [Tooltip("Walk, run and crouch clips step in time with the camera's footsteps (BodycamCameraRig.StepPhase): a heel comes down with every step sound. Off = the clips run at the speeds below.")]
        public bool stepWithCamera = true;

        [Header("Clip speeds without the camera's steps (m/s at which the feet keep up with the floor)")]
        public float walkClipSpeed = 1.5f;
        public float runClipSpeed = 4.5f;
        public float crouchClipSpeed = 1.1f;
        [Tooltip("How fast the clips blend into one another (1/s).")]
        public float blendSpeed = 9f;

        /// <summary>The model instance (null until set up).</summary>
        public Transform Model { get; private set; }

        enum Slot { Idle, WalkF, WalkB, StrafeL, StrafeR, Run, CrouchIdle, CrouchWalk, Fall, Slide, Lying, Count }

        BodycamPlayerController player;
        Animator animator;
        PlayableGraph graph;
        AnimationMixerPlayable mixer;
        readonly AnimationClipPlayable[] playables = new AnimationClipPlayable[(int)Slot.Count];
        readonly AnimationClip[] clips = new AnimationClip[(int)Slot.Count];
        readonly float[] weights = new float[(int)Slot.Count];
        readonly float[] targets = new float[(int)Slot.Count];
        readonly float[] heelStrike = new float[(int)Slot.Count];   // share of each locomotion clip at which the left heel comes down
        BodycamCameraRig rig;
        float lyingRate = 1f;   // how much faster the lying clip plays, so the body lands with the camera
        Transform[] hidden;
        Transform neck, hips;
        float phase, lie, sink, oneShotTime;
        MoveState lastState;

        void Start()
        {
            player = GetComponent<BodycamPlayerController>();
            if (modelPrefab) Build();
        }

        void OnDestroy()
        {
            if (graph.IsValid()) graph.Destroy();
            PlayerBodyPass.Unregister(this);
        }

        void Build()
        {
            var inst = Instantiate(modelPrefab, transform, false);
            inst.name = "FirstPersonBody";
            Model = inst.transform;
            // its own layer: drawn by PlayerBodyPass under the hands rather than in the usual opaque pass (no colliders, raycasts pass)
            foreach (var t in inst.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = PlayerBodyPass.BodyLayer;
            PlayerBodyPass.Register(this, stencilShader);
            foreach (var c in inst.GetComponentsInChildren<Collider>(true)) Destroy(c);
            foreach (var r in inst.GetComponentsInChildren<Renderer>(true))
            {
                if (material) { var mats = r.sharedMaterials; for (int i = 0; i < mats.Length; i++) mats[i] = material; r.sharedMaterials = mats; }
                r.shadowCastingMode = castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
                r.renderingLayerMask = ViewModelLighting.WorldLayer;   // a thing in the world: the flashlight lights the legs
                if (r is SkinnedMeshRenderer smr) smr.updateWhenOffscreen = true;
            }

            animator = inst.GetComponentInChildren<Animator>();
            if (!animator || !animator.avatar || !animator.avatar.isHuman)
            {
                Debug.LogWarning("[PlayerBody] The model needs a humanoid Animator.", this);
                return;
            }
            animator.runtimeAnimatorController = null;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            neck = animator.GetBoneTransform(HumanBodyBones.Neck) ?? animator.GetBoneTransform(HumanBodyBones.Head);
            hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            hidden = new Transform[hiddenBones.Length];
            for (int i = 0; i < hiddenBones.Length; i++) hidden[i] = animator.GetBoneTransform(hiddenBones[i]);

            // every empty slot borrows the nearest clip there is
            AnimationClip Pick(params AnimationClip[] options) { foreach (var c in options) if (c) return c; return null; }
            clips[(int)Slot.Idle] = Pick(idle, walkForward);
            clips[(int)Slot.WalkF] = Pick(walkForward, run, idle);
            clips[(int)Slot.WalkB] = Pick(walkBackward, walkForward, idle);
            clips[(int)Slot.StrafeL] = Pick(strafeLeft, walkForward, idle);
            clips[(int)Slot.StrafeR] = Pick(strafeRight, walkForward, idle);
            clips[(int)Slot.Run] = Pick(run, walkForward, idle);
            clips[(int)Slot.CrouchIdle] = Pick(crouchIdle, crouchWalk, idle);
            clips[(int)Slot.CrouchWalk] = Pick(crouchWalk, walkForward, idle);
            clips[(int)Slot.Fall] = Pick(fall, idle);
            clips[(int)Slot.Slide] = Pick(slide, fall, idle);
            clips[(int)Slot.Lying] = Pick(lying, idle);
            if (!clips[(int)Slot.Idle]) { Debug.LogWarning("[PlayerBody] No clips assigned.", this); return; }

            graph = PlayableGraph.Create("PlayerBody");
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            mixer = AnimationMixerPlayable.Create(graph, (int)Slot.Count);
            for (int i = 0; i < (int)Slot.Count; i++)
            {
                playables[i] = AnimationClipPlayable.Create(graph, clips[i]);
                playables[i].SetApplyFootIK(true);
                playables[i].Pause();   // their time is set by hand below
                graph.Connect(playables[i], 0, mixer, i);
            }
            AnimationPlayableOutput.Create(graph, "Body", animator).SetSourcePlayable(mixer);
            weights[(int)Slot.Idle] = 1f;
            mixer.SetInputWeight((int)Slot.Idle, 1f);
            graph.Play();

            // sized in the pose it stands in, not the file's: eyes at the camera's standing height
            if (fitToEyeHeight)
            {
                Model.localScale = Vector3.one;
                graph.Evaluate(0f);
                Transform head = animator.GetBoneTransform(HumanBodyBones.Head);
                float headHeight = head ? head.position.y - transform.position.y : 0f;
                float eye = player.cameraPivot ? player.cameraPivot.position.y - transform.position.y : 1.6f;
                if (headHeight > 0.1f) modelScale = eye / headHeight;
            }
            Model.localScale = Vector3.one * modelScale;
            rig = GetComponentInChildren<BodycamCameraRig>(true);
            FindHeelStrikes();
        }

        /// <summary>
        /// Plays every locomotion clip through once and notes when the left foot is furthest out along the way it
        /// walks, measured from the hips (forward for the forward clips, back for the backward one, sideways for the strafes): the heel strike.
        /// With it the clips can be lined up with the camera's footfalls.
        /// </summary>
        void FindHeelStrikes()
        {
            Transform foot = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
            if (!foot) return;
            for (int i = 0; i <= (int)Slot.CrouchWalk; i++)
            {
                AnimationClip c = clips[i];
                if (!c || i == (int)Slot.Idle || i == (int)Slot.CrouchIdle) continue;
                Vector3 way = i == (int)Slot.WalkB ? Vector3.back : i == (int)Slot.StrafeL ? Vector3.left : i == (int)Slot.StrafeR ? Vector3.right : Vector3.forward;
                for (int j = 0; j < (int)Slot.Count; j++) mixer.SetInputWeight(j, j == i ? 1f : 0f);
                const int samples = 48;
                float best = float.MinValue;
                for (int s = 0; s < samples; s++)
                {
                    float t = s / (float)samples;
                    playables[i].SetTime(t * c.length);
                    graph.Evaluate(0f);
                    // against the hips: clips not made In Place carry the whole body forward
                    Vector3 rel = Model.InverseTransformPoint(foot.position) - (hips ? Model.InverseTransformPoint(hips.position) : Vector3.zero);
                    float along = Vector3.Dot(rel, way);
                    if (along > best) { best = along; heelStrike[i] = t; }
                }
            }
            FindLyingLanding();
            for (int j = 0; j < (int)Slot.Count; j++) mixer.SetInputWeight(j, j == (int)Slot.Idle ? 1f : 0f);
            graph.Evaluate(0f);
        }

        /// <summary>
        /// When the lying clip (a fall onto the back) has the neck down near the floor; it then plays fast enough to
        /// get there by <see cref="diveLandTime"/>, when the dive has already put the camera on the floor.
        /// </summary>
        void FindLyingLanding()
        {
            AnimationClip c = clips[(int)Slot.Lying];
            if (!lying || !c || !neck) return;
            int i = (int)Slot.Lying;
            for (int j = 0; j < (int)Slot.Count; j++) mixer.SetInputWeight(j, j == i ? 1f : 0f);
            const int samples = 60;
            var heights = new float[samples + 1];
            for (int s = 0; s <= samples; s++)
            {
                playables[i].SetTime(s / (float)samples * (c.length - 0.001f));
                graph.Evaluate(0f);
                heights[s] = Model.InverseTransformPoint(neck.position).y;
            }
            float down = heights[samples];
            float landed = c.length;
            for (int s = 0; s <= samples; s++)
                if (heights[s] <= down + 0.08f) { landed = s / (float)samples * c.length; break; }
            lyingRate = Mathf.Max(1f, landed / Mathf.Max(diveLandTime, 0.05f));
        }

        void LateUpdate()
        {
            if (!graph.IsValid() || !Model) return;
            float dt = Time.deltaTime;
            MoveState state = player.State;
            bool lyingState = state == MoveState.DiveBack || state == MoveState.Prone || state == MoveState.ProneStandUp;
            // one-shot clips start over on a new state; the lying one runs on through dive, prone and standing up
            bool wasLying = lastState == MoveState.DiveBack || lastState == MoveState.Prone || lastState == MoveState.ProneStandUp;
            if (state != lastState && !(lyingState && wasLying)) oneShotTime = 0f;
            lastState = state;
            oneShotTime += dt;

            // ---- what should be playing
            for (int i = 0; i < targets.Length; i++) targets[i] = 0f;
            Vector3 v = transform.InverseTransformDirection(player.HorizontalVelocity);
            float speed = new Vector2(v.x, v.z).magnitude;
            float clipSpeed;

            if (lyingState) { targets[(int)Slot.Lying] = 1f; clipSpeed = 1f; }
            else if (state == MoveState.Sliding) { targets[(int)Slot.Slide] = 1f; clipSpeed = 1f; }
            else if (state == MoveState.Airborne) { targets[(int)Slot.Fall] = 1f; clipSpeed = 1f; }
            else if (player.IsCrouching)
            {
                float m = Mathf.Clamp01(speed / 0.6f);
                targets[(int)Slot.CrouchIdle] = 1f - m;
                targets[(int)Slot.CrouchWalk] = m;
                clipSpeed = crouchClipSpeed;
            }
            else
            {
                float m = Mathf.Clamp01(speed / 0.8f);
                targets[(int)Slot.Idle] = 1f - m;
                // direction: cosine shares of forward / back / left / right
                Vector2 dir = speed > 0.05f ? new Vector2(v.x, v.z) / speed : Vector2.up;
                float f = Mathf.Max(0f, dir.y), b = Mathf.Max(0f, -dir.y), l = Mathf.Max(0f, -dir.x), r = Mathf.Max(0f, dir.x);
                float sum = Mathf.Max(f + b + l + r, 1e-4f);
                float running = Mathf.InverseLerp(player.walkSpeed + 0.3f, player.sprintSpeed - 0.5f, speed);
                targets[(int)Slot.WalkF] = m * f / sum * (1f - running);
                targets[(int)Slot.Run] = m * f / sum * running;
                targets[(int)Slot.WalkB] = m * b / sum;
                targets[(int)Slot.StrafeL] = m * l / sum;
                targets[(int)Slot.StrafeR] = m * r / sum;
                clipSpeed = Mathf.Lerp(walkClipSpeed, runClipSpeed, running);
            }

            // ---- blend, and keep the feet in step with the ground
            float k = 1f - Mathf.Exp(-blendSpeed * dt), total = 0f;
            for (int i = 0; i < weights.Length; i++) { weights[i] = Mathf.Lerp(weights[i], targets[i], k); total += weights[i]; }
            float cycleRate = speed > 0.05f ? speed / Mathf.Max(clipSpeed, 0.1f) : 1f;
            // one walk cycle = the camera's left + right step, the left heel down at its start
            bool camSteps = stepWithCamera && rig;
            if (camSteps) phase = Mathf.Repeat(rig.StepPhase / (2f * Mathf.PI), 1f);
            for (int i = 0; i < weights.Length; i++)
            {
                mixer.SetInputWeight(i, total > 0f ? weights[i] / total : 0f);
                AnimationClip c = clips[i];
                if (!c) continue;
                bool locomotion = i <= (int)Slot.CrouchWalk;
                double t = locomotion ? Mathf.Repeat(phase + (camSteps ? heelStrike[i] : 0f), 1f) * c.length : Mathf.Min(oneShotTime * (i == (int)Slot.Lying ? lyingRate : 1f), c.length - 0.001f);
                if (!locomotion && c.isLooping) t = Mathf.Repeat(oneShotTime, c.length);
                playables[i].SetTime(t);
            }
            float baseLength = clips[(int)Slot.WalkF] ? clips[(int)Slot.WalkF].length : 1f;
            if (!camSteps) phase = Mathf.Repeat(phase + dt * cycleRate / Mathf.Max(baseLength, 0.1f), 1f);
            graph.Evaluate(0f);

            // ---- lay the body down on its back, and sink it when crouching with no crouch clips
            lie = Mathf.MoveTowards(lie, lyingState ? 1f : 0f, dt * 3f);
            bool crouchClips = crouchIdle || crouchWalk;
            sink = Mathf.MoveTowards(sink, player.IsCrouching && !crouchClips ? 1f : 0f, dt * 6f);
            Model.localPosition = Vector3.zero;
            // a clip of its own (falling onto the back) lies down by itself; the stand-in idle is tipped over by hand
            bool ownLyingClip = lying;
            Model.localRotation = Quaternion.Euler(ownLyingClip ? 0f : -90f * lie, 0f, 0f);

            // ---- the neck goes just behind and below the camera pivot: across always, up and down when lying or sinking
            if (neck && player.cameraPivot)
            {
                Transform pivot = player.cameraPivot;
                Vector3 back = Vector3.ProjectOnPlane(-transform.forward, Vector3.up).normalized;
                Vector3 want = pivot.position + back * neckBehind + Vector3.down * neckBelow;
                Vector3 shift = want - neck.position;
                float upDown = Mathf.Max(lie, sink);
                float shiftY = shift.y * upDown;
                // lying, the hips rest on the floor rather than the neck hanging under the low camera
                // the clip brings the body down to the floor itself; only never with the neck above the camera, or the
                // camera, which drops faster, would be inside the chest
                if (ownLyingClip) shiftY = lyingState ? Mathf.Min(0f, shift.y) : shift.y * sink;
                else if (hips && lie > 0f)
                    shiftY = Mathf.Lerp(shiftY, transform.position.y + lyingHipHeight - hips.position.y, lie);
                Model.position += new Vector3(shift.x, shiftY, shift.z);
            }

            // ---- head and arms folded away: the camera and the first-person arms are there
            foreach (var h in hidden) if (h) h.localScale = Vector3.one * 0.0001f;
        }
    }
}
