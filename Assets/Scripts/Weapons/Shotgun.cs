using UnityEngine;

namespace BodycamHorror.Weapons
{
    /// <summary>
    /// Pump-action shotgun: a spread of pellets that loses punch with distance, an automatic pump
    /// stroke after every shot (the left hand rides the fore-end), shell-by-shell reloading and a
    /// gun-butt shove for melee. Origin = right hand on the pistol grip.
    /// </summary>
    public class Shotgun : Firearm
    {
        [Header("Pump")]
        public Transform pump;
        [Tooltip("How far the fore-end travels back (m).")]
        public float pumpTravel = 0.09f;
        [Tooltip("Pause after the shot before pumping (s).")]
        public float pumpDelay = 0.16f;
        [Tooltip("Back-and-forward stroke time (s).")]
        public float pumpTime = 0.34f;

        public bool IsPumping => pumpStart >= 0f && Time.time - pumpStart < pumpTime;

        float pumpStart = -10f;
        Vector3 pumpRest;
        bool pumpRestSet, pumpSoundPlayed;

        void Reset() => ApplyShotgunDefaults();

        /// <summary>Shotgun numbers (the base class defaults are the pistol's).</summary>
        [ContextMenu("Apply Shotgun Defaults")]
        public void ApplyShotgunDefaults()
        {
            damage = 13f; range = 60f; impactForce = 2.5f;
            pelletsPerShot = 9; pelletSpread = 3.2f;
            falloffStart = 7f; falloffEnd = 28f; minDamageScale = 0.2f;
            magazineSize = 6; reloadStyle = ReloadStyle.PerShell;
            reloadTime = 0.6f; shellInsertTime = 0.48f; emptyReloadExtra = 0.45f;
            fireInterval = pumpDelay + pumpTime + 0.08f;
            readyPosition = new Vector3(0.06f, -0.175f, 0.27f);
            aimPosition = new Vector3(0f, -0.125f, 0.2f);
            readyCant = -3f; sightHeight = 0.066f; tuckDistance = 0.95f;
            hipSpread = 0.6f; aimSpread = 0.2f;
            kickPitch = 700f; kickYaw = 130f; kickRoll = 180f; kickBack = 3.2f; kickUp = 0.7f;
            cameraTrauma = 0.38f; cameraTremor = 1.1f; cameraKickPitch = 260f; cameraKickRoll = 120f; cameraKickBack = 0.7f;
            meleeWithWeapon = true; meleeDamage = 45f; meleeForce = 12f;
            flashIntensity = 14f; flashDuration = 0.07f; bulletHoleSize = 0.05f; impactFxScale = 0.35f;
        }

        protected override void Awake()
        {
            base.Awake();
            if (pump && !pumpRestSet) { pumpRest = pump.localPosition; pumpRestSet = true; }
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            // never leave the fore-end half-way back when holstered mid-pump
            if (pump && pumpRestSet) pump.localPosition = pumpRest;
            if (pumpStart >= 0f && Time.time - pumpStart < pumpTime) pumpStart = -10f;
        }

        protected override void Update()
        {
            base.Update();
            if (!pump || !pumpRestSet) return;
            float t = (Time.time - pumpStart) / pumpTime;
            float back = t >= 0f && t <= 1f ? Mathf.Sin(t * Mathf.PI) : 0f;
            pump.localPosition = pumpRest + Vector3.back * (pumpTravel * back);
            if (t >= 0f && t < 1f && !pumpSoundPlayed) { pumpSoundPlayed = true; Play(Sfx.Rack); }
        }

        protected override void OnShot() => StartPump(pumpDelay);

        public override void Rack()
        {
            StartPump(0f);
            nextFireTime = Mathf.Max(nextFireTime, Time.time + pumpTime);
        }

        void StartPump(float delay)
        {
            pumpStart = Time.time + delay;
            pumpSoundPlayed = false;
        }

        public override void GetHandlingOffset(out Vector3 position, out Vector3 euler)
        {
            float t = (Time.time - pumpStart) / pumpTime;
            float a = t >= 0f && t <= 1f ? Mathf.Sin(t * Mathf.PI) : 0f;
            // the gun rocks a little as the left hand works the pump
            position = new Vector3(0f, -0.012f, -0.02f) * a;
            euler = new Vector3(5f, 0f, -6f) * a;
        }

        protected override AudioClip CreateClip(Sfx sfx) => sfx switch
        {
            Sfx.Shot => ProceduralAudio.ShotgunBlast(),
            Sfx.Dry => ProceduralAudio.Click(0.03f, 2200f, 0.55f),
            Sfx.Rack => ProceduralAudio.DoubleClick(0.16f, 900f, 1f),
            Sfx.ShellIn => ProceduralAudio.Click(0.06f, 1500f, 0.7f),
            Sfx.MagIn => ProceduralAudio.Click(0.06f, 1500f, 0.7f),
            _ => ProceduralAudio.Click(0.05f, 1300f, 0.6f),
        };

        protected override void BuildModel(Transform model)
        {
            Material poly = Polymer, metal = Metal;
            // origin = right hand on the pistol grip; barrel along +Z
            Box(model, "Grip", new Vector3(0f, -0.035f, -0.005f), new Vector3(0.034f, 0.095f, 0.045f), poly, 16f);
            Box(model, "Receiver", new Vector3(0f, 0.035f, 0.06f), new Vector3(0.046f, 0.062f, 0.2f), metal);
            Box(model, "TriggerGuard", new Vector3(0f, -0.006f, 0.03f), new Vector3(0.01f, 0.03f, 0.06f), metal);
            Box(model, "Stock", new Vector3(0f, -0.02f, -0.19f), new Vector3(0.04f, 0.075f, 0.3f), poly, -4f);
            Box(model, "Barrel", new Vector3(0f, 0.05f, 0.34f), new Vector3(0.026f, 0.026f, 0.42f), metal);
            Box(model, "MagTube", new Vector3(0f, 0.018f, 0.29f), new Vector3(0.024f, 0.024f, 0.32f), metal);
            Box(model, "Bead", new Vector3(0f, 0.066f, 0.535f), new Vector3(0.006f, 0.006f, 0.006f), metal);

            // the fore-end slides on an unscaled pivot so the left-hand grip point can ride on it
            pump = MakePoint("Pump", model, new Vector3(0f, 0.018f, 0.3f));
            Box(pump, "ForeEnd", Vector3.zero, new Vector3(0.052f, 0.046f, 0.14f), poly);
            leftGrip = MakePoint("LeftGrip", pump, new Vector3(-0.034f, -0.022f, 0f));
            pumpRest = pump.localPosition;
            pumpRestSet = true;

            muzzle = MakePoint("Muzzle", transform, new Vector3(0f, 0.05f, 0.555f));
            BuildMuzzleFlash(1.7f, 10f);
        }
    }
}
