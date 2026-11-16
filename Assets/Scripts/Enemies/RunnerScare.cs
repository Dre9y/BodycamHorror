using BodycamHorror.Player;
using BodycamHorror.Weapons;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using Random = UnityEngine.Random;

namespace BodycamHorror.Enemies
{
    /// <summary>
    /// A staged scare: when the player walks into the trigger box, a monster bursts out of hiding and
    /// sprints across the way ahead, screaming, then is gone into the dark. Nothing to fight: it never
    /// looks at the player and is not there before or after. Put it anywhere; the trigger box is its own
    /// transform's position and <see cref="triggerSize"/>, the run goes from <see cref="from"/> to
    /// <see cref="to"/> (world points on the floor).
    /// Or, with a <see cref="path"/>, it crawls: along the floor, up a wall, over the ceiling. Each path
    /// point is where it touches a surface and each normal is that surface's up; the body is turned so its
    /// feet stay on the surface, and it runs through the points on a smooth curve.
    /// </summary>
    public class RunnerScare : MonoBehaviour
    {
        [Header("Trigger (box around this transform)")]
        public Vector3 triggerSize = new Vector3(2.4f, 3f, 0.6f);
        public bool once = true;
        [Tooltip("Seconds between the player stepping in and the monster setting off.")]
        public float delay = 0.05f;

        [Header("Run")]
        public Vector3 from;
        public Vector3 to;
        [Tooltip("m/s. A person sprints at about 8.")]
        public float speed = 9.5f;
        [Tooltip("Optional: a crawl through these points (world) instead of the straight run from-to.")]
        public Vector3[] path;
        [Tooltip("The surface's up at each path point (floor = up, wall = out of the wall, ceiling = down).")]
        public Vector3[] normals;
        [Tooltip("How fast the body turns onto a new surface (higher = snappier).")]
        public float alignSharpness = 12f;

        [Header("Monster")]
        public GameObject modelPrefab;
        [Tooltip("Optional: replaces every material of the model.")]
        public Material modelMaterial;
        public float modelScale = 0.7f;
        [Tooltip("Strip the prefab's own scripts, rigidbodies and colliders (an enemy prefab used as a puppet).")]
        public bool stripBehaviours = true;
        [Tooltip("A sprint clip played in place; the monster itself is moved along the run.")]
        public AnimationClip runClip;
        [Tooltip("Plays the run clip this much faster, so the legs keep up with the speed.")]
        public float animationSpeed = 1.25f;

        [Header("Sound")]
        public AudioClip scream;
        [Range(0f, 1f)] public float screamVolume = 1f;
        [Range(0.3f, 3f)] public float screamPitch = 1f;
        [Tooltip("Heavy, fast footfalls under the scream (any step recordings; played low and hard).")]
        public AudioClip[] steps;
        [Range(0f, 1f)] public float stepVolume = 0.9f;
        public float stepInterval = 0.24f;
        public float stepPitch = 0.72f;
        [Tooltip("No step recordings: use generated skittering (fingers and tendrils on tile).")]
        public bool proceduralSkitter;

        [Header("The moment")]
        [Tooltip("A lamp that stutters while it passes (optional).")]
        public Light flickerLight;
        [Tooltip("Stress given to the player (0-1).")]
        [Range(0f, 1f)] public float stress = 0.45f;

        enum Phase { Waiting, Delay, Running, Done }

        Phase phase;
        Transform player;
        GameObject monster;
        AudioSource voice, feet;
        PlayableGraph graph;
        float timer, stepTimer, lightIntensity, travelled, pathLength;
        float[] cumulative;
        AudioClip[] stepClips;

        bool Crawling => path != null && path.Length >= 2;

        void Start()
        {
            var controller = FindAnyObjectByType<BodycamPlayerController>();
            if (controller) player = controller.transform;
            if (flickerLight) lightIntensity = flickerLight.intensity;
        }

        void Update()
        {
            switch (phase)
            {
                case Phase.Waiting:
                    if (player && Inside(player.position + Vector3.up * 0.9f)) { phase = Phase.Delay; timer = delay; }
                    break;
                case Phase.Delay:
                    timer -= Time.deltaTime;
                    if (timer <= 0f) Begin();
                    break;
                case Phase.Running:
                    Run(Time.deltaTime);
                    break;
            }
        }

        bool Inside(Vector3 point)
        {
            Vector3 local = transform.InverseTransformPoint(point);
            Vector3 h = triggerSize * 0.5f;
            return Mathf.Abs(local.x) <= h.x && Mathf.Abs(local.y) <= h.y && Mathf.Abs(local.z) <= h.z;
        }

        void Begin()
        {
            phase = Phase.Running;
            monster = new GameObject("Runner");
            if (Crawling)
            {
                BuildPathTable();
                travelled = 0f;
                monster.transform.SetPositionAndRotation(path[0], PathRotation(0f, Quaternion.identity));
            }
            else
            {
                Vector3 dir = to - from;
                dir.y = 0f;
                monster.transform.SetPositionAndRotation(from, Quaternion.LookRotation(dir.sqrMagnitude > 0.001f ? dir.normalized : Vector3.forward));
            }

            if (modelPrefab)
            {
                // made under an inactive holder, so the prefab's own scripts never wake up before they go
                var holder = new GameObject("Holder");
                holder.SetActive(false);
                holder.transform.SetParent(monster.transform, false);
                var model = Instantiate(modelPrefab, holder.transform, false);
                if (stripBehaviours)
                {
                    foreach (var j in model.GetComponentsInChildren<Joint>(true)) DestroyImmediate(j);
                    foreach (var b in model.GetComponentsInChildren<MonoBehaviour>(true)) DestroyImmediate(b);
                    foreach (var b in model.GetComponentsInChildren<Rigidbody>(true)) DestroyImmediate(b);
                }
                foreach (var c in model.GetComponentsInChildren<Collider>(true)) DestroyImmediate(c);
                model.transform.SetParent(monster.transform, false);
                model.transform.localPosition = Vector3.zero;
                model.transform.localRotation = Quaternion.identity;
                model.transform.localScale = Vector3.one * modelScale;
                Destroy(holder);

                foreach (var r in model.GetComponentsInChildren<Renderer>(true))
                {
                    if (modelMaterial) r.sharedMaterial = modelMaterial;
                    if (r is SkinnedMeshRenderer smr) smr.updateWhenOffscreen = true;
                }
                var animator = model.GetComponentInChildren<Animator>();
                if (animator && runClip)
                {
                    animator.applyRootMotion = false;
                    animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                    var playable = AnimationPlayableUtilities.PlayClip(animator, runClip, out graph);
                    playable.SetSpeed(animationSpeed);
                    playable.SetTime(Random.Range(0f, runClip.length));
                }
            }

            voice = Source("Voice", 3f, 40f);
            voice.pitch = screamPitch;
            if (scream) voice.PlayOneShot(scream, screamVolume);
            feet = Source("Feet", 2f, 30f);
            stepTimer = 0f;
            stepClips = steps != null ? System.Array.FindAll(steps, c => c) : new AudioClip[0];
            if (stepClips.Length == 0 && proceduralSkitter)
            {
                stepClips = new AudioClip[6];
                for (int i = 0; i < stepClips.Length; i++) stepClips[i] = ProceduralAudio.Skitter(i);
            }

            var stressed = player ? player.GetComponentInChildren<PlayerStress>() : null;
            if (stressed) stressed.Add(stress);
        }

        AudioSource Source(string name, float near, float far)
        {
            var go = new GameObject(name);
            go.transform.SetParent(monster.transform, false);
            go.transform.localPosition = Vector3.up * (Crawling ? 0.25f : 1.4f);
            var s = go.AddComponent<AudioSource>();
            s.playOnAwake = false;
            s.spatialBlend = 1f;
            s.rolloffMode = AudioRolloffMode.Logarithmic;
            s.minDistance = near;
            s.maxDistance = far;
            s.dopplerLevel = 0.4f;   // a touch of pitch drop as it tears past
            s.priority = 0;
            return s;
        }

        void Run(float dt)
        {
            if (!monster) { Finish(); return; }
            bool arrived;
            if (Crawling)
            {
                travelled = Mathf.Min(travelled + speed * dt, pathLength);
                monster.transform.position = PathPoint(travelled);
                Quaternion want = PathRotation(travelled, monster.transform.rotation);
                monster.transform.rotation = Quaternion.Slerp(monster.transform.rotation, want, 1f - Mathf.Exp(-alignSharpness * dt));
                arrived = travelled >= pathLength - 0.0001f;
            }
            else
            {
                Vector3 p = Vector3.MoveTowards(monster.transform.position, to, speed * dt);
                monster.transform.position = p;
                arrived = (p - to).sqrMagnitude < 0.0001f;
            }

            stepTimer -= dt;
            if (stepTimer <= 0f && stepClips != null && stepClips.Length > 0)
            {
                stepTimer = stepInterval * Random.Range(0.9f, 1.1f);
                var clip = stepClips[Random.Range(0, stepClips.Length)];
                feet.pitch = stepPitch * Random.Range(0.94f, 1.06f);
                if (clip) feet.PlayOneShot(clip, stepVolume);
            }

            if (flickerLight)
                flickerLight.intensity = lightIntensity * (Random.value < 0.35f ? Random.Range(0f, 0.25f) : Random.Range(0.6f, 1.1f));

            // gone: wait for the scream to fade out in the dark before removing it
            if (arrived)
            {
                if (flickerLight) flickerLight.intensity = lightIntensity;
                if (graph.IsValid()) graph.Destroy();
                foreach (var r in monster.GetComponentsInChildren<Renderer>()) r.enabled = false;
                Destroy(monster, 3f);
                Finish();
            }
        }

        // ------------------------------------------------------------------ crawl path

        /// <summary>Length along the curve up to each path point (each span sampled finely).</summary>
        void BuildPathTable()
        {
            int n = path.Length;
            cumulative = new float[n];
            float length = 0f;
            for (int i = 0; i < n - 1; i++)
            {
                cumulative[i] = length;
                Vector3 prev = path[i];
                for (int k = 1; k <= 16; k++)
                {
                    Vector3 q = Curve(i, k / 16f);
                    length += Vector3.Distance(prev, q);
                    prev = q;
                }
            }
            cumulative[n - 1] = length;
            pathLength = length;
        }

        /// <summary>Catmull-Rom through the points, so the crawl has no kinks.</summary>
        Vector3 Curve(int i, float t)
        {
            int n = path.Length;
            Vector3 p0 = path[Mathf.Max(i - 1, 0)], p1 = path[i], p2 = path[Mathf.Min(i + 1, n - 1)], p3 = path[Mathf.Min(i + 2, n - 1)];
            float t2 = t * t, t3 = t2 * t;
            return 0.5f * (2f * p1 + (p2 - p0) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (3f * p1 - p0 - 3f * p2 + p3) * t3);
        }

        void Locate(float distance, out int i, out float t)
        {
            int n = path.Length;
            i = 0;
            while (i < n - 2 && cumulative[i + 1] < distance) i++;
            float span = cumulative[i + 1] - cumulative[i];
            t = span > 0.0001f ? Mathf.Clamp01((distance - cumulative[i]) / span) : 0f;
        }

        Vector3 PathPoint(float distance)
        {
            Locate(distance, out int i, out float t);
            return Curve(i, t);
        }

        Vector3 PathNormal(float distance)
        {
            Locate(distance, out int i, out float t);
            float s = t * t * (3f - 2f * t);
            Vector3 n = Vector3.Slerp(NormalAt(i), NormalAt(i + 1), s);
            return n.sqrMagnitude > 0.0001f ? n.normalized : Vector3.up;
        }

        Vector3 NormalAt(int i) =>
            normals != null && i < normals.Length && normals[i].sqrMagnitude > 0.0001f ? normals[i].normalized : Vector3.up;

        Quaternion PathRotation(float distance, Quaternion current)
        {
            Vector3 up = PathNormal(distance);
            float a = Mathf.Clamp(distance, 0f, Mathf.Max(pathLength - 0.05f, 0f));
            Vector3 forward = Vector3.ProjectOnPlane(PathPoint(Mathf.Min(a + 0.05f, pathLength)) - PathPoint(a), up);
            if (forward.sqrMagnitude < 0.000001f) return current;
            return Quaternion.LookRotation(forward.normalized, up);
        }

        void Finish()
        {
            phase = once ? Phase.Done : Phase.Waiting;
            if (!once) enabled = true;
        }

        void OnDestroy()
        {
            if (graph.IsValid()) graph.Destroy();
        }

        void OnDrawGizmos()
        {
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = new Color(1f, 0.3f, 0.2f, 0.35f);
            Gizmos.DrawCube(Vector3.zero, triggerSize);
            Gizmos.matrix = Matrix4x4.identity;
            Gizmos.color = new Color(1f, 0.2f, 0.1f, 0.9f);
            if (Crawling)
            {
                for (int i = 0; i < path.Length; i++)
                {
                    if (i < path.Length - 1) Gizmos.DrawLine(path[i], path[i + 1]);
                    Gizmos.DrawLine(path[i], path[i] + NormalAt(i) * 0.3f);
                }
                Gizmos.DrawWireSphere(path[0], 0.3f);
                return;
            }
            Gizmos.DrawLine(from, to);
            Gizmos.DrawWireSphere(from, 0.3f);
        }
    }
}
