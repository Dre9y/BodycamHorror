using System.IO;
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
    /// Menu: BodycamHorror > Build Level Scene. Rebuilding writes over the scene.
    ///
    /// Coordinates (m): X to the right of the sketch, Z up the sketch, origin at the big room's inner
    /// bottom-left corner.
    /// </summary>
    public static class LevelSceneBuilder
    {
        const string ScenePath = "Assets/Scenes/Level.unity";
        const string SourceScenePath = "Assets/Scenes/LightingTest.unity";

        const float Height = 2.7f;      // a panel block's low ceiling
        const float Wall = 0.3f;
        const float PanelHeight = 1.45f;   // oil paint up to here, plaster above

        // the outer shell, inside faces
        const float RoomW = 9f, RoomD = 8f;
        // the small room: top-left corner of the shell
        const float SmallX1 = 4.8f;     // its right (inner) wall's inside face
        const float SmallZ0 = 3f;       // its bottom (inner) wall's inside face
        // doors (hinge-side edge of the opening)
        const float SmallDoorX = 1.0f;  // in the small room's bottom wall
        const float OuterDoorZ = 6.6f;  // in the big room's right wall
        // the window in the small room's right wall
        const float WindowZ0 = 5.2f, WindowZ1 = 6.9f, SillY = 0.9f, WindowTopY = 2.1f;   // wide: from anywhere along the desk the outer doorway shows through it
        // the corridor outside the outer door
        const float CorridorX1 = RoomW + Wall + 2.2f, CorridorZ0 = 3.6f, CorridorZ1 = 10f;

        static Material wallPaint, wallPlaster, floorMat, ceilingMat;
        static Material wood, woodDark, woodPaint, fabric, fabricDark, paper, grime, wet, rust, enamel, cardboard, rubbish, carpet, glass;

        [MenuItem("BodycamHorror/Build Level Scene")]
        public static void Build()
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
                // in the corridor, facing the outer door
                player.transform.SetPositionAndRotation(new Vector3(RoomW + Wall + 1.1f, player.transform.position.y, OuterDoorZ + 0.5f),
                                                        Quaternion.Euler(0f, -90f, 0f));
                var shooting = player.GetComponentInChildren<PlayerShooting>(true);
                if (shooting)
                {
                    shooting.startUnarmed = true;   // the gun is found here
                    EditorUtility.SetDirty(shooting);
                    foreach (Firearm w in shooting.weapons) if (w is Pistol p) pistol = p;
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
            Rubbish(level);
            Leaks(level);
            WeaponPickup pickup = PlacePistol(pistol);
            Lights(level);
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

        // ------------------------------------------------------------------ materials

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
            WallX("Corridor_EndNear", shell, RoomW + w, CorridorX1, CorridorZ0 - w, CorridorZ0);
            WallX("Corridor_EndFar", shell, RoomW, CorridorX1, CorridorZ1, CorridorZ1 + w);
            // its near side is the big room's right wall; past the room's top corner it needs a wall of its own
            WallZ("Corridor_Near", shell, RoomD + w, CorridorZ1, RoomW, RoomW + w);
        }

        static void BuildDoors(Transform level)
        {
            var doors = Group(level, "Doors");
            // small room: in the middle of its bottom wall, front facing into the small room
            DoorBuilder.Place(new Vector3(SmallDoorX, 0f, SmallZ0 - Wall * 0.5f), Vector3.right, false, doors).name = "Door_SmallRoom";
            // outer: in the big room's right wall, front facing into the big room
            DoorBuilder.Place(new Vector3(RoomW + Wall * 0.5f, 0f, OuterDoorZ), Vector3.forward, false, doors).name = "Door_Outer";
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

            // the desk along the window wall, under the window
            var desk = Group(room, "Desk");
            float dx0 = SmallX1 - 0.7f, dx1 = SmallX1 - 0.02f, dz0 = 5.05f, dz1 = 6.55f, top = 0.76f;
            Prop(desk, "Top", new Vector3(dx0, top - 0.04f, dz0), new Vector3(dx1, top, dz1), wood);
            Prop(desk, "Pedestal", new Vector3(dx0 + 0.03f, 0f, dz0 + 0.02f), new Vector3(dx1 - 0.03f, top - 0.04f, dz0 + 0.45f), wood);
            for (int i = 0; i < 3; i++)
                Prop(desk, "Drawer" + i, new Vector3(dx0 + 0.01f, 0.06f + i * 0.22f, dz0 + 0.04f), new Vector3(dx0 + 0.03f, 0.24f + i * 0.22f, dz0 + 0.43f), woodDark, collider: false);
            Prop(desk, "Leg_A", new Vector3(dx0 + 0.03f, 0f, dz1 - 0.07f), new Vector3(dx0 + 0.08f, top - 0.04f, dz1 - 0.02f), wood);
            Prop(desk, "Leg_B", new Vector3(dx1 - 0.08f, 0f, dz1 - 0.07f), new Vector3(dx1 - 0.03f, top - 0.04f, dz1 - 0.02f), wood);
            // on it: papers, a mug, a book; the lamp is built with the lights
            Prop(desk, "Papers", new Vector3(dx0 + 0.15f, top, 5.25f), new Vector3(dx0 + 0.45f, top + 0.01f, 5.55f), paper, 14f, false);
            Prop(desk, "Paper_Loose", new Vector3(dx0 + 0.05f, top, 6.1f), new Vector3(dx0 + 0.26f, top + 0.003f, 6.4f), paper, -22f, false);
            Cylinder(desk, "Mug", new Vector3(dx0 + 0.2f, top + 0.045f, 5.75f), 0.04f, 0.09f, enamel, false);
            Prop(desk, "Book", new Vector3(dx0 + 0.42f, top, 5.15f), new Vector3(dx0 + 0.6f, top + 0.04f, 5.4f), woodDark, -6f, false);

            // the chair pushed back from the desk
            Chair(room, "Chair_Desk", new Vector3(dx0 - 0.45f, 0f, 5.75f), 85f, false);

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
            Chair(room, "Chair_Fallen", new Vector3(SmallX1 - 0.5f, 0f, SmallZ0 + 0.5f), -30f, true);
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

            // the bottom strip: a sofa on the left, an overturned table, a fridge in the far corner
            var sofa = Group(room, "Sofa");
            Prop(sofa, "Base", new Vector3(0.05f, 0f, 0.3f), new Vector3(0.9f, 0.42f, 2.3f), fabric);
            Prop(sofa, "Back", new Vector3(0.05f, 0.42f, 0.3f), new Vector3(0.3f, 0.9f, 2.3f), fabric);
            Prop(sofa, "Arm_A", new Vector3(0.05f, 0.42f, 0.3f), new Vector3(0.9f, 0.6f, 0.45f), fabric);
            Prop(sofa, "Arm_B", new Vector3(0.05f, 0.42f, 2.15f), new Vector3(0.9f, 0.6f, 2.3f), fabric);
            Prop(sofa, "Cushion_Fallen", new Vector3(1.0f, 0f, 1.0f), new Vector3(1.55f, 0.12f, 1.55f), fabricDark, 25f);
            Prop(sofa, "Stain", new Vector3(0.35f, 0.421f, 1.2f), new Vector3(0.8f, 0.423f, 1.9f), grime, 0f, false);

            var table = Group(room, "Table_Overturned");   // on its side, legs to the room
            Prop(table, "Top", new Vector3(2.6f, 0f, 1.6f), new Vector3(3.8f, 0.75f, 1.64f), wood, 8f);
            for (int i = 0; i < 2; i++)
            {
                float x = 2.7f + i * 0.95f;
                Prop(table, "Leg" + i, new Vector3(x, 0.05f, 0.95f), new Vector3(x + 0.05f, 0.1f, 1.6f), wood, 8f);
                Prop(table, "LegT" + i, new Vector3(x, 0.62f, 0.95f), new Vector3(x + 0.05f, 0.67f, 1.6f), wood, 8f);
            }

            var fridge = Group(room, "Fridge");
            Prop(fridge, "Body", new Vector3(RoomW - 0.7f, 0f, 0.05f), new Vector3(RoomW - 0.05f, 1.45f, 0.68f), enamel);
            Prop(fridge, "Rust", new Vector3(RoomW - 0.71f, 0.02f, 0.1f), new Vector3(RoomW - 0.7f, 0.35f, 0.6f), rust, 0f, false);
            Prop(fridge, "Handle", new Vector3(RoomW - 0.73f, 0.9f, 0.55f), new Vector3(RoomW - 0.7f, 1.2f, 0.58f), rust, 0f, false);

            // cardboard boxes, a heap of rubbish bags
            var boxes = Group(room, "Boxes");
            Prop(boxes, "Box0", new Vector3(5.6f, 0f, 0.1f), new Vector3(6.2f, 0.45f, 0.6f), cardboard, 6f);
            Prop(boxes, "Box1", new Vector3(6.25f, 0f, 0.12f), new Vector3(6.8f, 0.5f, 0.62f), cardboard, -4f);
            Prop(boxes, "Box2", new Vector3(5.75f, 0.45f, 0.15f), new Vector3(6.25f, 0.8f, 0.55f), cardboard, 18f);
            Prop(boxes, "Box_Crushed", new Vector3(6.6f, 0f, 0.9f), new Vector3(7.1f, 0.12f, 1.3f), cardboard, 40f);
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

            // the corridor outside: bare, a few things against the far wall
            var corridor = Group(level, "Corridor");
            Prop(corridor, "Box", new Vector3(CorridorX1 - 0.6f, 0f, 8.6f), new Vector3(CorridorX1 - 0.05f, 0.5f, 9.2f), cardboard, 10f);
            Bags(corridor, new Vector3(CorridorX1 - 0.4f, 0f, 4.3f), 3);
            Cylinder(corridor, "Riser", new Vector3(CorridorX1 - 0.12f, Height * 0.5f, 6.0f), 0.05f, Height, rust);
        }

        /// <summary>Litter over both floors and the corridor: papers, chunks of plaster, bits, puddles.</summary>
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
                new[] { RoomW + Wall + 0.2f, CorridorZ0 + 0.2f, CorridorX1 - 0.2f, CorridorZ1 - 0.2f, 22f },
            };
            int n = 0;
            foreach (float[] a in areas)
                for (int i = 0; i < (int)a[4]; i++)
                {
                    float x = Mathf.Lerp(a[0], a[2], (float)rng.NextDouble());
                    float z = Mathf.Lerp(a[1], a[3], (float)rng.NextDouble());
                    float yaw = (float)rng.NextDouble() * 360f;
                    double kind = rng.NextDouble();
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
            // puddles under the leaks (see Leaks): flat and glossy, they shine in the flashlight
            Puddle(root, new Vector3(3.3f, 0f, RoomD - 0.6f), 0.9f, 0.5f, 10f);
            Puddle(root, new Vector3(5.6f, 0f, 1.2f), 1.4f, 0.8f, -20f);
            Puddle(root, new Vector3(8.3f, 0f, 6.3f), 0.8f, 1.1f, 35f);
            Puddle(root, new Vector3(4.25f, 0f, 2.15f), 1.0f, 0.7f, 5f);   // under the ceiling hole
            Puddle(root, new Vector3(RoomW + Wall + 1.0f, 0f, 5.2f), 0.9f, 1.4f, 0f);
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
            // damp patches on the ceilings
            Prop(root, "Ceiling_Damp0", new Vector3(2.6f, Height - 0.003f, 7.0f), new Vector3(4.0f, Height, 7.95f), grime, 0f, false);
            Prop(root, "Ceiling_Damp1", new Vector3(5.0f, Height - 0.003f, 0.05f), new Vector3(6.5f, Height, 1.6f), grime, 0f, false);
            Prop(root, "Ceiling_Damp2", new Vector3(7.6f, Height - 0.003f, 5.6f), new Vector3(8.95f, Height, 7.0f), grime, 0f, false);
        }

        // ------------------------------------------------------------------ the pistol, the lights, the scare

        static WeaponPickup PlacePistol(Pistol pistol)
        {
            // on the desk's room-side edge: far enough from the sill to be seen through the window from the big room
            var point = new Vector3(SmallX1 - 0.56f, 0.76f, 5.95f);
            var pickup = DarkRoomSceneBuilder.PlaceGunPickup(pistol, WeaponPickup.Kind.Pistol, point, Quaternion.Euler(0f, 112f, 90f));
            pickup.name = "Pickup_Pistol";
            if (pickup.glow)   // the desk lamp lights it: the glow only has to keep it findable
            {
                pickup.glow.intensity = 0.2f;
                pickup.glow.range = 1.3f;
            }
            EditorUtility.SetDirty(pickup);
            return pickup;
        }

        static void Lights(Transform level)
        {
            var lights = Group(level, "Lights");
            Material lampMetal = LightingTestSceneBuilder.SimpleMaterial("LampMetal", new Color(0.05f, 0.05f, 0.05f), 0.7f, 0.35f);
            Material bulb = LightingTestSceneBuilder.EmissiveMaterial("Level_DeskBulb", new Color(1f, 0.75f, 0.45f), 6f);

            // the desk lamp: a warm pool on the desk and the gun, nothing much beyond it
            var lamp = Group(lights, "DeskLamp");
            Vector3 b = new Vector3(SmallX1 - 0.18f, 0.76f, 6.35f);
            Cylinder(lamp, "Base", b + Vector3.up * 0.012f, 0.07f, 0.024f, lampMetal, false);
            var arm = Cylinder(lamp, "Arm", b + new Vector3(-0.06f, 0.2f, -0.04f), 0.008f, 0.42f, lampMetal, false);
            arm.transform.rotation = Quaternion.Euler(-12f, 0f, 22f);
            var shade = Cylinder(lamp, "Shade", b + new Vector3(-0.17f, 0.36f, -0.12f), 0.065f, 0.12f, lampMetal, false);
            shade.transform.rotation = Quaternion.Euler(-25f, 0f, 45f);
            shade.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            var bulbGo = Cylinder(lamp, "Bulb", b + new Vector3(-0.19f, 0.32f, -0.14f), 0.03f, 0.02f, bulb, false);
            var deskLight = new GameObject("Light").AddComponent<Light>();
            deskLight.transform.SetParent(lamp, false);
            deskLight.transform.SetPositionAndRotation(b + new Vector3(-0.19f, 0.32f, -0.14f), Quaternion.LookRotation(new Vector3(-0.5f, -1f, -0.4f)));
            deskLight.type = LightType.Spot;
            deskLight.color = new Color(1f, 0.72f, 0.42f);
            deskLight.intensity = 2.4f;   // the goal of the room: it has to pull the eye, through the door and the window
            deskLight.range = 3.8f;
            deskLight.spotAngle = 95f;
            deskLight.innerSpotAngle = 50f;
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
            coldLight.color = new Color(0.62f, 0.78f, 0.92f);
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
            AmbienceBuilder.Shots(root, "Drip_Small", new Vector3(3.3f, Height - 0.1f, RoomD - 0.6f), 0.35f, new Vector2(1.2f, 4f), 0f, 1f, 10f, AmbienceBuilder.Named("drip_"));
            AmbienceBuilder.Shots(root, "Drip_Big", new Vector3(4.25f, Height - 0.1f, 2.15f), 0.4f, new Vector2(0.9f, 3f), 0f, 1f, 12f, AmbienceBuilder.Named("drip_"));
            AmbienceBuilder.Shots(root, "Creaks", new Vector3(4.5f, Height + 0.5f, 4f), 0.4f, new Vector2(14f, 34f), 6f, 2f, 30f, AmbienceBuilder.Named("creak_"));
        }

        // ------------------------------------------------------------------ shapes

        static Transform Group(Transform parent, string name)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            return t;
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
            const float s = 0.42f, seat = 0.45f;
            Prop(chair, "Seat", new Vector3(-s / 2, seat - 0.03f, -s / 2), new Vector3(s / 2, seat, s / 2), wood);
            Prop(chair, "Back", new Vector3(-s / 2, seat, s / 2 - 0.03f), new Vector3(s / 2, seat + 0.45f, s / 2), wood);
            for (int i = 0; i < 4; i++)
            {
                float x = (i % 2 == 0 ? -1 : 1) * (s / 2 - 0.025f), z = (i < 2 ? -1 : 1) * (s / 2 - 0.025f);
                Prop(chair, "Leg" + i, new Vector3(x - 0.02f, 0f, z - 0.02f), new Vector3(x + 0.02f, seat - 0.03f, z + 0.02f), wood, 0f, false);
            }
            if (fallen)
            {
                chair.localPosition = new Vector3(0f, s / 2 + 0.02f, 0f);
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
