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

        /// <summary>
        /// Turning to stone: a low thump, then a crackling of stone and bone that thins out over most of a second, over a
        /// dry grinding rumble that sinks in pitch (the light monster caught by the flashlight).
        /// </summary>
        public static AudioClip Petrify(int seed)
        {
            const float length = 0.95f;
            int n = (int)(Rate * length);
            var d = new float[n];
            var rng = new System.Random(seed);
            float lp = 0f, lp2 = 0f, crack = 0f, crackLp = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                // grinding: low-passed noise, its rumble slowing and dropping
                float cut = Mathf.Lerp(0.05f, 0.012f, t / length);
                lp += (noise - lp) * cut;
                lp2 += (lp - lp2) * cut;
                float grind = lp2 * (0.6f + 0.4f * Mathf.Sin(2f * Mathf.PI * (38f - 20f * t) * t)) * Mathf.Exp(-t * 2.2f) * 9f;
                // crackles: sharp ticks, dense at first and thinning out
                float density = 0.012f * Mathf.Exp(-t * 3.5f) + 0.0006f;
                if (rng.NextDouble() < density) crack = (float)(rng.NextDouble() * 0.7 + 0.3) * (rng.NextDouble() < 0.5 ? -1f : 1f);
                crack *= 0.93f;
                crackLp += (crack * noise - crackLp) * 0.45f;
                float thump = Mathf.Sin(2f * Mathf.PI * (70f - 60f * t) * t) * Mathf.Exp(-t * 18f);
                d[i] = thump * 1.1f + grind + crackLp * 1.6f;
            }
            // a short fade at the end
            for (int i = n - 2000; i < n; i++) d[i] *= (n - i) / 2000f;
            return Finish("Petrify", d, 0.95f, 1.3f);
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

        /// <summary>A jump-scare sting: a hit of noise and a sub boom right on the attack, a cluster of
        /// screeching detuned high voices that bends upward and shudders, and a dissonant low drone
        /// under it, all dying away over a couple of seconds. Meant to be played loud and flat (2D).</summary>
        public static AudioClip ScareStinger()
        {
            const float length = 2.8f;
            int n = (int)(Rate * length);
            var d = new float[n];
            var rng = new System.Random(666);
            float[] high = { 1180f, 1243f, 1318f, 1402f, 1489f, 1571f };   // a tight, ugly cluster
            float[] low = { 98f, 103.8f, 146.8f, 155.6f };                 // tritones and seconds
            var hp = new float[high.Length];
            var lpPh = new float[low.Length];
            float subPhase = 0f, hitLp = 0f, screechLp = 0f, wobble = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);

                // the hit: a burst of dark noise and a falling sub boom
                hitLp += (noise - hitLp) * 0.18f;
                float hit = hitLp * Mathf.Exp(-t * 14f) * 1.6f;
                subPhase += Mathf.Lerp(62f, 28f, Mathf.Clamp01(t / 0.9f)) / Rate;
                float sub = Mathf.Sin(2f * Mathf.PI * subPhase) * Mathf.Exp(-t * 2.2f) * 1.3f;

                // the screech: saws sliding up a little, rough tremolo, fading out
                wobble += (noise - wobble) * 0.0015f;
                float bend = 1f + 0.12f * Mathf.Clamp01(t / 1.2f) + wobble * 0.05f;
                float screech = 0f;
                for (int v = 0; v < high.Length; v++)
                {
                    hp[v] += high[v] * bend * (1f + 0.004f * v) / Rate;
                    hp[v] -= Mathf.Floor(hp[v]);
                    screech += hp[v] * 2f - 1f;
                }
                screech /= high.Length;
                screech = screech * 0.75f + screech * noise * 0.35f;            // grit
                screechLp += (screech - screechLp) * 0.45f;
                float trem = 0.65f + 0.35f * Mathf.Sin(2f * Mathf.PI * 14f * t);
                float screechEnv = Mathf.Clamp01(t / 0.015f) * Mathf.Exp(-t * 1.1f);

                // the drone under it
                float drone = 0f;
                for (int v = 0; v < low.Length; v++)
                {
                    lpPh[v] += low[v] / Rate;
                    lpPh[v] -= Mathf.Floor(lpPh[v]);
                    drone += lpPh[v] * 2f - 1f;
                }
                drone = drone / low.Length * Mathf.Clamp01(t / 0.05f) * Mathf.Exp(-t * 0.9f) * 0.6f;

                float tail = Mathf.Clamp01((length - t) / 0.4f);
                d[i] = (hit + sub + screechLp * screechEnv * trem + drone) * tail;
            }
            return Finish("ScareStinger", d, 1f, 2.4f);
        }

        /// <summary>
        /// Sobbing: two to four broken, wavering wails, each after a few shaky, hiccuping breaths in.
        /// A child-like voice through a mouth that is not quite right. Every seed cries differently.
        /// </summary>
        public static AudioClip CryingSob(int seed)
        {
            var rng = new System.Random(seed * 7919 + 101);
            float R() => (float)rng.NextDouble();

            // the plan: breaths in (hiccups) and wails, one after another
            var parts = new System.Collections.Generic.List<(float start, float length, bool wail, float from, float to)>();
            float at = 0.08f;
            int wails = 2 + rng.Next(3);
            for (int w = 0; w < wails; w++)
            {
                int hiccups = 1 + rng.Next(3);
                for (int h = 0; h < hiccups; h++)
                {
                    parts.Add((at, 0.08f + 0.06f * R(), false, 0f, 0f));
                    at += 0.13f + 0.09f * R();
                }
                float length = 0.6f + 1.0f * R();
                float top = 340f + 140f * R();
                parts.Add((at, length, true, top, top * (0.6f + 0.12f * R())));
                at += length + 0.12f + 0.4f * R();
            }

            int n = (int)(Rate * (at + 0.25f));
            var d = new float[n];
            foreach (var part in parts)
            {
                int s0 = (int)(part.start * Rate), len = (int)(part.length * Rate);
                float phase = 0f, jitter = 0f, b1 = 0f, l1 = 0f, b2 = 0f, l2 = 0f, b3 = 0f, l3 = 0f;
                float catchRate = 2.4f + 1.6f * R(), catchPhase = R() * 6.28f;
                for (int j = 0; j < len && s0 + j < n; j++)
                {
                    float t = j / (float)Rate, p = j / (float)len;
                    float noise = R() * 2f - 1f;
                    float x;
                    if (part.wail)
                    {
                        // the voice: a falling, shaking pitch with breaks where the sob catches in the throat
                        jitter += (noise - jitter) * 0.003f;
                        float f0 = Mathf.Lerp(part.from, part.to, Mathf.Pow(p, 0.7f))
                                   * (1f + 0.04f * Mathf.Sin(2f * Mathf.PI * 6.3f * t) + jitter * 0.08f);
                        phase += f0 / Rate;
                        phase -= Mathf.Floor(phase);
                        float glottal = (phase * 2f - 1f) * 0.8f + noise * 0.3f;      // breathy
                        // "ooh / uh": two formants (state variable band-passes)
                        float fa = 2f * Mathf.Sin(Mathf.PI * Mathf.Lerp(780f, 520f, p) / Rate);
                        float fb = 2f * Mathf.Sin(Mathf.PI * 1150f / Rate);
                        float hi1 = glottal - l1 - 0.35f * b1; b1 += fa * hi1; l1 += fa * b1;
                        float hi2 = glottal - l2 - 0.45f * b2; b2 += fb * hi2; l2 += fb * b2;
                        float catchDip = 0.45f + 0.55f * Mathf.Pow(Mathf.Abs(Mathf.Sin(Mathf.PI * catchRate * t + catchPhase)), 0.35f);
                        float env = Mathf.Clamp01(p / 0.07f) * Mathf.Pow(Mathf.Clamp01((1f - p) / 0.4f), 1.4f);
                        x = (b1 * 0.9f + b2 * 0.45f) * catchDip * env * 0.55f;
                    }
                    else
                    {
                        // a breath in: rising hiss with a faint squeak
                        float fc = 2f * Mathf.Sin(Mathf.PI * Mathf.Lerp(1400f, 2300f, p) / Rate);
                        float hi3 = noise - l3 - 0.3f * b3; b3 += fc * hi3; l3 += fc * b3;
                        phase += Mathf.Lerp(700f, 950f, p) / Rate;
                        phase -= Mathf.Floor(phase);
                        float env = Mathf.Clamp01(p / 0.15f) * Mathf.Clamp01((1f - p) / 0.3f);
                        x = (b3 * 0.5f + Mathf.Sin(2f * Mathf.PI * phase) * 0.08f) * env;
                    }
                    d[s0 + j] += x;
                }
            }
            return Finish("CryingSob", d, 0.9f, 1.6f);
        }

        /// <summary>One tap of a hard fingertip on the floor: the crawling head's step.</summary>
        public static AudioClip Skitter(int seed)
        {
            const float length = 0.06f;
            int n = (int)(Rate * length);
            var d = new float[n];
            var rng = new System.Random(seed + 4242);
            float fc = 2f * Mathf.Sin(Mathf.PI * (2200f + 1600f * (float)rng.NextDouble()) / Rate);
            float tone = 1400f + 900f * (float)rng.NextDouble();
            float b = 0f, l = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                float hi = noise - l - 0.25f * b; b += fc * hi; l += fc * b;
                d[i] = (b * 0.8f + Mathf.Sin(2f * Mathf.PI * tone * t) * 0.4f) * Mathf.Exp(-t * 110f);
            }
            return Finish("Skitter", d, 0.8f, 1.5f);
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

        /// <summary>Handle pressed, latch drawn back, then the hinges complain as the door swings.</summary>
        public static AudioClip DoorOpen()
        {
            var d = new float[(int)(Rate * 1.05f)];
            Latch(d, 0f, 850f, 0.7f, 11);
            Latch(d, 0.075f, 1500f, 1f, 12);
            Creak(d, 0.14f, 0.85f, 21, 60f, 125f, 1f);
            return Finish("DoorOpen", d, 0.85f, 1.3f);
        }

        /// <summary>Hinges alone: a door starting to swing shut.</summary>
        public static AudioClip DoorCreak()
        {
            var d = new float[(int)(Rate * 0.6f)];
            Creak(d, 0f, 0.58f, 34, 110f, 70f, 1f);
            return Finish("DoorCreak", d, 0.6f, 1.3f);
        }

        /// <summary>The door meeting its frame: a dull knock and the latch snapping home.</summary>
        public static AudioClip DoorShut()
        {
            const float length = 0.4f;
            int n = (int)(Rate * length);
            var d = new float[n];
            var rng = new System.Random(57);
            float lp = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                lp += (noise - lp) * 0.08f;
                float knock = Mathf.Sin(2f * Mathf.PI * (95f - 60f * t) * t) * Mathf.Exp(-t * 22f);
                float panel = Mathf.Sin(2f * Mathf.PI * 230f * t) * Mathf.Exp(-t * 38f) * 0.35f;
                d[i] = knock * 1.2f + panel + lp * Mathf.Exp(-t * 30f) * 2.2f + noise * Mathf.Exp(-t * 260f) * 0.35f;
            }
            Latch(d, 0.035f, 1300f, 0.9f, 58);
            return Finish("DoorShut", d, 0.9f, 1.4f);
        }

        static void Latch(float[] d, float at, float freq, float gain, int seed)
        {
            var rng = new System.Random(seed);
            int start = (int)(at * Rate);
            int n = Mathf.Min((int)(0.06f * Rate), d.Length - start);
            for (int i = 0; i < n; i++)
                d[start + i] += ClickAt(i / (float)Rate, (float)(rng.NextDouble() * 2.0 - 1.0), freq) * gain;
        }

        /// <summary>Dry hinge: stick-slip pulses (rate sliding from one to the other, Hz) ringing through two resonances.</summary>
        static void Creak(float[] d, float at, float length, int seed, float rateFrom, float rateTo, float gain)
        {
            var rng = new System.Random(seed);
            int start = (int)(at * Rate);
            int n = Mathf.Min((int)(length * Rate), d.Length - start);
            float k1 = 2f * Mathf.Sin(Mathf.PI * 640f / Rate), k2 = 2f * Mathf.Sin(Mathf.PI * 1720f / Rate);
            const float damp = 0.07f;
            float low1 = 0f, band1 = 0f, low2 = 0f, band2 = 0f, phase = 0f, drift = 0.5f, driftTarget = 0.5f;
            for (int i = 0; i < n; i++)
            {
                float p = i / (float)n;
                if (i % 1800 == 0) driftTarget = (float)rng.NextDouble();   // the hinge catches and lets go unevenly
                drift += (driftTarget - drift) * 0.0012f;
                phase += Mathf.Lerp(rateFrom, rateTo, p) * (0.7f + 0.7f * drift) / Rate;
                if (phase >= 1f) phase -= 1f;
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                float x = Mathf.Exp(-phase * 10f) * (0.8f + 0.2f * noise);

                low1 += k1 * band1;
                band1 += k1 * (x - low1 - damp * band1);
                low2 += k2 * band2;
                band2 += k2 * (x - low2 - damp * band2);

                float env = Mathf.Clamp01(p / 0.1f) * Mathf.Clamp01((1f - p) / 0.3f) * (0.5f + 0.5f * drift);
                d[start + i] += (band1 + band2 * 0.55f) * env * gain * 0.6f;
            }
        }

        /// <summary>The action cycling right after a shot: slide (or bolt) slamming back and then home,
        /// two hard metallic clacks a few hundredths of a second apart.</summary>
        public static AudioClip ActionCycle()
        {
            const float length = 0.12f, second = 0.038f;
            int n = (int)(Rate * length);
            var d = new float[n];
            var rng = new System.Random(777);
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                d[i] = Clack(t, noise, 1f) * 0.8f;
                if (t >= second) d[i] += Clack(t - second, noise, 0.87f);
            }
            return Finish("ActionCycle", d, 0.9f, 1.3f);
        }

        // steel on steel: a burst of noise and a few inharmonic partials that die within milliseconds
        static float Clack(float t, float noise, float tune)
        {
            float ring = Mathf.Sin(2f * Mathf.PI * 2350f * tune * t) * 0.55f
                       + Mathf.Sin(2f * Mathf.PI * 4170f * tune * t) * 0.45f
                       + Mathf.Sin(2f * Mathf.PI * 6830f * tune * t) * 0.3f;
            return noise * Mathf.Exp(-t * 700f) * 1.2f + ring * Mathf.Exp(-t * 160f);
        }

        /// <summary>A spent casing hitting something hard. Brass rings high and clear for a moment;
        /// a shotgun shell (plastic hull, brass head) gives a dull, hollow tock. <paramref name="seed"/>
        /// varies it, so a few clips make a natural spread.</summary>
        public static AudioClip CasingHit(bool shotgunShell, int seed)
        {
            float length = shotgunShell ? 0.18f : 0.32f;
            int n = (int)(Rate * length);
            var d = new float[n];
            var rng = new System.Random(seed * 7919 + (shotgunShell ? 1 : 0));
            float v = 1f + ((float)rng.NextDouble() - 0.5f) * 0.12f;   // each clip a slightly different piece of metal
            // brass: thin tube modes; the shell: a hollow plastic knock with a little of its metal head
            float[] freq = shotgunShell ? new[] { 820f, 1530f, 2650f, 5200f } : new[] { 3900f, 6150f, 8650f, 11800f };
            float[] amp = shotgunShell ? new[] { 1f, 0.6f, 0.35f, 0.15f } : new[] { 1f, 0.75f, 0.5f, 0.3f };
            float[] decay = shotgunShell ? new[] { 55f, 70f, 95f, 140f } : new[] { 13f, 18f, 26f, 38f };
            float[] phase = new float[4];
            for (int k = 0; k < 4; k++) phase[k] = (float)rng.NextDouble() * 6.283f;
            float lp = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                lp += (noise - lp) * (shotgunShell ? 0.25f : 0.7f);
                float s = lp * Mathf.Exp(-t * (shotgunShell ? 260f : 900f)) * (shotgunShell ? 1.4f : 0.8f);
                for (int k = 0; k < 4; k++)
                    s += Mathf.Sin(2f * Mathf.PI * freq[k] * v * t + phase[k]) * amp[k] * Mathf.Exp(-t * decay[k]) * 0.5f;
                d[i] = s * Mathf.Clamp01(t / 0.0004f);
            }
            return Finish(shotgunShell ? "ShellHit" : "CasingHit", d, 0.85f, 1.1f);
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

        /// <summary>
        /// A short circuit arcing: a buzz at mains hum and its harmonics chopped into stutters, sharp snaps of noise
        /// through it, about a third of a second.
        /// </summary>
        public static AudioClip ElectricCrackle(int seed)
        {
            const float length = 0.38f;
            int n = (int)(Rate * length);
            var d = new float[n];
            var rng = new System.Random(seed);
            float hp = 0f, prev = 0f, gate = 1f, nextGate = 0f, snap = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                hp = 0.92f * (hp + noise - prev); prev = noise;                     // bright, thin noise
                if (t >= nextGate) { gate = rng.NextDouble() < 0.65 ? 1f : 0.05f; nextGate = t + 0.008f + (float)rng.NextDouble() * 0.03f; }
                if (rng.NextDouble() < 0.0009) snap = 1f;                           // a crack of the arc
                snap *= 0.993f;
                float buzz = Mathf.Sign(Mathf.Sin(2f * Mathf.PI * 100f * t)) * 0.4f + Mathf.Sin(2f * Mathf.PI * 300f * t) * 0.3f;
                float env = Mathf.Clamp01(t / 0.004f) * Mathf.Clamp01((length - t) / 0.08f);
                d[i] = (buzz * 0.5f * gate + hp * (0.35f * gate + 1.6f * snap)) * env;
            }
            return Finish("ElectricCrackle", d, 0.8f, 2.2f);
        }

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
