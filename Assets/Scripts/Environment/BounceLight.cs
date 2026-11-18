using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace BodycamHorror.Environment
{
    /// <summary>
    /// Light that comes back off what a lamp shines on. Realtime lamps light only what they hit directly,
    /// so a lit wall sits next to a black ceiling and the room reads flat. This puts a soft, shadowless
    /// point light just in front of the surface the lamp (or the flashlight) hits, tinted by that surface
    /// and following the lamp's brightness every frame, so flicker, bursting bulbs and the flashlight
    /// switch carry through to the bounce. Corners then fall into SSAO shade instead of staying flat.
    /// Every shadow-casting spot/point light gets one at scene load (see <see cref="BounceLighting"/>);
    /// add <see cref="NoBounce"/> to a light to leave it out.
    /// </summary>
    [DisallowMultipleComponent]
    public class BounceLight : MonoBehaviour
    {
        [Tooltip("Multiplier on the physically estimated bounce (one patch stands in for every lit surface and for further bounces).")]
        [Range(0f, 6f)] public float strength = 1.2f;
        [Tooltip("How far behind the surface the virtual bounce lamp sits (m): about the size of the lit patch. Further = softer, more even wash with no hot spot near the surface.")]
        public float standOff = 1f;
        [Tooltip("Scales how far the reflected light reaches (lamp-to-surface distance + 2 m).")]
        [Range(0.3f, 3f)] public float rangeScale = 1.8f;
        [Tooltip("Used when the surface has no readable colour.")]
        public Color fallbackAlbedo = new Color(0.45f, 0.45f, 0.43f);
        [Tooltip("How quickly the bounce follows a moving lamp (1/s). Smooths the flashlight sweeping over edges.")]
        public float follow = 14f;
        public LayerMask mask = ~(1 << 2);   // not Ignore Raycast (the player)

        Light source, bounce;
        Vector3 smoothedPos, smoothedNormal = Vector3.up;
        Color albedo;
        Renderer lastRenderer;
        bool placed;

        void Awake()
        {
            source = GetComponent<Light>();
            var go = new GameObject("Bounce");
            go.transform.SetParent(transform, false);
            go.layer = gameObject.layer;
            bounce = go.AddComponent<Light>();
            bounce.type = LightType.Spot;
            bounce.spotAngle = 170f;          // near-cosine spread of a matte surface
            bounce.innerSpotAngle = 30f;
            bounce.shadows = LightShadows.None;
            bounce.renderMode = LightRenderMode.ForcePixel;
            go.AddComponent<NoBounce>();
            CopyLayers();
            albedo = fallbackAlbedo;
        }

        void CopyLayers()
        {
            uint layers = (uint)source.renderingLayerMask | Player.ViewModelLighting.ViewModelLayer;   // bounced light reaches the hands too
            Player.ViewModelLighting.SetLightLayers(bounce, layers);
        }

        void OnDisable() { if (bounce) bounce.enabled = false; }

        void LateUpdate()
        {
            if (!source) { Destroy(this); return; }
            bool lit = source.enabled && source.intensity > 0.001f;
            if (!lit) { bounce.enabled = false; return; }

            Vector3 origin = source.transform.position;
            Vector3 dir = source.type == LightType.Spot ? source.transform.forward : Vector3.down;
            float reach = source.range;
            if (!FirstStaticHit(origin, dir, reach, out RaycastHit hit))
            {
                bounce.enabled = false;
                placed = false;
                return;
            }

            if (hit.collider.TryGetComponent(out Renderer r) || (r = hit.collider.GetComponentInParent<Renderer>()))
            {
                if (r != lastRenderer) { lastRenderer = r; albedo = Albedo(r); }
            }
            else { lastRenderer = null; albedo = fallbackAlbedo; }

            // the lit patch acts as a wide lamp facing out of the surface: it lights what faces it (walls, ceiling,
            // the player) and not the surface itself, so no highlight of its own shows on a glossy floor.
            // The virtual lamp sits *behind* the surface: a patch of light has no hot point, so nothing standing in
            // the pool (an enemy, the player's legs) gets blown out the way it would next to a point source.
            Vector3 target = hit.point - hit.normal * standOff;
            Vector3 normal = hit.normal;
            float k = placed ? 1f - Mathf.Exp(-follow * Time.deltaTime) : 1f;
            smoothedPos = Vector3.Lerp(smoothedPos, target, k);
            smoothedNormal = placed ? Vector3.Slerp(smoothedNormal, normal, k) : normal;
            placed = true;
            bounce.transform.SetPositionAndRotation(smoothedPos, Quaternion.LookRotation(smoothedNormal));

            // flux the lamp sends into its cone, I * 2pi(1 - cos(half angle)), comes back times the albedo from a
            // Lambertian patch, whose peak intensity is flux / pi: I_bounce = albedo * I * 2(1 - cos). Flux is kept,
            // so there is no distance term.
            float cone = source.type == LightType.Spot ? 1f - Mathf.Cos(source.spotAngle * 0.5f * Mathf.Deg2Rad) : 0.5f;
            float lum = albedo.r * 0.3f + albedo.g * 0.59f + albedo.b * 0.11f;
            float peak = Mathf.Max(albedo.r, Mathf.Max(albedo.g, albedo.b), 1e-4f);
            bounce.color = source.color * (albedo / peak);   // hue of the surface; brightness goes in intensity
            bounce.intensity = source.intensity * strength * 2f * cone * lum;
            // stays local: a pool of reflected light around the lit patch, not a fill for the whole room
            bounce.range = Mathf.Clamp((hit.distance + 2f) * rangeScale, 2.5f, source.range) + standOff;
            bounce.enabled = true;
        }

        static readonly RaycastHit[] hits = new RaycastHit[16];

        /// <summary>
        /// Nearest hit on level geometry. Enemies, props with rigidbodies and anything animated are skipped: they are
        /// small and move, so light bounced off them would hang in front of them and light them up.
        /// </summary>
        bool FirstStaticHit(Vector3 origin, Vector3 dir, float reach, out RaycastHit best)
        {
            best = default;
            int n = Physics.RaycastNonAlloc(origin, dir, hits, reach, mask, QueryTriggerInteraction.Ignore);
            float nearest = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                Collider c = hits[i].collider;
                if (hits[i].distance >= nearest || IsDynamic(c)) continue;
                nearest = hits[i].distance;
                best = hits[i];
            }
            return nearest < float.MaxValue;
        }

        static readonly Dictionary<Collider, bool> dynamicCache = new Dictionary<Collider, bool>();

        static bool IsDynamic(Collider c)
        {
            if (c.attachedRigidbody) return true;
            if (dynamicCache.TryGetValue(c, out bool d)) return d;
            d = c.GetComponentInParent<Animator>() || c.GetComponentInParent<CharacterController>()
                || c.GetComponentInParent<Combat.IDamageable>() != null;
            dynamicCache[c] = d;
            return d;
        }

        /// <summary>Surface colour: the material's tint times its texture's average, if one can be read.</summary>
        Color Albedo(Renderer r)
        {
            Material m = r.sharedMaterial;
            if (!m) return fallbackAlbedo;
            Color c = m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : m.HasProperty("_Color") ? m.color : Color.white;
            Color tex = TextureAverage(m.HasProperty("_BaseMap") ? m.GetTexture("_BaseMap") : m.mainTexture);
            c *= tex;
            float lum = c.r * 0.3f + c.g * 0.59f + c.b * 0.11f;
            if (lum < 0.02f) return fallbackAlbedo * 0.3f;
            c.a = 1f;
            return c;
        }

        static readonly Dictionary<Texture, Color> averages = new Dictionary<Texture, Color>();

        /// <summary>Average colour of a texture via a 1x1 GPU downsample (works on non-readable textures).</summary>
        static Color TextureAverage(Texture t)
        {
            if (!t) return new Color(0.5f, 0.5f, 0.5f);
            if (averages.TryGetValue(t, out Color cached)) return cached;
            var rt = RenderTexture.GetTemporary(16, 16, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var small = RenderTexture.GetTemporary(1, 1, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Graphics.Blit(t, rt);
            Graphics.Blit(rt, small);
            var prev = RenderTexture.active;
            RenderTexture.active = small;
            var read = new Texture2D(1, 1, TextureFormat.RGBA32, false, false);
            read.ReadPixels(new Rect(0, 0, 1, 1), 0, 0);
            read.Apply();
            RenderTexture.active = prev;
            Color c = read.GetPixel(0, 0).linear;
            Destroy(read);
            RenderTexture.ReleaseTemporary(rt);
            RenderTexture.ReleaseTemporary(small);
            averages[t] = c;
            return c;
        }
    }

    /// <summary>Gives every shadow-casting spot and point light a bounce, at load and for lights made later.</summary>
    public class BounceLighting : MonoBehaviour
    {
        public static bool Enabled = true;
        static BounceLighting instance;
        float nextScan;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (instance) return;
            var go = new GameObject("BounceLighting");
            DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideInHierarchy;
            instance = go.AddComponent<BounceLighting>();
            SceneManager.sceneLoaded += (_, __) => { if (instance) instance.nextScan = 0f; };
        }

        void Update()
        {
            if (!Enabled || Time.unscaledTime < nextScan) return;
            nextScan = Time.unscaledTime + 1f;
            foreach (Light l in FindObjectsByType<Light>(FindObjectsInactive.Include))
            {
                if (l.shadows == LightShadows.None) continue;
                if (l.type != LightType.Spot && l.type != LightType.Point) continue;
                if (l.GetComponent<BounceLight>() || l.GetComponent<NoBounce>()) continue;
                l.gameObject.AddComponent<BounceLight>();
            }
        }
    }
}
