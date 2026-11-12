using BodycamHorror.Audio;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace BodycamHorror.EditorTools
{
    /// <summary>
    /// Puts the sound of the place into the lighting test scene: a dark bed under everything, lamps
    /// that hum where they hang, water dripping in a few spots, and the building itself: metal
    /// creaking and groaning, something scraping, a knock on a pipe somewhere behind the walls.
    /// Recordings: Assets/Audio/Ambience (CC0, see SOURCES.txt there). Running it again replaces the
    /// "Ambience" object it made before. Also called by Build Lighting Test Scene.
    /// Menu: BodycamHorror > Add Ambience To Open Scene.
    /// </summary>
    public static class AmbienceBuilder
    {
        const string SourceDir = "Assets/Audio/Ambience/";
        const string Dir = "Assets/Audio/Ambience/Prepared/";
        const string RootName = "Ambience";

        // the lighting test layout (see LightingTestSceneBuilder): corridor x ±1.2, z -1..24, 3 m high;
        // end room 9 x 9 at z 24..33; light room x 1.5..8.5, z 2..9
        const float Height = 3f;

        [MenuItem("BodycamHorror/Add Ambience To Open Scene")]
        public static void AddToOpenScene()
        {
            var scene = EditorSceneManager.GetActiveScene();
            Build();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("Ambience added to " + scene.path);
        }

        internal static void Build()
        {
            if (!System.IO.Directory.Exists(Dir)) Prepare();
            ImportSettings();
            var old = GameObject.Find(RootName);
            if (old) Object.DestroyImmediate(old);
            // and the hums it hung on lamps last time
            foreach (var a in Object.FindObjectsByType<AmbientSound>(FindObjectsInactive.Include))
                if (a) Object.DestroyImmediate(a.gameObject);
            var root = new GameObject(RootName).transform;

            // the bed: a low basement drone and the building's hum, the same everywhere
            Loop(root, "Bed_Drone", Vector3.zero, 0.22f, 0f, 0f, 0f, "loop_bed_drone");
            Loop(root, "Bed_Hum", Vector3.zero, 0.07f, 0f, 0f, 0f, "loop_bed_hum");

            // lamps hum where they hang
            foreach (Transform lamp in Children("Lamps"))
                Loop(lamp, "Hum", Vector3.up * -0.6f, 0.18f, 1f, 0.4f, 5f, "loop_hum_bulb");
            var tube = GameObject.Find("Fixtures/Ceiling_Fluorescent");
            if (tube) Loop(tube.transform, "Hum", Vector3.down * 0.1f, 0.22f, 1f, 0.5f, 7f, "loop_hum_fluorescent");
            var emergency = GameObject.Find("Fixtures/Wall_Emergency");
            if (emergency) Loop(emergency.transform, "Hum", Vector3.zero, 0.1f, 1f, 0.3f, 4f, "loop_hum_bulb");

            // water: a leak half-way down the corridor, a corner of the end room, the light room
            var drips = Group(root, "Drips");
            string[] drop = Named("drip_");
            Shots(drips, "Drip_Corridor", new Vector3(-1f, Height - 0.2f, 9f), 0.35f, new Vector2(1.2f, 4.5f), 0f, 1f, 14f, drop);
            Shots(drips, "Drip_EndRoom", new Vector3(3.6f, Height - 0.2f, 31.5f), 0.4f, new Vector2(0.7f, 2.6f), 0f, 1f, 16f, drop);
            Shots(drips, "Drip_LightRoom", new Vector3(7.6f, Height - 0.2f, 8f), 0.28f, new Vector2(2.5f, 7f), 0f, 1f, 12f, drop);

            // the building: creaks and groans in the structure, scrapes and knocks somewhere beyond the walls
            var building = Group(root, "Building");
            string[] creaks = Named("creak_"), scrapes = Named("scrape_"), knocks = Named("knock_");
            Shots(building, "Creaks_Ceiling", new Vector3(0f, Height + 0.5f, 18f), 0.4f, new Vector2(14f, 34f), 9f, 2f, 35f, creaks);
            Shots(building, "Creaks_EndRoom", new Vector3(0f, Height - 0.6f, 30f), 0.35f, new Vector2(18f, 45f), 4f, 2f, 30f, creaks);
            Shots(building, "Scrapes_BeyondFarWall", new Vector3(0f, 1.2f, 38f), 0.55f, new Vector2(25f, 60f), 5f, 3f, 45f, scrapes);
            Shots(building, "Scrapes_BehindLeftWall", new Vector3(-6f, 1.2f, 12f), 0.45f, new Vector2(30f, 70f), 6f, 3f, 40f, scrapes);
            Shots(building, "Knocks_Pipes", new Vector3(5f, Height + 0.4f, 20f), 0.4f, new Vector2(16f, 45f), 8f, 3f, 45f, knocks);
        }

        /// <summary>Names (no extension) of the prepared clips that start with this.</summary>
        static string[] Named(string prefix)
        {
            var list = new System.Collections.Generic.List<string>();
            foreach (string guid in AssetDatabase.FindAssets("t:AudioClip", new[] { Dir.TrimEnd('/') }))
            {
                string file = System.IO.Path.GetFileNameWithoutExtension(AssetDatabase.GUIDToAssetPath(guid));
                if (file.StartsWith(prefix)) list.Add(file);
            }
            return list.ToArray();
        }

        // ------------------------------------------------------------------ preparing the recordings

        /// <summary>Turns the downloaded recordings into game-ready clips in Prepared/: long runs of drips
        /// and knocks cut into single ones (the cleanest kept), every one-shot trimmed and brought to the
        /// same peak, loops brought to a level and their ends crossfaded into their starts so they never
        /// click. Run again after changing the recordings. Menu: BodycamHorror > Prepare Ambience Sounds.</summary>
        [MenuItem("BodycamHorror/Prepare Ambience Sounds")]
        public static void Prepare()
        {
            System.IO.Directory.CreateDirectory(Dir);
            foreach (string guid in AssetDatabase.FindAssets("t:AudioClip", new[] { SourceDir.TrimEnd('/') }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.StartsWith(Dir)) continue;
                var importer = (AudioImporter)AssetImporter.GetAtPath(path);
                var s = importer.defaultSampleSettings;
                if (s.loadType != AudioClipLoadType.DecompressOnLoad) { s.loadType = AudioClipLoadType.DecompressOnLoad; importer.defaultSampleSettings = s; importer.SaveAndReimport(); }
            }

            // single sounds, as they are
            foreach (string name in new[] { "drip_single", "scrape_stone", "scrape_pipe", "scrape_lever", "bang_pipe_concrete",
                                            "creak_metallic_groan", "creak_crank_groan", "creak_bending_metal", "creak_moaning_chair", "creak_truck_squeaks" })
            {
                var data = Mono(name, out int rate);
                if (data == null) continue;
                string outName = name.StartsWith("bang_") ? "knock_" + name.Substring(5) : name;
                Write(outName, Normalize(Trim(data, rate), 0.9f), rate, 1);
            }
            // runs of single sounds, cut apart
            Slice("bang_pipe_hammer", "knock_pipe", 8, 0.7f);
            Slice("drip_cave", "drip_cave", 10, 1.1f);
            Slice("drip_on_water", "drip_water", 8, 0.6f);
            // loops
            LoopClip("bed_basement_drone", "loop_bed_drone", false);
            LoopClip("bed_facility_hum", "loop_bed_hum", false);
            LoopClip("hum_bulb_buzz", "loop_hum_bulb", true);
            LoopClip("hum_fluorescent", "loop_hum_fluorescent", true);

            AssetDatabase.Refresh();
            Debug.Log("Ambience sounds prepared in " + Dir);
        }

        static float[] Samples(string name, out int rate, out int channels)
        {
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(SourceDir + name + ".mp3");
            rate = clip ? clip.frequency : 0;
            channels = clip ? clip.channels : 0;
            if (!clip) { Debug.LogWarning("Ambience: missing " + SourceDir + name + ".mp3"); return null; }
            var data = new float[clip.samples * clip.channels];
            return clip.GetData(data, 0) ? data : null;
        }

        static float[] Mono(string name, out int rate)
        {
            var data = Samples(name, out rate, out int channels);
            if (data == null || channels == 1) return data;
            var mono = new float[data.Length / channels];
            for (int i = 0; i < mono.Length; i++)
            {
                float sum = 0f;
                for (int c = 0; c < channels; c++) sum += data[i * channels + c];
                mono[i] = sum / channels;
            }
            return mono;
        }

        /// <summary>Peak level in 10 ms steps.</summary>
        static float[] Envelope(float[] d, int rate, out int step)
        {
            step = Mathf.Max(1, rate / 100);
            var env = new float[d.Length / step];
            for (int w = 0; w < env.Length; w++)
            {
                float m = 0f;
                for (int i = w * step, end = i + step; i < end; i++) m = Mathf.Max(m, Mathf.Abs(d[i]));
                env[w] = m;
            }
            return env;
        }

        /// <summary>Cuts the silence off both ends, with a soft fade at each.</summary>
        static float[] Trim(float[] d, int rate)
        {
            var env = Envelope(d, rate, out int step);
            float peak = 0f;
            foreach (float e in env) peak = Mathf.Max(peak, e);
            int first = 0, last = env.Length - 1;
            while (first < last && env[first] < peak * 0.02f) first++;
            while (last > first && env[last] < peak * 0.01f) last--;
            int a = Mathf.Max(0, (first - 1) * step), b = Mathf.Min(d.Length, (last + 2) * step);
            var cut = new float[b - a];
            System.Array.Copy(d, a, cut, 0, cut.Length);
            return Fade(cut, rate, 0.003f, 0.04f);
        }

        static float[] Fade(float[] d, int rate, float fadeIn, float fadeOut)
        {
            int fi = Mathf.Min(d.Length, (int)(fadeIn * rate)), fo = Mathf.Min(d.Length, (int)(fadeOut * rate));
            for (int i = 0; i < fi; i++) d[i] *= i / (float)fi;
            for (int i = 0; i < fo; i++) d[d.Length - 1 - i] *= i / (float)fo;
            return d;
        }

        static float[] Normalize(float[] d, float peak)
        {
            float m = 0.00001f;
            foreach (float s in d) m = Mathf.Max(m, Mathf.Abs(s));
            float k = peak / m;
            for (int i = 0; i < d.Length; i++) d[i] *= k;
            return d;
        }

        /// <summary>Finds each separate sound in a run (a rise after a quiet spell), keeps the
        /// <paramref name="keep"/> loudest, each with up to <paramref name="maxLength"/> s of its tail.</summary>
        static void Slice(string name, string outPrefix, int keep, float maxLength)
        {
            var d = Mono(name, out int rate);
            if (d == null) return;
            var env = Envelope(d, rate, out int step);
            float peak = 0f;
            foreach (float e in env) peak = Mathf.Max(peak, e);

            var starts = new System.Collections.Generic.List<int>();
            int quiet = 99;
            for (int w = 0; w < env.Length; w++)
            {
                if (env[w] > peak * 0.25f && quiet >= 12) starts.Add(w);
                quiet = env[w] < peak * 0.08f ? quiet + 1 : 0;
            }
            // loudest first
            starts.Sort((x, y) => LocalPeak(env, y).CompareTo(LocalPeak(env, x)));
            int made = 0;
            foreach (int w in starts)
            {
                if (made >= keep) break;
                int a = Mathf.Max(0, (w - 1) * step);
                int next = env.Length;
                foreach (int o in starts) if (o > w && o < next) next = o;
                int b = Mathf.Min(d.Length, Mathf.Min(next * step - step, a + (int)(maxLength * rate)));
                if (b - a < rate / 20) continue;
                var cut = new float[b - a];
                System.Array.Copy(d, a, cut, 0, cut.Length);
                made++;
                Write(outPrefix + "_" + made.ToString("00"), Normalize(Fade(cut, rate, 0.002f, Mathf.Min(0.25f, cut.Length / (float)rate * 0.4f)), 0.9f), rate, 1);
            }
        }

        static float LocalPeak(float[] env, int w)
        {
            float m = 0f;
            for (int i = w; i < Mathf.Min(env.Length, w + 10); i++) m = Mathf.Max(m, env[i]);
            return m;
        }

        /// <summary>A seamless loop: the last half second is faded into the first.</summary>
        static void LoopClip(string name, string outName, bool mono)
        {
            int rate, channels;
            float[] d;
            if (mono) { d = Mono(name, out rate); channels = 1; }
            else d = Samples(name, out rate, out channels);
            if (d == null) return;
            int frames = d.Length / channels, fade = Mathf.Min(frames / 4, rate / 2);
            var loop = new float[(frames - fade) * channels];
            for (int f = 0; f < frames - fade; f++)
                for (int c = 0; c < channels; c++)
                {
                    float s = d[f * channels + c];
                    if (f < fade)
                    {
                        float t = f / (float)fade;
                        s = s * Mathf.Sqrt(t) + d[(frames - fade + f) * channels + c] * Mathf.Sqrt(1f - t);
                    }
                    loop[f * channels + c] = s;
                }
            Write(outName, Normalize(loop, 0.8f), rate, channels);
        }

        /// <summary>16-bit PCM WAV.</summary>
        static void Write(string name, float[] d, int rate, int channels)
        {
            using (var w = new System.IO.BinaryWriter(System.IO.File.Create(Dir + name + ".wav")))
            {
                int bytes = d.Length * 2;
                w.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + bytes);
                w.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt ")); w.Write(16); w.Write((short)1); w.Write((short)channels);
                w.Write(rate); w.Write(rate * channels * 2); w.Write((short)(channels * 2)); w.Write((short)16);
                w.Write(System.Text.Encoding.ASCII.GetBytes("data")); w.Write(bytes);
                foreach (float s in d) w.Write((short)Mathf.Clamp(Mathf.RoundToInt(s * 32767f), -32768, 32767));
            }
        }

        static Transform[] Children(string rootName)
        {
            var root = GameObject.Find(rootName);
            if (!root) return new Transform[0];
            var list = new Transform[root.transform.childCount];
            for (int i = 0; i < list.Length; i++) list[i] = root.transform.GetChild(i);
            return list;
        }

        static Transform Group(Transform parent, string name)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            return t;
        }

        static void Loop(Transform parent, string name, Vector3 localPosition, float volume, float spatial, float minDistance, float maxDistance, string clip)
        {
            var a = Make(parent, name, localPosition, clip);
            if (!a) return;
            a.mode = AmbientSound.Mode.Loop;
            a.volume = volume;
            a.pitch = new Vector2(0.97f, 1.03f);
            a.spatialBlend = spatial;
            if (spatial > 0f) { a.minDistance = minDistance; a.maxDistance = maxDistance; }
            a.occlusion = spatial > 0f;
        }

        static void Shots(Transform parent, string name, Vector3 position, float volume, Vector2 interval, float radius,
                          float minDistance, float maxDistance, params string[] clips)
        {
            var a = Make(parent, name, position, clips);
            if (!a) return;
            a.mode = AmbientSound.Mode.Random;
            a.volume = volume;
            a.interval = interval;
            a.radius = radius;
            a.minDistance = minDistance;
            a.maxDistance = maxDistance;
            a.spatialBlend = 1f;
        }

        static AmbientSound Make(Transform parent, string name, Vector3 position, params string[] clipNames)
        {
            var clips = new System.Collections.Generic.List<AudioClip>();
            foreach (string c in clipNames)
            {
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(Dir + c + ".wav");
                if (clip) clips.Add(clip);
                else Debug.LogWarning("Ambience: missing " + Dir + c + ".wav");
            }
            if (clips.Count == 0) return null;
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            var a = go.AddComponent<AmbientSound>();
            a.clips = clips.ToArray();
            return a;
        }

        /// <summary>Beds stream (long), the rest sit decompressed in memory (short, played often);
        /// everything heard from a point is made mono, so it places properly.</summary>
        static void ImportSettings()
        {
            foreach (string guid in AssetDatabase.FindAssets("t:AudioClip", new[] { Dir.TrimEnd('/') }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var importer = (AudioImporter)AssetImporter.GetAtPath(path);
                bool bed = System.IO.Path.GetFileName(path).StartsWith("loop_bed_");
                var s = importer.defaultSampleSettings;
                var load = bed ? AudioClipLoadType.Streaming : AudioClipLoadType.CompressedInMemory;
                bool changed = s.loadType != load || s.compressionFormat != AudioCompressionFormat.Vorbis;
                s.loadType = load;
                s.compressionFormat = AudioCompressionFormat.Vorbis;
                s.quality = 0.8f;
                importer.defaultSampleSettings = s;
                if (changed) importer.SaveAndReimport();
            }
        }
    }
}
