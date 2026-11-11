using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace BodycamHorror.AI
{
    /// <summary>
    /// Bakes a NavMesh from the scene colliders when the scene starts, so the level can be edited
    /// freely without re-baking and without the AI Navigation package. Monsters and the player are
    /// left out of the bake. Call Rebake() after moving level geometry at runtime.
    /// </summary>
    [DefaultExecutionOrder(-500)]
    public class RuntimeNavMesh : MonoBehaviour
    {
        [Tooltip("World area to bake (centered on this object).")]
        public Vector3 size = new Vector3(60f, 20f, 60f);
        [Tooltip("Layers used for the bake. Ignore Raycast (the player) is excluded.")]
        public LayerMask layers = ~(1 << 2);
        [Tooltip("0 = Humanoid agent type.")]
        public int agentTypeId;
        public float agentRadius = 0.4f;
        public float agentHeight = 1.8f;
        public float agentClimb = 0.45f;

        NavMeshDataInstance instance;
        NavMeshData data;

        void Awake() => Rebake();

        void OnDestroy() { if (instance.valid) instance.Remove(); }

        public void Rebake()
        {
            var bounds = new Bounds(transform.position, size);
            var markups = new List<NavMeshBuildMarkup>();
            foreach (var m in FindObjectsByType<MonsterBrain>(FindObjectsInactive.Include))
                markups.Add(new NavMeshBuildMarkup { root = m.transform, overrideIgnore = true, ignoreFromBuild = true, applyToChildren = true });

            // doors the monsters can open are not walls to them: the way through stays on the mesh
            foreach (var d in FindObjectsByType<BodycamHorror.Environment.Door>(FindObjectsInactive.Include))
                if (d.enemiesCanOpen && !d.locked)
                    markups.Add(new NavMeshBuildMarkup { root = d.transform, overrideIgnore = true, ignoreFromBuild = true, applyToChildren = true });

            var sources = new List<NavMeshBuildSource>();
            NavMeshBuilder.CollectSources(bounds, layers, NavMeshCollectGeometry.PhysicsColliders, 0, markups, sources);
            // loose physics props would leave holes that move away
            sources.RemoveAll(s => s.component && s.component.GetComponentInParent<Rigidbody>() is Rigidbody rb && !rb.isKinematic);

            NavMeshBuildSettings settings = NavMesh.GetSettingsByID(agentTypeId);
            settings.agentRadius = agentRadius;
            settings.agentHeight = agentHeight;
            settings.agentClimb = agentClimb;

            if (instance.valid) instance.Remove();
            data = NavMeshBuilder.BuildNavMeshData(settings, sources, new Bounds(Vector3.zero, size), transform.position, Quaternion.identity);
            if (data) instance = NavMesh.AddNavMeshData(data);
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.4f);
            Gizmos.DrawWireCube(transform.position, size);
        }
    }
}
