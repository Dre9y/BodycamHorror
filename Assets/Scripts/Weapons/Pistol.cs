using System;
using BodycamHorror.Combat;
using BodycamHorror.Player;
using UnityEngine;
using UnityEngine.Rendering;
using Random = UnityEngine.Random;

namespace BodycamHorror.Weapons
{
    /// <summary>
    /// The pistol itself: hitscan firing from the muzzle, ammo, slide, muzzle flash, bullet holes,
    /// impact particles and procedurally generated sounds. Its transform origin is the right-hand grip;
    /// posing and input live in <see cref="PlayerShooting"/>.
    /// </summary>
    public class Pistol : MonoBehaviour
    {
        [Header("Ballistics")]
        public float damage = 34f;
        public float range = 150f;
        public float impactForce = 6f;
        [Tooltip("Minimum time between shots (semi-auto).")]
        public float fireInterval = 0.11f;
        public int magazineSize = 15;
        public float reloadTime = 1.6f;
        [Tooltip("Extra time to rack the slide after reloading an empty gun.")]
        public float emptyReloadExtra = 0.35f;
        public LayerMask hitMask = ~(1 << 2);

        [Header("Parts (built by Build Placeholder Model)")]
        public Transform muzzle;
        public Transform leftGrip;
        public Transform slide;
        public Transform flashMesh;
        public Light flashLight;

        [Header("Materials")]
        public Material polymerMaterial;
        public Material metalMaterial;
        public Material flashMaterial;
        public Material bulletHoleMaterial;
        public Material impactMaterial;

        [Header("Effects")]
        public float flashIntensity = 9f;
        public float flashDuration = 0.05f;
        public float slideTravel = 0.028f;
        public int maxBulletHoles = 64;
        public float bulletHoleSize = 0.07f;
        [Range(0f, 1f)] public float volume = 0.9f;

        public int Ammo { get; set; }
        public bool CanFire => Time.time >= nextFireTime;
        /// <summary>Height of the sight line above the grip origin (local).</summary>
        public float SightHeight => 0.094f;
        public Vector3 LeftGripPosition => leftGrip ? leftGrip.position : transform.TransformPoint(new Vector3(-0.036f, -0.022f, 0.004f));

        /// <summary>Raised after every live shot: (hit something, hit info).</summary>
        public event Action<bool, RaycastHit> Fired;

        float nextFireTime, flashTimer, slideOffset;
        Vector3 slideRest;
        AudioSource audioSource;
        AudioClip shotClip, dryClip, magOutClip, magInClip, rackClip;
        ParticleSystem impactFx;

        struct Hole { public Transform t; public Transform follow; public bool hasFollow; public Vector3 localPoint; public Quaternion localRot; }
        Hole[] holes;
        int nextHole;
        Transform holeRoot;

        void Awake()
        {
            if (!muzzle) BuildPlaceholderModel();
            Ammo = magazineSize;
            ViewModelLighting.MarkViewModel(gameObject);                  // keep the flashlight beam off the gun
            if (flashLight) ViewModelLighting.AlsoLightViewModel(flashLight);   // the muzzle flash lights hands and world
            if (slide) slideRest = slide.localPosition;
            if (flashMesh) flashMesh.gameObject.SetActive(false);
            if (flashLight) flashLight.intensity = 0f;

            EnsureAudio();
        }

        /// <summary>Generated clips are not serialized, so a script reload during Play Mode drops
        /// them; rebuild on demand.</summary>
        void EnsureAudio()
        {
            if (!audioSource)
            {
                audioSource = GetComponent<AudioSource>();
                if (!audioSource) audioSource = gameObject.AddComponent<AudioSource>();
                audioSource.playOnAwake = false;
                audioSource.spatialBlend = 0f;
            }
            if (!shotClip) shotClip = ProceduralAudio.Gunshot();
            if (!dryClip) dryClip = ProceduralAudio.Click(0.025f, 3200f, 0.5f);
            if (!magOutClip) magOutClip = ProceduralAudio.Click(0.05f, 1400f, 0.6f);
            if (!magInClip) magInClip = ProceduralAudio.DoubleClick(0.07f, 1800f, 0.8f);
            if (!rackClip) rackClip = ProceduralAudio.DoubleClick(0.12f, 2400f, 0.9f);
        }

        enum Sfx { Shot, Dry, MagOut, MagIn, Rack }

        void Play(Sfx sfx, float pitch = 1f)
        {
            EnsureAudio();   // first, so the clip fields below are valid
            AudioClip clip = sfx switch
            {
                Sfx.Shot => shotClip, Sfx.Dry => dryClip, Sfx.MagOut => magOutClip, Sfx.MagIn => magInClip, _ => rackClip,
            };
            audioSource.pitch = pitch;
            audioSource.PlayOneShot(clip, volume);
        }

        void OnDestroy()
        {
            if (holeRoot) Destroy(holeRoot.gameObject);
            if (impactFx) Destroy(impactFx.gameObject);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (flashTimer > 0f)
            {
                flashTimer -= dt;
                float f = Mathf.Clamp01(flashTimer / flashDuration);
                if (flashLight) flashLight.intensity = flashIntensity * f * f;
                if (flashMesh && flashTimer < flashDuration * 0.4f) flashMesh.gameObject.SetActive(false);
            }

            if (slide)
            {
                // Slide snaps back on the shot and returns; stays locked back on an empty mag.
                float target = Ammo <= 0 ? 1f : 0f;
                slideOffset = Mathf.MoveTowards(slideOffset, target, dt / 0.06f);
                slide.localPosition = slideRest + Vector3.back * (slideTravel * slideOffset);
            }
        }

        void LateUpdate()
        {
            if (holes == null) return;
            for (int i = 0; i < holes.Length; i++)
            {
                ref Hole h = ref holes[i];
                if (!h.t || !h.t.gameObject.activeSelf) continue;
                if (!h.hasFollow) continue;
                if (h.follow) h.t.SetPositionAndRotation(h.follow.TransformPoint(h.localPoint), h.follow.rotation * h.localRot);
                else h.t.gameObject.SetActive(false);   // followed object was destroyed
            }
        }

        // ------------------------------------------------------------------ firing

        /// <summary>Fires one round along the barrel. <paramref name="eye"/> guards against shooting
        /// through a wall the muzzle has poked into.</summary>
        public bool Fire(Vector3 eye, float spreadDegrees, GameObject source)
        {
            if (!CanFire || Ammo <= 0) return false;
            nextFireTime = Time.time + fireInterval;
            Ammo--;

            Vector3 origin = muzzle.position;
            Vector2 r = Random.insideUnitCircle * Mathf.Tan(spreadDegrees * Mathf.Deg2Rad);
            Vector3 dir = (muzzle.forward + muzzle.right * r.x + muzzle.up * r.y).normalized;

            bool hit;
            RaycastHit info;
            if (Physics.Linecast(eye, origin, out info, hitMask, QueryTriggerInteraction.Ignore)) hit = true;
            else hit = Physics.Raycast(origin, dir, out info, range, hitMask, QueryTriggerInteraction.Ignore);

            if (hit) ApplyHit(info, dir, source);

            // feedback
            flashTimer = flashDuration;
            if (flashLight) flashLight.intensity = flashIntensity;
            if (flashMesh)
            {
                flashMesh.gameObject.SetActive(true);
                flashMesh.localRotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
                flashMesh.localScale = Vector3.one * Random.Range(0.8f, 1.25f);
            }
            slideOffset = 1f;
            Play(Sfx.Shot, Random.Range(0.94f, 1.06f));

            Fired?.Invoke(hit, info);
            return true;
        }

        public void DryFire()
        {
            Play(Sfx.Dry);
            nextFireTime = Time.time + 0.2f;
        }

        public void PlayMagOut() => Play(Sfx.MagOut);
        public void PlayMagIn() => Play(Sfx.MagIn);
        public void PlayRack() => Play(Sfx.Rack);

        void ApplyHit(RaycastHit hit, Vector3 dir, GameObject source)
        {
            var damageable = hit.collider.GetComponentInParent<IDamageable>();
            damageable?.TakeDamage(new DamageInfo
            {
                amount = damage, point = hit.point, normal = hit.normal, direction = dir,
                collider = hit.collider, source = source
            });

            if (hit.rigidbody && !hit.rigidbody.isKinematic)
                hit.rigidbody.AddForceAtPosition(dir * impactForce, hit.point, ForceMode.Impulse);

            bool moving = hit.rigidbody || damageable != null;
            SpawnBulletHole(hit, moving ? hit.collider.transform : null);
            SpawnImpactParticles(hit.point, hit.normal, dir);
        }

        void SpawnBulletHole(RaycastHit hit, Transform follow)
        {
            if (!bulletHoleMaterial || maxBulletHoles <= 0) return;
            if (holes == null)
            {
                holes = new Hole[maxBulletHoles];
                holeRoot = new GameObject("BulletHoles").transform;
            }

            ref Hole h = ref holes[nextHole];
            nextHole = (nextHole + 1) % holes.Length;
            if (!h.t)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
                go.name = "BulletHole";
                Destroy(go.GetComponent<Collider>());
                var mr = go.GetComponent<MeshRenderer>();
                mr.sharedMaterial = bulletHoleMaterial;
                mr.shadowCastingMode = ShadowCastingMode.Off;
                go.layer = 2;
                h.t = go.transform;
                h.t.SetParent(holeRoot, false);
            }

            h.t.gameObject.SetActive(true);
            // Quad faces -Z, so look into the surface.
            Quaternion rot = Quaternion.LookRotation(-hit.normal) * Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
            Vector3 pos = hit.point + hit.normal * 0.004f;
            h.t.SetPositionAndRotation(pos, rot);
            h.t.localScale = Vector3.one * (bulletHoleSize * Random.Range(0.8f, 1.2f));
            h.follow = follow;
            h.hasFollow = follow;
            if (follow)
            {
                h.localPoint = follow.InverseTransformPoint(pos);
                h.localRot = Quaternion.Inverse(follow.rotation) * rot;
            }
        }

        void SpawnImpactParticles(Vector3 point, Vector3 normal, Vector3 dir)
        {
            if (!impactMaterial) return;
            if (!impactFx) impactFx = CreateImpactSystem(impactMaterial);

            Vector3 reflect = Vector3.Reflect(dir, normal);
            var p = new ParticleSystem.EmitParams();

            // dust puff
            for (int i = 0; i < 7; i++)
            {
                p.position = point + normal * 0.03f;
                p.velocity = normal * Random.Range(0.4f, 1.2f) + Random.insideUnitSphere * 0.35f;
                p.startSize = Random.Range(0.07f, 0.16f);
                p.startLifetime = Random.Range(0.6f, 1.1f);
                float g = Random.Range(0.28f, 0.4f);
                p.startColor = new Color(g, g * 0.95f, g * 0.9f, 0.55f);
                p.rotation = Random.Range(0f, 360f);
                impactFx.Emit(p, 1);
            }
            // sparks / chips
            for (int i = 0; i < 6; i++)
            {
                p.position = point + normal * 0.01f;
                p.velocity = (reflect * 0.5f + normal + Random.insideUnitSphere * 0.8f).normalized * Random.Range(2.5f, 5.5f);
                p.startSize = Random.Range(0.012f, 0.022f);
                p.startLifetime = Random.Range(0.12f, 0.3f);
                p.startColor = new Color(2.5f, 1.4f, 0.5f, 1f);
                impactFx.Emit(p, 1);
            }
        }

        static ParticleSystem CreateImpactSystem(Material mat)
        {
            var go = new GameObject("PistolImpactFX");
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.loop = true;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 500;
            main.startSpeed = 0f;
            main.gravityModifier = 0.35f;

            var emission = ps.emission;
            emission.rateOverTime = 0f;
            var shape = ps.shape;
            shape.enabled = false;

            var col = ps.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                         new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.6f, 0.4f), new GradientAlphaKey(0f, 1f) });
            col.color = grad;

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 1.6f));

            var vel = ps.limitVelocityOverLifetime;
            vel.enabled = true;
            vel.limit = 6f;
            vel.dampen = 0.15f;

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = mat;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode = ShadowCastingMode.Off;

            ps.Play();
            return ps;
        }

        // ------------------------------------------------------------------ placeholder model

        /// <summary>Builds a boxy pistol around the grip origin (barrel along +Z). Safe to call in edit mode.</summary>
        [ContextMenu("Build Placeholder Model")]
        public void BuildPlaceholderModel()
        {
            var old = transform.Find("Model");
            if (old) DestroyImmediate(old.gameObject);
            foreach (var n in new[] { "Muzzle", "LeftGrip" })
            {
                var t = transform.Find(n);
                if (t) DestroyImmediate(t.gameObject);
            }

            var model = new GameObject("Model").transform;
            model.SetParent(transform, false);
            model.gameObject.layer = gameObject.layer;

            Material poly = polymerMaterial ? polymerMaterial : Fallback(new Color(0.06f, 0.06f, 0.065f));
            Material metal = metalMaterial ? metalMaterial : Fallback(new Color(0.12f, 0.12f, 0.13f));

            Box(model, "Grip", new Vector3(0f, -0.012f, -0.014f), new Vector3(0.031f, 0.11f, 0.047f), poly, 12f);
            Box(model, "Frame", new Vector3(0f, 0.046f, 0.042f), new Vector3(0.029f, 0.03f, 0.165f), poly);
            Box(model, "TriggerGuard", new Vector3(0f, 0.018f, 0.045f), new Vector3(0.008f, 0.026f, 0.05f), poly);
            slide = Box(model, "Slide", new Vector3(0f, 0.075f, 0.04f), new Vector3(0.027f, 0.032f, 0.195f), metal);
            Box(slide, "FrontSight", new Vector3(0f, 0.019f, 0.09f), new Vector3(0.005f, 0.007f, 0.006f), metal, 0f, true);
            Box(slide, "RearSight", new Vector3(0f, 0.019f, -0.088f), new Vector3(0.022f, 0.008f, 0.008f), metal, 0f, true);

            muzzle = new GameObject("Muzzle").transform;
            muzzle.SetParent(transform, false);
            muzzle.localPosition = new Vector3(0f, 0.075f, 0.142f);
            muzzle.gameObject.layer = gameObject.layer;

            leftGrip = new GameObject("LeftGrip").transform;
            leftGrip.SetParent(transform, false);
            leftGrip.localPosition = new Vector3(-0.036f, -0.022f, 0.004f);
            leftGrip.gameObject.layer = gameObject.layer;

            // muzzle flash: three crossed additive quads + a light
            flashMesh = new GameObject("MuzzleFlash").transform;
            flashMesh.SetParent(muzzle, false);
            flashMesh.gameObject.layer = gameObject.layer;
            if (flashMaterial)
            {
                FlashQuad(flashMesh, new Vector3(0f, 0f, 0.05f), Quaternion.Euler(0f, 90f, 0f), new Vector3(0.14f, 0.07f, 1f));
                FlashQuad(flashMesh, new Vector3(0f, 0f, 0.05f), Quaternion.Euler(90f, 90f, 0f), new Vector3(0.14f, 0.07f, 1f));
                FlashQuad(flashMesh, new Vector3(0f, 0f, 0.01f), Quaternion.identity, new Vector3(0.09f, 0.09f, 1f));
            }

            var lightGo = new GameObject("FlashLight");
            lightGo.transform.SetParent(muzzle, false);
            lightGo.transform.localPosition = new Vector3(0f, 0f, 0.08f);
            lightGo.layer = gameObject.layer;
            flashLight = lightGo.AddComponent<Light>();
            flashLight.type = LightType.Point;
            flashLight.range = 8f;
            flashLight.color = new Color(1f, 0.72f, 0.42f);
            flashLight.intensity = 0f;
            flashLight.shadows = LightShadows.None;
        }

        Transform Box(Transform parent, string boxName, Vector3 pos, Vector3 size, Material mat, float tiltX = 0f, bool parentScaled = false)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = boxName;
            go.layer = gameObject.layer;
            DestroyImmediate(go.GetComponent<Collider>());
            var t = go.transform;
            t.SetParent(parent, false);
            if (parentScaled)
            {
                // undo the parent's scale so sizes stay in metres
                Vector3 ps = parent.localScale;
                pos = new Vector3(pos.x / ps.x, pos.y / ps.y, pos.z / ps.z);
                size = new Vector3(size.x / ps.x, size.y / ps.y, size.z / ps.z);
            }
            t.localPosition = pos;
            t.localRotation = Quaternion.Euler(tiltX, 0f, 0f);
            t.localScale = size;
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            return t;
        }

        void FlashQuad(Transform parent, Vector3 pos, Quaternion rot, Vector3 scale)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = "FlashQuad";
            go.layer = gameObject.layer;
            DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = rot;
            go.transform.localScale = scale;
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = flashMaterial;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
        }

        static Material Fallback(Color c)
        {
            var baseMat = GraphicsSettings.currentRenderPipeline ? GraphicsSettings.currentRenderPipeline.defaultMaterial : null;
            if (!baseMat) return null;
            var m = new Material(baseMat) { color = c };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            return m;
        }
    }
}
