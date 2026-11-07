using UnityEngine;

namespace BodycamHorror.AI
{
    /// <summary>Placeholder monster voice synthesised at startup, until real recordings are in.</summary>
    public static class MonsterSounds
    {
        const int Rate = 44100;
        static AudioClip screech, growl, chitter, hurt, death;

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

        // ------------------------------------------------------------------ footsteps

        const int StepVariants = 6;
        static AudioClip[] steps;

        /// <summary>A random footstep variant (bare padded foot with claws on concrete).</summary>
        public static AudioClip RandomStep()
        {
            if (steps == null || steps.Length != StepVariants) steps = new AudioClip[StepVariants];
            int i = Random.Range(0, StepVariants);
            if (!steps[i]) steps[i] = MakeStep(i);
            return steps[i];
        }

        /// <summary>
        /// Heel-then-toe contact: a soft low thump of the padded heel, a damp fleshy slap, a couple of
        /// claw ticks on the toe roll and a short gritty scuff on concrete. Each variant shifts timings,
        /// pitch and balance so repeated steps don't machine-gun.
        /// </summary>
        static AudioClip MakeStep(int variant)
        {
            var rng = new System.Random(1000 + variant * 7919);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);

            const float length = 0.32f;
            int count = (int)(Rate * length);
            var d = new float[count];

            float toeDelay = R(0.025f, 0.055f);        // heel -> toe roll
            float thumpFreq = R(55f, 80f);
            float slapCenter = R(700f, 1300f);
            float clawFreq = R(3800f, 5600f);
            float claw2 = toeDelay + R(0.008f, 0.02f);
            float scuffAmt = R(0.15f, 0.4f);
            float heelAmt = R(0.8f, 1f), toeAmt = R(0.45f, 0.7f);

            // state-variable band-pass for the slap, one-poles for the grit
            float bpLow = 0f, bpBand = 0f, gritLp = 0f, gritSlow = 0f, prevN = 0f;
            float fSlap = 2f * Mathf.Sin(Mathf.PI * slapCenter / Rate), q = 0.9f;

            for (int i = 0; i < count; i++)
            {
                float t = i / (float)Rate;
                float n = (float)(rng.NextDouble() * 2.0 - 1.0);

                // band-passed noise = damp fleshy contact
                bpLow += fSlap * bpBand;
                float high = n - bpLow - q * bpBand;
                bpBand += fSlap * high;
                float slap = bpBand * (Env(t, 0.0015f, 70f) * heelAmt + Env(t - toeDelay, 0.001f, 90f) * toeAmt);

                // weight landing: pitch-dropping sine, felt more than heard
                float tf = Mathf.Max(t, 0f);
                float thumpPhase = 2f * Mathf.PI * (thumpFreq * tf - 18f * tf * tf);
                float thump = Mathf.Sin(thumpPhase) * Env(t, 0.003f, 28f) * 1.1f
                            + Mathf.Sin(thumpPhase * 1.5f) * Env(t - toeDelay, 0.002f, 40f) * 0.35f;

                // claw ticks: very short high resonant clicks on the toe roll
                float hp = n - prevN; prevN = n;
                float claws = (Mathf.Sin(2f * Mathf.PI * clawFreq * Mathf.Max(t - toeDelay, 0f)) * 0.5f + hp * 0.5f) * Env(t - toeDelay, 0.0003f, 380f) * 0.35f
                            + (Mathf.Sin(2f * Mathf.PI * clawFreq * 1.17f * Mathf.Max(t - claw2, 0f)) * 0.5f + hp * 0.5f) * Env(t - claw2, 0.0003f, 420f) * 0.22f;

                // gritty scuff as the foot drags a little on concrete
                // band-limited grit: fast low-pass minus slow low-pass
                gritLp += (n - gritLp) * 0.35f;
                gritSlow += (gritLp - gritSlow) * 0.04f;
                float gritHp = gritLp - gritSlow;
                float scuffEnv = Env(t - toeDelay * 0.6f, 0.012f, 22f);
                float scuff = gritHp * scuffEnv * scuffAmt * (0.6f + 0.4f * Mathf.Sin(t * 260f + variant));

                d[i] = slap * 2.2f + thump + claws + scuff;
            }

            // soft clip, normalise low: steps sit well under the voice in the mix
            float max = 0.0001f;
            for (int i = 0; i < count; i++) { d[i] = (float)System.Math.Tanh(d[i] * 1.2f); max = Mathf.Max(max, Mathf.Abs(d[i])); }
            for (int i = 0; i < count; i++) d[i] *= 0.7f / max;

            var clip = AudioClip.Create("MonsterStep" + variant, count, 1, Rate, false);
            clip.SetData(d, 0);
            return clip;
        }

        /// <summary>Attack/decay envelope starting at t = 0 (zero before).</summary>
        static float Env(float t, float attack, float decay) =>
            t < 0f ? 0f : Mathf.Clamp01(t / attack) * Mathf.Exp(-t * decay);

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
