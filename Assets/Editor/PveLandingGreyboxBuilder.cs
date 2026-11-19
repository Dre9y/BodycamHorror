using System.Collections.Generic;
using BodycamHorror.Environment;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using L = BodycamHorror.EditorTools.LevelSceneBuilder;

namespace BodycamHorror.EditorTools
{
    /// <summary>
    /// The PvE mini level "a Khrushchevka landing", where the old annex room was: the open door at the end of Level's
    /// long corridor (the leg, where the head-banger is) opens onto the bottom of the landing. A landing with a heavy
    /// iron door at its far end (the way out), and four flats round it, their doors numbered in spray paint.
    /// The key hunt: a key cabinet by the iron door holds the key to flat 3; flat 3 leads through a hole knocked in the wall
    /// into flat 4, where the key to flat 1 lies on the desk; flat 4's door opens only from inside, back onto
    /// the landing. Flat 1 (no light) holds the big iron key; a hole knocked through behind a pushed-aside wardrobe leads into
    /// flat 2, whose door also opens only from inside. The iron key opens the exit.
    /// Dressed like the rest of Level: oil paint below, plaster above, tiled floors, the same lamps (one shadowed lamp
    /// per lit area at most), the Blender props, graffiti and litter (no puddles, matte floor: nothing to glint in the flashlight). The keys glow faintly.
    /// Built into the open scene under a root "PvE_Landing" (rebuilding replaces it; Level's own builders call this).
    /// Needs the landing props (BodycamHorror > Set Up Landing Props) and the decor props (Set Up Decor Props).
    /// No enemies yet. Menu: BodycamHorror > Level: Build PvE Landing Greybox.
    ///
    /// Coordinates (m), local to the root: X to the right of the sketch, Z up the sketch, origin at the landing's
    /// inner bottom-left corner. The root is turned round (the sketch's "up" is Level's -Z) and set so the middle of the
    /// landing's bottom edge is the leg's doorway; the leg's bottom wall is the landing's bottom wall as far as it goes.
    /// Props are placed in that local space; their front (+Z of the model) faces where their yaw turns it.
    /// </summary>
    public static class PveLandingGreyboxBuilder
    {
        const string RootName = "PvE_Landing";
        static readonly Quaternion Turn = Quaternion.Euler(0f, 180f, 0f);
        static Vector3 Origin => L.LegDoorwayFarSide - Turn * new Vector3((LandX0 + LandX1) / 2, 0f, 0f);
        /// <summary>Local x up to which the leg's bottom wall already closes the landing's bottom (it runs the other way).</summary>
        static float LegWallEnd => Origin.x - L.LegBottomWallX1;

        const float H = L.Height;      // floor to ceiling
        const float T = 0.3f;          // wall thickness
        const float DoorTop = 2.1f;    // a plain doorway between rooms
        // a flat's front door: the hole is the frame's jambs (x +-0.51, up to 2.12); its casing (+-0.55, to 2.18, z 0.06..0.082)
        // lies on the landing side of the wall and hides the joint
        const float FlatDoorTop = 2.12f, FlatDoorW = 1.02f;

        // inside faces. Rooms are roomier than a real Khrushchevka: a pack of infected needs space to run and be seen
        const float LandX0 = 0f, LandX1 = 4.5f, LandZ1 = 16f;
        const float LeftX0 = -11.3f, RightX1 = 15.8f;            // flats 11 m across
        const float LowZ1 = 7f, HighZ0 = 7.3f;                   // flats 1/4 below (7 m deep), 2/3 above (8.7 m)
        const float LeftSplitX = -5.3f, RightSplitX = 9.8f;      // wall between a flat's two rooms (centre line)
        const float LowDoorZ = 3.6f, HighDoorZ = 10.1f;          // middle of the flats' front doors
        const float ExitX = (LandX0 + LandX1) / 2, ExitW = 1.03f, ExitTop = 2.08f;   // the iron door (frame 1.04 x 2.09: a hair over the hole)
        const float VestibuleZ1 = LandZ1 + T + 2.6f;             // the little space behind the iron door

        // openings along a wall: from, to (along the wall), bottom, top
        struct Hole { public float a, b, y0, y1; public Hole(float a, float b, float y0, float y1) { this.a = a; this.b = b; this.y0 = y0; this.y1 = y1; } }
        static readonly Hole FrontLow = new Hole(LowDoorZ - FlatDoorW / 2, LowDoorZ + FlatDoorW / 2, 0f, FlatDoorTop);
        static readonly Hole FrontHigh = new Hole(HighDoorZ - FlatDoorW / 2, HighDoorZ + FlatDoorW / 2, 0f, FlatDoorTop);
        static readonly Hole InnerLow = new Hole(3.0f, 4.2f, 0f, DoorTop);        // flats 1 and 4, between their rooms
        static readonly Hole InnerHigh = new Hole(12f, 13.2f, 0f, DoorTop);       // flats 2 and 3
        // knocked-through holes, walked through upright (no crouching or vaulting needed)
        static readonly Hole Crawl = new Hole(-9.0f, -7.6f, 0f, 2.0f);            // flats 1-2, behind the pushed-aside wardrobe
        static readonly Hole Sill = new Hole(11.6f, 13.2f, 0f, 2.0f);             // flats 3-4
        static readonly Hole Exit = new Hole(ExitX - ExitW / 2, ExitX + ExitW / 2, 0f, ExitTop);

        // the props (floor-pivoted, front +Z; the wall ones pivoted on the wall, out along +Z)
        const string Props = "Assets/Prefabs/Props/";
        const string Sofa = Props + "Sofa.prefab", Desk = Props + "Desk.prefab", ChairM = Props + "Chair.prefab", Fridge = Props + "Fridge.prefab";
        const string Bookcase = Props + "Bookcase.prefab", MetalRack = Props + "MetalRack.prefab", WallShelf = Props + "WallShelf.prefab";
        const string Poster = Props + "Poster.prefab", Panel = Props + "ElectricPanel.prefab", WallLampM = Props + "WallLamp.prefab";
        const string Mug = Props + "Mug.prefab", Clipboard = Props + "Clipboard.prefab", DeskLamp = Props + "DeskLamp.prefab";
        const string BoxClosed = Props + "Box_Closed.prefab", BoxOpen = Props + "Box_Open.prefab", BoxSmall = Props + "Box_Small.prefab", BoxCrushed = Props + "Box_Crushed.prefab";
        const string BagA = Props + "TrashBag_A.prefab", BagB = Props + "TrashBag_B.prefab", BagC = Props + "TrashBag_C.prefab";
        const string BookStack = Props + "Book_Stack.prefab", BookRow = Props + "Book_Row.prefab", BookOpen = Props + "Book_Open.prefab";
        static string Graffiti(string name) => Props + "Graffiti/Graffiti_" + name + ".prefab";

        static Transform root;
        static GameObject hangingLamp;

        [MenuItem("BodycamHorror/Level: Build PvE Landing Greybox")]
        public static void Build()
        {
            L.LoadMaterials();
            if (!AssetDatabase.LoadAssetAtPath<GameObject>(KeyPropsBuilder.ApartmentDoorPath)) KeyPropsBuilder.SetUp();
            if (!AssetDatabase.LoadAssetAtPath<GameObject>(Bookcase)) DecorPropsSetup.SetUp();
            hangingLamp = LightingTestSceneBuilder.BuildLampPrefab();

            var old = GameObject.Find(RootName);
            if (old) Object.DestroyImmediate(old);
            root = new GameObject(RootName).transform;
            root.SetPositionAndRotation(Origin, Turn);

            var still = new List<Transform>
            {
                BuildShell(Group("Shell")), BuildVestibule(Group("Vestibule")),
                BuildHoles(Group("Holes")), Furnish(Group("Furniture")), Dress(Group("Decor")),
            };
            Doors(Group("Doors"));
            Items(Group("Items"));
            Lights(Group("Lights"));
            Markers(Group("Markers"));
            foreach (Transform g in still)
                foreach (Transform t in g.GetComponentsInChildren<Transform>(true)) t.gameObject.isStatic = true;

            Selection.activeGameObject = root.gameObject;
            EditorSceneManager.MarkSceneDirty(root.gameObject.scene);
            Debug.Log("PvE landing built at " + Origin);
        }

        // ---------------------------------------------------------------- shell: paint below, plaster above, tiles, plaster ceiling

        static Transform BuildShell(Transform p)
        {
            // the bottom edge (z = 0) is the leg's bottom wall's far face: nothing of this level goes past it
            float x0 = LeftX0 - T, x1 = RightX1 + T, z0 = 0f, z1 = LandZ1 + T;

            Slab(p, "Floor", x0, x1, z0, z1, -0.2f, 0f, MatteFloor(), 2f);
            Slab(p, "Ceiling", x0, x1, z0, z1, H, H + 0.2f, L.ceilingMat, 2.5f);

            // walls along Z (x = centre line)
            WallZ(p, "Wall_LeftOuter", LeftX0 - T / 2, z0, z1);
            WallZ(p, "Wall_LandingLeft", LandX0 - T / 2, z0, z1, FrontLow, FrontHigh);
            WallZ(p, "Wall_LandingRight", LandX1 + T / 2, z0, z1, FrontLow, FrontHigh);
            WallZ(p, "Wall_RightOuter", RightX1 + T / 2, z0, z1);
            WallZ(p, "Flat1_Inner", LeftSplitX, 0f, LowZ1, InnerLow);
            WallZ(p, "Flat2_Inner", LeftSplitX, HighZ0, LandZ1, InnerHigh);
            WallZ(p, "Flat4_Inner", RightSplitX, 0f, LowZ1, InnerLow);
            WallZ(p, "Flat3_Inner", RightSplitX, HighZ0, LandZ1, InnerHigh);

            // walls along X (z = centre line); at the bottom, a wall of its own only past the end of the leg's wall
            // (with the leg's open door in it, the way in); the iron door at the top
            WallX(p, "Wall_Bottom", -T / 2, x0, LegWallEnd);
            WallX(p, "Wall_Top", LandZ1 + T / 2, x0, x1, Exit);
            WallX(p, "Wall_Flats1_2", (LowZ1 + HighZ0) / 2, x0 + T, LandX0 - T, Crawl);
            WallX(p, "Wall_Flats3_4", (LowZ1 + HighZ0) / 2, LandX1 + T, x1 - T, Sill);
            return p;
        }

        /// <summary>A wall running along Z at x, from z0 to z1, with openings cut out of it.</summary>
        static void WallZ(Transform p, string name, float x, float z0, float z1, params Hole[] holes)
        {
            var g = Group(name, p);
            foreach (Vector4 s in Spans(z0, z1, holes))
                Wall(g, new Vector3(x - T / 2, s.z, s.x), new Vector3(x + T / 2, s.w, s.y));
        }

        static void WallX(Transform p, string name, float z, float x0, float x1, params Hole[] holes)
        {
            var g = Group(name, p);
            foreach (Vector4 s in Spans(x0, x1, holes))
                Wall(g, new Vector3(s.x, s.z, z - T / 2), new Vector3(s.y, s.w, z + T / 2));
        }

        /// <summary>Pieces of a wall: (from, to, bottom, top). Full height between openings, a sill under and a lintel over each.</summary>
        static IEnumerable<Vector4> Spans(float a, float b, Hole[] holes)
        {
            var sorted = new List<Hole>(holes);
            sorted.Sort((u, v) => u.a.CompareTo(v.a));
            float at = a;
            foreach (Hole o in sorted)
            {
                if (o.a > at) yield return new Vector4(at, o.a, 0f, H);
                if (o.y0 > 0.01f) yield return new Vector4(o.a, o.b, 0f, o.y0);
                if (o.y1 < H - 0.01f) yield return new Vector4(o.a, o.b, o.y1, H);
                at = o.b;
            }
            if (b > at) yield return new Vector4(at, b, 0f, H);
        }

        /// <summary>A piece of wall between two local corners: oil paint up to the panel line, plaster above.</summary>
        static void Wall(Transform p, Vector3 min, Vector3 max)
        {
            if (min.y < L.PanelHeight) TexBox(p, "Paint", min, new Vector3(max.x, Mathf.Min(max.y, L.PanelHeight), max.z), L.wallPaint, LightingTestSceneBuilder.WallTile, LightingTestSceneBuilder.WallTile);
            if (max.y > L.PanelHeight) TexBox(p, "Plaster", new Vector3(min.x, Mathf.Max(min.y, L.PanelHeight), min.z), max, L.wallPlaster, LightingTestSceneBuilder.WallTile, LightingTestSceneBuilder.WallTile);
        }

        static void Slab(Transform p, string name, float x0, float x1, float z0, float z1, float y0, float y1, Material m, float tile) =>
            TexBox(p, name, new Vector3(x0, y0, z0), new Vector3(x1, y1, z1), m, tile, tile);

        /// <summary>Level's tiles without their gloss (Tiles110_Matte): the shine on the floor caught the flashlight everywhere.</summary>
        static Material MatteFloor()
        {
            const string path = "Assets/Materials/Environment/Tiles110_Matte.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!m) { m = new Material(L.floorMat); AssetDatabase.CreateAsset(m, path); }
            m.CopyPropertiesFromMaterial(L.floorMat);
            m.SetFloat("_Smoothness", 0.2f);
            m.SetFloat("_SpecularHighlights", 0f);
            m.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
            m.SetFloat("_EnvironmentReflections", 0f);
            m.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>Level's world-mapped box (its texture runs on across neighbouring pieces) between two local corners.</summary>
        static GameObject TexBox(Transform p, string name, Vector3 localMin, Vector3 localMax, Material m, float tileAcross, float tileUp)
        {
            Vector3 a = p.TransformPoint(localMin), b = p.TransformPoint(localMax);
            var go = LightingTestSceneBuilder.Box(name, p, Vector3.Min(a, b), Vector3.Max(a, b), m, tileAcross, tileUp);
            go.transform.rotation = Quaternion.identity;   // built in world axes (the root is turned round)
            return go;
        }

        // ---------------------------------------------------------------- behind the iron door: a few steps up, and on

        static Transform BuildVestibule(Transform p)
        {
            float x0 = ExitX - 1.6f, x1 = ExitX + 1.6f, z0 = LandZ1 + T, z1 = VestibuleZ1;
            Slab(p, "Floor", x0 - T, x1 + T, z0, z1 + T, -0.2f, 0f, MatteFloor(), 2f);
            Slab(p, "Ceiling", x0 - T, x1 + T, z0, z1 + T, H, H + 0.2f, L.ceilingMat, 2.5f);
            Wall(p, new Vector3(x0 - T, 0f, z0), new Vector3(x0, H, z1 + T));
            Wall(p, new Vector3(x1, 0f, z0), new Vector3(x1 + T, H, z1 + T));
            Wall(p, new Vector3(x0, 0f, z1), new Vector3(x1, H, z1 + T));
            for (int i = 0; i < 4; i++)   // the bottom of a flight going up, out of sight
                TexBox(p, "StepUp" + i, new Vector3(ExitX - 0.3f, 0f, z1 - 0.3f * (4 - i)), new Vector3(ExitX + 1.5f, 0.16f * (i + 1), z1 - 0.3f * (3 - i)), MatteFloor(), 2f, 2f);
            return p;
        }

        // ---------------------------------------------------------------- knocked-through holes

        static Transform BuildHoles(Transform p)
        {
            float zc = (LowZ1 + HighZ0) / 2;
            // broken brick and plaster off to the sides: the way through stays clear
            Rubble(Group("Crawl_Rubble_A", p), new Vector3(Crawl.a - 0.5f, 0f, zc - 0.6f), 6);
            Rubble(Group("Crawl_Rubble_B", p), new Vector3(Crawl.b + 0.4f, 0f, zc + 0.6f), 5);
            Rubble(Group("Sill_Rubble_A", p), new Vector3(Sill.a - 0.4f, 0f, zc - 0.6f), 6);
            Rubble(Group("Sill_Rubble_B", p), new Vector3(Sill.b + 0.5f, 0f, zc + 0.6f), 7);
            // a broken edge still hanging on at the top of the flats 3-4 hole
            Block(p, "Sill_Chunk", new Vector3(Sill.b - 0.18f, Sill.y1 - 0.1f, zc), new Vector3(0.35f, 0.22f, T + 0.04f), L.wallPlaster).localRotation = Quaternion.Euler(0, 0, -15);
            return p;
        }

        static void Rubble(Transform p, Vector3 c, int count)
        {
            var rng = new System.Random(Mathf.RoundToInt(c.x * 100 + c.z * 7));
            for (int i = 0; i < count; i++)
            {
                float s = 0.1f + (float)rng.NextDouble() * 0.2f;
                var pos = c + new Vector3(((float)rng.NextDouble() - 0.5f) * 0.7f, s * 0.35f, ((float)rng.NextDouble() - 0.5f) * 0.7f);
                Material m = i % 2 == 0 ? L.wallPlaster : L.wallPaint;   // plaster and painted bits (nothing metallic: it glints in the flashlight)
                var b = Block(p, "Chunk", pos, new Vector3(s, s * 0.7f, s * 1.2f), m);
                b.localRotation = Quaternion.Euler(rng.Next(-20, 20), rng.Next(0, 90), rng.Next(-20, 20));
            }
        }

        // ---------------------------------------------------------------- furniture: low (trip over) and tall (cover)

        static Transform Furnish(Transform p)
        {
            // flat 2 (top left): a living room (outer) and the kitchen by the landing
            var f2 = Group("Flat2", p);
            Model(Sofa, f2, "Sofa", new Vector3(-10.88f, 0f, 13.1f), 90f);
            Model(Bookcase, f2, "Bookcase", new Vector3(-8.2f, 0f, LandZ1 - 0.19f), 180f);
            Model(Desk, f2, "Table", new Vector3(-8.4f, 0f, 10.3f), 0f);
            Chair(f2, "Chair", new Vector3(-8.5f, 0f, 9.6f), 0f, false);
            Chair(f2, "Chair_Fallen", new Vector3(-7.4f, 0f, 11.2f), 140f, true);
            Bare(Model(BookStack, f2, "Books", new Vector3(-8.7f, 0.76f, 10.4f), 20f));
            Bare(Model(Mug, f2, "Mug", new Vector3(-8.0f, 0.76f, 10.2f), 70f));
            Model(WallShelf, f2, "Shelf", new Vector3(LeftX0, 1.6f, 10.4f), 90f);
            Block(f2, "Rug", new Vector3(-9.4f, 0.004f, 12.8f), new Vector3(2.4f, 0.008f, 3.0f), L.carpet, false);
            Block(f2, "Armchair", new Vector3(-9.6f, 0.22f, 15.3f), new Vector3(0.8f, 0.44f, 0.8f), L.fabric);
            Block(f2, "Armchair_Back", new Vector3(-9.6f, 0.6f, 15.65f), new Vector3(0.8f, 0.5f, 0.12f), L.fabric);
            // kitchen: fridge, a counter with a sink, a table with two chairs, rubbish in the corner
            Model(Fridge, f2, "Fridge", new Vector3(-1.0f, 0f, LandZ1 - 0.32f), 180f);
            Counter(f2, "Counter", -4.9f, -1.6f, LandZ1);
            Block(f2, "KitchenTable", new Vector3(-2.8f, 0.72f, 11.5f), new Vector3(1.1f, 0.04f, 0.8f), L.woodPaint);
            Legs(f2, new Vector3(-2.8f, 0f, 11.5f), 1.1f, 0.8f, 0.72f, L.woodPaint);
            Chair(f2, "KitchenChair", new Vector3(-2.8f, 0f, 10.85f), 0f, false);
            Chair(f2, "KitchenChair2", new Vector3(-2.0f, 0f, 12.1f), -110f, false);
            Model(BagB, f2, "Bag", new Vector3(-4.7f, 0f, 14.6f), 30f);
            Model(BagC, f2, "Bag2", new Vector3(-4.6f, 0f, 14.1f), 100f);

            // flat 1 (bottom left, no light): the wardrobe that hid the hole, pushed aside; boxes, a bed
            var f1 = Group("Flat1", p);
            Bed(f1, "Bed", new Vector3(-11.2f, 0f, 0.1f), new Vector3(-9.2f, 0f, 1.5f));
            Block(f1, "Wardrobe", new Vector3(-10.9f, 1.0f, 4.7f), new Vector3(0.6f, 2.0f, 1.4f), L.woodDark);
            var pushed = Block(f1, "Wardrobe_PushedAside", new Vector3(-9.85f, 1.0f, 6.55f), new Vector3(1.0f, 2.0f, 0.6f), L.woodDark);
            pushed.localRotation = Quaternion.Euler(0f, 12f, 0f);
            Model(BoxClosed, f1, "Box_Key", new Vector3(-7.6f, 0f, 2.9f), 8f);
            Model(BoxSmall, f1, "Box_Small", new Vector3(-7.2f, 0f, 3.6f), -25f);
            Model(BoxOpen, f1, "Box_Open", new Vector3(-8.6f, 0f, 1.2f), 40f);
            Model(BoxCrushed, f1, "Box_Crushed", new Vector3(-6.2f, 0f, 0.6f), 10f);
            Model(MetalRack, f1, "Rack", new Vector3(-3.3f, 0f, 0.22f), 0f);
            Block(f1, "Table", new Vector3(-2.4f, 0.72f, 4.9f), new Vector3(1.2f, 0.04f, 0.8f), L.wood);
            Legs(f1, new Vector3(-2.4f, 0f, 4.9f), 1.2f, 0.8f, 0.72f, L.wood);
            Chair(f1, "Chair_Fallen", new Vector3(-1.4f, 0f, 3.9f), 200f, true);
            Model(BagA, f1, "Bag", new Vector3(-0.7f, 0f, 0.5f), 0f);
            Model(BagB, f1, "Bag2", new Vector3(-1.2f, 0f, 0.4f), 60f);

            // flat 3 (top right): a living room by the landing, a bedroom beyond
            var f3 = Group("Flat3", p);
            Model(Sofa, f3, "Sofa", new Vector3(7.0f, 0f, LandZ1 - 0.42f), 180f);
            Model(Bookcase, f3, "Bookcase", new Vector3(LandX1 + T + 0.19f, 0f, 14.4f), 90f);
            Model(Desk, f3, "Table", new Vector3(7.0f, 0f, 12.4f), 180f);
            Chair(f3, "Chair", new Vector3(7.0f, 0f, 13.1f), 180f, false);
            Bare(Model(BookOpen, f3, "Book", new Vector3(6.8f, 0.76f, 12.4f), -15f));
            Bare(Model(DeskLamp, f3, "DeskLamp", new Vector3(7.4f, 0.76f, 12.25f), 160f));
            Model(Poster, f3, "Poster", new Vector3(8.7f, 1.6f, LandZ1), 180f);
            Block(f3, "Rug", new Vector3(7.0f, 0.004f, 14.0f), new Vector3(2.6f, 0.008f, 1.8f), L.carpet, false);
            Bed(f3, "Bed", new Vector3(13.8f, 0f, 11.5f), new Vector3(15.6f, 0f, 13.0f));
            Block(f3, "Wardrobe", new Vector3(10.38f, 1.0f, 8.8f), new Vector3(0.55f, 2.0f, 1.6f), L.woodDark);
            Model(Desk, f3, "Desk", new Vector3(14.9f, 0f, LandZ1 - 0.42f), 180f);
            Chair(f3, "DeskChair", new Vector3(14.9f, 0f, 15.0f), 180f, false);
            Model(WallShelf, f3, "Shelf", new Vector3(RightX1, 1.65f, 9.6f), -90f);
            Model(BagC, f3, "Bag", new Vector3(10.4f, 0f, 15.6f), 0f);

            // flat 4 (bottom right): a kitchen-diner with the desk the key lies on, a back room with the sofa
            var f4 = Group("Flat4", p);
            Model(Desk, f4, "Desk_Key", new Vector3(7.0f, 0f, 1.6f), 0f);
            Chair(f4, "Chair", new Vector3(7.0f, 0f, 0.95f), 0f, false);
            Bare(Model(Clipboard, f4, "Clipboard", new Vector3(6.5f, 0.76f, 1.7f), 15f));
            Model(Fridge, f4, "Fridge", new Vector3(5.3f, 0f, LowZ1 - 0.32f), 180f);
            Counter(f4, "Counter", 6.0f, 8.6f, LowZ1);
            Model(Sofa, f4, "Sofa", new Vector3(RightX1 - 0.42f, 0f, 1.7f), -90f);
            Block(f4, "Wardrobe", new Vector3(10.38f, 1.0f, 5.8f), new Vector3(0.55f, 2.0f, 1.6f), L.woodDark);
            Model(BoxClosed, f4, "Box", new Vector3(12.5f, 0f, 2.4f), 20f);
            Model(BoxSmall, f4, "Box_Small", new Vector3(12.9f, 0.46f, 2.4f), -10f);
            Model(BagA, f4, "Bag", new Vector3(9.3f, 0f, 0.5f), 45f);

            // landing: an old cupboard, a bench, the electrics, rubbish bags by the bottom
            var land = Group("Landing", p);
            Block(land, "Cupboard", new Vector3(4.15f, 0.6f, 7.1f), new Vector3(0.6f, 1.2f, 1.2f), L.woodPaint);
            Block(land, "Bench_Seat", new Vector3(0.3f, 0.43f, 11.75f), new Vector3(0.38f, 0.05f, 1.5f), L.wood);
            Block(land, "Bench_Base", new Vector3(0.3f, 0.2f, 11.75f), new Vector3(0.3f, 0.4f, 1.3f), L.woodDark);
            Model(Panel, land, "ElectricPanel", new Vector3(LandX1, 1.45f, 12.8f), -90f);
            Model(BagA, land, "Bag", new Vector3(0.35f, 0f, 5.2f), 10f);
            Model(BagB, land, "Bag2", new Vector3(0.45f, 0f, 5.8f), 70f);
            Model(BoxCrushed, land, "Box", new Vector3(3.9f, 0f, 14.7f), -20f);
            return p;
        }

        /// <summary>A kitchen counter along a wall (its back at z = wallZ, from x0 to x1): a painted base, an enamel top, a sink.</summary>
        static void Counter(Transform p, string name, float x0, float x1, float wallZ)
        {
            var g = Group(name, p);
            float d = 0.6f, z = wallZ - d / 2;
            Block(g, "Base", new Vector3((x0 + x1) / 2, 0.43f, z), new Vector3(x1 - x0, 0.86f, d), L.woodPaint);
            Block(g, "Top", new Vector3((x0 + x1) / 2, 0.88f, z - 0.01f), new Vector3(x1 - x0 + 0.02f, 0.04f, d + 0.03f), L.enamel);
            Block(g, "Sink", new Vector3(x0 + 0.6f, 0.9f, z), new Vector3(0.5f, 0.02f, 0.4f), L.rust, false);
        }

        static void Legs(Transform p, Vector3 floorCentre, float w, float d, float h, Material m)
        {
            for (int i = 0; i < 4; i++)
            {
                float x = (i % 2 == 0 ? -1 : 1) * (w / 2 - 0.06f), z = (i < 2 ? -1 : 1) * (d / 2 - 0.06f);
                Block(p, "Leg", floorCentre + new Vector3(x, h / 2, z), new Vector3(0.05f, h, 0.05f), m, false);
            }
            Block(p, "Legs_Collider", floorCentre + new Vector3(0f, h / 2, 0f), new Vector3(w, h, d), m).GetComponent<Renderer>().enabled = false;
        }

        static void Bed(Transform p, string name, Vector3 min, Vector3 max)
        {
            var g = Group(name, p);
            Vector3 c = (min + max) / 2, s = max - min;
            Block(g, "Frame", c + Vector3.up * 0.16f, new Vector3(s.x, 0.32f, s.z), L.woodDark);
            Block(g, "Mattress", c + Vector3.up * 0.4f, new Vector3(s.x - 0.08f, 0.16f, s.z - 0.08f), L.fabric, false);
            Block(g, "Blanket", c + new Vector3(0.3f, 0.5f, 0f), new Vector3(s.x * 0.6f, 0.05f, s.z - 0.04f), L.fabricDark, false).localRotation = Quaternion.Euler(0f, 4f, 0f);
        }

        /// <summary>A plain wooden chair (the Blender one), facing <paramref name="yaw"/>; <paramref name="fallen"/> lays it on its back.</summary>
        static void Chair(Transform p, string name, Vector3 floor, float yaw, bool fallen)
        {
            var c = Model(ChairM, p, name, floor, yaw);
            if (!fallen) return;
            c.transform.localPosition = floor + Vector3.up * 0.22f;
            c.transform.localRotation = Quaternion.Euler(0f, yaw, 0f) * Quaternion.Euler(-90f, 0f, 0f);
        }

        // ---------------------------------------------------------------- decor: graffiti, litter, puddles

        static Transform Dress(Transform p)
        {
            // graffiti on the landing (decals: forward into the wall)
            Decal(p, "NoExit", new Vector3(LandX0, 1.75f, 13.6f), -90f);     // left wall, near the iron door
            Decal(p, "Eye", new Vector3(LandX1, 1.8f, 5.6f), 90f);           // right wall, over the cupboard
            Decal(p, "Tag", new Vector3(LandX0, 1.25f, 1.2f), -90f);         // by the way in
            Decal(p, "Smiley", new Vector3(LandX1, 1.5f, 15.2f), 90f);
            // and inside: a warning in the dark flat, a tag in flat 2's kitchen
            Decal(p, "DontLook", new Vector3(-8.2f, 1.6f, 0f), 180f);        // flat 1, on the wall opposite the hole to flat 2
            Decal(p, "LMM", new Vector3(-3.0f, 1.7f, HighZ0), 180f);         // flat 2's kitchen, over the table

            // litter: paper, cardboard, bits along the walls and in corners; a puddle or two under leaks
            Litter(p, "Litter_Landing_A", new Vector3(0.6f, 0f, 2.4f), 0.6f, 9, 11);
            Litter(p, "Litter_Landing_B", new Vector3(3.8f, 0f, 9.5f), 0.7f, 10, 12);
            Litter(p, "Litter_Landing_C", new Vector3(1.0f, 0f, 14.6f), 0.5f, 7, 13);
            Litter(p, "Litter_F1", new Vector3(-6.8f, 0f, 4.8f), 0.9f, 12, 14);
            Litter(p, "Litter_F2", new Vector3(-3.6f, 0f, 13.8f), 0.6f, 8, 15);
            Litter(p, "Litter_F3", new Vector3(12.0f, 0f, 9.0f), 0.8f, 10, 16);
            Litter(p, "Litter_F4", new Vector3(8.4f, 0f, 3.8f), 0.7f, 9, 17);
            // no puddles here: their mirror shine kept catching the flashlight
            return p;
        }

        static void Decal(Transform p, string name, Vector3 wallPoint, float yaw)
        {
            var d = Place(Graffiti(name), p, "Graffiti_" + name, wallPoint, Quaternion.Euler(0f, yaw, 0f));
            d.isStatic = true;
        }

        static void Litter(Transform p, string name, Vector3 at, float radius, int count, int seed)
        {
            var rng = new System.Random(seed);
            var g = Group(name, p);
            for (int i = 0; i < count; i++)
            {
                float a = (float)rng.NextDouble() * Mathf.PI * 2f, r = Mathf.Sqrt((float)rng.NextDouble()) * radius;
                float s = 0.05f + (float)rng.NextDouble() * 0.12f;
                Material m = i % 3 == 0 ? L.paper : i % 3 == 1 ? L.rubbish : L.cardboard;
                var b = Block(g, "Bit", at + new Vector3(Mathf.Cos(a) * r, s * 0.2f, Mathf.Sin(a) * r), new Vector3(s * 2f, s * 0.4f, s * 1.6f), m, false);
                b.localRotation = Quaternion.Euler((float)rng.NextDouble() * 20f, (float)rng.NextDouble() * 360f, (float)rng.NextDouble() * 20f);
            }
        }

        // ---------------------------------------------------------------- doors

        static void Doors(Transform p)
        {
            // flat doors, the front (+Z of the model) to the landing: the frame is centred on the model's X (the bell
            // push beside it is not part of it), its casing's back (z = 0.06) on the landing face of the wall
            const float casingBack = 0.06f;
            var left = Quaternion.Euler(0f, 90f, 0f);    // front to +X
            var right = Quaternion.Euler(0f, -90f, 0f);  // front to -X
            Door d1 = FlatDoor(p, 1, new Vector3(LandX0 - casingBack, 0f, LowDoorZ), left);
            Door d2 = FlatDoor(p, 2, new Vector3(LandX0 - casingBack, 0f, HighDoorZ), left);
            Door d3 = FlatDoor(p, 3, new Vector3(LandX1 + casingBack, 0f, HighDoorZ), right);
            Door d4 = FlatDoor(p, 4, new Vector3(LandX1 + casingBack, 0f, LowDoorZ), right);

            Lock(d1, "flat1");
            Lock(d3, "flat3");
            Latch(d2, new Vector3(-2.5f, 1f, HighDoorZ));   // opens only from inside the flat
            Latch(d4, new Vector3(7f, 1f, LowDoorZ));

            // the way out: the iron door at the landing's end, its front to the landing
            var exit = Place(KeyPropsBuilder.HeavyDoorPath, p, "Door_Exit", new Vector3(ExitX, 0f, LandZ1 + 0.06f), Quaternion.Euler(0f, 180f, 0f));
            Door de = exit.GetComponentInChildren<Door>();
            Lock(de, "exit");
        }

        static Door FlatDoor(Transform p, int n, Vector3 pos, Quaternion rot)
        {
            var go = Place(KeyPropsBuilder.ApartmentDoorPath, p, "Door_Flat" + n, pos, rot);
            Door door = go.GetComponentInChildren<Door>();
            // the flat's number sprayed on the leaf (it swings with it), forward into the door
            var decal = Place(KeyPropsBuilder.NumberDecalPath(n), door.leaf, "Number", Vector3.zero, Quaternion.identity);
            decal.transform.localPosition = new Vector3(KeyPropsBuilder.FlatLeafWidth * 0.5f, 1.5f, KeyPropsBuilder.FlatLeafFront + 0.13f);
            decal.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            decal.isStatic = false;
            return door;
        }

        static void Lock(Door d, string key)
        {
            d.locked = true;
            d.keyId = key;
            d.enemiesCanOpen = d.enemiesCanOpen && key != "exit";
            EditorUtility.SetDirty(d);
        }

        /// <summary>Bolted on the side where <paramref name="inside"/> (local to the root) is.</summary>
        static void Latch(Door d, Vector3 inside)
        {
            bool front = d.transform.InverseTransformPoint(root.TransformPoint(inside)).z >= 0f;
            d.latch = front ? Door.Latch.FrontOnly : Door.Latch.BackOnly;
            EditorUtility.SetDirty(d);
        }

        // ---------------------------------------------------------------- the keys

        static void Items(Transform p)
        {
            // the key cabinet on the end wall, left of the iron door it is the start of the way to; the key to flat 3 on
            // hook 3 (its door swings open away from the iron door)
            var cabinet = Place(KeyPropsBuilder.CabinetPath, p, "KeyCabinet", new Vector3(ExitX - 1.25f, 1.45f, LandZ1), Quaternion.Euler(0f, 180f, 0f));
            foreach (var t in cabinet.GetComponentsInChildren<Transform>(true)) t.gameObject.isStatic = true;
            var key3 = Place(KeyPropsBuilder.FlatKeyPath(3), p, "Key_Flat3", Vector3.zero, Quaternion.identity);
            key3.transform.SetPositionAndRotation(cabinet.transform.TransformPoint(KeyPropsBuilder.CabinetHook3), cabinet.transform.rotation);

            // the key to flat 1, left on flat 4's desk (top 0.76); the iron key on the closed box (top 0.46) in dark
            // flat 1, by the hole to flat 2
            Place(KeyPropsBuilder.FlatKeyPath(1), p, "Key_Flat1", new Vector3(7.15f, 0.76f + 0.012f, 1.45f), Quaternion.Euler(-90f, 25f, 0f));
            Place(KeyPropsBuilder.IronKeyPath, p, "Key_Exit", new Vector3(-7.5f, 0.46f + 0.011f, 2.8f), Quaternion.Euler(-90f, -70f, 0f));
        }

        // ---------------------------------------------------------------- lights: Level's lamps; flat 1 stays dark

        static void Lights(Transform p)
        {
            // the landing: a bulb near the way in (the one shadowed lamp out here), a failing one halfway, and a
            // bulkhead lamp over the key cabinet by the iron door, so the goal is the brightest thing at the far end
            Hanging(p, "Lamp_Landing_In", new Vector3(ExitX, H, 3.2f), 0.5f, false);
            Hanging(p, "Lamp_Landing_Mid", new Vector3(ExitX, H, 9.6f), 0.3f, true);
            WallLamp(p, "WallLamp_Cabinet", new Vector3(ExitX - 1.25f, 2.1f, LandZ1), 180f, 0.9f, false);
            WallLamp(p, "WallLamp_Exit", new Vector3(ExitX + 1.2f, 2.1f, LandZ1), 180f, 0.4f, true);
            // flats: one weak bulb a room, a couple failing; flat 4's back room shadowed, over the key's desk
            Hanging(p, "Lamp_F2_Room", new Vector3(-8.4f, H, 12.0f), 0.3f, false);
            Hanging(p, "Lamp_F2_Kitchen", new Vector3(-2.7f, H, 12.6f), 0.25f, true);
            Hanging(p, "Lamp_F3_Room", new Vector3(7.2f, H, 12.2f), 0.3f, false);
            Hanging(p, "Lamp_F3_Bedroom", new Vector3(12.9f, H, 12.0f), 0.2f, true);
            Hanging(p, "Lamp_F4_Room", new Vector3(7.0f, H, 2.4f), 0.45f, false);
            Hanging(p, "Lamp_F4_Back", new Vector3(12.9f, H, 3.5f), 0.2f, false);
            WallLamp(p, "WallLamp_Vestibule", new Vector3(ExitX - 1.6f, 2.0f, VestibuleZ1 - 1.2f), 90f, 0.3f, true);
        }

        static void Hanging(Transform p, string name, Vector3 ceiling, float dim, bool flicker)
        {
            LightingTestSceneBuilder.PlaceLamp(hangingLamp, p, name, p.TransformPoint(ceiling), false);
            Transform lamp = p.Find(name);
            Light main = null;
            foreach (Light l in lamp.GetComponentsInChildren<Light>(true))
            {
                l.intensity *= dim;
                if (l.name == "Light") main = l;
                if (dim < 0.45f) l.shadows = LightShadows.None;   // shadows from the brighter lamps only (each costs a pass)
            }
            if (flicker && main) Flicker(main, lamp.GetComponentsInChildren<Renderer>(true), "Bulb");
        }

        static void WallLamp(Transform p, string name, Vector3 wallPoint, float yaw, float intensity, bool flicker)
        {
            var lamp = Model(WallLampM, p, name, wallPoint, yaw);
            Light spot = lamp.transform.Find("Light").GetComponent<Light>();
            Light wash = lamp.transform.Find("WallWash").GetComponent<Light>();
            wash.intensity *= intensity / spot.intensity;
            spot.intensity = intensity;
            spot.shadows = LightShadows.None;
            wash.shadows = LightShadows.None;
            if (flicker) Flicker(spot, lamp.GetComponentsInChildren<Renderer>(true), "_Glass");
        }

        static void Flicker(Light light, Renderer[] renderers, string glowName)
        {
            var f = light.gameObject.AddComponent<LightFlicker>();
            f.kind = LightFlicker.Kind.Fluorescent;
            f.depth = 0.6f;
            f.pace = new Vector2(3f, 10f);
            foreach (Renderer r in renderers) if (r.name == glowName || r.name.EndsWith(glowName)) f.glow = r;
        }

        // ---------------------------------------------------------------- markers (empties, for what comes later)

        static void Markers(Transform p)
        {
            // the wave comes in the way the player did: down the leg (a point in it, a third of the way from its start)
            Vector3 leg = L.LegDoorwayFarSide;
            Marker(p, "WaveSpawn_Leg", root.InverseTransformPoint(new Vector3(leg.x - 16f, 0f, leg.z + 1.0f)));
        }

        static void Marker(Transform p, string name, Vector3 pos)
        {
            var go = new GameObject("Marker_" + name);
            go.transform.SetParent(p, false);
            go.transform.localPosition = pos;
        }

        // ---------------------------------------------------------------- helpers

        static Transform Group(string name, Transform parent = null)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent ? parent : root, false);
            return t;
        }

        static GameObject Place(string path, Transform parent, string name, Vector3 localPos, Quaternion localRot)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (!prefab) throw new System.IO.FileNotFoundException("Missing prefab (run BodycamHorror > Set Up Landing Props / Set Up Decor Props)", path);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            go.name = name;
            go.transform.localPosition = localPos;
            go.transform.localRotation = localRot;
            return go;
        }

        /// <summary>A prop at a local point, its front turned <paramref name="yaw"/> degrees from the sketch's up.</summary>
        static GameObject Model(string path, Transform parent, string name, Vector3 localPos, float yaw) =>
            Place(path, parent, name, localPos, Quaternion.Euler(0f, yaw, 0f));

        /// <summary>Takes the colliders off a small thing lying on a surface.</summary>
        static GameObject Bare(GameObject go)
        {
            foreach (Collider c in go.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
            return go;
        }

        /// <summary>A plain cube (furniture parts, rubble): centre and size in the parent's space.</summary>
        static Transform Block(Transform parent, string name, Vector3 centre, Vector3 size, Material mat, bool collider = true)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            var t = go.transform;
            t.SetParent(parent, false);
            t.localPosition = centre;
            t.localScale = size;
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            if (!collider) Object.DestroyImmediate(go.GetComponent<Collider>());
            return t;
        }
    }
}
