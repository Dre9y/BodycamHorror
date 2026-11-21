using System.Collections;
using System.Collections.Generic;
using BodycamHorror.Audio;
using BodycamHorror.Enemies;
using BodycamHorror.Player;
using UnityEngine;

namespace BodycamHorror.Environment
{
    /// <summary>
    /// The light monster's level, start to end.
    /// The player walks round the shaft to the far end of the hall: behind them the way in slams shut and locks. Something
    /// beats twice on the side door (<see cref="bashedDoor"/>) while a low hum swells up: on the second blow it flies off into
    /// the hall, the monster is standing in the doorway, and every lamp in the level stutters and bursts, one after another,
    /// very loud. With the last one every sound of the place is gone (the flashlight cuts out for a moment with them).
    /// From then on it hunts. The emergency power crank in the switch room turns only with the fuse (key id
    /// <see cref="fuseKey"/>) on the key ring; turning it all the way lights the red emergency lamps and opens the exit gate.
    /// </summary>
    public class LightFearEvent : MonoBehaviour
    {
        [Header("Trigger: the player inside this box (the hall's far end)")]
        public Vector3 triggerCenter;
        public Vector3 triggerSize = new Vector3(4f, 3f, 2f);

        [Header("The moment")]
        public Door entryDoor;
        public BashedDoor bashedDoor;
        public Manthing manthing;
        [Tooltip("Every lamp of the level: all of them go out in the blackout.")]
        public Transform levelLights;
        [Tooltip("From the slam to the first blow on the door (s).")]
        public float bashDelay = 1.6f;
        [Tooltip("From the door flying off to the lights dying (s): long enough to see what is in the doorway.")]
        public float blackoutDelay = 0.7f;
        [Tooltip("After the lights die the flashlight is dead too, until the player switches it on or fires this many shots.")]
        public int torchShotsToWake = 2;
        public AudioClip slam;
        [Range(0f, 1f)] public float slamVolume = 1f;
        public AudioClip riser;
        [Range(0f, 1f)] public float riserVolume = 0.6f;
        [Tooltip("A low hum looped under the door being beaten: it swells from the first blow until the lights die.")]
        public AudioClip bashHum;
        [Range(0f, 1f)] public float bashHumVolume = 0.85f;
        [Tooltip("A bulb bursting: played very loud at every lamp in turn.")]
        public AudioClip bulbBurst;
        [Tooltip("Laid over the burst: glass dropping.")]
        public AudioClip bulbBurstLayer;
        [Range(0f, 1f)] public float bulbVolume = 1f;

        [Header("Emergency power")]
        [Tooltip("The crank (a Valve): dead until the fuse is in.")]
        public Valve powerCrank;
        public string fuseKey = "fuse";
        [Tooltip("Shutters that go up when the fuse is taken (the rooms open one stage at a time).")]
        public SlidingGate[] openOnFuse;
        [Tooltip("Off until the power is on.")]
        public GameObject emergencyLights;
        public SlidingGate exitGate;
        public AudioClip powerOnClip;

        [Header("The way out")]
        public Vector3 exitCenter;
        public Vector3 exitSize = new Vector3(3f, 3f, 2f);
        public string endMessage = "Конец прототипа: дальше — побег";

        BodycamPlayerController player;
        KeyRing ring;
        bool started, powered, ended, nearCrank;

        void Start()
        {
            player = FindAnyObjectByType<BodycamPlayerController>();
            if (player) ring = KeyRing.Of(player.gameObject);
            if (emergencyLights) emergencyLights.SetActive(false);
            if (powerCrank)
            {
                powerCrank.enabled = false;
                powerCrank.closed.AddListener(PowerOn);
            }
            KeyPickup.Taken += OnKeyTaken;
        }

        void OnDestroy() => KeyPickup.Taken -= OnKeyTaken;

        void OnKeyTaken(KeyPickup key, GameObject who)
        {
            if (key.keyId != fuseKey || openOnFuse == null) return;
            foreach (SlidingGate g in openOnFuse) if (g) g.Open();
        }

        void Update()
        {
            if (!player) return;
            Vector3 p = player.transform.position + Vector3.up * 0.9f;
            if (!started && Inside(p, triggerCenter, triggerSize)) Begin();
            if (powerCrank && !powered) Crank(p);
            if (powered && !ended && Inside(p, exitCenter, exitSize))
            {
                ended = true;
                if (ring) ring.Say(endMessage);
            }
        }

        static bool Inside(Vector3 p, Vector3 c, Vector3 size)
        {
            Vector3 d = p - c, h = size * 0.5f;
            return Mathf.Abs(d.x) <= h.x && Mathf.Abs(d.y) <= h.y && Mathf.Abs(d.z) <= h.z;
        }

        /// <summary>The crank is dead without the fuse; walking up to it with one puts it in.</summary>
        void Crank(Vector3 p)
        {
            bool near = Vector3.Distance(p, powerCrank.transform.position) < 2.5f;
            if (near && !nearCrank && ring)
            {
                if (powerCrank.enabled) { }
                else if (ring.Has(fuseKey))
                {
                    ring.UseUp(fuseKey);
                    powerCrank.enabled = true;
                    ring.Say("Предохранитель вставлен. Крути ручку");
                }
                else ring.Say("Щит мёртв: нет предохранителя");
            }
            nearCrank = near;
        }

        public void Begin()
        {
            if (started) return;
            started = true;
            StartCoroutine(Run());
        }

        IEnumerator Run()
        {
            // the way in: shut and locked behind the player
            if (entryDoor)
            {
                entryDoor.closeTime = 0.35f;
                entryDoor.Close();
                entryDoor.locked = true;
                entryDoor.keyId = "";
                entryDoor.enemiesCanOpen = false;
                yield return new WaitForSeconds(0.35f);
                if (slam) SpatialSound.PlayAt(slam, entryDoor.Centre, slamVolume, 2f, 60f);
                var rig = player.GetComponentInChildren<BodycamCameraRig>();
                if (rig) rig.AddTrauma(0.35f);
            }

            yield return new WaitForSeconds(bashDelay);

            // from the first blow on the door a low hum swells up under everything, and the dread riser with it;
            // they keep climbing until the lights die, and are cut dead with them
            float build = (bashedDoor ? bashedDoor.gap * Mathf.Max(1, bashedDoor.blows - 1) : 1f) + blackoutDelay + 0.5f;
            AudioSource hum = Flat(bashHum, true);
            AudioSource swell = Flat(riser, false);
            if (hum) StartCoroutine(Swell(hum, bashHumVolume, build, 0.85f, 1.12f));
            if (swell) StartCoroutine(Swell(swell, riserVolume, build * 0.6f, 1f, 1f));

            // two blows on the side door; on the second it flies off and the thing is in the doorway
            if (bashedDoor)
            {
                bool torn = false;
                bashedDoor.TornOff += () => torn = true;
                bashedDoor.Begin();
                while (!torn) yield return null;
            }
            if (manthing) manthing.AppearNow();

            yield return new WaitForSeconds(blackoutDelay);
            yield return Blackout(hum, swell);
        }

        /// <summary>A sound in the head (not placed in the room), started silent.</summary>
        AudioSource Flat(AudioClip clip, bool loop)
        {
            if (!clip) return null;
            var a = gameObject.AddComponent<AudioSource>();
            a.clip = clip;
            a.loop = loop;
            a.spatialBlend = 0f;
            a.volume = 0f;
            ScriptedSound.Mark(a);
            a.Play();
            return a;
        }

        /// <summary>Rises from silence to <paramref name="volume"/> over <paramref name="time"/>, its pitch creeping up with it.</summary>
        static IEnumerator Swell(AudioSource a, float volume, float time, float pitchFrom, float pitchTo)
        {
            float t = 0f;
            while (a && a.isPlaying && t < 1f)
            {
                t = Mathf.Min(1f, t + Time.deltaTime / Mathf.Max(time, 0.05f));
                a.volume = volume * t * t;   // slow at first, then it climbs
                a.pitch = Mathf.Lerp(pitchFrom, pitchTo, t);
                yield return null;
            }
        }

        /// <summary>
        /// The lamps stutter, then burst one after another, loud, in a wave out from the broken door. With the last one
        /// every sound of the place is gone, the swell too, and the flashlight drops out for a moment.
        /// </summary>
        IEnumerator Blackout(AudioSource hum, AudioSource swell)
        {
            if (!levelLights) yield break;
            var lights = levelLights.GetComponentsInChildren<Light>(true);
            var glows = levelLights.GetComponentsInChildren<Renderer>(true);
            foreach (var b in levelLights.GetComponentsInChildren<LightFlicker>(true)) b.enabled = false;

            float[] stutter = { 0.08f, 0.12f, 0.05f, 0.16f };
            for (int i = 0; i < stutter.Length; i++)
            {
                bool on = i % 2 == 1;
                foreach (var l in lights) if (l) l.enabled = on;
                SetGlow(glows, on);
                yield return new WaitForSeconds(stutter[i]);
            }
            foreach (var l in lights) if (l) l.enabled = true;
            SetGlow(glows, true);

            // pop, pop, pop: nearest the door first
            var lamps = new List<Transform>();
            foreach (Transform lamp in levelLights) lamps.Add(lamp);
            Vector3 from = bashedDoor ? bashedDoor.transform.position : player.transform.position;
            lamps.Sort((a, b) => (a.position - from).sqrMagnitude.CompareTo((b.position - from).sqrMagnitude));
            Transform nearest = null;
            float best = float.MaxValue;
            foreach (Transform lamp in lamps)
            {
                float d = (lamp.position - player.transform.position).sqrMagnitude;
                if (d < best) { best = d; nearest = lamp; }
            }
            foreach (Transform lamp in lamps)
            {
                Burst(lamp, lamp == nearest);
                yield return new WaitForSeconds(Random.Range(0.05f, 0.16f));
            }

            // silence: the swell, the hum and every ambient sound of the place, cut dead
            if (hum) hum.Stop();
            if (swell) swell.Stop();
            KillAmbience();

            var torch = player.GetComponentInChildren<BodycamFlashlight>(true);
            if (torch)
            {
                torch.SetOn(false, false);
                StartCoroutine(TorchDead(torch));
            }
        }

        /// <summary>
        /// After the blackout the flashlight stays dead, dark, the monster loose in it: until the player thinks to switch it
        /// back on, or fires a few shots in a panic, the recoil jarring it back to life with a stutter.
        /// </summary>
        IEnumerator TorchDead(BodycamFlashlight torch)
        {
            var shooting = player.GetComponentInChildren<Weapons.PlayerShooting>(true);
            int shots = 0;
            void Shot() => shots++;
            if (shooting) shooting.Fired += Shot;
            while (torch && !torch.IsOn && shots < torchShotsToWake) yield return null;
            if (shooting) shooting.Fired -= Shot;
            if (!torch || torch.IsOn) yield break;   // switched on by the player
            yield return new WaitForSeconds(0.12f);
            torch.SetOn(true, true);
            torch.Flicker(0.9f);
        }

        /// <summary>One lamp bursts: a very loud crack of glass where it hangs, and it is dark for good.</summary>
        void Burst(Transform lamp, bool nearest)
        {
            Vector3 at = lamp.position + Vector3.down * 0.45f;
            foreach (var r in lamp.GetComponentsInChildren<Renderer>(true))
                if (r.name == "Bulb") r.enabled = false;               // the glass is gone
            foreach (var l in lamp.GetComponentsInChildren<Light>(true)) l.enabled = false;
            foreach (var h in lamp.GetComponentsInChildren<LightHaze>(true))
            {
                foreach (var r in h.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
                h.enabled = false;
            }
            // full loudness out to a long way (a big minimum distance), two layers on top of each other
            var crack = bulbBurst ? SpatialSound.PlayAt(bulbBurst, at, bulbVolume, 14f, 90f) : null;
            if (crack) crack.pitch = Random.Range(0.9f, 1.1f);
            var drop = bulbBurstLayer ? SpatialSound.PlayAt(bulbBurstLayer, at, bulbVolume, 8f, 70f) : null;
            if (drop) drop.pitch = Random.Range(0.95f, 1.15f);
            float d = Vector3.Distance(at, player.transform.position);
            if (nearest && bulbBurst)
            {
                // the one closest to the player also hits flat in the ears
                var a = Flat(bulbBurst, false);
                a.volume = bulbVolume;
                Destroy(a, bulbBurst.length + 0.1f);
            }
            var rig = player.GetComponentInChildren<BodycamCameraRig>();
            if (rig) rig.AddTrauma(Mathf.Lerp(0.3f, 0.05f, Mathf.InverseLerp(2f, 15f, d)));
            var stress = player.GetComponent<PlayerStress>();
            if (stress) stress.Add(Mathf.Lerp(0.12f, 0.03f, Mathf.InverseLerp(2f, 15f, d)));
        }

        /// <summary>Every ambient loop and random sound, and any other loop still playing that is not the monster's or the player's.</summary>
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
                s.Stop();
            }
        }

        static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

        static void SetGlow(Renderer[] renderers, bool on)
        {
            var block = new MaterialPropertyBlock();
            foreach (var r in renderers)
            {
                if (!r || !r.sharedMaterial || !r.sharedMaterial.IsKeywordEnabled("_EMISSION")) continue;
                if (on) r.SetPropertyBlock(null);
                else { r.GetPropertyBlock(block); block.SetColor(EmissionId, Color.black); r.SetPropertyBlock(block); }
            }
        }

        void PowerOn()
        {
            if (powered) return;
            powered = true;
            if (emergencyLights) emergencyLights.SetActive(true);
            if (powerOnClip && powerCrank) SpatialSound.PlayAt(powerOnClip, powerCrank.transform.position, 0.8f, 2f, 40f);
            if (exitGate) exitGate.Open();
            if (ring) ring.Say("Аварийное питание включено. Ворота у триггера открыты");
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.8f, 0.1f, 0.1f, 0.35f);
            Gizmos.DrawCube(triggerCenter, triggerSize);
            Gizmos.color = new Color(0.1f, 0.8f, 0.2f, 0.35f);
            Gizmos.DrawCube(exitCenter, exitSize);
        }
    }
}
