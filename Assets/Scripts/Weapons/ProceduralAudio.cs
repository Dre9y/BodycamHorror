using UnityEngine;

namespace BodycamHorror.Weapons
{
    /// <summary>Placeholder weapon sounds synthesised at startup, until real recordings are in.</summary>
    public static class ProceduralAudio
    {
        const int Rate = 44100;

        public static AudioClip Gunshot()
        {
            const float length = 0.9f;
            int n = (int)(Rate * length);
            var d = new float[n];
            var rng = new System.Random(1234);
            float rumbleLp = 0f, bodyLp = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                rumbleLp += (noise - rumbleLp) * 0.06f;
                bodyLp += (noise - bodyLp) * 0.3f;

                float crack = noise * Mathf.Exp(-t * 90f);
                float body = bodyLp * Mathf.Exp(-t * 14f) * 1.6f;
                float rumble = rumbleLp * Mathf.Exp(-t * 4f) * 4f;          // room tail
                float thump = Mathf.Sin(2f * Mathf.PI * (75f - 35f * t) * t) * Mathf.Exp(-t * 18f);
                float attack = Mathf.Clamp01(t / 0.0008f);
                d[i] = (crack * 0.9f + body + rumble + thump) * attack;
            }
            return Finish("Gunshot", d, 0.95f, 1.6f);
        }

        public static AudioClip ShotgunBlast()
        {
            const float length = 1.3f;
            int n = (int)(Rate * length);
            var d = new float[n];
            var rng = new System.Random(4321);
            float rumbleLp = 0f, bodyLp = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                rumbleLp += (noise - rumbleLp) * 0.035f;
                bodyLp += (noise - bodyLp) * 0.2f;

                float crack = noise * Mathf.Exp(-t * 60f);
                float body = bodyLp * Mathf.Exp(-t * 9f) * 2.2f;
                float rumble = rumbleLp * Mathf.Exp(-t * 2.6f) * 6f;           // long room tail
                float thump = Mathf.Sin(2f * Mathf.PI * (60f - 30f * t) * t) * Mathf.Exp(-t * 10f) * 1.4f;
                float attack = Mathf.Clamp01(t / 0.001f);
                d[i] = (crack + body + rumble + thump) * attack;
            }
            return Finish("ShotgunBlast", d, 0.98f, 2f);
        }

        /// <summary>Air swish of a swing.</summary>
        public static AudioClip Whoosh()
        {
            const float length = 0.28f;
            int n = (int)(Rate * length);
            var d = new float[n];
            var rng = new System.Random(77);
            float lp = 0f, lp2 = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float p = t / length;
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                float cutoff = Mathf.Lerp(0.03f, 0.25f, Mathf.Sin(p * Mathf.PI));   // sweeps up and back down
                lp += (noise - lp) * cutoff;
                lp2 += (lp - lp2) * cutoff;
                d[i] = (lp - lp2) * Mathf.Sin(p * Mathf.PI) * 6f;                 // band-passed, swelling
            }
            return Finish("Whoosh", d, 0.6f, 1f);
        }

        /// <summary>Dull body hit.</summary>
        public static AudioClip Thud()
        {
            const float length = 0.22f;
            int n = (int)(Rate * length);
            var d = new float[n];
            var rng = new System.Random(99);
            float lp = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                lp += (noise - lp) * 0.12f;
                float knock = Mathf.Sin(2f * Mathf.PI * (110f - 120f * t) * t) * Mathf.Exp(-t * 28f);
                d[i] = knock * 1.3f + lp * Mathf.Exp(-t * 45f) * 2.5f + noise * Mathf.Exp(-t * 300f) * 0.4f;
            }
            return Finish("Thud", d, 0.9f, 1.4f);
        }

        /// <summary>Wet, rattling growl of an infected (seed varies the voice).</summary>
        public static AudioClip InfectedGrowl(int seed)
        {
            var r = new System.Random(seed);
            float f0 = 70f + (float)r.NextDouble() * 45f;
            return Voice("InfectedGrowl", 1.3f + (float)r.NextDouble() * 0.6f, seed,
                         p => f0 * (1f + 0.15f * Mathf.Sin(p * 9f)), 0.75f, 6f + (float)r.NextDouble() * 4f, 0.8f);
        }

        /// <summary>Shriek when an infected spots you.</summary>
        public static AudioClip InfectedShriek(int seed)
        {
            var r = new System.Random(seed * 7 + 1);
            float peak = 520f + (float)r.NextDouble() * 260f;
            return Voice("InfectedShriek", 0.9f + (float)r.NextDouble() * 0.3f, seed + 11,
                         p => Mathf.Lerp(260f, peak, Mathf.Sin(Mathf.Min(p * 1.6f, 1f) * Mathf.PI * 0.5f)) * (1f - 0.25f * p),
                         0.55f, 18f, 0.95f);
        }

        /// <summary>Short effort grunt (attack swing, getting up).</summary>
        public static AudioClip InfectedGrunt(int seed) =>
            Voice("InfectedGrunt", 0.35f, seed + 23, p => Mathf.Lerp(150f, 85f, p), 0.6f, 0f, 0.85f);

        /// <summary>Rough voiced sound: a sawtooth at the f0 curve, roughened with noise, tremolo and a soft fade.</summary>
        static AudioClip Voice(string name, float length, int seed, System.Func<float, float> f0, float rasp, float tremoloHz, float peak)
        {
            int n = (int)(Rate * length);
            var d = new float[n];
            var rng = new System.Random(seed);
            float phase = 0f, lp = 0f, lp2 = 0f, jitter = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float p = t / length;
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                jitter += (noise - jitter) * 0.002f;                       // slow pitch wobble
                phase += f0(p) * (1f + jitter * 0.6f) / Rate;
                phase -= Mathf.Floor(phase);
                float saw = phase * 2f - 1f;
                float voiced = saw * (1f - rasp) + saw * noise * rasp;     // throat rattle
                lp += (voiced - lp) * 0.25f;                               // muffle the buzz
                lp2 += (lp - lp2) * 0.35f;
                float env = Mathf.Clamp01(p / 0.08f) * Mathf.Clamp01((1f - p) / 0.25f);
                float trem = tremoloHz > 0f ? 0.75f + 0.25f * Mathf.Sin(2f * Mathf.PI * tremoloHz * t) : 1f;
                d[i] = lp2 * env * trem;
            }
            return Finish(name, d, peak, 2.2f);
        }

        public static AudioClip Click(float length, float freq, float amp) => Make("Click", length, 0f, freq, amp);

        public static AudioClip DoubleClick(float gap, float freq, float amp) => Make("DoubleClick", gap + 0.06f, gap, freq, amp);

        static AudioClip Make(string name, float length, float secondAt, float freq, float amp)
        {
            int n = (int)(Rate * length);
            var d = new float[n];
            var rng = new System.Random(name.GetHashCode() ^ (int)freq);
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                d[i] = ClickAt(t, noise, freq);
                if (secondAt > 0f && t >= secondAt) d[i] += ClickAt(t - secondAt, noise, freq * 0.8f) * 1.2f;
            }
            return Finish(name, d, amp, 1f);
        }

        static float ClickAt(float t, float noise, float freq) =>
            noise * Mathf.Exp(-t * 420f) + Mathf.Sin(2f * Mathf.PI * freq * t) * Mathf.Exp(-t * 260f) * 0.6f;

        static AudioClip Finish(string name, float[] d, float peak, float drive)
        {
            float max = 0.0001f;
            for (int i = 0; i < d.Length; i++)
            {
                d[i] = (float)System.Math.Tanh(d[i] * drive);   // soft clip
                max = Mathf.Max(max, Mathf.Abs(d[i]));
            }
            float k = peak / max;
            for (int i = 0; i < d.Length; i++) d[i] *= k;

            var clip = AudioClip.Create(name, d.Length, 1, Rate, false);
            clip.SetData(d, 0);
            return clip;
        }
    }
}
