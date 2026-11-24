using System.IO;
using BodycamHorror.Environment;
using UnityEditor;
using UnityEngine;

namespace BodycamHorror.EditorTools
{
    /// <summary>
    /// The pieces of the PvE landing's key hunt. Brings in the Blender props (Blender/tools/landing_props.py:
    /// FlatKeys.fbx split into Key_1..Key_4, IronKey.fbx, KeyCabinet.fbx) as prefabs in Assets/Prefabs/Props, the keys
    /// with a KeyPickup on them; makes the sprayed flat numbers (Blender/tools/landing_textures.py) into decals
    /// Graffiti_Num1..4; and builds two doors that really open (<see cref="Door"/>) from existing models:
    /// ApartmentDoor_Hinged (the flat door, its printed "37" painted out) and HeavyDoor_Hinged (the iron door).
    /// The hinged prefabs keep their model's own space (origin on the floor, front +Z), so they go where the plain
    /// models went; inside, a child "Door" sits on the hinge line and carries the Door component.
    /// Menu: BodycamHorror > Set Up Landing Props. Rerunning writes over them.
    /// </summary>
    public static class KeyPropsBuilder
    {
        const string PropDir = "Assets/Prefabs/Props";
        public const string ApartmentDoorPath = PropDir + "/ApartmentDoor_Hinged.prefab";
        public const string HeavyDoorPath = PropDir + "/HeavyDoor_Hinged.prefab";
        public const string CabinetPath = PropDir + "/KeyCabinet.prefab";
        public const string IronKeyPath = PropDir + "/IronKey.prefab";
        public static string FlatKeyPath(int n) => $"{PropDir}/Key_{n}.prefab";
        public static string NumberDecalPath(int n) => $"{PropDir}/Graffiti/Graffiti_Num{n}.prefab";

        /// <summary>Where a key's origin goes to hang on hook 3 of the cabinet, in the cabinet's space
        /// (landing_props.py HOOK3, Blender (x, y, z) to Unity (-x, z, -y)).</summary>
        public static readonly Vector3 CabinetHook3 = new Vector3(-0.035f, 0.075f + 0.0018f, 0.03f);

        /// <summary>The flat door's leaf: hinge at the model's x = -0.438, 0.86 wide, its painted front at z = 0.02
        /// (only the handle stands out to 0.093).</summary>
        public const float FlatLeafWidth = 0.86f, FlatLeafFront = 0.02f;

        [MenuItem("BodycamHorror/Set Up Landing Props")]
        public static void SetUp()
        {
            ApartmentPropsSetup.MaxSize["FlatKeys"] = 1024;
            ApartmentPropsSetup.MaxSize["IronKey"] = 1024;
            ApartmentPropsSetup.MaxSize["KeyCabinet"] = 1024;

            ApartmentPropsSetup.Make("FlatKeys", ApartmentPropsSetup.Hull.Box);
            ApartmentPropsSetup.Split("FlatKeys");
            for (int n = 1; n <= 4; n++) MakeKey(FlatKeyPath(n), "flat" + n, "кв. " + n);
            ApartmentPropsSetup.Make("IronKey", ApartmentPropsSetup.Hull.Box);
            MakeKey(IronKeyPath, "exit", "от железной двери");
            ApartmentPropsSetup.Make("KeyCabinet", ApartmentPropsSetup.Hull.Box);
            // the back board only: a box round the whole cabinet would hide the key on its hook from the player's look
            ApartmentPropsSetup.FitBox("KeyCabinet", new Vector3(0f, 0f, 0.005f), new Vector3(0.32f, 0.42f, 0.01f));

            for (int n = 1; n <= 4; n++) DecorPropsSetup.MakeGraffiti("Num" + n, 0.4f);
            DecorPropsSetup.MakeGraffiti("BloodArrow", 1.0f);   // on the landing floor, to the iron door

            BuildApartmentDoor();
            BuildHeavyDoor();
            AssetDatabase.SaveAssets();
            Debug.Log("Landing props set up in " + PropDir);
        }

        /// <summary>A key to pick up: no collider (nothing to trip over or shoot), not static (it is taken away).</summary>
        static void MakeKey(string path, string id, string label)
        {
            var root = PrefabUtility.LoadPrefabContents(path);
            foreach (var c in root.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.isStatic = false;
            foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true)) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var pickup = root.GetComponent<KeyPickup>();
            if (!pickup) pickup = root.AddComponent<KeyPickup>();
            pickup.keyId = id;
            pickup.label = label;
            pickup.promptHeight = 0.08f;

            // a faint warm glint on it, so it can be found without a flashlight: dim and tight, it must not light up
            // its surroundings (in the key cabinet, under a wall lamp, a brighter one flooded the whole box)
            Transform old = root.transform.Find("Glow");
            if (old) Object.DestroyImmediate(old.gameObject);
            Bounds b = default; bool any = false;
            foreach (var r in root.GetComponentsInChildren<Renderer>(true)) { if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds); }
            var glow = new GameObject("Glow").AddComponent<Light>();
            glow.transform.SetParent(root.transform, false);
            glow.transform.position = any ? b.center : root.transform.position;
            glow.type = LightType.Point;
            glow.color = new Color(1f, 0.82f, 0.55f);
            bool big = id == "exit";
            glow.range = big ? 0.4f : 0.25f;
            glow.intensity = big ? 0.06f : 0.036f;   // 40% under what it was: it read as a lamp in the cabinet
            glow.shadows = LightShadows.None;
            pickup.glow = glow;
            PrefabUtility.SaveAsPrefabAsset(root, path);
            PrefabUtility.UnloadPrefabContents(root);
        }

        // ---------------------------------------------------------------- doors that open

        static void BuildApartmentDoor()
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(PropDir + "/ApartmentDoor.prefab");
            var frame = model.transform.Find("Door_Frame");
            var leaf = model.transform.Find("Door_Leaf");

            // the leaf's paint without the printed flat number (Blender/tools/landing_textures.py)
            var paint = frame.GetComponent<MeshRenderer>().sharedMaterial;
            const string texPath = "Assets/Generated/Textures/Props/Door_NoNumber_BaseColor.png";
            AssetDatabase.ImportAsset(texPath);
            const string matPath = "Assets/Generated/Materials/Props/Door_Mat_NoNumber.mat";
            var noNumber = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (!noNumber) { noNumber = new Material(paint); AssetDatabase.CreateAsset(noNumber, matPath); }
            noNumber.CopyPropertiesFromMaterial(paint);
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
            noNumber.SetTexture("_BaseMap", tex);
            noNumber.SetTexture("_MainTex", tex);
            EditorUtility.SetDirty(noNumber);

            var go = Hinged("ApartmentDoor_Hinged", leaf.localPosition, false,
                frame.GetComponent<MeshFilter>().sharedMesh, new[] { paint },
                WithoutNumber(leaf.GetComponent<MeshFilter>().sharedMesh), new[] { noNumber }, null,
                new Vector3(FlatLeafWidth - 0.1f, 1.0f, 0.15f), new Vector3(FlatLeafWidth - 0.1f, 1.0f, -0.08f), out Door door);
            door.openAngle = 100f;
            door.openTime = 1.1f;
            door.closeTime = 0.8f;
            Sounds(door);
            PrefabUtility.SaveAsPrefabAsset(go, ApartmentDoorPath);
            Object.DestroyImmediate(go);
        }

        /// <summary>The leaf with its raised "37" cut off: the digits are thin solids standing 2..8 mm proud of the
        /// paint (z 0.0215..0.029) in a small box round x 0.44, y 1.8; every triangle wholly inside that box goes.</summary>
        static Mesh WithoutNumber(Mesh source)
        {
            const string path = "Assets/Generated/Imported/ApartmentDoor_LeafNoNumber.asset";
            var digits = new Bounds();
            digits.SetMinMax(new Vector3(0.39f, 1.765f, 0.0215f), new Vector3(0.49f, 1.835f, 0.035f));
            Vector3[] v = source.vertices;
            var keep = new System.Collections.Generic.List<int>();
            int[] tris = source.triangles;
            for (int i = 0; i < tris.Length; i += 3)
            {
                if (digits.Contains(v[tris[i]]) && digits.Contains(v[tris[i + 1]]) && digits.Contains(v[tris[i + 2]])) continue;
                keep.Add(tris[i]); keep.Add(tris[i + 1]); keep.Add(tris[i + 2]);
            }
            // the paint had the digits' outline cut out of it: a flat patch closes it, mapped as the paint round it is
            // (u = 0.0889 + (x - 0.35) * 0.2553, v = 0.117 - (y - 1.6) * 0.2553, measured off the mesh), but onto clean
            // paint 62 px further along v: the maps carry the digits' ghost (baked shading, and in the smoothness)
            const float clean = 62f / 2048f;
            var verts = new System.Collections.Generic.List<Vector3>(v);
            var normals = new System.Collections.Generic.List<Vector3>(source.normals);
            var tangents = new System.Collections.Generic.List<Vector4>(source.tangents);
            var uvs = new System.Collections.Generic.List<Vector2>(source.uv);
            int first = verts.Count;
            foreach (var (x, y) in new[] { (0.38f, 1.755f), (0.50f, 1.755f), (0.50f, 1.845f), (0.38f, 1.845f) })
            {
                verts.Add(new Vector3(x, y, 0.0203f));
                normals.Add(Vector3.forward);
                tangents.Add(new Vector4(1f, 0f, 0f, -1f));
                uvs.Add(new Vector2(0.0889f + (x - 0.35f) * 0.2553f, 0.117f - (y - 1.6f) * 0.2553f + clean));
            }
            keep.AddRange(new[] { first, first + 1, first + 2, first, first + 2, first + 3 });   // facing +Z

            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            bool fresh = !mesh;
            if (fresh) mesh = new Mesh();
            mesh.Clear();
            mesh.name = "Door_Leaf_NoNumber";
            mesh.indexFormat = source.indexFormat;
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetTangents(tangents);
            mesh.SetUVs(0, uvs);
            mesh.triangles = keep.ToArray();
            mesh.RecalculateBounds();
            if (fresh) AssetDatabase.CreateAsset(mesh, path);
            else EditorUtility.SetDirty(mesh);
            return mesh;
        }

        static void BuildHeavyDoor()
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Blender/Props/HeavyDoor.prefab");
            var frame = model.transform.Find("DoorFrame");
            var leaf = model.transform.Find("DoorLeaf");
            var wheel = model.transform.Find("DoorWheel");
            var go = Hinged("HeavyDoor_Hinged", leaf.localPosition, true,
                frame.GetComponent<MeshFilter>().sharedMesh, frame.GetComponent<MeshRenderer>().sharedMaterials,
                leaf.GetComponent<MeshFilter>().sharedMesh, leaf.GetComponent<MeshRenderer>().sharedMaterials, wheel,
                new Vector3(0.8f, 1.1f, -0.12f), new Vector3(0.8f, 1.1f, 0.12f), out Door door);
            door.openAngle = 95f;
            door.openTime = 2.2f;
            door.closeTime = 1.6f;
            door.volume = 0.55f;
            door.enemiesCanOpen = false;
            Sounds(door);
            PrefabUtility.SaveAsPrefabAsset(go, HeavyDoorPath);
            Object.DestroyImmediate(go);
        }

        static void Sounds(Door door)
        {
            door.openClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/Door/door_open.wav");
            door.closeClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/Door/door_close.wav");
            door.shutClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/Door/door_shut.wav");
            door.lockedClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/Door/door_locked.wav");
        }

        /// <summary>
        /// Root in the model's space; a child "Door" on the hinge (the leaf's own origin), turned so its +X runs along
        /// the leaf (a leaf that runs along the model's -X makes it face the other way); under it the fixed frame and
        /// the turning "Leaf" with the leaf mesh, any extra part that moves with it, and the two handles (in the
        /// Door's space: x along the leaf, z out of its +Z side).
        /// </summary>
        static GameObject Hinged(string name, Vector3 hinge, bool leafAlongMinusX, Mesh frameMesh, Material[] frameMats,
            Mesh leafMesh, Material[] leafMats, Transform extra, Vector3 handleA, Vector3 handleB, out Door door)
        {
            var root = new GameObject(name);
            var doorT = new GameObject("Door").transform;
            doorT.SetParent(root.transform, false);
            doorT.localPosition = hinge;
            doorT.localRotation = leafAlongMinusX ? Quaternion.Euler(0f, 180f, 0f) : Quaternion.identity;
            Quaternion undo = Quaternion.Inverse(doorT.localRotation);

            var frame = Part(doorT, "Frame", frameMesh, frameMats, undo * -hinge, undo);
            frame.AddComponent<MeshCollider>().sharedMesh = frameMesh;
            frame.isStatic = true;

            var leaf = new GameObject("Leaf");
            leaf.transform.SetParent(doorT, false);
            var body = leaf.AddComponent<Rigidbody>();   // moved by script: lets it shove bodies aside
            body.isKinematic = true;
            body.useGravity = false;

            var leafGo = Part(leaf.transform, "LeafMesh", leafMesh, leafMats, Vector3.zero, undo);
            var box = leafGo.AddComponent<BoxCollider>();
            box.center = leafMesh.bounds.center;
            box.size = leafMesh.bounds.size;
            if (extra)
                Part(leaf.transform, extra.name, extra.GetComponent<MeshFilter>().sharedMesh, extra.GetComponent<MeshRenderer>().sharedMaterials,
                     undo * (extra.localPosition - hinge), undo * extra.localRotation);

            var handles = new Transform[2];
            foreach (var (i, p) in new[] { (0, handleA), (1, handleB) })
            {
                var h = new GameObject(i == 0 ? "Handle_A" : "Handle_B").transform;
                h.SetParent(leaf.transform, false);
                h.localPosition = p;
                handles[i] = h;
            }

            door = doorT.gameObject.AddComponent<Door>();
            door.leaf = leaf.transform;
            door.handles = handles;
            return root;
        }

        static GameObject Part(Transform parent, string name, Mesh mesh, Material[] mats, Vector3 localPosition, Quaternion localRotation)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localRotation = localRotation;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterials = mats;
            return go;
        }
    }
}
