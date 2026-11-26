using BodycamHorror.Environment;
using UnityEditor;
using UnityEngine;

namespace BodycamHorror.EditorTools
{
    /// <summary>
    /// The light fear level's emergency power, from the props made in Blender (Blender/tools/emergency_power.py):
    /// the crank on its gearbox, the fuse the player carries to it and the fuse's holder on the fuse box door.
    /// The crank is a <see cref="Valve"/> whose wheel is an empty "Wheel" on the shaft with the handle model inside it,
    /// so only the handle turns, and a reimported model can never take the reference with it.
    /// Menu: BodycamHorror > Basement > Build Emergency Crank Prefab.
    /// </summary>
    public static class EmergencyPowerBuilder
    {
        const string KitPrefabs = "Assets/Prefabs/Basement";
        public const string CrankPrefabPath = KitPrefabs + "/EmergencyCrank_Unit.prefab";
        const string TurnClipPath = "Assets/Audio/Valve/valve_turn.wav";
        const string ClosedClipPath = "Assets/Audio/Ambience/Prepared/knock_pipe_concrete.wav";

        /// <summary>The gearbox's shaft end, out from the wall (m): the handle turns there.</summary>
        public const float ShaftOut = 0.215f;
        /// <summary>A fuse in the holder: its axis, out from the holder's back (m).</summary>
        public const float FuseAxisOut = 0.046f;
        /// <summary>A fuse upright in the holder: length up, blades edge-on between the jaws, its stamp to the front.</summary>
        public static readonly Quaternion FuseInHolder = Quaternion.LookRotation(Vector3.right, Vector3.forward);

        static readonly string[] Props = { "EmergencyCrank", "EmergencyCrank_Handle", "Fuse", "FuseHolder" };

        [MenuItem("BodycamHorror/Basement/Build Emergency Crank Prefab")]
        public static GameObject BuildCrank()
        {
            BasementSetup.SetUpProps(Props);
            var root = new GameObject("EmergencyCrank");
            Kit("EmergencyCrank", root.transform).name = "Body";
            var wheel = new GameObject("Wheel");
            wheel.transform.SetParent(root.transform, false);
            wheel.transform.localPosition = new Vector3(0f, 0f, ShaftOut);
            Kit("EmergencyCrank_Handle", wheel.transform).name = "Handle";

            // the plate and the gearbox; the tube up the wall on its own
            var box = root.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, 0.07f, 0.11f);
            box.size = new Vector3(0.42f, 0.62f, 0.22f);
            var tube = root.AddComponent<BoxCollider>();
            tube.center = new Vector3(-0.28f, 1.15f, 0.09f);
            tube.size = new Vector3(0.07f, 2.0f, 0.07f);

            var valve = root.AddComponent<Valve>();
            valve.wheel = wheel.transform;
            valve.axis = Vector3.forward;
            valve.clockwise = false;   // a positive turn about +z is clockwise to someone facing the wall: the painted arrow's way
            valve.turns = 4f;
            valve.promptOffset = new Vector3(0f, 0f, 0.07f);
            valve.turnClip = AssetDatabase.LoadAssetAtPath<AudioClip>(TurnClipPath);
            valve.closedClip = AssetDatabase.LoadAssetAtPath<AudioClip>(ClosedClipPath);

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, CrankPrefabPath);
            Object.DestroyImmediate(root);
            AssetDatabase.SaveAssets();
            return prefab;
        }

        /// <summary>The crank on a wall: <paramref name="wallPoint"/> on the wall at the shaft's height, the normal into the room.</summary>
        public static Valve PlaceCrank(Vector3 wallPoint, Vector3 wallNormal, Transform parent)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CrankPrefabPath);
            if (!prefab) prefab = BuildCrank();
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            wallNormal = Vector3.ProjectOnPlane(wallNormal, Vector3.up).normalized;
            go.transform.SetPositionAndRotation(wallPoint, Quaternion.LookRotation(wallNormal, Vector3.up));
            return go.GetComponent<Valve>();
        }

        /// <summary>The fuse model (no collider), its length along local x, its stamp up (+y).</summary>
        public static GameObject Fuse(Transform parent)
        {
            if (!AssetDatabase.LoadAssetAtPath<GameObject>($"{KitPrefabs}/Fuse.prefab")) BasementSetup.SetUpProps(Props);
            return Kit("Fuse", parent);
        }

        /// <summary>The fuse holder (back on the door, front +z) and the empty "FuseIn" spot in it, a fuse in it (off until put in).</summary>
        public static GameObject FuseHolder(Transform door, Vector3 localPosition, out GameObject fuseIn)
        {
            if (!AssetDatabase.LoadAssetAtPath<GameObject>($"{KitPrefabs}/FuseHolder.prefab")) BasementSetup.SetUpProps(Props);
            var holder = Kit("FuseHolder", door);
            holder.transform.localPosition = localPosition;
            fuseIn = new GameObject("FuseIn");
            fuseIn.transform.SetParent(door, false);
            fuseIn.transform.localPosition = localPosition + new Vector3(0f, 0f, FuseAxisOut);
            var body = Fuse(fuseIn.transform);
            body.name = "Body";
            body.transform.localRotation = FuseInHolder;
            return holder;
        }

        static GameObject Kit(string name, Transform parent)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>($"{KitPrefabs}/{name}.prefab");
            var go = (GameObject)PrefabUtility.InstantiatePrefab(asset, parent);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            return go;
        }
    }
}
