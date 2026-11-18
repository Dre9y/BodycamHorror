using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace BodycamHorror.EditorTools
{
    /// <summary>
    /// Generates two small props in code, meshes and textures and all: a closed hardcover book (cloth boards
    /// with worn corners and a printed band, a rounded spine, the page block with its edge lines) and a
    /// newspaper folded once (a sheet with a soft wave and a lifted corner, printed both sides: masthead,
    /// columns of text, a photo, a fold crease, damp stains). Saved as prefabs under Assets/Prefabs/Props.
    /// Menu: BodycamHorror > Build Paper Props (Book, Newspaper). Rebuilding writes over them.
    /// </summary>
    public static class PaperPropsBuilder
    {
        const string PrefabDir = "Assets/Prefabs/Props";
        const string MeshDir = "Assets/Prefabs/Props/Meshes";
        const string MaterialDir = "Assets/Materials/Props";
        const string TextureDir = "Assets/Textures/Props";

        [MenuItem("BodycamHorror/Build Paper Props (Book, Newspaper)")]
        public static void Build()
        {
            foreach (string dir in new[] { PrefabDir, MeshDir, MaterialDir, TextureDir }) Directory.CreateDirectory(dir);

            BuildBook("Book_Red", new Color(0.36f, 0.08f, 0.06f), 11);
            BuildBook("Book_Green", new Color(0.12f, 0.2f, 0.13f), 23);
            BuildBook("Book_Brown", new Color(0.25f, 0.15f, 0.08f), 37);
            BuildNewspaper("Newspaper", 5);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("Paper props built in " + PrefabDir);
        }

        // ------------------------------------------------------------------ book

        const float BookW = 0.15f, BookD = 0.215f, BookT = 0.034f;   // across, along the spine, thick
        const float Board = 0.0028f, Overhang = 0.003f, SpineBulge = 0.006f;

        static void BuildBook(string name, Color cloth, int seed)
        {
            Texture2D coverTex = SaveTexture(CoverTexture(cloth, seed), name + "_Cover");
            Texture2D pagesTex = SaveTexture(PagesTexture(seed), "Book_Pages");
            Material cover = TexturedMaterial(name + "_Cover", coverTex, 0.18f);
            Material pages = TexturedMaterial("Book_Pages", pagesTex, 0.08f);

            var coverMesh = new Builder();
            var pagesMesh = new Builder();
            float hw = BookW * 0.5f, hd = BookD * 0.5f;
            float spineX = -hw;                              // the spine side
            // boards: bottom (back) and top (front), a touch bigger than the block
            float pagesTop = BookT - Board, pagesBottom = Board;
            // cover atlas: u 0..0.5 plain cloth (back, edges), 0.5..0.75 spine, 0.75..1 front design; v along the spine
            BoardBox(coverMesh, new Vector3(spineX, 0f, -hd - Overhang), new Vector3(hw + Overhang, Board, hd + Overhang), false);
            BoardBox(coverMesh, new Vector3(spineX, pagesTop, -hd - Overhang), new Vector3(hw + Overhang, BookT, hd + Overhang), true);
            // the spine: a rounded strip round the block's back edge, from the bottom board to the top one
            const int arc = 10;
            for (int i = 0; i < arc; i++)
            {
                float a0 = Mathf.PI * (0.5f + i / (float)arc), a1 = Mathf.PI * (0.5f + (i + 1) / (float)arc);
                Vector3 c = new Vector3(spineX, BookT * 0.5f, 0f);
                Vector3 r0 = new Vector3(Mathf.Cos(a0) * SpineBulge, Mathf.Sin(a0) * BookT * 0.5f, 0f);
                Vector3 r1 = new Vector3(Mathf.Cos(a1) * SpineBulge, Mathf.Sin(a1) * BookT * 0.5f, 0f);
                Vector3 n0 = new Vector3(Mathf.Cos(a0) / SpineBulge, Mathf.Sin(a0) / (BookT * 0.5f), 0f).normalized;
                Vector3 n1 = new Vector3(Mathf.Cos(a1) / SpineBulge, Mathf.Sin(a1) / (BookT * 0.5f), 0f).normalized;
                float u0 = Mathf.Lerp(0.75f, 0.5f, i / (float)arc), u1 = Mathf.Lerp(0.75f, 0.5f, (i + 1) / (float)arc);
                float z0 = -hd - Overhang, z1 = hd + Overhang;
                coverMesh.Face(c + r0 + Vector3.forward * z0, c + r1 + Vector3.forward * z0, c + r1 + Vector3.forward * z1, c + r0 + Vector3.forward * z1,
                               n0, n1, n1, n0, new Vector2(u0, 0f), new Vector2(u1, 0f), new Vector2(u1, 1f), new Vector2(u0, 1f));
                // the spine's ends, closing the gap at head and tail
                coverMesh.Tri(c + Vector3.forward * z0, c + r1 + Vector3.forward * z0, c + r0 + Vector3.forward * z0, Vector3.back, new Vector2(0.1f, 0.1f));
                coverMesh.Tri(c + Vector3.forward * z1, c + r0 + Vector3.forward * z1, c + r1 + Vector3.forward * z1, Vector3.forward, new Vector2(0.1f, 0.1f));
            }
            // the page block, a little inset; its fore edge bows in slightly opposite the rounded spine
            float px0 = spineX + 0.002f, px1 = hw - 0.001f, pz0 = -hd + 0.0015f, pz1 = hd - 0.0015f;
            PagesBlock(pagesMesh, px0, px1, pagesBottom, pagesTop, pz0, pz1);

            Mesh mesh = Builder.Combine(name, coverMesh, pagesMesh);
            mesh = SaveMesh(mesh, MeshDir + "/" + name + ".asset");
            SavePrefab(name, mesh, new[] { cover, pages }, new Vector3(BookW + Overhang + SpineBulge, BookT, BookD + 2f * Overhang),
                       new Vector3((Overhang - SpineBulge) * 0.5f, BookT * 0.5f, 0f));
        }

        /// <summary>A board: a thin box. The top board's upper face carries the front design, everything else plain cloth.</summary>
        static void BoardBox(Builder b, Vector3 min, Vector3 max, bool front)
        {
            float w = max.x - min.x, d = max.z - min.z;
            Vector2 Plain(float x, float z) => new Vector2(0.02f + 0.46f * Mathf.InverseLerp(min.x, max.x, x), Mathf.InverseLerp(min.z, max.z, z));
            Vector2 Front(float x, float z) => new Vector2(0.75f + 0.25f * Mathf.InverseLerp(min.x, max.x, x), Mathf.InverseLerp(min.z, max.z, z));
            // top and bottom faces
            Vector3 a = new Vector3(min.x, max.y, min.z), bq = new Vector3(max.x, max.y, min.z), c = new Vector3(max.x, max.y, max.z), dq = new Vector3(min.x, max.y, max.z);
            System.Func<float, float, Vector2> top = front ? Front : Plain;
            b.Quad(a, dq, c, bq, Vector3.up, top(a.x, a.z), top(dq.x, dq.z), top(c.x, c.z), top(bq.x, bq.z));
            Vector3 a2 = new Vector3(min.x, min.y, min.z), b2 = new Vector3(max.x, min.y, min.z), c2 = new Vector3(max.x, min.y, max.z), d2 = new Vector3(min.x, min.y, max.z);
            b.Quad(a2, b2, c2, d2, Vector3.down, Plain(a2.x, a2.z), Plain(b2.x, b2.z), Plain(c2.x, c2.z), Plain(d2.x, d2.z));
            // the board's edges (not the spine side, the spine covers it)
            b.Quad(new Vector3(max.x, min.y, min.z), new Vector3(max.x, max.y, min.z), new Vector3(max.x, max.y, max.z), new Vector3(max.x, min.y, max.z), Vector3.right,
                   new Vector2(0.01f, 0f), new Vector2(0.03f, 0f), new Vector2(0.03f, 1f), new Vector2(0.01f, 1f));
            b.Quad(new Vector3(min.x, min.y, min.z), new Vector3(max.x, min.y, min.z), new Vector3(max.x, max.y, min.z), new Vector3(min.x, max.y, min.z), Vector3.back,
                   new Vector2(0.02f, 0.01f), new Vector2(0.48f, 0.01f), new Vector2(0.48f, 0.03f), new Vector2(0.02f, 0.03f));
            b.Quad(new Vector3(min.x, min.y, max.z), new Vector3(min.x, max.y, max.z), new Vector3(max.x, max.y, max.z), new Vector3(max.x, min.y, max.z), Vector3.forward,
                   new Vector2(0.02f, 0.97f), new Vector2(0.02f, 0.99f), new Vector2(0.48f, 0.99f), new Vector2(0.48f, 0.97f));
        }

        /// <summary>The page block: its three open edges show the stacked page lines (v across the thickness).</summary>
        static void PagesBlock(Builder b, float x0, float x1, float y0, float y1, float z0, float z1)
        {
            const float bow = 0.0025f;   // the fore edge curves in a little, like a real rounded book
            const int steps = 8;
            for (int i = 0; i < steps; i++)
            {
                float t0 = i / (float)steps, t1 = (i + 1) / (float)steps;
                float s0 = Mathf.Sin(t0 * Mathf.PI) * bow, s1 = Mathf.Sin(t1 * Mathf.PI) * bow;
                float za = Mathf.Lerp(z0, z1, t0), zb = Mathf.Lerp(z0, z1, t1);
                // fore edge
                b.Quad(new Vector3(x1 - s0, y0, za), new Vector3(x1 - s0, y1, za), new Vector3(x1 - s1, y1, zb), new Vector3(x1 - s1, y0, zb), Vector3.right,
                       new Vector2(t0, 0f), new Vector2(t0, 1f), new Vector2(t1, 1f), new Vector2(t1, 0f));
            }
            // head and tail
            b.Quad(new Vector3(x0, y0, z0), new Vector3(x1, y0, z0), new Vector3(x1, y1, z0), new Vector3(x0, y1, z0), Vector3.back,
                   new Vector2(0f, 0f), new Vector2(0.7f, 0f), new Vector2(0.7f, 1f), new Vector2(0f, 1f));
            b.Quad(new Vector3(x0, y0, z1), new Vector3(x0, y1, z1), new Vector3(x1, y1, z1), new Vector3(x1, y0, z1), Vector3.forward,
                   new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0.7f, 1f), new Vector2(0.7f, 0f));
        }

        static Texture2D CoverTexture(Color cloth, int seed)
        {
            const int w = 512, h = 512;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, true);
            var rng = new System.Random(seed);
            float ox = (float)rng.NextDouble() * 100f, oy = (float)rng.NextDouble() * 100f;
            Color gold = new Color(0.62f, 0.5f, 0.28f);
            var px = new Color[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float u = x / (float)w, v = y / (float)h;
                    // woven cloth: a fine cross-hatch and a slow blotchy fade
                    float weave = 0.93f + 0.07f * Mathf.Sin(x * 1.9f) * Mathf.Sin(y * 1.9f);
                    float blotch = 0.8f + 0.35f * Mathf.PerlinNoise(ox + u * 6f, oy + v * 6f);
                    Color c = cloth * weave * blotch;
                    // worn and faded towards the edges and corners of every region
                    float lu = u < 0.5f ? u / 0.5f : u < 0.75f ? (u - 0.5f) / 0.25f : (u - 0.75f) / 0.25f;
                    float edge = Mathf.Min(Mathf.Min(lu, 1f - lu) * 6f, Mathf.Min(v, 1f - v) * 10f);
                    float wear = Mathf.Clamp01(1f - edge) * (0.6f + 0.4f * Mathf.PerlinNoise(ox + x * 0.08f, oy + y * 0.08f));
                    c = Color.Lerp(c, new Color(0.45f, 0.4f, 0.33f), wear * 0.55f);
                    // the spine: two gilt bands and a title block
                    if (u >= 0.5f && u < 0.75f)
                    {
                        bool band = (v > 0.08f && v < 0.1f) || (v > 0.9f && v < 0.92f);
                        bool title = v > 0.62f && v < 0.82f && lu > 0.25f && lu < 0.75f && ((y / 6) % 2 == 0) && ((y / 12 * 7 + x / 5) % 4 != 0);   // stamped letters
                        if (band || title) c = Color.Lerp(c, gold, 0.75f);
                    }
                    // the front: a pressed frame and a title plate
                    if (u >= 0.75f)
                    {
                        float fx = (u - 0.75f) / 0.25f;
                        bool frame = (Mathf.Abs(fx - 0.08f) < 0.006f || Mathf.Abs(fx - 0.92f) < 0.006f) && v > 0.06f && v < 0.94f
                                  || (Mathf.Abs(v - 0.06f) < 0.004f || Mathf.Abs(v - 0.94f) < 0.004f) && fx > 0.08f && fx < 0.92f;
                        bool plate = fx > 0.22f && fx < 0.78f && v > 0.66f && v < 0.8f;
                        if (frame) c *= 0.7f;
                        if (plate)
                        {
                            c = Color.Lerp(c, gold, 0.6f);
                            bool letters = v > 0.7f && v < 0.76f && fx > 0.28f && fx < 0.72f && ((x / 5) % 3 != 0);
                            if (letters) c *= 0.55f;
                        }
                    }
                    c.a = 1f;
                    px[y * w + x] = c;
                }
            tex.SetPixels(px);
            tex.Apply();
            return tex;
        }

        static Texture2D PagesTexture(int seed)
        {
            const int w = 256, h = 128;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, true);
            var rng = new System.Random(seed * 7 + 1);
            float[] rows = new float[h];
            for (int y = 0; y < h; y++) rows[y] = 0.88f + (float)rng.NextDouble() * 0.12f;
            var px = new Color[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float yellow = Mathf.PerlinNoise(x * 0.03f, y * 0.05f + seed) * 0.12f;   // old paper going yellow at the edges
                    Color c = new Color(0.86f, 0.82f, 0.7f) * rows[y] * (1f - yellow);
                    c.a = 1f;
                    px[y * w + x] = c;
                }
            tex.SetPixels(px);
            tex.Apply();
            return tex;
        }

        // ------------------------------------------------------------------ newspaper

        const float PaperW = 0.29f, PaperD = 0.41f;   // folded once: a half sheet

        static void BuildNewspaper(string name, int seed)
        {
            Texture2D tex = SaveTexture(NewspaperTexture(seed), name + "_Page");
            Material mat = TexturedMaterial(name, tex, 0.05f);
            mat.SetFloat("_Cull", 0f);   // the sheet is seen from both sides
            mat.doubleSidedGI = true;

            var b = new Builder();
            const int nx = 14, nz = 18;
            float hw = PaperW * 0.5f, hd = PaperD * 0.5f;
            var rng = new System.Random(seed);
            float phase = (float)rng.NextDouble() * 6f;
            Vector3 P(int i, int j)
            {
                float u = i / (float)nx, v = j / (float)nz;
                float x = Mathf.Lerp(-hw, hw, u), z = Mathf.Lerp(-hd, hd, v);
                // a soft wave across the sheet, the fold edge (x = -hw) puffed up a little, one corner lifted
                float y = 0.0035f * (0.5f + 0.5f * Mathf.Sin(v * 5f + phase)) * Mathf.Sin(u * Mathf.PI)
                        + 0.004f * Mathf.Pow(1f - u, 6f)
                        + 0.022f * Mathf.Pow(Mathf.Max(0f, u - 0.75f) / 0.25f * Mathf.Max(0f, v - 0.8f) / 0.2f, 1.6f);
                return new Vector3(x, y + 0.0008f, z);
            }
            for (int j = 0; j < nz; j++)
                for (int i = 0; i < nx; i++)
                {
                    Vector3 a = P(i, j), bb = P(i + 1, j), c = P(i + 1, j + 1), d = P(i, j + 1);
                    Vector3 n = Vector3.Cross(d - a, bb - a).normalized;
                    Vector2 ua = new Vector2(i / (float)nx, j / (float)nz), ub = new Vector2((i + 1) / (float)nx, j / (float)nz),
                            uc = new Vector2((i + 1) / (float)nx, (j + 1) / (float)nz), ud = new Vector2(i / (float)nx, (j + 1) / (float)nz);
                    b.Quad(a, d, c, bb, n, ua, ud, uc, ub);
                }
            b.SmoothNormals();
            Mesh mesh = Builder.Combine(name, b);
            mesh = SaveMesh(mesh, MeshDir + "/" + name + ".asset");
            SavePrefab(name, mesh, new[] { mat }, new Vector3(PaperW, 0.01f, PaperD), new Vector3(0f, 0.005f, 0f));
        }

        /// <summary>A front page: masthead, rules, headline, five columns of grey text lines, a photo, a crease, stains.</summary>
        static Texture2D NewspaperTexture(int seed)
        {
            const int w = 724, h = 1024;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, true);
            var rng = new System.Random(seed);
            var px = new Color[w * h];
            Color paper = new Color(0.8f, 0.77f, 0.66f), ink = new Color(0.12f, 0.11f, 0.1f);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float n = 0.92f + 0.08f * Mathf.PerlinNoise(x * 0.02f, y * 0.02f) + ((float)rng.NextDouble() - 0.5f) * 0.03f;
                    px[y * w + x] = paper * n;
                }
            void Rect(int x0, int y0, int x1, int y1, Color c, float k = 1f)
            {
                for (int y = Mathf.Max(0, y0); y < Mathf.Min(h, y1); y++)
                    for (int x = Mathf.Max(0, x0); x < Mathf.Min(w, x1); x++)
                        px[y * w + x] = Color.Lerp(px[y * w + x], c, k);
            }
            int margin = 36, top = h - margin;   // texture y runs bottom-up: the masthead is at the top
            // masthead: blocky letters
            int letterX = margin + 20;
            for (int i = 0; i < 9; i++)
            {
                int lw = 44 + rng.Next(0, 14);
                if (i == 4) { letterX += 30; continue; }
                Rect(letterX, top - 110, letterX + lw, top - 20, ink, 0.95f);
                Rect(letterX + 10, top - 95, letterX + lw - 10, top - 35, paper, 0.85f);   // hollow them out a little
                Rect(letterX + 10, top - 70, letterX + lw, top - 60, ink, 0.95f);
                letterX += lw + 10;
            }
            Rect(margin, top - 128, w - margin, top - 122, ink, 0.9f);   // rules under the masthead
            Rect(margin, top - 136, w - margin, top - 133, ink, 0.9f);
            // a dateline strip
            for (int x = margin; x < w - margin; x += 9) if (rng.NextDouble() < 0.7) Rect(x, top - 152, x + 6, top - 145, ink, 0.5f);
            // headline across the page
            for (int x = margin; x < w - margin - 40; x += 26) if (rng.NextDouble() < 0.85) Rect(x, top - 200, x + 20, top - 168, ink, 0.9f);
            // the photo, top left under the headline
            int photoX1 = margin + (w - 2 * margin) * 3 / 5, photoY0 = top - 470, photoY1 = top - 220;
            for (int y = photoY0; y < photoY1; y++)
                for (int x = margin; x < photoX1; x++)
                {
                    float g = 0.25f + 0.5f * Mathf.PerlinNoise(x * 0.012f + seed, y * 0.012f) + ((x + y) % 3 == 0 ? 0.05f : 0f);   // a coarse halftone
                    px[y * w + x] = Color.Lerp(paper, ink, 1f - g);
                }
            // columns of text
            int cols = 5, gutter = 14, colW = (w - 2 * margin - (cols - 1) * gutter) / cols;
            for (int col = 0; col < cols; col++)
            {
                int x0 = margin + col * (colW + gutter), x1 = x0 + colW;
                int yTop = x0 < photoX1 ? photoY0 - 20 : top - 225;
                for (int y = yTop; y > margin; y -= 11)
                {
                    if (rng.NextDouble() < 0.06) { y -= 14; for (int hx = x0; hx < x1 - 20; hx += 12) Rect(hx, y - 2, hx + 9, y + 6, ink, 0.85f); continue; }   // a sub-heading
                    int x = x0 + (rng.NextDouble() < 0.12 ? 10 : 0);
                    int end = rng.NextDouble() < 0.1 ? x0 + colW / 2 : x1;
                    while (x < end)
                    {
                        int word = 6 + rng.Next(0, 24);
                        Rect(x, y, Mathf.Min(x + word, end), y + 5, ink, 0.55f);
                        x += word + 4;
                    }
                }
                if (col < cols - 1) Rect(x1 + gutter / 2, margin, x1 + gutter / 2 + 1, top - 210, ink, 0.4f);
            }
            // a fold crease across the middle, and damp stains
            Rect(0, h / 2 - 2, w, h / 2 + 2, new Color(0.5f, 0.47f, 0.4f), 0.35f);
            for (int s = 0; s < 3; s++)
            {
                int cx = rng.Next(0, w), cy = rng.Next(0, h), r = 50 + rng.Next(0, 110);
                for (int y = cy - r; y < cy + r; y++)
                    for (int x = cx - r; x < cx + r; x++)
                    {
                        if (x < 0 || y < 0 || x >= w || y >= h) continue;
                        float d = Vector2.Distance(new Vector2(x, y), new Vector2(cx, cy)) / r;
                        if (d > 1f) continue;
                        float ring = Mathf.Exp(-Mathf.Pow((d - 0.92f) * 14f, 2f)) * 0.35f + (1f - d) * 0.1f;   // a tide mark round a paler middle
                        px[y * w + x] = Color.Lerp(px[y * w + x], new Color(0.55f, 0.45f, 0.3f), ring);
                    }
            }
            tex.SetPixels(px);
            tex.Apply();
            return tex;
        }

        // ------------------------------------------------------------------ assets

        static Texture2D SaveTexture(Texture2D tex, string name)
        {
            string path = TextureDir + "/" + name + ".png";
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            if (AssetImporter.GetAtPath(path) is TextureImporter importer)
            {
                importer.sRGBTexture = true;
                importer.mipmapEnabled = true;
                importer.anisoLevel = 4;
                importer.maxTextureSize = 1024;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static Material TexturedMaterial(string name, Texture2D tex, float smoothness)
        {
            string path = MaterialDir + "/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!material)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(material, path);
            }
            material.SetTexture("_BaseMap", tex);
            material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_Metallic", 0f);
            material.SetFloat("_Smoothness", smoothness);
            EditorUtility.SetDirty(material);
            return material;
        }

        static Mesh SaveMesh(Mesh mesh, string path)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing)
            {
                existing.Clear();
                EditorUtility.CopySerialized(mesh, existing);
                Object.DestroyImmediate(mesh);
                EditorUtility.SetDirty(existing);
                return existing;
            }
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        static void SavePrefab(string name, Mesh mesh, Material[] materials, Vector3 colliderSize, Vector3 colliderCentre)
        {
            var go = new GameObject(name);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterials = materials;
            var box = go.AddComponent<BoxCollider>();
            box.size = colliderSize;
            box.center = colliderCentre;
            PrefabUtility.SaveAsPrefabAsset(go, PrefabDir + "/" + name + ".prefab");
            Object.DestroyImmediate(go);
        }

        // ------------------------------------------------------------------ mesh building

        /// <summary>Faces with explicit texture coordinates; one builder is one submesh.</summary>
        sealed class Builder
        {
            public readonly List<Vector3> V = new List<Vector3>(), N = new List<Vector3>();
            public readonly List<Vector2> UV = new List<Vector2>();
            public readonly List<int> T = new List<int>();

            public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 n, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud) =>
                Face(a, b, c, d, n, n, n, n, ua, ub, uc, ud);

            /// <summary>A quad wound to face its normals.</summary>
            public void Face(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 na, Vector3 nb, Vector3 nc, Vector3 nd,
                             Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud)
            {
                Vector3 facing = (na + nb + nc + nd);
                if (Vector3.Dot(Vector3.Cross(b - a, c - a), facing) < 0f)
                {
                    (b, d) = (d, b); (nb, nd) = (nd, nb); (ub, ud) = (ud, ub);
                }
                int i = V.Count;
                V.Add(a); V.Add(b); V.Add(c); V.Add(d);
                N.Add(na); N.Add(nb); N.Add(nc); N.Add(nd);
                UV.Add(ua); UV.Add(ub); UV.Add(uc); UV.Add(ud);
                T.Add(i); T.Add(i + 1); T.Add(i + 2);
                T.Add(i); T.Add(i + 2); T.Add(i + 3);
            }

            public void Tri(Vector3 a, Vector3 b, Vector3 c, Vector3 n, Vector2 uv)
            {
                if (Vector3.Dot(Vector3.Cross(b - a, c - a), n) < 0f) (b, c) = (c, b);
                int i = V.Count;
                V.Add(a); V.Add(b); V.Add(c);
                N.Add(n); N.Add(n); N.Add(n);
                UV.Add(uv); UV.Add(uv); UV.Add(uv);
                T.Add(i); T.Add(i + 1); T.Add(i + 2);
            }

            /// <summary>Averages the normals of vertices at the same place (for the curved sheet).</summary>
            public void SmoothNormals()
            {
                var sum = new Dictionary<Vector3, Vector3>();
                for (int i = 0; i < V.Count; i++) sum[V[i]] = (sum.TryGetValue(V[i], out Vector3 s) ? s : Vector3.zero) + N[i];
                for (int i = 0; i < V.Count; i++) N[i] = sum[V[i]].normalized;
            }

            public static Mesh Combine(string name, params Builder[] parts)
            {
                var mesh = new Mesh { name = name };
                var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>();
                foreach (Builder p in parts) { v.AddRange(p.V); n.AddRange(p.N); uv.AddRange(p.UV); }
                mesh.SetVertices(v); mesh.SetNormals(n); mesh.SetUVs(0, uv);
                mesh.subMeshCount = parts.Length;
                int offset = 0;
                for (int s = 0; s < parts.Length; s++)
                {
                    var t = new List<int>(parts[s].T.Count);
                    foreach (int i in parts[s].T) t.Add(i + offset);
                    mesh.SetTriangles(t, s);
                    offset += parts[s].V.Count;
                }
                mesh.RecalculateBounds();
                mesh.RecalculateTangents();
                return mesh;
            }
        }
    }
}
