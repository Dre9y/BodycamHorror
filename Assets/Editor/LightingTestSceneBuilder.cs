using System.IO;
using BodycamHorror.Enemies;
using BodycamHorror.Environment;
using BodycamHorror.Player;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace BodycamHorror.EditorTools
{
    /// <summary>
    /// Builds Assets/Scenes/LightingTest.unity from scratch: a straight corridor that opens into a
    /// square room, a passive infected under a hanging lamp in each (the one in the room swings),
    /// and the shared game look (GameLook). The player and the infected look are taken from
    /// SampleScene, so their saved values come along. Also makes the things the scene needs if they
    /// are missing: materials for the scanned wall / floor / ceiling textures, the lamp prefab and the haze material.
    /// Menu: BodycamHorror > Build Lighting Test Scene.
    /// </summary>
    public static class LightingTestSceneBuilder
    {
        const string ScenePath = "Assets/Scenes/LightingTest.unity";
        const string SourceScenePath = "Assets/Scenes/SampleScene.unity";
        internal const string TextureDir = "Assets/Textures/Environment";
        internal const string MaterialDir = "Assets/Materials/Environment";
        const string PrefabDir = "Assets/Prefabs/Environment";

        // layout, metres. The corridor runs along +Z from the player's back wall into the room.
        const float Height = 3f;
        const float CorridorHalfWidth = 1.2f;
        const float CorridorStart = -1f;
        const float CorridorEnd = 24f;
        const float RoomSize = 9f;
        const float Wall = 0.3f;
        const float PanelHeight = 1.45f;   // oil paint up to here, bare plaster above
        const float WallTile = 2f;         // metres one repeat of each texture covers
        const float FloorTile = 2f;
        const float CeilingTile = 2.5f;

        // the light room: off the right side of the corridor, through a door near the player's end
        const float DoorZ0 = 4.8f, DoorZ1 = 6.2f, DoorHeight = 2.2f;
        const float LightRoomSize = 7f;
        const float LightRoomZ0 = 2f;
        static readonly Vector2 LightRoomCentre = new Vector2(CorridorHalfWidth + Wall + LightRoomSize * 0.5f, LightRoomZ0 + LightRoomSize * 0.5f);   // x, z

        internal static Material wallPaint, wallPlaster;

        [MenuItem("BodycamHorror/Build Lighting Test Scene")]
        public static void Build()
        {
            foreach (string dir in new[] { TextureDir, MaterialDir, PrefabDir }) Directory.CreateDirectory(dir);
            AssetDatabase.Refresh();

            // scanned sets from ambientCG (CC0); tinted down a little, they are photographed cleaner than this place is
            wallPaint = EnvironmentTextures.ScannedMaterial("PaintedPlaster011", MaterialDir, TextureDir, new Color(0.62f, 0.7f, 0.68f), 1.2f);
            wallPlaster = EnvironmentTextures.ScannedMaterial("PaintedPlaster015", MaterialDir, TextureDir, new Color(0.72f, 0.72f, 0.68f), 1.2f);
            Material floor = EnvironmentTextures.ScannedMaterial("Tiles110", MaterialDir, TextureDir, new Color(0.6f, 0.6f, 0.58f), 1f);
            Material ceiling = EnvironmentTextures.ScannedMaterial("Plaster007", MaterialDir, TextureDir, new Color(0.7f, 0.7f, 0.66f), 1.2f);
            VolumeProfile grade = GameLook.BuildProfile();   // the look every scene of the game shares
            GameObject lampPrefab = BuildLampPrefab();

            // start from a copy of SampleScene: the player and the infected spawner keep their saved values
            // written over the old file, so the scene keeps its GUID (and build-list entry) from one build to the next
            EditorSceneManager.OpenScene(SourceScenePath, OpenSceneMode.Single);   // never copy over the scene that is open
            if (File.Exists(ScenePath))
            {
                File.Copy(SourceScenePath, ScenePath, true);
                AssetDatabase.ImportAsset(ScenePath, ImportAssetOptions.ForceUpdate);
            }
            else AssetDatabase.CopyAsset(SourceScenePath, ScenePath);
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            var player = Object.FindAnyObjectByType<BodycamPlayerController>();
            var spawner = Object.FindAnyObjectByType<InfectedSpawner>();
            var volume = Object.FindAnyObjectByType<Volume>();
            float oldFloor = 0f;
            var oldFloorObject = GameObject.Find("Floor");
            if (oldFloorObject && oldFloorObject.TryGetComponent(out Collider oldFloorCollider)) oldFloor = oldFloorCollider.bounds.max.y;

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (player && root == player.gameObject) continue;
                if (spawner && root == spawner.gameObject) continue;
                if (volume && root == volume.gameObject) continue;
                Object.DestroyImmediate(root);
            }

            if (player)
            {
                player.transform.SetPositionAndRotation(
                    new Vector3(0f, player.transform.position.y - oldFloor, CorridorStart + 2f), Quaternion.identity);
            }
            if (volume) volume.sharedProfile = grade;

            BuildShell(floor, ceiling);
            BuildLightRoom(floor, ceiling);

            float roomCentre = CorridorEnd + RoomSize * 0.5f;
            var lamps = new GameObject("Lamps").transform;
            PlaceLamp(lampPrefab, lamps, "Lamp_Corridor", new Vector3(0f, Height, 14f), false);
            PlaceLamp(lampPrefab, lamps, "Lamp_Room_Swinging", new Vector3(0f, Height, roomCentre), true);

            if (spawner)
            {
                SetUpStander(spawner, "Infected_Corridor", new Vector3(0f, 0f, 14f));
                var second = Object.Instantiate(spawner);
                SetUpStander(second, "Infected_Room", new Vector3(0f, 0f, roomCentre));
                var third = Object.Instantiate(spawner);
                SetUpStander(third, "Infected_LightRoom", new Vector3(LightRoomCentre.x, 0f, LightRoomCentre.y), 270f);
            }

            GameLook.ApplyAtmosphere();
            foreach (AutoExposure exposure in Object.FindObjectsByType<AutoExposure>(FindObjectsInactive.Include))
            {
                exposure.maxStops = GameLook.MaxStops;
                EditorUtility.SetDirty(exposure);
            }
            AddDust();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log("Lighting test scene built: " + ScenePath);
        }

        // ------------------------------------------------------------------ atmosphere

        /// <summary>Dust in the air around the camera: specks that only show where a light catches them.</summary>
        internal static void AddDust()
        {
            const string path = MaterialDir + "/DustMote.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!material)
            {
                material = new Material(Shader.Find("BodycamHorror/Dust Mote"));
                AssetDatabase.CreateAsset(material, path);
            }
            material.SetFloat("_Brightness", 1.1f);
            material.SetFloat("_Ceiling", 1.6f);
            material.SetFloat("_Reach", 7f);
            EditorUtility.SetDirty(material);
            new GameObject("Dust").AddComponent<DustMotes>().material = material;
        }

        // ------------------------------------------------------------------ infected

        static void SetUpStander(InfectedSpawner spawner, string name, Vector3 position, float facing = 180f)
        {
            spawner.name = name;
            // a little under the floor: the spawner looks for ground from 3 m above itself, which must stay below the ceiling
            spawner.transform.SetPositionAndRotation(position + Vector3.down * 0.5f, Quaternion.Euler(0f, facing, 0f));
            spawner.count = 1;
            spawner.radius = 0f;
            spawner.roamerShare = 0f;
            spawner.startAlerted = false;
            spawner.faceForward = true;   // by default towards the player's end of the corridor
            spawner.passive = true;
            spawner.respawnWhenAllDead = true;
            EditorUtility.SetDirty(spawner);
        }

        // ------------------------------------------------------------------ lamps

        static void PlaceLamp(GameObject prefab, Transform parent, string name, Vector3 ceilingPoint, bool swinging)
        {
            var lamp = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            lamp.name = name;
            lamp.transform.position = ceilingPoint;
            lamp.GetComponentInChildren<SwingingLamp>(true).enabled = swinging;
        }

        /// <summary>Small hanging lamp: ceiling rose, cord, enamel shade, bare bulb. Everything below the
        /// rose hangs on a pivot that SwingingLamp rocks (off in the prefab; switch it on per lamp).</summary>
        static GameObject BuildLampPrefab()
        {
            const string path = PrefabDir + "/HangingLamp.prefab";
            const float drop = 0.45f;   // ceiling to bulb: short, so the bulb is not right on top of a head

            Material metal = SimpleMaterial("LampMetal", new Color(0.05f, 0.05f, 0.05f), 0.7f, 0.35f);
            Material shadeMat = SimpleMaterial("LampShade", new Color(0.07f, 0.11f, 0.1f), 0.2f, 0.5f);
            shadeMat.SetFloat("_Cull", (float)CullMode.Off);   // the shade is a single skin seen from both sides
            Material bulbMat = SimpleMaterial("LampBulb", new Color(1f, 0.9f, 0.7f), 0f, 0.8f);
            bulbMat.EnableKeyword("_EMISSION");
            bulbMat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            bulbMat.SetColor("_EmissionColor", new Color(0.92f, 0.95f, 0.93f) * 8f);
            Material hazeMat = AssetDatabase.LoadAssetAtPath<Material>(MaterialDir + "/LightHaze.mat");
            if (!hazeMat)
            {
                hazeMat = new Material(Shader.Find("BodycamHorror/Light Haze"));
                AssetDatabase.CreateAsset(hazeMat, MaterialDir + "/LightHaze.mat");
            }

            const string shadePath = PrefabDir + "/LampShadeMesh.asset";
            var shadeMesh = AssetDatabase.LoadAssetAtPath<Mesh>(shadePath);
            if (!shadeMesh)
            {
                shadeMesh = ConeShell(0.035f, 0.21f, 0.15f, 28);
                AssetDatabase.CreateAsset(shadeMesh, shadePath);
            }

            var root = new GameObject("HangingLamp");
            Part(PrimitiveType.Cylinder, "Rose", root.transform, new Vector3(0f, -0.015f, 0f), new Vector3(0.11f, 0.015f, 0.11f), metal);

            var pivot = new GameObject("Pivot");
            pivot.transform.SetParent(root.transform, false);
            var swing = pivot.AddComponent<SwingingLamp>();
            swing.cordLength = drop;
            swing.enabled = false;

            float cord = drop - 0.15f;
            Part(PrimitiveType.Cylinder, "Cord", pivot.transform, new Vector3(0f, -cord * 0.5f, 0f), new Vector3(0.012f, cord * 0.5f, 0.012f), metal);

            var shade = new GameObject("Shade", typeof(MeshFilter), typeof(MeshRenderer));
            shade.transform.SetParent(pivot.transform, false);
            shade.transform.localPosition = new Vector3(0f, -cord, 0f);
            shade.GetComponent<MeshFilter>().sharedMesh = shadeMesh;
            var shadeRenderer = shade.GetComponent<MeshRenderer>();
            shadeRenderer.sharedMaterial = shadeMat;
            shadeRenderer.shadowCastingMode = ShadowCastingMode.Off;   // the light's own cone does the shade's job

            GameObject bulb = Part(PrimitiveType.Sphere, "Bulb", pivot.transform, new Vector3(0f, -drop, 0f), Vector3.one * 0.07f, bulbMat);
            bulb.transform.localScale = new Vector3(0.06f, 0.085f, 0.06f);

            // the lamp itself: a dull white cone straight down with a clear edge, so it stands in the haze as a shaft
            var lightObject = new GameObject("Light");
            lightObject.transform.SetParent(pivot.transform, false);
            lightObject.transform.localPosition = new Vector3(0f, -drop, 0f);
            lightObject.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Spot;
            light.color = new Color(0.9f, 0.93f, 0.91f);
            light.intensity = 6.5f;
            light.range = 10f;
            light.spotAngle = 118f;
            light.innerSpotAngle = 86f;
            light.shadows = LightShadows.Soft;
            light.shadowNearPlane = 0.1f;
            ViewModelLighting.AlsoLightViewModel(light);
            var haze = lightObject.AddComponent<LightHaze>();
            haze.material = hazeMat;
            haze.density = 0.013f;
            haze.wisps = 0.6f;

            // the bare bulb's spill outside the main cone, on the walls beside the lamp. A spot, not a point light:
            // the shade is closed on top, so nothing may reach the ceiling above it
            var glowObject = new GameObject("Glow");
            glowObject.transform.SetParent(pivot.transform, false);
            glowObject.transform.localPosition = new Vector3(0f, -drop - 0.05f, 0f);
            glowObject.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            var glow = glowObject.AddComponent<Light>();
            glow.type = LightType.Spot;
            glow.spotAngle = 170f;
            glow.innerSpotAngle = 130f;
            glow.color = new Color(0.9f, 0.93f, 0.91f);
            glow.intensity = 1.2f;
            glow.range = 3.5f;
            glow.shadows = LightShadows.None;
            ViewModelLighting.AlsoLightViewModel(glow);
            var halo = glowObject.AddComponent<LightHaze>();   // a soft dome of lit air under the bulb
            halo.material = hazeMat;
            halo.density = 0.021f;
            halo.wisps = 0.2f;

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return prefab;
        }

        static GameObject Part(PrimitiveType type, string name, Transform parent, Vector3 localPosition, Vector3 localScale, Material material)
        {
            GameObject part = GameObject.CreatePrimitive(type);
            part.name = name;
            Object.DestroyImmediate(part.GetComponent<Collider>());
            part.transform.SetParent(parent, false);
            part.transform.localPosition = localPosition;
            part.transform.localScale = localScale;
            var renderer = part.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;   // they sit right at the light
            return part;
        }

        internal static Material SimpleMaterial(string name, Color color, float metallic, float smoothness)
        {
            string path = MaterialDir + "/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!material)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(material, path);
            }
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Metallic", metallic);
            material.SetFloat("_Smoothness", smoothness);
            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>Open cone skin: narrow at the top (y = 0), wide at the bottom (y = -height).</summary>
        static Mesh ConeShell(float topRadius, float bottomRadius, float height, int segments)
        {
            var vertices = new Vector3[(segments + 1) * 2];
            var normals = new Vector3[vertices.Length];
            var uv = new Vector2[vertices.Length];
            var triangles = new int[segments * 6];
            float slope = (bottomRadius - topRadius) / height;
            for (int i = 0; i <= segments; i++)
            {
                float a = i / (float)segments * Mathf.PI * 2f;
                var radial = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                vertices[i * 2] = radial * topRadius;
                vertices[i * 2 + 1] = radial * bottomRadius + Vector3.down * height;
                normals[i * 2] = normals[i * 2 + 1] = (radial + Vector3.up * slope).normalized;
                uv[i * 2] = new Vector2(i / (float)segments, 1f);
                uv[i * 2 + 1] = new Vector2(i / (float)segments, 0f);
            }
            for (int i = 0; i < segments; i++)
            {
                int a = i * 2, b = a + 1, c = a + 2, d = a + 3;
                triangles[i * 6] = a; triangles[i * 6 + 1] = c; triangles[i * 6 + 2] = b;
                triangles[i * 6 + 3] = b; triangles[i * 6 + 4] = c; triangles[i * 6 + 5] = d;
            }
            var mesh = new Mesh { name = "LampShade", vertices = vertices, normals = normals, uv = uv, triangles = triangles };
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            return mesh;
        }

        // ------------------------------------------------------------------ walls, floor, ceiling

        static void BuildShell(Material floor, Material ceiling)
        {
            var shell = new GameObject("Environment").transform;
            float hw = CorridorHalfWidth, z0 = CorridorStart, z1 = CorridorEnd;
            float half = RoomSize * 0.5f, z2 = z1 + RoomSize;

            var corridor = new GameObject("Corridor").transform;
            corridor.SetParent(shell, false);
            Box("Floor", corridor, new Vector3(-hw, -Wall, z0), new Vector3(hw, 0f, z1), floor, FloorTile, FloorTile);
            Box("Ceiling", corridor, new Vector3(-hw, Height, z0), new Vector3(hw, Height + Wall, z1), ceiling, CeilingTile, CeilingTile);
            WallBox("Wall_Left", corridor, new Vector3(-hw - Wall, 0f, z0), new Vector3(-hw, Height, z1 - Wall));
            // the right wall has the door into the light room in it
            WallBox("Wall_Right_A", corridor, new Vector3(hw, 0f, z0), new Vector3(hw + Wall, Height, DoorZ0));
            WallBox("Wall_Right_B", corridor, new Vector3(hw, 0f, DoorZ1), new Vector3(hw + Wall, Height, z1 - Wall));
            Box("Wall_Right_Lintel", corridor, new Vector3(hw, DoorHeight, DoorZ0), new Vector3(hw + Wall, Height, DoorZ1), wallPlaster, WallTile, WallTile);
            WallBox("Wall_Back", corridor, new Vector3(-hw - Wall, 0f, z0 - Wall), new Vector3(hw + Wall, Height, z0));

            var room = new GameObject("Room").transform;
            room.SetParent(shell, false);
            Box("Floor", room, new Vector3(-half, -Wall, z1), new Vector3(half, 0f, z2), floor, FloorTile, FloorTile);
            Box("Ceiling", room, new Vector3(-half, Height, z1), new Vector3(half, Height + Wall, z2), ceiling, CeilingTile, CeilingTile);
            WallBox("Wall_Left", room, new Vector3(-half - Wall, 0f, z1 - Wall), new Vector3(-half, Height, z2 + Wall));
            WallBox("Wall_Right", room, new Vector3(half, 0f, z1 - Wall), new Vector3(half + Wall, Height, z2 + Wall));
            WallBox("Wall_Far", room, new Vector3(-half, 0f, z2), new Vector3(half, Height, z2 + Wall));
            WallBox("Wall_Door_Left", room, new Vector3(-half, 0f, z1 - Wall), new Vector3(-hw, Height, z1));
            WallBox("Wall_Door_Right", room, new Vector3(hw, 0f, z1 - Wall), new Vector3(half, Height, z1));

            // a few crates, so the swinging lamp has something to throw moving shadows off
            var crate = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Crate.mat");
            var props = new GameObject("Props").transform;
            props.SetParent(shell, false);
            Box("Crate_A", props, new Vector3(2.9f, 0f, z2 - 1.3f), new Vector3(3.9f, 1f, z2 - 0.3f), crate, 1f, 1f);
            Box("Crate_B", props, new Vector3(3.05f, 1f, z2 - 1.15f), new Vector3(3.75f, 1.7f, z2 - 0.45f), crate, 1f, 1f).transform.Rotate(0f, 14f, 0f);
            Box("Crate_C", props, new Vector3(1.7f, 0f, z2 - 1.1f), new Vector3(2.5f, 0.8f, z2 - 0.3f), crate, 1f, 1f).transform.Rotate(0f, -9f, 0f);
            Box("Crate_D", props, new Vector3(-4.2f, 0f, z1 + 0.5f), new Vector3(-3.3f, 0.9f, z1 + 1.4f), crate, 1f, 1f).transform.Rotate(0f, 21f, 0f);
        }

        // ------------------------------------------------------------------ the light room

        /// <summary>A side room for comparing kinds of light, one on each sort of surface: a stuttering
        /// fluorescent tube on the ceiling, a red emergency lamp on one wall, a failing warm bulb on
        /// another, a work light lying on the floor and a monitor glowing on a table.</summary>
        static void BuildLightRoom(Material floor, Material ceiling)
        {
            float x0 = CorridorHalfWidth + Wall, x1 = x0 + LightRoomSize;
            float z0 = LightRoomZ0, z1 = z0 + LightRoomSize;
            var room = new GameObject("LightRoom").transform;

            Box("Floor", room, new Vector3(x0, -Wall, z0), new Vector3(x1, 0f, z1), floor, FloorTile, FloorTile);
            Box("Floor_Doorway", room, new Vector3(CorridorHalfWidth, -Wall, DoorZ0), new Vector3(x0, 0f, DoorZ1), floor, FloorTile, FloorTile);
            Box("Ceiling", room, new Vector3(x0, Height, z0), new Vector3(x1, Height + Wall, z1), ceiling, CeilingTile, CeilingTile);
            WallBox("Wall_South", room, new Vector3(x0, 0f, z0 - Wall), new Vector3(x1 + Wall, Height, z0));
            WallBox("Wall_North", room, new Vector3(x0, 0f, z1), new Vector3(x1 + Wall, Height, z1 + Wall));
            WallBox("Wall_East", room, new Vector3(x1, 0f, z0), new Vector3(x1 + Wall, Height, z1));

            Material metal = SimpleMaterial("LampMetal", new Color(0.05f, 0.05f, 0.05f), 0.7f, 0.35f);
            Material housing = SimpleMaterial("FixtureHousing", new Color(0.42f, 0.43f, 0.4f), 0.1f, 0.35f);
            var crate = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Crate.mat");
            var fixtures = new GameObject("Fixtures").transform;
            fixtures.SetParent(room, false);

            // ceiling: fluorescent tube, greenish white, stutters now and then
            {
                Transform f = Fixture("Ceiling_Fluorescent", fixtures, new Vector3(LightRoomCentre.x, Height, LightRoomCentre.y), Quaternion.identity);
                Part(PrimitiveType.Cube, "Housing", f, new Vector3(0f, -0.03f, 0f), new Vector3(0.17f, 0.06f, 1.32f), housing);
                GameObject tube = Part(PrimitiveType.Cylinder, "Tube", f, new Vector3(0f, -0.08f, 0f), new Vector3(0.035f, 0.6f, 0.035f),
                    EmissiveMaterial("GlowFluorescent", new Color(0.85f, 1f, 0.92f), 5f));
                tube.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                Light l = FixtureLight("Light", f, new Vector3(0f, -0.14f, 0f), Quaternion.Euler(90f, 0f, 0f), LightType.Spot,
                    new Color(0.85f, 1f, 0.92f), 4.5f, 8f, true, 0.0065f);
                l.spotAngle = 150f;
                l.innerSpotAngle = 110f;
                Flicker(l, LightFlicker.Kind.Fluorescent, 0.85f, new Vector2(2.5f, 8f), tube);
            }

            // east wall: red emergency lamp, steady
            {
                Transform f = Fixture("Wall_Emergency", fixtures, new Vector3(x1, 2.3f, z1 - 1.6f), Quaternion.identity);
                Part(PrimitiveType.Cube, "Base", f, new Vector3(-0.03f, 0f, 0f), new Vector3(0.06f, 0.13f, 0.2f), metal);
                Part(PrimitiveType.Sphere, "Dome", f, new Vector3(-0.07f, 0f, 0f), new Vector3(0.1f, 0.1f, 0.16f),
                    EmissiveMaterial("GlowEmergency", new Color(1f, 0.1f, 0.06f), 6f));
                FixtureLight("Light", f, new Vector3(-0.2f, 0f, 0f), Quaternion.identity, LightType.Point,
                    new Color(1f, 0.12f, 0.07f), 2.4f, 5.5f, false, 0.015f);
            }

            // north wall: bare warm bulb on a bracket, on its last legs
            {
                Transform f = Fixture("Wall_Bulb", fixtures, new Vector3(x0 + 1.7f, 1.95f, z1), Quaternion.identity);
                Part(PrimitiveType.Cube, "Bracket", f, new Vector3(0f, 0f, -0.07f), new Vector3(0.05f, 0.05f, 0.14f), metal);
                GameObject bulb = Part(PrimitiveType.Sphere, "Bulb", f, new Vector3(0f, -0.06f, -0.14f), new Vector3(0.06f, 0.085f, 0.06f),
                    EmissiveMaterial("GlowWarmBulb", new Color(1f, 0.72f, 0.4f), 8f));
                Light l = FixtureLight("Light", f, new Vector3(0f, -0.06f, -0.24f), Quaternion.identity, LightType.Point,
                    new Color(1f, 0.74f, 0.44f), 2.2f, 5f, false, 0.0105f);
                Flicker(l, LightFlicker.Kind.Flame, 0.45f, new Vector2(2.2f, 2.2f), bulb);
            }

            // floor: a work light left lying on its side, raking across the tiles
            {
                Transform f = Fixture("Floor_WorkLight", fixtures, new Vector3(x0 + 0.9f, 0.07f, z0 + 0.9f), Quaternion.Euler(-7f, 50f, 0f));
                GameObject body = Part(PrimitiveType.Cylinder, "Body", f, new Vector3(0f, 0f, -0.1f), new Vector3(0.11f, 0.1f, 0.11f), metal);
                body.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                GameObject lens = Part(PrimitiveType.Cylinder, "Lens", f, new Vector3(0f, 0f, 0.005f), new Vector3(0.1f, 0.004f, 0.1f),
                    EmissiveMaterial("GlowWorkLight", new Color(0.95f, 0.97f, 1f), 10f));
                lens.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                Light l = FixtureLight("Light", f, new Vector3(0f, 0f, 0.03f), Quaternion.identity, LightType.Spot,
                    new Color(0.95f, 0.97f, 1f), 30f, 11f, true, 0.0042f);
                l.spotAngle = 75f;
                l.innerSpotAngle = 40f;
            }

            // table: an old monitor left on, its picture jumping
            {
                Vector3 at = new Vector3(x1 - 0.55f, 0f, z0 + 1.3f);
                Transform table = new GameObject("Table").transform;
                table.SetParent(room, false);
                Box("Top", table, at + new Vector3(-0.4f, 0.72f, -0.65f), at + new Vector3(0.4f, 0.76f, 0.65f), crate, 1f, 1f);
                for (int i = 0; i < 4; i++)
                {
                    var leg = at + new Vector3(i < 2 ? -0.35f : 0.35f, 0f, i % 2 == 0 ? -0.6f : 0.6f);
                    Box("Leg", table, leg + new Vector3(-0.03f, 0f, -0.03f), leg + new Vector3(0.03f, 0.72f, 0.03f), metal, 1f, 1f);
                }
                Box("Monitor", table, at + new Vector3(-0.2f, 0.76f, -0.24f), at + new Vector3(0.2f, 1.14f, 0.24f), housing, 1f, 1f);

                Transform f = Fixture("Table_Monitor", fixtures, at + new Vector3(-0.2f, 0.95f, 0f), Quaternion.Euler(0f, -90f, 0f));
                GameObject screen = Part(PrimitiveType.Cube, "Screen", f, new Vector3(0f, 0f, 0.003f), new Vector3(0.4f, 0.3f, 0.006f),
                    EmissiveMaterial("GlowScreen", new Color(0.5f, 0.85f, 0.78f), 2.5f));
                Light l = FixtureLight("Light", f, new Vector3(0f, 0f, 0.08f), Quaternion.identity, LightType.Spot,
                    new Color(0.5f, 0.85f, 0.78f), 2.2f, 5f, false, 0.0105f);
                l.spotAngle = 130f;
                l.innerSpotAngle = 60f;
                Flicker(l, LightFlicker.Kind.Screen, 0.5f, new Vector2(0.06f, 0.5f), screen);
            }

            PlaceLightRoomDoor(room);

            // something for the coloured shadows to fall across
            Box("Crate_E", room, new Vector3(x0 + 3.9f, 0f, z0 + 0.7f), new Vector3(x0 + 4.7f, 0.8f, z0 + 1.5f), crate, 1f, 1f).transform.Rotate(0f, 17f, 0f);
            Box("Crate_F", room, new Vector3(x0 + 4.6f, 0f, z1 - 1.5f), new Vector3(x0 + 5.5f, 0.9f, z1 - 0.6f), crate, 1f, 1f).transform.Rotate(0f, -12f, 0f);
        }

        // ------------------------------------------------------------------ door

        const string DoorName = "Door_LightRoom";

        /// <summary>Puts the door into the scene that is open now, without rebuilding the rest of it.</summary>
        [MenuItem("BodycamHorror/Add Light Room Door")]
        public static void AddLightRoomDoor()
        {
            var room = GameObject.Find("LightRoom");
            if (!room)
            {
                Debug.LogWarning("No LightRoom in the open scene: build the lighting test scene first.");
                return;
            }
            PlaceLightRoomDoor(room.transform);
            EditorSceneManager.MarkSceneDirty(room.scene);
            EditorSceneManager.SaveScene(room.scene);
        }

        /// <summary>The door between the corridor and the light room, hinged on the player's-end jamb.</summary>
        static void PlaceLightRoomDoor(Transform room)
        {
            Transform old = room.Find(DoorName);
            if (old) Object.DestroyImmediate(old.gameObject);

            const float hingeGap = 0.03f, latchGap = 0.02f;
            GameObject prefab = BuildDoorPrefab(DoorZ1 - DoorZ0 - hingeGap - latchGap, DoorHeight - 0.03f);
            var door = (GameObject)PrefabUtility.InstantiatePrefab(prefab, room);
            door.name = DoorName;
            // in the middle of the wall's thickness, the leaf running along +Z, its front to the corridor
            door.transform.SetPositionAndRotation(
                new Vector3(CorridorHalfWidth + Wall * 0.5f, 0f, DoorZ0 + hingeGap), Quaternion.Euler(0f, -90f, 0f));
        }

        /// <summary>A plain painted door: the root stays on the hinge line, the leaf under it turns.
        /// Lever handles on both sides; the key prompt shows at them.</summary>
        static GameObject BuildDoorPrefab(float width, float height)
        {
            const string path = PrefabDir + "/Door.prefab";
            const float thickness = 0.05f, floorGap = 0.01f, handleHeight = 1.02f;

            Material paint = SimpleMaterial("DoorPaint", new Color(0.33f, 0.36f, 0.33f), 0f, 0.38f);
            Material steel = SimpleMaterial("DoorHandle", new Color(0.32f, 0.32f, 0.31f), 0.85f, 0.55f);

            var root = new GameObject("Door");
            var leaf = new GameObject("Leaf");
            leaf.transform.SetParent(root.transform, false);
            var body = leaf.AddComponent<Rigidbody>();   // moved by script: lets it shove bodies aside
            body.isKinematic = true;
            body.useGravity = false;

            GameObject panel = GameObject.CreatePrimitive(PrimitiveType.Cube);
            panel.name = "Panel";
            panel.transform.SetParent(leaf.transform, false);
            panel.transform.localPosition = new Vector3(width * 0.5f, floorGap + height * 0.5f, 0f);
            panel.transform.localScale = new Vector3(width, height, thickness);
            var panelRenderer = panel.GetComponent<MeshRenderer>();
            panelRenderer.sharedMaterial = paint;
            panelRenderer.shadowCastingMode = ShadowCastingMode.TwoSided;

            var handles = new Transform[2];
            for (int i = 0; i < 2; i++)
            {
                float side = i == 0 ? 1f : -1f;
                var handle = new GameObject(i == 0 ? "Handle_Front" : "Handle_Back").transform;
                handle.SetParent(leaf.transform, false);
                handle.localPosition = new Vector3(width - 0.09f, handleHeight, side * thickness * 0.5f);
                GameObject plate = Part(PrimitiveType.Cube, "Plate", handle, new Vector3(0f, -0.02f, side * 0.003f), new Vector3(0.04f, 0.16f, 0.006f), steel);
                GameObject stem = Part(PrimitiveType.Cylinder, "Stem", handle, new Vector3(0f, 0f, side * 0.028f), new Vector3(0.018f, 0.025f, 0.018f), steel);
                stem.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                Part(PrimitiveType.Cube, "Lever", handle, new Vector3(-0.05f, 0f, side * 0.05f), new Vector3(0.12f, 0.018f, 0.016f), steel);
                plate.isStatic = false;
                handles[i] = handle;
            }

            var component = root.AddComponent<Door>();
            component.leaf = leaf.transform;
            component.handles = handles;
            // recordings (Assets/Audio/CREDITS.md); a missing one falls back to the generated sound
            component.openClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/Door/door_open.wav");
            component.closeClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/Door/door_close.wav");
            component.shutClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/Door/door_shut.wav");
            component.openTime = 1f;   // the creak is a slow one: the door takes its time

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return prefab;
        }

        static Transform Fixture(string name, Transform parent, Vector3 position, Quaternion rotation)
        {
            var fixture = new GameObject(name).transform;
            fixture.SetParent(parent, false);
            fixture.SetPositionAndRotation(position, rotation);
            return fixture;
        }

        static Light FixtureLight(string name, Transform parent, Vector3 localPosition, Quaternion localRotation, LightType type,
            Color color, float intensity, float range, bool shadows, float hazeDensity)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localRotation = localRotation;
            var light = go.AddComponent<Light>();
            light.type = type;
            light.color = color;
            light.intensity = intensity;
            light.range = range;
            light.shadows = shadows ? LightShadows.Soft : LightShadows.None;
            light.shadowNearPlane = 0.1f;
            ViewModelLighting.AlsoLightViewModel(light);
            if (hazeDensity > 0f)
            {
                var haze = go.AddComponent<LightHaze>();
                haze.material = AssetDatabase.LoadAssetAtPath<Material>(MaterialDir + "/LightHaze.mat");
                haze.density = hazeDensity;
            }
            return light;
        }

        static void Flicker(Light light, LightFlicker.Kind kind, float depth, Vector2 pace, GameObject glow)
        {
            var flicker = light.gameObject.AddComponent<LightFlicker>();
            flicker.kind = kind;
            flicker.depth = depth;
            flicker.pace = pace;
            flicker.glow = glow.GetComponent<Renderer>();
        }

        static Material EmissiveMaterial(string name, Color color, float emission)
        {
            Material material = SimpleMaterial(name, color, 0f, 0.7f);
            material.EnableKeyword("_EMISSION");
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            material.SetColor("_EmissionColor", color * emission);
            return material;
        }

        /// <summary>A wall from floor to ceiling in two bands: paint up to PanelHeight, plaster above.</summary>
        internal static void WallBox(string name, Transform parent, Vector3 min, Vector3 max)
        {
            float split = min.y + PanelHeight;
            Box(name + "_Paint", parent, min, new Vector3(max.x, split, max.z), wallPaint, WallTile, WallTile);
            Box(name + "_Plaster", parent, new Vector3(min.x, split, min.z), max, wallPlaster, WallTile, WallTile);
        }

        /// <summary>A box between two world corners whose texture coordinates follow world position, so
        /// neighbouring pieces line up: the sides wrap every <paramref name="tileAcross"/> metres along the
        /// wall and <paramref name="tileUp"/> metres up it, top and bottom every tileAcross both ways.</summary>
        internal static GameObject Box(string name, Transform parent, Vector3 min, Vector3 max, Material material, float tileAcross, float tileUp)
        {
            Vector3 centre = (min + max) * 0.5f, e = (max - min) * 0.5f;
            var vertices = new Vector3[24];
            var normals = new Vector3[24];
            var uv = new Vector2[24];
            var triangles = new int[36];
            Vector3[] axes = { Vector3.right, Vector3.up, Vector3.forward };
            int v = 0, t = 0;
            for (int axis = 0; axis < 3; axis++)
            {
                for (int sign = -1; sign <= 1; sign += 2)
                {
                    Vector3 n = axes[axis] * sign;
                    Vector3 a = axes[(axis + 1) % 3], b = axes[(axis + 2) % 3];
                    Vector3 face = Vector3.Scale(n, e);
                    Vector3 ea = Vector3.Scale(a, e), eb = Vector3.Scale(b, e);
                    Vector3[] corners = { face - ea - eb, face + ea - eb, face + ea + eb, face - ea + eb };
                    for (int i = 0; i < 4; i++)
                    {
                        Vector3 world = centre + corners[i];
                        vertices[v + i] = corners[i];
                        normals[v + i] = n;
                        uv[v + i] = axis switch
                        {
                            0 => new Vector2(world.z / tileAcross, world.y / tileUp),
                            1 => new Vector2(world.x / tileAcross, world.z / tileAcross),
                            _ => new Vector2(world.x / tileAcross, world.y / tileUp),
                        };
                    }
                    bool flip = Vector3.Dot(Vector3.Cross(corners[1] - corners[0], corners[2] - corners[0]), n) < 0f;
                    int[] order = flip ? new[] { 0, 2, 1, 0, 3, 2 } : new[] { 0, 1, 2, 0, 2, 3 };
                    foreach (int o in order) triangles[t++] = v + o;
                    v += 4;
                }
            }
            var mesh = new Mesh { name = name, vertices = vertices, normals = normals, uv = uv, triangles = triangles };
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();

            var box = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer), typeof(BoxCollider));
            box.transform.SetParent(parent, false);
            box.transform.position = centre;
            box.GetComponent<MeshFilter>().sharedMesh = mesh;
            box.GetComponent<MeshRenderer>().sharedMaterial = material;
            box.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.TwoSided;
            box.GetComponent<BoxCollider>().size = e * 2f;
            box.isStatic = true;
            return box;
        }
    }
}
