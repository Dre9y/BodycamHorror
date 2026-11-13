using System.Collections.Generic;
using System.IO;
using BodycamHorror.Environment;
using UnityEditor;
using UnityEngine;

namespace BodycamHorror.EditorTools
{
    /// <summary>
    /// Builds the pipe with a hand-wheel valve: the meshes are generated here (a run of pipe with
    /// flanges, bolts, a valve body and wall brackets; a spoked wheel), saved next to the other
    /// environment prefabs and put together as PipeValve.prefab with a <see cref="Valve"/> on it.
    /// The prefab's own space: the pipe runs along X, the wheel faces +Z, the wall is 0.2 m behind (-Z).
    /// Menu: BodycamHorror > Build Pipe Valve Prefab. Running it again rebuilds the same assets in place.
    /// </summary>
    public static class ValveBuilder
    {
        public const string PrefabPath = LightingTestSceneBuilder.PrefabDir + "/PipeValve.prefab";
        const string PipeMeshPath = LightingTestSceneBuilder.PrefabDir + "/PipeValve_PipeMesh.asset";
        const string WheelMeshPath = LightingTestSceneBuilder.PrefabDir + "/PipeValve_WheelMesh.asset";
        const string TurnClipPath = "Assets/Audio/Valve/valve_turn.wav";
        const string ClosedClipPath = "Assets/Audio/Ambience/Prepared/knock_pipe_concrete.wav";
        const string HissClipPath = "Assets/Audio/Valve/steam_hiss.wav";
        const string SteamMaterialPath = LightingTestSceneBuilder.MaterialDir + "/Steam.mat";

        /// <summary>Where the pipe's bolted joints are, along the pipe from the valve (m): steam leaks from one of them.</summary>
        public const float JointOffset = 1.15f;
        /// <summary>Radius of a joint's flange (m): a leak starts at its rim.</summary>
        public const float JointRadius = 0.155f;

        /// <summary>Distance from the pipe's centre line to the wall it is bracketed to (m).</summary>
        public const float WallOffset = 0.2f;

        const float PipeRadius = 0.09f, PipeHalfLength = 1.6f, WheelZ = 0.33f, WheelRadius = 0.17f;

        [MenuItem("BodycamHorror/Build Pipe Valve Prefab")]
        public static GameObject Build()
        {
            Directory.CreateDirectory(LightingTestSceneBuilder.PrefabDir);
            Directory.CreateDirectory(LightingTestSceneBuilder.MaterialDir);
            // scanned sets from ambientCG (CC0): old green paint flaking to rust, bare iron rusted through
            const string mats = LightingTestSceneBuilder.MaterialDir, tex = LightingTestSceneBuilder.TextureDir;
            Material paint = EnvironmentTextures.ScannedMaterial("PaintedMetal006", mats, tex, new Color(0.34f, 0.36f, 0.31f), 1.6f);
            Material red = EnvironmentTextures.ScannedMaterial("Rust002", mats, tex, new Color(0.95f, 0.62f, 0.55f), 1.6f);
            red.SetTextureScale("_BaseMap", new Vector2(2.5f, 2.5f));   // the wheel is small: bring the rust down to its size
            foreach (string old in new[] { "PipePaint", "ValveWheel" }) AssetDatabase.DeleteAsset(mats + "/" + old + ".mat");

            Mesh pipeMesh = Save(PipeMesh(), PipeMeshPath);
            Mesh wheelMesh = Save(WheelMesh(), WheelMeshPath);

            var root = new GameObject("PipeValve");
            Part(root.transform, "Pipe", pipeMesh, paint, Vector3.zero);
            GameObject wheel = Part(root.transform, "Wheel", wheelMesh, red, new Vector3(0f, 0f, WheelZ));

            var run = root.AddComponent<CapsuleCollider>();
            run.direction = 0;   // along X
            run.radius = PipeRadius + 0.01f;
            run.height = PipeHalfLength * 2f;
            var body = root.AddComponent<BoxCollider>();   // valve body, stem and wheel: something to look at and to hit
            body.center = new Vector3(0f, 0f, WheelZ * 0.5f + 0.02f);
            body.size = new Vector3(WheelRadius * 2f + 0.04f, WheelRadius * 2f + 0.04f, WheelZ + 0.04f);

            var valve = root.AddComponent<Valve>();
            valve.wheel = wheel.transform;
            valve.turnClip = AssetDatabase.LoadAssetAtPath<AudioClip>(TurnClipPath);
            valve.closedClip = AssetDatabase.LoadAssetAtPath<AudioClip>(ClosedClipPath);

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            AssetDatabase.SaveAssets();
            Debug.Log("Pipe valve prefab built: " + PrefabPath);
            return prefab;
        }

        /// <summary>Puts the pipe on a wall: <paramref name="wallPoint"/> is the spot on the wall behind the valve,
        /// <paramref name="wallNormal"/> points out of the wall into the room. The pipe runs level along the wall.</summary>
        public static GameObject Place(Vector3 wallPoint, Vector3 wallNormal, Transform parent = null)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (!prefab) prefab = Build();
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            wallNormal = Vector3.ProjectOnPlane(wallNormal, Vector3.up).normalized;
            go.transform.SetPositionAndRotation(wallPoint + wallNormal * WallOffset, Quaternion.LookRotation(wallNormal, Vector3.up));
            if (parent) go.transform.SetParent(parent, true);
            return go;
        }

        /// <summary>A jet of scalding steam from the pipe's joint on the given side of the valve (-1 or 1 along
        /// the pipe), blowing out into the room. Shutting the valve stops it (SteamLeak).</summary>
        public static GameObject PlaceSteam(GameObject pipe, int side = -1, Transform parent = null)
        {
            var go = new GameObject("SteamLeak");
            Transform t = pipe.transform;
            go.transform.SetPositionAndRotation(t.TransformPoint(new Vector3(Mathf.Sign(side) * JointOffset, 0.03f, JointRadius - 0.02f)),
                                                Quaternion.LookRotation(t.forward, Vector3.up));
            if (parent) go.transform.SetParent(parent, true);
            var leak = go.AddComponent<SteamLeak>();
            leak.valve = pipe.GetComponent<Valve>();
            leak.material = SteamMaterial();
            leak.hissClip = AssetDatabase.LoadAssetAtPath<AudioClip>(HissClipPath);
            return go;
        }

        static Material SteamMaterial()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(SteamMaterialPath);
            if (!material)
            {
                material = new Material(Shader.Find("BodycamHorror/Steam"));
                AssetDatabase.CreateAsset(material, SteamMaterialPath);
            }
            return material;
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

        /// <summary>Writes the mesh over the asset already there, so the prefab keeps pointing at it.</summary>
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

        static Mesh PipeMesh()
        {
            var m = new Shape();
            Vector3 x = Vector3.right, z = Vector3.forward;

            // the run of pipe, and a fatter valve body in the middle of it
            m.Cylinder(-x * PipeHalfLength, x * PipeHalfLength, PipeRadius, 28);
            m.Cylinder(-x * 0.2f, x * 0.2f, 0.125f, 28);

            // flanges: a bolted pair either side of the valve, a single joint further out
            foreach (float at in new[] { -0.23f, 0.23f, -1.15f, 1.15f })
            {
                bool joint = Mathf.Abs(at) > 1f;
                float half = joint ? 0.035f : 0.02f;
                m.Cylinder(x * (at - half), x * (at + half), 0.155f, 28);
                for (int i = 0; i < 6; i++)
                {
                    float a = (i + 0.5f) * Mathf.PI / 3f;
                    Vector3 at2 = x * at + new Vector3(0f, Mathf.Cos(a), Mathf.Sin(a)) * 0.125f;
                    m.Cylinder(at2 - x * (half + 0.02f), at2 + x * (half + 0.02f), 0.016f, 6);
                }
            }

            // bonnet towards the viewer, its bolted cap, and the stem the wheel sits on
            m.Cylinder(Vector3.zero, z * 0.2f, 0.075f, 22);
            m.Cylinder(z * 0.19f, z * 0.22f, 0.105f, 22);
            for (int i = 0; i < 4; i++)
            {
                float a = (i + 0.5f) * Mathf.PI / 2f;
                Vector3 at = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * 0.082f;
                m.Cylinder(at + z * 0.215f, at + z * 0.24f, 0.014f, 6);
            }
            m.Cylinder(z * 0.22f, z * 0.255f, 0.034f, 14);
            m.Cylinder(z * 0.25f, z * (WheelZ + 0.01f), 0.016f, 12);

            // brackets holding it off the wall
            foreach (float at in new[] { -1.4f, 1.4f })
            {
                m.Cylinder(x * (at - 0.025f), x * (at + 0.025f), PipeRadius + 0.012f, 24);   // clamp band
                m.Cylinder(x * at - z * PipeRadius, x * at - z * (WallOffset - 0.012f), 0.026f, 10);
                m.Cylinder(x * at - z * (WallOffset - 0.014f), x * at - z * WallOffset, 0.075f, 16);
            }
            return m.Build("PipeValve_PipeMesh");
        }

        /// <summary>The hand wheel, turning about Z, centred on its own origin.</summary>
        static Mesh WheelMesh()
        {
            var m = new Shape();
            Vector3 z = Vector3.forward;
            m.Torus(Vector3.zero, z, WheelRadius, 0.017f, 40, 12);
            m.Cylinder(-z * 0.024f, z * 0.024f, 0.042f, 18);   // hub
            m.Cylinder(z * 0.024f, z * 0.042f, 0.024f, 6);     // nut
            for (int i = 0; i < 4; i++)
            {
                float a = i * Mathf.PI / 2f + Mathf.PI / 4f;
                var dir = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
                m.Cylinder(dir * 0.036f, dir * WheelRadius, 0.0125f, 10, false);
            }
            return m.Build("PipeValve_WheelMesh");
        }

        /// <summary>Collects cylinders and rings into one smooth-shaded mesh.</summary>
        sealed class Shape
        {
            readonly List<Vector3> vertices = new List<Vector3>(), normals = new List<Vector3>();
            readonly List<Vector2> uvs = new List<Vector2>();
            readonly List<int> triangles = new List<int>();

            static void Basis(Vector3 axis, out Vector3 u, out Vector3 w)
            {
                u = Vector3.Cross(axis, Mathf.Abs(axis.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
                w = Vector3.Cross(axis, u);
            }

            int Add(Vector3 p, Vector3 n, Vector2 uv)
            {
                vertices.Add(p); normals.Add(n); uvs.Add(uv);
                return vertices.Count - 1;
            }

            public void Cylinder(Vector3 a, Vector3 b, float radius, int segments, bool caps = true)
            {
                Vector3 axis = (b - a).normalized;
                Basis(axis, out Vector3 u, out Vector3 w);
                float length = Vector3.Distance(a, b);

                int side = vertices.Count;
                for (int i = 0; i <= segments; i++)
                {
                    float t = i / (float)segments, ang = t * Mathf.PI * 2f;
                    Vector3 n = u * Mathf.Cos(ang) + w * Mathf.Sin(ang);
                    Add(a + n * radius, n, new Vector2(t * radius * 6.28f, 0f));
                    Add(b + n * radius, n, new Vector2(t * radius * 6.28f, length));
                }
                for (int i = 0; i < segments; i++)
                {
                    int a0 = side + i * 2, b0 = a0 + 1, a1 = a0 + 2, b1 = a0 + 3;
                    triangles.Add(a0); triangles.Add(a1); triangles.Add(b0);
                    triangles.Add(a1); triangles.Add(b1); triangles.Add(b0);
                }
                if (!caps) return;

                for (int end = 0; end < 2; end++)
                {
                    Vector3 centre = end == 0 ? a : b, n = end == 0 ? -axis : axis;
                    int c = Add(centre, n, new Vector2(0.5f, 0.5f));
                    int ring = vertices.Count;
                    for (int i = 0; i <= segments; i++)
                    {
                        float ang = i / (float)segments * Mathf.PI * 2f;
                        Vector3 r = u * Mathf.Cos(ang) + w * Mathf.Sin(ang);
                        Add(centre + r * radius, n, new Vector2(0.5f + Mathf.Cos(ang) * 0.5f, 0.5f + Mathf.Sin(ang) * 0.5f));
                    }
                    for (int i = 0; i < segments; i++)
                    {
                        triangles.Add(c);
                        triangles.Add(end == 0 ? ring + i + 1 : ring + i);
                        triangles.Add(end == 0 ? ring + i : ring + i + 1);
                    }
                }
            }

            public void Torus(Vector3 centre, Vector3 axis, float radius, float tube, int segments, int sides)
            {
                axis.Normalize();
                Basis(axis, out Vector3 u, out Vector3 w);
                int first = vertices.Count;
                for (int i = 0; i <= segments; i++)
                {
                    float phi = i / (float)segments * Mathf.PI * 2f;
                    Vector3 e = u * Mathf.Cos(phi) + w * Mathf.Sin(phi);
                    for (int j = 0; j <= sides; j++)
                    {
                        float psi = j / (float)sides * Mathf.PI * 2f;
                        Vector3 n = e * Mathf.Cos(psi) + axis * Mathf.Sin(psi);
                        Add(centre + e * radius + n * tube, n, new Vector2(i / (float)segments * radius * 6.28f, j / (float)sides * tube * 6.28f));
                    }
                }
                int row = sides + 1;
                for (int i = 0; i < segments; i++)
                    for (int j = 0; j < sides; j++)
                    {
                        int p00 = first + i * row + j, p10 = p00 + row, p01 = p00 + 1, p11 = p10 + 1;
                        triangles.Add(p00); triangles.Add(p10); triangles.Add(p01);
                        triangles.Add(p10); triangles.Add(p11); triangles.Add(p01);
                    }
            }

            public Mesh Build(string name)
            {
                var mesh = new Mesh { name = name };
                mesh.SetVertices(vertices);
                mesh.SetNormals(normals);
                mesh.SetUVs(0, uvs);
                mesh.SetTriangles(triangles, 0);
                mesh.RecalculateBounds();
                mesh.RecalculateTangents();
                return mesh;
            }
        }
    }
}
