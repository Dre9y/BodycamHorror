using System;
using System.Collections.Generic;
using BodycamHorror.Combat;
using BodycamHorror.Player;
using UnityEngine;
using UnityEngine.Rendering;
using Random = UnityEngine.Random;

namespace BodycamHorror.Weapons
{
    /// <summary>
    /// Base for hand-held guns: hitscan from the muzzle (one or several pellets), ammo, muzzle flash,
    /// impacts, generated sounds, plus the handling profile (poses, recoil, spread, melee with it) that
    /// <see cref="PlayerShooting"/> reads. The transform origin is the right-hand grip, barrel along +Z.
    /// </summary>
    public abstract class Firearm : MonoBehaviour
    {
        public enum ReloadStyle { Magazine, PerShell }
        public enum Sfx { Shot, Dry, MagOut, MagIn, Rack, ShellIn }

        [Header("Ballistics")]
        [Tooltip("Damage per pellet (per bullet for single-shot guns).")]
        public float damage = 34f;
        public float range = 150f;
        public float impactForce = 6f;
        [Tooltip("Minimum time between shots.")]
        public float fireInterval = 0.11f;
        public int pelletsPerShot = 1;
        [Tooltip("Cone (deg) the pellets scatter in around the aim line.")]
        public float pelletSpread = 0f;
        [Tooltip("Damage starts dropping at this distance (m)...")]
        public float falloffStart = 1000f;
        [Tooltip("...and bottoms out at this distance (m)...")]
        public float falloffEnd = 1000f;
        [Tooltip("...at this share of full damage.")]
        [Range(0f, 1f)] public float minDamageScale = 1f;
        public LayerMask hitMask = ~(1 << 2);

        [Header("Ammo & reload")]
        public int magazineSize = 15;
        public ReloadStyle reloadStyle = ReloadStyle.Magazine;
        [Tooltip("Magazine: whole reload time. Per shell: time to get into and out of the loading position.")]
        public float reloadTime = 1.6f;
        [Tooltip("Per shell: time to load one shell.")]
        public float shellInsertTime = 0.5f;
        [Tooltip("Extra time to rack / pump after reloading an empty gun.")]
        public float emptyReloadExtra = 0.35f;

        [Header("Handling (pivot space; origin = right-hand grip)")]
        public Vector3 readyPosition = new Vector3(0.03f, -0.15f, 0.36f);
        public Vector3 aimPosition = new Vector3(0f, -0.105f, 0.31f);
        [Tooltip("Slight inward cant of the ready pose (deg).")]
        public float readyCant = -5f;
        [Tooltip("Height of the sight line above the grip origin.")]
        public float sightHeight = 0.094f;
        [Tooltip("How far ahead of the chest something has to be before the gun is tucked away (m).")]
        public float tuckDistance = 0.72f;
        [Tooltip("Accuracy of the aim line itself (deg), hip and aimed; pellets scatter on top of this.")]
        public float hipSpread = 0.7f;
        public float aimSpread = 0.15f;

        [Header("Recoil")]
        public float kickPitch = 330f;
        public float kickYaw = 70f;
        public float kickRoll = 120f;
        public float kickBack = 1.5f;
        public float kickUp = 0.3f;
        [Tooltip("Camera: slow shake / fast shudder / upward kick (deg/s) / roll kick / backward shove (m/s) per shot.")]
        public float cameraTrauma = 0.14f;
        public float cameraTremor = 0.55f;
        public float cameraKickPitch = 110f;
        public float cameraKickRoll = 60f;
        public float cameraKickBack = 0.25f;

        [Header("Melee with this weapon")]
        [Tooltip("On: shove/strike with the gun itself. Off: jab with the free left hand.")]
        public bool meleeWithWeapon = false;
        public float meleeDamage = 25f;
        public float meleeForce = 5f;

        [Header("Parts (built by Build Placeholder Model)")]
        public Transform muzzle;
        public Transform leftGrip;
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
        public float bulletHoleSize = 0.07f;
        [Tooltip("Scales the dust/spark count per hit (lower for many pellets).")]
        public float impactFxScale = 1f;
        [Range(0f, 1f)] public float volume = 0.9f;

        public int Ammo { get; set; }
        public bool CanFire => Time.time >= nextFireTime;
        public Vector3 LeftGripPosition => leftGrip ? leftGrip.position : transform.position;
        /// <summary>Extra pose (pivot space) the weapon wants right now, e.g. rocking while pumping.</summary>
        public virtual void GetHandlingOffset(out Vector3 position, out Vector3 euler) { position = Vector3.zero; euler = Vector3.zero; }

        /// <summary>Raised after every live shot (true if any pellet hit something).</summary>
        public event Action<bool> Fired;

        protected float nextFireTime;
        float flashTimer;
        AudioSource audioSource;
        readonly Dictionary<Sfx, AudioClip> clips = new Dictionary<Sfx, AudioClip>();

        protected virtual void Awake()
        {
            if (!muzzle) BuildPlaceholderModel();
            Ammo = magazineSize;
            ViewModelLighting.MarkViewModel(gameObject);                        // keep the flashlight beam off the gun
            if (flashLight) ViewModelLighting.AlsoLightViewModel(flashLight);   // the muzzle flash lights hands and world
            if (flashMesh) flashMesh.gameObject.SetActive(false);
            if (flashLight) flashLight.intensity = 0f;
        }

        protected virtual void OnDisable()
        {
            flashTimer = 0f;
            if (flashMesh) flashMesh.gameObject.SetActive(false);
            if (flashLight) flashLight.intensity = 0f;
        }

        protected virtual void Update()
        {
            if (flashTimer <= 0f) return;
            flashTimer -= Time.deltaTime;
            float f = Mathf.Clamp01(flashTimer / flashDuration);
            if (flashLight) flashLight.intensity = flashIntensity * f * f;
            if (flashMesh && flashTimer < flashDuration * 0.4f) flashMesh.gameObject.SetActive(false);
        }

        // ------------------------------------------------------------------ firing

        /// <summary>Fires one shot along the barrel. <paramref name="eye"/> guards against shooting
        /// through a wall the muzzle has poked into.</summary>
        public bool Fire(Vector3 eye, float aimSpreadDegrees, GameObject source)
        {
            if (!CanFire || Ammo <= 0) return false;
            nextFireTime = Time.time + fireInterval;
            Ammo--;

            Vector3 origin = muzzle.position;
            Vector3 aim = Cone(muzzle.forward, aimSpreadDegrees);
            bool anyHit = false;

            if (Physics.Linecast(eye, origin, out RaycastHit blocked, hitMask, QueryTriggerInteraction.Ignore))
            {
                // muzzle is inside / behind something: the whole shot goes into it
                ApplyHit(blocked, aim, source, pelletsPerShot);
                anyHit = true;
            }
            else
            {
                for (int i = 0; i < pelletsPerShot; i++)
                {
                    Vector3 dir = pelletsPerShot > 1 ? Cone(aim, pelletSpread) : aim;
                    if (Physics.Raycast(origin, dir, out RaycastHit hit, range, hitMask, QueryTriggerInteraction.Ignore))
                    {
                        ApplyHit(hit, dir, source, 1);
                        anyHit = true;
                    }
                }
            }

            flashTimer = flashDuration;
            if (flashLight) flashLight.intensity = flashIntensity;
            if (flashMesh)
            {
                flashMesh.gameObject.SetActive(true);
                flashMesh.localRotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
                flashMesh.localScale = Vector3.one * Random.Range(0.8f, 1.25f);
            }
            Play(Sfx.Shot, Random.Range(0.94f, 1.06f));
            OnShot();
            Fired?.Invoke(anyHit);
            return true;
        }

        /// <summary>Called right after a live shot (slide snap, pump...).</summary>
        protected virtual void OnShot() { }

        /// <summary>Rack / pump after reloading an empty gun.</summary>
        public virtual void Rack() => Play(Sfx.Rack);

        public void DryFire()
        {
            Play(Sfx.Dry);
            nextFireTime = Time.time + 0.2f;
        }

        void ApplyHit(RaycastHit hit, Vector3 dir, GameObject source, int pellets)
        {
            float falloff = Mathf.Lerp(1f, minDamageScale, Mathf.InverseLerp(falloffStart, falloffEnd, hit.distance));
            var damageable = hit.collider.GetComponentInParent<IDamageable>();
            damageable?.TakeDamage(new DamageInfo
            {
                amount = damage * falloff * pellets, point = hit.point, normal = hit.normal, direction = dir,
                collider = hit.collider, source = source
            });

            if (hit.rigidbody && !hit.rigidbody.isKinematic)
                hit.rigidbody.AddForceAtPosition(dir * (impactForce * falloff * pellets), hit.point, ForceMode.Impulse);

            var fx = ImpactEffects.Get();
            bool moving = hit.rigidbody || damageable != null;
            fx.BulletHole(hit, moving ? hit.collider.transform : null, bulletHoleMaterial, bulletHoleSize);
            fx.Burst(hit.point, hit.normal, dir, impactMaterial, impactFxScale);
        }

        static Vector3 Cone(Vector3 forward, float degrees)
        {
            if (degrees <= 0f) return forward;
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            if (right.sqrMagnitude < 0.0001f) right = Vector3.right;
            right.Normalize();
            Vector3 up = Vector3.Cross(forward, right);
            Vector2 r = Random.insideUnitCircle * Mathf.Tan(degrees * Mathf.Deg2Rad);
            return (forward + right * r.x + up * r.y).normalized;
        }

        // ------------------------------------------------------------------ audio

        /// <summary>Generated clips are not serialized (a Play Mode script reload drops them), so they
        /// are made on demand.</summary>
        protected abstract AudioClip CreateClip(Sfx sfx);

        public void Play(Sfx sfx, float pitch = 1f)
        {
            if (!audioSource)
            {
                audioSource = GetComponent<AudioSource>();
                if (!audioSource) audioSource = gameObject.AddComponent<AudioSource>();
                audioSource.playOnAwake = false;
                audioSource.spatialBlend = 0f;
            }
            if (!clips.TryGetValue(sfx, out AudioClip clip) || !clip) clips[sfx] = clip = CreateClip(sfx);
            if (!clip) return;
            audioSource.pitch = pitch;
            audioSource.PlayOneShot(clip, volume);
        }

        // ------------------------------------------------------------------ placeholder model

        /// <summary>Rebuilds the boxy placeholder model. Safe to call in edit mode.</summary>
        [ContextMenu("Build Placeholder Model")]
        public void BuildPlaceholderModel()
        {
            foreach (var n in new[] { "Model", "Muzzle", "LeftGrip" })
            {
                var t = transform.Find(n);
                if (t) DestroyImmediate(t.gameObject);
            }
            var model = new GameObject("Model").transform;
            model.SetParent(transform, false);
            model.gameObject.layer = gameObject.layer;
            BuildModel(model);
        }

        /// <summary>Builds the model under <paramref name="model"/> and sets muzzle / leftGrip / flash parts.</summary>
        protected abstract void BuildModel(Transform model);

        protected Material Polymer => polymerMaterial ? polymerMaterial : Fallback(new Color(0.06f, 0.06f, 0.065f));
        protected Material Metal => metalMaterial ? metalMaterial : Fallback(new Color(0.12f, 0.12f, 0.13f));

        protected Transform MakePoint(string pointName, Transform parent, Vector3 localPosition)
        {
            var t = new GameObject(pointName).transform;
            t.SetParent(parent, false);
            t.localPosition = localPosition;
            t.gameObject.layer = gameObject.layer;
            return t;
        }

        /// <summary>Muzzle flash (three crossed additive quads) and its light under the muzzle.</summary>
        protected void BuildMuzzleFlash(float size, float lightRange)
        {
            flashMesh = MakePoint("MuzzleFlash", muzzle, Vector3.zero);
            if (flashMaterial)
            {
                FlashQuad(flashMesh, new Vector3(0f, 0f, 0.05f * size), Quaternion.Euler(0f, 90f, 0f), new Vector3(0.14f, 0.07f, 1f) * size);
                FlashQuad(flashMesh, new Vector3(0f, 0f, 0.05f * size), Quaternion.Euler(90f, 90f, 0f), new Vector3(0.14f, 0.07f, 1f) * size);
                FlashQuad(flashMesh, new Vector3(0f, 0f, 0.01f), Quaternion.identity, new Vector3(0.09f, 0.09f, 1f) * size);
            }
            var lightT = MakePoint("FlashLight", muzzle, new Vector3(0f, 0f, 0.08f));
            flashLight = lightT.gameObject.AddComponent<Light>();
            flashLight.type = LightType.Point;
            flashLight.range = lightRange;
            flashLight.color = new Color(1f, 0.72f, 0.42f);
            flashLight.intensity = 0f;
            flashLight.shadows = LightShadows.None;
        }

        protected Transform Box(Transform parent, string boxName, Vector3 pos, Vector3 size, Material mat, float tiltX = 0f, bool parentScaled = false)
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
            go.transform.localScale = new Vector3(scale.x, scale.y, 1f);
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
