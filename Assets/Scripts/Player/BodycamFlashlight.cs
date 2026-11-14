using System.Collections.Generic;
using BodycamHorror.Environment;
using BodycamHorror.Weapons;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

namespace BodycamHorror.Player
{
    /// <summary>
    /// Flashlight. With a gun in hand that has a light fitted (Firearm.HasLight; the pistol's is a pickup of
    /// its own) it is a weapon light under the barrel: the beam goes exactly where
    /// the gun points and follows its recoil, sway and switching (a small light body is put on each gun).
    /// Bare-handed it sits on the chest next to the body camera and swings a touch behind the view.
    /// Flickers when the body takes a knock, F / D-pad up toggles it. Builds its own beam + spill lights.
    /// </summary>
    [DefaultExecutionOrder(70)]   // after PlayerShooting / the arms have posed the gun this frame
    public class BodycamFlashlight : MonoBehaviour
    {
        public BodycamPlayerController player;
        [Tooltip("The player has the flashlight. Off: it stays dark and F does nothing until a light is picked up (WeaponPickup, Kind Pistol Light).")]
        public bool owned = true;
        public bool startOn = true;

        [Header("Beam")]
        public float intensity = 70f;
        public float range = 26f;
        public float spotAngle = 58f;
        public float innerSpotAngle = 22f;
        [Tooltip("Size of the lit spot on a wall, as a share of what the two angles above give (1 = as they are). " +
                 "The debug panel (Tab) scales it further.")]
        [Range(0.3f, 1.5f)] public float beamSize = 0.82f;
        public Color color = new Color(1f, 0.94f, 0.82f);
        public LightShadows shadows = LightShadows.Soft;

        [Header("Close up (the beam eases off near a surface, so a wall or a face at arm's length is not a white blot)")]
        [Tooltip("Full brightness from this far (m); closer than this the beam dims.")]
        public float closeDimDistance = 6f;
        [Tooltip("How hard it dims: 2 would keep the lit spot equally bright at any distance, 0 switches this off.")]
        [Range(0f, 2f)] public float closeDimPower = 1.35f;
        [Tooltip("Never dimmer than this share of full brightness.")]
        [Range(0f, 1f)] public float closeDimFloor = 0.03f;
        [Tooltip("How quickly it settles (1/s).")]
        public float closeDimSpeed = 9f;
        public LayerMask closeDimMask = ~(1 << 2);   // not Ignore Raycast (the player)

        [Header("Beam in the air (needs the Light Haze material)")]
        public Material hazeMaterial;
        [Range(0f, 0.05f)] public float hazeDensity = 0.0007f;
        [Range(0f, 1f)] public float hazeWisps = 0.5f;

        [Header("Spill (wide, faint, no shadows)")]
        public float spillIntensity = 5f;
        public float spillAngle = 120f;
        public float spillRange = 10f;

        [Header("Hand fill (light on the chest only: lights the bare hands; the beam skips them, see ViewModelLighting)")]
        public float handFillIntensity = 0.5f;
        public float handFillRange = 1.2f;

        [Header("Dark fill (always on, light off or not: the hands and gun never vanish into pure black)")]
        public float darkFillIntensity = 0.19f;
        public float darkFillRange = 2f;
        [Tooltip("Where it sits, in the space of this light's parent: up and to one side, so it models the gun rather than flattening it.")]
        public Vector3 darkFillOffset = new Vector3(-0.22f, 0.32f, 0.05f);
        public Color darkFillColor = new Color(0.78f, 0.84f, 0.9f);

        [Header("Feel")]
        [Tooltip("The light trails the view by about this long (s).")]
        public float swingLag = 0.06f;
        public float maxSwing = 10f;
        [Tooltip("Flicker length after a knock (s).")]
        public float knockFlickerTime = 0.45f;

        [Header("Weapon light")]
        [Tooltip("Ride on the gun in hand (Firearm.flashlightOffset) when it has a light fitted (Firearm.HasLight); back on the chest with bare hands or a gun without one.")]
        public bool mountOnWeapon = true;
        [Tooltip("Found automatically.")]
        public PlayerShooting shooting;
        public Color bodyColor = new Color(0.06f, 0.06f, 0.065f);

        [Header("Switch sound (empty = generated click)")]
        public AudioClip onClip;
        public AudioClip offClip;
        [Range(0f, 1f)] public float clickVolume = 0.8f;

        public bool IsOn { get; private set; }
        /// <summary>The gun the light is on right now (null = chest).</summary>
        public Firearm MountedOn { get; private set; }

        Light beam, spill, handFill, darkFill;
        Quaternion smoothed;
        bool smoothInit;
        float flicker, seed, closeDim = 1f, appliedSize = -1f, appliedAngle = -1f;
        InputAction toggleAction;
        AudioSource audioSource;
        AudioClip clickClip;
        Vector3 chestPosition;
        readonly Dictionary<Firearm, Renderer> lenses = new Dictionary<Firearm, Renderer>();
        Material bodyMat, lensOnMat, lensOffMat;

        void Awake()
        {
            if (!player) player = GetComponentInParent<BodycamPlayerController>();
            if (!shooting && player) shooting = player.GetComponentInChildren<PlayerShooting>(true);
            chestPosition = transform.localPosition;
            beam = FindOrMakeLight("Beam");
            spill = FindOrMakeLight("Spill");
            handFill = FindOrMakeLight("HandFill");
            darkFill = MakeDarkFill();
            Configure();

            seed = Random.value * 100f;
            toggleAction = new InputAction("Flashlight", InputActionType.Button, "<Keyboard>/f");
            toggleAction.AddBinding("<Gamepad>/dpad/up");
            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.spatialBlend = 0f;
            SetOn(startOn && owned, false);
        }

        /// <summary>A whisper of light by the camera that reaches only the hands and gun (their rendering layer).
        /// It stays by the camera when the flashlight itself rides out on the gun.</summary>
        Light MakeDarkFill()
        {
            var go = new GameObject("ViewModelDarkFill");
            go.transform.SetParent(transform.parent, false);
            go.transform.localPosition = darkFillOffset;
            go.layer = gameObject.layer;
            var fill = go.AddComponent<Light>();
            fill.type = LightType.Point;
            fill.color = darkFillColor;
            fill.intensity = darkFillIntensity;
            fill.range = darkFillRange;
            fill.shadows = LightShadows.None;
            ViewModelLighting.SetLightLayers(fill, ViewModelLighting.ViewModelLayer);
            return fill;
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
            ApplyBeamSize();
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

            // the beam shows in the air: dust and smoke light up along it, and bodies cut shadows into it
            if (hazeMaterial && hazeDensity > 0f)
            {
                if (!beam.TryGetComponent(out LightHaze haze)) haze = beam.gameObject.AddComponent<LightHaze>();
                haze.material = hazeMaterial;
                haze.density = hazeDensity;
                haze.wisps = hazeWisps;
                haze.sourceSize = 0.45f;  // the lens is right by the camera: without this the first metre of beam is a white veil
            }
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
            on &= owned;   // nothing to switch on without one
            IsOn = on;
            beam.enabled = on;
            spill.enabled = on;
            foreach (var lens in lenses.Values) if (lens) lens.sharedMaterial = on ? lensOnMat : lensOffMat;
            if (!click || !audioSource) return;
            AudioClip clip = on ? onClip : offClip;
            if (!clip) clip = clickClip ? clickClip : (clickClip = ProceduralAudio.DoubleClick(0.035f, 2600f, 0.45f));   // lost on a Play Mode script reload
            audioSource.PlayOneShot(clip, clickVolume);
        }

        /// <summary>Makes the light stutter for a moment (bad contact after a knock).</summary>
        public void Flicker(float seconds) => flicker = Mathf.Max(flicker, seconds);

        void LateUpdate()
        {
            // at a door the same key works the door instead
            if (toggleAction.WasPressedThisFrame() && !PlayerInteractor.HasTarget && owned && !UI.PauseMenu.IsOpen) SetOn(!IsOn);

            Firearm gun = mountOnWeapon && shooting && shooting.isActiveAndEnabled ? shooting.CurrentWeapon : null;
            if (gun && gun.muzzle && gun.HasLight)
            {
                // weapon light: rigid on the gun, no trailing
                if (gun != MountedOn) { EnsureLightBody(gun); MountedOn = gun; }
                Transform g = gun.VisualRoot;
                transform.SetPositionAndRotation(g.TransformPoint(gun.muzzle.localPosition + gun.flashlightOffset), g.rotation);
                smoothInit = false;
            }
            else
            {
                if (MountedOn)
                {
                    MountedOn = null;
                    transform.localPosition = chestPosition;
                    smoothInit = false;
                }
                ChestSwing();
            }

            // on a gun the light is ahead of the hands and shines away from them
            handFill.enabled = IsOn && !MountedOn;
            // each gun takes the dark fill at its own strength
            Firearm held = shooting && shooting.isActiveAndEnabled ? shooting.CurrentWeapon : null;
            float fill = darkFillIntensity * (held ? held.darkFillScale : 1f);
            darkFill.intensity = Mathf.Lerp(darkFill.intensity, fill, 1f - Mathf.Exp(-10f * Time.deltaTime));
            UpdateIntensity();
        }

        void ChestSwing()
        {
            // Trail the parent's rotation slightly so the beam swings when turning.
            Transform parent = transform.parent;
            Quaternion target = parent ? parent.rotation : Quaternion.identity;
            if (!smoothInit) { smoothed = target; smoothInit = true; }
            float dt = Mathf.Min(Time.deltaTime, 0.1f);
            smoothed = Quaternion.Slerp(smoothed, target, 1f - Mathf.Exp(-dt / Mathf.Max(swingLag, 0.001f)));
            if (Quaternion.Angle(smoothed, target) > maxSwing) smoothed = Quaternion.RotateTowards(target, smoothed, maxSwing);
            transform.rotation = smoothed;
        }

        /// <summary>Sets the beam's cone so the spot it throws is <see cref="beamSize"/> (times the debug panel's
        /// value) as wide as the angles give: the width of a spot goes with the tangent of the half angle.</summary>
        void ApplyBeamSize()
        {
            float size = Mathf.Max(beamSize * UI.DebugMenu.FlashlightSize, 0.01f);
            if (Mathf.Approximately(size, appliedSize) && Mathf.Approximately(spotAngle, appliedAngle)) return;
            appliedSize = size;
            appliedAngle = spotAngle;
            float outer = Mathf.Clamp(Scaled(spotAngle, size), 6f, 160f);
            beam.spotAngle = outer;
            beam.innerSpotAngle = Mathf.Clamp(Scaled(innerSpotAngle, size), 1f, outer);
        }

        static float Scaled(float angle, float size) =>
            2f * Mathf.Atan(Mathf.Tan(Mathf.Clamp(angle, 1f, 170f) * 0.5f * Mathf.Deg2Rad) * size) * Mathf.Rad2Deg;

        void UpdateIntensity()
        {
            ApplyBeamSize();
            if (!IsOn) return;
            float k = 1f;
            if (flicker > 0f)
            {
                flicker -= Time.deltaTime;
                float n = Mathf.PerlinNoise(seed, Time.time * 35f);
                k = n < 0.45f ? 0.05f : Mathf.Lerp(0.5f, 1f, n);
            }
            float near = CloseDim();
            beam.intensity = intensity * k * near;
            spill.intensity = spillIntensity * k * near;
            handFill.intensity = handFillIntensity * k;
        }

        /// <summary>Share of full brightness for whatever the beam is pointed at right now: 1 out in the
        /// open, falling towards closeDimFloor as the lit surface comes up to the lens.</summary>
        float CloseDim()
        {
            float want = 1f;
            if (closeDimPower > 0f && closeDimDistance > 0f)
            {
                // a fat ray, so a doorframe or a pipe beside the line of sight counts too
                float distance = closeDimDistance;
                if (Physics.SphereCast(transform.position - transform.forward * 0.2f, 0.18f, transform.forward, out RaycastHit hit,
                        closeDimDistance + 0.2f, closeDimMask, QueryTriggerInteraction.Ignore))
                    distance = Mathf.Max(hit.distance - 0.2f, 0f);
                want = Mathf.Max(Mathf.Pow(distance / closeDimDistance, closeDimPower), closeDimFloor);
            }
            closeDim = Mathf.Lerp(closeDim, want, 1f - Mathf.Exp(-closeDimSpeed * Time.deltaTime));
            return closeDim;
        }

        /// <summary>A small light body under the barrel of this gun (built once per gun, hides with it).</summary>
        void EnsureLightBody(Firearm gun)
        {
            if (lenses.ContainsKey(gun) && lenses[gun]) return;
            var old = gun.VisualRoot.Find("WeaponLight");
            if (old) Destroy(old.gameObject);

            var baseMat = GraphicsSettings.currentRenderPipeline ? GraphicsSettings.currentRenderPipeline.defaultMaterial : null;
            if (!bodyMat && baseMat)
            {
                bodyMat = new Material(baseMat) { name = "WeaponLightBody", color = bodyColor };
                if (bodyMat.HasProperty("_BaseColor")) bodyMat.SetColor("_BaseColor", bodyColor);
                if (bodyMat.HasProperty("_Smoothness")) bodyMat.SetFloat("_Smoothness", 0.45f);
                if (bodyMat.HasProperty("_Metallic")) bodyMat.SetFloat("_Metallic", 0.6f);
                lensOffMat = new Material(baseMat) { name = "WeaponLightLensOff", color = new Color(0.2f, 0.2f, 0.2f) };
                if (lensOffMat.HasProperty("_BaseColor")) lensOffMat.SetColor("_BaseColor", new Color(0.2f, 0.2f, 0.2f));
                if (lensOffMat.HasProperty("_Smoothness")) lensOffMat.SetFloat("_Smoothness", 0.95f);
                lensOnMat = new Material(lensOffMat) { name = "WeaponLightLensOn" };
                lensOnMat.EnableKeyword("_EMISSION");
                if (lensOnMat.HasProperty("_EmissionColor")) lensOnMat.SetColor("_EmissionColor", color * 4f);
            }

            var root = new GameObject("WeaponLight").transform;
            root.SetParent(gun.VisualRoot, false);
            root.localPosition = gun.muzzle.localPosition + gun.flashlightOffset;
            root.gameObject.layer = gun.gameObject.layer;
            var lens = BuildLightBody(root, bodyMat, IsOn ? lensOnMat : lensOffMat);
            ViewModelLighting.MarkViewModel(root.gameObject);
            lenses[gun] = lens;
        }

        /// <summary>The weapon light itself, under <paramref name="root"/>: body along +Z, a clamp up to the
        /// gun (+Y) and the lens at the front. Returns the lens. Also used for the light lying in the world
        /// (works in the editor too).</summary>
        public static Renderer BuildLightBody(Transform root, Material body, Material lens)
        {
            Part(root, PrimitiveType.Cylinder, Vector3.zero, new Vector3(90f, 0f, 0f), new Vector3(0.026f, 0.03f, 0.026f), body);
            Part(root, PrimitiveType.Cylinder, new Vector3(0f, 0f, 0.028f), new Vector3(90f, 0f, 0f), new Vector3(0.031f, 0.006f, 0.031f), body);
            Part(root, PrimitiveType.Cube, new Vector3(0f, 0.016f, -0.004f), Vector3.zero, new Vector3(0.012f, 0.012f, 0.032f), body);
            return Part(root, PrimitiveType.Cylinder, new Vector3(0f, 0f, 0.0345f), new Vector3(90f, 0f, 0f), new Vector3(0.026f, 0.0015f, 0.026f), lens);
        }

        static Renderer Part(Transform parent, PrimitiveType type, Vector3 pos, Vector3 euler, Vector3 scale, Material mat)
        {
            var go = GameObject.CreatePrimitive(type);
            if (Application.isPlaying) Destroy(go.GetComponent<Collider>());
            else DestroyImmediate(go.GetComponent<Collider>());
            go.layer = parent.gameObject.layer;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localEulerAngles = euler;
            go.transform.localScale = scale;
            var r = go.GetComponent<Renderer>();
            if (mat) r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            return r;
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
