using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace BodycamHorror.EditorTools
{
    /// <summary>
    /// Brings in the basement kit made in Blender (Blender/tools/basement_*.py) after the cellar concept: tiling
    /// walls / floor / ceiling on Poly Haven scans (Assets/Generated/Textures/Basement), pipes and valves, and the
    /// interior props, each baked to its own maps (Assets/Generated/Textures/Basement/Props).
    /// Menu: BodycamHorror > Basement > Set Up Kit (materials + prefabs in Assets/Prefabs/Basement). Rerunning writes over them.
    /// </summary>
    public static class BasementSetup
    {
        const string ModelDir = "Assets/Generated/Imported/Basement";
        const string TilingDir = "Assets/Generated/Textures/Basement";
        const string PropTexDir = "Assets/Generated/Textures/Basement/Props";
        const string MaterialDir = "Assets/Generated/Materials/Basement";
        const string PrefabDir = "Assets/Prefabs/Basement";

        static readonly string[] Tiling =
        {
            "Basement_BrickPlaster", "Basement_DamagedPlaster", "Basement_Brick",
            "Basement_FloorConcrete", "Basement_FloorDamaged", "Basement_Ceiling", "Basement_MetalPaint", "Basement_Rust",
        };

        static readonly string[] Architecture =
        {
            "Wall_1m", "Wall_2m", "Wall_4m", "Wall_Door_2m", "Wall_Window_2m", "Wall_2m_Plaster", "Wall_4m_Plaster",
            "Floor_4x4", "Floor_4x4_Damaged", "Ceiling_4x4", "Ceiling_Beam_4m", "Column", "Wall_Corner",
        };

        /// <summary>Baked props: (name, split into one prefab per mesh, collider).</summary>
        static readonly (string name, bool split, bool collide)[] Props =
        {
            ("Pipe_Large_2m", false, true), ("Pipe_Large_Elbow", false, true), ("Pipe_Large_Valve", false, true),
            ("Pipe_Small_2m", false, false), ("Pipe_Small_Elbow", false, false), ("Pipe_Small_Valve", false, false),
            ("Pipe_Brackets", true, false),
            ("Barrel", false, true), ("Barrel_Open", false, true), ("Shelf_Wood", false, true), ("Shelf_Metal", false, true),
            ("Cabinet_Metal", false, true), ("Crate", false, true), ("Crate_Open", false, true), ("Pallet", false, true),
            ("Radiator_Basement", false, true), ("Bucket", false, true), ("PaintCans", true, true), ("Fusebox", false, true),
            ("Lamp_Caged", false, false), ("Door_Bunker", false, true), ("Window_Basement", false, false),
            ("GasCylinder", false, true), ("Chair_Broken", false, true), ("Workbench", false, true), ("Tyre", false, true),
            ("CementBags", false, true), ("Planks_Pile", false, true), ("Bricks", true, true), ("Mattress", false, true),
            ("Trash_Set", true, false),
            ("Gate_Grille", false, false), ("Floor_Drain", false, false),
            // the light fear level's working rooms (Blender/tools/rooms_props.py)
            ("Pump_Unit", false, true), ("Butcher_Table", false, true), ("Meat_Hooks", false, false), ("Blood_Pools", true, false),
            ("Severed_Head", false, false), ("Transformer", false, true), ("Cable_Tray_2m", false, false), ("Cable_Drop", false, false),
            ("Breaker_Panel", false, true), ("Beacon_Lamp", false, false),
            // the emergency power crank and its fuse (Blender/tools/emergency_power.py); EmergencyPowerBuilder puts them together
            ("EmergencyCrank", false, false), ("EmergencyCrank_Handle", false, false), ("Fuse", false, false), ("FuseHolder", false, false),
        };

        /// <summary>Materials and prefabs for just these props (names from <see cref="Props"/>).</summary>
        public static void SetUpProps(params string[] names)
        {
            Directory.CreateDirectory(MaterialDir);
            Directory.CreateDirectory(PrefabDir);
            AssetDatabase.Refresh();
            foreach (var (name, split, collide) in Props)
                if (System.Array.IndexOf(names, name) >= 0) MakeProp(name, split, collide);
            AssetDatabase.SaveAssets();
        }

        /// <summary>Only the working rooms' props (rerunning the whole kit takes a while).</summary>
        [MenuItem("BodycamHorror/Basement/Set Up Room Props")]
        public static void SetUpRoomProps()
        {
            Directory.CreateDirectory(MaterialDir);
            Directory.CreateDirectory(PrefabDir);
            AssetDatabase.Refresh();
            foreach (var (name, split, collide) in Props)
                if (System.Array.IndexOf(RoomProps, name) >= 0) MakeProp(name, split, collide);
            AssetDatabase.SaveAssets();
        }

        static readonly string[] RoomProps =
            { "Pump_Unit", "Butcher_Table", "Meat_Hooks", "Blood_Pools", "Severed_Head", "Transformer", "Cable_Tray_2m", "Cable_Drop", "Breaker_Panel", "Beacon_Lamp" };

        /// <summary>Prefab names for the pieces of the split sets (the mesh names are short and clash: two sets have a CanA).</summary>
        static string PieceName(string set, string mesh)
        {
            switch (set)
            {
                case "Pipe_Brackets": return "Pipe_Bracket_" + mesh;
                case "PaintCans": return "PaintCan_" + mesh.Substring(3);
                case "Bricks": return mesh == "Pile" ? "Brick_Pile" : mesh;
                case "Trash_Set":
                    switch (mesh)
                    {
                        case "CanA": return "Trash_TinCan_A";
                        case "CanB": return "Trash_TinCan_B";
                        case "Card": return "Trash_Cardboard";
                        case "Debris": return "Trash_Debris";
                        default: return "Trash_" + mesh;
                    }
            }
            return mesh;
        }

        static readonly Color BulbColor = new Color(1f, 0.72f, 0.42f);

        [MenuItem("BodycamHorror/Basement/Set Up Kit")]
        public static void SetUpKit()
        {
            Directory.CreateDirectory(MaterialDir);
            Directory.CreateDirectory(PrefabDir);
            AssetDatabase.Refresh();
            foreach (string t in Tiling) TilingMaterial(t);
            TessellatedMaterials();
            foreach (string a in Architecture) MakeArchitecture(a);
            foreach (var (name, split, collide) in Props) MakeProp(name, split, collide);
            AssetDatabase.SaveAssets();
            Debug.Log("Basement kit set up in " + PrefabDir);
        }

        // ------------------------------------------------------------------ materials

        static Material TilingMaterial(string name)
        {
            Texture2D baseMap = Tex($"{TilingDir}/{name}_BaseColor.jpg", true, false, 2048);
            Texture2D normal = Tex($"{TilingDir}/{name}_Normal.jpg", false, true, 2048);
            Texture2D ms = Tex($"{TilingDir}/{name}_MetallicSmoothness.png", false, false, 2048);
            var mat = LitMaterial($"{MaterialDir}/{name}.mat");
            mat.SetTexture("_BaseMap", baseMap);
            // the metal scan is pale paint over rust: darkened to old grey-green steel
            mat.SetColor("_BaseColor", name == "Basement_MetalPaint" ? new Color(0.3f, 0.33f, 0.31f) : Color.white);
            mat.SetTexture("_BumpMap", normal);
            mat.SetFloat("_BumpScale", 1f);
            mat.EnableKeyword("_NORMALMAP");
            mat.SetTexture("_MetallicGlossMap", ms);
            mat.EnableKeyword("_METALLICSPECGLOSSMAP");
            mat.SetFloat("_Smoothness", 0.85f);
            mat.SetTexture("_OcclusionMap", ms);           // G holds the scan's AO
            mat.EnableKeyword("_OCCLUSIONMAP");
            mat.SetFloat("_OcclusionStrength", 1f);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        const string TessShader = "BodycamHorror/Tessellated Surface";

        /// <summary>
        /// The walls' and floor's sets again on BodycamHorror/Tessellated Surface (relief pushed out of the scans' height
        /// maps near the camera): *_Tess.mat beside the plain ones. The floor's is two layers, the broken concrete with
        /// rust eating through it, grime over both, and water standing in the low spots where Basement_FloorBlend's blue says.
        /// </summary>
        static void TessellatedMaterials()
        {
            var shader = Shader.Find(TessShader);
            if (!shader) { Debug.LogWarning("Missing shader " + TessShader); return; }
            // levels measured off the scans (linear albedo, smoothness): the lower walls' brick-and-plaster scan comes in
            // glossy (0.48) and the plaster bright (0.24); old basement brick wants ~0.1-0.2 smoothness and ~0.1 albedo
            Material Wall(string set, float displacement, float damp, Color tint, float smoothness)
            {
                var m = TessMaterial(set, shader);
                m.SetColor("_BaseColor", tint);
                m.SetFloat("_Smoothness", smoothness);
                m.SetFloat("_Displacement", displacement);
                m.SetFloat("_HeightBias", 0f);
                m.SetFloat("_UseVertexWeight", 1f);   // the slabs' borders stay put
                m.SetFloat("_DampHeight", damp);
                m.SetFloat("_DampDarken", 0.45f);
                return m;
            }
            Wall("Basement_BrickPlaster", 0.03f, 1.0f, new Color(0.72f, 0.7f, 0.68f), 0.35f);    // 0.152 -> 0.11 albedo, 0.48 -> 0.17 smooth
            Wall("Basement_DamagedPlaster", 0.02f, 0f, new Color(0.6f, 0.58f, 0.55f), 1f);       // 0.244 -> 0.14
            Wall("Basement_Brick", 0.025f, 1.0f, new Color(0.68f, 0.66f, 0.64f), 1f);            // 0.164 -> 0.11

            // the ceiling and the concrete trims: only the broken-up tiling, no relief (and no extra triangles)
            var ceiling = TessMaterial("Basement_Ceiling", shader);
            ceiling.SetFloat("_Displacement", 0f);
            ceiling.SetFloat("_TessMax", 1f);
            ceiling.SetFloat("_UseVertexWeight", 0f);
            ceiling.SetFloat("_DampHeight", 0f);
            ceiling.SetFloat("_Smoothness", 0.55f);   // 0.23 -> 0.13: dry concrete
            EditorUtility.SetDirty(ceiling);

            var floor = TessMaterial("Basement_FloorDamaged", shader, "Basement_Floor_Tess");
            floor.SetTextureScale("_BaseMap", Vector2.one / 4.5f);   // the floor's UVs are in metres
            floor.SetColor("_BaseColor", new Color(2.6f, 2.5f, 2.35f));   // the scan is dark (0.049): up to ~0.12, level with the walls
            floor.SetFloat("_Smoothness", 1f);
            floor.SetFloat("_Displacement", 0.06f);
            floor.SetFloat("_HeightBias", 1f);        // all of it down from the floor's top: nothing stands in the air
            floor.SetFloat("_UseVertexWeight", 0f);
            floor.SetFloat("_DampHeight", 0f);
            floor.EnableKeyword("_LAYER2");
            floor.SetFloat("_Layer2", 1f);
            floor.SetTexture("_BaseMap2", Tex($"{TilingDir}/Basement_Rust_BaseColor.jpg", true, false, 2048));
            floor.SetTexture("_BumpMap2", Tex($"{TilingDir}/Basement_Rust_Normal.jpg", false, true, 2048));
            floor.SetTexture("_MaskMap2", Tex($"{TilingDir}/Basement_Rust_MetallicSmoothness.png", false, false, 2048));
            floor.SetTexture("_HeightMap2", Tex($"{TilingDir}/Basement_Rust_Height.jpg", false, false, 2048));
            floor.SetColor("_BaseColor2", new Color(1.5f, 1.15f, 0.9f));   // rust 0.06 -> ~0.075, a touch darker than the concrete
            floor.SetFloat("_Smoothness2", 0.55f);
            floor.SetFloat("_Layer2Scale", 1f / 2.2f);
            floor.SetTexture("_BlendMap", Tex($"{TilingDir}/Basement_FloorBlend.png", false, false, 1024));
            floor.SetFloat("_BlendScale", 1f / 12f);
            floor.SetFloat("_BlendContrast", 5f);
            floor.SetFloat("_Layer2Amount", 1.35f);
            floor.SetColor("_GrimeColor", new Color(0.62f, 0.55f, 0.46f));
            floor.SetFloat("_GrimeAmount", 1.1f);
            floor.SetColor("_WaterColor", new Color(0.09f, 0.085f, 0.07f));   // murky brown-grey
            floor.SetFloat("_WaterLevel", 0f);
            floor.SetFloat("_FloodDepth", 0.55f);
            floor.SetFloat("_WaterSmoothness", 0.96f);
            floor.SetFloat("_MacroVariation", 0.15f);
            EditorUtility.SetDirty(floor);
        }

        static Material TessMaterial(string set, Shader shader, string name = null)
        {
            string path = $"{MaterialDir}/{name ?? set + "_Tess"}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!mat) { mat = new Material(shader); AssetDatabase.CreateAsset(mat, path); }
            mat.shader = shader;
            mat.SetTexture("_BaseMap", Tex($"{TilingDir}/{set}_BaseColor.jpg", true, false, 2048));
            mat.SetTexture("_BumpMap", Tex($"{TilingDir}/{set}_Normal.jpg", false, true, 2048));
            mat.SetTexture("_MaskMap", Tex($"{TilingDir}/{set}_MetallicSmoothness.png", false, false, 2048));
            mat.SetTexture("_HeightMap", Tex($"{TilingDir}/{set}_Height.jpg", false, false, 2048));
            mat.SetColor("_BaseColor", Color.white);
            mat.SetFloat("_Smoothness", 0.85f);
            mat.SetFloat("_TessMax", 32f);
            mat.SetFloat("_TessEdgeNear", 0.03f);
            mat.SetFloat("_TessEdgeFar", 0.6f);
            mat.SetFloat("_TessNear", 2f);
            mat.SetFloat("_TessFar", 14f);
            // no visible repeats: patches read at random offsets, plus broad tone variation
            mat.EnableKeyword("_ANTITILE");
            mat.SetFloat("_AntiTile", 1f);
            mat.SetFloat("_AntiTileScale", 0.3f);
            mat.SetFloat("_MacroVariation", 0.22f);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static Material PropMaterial(string name, int maxSize)
        {
            var mat = LitMaterial($"{MaterialDir}/{name}_Mat.mat");
            mat.SetTexture("_BaseMap", Tex($"{PropTexDir}/{name}_BaseColor.png", true, false, maxSize));
            mat.SetColor("_BaseColor", new Color(1.25f, 1.25f, 1.25f));   // baked ~0.02-0.07 albedo: a little up, next to the walls
            mat.SetTexture("_BumpMap", Tex($"{PropTexDir}/{name}_Normal.png", false, true, maxSize));
            mat.EnableKeyword("_NORMALMAP");
            mat.SetTexture("_MetallicGlossMap", Tex($"{PropTexDir}/{name}_MetallicSmoothness.png", false, false, maxSize));
            mat.EnableKeyword("_METALLICSPECGLOSSMAP");
            mat.SetFloat("_Smoothness", 0.6f);   // scales the baked smoothness: flaking paint 0.45 -> 0.27, wood 0.21 -> 0.13
            mat.SetFloat("_SmoothnessTextureChannel", 0f);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static Material GlassMaterial()
        {
            var mat = LitMaterial($"{MaterialDir}/Basement_Glass.mat");
            mat.SetFloat("_Surface", 1f);       // transparent
            mat.SetFloat("_Blend", 0f);
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = (int)RenderQueue.Transparent;
            mat.SetColor("_BaseColor", new Color(0.32f, 0.36f, 0.34f, 0.45f));   // grimy
            mat.SetFloat("_Smoothness", 0.75f);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static Material BulbMaterial()
        {
            var mat = LitMaterial($"{MaterialDir}/Basement_Bulb.mat");
            mat.SetColor("_BaseColor", BulbColor);
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", BulbColor * 6f);
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static Material LitMaterial(string path)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!mat)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(mat, path);
            }
            return mat;
        }

        static Texture2D Tex(string path, bool sRGB, bool normalMap, int maxSize)
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            if (!importer) { Debug.LogWarning("Missing " + path); return null; }
            importer.textureType = normalMap ? TextureImporterType.NormalMap : TextureImporterType.Default;
            importer.sRGBTexture = sRGB;
            importer.mipmapEnabled = true;
            importer.maxTextureSize = maxSize;
            importer.anisoLevel = 4;
            var pc = importer.GetPlatformTextureSettings("Standalone");
            pc.overridden = normalMap;
            pc.maxTextureSize = maxSize;
            if (normalMap) pc.format = TextureImporterFormat.BC5;
            importer.SetPlatformTextureSettings(pc);
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        // ------------------------------------------------------------------ prefabs

        static ModelImporter Model(string name, bool lightmapUVs)
        {
            string path = $"{ModelDir}/{name}.fbx";
            var importer = (ModelImporter)AssetImporter.GetAtPath(path);
            if (!importer) { Debug.LogWarning("Missing " + path); return null; }
            importer.globalScale = 1f;
            importer.useFileScale = true;
            importer.generateSecondaryUV = lightmapUVs;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            return importer;
        }

        static void MakeArchitecture(string name)
        {
            var importer = Model(name, true);
            if (importer == null) return;
            foreach (string t in Tiling)
                importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), t),
                                  AssetDatabase.LoadAssetAtPath<Material>($"{MaterialDir}/{t}.mat"));
            importer.SaveAndReimport();
            var source = AssetDatabase.LoadAssetAtPath<GameObject>($"{ModelDir}/{name}.fbx");
            var go = (GameObject)PrefabUtility.InstantiatePrefab(source);
            go.name = name;
            foreach (var f in go.GetComponentsInChildren<MeshFilter>(true))
            {
                f.gameObject.AddComponent<MeshCollider>().sharedMesh = f.sharedMesh;
                GameObjectUtility.SetStaticEditorFlags(f.gameObject, (StaticEditorFlags)~0);
            }
            GameObjectUtility.SetStaticEditorFlags(go, (StaticEditorFlags)~0);
            PrefabUtility.SaveAsPrefabAsset(go, $"{PrefabDir}/{name}.prefab");
            Object.DestroyImmediate(go);
        }

        static void MakeProp(string name, bool split, bool collide)
        {
            var importer = Model(name, false);
            if (importer == null) return;
            int max = name.StartsWith("Pipe_Small") || name == "Lamp_Caged" || name == "Bucket" ? 1024 : 2048;
            Material mat = PropMaterial(name, max);
            Material glass = GlassMaterial(), bulb = BulbMaterial();
            importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), name + "_Mat"), mat);
            importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), "Glass"), glass);
            importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), "Bulb"), bulb);
            importer.SaveAndReimport();

            var source = AssetDatabase.LoadAssetAtPath<GameObject>($"{ModelDir}/{name}.fbx");
            if (split)
            {
                foreach (var filter in source.GetComponentsInChildren<MeshFilter>(true))
                {
                    var go = new GameObject(PieceName(name, filter.name));
                    go.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
                    go.AddComponent<MeshRenderer>().sharedMaterial = mat;
                    if (collide) Box(go, filter.sharedMesh);
                    PrefabUtility.SaveAsPrefabAsset(go, $"{PrefabDir}/{PieceName(name, filter.name)}.prefab");
                    Object.DestroyImmediate(go);
                }
                return;
            }
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(source);
            inst.name = name;
            foreach (var r in inst.GetComponentsInChildren<MeshRenderer>(true))
            {
                bool isGlass = r.name.Contains("Glass") || r.name.Contains("Jar");
                bool isBulb = r.name.Contains("Bulb");
                r.sharedMaterial = isGlass ? glass : isBulb ? bulb : mat;
                if (isGlass || isBulb) { r.shadowCastingMode = ShadowCastingMode.Off; continue; }
                if (collide) Box(r.gameObject, r.GetComponent<MeshFilter>().sharedMesh);
            }
            if (name == "Lamp_Caged") AddBulbLight(inst);
            // its box is far bigger than the model between the fins: sight goes by the real shape (the light monster)
            if (collide && name == "Transformer") inst.AddComponent<BodycamHorror.Environment.SeeThroughGaps>();
            PrefabUtility.SaveAsPrefabAsset(inst, $"{PrefabDir}/{name}.prefab");
            Object.DestroyImmediate(inst);
        }

        static void Box(GameObject go, Mesh mesh)
        {
            var box = go.AddComponent<BoxCollider>();
            box.center = mesh.bounds.center;
            box.size = Vector3.Max(mesh.bounds.size, Vector3.one * 0.02f);
        }

        /// <summary>A warm point light in the cage: shadows from the wires and everything round it.</summary>
        static void AddBulbLight(GameObject lamp)
        {
            var l = new GameObject("BulbLight").AddComponent<Light>();
            l.transform.SetParent(lamp.transform, false);
            l.transform.localPosition = new Vector3(0f, -0.64f, 0f);
            l.type = LightType.Point;
            l.color = BulbColor;
            l.intensity = 2.6f;
            l.range = 9f;
            l.shadows = LightShadows.Soft;
            l.shadowNearPlane = 0.1f;
        }
    }
}
