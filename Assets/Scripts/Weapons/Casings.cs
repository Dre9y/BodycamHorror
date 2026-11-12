using System.Collections.Generic;
using BodycamHorror.Player;
using UnityEngine;
using UnityEngine.Rendering;

namespace BodycamHorror.Weapons
{
    /// <summary>
    /// Spent casings: thrown out of the ejection port, they tumble, bounce and roll as real bodies and
    /// sound off whatever they land on. Brass plays recorded hits picked by how hard the knock was
    /// (Resources/Audio/Casings/Brass: casing_hard_*, _medium_*, _soft_*, _verysoft_*); a shotgun shell
    /// plays its recorded drop, bounces and all, once on landing (Resources/Audio/Casings/Shell).
    /// Missing recordings fall back to generated sounds. Built from primitives, so nothing has to be
    /// set up. The oldest go once there are too many, and every one goes after a while.
    /// </summary>
    public static class Casings
    {
        public enum Kind { Pistol, Shotgun }

        const int MaxAlive = 40;
        const float Lifetime = 10f;   // seconds a casing stays on the floor
        const int ClipVariants = 4;

        static readonly Queue<GameObject> alive = new Queue<GameObject>();
        static Material brass, hull;
        static AudioClip[] brassHits, shellHits;
        static AudioClip[][] brassTiers;
        static AudioClip shellDrop;
        static float shellDropStart;
        static bool recordingsLoaded;
        static readonly string[] TierNames = { "hard", "medium", "soft", "verysoft" };
        static PhysicsMaterial bounce;

        /// <summary>Throws one casing out of <paramref name="port"/> (world). <paramref name="gun"/>: the gun's
        /// rotation (barrel along its forward); <paramref name="throwVelocity"/>: right / up / back (m/s) in its
        /// space; <paramref name="carried"/>: the shooter's own velocity, which the casing keeps.</summary>
        public static void Eject(Kind kind, Vector3 port, Quaternion gun, Vector3 throwVelocity, Vector3 carried, Transform ignore)
        {
            while (alive.Count >= MaxAlive)
            {
                var oldest = alive.Dequeue();
                if (oldest) Object.Destroy(oldest);
            }

            bool shell = kind == Kind.Shotgun;
            var go = Build(shell);
            // it lies in the chamber along the barrel
            go.transform.SetPositionAndRotation(port, gun * Quaternion.Euler(90f, 0f, 0f));

            var body = go.AddComponent<Rigidbody>();
            body.mass = shell ? 0.012f : 0.005f;
            body.linearDamping = 0.05f;
            body.angularDamping = 0.4f;
            body.maxAngularVelocity = 60f;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;   // small and fast: never through the floor
            Vector3 jitter = new Vector3(Random.Range(-0.25f, 0.25f), Random.Range(-0.25f, 0.3f), Random.Range(-0.2f, 0.2f));
            body.linearVelocity = carried + gun * Vector3.Scale(throwVelocity, Vector3.one + jitter);
            // tumbling end over end, mostly about the gun's up axis
            body.angularVelocity = gun * new Vector3(Random.Range(-6f, 6f), Random.Range(18f, 30f) * (Random.value < 0.85f ? 1f : -1f), Random.Range(-8f, 8f));

            var col = go.GetComponent<Collider>();
            // the shooter (body, arms, hitboxes): the casing flies out past them, it does not land on them
            if (ignore && col)
                foreach (Collider own in ignore.GetComponentsInChildren<Collider>(true))
                    Physics.IgnoreCollision(col, own);

            var casing = go.AddComponent<Casing>();
            casing.shooter = ignore;
            LoadRecordings();
            if (shell && shellDrop) { casing.drop = shellDrop; casing.dropStart = shellDropStart; }
            else if (!shell && brassTiers != null) casing.tiers = brassTiers;
            else casing.hits = shell ? ShellHits() : BrassHits();
            casing.loudness = shell ? 0.81f : 1f;
            // brass is small and lands a metre or two away: heard as if close
            go.GetComponent<AudioSource>().minDistance = shell ? 0.35f : 1f;
            Object.Destroy(go, Lifetime);
            alive.Enqueue(go);
        }

        static GameObject Build(bool shell)
        {
            // Cylinder primitive: 1 across, 2 tall; its capsule-ish collider rolls well enough once swapped
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = shell ? "Casing_Shell" : "Casing_9mm";
            go.layer = 2;   // Ignore Raycast: shots, pickups and the acoustics' rays go straight past it
            Object.Destroy(go.GetComponent<Collider>());
            float diameter = shell ? 0.0205f : 0.0099f, length = shell ? 0.066f : 0.019f;
            go.transform.localScale = new Vector3(diameter, length * 0.5f, diameter);
            var capsule = go.AddComponent<CapsuleCollider>();
            capsule.direction = 1;
            capsule.radius = 0.5f;
            capsule.height = 2f;
            capsule.material = Bounce();

            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = shell ? Hull() : Brass();
            r.shadowCastingMode = ShadowCastingMode.On;
            r.renderingLayerMask = ViewModelLighting.WorldLayer;
            if (shell)
            {
                // the brass head of the shell
                var head = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                Object.Destroy(head.GetComponent<Collider>());
                head.name = "Head";
                head.layer = 2;
                head.transform.SetParent(go.transform, false);
                head.transform.localPosition = new Vector3(0f, -0.82f, 0f);
                head.transform.localScale = new Vector3(1.04f, 0.2f, 1.04f);
                var hr = head.GetComponent<Renderer>();
                hr.sharedMaterial = Brass();
                hr.renderingLayerMask = ViewModelLighting.WorldLayer;
            }
            return go;
        }

        static Material Lit(string name, Color color, float metallic, float smoothness)
        {
            var baseMat = GraphicsSettings.currentRenderPipeline ? GraphicsSettings.currentRenderPipeline.defaultMaterial : null;
            var m = baseMat ? new Material(baseMat) : new Material(Shader.Find("Universal Render Pipeline/Lit"));
            m.name = name;
            m.color = color;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metallic);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
            return m;
        }

        static Material Brass() => brass ? brass : brass = Lit("CasingBrass", new Color(0.78f, 0.58f, 0.27f), 1f, 0.72f);
        static Material Hull() => hull ? hull : hull = Lit("ShellHull", new Color(0.5f, 0.07f, 0.05f), 0f, 0.45f);

        static PhysicsMaterial Bounce()
        {
            if (bounce) return bounce;
            return bounce = new PhysicsMaterial("Casing")
            {
                bounciness = 0.35f, dynamicFriction = 0.35f, staticFriction = 0.5f,
                bounceCombine = PhysicsMaterialCombine.Maximum, frictionCombine = PhysicsMaterialCombine.Average,
            };
        }

        static void LoadRecordings()
        {
            if (recordingsLoaded) return;
            recordingsLoaded = true;
            var all = Resources.LoadAll<AudioClip>("Audio/Casings/Brass");
            var tiers = new AudioClip[TierNames.Length][];
            bool any = false;
            for (int t = 0; t < TierNames.Length; t++)
            {
                var list = new List<AudioClip>();
                foreach (var clip in all) if (clip.name.StartsWith("casing_" + TierNames[t] + "_")) list.Add(clip);
                tiers[t] = list.ToArray();
                any |= list.Count > 0;
            }
            if (any)
            {
                // a tier with nothing in it borrows from the next one that has something
                for (int t = 0; t < tiers.Length; t++)
                    if (tiers[t].Length == 0)
                        for (int k = 1; k < tiers.Length; k++)
                        {
                            var near = tiers[(t + k) % tiers.Length];
                            if (near.Length > 0) { tiers[t] = near; break; }
                        }
                brassTiers = tiers;
            }

            var drops = Resources.LoadAll<AudioClip>("Audio/Casings/Shell");
            if (drops.Length > 0)
            {
                shellDrop = drops[0];
                shellDropStart = FirstHit(shellDrop);
            }
        }

        /// <summary>Where the first knock in a recording is (s), less a hair, so a drop starts as it lands.</summary>
        static float FirstHit(AudioClip clip)
        {
            var data = new float[clip.samples * clip.channels];
            if (!clip.GetData(data, 0)) return 0f;
            float peak = 0f;
            foreach (float s in data) peak = Mathf.Max(peak, Mathf.Abs(s));
            for (int i = 0; i < data.Length; i++)
                if (Mathf.Abs(data[i]) > peak * 0.15f)
                    return Mathf.Max(0f, (i / clip.channels) / (float)clip.frequency - 0.01f);
            return 0f;
        }

        // generated clips are lost on a Play Mode script reload, so each is checked, not just the array
        static AudioClip[] BrassHits() => Clips(ref brassHits, false);
        static AudioClip[] ShellHits() => Clips(ref shellHits, true);

        static AudioClip[] Clips(ref AudioClip[] clips, bool shell)
        {
            if (clips == null) clips = new AudioClip[ClipVariants];
            for (int i = 0; i < clips.Length; i++) if (!clips[i]) clips[i] = ProceduralAudio.CasingHit(shell, i + 1);
            return clips;
        }
    }

    /// <summary>One spent casing: sounds on every real knock it takes, softer as it loses speed.</summary>
    public class Casing : MonoBehaviour
    {
        /// <summary>Recorded hits, hardest first; the knock's speed picks the tier.</summary>
        [System.NonSerialized] public AudioClip[][] tiers;
        /// <summary>A whole recorded drop (landing and its own bounces), played once from <see cref="dropStart"/>.</summary>
        [System.NonSerialized] public AudioClip drop;
        [System.NonSerialized] public float dropStart;
        /// <summary>Generated fallback.</summary>
        [System.NonSerialized] public AudioClip[] hits;
        /// <summary>Who fired it. Brushing past them makes no sound: the character controller cannot always
        /// be told to ignore it, and a landing sound must not go off at chest height.</summary>
        [System.NonSerialized] public Transform shooter;
        [System.NonSerialized] public float loudness = 0.75f;

        const float MinImpact = 0.25f;   // m/s: slower contacts are rolling, not knocks
        const int MaxSounds = 7;

        AudioSource source;
        float lastSound = -1f;
        int sounds;

        void Awake()
        {
            source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 1f;
            source.rolloffMode = AudioRolloffMode.Logarithmic;
            source.minDistance = 0.35f;
            source.maxDistance = 25f;
            source.dopplerLevel = 0f;
        }

        // knock speeds (m/s) from which the hard / medium / soft tiers are used; below them: very soft
        static readonly float[] TierFrom = { 2.6f, 1.5f, 0.75f };

        void OnCollisionEnter(Collision collision)
        {
            if (sounds >= MaxSounds) return;
            if (shooter && collision.collider && collision.collider.transform.IsChildOf(shooter)) return;
            float speed = collision.relativeVelocity.magnitude;
            if (speed < MinImpact || Time.time - lastSound < 0.035f) return;

            if (drop)
            {
                // the recording carries its own bounces: once, on landing
                sounds = MaxSounds;
                source.clip = drop;
                source.time = Mathf.Min(dropStart, drop.length - 0.05f);
                source.pitch = Random.Range(0.95f, 1.05f);
                source.volume = loudness * Mathf.Lerp(0.6f, 1f, Mathf.Clamp01(speed / 3f));
                source.Play();
                return;
            }

            AudioClip clip = null;
            float volume;
            if (tiers != null)
            {
                int tier = 0;
                while (tier < TierFrom.Length && speed < TierFrom[tier]) tier++;
                var set = tiers[tier];
                if (set != null && set.Length > 0) clip = set[Random.Range(0, set.Length)];
                // the tier already sounds as hard as the knock was; within it, a little louder the faster
                float lo = tier < TierFrom.Length ? TierFrom[tier] : MinImpact;
                float hi = tier > 0 ? TierFrom[tier - 1] : 5f;
                volume = loudness * Mathf.Lerp(0.75f, 1f, Mathf.InverseLerp(lo, hi, speed));
            }
            else
            {
                if (hits != null && hits.Length > 0) clip = hits[Random.Range(0, hits.Length)];
                volume = loudness * Mathf.Clamp01(speed / 3f);
            }
            if (!clip) return;
            lastSound = Time.time;
            sounds++;
            source.pitch = Random.Range(0.94f, 1.06f);
            source.PlayOneShot(clip, volume);
        }
    }
}
