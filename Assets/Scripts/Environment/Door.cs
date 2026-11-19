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
    /// A locked door with a <see cref="keyId"/> opens for a player carrying that key (KeyRing). A door with a
    /// <see cref="latch"/> is bolted on one side: it opens only from that side, and once opened it is a door like
    /// any other (enemies leave it alone until then).
    /// </summary>
    public class Door : MonoBehaviour
    {
        /// <summary>Which side a latched door can be opened from. Front is the door's +Z side.</summary>
        public enum Latch { None, FrontOnly, BackOnly }

        public static readonly List<Door> All = new List<Door>();
        static readonly Dictionary<Collider, Door> ByCollider = new Dictionary<Collider, Door>();
        static readonly Collider[] Buffer = new Collider[64];
        static AudioClip generatedOpen, generatedCreak, generatedShut, generatedLocked, generatedUnlock;

        [Header("Parts")]
        [Tooltip("The part that turns. Its origin is on the hinge line, the door extends along its +X.")]
        public Transform leaf;
        [Tooltip("Handles (one per side). The key prompt shows at the one nearer the player.")]
        public Transform[] handles;

        [Header("Swing")]
        public float openAngle = 95f;
        [Tooltip("Seconds to swing open. Keep it close to the length of the opening sound: the creak is cut (faded) when the door stops.")]
        public float openTime = 0.7f;
        public float closeTime = 0.6f;
        [Tooltip("Opens away from whoever opens it. Off: always towards its +Z side's far side (positive angle).")]
        public bool swingBothWays = true;
        [Tooltip("With Swing Both Ways off: open the other way (towards the +Z side instead of away from it).")]
        public bool reverseSwing;
        public bool startOpen;

        [Header("Lock")]
        public bool locked;
        [Tooltip("The key that unlocks it (a KeyPickup's Key Id). Empty: no key does.")]
        public string keyId;
        [Tooltip("The key stays in the lock: it is taken off the player's ring.")]
        public bool keyUsedUp = true;
        [Tooltip("Bolted on one side: opens only from there (front = +Z), then like any door.")]
        public Latch latch = Latch.None;
        [Tooltip("The key turning in the lock (empty = generated).")]
        public AudioClip unlockClip;
        /// <summary>Raised when a key unlocks it.</summary>
        public event System.Action<Door> Unlocked;

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
        [Range(0f, 1f)] public float volume = 0.4f;
        [Tooltip("When the door stops moving, whatever is left of its creak dies away over this long (s).")]
        public float creakFade = 0.2f;

        /// <summary>Open or on its way open.</summary>
        public bool IsOpen => Mathf.Abs(targetAngle) > 0.01f;
        public bool IsMoving { get; private set; }
        /// <summary>Latched and not yet opened from its open side.</summary>
        public bool IsLatched => latch != Latch.None && !latchReleased;
        /// <summary>Would not open for somebody standing here: locked, or latched on the other side.</summary>
        public bool IsBarred(Vector3 from) => locked || (IsLatched && !FromLatchSide(from));
        /// <summary>Middle of the closed door, world space.</summary>
        public Vector3 Centre => transform.TransformPoint(centreLocal);

        BoxCollider panel;
        Collider[] own;
        AudioSource source, swing;         // knocks (latch, the frame) and the creak of the swing itself
        Audio.SoundOcclusion swingOcclusion;
        float swingLevel, swingStart;
        bool swingFading;
        Vector3 centreLocal = new Vector3(0.5f, 1f, 0f);
        float angle, fromAngle, targetAngle, lastOpenAngle, progress, duration = 1f, scanTimer;
        bool latchReleased;

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

            // the creak has a source of its own, so it can be cut short when the door stops
            var swingObject = new GameObject("DoorSwingAudio");
            swingObject.transform.SetParent(transform, false);
            swingObject.transform.localPosition = centreLocal;
            swing = swingObject.AddComponent<AudioSource>();
            swing.playOnAwake = false;
            swing.spatialBlend = 1f;
            swing.minDistance = 1.5f;
            swing.maxDistance = 22f;
            swing.rolloffMode = AudioRolloffMode.Linear;
            swingOcclusion = Audio.SoundOcclusion.Attach(swing, transform);

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
            return d && d.enemiesCanOpen && !d.locked && !d.IsLatched;
        }

        bool FromLatchSide(Vector3 from)
        {
            bool front = transform.InverseTransformPoint(from).z >= 0f;
            return latch == Latch.FrontOnly ? front : latch == Latch.BackOnly ? !front : true;
        }

        /// <summary>A player tries a locked door: if their key ring holds its key it unlocks (and opens).
        /// False when it is not locked by a key, or they do not have it.</summary>
        public bool TryUnlock(GameObject player, Vector3 from)
        {
            if (!locked || string.IsNullOrEmpty(keyId)) return false;
            KeyRing ring = KeyRing.Of(player);
            if (!ring || !ring.Has(keyId)) return false;
            if (keyUsedUp) ring.UseUp(keyId);
            locked = false;
            Play(unlockClip ? unlockClip : generatedUnlock ? generatedUnlock : (generatedUnlock = ProceduralAudio.DoubleClick(0.14f, 700f, 0.6f)), 0.8f, 1f);
            Unlocked?.Invoke(this);
            Open(from);
            return true;
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
            if (IsBarred(from))
            {
                Play(lockedClip ? lockedClip : generatedLocked ? generatedLocked : (generatedLocked = ProceduralAudio.DoubleClick(0.09f, 900f, 0.5f)), 0.7f, 1f);
                return false;
            }
            latchReleased |= latch != Latch.None;   // the bolt is drawn: from now on it opens from either side
            float side = transform.InverseTransformPoint(from).z >= 0f ? 1f : -1f;
            float target = (swingBothWays ? side : reverseSwing ? -1f : 1f) * openAngle;
            if (IsOpen && Mathf.Approximately(target, targetAngle)) return true;
            Begin(target, thrown ? enemyOpenTime : openTime);
            PlaySwing(openClip ? openClip : generatedOpen ? generatedOpen : (generatedOpen = ProceduralAudio.DoorOpen()),
                thrown ? Mathf.Min(1f, volume * 2f) : volume, thrown ? 1.2f : Random.Range(0.95f, 1.05f));
            return true;
        }

        public void Close()
        {
            if (!IsOpen) return;
            Begin(0f, closeTime);
            PlaySwing(closeClip ? closeClip : generatedCreak ? generatedCreak : (generatedCreak = ProceduralAudio.DoorCreak()),
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
                    swingFading = true;   // the door has stopped: so does its creak
                    if (closing) Play(shutClip ? shutClip : generatedShut ? generatedShut : (generatedShut = ProceduralAudio.DoorShut()), volume, Random.Range(0.95f, 1.05f));
                }
            }

            if (swingFading)
            {
                swingLevel = Mathf.MoveTowards(swingLevel, 0f, dt * swingStart / Mathf.Max(creakFade, 0.01f));
                swingOcclusion.Volume = swingLevel;
                if (swingLevel <= 0f || !swing.isPlaying)
                {
                    swing.Stop();
                    swingFading = false;
                }
            }

            if (closing && enemiesCanOpen && !locked && !IsLatched)
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

        /// <summary>The creak of the door moving: one at a time, faded out when the door stops (see Update).</summary>
        void PlaySwing(AudioClip clip, float clipVolume, float pitch)
        {
            if (!clip || !swing) return;
            swing.Stop();
            swing.clip = clip;
            swing.pitch = pitch;
            swingLevel = swingStart = Mathf.Clamp01(clipVolume);
            swingOcclusion.Volume = swingLevel;   // the occlusion owns the source's volume
            swingFading = false;
            swing.Play();
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
