using System.Collections.Generic;
using BodycamHorror.Environment;
using BodycamHorror.UI;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace BodycamHorror.EditorTools
{
    /// <summary>
    /// Builds Assets/Scenes/MainMenu.unity: a shot from the floor of a dark block-of-flats corridor. A pistol lies
    /// in the near left under a small pool of light; the corridor runs away into the dark, apartment doors and a
    /// bank of mailboxes on the left, radiators and pipes on the right; far down it, under a weak flickering
    /// bulb, a figure stands at the wall, staring up into the light and now and then beating its forehead on the
    /// wall (<see cref="MenuMonster"/>). The menu (<see cref="MainMenu"/>) is drawn over it on the right.
    /// Puts the scene first in the build, with the Level right after it (the scene Play goes to).
    /// Menu: BodycamHorror > Build Main Menu Scene. Rebuilding writes over the scene.
    /// </summary>
    public static class MainMenuSceneBuilder
    {
        const string ScenePath = "Assets/Scenes/MainMenu.unity";
        const string LevelPath = "Assets/Scenes/Level.unity";
        const string MusicPath = "Assets/Audio/Music/menu_psycho_crime_loop.wav";
        const string TitleFontPath = "Assets/Fonts/RubikDistressed-Regular.ttf";
        const string ItemFontPath = "Assets/Fonts/Oswald.ttf";
        const string GeneratedDir = "Assets/Generated/MainMenu";

        const string LampPrefab = "Assets/Prefabs/Environment/HangingLamp.prefab";
        const string ApartmentDoorPrefab = "Assets/Prefabs/Props/ApartmentDoor.prefab";
        const string ElectricPanelPrefab = "Assets/Prefabs/Props/ElectricPanel.prefab";
        const string PistolArmsPrefab = "Assets/Pistol/PistolArms.prefab";
        const string PistolMaterial = "Assets/Pistol/Materials/Pistol_Gun.mat";
        const string ZombieModel = "Assets/Zombie/Slasher/ZombieSlasher.fbx";
        const string ZombieMaterial = "Assets/Zombie/Slasher/ZombieSlasher.mat";
        const string IdleClipModel = "Assets/Zombie_Animations/Animations/Zombie_Idle_01.FBX";
        const string BangClipModel = "Assets/Zombie/animations/Zombie_HeadBang.fbx";

        // the corridor: x across (left wall at -HalfW), z away from the camera, y up
        const float HalfW = 1.25f, Height = 2.75f, Length = 22.5f, Wall = 0.2f;
        const float MonsterZ = 18.2f;

        [MenuItem("BodycamHorror/Build Main Menu Scene")]
        public static void Build()
        {
            // a long music loop: streamed and compressed, not decoded into memory whole
            if (AssetImporter.GetAtPath(MusicPath) is AudioImporter importer)
            {
                var settings = importer.defaultSampleSettings;
                settings.loadType = AudioClipLoadType.Streaming;
                settings.compressionFormat = AudioCompressionFormat.Vorbis;
                settings.quality = 0.7f;
                importer.defaultSampleSettings = settings;
                importer.loadInBackground = true;
                importer.SaveAndReimport();
            }
            if (!AssetDatabase.IsValidFolder(GeneratedDir)) AssetDatabase.CreateFolder("Assets/Generated", "MainMenu");

            LevelSceneBuilder.LoadMaterials();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var world = new GameObject("Corridor").transform;
            BuildShell(world);
            DressLeftWall(world);
            DressRightWall(world);
            Transform bulb = PlaceLamps(world);
            PlacePistol(world);
            PlaceMonster(world, bulb);
            Litter(world);
            BloodOnWall(world);
            Graffiti(world);

            var cam = PlaceCamera();
            PlaceMenu(cam);

            // the game's grade and black fog, a little thicker here so the far end sinks into it
            var volume = new GameObject("Global Volume").AddComponent<Volume>();
            volume.isGlobal = true;
            volume.sharedProfile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(GameLook.ProfilePath);
            GameLook.ApplyAtmosphere();
            RenderSettings.fogDensity = 0.05f;
            RenderSettings.ambientLight = new Color(0.006f, 0.006f, 0.0065f);

            EditorSceneManager.SaveScene(scene, ScenePath);
            SetBuildOrder();
            AssetDatabase.SaveAssets();
            Debug.Log("Main menu scene built: " + ScenePath);
        }

        // ------------------------------------------------------------------ the corridor

        static void BuildShell(Transform parent)
        {
            var shell = Group(parent, "Shell");
            const float z0 = -1.5f;
            LightingTestSceneBuilder.Box("Floor", shell, new Vector3(-HalfW, -0.2f, z0), new Vector3(HalfW, 0f, Length), LevelSceneBuilder.floorMat, 1.2f, 1.2f);
            LightingTestSceneBuilder.Box("Ceiling", shell, new Vector3(-HalfW, Height, z0), new Vector3(HalfW, Height + 0.2f, Length), LevelSceneBuilder.ceilingMat, 2f, 2f);
            LightingTestSceneBuilder.WallBox("Wall_Left", shell, new Vector3(-HalfW - Wall, 0f, z0), new Vector3(-HalfW, Height, Length));
            LightingTestSceneBuilder.WallBox("Wall_Right", shell, new Vector3(HalfW, 0f, z0), new Vector3(HalfW + Wall, Height, Length));
            LightingTestSceneBuilder.WallBox("Wall_End", shell, new Vector3(-HalfW, 0f, Length), new Vector3(HalfW, Height, Length + Wall));
            LightingTestSceneBuilder.WallBox("Wall_Back", shell, new Vector3(-HalfW, 0f, z0 - Wall), new Vector3(HalfW, Height, z0));

            // a dark band of skirting along both walls, and the wet patches the light catches on the floor
            Material rust = LevelSceneBuilder.rust, wet = LevelSceneBuilder.wet, grime = LevelSceneBuilder.grime;
            LightingTestSceneBuilder.Box("Skirting_L", shell, new Vector3(-HalfW, 0f, z0), new Vector3(-HalfW + 0.02f, 0.12f, Length), LevelSceneBuilder.woodDark, 1f, 1f);
            LightingTestSceneBuilder.Box("Skirting_R", shell, new Vector3(HalfW - 0.02f, 0f, z0), new Vector3(HalfW, 0.12f, Length), LevelSceneBuilder.woodDark, 1f, 1f);
            Puddle(shell, "Puddle_Near", new Vector3(0.45f, 0f, 2.6f), new Vector2(1.0f, 2.2f), 8f, wet);
            Puddle(shell, "Puddle_Mid", new Vector3(-0.2f, 0f, 6.5f), new Vector2(1.2f, 3.2f), -5f, wet);
            Puddle(shell, "Puddle_Far", new Vector3(0.15f, 0f, 11.5f), new Vector2(1.4f, 2.4f), 12f, wet);
            // leak streaks down the walls
            LightingTestSceneBuilder.Box("Streak_1", shell, new Vector3(-HalfW + 0.001f, 0.4f, 7.2f), new Vector3(-HalfW + 0.004f, Height, 7.9f), grime, 1f, 1f);
            LightingTestSceneBuilder.Box("Streak_2", shell, new Vector3(HalfW - 0.004f, 0.9f, 9.6f), new Vector3(HalfW - 0.001f, Height, 10.1f), grime, 1f, 1f);
            LightingTestSceneBuilder.Box("Streak_3", shell, new Vector3(-HalfW + 0.001f, 1.2f, 14.6f), new Vector3(-HalfW + 0.004f, Height, 15.4f), grime, 1f, 1f);
        }

        static void Puddle(Transform parent, string name, Vector3 centre, Vector2 size, float yaw, Material m)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(centre + Vector3.up * 0.003f, Quaternion.Euler(90f, yaw, 0f));
            go.transform.localScale = new Vector3(size.x, size.y, 1f);
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = m;
            r.shadowCastingMode = ShadowCastingMode.Off;
        }

        /// <summary>Left: flat doors, a bank of mailboxes, an electric panel.</summary>
        static void DressLeftWall(Transform parent)
        {
            var left = Group(parent, "LeftWall");
            const float x = -HalfW + 0.02f;
            PlaceModel(ApartmentDoorPrefab, left, "Door_1", new Vector3(x, 0f, 2.4f), 90f);
            PlaceModel(ApartmentDoorPrefab, left, "Door_2", new Vector3(x, 0f, 8.8f), 90f);
            PlaceModel(ApartmentDoorPrefab, left, "Door_3", new Vector3(x, 0f, 14.6f), 90f);
            PlaceModel(ElectricPanelPrefab, left, "ElectricPanel", new Vector3(-HalfW, 1.45f, 11.2f), 90f);

            // mailboxes: a sheet-metal cabinet of small doors, the old green paint gone dark
            Material metal = LightingTestSceneBuilder.SimpleMaterial("Menu_MailboxMetal", new Color(0.09f, 0.11f, 0.1f), 0.55f, 0.35f);
            Material slot = LightingTestSceneBuilder.SimpleMaterial("Menu_MailboxSlot", new Color(0.01f, 0.01f, 0.01f), 0f, 0.2f);
            const float z0 = 4.6f, z1 = 6.0f, y0 = 1.05f, y1 = 1.85f, depth = 0.24f;
            var box = Group(left, "Mailboxes");
            LightingTestSceneBuilder.Box("Cabinet", box, new Vector3(-HalfW, y0, z0), new Vector3(-HalfW + depth, y1, z1), metal, 1f, 1f);
            const int cols = 4, rows = 3;
            for (int c = 0; c < cols; c++)
                for (int r = 0; r < rows; r++)
                {
                    float za = Mathf.Lerp(z0, z1, c / (float)cols) + 0.02f, zb = Mathf.Lerp(z0, z1, (c + 1f) / cols) - 0.02f;
                    float ya = Mathf.Lerp(y0, y1, r / (float)rows) + 0.02f, yb = Mathf.Lerp(y0, y1, (r + 1f) / rows) - 0.02f;
                    // a lid standing a little proud of the cabinet, a dark slot across its top
                    float open = (c * 7 + r * 3) % 5 == 0 ? 0.03f : 0f;   // the odd one bent open
                    LightingTestSceneBuilder.Box("Lid_" + c + r, box, new Vector3(-HalfW + depth, ya, za), new Vector3(-HalfW + depth + 0.012f + open, yb, zb), metal, 1f, 1f);
                    LightingTestSceneBuilder.Box("Slot_" + c + r, box, new Vector3(-HalfW + depth + 0.012f + open, yb - 0.07f, za + 0.04f), new Vector3(-HalfW + depth + 0.016f + open, yb - 0.05f, zb - 0.04f), slot, 1f, 1f);
                }
        }

        /// <summary>Right: cast-iron radiators under a run of pipes, a riser floor to ceiling.</summary>
        static void DressRightWall(Transform parent)
        {
            var right = Group(parent, "RightWall");
            Material iron = LightingTestSceneBuilder.SimpleMaterial("Menu_RadiatorIron", new Color(0.13f, 0.13f, 0.12f), 0.35f, 0.4f);
            Material pipe = LevelSceneBuilder.rust;

            Radiator(right, iron, 0.4f, 2.7f, 0.18f, 0.95f);
            Radiator(right, iron, 6.8f, 8.4f, 0.16f, 0.85f);
            Radiator(right, iron, 12.0f, 13.3f, 0.16f, 0.85f);

            // two pipes along the wall, one low, one high, and risers
            Pipe(right, "Pipe_Low", new Vector3(HalfW - 0.08f, 1.08f, -1.5f), new Vector3(HalfW - 0.08f, 1.08f, Length), 0.03f, pipe);
            Pipe(right, "Pipe_High", new Vector3(HalfW - 0.1f, 2.45f, -1.5f), new Vector3(HalfW - 0.1f, 2.45f, Length), 0.055f, pipe);
            Pipe(right, "Riser_1", new Vector3(HalfW - 0.09f, 0f, 3.15f), new Vector3(HalfW - 0.09f, Height, 3.15f), 0.045f, pipe);
            Pipe(right, "Riser_2", new Vector3(HalfW - 0.09f, 0f, 10.3f), new Vector3(HalfW - 0.09f, Height, 10.3f), 0.045f, pipe);
            PlaceModel(ElectricPanelPrefab, right, "ElectricPanel_R", new Vector3(HalfW, 1.5f, 15.5f), -90f);
        }

        /// <summary>A sectional radiator between z0 and z1 against the right wall: a row of tall iron fins.</summary>
        static void Radiator(Transform parent, Material m, float z0, float z1, float y0, float y1)
        {
            var rad = Group(parent, "Radiator_" + z0.ToString("0.0"));
            const float pitch = 0.09f;
            int n = Mathf.Max(2, Mathf.RoundToInt((z1 - z0) / pitch));
            float x = HalfW - 0.07f;
            for (int i = 0; i < n; i++)
            {
                float z = z0 + (i + 0.5f) * (z1 - z0) / n;
                var fin = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                fin.name = "Fin_" + i;
                Object.DestroyImmediate(fin.GetComponent<Collider>());
                fin.transform.SetParent(rad, false);
                fin.transform.position = new Vector3(x, (y0 + y1) * 0.5f, z);
                fin.transform.localScale = new Vector3(0.11f, (y1 - y0) * 0.5f, 0.075f);
                fin.GetComponent<MeshRenderer>().sharedMaterial = m;
                fin.isStatic = true;
            }
            // the hubs top and bottom that join the fins
            Pipe(rad, "Hub_Top", new Vector3(x, y1 - 0.08f, z0), new Vector3(x, y1 - 0.08f, z1), 0.035f, m);
            Pipe(rad, "Hub_Bottom", new Vector3(x, y0 + 0.08f, z0), new Vector3(x, y0 + 0.08f, z1), 0.035f, m);
            Pipe(rad, "Feed", new Vector3(x, y1 - 0.08f, z1), new Vector3(HalfW - 0.08f, 1.08f, z1 + 0.15f), 0.018f, LevelSceneBuilder.rust);
        }

        static void Pipe(Transform parent, string name, Vector3 a, Vector3 b, float radius, Material m)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation((a + b) * 0.5f, Quaternion.FromToRotation(Vector3.up, (b - a).normalized));
            go.transform.localScale = new Vector3(radius * 2f, (b - a).magnitude * 0.5f, radius * 2f);
            go.GetComponent<MeshRenderer>().sharedMaterial = m;
            go.isStatic = true;
        }

        /// <summary>Where the figure beats its head: a dark smear on the paint at forehead height, run down
        /// the wall in drips, spatter round it. A decal from a texture drawn here.</summary>
        static void BloodOnWall(Transform parent)
        {
            Material mat = DecalMaterial("BloodSmear", BloodTexture(512, 1024, 7));
            var go = new GameObject("Blood_HeadBang");
            go.transform.SetParent(parent, false);
            // the splat's heart (0.78 up the picture) where the forehead meets the wall
            const float w = 0.75f, h = 1.5f, hitY = 1.8f;   // the forehead meets the plaster here (head-bang clip, scale 0.7)
            go.transform.SetPositionAndRotation(new Vector3(-HalfW, hitY - (0.78f - 0.5f) * h, MonsterZ),
                                                Quaternion.LookRotation(Vector3.left, Vector3.up));
            var p = go.AddComponent<DecalProjector>();
            p.material = mat;
            p.size = new Vector3(w, h, 0.3f);
            p.pivot = Vector3.zero;
            p.drawDistance = 60f;
            p.startAngleFade = 60f;
            p.endAngleFade = 85f;
            // a second, older smear a little to the side: it has been at this for a long time
            var old = Object.Instantiate(go, parent);
            old.name = "Blood_HeadBang_Old";
            old.transform.position = new Vector3(-HalfW, hitY - 0.08f - (0.78f - 0.5f) * h * 0.7f, MonsterZ + 0.32f);
            var op = old.GetComponent<DecalProjector>();
            op.material = DecalMaterial("BloodSmearOld", BloodTexture(512, 1024, 23));
            op.size = new Vector3(w * 0.7f, h * 0.7f, 0.3f);
            op.fadeFactor = 0.75f;
        }

        /// <summary>Paint on the far wall and on the right wall near the end: an eye in a triangle, and an
        /// arrow over "НЕ ОГЛЯДЫВАЙСЯ" (the level's own graffiti decals).</summary>
        static void Graffiti(Transform parent)
        {
            var g = Group(parent, "Graffiti");
            var eye = PlaceModel("Assets/Prefabs/Props/Graffiti/Graffiti_Eye.prefab", g, "Graffiti_Eye", new Vector3(0.25f, 1.5f, Length), 0f);
            eye.transform.rotation = Quaternion.LookRotation(Vector3.forward, Vector3.up);
            var ep = eye.GetComponent<DecalProjector>();
            if (ep) ep.size = new Vector3(1.3f, 1.3f, ep.size.z);
            var words = PlaceModel("Assets/Prefabs/Props/Graffiti/Graffiti_DontLook.prefab", g, "Graffiti_DontLook", new Vector3(HalfW, 1.55f, 20.6f), 0f);
            words.transform.rotation = Quaternion.LookRotation(Vector3.right, Vector3.up);
            var wp = words.GetComponent<DecalProjector>();
            if (wp) wp.size = new Vector3(1.7f, 1.7f * 768f / 2048f, wp.size.z);
        }

        static Material DecalMaterial(string name, Texture2D tex)
        {
            string path = GeneratedDir + "/" + name + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!mat)
            {
                var template = AssetDatabase.LoadAssetAtPath<Material>("Assets/Resources/WoundDecal.mat");
                mat = template ? new Material(template) : new Material(Shader.Find("Shader Graphs/Decal"));
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.SetTexture("Base_Map", tex);
            if (mat.HasProperty("Normal_Blend")) mat.SetFloat("Normal_Blend", 0f);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>A blood smear drawn into a texture with alpha: a ragged blot high up (the impact), a wide
        /// smear dragged down from it, drips running from its lower edge, fine spatter round about.</summary>
        static Texture2D BloodTexture(int w, int h, int seed)
        {
            string path = GeneratedDir + "/BloodSmear_" + seed + ".png";
            var rng = new System.Random(seed);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            var alpha = new float[w * h];
            var thick = new float[w * h];
            float ox = R(0f, 100f), oy = R(0f, 100f);
            float Fbm(float x, float y)
            {
                float s = 0f, a = 0.5f, f = 1f;
                for (int o = 0; o < 4; o++) { s += a * Mathf.PerlinNoise(ox + x * f, oy + y * f); a *= 0.5f; f *= 2.1f; }
                return s;
            }
            void Put(int x, int y, float a, float t)
            {
                if (x < 0 || y < 0 || x >= w || y >= h) return;
                int i = y * w + x;
                alpha[i] = Mathf.Max(alpha[i], a);
                thick[i] = Mathf.Max(thick[i], t);
            }

            // blot and smear (u across, v up; one unit = the texture's width)
            float cx = 0.5f, cy = 0.78f * h / w;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float u = x / (float)w, v = y / (float)w;
                    float n = Fbm(u * 5f, v * 5f);
                    float dx = (u - cx) / 0.2f, dy = (v - cy) / 0.13f;
                    float blot = 1.15f - Mathf.Sqrt(dx * dx + dy * dy) - (n - 0.5f) * 1.1f;
                    // the smear: from the blot down, narrowing, streaky across
                    float below = cy - v;
                    float smear = 0f;
                    if (below > 0f && below < 0.45f)
                    {
                        float half = Mathf.Lerp(0.15f, 0.04f, below / 0.45f);
                        float across = Mathf.Abs(u - cx - Mathf.Sin(below * 6f) * 0.02f) / half;
                        float streak = Mathf.PerlinNoise(ox + u * 60f, 3.3f);
                        smear = (1f - across) * (1f - below / 0.45f) * (0.45f + streak * 0.8f);
                    }
                    float a = Mathf.Max(Mathf.Clamp01(blot * 3f), Mathf.Clamp01(smear * 1.6f) * 0.85f);
                    if (a > 0f) Put(x, y, a, Mathf.Clamp01(blot * 1.5f));
                }

            // drips from the lower edge of the blot
            int drips = 6 + rng.Next(4);
            for (int d = 0; d < drips; d++)
            {
                float u = cx + R(-0.15f, 0.15f);
                float top = cy - R(0.02f, 0.1f), len = R(0.12f, 0.75f) * (h / (float)w) * 0.6f;
                float width = R(2f, 5.5f);
                int x0 = Mathf.RoundToInt(u * w);
                int yTop = Mathf.RoundToInt(top * w), yEnd = Mathf.Max(2, Mathf.RoundToInt((top - len) * w));
                float wobble = R(0f, 10f);
                for (int y = yTop; y >= yEnd; y--)
                {
                    float k = (yTop - y) / (float)Mathf.Max(1, yTop - yEnd);
                    float wd = width * Mathf.Lerp(1f, 0.6f, k);
                    int xc = x0 + Mathf.RoundToInt(Mathf.Sin(y * 0.02f + wobble) * 2f);
                    for (int x = xc - Mathf.CeilToInt(wd); x <= xc + Mathf.CeilToInt(wd); x++)
                        Put(x, y, Mathf.Clamp01(1.2f - Mathf.Abs(x - xc) / wd) * 0.95f, 0.6f);
                }
                // the bead at its end
                float bead = width * 1.6f;
                for (int y = yEnd - (int)bead; y <= yEnd + (int)bead; y++)
                    for (int x = x0 - (int)bead; x <= x0 + (int)bead; x++)
                    {
                        float r = new Vector2(x - x0, y - yEnd).magnitude / bead;
                        if (r < 1f) Put(x, y, Mathf.Clamp01((1f - r) * 3f), 0.9f);
                    }
            }

            // spatter: flecks flung out round the blot
            for (int s = 0; s < 90; s++)
            {
                float ang = R(0f, Mathf.PI * 2f), dist = Mathf.Pow(R(0f, 1f), 0.6f) * 0.42f + 0.12f;
                float u = cx + Mathf.Cos(ang) * dist, v = cy + Mathf.Sin(ang) * dist * 0.8f;
                float rad = R(0.7f, 4.5f) * (1.2f - dist);
                int xc = Mathf.RoundToInt(u * w), yc = Mathf.RoundToInt(v * w);
                for (int y = yc - 6; y <= yc + 6; y++)
                    for (int x = xc - 6; x <= xc + 6; x++)
                    {
                        float r = new Vector2(x - xc, y - yc).magnitude / Mathf.Max(rad, 0.5f);
                        if (r < 1f) Put(x, y, Mathf.Clamp01((1f - r) * 2.5f) * 0.9f, 0.5f);
                    }
            }

            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            var px = new Color32[w * h];
            for (int i = 0; i < px.Length; i++)
            {
                // thin blood dries brown-red, thick stays near black-red
                float t = thick[i];
                var c = Color.Lerp(new Color(0.55f, 0.06f, 0.04f), new Color(0.26f, 0.015f, 0.01f), t);
                c.a = alpha[i];
                px[i] = c;
            }
            tex.SetPixels32(px);
            tex.Apply();
            System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>Scraps of paper on the floor along the walls.</summary>
        static void Litter(Transform parent)
        {
            var litter = Group(parent, "Litter");
            // dirty, damp scraps: dark enough not to glow in the spill of light from behind the camera
            Material paper = LightingTestSceneBuilder.SimpleMaterial("Menu_PaperDirty", new Color(0.16f, 0.15f, 0.13f), 0f, 0.15f);
            for (int i = 0; i < 7; i++)
            {
                float z = 2.2f + i * 1.7f, x = (i % 2 == 0 ? 0.8f : -0.85f) + Mathf.Sin(i * 2.3f) * 0.2f;
                var p = GameObject.CreatePrimitive(PrimitiveType.Quad);
                p.name = "Scrap_" + i;
                Object.DestroyImmediate(p.GetComponent<Collider>());
                p.transform.SetParent(litter, false);
                p.transform.SetPositionAndRotation(new Vector3(x, 0.004f, z), Quaternion.Euler(90f, i * 47f, 0f));
                p.transform.localScale = new Vector3(0.2f, 0.28f, 1f);
                p.GetComponent<MeshRenderer>().sharedMaterial = paper;
            }
        }

        // ------------------------------------------------------------------ light

        /// <summary>A dead lamp over the camera, a weak one halfway, and the one the figure stares into.
        /// Returns that last one's bulb.</summary>
        static Transform PlaceLamps(Transform parent)
        {
            var lamps = Group(parent, "Lamps");
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(LampPrefab);
            if (!prefab) prefab = LightingTestSceneBuilder.BuildLampPrefab();

            // near: switched off, a dark shape against the ceiling
            DeadLamp(prefab, lamps, "Lamp_Near_Dead", new Vector3(0.05f, Height, 3.6f));

            // far end: the figure's lamp, weak and failing
            var far = Lamp(prefab, lamps, "Lamp_Monster", new Vector3(-0.75f, Height, MonsterZ + 0.3f), 0.12f, true);
            Transform bulb = null;
            foreach (Transform t in far.GetComponentsInChildren<Transform>(true)) if (t.name == "Bulb") bulb = t;

            // halfway: dead too
            DeadLamp(prefab, lamps, "Lamp_Mid_Dead", new Vector3(0.1f, Height, 7.6f));

            // a cold spill from behind the camera (a window on the stairs): the near walls read as walls
            var spill = new GameObject("Spill").AddComponent<Light>();
            spill.transform.SetParent(lamps, false);
            spill.transform.position = new Vector3(0.6f, 2.3f, -1.2f);
            spill.transform.rotation = Quaternion.LookRotation(new Vector3(-0.1f, -0.25f, 1f));
            spill.type = LightType.Spot;
            spill.spotAngle = 70f;
            spill.range = 16f;
            spill.intensity = 3.5f;
            spill.color = new Color(0.62f, 0.72f, 0.88f);
            spill.shadows = LightShadows.None;

            // a dull cold wash under the ceiling near the camera (light from a stairwell window off the shot):
            // just enough for the walls, the mailboxes and the radiators to show as shapes
            var bounce = new GameObject("Bounce").AddComponent<Light>();
            bounce.transform.SetParent(lamps, false);
            bounce.transform.position = new Vector3(0f, 2.3f, 4.8f);
            bounce.type = LightType.Point;
            bounce.range = 7f;
            bounce.intensity = 0.8f;
            bounce.color = new Color(0.6f, 0.68f, 0.8f);
            bounce.shadows = LightShadows.None;

            // a glow at the very end, behind the figure, so it stands out against something
            var back = new GameObject("EndGlow").AddComponent<Light>();
            back.transform.SetParent(lamps, false);
            back.transform.position = new Vector3(0.2f, 2.2f, Length - 0.6f);
            back.type = LightType.Point;
            back.color = new Color(0.62f, 0.72f, 0.9f);
            back.intensity = 0.55f;
            back.range = 6f;
            back.shadows = LightShadows.None;
            return bulb ? bulb : far.transform;
        }

        /// <summary>A lamp whose bulb has gone: a dark shape hanging from the ceiling.</summary>
        static void DeadLamp(GameObject prefab, Transform parent, string name, Vector3 at)
        {
            var lamp = Lamp(prefab, parent, name, at, 0f, false);
            foreach (Light l in lamp.GetComponentsInChildren<Light>(true)) l.enabled = false;
            Material dead = LightingTestSceneBuilder.SimpleMaterial("Menu_DeadBulb", new Color(0.2f, 0.2f, 0.18f), 0f, 0.8f);
            foreach (Renderer r in lamp.GetComponentsInChildren<Renderer>(true))
                if (r.name == "Bulb") r.sharedMaterial = dead;
                else if (r.name.Contains("Haze")) r.enabled = false;
        }

        static GameObject Lamp(GameObject prefab, Transform parent, string name, Vector3 at, float dim, bool flicker)
        {
            var lamp = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            lamp.name = name;
            lamp.transform.position = at;
            var swing = lamp.GetComponentInChildren<SwingingLamp>(true);
            if (swing) swing.enabled = flicker;   // the live one sways a little in a draught
            Light main = null;
            foreach (Light l in lamp.GetComponentsInChildren<Light>(true))
            {
                l.intensity *= dim;
                if (l.name == "Light") main = l;
            }
            if (flicker && main)
            {
                main.shadows = LightShadows.Soft;
                var f = main.gameObject.AddComponent<LightFlicker>();
                f.kind = LightFlicker.Kind.Fluorescent;
                f.depth = 0.75f;
                f.pace = new Vector2(2f, 7f);
                foreach (Renderer r in lamp.GetComponentsInChildren<Renderer>(true)) if (r.name == "Bulb") f.glow = r;
            }
            return lamp;
        }

        // ------------------------------------------------------------------ the pistol

        /// <summary>The player's pistol (its three parts baked out of the arms rig) lying on its side on the
        /// floor in the near left, a warm narrow light on it from above.</summary>
        static void PlacePistol(Transform parent)
        {
            Mesh mesh = BakePistolMesh();
            var gun = new GameObject("Pistol");
            gun.transform.SetParent(parent, false);
            // on its side, muzzle to the right, rolled a little towards the camera (resting on the magazine's
            // lip) so the low camera sees its flank rather than just the top of the slide
            gun.transform.SetPositionAndRotation(new Vector3(-0.22f, 0f, 1.0f), Quaternion.Euler(0f, 72f, 68f));
            gun.AddComponent<MeshFilter>().sharedMesh = mesh;
            float lowest = float.MaxValue;
            foreach (Vector3 v in mesh.vertices) lowest = Mathf.Min(lowest, gun.transform.TransformPoint(v).y);
            gun.transform.position += Vector3.up * -lowest;   // touching the floor
            var r = gun.AddComponent<MeshRenderer>();
            r.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(PistolMaterial);
            r.renderingLayerMask = 1u;   // a thing in the world, lit like the floor (not the view model's layer)

            var light = new GameObject("PistolLight").AddComponent<Light>();
            light.transform.SetParent(parent, false);
            light.transform.position = new Vector3(-0.02f, 1.0f, 0.5f);   // above and a little in front: the flank catches it
            light.transform.LookAt(gun.transform.position);
            light.type = LightType.Spot;
            light.color = new Color(1f, 0.82f, 0.62f);
            light.intensity = 5f;
            light.range = 3f;
            light.spotAngle = 34f;
            light.innerSpotAngle = 8f;
            light.shadows = LightShadows.Soft;
            light.shadowNearPlane = 0.1f;
        }

        static Mesh BakePistolMesh()
        {
            string path = GeneratedDir + "/MenuPistol.asset";
            var arms = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(PistolArmsPrefab));
            var parts = new List<CombineInstance>();
            foreach (var smr in arms.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (!smr.name.StartsWith("xd_")) continue;
                var baked = new Mesh();
                smr.BakeMesh(baked, true);
                parts.Add(new CombineInstance
                {
                    mesh = baked,
                    transform = Matrix4x4.TRS(smr.transform.position, smr.transform.rotation, Vector3.one),
                });
            }
            // in the arms' own frame: put the gun's barrel along +z and its top up, the middle at the origin
            var combined = new Mesh { name = "MenuPistol" };
            combined.CombineMeshes(parts.ToArray(), true, true);
            Object.DestroyImmediate(arms);
            foreach (var p in parts) Object.DestroyImmediate(p.mesh);

            Vector3 centre = combined.bounds.center;
            var v = combined.vertices;
            for (int i = 0; i < v.Length; i++) v[i] -= centre;
            combined.vertices = v;
            combined.RecalculateBounds();

            var old = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (old)
            {
                EditorUtility.CopySerialized(combined, old);
                Object.DestroyImmediate(combined);
                return old;
            }
            AssetDatabase.CreateAsset(combined, path);
            return combined;
        }

        // ------------------------------------------------------------------ the figure

        static void PlaceMonster(Transform parent, Transform bulb)
        {
            var root = new GameObject("Monster");
            root.transform.SetParent(parent, false);
            // at the left wall, facing it, its forehead a hand from the plaster (where the head-bang clip expects it)
            root.transform.SetPositionAndRotation(new Vector3(-HalfW + 0.45f, 0f, MonsterZ), Quaternion.Euler(0f, -90f, 0f));

            var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(ZombieModel), root.transform);
            model.name = "Model";
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
            model.transform.localScale = Vector3.one * 0.7f;   // as the level's zombies
            var mat = AssetDatabase.LoadAssetAtPath<Material>(ZombieMaterial);
            foreach (Renderer r in model.GetComponentsInChildren<Renderer>(true))
            {
                if (mat) r.sharedMaterial = mat;
                r.shadowCastingMode = ShadowCastingMode.On;
                if (r is SkinnedMeshRenderer s) s.updateWhenOffscreen = true;
            }

            var animator = model.GetComponent<Animator>();
            if (!animator) animator = model.AddComponent<Animator>();
            animator.runtimeAnimatorController = BuildController();
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            var monster = root.AddComponent<MenuMonster>();
            monster.animator = animator;
            monster.lookTarget = bulb;
        }

        static AnimatorController BuildController()
        {
            string path = GeneratedDir + "/MenuMonster.controller";
            AssetDatabase.DeleteAsset(path);
            var c = AnimatorController.CreateAnimatorControllerAtPath(path);
            var sm = c.layers[0].stateMachine;
            var idle = sm.AddState("Idle");
            idle.motion = FirstClip(IdleClipModel);
            var bang = sm.AddState("WallBang");
            bang.motion = FirstClip(BangClipModel);
            sm.defaultState = idle;
            return c;
        }

        static AnimationClip FirstClip(string modelPath)
        {
            foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(modelPath))
                if (o is AnimationClip clip && !clip.name.StartsWith("__preview")) return clip;
            Debug.LogWarning("Main menu: no clip in " + modelPath);
            return null;
        }

        // ------------------------------------------------------------------ camera and menu

        static Camera PlaceCamera()
        {
            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var rig = new GameObject("CameraRig").transform;
            rig.position = new Vector3(0.12f, 0.17f, -0.15f);
            rig.rotation = Quaternion.LookRotation(new Vector3(-0.32f, 1.25f, 14f) - rig.position);
            camGo.transform.SetParent(rig, false);
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 40f;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 60f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            var data = camGo.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            camGo.AddComponent<AudioListener>();
            camGo.AddComponent<MenuCameraDrift>();
            return cam;
        }

        static void PlaceMenu(Camera cam)
        {
            var menuGo = new GameObject("MainMenu");
            var source = menuGo.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = true;
            source.spatialBlend = 0f;
            var menu = menuGo.AddComponent<MainMenu>();
            menu.music = AssetDatabase.LoadAssetAtPath<AudioClip>(MusicPath);
            menu.titleFont = AssetDatabase.LoadAssetAtPath<Font>(TitleFontPath);
            menu.itemFont = AssetDatabase.LoadAssetAtPath<Font>(ItemFontPath);
            menu.firstScene = System.IO.Path.GetFileNameWithoutExtension(LevelPath);
            if (!menu.music) Debug.LogWarning("Main menu: no music at " + MusicPath);
            if (!menu.titleFont || !menu.itemFont) Debug.LogWarning("Main menu: fonts missing under Assets/Fonts");
        }

        // ------------------------------------------------------------------ helpers

        static Transform Group(Transform parent, string name)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            return t;
        }

        static GameObject PlaceModel(string prefabPath, Transform parent, string name, Vector3 position, float yaw)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (!prefab) { Debug.LogWarning("Main menu: missing prefab " + prefabPath); return new GameObject(name); }
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            go.name = name;
            go.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
            return go;
        }

        /// <summary>The menu first, the level second, everything else as it was after them.</summary>
        static void SetBuildOrder()
        {
            var list = new List<EditorBuildSettingsScene>
            {
                new EditorBuildSettingsScene(ScenePath, true),
                new EditorBuildSettingsScene(LevelPath, true),
            };
            foreach (var s in EditorBuildSettings.scenes)
                if (s.path != ScenePath && s.path != LevelPath) list.Add(s);
            EditorBuildSettings.scenes = list.ToArray();
        }
    }
}
