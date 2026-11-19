using System.Collections;
using BodycamHorror.Audio;
using BodycamHorror.Enemies;
using BodycamHorror.Player;
using UnityEngine;

namespace BodycamHorror.Environment
{
    /// <summary>
    /// Something the player knocks over by walking past: stepping into the box (the yellow gizmo) sends
    /// <see cref="things"/> flying with a crash, and a moment later every infected within <see cref="wakeRadius"/>
    /// turns round and comes (<see cref="Infected.WakeByNoise"/>), even one that ignores gunshots.
    /// Drag the object about to try other spots; the things are its children and go with it.
    /// </summary>
    public class NoiseTrap : MonoBehaviour
    {
        [Header("Trigger (local to this object)")]
        public Vector3 triggerCentre = new Vector3(0f, 1f, 0f);
        [Tooltip("Make it as wide as the corridor so it cannot be stepped round.")]
        public Vector3 triggerSize = new Vector3(0.8f, 2f, 1.4f);

        [Header("What falls")]
        [Tooltip("Held still (kinematic) until the trap goes off.")]
        public Rigidbody[] things;
        [Tooltip("Kick given to each thing, local to this object (m/s).")]
        public Vector3 kick = new Vector3(1.6f, 0.5f, -0.3f);
        public float spin = 6f;

        [Header("Sound")]
        public AudioClip sound;
        [Range(0f, 1f)] public float volume = 1f;
        [Tooltip("From the kick to the crash (s): the things have to hit the floor.")]
        public float soundDelay = 0.2f;

        [Header("Who hears it")]
        public float wakeRadius = 25f;
        [Tooltip("From the crash to the infected reacting (s).")]
        public float wakeDelay = 0.5f;

        Transform player;
        bool sprung;

        public bool Sprung => sprung;

        void Start()
        {
            var p = FindAnyObjectByType<BodycamPlayerController>();
            if (p) player = p.transform;
            foreach (Rigidbody rb in things) if (rb) rb.isKinematic = true;
        }

        void Update()
        {
            if (sprung || !player) return;
            Vector3 local = transform.InverseTransformPoint(player.position + Vector3.up * 0.5f) - triggerCentre;
            Vector3 half = triggerSize * 0.5f;
            if (Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.y) <= half.y && Mathf.Abs(local.z) <= half.z) Spring();
        }

        [ContextMenu("Spring")]
        public void Spring()
        {
            if (sprung) return;
            sprung = true;
            foreach (Rigidbody rb in things)
            {
                if (!rb) continue;
                rb.isKinematic = false;
                rb.AddForce(transform.TransformDirection(kick) * Random.Range(0.8f, 1.2f), ForceMode.VelocityChange);
                rb.AddTorque(Random.onUnitSphere * spin, ForceMode.VelocityChange);
            }
            StartCoroutine(Aftermath());
        }

        IEnumerator Aftermath()
        {
            yield return new WaitForSeconds(soundDelay);
            SpatialSound.PlayAt(sound, transform.position + Vector3.up * 0.2f, volume, 2f, 40f);
            yield return new WaitForSeconds(wakeDelay);
            foreach (Infected inf in Infected.All.ToArray())
                if (inf && Vector3.Distance(inf.transform.position, transform.position) <= wakeRadius) inf.WakeByNoise();
        }

        void OnDrawGizmos()
        {
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = sprung ? new Color(0.5f, 0.5f, 0.5f, 0.5f) : new Color(1f, 0.85f, 0.2f, 0.8f);
            Gizmos.DrawWireCube(triggerCentre, triggerSize);
            Gizmos.matrix = Matrix4x4.identity;
            Gizmos.color = new Color(1f, 0.4f, 0.2f, 0.25f);
            Gizmos.DrawWireSphere(transform.position, wakeRadius);
        }
    }
}
