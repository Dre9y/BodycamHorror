using UnityEditor;
using UnityEngine;

namespace BodycamHorror.EditorTools
{
    /// <summary>
    /// The light monster's scare scream, made from "Death Scream Sound Effect.mp3" (Assets/Audio): the scream taken down
    /// in pitch, an octave under it at the same length, a growl of ring modulation through it, driven into distortion and
    /// left ringing in a long cavern of reverb. Writes Assets/Audio/Monster/death_scream_infernal.wav.
    /// Menu: BodycamHorror > Audio > Make Infernal Death Scream (rerunning writes over it).
    /// </summary>
    public static class InfernalScream
    {
        const string Source = "Assets/Audio/Death Scream Sound Effect.mp3";
        public const string OutPath = "Assets/Audio/Monster/death_scream_infernal.wav";

        [MenuItem("BodycamHorror/Audio/Make Infernal Death Scream")]
        public static AudioClip Make()
        {
            var importer = (AudioImporter)AssetImporter.GetAtPath(Source);
            if (!importer) { Debug.LogError("Missing " + Source); return null; }
            var s = importer.defaultSampleSettings;
            if (s.loadType != AudioClipLoadType.DecompressOnLoad)
            {
                s.loadType = AudioClipLoadType.DecompressOnLoad;   // so its samples can be read
                importer.defaultSampleSettings = s;
                importer.SaveAndReimport();
            }
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(Source);
            int rate = clip.frequency, ch = clip.channels;
            var raw = new float[clip.samples * ch];
            clip.GetData(raw, 0);
            var x = new float[clip.samples];
            for (int i = 0; i < x.Length; i++) { float sum = 0f; for (int c = 0; c < ch; c++) sum += raw[i * ch + c]; x[i] = sum / ch; }
            x = Trim(x, rate, 0.03f);   // a scare hits at once: no quiet lead-in (the recording has about a second of it)

            // the scream itself, lower and slower (a bigger throat)
            float[] a = Resample(x, 0.8f);
            // an octave under it, kept to its length: the voice of something huge under the scream
            float[] b = PitchShift(a, 0.5f, rate, 0.07f);
            // a fifth under that again, faint, for the rumble
            float[] c2 = PitchShift(a, 0.67f, rate, 0.05f);
            int n = a.Length + (int)(rate * 2.2f);   // room for the cavern's tail
            var mix = new float[n];
            float lpB = 0f;
            for (int i = 0; i < a.Length; i++)
            {
                float t = i / (float)rate;
                lpB += (b[i] - lpB) * 0.18f;                                      // the deep layer dark
                float ring = a[i] * Mathf.Sin(2f * Mathf.PI * 38f * t);          // a growl through it
                float v = a[i] * 0.85f + lpB * 1.1f + c2[i] * 0.35f + ring * 0.45f;
                mix[i] = (float)System.Math.Tanh(v * 2.6f) * 0.8f;              // driven hard, rounded off
            }
            float[] wet = Reverb(mix, rate);
            for (int i = 0; i < n; i++) mix[i] = mix[i] * 0.78f + wet[i] * 0.55f;
            Normalize(mix, 0.95f);
            mix = Trim(mix, rate, 0.004f);   // and nothing after the cavern has rung out
            n = mix.Length;
            int fade = Mathf.Min(n / 3, (int)(rate * 0.8f));
            for (int i = 0; i < fade; i++) mix[n - 1 - i] *= i / (float)fade;
            Write(OutPath, mix, rate);
            AssetDatabase.ImportAsset(OutPath, ImportAssetOptions.ForceUpdate);
            var outImp = (AudioImporter)AssetImporter.GetAtPath(OutPath);
            var os = outImp.defaultSampleSettings;
            os.loadType = AudioClipLoadType.DecompressOnLoad;   // played the instant the scare goes off
            outImp.defaultSampleSettings = os;
            outImp.SaveAndReimport();
            var made = AssetDatabase.LoadAssetAtPath<AudioClip>(OutPath);
            Debug.Log($"Infernal scream: {OutPath} ({made.length:F2} s)");
            return made;
        }

        /// <summary>Played back at <paramref name="speed"/> (lower and longer below 1).</summary>
        static float[] Resample(float[] x, float speed)
        {
            var y = new float[(int)(x.Length / speed)];
            for (int i = 0; i < y.Length; i++)
            {
                float p = i * speed; int k = (int)p; float f = p - k;
                float s0 = x[Mathf.Min(k, x.Length - 1)], s1 = x[Mathf.Min(k + 1, x.Length - 1)];
                y[i] = s0 + (s1 - s0) * f;
            }
            return y;
        }

        /// <summary>
        /// Pitch moved by <paramref name="ratio"/>, the length kept: two read heads sliding through a short window of the
        /// past at the new speed, cross-faded so the jumps back are not heard.
        /// </summary>
        static float[] PitchShift(float[] x, float ratio, int rate, float window)
        {
            int w = Mathf.Max(64, (int)(rate * window));
            var y = new float[x.Length];
            float phase = 0f, step = (1f - ratio) / w;   // each head's delay grows (pitch down) from 0 to w
            for (int i = 0; i < x.Length; i++)
            {
                phase = (phase + step) % 1f; if (phase < 0f) phase += 1f;
                float p2 = (phase + 0.5f) % 1f;
                float d1 = phase * w, d2 = p2 * w;
                float g1 = Mathf.Sin(Mathf.PI * phase), g2 = Mathf.Sin(Mathf.PI * p2);
                y[i] = Tap(x, i - d1) * g1 + Tap(x, i - d2) * g2;
            }
            return y;
        }

        static float Tap(float[] x, float p)
        {
            if (p < 0f) return 0f;
            int k = (int)p; float f = p - k;
            if (k + 1 >= x.Length) return 0f;
            return x[k] + (x[k + 1] - x[k]) * f;
        }

        /// <summary>A long dark room: four damped combs in parallel, two all-passes after them.</summary>
        static float[] Reverb(float[] x, int rate)
        {
            float k = rate / 44100f;
            int[] combs = { (int)(1557 * k * 1.6f), (int)(1617 * k * 1.6f), (int)(1491 * k * 1.6f), (int)(1422 * k * 1.6f) };
            var y = new float[x.Length];
            foreach (int len in combs)
            {
                var buf = new float[len]; int idx = 0; float lp = 0f;
                for (int i = 0; i < x.Length; i++)
                {
                    float o = buf[idx];
                    lp += (o - lp) * 0.35f;              // the walls swallow the highs
                    buf[idx] = x[i] + lp * 0.86f;
                    idx = (idx + 1) % len;
                    y[i] += o * 0.25f;
                }
            }
            foreach (int len in new[] { (int)(556 * k), (int)(441 * k) })
            {
                var buf = new float[len]; int idx = 0;
                for (int i = 0; i < y.Length; i++)
                {
                    float o = buf[idx];
                    float v = y[i] + o * 0.5f;
                    buf[idx] = v;
                    y[i] = o - v * 0.5f;
                    idx = (idx + 1) % len;
                }
            }
            return y;
        }

        /// <summary>The quiet cut off both ends (below <paramref name="floor"/> of the peak, in 10 ms steps; 5 ms kept before the start).</summary>
        static float[] Trim(float[] d, int rate, float floor)
        {
            float peak = 1e-5f;
            foreach (float v in d) peak = Mathf.Max(peak, Mathf.Abs(v));
            float th = peak * floor;
            int step = Mathf.Max(1, rate / 100), a = 0, b = d.Length;
            bool Loud(int from) { for (int i = from; i < Mathf.Min(d.Length, from + step); i++) if (Mathf.Abs(d[i]) > th) return true; return false; }
            while (a < d.Length && !Loud(a)) a += step;
            while (b > a && !Loud(Mathf.Max(0, b - step))) b -= step;
            a = Mathf.Max(0, a - rate / 200);
            var y = new float[Mathf.Max(1, b - a)];
            System.Array.Copy(d, a, y, 0, y.Length);
            return y;
        }

        static void Normalize(float[] d, float peak)
        {
            float m = 1e-5f;
            foreach (float v in d) m = Mathf.Max(m, Mathf.Abs(v));
            for (int i = 0; i < d.Length; i++) d[i] *= peak / m;
        }

        /// <summary>16-bit mono PCM WAV.</summary>
        static void Write(string path, float[] d, int rate)
        {
            using (var w = new System.IO.BinaryWriter(System.IO.File.Create(path)))
            {
                int bytes = d.Length * 2;
                w.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + bytes);
                w.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt ")); w.Write(16); w.Write((short)1); w.Write((short)1);
                w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
                w.Write(System.Text.Encoding.ASCII.GetBytes("data")); w.Write(bytes);
                foreach (float s in d) w.Write((short)Mathf.Clamp(Mathf.RoundToInt(s * 32767f), -32768, 32767));
            }
        }
    }
}
