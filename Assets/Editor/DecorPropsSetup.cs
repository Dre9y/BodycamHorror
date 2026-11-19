using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace BodycamHorror.EditorTools
{
    /// <summary>
    /// Brings in the decor props made in Blender (Blender/tools/decor_props.py): a bookcase full of books, a grey
    /// metal rack, a photo poster, and Books.fbx split into one prefab per book (seven books, one lying open, a stack,
    /// a row to fill a shelf). Then the graffiti (Blender/tools/decor_textures.py, PNGs with alpha in
    /// Assets/Generated/Textures/Graffiti) become URP decal prefabs in Assets/Prefabs/Props/Graffiti: put one against a
    /// wall with its blue axis (forward) pointing into the wall; the box is centred on the object, 0.3 m deep.
    /// Menu: BodycamHorror > Set Up Decor Props. Rerunning writes over them.
    /// </summary>
    public static class DecorPropsSetup
    {
        const string PrefabDir = "Assets/Prefabs/Props";
        const string GraffitiTex = "Assets/Generated/Textures/Graffiti";
        const string GraffitiMat = "Assets/Generated/Materials/Graffiti";
        const string GraffitiPrefabs = "Assets/Prefabs/Props/Graffiti";

        /// <summary>Each graffiti's width on the wall in metres (the height follows the picture).</summary>
        static readonly (string name, float width)[] Graffiti =
        {
            ("LMM", 1.7f), ("NoExit", 1.9f), ("DontLook", 1.8f), ("Tag", 1.0f), ("Smiley", 0.8f), ("Eye", 1.0f),
        };

        [MenuItem("BodycamHorror/Set Up Decor Props")]
        public static void SetUp()
        {
            Directory.CreateDirectory("Assets/Generated/Materials/Props");
            Directory.CreateDirectory(PrefabDir);
            ApartmentPropsSetup.MaxSize["Poster"] = 1024;

            ApartmentPropsSetup.Make("Bookcase", ApartmentPropsSetup.Hull.Box);
            ApartmentPropsSetup.FitBox("Bookcase", new Vector3(0f, 0.96f, 0f), new Vector3(0.95f, 1.92f, 0.34f));
            ApartmentPropsSetup.Make("MetalRack", ApartmentPropsSetup.Hull.Box);
            ApartmentPropsSetup.FitBox("MetalRack", new Vector3(0f, 0.9f, 0f), new Vector3(1.02f, 1.8f, 0.42f));
            ApartmentPropsSetup.Make("Books", ApartmentPropsSetup.Hull.Box);
            ApartmentPropsSetup.Split("Books");
            ApartmentPropsSetup.Make("Poster", ApartmentPropsSetup.Hull.Box);
            NoCollider("Poster");
            foreach (var (name, width) in Graffiti) MakeGraffiti(name, width);
            AssetDatabase.SaveAssets();
            Debug.Log("Decor props set up in " + PrefabDir);
        }

        /// <summary>Paper on a wall: nothing to bump into, and it should not stop a bullet or a footstep ray.</summary>
        static void NoCollider(string name)
        {
            string path = $"{PrefabDir}/{name}.prefab";
            var root = PrefabUtility.LoadPrefabContents(path);
            foreach (var c in root.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
            foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true)) r.shadowCastingMode = ShadowCastingMode.Off;
            PrefabUtility.SaveAsPrefabAsset(root, path);
            PrefabUtility.UnloadPrefabContents(root);
        }

        internal static void MakeGraffiti(string name, float width)
        {
            string texPath = $"{GraffitiTex}/Graffiti_{name}.png";
            var importer = (TextureImporter)AssetImporter.GetAtPath(texPath);
            if (!importer) { Debug.LogWarning("Missing " + texPath); return; }
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = true;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.mipmapEnabled = true;
            importer.maxTextureSize = 2048;
            importer.SaveAndReimport();
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);

            Directory.CreateDirectory(GraffitiMat);
            string matPath = $"{GraffitiMat}/Graffiti_{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (!mat)
            {
                var template = AssetDatabase.LoadAssetAtPath<Material>("Assets/Resources/WoundDecal.mat");
                mat = template ? new Material(template) : new Material(Shader.Find("Shader Graphs/Decal"));
                AssetDatabase.CreateAsset(mat, matPath);
            }
            mat.SetTexture("Base_Map", tex);
            if (mat.HasProperty("Normal_Blend")) mat.SetFloat("Normal_Blend", 0f);   // paint keeps the wall's own bumps
            mat.enableInstancing = true;
            EditorUtility.SetDirty(mat);

            Directory.CreateDirectory(GraffitiPrefabs);
            var go = new GameObject("Graffiti_" + name);
            var projector = go.AddComponent<DecalProjector>();
            projector.material = mat;
            const float depth = 0.3f;
            projector.size = new Vector3(width, width * tex.height / tex.width, depth);
            projector.pivot = Vector3.zero;
            projector.drawDistance = 40f;
            projector.fadeScale = 0.9f;
            projector.startAngleFade = 60f;   // not onto a floor or a door frame side met at a right angle
            projector.endAngleFade = 85f;
            go.isStatic = true;
            PrefabUtility.SaveAsPrefabAsset(go, $"{GraffitiPrefabs}/Graffiti_{name}.prefab");
            Object.DestroyImmediate(go);
        }
    }
}
