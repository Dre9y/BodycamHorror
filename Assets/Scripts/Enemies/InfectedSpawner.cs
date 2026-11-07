using System.Collections.Generic;
using UnityEngine;

namespace BodycamHorror.Enemies
{
    /// <summary>
    /// Drops a group of infected around this point (each one builds its own placeholder body).
    /// Can bring a fresh group back once the last one is dead, which is handy for testing.
    /// </summary>
    public class InfectedSpawner : MonoBehaviour
    {
        public int count = 6;
        public float radius = 4f;
        public bool spawnOnStart = true;
        [Tooltip("Spawn them already hunting the player.")]
        public bool startAlerted = false;
        public bool respawnWhenAllDead = true;
        public float respawnDelay = 6f;
        public LayerMask groundMask = ~(1 << 2);

        readonly List<Infected> alive = new List<Infected>();
        float respawnTimer = -1f;

        public IReadOnlyList<Infected> Alive => alive;

        void OnEnable() => Infected.Died += OnDied;
        void OnDisable() => Infected.Died -= OnDied;

        void Start()
        {
            if (spawnOnStart) SpawnGroup();
        }

        void Update()
        {
            if (respawnTimer < 0f) return;
            respawnTimer -= Time.deltaTime;
            if (respawnTimer < 0f) SpawnGroup();
        }

        [ContextMenu("Spawn Group")]
        public void SpawnGroup()
        {
            if (!Application.isPlaying) return;
            for (int i = 0; i < count; i++)
            {
                Vector2 r = Random.insideUnitCircle * radius;
                Vector3 pos = transform.position + new Vector3(r.x, 0f, r.y);
                if (Physics.Raycast(pos + Vector3.up * 3f, Vector3.down, out RaycastHit g, 10f, groundMask, QueryTriggerInteraction.Ignore))
                    pos = g.point + Vector3.up * 0.02f;
                var go = new GameObject("Infected");
                go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
                var inf = go.AddComponent<Infected>();
                alive.Add(inf);
                if (startAlerted) inf.Alert(false, false);
            }
        }

        void OnDied(Infected inf)
        {
            if (!alive.Remove(inf)) return;
            if (alive.Count == 0 && respawnWhenAllDead) respawnTimer = respawnDelay;
        }

        void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.8f, 0.2f, 0.1f, 0.6f);
            Gizmos.DrawWireSphere(transform.position, radius);
        }
    }
}
