using UnityEngine;

namespace BodycamHorror.Environment
{
    /// <summary>
    /// Makes a light misbehave the way its kind does: a fluorescent tube that hums along and now and
    /// then stutters, a bulb or flame that wavers, a screen that jumps between brightness levels as
    /// the picture changes. The glowing part of the fixture can follow along.
    /// </summary>
    [RequireComponent(typeof(Light))]
    public class LightFlicker : MonoBehaviour
    {
        public enum Kind { Fluorescent, Flame, Screen }

        public Kind kind = Kind.Fluorescent;
        [Tooltip("How far the light dips: 0 = steady, 1 = all the way to black.")]
        [Range(0f, 1f)] public float depth = 0.85f;
        [Tooltip("Fluorescent: seconds between stutters. Flame: how fast it wavers. Screen: seconds a brightness level holds.")]
        public Vector2 pace = new Vector2(2.5f, 9f);
        [Tooltip("Fluorescent only: how long one stutter lasts (s).")]
        public Vector2 burst = new Vector2(0.15f, 0.9f);
        [Tooltip("The glowing part of the fixture; its emission follows the light. Optional.")]
        public Renderer glow;

        static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

        Light source;
        float baseIntensity, seed, timer, burstLeft, level = 1f, target = 1f;
        Color baseEmission;
        MaterialPropertyBlock block;

        void Awake()
        {
            source = GetComponent<Light>();
            baseIntensity = source.intensity;
            seed = Random.value * 100f;
            timer = Random.Range(pace.x, pace.y);
            if (glow && glow.sharedMaterial && glow.sharedMaterial.HasProperty(EmissionId))
            {
                baseEmission = glow.sharedMaterial.GetColor(EmissionId);
                block = new MaterialPropertyBlock();
            }
        }

        void OnDisable()
        {
            if (source) source.intensity = baseIntensity;
            if (block != null && glow) glow.SetPropertyBlock(null);
        }

        void Update()
        {
            float t = Time.time, dt = Time.deltaTime;
            float k = 1f;
            switch (kind)
            {
                case Kind.Fluorescent:
                    k = 0.97f + 0.03f * Mathf.Sin(t * 110f);   // mains shimmer
                    timer -= dt;
                    if (timer <= 0f)
                    {
                        timer = Random.Range(pace.x, pace.y);
                        burstLeft = Random.Range(burst.x, burst.y);
                    }
                    if (burstLeft > 0f)
                    {
                        burstLeft -= dt;
                        float n = Mathf.PerlinNoise(seed, t * 28f);
                        k *= n < 0.5f ? 1f - depth : Mathf.Lerp(1f - depth * 0.4f, 1f, n);
                    }
                    break;

                case Kind.Flame:
                    float speed = Mathf.Max(pace.x, 0.1f);
                    float waver = Mathf.PerlinNoise(seed, t * speed) * 0.7f + Mathf.PerlinNoise(seed + 9f, t * speed * 3.1f) * 0.3f;
                    k = 1f - depth * waver;
                    break;

                case Kind.Screen:
                    timer -= dt;
                    if (timer <= 0f)
                    {
                        timer = Random.Range(pace.x, pace.y);
                        target = 1f - depth * Random.value;
                    }
                    level = Mathf.Lerp(level, target, 1f - Mathf.Exp(-30f * dt));
                    k = level * (0.97f + 0.03f * Mathf.Sin(t * 190f));   // scan flicker
                    break;
            }

            source.intensity = baseIntensity * k;
            if (block != null && glow)
            {
                block.SetColor(EmissionId, baseEmission * k);
                glow.SetPropertyBlock(block);
            }
        }
    }
}
