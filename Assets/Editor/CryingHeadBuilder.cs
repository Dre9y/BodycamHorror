using System.IO;
using BodycamHorror.Combat;
using BodycamHorror.Enemies;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace BodycamHorror.EditorTools
{
    /// <summary>
    /// The crying head enemy, from the model in Assets/crying_head_enemy (Monster_HEAD.fbx, two clips:
    /// Idle and move, both in place). Sets up the import (looping clips, linear masks, a normal map),
    /// packs the textures for URP (metallic + smoothness in one map, the DirectX normal map turned into
    /// Unity's OpenGL one), makes the material and the prefab, and puts one in the lighting test scene.
    /// </summary>
    public static class CryingHeadBuilder
    {
        const string Dir = "Assets/crying_head_enemy";
        const string ModelPath = Dir + "/source/Monster_HEAD.fbx";
        const string TexDir = Dir + "/textures";
        const string MaterialPath = Dir + "/CryingHead.mat";
        const string PrefabDir = "Assets/Prefabs/Enemies";
        const string PrefabPath = PrefabDir + "/CryingHead.prefab";
        const string ScenePath = "Assets/Scenes/LightingTest.unity";
        const string SceneObjectName = "CryingHead";

        /// <summary>The model is huge as it comes (a head 1.4 m across): this makes the head about 42 cm.</summary>
        const float ModelScale = 0.3f;
        /// <summary>The physics ball, around the head (m).</summary>
        const float BodyRadius = 0.225f;
        /// <summary>The head's root bone in the model (the middle of the head).</summary>
        const string HeadBone = "Armature/Basic";

        /// <summary>Where it sits in the lighting test scene, and which way it faces.</summary>
        static readonly Vector3 ScenePosition = new Vector3(7.6f, 0f, 7.9f);
        const float SceneFacing = 245f;   // towards the room's door

        [MenuItem("BodycamHorror/Crying Head/Build Prefab")]
        public static GameObject BuildPrefab()
        {
            SetUpImport();
            var material = BuildMaterial();
            AnimationClip idle = null, move = null;
            foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(ModelPath))
                if (o is AnimationClip c && !c.name.StartsWith("__preview"))
                {
                    if (c.name == "Idle") idle = c;
                    else if (c.name == "Move") move = c;
                }

            var root = new GameObject("CryingHead");
            try
            {
                var rb = root.AddComponent<Rigidbody>();
                rb.mass = 12f;
                rb.linearDamping = 0f;
                rb.angularDamping = 5f;
                rb.freezeRotation = true;
                rb.interpolation = RigidbodyInterpolation.Interpolate;

                var sphere = root.AddComponent<SphereCollider>();
                sphere.radius = BodyRadius;
                sphere.center = new Vector3(0f, BodyRadius, 0f);
                var slip = new PhysicsMaterial("CryingHead_Slip") { dynamicFriction = 0f, staticFriction = 0f, frictionCombine = PhysicsMaterialCombine.Minimum };
                sphere.sharedMaterial = SaveAsset(slip, Dir + "/CryingHead_Slip.physicMaterial");

                var flesh = root.AddComponent<FleshSurface>();
                flesh.bloodColor = new Color(0.24f, 0.03f, 0.02f, 0.92f);

                // the model, unpacked so the stray concrete block that came with it can go
                var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath));
                PrefabUtility.UnpackPrefabInstance(model, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                model.name = "Model";
                var block = model.transform.Find("Plane.001");
                if (block) Object.DestroyImmediate(block.gameObject);
                model.transform.SetParent(root.transform, false);
                model.transform.localScale = Vector3.one * ModelScale;

                // the middle of the head goes to the middle of the ball
                if (idle) idle.SampleAnimation(model, 0f);
                var head = model.transform.Find(HeadBone);
                Vector3 headLocal = head ? root.transform.InverseTransformPoint(head.position) : Vector3.zero;
                model.transform.localPosition = sphere.center - headLocal;

                foreach (var r in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    r.sharedMaterial = material;
                    r.updateWhenOffscreen = true;   // the legs swing far out of the bind-pose bounds
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                }
                var animator = model.GetComponent<Animator>();
                if (!animator) animator = model.AddComponent<Animator>();
                animator.runtimeAnimatorController = null;
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

                var enemy = root.AddComponent<CryingHead>();
                enemy.model = model.transform;
                enemy.idleClip = idle;
                enemy.moveClip = move;
                enemy.shriek = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/Monster/monster_scream.wav");

                Directory.CreateDirectory(PrefabDir);
                var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                Debug.Log("Crying head prefab saved: " + PrefabPath);
                return prefab;
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [MenuItem("BodycamHorror/Crying Head/Add To Lighting Test Scene")]
        public static void AddToLightingTest()
        {
            var prefab = BuildPrefab();
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            foreach (var go in scene.GetRootGameObjects())
                if (go.name == SceneObjectName) Object.DestroyImmediate(go);

            var head = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            head.name = SceneObjectName;
            head.transform.SetPositionAndRotation(ScenePosition, Quaternion.Euler(0f, SceneFacing, 0f));
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("Crying head placed in " + ScenePath + " at " + ScenePosition);
        }

        // ------------------------------------------------------------------ import

        static void SetUpImport()
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath(ModelPath);
            bool changed = false;
            if (importer.animationType != ModelImporterAnimationType.Generic) { importer.animationType = ModelImporterAnimationType.Generic; changed = true; }
            if (!importer.importAnimation) { importer.importAnimation = true; changed = true; }

            var clips = importer.clipAnimations.Length > 0 ? importer.clipAnimations : importer.defaultClipAnimations;
            foreach (var c in clips)
            {
                string want = c.takeName.EndsWith("Idle") ? "Idle" : c.takeName.EndsWith("move") ? "Move" : c.name;
                if (c.name != want || !c.loopTime) changed = true;
                c.name = want;
                c.loopTime = true;
                c.loopPose = false;
            }
            importer.clipAnimations = clips;
            if (changed) importer.SaveAndReimport();

            Texture(TexDir + "/Material_Base_Color.png", TextureImporterType.Default, true);
            Texture(TexDir + "/Material_Mixed_AO.png", TextureImporterType.Default, false);
            Texture(TexDir + "/Material_Metallic.png", TextureImporterType.Default, false);
            Texture(TexDir + "/Material_Roughness.png", TextureImporterType.Default, false);
            Texture(TexDir + "/Material_Normal_DirectX.png", TextureImporterType.Default, false);
        }

        static void Texture(string path, TextureImporterType type, bool srgb)
        {
            if (!(AssetImporter.GetAtPath(path) is TextureImporter ti)) return;
            if (ti.textureType == type && ti.sRGBTexture == srgb) return;
            ti.textureType = type;
            ti.sRGBTexture = srgb;
            ti.SaveAndReimport();
        }

        // ------------------------------------------------------------------ material

        static Material BuildMaterial()
        {
            string normalPath = TexDir + "/CryingHead_Normal_OpenGL.png";
            string maskPath = TexDir + "/CryingHead_MetallicSmoothness.png";
            if (!File.Exists(normalPath)) PackNormal(TexDir + "/Material_Normal_DirectX.png", normalPath);
            if (!File.Exists(maskPath)) PackMask(TexDir + "/Material_Metallic.png", TexDir + "/Material_Roughness.png", maskPath);
            AssetDatabase.ImportAsset(normalPath);
            AssetDatabase.ImportAsset(maskPath);
            if (AssetImporter.GetAtPath(normalPath) is TextureImporter ni && ni.textureType != TextureImporterType.NormalMap)
            {
                ni.textureType = TextureImporterType.NormalMap;
                ni.SaveAndReimport();
            }
            Texture(maskPath, TextureImporterType.Default, false);

            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (!material)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(material, MaterialPath);
            }
            material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(TexDir + "/Material_Base_Color.png"));
            material.SetColor("_BaseColor", Color.white);
            material.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath));
            material.SetFloat("_BumpScale", 1f);
            material.EnableKeyword("_NORMALMAP");
            material.SetTexture("_MetallicGlossMap", AssetDatabase.LoadAssetAtPath<Texture2D>(maskPath));
            material.EnableKeyword("_METALLICSPECGLOSSMAP");
            material.SetFloat("_Smoothness", 1f);
            material.SetFloat("_SmoothnessTextureChannel", 0f);
            material.SetTexture("_OcclusionMap", AssetDatabase.LoadAssetAtPath<Texture2D>(TexDir + "/Material_Mixed_AO.png"));
            material.SetFloat("_OcclusionStrength", 1f);
            material.EnableKeyword("_OCCLUSIONMAP");
            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssets();
            return material;
        }

        /// <summary>DirectX normal maps have green pointing down; Unity wants it up.</summary>
        static void PackNormal(string from, string to)
        {
            var tex = Load(from);
            var px = tex.GetPixels32();
            for (int i = 0; i < px.Length; i++) px[i].g = (byte)(255 - px[i].g);
            Save(tex, px, to);
        }

        /// <summary>URP's metallic map: metal in R, smoothness (1 - roughness) in A.</summary>
        static void PackMask(string metallicPath, string roughnessPath, string to)
        {
            var metal = Load(metallicPath);
            var rough = Load(roughnessPath);
            var m = metal.GetPixels32();
            var r = rough.GetPixels32();
            var px = new Color32[m.Length];
            for (int i = 0; i < px.Length; i++)
            {
                byte metallic = m[i].r;
                byte smooth = (byte)(255 - (i < r.Length ? r[i].r : (byte)128));
                px[i] = new Color32(metallic, metallic, metallic, smooth);
            }
            Save(metal, px, to);
            Object.DestroyImmediate(rough);
        }

        static Texture2D Load(string path)
        {
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            tex.LoadImage(File.ReadAllBytes(path));
            return tex;
        }

        static void Save(Texture2D size, Color32[] px, string path)
        {
            var tex = new Texture2D(size.width, size.height, TextureFormat.RGBA32, false, true);
            tex.SetPixels32(px);
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            Object.DestroyImmediate(size);
        }

        static T SaveAsset<T>(T asset, string path) where T : Object
        {
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing)
            {
                EditorUtility.CopySerialized(asset, existing);
                return existing;
            }
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }
    }
}
