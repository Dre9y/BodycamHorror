using BodycamHorror.Weapons;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace BodycamHorror.Player
{
    /// <summary>
    /// How the lens sees. The picture sits in a dark rounded frame; it is sharp up to a distance (set in the Tab
    /// debug menu) and goes out of focus beyond it. Aiming closes the frame a little, like squinting.
    /// The frame is drawn by the Lens Vignette full-screen renderer feature, the focus blur by the Lens Focus one
    /// (before post-processing, with the depth); the fisheye bend by Lens Vignette too; the glass effects by a volume of its own.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class LensLook : MonoBehaviour
    {
        [Header("Frame (screen height = 2)")]
        [Tooltip("Where the picture goes fully black, in from the screen edge.")]
        public Vector2 frameInset = new Vector2(0.02f, 0.02f);
        public float frameRadius = 0.22f;
        [Tooltip("Width of the fall-off into the black edge.")]
        public float frameFeather = 0.2f;

        [Header("Shading inside the frame")]
        [Tooltip("Where the shading is at full strength, in from the screen edge.")]
        public Vector2 shadeInset = new Vector2(0.1f, 0.02f);
        [Tooltip("How far into the picture the shading reaches.")]
        public float shadeFeather = 0.45f;
        [Range(0f, 1f)] public float shadeStrength = 0.42f;

        [Header("Squint when aiming (added to the above)")]
        public Vector2 aimFrameInset = new Vector2(0.02f, 0.05f);
        public Vector2 aimShadeInset = new Vector2(0.06f, 0.04f);
        public float aimShadeStrength = 0.04f;

        [Header("Focus: sharp up to a distance, soft beyond it")]
        [Tooltip("Sharp up to this far (m); further away goes out of focus. The Tab debug menu overrides it (DebugMenu.FocusDistance).")]
        public float sharpDistance = 15f;
        [Tooltip("Over how far past the sharp distance the blur comes in fully, as a share of that distance.")]
        [Range(0.1f, 3f)] public float blurFalloff = 0.8f;
        [Tooltip("Not used any more (was the URP depth of field's radius); see farBlur.")]
        [HideInInspector] public float blurRadius = 1.2f;
        [Tooltip("How soft the far plane gets at full blur: the blur's radius as a share of the screen height.")]
        [Range(0f, 0.02f)] public float farBlur = 0.006f;
        [Tooltip("How quickly the eye adjusts to the aim (the frame squint).")]
        public float focusSpeed = 7f;

        [Header("Wide-angle glass (the body camera's fisheye)")]
        [Tooltip("Fisheye: the picture bent towards an equidistant lens, corners kept in place (Lens Vignette pass). " +
                 "0 = flat, 1 = full fisheye. The Tab debug menu overrides it (DebugMenu.Fisheye).")]
        [Range(0f, 1f)] public float fisheye = 0.5f;
        [Tooltip("Not used any more (was URP's lens distortion); replaced by the fisheye.")]
        [HideInInspector] public float barrel = 0.42f;
        [HideInInspector] public float barrelScale = 1.04f;
        [Tooltip("Colour fringes towards the edges of the frame, from cheap glass.")]
        [Range(0f, 1f)] public float fringing = 0.32f;

        [Header("Softness")]
        [Tooltip("Blur at the very corners of the frame, as a share of the screen height; fades to nothing towards the centre. 0 = off.")]
        [Range(0f, 0.03f)] public float edgeBlur = 0.007f;
        [Tooltip("Where the blur starts: 0 = the centre, 1 = the corners.")]
        [Range(0f, 1f)] public float edgeBlurStart = 0.45f;
        [Tooltip("Smear when the camera moves or turns fast. 0 = off.")]
        [Range(0f, 1f)] public float motionBlur = 0.06f;

        [Header("Driven by cutscenes")]
        [Tooltip("0 = eyes open, 1 = shut: the frame closes in from top and bottom like eyelids.")]
        [Range(0f, 1f)] public float eyelids;
        [Tooltip("0 = normal focus, 1 = everything past arm's length is badly out of focus (coming round, dazed).")]
        [Range(0f, 1f)] public float daze;
        [Tooltip("Focus blur at full daze: the blur's radius as a share of the screen height.")]
        [Range(0f, 0.03f)] public float dazeBlur = 0.014f;

        static readonly int OnId = Shader.PropertyToID("_LensOn");
        static readonly int FrameId = Shader.PropertyToID("_LensFrame");
        static readonly int ShadeId = Shader.PropertyToID("_LensShade");
        static readonly int BlurId = Shader.PropertyToID("_LensBlur");
        static readonly int FocusId = Shader.PropertyToID("_LensFocus");
        static readonly int FisheyeId = Shader.PropertyToID("_LensFisheye");

        Volume volume;
        VolumeProfile profile;
        DepthOfField focus;
        LensDistortion glass;
        ChromaticAberration fringe;
        MotionBlur smear;
        PlayerShooting shooting;
        Camera cam;
        float aim;

        void Awake()
        {
            cam = GetComponent<Camera>();
            var player = GetComponentInParent<BodycamPlayerController>();
            shooting = player ? player.GetComponentInChildren<PlayerShooting>(true) : null;

            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.name = "LensLook (runtime)";
            // URP's own depth of field off: its half-resolution Gaussian left hard, streaky edges round the
            // far patches. The focus is drawn by the Lens Focus full-screen feature instead (far only: the
            // hands and the gun right in front stay sharp).
            focus = profile.Add<DepthOfField>(true);
            focus.mode.value = DepthOfFieldMode.Off;
            profile.Add<Vignette>(true).intensity.value = 0f;   // the scenes' own, replaced by the frame
            glass = profile.Add<LensDistortion>(false);
            fringe = profile.Add<ChromaticAberration>(false);
            smear = profile.Add<MotionBlur>(false);
            smear.quality.Override(MotionBlurQuality.Medium);

            var go = new GameObject("LensLookVolume");
            volume = go.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 10f;
            volume.sharedProfile = profile;
        }

        void OnEnable() => Shader.SetGlobalFloat(OnId, 1f);

        void OnDisable()
        {
            Shader.SetGlobalFloat(OnId, 0f);
            Shader.SetGlobalVector(FocusId, Vector4.zero);   // no player lens: the focus pass does nothing
            Shader.SetGlobalVector(FisheyeId, Vector4.zero);
        }

        void OnDestroy()
        {
            if (volume) Destroy(volume.gameObject);
            if (profile) Destroy(profile);
        }

        void LateUpdate()
        {
            float want = shooting ? shooting.AimWeight : 0f;
            aim = Mathf.Lerp(aim, want, 1f - Mathf.Exp(-focusSpeed * Time.deltaTime));

            float sharp = BodycamHorror.UI.DebugMenu.FocusDistance > 0f ? BodycamHorror.UI.DebugMenu.FocusDistance : sharpDistance;
            float blur = farBlur;
            if (daze > 0f)
            {
                sharp = Mathf.Lerp(sharp, 0.35f, daze);
                blur = Mathf.Lerp(farBlur, dazeBlur, daze);
            }
            Shader.SetGlobalVector(FocusId, new Vector4(sharp, sharp * (1f + blurFalloff), blur, blur > 0f ? 1f : 0f));
            glass.active = false;   // the fisheye in the Lens Vignette pass bends the picture instead
            float bend = BodycamHorror.UI.DebugMenu.Fisheye >= 0f ? BodycamHorror.UI.DebugMenu.Fisheye : fisheye;
            Shader.SetGlobalVector(FisheyeId, new Vector4(bend, Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad), 0f, 0f));
            fringe.intensity.Override(fringing);
            smear.active = motionBlur > 0f;
            smear.intensity.Override(motionBlur);
            Shader.SetGlobalVector(BlurId, new Vector4(edgeBlur, edgeBlurStart, 0f, 0f));

            Vector2 frame = frameInset + aimFrameInset * aim;
            Vector2 shade = shadeInset + aimShadeInset * aim;
            float feather = frameFeather;
            float shadeK = Mathf.Clamp01(shadeStrength + aimShadeStrength * aim);
            if (eyelids > 0f)
            {
                // eyelids: the frame narrows to a slit from top and bottom, softer the more it closes; shut = black
                float lid = Mathf.Clamp01(eyelids);
                frame.y = Mathf.Lerp(frame.y, 1f, lid);   // 1 = the box has no height left: all black
                frame.x = Mathf.Lerp(frame.x, 0.25f, lid);
                feather = Mathf.Lerp(frameFeather, 0.55f, Mathf.Sqrt(lid));
                shade.y = Mathf.Lerp(shade.y, 0.9f, lid);
                shadeK = Mathf.Lerp(shadeK, 0.9f, lid);
            }
            Shader.SetGlobalVector(FrameId, new Vector4(frame.x, frame.y, frameRadius, feather));
            Shader.SetGlobalVector(ShadeId, new Vector4(shade.x, shade.y, shadeFeather, shadeK));
        }
    }
}
