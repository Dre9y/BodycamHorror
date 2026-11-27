using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace BodycamHorror.EditorTools
{
    /// <summary>
    /// Brings in the Level polish props made in Blender (Blender/tools/level_polish_props.py), one prefab per piece:
    /// Crates (Crate_Slat, Crate_Box, Crate_Broken), Fixtures (Bucket, TrashBin, LampCord, PipeClamp, PipeCollar),
    /// SoftGoods (Mattress_Single, Mattress_Double, Cushion), Debris (Paper_Sheet, Paper_Ball, Plaster_A, Plaster_B,
    /// Plaster_Slab, Bottle, BrokenBottle, Can, Rag, Junk_Pile) and the WindowFrame. Rubbish, trim and the lamp's cord
    /// get no collider (nothing to trip on, nothing to stop a ray). Prefabs go to Assets/Prefabs/Props.
    /// Menu: BodycamHorror > Set Up Level Polish Props. Rerunning writes over them.
    /// </summary>
    public static class LevelPolishPropsSetup
    {
        const string PrefabDir = "Assets/Prefabs/Props";
        const string MaterialDir = "Assets/Generated/Materials/Props";

        static readonly string[] Sets = { "Crates", "Fixtures", "SoftGoods", "Debris" };
        static readonly string[] NoColliders =
        {
            "LampCord", "PipeClamp", "PipeCollar", "Paper_Sheet", "Paper_Ball", "Plaster_A", "Plaster_B", "Plaster_Slab",
            "Bottle", "BrokenBottle", "Can", "Rag", "Junk_Pile", "Cushion", "WindowFrame",
        };

        [MenuItem("BodycamHorror/Set Up Level Polish Props")]
        public static void SetUp()
        {
            Directory.CreateDirectory(MaterialDir);
            Directory.CreateDirectory(PrefabDir);
            foreach (string set in Sets)
            {
                ApartmentPropsSetup.Make(set, ApartmentPropsSetup.Hull.Box);
                ApartmentPropsSetup.Split(set);
                AssetDatabase.DeleteAsset($"{PrefabDir}/{set}.prefab");   // only its pieces are used
            }
            ApartmentPropsSetup.Make("WindowFrame", ApartmentPropsSetup.Hull.Box);
            foreach (string n in NoColliders) Strip(n, n != "LampCord" && n != "PipeCollar" && n != "PipeClamp");
            // the paper and plaster are baked near white: under the bare bulbs they glared
            var debris = AssetDatabase.LoadAssetAtPath<Material>($"{MaterialDir}/Debris_Mat.mat");
            if (debris) { debris.SetColor("_BaseColor", new Color(0.72f, 0.7f, 0.66f)); EditorUtility.SetDirty(debris); }
            AssetDatabase.SaveAssets();
            Debug.Log("Level polish props set up in " + PrefabDir);
        }

        static void Strip(string name, bool shadows)
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
    }
}
