using UnityEngine;

namespace BodycamHorror.UI
{
    /// <summary>
    /// The figure at the far end of the main menu's corridor. It stands by the wall under the lamp and keeps to
    /// one loop: a while staring up into the bulb (its head turned up to it over the idle clip, with small
    /// twitches), then a few blows of its forehead against the wall (the head-bang clip, a dull knock at each),
    /// then back to the bulb. Its own little controller holds just the two states, "Idle" and "WallBang".
    /// </summary>
    public class MenuMonster : MonoBehaviour
    {
        public Animator animator;
        [Tooltip("What it stares at between bouts (the lamp's bulb).")]
        public Transform lookTarget;

        [Header("Loop")]
        [Tooltip("Seconds staring up at the lamp (random in this range).")]
        public Vector2 stareTime = new Vector2(4.5f, 8f);
        [Tooltip("Head-bang cycles per bout (random in this range, inclusive).")]
        public Vector2Int bangCycles = new Vector2Int(2, 4);
        public float crossFade = 0.45f;
        [Tooltip("Seconds for the head to go up to the lamp and come back down.")]
        public float lookBlend = 1.1f;
        [Tooltip("Share of the turn the chest takes, then the neck of what is left; the head does the rest.")]
        [Range(0f, 1f)] public float chestShare = 0.3f;
        [Range(0f, 1f)] public float neckShare = 0.45f;
        [Tooltip("Most any one bone turns from the clip's pose towards the lamp (degrees).")]
        public float maxLookAngle = 65f;

        [Header("Hands")]
        [Tooltip("Keep everything from the wrists down in the model's own pose (the slasher's bone blades).")]
        public bool rigidHands = true;

        [Header("Knocks")]
        [Range(0f, 1f)] public float knockVolume = 0.5f;

        static readonly int IdleState = Animator.StringToHash("Idle");
        static readonly int BangState = Animator.StringToHash("WallBang");
        // the forehead meets the wall at these points of the head-bang clip (frames 36 and 67 of 96)
        static readonly float[] Hits = { 36f / 96f, 67f / 96f };

        Transform chest, neck, head;
        Vector3 faceLocal;          // the face's direction in the head bone's space
        float look;                 // 0..1 how far the head is turned up to the lamp
        bool banging;
        float phaseEnd;             // end of the stare (s), or the bout's last cycle (normalized time)
        float lastBangT = -1f;
        Quaternion twitch = Quaternion.identity, twitchTarget = Quaternion.identity;
        float nextTwitch;
        Transform[] blades;
        Quaternion[] bladeRest;
        AudioSource knock;
        AudioClip[] knockClips;

        void Start()
        {
            if (!animator) animator = GetComponentInChildren<Animator>();
            if (animator && animator.isHuman)
            {
                chest = animator.GetBoneTransform(HumanBodyBones.UpperChest);
                if (!chest) chest = animator.GetBoneTransform(HumanBodyBones.Chest);
                neck = animator.GetBoneTransform(HumanBodyBones.Neck);
                head = animator.GetBoneTransform(HumanBodyBones.Head);
                if (rigidHands) RememberBlades();
            }
            // the model is placed facing forward: in this first pose the face points along the root's forward
            if (head) faceLocal = Quaternion.Inverse(head.rotation) * transform.forward;

            var clips = new System.Collections.Generic.List<AudioClip>();
            foreach (AudioClip c in Resources.LoadAll<AudioClip>("Audio/Monster"))
                if (c.name.StartsWith("head_bang")) clips.Add(c);
            knockClips = clips.ToArray();
            knock = gameObject.AddComponent<AudioSource>();
            knock.playOnAwake = false;
            knock.spatialBlend = 0.6f;   // far down the corridor, but the menu has one fixed listener
            knock.minDistance = 4f;
            knock.maxDistance = 40f;
            knock.rolloffMode = AudioRolloffMode.Linear;

            banging = false;
            phaseEnd = Time.time + Random.Range(1.5f, 3f);
            if (animator) animator.Play(IdleState, 0, Random.value);
        }

        void StartStare(float seconds)
        {
            banging = false;
            phaseEnd = Time.time + seconds;
            if (animator) animator.CrossFadeInFixedTime(IdleState, crossFade);
        }

        void StartBang()
        {
            banging = true;
            lastBangT = -1f;
            phaseEnd = Random.Range(bangCycles.x, bangCycles.y + 1);
            if (animator) animator.CrossFadeInFixedTime(BangState, crossFade, 0, 0f);
        }

        void Update()
        {
            if (!animator) return;
            if (!banging)
            {
                look = Mathf.MoveTowards(look, 1f, Time.deltaTime / lookBlend);
                if (Time.time >= phaseEnd)
                {
                    StartBang();
                }
                return;
            }

            // the head comes down off the lamp while it leans in to the wall
            look = Mathf.MoveTowards(look, 0f, Time.deltaTime / (lookBlend * 0.5f));
            AnimatorStateInfo s = animator.GetCurrentAnimatorStateInfo(0);
            if (s.shortNameHash != BangState)
            {
                // a cross-fade asked for in the same frame as another can be dropped: ask again until it starts
                if (!animator.IsInTransition(0) || animator.GetNextAnimatorStateInfo(0).shortNameHash != BangState)
                    animator.CrossFadeInFixedTime(BangState, crossFade, 0, 0f);
                return;
            }
            float t = s.normalizedTime;
            if (lastBangT >= 0f)
                for (int i = 0; i < Hits.Length; i++)
                {
                    // whole cycles count up; a hit is crossed when its point in the cycle is passed
                    float hit = Mathf.Floor(t) + Hits[i];
                    if (lastBangT < hit && t >= hit) Knock(i == 1 ? 1f : 0.85f);
                }
            lastBangT = t;
            // back to the lamp once the last cycle's second blow has landed and the head has come off the wall
            if (t >= phaseEnd - 1f + 0.9f) StartStare(Random.Range(stareTime.x, stareTime.y));
        }

        void Knock(float volume)
        {
            if (knockClips.Length == 0) return;
            knock.pitch = Random.Range(0.92f, 1.05f);
            knock.PlayOneShot(knockClips[Random.Range(0, knockClips.Length)], volume * knockVolume);
        }

        void LateUpdate()
        {
            if (rigidHands && blades != null)
                for (int i = 0; i < blades.Length; i++) blades[i].localRotation = bladeRest[i];
            if (!head || !lookTarget) return;

            // small jerks of the head while it stares, as if the light hurt
            if (Time.time >= nextTwitch)
            {
                nextTwitch = Time.time + Random.Range(0.25f, 1.6f);
                twitchTarget = Random.value < 0.6f
                    ? Quaternion.Euler(Random.Range(-6f, 6f), Random.Range(-9f, 9f), Random.Range(-14f, 14f))
                    : Quaternion.identity;
            }
            twitch = Quaternion.Slerp(twitch, twitchTarget, 1f - Mathf.Exp(-Time.deltaTime * 18f));

            float w = Mathf.SmoothStep(0f, 1f, look);
            if (w <= 0.001f) return;
            // the head goes back to the light: the chest arches a little, the neck and head do most of it
            Turn(chest, chestShare * w);
            Turn(neck, neckShare * w);
            Turn(head, w);
            head.rotation = Quaternion.Slerp(Quaternion.identity, twitch, w) * head.rotation;
        }

        /// <summary>Turns <paramref name="bone"/> so the face swings this share of the way to the target.</summary>
        void Turn(Transform bone, float share)
        {
            if (!bone) return;
            Vector3 face = head.rotation * faceLocal;
            Vector3 to = (lookTarget.position - head.position).normalized;
            Quaternion delta = Quaternion.FromToRotation(face, to);
            delta.ToAngleAxis(out float angle, out Vector3 axis);
            if (angle > 180f) angle -= 360f;
            angle = Mathf.Clamp(angle, -maxLookAngle, maxLookAngle) * share;
            bone.rotation = Quaternion.AngleAxis(angle, axis) * bone.rotation;
        }

        void RememberBlades()
        {
            var list = new System.Collections.Generic.List<Transform>();
            Transform l = animator.GetBoneTransform(HumanBodyBones.LeftHand), r = animator.GetBoneTransform(HumanBodyBones.RightHand);
            if (l) list.AddRange(l.GetComponentsInChildren<Transform>());
            if (r) list.AddRange(r.GetComponentsInChildren<Transform>());
            blades = list.ToArray();
            bladeRest = new Quaternion[blades.Length];
            for (int i = 0; i < blades.Length; i++) bladeRest[i] = blades[i].localRotation;
        }
    }
}
