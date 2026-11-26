using System.Collections.Generic;
using System.IO;
using BodycamHorror.AI;
using BodycamHorror.Enemies;
using BodycamHorror.Environment;
using BodycamHorror.Player;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace BodycamHorror.EditorTools
{
    /// <summary>
    /// Builds Assets/Scenes/LightFear_Greybox.unity: the light monster's level as a greybox (plain walls, no dressing).
    ///
    /// A cramped hall wrapped round a lift shaft (a ring the player can always walk round the monster on), three rooms off
    /// it, each with two ways out, joined by short corridors:
    ///   - the pump room (north-west): through the side door the monster beats down, or down the corridor to the crossing;
    ///   - the crossing (west), lit by chemical sticks: a safe zone, with the corridor on to the workshop;
    ///   - the workshop (south-west): by the crossing, or by a short corridor from the hall's bottom corner;
    ///   - the switch room (east): through a shutter in the hall's east wall, or by a corridor with a second safe zone at
    ///     its bend that comes back into the hall by the trigger.
    /// Every room starts shut behind steel grilles (no doors: the monster cannot squeeze through one), and they open a stage at a time:
    /// the trigger at the hall's far end (the way in slams shut, the side door is beaten down: the pump room; the lights die)
    /// -> the valve in the pump room (grilles up: the pump room's back way to the crossing, the crossing to the hall, the
    ///    workshop from the crossing and from the hall, the switch room from the hall and from its back corridor)
    /// -> the fuse on the workshop's bench (small enemies: markers only)
    /// -> the fuse into the fuse box in the switch room, and the emergency power crank beside it turned (red lamps, the
    ///    exit gate past the trigger opens) -> out.
    /// Every corridor mouth is a Doorway: the monster shoots through and cannot be frozen half inside one.
    /// Coordinates (m): x east, z north, as in the sketch; the hall's middle is about (18, 11).
    ///
    /// Dressed with the basement kit (BasementSetup, Assets/Prefabs/Basement; run its Set Up Kit first): brick and
    /// broken plaster on the walls, a concrete floor, the kit's props where the greybox had boxes, and pipes that tell
    /// the player where to go without a word:
    ///   - the big main comes in over the way in, runs round the hall's west side, through the wall over the side door
    ///     the monster beats down and across the pump room straight down to the valve (job 1);
    ///   - the power line (two thin pipes) comes out of the workshop's wall by the bench with the fuse and runs to an
    ///     open fuse box on the switchgear in the switch room: with the fuse in hand it leads east to where it goes in,
    ///     beside the crank (job 3).
    /// Menu: BodycamHorror > Build Light Fear Greybox.
    /// </summary>
    public static class LightFearSceneBuilder
    {
        const string ScenePath = "Assets/Scenes/LightFear_Greybox.unity";
        const string SourceScenePath = "Assets/Scenes/LightingTest.unity";
        const string ChainAudio = "Assets/Audio/ChainRoom";
        const string AmbienceAudio = "Assets/Audio/Ambience";
        const string RampageRunPath = "Assets/NowYouSeeMee/ManthingSecond/SnifferHostileRun.fbx";
        /// <summary>The shotgun lies in the way out, past its gate and short of the end; the monster comes back across the hall.</summary>
        static readonly Vector3 ShotgunAt = new Vector3(18f, 0f, 21.6f);
        static readonly Vector3 AttackFrom = new Vector3(18f, 0f, 4.8f);

        const float H = 3.2f;          // floor to ceiling
        const float T = 0.3f;          // wall thickness
        const float CW = 2.0f;         // corridor, wall centre to wall centre (1.7 m clear)
        const float DoorTop = 2.3f;
        const float Panel = 1.45f;     // oil paint below, plaster above
        const float ManthingPoseHeight = 1.6f, ManthingScale = 1.8f;

        class Room
        {
            public string name;
            public Vector2[] p;
            public List<Vector3>[] holes;   // per edge: (from, to, top) along the edge from its first corner
            public bool closed = true;
            public Room(string name, params Vector2[] p)
            {
                this.name = name; this.p = p;
                holes = new List<Vector3>[p.Length];
                for (int i = 0; i < p.Length; i++) holes[i] = new List<Vector3>();
            }
            public Vector2 A(int e) => p[e];
            public Vector2 B(int e) => p[(e + 1) % p.Length];
        }

        static Room hall, shaft, pump, crossing, switchRoom, workshop, vestibule, exitRoom;
        static readonly List<Room> rooms = new List<Room>();
        static Transform env;
        static Material paint, plaster, floorMat, ceilingMat, metal, wood, rust, chemMat, redMat, wet;
        // metres one repeat covers: the basement scans keep their real size (Assets/Generated/Textures/Basement/sizes.json)
        static float paintTile = LightingTestSceneBuilder.WallTile, plasterTile = LightingTestSceneBuilder.WallTile, floorTile = 2f, ceilingTile = 2.5f;

        const string KitPrefabs = "Assets/Prefabs/Basement";
        const string KitMaterials = "Assets/Generated/Materials/Basement";

        /// <summary>The basement kit's tiling materials over the greybox ones (left as they were if the kit is not set up).</summary>
        static void KitMaterialsOver()
        {
            Material Kit(string n) => AssetDatabase.LoadAssetAtPath<Material>($"{KitMaterials}/{n}.mat");
            Material brick = Kit("Basement_BrickPlaster"), broken = Kit("Basement_DamagedPlaster");
            Material floor = Kit("Basement_FloorConcrete"), ceiling = Kit("Basement_Ceiling");
            if (brick) { paint = brick; paintTile = 1.5f; }
            if (broken) { plaster = broken; plasterTile = 1.85f; }
            if (floor) { floorMat = floor; floorTile = 3f; }
            if (ceiling) { ceilingMat = ceiling; ceilingTile = 2f; }
            if (!brick) Debug.LogWarning("Basement kit not set up (BodycamHorror > Basement > Set Up Kit): building plain");
            // trims: concrete plinth and ledge, steel casings round the openings, brick piers in the corners, worn screed
            trimMat = ceiling ? ceiling : plaster; trimTile = ceiling ? 2f : plasterTile;
            Material steel = Kit("Basement_MetalPaint");
            casingMat = steel ? steel : metal; casingTile = 1f;
            Material piers = Kit("Basement_Brick");
            pierMat = piers ? piers : paint; pierTile = piers ? 1.4f : paintTile;
            screedMat = Kit("Basement_FloorDamaged"); screedTile = 4.5f;
            if (brick) wet = PuddleMaterial();
            // relief: the same sets on BodycamHorror/Tessellated Surface (the kit's *_Tess materials), when they are there
            Material brickTess = Kit("Basement_BrickPlaster_Tess"), plasterTess = Kit("Basement_DamagedPlaster_Tess"), pierTess = Kit("Basement_Brick_Tess");
            if (brickTess) paint = brickTess;
            if (plasterTess) plaster = plasterTess;
            if (pierTess) pierMat = pierTess;
            floorTess = Kit("Basement_Floor_Tess");
            Material ceilingTess = Kit("Basement_Ceiling_Tess");   // no relief, only the broken-up tiling
            if (ceilingTess) { ceilingMat = ceilingTess; trimMat = ceilingTess; }
        }

        static Material floorTess;

        static bool Tessellated(Material m) => m && m.shader && m.shader.name == "BodycamHorror/Tessellated Surface";

        /// <summary>A film of water: see-through dark and glassy, so the concrete shows through and the torch glints
        /// off it (the level's opaque black wet looked like holes on the scanned floor).</summary>
        static Material PuddleMaterial()
        {
            string path = KitMaterials + "/Basement_Puddle.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!mat)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend", 0f);
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = (int)RenderQueue.Transparent;
            mat.SetColor("_BaseColor", new Color(0.03f, 0.032f, 0.03f, 0.5f));
            mat.SetFloat("_Metallic", 0f);
            mat.SetFloat("_Smoothness", 0.96f);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static Material trimMat, casingMat, pierMat, screedMat;
        static float trimTile, casingTile, pierTile, screedTile;

        static Vector2 V(float x, float z) => new Vector2(x, z);
        static Vector3 W(Vector2 v, float y = 0f) => new Vector3(v.x, y, v.y);

        [MenuItem("BodycamHorror/Build Light Fear Greybox")]
        public static void Build()
        {
            LevelSceneBuilder.LoadMaterials();
            paint = LevelSceneBuilder.wallPaint; plaster = LevelSceneBuilder.wallPlaster;
            floorMat = LevelSceneBuilder.floorMat; ceilingMat = LevelSceneBuilder.ceilingMat;
            wood = LevelSceneBuilder.woodDark; rust = LevelSceneBuilder.rust;
            metal = LightingTestSceneBuilder.SimpleMaterial("LightFear_Metal", new Color(0.16f, 0.17f, 0.17f), 0.5f, 0.3f);
            wet = LevelSceneBuilder.wet;
            chemMat = LightingTestSceneBuilder.EmissiveMaterial("LightFear_Chemlight", new Color(0.35f, 1f, 0.4f), 6f);
            redMat = LightingTestSceneBuilder.EmissiveMaterial("LightFear_EmergencyLamp", new Color(1f, 0.1f, 0.06f), 5f);
            KitMaterialsOver();

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
            if (player) player.transform.SetPositionAndRotation(new Vector3(18f, player.transform.position.y, -4.5f), Quaternion.identity);

            env = new GameObject("Environment").transform;
            Layout();
            occupied.Clear();
            BashedDoor bashed = BuildWalls();
            Doors();
            Clutter();
            Pipes();
            RoomDressing();
            Scatter();
            Door entry = EntryDoor();
            Transform lights = Lamps();
            SafeZones();
            GameObject emergency = EmergencyLights();
            var (valve, crank, exitGate, fuseSlot, fuseIn) = Mechanisms();
            FuseSparks(fuseIn);
            Manthing thing = PlaceManthing();
            // the first job is where it shows itself: it comes to the light while the valve turns (Manthing's showdown)
            thing.showdownValve = valve;
            EditorUtility.SetDirty(thing);
            Markers();
            Doorways();

            var nav = new GameObject("NavMesh").AddComponent<RuntimeNavMesh>();
            nav.transform.position = new Vector3(18f, 0f, 9.5f);
            nav.size = new Vector3(44f, 8f, 38f);

            var e = new GameObject("LightFearEvent").AddComponent<LightFearEvent>();
            e.triggerCenter = new Vector3(18f, 1.5f, 18.2f);
            e.triggerSize = new Vector3(5f, 3f, 2.2f);
            e.entryDoor = entry;
            e.bashedDoor = bashed;
            e.manthing = thing;
            e.levelLights = lights;
            e.slam = Clip(ChainAudio + "/door_slam_boom.wav");
            e.riser = Clip(ChainAudio + "/chainroom_riser.wav");
            e.bashHum = Clip(AmbienceAudio + "/bed_facility_hum.mp3");
            e.bulbBurst = Clip(ChainAudio + "/bulb_burst.wav");
            e.bulbBurstLayer = Clip(ChainAudio + "/lamp_break_drop.mp3");
            e.powerCrank = crank;
            e.fuseBox = fuseSlot;
            e.fuseInBox = fuseIn;
            e.openOnFuse = fuseGates;
            e.emergencyLights = emergency;
            e.exitGate = exitGate;
            e.powerOnClip = Clip(AmbienceAudio + "/Prepared/creak_metallic_groan.wav");
            e.exitCenter = new Vector3(18f, 1.5f, 23.6f);
            e.exitSize = new Vector3(2.4f, 3f, 2f);
            e.shotgun = PlaceShotgun(player);
            e.attackFrom = AttackFrom;
            EditorUtility.SetDirty(e);

            GameLook.ApplyAtmosphere();
            foreach (AutoExposure exposure in Object.FindObjectsByType<AutoExposure>(FindObjectsInactive.Include))
            {
                exposure.maxStops = GameLook.MaxStops;
                EditorUtility.SetDirty(exposure);
            }
            LightingTestSceneBuilder.AddDust();
            AmbienceBuilder.ImportSettings();
            var amb = new GameObject("Ambience").transform;
            AmbienceBuilder.Loop(amb, "Bed_Drone", Vector3.zero, 0.22f, 0f, 0f, 0f, "loop_bed_drone");

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AddToBuild();
            AssetDatabase.SaveAssets();
            Debug.Log("Light fear greybox built: " + ScenePath);
        }

        static AudioClip Clip(string path) => AssetDatabase.LoadAssetAtPath<AudioClip>(path);

        static void AddToBuild()
        {
            var list = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            if (list.Exists(s => s.path == ScenePath)) return;
            list.Add(new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = list.ToArray();
        }

        // ---------------------------------------------------------------- the plan

        static Vector2 brokenL, brokenR;   // where the side door's corridor meets the hall wall

        /// <summary>Where a corridor meets a room: its two wall lines there, and the way into the corridor.</summary>
        struct Mouth { public Vector2 l, r, inward; public Vector2 Mid => (l + r) * 0.5f; }
        /// <summary>Corridor mouths by "{corridor}_A" (its start) and "{corridor}_B" (its end).</summary>
        static readonly Dictionary<string, Mouth> mouths = new Dictionary<string, Mouth>();

        static void Layout()
        {
            rooms.Clear();
            mouths.Clear();
            hall = Add(new Room("Hall", V(14.7f, 3.48f), V(21.3f, 3.48f), V(23.1f, 6.3f), V(23.52f, 14.1f), V(21.9f, 19.5f), V(14.1f, 19.5f), V(12.48f, 14.1f), V(12.9f, 6.3f)));
            shaft = Add(new Room("Shaft", V(16.32f, 14.7f), V(19.68f, 14.7f), V(20.64f, 11.28f), V(19.32f, 7.92f), V(16.68f, 7.92f), V(15.36f, 11.28f)));
            pump = Add(new Room("PumpRoom", V(1.2f, 23.28f), V(9.6f, 24f), V(11.1f, 19.8f), V(9.0f, 16.5f), V(1.8f, 16.8f)));
            crossing = Add(new Room("Crossing", V(3f, 10.2f), V(6.6f, 10.2f), V(6.6f, 13.8f), V(3f, 13.8f)));
            workshop = Add(new Room("Workshop", V(1.2f, 9f), V(9f, 9.3f), V(10.5f, 5.4f), V(8.4f, 0.9f), V(1.8f, 1.2f)));
            switchRoom = Add(new Room("SwitchRoom", V(26.04f, 16.08f), V(33.6f, 17.4f), V(35.4f, 12f), V(33.6f, 7.2f), V(26.52f, 7.8f)));
            vestibule = Add(new Room("Vestibule", V(16.68f, -6f), V(19.32f, -6f), V(19.32f, 3.48f), V(16.68f, 3.48f)));
            exitRoom = Add(new Room("ExitRoom", V(16.8f, 19.5f), V(16.8f, 25f), V(19.2f, 25f), V(19.2f, 19.5f)));

            // the way in and the way out open straight into the hall (no corridor between)
            Open(hall, 0, V(16.68f, 3.48f), V(19.32f, 3.48f), H);
            Open(vestibule, 2, V(19.32f, 3.48f), V(16.68f, 3.48f), H);
            Open(hall, 4, V(19.2f, 19.5f), V(16.8f, 19.5f), H);
            Open(exitRoom, 3, V(19.2f, 19.5f), V(16.8f, 19.5f), H);

            // doors that go nowhere: sealed bunker doors on three walls, a locked steel door in the workshop
            doorHoles.Clear(); doorSpots.Clear();
            DoorHole(hall, 6, 0.628f, false);        // hall's west wall, between the crate and the corridor to the crossing
            DoorHole(pump, 1, 0.5f, false);
            DoorHole(switchRoom, 2, 0.5f, false);
            DoorHole(workshop, 1, 0.5f, true);
        }

        static readonly List<Vector2> doorHoles = new List<Vector2>();
        static readonly List<(Room room, int edge, Vector2 at, bool steel)> doorSpots = new List<(Room, int, Vector2, bool)>();

        /// <summary>A hole for a door t of the way along a room's edge: the kit's bunker door (1.0 x 2.1) or the
        /// project's steel door (DoorBuilder's opening).</summary>
        static void DoorHole(Room r, int e, float t, bool steel)
        {
            Vector2 c = Vector2.Lerp(r.A(e), r.B(e), t), dir = (r.B(e) - r.A(e)).normalized;
            float w = steel ? DoorBuilder.OpeningWidth : 1.0f, top = steel ? DoorBuilder.OpeningHeight : 2.1f;
            Open(r, e, c - dir * w / 2, c + dir * w / 2, top);
            doorHoles.Add(c);
            doorSpots.Add((r, e, c, steel));
        }

        /// <summary>The doors into their holes, fronts into the rooms. None of them opens.</summary>
        static void Doors()
        {
            var g = Group("Doors", env);
            foreach (var (r, e, c, steel) in doorSpots)
            {
                Vector2 dir = (r.B(e) - r.A(e)).normalized, inward = Left(dir) * Inward(r);
                if (!steel)
                {
                    var d = Prop(g, "Door_Bunker", c.x, c.y);
                    if (d) d.transform.rotation = Quaternion.LookRotation(W(inward), Vector3.up);
                    continue;
                }
                Vector2 along = Vector2.Dot(Left(dir), inward) > 0f ? dir : -dir;   // the door's front faces left of along
                var go = DoorBuilder.Place(W(c - along * DoorBuilder.OpeningWidth / 2), W(along), false, g);
                go.name = "Door_Locked";
                var door = go.GetComponent<Door>();
                door.startOpen = false;
                door.locked = true;
                door.enemiesCanOpen = false;
                EditorUtility.SetDirty(door);
            }
        }

        static Room Add(Room r) { rooms.Add(r); return r; }

        static float Cross(Vector2 u, Vector2 v) => u.x * v.y - u.y * v.x;
        static Vector2 Left(Vector2 d) => new Vector2(-d.y, d.x);

        /// <summary>Cuts an opening in a room's edge between where two points fall along it.</summary>
        static void Open(Room r, int edge, Vector2 p, Vector2 q, float top)
        {
            Vector2 a = r.A(edge), dir = (r.B(edge) - a).normalized;
            float sa = Vector2.Dot(p - a, dir), sb = Vector2.Dot(q - a, dir);
            r.holes[edge].Add(new Vector3(Mathf.Min(sa, sb), Mathf.Max(sa, sb), top));
        }

        /// <summary>Where the line through p along d crosses the line a-b.</summary>
        static Vector2 Meet(Vector2 p, Vector2 d, Vector2 a, Vector2 b)
        {
            Vector2 e = b - a;
            float den = Cross(d, e);
            if (Mathf.Abs(den) < 1e-5f) return p;
            return p + d * (Cross(a - p, e) / den);
        }

        /// <summary>
        /// A corridor along a polyline from one room's edge to another's: two walls offset to the sides (mitred at the
        /// bends), trimmed where they meet the rooms' walls, and the openings cut in those walls to match.
        /// Returns the corridor's mouths: (left, right) at the start, (left, right) at the end.
        /// </summary>
        static (Vector2, Vector2, Vector2, Vector2) Corridor(Transform parent, string name, Vector2[] pts, Room ra, int ea, Room rb, int eb,
                                                           float width = CW, float topA = H, float topB = H)
        {
            int n = pts.Length;
            var L = new Vector2[n];
            var R = new Vector2[n];
            for (int i = 0; i < n; i++)
            {
                Vector2 dPrev = (i > 0 ? pts[i] - pts[i - 1] : pts[1] - pts[0]).normalized;
                Vector2 dNext = (i < n - 1 ? pts[i + 1] - pts[i] : dPrev).normalized;
                Vector2 m = (Left(dPrev) + Left(dNext)).normalized;
                float k = width * 0.5f / Mathf.Max(0.35f, Vector2.Dot(m, Left(dPrev)));
                L[i] = pts[i] + m * k;
                R[i] = pts[i] - m * k;
            }
            L[0] = Meet(L[0], L[1] - L[0], ra.A(ea), ra.B(ea));
            R[0] = Meet(R[0], R[1] - R[0], ra.A(ea), ra.B(ea));
            L[n - 1] = Meet(L[n - 1], L[n - 1] - L[n - 2], rb.A(eb), rb.B(eb));
            R[n - 1] = Meet(R[n - 1], R[n - 1] - R[n - 2], rb.A(eb), rb.B(eb));
            Open(ra, ea, L[0], R[0], topA);
            Open(rb, eb, L[n - 1], R[n - 1], topB);
            mouths[name + "_A"] = new Mouth { l = L[0], r = R[0], inward = (pts[1] - pts[0]).normalized };
            mouths[name + "_B"] = new Mouth { l = L[n - 1], r = R[n - 1], inward = (pts[n - 2] - pts[n - 1]).normalized };

            var g = Group(name, parent);
            // two walls mitred at the bends, stopping square on the rooms' wall lines; the trims on the corridor's side
            PolyWall(g, L, false, null);
            PolyWall(g, R, false, null);
            Trims(g, L, false, null, -1f);
            Trims(g, R, false, null, 1f);
            return (L[0], R[0], L[n - 1], R[n - 1]);
        }

        static BashedDoor BuildWalls()
        {
            var corridors = Group("Corridors", env);
            // the side door the monster beats down: hall -> pump room (narrower: one door's width)
            var mouth = Corridor(corridors, "Corridor_SideDoor", new[] { V(13.08f, 16.8f), V(9.955f, 18f) }, hall, 5, pump, 2, 1.6f, DoorTop, H);
            brokenL = mouth.Item1; brokenR = mouth.Item2;
            // pump room -> crossing -> workshop, and the crossing out to the hall
            Corridor(corridors, "Corridor_PumpCrossing", new[] { V(4.8f, 16.7f), V(4.8f, 13.8f) }, pump, 3, crossing, 2);
            Corridor(corridors, "Corridor_CrossingWorkshop", new[] { V(4.8f, 10.2f), V(4.8f, 9.14f) }, crossing, 0, workshop, 0);
            Corridor(corridors, "Corridor_CrossingHall", new[] { V(6.6f, 12f), V(12.59f, 12f) }, crossing, 1, hall, 6);
            // workshop -> the hall's bottom-left corner
            Corridor(corridors, "Corridor_WorkshopHall", new[] { V(9.45f, 3.15f), V(14.11f, 4.4f) }, workshop, 2, hall, 7);
            // hall -> switch room (the shutter), and the switch room's back corridor round to the hall by the trigger
            Corridor(corridors, "Corridor_HallSwitch", new[] { V(23.41f, 12f), V(26.28f, 12f) }, hall, 2, switchRoom, 4);
            Corridor(corridors, "Corridor_SwitchBack", new[] { V(31.2f, 16.98f), V(30f, 21f), V(24.3f, 21.3f), V(22.21f, 18.48f) }, switchRoom, 0, hall, 3);

            var walls = Group("Rooms", env);
            foreach (Room r in rooms)
            {
                var g = Group(r.name, walls);
                PolyWall(g, r.p, true, r.holes);
                if (r == vestibule || r == exitRoom) continue;   // narrow passages: plain walls
                float inward = Inward(r);
                Trims(g, r.p, true, r.holes, inward);
                if (r == shaft) continue;
                Casings(g, r, inward);
                if (r != crossing) CornerPiers(g, r, inward);
            }

            // one floor and one ceiling over the whole plan; worn screed over some rooms, beams under the ceiling
            var floorBox = Box("Floor", env, new Vector3(-2f, -0.2f, -8f), new Vector3(38f, 0f, 27f), floorMat, floorTile);
            if (floorTess)
            {
                floorBox.GetComponent<MeshRenderer>().enabled = false;   // stays as the flat ground everyone walks on
                FloorGrid(env, -2f, 38f, -8f, 27f);
            }
            Box("Ceiling", env, new Vector3(-2f, H, -8f), new Vector3(38f, H + 0.2f, 27f), ceilingMat, ceilingTile);
            FloorsAndCeilings();

            return SideDoor();
        }

        /// <summary>Lamps and red emergency lamps (as in Lamps / EmergencyLights): no beam passes over them.</summary>
        static readonly Vector2[] CeilingFixtures =
        {
            V(18f, -3f), V(18f, 5.6f), V(13.9f, 11f), V(22.2f, 11f), V(14.9f, 16f), V(18f, 17.3f), V(6f, 20.5f), V(5.5f, 5f), V(30.5f, 15f),
            V(18f, 5f), V(13.6f, 11f), V(22.4f, 11f), V(18f, 18f), V(5f, 5f), V(5.5f, 20.5f), V(30.5f, 14f), V(18f, 22.5f),
        };

        /// <summary>Worn screed over the side rooms' floors, puddles that catch the torch, drains, a few vents high on
        /// the walls, and concrete beams across the ceilings.</summary>
        static void FloorsAndCeilings()
        {
            var g = Group("FloorAndCeiling", env);
            // the tessellated floor brings its own rust, grime and standing water: no screed or puddle discs over it
            if (screedMat && !floorTess) foreach (Room r in new[] { pump, switchRoom, workshop, crossing }) Screed(g, r);

            foreach (var (x, z, sx, sz, yaw) in new[]
            {
                (19.6f, 5.3f, 1.3f, 0.7f, 15f), (21.0f, 15.2f, 0.9f, 1.1f, -30f), (6.4f, 19.6f, 1.4f, 0.9f, 10f), (3.7f, 17.6f, 0.7f, 0.5f, 40f),
                (29.2f, 14.7f, 1.1f, 0.8f, -15f), (4.6f, 6.1f, 1.0f, 0.7f, 25f), (18.4f, -2.2f, 0.8f, 1.2f, 5f), (5.6f, 10.9f, 0.6f, 0.5f, 0f),
            })
            {
                if (!wet || floorTess) break;
                var p = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                p.name = "Puddle";
                Object.DestroyImmediate(p.GetComponent<Collider>());
                p.transform.SetParent(g, false);
                p.transform.SetPositionAndRotation(new Vector3(x, 0.0065f, z), Quaternion.Euler(0f, yaw, 0f));
                p.transform.localScale = new Vector3(sx, 0.001f, sz);
                var mr = p.GetComponent<MeshRenderer>();
                mr.sharedMaterial = wet;
                mr.shadowCastingMode = ShadowCastingMode.Off;
                p.isStatic = true;
            }
            foreach (var (x, z) in new[] { (18.7f, -1.2f), (21.2f, 6.2f), (5.3f, 18.6f), (31.3f, 15.2f), (3.2f, 3.2f), (4.0f, 13.2f) })
                Prop(g, "Floor_Drain", x, z, 0f, 0.0045f);

            var vent = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Models/Khrushchevka/WallVent/WallVent.prefab");
            if (vent)
                foreach (var (r, e, t) in new[] { (hall, 4, 0.85f), (pump, 0, 0.3f), (workshop, 1, 0.4f), (switchRoom, 0, 0.25f) })
                {
                    float len = Vector2.Distance(r.A(e), r.B(e));
                    Vector2 face = OnEdge(r.p, true, e, len * t, Inward(r) * T / 2);
                    Vector2 inward = Left((r.B(e) - r.A(e)).normalized) * Inward(r);
                    var v = (GameObject)PrefabUtility.InstantiatePrefab(vent, g);
                    v.transform.SetPositionAndRotation(W(face, 2.55f), Quaternion.LookRotation(W(inward), Vector3.up));
                }

            foreach (Room r in new[] { hall, pump, switchRoom, workshop }) Beams(g, r);
        }

        /// <summary>The visible floor for the tessellated material: a flat grid of half-metre cells (something for the
        /// tessellation to cut finer), UVs in metres, no collider (the flat box under it is the ground).</summary>
        static void FloorGrid(Transform parent, float x0, float x1, float z0, float z1)
        {
            const float cell = 0.5f;
            int nx = Mathf.CeilToInt((x1 - x0) / cell), nz = Mathf.CeilToInt((z1 - z0) / cell);
            var verts = new List<Vector3>(); var uvs = new List<Vector2>(); var norms = new List<Vector3>(); var tris = new List<int>();
            var centre = new Vector3((x0 + x1) * 0.5f, 0f, (z0 + z1) * 0.5f);
            for (int j = 0; j <= nz; j++)
                for (int i = 0; i <= nx; i++)
                {
                    var q = new Vector3(Mathf.Min(x0 + i * cell, x1), 0f, Mathf.Min(z0 + j * cell, z1));
                    verts.Add(q - centre); uvs.Add(new Vector2(q.x, q.z)); norms.Add(Vector3.up);
                }
            for (int j = 0; j < nz; j++)
                for (int i = 0; i < nx; i++)
                {
                    int a = j * (nx + 1) + i, b = a + 1, c = a + nx + 2, d = a + nx + 1;
                    tris.AddRange(new[] { a, d, c, a, c, b });   // facing up
                }
            var mesh = new Mesh { name = "FloorGrid", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.SetVertices(verts); mesh.SetNormals(norms); mesh.SetUVs(0, uvs); mesh.SetTriangles(tris, 0);
            mesh.RecalculateTangents(); mesh.RecalculateBounds();
            mesh.bounds = new Bounds(mesh.bounds.center, mesh.bounds.size + Vector3.one * 0.2f);
            var go = new GameObject("Floor_Relief", typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, false);
            go.transform.position = centre;
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            go.GetComponent<MeshRenderer>().sharedMaterial = floorTess;
            go.isStatic = true;
        }

        /// <summary>A room's floor again 4 mm up, inside its walls: the worn screed.</summary>
        static void Screed(Transform parent, Room r)
        {
            int n = r.p.Length;
            var pts = new Vector3[n];
            Vector3 c = Vector3.zero;
            for (int i = 0; i < n; i++) { pts[i] = W(Corner(r.p, true, i, Inward(r) * T / 2), 0.004f); c += pts[i]; }
            c /= n;
            var verts = new List<Vector3> { Vector3.zero };
            var uvs = new List<Vector2> { new Vector2(c.x / screedTile, c.z / screedTile) };
            foreach (var q in pts) { verts.Add(q - c); uvs.Add(new Vector2(q.x / screedTile, q.z / screedTile)); }
            var tris = new List<int>();
            for (int i = 0; i < n; i++)
            {
                int a = 1 + i, b = 1 + (i + 1) % n;
                if (Vector3.Cross(verts[a], verts[b]).y > 0f) tris.AddRange(new[] { 0, a, b }); else tris.AddRange(new[] { 0, b, a });
            }
            var mesh = new Mesh { name = "Screed_" + r.name };
            mesh.SetVertices(verts); mesh.SetUVs(0, uvs); mesh.SetTriangles(tris, 0);
            var nrm = new Vector3[verts.Count];
            for (int i = 0; i < nrm.Length; i++) nrm[i] = Vector3.up;
            mesh.normals = nrm;
            mesh.RecalculateTangents(); mesh.RecalculateBounds();
            var go = new GameObject("Screed_" + r.name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, false);
            go.transform.position = c;
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            go.GetComponent<MeshRenderer>().sharedMaterial = screedMat;
            go.isStatic = true;
        }

        /// <summary>Concrete beams across a room under the ceiling, east-west, every 2.6 m, wall to wall (into the
        /// walls by half their thickness); shifted along where one would run over a lamp. 16 cm deep: the pipes
        /// pass under them.</summary>
        static void Beams(Transform parent, Room r)
        {
            float zMin = float.MaxValue, zMax = float.MinValue;
            foreach (var v in r.p) { zMin = Mathf.Min(zMin, v.y); zMax = Mathf.Max(zMax, v.y); }
            for (float z0 = zMin + 1.3f; z0 < zMax - 0.8f; z0 += 2.6f)
            {
                float z = z0;
                var xs = new List<float>();
                for (int tries = 0; tries < 4; tries++)
                {
                    xs.Clear();
                    for (int e = 0; e < r.p.Length; e++)
                    {
                        Vector2 a = r.A(e), b = r.B(e);
                        if ((a.y - z) * (b.y - z) < 0f) xs.Add(a.x + (z - a.y) / (b.y - a.y) * (b.x - a.x));
                    }
                    xs.Sort();
                    float zz = z;
                    bool clash = xs.Count >= 2 && System.Array.Exists(CeilingFixtures, f => Mathf.Abs(f.y - zz) < 0.45f && f.x > xs[0] && f.x < xs[xs.Count - 1]);
                    if (!clash) break;
                    z += 0.5f;
                }
                for (int k = 0; k + 1 < xs.Count; k += 2)
                    Box("Beam", parent, new Vector3(xs[k], H - 0.16f, z - 0.14f), new Vector3(xs[k + 1], H, z + 0.14f), trimMat, trimTile);
            }
        }

        /// <summary>A straight wall a-b with openings (from, to, top) cut out, square at both ends.</summary>
        static void WallLine(Transform parent, Vector2 a, Vector2 b, List<Vector3> holes) =>
            PolyWall(parent, new[] { a, b }, false, new[] { holes });

        // ---------------------------------------------------------------- walls: mitred slabs, nothing overlapping

        // The greybox's walls were boxes run on past each corner, so at the joints their faces lay in one plane and
        // flickered. Now each run is a slab mitred into its neighbours (square where a run stops), so no two faces
        // ever share a plane; the trims on top follow the same corners.

        static float Area(Vector2[] p)
        {
            float a = 0f;
            for (int i = 0; i < p.Length; i++) a += Cross(p[i], p[(i + 1) % p.Length]);
            return a * 0.5f;
        }

        /// <summary>+1 if a room's inside is to the left of its edges, -1 if to the right; the shaft is seen from outside.</summary>
        static float Inward(Room r) => (Area(r.p) > 0f ? 1f : -1f) * (r == shaft ? -1f : 1f);

        /// <summary>Vertex i of a wall line, offset d to the left of it: where the offset lines of its two edges meet
        /// (a closed line has a mitre at every vertex; an open one is square at its two ends).</summary>
        static Vector2 Corner(Vector2[] p, bool closed, int i, float d)
        {
            int n = p.Length;
            bool hasPrev = closed || i > 0, hasNext = closed || i < n - 1;
            Vector2 dPrev = hasPrev ? (p[i] - p[(i - 1 + n) % n]).normalized : Vector2.zero;
            Vector2 dNext = hasNext ? (p[(i + 1) % n] - p[i]).normalized : Vector2.zero;
            if (!hasPrev) return p[i] + Left(dNext) * d;
            if (!hasNext) return p[i] + Left(dPrev) * d;
            Vector2 a = p[i] + Left(dPrev) * d, b = p[i] + Left(dNext) * d;
            if (Mathf.Abs(Cross(dPrev, dNext)) < 1e-4f) return b;
            return Meet(a, dPrev, b, b + dNext);
        }

        /// <summary>The point s along edge e, offset d to its left: mitred at the edge's ends, square in between.</summary>
        static Vector2 OnEdge(Vector2[] p, bool closed, int e, float s, float d)
        {
            Vector2 a = p[e], b = p[(e + 1) % p.Length];
            float len = Vector2.Distance(a, b);
            if (s <= 1e-4f) return Corner(p, closed, e, d);
            if (s >= len - 1e-4f) return Corner(p, closed, (e + 1) % p.Length, d);
            Vector2 dir = (b - a) / len;
            return a + dir * s + Left(dir) * d;
        }

        /// <summary>The solid stretches of an edge: (from, to, bottom), full height between the holes and a lintel
        /// (from the hole's top up) over each hole that does not reach the ceiling.</summary>
        static List<Vector3> Spans(float len, List<Vector3> holes)
        {
            var list = new List<Vector3>();
            var sorted = holes != null ? new List<Vector3>(holes) : new List<Vector3>();
            sorted.Sort((u, v) => u.x.CompareTo(v.x));
            float at = 0f;
            foreach (Vector3 h in sorted)
            {
                float h0 = Mathf.Clamp(h.x, 0f, len), h1 = Mathf.Clamp(h.y, 0f, len);
                if (h0 > at + 0.001f) list.Add(new Vector3(at, h0, 0f));
                if (h.z < H - 0.01f && h1 > h0) list.Add(new Vector3(h0, h1, h.z));
                at = Mathf.Max(at, h1);
            }
            if (len > at + 0.001f) list.Add(new Vector3(at, len, 0f));
            return list;
        }

        /// <summary>A wall T thick centred on a line (closed: round a room), with holes per edge: brick up to the
        /// panel line, broken plaster above it.</summary>
        static void PolyWall(Transform parent, Vector2[] p, bool closed, List<Vector3>[] holes)
        {
            int edges = closed ? p.Length : p.Length - 1;
            for (int e = 0; e < edges; e++)
            {
                float len = Vector2.Distance(p[e], p[(e + 1) % p.Length]);
                foreach (Vector3 s in Spans(len, holes != null ? holes[e] : null))
                {
                    Vector2 a0 = OnEdge(p, closed, e, s.x, -T / 2), a1 = OnEdge(p, closed, e, s.y, -T / 2);
                    Vector2 b0 = OnEdge(p, closed, e, s.x, T / 2), b1 = OnEdge(p, closed, e, s.y, T / 2);
                    if (s.z < Panel) Slab(parent, "Wall_Brick", a0, a1, b1, b0, s.z, Mathf.Min(H, Panel), paint, paintTile);
                    if (H > Panel) Slab(parent, "Wall_Plaster", a0, a1, b1, b0, Mathf.Max(s.z, Panel), H, plaster, plasterTile);
                }
            }
        }

        /// <summary>A concrete plinth along the foot of a wall and a ledge on its panel line, on the side given
        /// (+1 left of the line, -1 right), broken where the wall is.</summary>
        static void Trims(Transform parent, Vector2[] p, bool closed, List<Vector3>[] holes, float side)
        {
            int edges = closed ? p.Length : p.Length - 1;
            for (int e = 0; e < edges; e++)
            {
                float len = Vector2.Distance(p[e], p[(e + 1) % p.Length]);
                foreach (Vector3 s in Spans(len, holes != null ? holes[e] : null))
                {
                    if (s.z > 0.001f) continue;   // lintels: nothing to stand on the floor
                    void Strip(string name, float d0, float d1, float y0, float y1)
                    {
                        Vector2 a0 = OnEdge(p, closed, e, s.x, side * d0), a1 = OnEdge(p, closed, e, s.y, side * d0);
                        Vector2 b0 = OnEdge(p, closed, e, s.x, side * d1), b1 = OnEdge(p, closed, e, s.y, side * d1);
                        Slab(parent, name, a0, a1, b1, b0, y0, y1, trimMat, trimTile, false);
                    }
                    Strip("Plinth", T / 2, T / 2 + 0.05f, 0f, 0.2f);
                    Strip("Ledge", T / 2, T / 2 + 0.035f, Panel - 0.025f, Panel + 0.03f);
                }
            }
        }

        /// <summary>Steel channel round the openings out of a room (not the doors: they bring their own frames).</summary>
        static void Casings(Transform parent, Room r, float inward)
        {
            for (int e = 0; e < r.p.Length; e++)
            {
                float len = Vector2.Distance(r.A(e), r.B(e));
                foreach (Vector3 h in r.holes[e])
                {
                    if (h.x < 0.05f || h.y > len - 0.05f) continue;   // the whole edge open
                    Vector2 mid = Vector2.Lerp(r.A(e), r.B(e), (h.x + h.y) * 0.5f / len);
                    if (doorHoles.Exists(c => Vector2.Distance(c, mid) < 0.3f)) continue;
                    float top = Mathf.Min(h.z, H);
                    void Part(float s0, float s1, float y0, float y1)
                    {
                        s0 = Mathf.Clamp(s0, 0f, len); s1 = Mathf.Clamp(s1, 0f, len);
                        if (s1 - s0 < 0.02f) return;
                        Vector2 a0 = OnEdge(r.p, true, e, s0, inward * (T / 2 - 0.02f)), a1 = OnEdge(r.p, true, e, s1, inward * (T / 2 - 0.02f));
                        Vector2 b0 = OnEdge(r.p, true, e, s0, inward * (T / 2 + 0.06f)), b1 = OnEdge(r.p, true, e, s1, inward * (T / 2 + 0.06f));
                        Slab(parent, "Casing", a0, a1, b1, b0, y0, y1, casingMat, casingTile, false);
                    }
                    Part(h.x - 0.12f, h.x, 0f, top);
                    Part(h.y, h.y + 0.12f, 0f, top);
                    if (top < H - 0.05f) Part(h.x - 0.12f, h.y + 0.12f, top, Mathf.Min(top + 0.12f, H));
                }
            }
        }

        /// <summary>A square brick pier in each corner of a room, turned to the corner's bisector (not where an
        /// opening comes close to the corner).</summary>
        static void CornerPiers(Transform parent, Room r, float inward)
        {
            int n = r.p.Length;
            for (int i = 0; i < n; i++)
            {
                int ePrev = (i - 1 + n) % n;
                float lenPrev = Vector2.Distance(r.A(ePrev), r.B(ePrev));
                bool near = r.holes[i].Exists(h => h.x < 0.75f) || r.holes[ePrev].Exists(h => h.y > lenPrev - 0.75f);
                if (near) continue;
                Vector2 dPrev = (r.p[i] - r.p[ePrev]).normalized, dNext = (r.p[(i + 1) % n] - r.p[i]).normalized;
                Vector2 u = ((Left(dPrev) + Left(dNext)) * inward).normalized, w = Left(u);
                Vector2 c = Corner(r.p, true, i, inward * T / 2) + u * 0.12f;
                const float half = 0.22f;
                Slab(parent, "Pier", c - u * half - w * half, c - u * half + w * half, c + u * half + w * half, c + u * half - w * half,
                     0f, H, pierMat, pierTile);
            }
        }

        /// <summary>
        /// A solid between two horizontal quads: a0-a1 one face's ends, b0-b1 the other's, from y0 up to y1. Six
        /// faces, each turned outwards; UVs tile in world metres (along the face and up it; x-z on top and bottom),
        /// so neighbouring slabs line up.
        /// </summary>
        static GameObject Slab(Transform parent, string name, Vector2 a0, Vector2 a1, Vector2 b1, Vector2 b0, float y0, float y1,
                               Material m, float tile, bool collide = true)
        {
            if (y1 - y0 < 0.005f) return null;
            Vector3 P(Vector2 v, float y) => new Vector3(v.x, y, v.y);
            Vector3[] lo = { P(a0, y0), P(a1, y0), P(b1, y0), P(b0, y0) }, hi = { P(a0, y1), P(a1, y1), P(b1, y1), P(b0, y1) };
            Vector3 centre = Vector3.zero;
            foreach (var v in lo) centre += v;
            foreach (var v in hi) centre += v;
            centre /= 8f;
            var verts = new List<Vector3>(); var norms = new List<Vector3>(); var uvs = new List<Vector2>(); var tris = new List<int>();
            var colors = new List<Color>();
            bool tess = Tessellated(m);
            void Face(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3)
            {
                Vector3 nrm = Vector3.Cross(p1 - p0, p2 - p0);
                if (nrm.sqrMagnitude < 1e-10f) nrm = Vector3.Cross(p2 - p0, p3 - p0);
                if (nrm.sqrMagnitude < 1e-10f) return;   // a face squeezed to nothing
                nrm.Normalize();
                if (Vector3.Dot(nrm, (p0 + p1 + p2 + p3) * 0.25f - centre) < 0f) { var t = p1; p1 = p3; p3 = t; nrm = -nrm; }
                Vector3 along = Mathf.Abs(nrm.y) > 0.5f ? Vector3.right : Vector3.Cross(Vector3.up, nrm).normalized;
                Vector2 UV(Vector3 q) => Mathf.Abs(nrm.y) > 0.5f ? new Vector2(q.x / tile, q.z / tile) : new Vector2(Vector3.Dot(q, along) / tile, q.y / tile);
                int s = verts.Count;
                if (tess && Mathf.Abs(nrm.y) < 0.5f)
                {
                    // a wall face for the tessellation: cut into cells of at most half a metre, with a 4 cm strip round
                    // the border; vertex colour R = 0 on the border (no displacement there), 1 inside
                    float[] Cuts(float len)
                    {
                        var c = new List<float> { 0f };
                        float b = Mathf.Min(0.04f / Mathf.Max(len, 0.01f), 0.25f);
                        int n = Mathf.Max(1, Mathf.CeilToInt((len * (1f - 2f * b)) / 0.5f));
                        for (int k = 0; k <= n; k++) c.Add(Mathf.Lerp(b, 1f - b, k / (float)n));
                        c.Add(1f);
                        return c.ToArray();
                    }
                    float[] us = Cuts(Vector3.Distance(p0, p1)), vs = Cuts(Vector3.Distance(p0, p3));
                    for (int j = 0; j < vs.Length; j++)
                        for (int i = 0; i < us.Length; i++)
                        {
                            Vector3 q = Vector3.Lerp(Vector3.Lerp(p0, p1, us[i]), Vector3.Lerp(p3, p2, us[i]), vs[j]);
                            verts.Add(q - centre); norms.Add(nrm); uvs.Add(UV(q));
                            bool border = i == 0 || j == 0 || i == us.Length - 1 || j == vs.Length - 1;
                            colors.Add(border ? Color.black : Color.white);
                        }
                    for (int j = 0; j + 1 < vs.Length; j++)
                        for (int i = 0; i + 1 < us.Length; i++)
                        {
                            int a = s + j * us.Length + i, b2 = a + 1, c2 = a + us.Length + 1, d = a + us.Length;
                            tris.AddRange(new[] { a, b2, c2, a, c2, d });
                        }
                    return;
                }
                foreach (var q in new[] { p0, p1, p2, p3 }) { verts.Add(q - centre); norms.Add(nrm); uvs.Add(UV(q)); colors.Add(Color.black); }
                tris.AddRange(new[] { s, s + 1, s + 2, s, s + 2, s + 3 });
            }
            Face(lo[0], lo[1], lo[2], lo[3]);
            Face(hi[0], hi[1], hi[2], hi[3]);
            for (int k = 0; k < 4; k++) Face(lo[k], lo[(k + 1) % 4], hi[(k + 1) % 4], hi[k]);
            var mesh = new Mesh { name = name };
            mesh.SetVertices(verts); mesh.SetNormals(norms); mesh.SetUVs(0, uvs); mesh.SetColors(colors); mesh.SetTriangles(tris, 0);
            mesh.RecalculateTangents(); mesh.RecalculateBounds();
            if (tess) mesh.bounds = new Bounds(mesh.bounds.center, mesh.bounds.size + Vector3.one * 0.1f);   // room for the displacement
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, false);
            go.transform.position = centre;
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            go.GetComponent<MeshRenderer>().sharedMaterial = m;
            if (collide) go.AddComponent<MeshCollider>().sharedMesh = mesh;
            go.isStatic = true;
            return go;
        }

        /// <summary>The project's world-tiled box, built in the parent's own axes (it may be turned).</summary>
        static GameObject LocalBox(string name, Transform parent, Vector3 min, Vector3 max, Material m, float tile)
        {
            var go = LightingTestSceneBuilder.Box(name, null, min, max, m, tile, tile);   // at the local centre, unparented
            go.transform.SetParent(parent, false);
            return go;
        }

        static GameObject Box(string name, Transform parent, Vector3 min, Vector3 max, Material m, float tile) =>
            LightingTestSceneBuilder.Box(name, parent, min, max, m, tile, tile);

        // ---------------------------------------------------------------- doors

        /// <summary>The side door in the hall's west wall: a plain wooden leaf filling the hole, beaten off from behind.</summary>
        static BashedDoor SideDoor()
        {
            Vector2 mid = (brokenL + brokenR) * 0.5f, along = (brokenR - brokenL).normalized;
            Vector2 intoHall = Left(along);
            if (Vector2.Dot(intoHall, V(18f, 11f) - mid) < 0f) intoHall = -intoHall;
            var go = new GameObject("Door_Side_Bashed");
            go.transform.SetParent(env, false);
            go.transform.SetPositionAndRotation(W(mid), Quaternion.LookRotation(W(intoHall), Vector3.up));
            float width = Vector2.Distance(brokenL, brokenR) - 0.04f;
            var leaf = LocalBox("Leaf", go.transform, new Vector3(-width / 2, 0.01f, -0.05f), new Vector3(width / 2, DoorTop - 0.02f, 0.05f), wood, 1f);
            leaf.isStatic = false;
            DoorModel(leaf);
            var d = go.AddComponent<BashedDoor>();
            d.leaf = leaf.transform;
            d.blowClip = Clip(ChainAudio + "/door_slam_boom.wav");
            d.burstClip = Clip(AmbienceAudio + "/bang_pipe_concrete.mp3");
            EditorUtility.SetDirty(d);
            return d;
        }

        /// <summary>
        /// The side door's leaf: the level's steel door (MetalDoor: rim, ribs, rivets, vent, kick plate, its scanned
        /// metal) made to the hole's size and beaten out of shape from behind (DoorBuilder.BeatenLeaf). Its mesh and
        /// materials go on the leaf itself, so the box collider and BashedDoor's throw stay as they were.
        /// </summary>
        static void DoorModel(GameObject leaf)
        {
            var box = leaf.GetComponent<BoxCollider>();
            Vector3 size = box ? box.size : new Vector3(1.565f, 2.27f, 0.1f);
            Mesh mesh = DoorBuilder.BeatenLeaf(size.x, size.y, size.z, LightingTestSceneBuilder.PrefabDir + "/SideDoor_BeatenLeaf.asset", out Material[] mats);
            leaf.GetComponent<MeshFilter>().sharedMesh = mesh;
            leaf.GetComponent<MeshRenderer>().sharedMaterials = mats;
            // behind the mouth the corridor narrows (its walls 0.65 m off the middle, the mouth's 0.8): the leaf sits
            // forward in the mouth, its back face and back handle clear of the narrow part, or the bricks show through it
            Vector3 p = leaf.transform.localPosition;
            leaf.transform.localPosition = new Vector3(p.x, p.y, size.z * 0.5f + 0.08f);
        }

        /// <summary>The steel door across the way in (the player opens it; the event slams and locks it).</summary>
        static Door EntryDoor()
        {
            float ow = DoorBuilder.OpeningWidth, oh = DoorBuilder.OpeningHeight;
            var g = Group("EntryWall", env);
            WallLine(g, V(16.68f, 0f), V(19.32f, 0f), new List<Vector3> { new Vector3(1.32f - ow / 2, 1.32f + ow / 2, oh) });
            var go = DoorBuilder.Place(new Vector3(18f - ow / 2, 0f, 0f), Vector3.right, false, env);
            go.name = "Door_Entry";
            var door = go.GetComponent<Door>();
            door.startOpen = false;
            door.locked = false;
            door.enemiesCanOpen = false;
            EditorUtility.SetDirty(door);
            return door;
        }

        // ---------------------------------------------------------------- clutter (the hall stays walkable all round)

        /// <summary>A basement kit prefab (front +Z) standing at (x, z), turned yaw degrees; null if the kit is missing.</summary>
        static GameObject Prop(Transform parent, string prefab, float x, float z, float yaw = 0f, float y = 0f, Quaternion? rotation = null)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>($"{KitPrefabs}/{prefab}.prefab");
            if (!asset) { Debug.LogWarning("Missing kit prefab " + prefab); return null; }
            var go = (GameObject)PrefabUtility.InstantiatePrefab(asset, parent);
            go.transform.SetPositionAndRotation(new Vector3(x, y, z), rotation ?? Quaternion.Euler(0f, yaw, 0f));
            if (y < 0.6f && !prefab.StartsWith("Pipe_")) occupied.Add(V(x, z));   // the scattered rubbish keeps off it
            return go;
        }

        static readonly List<Vector2> occupied = new List<Vector2>();

        static bool Inside(Vector2[] poly, Vector2 q)
        {
            bool c = false;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
                if ((poly[i].y > q.y) != (poly[j].y > q.y) && q.x < (poly[j].x - poly[i].x) * (q.y - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x) c = !c;
            return c;
        }

        static float DistToEdges(Vector2[] poly, Vector2 q)
        {
            float best = float.MaxValue;
            for (int i = 0; i < poly.Length; i++)
            {
                Vector2 a = poly[i], b = poly[(i + 1) % poly.Length], ab = b - a;
                float t = Mathf.Clamp01(Vector2.Dot(q - a, ab) / ab.sqrMagnitude);
                best = Mathf.Min(best, Vector2.Distance(q, a + ab * t));
            }
            return best;
        }

        /// <summary>
        /// Rubbish and bricks over the floors: most of it drifted against the walls, some out in the open, bricks often
        /// in little heaps. It keeps off the props, the safe zones, the jobs' standing room, the doors and every corridor
        /// mouth, and loses its colliders: nothing here may trip the player or the monster's path.
        /// </summary>
        static void Scatter()
        {
            var g = Group("Rubbish", env);
            var rng = new System.Random(1977);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            var keepOff = new List<(Vector2 at, float r)>
            {
                (V(2.9f, 20f), 1.7f), (V(4.8f, 12f), 1.9f), (V(30f, 21f), 1.8f),   // safe zones
                (V(2.6f, 20f), 1.4f), (V(30f, 8.3f), 1.6f), (V(2.4f, 5f), 1.4f),    // valve, crank, the workshop's bench
                (V(18f, 18.2f), 1.0f),                                              // the trigger's middle
            };
            foreach (var c in doorHoles) keepOff.Add((c, 1.1f));
            foreach (var m in mouths.Values) keepOff.Add((m.Mid, 1.2f));
            foreach (var o in occupied) keepOff.Add((o, 0.8f));
            var placed = new List<Vector2>();

            (string prefab, float w)[] kinds =
            {
                ("Brick_Single", 26f), ("Brick_Half", 22f), ("Trash_Newspaper", 10f), ("Trash_TinCan_A", 8f), ("Trash_TinCan_B", 7f),
                ("Trash_BottleA", 4f), ("Trash_BottleB", 7f), ("Trash_Rag", 5f), ("Trash_Cardboard", 4f), ("Trash_Debris", 3f),
            };
            float total = 0f; foreach (var k in kinds) total += k.w;
            string Pick() { float x = R(0f, total); foreach (var k in kinds) { if ((x -= k.w) <= 0f) return k.prefab; } return kinds[0].prefab; }

            bool Free(Vector2 q, Room room)
            {
                if (!Inside(room.p, q) || (room == hall && Inside(shaft.p, q))) return false;
                if (DistToEdges(room.p, q) < T / 2 + 0.22f || (room == hall && DistToEdges(shaft.p, q) < T / 2 + 0.22f)) return false;
                foreach (var (at, r) in keepOff) if (Vector2.Distance(at, q) < r) return false;
                foreach (var p in placed) if (Vector2.Distance(p, q) < 0.28f) return false;
                return true;
            }
            void Drop(string prefab, Vector2 q, float yaw)
            {
                var go = Prop(g, prefab, q.x, q.y, yaw);
                if (!go) return;
                occupied.RemoveAt(occupied.Count - 1);   // rubbish does not push other rubbish away beyond 'placed'
                go.transform.rotation = Quaternion.Euler(R(-4f, 4f), yaw, R(-4f, 4f));
                go.transform.localScale = Vector3.one * R(0.9f, 1.1f);
                foreach (var col in go.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(col);
                placed.Add(q);
            }

            foreach (var (room, count) in new[] { (hall, 34), (pump, 16), (switchRoom, 14), (workshop, 16), (crossing, 3), (vestibule, 5) })
            {
                float xMin = float.MaxValue, xMax = float.MinValue, zMin = float.MaxValue, zMax = float.MinValue;
                foreach (var v in room.p) { xMin = Mathf.Min(xMin, v.x); xMax = Mathf.Max(xMax, v.x); zMin = Mathf.Min(zMin, v.y); zMax = Mathf.Max(zMax, v.y); }
                int made = 0;
                for (int tries = 0; tries < count * 60 && made < count; tries++)
                {
                    var q = V(R(xMin, xMax), R(zMin, zMax));
                    if (!Free(q, room)) continue;
                    float wall = Mathf.Min(DistToEdges(room.p, q), room == hall ? DistToEdges(shaft.p, q) : float.MaxValue);
                    if (wall > 1.3f && rng.NextDouble() > 0.3) continue;   // most of it against the walls
                    string prefab = Pick();
                    Drop(prefab, q, R(0f, 360f));
                    made++;
                    if (prefab.StartsWith("Brick") && rng.NextDouble() < 0.45)   // a little heap round it
                        for (int k = 0, n = rng.Next(1, 4); k < n; k++)
                        {
                            float a = R(0f, Mathf.PI * 2f), d = R(0.16f, 0.38f);
                            var p = q + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * d;
                            if (Inside(room.p, p) && DistToEdges(room.p, p) > T / 2 + 0.15f && !placed.Exists(o => Vector2.Distance(o, p) < 0.14f))
                                Drop(rng.NextDouble() < 0.5 ? "Brick_Half" : "Brick_Single", p, R(0f, 360f));
                        }
                }
            }
        }

        /// <summary>The kit's props where the greybox had boxes (the same footprints: the ways round stay as they were),
        /// and small rubbish along the walls, out of the hall's ring and the corridors.</summary>
        static void Clutter()
        {
            var g = Group("Clutter", env);
            // hall
            Prop(g, "Crate", 13.85f, 7.85f, 25f);
            Prop(g, "Crate", 13.82f, 7.9f, 8f, 0.53f);
            Prop(g, "Cabinet_Metal", 22.78f, 9.2f, -90f);
            Prop(g, "Barrel", 20.9f, 16.72f, 40f);
            Prop(g, "Barrel_Open", 20.95f, 17.32f, 160f);
            Prop(g, "Radiator_Basement", 20.3f, 3.71f, 0f);
            Prop(g, "Trash_Newspaper", 15.2f, 4.6f, 30f);
            Prop(g, "Trash_BottleB", 21.6f, 7.0f, 75f);
            Prop(g, "Trash_Debris", 22.6f, 13.3f);
            Prop(g, "Trash_Rag", 15.0f, 18.9f, 20f);
            // switch room (east): the transformer in the middle (RoomDressing), the fuse box on its cabinet's east face; the crank on the south wall
            Prop(g, "Shelf_Metal", 28.0f, 16.07f, 170f);
            Prop(g, "Cabinet_Metal", 33.0f, 7.75f, -5f);
            Prop(g, "Barrel", 27.3f, 8.6f, 10f);
            Prop(g, "Barrel_Open", 27.95f, 8.45f, 200f);
            Prop(g, "Crate", 34.0f, 11.2f, 15f);
            Prop(g, "Trash_Cardboard", 33.6f, 13.4f, 35f);
            Prop(g, "Trash_TinCan_A", 31.8f, 14.0f, 40f);
            // pump room (the valve's corner, x < 4.2 round z 20, stays clear)
            Prop(g, "Shelf_Wood", 8.8f, 23.45f, 175f);
            Prop(g, "CementBags", 6.5f, 23.2f, 178f);
            Prop(g, "Pallet", 5.0f, 23.35f, 0f, 0.41f, Quaternion.Euler(0f, 175f, 0f) * Quaternion.Euler(-80f, 0f, 0f));
            Prop(g, "Barrel", 2.5f, 22.75f, 70f);
            Prop(g, "Brick_Pile", 3.2f, 17.6f, 15f);      // (the pump unit stands along the south wall)
            Prop(g, "Trash_Debris", 9.9f, 21.5f);
            // crossing: its corners only (the safe zone and three corridors take the rest)
            Prop(g, "Crate_Open", 3.5f, 13.3f, 10f);
            Prop(g, "Bucket", 3.4f, 10.6f);
            Prop(g, "Trash_BottleA", 6.2f, 10.6f);
            // workshop (south-west): the bench with the fuse against the west wall, its front to the room
            var bench = Prop(g, "Workbench", 2.05f, 5.0f, 90f);
            // its collider only up to the top (the whole bench, vise and all, hid the fuse from the player's look)
            if (bench)
                foreach (var box in bench.GetComponentsInChildren<BoxCollider>())
                {
                    Vector3 c = box.center, s = box.size;
                    float bottom = c.y - s.y * 0.5f, top = (BenchTop - 0.01f - box.transform.position.y) / Mathf.Max(box.transform.lossyScale.y, 1e-4f);
                    box.size = new Vector3(s.x, top - bottom, s.z);
                    box.center = new Vector3(c.x, (top + bottom) * 0.5f, c.z);
                }
            Prop(g, "Shelf_Metal", 6.0f, 8.8f, 178f);
            Prop(g, "Cabinet_Metal", 7.0f, 1.6f, -3f);
            Prop(g, "GasCylinder", 8.3f, 8.5f);           // (the meat hooks hang along the west wall)
            Prop(g, "Barrel", 3.0f, 1.9f, 30f);
            Prop(g, "Tyre", 6.9f, 3.6f);
            Prop(g, "Trash_Debris", 4.6f, 2.6f, 90f);
            // corridors and the way in
            Prop(g, "Trash_Rag", 26.0f, 20.7f, 60f);
            Prop(g, "Trash_Newspaper", 17.2f, -4.5f, 10f);
            Prop(g, "Brick_Single", 19.0f, -1.5f, 70f);
        }

        // ---------------------------------------------------------------- the working rooms (Blender/tools/rooms_props.py)

        /// <summary>Ground the dressing takes up (centre, radius): the rubbish keeps off it.</summary>
        static readonly List<(Vector2 at, float r)> dressed = new List<(Vector2, float)>();

        /// <summary>A point by a room's wall (on the edge from corner i to the next, t along it), off its inner face by
        /// <paramref name="off"/>, and the way into the room from there.</summary>
        static (Vector2 at, Vector2 inward) OnWall(Room r, int i, float t, float off)
        {
            Vector2 a = r.p[i], b = r.p[(i + 1) % r.p.Length];
            Vector2 n = Left(b - a).normalized, c = Vector2.zero;
            foreach (var v in r.p) c += v;
            c /= r.p.Length;
            Vector2 mid = Vector2.Lerp(a, b, t);
            if (Vector2.Dot(n, c - mid) < 0f) n = -n;
            return (mid + n * (T / 2 + off), n);
        }

        static GameObject Place(Transform g, string prefab, Vector3 at, Quaternion rot, float keepOff = 0f)
        {
            var go = Prop(g, prefab, at.x, at.z);
            if (!go) return null;
            go.transform.SetPositionAndRotation(at, rot);
            if (keepOff > 0f) { dressed.Add((V(at.x, at.z), keepOff)); occupied.Add(V(at.x, at.z)); }
            return go;
        }

        /// <summary>
        /// The rooms with the jobs in them dressed: a pump unit along the pump room's south wall; in the workshop a
        /// butcher's table with the officer's head on it, meat hooks along the west wall and blood on the floor; in the
        /// switch room a transformer where the switchgear block stood (the fuse box on its cabinet), a breaker board
        /// in the north-east corner and cable trays under the ceiling between them.
        /// </summary>
        static void RoomDressing()
        {
            dressed.Clear();
            var g = Group("RoomDressing", env);
            // pump room: its back to the south wall, between the crossing's corridor and the side door
            var (pa, pin) = OnWall(pump, 3, (9f - 7.2f) / 7.2f, 0.72f);
            Place(g, "Pump_Unit", W(pa), Quaternion.LookRotation(W(pin), Vector3.up), 1.3f);
            dressed.Add((pa + new Vector2(-0.8f, 0f), 0.9f)); dressed.Add((pa + new Vector2(0.8f, 0f), 0.9f));

            // workshop: the table along the bench, its bloody side to the room; the head stood up on it, facing the room
            var table = Place(g, "Butcher_Table", new Vector3(4.4f, 0f, 5.2f), Quaternion.Euler(0f, 90f, 0f), 1.1f);
            dressed.Add((V(4.4f, 4.5f), 0.7f)); dressed.Add((V(4.4f, 5.9f), 0.7f));
            if (table)
            {
                // the wet pool on the block's top (Blender x 0.12, y 0.04: x mirrors into Unity)
                Vector3 spot = table.transform.TransformPoint(new Vector3(-0.15f, 0.9f, -0.04f));
                // upright on the cut (hidden under it), looking out past the table's end towards the way in from the crossing
                var head = Place(g, "Severed_Head", spot, Quaternion.LookRotation(new Vector3(1f, 0f, 0.45f), Vector3.up));
                if (head) RestOn(head, 0.9f);
            }
            Place(g, "Meat_Hooks", new Vector3(2.45f, 0f, 7.4f), Quaternion.Euler(0f, 90f, 0f));
            dressed.Add((V(2.45f, 6.6f), 0.6f)); dressed.Add((V(2.45f, 8.2f), 0.6f));
            Place(g, "Blood_PoolA", new Vector3(2.55f, 0f, 7.15f), Quaternion.Euler(0f, 20f, 0f), 0.8f);
            Place(g, "Blood_PoolB", new Vector3(5.15f, 0f, 4.6f), Quaternion.Euler(0f, 70f, 0f), 0.5f);
            Vector3 drag = new Vector3(-1.15f, 0f, 0.8f).normalized;   // from the table towards the hooks
            Place(g, "Blood_Smear", new Vector3(3.95f, 0f, 6.2f), Quaternion.FromToRotation(Vector3.left, drag), 0.6f);

            // switch room: the transformer where the switchgear block stood, its cabinet's face at x 31.2 (the fuse box)
            Place(g, "Transformer", new Vector3(30f, 0f, 11.8f), Quaternion.Euler(0f, 90f, 0f), 1.6f);
            dressed.Add((V(30f, 10.8f), 1.2f)); dressed.Add((V(30f, 12.8f), 1.2f));
            // the breaker board on the north-east wall by the corner, cables from it up into a tray along the north wall,
            // a second tray from that over the transformer, the cables dropping onto its cover
            var (ba, bin) = OnWall(switchRoom, 1, 0.14f, 0f);
            Quaternion boardRot = Quaternion.LookRotation(W(bin), Vector3.up);
            Place(g, "Breaker_Panel", W(ba, 0.9f), boardRot, 0.9f);
            var dropA = Place(g, "Cable_Drop", W(ba, 2.0f), boardRot);
            if (dropA) dropA.transform.localScale = new Vector3(1f, (2.95f - 2.0f) / 2f, 1f);
            Vector2 north = (switchRoom.p[1] - switchRoom.p[0]).normalized;
            foreach (float x in new[] { 32.3f, 30.3f })
            {
                var (ta, _) = OnWall(switchRoom, 0, (x - switchRoom.p[0].x) / (switchRoom.p[1].x - switchRoom.p[0].x), 0.35f);
                Tray(g, W(ta, 2.95f), W(north));
            }
            foreach (float z in new[] { 13.45f, 15.45f }) Tray(g, new Vector3(29.75f, 2.85f, z), Vector3.forward);
            var dropB = Place(g, "Cable_Drop", new Vector3(29.75f, 1.72f, 12.45f), Quaternion.Euler(0f, 90f, 0f));
            if (dropB) dropB.transform.localScale = new Vector3(1f, (2.85f - 1.72f) / 2f, 1f);
        }

        /// <summary>A 2 m cable tray under the ceiling, hung on two rods.</summary>
        static void Tray(Transform g, Vector3 at, Vector3 along)
        {
            Place(g, "Cable_Tray_2m", at, Quaternion.FromToRotation(Vector3.right, along.normalized));
            foreach (float k in new[] { -0.7f, 0.7f }) Hanger(g, at + along.normalized * k + Vector3.up * 0.06f, 0f);
        }

        /// <summary>Drops <paramref name="go"/> straight down until its lowest point rests at <paramref name="y"/>.</summary>
        static void RestOn(GameObject go, float y)
        {
            float low = float.MaxValue;
            foreach (var f in go.GetComponentsInChildren<MeshFilter>())
                foreach (var v in f.sharedMesh.vertices) low = Mathf.Min(low, f.transform.TransformPoint(v).y);
            if (low < float.MaxValue) go.transform.position += Vector3.up * (y + 0.002f - low);
        }

        /// <summary>The fuse box arcs now and then until the fuse is in (SparkEmitter on the cable at its foot).</summary>
        static void FuseSparks(GameObject fuseIn)
        {
            if (!fuseIn || !fuseIn.transform.parent) { Debug.LogWarning("No fuse box: no sparks"); return; }
            Transform box = fuseIn.transform.parent;
            var go = new GameObject("FuseBox_Sparks");
            go.transform.SetParent(box, false);
            go.transform.localPosition = new Vector3(-0.1f, -0.04f, 0.17f);   // where the cable leaves its foot, in front of the door (Blender x 0.1 mirrored)
            var s = go.AddComponent<SparkEmitter>();
            s.sparkMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Weapons/ImpactParticle.mat");
            s.stopWhenActive = fuseIn;
            EditorUtility.SetDirty(s);
        }

        /// <summary>
        /// The working rooms dressed in the scene that is open, without building the level again: the old dressing and
        /// the switchgear block go, the props in the new pieces' way move, the rubbish under them goes.
        /// </summary>
        [MenuItem("BodycamHorror/Light Fear/Dress Working Rooms")]
        public static void DressOpenScene()
        {
            LevelSceneBuilder.LoadMaterials();
            rust = LevelSceneBuilder.rust;
            Layout();
            var envGo = GameObject.Find("Environment");
            if (!envGo) { Debug.LogError("No Environment in the open scene (open LightFear_Greybox)"); return; }
            env = envGo.transform;
            var all = env.GetComponentsInChildren<Transform>(true);
            foreach (Transform t in all)
            {
                if (!t) continue;
                if (t.name == "RoomDressing" || t.name == "Switchgear") { Object.DestroyImmediate(t.gameObject); continue; }
                Vector2 q = V(t.position.x, t.position.z);
                void Move(string name, Vector2 from, Vector2 to)
                {
                    if (t && t.name == name && Vector2.Distance(q, from) < 0.3f) t.position = new Vector3(to.x, t.position.y, to.y);
                }
                Move("Brick_Pile", V(6.6f, 17.4f), V(3.2f, 17.6f));
                Move("Trash_Debris", V(8.3f, 17.4f), V(9.9f, 21.5f));
                Move("GasCylinder", V(2.3f, 8.35f), V(8.3f, 8.5f));
            }
            foreach (var old in Object.FindObjectsByType<SparkEmitter>(FindObjectsInactive.Include)) Object.DestroyImmediate(old.gameObject);
            occupied.Clear();
            RoomDressing();
            var rubbish = env.Find("Rubbish");
            if (rubbish)
                for (int i = rubbish.childCount - 1; i >= 0; i--)
                {
                    Transform c = rubbish.GetChild(i);
                    Vector2 q = V(c.position.x, c.position.z);
                    if (dressed.Exists(d => Vector2.Distance(d.at, q) < d.r)) Object.DestroyImmediate(c.gameObject);
                }
            var fuseIn = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            GameObject fi = null;
            foreach (var t in fuseIn) if (t.name == "FuseIn") { fi = t.gameObject; break; }
            FuseSparks(fi);
            UpgradeEmergencyLights();
            var bashed = Object.FindAnyObjectByType<BashedDoor>();
            if (bashed && bashed.leaf) { DoorModel(bashed.leaf.gameObject); EditorUtility.SetDirty(bashed.leaf.gameObject); }
            EditorSceneManager.MarkSceneDirty(env.gameObject.scene);
            Debug.Log("Working rooms dressed");
        }

        // ---------------------------------------------------------------- pipes: the way, told without words

        const float MainY = 2.85f;                 // the big main, just under the ceiling
        const float PowerY = 2.6f, PowerGap = 0.12f;   // the power line: two thin pipes, one above the other

        /// <summary>
        /// The big main: in over the way in, round the hall's west side, through the wall over the side door and
        /// straight across the pump room to the valve, where it drops to the floor beside the valve's own pipe (which
        /// ends against it). The power line: from the power box by the fuse's bench, out of the workshop by its corridor,
        /// along the hall's south and east sides and through the shutter into the switch room, down to an open fuse box
        /// on the switchgear by the crank.
        /// </summary>
        static void Pipes()
        {
            var g = Group("Pipes", env);
            PipeRun(g, "Main", true, new[]
            {
                new Vector3(17.3f, MainY, -6f), new Vector3(17.3f, MainY, 6f), new Vector3(14.4f, MainY, 6f),
                new Vector3(14.4f, MainY, 16.5f), new Vector3(1.86f, MainY, 21.75f), new Vector3(1.86f, 0f, 21.75f),
            });
            Vector2[] power = { V(1.62f, 7f), V(7.6f, 7f), V(7.6f, 2.65f), V(14.6f, 4.53f), V(21.6f, 4.53f), V(21.6f, 13.6f), V(31.25f, 13.6f), V(31.25f, 12f) };
            for (int k = 0; k < 2; k++)
            {
                float y = PowerY - k * PowerGap;
                var pts = new Vector3[power.Length];
                for (int i = 0; i < power.Length; i++) pts[i] = W(power[i], y);
                PipeRun(g, "Power" + (k + 1), false, pts);
            }
            // the fuse box at the line's switch room end: on the switchgear's east face, empty (the fuse goes in here)
            fuseBoxProp = Prop(g, "Fusebox", 31.2f, 12f, 90f, 2.55f - 1.26f);
        }

        static GameObject fuseBoxProp;

        static Vector3 straightAxis, elbowLegA;   // the kit pipes' own axes, read off their meshes

        static void ReadPipeAxes()
        {
            Bounds B(string prefab)
            {
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>($"{KitPrefabs}/{prefab}.prefab");
                var go = (GameObject)PrefabUtility.InstantiatePrefab(asset);
                go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                var b = new Bounds(Vector3.zero, Vector3.zero);
                foreach (var r in go.GetComponentsInChildren<Renderer>()) b.Encapsulate(r.bounds);
                Object.DestroyImmediate(go);
                return b;
            }
            Bounds s = B("Pipe_Large_2m");
            straightAxis = s.size.x >= s.size.z ? Vector3.right : Vector3.forward;
            Bounds e = B("Pipe_Large_Elbow");   // legs along one horizontal axis and up from the corner at its origin
            elbowLegA = Mathf.Abs(e.center.x) >= Mathf.Abs(e.center.z) ? new Vector3(Mathf.Sign(e.center.x), 0f, 0f) : new Vector3(0f, 0f, Mathf.Sign(e.center.z));
        }

        /// <summary>
        /// A run of kit pipe through the points: square turns get an elbow (its legs eat into the straights either side),
        /// other turns are a bolted joint (the straights meet flange to flange). Each straight is the fewest whole
        /// lengths, stretched or squeezed a little to fit; the level ones hang from the ceiling on rods.
        /// </summary>
        static void PipeRun(Transform parent, string name, bool large, Vector3[] pts)
        {
            if (straightAxis == Vector3.zero) ReadPipeAxes();
            var g = Group("Pipe_" + name, parent);
            string straight = large ? "Pipe_Large_2m" : "Pipe_Small_2m", elbow = large ? "Pipe_Large_Elbow" : "Pipe_Small_Elbow";
            float leg = large ? 0.6f : 0.3f, radius = large ? 0.11f : 0.035f;
            int n = pts.Length;
            var elbowAt = new bool[n];
            for (int i = 1; i < n - 1; i++)
            {
                Vector3 dIn = (pts[i] - pts[i - 1]).normalized, dOut = (pts[i + 1] - pts[i]).normalized;
                if (Mathf.Abs(Vector3.Dot(dIn, dOut)) > 0.05f) continue;
                elbowAt[i] = true;
                Vector3 d1 = -dIn, d2 = dOut;   // from the corner along each leg
                Quaternion local = Quaternion.LookRotation(Vector3.Cross(elbowLegA, Vector3.up), Vector3.up);
                Quaternion want = Quaternion.LookRotation(Vector3.Cross(d1, d2), d2);
                var e = Prop(g, elbow, 0f, 0f);
                if (e) e.transform.SetPositionAndRotation(pts[i], want * Quaternion.Inverse(local));
            }
            for (int i = 0; i < n - 1; i++)
            {
                Vector3 a = pts[i], b = pts[i + 1], dir = (b - a).normalized;
                if (elbowAt[i]) a += dir * leg;
                if (elbowAt[i + 1]) b -= dir * leg;
                float len = Vector3.Distance(a, b);
                if (len < 0.2f) continue;
                int count = Mathf.Max(1, Mathf.RoundToInt(len / 2f));
                float piece = len / count;
                bool level = Mathf.Abs(dir.y) < 0.1f;
                Quaternion rot = Quaternion.LookRotation(dir, level ? Vector3.up : Vector3.forward) *
                                 (straightAxis == Vector3.right ? Quaternion.Euler(0f, -90f, 0f) : Quaternion.identity);
                for (int k = 0; k < count; k++)
                {
                    Vector3 mid = a + dir * (piece * (k + 0.5f));
                    var p = Prop(g, straight, 0f, 0f);
                    if (!p) continue;
                    p.transform.SetPositionAndRotation(mid, rot);
                    Vector3 s = Vector3.one; s[straightAxis == Vector3.right ? 0 : 2] = piece / 2f;
                    p.transform.localScale = s;
                    if (level) Hanger(g, mid, radius);
                }
            }
        }

        /// <summary>A rod from the top of a level pipe up into the ceiling.</summary>
        static void Hanger(Transform parent, Vector3 at, float radius)
        {
            float y0 = at.y + radius, len = H - y0;
            if (len < 0.05f) return;
            var rod = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            rod.name = "Hanger";
            Object.DestroyImmediate(rod.GetComponent<Collider>());
            rod.transform.SetParent(parent, false);
            rod.transform.position = new Vector3(at.x, y0 + len * 0.5f, at.z);
            rod.transform.localScale = new Vector3(0.022f, len * 0.5f, 0.022f);
            rod.GetComponent<MeshRenderer>().sharedMaterial = rust;
            rod.isStatic = true;
        }

        // ---------------------------------------------------------------- light

        /// <summary>The level's lamps (all of them die in the blackout): the hall bright enough to cross before it.</summary>
        static Transform Lamps()
        {
            var lights = Group("Lights", null);
            GameObject prefab = LightingTestSceneBuilder.BuildLampPrefab();
            Lamp(prefab, lights, "Lamp_Vestibule", V(18f, -3f), 0.5f, false, false);
            Lamp(prefab, lights, "Lamp_Hall_East", V(22.2f, 11f), 0.4f, false, false);
            Lamp(prefab, lights, "Lamp_Hall_SideDoor", V(14.9f, 16.0f), 0.6f, true, false);   // on the side door: what comes through is lit (clear of the main's joint)
            WarmWallLamp(lights);
            Lamp(prefab, lights, "Lamp_Pump", V(6f, 20.5f), 0.35f, false, true);
            Lamp(prefab, lights, "Lamp_Workshop", V(5.5f, 5f), 0.35f, false, true);
            Lamp(prefab, lights, "Lamp_Switch", V(30.5f, 15f), 0.35f, false, false);
            return lights;
        }

        const string WallLampPrefab = "Assets/Prefabs/Props/WallLamp.prefab";
        static readonly Color WarmLamp = new Color(1f, 0.72f, 0.36f);

        /// <summary>
        /// Over the way out (past it, the shotgun): no cold hanging lamp, a dim yellow bulkhead lamp on the hall's north
        /// wall beside the exit gate, facing the hall. It dies in the blackout with the rest (it is one of the Lights).
        /// </summary>
        static void WarmWallLamp(Transform lights)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(WallLampPrefab);
            if (!prefab) { Debug.LogWarning("WallLamp prefab missing (BodycamHorror > Set Up Utility Props)"); return; }
            var lamp = (GameObject)PrefabUtility.InstantiatePrefab(prefab, lights);
            lamp.name = "WallLamp_Exit";
            lamp.transform.SetPositionAndRotation(new Vector3(20.1f, 2.35f, 19.35f), Quaternion.Euler(0f, 180f, 0f));
            foreach (var l in lamp.GetComponentsInChildren<Light>(true))
            {
                l.color = WarmLamp;
                l.intensity = l.name == "Light" ? 0.9f : 0.25f;
                if (l.name == "Light") l.range = 6f;
                l.shadows = LightShadows.None;
            }
            var glass = WarmLampGlass();
            foreach (var r in lamp.GetComponentsInChildren<Renderer>(true))
                if (r.name.EndsWith("_Glass") && glass) r.sharedMaterial = glass;
        }

        /// <summary>The bulkhead lamp's glass, glowing a dim yellow (a copy of its own glow material).</summary>
        static Material WarmLampGlass()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(WallLampPrefab);
            Material src = null;
            foreach (var r in prefab.GetComponentsInChildren<Renderer>(true)) if (r.name.EndsWith("_Glass")) src = r.sharedMaterial;
            if (!src) return null;
            string path = $"{KitMaterials}/LightFear_WallLampWarm.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!mat) { mat = new Material(src); AssetDatabase.CreateAsset(mat, path); }
            else mat.CopyPropertiesFromMaterial(src);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", mat.GetColor("_BaseColor") * WarmLamp);
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", WarmLamp * 0.8f);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>The open scene's hall lamps as now built: the two cold ones near the start gone, the one by the
        /// way out swapped for the yellow wall lamp.</summary>
        [MenuItem("BodycamHorror/Light Fear/Update Hall Lamps")]
        public static void UpdateHallLamps()
        {
            Transform lights = null;
            foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (t.name == "Lights" && !t.parent) { lights = t; break; }
            if (!lights) { Debug.LogError("No Lights in the open scene (open LightFear_Greybox)"); return; }
            foreach (var n in new[] { "Lamp_Hall_South", "Lamp_Hall_West", "Lamp_Hall_Trigger", "WallLamp_Exit" })
            {
                var old = lights.Find(n);
                if (old) Object.DestroyImmediate(old.gameObject);
            }
            WarmWallLamp(lights);
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(lights.gameObject.scene);
        }

        static void Lamp(GameObject prefab, Transform parent, string name, Vector2 at, float dim, bool shadows, bool flicker)
        {
            LightingTestSceneBuilder.PlaceLamp(prefab, parent, name, W(at, H), false);
            Transform lamp = parent.Find(name);
            Light main = null;
            foreach (Light l in lamp.GetComponentsInChildren<Light>(true))
            {
                l.intensity *= dim;
                if (l.name == "Light") main = l;
                if (!shadows) l.shadows = LightShadows.None;
            }
            if (flicker && main)
            {
                var f = main.gameObject.AddComponent<LightFlicker>();
                f.kind = LightFlicker.Kind.Fluorescent;
                f.depth = 0.6f;
                f.pace = new Vector2(3f, 10f);
                foreach (Renderer r in lamp.GetComponentsInChildren<Renderer>(true)) if (r.name == "Bulb") f.glow = r;
            }
        }

        /// <summary>The safe zones: chemical sticks on the floor by the pump room's valve, at the crossing and at the back corridor's bend.</summary>
        static void SafeZones()
        {
            var g = Group("SafeZones", null);
            // a small one at the first valve: the first job is the lesson, turned away in peace. The further the player gets,
            // the more shutters are up, the more ways the monster has round them, and the harder it is to slip past
            Zone(g, "SafeZone_Valve", V(2.9f, 20f), 1.25f, 31);   // where one stands to turn the wheel (it sticks out to x 2.25)
            Zone(g, "SafeZone_Crossing", V(4.8f, 12f), 1.5f, 11);
            Zone(g, "SafeZone_Bend", V(30f, 21f), 1.4f, 23);
        }

        static void Zone(Transform parent, string name, Vector2 at, float radius, int seed)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = W(at);
            var z = go.AddComponent<SafeZone>();
            z.radius = radius;
            z.height = H;

            var rng = new System.Random(seed);
            for (int i = 0; i < 4; i++)
            {
                var stick = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                stick.name = "Chemlight";
                Object.DestroyImmediate(stick.GetComponent<Collider>());
                stick.transform.SetParent(go.transform, false);
                float a = (float)rng.NextDouble() * Mathf.PI * 2f, r = 0.2f + (float)rng.NextDouble() * 0.5f;
                stick.transform.localPosition = new Vector3(Mathf.Cos(a) * r, 0.012f, Mathf.Sin(a) * r);
                stick.transform.localRotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 90f);
                stick.transform.localScale = new Vector3(0.022f, 0.075f, 0.022f);
                var mr = stick.GetComponent<MeshRenderer>();
                mr.sharedMaterial = chemMat;
                mr.shadowCastingMode = ShadowCastingMode.Off;
            }
            var lightGo = new GameObject("Glow");
            lightGo.transform.SetParent(go.transform, false);
            lightGo.transform.localPosition = new Vector3(0f, 0.35f, 0f);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(0.35f, 1f, 0.4f);
            light.intensity = 2.2f;
            light.range = radius + 3f;
            light.shadows = LightShadows.None;
            ViewModelLighting.SetLightLayers(light, ViewModelLighting.WorldLayer | ViewModelLighting.ViewModelLayer);
        }

        /// <summary>Red emergency lamps, off until the power is on. The monster does not freeze in them.</summary>
        static GameObject EmergencyLights()
        {
            var g = Group("EmergencyLights", null);
            Vector2[] spots = { V(18f, 5f), V(13.6f, 11f), V(22.4f, 11f), V(18f, 18f), V(5f, 5f), V(5.5f, 20.5f), V(30.5f, 14f), V(18f, 22.5f), V(18f, -3f) };
            for (int i = 0; i < spots.Length; i++)
            {
                var lamp = new GameObject("Emergency_" + i).transform;
                lamp.SetParent(g, false);
                lamp.position = W(spots[i], H - 0.1f);
                if (Beacon(lamp)) continue;
                var fixture = GameObject.CreatePrimitive(PrimitiveType.Cube);
                fixture.name = "Fixture";
                Object.DestroyImmediate(fixture.GetComponent<Collider>());
                fixture.transform.SetParent(lamp, false);
                fixture.transform.localScale = new Vector3(0.3f, 0.08f, 0.14f);
                fixture.GetComponent<MeshRenderer>().sharedMaterial = redMat;
                var lightGo = new GameObject("Light");
                lightGo.transform.SetParent(lamp, false);
                lightGo.transform.localPosition = Vector3.down * 0.15f;
                var light = lightGo.AddComponent<Light>();
                light.type = LightType.Point;
                light.color = new Color(1f, 0.1f, 0.05f);
                light.intensity = 1.6f;
                light.range = 7f;
                light.shadows = LightShadows.None;
                ViewModelLighting.SetLightLayers(light, ViewModelLighting.WorldLayer | ViewModelLighting.ViewModelLayer);
                lightGo.AddComponent<MonsterBlindLight>();
            }
            g.gameObject.SetActive(false);
            return g.gameObject;
        }

        /// <summary>
        /// A rotating beacon (Beacon_Lamp, after the old Soviet ones: a grey can, a red glass dome) hung from the
        /// ceiling over <paramref name="lamp"/>'s spot, dome down: a wide dim red beam sweeps round with its reflector,
        /// its top edge kept just under level so the ceiling round the lamp stays dark (no point glow there either: the
        /// dome's own emission is all that shows of it). False when the kit has no beacon yet (the plain red block then).
        /// </summary>
        const float BeaconBeam = 10f, BeaconBeamAngle = 76f, BeaconBeamTilt = 40f;

        static bool Beacon(Transform lamp)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{KitPrefabs}/Beacon_Lamp.prefab");
            if (!prefab) return false;
            Vector3 p = lamp.position; p.y = H;
            lamp.position = p;
            var model = (GameObject)PrefabUtility.InstantiatePrefab(prefab, lamp);
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.Euler(180f, Random.Range(0f, 360f), 0f);   // from the ceiling, dome down
            Material glass = BeaconGlass(), bulb = LightingTestSceneBuilder.EmissiveMaterial("LightFear_BeaconBulb", new Color(1f, 0.25f, 0.12f), 3f);
            Transform rotor = null;
            foreach (var r in model.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (r.name.Contains("Glass")) r.sharedMaterial = glass;
                if (r.name.Contains("Bulb")) r.sharedMaterial = bulb;
                if (r.name.Contains("Mirror")) rotor = r.transform;
            }
            Vector3 centre = model.transform.TransformPoint(new Vector3(0f, 0.12f, 0f));
            Light Make(string name, Transform parent, LightType type, float intensity, float range)
            {
                var go = new GameObject(name);
                go.transform.SetParent(parent, false);
                go.transform.position = centre;
                var l = go.AddComponent<Light>();
                l.type = type;
                l.color = new Color(1f, 0.08f, 0.04f);
                l.intensity = intensity;
                l.range = range;
                l.shadows = LightShadows.None;
                ViewModelLighting.SetLightLayers(l, ViewModelLighting.WorldLayer | ViewModelLighting.ViewModelLayer);
                go.AddComponent<MonsterBlindLight>();
                return l;
            }
            var beacon = lamp.gameObject.AddComponent<RotatingBeacon>();
            beacon.rotor = rotor;
            beacon.glow = null;
            beacon.beamTilt = BeaconBeamTilt;   // half the cone and a little more: the top edge misses the ceiling
            if (rotor)
            {
                beacon.beam = Make("Beam", rotor, LightType.Spot, BeaconBeam, 13f);
                beacon.beam.spotAngle = BeaconBeamAngle;
                beacon.beam.innerSpotAngle = 28f;
            }
            EditorUtility.SetDirty(beacon);
            return true;
        }

        /// <summary>The beacon's dome: red glass, see-through, glowing a little from the bulb inside.</summary>
        static Material BeaconGlass()
        {
            const string path = KitMaterials + "/LightFear_BeaconGlass.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!mat)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend", 0f);
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = (int)RenderQueue.Transparent;
            mat.SetColor("_BaseColor", new Color(0.55f, 0.03f, 0.02f, 0.72f));
            mat.SetFloat("_Metallic", 0f);
            mat.SetFloat("_Smoothness", 0.9f);
            mat.EnableKeyword("_EMISSION");
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            mat.SetColor("_EmissionColor", new Color(1f, 0.05f, 0.02f) * 0.8f);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>The open scene's red emergency lamps swapped for beacons (the lamps keep their names and spots).</summary>
        static void UpgradeEmergencyLights()
        {
            var g = GameObject.Find("EmergencyLights");
            if (!g)
                foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                    if (t.name == "EmergencyLights" && !t.parent) { g = t.gameObject; break; }
            if (!g) { Debug.LogWarning("No EmergencyLights in the scene"); return; }
            foreach (Transform lamp in g.transform)
            {
                var old = lamp.GetComponent<RotatingBeacon>();
                if (old) Object.DestroyImmediate(old);
                for (int i = lamp.childCount - 1; i >= 0; i--) Object.DestroyImmediate(lamp.GetChild(i).gameObject);
                if (!Beacon(lamp)) Debug.LogWarning("Beacon_Lamp prefab missing (BodycamHorror > Basement > Set Up Room Props)");
            }
        }

        // ---------------------------------------------------------------- the jobs

        static (Valve, Valve, SlidingGate, UseSpot, GameObject) Mechanisms()
        {
            var g = Group("Mechanisms", null);
            // the exit gate (past the trigger)
            SlidingGate exitGate = Gate(g, "Gate_Exit", new Vector3(18f, 0f, 19.85f), new Vector3(-1.25f, 0f, -0.08f), new Vector3(1.25f, H, 0.08f), null);

            // 1: the valve on the pump room's far (west) wall: turning it, the player's back is to both ways in
            var pipe = ValveBuilder.Place(new Vector3(1.66f, 1.25f, 20f), Vector3.right, g);
            pipe.name = "Valve_Pump";
            Valve valve = pipe.GetComponentInChildren<Valve>();
            valve.turnTime = 8f;
            // every room starts shut behind grilles (doors would stop the monster: it cannot squeeze through one); the valve
            // opens them all: the pump room's back way to the crossing, the crossing to the hall, the workshop from the
            // crossing and from the hall, and the switch room from the hall (the shutter in its east wall) and from its back corridor
            UnityEventTools.AddPersistentListener(valve.closed, MouthGate(g, "Gate_PumpBack", "Corridor_PumpCrossing_A", "Где-то поднялись затворы").Open);
            UnityEventTools.AddPersistentListener(valve.closed, MouthGate(g, "Gate_CrossingHall", "Corridor_CrossingHall_B", null).Open);
            UnityEventTools.AddPersistentListener(valve.closed, MouthGate(g, "Gate_WorkshopFromCrossing", "Corridor_CrossingWorkshop_B", null).Open);
            UnityEventTools.AddPersistentListener(valve.closed, MouthGate(g, "Gate_WorkshopHall", "Corridor_WorkshopHall_B", null).Open);
            UnityEventTools.AddPersistentListener(valve.closed, Gate(g, "Gate_SwitchFront", new Vector3(23.9f, 0f, 12f),
                new Vector3(-0.08f, 0f, -1.05f), new Vector3(0.08f, H, 1.05f), null).Open);
            UnityEventTools.AddPersistentListener(valve.closed, MouthGate(g, "Gate_SwitchBack", "Corridor_SwitchBack_A", null).Open);
            EditorUtility.SetDirty(valve);

            // 2: the fuse on the workshop's bench
            var fuse = new GameObject("Fuse");
            fuse.transform.SetParent(g, false);
            fuse.transform.position = new Vector3(2.1f, BenchTop, 4.75f);   // on the workbench's top
            var body = EmergencyPowerBuilder.Fuse(fuse.transform);   // lying on its side, the stamp up
            body.name = "Body";
            body.transform.localPosition = new Vector3(0f, 0.021f, 0f);
            body.transform.localRotation = Quaternion.Euler(0f, 30f, 0f);
            var glowGo = new GameObject("Glow");
            glowGo.transform.SetParent(fuse.transform, false);
            glowGo.transform.localPosition = new Vector3(0f, 0.25f, 0f);
            var glow = glowGo.AddComponent<Light>();
            glow.type = LightType.Point;
            glow.color = new Color(1f, 0.8f, 0.45f);
            glow.intensity = 0.25f;
            glow.range = 1.2f;
            glow.shadows = LightShadows.None;
            glowGo.AddComponent<MonsterBlindLight>();
            var pickup = fuse.AddComponent<KeyPickup>();
            pickup.keyId = "fuse";
            pickup.label = "предохранитель";
            pickup.glow = glow;
            EditorUtility.SetDirty(pickup);

            // 3: the emergency power crank in the switch room's north-west corner, on the west wall between the corner
            // pier and the shelf (dead until the fuse is in); its tube runs up to the ceiling
            Valve crank = EmergencyPowerBuilder.PlaceCrank(W(V(26.375f, 15.1f), 1.1f), Vector3.right, g);
            crank.name = "Crank_EmergencyPower";
            crank.turnTime = 10f;
            EditorUtility.SetDirty(crank);

            // the fuse box on the switchgear: an empty fuse holder on its door; the player puts the fuse in by hand
            // (LightFearEvent), and then it shows in there
            UseSpot fuseSlot = null;
            GameObject fuseIn = null;
            if (fuseBoxProp)
            {
                var holderAt = new Vector3(0.11f, 0.2f, 0.167f);   // on the door's raised panel, clear of its stencil and lock
                EmergencyPowerBuilder.FuseHolder(fuseBoxProp.transform, holderAt, out fuseIn);
                fuseSlot = fuseBoxProp.AddComponent<UseSpot>();
                fuseSlot.promptOffset = holderAt + new Vector3(0f, 0f, 0.09f);
                EditorUtility.SetDirty(fuseSlot);
            }
            else Debug.LogWarning("No fuse box prop: the fuse goes in at the crank");

            fuseGates = new SlidingGate[0];   // the fuse opens nothing now: the valve opens every room
            return (valve, crank, exitGate, fuseSlot, fuseIn);
        }

        /// <summary>The workbench's top (m): its collider is cut down to it, so what lies there can be looked at and taken.</summary>
        const float BenchTop = 0.865f;

        static SlidingGate[] fuseGates;

        /// <summary>A shutter just inside a corridor's mouth, across its whole width.</summary>
        static SlidingGate MouthGate(Transform parent, string name, string mouthKey, string message)
        {
            Mouth m = mouths[mouthKey];
            float width = Vector2.Distance(m.l, m.r) + 0.1f;
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(W(m.Mid + m.inward * 0.3f), Quaternion.LookRotation(W(m.inward), Vector3.up));
            var panel = LocalBox("Panel", go.transform, new Vector3(-width / 2, 0f, -0.08f), new Vector3(width / 2, H, 0.08f), rust, 1f);
            panel.isStatic = false;
            Grille(panel, width, 0f);
            var gate = go.AddComponent<SlidingGate>();
            gate.panel = panel.transform;
            gate.travel = new Vector3(0f, 2.5f, 0f);
            gate.grindClip = Clip(AmbienceAudio + "/creak_bending_metal.mp3");
            gate.message = message;
            EditorUtility.SetDirty(gate);
            return gate;
        }

        /// <summary>Every corridor mouth and every wide opening is a doorway the monster dashes through.</summary>
        static void Doorways()
        {
            var g = Group("Doorways", null);
            void Mark(string name, Vector2 at)
            {
                var go = new GameObject("Doorway_" + name);
                go.transform.SetParent(g, false);
                go.transform.position = W(at);
                go.AddComponent<Doorway>();
            }
            foreach (var pair in mouths) Mark(pair.Key, pair.Value.Mid);
            Mark("Vestibule", V(18f, 3.48f));
            Mark("Exit", V(18f, 19.5f));
        }

        /// <summary>
        /// A shutter's look: the kit's steel grille, stretched to the opening, riding on the panel. The panel's box
        /// stays (unseen) as its collider, so the shutter stops the player, the monster and their sight exactly as
        /// before; through the bars the player sees what is waiting behind.
        /// </summary>
        static void Grille(GameObject panel, float width, float yaw)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>($"{KitPrefabs}/Gate_Grille.prefab");
            if (!asset) return;
            panel.GetComponent<MeshRenderer>().enabled = false;
            var g = (GameObject)PrefabUtility.InstantiatePrefab(asset, panel.transform);
            g.transform.localPosition = new Vector3(0f, -panel.transform.localPosition.y, 0f);   // the panel sits at its middle; the grille stands on the floor
            g.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            g.transform.localScale = new Vector3(width / 2f, H / 3.2f, 1f);
        }

        static SlidingGate Gate(Transform parent, string name, Vector3 at, Vector3 min, Vector3 max, string message)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = at;
            var panel = LocalBox("Panel", go.transform, min, max, rust, 1f);
            panel.isStatic = false;
            bool alongX = max.x - min.x >= max.z - min.z;
            Grille(panel, alongX ? max.x - min.x : max.z - min.z, alongX ? 0f : 90f);
            var gate = go.AddComponent<SlidingGate>();
            gate.panel = panel.transform;
            gate.travel = new Vector3(0f, 2.5f, 0f);
            gate.grindClip = Clip(AmbienceAudio + "/creak_bending_metal.mp3");
            gate.message = message;
            EditorUtility.SetDirty(gate);
            return gate;
        }

        // ---------------------------------------------------------------- the monster

        static Manthing PlaceManthing()
        {
            var root = new GameObject("Manthing");
            root.layer = 2;   // out of sight lines, shots and the NavMesh bake; the player still bumps into it
            // in the side door's corridor, right behind the door it is about to beat down
            Vector2 at = V(11.85f, 17.27f), face = V(13.08f, 16.8f) - V(9.955f, 18f);
            root.transform.SetPositionAndRotation(W(at), Quaternion.LookRotation(W(face), Vector3.up));
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
            AssignManthingClips(thing);
            thing.poseHeight = ManthingPoseHeight;
            thing.appearOnCall = true;
            thing.useNavMesh = true;
            thing.killDistance = 1.15f * ManthingScale;
            thing.soleHeight = 0.03f * ManthingScale;
            var steps = new List<AudioClip>();
            foreach (string guid in AssetDatabase.FindAssets("monster_barefoot t:AudioClip", new[] { ChainAudio }))
                steps.Add(AssetDatabase.LoadAssetAtPath<AudioClip>(AssetDatabase.GUIDToAssetPath(guid)));
            thing.steps = steps.ToArray();
            thing.scream = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/Monster/monster_scream.wav");
            var breaths = new List<AudioClip>();
            foreach (string guid in AssetDatabase.FindAssets("monster_breath_ t:AudioClip", new[] { ChainAudio }))
                breaths.Add(AssetDatabase.LoadAssetAtPath<AudioClip>(AssetDatabase.GUIDToAssetPath(guid)));
            thing.breaths = breaths.ToArray();
            EditorUtility.SetDirty(thing);
            return thing;
        }

        /// <summary>
        /// The clips its gait and poses are made of: the hunched run and the crawl on all fours mixed while it moves,
        /// crouched or hunched standing, the gallop when it charges, the scream. Also for patching a built scene.
        /// </summary>
        /// <summary>
        /// The shotgun on the floor of the way out (the player's own, still put away): taking it brings the monster back
        /// for the last attack (LightFearEvent). Also for patching a built scene.
        /// </summary>
        public static Weapons.WeaponPickup PlaceShotgun(BodycamPlayerController player)
        {
            var old = GameObject.Find("Pickup_Shotgun");
            if (old) Object.DestroyImmediate(old);
            var shooting = player ? player.GetComponentInChildren<Weapons.PlayerShooting>(true) : null;
            var gun = shooting ? shooting.FindWeapon<Weapons.Shotgun>() : null;
            var pickup = DarkRoomSceneBuilder.PlaceGunPickup(gun, Weapons.WeaponPickup.Kind.Shotgun, ShotgunAt, Quaternion.Euler(0f, 74f, 90f));
            if (env) pickup.transform.SetParent(env, true);
            DullShotgun(pickup);
            EditorUtility.SetDirty(pickup);
            return pickup;
        }

        /// <summary>
        /// The shotgun on the floor in darker, duller copies of its materials (the held one keeps the originals): the
        /// polished metal no longer shines out of the gloom, but under the torch and its small glow it still reads.
        /// </summary>
        static void DullShotgun(Weapons.WeaponPickup pickup)
        {
            foreach (var r in pickup.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    Material src = mats[i];
                    if (!src || src.name.EndsWith("_Floor")) continue;
                    string path = $"{KitMaterials}/{src.name}_Floor.mat";
                    var dull = AssetDatabase.LoadAssetAtPath<Material>(path);
                    if (!dull) { dull = new Material(src); AssetDatabase.CreateAsset(dull, path); }
                    else dull.CopyPropertiesFromMaterial(src);
                    dull.SetColor("_BaseColor", new Color(0.42f, 0.42f, 0.42f, 1f));
                    dull.SetFloat("_Smoothness", 0.6f);
                    EditorUtility.SetDirty(dull);
                    mats[i] = dull;
                }
                r.sharedMaterials = mats;
            }
        }

        public static void AssignManthingClips(Manthing thing)
        {
            thing.runClip = ManthingSetup.Clip("standing run");
            thing.walkClip = ManthingSetup.Clip("walk");
            thing.crawlRunClip = ManthingSetup.Clip("run");
            thing.searchClip = ManthingSetup.Clip("search");
            thing.idleClip = ManthingSetup.Clip("idle standing");
            thing.screechClip = ManthingSetup.Clip("screech");
            thing.freezeClips = new AnimationClip[0];
            // the last attack (the shotgun taken): the Sniffer's hostile run, baked onto a humanoid rig
            // (Blender/tools/sniffer_run_rig.py), screaming as it comes
            thing.rampageWalkClip = null;
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(RampageRunPath))
                if (o is AnimationClip c && c.name == "RampageRun") thing.rampageWalkClip = c;
            var screams = new List<AudioClip>();
            foreach (string s in new[] { "monster_screamer.wav", "monster_scream.wav", "distorted_screech.mp3" })
            {
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/Monster/" + s);
                if (clip) screams.Add(clip);
            }
            thing.rampageScreams = screams.ToArray();
            // the screamer's scream is the rage, once, when it snaps; then the rampage roar, looped, while it comes.
            // The kill keeps the plain scream for now (a placeholder until the screamer gets its own)
            var screamer = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/Monster/monster_screamer.wav");
            thing.killScream = null;
            thing.enrageScream = screamer;
            thing.enrageHowls = new AudioClip[0];
            // the valve's scare: the death scream made infernal (InfernalScream), made now if it is not there yet
            var sting = AssetDatabase.LoadAssetAtPath<AudioClip>(InfernalScream.OutPath);
            thing.showdownSting = sting ? sting : InfernalScream.Make();
            thing.enrageLoop = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/Monster/monster_rampage_loop.wav");
            thing.retreatWail = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/Monster/monster_rampage_wail.wav");
            thing.petrifyClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/Monster/monster_petrify.wav");
            thing.poses = Manthing.DefaultPoses(thing.runClip, thing.walkClip, thing.crawlRunClip, thing.searchClip, thing.idleClip);
            EditorUtility.SetDirty(thing);
        }

        /// <summary>Empties for what comes later: the small enemies in the workshop.</summary>
        static void Markers()
        {
            var g = Group("Markers", null);
            foreach (var (name, at) in new[] { ("SmallEnemy_1", V(3.5f, 7.8f)), ("SmallEnemy_2", V(8.3f, 4.2f)), ("SmallEnemy_3", V(5.0f, 2.0f)) })
            {
                var m = new GameObject("Marker_" + name);
                m.transform.SetParent(g, false);
                m.transform.position = W(at);
            }
        }

        static Transform Group(string name, Transform parent)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            return t;
        }
    }
}
