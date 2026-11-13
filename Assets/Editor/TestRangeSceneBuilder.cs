using System.IO;
using BodycamHorror.Combat;
using BodycamHorror.Player;
using BodycamHorror.Weapons;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace BodycamHorror.EditorTools
{
    /// <summary>
    /// Builds Assets/Scenes/TestRange.unity: a long closed hall to try things out in. The player starts
    /// empty-handed and without a flashlight (F does nothing); on the floor in front of them lie a pistol, the pistol's weapon light and a shotgun,
    /// each marked by its own faint glow (WeaponPickup). Down range stand pop-up targets at 6, 10, 15 and
    /// 21 m, each under a dim lamp. On the left wall by the start runs a pipe with a burst joint blowing steam (it kills) and the valve that shuts it off. A copy of the lighting test scene: the player and the grade come along.
    /// Menu: BodycamHorror > Build Test Range Scene.
    /// </summary>
    public static class TestRangeSceneBuilder
    {
        const string ScenePath = "Assets/Scenes/TestRange.unity";
        const string SourceScenePath = "Assets/Scenes/LightingTest.unity";

        // metres; the hall runs from the player's back wall along +Z
        const float Width = 12f;
        const float Length = 26f;
        const float Height = 3.5f;
        const float Wall = 0.3f;
        const float PlayerZ = 1.2f;
        const float ItemsZ = 3.2f;

        [MenuItem("BodycamHorror/Build Test Range Scene")]
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
                Object.DestroyImmediate(root);
            }

            Pistol pistol = null;
            Shotgun shotgun = null;
            if (player)
            {
                player.transform.SetPositionAndRotation(new Vector3(0f, player.transform.position.y, PlayerZ), Quaternion.identity);
                var shooting = player.GetComponentInChildren<PlayerShooting>(true);
                if (shooting)
                {
                    shooting.startUnarmed = true;
                    EditorUtility.SetDirty(shooting);
                    pistol = shooting.FindWeapon<Pistol>();
                    shotgun = shooting.FindWeapon<Shotgun>();
                }
                if (pistol)
                {
                    pistol.startWithLight = false;   // its light lies next to it
                    EditorUtility.SetDirty(pistol);
                }
                var flashlight = player.GetComponentInChildren<BodycamFlashlight>(true);
                if (flashlight)
                {
                    flashlight.owned = false;   // no light at all until the one on the floor is picked up
                    flashlight.startOn = false;
                    EditorUtility.SetDirty(flashlight);
                }
                if (!pistol) Debug.LogWarning("Test range: the player has no pistol, its pickup will do nothing.");
                if (!shotgun) Debug.LogWarning("Test range: the player has no shotgun, its pickup will do nothing.");
            }

            if (volume) volume.sharedProfile = GameLook.BuildProfile();
            foreach (AutoExposure exposure in Object.FindObjectsByType<AutoExposure>(FindObjectsInactive.Include))
            {
                exposure.maxStops = GameLook.MaxStops;
                EditorUtility.SetDirty(exposure);
            }

            BuildHall(floor, ceiling);
            var items = new GameObject("Pickups").transform;
            DarkRoomSceneBuilder.PlaceGunPickup(pistol, WeaponPickup.Kind.Pistol, new Vector3(-1.4f, 0f, ItemsZ), Quaternion.Euler(0f, 38f, 90f))
                .transform.SetParent(items, true);
            PlaceWeaponLight(new Vector3(0f, 0f, ItemsZ)).transform.SetParent(items, true);
            DarkRoomSceneBuilder.PlaceGunPickup(shotgun, WeaponPickup.Kind.Shotgun, new Vector3(1.5f, 0f, ItemsZ + 0.2f), Quaternion.Euler(0f, -24f, 90f))
                .transform.SetParent(items, true);

            var targets = new GameObject("Targets").transform;
            PlaceTarget(targets, new Vector3(-2.5f, 0f, 6f));
            PlaceTarget(targets, new Vector3(1.5f, 0f, 10f));
            PlaceTarget(targets, new Vector3(-0.5f, 0f, 15f));
            PlaceTarget(targets, new Vector3(3f, 0f, 21f));
            PlaceTarget(targets, new Vector3(-3.5f, 0f, 21f));

            PlaceValve();

            GameLook.ApplyAtmosphere();
            PlaceLamps(targets);
            LightingTestSceneBuilder.AddDust();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log("Test range scene built: " + ScenePath);
        }

        /// <summary>A pipe with a hand-wheel valve on the left wall, a few steps from the start (Valve). Its far
        /// joint has burst: a jet of steam blows across the floor there until the valve is shut (SteamLeak).
        /// A dim lamp over it, so the steam can be seen before it is walked into.</summary>
        public static GameObject PlaceValve()
        {
            var group = new GameObject("SteamPipe").transform;
            GameObject valve = ValveBuilder.Place(new Vector3(-Width * 0.5f, 1.15f, ItemsZ + 0.6f), Vector3.right, group);
            valve.name = "PipeValve";
            GameObject steam = ValveBuilder.PlaceSteam(valve, -1, group);   // the joint further down the hall

            Vector3 aim = steam.transform.position + Vector3.right * 0.8f;
            Vector3 from = new Vector3(aim.x + 1.6f, Height - 0.05f, aim.z - 0.8f);
            var lamp = new GameObject("Lamp_SteamPipe");
            lamp.transform.SetParent(group, false);
            lamp.transform.SetPositionAndRotation(from, Quaternion.LookRotation(aim - from));
            var light = lamp.AddComponent<Light>();
            light.type = LightType.Spot;
            light.color = new Color(1f, 0.86f, 0.68f);
            light.intensity = 3f;
            light.range = 6f;
            light.spotAngle = 80f;
            light.innerSpotAngle = 35f;
            light.shadows = LightShadows.Soft;
            return group.gameObject;
        }

        static void BuildHall(Material floor, Material ceiling)
        {
            const float tile = 2f, ceilingTile = 2.5f;
            float half = Width * 0.5f;
            var hall = new GameObject("TestRange").transform;
            LightingTestSceneBuilder.Box("Floor", hall, new Vector3(-half, -Wall, 0f), new Vector3(half, 0f, Length), floor, tile, tile);
            LightingTestSceneBuilder.Box("Ceiling", hall, new Vector3(-half, Height, 0f), new Vector3(half, Height + Wall, Length), ceiling, ceilingTile, ceilingTile);
            LightingTestSceneBuilder.WallBox("Wall_Back", hall, new Vector3(-half - Wall, 0f, -Wall), new Vector3(half + Wall, Height, 0f));
            LightingTestSceneBuilder.WallBox("Wall_Far", hall, new Vector3(-half - Wall, 0f, Length), new Vector3(half + Wall, Height, Length + Wall));
            LightingTestSceneBuilder.WallBox("Wall_Left", hall, new Vector3(-half - Wall, 0f, 0f), new Vector3(-half, Height, Length));
            LightingTestSceneBuilder.WallBox("Wall_Right", hall, new Vector3(half, 0f, 0f), new Vector3(half + Wall, Height, Length));
        }

        /// <summary>The pistol's weapon light lying on the floor, the same little body that ends up under the barrel.</summary>
        static WeaponPickup PlaceWeaponLight(Vector3 floorPoint)
        {
            var color = new Color(0.06f, 0.06f, 0.065f);
            Material body = LightingTestSceneBuilder.SimpleMaterial("WeaponLightBody", color, 0.6f, 0.45f);
            // left switched on: the lens glows and it throws its beam along the floor
            var beamColor = new Color(1f, 0.94f, 0.82f);
            Material lens = LightingTestSceneBuilder.EmissiveMaterial("WeaponLightLensLit", beamColor, 4f);
            var model = new GameObject("Model");
            BodycamFlashlight.BuildLightBody(model.transform, body, lens);
            model.transform.localScale = Vector3.one * 1.25f;   // a touch bigger, so it reads on the floor from standing height
            var pickup = DarkRoomSceneBuilder.PlacePickup("Pickup_PistolLight", WeaponPickup.Kind.PistolLight, model, floorPoint,
                                                          Quaternion.Euler(0f, 64f, 0f));
            pickup.promptHeight = 0.15f;

            var beam = new GameObject("Beam").AddComponent<Light>();
            beam.transform.SetParent(pickup.transform.Find("Model"), false);
            beam.transform.localPosition = new Vector3(0f, 0f, 0.04f);   // just in front of the lens, along the body
            beam.type = LightType.Spot;
            beam.color = beamColor;
            beam.intensity = 18f;
            beam.range = 12f;
            beam.spotAngle = 50f;
            beam.innerSpotAngle = 18f;
            beam.shadows = LightShadows.Soft;
            beam.shadowNearPlane = 0.05f;
            var haze = beam.gameObject.AddComponent<BodycamHorror.Environment.LightHaze>();   // the beam shows in the dusty air
            haze.material = AssetDatabase.LoadAssetAtPath<Material>(LightingTestSceneBuilder.MaterialDir + "/LightHaze.mat");
            haze.density = 0.004f;
            haze.wisps = 0.5f;
            return pickup;
        }

        /// <summary>A pop-up target: a man-sized board on a post, pivoting at its foot (ShootingTarget).</summary>
        static void PlaceTarget(Transform parent, Vector3 floorPoint)
        {
            Material board = LightingTestSceneBuilder.SimpleMaterial("TargetBoard", new Color(0.62f, 0.52f, 0.36f), 0f, 0.15f);
            Material ring = LightingTestSceneBuilder.SimpleMaterial("TargetMark", new Color(0.55f, 0.08f, 0.06f), 0f, 0.2f);
            Material post = LightingTestSceneBuilder.SimpleMaterial("TargetPost", new Color(0.08f, 0.08f, 0.08f), 0.7f, 0.35f);

            var pivot = new GameObject("Target_" + Mathf.RoundToInt(floorPoint.z) + "m");
            pivot.transform.SetParent(parent, false);
            pivot.transform.SetPositionAndRotation(floorPoint, Quaternion.Euler(0f, 180f, 0f));   // faces the player
            Piece(pivot.transform, "Base", PrimitiveType.Cube, new Vector3(0f, 0.025f, 0f), new Vector3(0.5f, 0.05f, 0.35f), post);
            Piece(pivot.transform, "Post", PrimitiveType.Cube, new Vector3(0f, 0.45f, 0f), new Vector3(0.05f, 0.8f, 0.05f), post);
            Piece(pivot.transform, "Body", PrimitiveType.Cube, new Vector3(0f, 1.25f, 0.03f), new Vector3(0.5f, 0.8f, 0.02f), board);
            Piece(pivot.transform, "Head", PrimitiveType.Cube, new Vector3(0f, 1.8f, 0.03f), new Vector3(0.24f, 0.28f, 0.02f), board);
            Piece(pivot.transform, "Mark", PrimitiveType.Cube, new Vector3(0f, 1.35f, 0.018f), new Vector3(0.14f, 0.14f, 0.005f), ring);
            pivot.AddComponent<ShootingTarget>();
        }

        static void Piece(Transform parent, string name, PrimitiveType type, Vector3 position, Vector3 scale, Material material)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = material;
        }

        /// <summary>A dim, warm spot from the ceiling a little this side of each target, aimed at its
        /// chest: the targets read from the line, and the rest stays dark enough that the glows over the
        /// pickups and the flashlight still matter.</summary>
        static void PlaceLamps(Transform targets)
        {
            var lamps = new GameObject("Lamps").transform;
            foreach (Transform target in targets)
            {
                Vector3 aim = target.position + Vector3.up * 1.3f;
                Vector3 from = new Vector3(target.position.x * 0.6f, Height - 0.05f, target.position.z - 2.2f);
                var go = new GameObject("Lamp_" + target.name);
                go.transform.SetParent(lamps, false);
                go.transform.SetPositionAndRotation(from, Quaternion.LookRotation(aim - from));
                var light = go.AddComponent<Light>();
                light.type = LightType.Spot;
                light.color = new Color(1f, 0.86f, 0.68f);
                light.intensity = 3f;
                light.range = 6f;
                light.spotAngle = 70f;
                light.innerSpotAngle = 30f;
                light.shadows = LightShadows.Soft;
            }
        }
    }
}
