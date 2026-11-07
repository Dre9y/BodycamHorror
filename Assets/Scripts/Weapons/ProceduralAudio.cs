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
