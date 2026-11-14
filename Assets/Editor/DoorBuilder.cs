using System.IO;
using BodycamHorror.Environment;
using UnityEditor;
using UnityEngine;

namespace BodycamHorror.EditorTools
{
    /// <summary>
    /// Builds the metal door: a steel leaf (rim, stiffening ribs, rivets, a louvred vent, a kick plate)
    /// in a steel frame, lever handles on both sides. The meshes are generated here and textured with a
    /// scanned set (ambientCG, CC0). Two prefabs: MetalDoor, and MetalDoor_Locked with a hasp and a
    /// padlock on its front that has to be shot off (<see cref="DoorLock"/>).
    /// The prefab's own space: origin on the floor on the hinge line, the leaf runs along +X, its front
    /// (the padlock's side) faces +Z. It needs a hole in the wall <see cref="OpeningWidth"/> wide and
    /// <see cref="OpeningHeight"/> high. Menu: BodycamHorror > Build Metal Door Prefabs.
    /// </summary>
    public static class DoorBuilder
    {
        public const string PrefabPath = LightingTestSceneBuilder.PrefabDir + "/MetalDoor.prefab";
        public const string LockedPrefabPath = LightingTestSceneBuilder.PrefabDir + "/MetalDoor_Locked.prefab";
        const string MeshDir = LightingTestSceneBuilder.PrefabDir;

        public const float LeafWidth = 0.9f, LeafHeight = 2.05f, Thickness = 0.06f;
        const float FloorGap = 0.01f, HingeGap = 0.02f, LatchGap = 0.015f, TopGap = 0.015f, Jamb = 0.05f;
        const float HandleHeight = 1.02f, LockHeight = 1.32f;

        /// <summary>Hole in the wall the door and its frame fill (m).</summary>
        public const float OpeningWidth = Jamb + HingeGap + LeafWidth + LatchGap + Jamb;
        public const float OpeningHeight = FloorGap + LeafHeight + TopGap + Jamb;

        [MenuItem("BodycamHorror/Build Metal Door Prefabs")]
        public static void Build()
        {
            Directory.CreateDirectory(LightingTestSceneBuilder.PrefabDir);
            Directory.CreateDirectory(LightingTestSceneBuilder.MaterialDir);
            const string mats = LightingTestSceneBuilder.MaterialDir, tex = LightingTestSceneBuilder.TextureDir;
            Material metal = EnvironmentTextures.ScannedMaterial("Metal021", mats, tex, new Color(0.46f, 0.48f, 0.47f), 1.8f);
            metal.SetFloat("_Smoothness", 0.55f);   // old paint: no glare off it under the flashlight
            // the same metal, darker, for everything pressed in or turned away: grime sits in the low places, and
            // it is what keeps the door's shape readable when a light held at the eye flattens every shadow
            Material recess = DarkCopy(metal, mats + "/Metal021_Recess.mat", new Color(0.2f, 0.21f, 0.2f));   // packed without its metalness map: old paint over steel, not a mirror
            Material steel = LightingTestSceneBuilder.SimpleMaterial("DoorHandle", new Color(0.32f, 0.32f, 0.31f), 0.85f, 0.55f);
            Material brass = LightingTestSceneBuilder.SimpleMaterial("Padlock", new Color(0.5f, 0.38f, 0.16f), 0.9f, 0.5f);

            Mesh leaf = Save(LeafMesh(out Mesh leafRecess), MeshDir + "/MetalDoor_LeafMesh.asset");
            leafRecess = Save(leafRecess, MeshDir + "/MetalDoor_LeafRecessMesh.asset");
            Mesh hardware = Save(HardwareMesh(), MeshDir + "/MetalDoor_HardwareMesh.asset");
            Mesh frame = Save(FrameMesh(0.24f), MeshDir + "/MetalDoor_FrameMesh.asset");
            Mesh hasp = Save(HaspMesh(), MeshDir + "/MetalDoor_HaspMesh.asset");
            Mesh padlock = Save(PadlockMesh(), MeshDir + "/MetalDoor_PadlockMesh.asset");

            BuildPrefab(PrefabPath, false, leaf, leafRecess, recess, hardware, frame, hasp, padlock, metal, steel, brass);
            BuildPrefab(LockedPrefabPath, true, leaf, leafRecess, recess, hardware, frame, hasp, padlock, metal, steel, brass);
            AssetDatabase.SaveAssets();
            Debug.Log("Metal door prefabs built: " + PrefabPath + ", " + LockedPrefabPath);
        }

        /// <summary>Puts a door into a hole in a wall. <paramref name="openingStart"/>: the floor point at the
        /// hinge-side edge of the hole, in the middle of the wall's thickness; <paramref name="along"/>: along
        /// the wall towards the latch side. The front (padlock side) faces along x up.</summary>
        public static GameObject Place(Vector3 openingStart, Vector3 along, bool locked, Transform parent = null)
        {
            string path = locked ? LockedPrefabPath : PrefabPath;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (!prefab) { Build(); prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path); }
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            along = Vector3.ProjectOnPlane(along, Vector3.up).normalized;
            go.transform.SetPositionAndRotation(openingStart + along * (Jamb + HingeGap),
                                                Quaternion.LookRotation(Vector3.Cross(along, Vector3.up), Vector3.up));
            if (parent) go.transform.SetParent(parent, true);
            return go;
        }

        static void BuildPrefab(string path, bool locked, Mesh leafMesh, Mesh leafRecessMesh, Material recess, Mesh hardwareMesh, Mesh frameMesh, Mesh haspMesh, Mesh padlockMesh,
            Material metal, Material steel, Material brass)
        {
            var root = new GameObject(locked ? "MetalDoor_Locked" : "MetalDoor");

            // the frame stays put, and is solid: nobody squeezes past a door through its jamb
            GameObject frame = Part(root.transform, "Frame", frameMesh, metal, Vector3.zero);
            float x0 = -HingeGap - Jamb, x1 = LeafWidth + LatchGap + Jamb, half = 0.12f;
            AddBox(frame, new Vector3(x0, 0f, -half), new Vector3(-HingeGap, OpeningHeight, half));
            AddBox(frame, new Vector3(LeafWidth + LatchGap, 0f, -half), new Vector3(x1, OpeningHeight, half));
            AddBox(frame, new Vector3(x0, OpeningHeight - Jamb, -half), new Vector3(x1, OpeningHeight, half));

            var leaf = new GameObject("Leaf");
            leaf.transform.SetParent(root.transform, false);
            var body = leaf.AddComponent<Rigidbody>();   // moved by script: lets it shove bodies aside
            body.isKinematic = true;
            body.useGravity = false;

            GameObject panel = Part(leaf.transform, "Panel", leafMesh, metal, Vector3.zero);
            // one-sided shadows (the default): the leaf is a closed shape, and cast from both sides its far
            // face, pushed towards the lamp by the shadow bias, throws shadow onto the near one
            AddBox(panel, new Vector3(0f, FloorGap, -Thickness * 0.5f), new Vector3(LeafWidth, FloorGap + LeafHeight, Thickness * 0.5f));
            Part(leaf.transform, "Recess", leafRecessMesh, recess, Vector3.zero);
            Part(leaf.transform, "Hardware", hardwareMesh, steel, Vector3.zero);

            var handles = new Transform[2];
            for (int i = 0; i < 2; i++)
            {
                var handle = new GameObject(i == 0 ? "Handle_Front" : "Handle_Back").transform;
                handle.SetParent(leaf.transform, false);
                handle.localPosition = new Vector3(LeafWidth - 0.09f, HandleHeight, (i == 0 ? 1f : -1f) * (Thickness * 0.5f + 0.05f));
                handles[i] = handle;
            }

            var door = root.AddComponent<Door>();
            door.leaf = leaf.transform;
            door.handles = handles;
            door.openAngle = 100f;
            door.openTime = 1f;   // heavy, and the creak is a slow one
            // recordings (Assets/Audio/CREDITS.md); a missing one falls back to the generated sound
            door.openClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/Door/door_open.wav");
            door.closeClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/Door/door_close.wav");
            door.shutClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/Door/door_shut.wav");

            if (locked)
            {
                // on the front, above the handle, by the latch edge
                var lockRoot = new GameObject("Lock");
                lockRoot.transform.SetParent(leaf.transform, false);
                lockRoot.transform.localPosition = new Vector3(LeafWidth - 0.1f, LockHeight, Thickness * 0.5f);
                Part(lockRoot.transform, "Hasp", haspMesh, steel, Vector3.zero);
                GameObject padlock = Part(lockRoot.transform, "Padlock", padlockMesh, brass, new Vector3(0.03f, -0.012f, 0.022f));

                var target = lockRoot.AddComponent<BoxCollider>();   // a little generous: it is a small thing to hit
                target.center = new Vector3(0.03f, -0.045f, 0.022f);
                target.size = new Vector3(0.1f, 0.13f, 0.06f);
                var doorLock = lockRoot.AddComponent<DoorLock>();
                doorLock.door = door;
                doorLock.padlock = padlock.transform;
                door.locked = true;
                door.enemiesCanOpen = false;
            }

            PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
        }

        static Material DarkCopy(Material source, string path, Color tint)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!material)
            {
                material = new Material(source);
                AssetDatabase.CreateAsset(material, path);
            }
            material.CopyPropertiesFromMaterial(source);
            material.SetColor("_BaseColor", tint);
            EditorUtility.SetDirty(material);
            return material;
        }

        static void AddBox(GameObject go, Vector3 min, Vector3 max)
        {
            var box = go.AddComponent<BoxCollider>();
            box.center = (min + max) * 0.5f;
            box.size = max - min;
        }

        static GameObject Part(Transform parent, string name, Mesh mesh, Material material, Vector3 localPosition)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = material;
            return go;
        }

        /// <summary>Writes the mesh over the asset already there, so the prefabs keep pointing at it.</summary>
        static Mesh Save(Mesh mesh, string path)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (!existing)
            {
                AssetDatabase.CreateAsset(mesh, path);
                return mesh;
            }
            string name = existing.name;
            EditorUtility.CopySerialized(mesh, existing);
            existing.name = name;
            EditorUtility.SetDirty(existing);
            Object.DestroyImmediate(mesh);
            return existing;
        }

        // ------------------------------------------------------------------ the shapes

        /// <summary>The steel leaf: a full-thickness rim and two ribs across, and between them three panels pressed
        /// in with sloped walls; domed rivets round the rim, a louvred vent, a raised kick plate. The same on both
        /// faces. Everything that stands out or sinks in does so through a slope: a flashlight is held next to
        /// the eye, so square steps vanish under it, while a slope always shades differently from the flat.</summary>
        static Mesh LeafMesh(out Mesh recessed)
        {
            MeshShape m = new MeshShape(), d = new MeshShape();   // what stands out; what is pressed in or turned away
            float w = LeafWidth, y0 = FloorGap, y1 = FloorGap + LeafHeight;
            const float slab = 0.006f, proud = Thickness * 0.5f, rim = 0.07f, rib = 0.03f, slope = 0.022f;
            float[] ribs = { 0.74f, 1.4f };

            d.Box(new Vector3(rim, y0 + rim, -slab), new Vector3(w - rim, y1 - rim, slab));
            for (int s = -1; s <= 1; s += 2)
            {
                Vector3 facing = Vector3.forward * s;
                float zFloor = s * slab, zTop = s * proud;
                float za = Mathf.Min(zFloor, zTop), zb = Mathf.Max(zFloor, zTop);

                // rim and ribs, the full thickness of the leaf
                if (s > 0)   // the rim is one solid frame, both faces at once
                {
                    m.Box(new Vector3(0f, y0, -proud), new Vector3(rim, y1, proud));
                    m.Box(new Vector3(w - rim, y0, -proud), new Vector3(w, y1, proud));
                    m.Box(new Vector3(rim, y0, -proud), new Vector3(w - rim, y0 + rim, proud));
                    m.Box(new Vector3(rim, y1 - rim, -proud), new Vector3(w - rim, y1, proud));
                }
                foreach (float at in ribs) m.Box(new Vector3(rim, at - rib, za), new Vector3(w - rim, at + rib, zb));

                // the three panels between them: sloped walls down to the floor of each
                float[] edges = { y0 + rim, ribs[0] - rib, ribs[0] + rib, ribs[1] - rib, ribs[1] + rib, y1 - rim };
                for (int i = 0; i < edges.Length; i += 2)
                    Slopes(d, rim, edges[i], w - rim, edges[i + 1], zTop, slope, zFloor, facing);

                // kick plate on the bottom panel, vent on the top one: raised, with sloped edges
                Raised(m, d, rim + 0.05f, edges[0] + 0.045f, w - rim - 0.05f, 0.42f, zFloor, s * (slab + 0.008f), 0.012f, facing);
                float vx0 = w * 0.5f - 0.18f, vx1 = w * 0.5f + 0.18f, vy0 = 1.56f, vy1 = 1.84f, zVent = s * (slab + 0.007f);
                Raised(d, d, vx0, vy0, vx1, vy1, zFloor, zVent, 0.01f, facing);
                // louvres: each slat leans out at the bottom, and its underside is in its own shade
                for (int i = 0; i < 5; i++)
                {
                    float top = vy1 - 0.035f - i * 0.045f, bottom = top - 0.034f, zOut = zVent + s * 0.014f;
                    Vector3 a0 = new Vector3(vx0 + 0.03f, top, zVent), a1 = new Vector3(vx1 - 0.03f, top, zVent);
                    Vector3 b0 = new Vector3(vx0 + 0.03f, bottom, zOut), b1 = new Vector3(vx1 - 0.03f, bottom, zOut);
                    Vector3 c0 = new Vector3(vx0 + 0.03f, bottom, zVent), c1 = new Vector3(vx1 - 0.03f, bottom, zVent);
                    m.Quad(a0, a1, b1, b0, facing + Vector3.up);
                    d.Quad(b0, b1, c1, c0, Vector3.down);
                    d.Quad(a0, b0, c0, c0, Vector3.left);
                    d.Quad(a1, b1, c1, c1, Vector3.right);
                }

                // rivets round the rim
                Vector3 face = Vector3.forward * zTop, head = facing * 0.008f;
                int up = Mathf.RoundToInt((LeafHeight - rim) / 0.19f);
                for (int i = 0; i <= up; i++)
                {
                    float y = y0 + rim * 0.5f + (LeafHeight - rim) * i / up;
                    Rivet(m, new Vector3(rim * 0.5f, y, 0f) + face, head);
                    Rivet(m, new Vector3(w - rim * 0.5f, y, 0f) + face, head);
                }
                const int across = 4;
                for (int i = 1; i < across; i++)
                {
                    float x = rim * 0.5f + (w - rim) * i / across;
                    Rivet(m, new Vector3(x, y0 + rim * 0.5f, 0f) + face, head);
                    Rivet(m, new Vector3(x, y1 - rim * 0.5f, 0f) + face, head);
                }
                // and a row along each rib
                foreach (float at in ribs)
                    for (int i = 1; i < across; i++)
                        Rivet(m, new Vector3(rim * 0.5f + (w - rim) * i / across, at, 0f) + face, head);
            }
            recessed = d.Build("MetalDoor_LeafRecessMesh");
            return m.Build("MetalDoor_LeafMesh");
        }

        /// <summary>Four sloped walls from the rectangle x0..x1, y0..y1 at depth zEdge to the same rectangle drawn in
        /// by <paramref name="inset"/> at depth zInner: the sides of a pressed-in panel, or of a raised plate.</summary>
        static void Slopes(MeshShape m, float x0, float y0, float x1, float y1, float zEdge, float inset, float zInner, Vector3 facing)
        {
            Vector3 a0 = new Vector3(x0, y0, zEdge), a1 = new Vector3(x1, y0, zEdge), a2 = new Vector3(x1, y1, zEdge), a3 = new Vector3(x0, y1, zEdge);
            Vector3 b0 = new Vector3(x0 + inset, y0 + inset, zInner), b1 = new Vector3(x1 - inset, y0 + inset, zInner);
            Vector3 b2 = new Vector3(x1 - inset, y1 - inset, zInner), b3 = new Vector3(x0 + inset, y1 - inset, zInner);
            m.Quad(a0, a1, b1, b0, facing);
            m.Quad(a1, a2, b2, b1, facing);
            m.Quad(a2, a3, b3, b2, facing);
            m.Quad(a3, a0, b0, b3, facing);
        }

        /// <summary>A plate standing out of the surface at zBase up to zTop, its edges sloped (and in the darker metal).</summary>
        static void Raised(MeshShape top, MeshShape sides, float x0, float y0, float x1, float y1, float zBase, float zTop, float inset, Vector3 facing)
        {
            Slopes(sides, x0, y0, x1, y1, zBase, inset, zTop, facing);
            top.Quad(new Vector3(x0 + inset, y0 + inset, zTop), new Vector3(x1 - inset, y0 + inset, zTop),
                   new Vector3(x1 - inset, y1 - inset, zTop), new Vector3(x0 + inset, y1 - inset, zTop), facing);
        }

        static void Rivet(MeshShape m, Vector3 at, Vector3 head) => m.Stud(at, at + head, 0.017f, 0.008f, 10);

        /// <summary>Hinge barrels down the hinge edge and a lever handle on each face.</summary>
        static Mesh HardwareMesh()
        {
            var m = new MeshShape();
            float face = Thickness * 0.5f;

            foreach (float at in new[] { 0.26f, 1.03f, 1.8f })
            {
                m.Cylinder(new Vector3(-0.008f, at - 0.06f, face + 0.004f), new Vector3(-0.008f, at + 0.06f, face + 0.004f), 0.011f, 10);
                m.Box(new Vector3(0f, at - 0.05f, face), new Vector3(0.055f, at + 0.05f, face + 0.004f));
            }

            for (int s = -1; s <= 1; s += 2)
            {
                float x = LeafWidth - 0.09f, y = HandleHeight;
                float z0 = s * face, plate = s * (face + 0.005f), lever = s * (face + 0.05f);
                m.Box(new Vector3(x - 0.022f, y - 0.1f, Mathf.Min(z0, plate)), new Vector3(x + 0.022f, y + 0.06f, Mathf.Max(z0, plate)));
                m.Cylinder(new Vector3(x, y, plate), new Vector3(x, y, lever + s * 0.008f), 0.01f, 10);
                m.Box(new Vector3(x - 0.12f, y - 0.009f, Mathf.Min(lever - 0.008f, lever + 0.008f)), new Vector3(x + 0.012f, y + 0.009f, Mathf.Max(lever - 0.008f, lever + 0.008f)));
                // keyhole boss under the lever
                m.Cylinder(new Vector3(x, y - 0.065f, plate), new Vector3(x, y - 0.065f, plate + s * 0.004f), 0.012f, 10);
            }
            return m.Build("MetalDoor_HardwareMesh");
        }

        /// <summary>The steel frame: two jambs and a head, as deep as the wall and a little proud of it.</summary>
        static Mesh FrameMesh(float depth)
        {
            var m = new MeshShape();
            float x0 = -HingeGap - Jamb, x1 = LeafWidth + LatchGap + Jamb, half = depth * 0.5f;
            m.Box(new Vector3(x0, 0f, -half), new Vector3(-HingeGap, OpeningHeight - Jamb, half));
            m.Box(new Vector3(LeafWidth + LatchGap, 0f, -half), new Vector3(x1, OpeningHeight - Jamb, half));
            m.Box(new Vector3(x0, OpeningHeight - Jamb, -half), new Vector3(x1, OpeningHeight, half));
            // the stop the leaf shuts against, on the back
            float back = -Thickness * 0.5f;
            m.Box(new Vector3(LeafWidth - 0.004f, 0f, back - 0.02f), new Vector3(LeafWidth + LatchGap, OpeningHeight - Jamb, back));
            return m.Build("MetalDoor_FrameMesh");
        }

        /// <summary>The hasp: a plate bolted to the leaf with the staple the padlock hangs through. Origin on the leaf's face.</summary>
        static Mesh HaspMesh()
        {
            var m = new MeshShape();
            m.Box(new Vector3(-0.07f, -0.03f, 0f), new Vector3(0.06f, 0.03f, 0.005f));
            m.Box(new Vector3(-0.07f, -0.022f, 0.005f), new Vector3(0.05f, 0.022f, 0.009f));   // the hinged strap
            foreach (float x in new[] { -0.055f, -0.025f })
                m.Cylinder(new Vector3(x, 0f, 0.009f), new Vector3(x, 0f, 0.013f), 0.007f, 8);
            m.Torus(new Vector3(0.03f, 0f, 0.012f), Vector3.right, 0.011f, 0.0035f, 16, 8);    // staple
            return m.Build("MetalDoor_HaspMesh");
        }

        /// <summary>The padlock: its shackle's top is at the origin, the body hangs below.</summary>
        static Mesh PadlockMesh()
        {
            var m = new MeshShape();
            m.Torus(new Vector3(0f, -0.016f, 0f), Vector3.forward, 0.016f, 0.0042f, 20, 8);
            m.Box(new Vector3(-0.027f, -0.075f, -0.012f), new Vector3(0.027f, -0.026f, 0.012f));
            m.Cylinder(new Vector3(0f, -0.058f, 0.012f), new Vector3(0f, -0.058f, 0.0145f), 0.007f, 10);   // keyway
            return m.Build("MetalDoor_PadlockMesh");
        }
    }
}
