using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace BodycamHorror.EditorTools
{
    /// <summary>
    /// Brings in the Khrushchevka living-room props made in Blender (Blender/tools/khrushchev_props.py): a wardrobe, a
    /// glazed sideboard with china, a single bed, a tube TV and its stand, a kitchen table under oilcloth, a coffee
    /// table, a cast-iron radiator, a wall carpet, a floor rug, curtains on a rod, a three-globe chandelier, and two sets
    /// split one prefab per piece: FlatDecor (WallClock, Portrait, Calendar, Radio, Ashtray, PottedPlant, Kettle) and
    /// FlatFurniture (Nightstand, Stool, FloorLamp). Prefabs go to Assets/Prefabs/Props.
    /// Menu: BodycamHorror > Set Up Khrushchev Props. Rerunning writes over them.
    /// </summary>
    public static class KhrushchevPropsSetup
    {
        const string PrefabDir = "Assets/Prefabs/Props";
        const string MaterialDir = "Assets/Generated/Materials/Props";

        static readonly string[] Singles =
        {
            "Wardrobe", "Sideboard", "Bed", "CRT_TV", "TV_Stand", "DiningTable", "CoffeeTable", "Radiator_Cast",
            "WallCarpet", "Rug_Floor", "Curtains", "Chandelier",
        };

        [MenuItem("BodycamHorror/Set Up Khrushchev Props")]
        public static void SetUp()
        {
            Directory.CreateDirectory(MaterialDir);
            Directory.CreateDirectory(PrefabDir);
            foreach (string n in Singles) ApartmentPropsSetup.Make(n, ApartmentPropsSetup.Hull.Box);

            // colliders: the body, not what sticks out of it
            ApartmentPropsSetup.FitBox("Wardrobe", new Vector3(0f, 0.975f, 0f), new Vector3(1.03f, 1.95f, 0.6f));
            ApartmentPropsSetup.SetBoxes("Sideboard", (new Vector3(0f, 0.9f, 0f), new Vector3(1.23f, 1.81f, 0.45f)));
            ApartmentPropsSetup.SetBoxes("Bed", (new Vector3(0f, 0.3f, 0f), new Vector3(1.0f, 0.6f, 2.05f)),
                                                (new Vector3(0f, 0.5f, -1.0f), new Vector3(1.05f, 1.0f, 0.08f)));
            ApartmentPropsSetup.SetBoxes("CRT_TV", (new Vector3(0f, 0.24f, -0.06f), new Vector3(0.62f, 0.48f, 0.6f)));
            ApartmentPropsSetup.SetBoxes("Radiator_Cast", (new Vector3(0f, 0.41f, 0.115f), new Vector3(0.9f, 0.6f, 0.16f)));
            foreach (string n in new[] { "WallCarpet", "Rug_Floor" }) NoCollider(n, false);
            foreach (string n in new[] { "Curtains", "Chandelier" }) NoCollider(n, true);
            GlassPanes("Sideboard");

            foreach (string set in new[] { "FlatDecor", "FlatFurniture" })
            {
                ApartmentPropsSetup.Make(set, ApartmentPropsSetup.Hull.Box);
                ApartmentPropsSetup.Split(set);
                AssetDatabase.DeleteAsset($"{PrefabDir}/{set}.prefab");   // only its pieces are used
            }
            foreach (string n in new[] { "WallClock", "Portrait", "Calendar" }) NoCollider(n, false);
            AssetDatabase.SaveAssets();
            Debug.Log("Khrushchev props set up in " + PrefabDir);
        }

        /// <summary>Hung on a wall, lying flat, or overhead: nothing to bump into, and nothing to stop a ray.</summary>
        static void NoCollider(string name, bool shadows)
        {
            string path = $"{PrefabDir}/{name}.prefab";
            if (!File.Exists(path)) { Debug.LogWarning("Missing " + path); return; }
            var root = PrefabUtility.LoadPrefabContents(path);
            foreach (var c in root.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
            foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
                r.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            PrefabUtility.SaveAsPrefabAsset(root, path);
            PrefabUtility.UnloadPrefabContents(root);
        }

        /// <summary>The prop's *_Glass mesh gets a dusty see-through glass.</summary>
        static void GlassPanes(string name)
        {
            string path = $"{PrefabDir}/{name}.prefab";
            var root = PrefabUtility.LoadPrefabContents(path);
            var glass = GlassMaterial();
            foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (!r.name.EndsWith("_Glass")) continue;
                r.sharedMaterial = glass;
                r.shadowCastingMode = ShadowCastingMode.Off;   // (the prop's one collider box, from SetBoxes, may sit on it)
            }
            PrefabUtility.SaveAsPrefabAsset(root, path);
            PrefabUtility.UnloadPrefabContents(root);
        }

        static Material GlassMaterial()
        {
            string path = $"{MaterialDir}/Khrush_Glass.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!mat)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend", 0f);
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = (int)RenderQueue.Transparent;
            mat.SetColor("_BaseColor", new Color(0.55f, 0.58f, 0.55f, 0.22f));
            mat.SetFloat("_Metallic", 0f);
            mat.SetFloat("_Smoothness", 0.82f);
            EditorUtility.SetDirty(mat);
            return mat;
        }
    }
}
