using UnityEngine;
using UnityEngine.Rendering;

namespace BodycamHorror.Weapons
{
    /// <summary>
    /// Shared bullet holes and impact dust/sparks for every weapon (and melee). Created on first use;
    /// holes on moving or damageable things follow them around.
    /// </summary>
    public class ImpactEffects : MonoBehaviour
    {
        const int MaxHoles = 96;

        static ImpactEffects instance;

        struct Hole { public Transform t; public Transform follow; public bool hasFollow; public Vector3 localPoint; public Quaternion localRot; }
        readonly Hole[] holes = new Hole[MaxHoles];
        int nextHole;
        ParticleSystem fx;

        public static ImpactEffects Get()
        {
            if (!instance) instance = new GameObject("ImpactEffects").AddComponent<ImpactEffects>();
            return instance;
        }

        /// <summary>Leaves a hole decal; <paramref name="follow"/> makes it stick to a moving object.</summary>
        public void BulletHole(RaycastHit hit, Transform follow, Material material, float size)
        {
            if (!material) return;
            ref Hole h = ref holes[nextHole];
            nextHole = (nextHole + 1) % holes.Length;
            if (!h.t)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
                go.name = "BulletHole";
                Destroy(go.GetComponent<Collider>());
                var mr = go.GetComponent<MeshRenderer>();
                mr.shadowCastingMode = ShadowCastingMode.Off;
                go.layer = 2;
                h.t = go.transform;
                h.t.SetParent(transform, false);
            }
            h.t.GetComponent<MeshRenderer>().sharedMaterial = material;
            h.t.gameObject.SetActive(true);
            // Quad faces -Z, so look into the surface.
            Quaternion rot = Quaternion.LookRotation(-hit.normal) * Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
            Vector3 pos = hit.point + hit.normal * 0.004f;
            h.t.SetPositionAndRotation(pos, rot);
            h.t.localScale = Vector3.one * (size * Random.Range(0.8f, 1.2f));
            h.follow = follow;
            h.hasFollow = follow;
            if (follow)
            {
                h.localPoint = follow.InverseTransformPoint(pos);
                h.localRot = Quaternion.Inverse(follow.rotation) * rot;
            }
        }

        /// <summary>Dust puff (and sparks) where something was hit. <paramref name="amount"/> scales the count.</summary>
        public void Burst(Vector3 point, Vector3 normal, Vector3 dir, Material material, float amount = 1f, bool sparks = true)
        {
            if (!material) return;
            if (!fx) fx = CreateSystem(material);

            Vector3 reflect = Vector3.Reflect(dir, normal);
            var p = new ParticleSystem.EmitParams();
            int dust = Mathf.Max(1, Mathf.RoundToInt(7 * amount));
            for (int i = 0; i < dust; i++)
            {
                p.position = point + normal * 0.03f;
                p.velocity = normal * Random.Range(0.4f, 1.2f) + Random.insideUnitSphere * 0.35f;
                p.startSize = Random.Range(0.07f, 0.16f);
                p.startLifetime = Random.Range(0.6f, 1.1f);
                float g = Random.Range(0.28f, 0.4f);
                p.startColor = new Color(g, g * 0.95f, g * 0.9f, 0.55f);
                p.rotation = Random.Range(0f, 360f);
                fx.Emit(p, 1);
            }
            if (!sparks) return;
            int sparkCount = Mathf.Max(1, Mathf.RoundToInt(6 * amount));
            for (int i = 0; i < sparkCount; i++)
            {
                p.position = point + normal * 0.01f;
                p.velocity = (reflect * 0.5f + normal + Random.insideUnitSphere * 0.8f).normalized * Random.Range(2.5f, 5.5f);
                p.startSize = Random.Range(0.012f, 0.022f);
                p.startLifetime = Random.Range(0.12f, 0.3f);
                p.startColor = new Color(2.5f, 1.4f, 0.5f, 1f);
                fx.Emit(p, 1);
            }
        }

        Material lastBloodMaterial;

        /// <summary>Blood spray for callers without a material of their own (a hit has already supplied one).</summary>
        public void BloodDefault(Vector3 point, Vector3 dir, Color color, float amount = 1f)
        {
            if (lastBloodMaterial) Blood(point, dir, dir, lastBloodMaterial, color, amount);
        }

        /// <summary>Blood spray out of a flesh hit, mostly along the bullet's travel and back out of the wound.</summary>
        public void Blood(Vector3 point, Vector3 normal, Vector3 dir, Material material, Color color, float amount = 1f)
        {
            if (!material) return;
            lastBloodMaterial = material;
            if (!fx) fx = CreateSystem(material);
            var p = new ParticleSystem.EmitParams();
            int count = Mathf.Max(2, Mathf.RoundToInt(10 * amount));
            for (int i = 0; i < count; i++)
            {
                bool exit = i % 3 != 0;   // most of it goes on through
                Vector3 baseDir = exit ? dir : normal;
                p.position = point;
                p.velocity = (baseDir + Random.insideUnitSphere * 0.45f).normalized * Random.Range(1.2f, exit ? 4.5f : 2.5f);
                p.startSize = Random.Range(0.03f, exit ? 0.09f : 0.06f);
                p.startLifetime = Random.Range(0.25f, 0.6f);
                p.startColor = color * Random.Range(0.75f, 1.1f);
                fx.Emit(p, 1);
            }
        }

        void LateUpdate()
        {
            for (int i = 0; i < holes.Length; i++)
            {
                ref Hole h = ref holes[i];
                if (!h.hasFollow || !h.t || !h.t.gameObject.activeSelf) continue;
                if (h.follow) h.t.SetPositionAndRotation(h.follow.TransformPoint(h.localPoint), h.follow.rotation * h.localRot);
                else h.t.gameObject.SetActive(false);   // followed object was destroyed
            }
        }

        ParticleSystem CreateSystem(Material mat)
        {
            var go = new GameObject("ImpactFX");
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.loop = true;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 800;
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
    }
}
