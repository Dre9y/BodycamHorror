using BodycamHorror.Player;
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

        [Header("Monster")]
        public GameObject modelPrefab;
        public Material modelMaterial;
        public float modelScale = 0.7f;
        [Tooltip("A sprint clip played in place; the monster itself is moved along the run.")]
        public AnimationClip runClip;
        [Tooltip("Plays the run clip this much faster, so the legs keep up with the speed.")]
        public float animationSpeed = 1.25f;

        [Header("Sound")]
        public AudioClip scream;
        [Range(0f, 1f)] public float screamVolume = 1f;
        [Tooltip("Heavy, fast footfalls under the scream (any step recordings; played low and hard).")]
        public AudioClip[] steps;
        [Range(0f, 1f)] public float stepVolume = 0.9f;
        public float stepInterval = 0.24f;
        public float stepPitch = 0.72f;

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
        float timer, stepTimer, lightIntensity;

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
            Vector3 dir = to - from;
            dir.y = 0f;
            monster = new GameObject("Runner");
            monster.transform.SetPositionAndRotation(from, Quaternion.LookRotation(dir.sqrMagnitude > 0.001f ? dir.normalized : Vector3.forward));

            if (modelPrefab)
            {
                var model = Instantiate(modelPrefab, monster.transform, false);
                model.transform.localScale = Vector3.one * modelScale;
                foreach (var c in model.GetComponentsInChildren<Collider>(true)) Destroy(c);
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
            if (scream) voice.PlayOneShot(scream, screamVolume);
            feet = Source("Feet", 2f, 30f);
            stepTimer = 0f;

            var stressed = player ? player.GetComponentInChildren<PlayerStress>() : null;
            if (stressed) stressed.Add(stress);
        }

        AudioSource Source(string name, float near, float far)
        {
            var go = new GameObject(name);
            go.transform.SetParent(monster.transform, false);
            go.transform.localPosition = Vector3.up * 1.4f;
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
            Vector3 p = Vector3.MoveTowards(monster.transform.position, to, speed * dt);
            monster.transform.position = p;

            stepTimer -= dt;
            if (stepTimer <= 0f && steps != null && steps.Length > 0)
            {
                stepTimer = stepInterval * Random.Range(0.9f, 1.1f);
                var clip = steps[Random.Range(0, steps.Length)];
                feet.pitch = stepPitch * Random.Range(0.94f, 1.06f);
                if (clip) feet.PlayOneShot(clip, stepVolume);
            }

            if (flickerLight)
                flickerLight.intensity = lightIntensity * (Random.value < 0.35f ? Random.Range(0f, 0.25f) : Random.Range(0.6f, 1.1f));

            // gone: wait for the scream to fade out in the dark before removing it
            if ((p - to).sqrMagnitude < 0.0001f)
            {
                if (flickerLight) flickerLight.intensity = lightIntensity;
                if (graph.IsValid()) graph.Destroy();
                foreach (var r in monster.GetComponentsInChildren<Renderer>()) r.enabled = false;
                Destroy(monster, 3f);
                Finish();
            }
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
            Gizmos.DrawLine(from, to);
            Gizmos.DrawWireSphere(from, 0.3f);
        }
    }
}
