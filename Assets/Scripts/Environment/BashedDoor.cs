using System;
using System.Collections;
using BodycamHorror.AI;
using BodycamHorror.Audio;
using BodycamHorror.Player;
using UnityEngine;

namespace BodycamHorror.Environment
{
    /// <summary>
    /// A shut door something beats on from the other side: the first blow shakes it in its frame with a boom, the second
    /// tears it off and throws it into the room with a crash. Started by a script (<see cref="Begin"/>). Once the way is
    /// open the NavMesh is baked again (RuntimeNavMesh), so what is behind it can walk through.
    /// </summary>
    public class BashedDoor : MonoBehaviour
    {
        [Tooltip("The leaf: a solid box with a collider. It flies off on the last blow.")]
        public Transform leaf;
        [Tooltip("Blows it takes; the last one tears it off.")]
        public int blows = 2;
        [Tooltip("Time between blows (s).")]
        public float gap = 1.4f;
        [Tooltip("Direction it is thrown, in the door's own space (+Z: out of the door's front).")]
        public Vector3 throwDirection = new Vector3(0f, 0.15f, 1f);
        public float throwSpeed = 8f;
        public float leafMass = 35f;

        [Header("Sound")]
        public AudioClip blowClip;
        [Range(0f, 1f)] public float blowVolume = 0.9f;
        public AudioClip burstClip;
        [Range(0f, 1f)] public float burstVolume = 1f;

        public event Action TornOff;
        public bool IsTorn { get; private set; }

        Vector3 restPos;
        Quaternion restRot;
        bool started;

        void Awake()
        {
            if (!leaf) leaf = transform.Find("Leaf");
            if (leaf) { restPos = leaf.localPosition; restRot = leaf.localRotation; }
        }

        public void Begin()
        {
            if (started || !leaf) return;
            started = true;
            StartCoroutine(Run());
        }

        IEnumerator Run()
        {
            for (int i = 1; i < blows; i++)
            {
                Blow(0.35f);
                yield return Shake(0.35f);
                yield return new WaitForSeconds(Mathf.Max(0f, gap - 0.35f));
            }
            TearOff();
        }

        void Blow(float trauma)
        {
            if (blowClip) SpatialSound.PlayAt(blowClip, leaf.position, blowVolume, 2f, 45f);
            Jolt(trauma);
        }

        /// <summary>The leaf jumps in its frame and settles back.</summary>
        IEnumerator Shake(float time)
        {
            Vector3 push = Vector3.forward * 0.05f;
            float t = 0f;
            while (t < time)
            {
                t += Time.deltaTime;
                float k = Mathf.Exp(-t * 9f) * Mathf.Cos(t * 55f);
                leaf.localPosition = restPos + push * k;
                leaf.localRotation = restRot * Quaternion.Euler(k * 2.5f, 0f, k * 1.2f);
                yield return null;
            }
            leaf.localPosition = restPos;
            leaf.localRotation = restRot;
        }

        void TearOff()
        {
            IsTorn = true;
            if (burstClip) SpatialSound.PlayAt(burstClip, leaf.position, burstVolume, 2.5f, 60f);
            if (blowClip) SpatialSound.PlayAt(blowClip, leaf.position, blowVolume, 2f, 45f);
            Jolt(0.6f);

            Vector3 dir = transform.TransformDirection(throwDirection.normalized);
            leaf.SetParent(null, true);
            var body = leaf.GetComponent<Rigidbody>();
            if (!body) body = leaf.gameObject.AddComponent<Rigidbody>();   // not ??: a missing component is Unity's fake null
            body.isKinematic = false;
            body.useGravity = true;
            body.mass = leafMass;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.linearVelocity = dir * throwSpeed;
            body.angularVelocity = new Vector3(UnityEngine.Random.Range(-2f, 2f), UnityEngine.Random.Range(-4f, 4f), UnityEngine.Random.Range(-6f, 6f));
            TornOff?.Invoke();
            StartCoroutine(Rebake());
        }

        IEnumerator Rebake()
        {
            yield return null;   // the leaf is out of the frame and a loose body now: the bake leaves it out
            var nav = FindAnyObjectByType<RuntimeNavMesh>();
            if (nav) { nav.Rebake(); SafeZone.Recarve(); }
        }

        void Jolt(float amount)
        {
            var player = FindAnyObjectByType<BodycamPlayerController>();
            if (!player) return;
            float near = 1f - Mathf.Clamp01(Vector3.Distance(player.transform.position, transform.position) / 20f);
            var rig = player.GetComponentInChildren<BodycamCameraRig>();
            if (rig) rig.AddTrauma(amount * (0.4f + 0.6f * near));
            var stress = player.GetComponent<PlayerStress>();
            if (stress) stress.Add(0.15f + 0.2f * near);
        }
    }
}
