using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace BodycamHorror.EditorTools
{
    /// <summary>
    /// Turns the dressing models made by Blender/tools/basement_dressing.py (FBX in Assets/Models/BasementDressing)
    /// into prefabs in Assets/Prefabs/Basement/Dressing. Blender only names the materials; here each name gets a
    /// matte URP Lit material (nothing glossy: the bulbs and the flashlight made every shine a hot spot).
    /// Menu: BodycamHorror > Level > Build Basement Dressing Prefabs. Running it again rebuilds them in place.
    /// </summary>
    internal static class BasementDressingSetup
    {
        const string Models = "Assets/Models/BasementDressing";
        const string MaterialsDir = Models + "/Materials";
        internal const string PrefabDir = "Assets/Prefabs/Basement/Dressing";

        // colour, smoothness, metallic
        static readonly Dictionary<string, (Color c, float s, float m)> Palette = new Dictionary<string, (Color, float, float)>
        {
            { "Cable", (new Color(.12f, .11f, .10f), .3f, 0) },
            { "CableBlack", (new Color(.04f, .04f, .04f), .3f, 0) },
            { "Copper", (new Color(.55f, .30f, .15f), .35f, .8f) },
            { "Steel", (new Color(.33f, .33f, .32f), .25f, .6f) },
            { "Duct", (new Color(.46f, .46f, .44f), .28f, .5f) },
            { "DuctSeam", (new Color(.38f, .38f, .36f), .22f, .5f) },
            { "Wood", (new Color(.33f, .24f, .16f), .12f, 0) },
            { "WoodDark", (new Color(.18f, .13f, .08f), .12f, 0) },
            { "Padlock", (new Color(.30f, .26f, .18f), .3f, .7f) },
            { "Enamel", (new Color(.70f, .68f, .62f), .25f, 0) },
            { "EnamelBlue", (new Color(.07f, .16f, .38f), .35f, 0) },
            { "EnamelWhite", (new Color(.80f, .80f, .76f), .35f, 0) },
            { "Iron", (new Color(.05f, .05f, .05f), .15f, .4f) },
            { "Rail", (new Color(.11f, .20f, .13f), .25f, .3f) },
            { "SledRunner", (new Color(.33f, .07f, .05f), .25f, .4f) },
            { "SledWood", (new Color(.45f, .34f, .20f), .15f, 0) },
            { "Tyre", (new Color(.03f, .03f, .03f), .15f, 0) },
            { "BikeChrome", (new Color(.40f, .40f, .40f), .3f, .7f) },
            { "BikeFrame", (new Color(.09f, .22f, .32f), .3f, .3f) },
            { "Pickles", (new Color(.20f, .27f, .09f), .45f, 0) },
            { "Tomatoes", (new Color(.42f, .09f, .05f), .45f, 0) },
            { "Jam", (new Color(.28f, .05f, .11f), .45f, 0) },
            { "Lid", (new Color(.50f, .45f, .35f), .3f, .7f) },
            { "Dust", (new Color(.38f, .38f, .36f), .08f, 0) },
        };

        // which prefabs are solid (a box round the whole thing); cables, signs and the rail are not in the way
        static readonly HashSet<string> Solid = new HashSet<string> { "Storage_Lockers", "Bicycle", "Sled", "Jars_Preserves", "Duct_2m", "Duct_2m_Grille" };

        [MenuItem("BodycamHorror/Level/Build Basement Dressing Prefabs")]
        internal static void Build()
        {
            if (!AssetDatabase.IsValidFolder(MaterialsDir)) AssetDatabase.CreateFolder(Models, "Materials");
            if (!AssetDatabase.IsValidFolder(PrefabDir)) AssetDatabase.CreateFolder("Assets/Prefabs/Basement", "Dressing");
            foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { Models }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var importer = (ModelImporter)AssetImporter.GetAtPath(path);
                importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
                importer.importAnimation = false;
                importer.animationType = ModelImporterAnimationType.None;
                importer.importCameras = importer.importLights = false;
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                var names = model.GetComponentsInChildren<Renderer>().SelectMany(r => r.sharedMaterials).Where(m => m).Select(m => m.name).Distinct().ToList();
                foreach (var n in names)
                    importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), n), Matte(n));
                importer.SaveAndReimport();
                Prefab(path);
            }
            AssetDatabase.SaveAssets();
            Debug.Log("Basement dressing prefabs built in " + PrefabDir);
        }

        static Material Matte(string name)
        {
            string path = MaterialsDir + "/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!m) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, path); }
            (Color c, float s, float m) p = Palette.TryGetValue(name, out var v) ? v : (new Color(.4f, .4f, .4f), .2f, 0f);
            m.SetColor("_BaseColor", p.c);
            m.SetFloat("_Smoothness", p.s);
            m.SetFloat("_Metallic", p.m);
            EditorUtility.SetDirty(m);
            return m;
        }

        static void Prefab(string modelPath)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(model);
            go.name = model.name;
            foreach (var c in go.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
            foreach (var r in go.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            if (Solid.Contains(model.name))
            {
                var b = new Bounds(go.transform.position, Vector3.zero);
                foreach (var r in go.GetComponentsInChildren<Renderer>()) b.Encapsulate(r.bounds);
                var box = go.AddComponent<BoxCollider>();
                box.center = go.transform.InverseTransformPoint(b.center);
                box.size = b.size;
            }
            go.isStatic = true;
            PrefabUtility.SaveAsPrefabAsset(go, PrefabDir + "/" + model.name + ".prefab");
            Object.DestroyImmediate(go);
        }
    }
}
