using System.IO;
using BodycamHorror.Player;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace BodycamHorror.EditorTools
{
    /// <summary>
    /// Puts the player's own body (PlayerBody) on the player in every scene.
    /// With Mixamo files in Assets/Character/SWAT it uses those: the character (the file with a mesh in it) and
    /// the clips, matched to slots by their names; the clips are set to loop and imported as Humanoid. Without
    /// them it falls back to the infected's model and walk / run / idle clips, so the system can be tried out.
    /// Menu: BodycamHorror > Set Up Player Body.
    /// </summary>
    public static class PlayerBodySetup
    {
        const string SwatDir = "Assets/Character/SWAT";

        [MenuItem("BodycamHorror/Set Up Player Body")]
        public static void SetUpAllScenes()
        {
            var config = Prepare(out string source);
            string open = EditorSceneManager.GetActiveScene().path;
            int count = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:Scene", new[] { "Assets/Scenes" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                foreach (var player in Object.FindObjectsByType<BodycamPlayerController>(FindObjectsInactive.Include))
                {
                    var body = player.GetComponent<PlayerBody>() ?? player.gameObject.AddComponent<PlayerBody>();
                    config(body);
                    // where it sits relative to the camera, and what is folded away (scenes keep their own copies)
                    body.neckBehind = 0.26f;
                    body.neckBelow = 0.12f;
                    body.hiddenBones = new[] { HumanBodyBones.Head, HumanBodyBones.LeftShoulder, HumanBodyBones.RightShoulder };
                    body.stencilShader = Shader.Find("Hidden/BodycamHorror/ViewModelStencil");
                    body.diveLandTime = 0.45f;
                    player.maxPitchDown = 72f;
                    EditorUtility.SetDirty(player);
                    EditorUtility.SetDirty(body);
                    count++;
                }
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }
            if (!string.IsNullOrEmpty(open)) EditorSceneManager.OpenScene(open, OpenSceneMode.Single);
            Debug.Log($"Player body ({source}) set up on {count} players.");
        }

        /// <summary>Works out the model and clips, and returns what to apply to each PlayerBody.</summary>
        static System.Action<PlayerBody> Prepare(out string source)
        {
            if (Directory.Exists(SwatDir) && AssetDatabase.FindAssets("t:Model", new[] { SwatDir }).Length > 0)
            {
                source = "SWAT";
                return PrepareSwat();
            }
            source = "infected placeholder";
            var model = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Zombie/animations/MutantLow.fbx");
            var mat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Zombie/MutantMaterial.mat");
            AnimationClip Clip(string file) => FirstClip("Assets/Zombie_Animations/Animations/" + file);
            var idle = Clip("Zombie_Idle_01.FBX");
            var walk = Clip("Zombie_Walk_01_Forward_InPlace.fbx");
            var run = Clip("Zombie_Run_01_Forward_InPlace.fbx");
            return b =>
            {
                b.modelPrefab = model; b.material = mat;
                b.idle = idle; b.walkForward = walk; b.run = run;
                b.walkBackward = b.strafeLeft = b.strafeRight = b.crouchIdle = b.crouchWalk = b.fall = b.slide = b.lying = null;
            };
        }

        static System.Action<PlayerBody> PrepareSwat()
        {
            // the model: the file with a skinned mesh in it
            GameObject model = null;
            foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { SwatDir }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (go && go.GetComponentInChildren<SkinnedMeshRenderer>())
                {
                    SetHumanoid((ModelImporter)AssetImporter.GetAtPath(path), null);
                    model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    break;
                }
            }
            if (!model) { Debug.LogError("No skinned model found in " + SwatDir); return b => { }; }
            var avatar = AssetDatabase.LoadAssetAtPath<Avatar>(AssetDatabase.GetAssetPath(model));

            // the clips: humanoid .anim assets (copied out of the Mixamo files), and any other Mixamo files left beside them
            var clips = new System.Collections.Generic.Dictionary<string, AnimationClip>();
            foreach (string guid in AssetDatabase.FindAssets("t:AnimationClip", new[] { SwatDir }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.EndsWith(".anim")) continue;
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                if (clip) clips[Path.GetFileNameWithoutExtension(path).ToLowerInvariant()] = clip;
            }
            foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { SwatDir }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path == AssetDatabase.GetAssetPath(model)) continue;
                SetHumanoid((ModelImporter)AssetImporter.GetAtPath(path), avatar);
                string name = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
                if (name.Contains("@")) name = name.Substring(name.IndexOf('@') + 1);
                var clip = FirstClip(path);
                if (clip && !clips.ContainsKey(name)) clips[name] = clip;
            }

            AnimationClip Find(params string[][] wanted)
            {
                // the first clip whose name has every word of one of the wanted sets, and none of the excluded ("-word")
                foreach (var set in wanted)
                    foreach (var pair in clips)
                    {
                        bool ok = true;
                        foreach (var w in set)
                            if (w.StartsWith("-") ? pair.Key.Contains(w.Substring(1)) : !pair.Key.Contains(w)) { ok = false; break; }
                        if (ok) return pair.Value;
                    }
                return null;
            }
            var idle = Find(new[] { "idle", "-crouch", "-fall" });
            var walkF = Find(new[] { "walk", "-back", "-crouch", "-strafe", "-left", "-right" });
            var walkB = Find(new[] { "walk", "back", "-crouch" }, new[] { "backward" });
            var left = Find(new[] { "strafe", "left" }, new[] { "walk", "left" });
            var right = Find(new[] { "strafe", "right" }, new[] { "walk", "right" });
            var run = Find(new[] { "run", "-slide" }, new[] { "sprint" });
            var crouchIdle = Find(new[] { "crouch", "idle" });
            var crouchWalk = Find(new[] { "crouch", "walk", "-right", "-left", "-back" }, new[] { "crouch", "walk" });
            var fall = Find(new[] { "falling", "idle" }, new[] { "fall", "-back", "-flat" }, new[] { "jump" });
            var slide = Find(new[] { "slide" });
            var lying = Find(new[] { "fall", "back" }, new[] { "hit", "back" }, new[] { "flat" }, new[] { "lying" });
            Debug.Log($"SWAT clips: idle {N(idle)}, walk {N(walkF)}, back {N(walkB)}, left {N(left)}, right {N(right)}, run {N(run)}, "
                      + $"crouch {N(crouchIdle)}/{N(crouchWalk)}, fall {N(fall)}, slide {N(slide)}, lying {N(lying)}");
            return b =>
            {
                b.modelPrefab = model; b.material = null;
                b.idle = idle; b.walkForward = walkF; b.walkBackward = walkB; b.strafeLeft = left; b.strafeRight = right;
                b.run = run; b.crouchIdle = crouchIdle; b.crouchWalk = crouchWalk; b.fall = fall; b.slide = slide; b.lying = lying;
            };
        }

        static string N(AnimationClip c) => c ? c.name : "-";

        static void SetHumanoid(ModelImporter importer, Avatar source)
        {
            bool dirty = importer.animationType != ModelImporterAnimationType.Human;
            importer.animationType = ModelImporterAnimationType.Human;
            if (source)
            {
                if (importer.avatarSetup != ModelImporterAvatarSetup.CopyFromOther || importer.sourceAvatar != source) dirty = true;
                importer.avatarSetup = ModelImporterAvatarSetup.CopyFromOther;
                importer.sourceAvatar = source;
                // loop everything except the one-shots
                var clips = importer.defaultClipAnimations;
                foreach (var c in clips)
                {
                    string n = Path.GetFileNameWithoutExtension(importer.assetPath).ToLowerInvariant();
                    bool once = n.Contains("fall") && (n.Contains("back") || n.Contains("flat")) || n.Contains("slide") || n.Contains("jump");
                    c.loopTime = !once;
                    c.lockRootRotation = c.lockRootHeightY = c.lockRootPositionXZ = true;
                    c.keepOriginalOrientation = c.keepOriginalPositionY = c.keepOriginalPositionXZ = true;
                }
                importer.clipAnimations = clips;
                dirty = true;
            }
            if (dirty) importer.SaveAndReimport();
        }

        static AnimationClip FirstClip(string path)
        {
            foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path))
                if (asset is AnimationClip clip && !clip.name.StartsWith("__preview__")) return clip;
            return null;
        }
    }
}
