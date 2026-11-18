using System;
using System.Collections;
using BodycamHorror.Player;
using UnityEngine;

namespace BodycamHorror.Environment
{
    /// <summary>
    /// The one bright bulb of a room. When the player steps past <see cref="triggerCenter"/> it stutters for a moment
    /// and bursts: a pop of glass, a flash, then dark, with a few shards falling. <see cref="Burst"/> tells whoever
    /// waits for the dark (the manthing).
    /// </summary>
    public class BurstingBulb : MonoBehaviour
    {
        public Light bulbLight;
        [Tooltip("The glowing glass: hidden when it bursts.")]
        public Renderer glass;
        public AudioClip burstClip;
        [Tooltip("The bulb's hum: stops when it bursts.")]
        public AudioSource hum;
        [Range(0f, 1f)] public float volume = 1f;

        [Header("Trigger: the player inside this box")]
        [Tooltip("Off: only a script bursts it (BurstNow), as the chain room event does.")]
        public bool autoTrigger = true;
        public Vector3 triggerCenter;
        public Vector3 triggerSize = new Vector3(4f, 3f, 1f);

        [Header("The moment")]
        [Tooltip("How long the bulb stutters before it goes (s).")]
        public float stutter = 0.45f;
        [Tooltip("Fear the burst puts on the player at once.")]
        public float fright = 0.35f;

        /// <summary>The bulb has gone: the room is dark.</summary>
        public event Action Burst;
        public bool HasBurst { get; private set; }

        BodycamPlayerController player;
        float baseIntensity;
        bool started;

        void Start()
        {
            player = FindAnyObjectByType<BodycamPlayerController>();
            if (bulbLight) baseIntensity = bulbLight.intensity;
        }

        void Update()
        {
            if (started || !player || !autoTrigger) return;
            Vector3 local = player.transform.position + Vector3.up * 0.9f - triggerCenter;
            Vector3 half = triggerSize * 0.5f;
            if (Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.y) <= half.y && Mathf.Abs(local.z) <= half.z)
            {
                started = true;
                StartCoroutine(Go());
            }
        }

        /// <summary>Bursts now, whoever is where (for tests and scripted moments).</summary>
        public void BurstNow()
        {
            if (started && HasBurst) return;
            started = true;
            StopAllCoroutines();
            StartCoroutine(Go(skipStutter: true));
        }

        IEnumerator Go(bool skipStutter = false)
        {
            // the filament stutters: a few dips and a surge
            float t = 0f;
            while (!skipStutter && t < stutter)
            {
                t += Time.deltaTime;
                if (bulbLight) bulbLight.intensity = baseIntensity * (Mathf.PerlinNoise(t * 30f, 0.3f) > 0.45f ? 1.25f : 0.15f);
                yield return null;
            }

            // pop: a white flash, then nothing
            if (burstClip) BodycamHorror.Audio.SpatialSound.PlayAt(burstClip, transform.position, volume, 1f, 100f);
            if (bulbLight) bulbLight.intensity = baseIntensity * 3f;
            yield return new WaitForSeconds(0.04f);
            if (bulbLight) bulbLight.enabled = false;
            if (glass) glass.enabled = false;
            if (hum) hum.Stop();
            Shards();
            HasBurst = true;

            var stress = player ? player.GetComponent<PlayerStress>() : null;
            if (stress) stress.Add(fright);
            var rig = player ? player.GetComponentInChildren<BodycamCameraRig>() : null;
            if (rig) rig.AddTrauma(0.25f);
            Burst?.Invoke();
        }

        /// <summary>A handful of glass splinters dropping from the socket.</summary>
        void Shards()
        {
            var go = new GameObject("GlassShards");
            go.transform.position = transform.position;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.duration = 0.2f; main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 2f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.6f, 2.4f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.008f, 0.025f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.gravityModifier = 1f;
            main.startColor = new Color(0.9f, 0.92f, 0.95f, 0.8f);
            main.stopAction = ParticleSystemStopAction.Destroy;
            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 28) });
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere; shape.radius = 0.04f;
            var collision = ps.collision;
            collision.enabled = true; collision.type = ParticleSystemCollisionType.World; collision.bounce = 0.25f; collision.dampen = 0.6f;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.material = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            r.material.SetColor("_BaseColor", new Color(0.85f, 0.88f, 0.9f, 0.9f));
            ps.Play();
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.6f, 0.2f, 0.35f);
            Gizmos.DrawCube(triggerCenter, triggerSize);
        }
    }
}
