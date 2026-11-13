using System.IO;
using UnityEditor;
using UnityEngine;

namespace BodycamHorror.EditorTools
{
    /// <summary>
    /// Turns the scanned texture sets in Assets/Textures/Environment (ambientCG, CC0; see SOURCES.md there)
    /// into URP Lit materials, and paints the lens dirt for the bloom in code.
    /// </summary>
    public static class EnvironmentTextures
    {
        /// <summary>
        /// Material for the set in <c>textureDir/id</c>: Color, NormalGL and a mask packed here from the
        /// set's Roughness, AmbientOcclusion and (for metals) Metalness maps, which are deleted once packed.
        /// </summary>
        public static Material ScannedMaterial(string id, string materialDir, string textureDir, Color tint, float bumpScale)
        {
            string folder = $"{textureDir}/{id}";
            string colorPath = $"{folder}/{id}_Color.jpg";
            string normalPath = $"{folder}/{id}_NormalGL.jpg";
            string maskPath = $"{folder}/{id}_Mask.png";
            string roughnessPath = $"{folder}/{id}_Roughness.jpg";
            string occlusionPath = $"{folder}/{id}_AmbientOcclusion.jpg";
            string metalnessPath = $"{folder}/{id}_Metalness.jpg";

            if (!File.Exists(maskPath) && File.Exists(roughnessPath))
            {
                PackMask(roughnessPath, File.Exists(occlusionPath) ? occlusionPath : null,
                         File.Exists(metalnessPath) ? metalnessPath : null, maskPath);
                AssetDatabase.DeleteAsset(roughnessPath);
                AssetDatabase.DeleteAsset(occlusionPath);
                AssetDatabase.DeleteAsset(metalnessPath);
                AssetDatabase.Refresh();
            }
            Import(colorPath, TextureImporterType.Default, true);
            Import(normalPath, TextureImporterType.NormalMap, false);
            Import(maskPath, TextureImporterType.Default, false);

            string materialPath = $"{materialDir}/{id}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (!material)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(material, materialPath);
            }
            var mask = AssetDatabase.LoadAssetAtPath<Texture2D>(maskPath);
            material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(colorPath));
            material.SetColor("_BaseColor", tint);
            material.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath));
            material.SetFloat("_BumpScale", bumpScale);
            material.EnableKeyword("_NORMALMAP");
            material.SetTexture("_MetallicGlossMap", mask);
            material.EnableKeyword("_METALLICSPECGLOSSMAP");
            material.SetTexture("_OcclusionMap", mask);
            material.EnableKeyword("_OCCLUSIONMAP");
            material.SetFloat("_OcclusionStrength", 1f);
            material.SetFloat("_Smoothness", 1f);
            material.SetFloat("_Metallic", 0f);
            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>URP Lit layout: metallic in R (0 without a metalness map), occlusion in G, smoothness in A.</summary>
        static void PackMask(string roughnessPath, string occlusionPath, string metalnessPath, string maskPath)
        {
            var roughness = new Texture2D(2, 2);
            roughness.LoadImage(File.ReadAllBytes(roughnessPath));
            Color32[] rough = roughness.GetPixels32();
            Color32[] occlusion = null;
            if (occlusionPath != null)
            {
                var ao = new Texture2D(2, 2);
                ao.LoadImage(File.ReadAllBytes(occlusionPath));
                if (ao.width == roughness.width && ao.height == roughness.height) occlusion = ao.GetPixels32();
                Object.DestroyImmediate(ao);
            }

            Color32[] metal = null;
            if (metalnessPath != null)
            {
                var m = new Texture2D(2, 2);
                m.LoadImage(File.ReadAllBytes(metalnessPath));
                if (m.width == roughness.width && m.height == roughness.height) metal = m.GetPixels32();
                Object.DestroyImmediate(m);
            }

            var packed = new Color32[rough.Length];
            for (int i = 0; i < rough.Length; i++)
                packed[i] = new Color32(metal != null ? metal[i].r : (byte)0, occlusion != null ? occlusion[i].g : (byte)255, 0, (byte)(255 - rough[i].r));

            var mask = new Texture2D(roughness.width, roughness.height, TextureFormat.RGBA32, false);
            mask.SetPixels32(packed);
            mask.Apply();
            File.WriteAllBytes(maskPath, mask.EncodeToPNG());
            Object.DestroyImmediate(mask);
            Object.DestroyImmediate(roughness);
            AssetDatabase.ImportAsset(maskPath);
        }

        static void Import(string path, TextureImporterType type, bool srgb)
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            if (!importer) return;
            if (importer.textureType == type && importer.sRGBTexture == srgb && importer.anisoLevel == 8 && !importer.alphaIsTransparency) return;
            importer.textureType = type;
            importer.sRGBTexture = srgb;
            importer.alphaIsTransparency = false;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.anisoLevel = 8;
            importer.SaveAndReimport();
        }

        // ------------------------------------------------------------------ lens dirt

        /// <summary>Smudges, specks and a greasy film for the bloom's dirt texture. Painted once; delete the file to repaint.</summary>
        public static Texture2D LensDirt(string textureDir)
        {
            string path = textureDir + "/LensDirt.png";
            if (!File.Exists(path))
            {
                const int w = 1024, h = 512;
                var pixels = new Color[w * h];
                var random = new System.Random(7);
                float Next() => (float)random.NextDouble();
                // soft smudges and out-of-focus specks, mostly towards the edges where a thumb wipes least
                for (int n = 0; n < 150; n++)
                {
                    float cx = Next() * w, cy = Next() * h;
                    bool speck = n > 40;
                    float radius = speck ? Mathf.Lerp(3f, 16f, Next() * Next()) : Mathf.Lerp(40f, 150f, Next());
                    float edge = Mathf.Clamp01(new Vector2(cx / w - 0.5f, cy / h - 0.5f).magnitude * 1.8f);
                    float strength = (speck ? Mathf.Lerp(0.08f, 0.4f, Next()) : Mathf.Lerp(0.02f, 0.08f, Next())) * Mathf.Lerp(0.35f, 1f, edge);
                    float hardness = speck ? 0.55f : 0f;
                    int x0 = Mathf.Max(0, (int)(cx - radius)), x1 = Mathf.Min(w - 1, (int)(cx + radius));
                    int y0 = Mathf.Max(0, (int)(cy - radius)), y1 = Mathf.Min(h - 1, (int)(cy + radius));
                    for (int y = y0; y <= y1; y++)
                    {
                        for (int x = x0; x <= x1; x++)
                        {
                            float d = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)) / radius;
                            float a = 1f - Ramp(hardness, 1f, d);
                            pixels[y * w + x] += new Color(a, a, a, 0f) * strength;
                        }
                    }
                }
                for (int y = 0; y < h; y++)
                {
                    for (int x = 0; x < w; x++)
                    {
                        float u = (x + 0.5f) / w, v = (y + 0.5f) / h;
                        float smear = Mathf.Pow(Mathf.PerlinNoise(u * 6f + 3.1f, v * 3f + 7.7f), 3f) * 0.5f;   // greasy film
                        float streak = Mathf.Pow(Mathf.PerlinNoise(u * 3f + 11.3f, v * 60f + 5.9f), 6f) * 0.25f;
                        Color c = pixels[y * w + x] + new Color(smear, smear, smear) + new Color(streak, streak, streak);
                        c *= new Color(0.95f, 1f, 1.03f);
                        c.a = 1f;
                        pixels[y * w + x] = c;
                    }
                }
                var texture = new Texture2D(w, h, TextureFormat.RGBA32, false);
                texture.SetPixels(pixels);
                texture.Apply();
                File.WriteAllBytes(path, texture.EncodeToPNG());
                Object.DestroyImmediate(texture);
                AssetDatabase.Refresh();
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.mipmapEnabled = false;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>0 below <paramref name="from"/>, 1 above <paramref name="to"/>, eased in between (Mathf.SmoothStep is a different thing).</summary>
        static float Ramp(float from, float to, float x)
        {
            float t = Mathf.Clamp01((x - from) / (to - from));
            return t * t * (3f - 2f * t);
        }
    }
}
