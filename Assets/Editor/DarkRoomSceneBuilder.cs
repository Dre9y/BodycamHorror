using System.IO;
using BodycamHorror.Enemies;
using BodycamHorror.Player;
using BodycamHorror.Weapons;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace BodycamHorror.EditorTools
{
    /// <summary>
    /// Builds Assets/Scenes/DarkRoom.unity: a copy of the lighting test scene cut down to one closed
    /// room with no lamps at all. The player starts with empty hands and the flashlight off; a pistol
    /// lies on the floor a little this side of the middle, marked by a faint glow of its own, and is
    /// picked up with the use key (WeaponPickup).
    /// Menu: BodycamHorror > Build Dark Room Scene.
    /// </summary>
    public static class DarkRoomSceneBuilder
    {
        const string ScenePath = "Assets/Scenes/DarkRoom.unity";
        const string SourceScenePath = "Assets/Scenes/LightingTest.unity";

        // metres; the room runs from the player's back wall along +Z
        const float Size = 10f;
        const float Height = 3f;
        const float Wall = 0.3f;
        const float PlayerZ = 0.9f;
        const float PistolZ = 4.4f;   // the middle is at 5: a little nearer the player

        [MenuItem("BodycamHorror/Build Dark Room Scene")]
        public static void Build()
        {
            const string mats = LightingTestSceneBuilder.MaterialDir, tex = LightingTestSceneBuilder.TextureDir;
            LightingTestSceneBuilder.wallPaint = EnvironmentTextures.ScannedMaterial("PaintedPlaster011", mats, tex, new Color(0.62f, 0.7f, 0.68f), 1.2f);
            LightingTestSceneBuilder.wallPlaster = EnvironmentTextures.ScannedMaterial("PaintedPlaster015", mats, tex, new Color(0.72f, 0.72f, 0.68f), 1.2f);
            Material floor = EnvironmentTextures.ScannedMaterial("Tiles110", mats, tex, new Color(0.6f, 0.6f, 0.58f), 1f);
            Material ceiling = EnvironmentTextures.ScannedMaterial("Plaster007", mats, tex, new Color(0.7f, 0.7f, 0.66f), 1.2f);

            // a copy of the lighting test scene: the player, the grade and the render settings come along
            EditorSceneManager.OpenScene(SourceScenePath, OpenSceneMode.Single);   // never copy over the scene that is open
            if (File.Exists(ScenePath))
            {
                File.Copy(SourceScenePath, ScenePath, true);   // written over the old file: the scene keeps its GUID
                AssetDatabase.ImportAsset(ScenePath, ImportAssetOptions.ForceUpdate);
            }
            else AssetDatabase.CopyAsset(SourceScenePath, ScenePath);
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            var player = Object.FindAnyObjectByType<BodycamPlayerController>();
            var volume = Object.FindAnyObjectByType<Volume>();
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (player && root == player.gameObject) continue;
                if (volume && root == volume.gameObject) continue;
                Object.DestroyImmediate(root);   // corridor, rooms, lamps, infected: all of it
            }

            Pistol pistol = null;
            if (player)
            {
                player.transform.SetPositionAndRotation(new Vector3(0f, player.transform.position.y, PlayerZ), Quaternion.identity);

                var shooting = player.GetComponentInChildren<PlayerShooting>(true);
                if (shooting)
                {
                    shooting.startUnarmed = true;
                    EditorUtility.SetDirty(shooting);
                    foreach (Firearm w in shooting.weapons) if (w is Pistol p) pistol = p;
                }
                var flashlight = player.GetComponentInChildren<BodycamFlashlight>(true);
                if (flashlight)
                {
                    flashlight.startOn = false;   // the room is meant to be dark; F still switches it on
                    EditorUtility.SetDirty(flashlight);
                }
            }

            if (volume) volume.sharedProfile = GameLook.BuildProfile();   // the shared look, which was worked out in this room
            // the camera must not open up to "see" in the dark: that is what turns black into grey
            foreach (AutoExposure exposure in Object.FindObjectsByType<AutoExposure>(FindObjectsInactive.Include))
            {
                exposure.maxStops = GameLook.MaxStops;
                EditorUtility.SetDirty(exposure);
            }

            BuildRoom(floor, ceiling);
            PlacePistol(pistol, new Vector3(0f, 0f, PistolZ));

            // no lamps, and next to no fill: what shows, shows by the pistol's glow
            GameLook.ApplyAtmosphere();

            LightingTestSceneBuilder.AddDust();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log("Dark room scene built: " + ScenePath);
        }

        /// <summary>Puts the pickup scare into the scene that is open now: taking the gun switches the
        /// flashlight off, the light comes on for two seconds over a ring of infected, then all of it is gone.</summary>
        [MenuItem("BodycamHorror/Add Pickup Scare To Open Scene")]
        public static void AddPickupScare()
        {
            var pickup = Object.FindAnyObjectByType<WeaponPickup>();
            if (!pickup)
            {
                Debug.LogWarning("No WeaponPickup in the open scene: nothing to hang the scare on.");
                return;
            }
            pickup.flashlightOff = true;
            EditorUtility.SetDirty(pickup);

            var scare = Object.FindAnyObjectByType<PickupScare>();
            if (!scare) scare = new GameObject("PickupScare").AddComponent<PickupScare>();
            scare.pickup = pickup;
            scare.lightHeight = Height - 0.25f;
            // the same infected the lighting test scene uses
            scare.modelPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Zombie/animations/MutantLow.fbx");
            scare.modelMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Zombie/MutantMaterial.mat");
            scare.modelScale = 0.7f;
            scare.animatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>("Assets/Zombie/ZombieAnimator.controller");
            var idles = new System.Collections.Generic.List<AnimationClip>();
            foreach (string file in new[] { "Zombie_Idle_01", "Zombie_Idle_02", "Zombie_Idle_03" })
                foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath("Assets/Zombie_Animations/Animations/" + file + ".FBX"))
                    if (asset is AnimationClip clip && !clip.name.StartsWith("__preview__")) { idles.Add(clip); break; }
            scare.idleVariants = idles.ToArray();
            EditorUtility.SetDirty(scare);

            EditorSceneManager.MarkSceneDirty(scare.gameObject.scene);
            EditorSceneManager.SaveScene(scare.gameObject.scene);
            Debug.Log("Pickup scare added to " + scare.gameObject.scene.path);
        }

        static void BuildRoom(Material floor, Material ceiling)
        {
            const float tile = 2f, ceilingTile = 2.5f;
            float half = Size * 0.5f;
            var room = new GameObject("DarkRoom").transform;
            LightingTestSceneBuilder.Box("Floor", room, new Vector3(-half, -Wall, 0f), new Vector3(half, 0f, Size), floor, tile, tile);
            LightingTestSceneBuilder.Box("Ceiling", room, new Vector3(-half, Height, 0f), new Vector3(half, Height + Wall, Size), ceiling, ceilingTile, ceilingTile);
            LightingTestSceneBuilder.WallBox("Wall_Back", room, new Vector3(-half - Wall, 0f, -Wall), new Vector3(half + Wall, Height, 0f));
            LightingTestSceneBuilder.WallBox("Wall_Far", room, new Vector3(-half - Wall, 0f, Size), new Vector3(half + Wall, Height, Size + Wall));
            LightingTestSceneBuilder.WallBox("Wall_Left", room, new Vector3(-half - Wall, 0f, 0f), new Vector3(-half, Height, Size));
            LightingTestSceneBuilder.WallBox("Wall_Right", room, new Vector3(half, 0f, 0f), new Vector3(half + Wall, Height, Size));
        }

        /// <summary>The pistol on the floor, lying on its side, with a small light of its own over it.</summary>
        static void PlacePistol(Pistol pistol, Vector3 floorPoint)
        {
            var root = new GameObject("Pickup_Pistol");
            root.transform.position = floorPoint;
            var pickup = root.AddComponent<WeaponPickup>();
            pickup.weapon = WeaponPickup.Kind.Pistol;

            // the look of the gun the player will end up holding: its built model, or the imported one
            GameObject model = null;
            Transform built = pistol ? pistol.transform.Find("Model") : null;
            if (built) model = Object.Instantiate(built.gameObject);
            else if (pistol && pistol.modelPrefab)
            {
                model = Object.Instantiate(pistol.modelPrefab);
                model.transform.localScale = Vector3.one * pistol.modelScale;
            }
            if (!model)
            {
                model = GameObject.CreatePrimitive(PrimitiveType.Cube);
                model.transform.localScale = new Vector3(0.03f, 0.13f, 0.19f);
            }
            model.name = "Model";
            model.SetActive(true);
            model.transform.SetParent(root.transform, false);
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.Euler(0f, 38f, 90f);   // on its side, askew
            foreach (Collider c in model.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
            foreach (Transform t in model.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 0;

            var renderers = model.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length > 0)
            {
                Bounds bounds = renderers[0].bounds;
                foreach (Renderer r in renderers)
                {
                    bounds.Encapsulate(r.bounds);
                    r.renderingLayerMask = ViewModelLighting.WorldLayer;   // a thing in the world now, lit like the room
                    r.shadowCastingMode = ShadowCastingMode.On;
                }
                // flat on the floor, the middle of it over the pickup point
                model.transform.position += new Vector3(floorPoint.x - bounds.center.x, floorPoint.y + 0.004f - bounds.min.y, floorPoint.z - bounds.center.z);
            }

            // the glow: low over the gun, a pool of cold light about a metre across on the tiles
            var glowObject = new GameObject("Glow");
            glowObject.transform.SetParent(root.transform, false);
            glowObject.transform.localPosition = new Vector3(0f, 0.4f, 0f);
            var glow = glowObject.AddComponent<Light>();
            glow.type = LightType.Point;
            glow.color = new Color(0.72f, 0.86f, 1f);
            glow.intensity = 0.3f;
            glow.range = 1.9f;
            glow.shadows = LightShadows.Soft;
            glow.shadowNearPlane = 0.05f;
            ViewModelLighting.AlsoLightViewModel(glow);
            var haze = glowObject.AddComponent<BodycamHorror.Environment.LightHaze>();   // a faint dome of lit air
            haze.material = AssetDatabase.LoadAssetAtPath<Material>(LightingTestSceneBuilder.MaterialDir + "/LightHaze.mat");
            haze.density = 0.012f;
            haze.wisps = 0.3f;
            pickup.glow = glow;
        }
    }
}
