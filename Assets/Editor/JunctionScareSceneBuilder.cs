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
    /// quarter of the way before the T the player crosses a trigger, and the crying head shrieks and
    /// scuttles out of the dead end, up the T's far wall and away over the ceiling to the left (RunnerScare).
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
        const float TriggerZ = 15f;         // a quarter of the way short of the T
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
            ConfigureHeadCrawl(scare, junctionLamp);
        }

        /// <summary>
        /// The crying head as the scare: it scuttles out of the dead end along the floor at the foot of the
        /// T's far wall, climbs that wall up and to the left in a curve, goes over onto the ceiling and away
        /// along it into the dark left corridor. Path points are where it touches, normals the surface's up.
        /// </summary>
        static void ConfigureHeadCrawl(RunnerScare scare, Transform junctionLamp)
        {
            scare.transform.position = new Vector3(0f, 1.5f, TriggerZ);
            scare.triggerSize = new Vector3(HalfWidth * 2f, 3f, 0.8f);

            float farWall = Length + CrossWidth;   // the far wall's face
            Vector3 up = Vector3.up, outOfWall = Vector3.back, down = Vector3.down;
            Vector3 floorToWall = (up + outOfWall).normalized, wallToCeiling = (down + outOfWall).normalized;
            scare.path = new[]
            {
                // the floor: out of the dead end, hugging the far wall
                new Vector3(RightStub - 0.4f, 0f, farWall - 0.9f),
                new Vector3(2.0f, 0f, farWall - 0.55f),
                new Vector3(1.0f, 0f, farWall - 0.25f),
                // round the foot of the wall and up it, curving to the left
                new Vector3(0.75f, 0.2f, farWall - 0.05f),
                new Vector3(0.5f, 0.75f, farWall),
                new Vector3(0.15f, 1.6f, farWall),
                new Vector3(-0.3f, 2.35f, farWall),
                // over the top onto the ceiling
                new Vector3(-0.6f, Height - 0.1f, farWall - 0.15f),
                new Vector3(-1.1f, Height, farWall - 0.7f),
                // along the ceiling, away down the dark left corridor
                new Vector3(-2.4f, Height, farWall - 1.2f),
                new Vector3(-4.5f, Height, farWall - 1.3f),
                new Vector3(-10f, Height, farWall - 1.3f),
            };
            scare.normals = new[]
            {
                up, up, up,
                floorToWall, outOfWall, outOfWall, outOfWall,
                wallToCeiling, down,
                down, down, down,
            };
            scare.from = scare.path[0];
            scare.to = scare.path[scare.path.Length - 1];
            scare.speed = 5.5f;
            scare.alignSharpness = 10f;
            scare.delay = 0.05f;

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Enemies/CryingHead.prefab");
            var head = prefab ? prefab.GetComponent<CryingHead>() : null;
            scare.modelPrefab = prefab;
            scare.modelMaterial = null;           // the prefab's own
            scare.modelScale = 1.3f;              // a little bigger than the walking one, so it reads from down the corridor
            scare.stripBehaviours = true;
            scare.runClip = head ? head.moveClip : null;
            scare.animationSpeed = head ? scare.speed / Mathf.Max(head.strideSpeed * scare.modelScale, 0.1f) : 2f;

            scare.scream = null;                  // no scream: the crying head is only heard when it attacks
            scare.screamVolume = 1f;
            scare.screamPitch = head ? head.shriekPitch : 1.55f;
            scare.steps = new AudioClip[0];
            scare.proceduralSkitter = true;       // fingers and tendrils on tile and plaster
            scare.stepInterval = 0.07f;
            scare.stepPitch = 1f;
            scare.stepVolume = 0.7f;
            scare.scurryLoop = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/Monster/creature_scurry_loop.wav");
            if (junctionLamp) scare.flickerLight = junctionLamp.GetComponentInChildren<Light>();
            EditorUtility.SetDirty(scare);
            EditorUtility.SetDirty(scare.transform);
        }

        /// <summary>Puts the crying head into the scene as it is now (no rebuild): reconfigures its RunnerScare.</summary>
        [MenuItem("BodycamHorror/Junction Scare: Use Crying Head")]
        public static void UseCryingHead()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var scare = Object.FindAnyObjectByType<RunnerScare>();
            if (!scare)
            {
                scare = new GameObject("RunnerScare").AddComponent<RunnerScare>();
                Undo.RegisterCreatedObjectUndo(scare.gameObject, "Crying head scare");
            }
            var lamp = GameObject.Find("Lamps/Lamp_Junction");
            ConfigureHeadCrawl(scare, lamp ? lamp.transform : null);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("Junction scare now uses the crying head: " + ScenePath);
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
