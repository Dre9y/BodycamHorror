using UnityEngine;
using UnityEngine.Rendering;

namespace BodycamHorror.Environment
{
    /// <summary>
    /// Dust hanging in the air. One box of slow-drifting specks that travels with the camera, so there
    /// is dust wherever the player goes without filling the whole level; the specks themselves stay
    /// put in the world. With the Dust Mote material they show only where a light reaches them.
    /// </summary>
    public class DustMotes : MonoBehaviour
    {
        [Tooltip("Material with the BodycamHorror/Dust Mote shader.")]
        public Material material;
        [Tooltip("Camera to stay around; empty = the main camera.")]
        public Transform follow;
        [Tooltip("Specks alive at once.")]
        public int count = 1400;
        [Tooltip("Box of air around the camera that holds them (m).")]
        public Vector3 volume = new Vector3(9f, 3f, 9f);
        [Tooltip("Speck size, smallest to largest (m).")]
        public Vector2 size = new Vector2(0.0025f, 0.007f);
        [Tooltip("Seconds a speck lasts before another takes its place somewhere else in the box.")]
        public Vector2 lifetime = new Vector2(7f, 14f);
        [Tooltip("How fast the air carries them about (m/s).")]
        public float drift = 0.035f;
        [Tooltip("How fast they settle (m/s).")]
        public float fall = 0.008f;

        ParticleSystem motes;
        ParticleSystem.Particle[] buffer;
        Vector3 wrappedAt;
        float wrapTimer;

        void Awake()
        {
            var go = new GameObject("Motes");
            go.transform.SetParent(transform, false);
            go.layer = gameObject.layer;
            motes = go.AddComponent<ParticleSystem>();
            motes.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = motes.main;
            main.loop = true;
            main.prewarm = true;
            main.duration = lifetime.y;
            main.startLifetime = new ParticleSystem.MinMaxCurve(lifetime.x, lifetime.y);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(size.x, size.y);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 1f, 1f, 0.1f), new Color(1f, 1f, 1f, 0.6f));
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = count;
            main.gravityModifier = 0f;

            var emission = motes.emission;
            emission.rateOverTime = count / ((lifetime.x + lifetime.y) * 0.5f);

            var shape = motes.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = volume;

            var velocity = motes.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            velocity.x = 0f;
            velocity.y = -fall;
            velocity.z = 0f;

            var noise = motes.noise;
            noise.enabled = true;
            noise.strength = drift;
            noise.frequency = 0.35f;
            noise.scrollSpeed = 0.08f;
            noise.damping = false;

            // each one eases in and out, so none pops into a beam
            var fade = motes.colorOverLifetime;
            fade.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(1f, 0.8f), new GradientAlphaKey(0f, 1f) });
            fade.color = gradient;

            var render = go.GetComponent<ParticleSystemRenderer>();
            render.sharedMaterial = material;
            render.renderMode = ParticleSystemRenderMode.Billboard;
            render.shadowCastingMode = ShadowCastingMode.Off;
            render.receiveShadows = false;
            render.minParticleSize = 0.0011f;   // about a pixel: smaller than that they would only flicker
            render.maxParticleSize = 0.012f;

            Follow();
            motes.Play();
        }

        void LateUpdate()
        {
            Follow();
            // copying every speck out and back is only worth it once the box has moved, or now and then for the drift
            wrapTimer -= Time.deltaTime;
            if (wrapTimer <= 0f || (motes.transform.position - wrappedAt).sqrMagnitude > 0.04f)
            {
                wrapTimer = 0.5f;
                wrappedAt = motes.transform.position;
                Wrap();
            }
        }

        void Follow()
        {
            if (!follow && Camera.main) follow = Camera.main.transform;
            if (follow) motes.transform.position = follow.position;
        }

        /// <summary>A speck the box has moved away from comes back in on the opposite side, so running
        /// or turning a corner never leaves the player in clean air waiting for new dust.</summary>
        void Wrap()
        {
            buffer ??= new ParticleSystem.Particle[count];
            int alive = motes.GetParticles(buffer);
            Vector3 centre = motes.transform.position, half = volume * 0.5f;
            bool moved = false;
            for (int i = 0; i < alive; i++)
            {
                Vector3 offset = buffer[i].position - centre;
                Vector3 wrapped = new Vector3(
                    Mathf.Repeat(offset.x + half.x, volume.x) - half.x,
                    Mathf.Repeat(offset.y + half.y, volume.y) - half.y,
                    Mathf.Repeat(offset.z + half.z, volume.z) - half.z);
                if (wrapped == offset) continue;
                buffer[i].position = centre + wrapped;
                moved = true;
            }
            if (moved) motes.SetParticles(buffer, alive);
        }
    }
}
