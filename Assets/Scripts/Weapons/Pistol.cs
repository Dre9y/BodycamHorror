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

        [Header("Animated hands (empty = procedural arms and the model above)")]
        [Tooltip("Rig with arms, gun and clips. Replaces the shared arms while this gun is out.")]
        public AnimatedHands handsPrefab;

        // the pistol comes bare: its light is a separate pickup (scenes saved before the field existed get this too)
        public Pistol() => startWithLight = false;

        float slideOffset;
        Vector3 slideRest;
        AnimatedHands hands;

        protected override void Awake()
        {
            base.Awake();
            if (handsPrefab) SetUpHands();
            else if (slide) slideRest = slide.localPosition;
        }

        /// <summary>Swaps the static model for the animated rig; handling and timings come from its clips.</summary>
        void SetUpHands()
        {
            var player = GetComponentInParent<Player.BodycamPlayerController>();
            var shooting = GetComponentInParent<PlayerShooting>();
            var model = transform.Find("Model");
            if (model) model.gameObject.SetActive(false);

            hands = Instantiate(handsPrefab, transform.parent, false);
            hands.name = handsPrefab.name;
            hands.Bind(this, player, shooting);

            ClipsDriveRun = ClipsDriveSwitch = ClipsDriveReload = true;
            readyPosition = hands.GunPosition;
            readyCant = hands.IdleCant;
            sightHeight = hands.SightHeight;
            aimPosition = new Vector3(0f, -hands.SightHeight, hands.aimDistance);
            if (muzzle) muzzle.SetLocalPositionAndRotation(hands.MuzzleLocal, Quaternion.identity);
            reloadTime = hands.ReloadLength;
            emptyReloadExtra = 0f;
            DrawTime = hands.DrawLength;
            HolsterTime = hands.HolsterLength;
            ReloadMagOutAt = hands.magOutAt;
            ReloadMagInAt = hands.magInAt;
            ReloadRackAt = hands.slideCloseAt;
        }

        public override Transform VisualRoot => hands ? hands.GunAnchor : transform;

        void OnEnable()
        {
            if (hands) hands.gameObject.SetActive(true);
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            if (hands) hands.gameObject.SetActive(false);
        }

        protected override void Update()
        {
            base.Update();
            if (hands || !slide) return;
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
            var mesh = InstantiateModel(model, modelPrefab, modelScale, Vector3.zero, modelGripPoint);
            slide = MakePoint("Slide", model, Vector3.zero);
            MoveParts(mesh, slide, slideParts);

            muzzle = MakePoint("Muzzle", transform, ModelPoint(mesh, modelMuzzlePoint));
            leftGrip = MakePoint("LeftGrip", transform, new Vector3(-0.036f, -0.022f, 0.004f));
            BuildMuzzleFlash(1f, 8f);
        }
    }
}
