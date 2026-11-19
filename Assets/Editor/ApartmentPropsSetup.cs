using System.IO;
using BodycamHorror.Player;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace BodycamHorror.EditorTools
{
    /// <summary>
    /// Brings in the Khrushchevka apartment props made in Blender (Blender/tools/apartment_props.py): a mug, a
    /// clipboard with a form and a pen, a small sofa with a throw, a wall shelf with books and an ivy. Each has an
    /// FBX in Assets/Generated/Imported and baked maps in Assets/Generated/Textures/Props; this sets the maps'
    /// import types, makes one URP Lit material per prop, points the FBX at it and saves a prefab with a collider
    /// in Assets/Prefabs/Props. Menu: BodycamHorror > Set Up Apartment Props. Rerunning writes over them.
    /// The utility props (Blender/tools/utility_props.py: an oval bulkhead wall lamp that lights up, an open
    /// electrical panel) the same way: BodycamHorror > Set Up Utility Props.
    /// </summary>
    public static class ApartmentPropsSetup
    {
        const string ModelDir = "Assets/Generated/Imported";
        const string TextureDir = "Assets/Generated/Textures/Props";
        const string MaterialDir = "Assets/Generated/Materials/Props";
        const string PrefabDir = "Assets/Prefabs/Props";

        internal enum Hull { Box, Mesh }

        [MenuItem("BodycamHorror/Set Up Apartment Props")]
        public static void SetUp()
        {
            Directory.CreateDirectory(MaterialDir);
            Directory.CreateDirectory(PrefabDir);
            Make("Mug", Hull.Box);
            Make("Clipboard", Hull.Box);
            Make("Sofa", Hull.Box);
            // simple boxes, not the 18k-triangle mesh: the seat and base, the back, the two arms
            SetBoxes("Sofa", (new Vector3(0f, 0.22f, 0.02f), new Vector3(1.45f, 0.44f, 0.8f)),
                             (new Vector3(0f, 0.62f, -0.3f), new Vector3(1.45f, 0.42f, 0.2f)),
                             (new Vector3(-0.65f, 0.55f, 0f), new Vector3(0.17f, 0.22f, 0.8f)),
                             (new Vector3(0.65f, 0.55f, 0f), new Vector3(0.17f, 0.22f, 0.8f)));
            Make("WallShelf", Hull.Box);
            SetBoxes("WallShelf", (new Vector3(0f, 0.011f, 0.105f), new Vector3(0.82f, 0.022f, 0.21f)));   // the board only
            AssetDatabase.SaveAssets();
            Debug.Log("Apartment props set up in " + PrefabDir);
        }

        /// <summary>The cold blue the level's lamps shine (see LevelSceneBuilder / the hanging lamp).</summary>
        static readonly Color LampColor = new Color(0.7f, 0.83f, 1f);

        [MenuItem("BodycamHorror/Set Up Utility Props")]
        public static void SetUpUtility()
        {
            Directory.CreateDirectory(MaterialDir);
            Directory.CreateDirectory(PrefabDir);
            Make("ElectricPanel", Hull.Box);
            FitBox("ElectricPanel", new Vector3(0f, 0f, 0.065f), new Vector3(0.36f, 0.56f, 0.13f));   // the box only, not the cables up and down the wall
            Make("WallLamp", Hull.Box);
            LightUpLamp();
            AssetDatabase.SaveAssets();
            Debug.Log("Utility props set up in " + PrefabDir);
        }

        /// <summary>The bulkhead lamp's glass glows (its baked colour as the emission map, so the dirt on it
        /// shows dark against the light), and two lights come out of it: a wide spot into the room that casts
        /// shadows, and a faint point light that washes the wall round the lamp.</summary>
        static void LightUpLamp()
        {
            string path = $"{PrefabDir}/WallLamp.prefab";
            var lampMat = AssetDatabase.LoadAssetAtPath<Material>($"{MaterialDir}/WallLamp_Mat.mat");
            string glowPath = $"{MaterialDir}/WallLamp_Glow.mat";
            var glow = AssetDatabase.LoadAssetAtPath<Material>(glowPath);
            if (!glow) { glow = new Material(lampMat); AssetDatabase.CreateAsset(glow, glowPath); }
            glow.CopyPropertiesFromMaterial(lampMat);
            glow.EnableKeyword("_EMISSION");
            glow.SetTexture("_EmissionMap", lampMat.GetTexture("_BaseMap"));
            glow.SetColor("_EmissionColor", LampColor * 3.2f);
            glow.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            EditorUtility.SetDirty(glow);

            var root = PrefabUtility.LoadPrefabContents(path);
            foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                r.shadowCastingMode = ShadowCastingMode.Off;   // the lights sit right in front of it
                if (r.name.EndsWith("_Glass")) r.sharedMaterial = glow;
            }
            foreach (var l in root.GetComponentsInChildren<Light>(true)) Object.DestroyImmediate(l.gameObject);

            var spot = new GameObject("Light").AddComponent<Light>();
            spot.transform.SetParent(root.transform, false);
            spot.transform.localPosition = new Vector3(0f, 0f, 0.075f);   // just in front of the glass
            spot.type = LightType.Spot;
            spot.color = LampColor;
            spot.intensity = 2.2f;
            spot.range = 7f;
            spot.spotAngle = 160f;
            spot.innerSpotAngle = 70f;
            spot.shadows = LightShadows.Soft;
            spot.shadowNearPlane = 0.05f;
            ViewModelLighting.AlsoLightViewModel(spot);

            var wash = new GameObject("WallWash").AddComponent<Light>();
            wash.transform.SetParent(root.transform, false);
            wash.transform.localPosition = new Vector3(0f, 0f, 0.09f);
            wash.type = LightType.Point;
            wash.color = LampColor;
            wash.intensity = 0.5f;
            wash.range = 1.3f;
            wash.shadows = LightShadows.None;

            PrefabUtility.SaveAsPrefabAsset(root, path);
            PrefabUtility.UnloadPrefabContents(root);
        }

        [MenuItem("BodycamHorror/Set Up Storage Props")]
        public static void SetUpStorage()
        {
            Directory.CreateDirectory(MaterialDir);
            Directory.CreateDirectory(PrefabDir);
            Make("Fridge", Hull.Box);
            FitBox("Fridge", new Vector3(0f, 0.725f, 0f), new Vector3(0.63f, 1.45f, 0.62f));   // the body, not the coil on the back
            Make("CardboardBoxes", Hull.Box);
            Split("CardboardBoxes");
            FitBox("Box_Open", new Vector3(0f, 0.25f, 0f), new Vector3(0.55f, 0.5f, 0.5f));   // the box, not its flaps
            AssetDatabase.SaveAssets();
            Debug.Log("Storage props set up in " + PrefabDir);
        }

        /// <summary>One prefab per mesh of a set made together (one texture): CardboardBoxes.fbx holds
        /// Box_Closed, Box_Open, Box_Small, Box_Crushed, each with its origin at its own foot.</summary>
        internal static void Split(string name)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>($"{ModelDir}/{name}.fbx");
            var mat = AssetDatabase.LoadAssetAtPath<Material>($"{MaterialDir}/{name}_Mat.mat");
            foreach (var filter in model.GetComponentsInChildren<MeshFilter>(true))
            {
                var go = new GameObject(filter.name);
                go.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
                go.AddComponent<MeshRenderer>().sharedMaterial = mat;
                var box = go.AddComponent<BoxCollider>();
                box.center = filter.sharedMesh.bounds.center;
                box.size = filter.sharedMesh.bounds.size;
                go.isStatic = true;
                PrefabUtility.SaveAsPrefabAsset(go, $"{PrefabDir}/{filter.name}.prefab");
                Object.DestroyImmediate(go);
            }
        }

        /// <summary>Replaces a prefab's colliders with these boxes (centre, size in the prefab's space).</summary>
        internal static void SetBoxes(string name, params (Vector3 center, Vector3 size)[] boxes)
        {
            string path = $"{PrefabDir}/{name}.prefab";
            var root = PrefabUtility.LoadPrefabContents(path);
            var holder = root.GetComponentInChildren<MeshRenderer>(true).gameObject;
            foreach (var c in root.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
            foreach (var (center, size) in boxes)
            {
                var box = holder.AddComponent<BoxCollider>();
                box.center = center;
                box.size = size;
            }
            PrefabUtility.SaveAsPrefabAsset(root, path);
            PrefabUtility.UnloadPrefabContents(root);
        }

        internal static void FitBox(string name, Vector3 center, Vector3 size)
        {
            string path = $"{PrefabDir}/{name}.prefab";
            var root = PrefabUtility.LoadPrefabContents(path);
            foreach (var box in root.GetComponentsInChildren<BoxCollider>(true)) { box.center = center; box.size = size; }
            PrefabUtility.SaveAsPrefabAsset(root, path);
            PrefabUtility.UnloadPrefabContents(root);
        }

        /// <summary>Largest a prop's maps are imported at, when it is less than they were baked at: small things
        /// seen close need no 2048 (a 10 cm mug at 1024 is ~10 texels per millimetre).</summary>
        internal static readonly System.Collections.Generic.Dictionary<string, int> MaxSize = new System.Collections.Generic.Dictionary<string, int>
        {
            { "Mug", 512 }, { "Clipboard", 1024 }, { "WallShelf", 1024 }, { "Newspaper", 1024 },
        };

        [MenuItem("BodycamHorror/Optimise Prop Textures")]
        public static void OptimiseTextures()
        {
            foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { TextureDir }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string file = Path.GetFileNameWithoutExtension(path);
                string prop = file.Substring(0, file.LastIndexOf('_'));
                bool normal = file.EndsWith("_Normal");
                Configure(path, !normal && file.EndsWith("_BaseColor"), normal, MaxSize.TryGetValue(prop, out int m) ? m : 2048);
            }
        }

        /// <summary>Import settings for a baked map: its type, colour space, size cap, and on PC BC5 for a normal map
        /// (two channels at full precision, the same memory as DXT5).</summary>
        static void Configure(string path, bool sRGB, bool normalMap, int maxSize)
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            if (!importer) return;
            importer.textureType = normalMap ? TextureImporterType.NormalMap : TextureImporterType.Default;
            importer.sRGBTexture = sRGB;
            importer.mipmapEnabled = true;
            importer.maxTextureSize = maxSize;
            var pc = importer.GetPlatformTextureSettings("Standalone");
            pc.overridden = normalMap;
            pc.maxTextureSize = maxSize;
            if (normalMap) pc.format = TextureImporterFormat.BC5;
            importer.SetPlatformTextureSettings(pc);
            importer.SaveAndReimport();
        }

        internal static void Make(string name, Hull hull)
        {
            string model = $"{ModelDir}/{name}.fbx";
            if (!File.Exists(model)) { Debug.LogWarning("Missing " + model); return; }

            int max = MaxSize.TryGetValue(name, out int m) ? m : 2048;
            Texture2D baseMap = Texture($"{name}_BaseColor", true, false, max);
            Texture2D normal = Texture($"{name}_Normal", false, true, max);
            Texture2D metal = Texture($"{name}_MetallicSmoothness", false, false, max);

            string matPath = $"{MaterialDir}/{name}_Mat.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (!mat)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(mat, matPath);
            }
            mat.SetTexture("_BaseMap", baseMap);
            mat.SetTexture("_MainTex", baseMap);
            mat.SetColor("_BaseColor", Color.white);
            mat.SetTexture("_BumpMap", normal);
            mat.EnableKeyword("_NORMALMAP");
            mat.SetTexture("_MetallicGlossMap", metal);
            mat.EnableKeyword("_METALLICSPECGLOSSMAP");
            mat.SetFloat("_Smoothness", 1f);            // scales the map's alpha
            mat.SetFloat("_SmoothnessTextureChannel", 0f);
            EditorUtility.SetDirty(mat);

            var importer = (ModelImporter)AssetImporter.GetAtPath(model);
            importer.globalScale = 1f;
            importer.useFileScale = true;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), name + "_Mat"), mat);
            importer.SaveAndReimport();

            var source = AssetDatabase.LoadAssetAtPath<GameObject>(model);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(source);
            instance.name = name;
            foreach (var r in instance.GetComponentsInChildren<MeshRenderer>(true))
            {
                r.sharedMaterial = mat;
                var filter = r.GetComponent<MeshFilter>();
                if (hull == Hull.Mesh) r.gameObject.AddComponent<MeshCollider>().sharedMesh = filter.sharedMesh;
                else
                {
                    var box = r.gameObject.AddComponent<BoxCollider>();
                    Bounds b = filter.sharedMesh.bounds;
                    box.center = b.center;
                    box.size = b.size;
                }
            }
            PrefabUtility.SaveAsPrefabAsset(instance, $"{PrefabDir}/{name}.prefab");
            Object.DestroyImmediate(instance);
        }

        static Texture2D Texture(string file, bool sRGB, bool normalMap, int maxSize)
        {
            string path = $"{TextureDir}/{file}.png";
            Configure(path, sRGB, normalMap, maxSize);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
    }
}
