using System.Collections.Generic;
using BodycamHorror.AI;
using BodycamHorror.Enemies;
using BodycamHorror.Player;
using BodycamHorror.Weapons;
using UnityEngine;

namespace BodycamHorror.Environment
{
    /// <summary>
    /// A hinged door. This object sits on the hinge line and stays put; <see cref="leaf"/> is the part
    /// that turns (about its own Y axis, closed at zero). The player opens and closes it through
    /// PlayerInteractor; it swings away from whoever opens it. Enemies heading for a closed door throw
    /// it open themselves. Closing onto somebody standing in the way makes it swing back open.
    /// </summary>
    public class Door : MonoBehaviour
    {
        public static readonly List<Door> All = new List<Door>();
        static readonly Dictionary<Collider, Door> ByCollider = new Dictionary<Collider, Door>();
        static readonly Collider[] Buffer = new Collider[64];
        static AudioClip generatedOpen, generatedCreak, generatedShut, generatedLocked;

        [Header("Parts")]
        [Tooltip("The part that turns. Its origin is on the hinge line, the door extends along its +X.")]
        public Transform leaf;
        [Tooltip("Handles (one per side). The key prompt shows at the one nearer the player.")]
        public Transform[] handles;

        [Header("Swing")]
        public float openAngle = 95f;
        public float openTime = 0.7f;
        public float closeTime = 0.6f;
        [Tooltip("Opens away from whoever opens it. Off: always towards its +Z side's far side (positive angle).")]
        public bool swingBothWays = true;
        public bool startOpen;
        public bool locked;

        [Header("Enemies")]
        public bool enemiesCanOpen = true;
        [Tooltip("An enemy heading for the door opens it from this far (m).")]
        public float enemyReach = 1.5f;
        [Tooltip("Enemies do not open it gently (s).")]
        public float enemyOpenTime = 0.3f;

        [Header("Sound (empty = generated)")]
        [Tooltip("Handle, latch and the hinges as it swings open.")]
        public AudioClip openClip;
        [Tooltip("Hinges as it starts to close.")]
        public AudioClip closeClip;
        [Tooltip("The door meeting its frame.")]
        public AudioClip shutClip;
        public AudioClip lockedClip;
        [Range(0f, 1f)] public float volume = 0.8f;

        /// <summary>Open or on its way open.</summary>
        public bool IsOpen => Mathf.Abs(targetAngle) > 0.01f;
        public bool IsMoving { get; private set; }
        /// <summary>Middle of the closed door, world space.</summary>
        public Vector3 Centre => transform.TransformPoint(centreLocal);

        BoxCollider panel;
        Collider[] own;
        AudioSource source;
        Vector3 centreLocal = new Vector3(0.5f, 1f, 0f);
        float angle, fromAngle, targetAngle, lastOpenAngle, progress, duration = 1f, scanTimer;

        void Awake()
        {
            if (!leaf) leaf = transform.childCount > 0 ? transform.GetChild(0) : transform;
            own = leaf.GetComponentsInChildren<Collider>(true);
            panel = leaf.GetComponentInChildren<BoxCollider>(true);
            leaf.localRotation = Quaternion.identity;
            if (panel) centreLocal = transform.InverseTransformPoint(panel.transform.TransformPoint(panel.center));

            var audioObject = new GameObject("DoorAudio");
            audioObject.transform.SetParent(transform, false);
            audioObject.transform.localPosition = centreLocal;
            source = audioObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 1f;
            source.minDistance = 1.5f;
            source.maxDistance = 22f;
            source.rolloffMode = AudioRolloffMode.Linear;
            Audio.SoundOcclusion.Attach(source, transform);

            lastOpenAngle = openAngle;
            if (startOpen) { targetAngle = openAngle; SetAngle(openAngle); }
        }

        void OnEnable()
        {
            All.Add(this);
            if (own != null) foreach (var c in own) if (c) ByCollider[c] = this;
        }

        void OnDisable()
        {
            All.Remove(this);
            if (own != null) foreach (var c in own) if (c) ByCollider.Remove(c);
        }

        void Start() => PlayerInteractor.Ensure();

        /// <summary>The door this collider is part of, if any.</summary>
        public static Door Of(Collider c) => c && ByCollider.TryGetValue(c, out Door d) ? d : null;

        /// <summary>True for a door an enemy may plan its way through: it will open it when it gets there.</summary>
        public static bool EnemyPassable(Collider c)
        {
            Door d = Of(c);
            return d && d.enemiesCanOpen && !d.locked;
        }

        /// <summary>Where the key prompt goes: the handle on the viewer's side.</summary>
        public Vector3 PromptPoint(Vector3 viewer)
        {
            Vector3 best = Centre;
            float bestDistance = float.MaxValue;
            if (handles != null)
            {
                foreach (Transform h in handles)
                {
                    if (!h) continue;
                    float d = (h.position - viewer).sqrMagnitude;
                    if (d < bestDistance) { bestDistance = d; best = h.position; }
                }
            }
            return best;
        }

        /// <summary>Opens a closed door, closes an open one. <paramref name="from"/> is where the one doing it stands.</summary>
        public bool Toggle(Vector3 from)
        {
            if (!IsOpen) return Open(from);
            Close();
            return true;
        }

        public bool Open(Vector3 from, bool thrown = false)
        {
            if (locked)
            {
                Play(lockedClip ? lockedClip : generatedLocked ? generatedLocked : (generatedLocked = ProceduralAudio.DoubleClick(0.09f, 900f, 0.5f)), 0.7f, 1f);
                return false;
            }
            float side = transform.InverseTransformPoint(from).z >= 0f ? 1f : -1f;
            float target = (swingBothWays ? side : 1f) * openAngle;
            if (IsOpen && Mathf.Approximately(target, targetAngle)) return true;
            Begin(target, thrown ? enemyOpenTime : openTime);
            Play(openClip ? openClip : generatedOpen ? generatedOpen : (generatedOpen = ProceduralAudio.DoorOpen()),
                thrown ? 1f : volume, thrown ? 1.2f : Random.Range(0.95f, 1.05f));
            return true;
        }

        public void Close()
        {
            if (!IsOpen) return;
            Begin(0f, closeTime);
            Play(closeClip ? closeClip : generatedCreak ? generatedCreak : (generatedCreak = ProceduralAudio.DoorCreak()),
                volume * 0.7f, Random.Range(0.95f, 1.05f));
        }

        void Begin(float target, float time)
        {
            if (Mathf.Abs(target) > 0.01f) lastOpenAngle = target;
            fromAngle = angle;
            targetAngle = target;
            progress = 0f;
            // a door that is already most of the way there does not take the whole time again
            duration = Mathf.Max(time * Mathf.Clamp(Mathf.Abs(target - angle) / Mathf.Max(openAngle, 1f), 0.25f, 2f), 0.05f);
            IsMoving = true;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            bool closing = !IsOpen;

            if (IsMoving)
            {
                progress = Mathf.MoveTowards(progress, 1f, dt / duration);
                // opening eases in and out; closing gathers speed into the frame
                float eased = closing ? progress * progress : progress * progress * (3f - 2f * progress);
                float next = Mathf.LerpUnclamped(fromAngle, targetAngle, eased);
                if (closing && Blocked(next))
                {
                    Begin(lastOpenAngle, openTime);   // somebody in the way: back open
                    return;
                }
                SetAngle(next);
                if (progress >= 1f)
                {
                    IsMoving = false;
                    if (closing) Play(shutClip ? shutClip : generatedShut ? generatedShut : (generatedShut = ProceduralAudio.DoorShut()), volume, Random.Range(0.95f, 1.05f));
                }
            }

            if (closing && enemiesCanOpen && !locked)
            {
                scanTimer -= dt;
                if (scanTimer <= 0f)
                {
                    scanTimer = 0.12f;
                    LetEnemiesThrough();
                }
            }
        }

        void SetAngle(float a)
        {
            angle = a;
            leaf.localRotation = Quaternion.Euler(0f, a, 0f);
        }

        /// <summary>Would the leaf at this angle be inside the player or a body?</summary>
        bool Blocked(float nextAngle)
        {
            if (!panel) return false;
            Quaternion keep = leaf.localRotation;
            leaf.localRotation = Quaternion.Euler(0f, nextAngle, 0f);
            Transform t = panel.transform;
            Vector3 half = Vector3.Scale(panel.size, t.lossyScale) * 0.5f;
            half = new Vector3(Mathf.Abs(half.x), Mathf.Abs(half.y), Mathf.Abs(half.z)) * 0.96f;
            int n = Physics.OverlapBoxNonAlloc(t.TransformPoint(panel.center), half, Buffer, t.rotation, ~0, QueryTriggerInteraction.Ignore);
            leaf.localRotation = keep;
            for (int i = 0; i < n; i++)
            {
                Collider c = Buffer[i];
                if (Of(c) == this) continue;
                if (c is CharacterController) return true;
                Rigidbody body = c.attachedRigidbody;
                if (body && !body.isKinematic) return true;
            }
            return false;
        }

        /// <summary>Opens for an enemy that is close and coming this way.</summary>
        void LetEnemiesThrough()
        {
            Vector3 centre = Centre;
            int n = Physics.OverlapSphereNonAlloc(centre, enemyReach, Buffer, ~0, QueryTriggerInteraction.Collide);
            for (int i = 0; i < n; i++)
            {
                Collider c = Buffer[i];
                if (Of(c)) continue;
                Vector3 position, velocity, facing;
                bool hunting;
                var infected = c.GetComponentInParent<Infected>();
                if (infected)
                {
                    if (infected.IsDead) continue;
                    position = infected.transform.position;
                    facing = infected.transform.forward;
                    velocity = infected.TryGetComponent(out Rigidbody body) && !body.isKinematic ? body.linearVelocity : Vector3.zero;
                    hunting = infected.CurrentState == Infected.State.Chase;
                }
                else
                {
                    var monster = c.GetComponentInParent<MonsterBrain>();
                    if (!monster || monster.IsDead) continue;
                    position = monster.transform.position;
                    facing = monster.transform.forward;
                    velocity = monster.Velocity;
                    hunting = false;
                }

                Vector3 to = centre - position;
                to.y = 0f;
                velocity.y = 0f;
                float distance = to.magnitude;
                bool coming = distance < 0.05f;
                if (!coming)
                {
                    to /= distance;
                    // walking at the door, or pressed up against it while after somebody
                    coming = velocity.magnitude > 0.3f
                        ? Vector3.Dot(velocity.normalized, to) > 0.35f
                        : hunting && Vector3.Dot(facing, to) > 0.5f;
                }
                if (!coming) continue;
                Open(position, true);
                return;
            }
        }

        void Play(AudioClip clip, float clipVolume, float pitch)
        {
            if (!clip || !source) return;
            source.pitch = pitch;
            source.PlayOneShot(clip, clipVolume);
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.5f, 0.2f, 0.5f);
            Gizmos.DrawWireSphere(Application.isPlaying ? Centre : transform.TransformPoint(centreLocal), enemyReach);
        }
    }
}
