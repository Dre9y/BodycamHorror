using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace BodycamHorror.EditorTools
{
    /// <summary>
    /// Dresses Assets/Scenes/Level.unity (open it first) with the Khrushchevka props (BodycamHorror > Set Up Khrushchev
    /// Props): the grey block stand-ins of beds, wardrobes, tables, the TV and the radiators give way to the models,
    /// and the flats fill up with the rest of a Soviet living room: a carpet on the wall, a sideboard, a rug, a coffee
    /// table, a chandelier, a clock, a portrait, a calendar, a kettle, a plant. Everything new goes under one root,
    /// "Interior_Khrushchev"; the stand-ins are only switched off (the ones with a job, like the wardrobe pushed aside
    /// in the landing's flat, are left alone). Wall things find their wall by a ray from a point in the room, so they sit
    /// on it whatever the room's exact size. Rerunning rebuilds the root and puts the moved things back on their tops.
    /// A full Build Level Scene wipes it, like the rest of the hand dressing: run this again after it.
    /// Menu: BodycamHorror > Dress Level Interiors.
    /// </summary>
    public static class LevelInteriorDresser
    {
        const string ScenePath = "Assets/Scenes/Level.unity";
        const string RootName = "Interior_Khrushchev";
        const string P = "Assets/Prefabs/Props/";
        static Transform root, group;
        static readonly List<string> missed = new List<string>();

        [MenuItem("BodycamHorror/Dress Level Interiors")]
        public static void Dress()
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (scene.path != ScenePath) { Debug.LogError("Open " + ScenePath + " first"); return; }
            foreach (var go in scene.GetRootGameObjects()) if (go.name == RootName) Object.DestroyImmediate(go);
            Physics.SyncTransforms();
            missed.Clear();
            root = new GameObject(RootName).transform;

            Flat();
            BigRoom();
            SmallRoom();
            PveFlats();

            foreach (Transform t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.isStatic = true;
            EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log($"Level interiors dressed: {root.GetComponentsInChildren<Collider>(true).Length} colliders, " +
                      $"{CountProps()} props" + (missed.Count > 0 ? "; no wall found for: " + string.Join(", ", missed) : ""));
        }

        static int CountProps() { int n = 0; foreach (Transform g in root) n += g.childCount; return n; }

        // ---------------------------------------------------------------- the rooms

        /// <summary>The lived-in room behind the locked door (x 7.5..13.5, z 10.3..14.8).</summary>
        static void Flat()
        {
            Group("Flat");
            const string F = "Level/Annex/Flat/";
            Hide(F + "Bed");
            Put("Bed", new Vector3(8.58f, 0f, 14.27f), 90f);
            AgainstWall(F + "Wardrobe", "Wardrobe", 0.56f);

            // the kitchen table: the oilcloth one; the mug and the bottle stay, on its top
            foreach (var n in new[] { "Top", "Leg0", "Leg1", "Leg2", "Leg3", "Cloth" }) Hide(F + "Table/" + n);
            Put("DiningTable", new Vector3(9.2f, 0f, 12.6f), 0f);
            const float tableTop = 0.746f;
            Onto(F + "Table/Mug", tableTop);
            Onto(F + "Table/Bottle", tableTop);
            Put("Kettle", new Vector3(8.95f, tableTop, 12.74f), 30f);
            Put("Stool", new Vector3(10.05f, 0f, 12.4f), 12f);

            // the TV corner (the set's flicker light stays)
            foreach (var n in new[] { "Cabinet", "Set", "Screen", "Antenna" }) Hide(F + "TV/" + n);
            Put("TV_Stand", new Vector3(11.9f, 0f, 10.52f), 0f);
            Put("CRT_TV", new Vector3(11.9f, 0.52f, 10.68f), 0f);
            var box = Find(F + "Box");
            if (box) box.position = new Vector3(13.0f, 0f, 10.85f);

            Hide(F + "Radiator");
            WallFloor("Radiator_Cast", new Vector3(8.75f, 0.5f, 10.9f), Vector3.back, 0f);
            Hide(F + "Rug");
            Put("Rug_Floor", new Vector3(11.75f, 0f, 12.65f), 0f);
            Hide(F + "Nightstand/Body");
            Put("Nightstand", new Vector3(9.97f, 0f, 14.58f), 180f);
            Onto(F + "Nightstand/Lamp", 0.6f);

            var sideboard = WallFloor("Sideboard", new Vector3(12.9f, 1.0f, 13.65f), Vector3.right, 0.42f);
            if (sideboard) Put("Radio", sideboard.transform.position + Vector3.up * 1.833f, sideboard.transform.eulerAngles.y);
            Put("PottedPlant", new Vector3(7.87f, 1.45f, 10.68f), 0f);   // on the fridge
            Put("CoffeeTable", new Vector3(11.84f, 0f, 13.4f), 0f);
            Put("Ashtray", new Vector3(12.08f, 0.5f, 13.3f), 0f);
            Put("FloorLamp", new Vector3(10.7f, 0f, 14.25f), 0f);

            Wall("WallCarpet", new Vector3(11.95f, 1.65f, 14.0f), Vector3.forward);
            Wall("Portrait", new Vector3(8.6f, 1.68f, 14.0f), Vector3.forward);
            Wall("Calendar", new Vector3(8.0f, 1.45f, 13.3f), Vector3.left);
            Wall("WallClock", new Vector3(9.6f, 1.95f, 11.0f), Vector3.back);
        }

        /// <summary>The big L-shaped room the player first crosses: wrecked, slept in.</summary>
        static void BigRoom()
        {
            Group("BigRoom");
            AgainstWall("Level/BigRoom/Wardrobe", "Wardrobe", 0.56f);
            Hide("Level/BigRoom/Radiator");
            WallFloor("Radiator_Cast", new Vector3(6.8f, 0.5f, 7.4f), Vector3.forward, 0f);
            WallFloor("Sideboard", new Vector3(8.4f, 1.0f, 2.0f), Vector3.right, 0.42f);
            WallFloor("Nightstand", new Vector3(8.4f, 0.5f, 5.6f), Vector3.right, 0.4f);
            Wall("WallCarpet", new Vector3(7.8f, 1.75f, 7.4f), Vector3.forward);
            Wall("Portrait", new Vector3(8.4f, 1.62f, 4.4f), Vector3.right);
            Ceiling("Chandelier", 7.0f, 3.0f);
            var tv = Put("CRT_TV", new Vector3(6.1f, 0f, 2.3f), 200f);   // dragged off and dropped
            if (tv) tv.transform.rotation = Quaternion.Euler(0f, 200f, 0f);
            Put("Kettle", new Vector3(7.25f, 0f, 5.45f), 110f);
            Put("Stool", new Vector3(5.75f, 0f, 5.6f), 35f);
        }

        /// <summary>The small room with the desk (the pistol) by the inner window.</summary>
        static void SmallRoom()
        {
            Group("SmallRoom");
            AgainstWall("Level/SmallRoom/Wardrobe", "Wardrobe", 0.56f);
            Hide("Level/SmallRoom/Radiator");
            WallFloor("Radiator_Cast", new Vector3(2.65f, 0.5f, 7.4f), Vector3.forward, 0f);
            Wall("WallClock", new Vector3(3.3f, 1.95f, 3.6f), Vector3.back);
            Wall("Calendar", new Vector3(0.6f, 1.5f, 3.8f), Vector3.left);
            Wall("Portrait", new Vector3(0.6f, 1.62f, 6.9f), Vector3.left);
        }

        /// <summary>The flats off the PvE landing (hand-edited: only the plain wardrobe blocks are swapped).</summary>
        static void PveFlats()
        {
            const string L = "PvE_Landing/Furniture/";
            Group("Flat1");
            AgainstWall(L + "Flat1/Wardrobe", "Wardrobe", 0.56f);
            Wall("Portrait", new Vector3(53.2f, 1.65f, 15.0f), Vector3.forward);
            Wall("Calendar", new Vector3(47.6f, 1.5f, 15.0f), Vector3.forward);

            Group("Flat2");
            Wall("WallCarpet", new Vector3(53.7f, 1.7f, 2.75f), Vector3.right);
            Put("CoffeeTable", new Vector3(52.95f, 0f, 2.75f), 90f);
            Put("Ashtray", new Vector3(52.9f, 0.5f, 2.6f), 0f);
            Ceiling("Chandelier", 52.4f, 3.0f);
            Put("Kettle", new Vector3(46.5f, 0.91f, 0.2f), 160f);
            Wall("Calendar", new Vector3(46.0f, 1.6f, 0.9f), Vector3.back);
            Wall("WallClock", new Vector3(49.0f, 2.0f, 3.0f), Vector3.back);

            Group("Flat3");
            AgainstWall(L + "Flat3/Wardrobe", "Wardrobe", 0.56f);
            Wall("WallCarpet", new Vector3(36.0f, 1.7f, 0.9f), Vector3.back);
            Put("CoffeeTable", new Vector3(36.0f, 0f, 1.27f), 0f);
            Put("Ashtray", new Vector3(36.2f, 0.5f, 1.25f), 0f);
            Ceiling("Chandelier", 36.0f, 2.0f);
            Wall("Calendar", new Vector3(28.1f, 1.5f, 0.8f), Vector3.back);
            Put("Radio", new Vector3(28.45f, 0.76f, 0.3f), 0f);

            Group("Flat4");
            AgainstWall(L + "Flat4/Wardrobe", "Wardrobe", 0.56f);
            Wall("WallCarpet", new Vector3(28.4f, 1.7f, 14.2f), Vector3.left);
            Put("Kettle", new Vector3(35.5f, 0.91f, 9.2f), 200f);
            Put("PottedPlant", new Vector3(36.6f, 0.91f, 9.15f), 0f);
        }

        // ---------------------------------------------------------------- placing

        static void Group(string name)
        {
            group = new GameObject(name).transform;
            group.SetParent(root, false);
        }

        static GameObject Put(string prefab, Vector3 position, float yaw)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(P + prefab + ".prefab");
            if (!asset) { Debug.LogWarning("Missing prefab " + prefab + " (BodycamHorror > Set Up Khrushchev Props)"); return null; }
            var go = (GameObject)PrefabUtility.InstantiatePrefab(asset, group);
            go.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
            return go;
        }

        /// <summary>A thing hung on the wall a ray from <paramref name="from"/> along <paramref name="dir"/> meets,
        /// at the ray's height, its face (+Z) out into the room.</summary>
        static GameObject Wall(string prefab, Vector3 from, Vector3 dir)
        {
            if (!WallHit(from, dir, out RaycastHit hit)) { missed.Add($"{group.name}/{prefab}"); return null; }
            Vector3 n = Flat(hit.normal);
            var go = Put(prefab, hit.point + n * 0.004f, 0f);
            if (go) go.transform.rotation = Quaternion.LookRotation(n, Vector3.up);
            return go;
        }

        /// <summary>A thing standing on the floor with its back to that wall (<paramref name="depth"/> deep; 0 for
        /// props whose origin is on the wall plane).</summary>
        static GameObject WallFloor(string prefab, Vector3 from, Vector3 dir, float depth)
        {
            if (!WallHit(from, dir, out RaycastHit hit)) { missed.Add($"{group.name}/{prefab}"); return null; }
            Vector3 n = Flat(hit.normal);
            Vector3 p = hit.point + n * (depth * 0.5f + (depth > 0f ? 0.012f : 0.0f));
            p.y = 0f;
            var go = Put(prefab, p, 0f);
            if (go) go.transform.rotation = Quaternion.LookRotation(n, Vector3.up);
            return go;
        }

        /// <summary>Swaps a block stand-in for the prop, its back to the nearest wall, centred where the block was.</summary>
        static GameObject AgainstWall(string path, string prefab, float depth)
        {
            var t = Find(path);
            if (!t) { missed.Add(path); return null; }
            t.gameObject.SetActive(true);   // (hidden by an earlier run: its bounds are needed)
            Bounds b = RendererBounds(t);
            if (b.size == Vector3.zero) { missed.Add(path); return null; }
            Hide(path);
            Physics.SyncTransforms();
            Vector3 c = new Vector3(b.center.x, 1.0f, b.center.z);
            RaycastHit best = default; float bestD = float.MaxValue;
            foreach (var d in new[] { Vector3.left, Vector3.right, Vector3.forward, Vector3.back })
                if (WallHit(c, d, out RaycastHit h) && h.distance < bestD && h.distance < 1.2f) { best = h; bestD = h.distance; }
            if (bestD == float.MaxValue) { missed.Add(path); return null; }
            Vector3 n = Flat(best.normal);
            Vector3 along = Vector3.Cross(Vector3.up, n);
            Vector3 p = best.point + n * (depth * 0.5f + 0.012f);
            p += along * Vector3.Dot(new Vector3(b.center.x, 0f, b.center.z) - new Vector3(p.x, 0f, p.z), along);   // keep it centred where the block was
            p.y = 0f;
            var go = Put(prefab, p, 0f);
            if (go) go.transform.rotation = Quaternion.LookRotation(n, Vector3.up);
            return go;
        }

        static GameObject Ceiling(string prefab, float x, float z)
        {
            float y = LevelSceneBuilder.Height;
            foreach (var h in Physics.RaycastAll(new Vector3(x, 1.5f, z), Vector3.up, 3f, ~0, QueryTriggerInteraction.Ignore))
            {
                Vector3 s = h.collider.bounds.size;
                if (s.y < 0.5f && s.x > 1f && s.z > 1f && h.point.y < y + 0.5f) { y = h.point.y; break; }
            }
            return Put(prefab, new Vector3(x, y - 0.002f, z), 0f);
        }

        /// <summary>The nearest wall along the ray: a long, thin, tall collider (not a cupboard, a door or a prop).</summary>
        static bool WallHit(Vector3 from, Vector3 dir, out RaycastHit hit)
        {
            var hits = Physics.RaycastAll(from, dir, 4f, ~0, QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (var h in hits)
            {
                if (root && h.collider.transform.IsChildOf(root)) continue;
                Vector3 s = h.collider.bounds.size;
                float thin = Mathf.Min(s.x, s.z), lng = Mathf.Max(s.x, s.z);
                if (thin <= 0.4f && lng >= 1.0f && s.y >= 1.0f) { hit = h; return true; }
            }
            hit = default;
            return false;
        }

        static Vector3 Flat(Vector3 n) { n.y = 0f; return n.sqrMagnitude > 1e-4f ? n.normalized : Vector3.forward; }

        // ---------------------------------------------------------------- the scene's own things

        static Transform Find(string path)
        {
            int slash = path.IndexOf('/');
            string top = slash < 0 ? path : path.Substring(0, slash);
            foreach (var go in EditorSceneManager.GetActiveScene().GetRootGameObjects())
                if (go.name == top) return slash < 0 ? go.transform : go.transform.Find(path.Substring(slash + 1));
            return null;
        }

        static void Hide(string path)
        {
            var t = Find(path);
            if (t) t.gameObject.SetActive(false);
            else missed.Add(path);
        }

        /// <summary>Stands a thing on a top at <paramref name="y"/> (its lowest point on it).</summary>
        static void Onto(string path, float y)
        {
            var t = Find(path);
            if (!t) { missed.Add(path); return; }
            Bounds b = RendererBounds(t);
            if (b.size == Vector3.zero) return;
            t.position += Vector3.up * (y + 0.001f - b.min.y);
        }

        static Bounds RendererBounds(Transform t)
        {
            var rs = t.GetComponentsInChildren<Renderer>(false);
            if (rs.Length == 0) return new Bounds(t.position, Vector3.zero);
            Bounds b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            return b;
        }
    }
}
