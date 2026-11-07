using BodycamHorror.Weapons;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

namespace BodycamHorror.Player
{
    /// <summary>
    /// Chest-mounted flashlight next to the body camera. Swings a touch behind the view, flickers when
    /// the body takes a knock, F / D-pad up toggles it. Builds its own beam + soft spill lights.
    /// </summary>
    public class BodycamFlashlight : MonoBehaviour
    {
        public BodycamPlayerController player;
        public bool startOn = true;

        [Header("Beam")]
        public float intensity = 160f;
        public float range = 26f;
        public float spotAngle = 58f;
        public float innerSpotAngle = 22f;
        public Color color = new Color(1f, 0.94f, 0.82f);
        public LightShadows shadows = LightShadows.Soft;

        [Header("Spill (wide, faint, no shadows)")]
        public float spillIntensity = 5f;
        public float spillAngle = 120f;
        public float spillRange = 10f;

        [Header("Hand fill (lights only the hands and gun; the beam skips them, see ViewModelLighting)")]
        public float handFillIntensity = 0.5f;
        public float handFillRange = 1.2f;

        [Header("Feel")]
        [Tooltip("The light trails the view by about this long (s).")]
        public float swingLag = 0.06f;
        public float maxSwing = 10f;
        [Tooltip("Flicker length after a knock (s).")]
        public float knockFlickerTime = 0.45f;

        public bool IsOn { get; private set; }

        Light beam, spill, handFill;
        Quaternion smoothed;
        bool smoothInit;
        float flicker, seed;
        InputAction toggleAction;
        AudioSource audioSource;
        AudioClip clickClip;

        void Awake()
        {
            if (!player) player = GetComponentInParent<BodycamPlayerController>();
            beam = FindOrMakeLight("Beam");
            spill = FindOrMakeLight("Spill");
            handFill = FindOrMakeLight("HandFill");
            Configure();

            seed = Random.value * 100f;
            toggleAction = new InputAction("Flashlight", InputActionType.Button, "<Keyboard>/f");
            toggleAction.AddBinding("<Gamepad>/dpad/up");
            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.spatialBlend = 0f;
            clickClip = ProceduralAudio.DoubleClick(0.035f, 2600f, 0.45f);
            SetOn(startOn, false);
        }

        Light FindOrMakeLight(string lightName)
        {
            Transform t = transform.Find(lightName);
            if (t && t.TryGetComponent(out Light existing)) return existing;
            var go = new GameObject(lightName);
            go.transform.SetParent(transform, false);
            go.layer = gameObject.layer;
            return go.AddComponent<Light>();
        }

        void Configure()
        {
            beam.type = LightType.Spot;
            beam.color = color;
            beam.range = range;
            beam.spotAngle = spotAngle;
            beam.innerSpotAngle = innerSpotAngle;
            beam.shadows = shadows;
            beam.shadowNearPlane = 0.15f;
            beam.renderMode = LightRenderMode.ForcePixel;

            spill.type = LightType.Spot;
            spill.color = color;
            spill.range = spillRange;
            spill.spotAngle = spillAngle;
            spill.innerSpotAngle = spillAngle * 0.3f;
            spill.shadows = LightShadows.None;

            handFill.type = LightType.Point;
            handFill.color = color;
            handFill.range = handFillRange;
            handFill.shadows = LightShadows.None;
            handFill.transform.localPosition = new Vector3(0f, 0.05f, -0.05f);

            ViewModelLighting.SetLightLayers(beam, ViewModelLighting.WorldLayer);
            ViewModelLighting.SetLightLayers(spill, ViewModelLighting.WorldLayer);
            ViewModelLighting.SetLightLayers(handFill, ViewModelLighting.ViewModelLayer);
        }

        void OnEnable()
        {
            toggleAction.Enable();
            if (player) { player.Impacted += OnImpacted; player.LandingResolved += OnLandingResolved; }
        }

        void OnDisable()
        {
            toggleAction.Disable();
            if (player) { player.Impacted -= OnImpacted; player.LandingResolved -= OnLandingResolved; }
        }

        void OnDestroy() => toggleAction?.Dispose();

        public void SetOn(bool on, bool click = true)
        {
            IsOn = on;
            beam.enabled = on;
            spill.enabled = on;
            handFill.enabled = on;
            if (!click || !audioSource) return;
            if (!clickClip) clickClip = ProceduralAudio.DoubleClick(0.035f, 2600f, 0.45f);   // lost on a Play Mode script reload
            audioSource.PlayOneShot(clickClip, 0.8f);
        }

        /// <summary>Makes the light stutter for a moment (bad contact after a knock).</summary>
        public void Flicker(float seconds) => flicker = Mathf.Max(flicker, seconds);

        void LateUpdate()
        {
            if (toggleAction.WasPressedThisFrame()) SetOn(!IsOn);

            // Trail the parent's rotation slightly so the beam swings when turning.
            Transform parent = transform.parent;
            Quaternion target = parent ? parent.rotation : Quaternion.identity;
            if (!smoothInit) { smoothed = target; smoothInit = true; }
            float dt = Mathf.Min(Time.deltaTime, 0.1f);
            smoothed = Quaternion.Slerp(smoothed, target, 1f - Mathf.Exp(-dt / Mathf.Max(swingLag, 0.001f)));
            if (Quaternion.Angle(smoothed, target) > maxSwing) smoothed = Quaternion.RotateTowards(target, smoothed, maxSwing);
            transform.rotation = smoothed;

            if (!IsOn) return;
            float k = 1f;
            if (flicker > 0f)
            {
                flicker -= Time.deltaTime;
                float n = Mathf.PerlinNoise(seed, Time.time * 35f);
                k = n < 0.45f ? 0.05f : Mathf.Lerp(0.5f, 1f, n);
            }
            beam.intensity = intensity * k;
            spill.intensity = spillIntensity * k;
            handFill.intensity = handFillIntensity * k;
        }

        void OnImpacted(ImpactKind kind, float speed)
        {
            if (kind == ImpactKind.Wall || kind == ImpactKind.Stumble) Flicker(knockFlickerTime);
        }

        void OnLandingResolved(LandingKind kind, float drop)
        {
            if (kind == LandingKind.Hard) Flicker(knockFlickerTime * 1.5f);
        }
    }
}
