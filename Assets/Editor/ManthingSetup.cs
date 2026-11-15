using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace BodycamHorror.EditorTools
{
    /// <summary>
    /// Imports the "Now you see me" monster (Assets/NowYouSeeMee/Manthing): the manthing model (a 3ds Max biped rig)
    /// and the animations, which were made on a different skeleton (a Blender metarig on another creature).
    /// Both are set up as Humanoid with a hand-made bone map and a T-pose, so the clips retarget onto the manthing.
    /// Also builds the URP material. Menu: BodycamHorror > Set Up Manthing.
    /// </summary>
    public static class ManthingSetup
    {
        public const string Dir = "Assets/NowYouSeeMee/Manthing";
        public const string ModelPath = Dir + "/Manthing.fbx";
        public const string AnimPath = Dir + "/ManthingAnimations.fbx";
        public const string MaterialPath = Dir + "/Manthing.mat";

        // Humanoid bone -> transform name.
        static readonly Dictionary<string, string> ManthingMap = new Dictionary<string, string>
        {
            { "Hips", "Base HumanPelvis" },
            { "LeftUpperLeg", "Base HumanLThigh" }, { "LeftLowerLeg", "Base HumanLCalf" }, { "LeftFoot", "Base HumanLFoot" }, { "LeftToes", "Base HumanFootBone002" },
            { "RightUpperLeg", "Base HumanRThigh" }, { "RightLowerLeg", "Base HumanRCalf" }, { "RightFoot", "Base HumanRFoot" }, { "RightToes", "Base HumanFootBone001" },
            { "Spine", "Base HumanSpine1" }, { "Chest", "Base HumanSpine3" }, { "UpperChest", "Base HumanRibcage" },
            { "Neck", "Base HumanNeck1" }, { "Head", "Base HumanHead" },
            { "LeftShoulder", "Base HumanLCollarbone" }, { "LeftUpperArm", "Base HumanLUpperarm1" }, { "LeftLowerArm", "Base HumanLForearm1" }, { "LeftHand", "Base HumanLPalm" },
            { "RightShoulder", "Base HumanRCollarbone" }, { "RightUpperArm", "Base HumanRUpperarm1" }, { "RightLowerArm", "Base HumanRForearm1" }, { "RightHand", "Base HumanRPalm" },
            { "Left Thumb Proximal", "Base HumanLDigit11" }, { "Left Thumb Intermediate", "Base HumanLDigit12" }, { "Left Thumb Distal", "Base HumanLDigit13" },
            { "Left Index Proximal", "Base HumanLDigit21" }, { "Left Index Intermediate", "Base HumanLDigit22" }, { "Left Index Distal", "Base HumanLDigit23" },
            { "Left Middle Proximal", "Base HumanLDigit31" }, { "Left Middle Intermediate", "Base HumanLDigit32" }, { "Left Middle Distal", "Base HumanLDigit33" },
            { "Left Ring Proximal", "Base HumanLDigit41" }, { "Left Ring Intermediate", "Base HumanLDigit42" }, { "Left Ring Distal", "Base HumanLDigit43" },
            { "Left Little Proximal", "Base HumanLDigit51" }, { "Left Little Intermediate", "Base HumanLDigit52" }, { "Left Little Distal", "Base HumanLDigit53" },
            { "Right Thumb Proximal", "Base HumanRDigit11" }, { "Right Thumb Intermediate", "Base HumanRDigit12" }, { "Right Thumb Distal", "Base HumanRDigit13" },
            { "Right Index Proximal", "Base HumanRDigit21" }, { "Right Index Intermediate", "Base HumanRDigit22" }, { "Right Index Distal", "Base HumanRDigit23" },
            { "Right Middle Proximal", "Base HumanRDigit31" }, { "Right Middle Intermediate", "Base HumanRDigit32" }, { "Right Middle Distal", "Base HumanRDigit33" },
            { "Right Ring Proximal", "Base HumanRDigit41" }, { "Right Ring Intermediate", "Base HumanRDigit42" }, { "Right Ring Distal", "Base HumanRDigit43" },
            { "Right Little Proximal", "Base HumanRDigit51" }, { "Right Little Intermediate", "Base HumanRDigit52" }, { "Right Little Distal", "Base HumanRDigit53" },
        };

        static readonly Dictionary<string, string> MetarigMap = new Dictionary<string, string>
        {
            { "Hips", "spine" }, { "Spine", "spine.001" }, { "Chest", "spine.002" }, { "UpperChest", "spine.003" },
            { "Neck", "spine.004" }, { "Head", "head" }, { "Jaw", "jaw" },
            { "LeftUpperLeg", "thigh.L" }, { "LeftLowerLeg", "shin.L" }, { "LeftFoot", "foot.L" }, { "LeftToes", "toe.L" },
            { "RightUpperLeg", "thigh.R" }, { "RightLowerLeg", "shin.R" }, { "RightFoot", "foot.R" }, { "RightToes", "toe.R" },
            { "LeftShoulder", "shoulder.L" }, { "LeftUpperArm", "upper_arm.L" }, { "LeftLowerArm", "forearm.L" }, { "LeftHand", "hand.L" },
            { "RightShoulder", "shoulder.R" }, { "RightUpperArm", "upper_arm.R" }, { "RightLowerArm", "forearm.R" }, { "RightHand", "hand.R" },
            { "Left Thumb Proximal", "thumb.L" },
            { "Left Index Proximal", "index1.L" }, { "Left Index Intermediate", "index2.L" },
            { "Left Middle Proximal", "middle1.L" }, { "Left Middle Intermediate", "middle2.L" },
            { "Left Ring Proximal", "ring1.L" }, { "Left Ring Intermediate", "ring2.L" },
            { "Left Little Proximal", "pinky1.L" }, { "Left Little Intermediate", "pinky2.L" },
            { "Right Thumb Proximal", "thumb.R" },
            { "Right Index Proximal", "index1.R" }, { "Right Index Intermediate", "index2.R" },
            { "Right Middle Proximal", "middle1.R" }, { "Right Middle Intermediate", "middle2.R" },
            { "Right Ring Proximal", "ring1.R" }, { "Right Ring Intermediate", "ring2.R" },
            { "Right Little Proximal", "pinky1.R" }, { "Right Little Intermediate", "pinky2.R" },
        };

        /// <summary>Clips that loop; the rest play once.</summary>
        static readonly HashSet<string> Looping = new HashSet<string> { "idle", "idle standing", "run", "standing run", "walk", "search" };

        [MenuItem("BodycamHorror/Set Up Manthing")]
        public static void SetUp()
        {
            BuildMaterial();
            Humanoid(ModelPath, ManthingMap, false);
            Humanoid(AnimPath, MetarigMap, true);
            AssetDatabase.SaveAssets();
            Debug.Log("Manthing set up: " + Report());
        }

        public static string Report()
        {
            var a = AssetDatabase.LoadAssetAtPath<Avatar>(ModelPath);
            var b = AssetDatabase.LoadAssetAtPath<Avatar>(AnimPath);
            var clips = new List<string>();
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(AnimPath))
                if (o is AnimationClip c && !c.name.StartsWith("__preview")) clips.Add(c.name + (c.isHumanMotion ? "" : "(not humanoid)"));
            return $"model avatar {(a ? a.isValid && a.isHuman : false)}, anim avatar {(b ? b.isValid && b.isHuman : false)}, clips {string.Join(", ", clips)}";
        }

        /// <summary>A clip of the monster by its name ("idle standing"), or null.</summary>
        public static AnimationClip Clip(string name)
        {
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(AnimPath))
                if (o is AnimationClip c && c.name == name) return c;
            return null;
        }

        static void BuildMaterial()
        {
            Texture2D T(string n) => AssetDatabase.LoadAssetAtPath<Texture2D>(Dir + "/Textures/Manthing_" + n + ".png");
            var normal = (TextureImporter)AssetImporter.GetAtPath(Dir + "/Textures/Manthing_Normal.png");
            if (normal.textureType != TextureImporterType.NormalMap) { normal.textureType = TextureImporterType.NormalMap; normal.SaveAndReimport(); }
            var occ = (TextureImporter)AssetImporter.GetAtPath(Dir + "/Textures/Manthing_Occlusion.png");
            if (occ.sRGBTexture) { occ.sRGBTexture = false; occ.SaveAndReimport(); }

            // URP wants smoothness in the alpha of a metallic map: built from the roughness the model keeps in an alpha
            string msPath = Dir + "/Textures/Manthing_MetallicSmoothness.png";
            if (!AssetDatabase.LoadAssetAtPath<Texture2D>(msPath))
            {
                var ri = (TextureImporter)AssetImporter.GetAtPath(Dir + "/Textures/Manthing_RoughnessAlpha.png");
                ri.isReadable = true; ri.sRGBTexture = false; ri.textureCompression = TextureImporterCompression.Uncompressed; ri.SaveAndReimport();
                var rough = T("RoughnessAlpha");
                var src = rough.GetPixels();
                var dst = new Color[src.Length];
                for (int i = 0; i < src.Length; i++) dst[i] = new Color(0f, 0f, 0f, 1f - src[i].a);
                var t = new Texture2D(rough.width, rough.height, TextureFormat.RGBA32, false, true);
                t.SetPixels(dst); t.Apply();
                System.IO.File.WriteAllBytes(msPath, t.EncodeToPNG());
                Object.DestroyImmediate(t);
                AssetDatabase.ImportAsset(msPath);
                var mi = (TextureImporter)AssetImporter.GetAtPath(msPath);
                mi.sRGBTexture = false; mi.SaveAndReimport();
                ri.isReadable = false; ri.SaveAndReimport();
            }

            var mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (!mat) { mat = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(mat, MaterialPath); }
            mat.SetTexture("_BaseMap", T("BaseColor")); mat.SetColor("_BaseColor", Color.white);
            mat.SetTexture("_BumpMap", T("Normal")); mat.EnableKeyword("_NORMALMAP");
            mat.SetTexture("_MetallicGlossMap", AssetDatabase.LoadAssetAtPath<Texture2D>(msPath)); mat.EnableKeyword("_METALLICSPECGLOSSMAP");
            mat.SetFloat("_Smoothness", 1f);
            mat.SetTexture("_OcclusionMap", T("Occlusion")); mat.EnableKeyword("_OCCLUSIONMAP");
            EditorUtility.SetDirty(mat);
        }

        static void Humanoid(string path, Dictionary<string, string> map, bool clips)
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath(path);
            // a plain import first, so the skeleton is read as it is in the file
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.avatarSetup = ModelImporterAvatarSetup.NoAvatar;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.importAnimation = clips;
            importer.SaveAndReimport();

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var inst = Object.Instantiate(prefab);
            inst.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            try
            {
                var bones = new Dictionary<string, Transform>();
                foreach (var t in inst.GetComponentsInChildren<Transform>(true)) bones[t.name] = t;
                TPose(bones, map);

                var human = new List<HumanBone>();
                foreach (var pair in map)
                {
                    if (!bones.ContainsKey(pair.Value)) { Debug.LogWarning($"[ManthingSetup] {path}: no bone {pair.Value} for {pair.Key}"); continue; }
                    var hb = new HumanBone { humanName = pair.Key, boneName = pair.Value };
                    hb.limit.useDefaultValues = true;
                    human.Add(hb);
                }
                var skeleton = new List<SkeletonBone>();
                foreach (var t in inst.GetComponentsInChildren<Transform>(true))
                    skeleton.Add(new SkeletonBone
                    {
                        name = t == inst.transform ? prefab.name : t.name,
                        position = t.localPosition, rotation = t.localRotation, scale = t.localScale
                    });

                var hd = importer.humanDescription;
                hd.human = human.ToArray();
                hd.skeleton = skeleton.ToArray();
                hd.upperArmTwist = 0.5f; hd.lowerArmTwist = 0.5f; hd.upperLegTwist = 0.5f; hd.lowerLegTwist = 0.5f;
                hd.armStretch = 0.05f; hd.legStretch = 0.05f; hd.feetSpacing = 0f;
                hd.hasTranslationDoF = false;
                importer.humanDescription = hd;
            }
            finally { Object.DestroyImmediate(inst); }

            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            if (clips)
            {
                var list = importer.defaultClipAnimations;
                foreach (var c in list)
                {
                    c.name = c.name.Contains("|") ? c.name.Substring(c.name.LastIndexOf('|') + 1) : c.name;
                    c.loopTime = Looping.Contains(c.name);
                    // in place: the script moves the monster
                    c.lockRootRotation = c.lockRootHeightY = c.lockRootPositionXZ = true;
                    c.keepOriginalOrientation = c.keepOriginalPositionY = c.keepOriginalPositionXZ = true;
                }
                importer.clipAnimations = list;
            }
            importer.SaveAndReimport();
        }

        /// <summary>
        /// Puts the skeleton in a T-pose (what Humanoid retargeting measures every pose against): legs straight
        /// down, arms straight out to the sides, hands and fingers along the arms. Neither rig was built in one.
        /// </summary>
        static void TPose(Dictionary<string, Transform> bones, Dictionary<string, string> map)
        {
            Transform B(string human) => map.TryGetValue(human, out var n) && bones.TryGetValue(n, out var t) ? t : null;
            // stand the body up first: a rig may rest lying down (the animation rig rests face down, on all fours).
            // The hips turn so the spine points up and the front faces +Z; everything else rides along.
            Transform hips = B("Hips");
            Vector3 up = (B("Head").position - hips.position).normalized;
            Vector3 right = Vector3.ProjectOnPlane(B("RightUpperLeg").position - B("LeftUpperLeg").position, up).normalized;
            Vector3 front = Vector3.Cross(right, up);
            hips.rotation = Quaternion.Inverse(Quaternion.LookRotation(front, up)) * hips.rotation;
            Vector3 left = Vector3.left;

            void Aim(Transform bone, Transform child, Vector3 dir)
            {
                if (!bone || !child) return;
                Vector3 now = child.position - bone.position;
                if (now.sqrMagnitude < 1e-8f) return;
                bone.rotation = Quaternion.FromToRotation(now, dir) * bone.rotation;
            }

            foreach (string side in new[] { "Left", "Right" })
            {
                Vector3 outward = side == "Left" ? left : -left;
                Aim(B(side + "UpperLeg"), B(side + "LowerLeg"), Vector3.down);
                Aim(B(side + "LowerLeg"), B(side + "Foot"), Vector3.down);
                Aim(B(side + "UpperArm"), B(side + "LowerArm"), outward);
                Aim(B(side + "LowerArm"), B(side + "Hand"), outward);
                Aim(B(side + "Hand"), B(side + " Middle Proximal"), outward);
                foreach (string f in new[] { "Index", "Middle", "Ring", "Little" })
                {
                    Aim(B($"{side} {f} Proximal"), B($"{side} {f} Intermediate"), outward);
                    Aim(B($"{side} {f} Intermediate"), B($"{side} {f} Distal"), outward);
                }
            }
        }
    }
}
