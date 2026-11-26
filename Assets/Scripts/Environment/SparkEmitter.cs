using BodycamHorror.Audio;
using UnityEngine;
using UnityEngine.Rendering;

namespace BodycamHorror.Environment
{
    /// <summary>
    /// A short circuit that arcs now and then: a stutter of flashes that light up what is round it, a spray of sparks
    /// falling and bouncing off the floor, a crackle. Quiet once <see cref="stopWhenActive"/> shows (the fuse is in), or
    /// when <see cref="Stop"/> is called. The light does not hold the light monster (<see cref="MonsterBlindLight"/>).
    /// </summary>
    public class SparkEmitter : MonoBehaviour
    {
        [Tooltip("The sparks' material (an additive / unlit particle material). Empty: URP's particle unlit.")]
        public Material sparkMaterial;
        [Tooltip("Seconds between one fit of arcing and the next (random in this range).")]
        public Vector2 every = new Vector2(1.2f, 4.5f);
        [Tooltip("Flashes in one fit.")]
        public Vector2Int flashes = new Vector2Int(2, 5);
        [Tooltip("Sparks thrown per flash.")]
        public Vector2Int sparks = new Vector2Int(6, 16);
        [Tooltip("The way the sparks are thrown (local space); they fan out round it and fall.")]
        public Vector3 throwDirection = new Vector3(0f, -0.3f, 1f);
        public float lightIntensity = 3.5f;
        public float lightRange = 3.2f;
        public Color lightColor = new Color(0.75f, 0.85f, 1f);
        [Tooltip("Where the flash's light sits, from the sparks (local): out in front, so it lights up the box's face.")]
        public Vector3 lightOffset = new Vector3(0f, 0.25f, 0.3f);
        [Range(0f, 1f)] public float volume = 0.45f;
        [Tooltip("Once this is active (the fuse put in the box) the arcing stops for good.")]
        public GameObject stopWhenActive;

        ParticleSystem fx;
        Light flash;
        AudioClip[] crackles;
        float nextFit;
        bool stopped;

        void Awake()
        {
            var lightGo = new GameObject("SparkLight");
            lightGo.transform.SetParent(transform, false);
            lightGo.transform.localPosition = lightOffset;
            flash = lightGo.AddComponent<Light>();
            flash.type = LightType.Point;
            flash.color = lightColor;
            flash.range = lightRange;
            flash.intensity = 0f;
            flash.shadows = LightShadows.None;
            flash.enabled = false;
            lightGo.AddComponent<MonsterBlindLight>();
            fx = CreateSystem();
            crackles = new[] { Weapons.ProceduralAudio.ElectricCrackle(11), Weapons.ProceduralAudio.ElectricCrackle(23), Weapons.ProceduralAudio.ElectricCrackle(47) };
            nextFit = Time.time + Random.Range(0.5f, every.y);
        }

        public void Stop()
        {
            stopped = true;
            StopAllCoroutines();
            if (flash) { flash.intensity = 0f; flash.enabled = false; }
        }

        void Update()
        {
            if (stopped) return;
            if (stopWhenActive && stopWhenActive.activeInHierarchy) { Stop(); return; }
            if (Time.time < nextFit) return;
            nextFit = Time.time + Random.Range(every.x, every.y);
            StartCoroutine(Fit());
        }

        System.Collections.IEnumerator Fit()
        {
            int n = Random.Range(flashes.x, flashes.y + 1);
            SpatialSound.PlayAt(crackles[Random.Range(0, crackles.Length)], transform.position, volume * Random.Range(0.7f, 1f), 1.2f, 14f);
            for (int i = 0; i < n; i++)
            {
                Burst();
                flash.enabled = true;
                flash.intensity = lightIntensity * Random.Range(0.5f, 1f);
                yield return new WaitForSeconds(Random.Range(0.02f, 0.06f));
                flash.intensity = lightIntensity * Random.Range(0.05f, 0.2f);
                yield return new WaitForSeconds(Random.Range(0.03f, 0.1f));
            }
            for (float t = 0f; t < 0.12f; t += Time.deltaTime)
            {
                flash.intensity = Mathf.Lerp(flash.intensity, 0f, t / 0.12f);
                yield return null;
            }
            flash.intensity = 0f;
            flash.enabled = false;
        }

        void Burst()
        {
            Vector3 dir = transform.TransformDirection(throwDirection.normalized);
            var p = new ParticleSystem.EmitParams();
            int count = Random.Range(sparks.x, sparks.y + 1);
            for (int i = 0; i < count; i++)
            {
                p.position = transform.position + Random.insideUnitSphere * 0.015f;
                p.velocity = (dir + Random.insideUnitSphere * 0.7f).normalized * Random.Range(0.8f, 3.2f);
                p.startSize = Random.Range(0.014f, 0.028f);
                p.startLifetime = Random.Range(0.25f, 0.8f);
                p.startColor = Color.Lerp(new Color(3f, 2.4f, 1.4f, 1f), new Color(2.2f, 2.6f, 3.2f, 1f), Random.value * 0.6f);
                fx.Emit(p, 1);
            }
        }

        ParticleSystem CreateSystem()
        {
            var go = new GameObject("Sparks");
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 200;
            main.startSpeed = 0f;
            main.gravityModifier = 1f;
            var emission = ps.emission;
            emission.rateOverTime = 0f;
            var shape = ps.shape;
            shape.enabled = false;
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(1f, 0.45f, 0.15f), 1f) },
                         new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.6f), new GradientAlphaKey(0f, 1f) });
            col.color = grad;
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.3f));
            // they bounce off the floor and whatever is under them
            var collision = ps.collision;
            collision.enabled = true;
            collision.type = ParticleSystemCollisionType.World;
            collision.mode = ParticleSystemCollisionMode.Collision3D;
            collision.dampen = 0.5f;
            collision.bounce = 0.35f;
            collision.lifetimeLoss = 0.25f;
            collision.quality = ParticleSystemCollisionQuality.Medium;
            collision.collidesWith = ~(1 << 2);
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            Material mat = sparkMaterial;
            if (!mat)
            {
                var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
                if (shader) mat = new Material(shader);
            }
            renderer.sharedMaterial = mat;
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.velocityScale = 0.06f;
            renderer.lengthScale = 1f;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            ps.Play();
            return ps;
        }
    }
}
