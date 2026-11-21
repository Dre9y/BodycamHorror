using System.Collections;
using BodycamHorror.AI;
using BodycamHorror.Audio;
using BodycamHorror.Player;
using UnityEngine;

namespace BodycamHorror.Environment
{
    /// <summary>
    /// A shutter or a gate that rises out of the way when something (a valve, a power switch) calls <see cref="Open"/>.
    /// Grinds up over <see cref="time"/> seconds, then the NavMesh is baked again so the monster can use the way too.
    /// </summary>
    public class SlidingGate : MonoBehaviour
    {
        [Tooltip("What moves: the shutter itself. This object when empty.")]
        public Transform panel;
        [Tooltip("How far it moves, in this object's space (m).")]
        public Vector3 travel = new Vector3(0f, 2.3f, 0f);
        public float time = 2.5f;
        public AudioClip grindClip;
        [Range(0f, 1f)] public float volume = 0.7f;
        [Tooltip("Shown to the player when it opens (empty: nothing).")]
        public string message;

        public bool IsOpen { get; private set; }

        public void Open()
        {
            if (IsOpen) return;
            IsOpen = true;
            if (!panel) panel = transform;
            StartCoroutine(Run());
        }

        IEnumerator Run()
        {
            if (grindClip) SpatialSound.PlayAt(grindClip, panel.position, volume, 2f, 40f);
            if (!string.IsNullOrEmpty(message))
            {
                var player = FindAnyObjectByType<BodycamPlayerController>();
                var ring = player ? KeyRing.Of(player.gameObject) : null;
                if (ring) ring.Say(message);
            }
            Vector3 from = panel.localPosition, to = from + travel;
            float t = 0f;
            while (t < 1f)
            {
                t = Mathf.Min(1f, t + Time.deltaTime / Mathf.Max(time, 0.05f));
                panel.localPosition = Vector3.Lerp(from, to, Mathf.SmoothStep(0f, 1f, t));
                yield return null;
            }
            var nav = FindAnyObjectByType<RuntimeNavMesh>();
            if (nav) { nav.Rebake(); SafeZone.Recarve(); }
        }
    }
}
