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
        [Tooltip("How much of the surface's colour the bounce takes (0 = the lamp's own colour, 1 = fully the surface's hue).")]
        [Range(0f, 1f)] public float tint = 1f;
        [Tooltip("When something that moves (an enemy) is in the way, the bounce fades out instead of coming off the wall behind it.")]
        public bool blockedByDynamic;
        [Tooltip("Longest reach of the bounce (m); 0 = up to the lamp's own range.")]
        public float maxRange;
        [Tooltip("How fast the bounce fades in and out (1/s): no pop when the lit surface changes.")]
        public float fade = 8f;

        Light source, bounce;
        Vector3 smoothedPos, smoothedNormal = Vector3.up;
        Color albedo;
        Renderer lastRenderer;
        bool placed, jumping;
        float level;   // 0..1 of the full bounce

        /// <summary>Settings for a light carried around (the flashlight): faint, neutral, short, and never off an enemy's back wall.</summary>
        public void MakeHandheld()
        {
            strength = 0.45f;
            standOff = 0.35f;
            rangeScale = 0.9f;
            maxRange = 6f;
            tint = 0.2f;
            blockedByDynamic = true;
            follow = 10f;
            fade = 5f;
            // the light monster stands on Ignore Raycast, like the player: look at that layer too, minus the one carrying the light
            mask = ~0;
            ignoreRoot = transform.root;
        }

        Transform ignoreRoot;

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
            if (!lit) { bounce.enabled = false; level = 0f; return; }

            Vector3 origin = source.transform.position;
            Vector3 dir = source.type == LightType.Spot ? source.transform.forward : Vector3.down;
            float reach = source.range;
            float step = 1f - Mathf.Exp(-fade * Time.deltaTime);
            bool blocked;
            if (!FirstStaticHit(origin, dir, reach, out RaycastHit hit, out blocked))
            {
                // nothing to bounce off: fade out where it is
                level = Mathf.Lerp(level, 0f, step);
                bounce.intensity *= 1f - step;
                bounce.enabled = level > 0.01f;
                if (!bounce.enabled) placed = false;
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
            depth = BehindDepth(hit.point, hit.normal);
            Vector3 target = hit.point - hit.normal * depth;
            Vector3 normal = hit.normal;
            // a jump to a far surface (the beam off an edge, onto the far wall) fades out, moves, and fades back in,
            // instead of the patch of light sliding across the room
            if (placed && (target - smoothedPos).sqrMagnitude > 2.25f) jumping = true;
            if (jumping && level < 0.08f) { jumping = false; placed = false; }
            float k = placed ? 1f - Mathf.Exp(-follow * Time.deltaTime) : 1f;
            if (!jumping)
            {
                smoothedPos = Vector3.Lerp(smoothedPos, target, k);
                smoothedNormal = placed ? Vector3.Slerp(smoothedNormal, normal, k) : normal;
                placed = true;
            }
            bounce.transform.SetPositionAndRotation(smoothedPos, Quaternion.LookRotation(smoothedNormal));
            level = Mathf.Lerp(level, blocked || jumping ? 0f : 1f, step);

            // flux the lamp sends into its cone, I * 2pi(1 - cos(half angle)), comes back times the albedo from a
            // Lambertian patch, whose peak intensity is flux / pi: I_bounce = albedo * I * 2(1 - cos). Flux is kept,
            // so there is no distance term.
            float cone = source.type == LightType.Spot ? 1f - Mathf.Cos(source.spotAngle * 0.5f * Mathf.Deg2Rad) : 0.5f;
            float lum = albedo.r * 0.3f + albedo.g * 0.59f + albedo.b * 0.11f;
            float peak = Mathf.Max(albedo.r, Mathf.Max(albedo.g, albedo.b), 1e-4f);
            Color hue = Color.Lerp(Color.white, albedo / peak, tint);
            bounce.color = source.color * hue;   // hue of the surface (as much as tint lets); brightness goes in intensity
            bounce.intensity = source.intensity * strength * 2f * cone * lum * level;
            // stays local: a pool of reflected light around the lit patch, not a fill for the whole room
            float far = maxRange > 0f ? Mathf.Min(maxRange, source.range) : source.range;
            float range = Mathf.Clamp((hit.distance + 2f) * rangeScale, Mathf.Min(2.5f, far), far);
            // the bounce has no shadows: kept inside the room it is in, or it shines through the walls next door
            range = Mathf.Min(range, RoomReach(hit.point, hit.normal, range));
            bounce.range = range + depth;
            bounce.enabled = level > 0.01f;
        }

        static readonly RaycastHit[] hits = new RaycastHit[16];

        /// <summary>
        /// How far behind the lit surface the virtual lamp may sit: <see cref="standOff"/>, but never out through the
        /// back of a thin wall or floor, where it would sit in the room next door and light it (it has no shadows).
        /// </summary>
        float BehindDepth(Vector3 point, Vector3 normal)
        {
            Vector3 back = point - normal * standOff;
            // from the far spot back towards the surface: hitting something first means that spot is past the
            // wall's other face, in open space; the wall is only (standOff - distance) thick
            if (Physics.Raycast(back, normal, out RaycastHit face, standOff - 0.005f, mask & ~(1 << 2), QueryTriggerInteraction.Ignore))
                return Mathf.Max(0.01f, (standOff - face.distance) * 0.7f);
            return standOff;
        }

        float depth;
        Vector3 reachAt;
        float reachCached = -1f;
        static readonly float[] spokes = new float[24];

        /// <summary>
        /// How far the room reaches from the lit patch: the farthest of the nearest surfaces in a fan of directions
        /// out of it (along the surface and up off it), from about head height above a floor. Everything the bounce
        /// may light is within that, so the light stops at the room's own walls. Cached while the patch stays put.
        /// </summary>
        float RoomReach(Vector3 point, Vector3 normal, float limit)
        {
            if (reachCached >= 0f && (point - reachAt).sqrMagnitude < 0.0625f) return reachCached;
            reachAt = point;
            Vector3 origin = point + normal * 0.6f;
            Vector3 t1 = Vector3.Cross(normal, Mathf.Abs(normal.y) > 0.9f ? Vector3.forward : Vector3.up).normalized;
            Vector3 t2 = Vector3.Cross(normal, t1);
            int roomMask = mask & ~(1 << 2);   // not the player or the living infected
            const int Spokes = 12;
            int n = 0;
            for (int ring = 0; ring < 2; ring++)
            {
                float lift = ring == 0 ? 0.15f : 0.6f;   // nearly along the surface, then tilted up off it
                for (int i = 0; i < Spokes; i++)
                {
                    float a = (i + ring * 0.5f) * Mathf.PI * 2f / Spokes;
                    Vector3 dir = ((t1 * Mathf.Cos(a) + t2 * Mathf.Sin(a)) + normal * lift).normalized;
                    spokes[n++] = Physics.Raycast(origin, dir, out RaycastHit h, limit, roomMask, QueryTriggerInteraction.Ignore) ? h.distance : limit;
                }
            }
            // not the very farthest: down a corridor one or two spokes run long, and the side rooms within that
            // distance would still be lit through their walls. Three quarters of the way out is the room's size.
            System.Array.Sort(spokes, 0, n);
            float reach = spokes[Mathf.Min(n - 1, Mathf.CeilToInt(n * 0.75f) - 1)];
            float up = Physics.Raycast(origin, normal, out RaycastHit top, limit, roomMask, QueryTriggerInteraction.Ignore) ? top.distance : limit;
            reachCached = Mathf.Clamp(Mathf.Max(reach, Mathf.Min(up, 3f)) + 0.6f + 0.3f, 1.5f, limit);
            return reachCached;
        }

        /// <summary>
        /// Nearest hit on level geometry. Enemies, props with rigidbodies and anything animated are skipped: they are
        /// small and move, so light bounced off them would hang in front of them and light them up.
        /// </summary>
        bool FirstStaticHit(Vector3 origin, Vector3 dir, float reach, out RaycastHit best, out bool blocked)
        {
            best = default;
            int n = Physics.RaycastNonAlloc(origin, dir, hits, reach, mask, QueryTriggerInteraction.Ignore);
            float nearest = float.MaxValue, nearestDynamic = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                Collider c = hits[i].collider;
                if (ignoreRoot && c.transform.IsChildOf(ignoreRoot)) continue;
                if (IsDynamic(c) || c.gameObject.layer == 2) { nearestDynamic = Mathf.Min(nearestDynamic, hits[i].distance); continue; }
                if (hits[i].distance >= nearest) continue;
                nearest = hits[i].distance;
                best = hits[i];
            }
            blocked = blockedByDynamic && nearestDynamic < nearest;
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
                // Mixed/Baked lamps already have their bounce baked into the probe volume.
                if (l.bakingOutput.isBaked && l.bakingOutput.lightmapBakeType != LightmapBakeType.Realtime) continue;
                var b = l.gameObject.AddComponent<BounceLight>();
                if (l.GetComponentInParent<Player.BodycamFlashlight>()) b.MakeHandheld();
            }
        }
    }
}
