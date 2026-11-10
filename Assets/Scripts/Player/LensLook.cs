using BodycamHorror.Weapons;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace BodycamHorror.Player
{
    /// <summary>
    /// How the lens sees. The picture sits in a dark rounded frame and only what is right up against
    /// the lens goes soft; aiming sharpens that up and closes the frame a little, like squinting.
    /// The frame is drawn by the Lens Vignette full-screen renderer feature, the blur by a volume of its own.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class LensLook : MonoBehaviour
    {
        [Header("Frame (screen height = 2)")]
        [Tooltip("Where the picture goes fully black, in from the screen edge.")]
        public Vector2 frameInset = new Vector2(0.02f, 0.02f);
        public float frameRadius = 0.5f;
        [Tooltip("Width of the fall-off into the black edge.")]
        public float frameFeather = 0.55f;

        [Header("Shading inside the frame")]
        [Tooltip("Where the shading is at full strength, in from the screen edge.")]
        public Vector2 shadeInset = new Vector2(0.1f, 0.02f);
        [Tooltip("How far into the picture the shading reaches.")]
        public float shadeFeather = 1.05f;
        [Range(0f, 1f)] public float shadeStrength = 0.55f;

        [Header("Squint when aiming (added to the above)")]
        public Vector2 aimFrameInset = new Vector2(0.02f, 0.05f);
        public Vector2 aimShadeInset = new Vector2(0.06f, 0.04f);
        public float aimShadeStrength = 0.04f;

        [Header("Focus")]
        [Tooltip("In focus at this distance (m). Everything further stays practically sharp; closer gets softer towards the lens.")]
        public float focusDistance = 6f;
        [Tooltip("Longer lens = stronger near blur (mm).")]
        [Range(1f, 300f)] public float focalLength = 35f;
        [Tooltip("Lower = stronger near blur.")]
        [Range(1f, 32f)] public float aperture = 24f;
        [Tooltip("The same when aiming: high, so the sights stay crisp.")]
        [Range(1f, 32f)] public float aimAperture = 32f;
        [Tooltip("How quickly the eye refocuses.")]
        public float focusSpeed = 7f;

        static readonly int OnId = Shader.PropertyToID("_LensOn");
        static readonly int FrameId = Shader.PropertyToID("_LensFrame");
        static readonly int ShadeId = Shader.PropertyToID("_LensShade");

        Volume volume;
        VolumeProfile profile;
        DepthOfField focus;
        PlayerShooting shooting;
        float aim;

        void Awake()
        {
            var player = GetComponentInParent<BodycamPlayerController>();
            shooting = player ? player.GetComponentInChildren<PlayerShooting>(true) : null;

            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.name = "LensLook (runtime)";
            focus = profile.Add<DepthOfField>(true);
            focus.mode.value = DepthOfFieldMode.Bokeh;   // the only mode that blurs in front of the focus
            focus.focusDistance.value = focusDistance;
            focus.focalLength.value = focalLength;
            profile.Add<Vignette>(true).intensity.value = 0f;   // the scenes' own, replaced by the frame

            var go = new GameObject("LensLookVolume");
            volume = go.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 10f;
            volume.sharedProfile = profile;
        }

        void OnEnable() => Shader.SetGlobalFloat(OnId, 1f);

        void OnDisable() => Shader.SetGlobalFloat(OnId, 0f);

        void OnDestroy()
        {
            if (volume) Destroy(volume.gameObject);
            if (profile) Destroy(profile);
        }

        void LateUpdate()
        {
            float want = shooting ? shooting.AimWeight : 0f;
            aim = Mathf.Lerp(aim, want, 1f - Mathf.Exp(-focusSpeed * Time.deltaTime));

            focus.aperture.value = Mathf.Lerp(aperture, aimAperture, aim);

            Vector2 frame = frameInset + aimFrameInset * aim;
            Vector2 shade = shadeInset + aimShadeInset * aim;
            Shader.SetGlobalVector(FrameId, new Vector4(frame.x, frame.y, frameRadius, frameFeather));
            Shader.SetGlobalVector(ShadeId, new Vector4(shade.x, shade.y, shadeFeather, Mathf.Clamp01(shadeStrength + aimShadeStrength * aim)));
        }
    }
}
