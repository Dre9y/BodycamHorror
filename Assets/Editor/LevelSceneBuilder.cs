using System.IO;
using System.Linq;
using BodycamHorror.Enemies;
using BodycamHorror.Environment;
using BodycamHorror.Player;
using BodycamHorror.Weapons;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace BodycamHorror.EditorTools
{
    /// <summary>
    /// Builds Assets/Scenes/Level.unity: a blockout on primitives of one place in the level, after the
    /// top-down sketch. A big L-shaped room wraps around a small one. The player comes in from a dark
    /// corridor through a door in the big room's right wall, goes round the L and through a door into the
    /// small room. There, on a desk under a window in the inner wall, lies the pistol; through the window
    /// the big room is seen, where the monster stands once the gun is taken and from where it runs out
    /// through the outer doorway (PeekScare). A grey, wet, run-down panel flat: no flashlight yet, a few
    /// dim lamps along the way, a desk lamp, rubbish on the floor, leak streaks on the walls.
    /// The game starts further back: knocked out in a store room (room 1, WakeUpIntro), from where a
    /// passage leads north into the corridor's near end. The big room has a second, padlocked door in its
    /// top wall onto a dark passage that goes up and turns right, towards what comes next.
    /// Menu: BodycamHorror > Build Level Scene. Rebuilding writes over the scene.
    ///
    /// Coordinates (m): X to the right of the sketch, Z up the sketch, origin at the big room's inner
    /// bottom-left corner.
    /// </summary>
    public static class LevelSceneBuilder
    {
        const string ScenePath = "Assets/Scenes/Level.unity";
        const string SourceScenePath = "Assets/Scenes/LightingTest.unity";

        internal const float Height = 2.7f;      // a panel block's low ceiling
        const float Wall = 0.3f;
        internal const float PanelHeight = 1.45f;   // oil paint up to here, plaster above

        // the outer shell, inside faces
        const float RoomW = 9f, RoomD = 8f;
        // the small room: top-left corner of the shell
        const float SmallX1 = 4.8f;     // its right (inner) wall's inside face
        const float SmallZ0 = 3f;       // its bottom (inner) wall's inside face
        // doors (hinge-side edge of the opening)
        const float SmallDoorX = 1.0f;  // in the small room's bottom wall
        const float OuterDoorZ = 6.6f;  // in the big room's right wall
        // the window in the small room's right wall
        const float DeskZ = 6.0f;      // the middle of the desk under the window (its length runs along Z)
        const float WindowZ0 = 5.2f, WindowZ1 = 6.9f, SillY = 0.9f, WindowTopY = 2.1f;   // wide: from anywhere along the desk the outer doorway shows through it
        // the corridor outside the outer door
        const float CorridorX1 = RoomW + Wall + 2.2f, CorridorZ0 = 3.6f, CorridorZ1 = 10f;
        // the passage south out of the corridor's near end, down to the start room: as wide as the corridor,
        // its left side the big room's right wall
        const float PassX0 = RoomW + Wall, PassX1 = CorridorX1, PassTop = 2.2f;
        // the start room (room 1), below the passage and off to its left; its door is where the passage comes in
        // it lies below the big room's bottom-left corner; a low corridor runs east from its right-hand door
        // and turns up into the passage
        const float Room1X0 = -2.8f, Room1X1 = 1.8f, Room1Z0 = -6.8f, Room1Z1 = -3f;
        const float LowZ0 = -6.05f, LowZ1 = -3.85f;   // the low corridor, inside faces (as wide as the corridor)
        const float Room1DoorZ = LowZ0 + (LowZ1 - LowZ0 - DoorBuilder.OpeningWidth) * 0.5f;
        // the locked door: straight ahead at the corridor's far end (the big room's door is the turn to the
        // left on the way), in the middle of its end wall, onto a dark passage that goes on up and turns right
        // (the way on, opened later)
        const float LockedDoorX = (RoomW + Wall + CorridorX1) * 0.5f - DoorBuilder.OpeningWidth * 0.5f;
        // behind the locked door: a small lived-in flat (a room in a communal flat), and out of its right wall a
        // door onto the long corridor ("the leg") with the head-banger at its far end
        const float FlatX0 = 7.5f, FlatX1 = 13.5f, FlatZ0 = CorridorZ1 + Wall, FlatZ1 = 14.8f;   // inside faces
        // out of the flat a zigzag: a short link right, a turn up, then the leg right to the dead end, so the
        // head-banger only comes into view (and earshot) round the second corner
        const float LinkX0 = FlatX1 + Wall, LinkZ0 = 11.6f, LinkZ1 = 13f;   // the link, inside faces
        const float ZigX0 = 15.6f, ZigX1 = 17f;                            // the stretch up, inside faces
        const float NorthLegZ0 = 16.2f, NorthZ1 = 17.6f;                   // the leg, inside faces along Z
        const float LegX0 = ZigX1;                                         // the leg proper starts past the turn
        const float FlatDoorZ = (LinkZ0 + LinkZ1) * 0.5f - DoorBuilder.OpeningWidth * 0.5f;
        // the leg runs right to a dead end, where a zombie bangs its head on the end wall under a lamp; an open
        // door just before the end, in the leg's bottom wall (on the zombie's right), leads down into the annex room
        const float NorthLegX1 = LegX0 + 24.4f;
        // the open door in the leg's bottom wall now leads onto the PvE landing, which took the place of the old annex
        // room. The landing lives in the scene as hand-edited objects (root "PvE_Landing", first laid out by
        // PveLandingGreyboxBuilder): Rebuild Shell and Annex leaves it alone, a full Build Level Scene wipes it
        const float AnnexDoorX = NorthLegX1 - 0.1f - DoorBuilder.OpeningWidth;

        /// <summary>Middle of the open door's hole in the leg's bottom wall, on the floor, on the wall's far (south) face.</summary>
        internal static Vector3 LegDoorwayFarSide => new Vector3(AnnexDoorX + DoorBuilder.OpeningWidth * 0.5f, 0f, NorthLegZ0 - Wall);
        /// <summary>The leg's bottom wall runs from x = ZigX1 to here (its outer end).</summary>
        internal const float LegBottomWallX1 = NorthLegX1 + Wall;
        const string HeadBangerPrefab = "Assets/Prefabs/Blender/Enemies/Zombie_HeadBanger.prefab";
        const string ApartmentDoorPrefab = "Assets/Prefabs/Props/ApartmentDoor.prefab";

        internal static Material wallPaint, wallPlaster, floorMat, ceilingMat;
        internal static Material wood, woodDark, woodPaint, fabric, fabricDark, paper, grime, wet, rust, enamel, cardboard, rubbish, carpet, glass;

        [MenuItem("BodycamHorror/Build Level Scene")]
        public static void Build()
        {
            LoadMaterials();

            EditorSceneManager.OpenScene(SourceScenePath, OpenSceneMode.Single);   // never copy over the scene that is open
            if (File.Exists(ScenePath))
            {
                File.Copy(SourceScenePath, ScenePath, true);
                AssetDatabase.ImportAsset(ScenePath, ImportAssetOptions.ForceUpdate);
            }
            else AssetDatabase.CopyAsset(SourceScenePath, ScenePath);
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            // keep the player and the grade; the rest of the test scene goes
            var player = Object.FindAnyObjectByType<BodycamPlayerController>();
            var volume = Object.FindAnyObjectByType<Volume>();
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (player && root == player.gameObject) continue;
                if (volume && root == volume.gameObject) continue;
                Object.DestroyImmediate(root);
            }

            Pistol pistol = null;
            if (player)
            {
                // knocked out on the start room's floor, the door in front once up (WakeUpIntro)
                var start = new Vector3(Room1X0 + 2.2f, player.transform.position.y, Room1DoorZ + DoorBuilder.OpeningWidth * 0.5f - 0.1f);
                Vector3 toDoor = new Vector3(Room1X1, start.y, Room1DoorZ + DoorBuilder.OpeningWidth * 0.5f) - start;
                player.transform.SetPositionAndRotation(start, Quaternion.LookRotation(toDoor));
                if (!player.GetComponent<WakeUpIntro>()) player.gameObject.AddComponent<WakeUpIntro>();
                var shooting = player.GetComponentInChildren<PlayerShooting>(true);
                if (shooting)
                {
                    shooting.startUnarmed = true;   // the gun is found here
                    EditorUtility.SetDirty(shooting);
                    foreach (Firearm w in shooting.weapons) if (w is Pistol p) pistol = p;
                    if (pistol) { pistol.startWithLight = false; EditorUtility.SetDirty(pistol); }   // its light is found later, in the basement
                }
                // no flashlight at this point of the game: the level's own lamps have to show the way
                var flashlight = player.GetComponentInChildren<BodycamFlashlight>(true);
                if (flashlight)
                {
                    flashlight.owned = false;
                    flashlight.startOn = false;
                    EditorUtility.SetDirty(flashlight);
                }
            }

            var level = new GameObject("Level").transform;
            BuildShell(level);
            BuildDoors(level);
            BuildWindow(level);
            FurnishSmallRoom(level);
            FurnishBigRoom(level);
            FurnishStartRoom(level);
            Rubbish(level);
            // no leak streaks: flat see-through boxes on the walls, they showed as hard-edged dark bands (a bug, not damp)
            WeaponPickup pickup = PlacePistol(pistol);
            Lights(level);
            BuildAnnex(level, LightingTestSceneBuilder.BuildLampPrefab());
            PlaceScare(pickup);

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
            Debug.Log("Level scene built: " + ScenePath);
        }

        /// <summary>
        /// Rebuilds, in the open Level scene, only what this builder owns outright and the corridors changed:
        /// the shell (walls, floors, ceilings), the litter and puddles, the leak streaks, the passage's lamps and
        /// the annex (apartment doors, the dead end with the head-banger, the open door).
        /// The PvE landing behind that door is hand-edited in the scene and is left alone.
        /// Everything else, with whatever was tuned by hand in the scene, stays as it is.
        /// Menu: BodycamHorror > Level: Rebuild Shell and Annex (keeps hand edits).
        /// </summary>
        [MenuItem("BodycamHorror/Level: Rebuild Shell and Annex (keeps hand edits)")]
        public static void RebuildShellAndAnnex()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogError("Level: leave Play Mode first, the rebuild has to be saved into the scene.");
                return;
            }
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (scene.path != ScenePath) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var levelObject = GameObject.Find("Level");
            if (!levelObject) { Debug.LogError("Level: no 'Level' object in " + ScenePath); return; }
            Transform level = levelObject.transform;
            LoadMaterials();

            foreach (string path in new[] { "Shell", "Rubbish", "Leaks", "Annex", "Lights/Lamp_LowCorner", "Lights/WallLamp_Passage" })
            {
                Transform old = level.Find(path);
                if (old) Object.DestroyImmediate(old.gameObject);
            }
            BuildShell(level);
            Rubbish(level);
            // no leak streaks: flat see-through boxes on the walls, they showed as hard-edged dark bands (a bug, not damp)
            GameObject lampPrefab = LightingTestSceneBuilder.BuildLampPrefab();
            Transform lights = level.Find("Lights") ? level.Find("Lights") : Group(level, "Lights");
            PassageLights(lights, lampPrefab);
            BuildAnnex(level, lampPrefab);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("Level: shell, corridors and annex rebuilt in " + ScenePath);
        }

        // ------------------------------------------------------------------ materials

        internal static void LoadMaterials()
        {
            const string mats = LightingTestSceneBuilder.MaterialDir, tex = LightingTestSceneBuilder.TextureDir;
            // darker and greyer than the test scenes: the place is old and dirty
            wallPaint = EnvironmentTextures.ScannedMaterial("PaintedPlaster011", mats, tex, new Color(0.62f, 0.7f, 0.68f), 1.2f);
            wallPlaster = EnvironmentTextures.ScannedMaterial("PaintedPlaster015", mats, tex, new Color(0.72f, 0.72f, 0.68f), 1.2f);
            floorMat = EnvironmentTextures.ScannedMaterial("Tiles110", mats, tex, new Color(0.6f, 0.6f, 0.58f), 1f);
            ceilingMat = EnvironmentTextures.ScannedMaterial("Plaster007", mats, tex, new Color(0.7f, 0.7f, 0.66f), 1.2f);
            LightingTestSceneBuilder.wallPaint = wallPaint;
            LightingTestSceneBuilder.wallPlaster = wallPlaster;
            MakeMaterials();
        }

        static void MakeMaterials()
        {
            Material M(string n, Color c, float metal, float smooth) => LightingTestSceneBuilder.SimpleMaterial("Level_" + n, c, metal, smooth);
            wood = M("Wood", new Color(0.2f, 0.12f, 0.07f), 0f, 0.3f);
            woodDark = M("WoodDark", new Color(0.09f, 0.06f, 0.04f), 0f, 0.25f);
            woodPaint = M("WoodPaint", new Color(0.42f, 0.45f, 0.4f), 0f, 0.3f);   // old window frames, dirty white-green
            fabric = M("Fabric", new Color(0.17f, 0.13f, 0.1f), 0f, 0.05f);
            fabricDark = M("FabricDark", new Color(0.07f, 0.07f, 0.06f), 0f, 0.05f);
            paper = M("Paper", new Color(0.55f, 0.52f, 0.44f), 0f, 0.1f);
            grime = M("Grime", new Color(0.03f, 0.028f, 0.018f, 0.62f), 0f, 0.55f);   // leak streaks: a dark wet stain over the paint
            MakeTransparent(grime);
            wet = M("Puddle", new Color(0.02f, 0.02f, 0.02f), 0f, 0.95f);           // puddles catch the flashlight
            rust = M("Rust", new Color(0.23f, 0.1f, 0.05f), 0.4f, 0.25f);
            enamel = M("Enamel", new Color(0.5f, 0.5f, 0.46f), 0f, 0.45f);
            cardboard = M("Cardboard", new Color(0.3f, 0.22f, 0.13f), 0f, 0.1f);
            rubbish = M("Rubbish", new Color(0.12f, 0.12f, 0.1f), 0f, 0.3f);
            carpet = M("Carpet", new Color(0.18f, 0.05f, 0.04f), 0f, 0.05f);
            glass = M("Glass", new Color(0.3f, 0.36f, 0.33f, 0.18f), 0f, 0.92f);
            MakeTransparent(glass);
        }

        static void MakeTransparent(Material m)
        {
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 0f);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.SetOverrideTag("RenderType", "Transparent");
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)RenderQueue.Transparent;
            EditorUtility.SetDirty(m);
        }

        // ------------------------------------------------------------------ shell

        /// <summary>One opening in a wall: from..to along the wall, bottom..top up it.</summary>
        struct Hole
        {
            public float from, to, bottom, top;
            public Hole(float from, float to, float bottom, float top) { this.from = from; this.to = to; this.bottom = bottom; this.top = top; }
        }

        static Hole DoorHole(float start) => new Hole(start, start + DoorBuilder.OpeningWidth, 0f, DoorBuilder.OpeningHeight);

        /// <summary>A wall along X from x0 to x1, thickness z0..z1, cut by holes (along X).</summary>
        static void WallX(string name, Transform parent, float x0, float x1, float z0, float z1, params Hole[] holes) =>
            WallAlong(name, parent, x0, x1, holes, (a, b, y0, y1) => Band(name, parent, new Vector3(a, y0, z0), new Vector3(b, y1, z1)));

        /// <summary>A wall along Z from z0 to z1, thickness x0..x1, cut by holes (along Z).</summary>
        static void WallZ(string name, Transform parent, float z0, float z1, float x0, float x1, params Hole[] holes) =>
            WallAlong(name, parent, z0, z1, holes, (a, b, y0, y1) => Band(name, parent, new Vector3(x0, y0, a), new Vector3(x1, y1, b)));

        static void WallAlong(string name, Transform parent, float s0, float s1, Hole[] holes, System.Action<float, float, float, float> piece)
        {
            System.Array.Sort(holes, (p, q) => p.from.CompareTo(q.from));
            float s = s0;
            foreach (Hole h in holes)
            {
                if (h.from > s) piece(s, h.from, 0f, Height);
                if (h.bottom > 0f) piece(h.from, h.to, 0f, h.bottom);
                if (h.top < Height) piece(h.from, h.to, h.top, Height);
                s = h.to;
            }
            if (s < s1) piece(s, s1, 0f, Height);
        }

        /// <summary>A piece of wall in two bands: paint up to PanelHeight, plaster above.</summary>
        static void Band(string name, Transform parent, Vector3 min, Vector3 max)
        {
            if (min.y < PanelHeight)
                LightingTestSceneBuilder.Box(name + "_Paint", parent, min, new Vector3(max.x, Mathf.Min(max.y, PanelHeight), max.z), wallPaint,
                                             LightingTestSceneBuilder.WallTile, LightingTestSceneBuilder.WallTile);
            if (max.y > PanelHeight)
                LightingTestSceneBuilder.Box(name + "_Plaster", parent, new Vector3(min.x, Mathf.Max(min.y, PanelHeight), min.z), max, wallPlaster,
                                             LightingTestSceneBuilder.WallTile, LightingTestSceneBuilder.WallTile);
        }

        static void BuildShell(Transform level)
        {
            var shell = Group(level, "Shell");
            const float tile = 2f, ceilingTile = 2.5f;
            float w = Wall;

            // floor and ceiling over the rooms and the corridor
            LightingTestSceneBuilder.Box("Floor", shell, new Vector3(-w, -w, -w), new Vector3(RoomW + w, 0f, RoomD + w), floorMat, tile, tile);
            LightingTestSceneBuilder.Box("Ceiling", shell, new Vector3(-w, Height, -w), new Vector3(RoomW + w, Height + w, RoomD + w), ceilingMat, ceilingTile, ceilingTile);
            LightingTestSceneBuilder.Box("Floor_Corridor", shell, new Vector3(RoomW + w, -w, CorridorZ0 - w), new Vector3(CorridorX1 + w, 0f, CorridorZ1 + w), floorMat, tile, tile);
            LightingTestSceneBuilder.Box("Ceiling_Corridor", shell, new Vector3(RoomW + w, Height, CorridorZ0 - w), new Vector3(CorridorX1 + w, Height + w, CorridorZ1 + w), ceilingMat, ceilingTile, ceilingTile);

            // outer walls
            WallX("Wall_Bottom", shell, -w, RoomW + w, -w, 0f);
            WallX("Wall_Top", shell, -w, RoomW + w, RoomD, RoomD + w);
            WallZ("Wall_Left", shell, 0f, RoomD, -w, 0f);
            WallZ("Wall_Right", shell, 0f, RoomD, RoomW, RoomW + w, DoorHole(OuterDoorZ));

            // the small room's inner walls: a door at the bottom, the window on the right
            WallX("Wall_SmallBottom", shell, 0f, SmallX1 + w, SmallZ0 - w, SmallZ0, DoorHole(SmallDoorX));
            WallZ("Wall_SmallRight", shell, SmallZ0, RoomD, SmallX1, SmallX1 + w, new Hole(WindowZ0, WindowZ1, SillY, WindowTopY));

            // the corridor outside: runs along Z past the outer door, both ends closed
            WallZ("Corridor_Far", shell, CorridorZ0 - w, CorridorZ1 + w, CorridorX1, CorridorX1 + w);
            WallX("Corridor_EndNear", shell, RoomW + w, CorridorX1, CorridorZ0 - w, CorridorZ0, new Hole(PassX0, PassX1, 0f, PassTop));
            // the corridor's far end is the flat's bottom wall, with the padlocked door in it (see the flat below)
            // its near side is the big room's right wall; past the room's top corner it needs a wall of its own
            WallZ("Corridor_Near", shell, RoomD + w, CorridorZ1, RoomW, RoomW + w);

            // the passage south from the corridor's near end, down to the corner with the low corridor
            float passZ0 = LowZ1 + w, passZ1 = CorridorZ0 - w;
            LightingTestSceneBuilder.Box("Floor_Passage", shell, new Vector3(PassX0 - w, -w, passZ0), new Vector3(PassX1 + w, 0f, passZ1), floorMat, tile, tile);
            LightingTestSceneBuilder.Box("Ceiling_Passage", shell, new Vector3(PassX0 - w, Height, passZ0), new Vector3(PassX1 + w, Height + w, passZ1), ceilingMat, ceilingTile, ceilingTile);
            // its left side: own wall below the big room, the big room's right wall from there up
            WallZ("Passage_Left", shell, LowZ1, -w, PassX0 - w, PassX0);
            WallZ("Passage_Right", shell, LowZ0, passZ1, PassX1, PassX1 + w);

            // the low corridor: east from the start room's door, under the big room, to the passage's foot
            float lowX0 = Room1X1 + w;
            LightingTestSceneBuilder.Box("Floor_Low", shell, new Vector3(lowX0, -w, LowZ0 - w), new Vector3(PassX1 + w, 0f, LowZ1 + w), floorMat, tile, tile);
            LightingTestSceneBuilder.Box("Ceiling_Low", shell, new Vector3(lowX0, Height, LowZ0 - w), new Vector3(PassX1 + w, Height + w, LowZ1 + w), ceilingMat, ceilingTile, ceilingTile);
            WallX("Low_Top", shell, lowX0, PassX0 - w, LowZ1, LowZ1 + w);
            WallX("Low_Bottom", shell, lowX0, PassX1 + w, LowZ0 - w, LowZ0);

            // the start room, its door in the right wall
            LightingTestSceneBuilder.Box("Floor_Room1", shell, new Vector3(Room1X0 - w, -w, Room1Z0 - w), new Vector3(Room1X1 + w, 0f, Room1Z1 + w), floorMat, tile, tile);
            LightingTestSceneBuilder.Box("Ceiling_Room1", shell, new Vector3(Room1X0 - w, Height, Room1Z0 - w), new Vector3(Room1X1 + w, Height + w, Room1Z1 + w), ceilingMat, ceilingTile, ceilingTile);
            WallX("Room1_Top", shell, Room1X0 - w, Room1X1 + w, Room1Z1, Room1Z1 + w);
            WallX("Room1_Bottom", shell, Room1X0 - w, Room1X1 + w, Room1Z0 - w, Room1Z0);
            WallZ("Room1_Left", shell, Room1Z0, Room1Z1, Room1X0 - w, Room1X0);
            WallZ("Room1_Right", shell, Room1Z0, Room1Z1, Room1X1, Room1X1 + w, DoorHole(Room1DoorZ));

            // behind the locked door: the flat, its bottom wall shared with the corridor's far end
            LightingTestSceneBuilder.Box("Floor_Flat", shell, new Vector3(FlatX0 - w, -w, CorridorZ1), new Vector3(FlatX1 + w, 0f, FlatZ1 + w), floorMat, tile, tile);
            LightingTestSceneBuilder.Box("Ceiling_Flat", shell, new Vector3(FlatX0 - w, Height, CorridorZ1), new Vector3(FlatX1 + w, Height + w, FlatZ1 + w), ceilingMat, ceilingTile, ceilingTile);
            WallX("Flat_Bottom", shell, FlatX0 - w, FlatX1 + w, CorridorZ1, FlatZ0, DoorHole(LockedDoorX));
            WallX("Flat_Top", shell, FlatX0 - w, FlatX1 + w, FlatZ1, FlatZ1 + w);
            WallZ("Flat_Left", shell, FlatZ0, FlatZ1, FlatX0 - w, FlatX0);
            WallZ("Flat_Right", shell, FlatZ0, FlatZ1, FlatX1, FlatX1 + w, DoorHole(FlatDoorZ));   // the door onto the leg

            // the zigzag: the link right out of the flat, the stretch up, then the leg right to the dead end
            LightingTestSceneBuilder.Box("Floor_Link", shell, new Vector3(LinkX0, -w, LinkZ0 - w), new Vector3(ZigX1 + w, 0f, LinkZ1), floorMat, tile, tile);
            LightingTestSceneBuilder.Box("Ceiling_Link", shell, new Vector3(LinkX0, Height, LinkZ0 - w), new Vector3(ZigX1 + w, Height + w, LinkZ1), ceilingMat, ceilingTile, ceilingTile);
            LightingTestSceneBuilder.Box("Floor_Zig", shell, new Vector3(ZigX0 - w, -w, LinkZ1), new Vector3(ZigX1 + w, 0f, NorthLegZ0 - w), floorMat, tile, tile);
            LightingTestSceneBuilder.Box("Ceiling_Zig", shell, new Vector3(ZigX0 - w, Height, LinkZ1), new Vector3(ZigX1 + w, Height + w, NorthLegZ0 - w), ceilingMat, ceilingTile, ceilingTile);
            WallX("Link_Bottom", shell, LinkX0, ZigX1 + w, LinkZ0 - w, LinkZ0);
            WallX("Link_Top", shell, LinkX0, ZigX0 - w, LinkZ1, LinkZ1 + w);
            WallZ("Zig_Left", shell, LinkZ1, NorthZ1 + w, ZigX0 - w, ZigX0);           // up the stretch and closing the leg's left end
            WallZ("Zig_Right", shell, LinkZ0, NorthLegZ0 - w, ZigX1, ZigX1 + w);
            LightingTestSceneBuilder.Box("Floor_NorthLeg", shell, new Vector3(ZigX0 - w, -w, NorthLegZ0 - w), new Vector3(NorthLegX1 + w, 0f, NorthZ1 + w), floorMat, tile, tile);
            LightingTestSceneBuilder.Box("Ceiling_NorthLeg", shell, new Vector3(ZigX0 - w, Height, NorthLegZ0 - w), new Vector3(NorthLegX1 + w, Height + w, NorthZ1 + w), ceilingMat, ceilingTile, ceilingTile);
            WallX("North_Top", shell, ZigX0 - w, NorthLegX1 + w, NorthZ1, NorthZ1 + w);
            // the leg's bottom wall, with the open door in it near the end: through it, the PvE landing
            WallX("North_LegBottom", shell, ZigX1, NorthLegX1 + w, NorthLegZ0 - w, NorthLegZ0, DoorHole(AnnexDoorX));
            WallZ("North_LegEnd", shell, NorthLegZ0, NorthZ1, NorthLegX1, NorthLegX1 + w);   // the wall the zombie bangs its head on
        }

        static void BuildDoors(Transform level)
        {
            var doors = Group(level, "Doors");
            // small room: in the middle of its bottom wall, front facing into the small room
            DoorBuilder.Place(new Vector3(SmallDoorX, 0f, SmallZ0 - Wall * 0.5f), Vector3.right, false, doors).name = "Door_SmallRoom";
            // outer: in the big room's right wall, front facing into the big room
            DoorBuilder.Place(new Vector3(RoomW + Wall * 0.5f, 0f, OuterDoorZ), Vector3.forward, false, doors).name = "Door_Outer";
            // start room: in its right wall onto the low corridor, front facing into the room
            DoorBuilder.Place(new Vector3(Room1X1 + Wall * 0.5f, 0f, Room1DoorZ), Vector3.forward, false, doors).name = "Door_StartRoom";
            // straight ahead at the corridor's far end: padlocked, the padlock facing down the corridor
            DoorBuilder.Place(new Vector3(LockedDoorX + DoorBuilder.OpeningWidth, 0f, CorridorZ1 + Wall * 0.5f), Vector3.left, true, doors).name = "Door_Locked";
        }

        static void BuildWindow(Transform level)
        {
            var window = Group(level, "Window");
            float x0 = SmallX1, x1 = SmallX1 + Wall, f = 0.06f;
            float zm = (WindowZ0 + WindowZ1) * 0.5f;
            // frame round the hole, a mullion and a transom: an old painted wooden window
            Prop(window, "Frame_Bottom", new Vector3(x0, SillY, WindowZ0), new Vector3(x1, SillY + f, WindowZ1), woodPaint);
            Prop(window, "Frame_Top", new Vector3(x0, WindowTopY - f, WindowZ0), new Vector3(x1, WindowTopY, WindowZ1), woodPaint);
            Prop(window, "Frame_Left", new Vector3(x0, SillY, WindowZ0), new Vector3(x1, WindowTopY, WindowZ0 + f), woodPaint);
            Prop(window, "Frame_Right", new Vector3(x0, SillY, WindowZ1 - f), new Vector3(x1, WindowTopY, WindowZ1), woodPaint);
            Prop(window, "Mullion", new Vector3(x0 + 0.1f, SillY, zm - 0.03f), new Vector3(x1 - 0.1f, WindowTopY, zm + 0.03f), woodPaint, 0f, false);   // no colliders on the thin bars: the glass stops you, the eye goes past them
            Prop(window, "Transom", new Vector3(x0 + 0.1f, 1.72f, WindowZ0), new Vector3(x1 - 0.1f, 1.77f, WindowZ1), woodPaint, 0f, false);
            // sills, both sides
            Prop(window, "Sill_In", new Vector3(x0 - 0.18f, SillY - 0.03f, WindowZ0 - 0.06f), new Vector3(x0, SillY, WindowZ1 + 0.06f), woodPaint);
            Prop(window, "Sill_Out", new Vector3(x1, SillY - 0.03f, WindowZ0 - 0.06f), new Vector3(x1 + 0.12f, SillY, WindowZ1 + 0.06f), woodPaint);
            // the glass: solid, so nobody climbs through
            var g = Prop(window, "Glass", new Vector3(x0 + 0.145f, SillY, WindowZ0), new Vector3(x0 + 0.155f, WindowTopY, WindowZ1), glass);
            g.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            g.isStatic = false;
            // dirt on the glass, low down in one pane
            Prop(window, "GlassDirt", new Vector3(x0 + 0.14f, SillY + 0.05f, WindowZ0 + 0.1f), new Vector3(x0 + 0.142f, SillY + 0.3f, zm - 0.1f), grime, collider: false);
        }

        // ------------------------------------------------------------------ props

        static void FurnishSmallRoom(Transform level)
        {
            var room = Group(level, "SmallRoom");

            // the desk along the window wall, under the window: the Blender model (1.24 x 0.69 m, front on its +Z),
            // turned so its front faces into the room, back against the wall; the pedestal ends up at the far (+Z) end
            var desk = Group(room, "Desk");
            float dx0 = SmallX1 - 0.71f, top = 0.76f;
            PlaceModel(DeskPrefab, desk, "Desk_Model", new Vector3(SmallX1 - 0.365f, 0f, DeskZ), -90f);
            // on it: the pistol in the middle of the lamp's pool (PlacePistol), the mug and the clipboard either
            // side of it in the light, a loose sheet and a book out in the dark; the lamp is built with the lights.
            // The small things on it have no colliders: nothing may block the pistol's pickup ray
            Bare(PlaceModel(ClipboardPrefab, desk, "Clipboard_Model", new Vector3(dx0 + 0.25f, top, DeskZ - 0.24f), -80f));   // clip to the window
            Bare(PlaceModel(MugPrefab, desk, "Mug_Model", new Vector3(dx0 + 0.17f, top, DeskZ + 0.16f), -110f));   // the roses to the room
            Prop(desk, "Paper_Loose", new Vector3(dx0 + 0.03f, top, DeskZ + 0.28f), new Vector3(dx0 + 0.24f, top + 0.003f, DeskZ + 0.58f), paper, -22f, false);
            Prop(desk, "Book", new Vector3(dx0 + 0.42f, top, DeskZ - 0.58f), new Vector3(dx0 + 0.6f, top + 0.04f, DeskZ - 0.33f), woodDark, -6f, false);

            // the chair pushed back from the desk at the knee space, a little askew
            Chair(room, "Chair_Desk", new Vector3(dx0 - 0.5f, 0f, DeskZ - 0.35f), 250f, false);

            // the sketch's grey things: a bookcase top left, a bin bottom left, a chair bottom right, rubbish by the desk
            var shelf = Group(room, "Bookcase");
            float bx0 = 0.05f, bx1 = 1.3f, bz0 = RoomD - 0.4f, bz1 = RoomD - 0.02f, bh = 2.0f;
            Prop(shelf, "Back", new Vector3(bx0, 0f, bz1 - 0.02f), new Vector3(bx1, bh, bz1), woodDark);
            Prop(shelf, "Side_L", new Vector3(bx0, 0f, bz0), new Vector3(bx0 + 0.03f, bh, bz1), wood);
            Prop(shelf, "Side_R", new Vector3(bx1 - 0.03f, 0f, bz0), new Vector3(bx1, bh, bz1), wood);
            var rng = new System.Random(7);
            for (int i = 0; i < 5; i++)
            {
                float y = 0.05f + i * 0.45f;
                Prop(shelf, "Shelf" + i, new Vector3(bx0 + 0.03f, y, bz0), new Vector3(bx1 - 0.03f, y + 0.025f, bz1 - 0.02f), wood);
                if (i == 4) break;
                float x = bx0 + 0.06f;
                while (x < bx1 - 0.15f)   // books, some gaps, a few fallen over
                {
                    float bw = 0.03f + (float)rng.NextDouble() * 0.05f, bhh = 0.2f + (float)rng.NextDouble() * 0.15f;
                    if (rng.NextDouble() < 0.25) { x += 0.1f; continue; }
                    Prop(shelf, "Book", new Vector3(x, y + 0.025f, bz0 + 0.05f), new Vector3(x + bw, y + 0.025f + bhh, bz0 + 0.3f),
                         rng.NextDouble() < 0.5 ? woodDark : fabric, collider: false);
                    x += bw + 0.005f;
                }
            }
            Prop(room, "Book_Floor", new Vector3(0.6f, 0f, 7.0f), new Vector3(0.8f, 0.04f, 7.27f), fabric, 30f, false);
            Prop(room, "Book_Floor2", new Vector3(1.0f, 0f, 7.2f), new Vector3(1.18f, 0.035f, 7.44f), woodDark, -50f, false);

            Cylinder(room, "Bin", new Vector3(0.4f, 0.22f, SmallZ0 + 0.4f), 0.18f, 0.44f, rust);
            Chair(room, "Chair_Fallen", new Vector3(SmallX1 - 0.75f, 0f, SmallZ0 + 0.75f), -30f, true);
            RubbishPile(room, "Pile_DeskCorner", new Vector3(SmallX1 - 0.45f, 0f, RoomD - 0.4f), 0.45f, 9, 3);

            // a wardrobe against the left wall, a door hanging open
            var wardrobe = Group(room, "Wardrobe");
            Prop(wardrobe, "Body", new Vector3(0.02f, 0f, 4.6f), new Vector3(0.62f, 2.05f, 5.8f), woodDark);
            var leaf = Prop(wardrobe, "Door_Open", new Vector3(0.62f, 0.05f, 5.78f), new Vector3(0.64f, 2.0f, 6.36f), wood, 0f, false);
            leaf.transform.RotateAround(new Vector3(0.63f, 0f, 5.79f), Vector3.up, -35f);

            // a radiator under the outer wall, its paint gone to rust
            Radiator(room, "Radiator", new Vector3(2.2f, 0.15f, RoomD - 0.12f), 9);
            // an old mattress on the floor in the corner by the door
            Prop(room, "Mattress", new Vector3(2.4f, 0f, SmallZ0 + 0.1f), new Vector3(3.3f, 0.14f, SmallZ0 + 1.95f), fabric, 4f);
            Prop(room, "Mattress_Stain", new Vector3(2.6f, 0.141f, SmallZ0 + 0.6f), new Vector3(3.0f, 0.143f, SmallZ0 + 1.1f), grime, 4f, false);
        }

        static void FurnishBigRoom(Transform level)
        {
            var room = Group(level, "BigRoom");

            // the bottom strip: a sofa on the left under a wall shelf, an overturned table, a fridge in the far corner
            var sofa = Group(room, "Sofa");
            PlaceModel(SofaPrefab, sofa, "Sofa_Model", new Vector3(0.47f, 0f, 1.3f), 90f);            // back to the left wall
            PlaceModel(WallShelfPrefab, sofa, "WallShelf_Model", new Vector3(0f, 1.45f, 1.3f), 90f);   // on the wall over it
            Prop(sofa, "Cushion_Fallen", new Vector3(1.0f, 0f, 1.0f), new Vector3(1.55f, 0.12f, 1.55f), fabricDark, 25f);

            // on its side, legs to the bottom wall, pushed up against it so it leaves the way past the sofa clear
            var table = Group(room, "Table_Overturned");
            const float tableZ = -0.8f;
            Prop(table, "Top", new Vector3(2.6f, 0f, 1.6f + tableZ), new Vector3(3.8f, 0.75f, 1.64f + tableZ), wood, 8f);
            for (int i = 0; i < 2; i++)
            {
                float x = 2.7f + i * 0.95f;
                Prop(table, "Leg" + i, new Vector3(x, 0.05f, 0.95f + tableZ), new Vector3(x + 0.05f, 0.1f, 1.6f + tableZ), wood, 8f);
                Prop(table, "LegT" + i, new Vector3(x, 0.62f, 0.95f + tableZ), new Vector3(x + 0.05f, 0.67f, 1.6f + tableZ), wood, 8f);
            }

            var fridge = Group(room, "Fridge");   // in the corner, its door to the room
            PlaceModel(FridgePrefab, fridge, "Fridge_Model", new Vector3(RoomW - 0.37f, 0f, 0.37f), -90f);

            // cardboard boxes, a heap of rubbish bags
            var boxes = Group(room, "Boxes");
            PlaceModel(BoxClosedPrefab, boxes, "Box_Closed", new Vector3(5.9f, 0f, 0.35f), 6f);
            PlaceModel(BoxOpenPrefab, boxes, "Box_Open", new Vector3(6.55f, 0f, 0.45f), -4f);       // flaps out, clear of the wall
            PlaceModel(BoxSmallPrefab, boxes, "Box_Small", new Vector3(5.98f, 0.45f, 0.35f), 18f);  // on the closed one
            PlaceModel(BoxCrushedPrefab, boxes, "Box_Crushed", new Vector3(6.85f, 0f, 1.1f), 40f);
            Bags(room, new Vector3(4.4f, 0f, 0.45f), 6);

            // the right part, seen through the window: a mattress, a wardrobe, a carpet, a chair
            Prop(room, "Carpet", new Vector3(6.0f, 0f, 3.6f), new Vector3(8.4f, 0.01f, 6.4f), carpet, -3f, false);
            Prop(room, "Mattress", new Vector3(7.5f, 0f, 3.3f), new Vector3(8.9f, 0.16f, 5.2f), fabricDark, 2f);
            Prop(room, "Mattress_Stain", new Vector3(7.8f, 0.161f, 3.8f), new Vector3(8.5f, 0.163f, 4.6f), grime, 2f, false);
            var wardrobe = Group(room, "Wardrobe");
            Prop(wardrobe, "Body", new Vector3(SmallX1 + Wall + 0.02f, 0f, 3.1f), new Vector3(SmallX1 + Wall + 0.62f, 2.0f, 4.4f), woodDark);
            Chair(room, "Chair", new Vector3(6.6f, 0f, 7.0f), 160f, false);
            Radiator(room, "Radiator", new Vector3(6.4f, 0.15f, RoomD - 0.12f), 8);

            // pipes: a riser in the corner, a run under the ceiling along the bottom wall
            var pipes = Group(room, "Pipes");
            Cylinder(pipes, "Riser", new Vector3(RoomW - 0.12f, Height * 0.5f, 0.9f), 0.05f, Height, rust);
            var run = Cylinder(pipes, "Run", new Vector3(RoomW * 0.5f, Height - 0.2f, 0.1f), 0.04f, RoomW, rust);
            run.transform.rotation = Quaternion.Euler(0f, 0f, 90f);
            var run2 = Cylinder(pipes, "Run_Right", new Vector3(RoomW - 0.1f, Height - 0.2f, RoomD * 0.5f), 0.04f, RoomD, rust);
            run2.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            // a slab of ceiling plaster come down
            Prop(room, "CeilingChunk", new Vector3(3.9f, 0f, 1.9f), new Vector3(4.6f, 0.05f, 2.4f), wallPlaster, 22f);
            Prop(room, "CeilingHole", new Vector3(3.9f, Height - 0.01f, 1.8f), new Vector3(4.7f, Height, 2.5f), grime, 15f, false);

            // the corridor outside: bare (the corridors are kept clear of litter), only a pipe riser
            var corridor = Group(level, "Corridor");
            Cylinder(corridor, "Riser", new Vector3(CorridorX1 - 0.12f, Height * 0.5f, 6.0f), 0.05f, Height, rust);
            // an electrical panel, its door gone, on the far wall across from the big room's door
            PlaceModel(ElectricPanelPrefab, corridor, "ElectricPanel", new Vector3(CorridorX1, 1.45f, 5.0f), -90f);
        }

        /// <summary>The start room: a bare store room where the player was dumped. A dark stain where the head lay,
        /// a chair knocked over, a shelf rack stripped bare, crates, a bucket; nothing to fight with.</summary>
        static void FurnishStartRoom(Transform level)
        {
            var room = Group(level, "StartRoom");

            // where the head hit the floor: a dark smear, a few spots
            // (the player lies just left of the middle, head to the left wall, feet to the door)
            float hx = Room1X0 + 1.6f, hz = Room1DoorZ + DoorBuilder.OpeningWidth * 0.5f - 0.1f;
            Prop(room, "Stain_Head", new Vector3(hx - 0.3f, 0.002f, hz - 0.3f), new Vector3(hx + 0.3f, 0.004f, hz + 0.25f), grime, 18f, false);
            Prop(room, "Stain_Drop0", new Vector3(hx + 1.3f, 0.002f, hz + 0.35f), new Vector3(hx + 1.4f, 0.004f, hz + 0.45f), grime, 30f, false);
            Prop(room, "Stain_Drop1", new Vector3(hx + 1.75f, 0.002f, hz - 0.15f), new Vector3(hx + 1.82f, 0.004f, hz - 0.08f), grime, 10f, false);

            // a metal rack along the left wall, stripped bare
            var rack = Group(room, "Rack");
            float rx0 = Room1X0 + 0.05f, rx1 = Room1X0 + 0.5f, rz0 = -6.4f, rz1 = -4.0f;
            for (int i = 0; i < 4; i++)
            {
                float x = i % 2 == 0 ? rx0 : rx1 - 0.04f, z = i < 2 ? rz0 : rz1 - 0.04f;
                Prop(rack, "Post" + i, new Vector3(x, 0f, z), new Vector3(x + 0.04f, 1.9f, z + 0.04f), rust);
            }
            for (int i = 0; i < 4; i++)
            {
                float y = 0.15f + i * 0.55f;
                Prop(rack, "Shelf" + i, new Vector3(rx0, y, rz0), new Vector3(rx1, y + 0.03f, rz1), rust);
            }
            Prop(rack, "Box_Empty", new Vector3(rx0 + 0.05f, 0.73f, -5.9f), new Vector3(rx1 - 0.05f, 1.0f, -5.4f), cardboard, 5f, false);

            // crates stacked in the bottom right corner (below the door's swing), one fallen
            var crates = Group(room, "Crates");
            Prop(crates, "Crate0", new Vector3(Room1X1 - 0.75f, 0f, Room1Z0 + 0.05f), new Vector3(Room1X1 - 0.05f, 0.6f, Room1Z0 + 0.7f), wood, 3f);
            Prop(crates, "Crate1", new Vector3(Room1X1 - 0.7f, 0.6f, Room1Z0 + 0.1f), new Vector3(Room1X1 - 0.1f, 1.1f, Room1Z0 + 0.6f), woodDark, -8f);
            Prop(crates, "Crate2", new Vector3(Room1X1 - 1.6f, 0f, Room1Z0 + 0.1f), new Vector3(Room1X1 - 0.95f, 0.55f, Room1Z0 + 0.7f), wood, 20f);
            Prop(crates, "Crate_Fallen", new Vector3(Room1X1 - 2.3f, 0f, Room1Z0 + 0.2f), new Vector3(Room1X1 - 1.75f, 0.45f, Room1Z0 + 0.75f), woodDark, 35f);

            // a chair knocked over by the top wall, a bucket in the top right corner
            Chair(room, "Chair_Fallen", new Vector3(Room1X0 + 1.2f, 0f, Room1Z1 - 0.7f), 140f, true);
            Cylinder(room, "Bucket", new Vector3(Room1X1 - 0.2f, 0.16f, Room1Z1 - 0.3f), 0.14f, 0.32f, rust);

            // pipes: a riser in the top left corner, a run along the top wall under the ceiling
            Cylinder(room, "Riser", new Vector3(Room1X0 + 0.12f, Height * 0.5f, Room1Z1 - 0.12f), 0.05f, Height, rust);
            var run = Cylinder(room, "Run", new Vector3((Room1X0 + Room1X1) * 0.5f, Height - 0.18f, Room1Z1 - 0.1f), 0.04f, Room1X1 - Room1X0, rust);
            run.transform.rotation = Quaternion.Euler(0f, 0f, 90f);
        }

        /// <summary>Litter over both floors: papers, chunks of plaster, bits; sparse in the small room.</summary>
        static void Rubbish(Transform level)
        {
            var root = Group(level, "Rubbish");
            var rng = new System.Random(1337);
            // areas to scatter over: x0, z0, x1, z1, count
            float[][] areas =
            {
                new[] { 0.3f, SmallZ0 + 0.3f, SmallX1 - 0.3f, RoomD - 0.5f, 38f },   // small room
                new[] { 0.3f, 0.3f, RoomW - 0.3f, SmallZ0 - 0.6f, 55f },               // bottom strip
                new[] { SmallX1 + Wall + 0.3f, SmallZ0, RoomW - 0.3f, RoomD - 0.3f, 35f }, // right part
                new[] { Room1X0 + 0.6f, Room1Z0 + 0.3f, Room1X1 - 0.3f, Room1Z1 - 0.3f, 26f },      // start room
                // the corridors: no litter
            };
            int n = 0;
            foreach (float[] a in areas)
                for (int i = 0; i < (int)a[4]; i++)
                {
                    float x = Mathf.Lerp(a[0], a[2], (float)rng.NextDouble());
                    float z = Mathf.Lerp(a[1], a[3], (float)rng.NextDouble());
                    float yaw = (float)rng.NextDouble() * 360f;
                    double kind = rng.NextDouble();
                    // the small room (the pistol's) keeps a third of its litter: the desk must stand out, not drown in it.
                    // The numbers are still drawn for the rest, so every other area stays as it was
                    if (a == areas[0] && i % 3 != 0)
                    {
                        int draws = kind < 0.4 ? 0 : kind < 0.7 ? 3 : kind < 0.88 ? 2 : 1;   // what that item's branch would draw
                        for (int d = 0; d < draws; d++) rng.NextDouble();
                        n++;
                        continue;
                    }
                    if (kind < 0.4)   // a sheet of paper
                        Prop(root, "Paper" + n++, new Vector3(x - 0.1f, 0f, z - 0.14f), new Vector3(x + 0.1f, 0.003f, z + 0.14f), paper, yaw, false);
                    else if (kind < 0.7)   // a lump of plaster
                    {
                        float s = 0.04f + (float)rng.NextDouble() * 0.1f;
                        Prop(root, "Plaster" + n++, new Vector3(x - s, 0f, z - s * 0.7f), new Vector3(x + s, s * 0.6f, z + s * 0.7f), wallPlaster, yaw, false)
                            .transform.Rotate((float)rng.NextDouble() * 20f, 0f, (float)rng.NextDouble() * 20f);
                    }
                    else if (kind < 0.88)   // dark junk: rags, wrappers, broken things
                    {
                        float s = 0.05f + (float)rng.NextDouble() * 0.12f;
                        Prop(root, "Junk" + n++, new Vector3(x - s, 0f, z - s * 0.5f), new Vector3(x + s, 0.03f + (float)rng.NextDouble() * 0.05f, z + s * 0.5f), rubbish, yaw, false);
                    }
                    else   // a bottle or a can on its side
                    {
                        var c = Cylinder(root, "Bottle" + n++, new Vector3(x, 0.035f, z), 0.035f, 0.24f, rng.NextDouble() < 0.5 ? glass : rust, false);
                        c.transform.rotation = Quaternion.Euler(90f, yaw, 0f);
                    }
                }
            // no puddles: they are gone from the floors (their gloss read as a bug, not as water)
        }

        /// <summary>Dark streaks running down the walls from the ceiling, where water has got in for years.</summary>
        static void Leaks(Transform level)
        {
            var root = Group(level, "Leaks");
            var rng = new System.Random(99);
            const float off = 0.004f;
            // wall face, as a line along the wall: start, end, and which side the room is
            void Streaks(string name, Vector3 a, Vector3 b, Vector3 into, int count)
            {
                for (int i = 0; i < count; i++)
                {
                    Vector3 p = Vector3.Lerp(a, b, (float)rng.NextDouble());
                    float w = 0.15f + (float)rng.NextDouble() * 0.45f;
                    float len = 0.4f + (float)rng.NextDouble() * (Height - 0.4f);
                    Vector3 along = (b - a).normalized;
                    Vector3 c0 = p - along * w * 0.5f + into * off, c1 = p + along * w * 0.5f + into * (off + 0.002f);
                    Prop(root, name + i, new Vector3(Mathf.Min(c0.x, c1.x), Height - len, Mathf.Min(c0.z, c1.z)),
                         new Vector3(Mathf.Max(c0.x, c1.x), Height, Mathf.Max(c0.z, c1.z)), grime, 0f, false);
                    // a thinner run further down from some of them
                    if (rng.NextDouble() < 0.3)
                    {
                        float tw = w * 0.35f, tl = (float)rng.NextDouble() * (Height - len);
                        Vector3 t0 = p - along * tw * 0.5f + into * off, t1 = p + along * tw * 0.5f + into * (off + 0.002f);
                        Prop(root, name + i + "_Run", new Vector3(Mathf.Min(t0.x, t1.x), Height - len - tl, Mathf.Min(t0.z, t1.z)),
                             new Vector3(Mathf.Max(t0.x, t1.x), Height - len, Mathf.Max(t0.z, t1.z)), grime, 0f, false);
                    }
                }
            }
            Streaks("Small_Top", new Vector3(1.5f, 0f, RoomD), new Vector3(SmallX1, 0f, RoomD), Vector3.back, 6);
            Streaks("Small_Left", new Vector3(0f, 0f, SmallZ0), new Vector3(0f, 0f, 4.5f), Vector3.right, 3);
            Streaks("Big_Bottom", new Vector3(0f, 0f, 0f), new Vector3(RoomW, 0f, 0f), Vector3.forward, 9);
            Streaks("Big_Right", new Vector3(RoomW, 0f, 1f), new Vector3(RoomW, 0f, OuterDoorZ - 0.1f), Vector3.left, 5);
            Streaks("Big_Top", new Vector3(SmallX1 + Wall, 0f, RoomD), new Vector3(RoomW, 0f, RoomD), Vector3.back, 4);
            Streaks("Corridor", new Vector3(CorridorX1, 0f, CorridorZ0), new Vector3(CorridorX1, 0f, CorridorZ1), Vector3.left, 6);
            Streaks("Passage", new Vector3(PassX0, 0f, LowZ1 + Wall + 0.3f), new Vector3(PassX0, 0f, CorridorZ0 - Wall - 0.3f), Vector3.right, 4);
            Streaks("Room1_Bottom", new Vector3(Room1X0 + 0.6f, 0f, Room1Z0), new Vector3(Room1X1, 0f, Room1Z0), Vector3.forward, 5);
            Streaks("Room1_Top", new Vector3(Room1X0 + 0.3f, 0f, Room1Z1), new Vector3(Room1X1, 0f, Room1Z1), Vector3.back, 3);
            Streaks("Low_Top", new Vector3(Room1X1 + Wall + 0.3f, 0f, LowZ1), new Vector3(PassX0 - Wall - 0.3f, 0f, LowZ1), Vector3.back, 5);
            Prop(root, "Ceiling_Damp3", new Vector3(Room1X0 + 0.6f, Height - 0.003f, Room1Z1 - 1.3f), new Vector3(Room1X0 + 1.8f, Height, Room1Z1 - 0.1f), grime, 0f, false);
            // damp patches on the ceilings
            Prop(root, "Ceiling_Damp0", new Vector3(2.6f, Height - 0.003f, 7.0f), new Vector3(4.0f, Height, 7.95f), grime, 0f, false);
            Prop(root, "Ceiling_Damp1", new Vector3(5.0f, Height - 0.003f, 0.05f), new Vector3(6.5f, Height, 1.6f), grime, 0f, false);
            Prop(root, "Ceiling_Damp2", new Vector3(7.6f, Height - 0.003f, 5.6f), new Vector3(8.95f, Height, 7.0f), grime, 0f, false);
        }

        // ------------------------------------------------------------------ the pistol, the lights, the scare

        static WeaponPickup PlacePistol(Pistol pistol)
        {
            // on the desk's room-side edge: far enough from the sill to be seen through the window from the big room
            var point = new Vector3(SmallX1 - 0.42f, 0.76f, DeskZ);   // the middle of the desk, where the lamp shines
            var pickup = DarkRoomSceneBuilder.PlaceGunPickup(pistol, WeaponPickup.Kind.Pistol, point, Quaternion.Euler(0f, 112f, 90f));
            pickup.name = "Pickup_Pistol";
            if (pickup.glow)   // the desk lamp lights it: no glow, it is found by looking
            {
                UnityEngine.Object.DestroyImmediate(pickup.glow.gameObject);
                pickup.glow = null;
            }
            // right up against the desk and looking down at it: the player's eyes are on the desk, not the doorway.
            // Pressed to the desk's front the eye is ~1.04 m from the gun, ~51 deg down; a step back is out of reach
            pickup.reach = 1.15f;
            pickup.lookAngle = 7f;
            pickup.minLookDown = 38f;
            // a little easier to aim from the room side, standing at the desk facing the window (no extra reach)
            pickup.easySide = Vector3.left;
            pickup.easySideReach = 0f;
            pickup.easySideLookAngle = 14f;
            EditorUtility.SetDirty(pickup);
            return pickup;
        }

        static void Lights(Transform level)
        {
            var lights = Group(level, "Lights");
            // the desk lamp: a warm pool on the desk and the gun, nothing much beyond it. The Blender model's shade
            // leans out along its +Z; turned to point at the gun, its cable trails back towards the wall
            var lamp = Group(lights, "DeskLamp");
            // standing at the back, a little to the left of the middle: its beam falls on the middle of the desk (on the pistol)
            var lampModel = PlaceModel(DeskLampPrefab, lamp, "DeskLamp_Model", new Vector3(SmallX1 - 0.19f, 0.76f, DeskZ + 0.21f), -132f);
            var bulbGo = lampModel.transform.Find("Bulb").gameObject;
            var deskLight = lampModel.GetComponentInChildren<Light>();
            deskLight.name = "Light";
            deskLight.type = LightType.Spot;
            deskLight.color = new Color(1f, 0.72f, 0.42f);
            // the goal of the room: a soft warm pool that pulls the eye through the door and the window. The bulb is
            // only 0.3 m over the gun, so anything stronger burns the gun and the paper white; a wide inner cone
            // leaves no hot spot in the middle
            deskLight.intensity = 0.3f;
            deskLight.range = 3.8f;
            deskLight.spotAngle = 125f;
            deskLight.innerSpotAngle = 75f;
            deskLight.shapeRadius = 0.06f;   // a frosted bulb: soft shadow edges
            deskLight.shadows = LightShadows.Soft;
            deskLight.shadowNearPlane = 0.05f;
            ViewModelLighting.AlsoLightViewModel(deskLight);
            var flicker = deskLight.gameObject.AddComponent<LightFlicker>();   // a bad contact, now and then
            flicker.kind = LightFlicker.Kind.Fluorescent;
            flicker.depth = 0.35f;
            flicker.pace = new Vector2(6f, 18f);
            flicker.glow = bulbGo.GetComponent<Renderer>();

            // a cold light in the big room's right part: what is seen through the window is seen against it
            Material tube = LightingTestSceneBuilder.EmissiveMaterial("Level_ColdTube", new Color(0.7f, 0.85f, 0.95f), 3f);
            var cold = Group(lights, "ColdLight_RightPart");
            var tubeGo = Prop(cold, "Tube", new Vector3(7.3f, Height - 0.06f, 6.2f), new Vector3(7.45f, Height - 0.02f, 7.4f), tube, 0f, false);
            tubeGo.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            var coldLight = new GameObject("Light").AddComponent<Light>();
            coldLight.transform.SetParent(cold, false);
            coldLight.transform.SetPositionAndRotation(new Vector3(7.37f, Height - 0.1f, 6.8f), Quaternion.Euler(90f, 0f, 0f));
            coldLight.type = LightType.Spot;
            coldLight.color = new Color(0.7f, 0.83f, 1f);
            coldLight.intensity = 2.2f;
            coldLight.range = 6f;
            coldLight.spotAngle = 140f;
            coldLight.innerSpotAngle = 80f;
            coldLight.shadows = LightShadows.Soft;
            coldLight.shadowNearPlane = 0.1f;
            ViewModelLighting.AlsoLightViewModel(coldLight);
            var coldFlicker = coldLight.gameObject.AddComponent<LightFlicker>();
            coldFlicker.kind = LightFlicker.Kind.Fluorescent;
            coldFlicker.depth = 0.85f;
            coldFlicker.pace = new Vector2(2.5f, 9f);
            coldFlicker.glow = tubeGo.GetComponent<Renderer>();

            // hanging lamps, turned down, one at each step of the way (no flashlight here):
            // the start and the outer door, the corner where the L turns, the small room's door
            var lampPrefab = LightingTestSceneBuilder.BuildLampPrefab();
            HangingLamp(lampPrefab, lights, "Lamp_Corridor_Door", new Vector3(RoomW + Wall + 1.0f, Height, OuterDoorZ + 0.2f), 0.45f, false);
            HangingLamp(lampPrefab, lights, "Lamp_Corridor_End", new Vector3(RoomW + Wall + 1.1f, Height, CorridorZ0 + 0.8f), 0.15f, true);
            HangingLamp(lampPrefab, lights, "Lamp_Corner", new Vector3(6.9f, Height, 1.35f), 0.28f, true);
            HangingLamp(lampPrefab, lights, "Lamp_SmallDoor", new Vector3(SmallDoorX + 0.5f, Height, 1.9f), 0.45f, false);
            // the small room's own ceiling light, low and failing: the room reads, the desk lamp still leads
            HangingLamp(lampPrefab, lights, "Lamp_SmallRoom", new Vector3(2.2f, Height, 5.4f), 0.3f, true);
            // the start room: one weak bulb between where the player lies and the door: seen over the head on
            // waking, and once up it is what lights the door (a lamp right over the head only fogs the view)
            HangingLamp(lampPrefab, lights, "Lamp_StartRoom", new Vector3(Room1X1 - 1.0f, Height, (LowZ0 + LowZ1) * 0.5f), 0.55f, true);
            // behind the outer door it is a flat, not the stairwell: old warm bulbs, a little weaker than the
            // corridor's cold lamps (the long cold tube over the right part stays cold: it bursts in the scare)
            foreach (string warm in new[] { "Lamp_SmallDoor", "Lamp_SmallRoom", "Lamp_Corner" })
                WarmUp(lights.Find(warm), 0.8f);
            PassageLights(lights, lampPrefab);
            LockedDoorLamp(lampPrefab, lights);
        }

        /// <summary>The locked door at the corridor's end: a steady lamp just in front of it, its light turned onto the
        /// padlock, so from the turn into the big room the lock reads at once.</summary>
        internal static void LockedDoorLamp(GameObject lampPrefab, Transform lights)
        {
            float lockX = LockedDoorX + DoorBuilder.OpeningWidth * 0.5f;
            HangingLamp(lampPrefab, lights, "Lamp_LockedDoor", new Vector3(lockX, Height, CorridorZ1 - 0.9f), 0.7f, false);
            Transform lockLamp = lights.Find("Lamp_LockedDoor");
            if (lockLamp)
                foreach (Light l in lockLamp.GetComponentsInChildren<Light>(true))
                {
                    if (l.name != "Light" || l.type != LightType.Spot) continue;
                    Vector3 padlock = new Vector3(lockX, 1.25f, CorridorZ1 - 0.05f);
                    l.transform.rotation = Quaternion.LookRotation(padlock - l.transform.position);
                    l.spotAngle = Mathf.Min(l.spotAngle, 90f);
                }
        }

        static void PassageLights(Transform lights, GameObject lampPrefab)
        {
            // the low corridor: dark along its length, a dim lamp at the far corner where it turns up
            HangingLamp(lampPrefab, lights, "Lamp_LowCorner", new Vector3((PassX0 + PassX1) * 0.5f, Height, (LowZ0 + LowZ1) * 0.5f), 0.25f, true);
            // the passage: a single dim bulkhead lamp on its right wall halfway, the corridor's end lamp beyond it
            WallLamp(lights, "WallLamp_Passage", new Vector3(PassX1, 1.85f, 0.2f), -90f, 1.43f, true);
            OuterDoorLamp(lights);
        }

        /// <summary>
        /// A weak bulkhead lamp on the corridor's far wall facing the outer door (the way into the pistol room), its
        /// beam narrowed onto the doorway: the door reads from down the corridor while the corridor past it stays
        /// dark for the zombie's escape (a full lamp over the doorway was taken out for that reason).
        /// </summary>
        internal static void OuterDoorLamp(Transform lights)
        {
            float doorMidZ = OuterDoorZ + DoorBuilder.OpeningWidth * 0.5f;
            WallLamp(lights, "WallLamp_OuterDoor", new Vector3(CorridorX1, 2.05f, doorMidZ), -90f, 1.6f, false);
            Transform lamp = lights.Find("WallLamp_OuterDoor");
            if (!lamp) return;
            Light spot = lamp.Find("Light").GetComponent<Light>();
            Vector3 doorway = new Vector3(RoomW + Wall * 0.5f, 1.0f, doorMidZ);
            spot.transform.rotation = Quaternion.LookRotation(doorway - spot.transform.position);
            spot.spotAngle = 75f;
            spot.innerSpotAngle = 35f;
            spot.range = 5f;
            spot.shadows = LightShadows.Soft;
            Transform wash = lamp.Find("WallWash");
            if (wash) Object.DestroyImmediate(wash.gameObject);   // no spill up the corridor walls
            WarmUp(lamp, 1f);   // an old warm bulb: tells it apart from the corridor's cold lamps
        }

        /// <summary>The colour of the old incandescent bulbs in the flat (the outer door's lamp, the rooms behind it).</summary>
        internal static readonly Color WarmBulb = new Color(1f, 0.82f, 0.62f);

        /// <summary>
        /// Turns a cold lamp into a dim warm one: every light in it takes <see cref="WarmBulb"/> at
        /// <paramref name="dim"/> of its brightness, and its glowing parts (bulb, glass) glow the same colour, so the
        /// fixture and the light it throws match. Safe to run on a lamp that is already warm.
        /// </summary>
        internal static void WarmUp(Transform lamp, float dim)
        {
            if (!lamp) return;
            foreach (Light l in lamp.GetComponentsInChildren<Light>(true))
            {
                if (l.color == WarmBulb) continue;
                l.color = WarmBulb;
                l.intensity *= dim;
                EditorUtility.SetDirty(l);
            }
            foreach (Renderer r in lamp.GetComponentsInChildren<Renderer>(true))
            {
                Material m = r.sharedMaterial;
                if (!m || m.name.EndsWith("_Warm") || !m.IsKeywordEnabled("_EMISSION")) continue;
                r.sharedMaterial = WarmVariant(m);
                EditorUtility.SetDirty(r);
            }
        }

        /// <summary>
        /// What a warm bulb or lamp glass glows. Deeper and weaker than <see cref="WarmBulb"/> on purpose: a strong glow
        /// is pushed to white by ACES and the game look's desaturation, so this is what reads on screen as the same
        /// colour as the light the lamp throws on the wall.
        /// </summary>
        static readonly Color WarmGlow = new Color(1f, 0.42f, 0.13f) * 1.4f;

        /// <summary>A copy of a glowing material (shared by every lamp of its kind) that glows <see cref="WarmGlow"/>.</summary>
        static Material WarmVariant(Material source)
        {
            string path = $"{LightingTestSceneBuilder.MaterialDir}/{source.name}_Warm.mat";
            var warm = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!warm)
            {
                warm = new Material(source) { name = source.name + "_Warm" };
                AssetDatabase.CreateAsset(warm, path);
            }
            warm.SetColor("_EmissionColor", WarmGlow);
            if (warm.HasProperty("_BaseColor")) warm.SetColor("_BaseColor", WarmBulb);
            EditorUtility.SetDirty(warm);
            return warm;
        }

        // ------------------------------------------------------------------ the annex

        /// <summary>
        /// Closed flat doors along the way from the start room (Blender's apartment door, shut for good, on the
        /// wall), and beyond the padlocked door: the leg's dead end, where a zombie stands with its back to the
        /// way in and bangs its head on the end wall under a lamp; on its right an open door into the annex room.
        /// </summary>
        static void BuildAnnex(Transform level, GameObject lampPrefab)
        {
            var annex = Group(level, "Annex");
            FurnishFlat(annex, lampPrefab);

            // the flat's door onto the leg: a plain flat door, shut; its front into the flat
            DoorBuilder.Place(new Vector3(FlatX1 + Wall * 0.5f, 0f, FlatDoorZ), Vector3.forward, false, annex).name = "Door_FlatToLeg";

            // flat doors, their backs against the wall: front (+Z of the model) into the corridor
            var flats = Group(annex, "ApartmentDoors");
            const float inset = 0.02f;   // the frame's back sinks into the plaster
            PlaceModel(ApartmentDoorPrefab, flats, "ApartmentDoor_Passage", new Vector3(PassX0 + inset, 0f, -2.0f), 90f);
            PlaceModel(ApartmentDoorPrefab, flats, "ApartmentDoor_Corner", new Vector3(PassX1 - inset, 0f, (LowZ0 + LowZ1) * 0.5f + 0.3f), -90f);
            PlaceModel(ApartmentDoorPrefab, flats, "ApartmentDoor_Low", new Vector3(7.75f, 0f, LowZ0 + inset), 0f);

            // the open door into the annex room, its front to the leg
            var door = DoorBuilder.Place(new Vector3(AnnexDoorX, 0f, NorthLegZ0 - Wall * 0.5f), Vector3.right, false, annex);
            door.name = "Door_Annex";
            var d = door.GetComponent<Door>();
            if (d)
            {
                d.startOpen = true;
                EditorUtility.SetDirty(d);
            }

            // the head-banger: facing the end wall, back to the way in, forehead about at the wall
            float legMidZ = (NorthLegZ0 + NorthZ1) * 0.5f;
            var bangerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(HeadBangerPrefab);
            if (bangerPrefab)
            {
                var banger = (GameObject)PrefabUtility.InstantiatePrefab(bangerPrefab, annex);
                // a little under the floor: the spawner looks for ground from 3 m above itself, which must stay under the ceiling
                banger.transform.SetPositionAndRotation(new Vector3(NorthLegX1 - 0.45f, -0.5f, legMidZ), Quaternion.Euler(0f, 90f, 0f));
                // its knocks are heard only in the leg itself, from the corner where you step into it (a little
                // before, so the first knock meets you as you come round); a recorded voice once it has seen you,
                // and no stopping to scream: it just comes
                var spawner = banger.GetComponent<InfectedSpawner>();
                spawner.bangHearZone = new Bounds(new Vector3((ZigX0 - 1.6f + NorthLegX1) * 0.5f, 1f, legMidZ),
                                                  new Vector3(NorthLegX1 - ZigX0 + 1.6f, 4f, NorthZ1 - NorthLegZ0 + 0.5f));
                spawner.screams = false;
                spawner.voiceClips = new[] { "zombie_groan", "zombie_groan_2", "zombie_groan_3" }
                    .Select(n => AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/Monster/" + n + ".wav")).Where(c => c).ToArray();
                EditorUtility.SetDirty(spawner);
            }
            else Debug.LogWarning("Level: no head-banger prefab at " + HeadBangerPrefab);

            // a bare bulb right over it: from the far end of the leg it is a lit figure at the end of a dark tunnel
            HangingLamp(lampPrefab, annex, "Lamp_HeadBanger", new Vector3(NorthLegX1 - 0.7f, Height, legMidZ), 0.6f, false);
            // at the leg's start, on its top wall just past the turn: a bulkhead lamp barely alive, so the first steps
            // in are not pitch black and the far bulb still reads as the only real light
            WallLamp(annex, "WallLamp_LegStart", new Vector3(LegX0 + 1.0f, 1.85f, NorthZ1), 180f, 0.25f, false);

            // a little past halfway down the leg: empty bottles by the top wall, the trap across the whole width.
            // Walking through knocks them over; the crash turns the head-banger round (it ignores the gunshot at the lock)
            const float trapX = LegX0 + 6f;   // as far into the leg as where it was tried out
            BottleTrap(annex, new Vector3(trapX, 0f, legMidZ));
        }

        /// <summary>
        /// The flat behind the padlocked door: one room someone lived in and left in a hurry. A bed in the top left
        /// corner, a wardrobe on the left wall, a table with a chair (another knocked over), a sofa facing an old
        /// TV, a fridge by the door, a rug, a shelf, a radiator, litter. The way through (padlocked door at the
        /// bottom to the door onto the leg on the right) is kept clear. Primitives and the Blender props for now.
        /// </summary>
        static void FurnishFlat(Transform parent, GameObject lampPrefab)
        {
            var flat = Group(parent, "Flat");
            float x0 = FlatX0, x1 = FlatX1, z0 = FlatZ0, z1 = FlatZ1;

            // bed in the top left corner: frame, mattress, pillow, a blanket half off
            var bed = Group(flat, "Bed");
            Prop(bed, "Frame", new Vector3(x0 + 0.05f, 0f, z1 - 0.95f), new Vector3(x0 + 2.05f, 0.32f, z1 - 0.05f), woodDark);
            Prop(bed, "Headboard", new Vector3(x0 + 0.05f, 0f, z1 - 0.95f), new Vector3(x0 + 0.1f, 0.85f, z1 - 0.05f), woodDark);
            Prop(bed, "Mattress", new Vector3(x0 + 0.12f, 0.32f, z1 - 0.9f), new Vector3(x0 + 2.0f, 0.48f, z1 - 0.1f), fabric, 0f, false);
            Prop(bed, "Pillow", new Vector3(x0 + 0.18f, 0.48f, z1 - 0.75f), new Vector3(x0 + 0.62f, 0.58f, z1 - 0.25f), paper, 4f, false);
            Prop(bed, "Blanket", new Vector3(x0 + 0.8f, 0.48f, z1 - 0.95f), new Vector3(x0 + 1.95f, 0.53f, z1 - 0.15f), fabricDark, -6f, false);
            Prop(bed, "Blanket_Hang", new Vector3(x0 + 1.1f, 0.05f, z1 - 1.0f), new Vector3(x0 + 1.9f, 0.5f, z1 - 0.96f), fabricDark, -6f, false);

            // wardrobe on the left wall, one door ajar
            var wardrobe = Group(flat, "Wardrobe");
            float wz0 = z0 + 1.3f, wz1 = z0 + 2.55f;
            Prop(wardrobe, "Body", new Vector3(x0, 0f, wz0), new Vector3(x0 + 0.58f, 2.0f, wz1), woodDark);
            Prop(wardrobe, "Seam", new Vector3(x0 + 0.58f, 0.05f, (wz0 + wz1) * 0.5f - 0.005f), new Vector3(x0 + 0.59f, 1.95f, (wz0 + wz1) * 0.5f + 0.005f), fabricDark, 0f, false);
            var leaf = Prop(wardrobe, "Door_Ajar", new Vector3(x0 + 0.58f, 0.05f, wz1 - 0.6f), new Vector3(x0 + 0.6f, 1.95f, wz1), wood, 0f, false);
            leaf.transform.RotateAround(new Vector3(x0 + 0.59f, 0f, wz1), Vector3.up, -25f);
            Prop(wardrobe, "Coat", new Vector3(x0 + 0.6f, 0.9f, wz1 - 0.45f), new Vector3(x0 + 0.66f, 1.7f, wz1 - 0.1f), fabric, 0f, false);

            // table with a chair at it, the other one knocked over; a mug and a bottle on it, a shelf above
            float tx0 = x0 + 1.2f, tx1 = x0 + 2.2f, tz0 = z0 + 1.9f, tz1 = z0 + 2.7f;
            var table = Group(flat, "Table");
            Prop(table, "Top", new Vector3(tx0, 0.72f, tz0), new Vector3(tx1, 0.76f, tz1), wood);
            for (int i = 0; i < 4; i++)
            {
                float lx = i % 2 == 0 ? tx0 + 0.04f : tx1 - 0.09f, lz = i < 2 ? tz0 + 0.04f : tz1 - 0.09f;
                Prop(table, "Leg" + i, new Vector3(lx, 0f, lz), new Vector3(lx + 0.05f, 0.72f, lz + 0.05f), wood, 0f, false);
            }
            Prop(table, "Cloth", new Vector3(tx0 + 0.1f, 0.76f, tz0 + 0.05f), new Vector3(tx1 - 0.05f, 0.765f, tz1 - 0.1f), carpet, 3f, false);
            Bare(PlaceModel(MugPrefab, table, "Mug", new Vector3(tx0 + 0.35f, 0.765f, tz0 + 0.3f), 40f));
            Cylinder(table, "Bottle", new Vector3(tx1 - 0.25f, 0.765f + 0.13f, tz1 - 0.3f), 0.035f, 0.26f, glass, false);
            Chair(flat, "Chair", new Vector3((tx0 + tx1) * 0.5f, 0f, tz0 - 0.35f), 180f, false);
            Chair(flat, "Chair_Fallen", new Vector3(tx1 + 0.45f, 0f, tz1 + 0.3f), 250f, true);
            PlaceModel(WallShelfPrefab, flat, "Shelf", new Vector3(x0 + 3.0f, 1.5f, z1), 180f);

            // the sofa against the top wall, facing the TV across a rug
            PlaceModel(SofaPrefab, flat, "Sofa", new Vector3(x1 - 1.7f, 0f, z1 - 0.45f), 180f);
            Prop(flat, "Rug", new Vector3(x1 - 2.9f, 0f, z0 + 1.2f), new Vector3(x1 - 0.6f, 0.006f, z1 - 1.0f), carpet, 2f, false);
            // an old TV on a low cabinet at the bottom wall, right of the padlocked door
            var tv = Group(flat, "TV");
            float cx = x1 - 1.6f;
            Prop(tv, "Cabinet", new Vector3(cx - 0.55f, 0f, z0 + 0.02f), new Vector3(cx + 0.55f, 0.5f, z0 + 0.47f), woodDark);
            Prop(tv, "Set", new Vector3(cx - 0.3f, 0.5f, z0 + 0.06f), new Vector3(cx + 0.3f, 0.95f, z0 + 0.5f), fabricDark);
            // the set is still on, showing nothing: a cold flickering glow onto the rug and the sofa
            Material screen = LightingTestSceneBuilder.EmissiveMaterial("Level_TVScreen", new Color(0.55f, 0.65f, 0.85f), 1.6f);
            var screenGo = Prop(tv, "Screen", new Vector3(cx - 0.23f, 0.56f, z0 + 0.5f), new Vector3(cx + 0.17f, 0.9f, z0 + 0.505f), screen, 0f, false);
            screenGo.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            var tvLight = new GameObject("Light").AddComponent<Light>();
            tvLight.transform.SetParent(tv, false);
            tvLight.transform.SetPositionAndRotation(new Vector3(cx - 0.03f, 0.75f, z0 + 0.6f), Quaternion.LookRotation(new Vector3(0f, -0.25f, 1f)));
            tvLight.type = LightType.Spot;
            tvLight.color = new Color(0.6f, 0.72f, 1f);
            tvLight.intensity = 1.6f;
            tvLight.range = 4.5f;
            tvLight.spotAngle = 120f;
            tvLight.innerSpotAngle = 60f;
            tvLight.shadows = LightShadows.None;
            ViewModelLighting.AlsoLightViewModel(tvLight);
            var tvFlicker = tvLight.gameObject.AddComponent<LightFlicker>();
            tvFlicker.kind = LightFlicker.Kind.Screen;
            tvFlicker.glow = screenGo.GetComponent<Renderer>();
            Cylinder(tv, "Antenna", new Vector3(cx + 0.1f, 1.1f, z0 + 0.25f), 0.006f, 0.32f, rust, false).transform.rotation = Quaternion.Euler(0f, 0f, 25f);

            // the fridge in the bottom left corner, a radiator under where a window would be
            PlaceModel(FridgePrefab, flat, "Fridge", new Vector3(x0 + 0.36f, 0f, z0 + 0.36f), 0f);
            Radiator(flat, "Radiator", new Vector3(x0 + 0.85f, 0.12f, z0 + 0.04f), 8);

            // litter: papers, a box, a bag of rubbish
            PlaceModel(BoxOpenPrefab, flat, "Box", new Vector3(x1 - 0.5f, 0f, z1 - 1.5f), 20f);
            RubbishPile(flat, "Litter", new Vector3(x0 + 2.6f, 0f, z0 + 1.0f), 0.6f, 10, 4242);
            RubbishPile(flat, "Litter_Bed", new Vector3(x0 + 1.4f, 0f, z1 - 1.4f), 0.5f, 8, 4343);

            // the ceiling lamp in the middle: enough to make out the room, the door on the right in its light
            HangingLamp(lampPrefab, flat, "Lamp_Flat", new Vector3((x0 + x1) * 0.5f + 0.6f, Height, (z0 + z1) * 0.5f), 0.6f, false);

            // a bedside lamp on a nightstand by the bed: a warm pool in the dark corner
            var stand = Group(flat, "Nightstand");
            float nx = x0 + 2.4f, nz = z1 - 0.3f;
            Prop(stand, "Body", new Vector3(nx - 0.22f, 0f, nz - 0.22f), new Vector3(nx + 0.22f, 0.5f, nz + 0.22f), woodDark);
            var bedside = PlaceModel(DeskLampPrefab, stand, "Lamp", new Vector3(nx, 0.5f, nz + 0.05f), 200f);
            Light warm = bedside.GetComponentInChildren<Light>();
            if (warm)
            {
                warm.type = LightType.Spot;
                warm.color = new Color(1f, 0.7f, 0.42f);
                warm.intensity = 1.5f;
                warm.range = 3.2f;
                warm.spotAngle = 120f;
                warm.innerSpotAngle = 70f;
                warm.shadows = LightShadows.Soft;
                warm.shadowNearPlane = 0.05f;
                ViewModelLighting.AlsoLightViewModel(warm);
            }
        }

        /// <summary>A few empty bottles at the top wall that the player knocks over (<see cref="NoiseTrap"/>).
        /// The trap's box spans the corridor; the bottles are its children.</summary>
        static void BottleTrap(Transform parent, Vector3 at)
        {
            var go = new GameObject("NoiseTrap_Bottles");
            go.transform.SetParent(parent, false);
            go.transform.position = at;
            var trap = go.AddComponent<NoiseTrap>();
            float legWidth = NorthZ1 - NorthLegZ0;
            trap.triggerSize = new Vector3(0.8f, 2f, legWidth);
            trap.kick = new Vector3(1.6f, 0.5f, -0.4f);   // on along the corridor, away from the wall
            trap.sound = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/ChainRoom/lamp_break_drop.mp3");
            trap.volume = 1f;

            Material bottleGlass = LightingTestSceneBuilder.SimpleMaterial("Level_BottleGlass", new Color(0.1f, 0.22f, 0.1f, 0.55f), 0f, 0.9f);
            MakeTransparent(bottleGlass);
            float wallSide = legWidth * 0.5f - 0.12f;   // against the top wall
            var bottles = new System.Collections.Generic.List<Rigidbody>
            {
                Bottle(go.transform, "Bottle0", new Vector3(-0.1f, 0f, wallSide), bottleGlass, false),
                Bottle(go.transform, "Bottle1", new Vector3(0.06f, 0f, wallSide - 0.05f), bottleGlass, false),
                Bottle(go.transform, "Bottle2", new Vector3(0.2f, 0f, wallSide + 0.01f), bottleGlass, false),
                Bottle(go.transform, "Bottle3", new Vector3(0.0f, 0f, wallSide - 0.25f), bottleGlass, true),
            };
            trap.things = bottles.ToArray();
            EditorUtility.SetDirty(trap);
        }

        /// <summary>An empty bottle: body and neck, a capsule collider, a light rigidbody (held still by the trap).</summary>
        static Rigidbody Bottle(Transform parent, string name, Vector3 localFloor, Material material, bool lying)
        {
            const float bodyH = 0.2f, neckH = 0.09f, r = 0.037f;
            var root = new GameObject(name).transform;
            root.SetParent(parent, false);
            var b = Cylinder(root, "Body", Vector3.zero, r, bodyH, material, false);
            b.transform.localPosition = new Vector3(0f, bodyH * 0.5f, 0f);
            var n = Cylinder(root, "Neck", Vector3.zero, r * 0.38f, neckH, material, false);
            n.transform.localPosition = new Vector3(0f, bodyH + neckH * 0.5f, 0f);
            foreach (Renderer rend in root.GetComponentsInChildren<Renderer>()) { rend.gameObject.isStatic = false; rend.shadowCastingMode = ShadowCastingMode.Off; }
            var col = root.gameObject.AddComponent<CapsuleCollider>();
            col.radius = r;
            col.height = bodyH + neckH;
            col.center = new Vector3(0f, (bodyH + neckH) * 0.5f, 0f);
            var rb = root.gameObject.AddComponent<Rigidbody>();
            rb.mass = 0.35f;
            rb.isKinematic = true;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            if (lying)
            {
                root.localPosition = localFloor + new Vector3(0f, r, 0f);
                root.localRotation = Quaternion.Euler(0f, 35f, 90f);
            }
            else root.localPosition = localFloor;
            return rb;
        }

        /// <summary>The oval bulkhead lamp (BodycamHorror > Set Up Utility Props) on a wall: its back on the wall
        /// at <paramref name="wallPoint"/>, turned <paramref name="yaw"/> to face the room; its spot at
        /// <paramref name="intensity"/>, the wall wash in proportion.</summary>
        static void WallLamp(Transform parent, string name, Vector3 wallPoint, float yaw, float intensity, bool flicker)
        {
            var lamp = PlaceModel(WallLampPrefab, parent, name, wallPoint, yaw);
            Light spot = lamp.transform.Find("Light").GetComponent<Light>();
            Light wash = lamp.transform.Find("WallWash").GetComponent<Light>();
            wash.intensity *= intensity / spot.intensity;
            spot.intensity = intensity;
            if (!flicker) return;
            var f = spot.gameObject.AddComponent<LightFlicker>();
            f.kind = LightFlicker.Kind.Fluorescent;
            f.depth = 0.6f;
            f.pace = new Vector2(3f, 10f);
            foreach (Renderer r in lamp.GetComponentsInChildren<Renderer>(true)) if (r.name.EndsWith("_Glass")) f.glow = r;
        }

        static void HangingLamp(GameObject prefab, Transform parent, string name, Vector3 ceilingPoint, float dim, bool flicker)
        {
            LightingTestSceneBuilder.PlaceLamp(prefab, parent, name, ceilingPoint, false);
            Transform lamp = parent.Find(name);
            Light main = null;
            foreach (Light l in lamp.GetComponentsInChildren<Light>(true))
            {
                l.intensity *= dim;
                if (l.name == "Light") main = l;
                // shadows only from the brighter lamps: each shadowed spot renders the scene again for itself,
                // and a dim one's shadows hardly show
                if (dim < 0.35f) l.shadows = LightShadows.None;
            }
            if (flicker && main)
            {
                var f = main.gameObject.AddComponent<LightFlicker>();
                f.kind = LightFlicker.Kind.Fluorescent;
                f.depth = 0.6f;
                f.pace = new Vector2(3f, 10f);
                var bulb = lamp.GetComponentsInChildren<Renderer>(true);
                foreach (Renderer r in bulb) if (r.name == "Bulb") f.glow = r;
            }
        }

        /// <summary>The pistol scare: the gun is taken while looking down at the desk. The outer door
        /// creaks open; looking up, through the window and across the big room, the monster is leaning
        /// out from behind the doorway's jamb in the corridor, looking in. Once it is seen: a loud sting
        /// and its scream, a moment's stare, then it ducks back behind the corner and runs off up the
        /// dark corridor.</summary>
        static void PlaceScare(WeaponPickup pickup)
        {
            var go = new GameObject("Scare_MonsterPeek");
            // hidden in the corridor just past the doorway's far jamb, turned to look in through it
            // (checked from the desk: fully hidden here, claws and all)
            go.transform.SetPositionAndRotation(new Vector3(RoomW + Wall + 0.4f, 0f, OuterDoorZ + DoorBuilder.OpeningWidth + 0.665f),
                                                Quaternion.LookRotation(new Vector3(-1f, 0f, -0.6f)));
            var scare = go.AddComponent<PeekScare>();
            scare.pickup = pickup;
            // it hardly steps: it tilts its upper body out round the jamb, so only the head, a shoulder and
            // an arm come into the doorway while the hips stay at the jamb's edge
            scare.peekOffset = new Vector3(0f, 0f, -0.05f);
            scare.peekLean = 23f;
            scare.appearDelay = 0f;   // there at once, already leaning out: the player is looking down at the desk
            scare.peekTime = 0f;
            scare.viewMargin = 0.02f; // goes off as soon as it shows at the edge of the view
            var doorObject = GameObject.Find("Level/Doors/Door_Outer");
            if (doorObject) scare.door = doorObject.GetComponent<Door>();
            var glassObject = GameObject.Find("Level/Window/Glass");
            if (glassObject) scare.seeThrough = new[] { glassObject.GetComponent<Collider>() };
            scare.fleePath = new[]
            {
                new Vector3(RoomW + Wall + 0.9f, 0f, OuterDoorZ + DoorBuilder.OpeningWidth + 0.9f),   // back behind the corner
                new Vector3(RoomW + Wall + 1.0f, 0f, CorridorZ1 - 0.4f),                              // up the dark corridor
            };
            scare.stress = 0.85f;   // a full panic heartbeat and ringing ears

            scare.modelPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Zombie/Slasher/ZombieSlasher.fbx");
            scare.modelMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Zombie/Slasher/ZombieSlasher.mat");
            scare.modelScale = 0.7f;
            scare.idleClip = Clip("Zombie_Idle_02");
            scare.runClip = Clip("Zombie_Sprint_01_Forward_InPlace");

            scare.scream = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/Monster/monster_scream.wav");
            var steps = new System.Collections.Generic.List<AudioClip>();
            foreach (string guid in AssetDatabase.FindAssets("t:AudioClip", new[] { "Assets/Resources/Audio/Footsteps" }))
                steps.Add(AssetDatabase.LoadAssetAtPath<AudioClip>(AssetDatabase.GUIDToAssetPath(guid)));
            scare.steps = steps.ToArray();
            // the lamp over the doorway stutters as it goes
            var lamp = GameObject.Find("Level/Lights/Lamp_Corridor_Door");
            if (lamp) foreach (Light l in lamp.GetComponentsInChildren<Light>(true)) if (l.name == "Light") scare.flickerLight = l;
            EditorUtility.SetDirty(scare);
        }

        static AnimationClip Clip(string file)
        {
            foreach (string ext in new[] { ".FBX", ".fbx" })
                foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath("Assets/Zombie_Animations/Animations/" + file + ext))
                    if (asset is AnimationClip clip && !clip.name.StartsWith("__preview__")) return clip;
            Debug.LogWarning("Level: no clip " + file);
            return null;
        }

        static void AddAmbience()
        {
            AmbienceBuilder.ImportSettings();
            var root = new GameObject("Ambience").transform;
            AmbienceBuilder.Loop(root, "Bed_Drone", Vector3.zero, 0.22f, 0f, 0f, 0f, "loop_bed_drone");
            AmbienceBuilder.Loop(root, "Bed_Hum", Vector3.zero, 0.05f, 0f, 0f, 0f, "loop_bed_hum");
            AmbienceBuilder.Loop(root, "Hum_ColdLight", new Vector3(7.37f, Height - 0.1f, 6.8f), 0.14f, 1f, 0.4f, 5f, "loop_hum_bulb");
            AmbienceBuilder.Shots(root, "Drip_Small", new Vector3(3.3f, Height - 0.1f, RoomD - 0.6f), 0.35f, new Vector2(4f, 11f), 0f, 1f, 10f, AmbienceBuilder.Named("drip_"));
            AmbienceBuilder.Shots(root, "Drip_Big", new Vector3(4.25f, Height - 0.1f, 2.15f), 0.4f, new Vector2(3f, 9f), 0f, 1f, 12f, AmbienceBuilder.Named("drip_"));
            AmbienceBuilder.Shots(root, "Creaks", new Vector3(4.5f, Height + 0.5f, 4f), 0.4f, new Vector2(14f, 34f), 6f, 2f, 30f, AmbienceBuilder.Named("creak_"));
        }

        // ------------------------------------------------------------------ shapes

        static Transform Group(Transform parent, string name)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            return t;
        }

        // the office props modelled in Blender (Blender/Props_Office.blend), floor-pivoted, front on their +Z
        const string DeskPrefab = "Assets/Prefabs/Props/Desk.prefab";
        const string ChairPrefab = "Assets/Prefabs/Props/Chair.prefab";
        const string DeskLampPrefab = "Assets/Prefabs/Props/DeskLamp.prefab";
        // the Khrushchevka apartment props (BodycamHorror > Set Up Apartment Props)
        const string SofaPrefab = "Assets/Prefabs/Props/Sofa.prefab";
        const string WallShelfPrefab = "Assets/Prefabs/Props/WallShelf.prefab";
        const string MugPrefab = "Assets/Prefabs/Props/Mug.prefab";
        const string ClipboardPrefab = "Assets/Prefabs/Props/Clipboard.prefab";
        // the utility props (BodycamHorror > Set Up Utility Props)
        const string WallLampPrefab = "Assets/Prefabs/Props/WallLamp.prefab";
        const string ElectricPanelPrefab = "Assets/Prefabs/Props/ElectricPanel.prefab";
        // the storage props (BodycamHorror > Set Up Storage Props)
        const string FridgePrefab = "Assets/Prefabs/Props/Fridge.prefab";
        const string BoxClosedPrefab = "Assets/Prefabs/Props/Box_Closed.prefab";
        const string BoxOpenPrefab = "Assets/Prefabs/Props/Box_Open.prefab";
        const string BoxSmallPrefab = "Assets/Prefabs/Props/Box_Small.prefab";
        const string BoxCrushedPrefab = "Assets/Prefabs/Props/Box_Crushed.prefab";

        /// <summary>Takes the colliders off a placed model (small things on a surface).</summary>
        static void Bare(GameObject go)
        {
            foreach (Collider c in go.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
        }

        /// <summary>A prefab instance at a world point, turned <paramref name="yaw"/> degrees.</summary>
        static GameObject PlaceModel(string prefabPath, Transform parent, string name, Vector3 position, float yaw)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null) throw new System.IO.FileNotFoundException("Missing prop prefab", prefabPath);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            go.name = name;
            go.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
            foreach (Transform t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.isStatic = true;   // furniture: batched
            return go;
        }

        /// <summary>A cube between two corners, turned <paramref name="yaw"/> degrees about its middle.</summary>
        static GameObject Prop(Transform parent, string name, Vector3 min, Vector3 max, Material material, float yaw = 0f, bool collider = true)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.position = (min + max) * 0.5f;
            go.transform.localScale = max - min;
            go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            go.GetComponent<Renderer>().sharedMaterial = material;
            if (!collider) Object.DestroyImmediate(go.GetComponent<Collider>());
            go.isStatic = true;
            return go;
        }

        /// <summary>An upright cylinder: <paramref name="centre"/> is its middle.</summary>
        static GameObject Cylinder(Transform parent, string name, Vector3 centre, float radius, float height, Material material, bool collider = true)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.position = centre;
            go.transform.localScale = new Vector3(radius * 2f, height * 0.5f, radius * 2f);
            go.GetComponent<Renderer>().sharedMaterial = material;
            if (!collider) Object.DestroyImmediate(go.GetComponent<Collider>());
            else
            {   // a capsule collider on a cylinder rounds its ends: a box is closer for furniture
                Object.DestroyImmediate(go.GetComponent<Collider>());
                go.AddComponent<BoxCollider>().size = new Vector3(1f, 2f, 1f);
            }
            go.isStatic = true;
            return go;
        }

        /// <summary>A plain wooden chair; <paramref name="fallen"/> lays it on its back.</summary>
        static void Chair(Transform parent, string name, Vector3 floor, float yaw, bool fallen)
        {
            var chair = Group(parent, name);
            const float s = 0.42f;
            // the Blender model faces its +Z; this chair's convention is back at +Z, facing -Z
            var model = PlaceModel(ChairPrefab, chair, "Chair_Model", Vector3.zero, 0f).transform;
            model.localPosition = Vector3.zero;
            model.localRotation = Quaternion.Euler(0f, 180f, 0f);
            if (fallen)
            {
                chair.localPosition = new Vector3(0f, s / 2 + 0.02f, 0.47f);   // lying on its back, centred on the pivot
                chair.localRotation = Quaternion.Euler(-90f, 0f, 0f);
                var pivot = Group(parent, name + "_Fallen");
                chair.SetParent(pivot, false);
                pivot.SetPositionAndRotation(floor, Quaternion.Euler(0f, yaw, 0f));
            }
            else chair.SetPositionAndRotation(floor, Quaternion.Euler(0f, yaw, 0f));
        }

        /// <summary>A cast-iron radiator against a wall: <paramref name="start"/> its left bottom front.</summary>
        static void Radiator(Transform parent, string name, Vector3 start, int fins)
        {
            var r = Group(parent, name);
            for (int i = 0; i < fins; i++)
                Prop(r, "Fin" + i, start + new Vector3(i * 0.09f, 0f, -0.04f), start + new Vector3(i * 0.09f + 0.07f, 0.55f, 0.08f), rust, 0f, false);
            var pipe = Cylinder(r, "Pipe", start + new Vector3(fins * 0.09f + 0.06f, (Height - start.y) * 0.5f, 0.06f), 0.02f, Height, rust, false);
            pipe.transform.position = new Vector3(pipe.transform.position.x, Height * 0.5f, pipe.transform.position.z);
            Prop(r, "Collider", start + new Vector3(0f, 0f, -0.04f), start + new Vector3(fins * 0.09f, 0.55f, 0.08f), rust).GetComponent<Renderer>().enabled = false;
        }

        static void Bags(Transform parent, Vector3 at, int count)
        {
            var rng = new System.Random(at.GetHashCode());
            var g = Group(parent, "Bags");
            for (int i = 0; i < count; i++)
            {
                var bag = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                bag.name = "Bag" + i;
                bag.transform.SetParent(g, false);
                float sx = 0.4f + (float)rng.NextDouble() * 0.25f;
                bag.transform.localScale = new Vector3(sx, 0.3f + (float)rng.NextDouble() * 0.2f, sx * 0.85f);
                bag.transform.position = at + new Vector3(((float)rng.NextDouble() - 0.5f) * 0.9f, bag.transform.localScale.y * 0.42f + (i > 3 ? 0.2f : 0f),
                                                          ((float)rng.NextDouble() - 0.5f) * 0.6f);
                bag.transform.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, ((float)rng.NextDouble() - 0.5f) * 20f);
                bag.GetComponent<Renderer>().sharedMaterial = fabricDark;
                bag.isStatic = true;
            }
        }

        static void RubbishPile(Transform parent, string name, Vector3 at, float radius, int count, int seed)
        {
            var rng = new System.Random(seed);
            var g = Group(parent, name);
            for (int i = 0; i < count; i++)
            {
                Vector2 o = Random2(rng) * radius;
                float s = 0.05f + (float)rng.NextDouble() * 0.12f;
                Material m = i % 3 == 0 ? paper : i % 3 == 1 ? rubbish : cardboard;
                Prop(g, "Bit" + i, at + new Vector3(o.x - s, 0f, o.y - s), at + new Vector3(o.x + s, s * 0.8f, o.y + s), m, (float)rng.NextDouble() * 360f, false)
                    .transform.Rotate((float)rng.NextDouble() * 30f, 0f, (float)rng.NextDouble() * 30f);
            }
        }

        static Vector2 Random2(System.Random rng)
        {
            float a = (float)rng.NextDouble() * Mathf.PI * 2f, r = Mathf.Sqrt((float)rng.NextDouble());
            return new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
        }

        static void Puddle(Transform parent, Vector3 at, float sx, float sz, float yaw)
        {
            var p = GameObject.CreatePrimitive(PrimitiveType.Cylinder);   // round-ish puddles
            p.name = "Puddle";
            p.transform.SetParent(parent, false);
            p.transform.position = at + Vector3.up * 0.002f;
            p.transform.localScale = new Vector3(sx, 0.001f, sz);
            p.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            p.GetComponent<Renderer>().sharedMaterial = wet;
            p.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            Object.DestroyImmediate(p.GetComponent<Collider>());
            p.isStatic = true;
        }
    }
}
