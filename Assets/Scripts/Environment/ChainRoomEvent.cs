using System.Collections;
using BodycamHorror.Audio;
using BodycamHorror.Enemies;
using BodycamHorror.Player;
using UnityEngine;

namespace BodycamHorror.Environment
{
    /// <summary>
    /// The chain room's moment. The player comes through the door and walks up to the writing on the far wall;
    /// behind them the door swings shut on its own with a long creak and slams, a dread swell rising all the while.
    /// Half a second after the slam the bulb bursts: the flashlight dies with it, the swell and every sound of the place
    /// are gone, and in the silence the manthing is standing by the shut door (it appears on the bulb's burst).
    /// The flashlight is the player's to switch back on.
    /// </summary>
    public class ChainRoomEvent : MonoBehaviour
    {
        public Door door;
        public BurstingBulb bulb;
        public Manthing manthing;

        [Header("Trigger: the player inside this box (by the writing)")]
        public Vector3 triggerCenter;
        public Vector3 triggerSize = new Vector3(6f, 3f, 2.5f);

        [Header("Sound")]
        [Tooltip("The door's creak as it swings shut.")]
        public AudioClip doorClose;
        [Range(0f, 1f)] public float doorCloseVolume = 1f;
        [Tooltip("Where in the creak the door lands (s): the swing takes this long, and the slam falls here.")]
        public float doorLands = 4.55f;
        public AudioClip slam;
        [Range(0f, 1f)] public float slamVolume = 1f;
        [Tooltip("The dread swell from the moment the door starts to move; cut dead when the light goes.")]
        public AudioClip riser;
        [Range(0f, 1f)] public float riserVolume = 0.7f;

        [Header("Timing")]
        [Tooltip("From the slam to the bulb bursting (s).")]
        public float blackoutDelay = 0.5f;

        BodycamPlayerController player;
        bool started;

        void Start()
        {
            player = FindAnyObjectByType<BodycamPlayerController>();
            if (bulb) bulb.autoTrigger = false;
        }

        void Update()
        {
            if (started || !player) return;
            Vector3 d = player.transform.position + Vector3.up * 0.9f - triggerCenter;
            Vector3 half = triggerSize * 0.5f;
            if (Mathf.Abs(d.x) <= half.x && Mathf.Abs(d.y) <= half.y && Mathf.Abs(d.z) <= half.z) Begin();
        }

        /// <summary>Starts the moment now (for tests and other scripts).</summary>
        public void Begin()
        {
            if (started) return;
            started = true;
            StartCoroutine(Run());
        }

        IEnumerator Run()
        {
            // the swell, flat in the head: it is the player's dread, not a thing in the room
            AudioSource swell = null;
            if (riser)
            {
                swell = gameObject.AddComponent<AudioSource>();
                swell.clip = riser;
                swell.spatialBlend = 0f;
                swell.volume = riserVolume;
                ScriptedSound.Mark(swell);
                swell.Play();
            }

            // the door swings shut on its own, in step with its creak; it is shut for good after
            float landing = doorLands;
            if (door)
            {
                float doorVolume = door.volume;
                door.volume = 0f;                 // its own creak and shut are replaced by the recording
                door.closeTime = landing;
                if (door.IsOpen) door.Close();
                else landing = 0.6f;              // already shut by the player: just the slam
                if (doorClose && landing > 1f) SpatialSound.PlayAt(doorClose, door.Centre, doorCloseVolume, 1.5f, 40f);
                door.locked = true;
                yield return new WaitForSeconds(landing);
                if (slam) SpatialSound.PlayAt(slam, door.Centre, slamVolume, 2f, 60f);
                var rig = player ? player.GetComponentInChildren<BodycamCameraRig>() : null;
                if (rig) rig.AddTrauma(0.45f);
                var stress = player ? player.GetComponent<PlayerStress>() : null;
                if (stress) stress.Add(0.3f);
                yield return new WaitForSeconds(0.3f);
                door.volume = doorVolume;
            }

            yield return new WaitForSeconds(Mathf.Max(0f, blackoutDelay - 0.3f));

            // the light goes: bulb, flashlight, every sound of the place, the swell with them
            if (swell) swell.Stop();
            KillAmbience();
            var torch = player ? player.GetComponentInChildren<BodycamFlashlight>(true) : null;
            if (torch) torch.SetOn(false, false);
            if (bulb) bulb.BurstNow();
        }

        /// <summary>Silences the place: every ambient loop and random sound, and any other loop playing in the
        /// scene that does not belong to the manthing, the chains or the player.</summary>
        void KillAmbience()
        {
            foreach (var a in FindObjectsByType<AmbientSound>())
            {
                foreach (var s in a.GetComponentsInChildren<AudioSource>(true)) s.Stop();
                a.enabled = false;
            }
            Transform playerRoot = player ? player.transform.root : null;
            foreach (var s in FindObjectsByType<AudioSource>())
            {
                if (!s.loop || !s.isPlaying) continue;
                if (playerRoot && s.transform.IsChildOf(playerRoot)) continue;
                if (manthing && s.transform.IsChildOf(manthing.transform)) continue;
                if (s.GetComponentInParent<HangingChain>()) continue;
                s.Stop();
            }
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.8f, 0.1f, 0.1f, 0.35f);
            Gizmos.DrawCube(triggerCenter, triggerSize);
        }
    }
}
