using BodycamHorror.Combat;
using BodycamHorror.Player;
using BodycamHorror.Weapons;
using UnityEngine;

namespace BodycamHorror.Environment
{
    /// <summary>
    /// A jet of scalding steam from a burst pipe: it blows along this object's forward from where the
    /// object stands. Walking into it kills within moments (PlayerHealth; one is put on the player if
    /// the scene's player has none). It is shut off with a <see cref="Valve"/>: the jet weakens as the
    /// wheel is turned and dies away once the valve is shut. The steam itself is built here in code.
    /// </summary>
    public class SteamLeak : MonoBehaviour
    {
        [Tooltip("Shutting this valve stops the steam. None: it blows for good (or until TurnOff is called).")]
        public Valve valve;
        [Tooltip("Steam puff material (BodycamHorror/Steam).")]
        public Material material;

        [Header("Jet")]
        [Tooltip("How far the scalding part of the jet reaches at full pressure (m).")]
        public float length = 3f;
        [Tooltip("Radius of the jet at the pipe and at its far end (m).")]
        public float startRadius = 0.1f;
        public float endRadius = 0.55f;
        [Tooltip("Speed the steam leaves the pipe at (m/s).")]
        public float speed = 7f;
        [Tooltip("Puffs per second at full pressure.")]
        public float rate = 80f;

        [Header("Danger")]
        [Tooltip("Damage per second to a player standing in the jet. Full health is 100.")]
        public float damagePerSecond = 450f;
        [Tooltip("The jet is dealt in bites this far apart (s): each one knocks the camera.")]
        public float damageInterval = 0.1f;
        [Tooltip("Below this pressure (0-1) the last wisps no longer burn.")]
        [Range(0f, 1f)] public float harmlessBelow = 0.25f;

        [Header("Shutting off")]
        [Tooltip("Pressure left (0-1) when the valve is almost shut: turning it visibly weakens the jet.")]
        [Range(0f, 1f)] public float pressureNearlyShut = 0.5f;
        [Tooltip("Seconds the jet takes to die away once the valve is shut.")]
        public float dieAwayTime = 1.4f;

        [Header("Sound")]
        [Tooltip("Loops while the steam blows. Generated noise when empty.")]
        public AudioClip hissClip;
        [Range(0f, 1f)] public float volume = 0.45f;

        /// <summary>The supply is open (the jet may still be dying away after it is shut).</summary>
        public bool IsOn { get; private set; } = true;
        /// <summary>0 = nothing, 1 = full blast.</summary>
        public float Pressure { get; private set; } = 1f;
        /// <summary>The jet burns right now.</summary>
        public bool IsDeadly => Pressure > harmlessBelow;

        ParticleSystem steam;
        AudioSource source;
        Audio.SoundOcclusion occlusion;
        BodycamPlayerController player;
        PlayerHealth health;
        float damageTimer;

        void Awake()
        {
            steam = GetComponentInChildren<ParticleSystem>();
            if (!steam) steam = BuildSteam();

            source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = true;
            source.spatialBlend = 1f;
            source.minDistance = 1.5f;
            source.maxDistance = 22f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.volume = 0f;
            source.clip = hissClip ? hissClip : ProceduralAudio.Whoosh();
            occlusion = Audio.SoundOcclusion.Attach(source, transform);
            occlusion.Volume = 0f;
        }

        void Start()
        {
            player = FindAnyObjectByType<BodycamPlayerController>();
            if (player)
            {
                health = player.GetComponent<PlayerHealth>();
                if (!health) health = player.gameObject.AddComponent<PlayerHealth>();   // something has to die
            }
            source.Play();
        }

        /// <summary>Cuts the supply: the jet dies away. Called by itself when the valve is shut.</summary>
        public void TurnOff() => IsOn = false;
        public void TurnOn() => IsOn = true;

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            if (valve && valve.IsClosed) IsOn = false;
            float target = !IsOn ? 0f : valve ? Mathf.Lerp(1f, pressureNearlyShut, valve.Progress) : 1f;
            Pressure = Mathf.MoveTowards(Pressure, target, dt / Mathf.Max(dieAwayTime, 0.05f));

            // --- the look and the sound follow the pressure
            var emission = steam.emission;
            emission.rateOverTime = rate * Pressure;
            var main = steam.main;
            main.startSpeed = speed * Mathf.Lerp(0.35f, 1f, Pressure);

            occlusion.Volume = volume * Mathf.Sqrt(Pressure);
            source.pitch = Mathf.Lerp(0.8f, 1f, Pressure);
            if (Pressure <= 0.001f) { if (source.isPlaying) source.Pause(); }
            else if (!source.isPlaying) source.UnPause();

            // --- and so does the burn
            if (!IsDeadly || !player || !health || !InJet(out Vector3 at)) { damageTimer = 0f; return; }
            damageTimer -= dt;
            if (damageTimer > 0f) return;
            damageTimer = Mathf.Max(damageInterval, 0.02f);
            health.TakeDamage(new DamageInfo
            {
                amount = damagePerSecond * damageTimer, point = at, normal = -transform.forward,
                direction = transform.forward, collider = null, source = gameObject, melee = true
            });
        }

        float Reach => length * Mathf.Lerp(0.45f, 1f, Pressure);

        /// <summary>The player's body (a line from the feet to the head) passes through the cone of the jet.</summary>
        bool InJet(out Vector3 at)
        {
            Vector3 a = transform.position, b = a + transform.forward * Reach;
            Vector3 feet = player.transform.position + Vector3.up * 0.15f;
            Vector3 head = player.cameraPivot ? player.cameraPivot.position : feet + Vector3.up * 1.5f;
            Closest(a, b, feet, head, out float t, out Vector3 onJet, out Vector3 onBody);
            at = onBody;
            const float bodyRadius = 0.28f;
            return Vector3.Distance(onJet, onBody) < Mathf.Lerp(startRadius, endRadius, t) + bodyRadius;
        }

        /// <summary>Closest points of segments p1-q1 and p2-q2; s is how far along the first one (0-1).</summary>
        static void Closest(Vector3 p1, Vector3 q1, Vector3 p2, Vector3 q2, out float s, out Vector3 c1, out Vector3 c2)
        {
            Vector3 d1 = q1 - p1, d2 = q2 - p2, r = p1 - p2;
            float a = Vector3.Dot(d1, d1), e = Vector3.Dot(d2, d2), f = Vector3.Dot(d2, r);
            float t;
            if (a < 1e-6f && e < 1e-6f) { s = t = 0f; }
            else if (a < 1e-6f) { s = 0f; t = Mathf.Clamp01(f / e); }
            else
            {
                float c = Vector3.Dot(d1, r);
                if (e < 1e-6f) { t = 0f; s = Mathf.Clamp01(-c / a); }
                else
                {
                    float b = Vector3.Dot(d1, d2), denom = a * e - b * b;
                    s = denom > 1e-6f ? Mathf.Clamp01((b * f - c * e) / denom) : 0f;
                    t = (b * s + f) / e;
                    if (t < 0f) { t = 0f; s = Mathf.Clamp01(-c / a); }
                    else if (t > 1f) { t = 1f; s = Mathf.Clamp01((b - c) / a); }
                }
            }
            c1 = p1 + d1 * s;
            c2 = p2 + d2 * t;
        }

        ParticleSystem BuildSteam()
        {
            var go = new GameObject("Steam");
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.loop = true;
            main.playOnAwake = true;
            main.duration = 5f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.75f, 1.15f);
            main.startSpeed = speed;
            main.startSize = new ParticleSystem.MinMaxCurve(startRadius * 1.8f, startRadius * 2.8f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new Color(1f, 1f, 1f, 0.55f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = -0.05f;   // hot: it lifts as it slows
            main.maxParticles = 400;

            var emission = ps.emission;
            emission.rateOverTime = rate;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 5f;
            shape.radius = Mathf.Max(0.01f, startRadius * 0.3f);

            // fast out of the pipe, then it hangs: most of the length is covered in the first moments
            var limit = ps.limitVelocityOverLifetime;
            limit.enabled = true;
            limit.limit = speed * 0.22f;
            limit.dampen = 0.07f;

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            float grow = endRadius / Mathf.Max(startRadius, 0.01f);
            size.size = new ParticleSystem.MinMaxCurve(grow, new AnimationCurve(new Keyframe(0f, 1f / grow), new Keyframe(0.35f, 0.55f), new Keyframe(1f, 1f)));

            var color = ps.colorOverLifetime;
            color.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.08f), new GradientAlphaKey(0.55f, 0.45f), new GradientAlphaKey(0f, 1f) });
            color.color = gradient;

            var spin = ps.rotationOverLifetime;
            spin.enabled = true;
            spin.z = new ParticleSystem.MinMaxCurve(-0.7f, 0.7f);

            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.3f;
            noise.frequency = 0.9f;
            noise.scrollSpeed = 0.6f;

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            ps.Play();
            return ps;
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.4f, 0.2f);
            Vector3 a = transform.position, b = a + transform.forward * length;
            Gizmos.DrawLine(a, b);
            Gizmos.DrawWireSphere(a, startRadius);
            Gizmos.DrawWireSphere(b, endRadius);
        }
    }
}
