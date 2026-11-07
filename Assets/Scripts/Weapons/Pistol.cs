using UnityEngine;

namespace BodycamHorror.Weapons
{
    /// <summary>
    /// Semi-auto pistol held in both hands. Slide snaps back on every shot and locks back on an empty
    /// magazine; melee is a jab with the free left hand.
    /// </summary>
    public class Pistol : Firearm
    {
        [Header("Pistol")]
        public Transform slide;
        public float slideTravel = 0.028f;

        [Header("Model (empty = boxy placeholder)")]
        [Tooltip("Imported pistol model (barrel along +Z, up +Y). Rebuild with Build Placeholder Model after changing.")]
        public GameObject modelPrefab;
        [Tooltip("Uniform scale applied to the model.")]
        public float modelScale = 0.045f;
        [Tooltip("Point in model space that sits at the grip origin (where the right hand holds).")]
        public Vector3 modelGripPoint = new Vector3(0f, -0.42f, -1.1f);
        [Tooltip("Point in model space at the front of the barrel, on the bore axis.")]
        public Vector3 modelMuzzlePoint = new Vector3(0f, 1.244f, 2.173f);
        [Tooltip("Model parts that move with the slide.")]
        public string[] slideParts = { "slid_low", "front scoop_low", "behind scoop_low", "led_low", "nail_low", "hh_low", "needle_low" };

        float slideOffset;
        Vector3 slideRest;

        protected override void Awake()
        {
            base.Awake();
            if (slide) slideRest = slide.localPosition;
        }

        protected override void Update()
        {
            base.Update();
            if (!slide) return;
            // Slide snaps back on the shot and returns; stays locked back on an empty mag.
            float target = Ammo <= 0 ? 1f : 0f;
            slideOffset = Mathf.MoveTowards(slideOffset, target, Time.deltaTime / 0.06f);
            slide.localPosition = slideRest + Vector3.back * (slideTravel * slideOffset);
        }

        protected override void OnShot() => slideOffset = 1f;

        protected override AudioClip CreateClip(Sfx sfx) => sfx switch
        {
            Sfx.Shot => ProceduralAudio.Gunshot(),
            Sfx.Dry => ProceduralAudio.Click(0.025f, 3200f, 0.5f),
            Sfx.MagOut => ProceduralAudio.Click(0.05f, 1400f, 0.6f),
            Sfx.MagIn => ProceduralAudio.DoubleClick(0.07f, 1800f, 0.8f),
            Sfx.Rack => ProceduralAudio.DoubleClick(0.12f, 2400f, 0.9f),
            _ => ProceduralAudio.Click(0.04f, 1600f, 0.6f),
        };

        protected override void BuildModel(Transform model)
        {
            if (modelPrefab) { BuildFromPrefab(model); return; }
            Material poly = Polymer, metal = Metal;
            Box(model, "Grip", new Vector3(0f, -0.012f, -0.014f), new Vector3(0.031f, 0.11f, 0.047f), poly, 12f);
            Box(model, "Frame", new Vector3(0f, 0.046f, 0.042f), new Vector3(0.029f, 0.03f, 0.165f), poly);
            Box(model, "TriggerGuard", new Vector3(0f, 0.018f, 0.045f), new Vector3(0.008f, 0.026f, 0.05f), poly);
            slide = Box(model, "Slide", new Vector3(0f, 0.075f, 0.04f), new Vector3(0.027f, 0.032f, 0.195f), metal);
            Box(slide, "FrontSight", new Vector3(0f, 0.019f, 0.09f), new Vector3(0.005f, 0.007f, 0.006f), metal, 0f, true);
            Box(slide, "RearSight", new Vector3(0f, 0.019f, -0.088f), new Vector3(0.022f, 0.008f, 0.008f), metal, 0f, true);

            muzzle = MakePoint("Muzzle", transform, new Vector3(0f, 0.075f, 0.142f));
            leftGrip = MakePoint("LeftGrip", transform, new Vector3(-0.036f, -0.022f, 0.004f));
            BuildMuzzleFlash(1f, 8f);
        }

        /// <summary>Places the imported model so its grip sits at the origin and splits the slide parts
        /// out under an unscaled Slide pivot, so slideTravel stays in metres.</summary>
        void BuildFromPrefab(Transform model)
        {
            var mesh = Instantiate(modelPrefab, model, false).transform;
            mesh.name = modelPrefab.name;
            mesh.localRotation = Quaternion.identity;
            mesh.localScale = Vector3.one * modelScale;
            mesh.localPosition = -modelGripPoint * modelScale;

            foreach (var c in mesh.GetComponentsInChildren<Component>(true))
                if (c is Collider || c is Animator) DestroyImmediate(c);
            foreach (var t in mesh.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = gameObject.layer;
            foreach (var r in mesh.GetComponentsInChildren<Renderer>(true)) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            slide = MakePoint("Slide", model, Vector3.zero);
            foreach (var partName in slideParts)
            {
                var part = FindDeep(mesh, partName);
                if (part) part.SetParent(slide, true);
                else Debug.LogWarning($"[Pistol] Slide part '{partName}' not found in {modelPrefab.name}.", this);
            }

            muzzle = MakePoint("Muzzle", transform, (modelMuzzlePoint - modelGripPoint) * modelScale);
            leftGrip = MakePoint("LeftGrip", transform, new Vector3(-0.036f, -0.022f, 0.004f));
            BuildMuzzleFlash(1f, 8f);
        }

        static Transform FindDeep(Transform root, string childName)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == childName) return t;
            return null;
        }
    }
}
