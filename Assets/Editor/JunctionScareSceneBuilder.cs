using System.IO;
using BodycamHorror.Enemies;
using BodycamHorror.Player;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace BodycamHorror.EditorTools
{
    /// <summary>
    /// Builds Assets/Scenes/JunctionScare.unity from the lighting test scene: a long corridor ending in a
    /// T. To the right of the T a short dead end, to the left a corridor running off into the dark. A
    /// third of the way before the T the player crosses a trigger, and a monster bursts out of the dead
    /// end and sprints across the T to the left, screaming, and is gone (RunnerScare).
    /// Menu: BodycamHorror > Build Junction Scare Scene.
    /// </summary>
    public static class JunctionScareSceneBuilder
    {
        const string ScenePath = "Assets/Scenes/JunctionScare.unity";
        const string SourceScenePath = "Assets/Scenes/LightingTest.unity";

        // metres. The corridor runs along +Z from the player's back wall to the T.
        const float Height = 3f;
        const float Wall = 0.3f;
        const float HalfWidth = 1.2f;       // corridor 2.4 m wide
        const float Length = 20f;           // from the back wall to the T
        const float CrossWidth = 2.6f;      // the T's crossing corridor
        const float RightStub = 3.6f;       // how far the dead end reaches right of the corridor's middle
        const float LeftReach = 18f;        // how far the left corridor runs before its end wall
        const float TriggerZ = 13f;         // a third of the way short of the T, as in the sketch
        const float PlayerZ = 1f;

        [MenuItem("BodycamHorror/Build Junction Scare Scene")]
        public static void Build()
        {
            const string mats = LightingTestSceneBuilder.MaterialDir, tex = LightingTestSceneBuilder.TextureDir;
            LightingTestSceneBuilder.wallPaint = EnvironmentTextures.ScannedMaterial("PaintedPlaster011", mats, tex, new Color(0.62f, 0.7f, 0.68f), 1.2f);
            LightingTestSceneBuilder.wallPlaster = EnvironmentTextures.ScannedMaterial("PaintedPlaster015", mats, tex, new Color(0.72f, 0.72f, 0.68f), 1.2f);
            Material floor = EnvironmentTextures.ScannedMaterial("Tiles110", mats, tex, new Color(0.6f, 0.6f, 0.58f), 1f);
            Material ceiling = EnvironmentTextures.ScannedMaterial("Plaster007", mats, tex, new Color(0.7f, 0.7f, 0.66f), 1.2f);

            EditorSceneManager.OpenScene(SourceScenePath, OpenSceneMode.Single);   // never copy over the scene that is open
            if (File.Exists(ScenePath))
            {
                File.Copy(SourceScenePath, ScenePath, true);
                AssetDatabase.ImportAsset(ScenePath, ImportAssetOptions.ForceUpdate);
            }
            else AssetDatabase.CopyAsset(SourceScenePath, ScenePath);
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            // keep the player and the grade; everything else (rooms, lamps, infected, ambience) goes
            var player = Object.FindAnyObjectByType<BodycamPlayerController>();
            var volume = Object.FindAnyObjectByType<Volume>();
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (player && root == player.gameObject) continue;
                if (volume && root == volume.gameObject) continue;
                Object.DestroyImmediate(root);
            }
            if (player) player.transform.SetPositionAndRotation(new Vector3(0f, player.transform.position.y, PlayerZ), Quaternion.identity);

            BuildShell(floor, ceiling);

            // light: a lamp half-way down the corridor and one over the T, so the run is seen; the left corridor stays dark
            var lampPrefab = LightingTestSceneBuilder.BuildLampPrefab();
            var lamps = new GameObject("Lamps").transform;
            LightingTestSceneBuilder.PlaceLamp(lampPrefab, lamps, "Lamp_Corridor", new Vector3(0f, Height, 7f), false);
            float crossZ = Length + CrossWidth * 0.5f;
            LightingTestSceneBuilder.PlaceLamp(lampPrefab, lamps, "Lamp_Junction", new Vector3(0.3f, Height, crossZ), false);

            PlaceScare(lamps.Find("Lamp_Junction"));

            GameLook.ApplyAtmosphere();
            foreach (AutoExposure exposure in Object.FindObjectsByType<AutoExposure>(FindObjectsInactive.Include))
            {
                exposure.maxStops = GameLook.MaxStops;
                EditorUtility.SetDirty(exposure);
            }
            LightingTestSceneBuilder.AddDust();
            AddAmbience();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log("Junction scare scene built: " + ScenePath);
        }

        static void BuildShell(Material floor, Material ceiling)
        {
            const float tile = 2f, ceilingTile = 2.5f;
            float hw = HalfWidth, z1 = Length, z2 = Length + CrossWidth;
            float right = RightStub, left = -LeftReach;
            var shell = new GameObject("Environment").transform;

            // the long corridor
            var corridor = new GameObject("Corridor").transform;
            corridor.SetParent(shell, false);
            LightingTestSceneBuilder.Box("Floor", corridor, new Vector3(-hw, -Wall, -Wall), new Vector3(hw, 0f, z1), floor, tile, tile);
            LightingTestSceneBuilder.Box("Ceiling", corridor, new Vector3(-hw, Height, -Wall), new Vector3(hw, Height + Wall, z1), ceiling, ceilingTile, ceilingTile);
            LightingTestSceneBuilder.WallBox("Wall_Back", corridor, new Vector3(-hw - Wall, 0f, -Wall), new Vector3(hw + Wall, Height, 0f));
            LightingTestSceneBuilder.WallBox("Wall_Left", corridor, new Vector3(-hw - Wall, 0f, 0f), new Vector3(-hw, Height, z1));
            LightingTestSceneBuilder.WallBox("Wall_Right", corridor, new Vector3(hw, 0f, 0f), new Vector3(hw + Wall, Height, z1));

            // the crossing: a dead end to the right, a corridor into the dark to the left
            var cross = new GameObject("Crossing").transform;
            cross.SetParent(shell, false);
            LightingTestSceneBuilder.Box("Floor", cross, new Vector3(left, -Wall, z1), new Vector3(right, 0f, z2), floor, tile, tile);
            LightingTestSceneBuilder.Box("Ceiling", cross, new Vector3(left, Height, z1), new Vector3(right, Height + Wall, z2), ceiling, ceilingTile, ceilingTile);
            LightingTestSceneBuilder.WallBox("Wall_Far", cross, new Vector3(left - Wall, 0f, z2), new Vector3(right + Wall, Height, z2 + Wall));
            LightingTestSceneBuilder.WallBox("Wall_Near_Left", cross, new Vector3(left - Wall, 0f, z1 - Wall), new Vector3(-hw, Height, z1));
            LightingTestSceneBuilder.WallBox("Wall_Near_Right", cross, new Vector3(hw, 0f, z1 - Wall), new Vector3(right + Wall, Height, z1));
            LightingTestSceneBuilder.WallBox("Wall_DeadEnd", cross, new Vector3(right, 0f, z1), new Vector3(right + Wall, Height, z2));
            LightingTestSceneBuilder.WallBox("Wall_LeftEnd", cross, new Vector3(left - Wall, 0f, z1), new Vector3(left, Height, z2));
        }

        static void PlaceScare(Transform junctionLamp)
        {
            var go = new GameObject("RunnerScare");
            go.transform.position = new Vector3(0f, 1.5f, TriggerZ);
            var scare = go.AddComponent<RunnerScare>();
            scare.triggerSize = new Vector3(HalfWidth * 2f, 3f, 0.8f);

            float crossZ = Length + CrossWidth * 0.5f;
            scare.from = new Vector3(RightStub - 0.5f, 0f, crossZ);     // pressed into the dead end, out of sight behind the corner
            scare.to = new Vector3(-LeftReach + 1.5f, 0f, crossZ);      // far down the dark left corridor
            scare.speed = 9.5f;

            scare.modelPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Zombie/animations/MutantLow.fbx");
            scare.modelMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Zombie/MutantMaterial.mat");
            scare.modelScale = 0.7f;
            foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath("Assets/Zombie_Animations/Animations/Zombie_Sprint_01_Forward_InPlace.fbx"))
                if (asset is AnimationClip clip && !clip.name.StartsWith("__preview__")) { scare.runClip = clip; break; }

            scare.scream = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/Monster/monster_scream.wav");
            var steps = new System.Collections.Generic.List<AudioClip>();
            foreach (string guid in AssetDatabase.FindAssets("t:AudioClip", new[] { "Assets/Resources/Audio/Footsteps" }))
                steps.Add(AssetDatabase.LoadAssetAtPath<AudioClip>(AssetDatabase.GUIDToAssetPath(guid)));
            scare.steps = steps.ToArray();
            if (junctionLamp) scare.flickerLight = junctionLamp.GetComponentInChildren<Light>();
            EditorUtility.SetDirty(scare);
        }

        /// <summary>The same sound of the place as the lighting test: a low bed, the lamps humming, water
        /// dripping somewhere down the dark left corridor, the building creaking.</summary>
        static void AddAmbience()
        {
            AmbienceBuilder.ImportSettings();
            var root = new GameObject("Ambience").transform;
            AmbienceBuilder.Loop(root, "Bed_Drone", Vector3.zero, 0.22f, 0f, 0f, 0f, "loop_bed_drone");
            AmbienceBuilder.Loop(root, "Bed_Hum", Vector3.zero, 0.07f, 0f, 0f, 0f, "loop_bed_hum");
            var lamps = GameObject.Find("Lamps");
            if (lamps) foreach (Transform lamp in lamps.transform)
                AmbienceBuilder.Loop(lamp, "Hum", Vector3.up * -0.6f, 0.18f, 1f, 0.4f, 5f, "loop_hum_bulb");
            float crossZ = Length + CrossWidth * 0.5f;
            AmbienceBuilder.Shots(root, "Drip_LeftCorridor", new Vector3(-9f, Height - 0.2f, crossZ), 0.4f, new Vector2(0.9f, 3.5f), 0f, 1f, 16f,
                                  AmbienceBuilder.Named("drip_"));
            AmbienceBuilder.Shots(root, "Creaks", new Vector3(0f, Height + 0.5f, 10f), 0.4f, new Vector2(14f, 34f), 8f, 2f, 35f,
                                  AmbienceBuilder.Named("creak_"));
        }
    }
}
