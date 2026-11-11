using System.Collections;
using System.Collections.Generic;
using BodycamHorror.Player;
using BodycamHorror.Weapons;
using UnityEngine;

namespace BodycamHorror.Enemies
{
    /// <summary>
    /// A scare hung on a pickup: the moment the gun is taken the lights come on for a couple of
    /// seconds and the room turns out to be full of infected standing around the spot, all facing it.
    /// They do nothing and nothing hurts them. Then the light dies and they are gone.
    /// </summary>
    public class PickupScare : MonoBehaviour
    {
        [Tooltip("The pickup that sets it off. Empty = any pickup in the scene.")]
        public WeaponPickup pickup;
        [Tooltip("Only once.")]
        public bool once = true;

        [Header("Light")]
        [Tooltip("Seconds the light stays on.")]
        public float duration = 2f;
        [Tooltip("Height of the light above the spot (m). Keep it under the ceiling.")]
        public float lightHeight = 2.75f;
        public Color lightColor = new Color(0.92f, 0.95f, 1f);
        public float lightIntensity = 9f;
        public float lightRange = 16f;
        [Tooltip("The tube stutters for this long as it comes on (s).")]
        public float stutterTime = 0.22f;

        [Header("Infected")]
        public int count = 7;
        [Tooltip("They stand in a ring this far from the spot (m, min..max).")]
        public Vector2 ringRadius = new Vector2(2.1f, 3.2f);
        [Tooltip("Nobody appears closer to the player than this (m).")]
        public float clearOfPlayer = 1.3f;
        public LayerMask groundMask = ~((1 << 2) | (1 << 8));   // not Ignore Raycast or Hitbox

        [Header("Look (empty = box placeholder), as on InfectedSpawner")]
        public GameObject modelPrefab;
        public Material modelMaterial;
        public float modelScale = 1f;
        public RuntimeAnimatorController animatorController;
        public AnimationClip[] idleVariants;

        [Header("Sound (empty = generated)")]
        public AudioClip onClip;
        public AudioClip offClip;
        [Range(0f, 1f)] public float volume = 0.9f;

        readonly List<GameObject> spawned = new List<GameObject>();
        Light lamp;
        bool running, done;

        void OnEnable() => WeaponPickup.Taken += OnTaken;

        void OnDisable()
        {
            WeaponPickup.Taken -= OnTaken;
            Clear();
            running = false;
        }

        void OnTaken(WeaponPickup taken, GameObject player)
        {
            if (running || (once && done) || (pickup && taken != pickup)) return;
            StartCoroutine(Run(taken.transform.position, player ? player.transform : null));
        }

        IEnumerator Run(Vector3 spot, Transform player)
        {
            running = done = true;
            Spawn(spot, player);

            if (!lamp)
            {
                var lampObject = new GameObject("ScareLight");
                lampObject.transform.SetParent(transform, false);
                lamp = lampObject.AddComponent<Light>();
                lamp.type = LightType.Point;
                lamp.shadows = LightShadows.Soft;
                lamp.shadowNearPlane = 0.1f;
                ViewModelLighting.AlsoLightViewModel(lamp);
            }
            lamp.transform.position = spot + Vector3.up * lightHeight;
            lamp.color = lightColor;
            lamp.range = lightRange;
            lamp.enabled = true;
            Play(onClip ? onClip : ProceduralAudio.DoubleClick(0.05f, 700f, 0.9f), lamp.transform.position);

            float t = 0f;
            while (t < duration)
            {
                // comes on like a tired tube: a couple of stutters, then steady
                bool lit = t >= stutterTime || Mathf.PerlinNoise(t * 60f, 3.7f) > 0.42f;
                lamp.intensity = lit ? lightIntensity : lightIntensity * 0.05f;
                t += Time.deltaTime;
                yield return null;
            }

            lamp.enabled = false;
            Play(offClip ? offClip : ProceduralAudio.Click(0.04f, 500f, 0.8f), lamp.transform.position);
            Clear();
            running = false;
        }

        void Spawn(Vector3 spot, Transform player)
        {
            float start = Random.Range(0f, 360f);
            for (int i = 0; i < count; i++)
            {
                // evenly round the ring, each a little off its place
                float angle = (start + (i + Random.Range(-0.3f, 0.3f)) * 360f / Mathf.Max(count, 1)) * Mathf.Deg2Rad;
                var dir = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));
                float reach = Random.Range(ringRadius.x, ringRadius.y);
                // not inside a wall: stop short of whatever is that way
                if (Physics.Raycast(spot + Vector3.up, dir, out RaycastHit wall, reach + 0.6f, groundMask, QueryTriggerInteraction.Ignore))
                    reach = Mathf.Max(wall.distance - 0.6f, 0.5f);
                Vector3 pos = spot + dir * reach;
                if (player)
                {
                    Vector3 away = pos - player.position;
                    away.y = 0f;
                    if (away.magnitude < clearOfPlayer)
                        pos = player.position + (away.sqrMagnitude > 0.001f ? away.normalized : dir) * clearOfPlayer;
                }
                if (Physics.Raycast(pos + Vector3.up * 2f, Vector3.down, out RaycastHit ground, 6f, groundMask, QueryTriggerInteraction.Ignore))
                    pos = ground.point + Vector3.up * 0.02f;

                // they all look at whoever just took the gun
                Vector3 look = (player ? player.position : spot) - pos;
                look.y = 0f;
                var go = new GameObject("ScareInfected");
                go.transform.SetPositionAndRotation(pos, look.sqrMagnitude > 0.001f ? Quaternion.LookRotation(look) : Quaternion.identity);
                var infected = go.AddComponent<Infected>();
                infected.temper = Infected.Temper.Idler;
                infected.invulnerable = true;
                infected.aggroRange = 0f;          // blind and deaf: they only stand there
                infected.closeAggroRange = 0f;
                infected.gunshotAlertRange = 0f;
                infected.alertSpreadRange = 0f;
                if (modelPrefab) go.GetComponent<InfectedBody>().UseModel(modelPrefab, modelMaterial, modelScale, animatorController, idleVariants);
                spawned.Add(go);
            }
        }

        void Clear()
        {
            foreach (GameObject go in spawned) if (go) Destroy(go);
            spawned.Clear();
        }

        void Play(AudioClip clip, Vector3 at)
        {
            if (clip) AudioSource.PlayClipAtPoint(clip, at, volume);
        }
    }
}
