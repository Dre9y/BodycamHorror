using UnityEngine;

namespace BodycamHorror.AI
{
    /// <summary>Placeholder monster voice synthesised at startup, until real recordings are in.</summary>
    public static class MonsterSounds
    {
        const int Rate = 44100;
        static AudioClip screech, growl, chitter, hurt, step, death;

        public static AudioClip Screech => screech ? screech : screech = Make("MonsterScreech", 0.9f, 77, (t, n, s) =>
        {
            // two detuned rising saws through noise: a wet shriek
            float f = 520f + 900f * Mathf.Sqrt(t) - 300f * t;
            float saw = Saw(f * t) * 0.5f + Saw(f * 1.07f * t) * 0.5f;
            float env = Mathf.Clamp01(t / 0.03f) * Mathf.Exp(-t * 2.2f);
            return (saw * 0.7f + s * 0.6f) * env;
        });

        public static AudioClip Growl => growl ? growl : growl = Make("MonsterGrowl", 1.4f, 13, (t, n, s) =>
        {
            float f = 62f + 8f * Mathf.Sin(t * 9f);
            float pulse = 0.55f + 0.45f * Mathf.Sin(t * 2f * Mathf.PI * 11f);   // vocal fry
            float env = Mathf.Clamp01(t / 0.15f) * Mathf.Clamp01((1.4f - t) / 0.4f);
            return (Saw(f * t) * 0.8f + s * 0.5f) * pulse * env;
        });

        public static AudioClip Chitter => chitter ? chitter : chitter = Make("MonsterChitter", 0.5f, 41, (t, n, s) =>
        {
            // fast clicks, like teeth chattering
            float clickPhase = (t * 26f) % 1f;
            float click = Mathf.Exp(-clickPhase * 30f) * Mathf.Sin(2f * Mathf.PI * 2400f * t);
            return (click * 0.9f + n * Mathf.Exp(-clickPhase * 40f) * 0.4f) * Mathf.Clamp01((0.5f - t) / 0.15f);
        });

        public static AudioClip Hurt => hurt ? hurt : hurt = Make("MonsterHurt", 0.45f, 5, (t, n, s) =>
        {
            float f = 380f - 260f * t;
            return (Saw(f * t) * 0.6f + s * 0.7f) * Mathf.Clamp01(t / 0.01f) * Mathf.Exp(-t * 7f);
        });

        public static AudioClip Death => death ? death : death = Make("MonsterDeath", 1.6f, 9, (t, n, s) =>
        {
            float f = 300f * Mathf.Exp(-t * 1.2f) + 40f;
            return (Saw(f * t) * 0.7f + s * 0.5f) * Mathf.Clamp01(t / 0.02f) * Mathf.Exp(-t * 1.8f);
        });

        public static AudioClip Step => step ? step : step = Make("MonsterStep", 0.12f, 3, (t, n, s) =>
            (n * 0.6f + Mathf.Sin(2f * Mathf.PI * 90f * t)) * Mathf.Exp(-t * 45f));

        static float Saw(float phase) => 2f * (phase - Mathf.Floor(phase + 0.5f));

        delegate float Synth(float t, float noise, float smoothNoise);

        static AudioClip Make(string name, float length, int seed, Synth synth)
        {
            int count = (int)(Rate * length);
            var d = new float[count];
            var rng = new System.Random(seed);
            float lp = 0f, max = 0.0001f;
            for (int i = 0; i < count; i++)
            {
                float n = (float)(rng.NextDouble() * 2.0 - 1.0);
                lp += (n - lp) * 0.15f;
                d[i] = (float)System.Math.Tanh(synth(i / (float)Rate, n, lp * 3f) * 1.5f);
                max = Mathf.Max(max, Mathf.Abs(d[i]));
            }
            for (int i = 0; i < count; i++) d[i] *= 0.9f / max;
            var clip = AudioClip.Create(name, count, 1, Rate, false);
            clip.SetData(d, 0);
            return clip;
        }
    }
}
