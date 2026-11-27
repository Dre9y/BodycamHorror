using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace BodycamHorror.EditorTools
{
    /// <summary>
    /// Polishes the first part of Assets/Scenes/Level.unity, everything before the padlocked door: the start room, the
    /// low hall, the big and the small room, the passage and the corridor up to the lock.
    /// <list type="bullet">
    /// <item>The block and ball stand-ins (crates, the metal shelf, the bucket and bin, the bags, the mattresses, the
    /// table on its side, the bookshelf of cube books, the window's frame, the rubbish on the floors, the ceiling roses
    /// and cords) give way to models (Blender/tools/level_polish_props.py; BodycamHorror > Set Up Level Polish Props)
    /// and the existing prop prefabs. A stand-in is only hidden: its collider keeps doing its job, and the model put in
    /// its place then gets none.</item>
    /// <item>The flat's rooms get papered (a cream floral paper in the big and small room, an older green stripe in the
    /// start room), parquet, skirting boards and a ceiling cornice; the hall, the passage and the corridor stay the
    /// painted common parts of the block.</item>
    /// <item>Pipes get clamps to the wall and couplings; the ceilings water stains and a hole; the walls torn paper and
    /// mould.</item>
    /// <item>Blood where the grey stains were (the start room, the mattresses) and along the way: drops through the hall
    /// and passage, a drag up the corridor that ends under the locked door, hands on the walls, a splatter.</item>
    /// </list>
    /// Everything new goes under one root, "Level_Polish"; rerunning rebuilds it. Textures: Blender/tools/
    /// level_polish_textures.py. Menu: BodycamHorror > Polish Level (before the lock).
    /// </summary>
    public static class LevelPolish
    {
        const string ScenePath = "Assets/Scenes/Level.unity";
        const string RootName = "Level_Polish";
        const string P = "Assets/Prefabs/Props/";
        const string TexDir = "Assets/Generated/Textures/Level/";
        const string MatDir = "Assets/Generated/Materials/Level/";
        const float CeilingY = 2.7f;

        // the rooms that get paper and parquet: (min x, min z, max x, max z), paper, its repeat in metres
        static readonly (Rect area, string paper)[] Rooms =
        {
            (Rect.MinMaxRect(0f, 0f, 9f, 8f), "Wallpaper_Floral"),          // the big and the small room
            (Rect.MinMaxRect(-2.8f, -6.8f, 1.8f, -3f), "Wallpaper_Stripe"),  // the start room
        };
        const float PaperTile = 1.06f, ParquetTile = 1.2f;

        static Transform root, group;
        static System.Random rng;
        static readonly List<string> log = new List<string>();

        [MenuItem("BodycamHorror/Polish Level (before the lock)")]
        public static void Polish()
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (scene.path != ScenePath) { Debug.LogError("Open " + ScenePath + " first"); return; }
            foreach (var go in scene.GetRootGameObjects()) if (go.name == RootName) Object.DestroyImmediate(go);
            foreach (var t in Children("Level/Lights").Where(t => t && t.name == "LampCord").ToList()) Object.DestroyImmediate(t.gameObject);   // hung on the lamps' pivots
            Physics.SyncTransforms();
            log.Clear();
            rng = new System.Random(1997);
            Materials();
            root = new GameObject(RootName).transform;

            Paper();
            Props();
            Pipes();
            Grime();
            Blood();

            EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log("Level polished (before the lock): " + string.Join("; ", log));
        }

        // ================================================================ materials

        static readonly Dictionary<string, Material> mats = new Dictionary<string, Material>();

        static void Materials()
        {
            Directory.CreateDirectory(MatDir);
            mats.Clear();
            foreach (var n in new[] { "Wallpaper_Floral", "Wallpaper_Stripe", "Parquet" }) mats[n] = Mat(n, false);
            foreach (var n in new[] { "Blood_Pool", "Blood_Drag", "Blood_Drops", "Blood_Splatter", "Blood_Hand", "Wall_Torn",
                                      "Ceiling_Stain", "Ceiling_Hole", "Wall_Mould" })
                mats[n] = Mat(n, true);
            mats["Trim"] = Plain("Trim_Paint", new Color(0.2f, 0.12f, 0.07f), 0.35f, "Parquet");
            mats["Cornice"] = Plain("Cornice_Plaster", new Color(0.62f, 0.6f, 0.55f), 0.1f, null);
            AssetDatabase.SaveAssets();
        }

        static Texture2D Tex(string file, bool srgb, bool normal, bool clamp)
        {
            string path = TexDir + file + ".png";
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            if (!imp) { Debug.LogWarning("Missing " + path); return null; }
            bool dirty = imp.sRGBTexture != srgb || (imp.textureType == TextureImporterType.NormalMap) != normal ||
                         (imp.wrapMode == TextureWrapMode.Clamp) != clamp || imp.alphaIsTransparency != (clamp && srgb);
            if (dirty)
            {
                imp.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
                imp.sRGBTexture = srgb;
                imp.wrapMode = clamp ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
                imp.alphaIsTransparency = clamp && srgb;
                imp.mipmapEnabled = true;
                imp.anisoLevel = 4;
                imp.maxTextureSize = 2048;
                imp.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static Material Mat(string name, bool overlay)
        {
            string path = MatDir + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!m) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, path); }
            var baseMap = Tex(name + "_BaseColor", true, false, overlay);
            m.SetTexture("_BaseMap", baseMap); m.SetTexture("_MainTex", baseMap);
            m.SetColor("_BaseColor", Color.white);
            m.SetTexture("_BumpMap", Tex(name + "_Normal", false, true, overlay)); m.EnableKeyword("_NORMALMAP");
            m.SetFloat("_BumpScale", 1f);
            m.SetTexture("_MetallicGlossMap", Tex(name + "_MetallicSmoothness", false, false, overlay));
            m.EnableKeyword("_METALLICSPECGLOSSMAP");
            m.SetFloat("_Smoothness", 1f);
            m.SetFloat("_SmoothnessTextureChannel", 0f);
            if (overlay) Transparent(m);
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>Alpha-blended, no z-write, and no specular where the alpha is nil (URP keeps it by default).</summary>
        static void Transparent(Material m)
        {
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 0f);
            m.SetFloat("_BlendModePreserveSpecular", 0f);
            m.DisableKeyword("_BLENDMODE_PRESERVE_SPECULAR_LIGHTING");
            m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            m.SetOverrideTag("RenderType", "Transparent");
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            m.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.SetFloat("_ReceiveShadows", 1f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)RenderQueue.Transparent - 50;
            m.SetShaderPassEnabled("ShadowCaster", false);
            m.SetShaderPassEnabled("DepthOnly", false);
        }

        static Material Plain(string name, Color c, float smooth, string normalFrom)
        {
            string path = MatDir + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!m) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, path); }
            m.SetColor("_BaseColor", c);
            m.SetFloat("_Smoothness", smooth);
            m.SetFloat("_Metallic", 0f);
            if (normalFrom != null) { m.SetTexture("_BumpMap", Tex(normalFrom + "_Normal", false, true, false)); m.EnableKeyword("_NORMALMAP"); m.SetFloat("_BumpScale", 0.4f); }
            EditorUtility.SetDirty(m);
            return m;
        }

        // ================================================================ paper, parquet, trim

        static void Paper()
        {
            Group("Interior");
            var shell = Find("Level/Shell");
            var walls = new List<(Transform t, Bounds b)>();
            foreach (Transform w in shell)
            {
                if (!w.name.EndsWith("_Paint") && !w.name.EndsWith("_Plaster")) continue;
                var mf = w.GetComponent<MeshFilter>();
                if (!mf || !mf.sharedMesh) continue;
                walls.Add((w, new Bounds(w.TransformPoint(mf.sharedMesh.bounds.center), Vector3.Scale(mf.sharedMesh.bounds.size, w.lossyScale))));
            }
            int skins = 0, strips = 0;
            var staticFlags = GameObjectUtility.GetStaticEditorFlags(shell.GetChild(0).gameObject);
            foreach (var (area, paper) in Rooms)
            {
                var paperMesh = new MeshBuilder(); var skirting = new MeshBuilder(); var cornice = new MeshBuilder();
                foreach (var (t, b) in walls)
                {
                    foreach (var n in new[] { Vector3.right, Vector3.left, Vector3.forward, Vector3.back })
                    {
                        Vector3 c = b.center + Vector3.Scale(n, b.extents);
                        Vector3 probe = c + n * 0.15f;
                        if (!area.Contains(new Vector2(probe.x, probe.z))) continue;
                        Vector3 along = Vector3.Cross(Vector3.up, n);   // so the face is seen from the room
                        float half = Mathf.Abs(Vector3.Dot(b.extents, new Vector3(Mathf.Abs(along.x), 0f, Mathf.Abs(along.z))));
                        Vector3 a0 = c - along * half; float len = half * 2f;
                        float y0 = b.min.y, y1 = b.max.y;
                        if (y1 <= 0.01f || y0 >= CeilingY - 0.01f) continue;
                        y0 = Mathf.Max(y0, 0f); y1 = Mathf.Min(y1, CeilingY);
                        paperMesh.Quad(a0 + n * 0.003f, along, n, len, y0, y1, PaperTile);
                        skins++;
                        if (y0 < 0.05f && len > 0.05f) { skirting.Strip(a0 + n * 0.003f, along, n, len, 0f, Skirting); strips++; }
                        if (y1 > CeilingY - 0.05f && len > 0.05f) { cornice.Strip(a0 + n * 0.003f, along, n, len, CeilingY, Cornice); strips++; }
                    }
                }
                string tag = paper == "Wallpaper_Floral" ? "Rooms" : "StartRoom";
                MakeMesh($"Paper_{tag}", paperMesh, mats[paper], staticFlags, true);
                MakeMesh($"Skirting_{tag}", skirting, mats["Trim"], staticFlags, true);
                MakeMesh($"Cornice_{tag}", cornice, mats["Cornice"], staticFlags, false);
                var floor = new MeshBuilder();
                floor.Floor(area, 0.002f, ParquetTile);
                MakeMesh($"Parquet_{tag}", floor, mats["Parquet"], staticFlags, false);
            }
            log.Add($"{skins} wall faces papered, {strips} skirting/cornice runs");
        }

        // skirting: (out from the wall, up); cornice: (out, down from the ceiling)
        static readonly Vector2[] Skirting = { new Vector2(0f, 0f), new Vector2(0.02f, 0f), new Vector2(0.02f, 0.058f), new Vector2(0.013f, 0.072f), new Vector2(0f, 0.075f) };
        static readonly Vector2[] Cornice = { new Vector2(0f, 0f), new Vector2(0f, -0.075f), new Vector2(0.008f, -0.068f), new Vector2(0.03f, -0.04f), new Vector2(0.05f, -0.014f), new Vector2(0.058f, 0f) };

        static void MakeMesh(string name, MeshBuilder mb, Material mat, StaticEditorFlags flags, bool shadows)
        {
            if (mb.Empty) return;
            var go = new GameObject(name);
            go.transform.SetParent(group, false);
            go.AddComponent<MeshFilter>().sharedMesh = mb.Build(name);
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            GameObjectUtility.SetStaticEditorFlags(go, flags);
        }

        /// <summary>Builds the paper, floor and trim meshes; they live in the scene (no asset), like ProBuilder's.</summary>
        class MeshBuilder
        {
            readonly List<Vector3> v = new List<Vector3>(); readonly List<Vector3> n = new List<Vector3>();
            readonly List<Vector2> uv = new List<Vector2>(); readonly List<int> tri = new List<int>();
            public bool Empty => v.Count == 0;

            void Face(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 nrm, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud)
            {
                int i = v.Count;
                v.AddRange(new[] { a, b, c, d }); n.AddRange(new[] { nrm, nrm, nrm, nrm }); uv.AddRange(new[] { ua, ub, uc, ud });
                // a b c d go round clockwise seen from the front (the side nrm points to), as Unity wants
                tri.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
            }

            /// <summary>A wall face from a0 along `along` for len, y0..y1, facing n; UV in tiles of `tile` metres
            /// (u from the world position along the wall, so neighbouring faces line up).</summary>
            public void Quad(Vector3 a0, Vector3 along, Vector3 nrm, float len, float y0, float y1, float tile)
            {
                float u0 = Vector3.Dot(a0, along) / tile, u1 = u0 + len / tile;
                Vector3 b0 = a0 + along * len;
                Face(new Vector3(a0.x, y0, a0.z), new Vector3(b0.x, y0, b0.z), new Vector3(b0.x, y1, b0.z), new Vector3(a0.x, y1, a0.z), nrm,
                     new Vector2(u0, y0 / tile), new Vector2(u1, y0 / tile), new Vector2(u1, y1 / tile), new Vector2(u0, y1 / tile));
            }

            public void Floor(Rect r, float y, float tile)
            {
                Face(new Vector3(r.xMin, y, r.yMin), new Vector3(r.xMin, y, r.yMax), new Vector3(r.xMax, y, r.yMax), new Vector3(r.xMax, y, r.yMin), Vector3.up,
                     new Vector2(r.xMin, r.yMin) / tile, new Vector2(r.xMin, r.yMax) / tile, new Vector2(r.xMax, r.yMax) / tile, new Vector2(r.xMax, r.yMin) / tile);
            }

            /// <summary>A moulding: the profile (out, up) swept along the wall at height y, with end caps.</summary>
            public void Strip(Vector3 a0, Vector3 along, Vector3 nrm, float len, float y, Vector2[] prof)
            {
                Vector3 P(Vector2 p, float s) => new Vector3(a0.x, y, a0.z) + along * s + nrm * p.x + Vector3.up * p.y;
                for (int k = 0; k < prof.Length - 1; k++)
                {
                    Vector2 p0 = prof[k], p1 = prof[k + 1];
                    Vector2 d = p1 - p0;
                    Vector3 fn = (nrm * d.y - Vector3.up * d.x).normalized;   // the profile's outward normal
                    if (Vector3.Dot(fn, nrm * (p0.x + p1.x) + Vector3.up * (p0.y + p1.y)) < 0f) fn = -fn;
                    float l0 = 0f, l1 = d.magnitude;
                    Vector3 a = P(p0, 0f), b = P(p0, len), c = P(p1, len), e = P(p1, 0f);
                    // a triangle (A, B, C) faces Cross(B - A, C - A): wind it to face fn
                    if (Vector3.Dot(Vector3.Cross(b - a, e - a), fn) > 0f)
                        Face(a, b, c, e, fn, new Vector2(0f, l0), new Vector2(len, l0), new Vector2(len, l1), new Vector2(0f, l1));
                    else
                        Face(a, e, c, b, fn, new Vector2(0f, l0), new Vector2(0f, l1), new Vector2(len, l1), new Vector2(len, l0));
                }
                foreach (float s in new[] { 0f, len })
                {
                    Vector3 cn = s == 0f ? -along : along;
                    int i = v.Count;
                    foreach (var p in prof) { v.Add(P(p, s)); n.Add(cn); uv.Add(p); }
                    for (int k = 1; k < prof.Length - 1; k++)
                    {
                        Vector3 A = v[i], B = v[i + k], C = v[i + k + 1];
                        if (Vector3.Dot(Vector3.Cross(B - A, C - A), cn) > 0f) tri.AddRange(new[] { i, i + k, i + k + 1 });
                        else tri.AddRange(new[] { i, i + k + 1, i + k });
                    }
                }
            }

            public Mesh Build(string name)
            {
                var m = new Mesh { name = name };
                if (v.Count > 65000) m.indexFormat = IndexFormat.UInt32;
                m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, uv); m.SetTriangles(tri, 0);
                m.RecalculateTangents(); m.RecalculateBounds();
                return m;
            }
        }

        // ================================================================ props

        static void Props()
        {
            Group("Props");
            int before = group.childCount;

            // --- the start room: a storeroom
            const string S = "Level/StartRoom/";
            foreach (var n in new[] { "Post0", "Post1", "Post2", "Post3", "Shelf0", "Shelf1", "Shelf2", "Shelf3", "Box_Empty" }) Hide(S + n);
            var rackA = Put("MetalRack", new Vector3(-2.575f, 0f, -4.66f), 90f, keepCollider: false, colliderOwner: Find(S + "Shelf0"));
            var rackB = Put("MetalRack", new Vector3(-2.575f, 0f, -5.74f), 90f, keepCollider: false, colliderOwner: Find(S + "Shelf0"));
            OnShelf(rackA, "Box_Small", 0.6f, 15f);
            OnShelf(rackB, "Book_Stack", 1.3f, 80f);
            OnShelf(rackB, "Box_Closed", 0.3f, -6f);
            Fit("Crate_Slat", S + "Crate0", 0f, false);
            Fit("Crate_Box", S + "Crate1", 0f, false);
            Fit("Crate_Slat", S + "Crate2", 90f, false);
            Fit("Crate_Broken", S + "Crate_Fallen", 0f, true);   // (its bounds take in the slat lying by it)
            Fit("Bucket", S + "Bucket", 30f, true);

            // --- the big room
            const string B = "Level/BigRoom/";
            Fit("Cushion", B + "Cushion_Fallen", 20f, true);
            foreach (var n in new[] { "Top", "Leg0", "LegT0", "Leg1", "LegT1" }) Hide(B + n);
            Put("CoffeeTable", new Vector3(3.2f, 0.25f, 0.33f), 0f, false, Find(B + "Top"), Quaternion.Euler(90f, 0f, 0f));
            string[] bags = { "TrashBag_A", "TrashBag_B", "TrashBag_C" };
            for (int i = 0; i < 6; i++) Fit(bags[i % 3], B + "Bag" + i, i * 57f, true);
            Hide(B + "Carpet");
            var rug = Put("Rug_Floor", new Vector3(7.2f, 0f, 5.0f), 90f, colliderOwner: Find(B + "Carpet"));
            rug.transform.localScale = Vector3.one * 1.2f;
            Fit("Mattress_Double", B + "Mattress", 6f, false);
            Hide(B + "Mattress_Stain");
            Fit("Plaster_Slab", B + "CeilingChunk", 25f, true, uniformByFootprint: true);
            Hide(B + "CeilingHole");

            // --- the small room
            const string M = "Level/SmallRoom/";
            var shelfBack = Find(M + "Back");
            if (shelfBack)
            {
                foreach (var r in shelfBack.parent.GetComponentsInChildren<MeshRenderer>(true)) r.enabled = false;
                Put("Bookcase", new Vector3(0.6f, 0f, 7.82f), 180f, colliderOwner: shelfBack);
                Put("Book_Stack", new Vector3(1.18f, 0f, 7.75f), 95f, colliderOwner: shelfBack);
            }
            Fit("Paper_Sheet", M + "Paper_Loose", 12f, true, uniformByFootprint: true);
            Swap("Book_Red", M + "Book", 18f);
            Swap("Book_Green", M + "Book_Floor", 40f);
            Swap("Book_Brown", M + "Book_Floor2", -25f);
            Fit("TrashBin", M + "Bin", 0f, true);
            var bits = Enumerable.Range(0, 9).Select(i => Find(M + "Bit" + i)).Where(t => t).ToList();
            if (bits.Count > 0)
            {
                Vector3 c = Vector3.zero; foreach (var t in bits) { c += t.position; Hide(t); }
                c /= bits.Count; c.y = 0f;
                Put("Junk_Pile", c, 30f, colliderOwner: bits[0]);
                Put("Paper_Ball", c + new Vector3(-0.35f, 0f, 0.2f), 0f, colliderOwner: bits[0]);
            }
            Fit("Mattress_Single", M + "Mattress", -4f, false);
            Hide(M + "Mattress_Stain");

            // --- the window between the rooms (the pane and its grime stay: the scare looks through them)
            const string W = "Level/Window/";
            foreach (var n in new[] { "Frame_Bottom", "Frame_Top", "Frame_Left", "Frame_Right", "Mullion", "Transom", "Sill_In", "Sill_Out" }) Hide(W + n);
            var win = Put("WindowFrame", new Vector3(4.95f, 1.5f, 6.05f), 0f, colliderOwner: Find(W + "Frame_Bottom"));
            if (win) win.transform.rotation = Quaternion.LookRotation(Vector3.left);   // its front (the inside sill) into the small room

            // --- the ceiling roses and cords of the hanging lamps (shade and bulb stay)
            // (the cord hangs from the lamp's swinging pivot, so the new one goes on the pivot too)
            foreach (var rose in Children("Level/Lights").Where(t => t.name == "Rose").ToList())
            {
                Hide(rose);
                var pivot = rose.parent.Find("Pivot");
                var cord = pivot ? pivot.Find("Cord") : null;
                if (cord) Hide(cord);
                var lc = Put("LampCord", new Vector3(rose.position.x, CeilingY, rose.position.z), (float)rng.NextDouble() * 360f, colliderOwner: rose);
                if (lc && pivot) lc.transform.SetParent(pivot, true);
            }

            // --- the rubbish on the floors
            int junk = 0;
            foreach (Transform t in Children("Level/Rubbish").ToList())
            {
                string kind = new string(t.name.TakeWhile(char.IsLetter).ToArray());
                double roll = rng.NextDouble();
                string pick = kind switch
                {
                    "Paper" => roll < 0.7 ? "Paper_Sheet" : "Paper_Ball",
                    "Plaster" => roll < 0.5 ? "Plaster_A" : "Plaster_B",
                    "Bottle" => roll < 0.7 ? "Bottle" : "BrokenBottle",
                    "Junk" => roll < 0.45 ? "Can" : roll < 0.8 ? "Rag" : "Plaster_A",
                    _ => null,
                };
                if (pick == null) continue;
                Hide(t);
                Put(pick, new Vector3(t.position.x, 0f, t.position.z), (float)rng.NextDouble() * 360f, colliderOwner: t);
                junk++;
            }
            log.Add($"{group.childCount - before} props placed ({junk} rubbish)");
        }

        // ================================================================ pipes

        static void Pipes()
        {
            Group("Pipework");
            int clamps = 0, collars = 0;
            foreach (var path in new[] { "Level/StartRoom", "Level/BigRoom", "Level/Corridor" })
            {
                foreach (Transform t in Children(path).ToList())
                {
                    if (!(t.name == "Riser" || t.name.StartsWith("Run"))) continue;
                    var mr = t.GetComponent<MeshRenderer>();
                    if (!mr || !mr.enabled || !t.gameObject.activeInHierarchy) continue;
                    Vector3 axis = t.up; float half = t.lossyScale.y, r = t.lossyScale.x * 0.5f;
                    var own = t.GetComponent<Collider>();
                    float len = half * 2f;
                    int n = Mathf.Max(1, Mathf.RoundToInt((len - 0.6f) / 1.3f) + 1);
                    for (int i = 0; i < n; i++)
                    {
                        float s = n == 1 ? 0f : Mathf.Lerp(-half + 0.35f, half - 0.35f, i / (float)(n - 1));
                        Vector3 p = t.position + axis * s;
                        if (p.y > CeilingY - 0.1f) continue;
                        Vector3? wall = NearestSurface(p, axis, own, 0.45f);
                        if (wall == null) continue;
                        var c = Put("PipeClamp", p, 0f, colliderOwner: t);
                        c.transform.rotation = Quaternion.LookRotation(wall.Value, axis) * StemFix(c);
                        c.transform.localScale = Vector3.one * (r / 0.05f);
                        clamps++;
                        if (i < n - 1 || n == 1)
                        {
                            float sc = n == 1 ? half * 0.5f : s + (len - 0.7f) / (n - 1) * 0.5f;
                            var k = Put("PipeCollar", t.position + axis * sc, 0f, colliderOwner: t);
                            k.transform.rotation = Quaternion.FromToRotation(Vector3.up, axis) * Quaternion.Euler(0f, (float)rng.NextDouble() * 60f, 0f);
                            k.transform.localScale = Vector3.one * (r / 0.05f);
                            collars++;
                        }
                    }
                }
            }
            log.Add($"{clamps} pipe clamps, {collars} couplings");
        }

        /// <summary>The direction, square to the pipe, of the nearest wall or ceiling within reach.</summary>
        static Vector3? NearestSurface(Vector3 p, Vector3 axis, Collider own, float reach)
        {
            Vector3 best = Vector3.zero; float bd = reach;
            foreach (var d in new[] { Vector3.right, Vector3.left, Vector3.forward, Vector3.back, Vector3.up })
            {
                if (Mathf.Abs(Vector3.Dot(d, axis)) > 0.5f) continue;
                foreach (var h in Physics.RaycastAll(p, d, reach, ~0, QueryTriggerInteraction.Ignore))
                {
                    if (h.collider == own || h.distance < 0.03f) continue;
                    if (h.collider.GetComponentInParent<CharacterController>()) continue;
                    if (h.distance < bd) { bd = h.distance; best = d; }
                }
            }
            return bd < reach ? best : (Vector3?)null;
        }

        /// <summary>The clamp's stem was modelled towards Blender's -Y; whichever way that came in, turn it to +Z.</summary>
        static Quaternion StemFix(GameObject clamp)
        {
            var mf = clamp.GetComponentInChildren<MeshFilter>();
            Vector3 c = clamp.transform.InverseTransformPoint(mf.transform.TransformPoint(mf.sharedMesh.bounds.center));
            return c.z >= 0f ? Quaternion.identity : Quaternion.Euler(0f, 180f, 0f);
        }

        // ================================================================ grime: stains, a hole, torn paper, mould

        static void Grime()
        {
            Group("Grime");
            int n = 0;
            // the ceilings: water from the flat above
            foreach (var (x, z, size, rot) in new[] { (6.0f, 5.6f, 1.5f, 20f), (2.4f, 1.1f, 1.1f, 70f), (3.4f, 6.9f, 1.2f, 140f), (0.0f, -6.1f, 1.0f, 5f), (8.2f, 1.4f, 0.9f, 200f) })
                n += Surface("Ceiling_Stain", new Vector3(x, 1.8f, z), Vector3.up, size, size, rot, 0.004f) ? 1 : 0;
            var hole = Find("Level/BigRoom/CeilingHole");
            n += Surface("Ceiling_Hole", hole ? new Vector3(hole.position.x, 1.8f, hole.position.z) : new Vector3(4.3f, 1.8f, 2.15f), Vector3.up, 1.0f, 0.9f, 15f, 0.003f) ? 1 : 0;
            // paper torn off the walls
            foreach (var (from, dir, w, h) in new[]
            {
                (new Vector3(2.0f, 1.55f, 1.0f), Vector3.back, 0.9f, 1.1f),
                (new Vector3(8.0f, 0.75f, 3.0f), Vector3.right, 0.7f, 0.8f),
                (new Vector3(2.0f, 2.15f, 7.0f), Vector3.forward, 0.8f, 0.9f),
                (new Vector3(6.2f, 1.9f, 4.6f), Vector3.left, 0.6f, 0.7f),
                (new Vector3(-1.5f, 1.4f, -4.0f), Vector3.forward, 0.8f, 1.0f),
                (new Vector3(0.6f, 0.6f, -6.0f), Vector3.back, 0.6f, 0.6f),
            })
                n += Surface("Wall_Torn", from, dir, w, h, (float)rng.NextDouble() * 30f - 15f, 0.006f) ? 1 : 0;
            // mould up from the floor in the cold corners
            foreach (var (from, dir, w) in new[]
            {
                (new Vector3(8.5f, 0.5f, 7.0f), Vector3.forward, 1.0f),
                (new Vector3(0.6f, 0.5f, 3.3f), Vector3.left, 0.9f),
                (new Vector3(-2.2f, 0.5f, -6.0f), Vector3.back, 1.0f),
                (new Vector3(8.5f, 0.5f, 0.6f), Vector3.right, 0.9f),
            })
            {
                var f = from; f.y = w * 0.5f;
                n += Surface("Wall_Mould", f, dir, w, w, 0f, 0.008f) ? 1 : 0;
            }
            log.Add($"{n} stains, holes and torn patches");
        }

        // ================================================================ blood

        static void Blood()
        {
            Group("Blood");
            int n = 0;
            // the start room: where the grey stain was, a pool; the body was dragged out of the door
            var head = Find("Level/StartRoom/Stain_Head");
            Vector3 pool = head ? head.position : new Vector3(-1.2f, 0f, -5.08f);
            foreach (var s in new[] { "Stain_Head", "Stain_Drop0", "Stain_Drop1" }) Hide("Level/StartRoom/" + s);
            n += Floor("Blood_Pool", pool, 1.25f, 1.1f, 25f, 0.004f);
            n += Drag(pool + new Vector3(0.25f, 0f, 0f), new Vector3(1.85f, 0f, -5.35f), 0.55f, 0.005f);
            n += Floor("Blood_Drops", new Vector3(0.15f, 0f, -4.65f), 0.4f, 0.4f, 10f, 0.006f);
            n += Floor("Blood_Drops", new Vector3(0.6f, 0f, -5.6f), 0.35f, 0.35f, 130f, 0.006f);
            n += Wall("Blood_Hand", new Vector3(1.0f, 1.12f, -4.3f), Vector3.right, 0.24f, 0.24f, -12f);
            n += Wall("Blood_Hand", new Vector3(1.0f, 0.95f, -4.05f), Vector3.right, 0.22f, 0.22f, 8f);
            // the low hall and the passage: drops
            foreach (var (x, z, s) in new[] { (2.7f, -5.05f, 0.45f), (4.1f, -4.8f, 0.4f), (5.6f, -5.1f, 0.45f), (7.3f, -4.9f, 0.4f), (8.8f, -5.0f, 0.45f),
                                              (10.3f, -4.4f, 0.45f), (10.5f, -2.6f, 0.4f), (10.3f, -0.7f, 0.45f), (10.5f, 1.4f, 0.4f), (10.4f, 3.2f, 0.45f) })
                n += Floor("Blood_Drops", new Vector3(x, 0f, z), s, s, (float)rng.NextDouble() * 360f, 0.006f);
            // the corridor: dragged to the locked door, and under it
            n += Drag(new Vector3(10.42f, 0f, 4.4f), new Vector3(10.35f, 0f, 7.4f), 0.5f, 0.005f);
            n += Drag(new Vector3(10.38f, 0f, 10.25f), new Vector3(10.3f, 0f, 7.2f), 0.6f, 0.0055f);
            n += Floor("Blood_Pool", new Vector3(10.45f, 0f, 9.75f), 0.95f, 0.8f, 70f, 0.004f);
            n += Wall("Blood_Hand", new Vector3(10.4f, 1.2f, 9.55f), Vector3.right, 0.24f, 0.24f, 5f);
            n += Wall("Blood_Hand", new Vector3(9.7f, 1.05f, 9.4f), Vector3.forward, 0.22f, 0.22f, -20f);
            n += Wall("Blood_Splatter", new Vector3(10.4f, 1.45f, 8.4f), Vector3.left, 1.1f, 1.1f, 0f);
            // the rooms: the mattresses soaked, a splatter by the window, drops to it
            var big = Find("Level/BigRoom/Mattress"); var small = Find("Level/SmallRoom/Mattress");
            if (big) n += Floor("Blood_Pool", big.position + new Vector3(-0.05f, 0f, -0.1f), 1.0f, 0.9f, 140f, 0.004f, big.position.y + big.lossyScale.y * 0.5f + 0.012f);
            if (small) n += Floor("Blood_Pool", small.position + new Vector3(-0.05f, 0f, -0.15f), 0.7f, 0.75f, 60f, 0.004f, small.position.y + small.lossyScale.y * 0.5f + 0.012f);
            n += Wall("Blood_Splatter", new Vector3(6.0f, 1.35f, 4.85f), Vector3.left, 1.2f, 1.2f, 0f);   // (clear of the wardrobe)
            n += Floor("Blood_Drops", new Vector3(5.45f, 0f, 4.3f), 0.5f, 0.5f, 33f, 0.006f);
            n += Floor("Blood_Drops", new Vector3(5.8f, 0f, 5.3f), 0.4f, 0.4f, 200f, 0.006f);
            n += Floor("Blood_Drops", new Vector3(3.9f, 0f, 6.6f), 0.4f, 0.4f, 300f, 0.006f);
            log.Add($"{n} blood marks");
        }

        static int Drag(Vector3 from, Vector3 to, float width, float lift)
        {
            Vector3 d = to - from; d.y = 0f;
            Vector3 c = (from + to) * 0.5f; c.y = lift;
            var q = Quaternion.LookRotation(Vector3.down, Vector3.Cross(Vector3.down, d.normalized));
            Overlay("Blood_Drag", c, q, d.magnitude, width);
            return 1;
        }

        static int Floor(string mat, Vector3 at, float w, float h, float yaw, float lift, float? onTop = null)
        {
            Vector3 dir = Quaternion.Euler(0f, yaw, 0f) * Vector3.right;
            var q = Quaternion.LookRotation(Vector3.down, Vector3.Cross(Vector3.down, dir));
            Overlay(mat, new Vector3(at.x, (onTop ?? 0f) + lift, at.z), q, w, h);
            return 1;
        }

        static int Wall(string mat, Vector3 from, Vector3 dir, float w, float h, float roll)
            => Surface(mat, from, dir, w, h, roll, 0.007f) ? 1 : 0;

        /// <summary>Finds the wall/ceiling a ray from `from` along `dir` meets and lays the overlay on it.</summary>
        static bool Surface(string mat, Vector3 from, Vector3 dir, float w, float h, float roll, float lift)
        {
            if (!Physics.Raycast(from, dir, out var hit, 4f, ~0, QueryTriggerInteraction.Ignore) || hit.collider.GetComponentInParent<CharacterController>())
            {
                log.Add($"no surface for {mat} at {from}");
                return false;
            }
            Vector3 up = Mathf.Abs(hit.normal.y) > 0.7f ? Vector3.forward : Vector3.up;
            var q = Quaternion.LookRotation(-hit.normal, up) * Quaternion.Euler(0f, 0f, roll);
            // papered walls stand 3 mm proud of the wall the ray meets
            Overlay(mat, hit.point + hit.normal * lift, q, w, h);
            return true;
        }

        static Mesh quad;
        static void Overlay(string mat, Vector3 pos, Quaternion rot, float w, float h)
        {
            if (!quad) quad = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
            var go = new GameObject(mat);
            go.transform.SetParent(group, false);
            go.transform.SetPositionAndRotation(pos, rot);
            go.transform.localScale = new Vector3(w, h, 1f);
            go.AddComponent<MeshFilter>().sharedMesh = quad;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mats[mat];
            r.shadowCastingMode = ShadowCastingMode.Off;
            go.isStatic = true;
        }

        // ================================================================ helpers

        static void Group(string name)
        {
            group = new GameObject(name).transform;
            group.SetParent(root, false);
        }

        /// <summary>By path, or else by its last name anywhere under the rest of the path (the rooms nest their
        /// pieces in groups: StartRoom/Rack/Post0).</summary>
        static Transform Find(string path)
        {
            var go = GameObject.Find(path);
            if (go) return go.transform;
            int i = path.LastIndexOf('/');
            if (i < 0) return null;
            var parent = Find(path.Substring(0, i));
            string name = path.Substring(i + 1);
            return parent ? parent.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name) : null;
        }

        static IEnumerable<Transform> Children(string path)
        {
            var t = Find(path);
            return t ? t.GetComponentsInChildren<Transform>(true).Where(c => c != t) : Enumerable.Empty<Transform>();
        }

        static void Hide(string path) { var t = Find(path); if (t) Hide(t); else log.Add("missing " + path); }
        static void Hide(Transform t) { foreach (var r in t.GetComponentsInChildren<MeshRenderer>(true)) r.enabled = false; }

        static GameObject Inst(string prefab)
        {
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(P + prefab + ".prefab");
            if (!src) { log.Add("no prefab " + prefab); return null; }
            var go = (GameObject)PrefabUtility.InstantiatePrefab(src);
            go.transform.SetParent(group, false);
            return go;
        }

        /// <summary>A prefab at pos, turned yaw about Y (then extra). colliderOwner: the stand-in whose collider
        /// stays, so the model's own are dropped; null keeps the model's.</summary>
        static GameObject Put(string prefab, Vector3 pos, float yaw, bool keepCollider = false, Transform colliderOwner = null, Quaternion? extra = null)
        {
            var go = Inst(prefab);
            if (!go) return null;
            go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f) * (extra ?? Quaternion.identity));
            if (!keepCollider && colliderOwner && colliderOwner.GetComponentInChildren<Collider>(true))
                foreach (var c in go.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
            if (!keepCollider && colliderOwner == null) { /* keeps its collider */ }
            return go;
        }

        /// <summary>Bounds of a prefab instance's meshes in its own space (scale 1).</summary>
        static Bounds LocalBounds(GameObject go)
        {
            var t = go.transform; var keep = (t.position, t.rotation, t.localScale);
            t.SetPositionAndRotation(Vector3.zero, Quaternion.identity); t.localScale = Vector3.one;
            Bounds b = default; bool first = true;
            foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
            {
                if (!mf.sharedMesh) continue;
                var mb = mf.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var p = mf.transform.TransformPoint(new Vector3(i % 2 == 0 ? mb.min.x : mb.max.x, (i / 2) % 2 == 0 ? mb.min.y : mb.max.y, i / 4 == 0 ? mb.min.z : mb.max.z));
                    if (first) { b = new Bounds(p, Vector3.zero); first = false; } else b.Encapsulate(p);
                }
            }
            t.SetPositionAndRotation(keep.position, keep.rotation); t.localScale = keep.localScale;
            return b;
        }

        /// <summary>Puts the prefab where the stand-in block/ball/cylinder at path is, turned like it (plus yaw), scaled
        /// to its size (uniformly: by the height, or the footprint), its bounds' centre on the stand-in's.</summary>
        static GameObject Fit(string prefab, string path, float yaw, bool uniform, bool uniformByFootprint = false)
        {
            var orig = Find(path);
            if (!orig) { log.Add("missing " + path); return null; }
            var mf = orig.GetComponent<MeshFilter>();
            Vector3 size = Vector3.Scale(mf.sharedMesh.bounds.size, orig.lossyScale);
            Vector3 center = orig.TransformPoint(mf.sharedMesh.bounds.center);
            Hide(orig);
            var go = Put(prefab, center, 0f, colliderOwner: orig);
            if (!go) return null;
            var pb = LocalBounds(go);
            bool swap = Mathf.Abs(Mathf.Repeat(yaw, 180f) - 90f) < 1f;
            Vector3 target = swap ? new Vector3(size.z, size.y, size.x) : size;
            Vector3 s = new Vector3(target.x / pb.size.x, target.y / pb.size.y, target.z / pb.size.z);
            if (uniformByFootprint) s = Vector3.one * Mathf.Clamp(Mathf.Sqrt(target.x * target.z / (pb.size.x * pb.size.z)), 0.5f, 2f);
            else if (uniform) s = Vector3.one * Mathf.Clamp(s.y, 0.5f, 2f);
            go.transform.rotation = orig.rotation * Quaternion.Euler(0f, yaw, 0f);
            go.transform.localScale = s;
            Vector3 bottom = center - Vector3.up * size.y * 0.5f;
            Vector3 off = go.transform.rotation * Vector3.Scale(new Vector3(pb.center.x, pb.min.y, pb.center.z), s);
            go.transform.position = new Vector3(center.x, bottom.y, center.z) - off;
            return go;
        }

        /// <summary>A book (or such) where the stand-in was: lying flat, on the stand-in's foot.</summary>
        static void Swap(string prefab, string path, float yaw)
        {
            var orig = Find(path);
            if (!orig) { log.Add("missing " + path); return; }
            var mf = orig.GetComponent<MeshFilter>();
            Vector3 center = orig.TransformPoint(mf.sharedMesh.bounds.center);
            float bottom = center.y - Vector3.Scale(mf.sharedMesh.bounds.size, orig.lossyScale).y * 0.5f;
            Hide(orig);
            var go = Put(prefab, center, yaw, colliderOwner: orig);
            if (!go) return;
            var pb = LocalBounds(go);
            // lay it with its thinnest side down (a book stood on a shelf comes in standing)
            if (pb.size.x < pb.size.y && pb.size.x <= pb.size.z) go.transform.rotation = Quaternion.Euler(0f, yaw, 0f) * Quaternion.Euler(0f, 0f, 90f);
            else if (pb.size.z < pb.size.y) go.transform.rotation = Quaternion.Euler(0f, yaw, 0f) * Quaternion.Euler(90f, 0f, 0f);
            var wb = WorldBounds(go);
            go.transform.position += new Vector3(center.x - wb.center.x, bottom - wb.min.y, center.z - wb.center.z);
        }

        static Bounds WorldBounds(GameObject go)
        {
            Bounds b = default; bool first = true;
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                if (first) { b = r.bounds; first = false; } else b.Encapsulate(r.bounds);
            }
            return b;
        }

        /// <summary>Sets something on the shelf of a rack that is nearest below `height`.</summary>
        static void OnShelf(GameObject rack, string prefab, float height, float yaw)
        {
            if (!rack) return;
            var temp = new List<MeshCollider>();
            foreach (var mf in rack.GetComponentsInChildren<MeshFilter>()) { var mc = mf.gameObject.AddComponent<MeshCollider>(); mc.sharedMesh = mf.sharedMesh; temp.Add(mc); }
            Physics.SyncTransforms();
            Vector3 from = rack.transform.position + Vector3.up * height;
            float y = -1f;
            foreach (var mc in temp)
                if (mc.Raycast(new Ray(from, Vector3.down), out var hit, height + 0.1f)) y = Mathf.Max(y, hit.point.y);
            foreach (var mc in temp) Object.DestroyImmediate(mc);
            if (y < 0f) { log.Add("no shelf on " + rack.name); return; }
            var go = Put(prefab, new Vector3(from.x, y, from.z), rack.transform.eulerAngles.y + yaw, colliderOwner: rack.transform);
            if (!go) return;
            foreach (var c in go.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
            var wb = WorldBounds(go);
            go.transform.position += Vector3.up * (y - wb.min.y);
        }
    }
}
