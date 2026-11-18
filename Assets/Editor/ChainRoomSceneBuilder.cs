using System.Collections.Generic;
using System.IO;
using BodycamHorror.Enemies;
using BodycamHorror.Environment;
using BodycamHorror.Player;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace BodycamHorror.EditorTools
{
    /// <summary>
    /// Builds Assets/Scenes/ChainRoom.unity ("Now you see me"): a dim corridor opens into a big square room lit by one
    /// bright bare bulb, with chains hanging from the ceiling that clank when brushed. A step into the room and the bulb
    /// bursts; in the dark the manthing is there, and it moves whenever it is not seen in the light.
    /// Menu: BodycamHorror > Build Chain Room Scene.
    /// </summary>
    public static class ChainRoomSceneBuilder
    {
        const string ScenePath = "Assets/Scenes/ChainRoom.unity";
        const string SourceScenePath = "Assets/Scenes/LightingTest.unity";
        const string AudioDir = "Assets/Audio/ChainRoom";
        public const int ChainLayer = 10;

        // metres. The corridor runs along +Z from the player's back wall into the room.
        const float Wall = 0.3f;
        const float CorridorHalfWidth = 1.2f, CorridorHeight = 3f, CorridorLength = 14f;
        const float RoomHalf = 8f, RoomHeight = 3.3f;        // a 16 x 16 m room with a low ceiling: the manthing all but stoops under it
        const float ManthingPoseHeight = 1.6f;               // its tallest pose at the model.s own scale
        static readonly float ManthingScale = Mathf.Min(2f, (RoomHeight - 0.15f) / ManthingPoseHeight);   // twice its size, or what fits
        const float RoomZ0 = CorridorLength, RoomZ1 = CorridorLength + RoomHalf * 2f;
        const float PlayerZ = 1f;
        static readonly Vector3 RoomCentre = new Vector3(0f, 0f, RoomZ0 + RoomHalf);

        [MenuItem("BodycamHorror/Build Chain Room Scene")]
        public static void Build()
        {
            EnsureChainLayer();
            const string mats = LightingTestSceneBuilder.MaterialDir, tex = LightingTestSceneBuilder.TextureDir;
            LightingTestSceneBuilder.wallPaint = EnvironmentTextures.ScannedMaterial("PaintedPlaster011", mats, tex, new Color(0.62f, 0.7f, 0.68f), 1.2f);
            LightingTestSceneBuilder.wallPlaster = EnvironmentTextures.ScannedMaterial("PaintedPlaster015", mats, tex, new Color(0.72f, 0.72f, 0.68f), 1.2f);
            Material floor = EnvironmentTextures.ScannedMaterial("Tiles110", mats, tex, new Color(0.6f, 0.6f, 0.58f), 1f);
            Material ceiling = EnvironmentTextures.ScannedMaterial("Plaster007", mats, tex, new Color(0.7f, 0.7f, 0.66f), 1.2f);
            Material rust = EnvironmentTextures.ScannedMaterial("Rust002", mats, tex, new Color(0.55f, 0.5f, 0.46f), 1f);

            EditorSceneManager.OpenScene(SourceScenePath, OpenSceneMode.Single);   // never copy over the scene that is open
            if (File.Exists(ScenePath))
            {
                File.Copy(SourceScenePath, ScenePath, true);
                AssetDatabase.ImportAsset(ScenePath, ImportAssetOptions.ForceUpdate);
            }
            else AssetDatabase.CopyAsset(SourceScenePath, ScenePath);
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            // keep the player and the grade; everything else goes
            var player = Object.FindAnyObjectByType<BodycamPlayerController>();
            var volume = Object.FindAnyObjectByType<Volume>();
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (player && root == player.gameObject) continue;
                if (volume && root == volume.gameObject) continue;
                Object.DestroyImmediate(root);
            }
            if (player)
            {
                player.transform.SetPositionAndRotation(new Vector3(0f, player.transform.position.y, PlayerZ), Quaternion.identity);
                if (!player.GetComponent<ChainPusher>()) player.gameObject.AddComponent<ChainPusher>();
            }

            BuildShell(floor, ceiling);

            var lamps = new GameObject("Lamps").transform;
            var lampPrefab = LightingTestSceneBuilder.BuildLampPrefab();
            LightingTestSceneBuilder.PlaceLamp(lampPrefab, lamps, "Lamp_Corridor", new Vector3(0f, CorridorHeight, 6f), false);
            BurstingBulb bulb = BuildBulb(lamps);
            BuildChains(rust);
            Door door = PlaceDoor();
            PlaceWriting();
            Manthing thing = PlaceManthing(bulb);
            PlaceEvent(door, bulb, thing);

            GameLook.ApplyAtmosphere();
            foreach (AutoExposure exposure in Object.FindObjectsByType<AutoExposure>(FindObjectsInactive.Include))
            {
                exposure.maxStops = GameLook.MaxStops;
                EditorUtility.SetDirty(exposure);
            }
            LightingTestSceneBuilder.AddDust();
            AddAmbience(bulb);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AddToBuild();
            AssetDatabase.SaveAssets();
            Debug.Log("Chain room scene built: " + ScenePath);
        }

        static void EnsureChainLayer()
        {
            var tags = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            var layers = tags.FindProperty("layers");
            var slot = layers.GetArrayElementAtIndex(ChainLayer);
            if (slot.stringValue != "Chain") { slot.stringValue = "Chain"; tags.ApplyModifiedProperties(); }
        }

        static void AddToBuild()
        {
            var list = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            if (list.Exists(s => s.path == ScenePath)) return;
            list.Add(new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = list.ToArray();
        }

        static void BuildShell(Material floor, Material ceiling)
        {
            const float tile = 2f, ceilingTile = 2.5f;
            float hw = CorridorHalfWidth, rh = RoomHalf;
            var shell = new GameObject("Environment").transform;

            var corridor = new GameObject("Corridor").transform;
            corridor.SetParent(shell, false);
            LightingTestSceneBuilder.Box("Floor", corridor, new Vector3(-hw, -Wall, -Wall), new Vector3(hw, 0f, RoomZ0), floor, tile, tile);
            LightingTestSceneBuilder.Box("Ceiling", corridor, new Vector3(-hw, CorridorHeight, -Wall), new Vector3(hw, CorridorHeight + Wall, RoomZ0), ceiling, ceilingTile, ceilingTile);
            LightingTestSceneBuilder.WallBox("Wall_Back", corridor, new Vector3(-hw - Wall, 0f, -Wall), new Vector3(hw + Wall, CorridorHeight, 0f));
            LightingTestSceneBuilder.WallBox("Wall_Left", corridor, new Vector3(-hw - Wall, 0f, 0f), new Vector3(-hw, CorridorHeight, RoomZ0));
            LightingTestSceneBuilder.WallBox("Wall_Right", corridor, new Vector3(hw, 0f, 0f), new Vector3(hw + Wall, CorridorHeight, RoomZ0));

            var room = new GameObject("Room").transform;
            room.SetParent(shell, false);
            LightingTestSceneBuilder.Box("Floor", room, new Vector3(-rh, -Wall, RoomZ0), new Vector3(rh, 0f, RoomZ1), floor, tile, tile);
            LightingTestSceneBuilder.Box("Ceiling", room, new Vector3(-rh, RoomHeight, RoomZ0), new Vector3(rh, RoomHeight + Wall, RoomZ1), ceiling, ceilingTile, ceilingTile);
            // the near wall, with a door in the middle (PlaceDoor) and wall over it
            float dx = DoorBuilder.OpeningWidth * 0.5f, dh = DoorBuilder.OpeningHeight;
            LightingTestSceneBuilder.WallBox("Wall_Near_Left", room, new Vector3(-rh - Wall, 0f, RoomZ0 - Wall), new Vector3(-dx, RoomHeight, RoomZ0));
            LightingTestSceneBuilder.WallBox("Wall_Near_Right", room, new Vector3(dx, 0f, RoomZ0 - Wall), new Vector3(rh + Wall, RoomHeight, RoomZ0));
            LightingTestSceneBuilder.Box("Wall_Near_Lintel", room, new Vector3(-dx, dh, RoomZ0 - Wall), new Vector3(dx, RoomHeight, RoomZ0),
                                         LightingTestSceneBuilder.wallPlaster, 2f, 2f);
            LightingTestSceneBuilder.WallBox("Wall_Far", room, new Vector3(-rh - Wall, 0f, RoomZ1), new Vector3(rh + Wall, RoomHeight, RoomZ1 + Wall));
            LightingTestSceneBuilder.WallBox("Wall_Left", room, new Vector3(-rh - Wall, 0f, RoomZ0), new Vector3(-rh, RoomHeight, RoomZ1));
            LightingTestSceneBuilder.WallBox("Wall_Right", room, new Vector3(rh, 0f, RoomZ0), new Vector3(rh + Wall, RoomHeight, RoomZ1));
        }

        /// <summary>One bare bulb on a long cord in the middle of the room, bright enough for all of it.</summary>
        static BurstingBulb BuildBulb(Transform parent)
        {
            float bulbY = RoomHeight - 1.15f;   // on a longer cord, so the ceiling right above is not blown out
            var root = new GameObject("Bulb_Centre").transform;
            root.SetParent(parent, false);
            root.position = new Vector3(RoomCentre.x, bulbY, RoomCentre.z);

            Material black = LightingTestSceneBuilder.SimpleMaterial("LampMetal", new Color(0.05f, 0.05f, 0.05f), 0.7f, 0.35f);
            Material glassMat = LightingTestSceneBuilder.EmissiveMaterial("ChainRoomBulb", new Color(0.7f, 0.83f, 1f), 14f);

            var cord = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            cord.name = "Cord"; Object.DestroyImmediate(cord.GetComponent<Collider>());
            cord.transform.SetParent(root, false);
            float cordLen = RoomHeight - bulbY - 0.06f;
            cord.transform.localPosition = new Vector3(0f, 0.06f + cordLen * 0.5f, 0f);
            cord.transform.localScale = new Vector3(0.008f, cordLen * 0.5f, 0.008f);
            cord.GetComponent<Renderer>().sharedMaterial = black;

            var socket = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            socket.name = "Socket"; Object.DestroyImmediate(socket.GetComponent<Collider>());
            socket.transform.SetParent(root, false);
            socket.transform.localPosition = new Vector3(0f, 0.04f, 0f);
            socket.transform.localScale = new Vector3(0.035f, 0.03f, 0.035f);
            socket.GetComponent<Renderer>().sharedMaterial = black;

            var glass = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            glass.name = "Glass"; Object.DestroyImmediate(glass.GetComponent<Collider>());
            glass.transform.SetParent(root, false);
            glass.transform.localPosition = new Vector3(0f, -0.04f, 0f);
            glass.transform.localScale = new Vector3(0.075f, 0.1f, 0.075f);
            var glassRenderer = glass.GetComponent<Renderer>();
            glassRenderer.sharedMaterial = glassMat;
            glassRenderer.shadowCastingMode = ShadowCastingMode.Off;

            var lightGo = new GameObject("Light");
            lightGo.transform.SetParent(root, false);
            lightGo.transform.localPosition = new Vector3(0f, -0.12f, 0f);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(0.7f, 0.83f, 1f);   // cold blue
            light.intensity = 16f;
            light.range = 17f;
            light.shadows = LightShadows.Soft;
            ViewModelLighting.SetLightLayers(light, ViewModelLighting.WorldLayer | ViewModelLighting.ViewModelLayer);

            var bulb = root.gameObject.AddComponent<BurstingBulb>();
            bulb.bulbLight = light;
            bulb.glass = glassRenderer;
            bulb.burstClip = AssetDatabase.LoadAssetAtPath<AudioClip>(AudioDir + "/lamp_break_drop.mp3");
            bulb.autoTrigger = false;   // the chain room event bursts it
            EditorUtility.SetDirty(bulb);
            return bulb;
        }

        static void BuildChains(Material rust)
        {
            const float segLen = 0.24f;
            var root = new GameObject("Chains").transform;
            Mesh link = LinkMesh();
            var rnd = new System.Random(7);
            float[] xs = { -5.5f, -2.6f, 2.6f, 5.5f }, zs = { 3f, 6.5f, 9.5f, 13f };
            var clanks = new List<AudioClip>();
            foreach (string guid in AssetDatabase.FindAssets("chain_clank t:AudioClip", new[] { AudioDir }))
                clanks.Add(AssetDatabase.LoadAssetAtPath<AudioClip>(AssetDatabase.GUIDToAssetPath(guid)));
            int n = 0;
            foreach (float x in xs)
                foreach (float z in zs)
                {
                    float jx = (float)(rnd.NextDouble() - 0.5) * 1.2f, jz = (float)(rnd.NextDouble() - 0.5) * 1.2f;
                    var top = new Vector3(x + jx, RoomHeight, RoomZ0 + z + jz);
                    float bottomY = 1.0f + (float)rnd.NextDouble() * 0.9f;
                    int count = Mathf.Max(4, Mathf.RoundToInt((RoomHeight - bottomY) / segLen));
                    BuildChain(root, "Chain_" + (++n).ToString("00"), top, count, segLen, link, rust, clanks.ToArray());
                }
        }

        static void BuildChain(Transform parent, string name, Vector3 top, int count, float segLen, Mesh link, Material material, AudioClip[] clanks)
        {
            var chainGo = new GameObject(name);
            chainGo.layer = ChainLayer;
            chainGo.transform.SetParent(parent, false);
            chainGo.transform.position = top;
            var anchor = chainGo.AddComponent<Rigidbody>();
            anchor.isKinematic = true;

            var segments = new Rigidbody[count];
            Rigidbody previous = anchor;
            for (int i = 0; i < count; i++)
            {
                var seg = new GameObject("Link_" + i.ToString("00"));
                seg.layer = ChainLayer;
                seg.transform.SetParent(chainGo.transform, false);
                seg.transform.localPosition = new Vector3(0f, -segLen * (i + 0.5f), 0f);
                seg.transform.localRotation = Quaternion.Euler(0f, i * 17f, 0f);
                // two links per segment, at right angles, as a chain hangs
                for (int k = 0; k < 2; k++)
                {
                    var l = new GameObject("Ring" + k);
                    l.layer = ChainLayer;
                    l.transform.SetParent(seg.transform, false);
                    l.transform.localPosition = new Vector3(0f, segLen * (0.25f - 0.5f * k), 0f);
                    l.transform.localRotation = Quaternion.Euler(0f, k * 90f, 0f);
                    l.AddComponent<MeshFilter>().sharedMesh = link;
                    var r = l.AddComponent<MeshRenderer>();
                    r.sharedMaterial = material;
                    r.shadowCastingMode = ShadowCastingMode.On;
                }
                var cap = seg.AddComponent<CapsuleCollider>();
                cap.direction = 1; cap.radius = 0.035f; cap.height = segLen;
                var rb = seg.AddComponent<Rigidbody>();
                rb.mass = 0.8f;
                rb.linearDamping = 0.15f;
                rb.angularDamping = 0.8f;
                rb.interpolation = RigidbodyInterpolation.Interpolate;
                rb.solverIterations = 12;
                var j = seg.AddComponent<CharacterJoint>();
                j.connectedBody = previous;
                j.anchor = new Vector3(0f, segLen * 0.5f, 0f);
                j.axis = Vector3.right;
                j.swingAxis = Vector3.forward;
                j.lowTwistLimit = new SoftJointLimit { limit = -40f };
                j.highTwistLimit = new SoftJointLimit { limit = 40f };
                j.swing1Limit = new SoftJointLimit { limit = 40f };
                j.swing2Limit = new SoftJointLimit { limit = 40f };
                j.enableProjection = true;
                segments[i] = rb;
                previous = rb;
            }
            var chain = chainGo.AddComponent<HangingChain>();
            chain.segments = segments;
            chain.clanks = clanks;
            chain.volume = 0.25f;   // muted for now: the steps are what to listen for
            EditorUtility.SetDirty(chain);
        }

        /// <summary>One oval chain ring (a squashed torus), 12 cm long, shared by every link.</summary>
        static Mesh LinkMesh()
        {
            const string path = LightingTestSceneBuilder.PrefabDir + "/ChainLinkMesh.asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh) return mesh;
            const int ring = 20, tube = 8;
            const float a = 0.062f, b = 0.032f, r = 0.009f;   // half length, half width, wire radius
            var v = new List<Vector3>(); var nrm = new List<Vector3>(); var tri = new List<int>();
            for (int i = 0; i <= ring; i++)
            {
                float u = i / (float)ring * Mathf.PI * 2f;
                Vector3 c = new Vector3(Mathf.Cos(u) * b, Mathf.Sin(u) * a, 0f);
                Vector3 outward = new Vector3(Mathf.Cos(u) / b, Mathf.Sin(u) / a, 0f).normalized;
                for (int k = 0; k <= tube; k++)
                {
                    float w = k / (float)tube * Mathf.PI * 2f;
                    Vector3 n = outward * Mathf.Cos(w) + Vector3.forward * Mathf.Sin(w);
                    v.Add(c + n * r); nrm.Add(n);
                }
            }
            for (int i = 0; i < ring; i++)
                for (int k = 0; k < tube; k++)
                {
                    int p0 = i * (tube + 1) + k, p1 = p0 + tube + 1;
                    tri.AddRange(new[] { p0, p1, p0 + 1, p0 + 1, p1, p1 + 1 });
                }
            mesh = new Mesh { name = "ChainLink" };
            mesh.SetVertices(v); mesh.SetNormals(nrm); mesh.SetTriangles(tri, 0);
            mesh.RecalculateBounds();
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        static Manthing PlaceManthing(BurstingBulb bulb)
        {
            var root = new GameObject("Manthing");
            root.layer = 2;   // not in the way of sight lines and shots; the player still bumps into it
            // by the door, once it is shut behind the player: it appears there when the light goes
            root.transform.position = new Vector3(1.5f, 0f, RoomZ0 + 1.4f);
            root.transform.rotation = Quaternion.LookRotation(Vector3.forward, Vector3.up);
            var col = root.AddComponent<CapsuleCollider>();
            col.radius = 0.4f * ManthingScale; col.height = 2.2f * ManthingScale; col.center = new Vector3(0f, 1.1f * ManthingScale, 0f);

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ManthingSetup.ModelPath);
            var model = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root.transform);
            model.name = "Model";
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
            model.transform.localScale = Vector3.one * ManthingScale;
            var mat = AssetDatabase.LoadAssetAtPath<Material>(ManthingSetup.MaterialPath);
            foreach (var r in model.GetComponentsInChildren<Renderer>(true))
            {
                r.sharedMaterial = mat;
                r.shadowCastingMode = ShadowCastingMode.On;
                if (r is SkinnedMeshRenderer smr) smr.updateWhenOffscreen = true;
            }
            var animator = model.GetComponent<Animator>() ?? model.AddComponent<Animator>();
            animator.avatar = AssetDatabase.LoadAssetAtPath<Avatar>(ManthingSetup.ModelPath);
            animator.runtimeAnimatorController = null;

            var thing = root.AddComponent<Manthing>();
            thing.animator = animator;
            thing.runClip = ManthingSetup.Clip("standing run");
            thing.freezeClips = new[] { ManthingSetup.Clip("standing run"), ManthingSetup.Clip("idle standing") };   // "ref" left out for now
            thing.poseHeight = ManthingPoseHeight;
            thing.screechClip = ManthingSetup.Clip("screech");
            thing.appearOn = bulb;
            thing.doorway = new Vector3(0f, 0f, RoomZ0 - 0.5f);
            thing.killDistance = 1.15f * ManthingScale;   // its reach grows with it
            thing.soleHeight = 0.03f * ManthingScale;
            var steps = new List<AudioClip>();
            foreach (string guid in AssetDatabase.FindAssets("monster_barefoot t:AudioClip", new[] { AudioDir }))
                steps.Add(AssetDatabase.LoadAssetAtPath<AudioClip>(AssetDatabase.GUIDToAssetPath(guid)));
            thing.steps = steps.ToArray();
            thing.scream = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/Monster/monster_scream.wav");
            var breaths = new List<AudioClip>();
            foreach (string guid in AssetDatabase.FindAssets("monster_breath_ t:AudioClip", new[] { AudioDir }))
                breaths.Add(AssetDatabase.LoadAssetAtPath<AudioClip>(AssetDatabase.GUIDToAssetPath(guid)));
            thing.breaths = breaths.ToArray();
            EditorUtility.SetDirty(thing);
            return thing;
        }

        /// <summary>A steel door between the corridor and the room, shut: the player opens it to go in.</summary>
        static Door PlaceDoor()
        {
            float dx = DoorBuilder.OpeningWidth * 0.5f;
            var go = DoorBuilder.Place(new Vector3(-dx, 0f, RoomZ0 - Wall * 0.5f), Vector3.right, false);
            go.name = "Door_ChainRoom";
            var door = go.GetComponent<Door>();
            door.startOpen = false;
            door.locked = false;
            door.enemiesCanOpen = false;   // nothing gets out once it is shut
            EditorUtility.SetDirty(door);
            return door;
        }

        /// <summary>"Now you see me" in blood on the far wall, facing the door.</summary>
        static void PlaceWriting()
        {
            const string matPath = "Assets/Textures/ChainRoom/NowYouSeeMe_Blood.mat";
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Textures/ChainRoom/NowYouSeeMe_Blood.png");
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (!mat) { mat = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(mat, matPath); }
            mat.SetTexture("_BaseMap", tex);
            mat.SetColor("_BaseColor", Color.white);
            mat.SetFloat("_AlphaClip", 1f); mat.EnableKeyword("_ALPHATEST_ON");
            mat.SetFloat("_Cutoff", 0.35f);
            mat.SetFloat("_Smoothness", 0.55f);   // still wet
            mat.SetFloat("_Metallic", 0f);
            mat.renderQueue = (int)RenderQueue.AlphaTest;
            EditorUtility.SetDirty(mat);

            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "Writing_NowYouSeeMe";
            Object.DestroyImmediate(quad.GetComponent<Collider>());
            float width = 5.6f, height = width * tex.height / tex.width;
            quad.transform.position = new Vector3(0f, 1.75f - height * 0.08f, RoomZ1 - 0.01f);
            quad.transform.rotation = Quaternion.LookRotation(Vector3.forward, Vector3.up);   // its face towards the door
            quad.transform.localScale = new Vector3(width, height, 1f);
            var r = quad.GetComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
        }

        /// <summary>The moment: walking up to the writing shuts the door behind, then the light goes.</summary>
        static void PlaceEvent(Door door, BurstingBulb bulb, Manthing thing)
        {
            var go = new GameObject("ChainRoomEvent");
            var e = go.AddComponent<ChainRoomEvent>();
            e.door = door;
            e.bulb = bulb;
            e.manthing = thing;
            e.triggerCenter = new Vector3(0f, 1.5f, RoomZ1 - 2.2f);   // a couple of steps from the writing
            e.triggerSize = new Vector3(7f, 3f, 4.4f);
            e.doorClose = AssetDatabase.LoadAssetAtPath<AudioClip>(AudioDir + "/metal_door_creak_close.mp3");
            e.doorLands = 4.55f;   // where the recording's creak ends in the latch
            e.slam = AssetDatabase.LoadAssetAtPath<AudioClip>(AudioDir + "/door_slam_boom.wav");
            e.riser = AssetDatabase.LoadAssetAtPath<AudioClip>(AudioDir + "/chainroom_riser.wav");
            EditorUtility.SetDirty(e);
        }

        /// <summary>The usual sound of the place, and the bright bulb humming until it goes.</summary>
        static void AddAmbience(BurstingBulb bulb)
        {
            AmbienceBuilder.ImportSettings();
            var root = new GameObject("Ambience").transform;
            AmbienceBuilder.Loop(root, "Bed_Drone", Vector3.zero, 0.22f, 0f, 0f, 0f, "loop_bed_drone");
            AmbienceBuilder.Loop(root, "Bed_Hum", Vector3.zero, 0.07f, 0f, 0f, 0f, "loop_bed_hum");
            var lamps = GameObject.Find("Lamps");
            if (lamps) foreach (Transform lamp in lamps.transform)
                AmbienceBuilder.Loop(lamp, "Hum", Vector3.up * -0.6f, 0.18f, 1f, 0.4f, 5f, "loop_hum_bulb");
            if (bulb)
            {
                var hum = bulb.transform.Find("Hum");
                if (hum) { bulb.hum = hum.GetComponent<AudioSource>(); EditorUtility.SetDirty(bulb); }
            }
            AmbienceBuilder.Shots(root, "Creaks", new Vector3(0f, RoomHeight + 0.5f, RoomCentre.z), 0.4f, new Vector2(14f, 34f), 8f, 2f, 35f,
                                  AmbienceBuilder.Named("creak_"));
        }
    }
}
