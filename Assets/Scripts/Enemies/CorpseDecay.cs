using System.Collections.Generic;
using UnityEngine;

namespace BodycamHorror.Enemies
{
    /// <summary>
    /// What happens to a body (or a severed piece) once it is dead weight. For a while it is a normal
    /// physics object; then, once it has come to rest, it freezes: every rigidbody under it goes
    /// kinematic and its colliders drop to the Hitbox layer, so it costs the physics nothing but can
    /// still be shot. Later it dissolves (the Corpse Dissolve shader eats holes through it) and is removed.
    /// </summary>
    public class CorpseDecay : MonoBehaviour
    {
        [Tooltip("Stays a physics object for at least this long (s).")]
        public float physicsTime = 8f;
        [Tooltip("Still tumbling at that point: waits for it to come to rest, but no longer than this (s).")]
        public float settleGrace = 4f;
        [Tooltip("Starts to dissolve this long after death (s).")]
        public float lifetime = 15f;
        [Tooltip("How long the dissolve takes (s).")]
        public float dissolveTime = 2.5f;

        public bool IsFrozen { get; private set; }
        public bool IsDissolving { get; private set; }

        const float RestSpeed = 0.35f;
        static readonly int DissolveId = Shader.PropertyToID("_Dissolve");
        static readonly int UseMetallicMapId = Shader.PropertyToID("_UseMetallicMap");
        static readonly Dictionary<Material, Material> Variants = new Dictionary<Material, Material>();
        static readonly int EdgeColorId = Shader.PropertyToID("_EdgeColor");
        static readonly int[] OwnFloats = { Shader.PropertyToID("_NoiseScale"), Shader.PropertyToID("_EdgeWidth") };
        static Material defaults;
        static Shader dissolveShader;
        static bool shaderLooked;

        Rigidbody[] bodies;
        Renderer[] renderers;
        UnityEngine.Rendering.Universal.DecalProjector[] wounds = System.Array.Empty<UnityEngine.Rendering.Universal.DecalProjector>();
        MaterialPropertyBlock block;
        float age, dissolve;

        /// <summary>Starts the clock on a body that has just died (or a piece that has just come off).</summary>
        public static CorpseDecay Begin(GameObject corpse, float physicsTime, float lifetime, float dissolveTime)
        {
            if (!corpse.TryGetComponent(out CorpseDecay decay)) decay = corpse.AddComponent<CorpseDecay>();
            decay.physicsTime = physicsTime;
            decay.lifetime = lifetime;
            decay.dissolveTime = dissolveTime;
            return decay;
        }

        void Update()
        {
            age += Time.deltaTime;
            if (!IsFrozen && age >= physicsTime && (age >= physicsTime + settleGrace || AtRest())) Freeze();
            if (age < lifetime) return;

            if (!IsDissolving)
            {
                if (!IsFrozen) Freeze();
                if (!StartDissolve()) { Destroy(gameObject); return; }
            }
            dissolve += Time.deltaTime / Mathf.Max(dissolveTime, 0.05f);
            if (dissolve >= 1f) { Destroy(gameObject); return; }
            block.SetFloat(DissolveId, dissolve);
            foreach (Renderer r in renderers) if (r) r.SetPropertyBlock(block);
            // wounds are gone before the skin under them is
            foreach (var wound in wounds) if (wound) wound.fadeFactor = Mathf.Clamp01(1f - dissolve * 2.5f);
        }

        bool AtRest()
        {
            bodies ??= GetComponentsInChildren<Rigidbody>();
            foreach (Rigidbody body in bodies)
                if (body && !body.isKinematic && body.linearVelocity.sqrMagnitude > RestSpeed * RestSpeed) return false;
            return true;
        }

        /// <summary>No more physics: it stays exactly as it lies.</summary>
        public void Freeze()
        {
            if (IsFrozen) return;
            IsFrozen = true;
            int layer = InfectedRagdoll.HitboxLayer;
            bodies = GetComponentsInChildren<Rigidbody>();
            foreach (Rigidbody body in bodies)
            {
                if (!body) continue;
                body.isKinematic = true;
                body.interpolation = RigidbodyInterpolation.None;
                // shots still find it, but nothing collides with it or is tested against it any more
                if (layer >= 0) body.gameObject.layer = layer;
            }
        }

        bool StartDissolve()
        {
            if (!shaderLooked)
            {
                shaderLooked = true;
                dissolveShader = Resources.Load<Shader>("CorpseDissolve");
                if (!dissolveShader) dissolveShader = Shader.Find("BodycamHorror/Corpse Dissolve");
            }
            if (!dissolveShader || !dissolveShader.isSupported) return false;
            if (!defaults) defaults = new Material(dissolveShader) { name = "Corpse Dissolve defaults" };

            IsDissolving = true;
            block = new MaterialPropertyBlock();
            renderers = GetComponentsInChildren<Renderer>();
            wounds = GetComponentsInChildren<UnityEngine.Rendering.Universal.DecalProjector>();
            foreach (Renderer r in renderers)
            {
                if (r is ParticleSystemRenderer) continue;
                Material[] materials = r.sharedMaterials;
                for (int i = 0; i < materials.Length; i++) materials[i] = Variant(materials[i]);
                r.sharedMaterials = materials;
            }
            return true;
        }

        /// <summary>The same material on the dissolve shader (made once per material, shared by every body).</summary>
        static Material Variant(Material source)
        {
            if (!source || source.shader == dissolveShader) return source;
            if (Variants.TryGetValue(source, out Material made) && made) return made;
            made = new Material(dissolveShader) { name = source.name + " (dissolve)" };
            made.CopyPropertiesFromMaterial(source);
            // the copy wipes whatever the source material does not have: put the dissolve's own settings back
            for (int i = 0; i < OwnFloats.Length; i++) made.SetFloat(OwnFloats[i], defaults.GetFloat(OwnFloats[i]));
            made.SetColor(EdgeColorId, defaults.GetColor(EdgeColorId));
            if (!source.HasProperty("_BaseColor") && source.HasProperty("_Color")) made.SetColor("_BaseColor", source.color);
            made.SetFloat(UseMetallicMapId, source.IsKeywordEnabled("_METALLICSPECGLOSSMAP") ? 1f : 0f);
            made.SetFloat(DissolveId, 0f);
            made.renderQueue = -1;   // the copy brought the opaque queue along; back to this shader's own
            Variants[source] = made;
            return made;
        }
    }
}
