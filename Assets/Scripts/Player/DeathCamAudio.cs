using UnityEngine;

namespace BodycamHorror.Player
{
    /// <summary>
    /// The sound side of the death cam: sits on the AudioListener and breaks up the whole mix the way a dying
    /// digital recorder does: fewer bits, a lower sample rate, chunks stuttering and dropping out, then the
    /// game sound gone under static and a low hum. Driven by <see cref="DeathCam"/>; with everything at rest it
    /// passes the mix through untouched. Runs on the audio thread: the controls are plain fields set from the
    /// main thread.
    /// </summary>
    public class DeathCamAudio : MonoBehaviour
    {
        /// <summary>0 = clean, 1 = the mix crushed, stuttering and dropping out.</summary>
        [System.NonSerialized] public float crush;
        /// <summary>Level of the game's own sound (1 = as it is, 0 = gone).</summary>
        [System.NonSerialized] public float gameLevel = 1f;
        /// <summary>Static (white noise with crackle) mixed in.</summary>
        [System.NonSerialized] public float noiseLevel;
        /// <summary>The low electrical hum of a dead feed.</summary>
        [System.NonSerialized] public float humLevel;

        volatile int thuds;   // queued from the main thread
        float thudLeft, thudPhase;
        float held0, held1;
        int holdLeft;
        float[] stutter = new float[8192];
        int stutterLen, writePos, playStart, playOff, stutterLeft, dropLeft;
        float humPhase;
        float gameSmooth = 1f, crushSmooth, noiseSmooth, humSmooth;
        uint rng = 0x9E3779B9u;
        int rate = 48000;

        void Awake() => rate = AudioSettings.outputSampleRate;

        /// <summary>A body (and the camera) hitting the floor: a low thump in the mix.</summary>
        public void Thud() => thuds++;

        public bool Idle => crush <= 0f && gameLevel >= 1f && noiseLevel <= 0f && humLevel <= 0f
                            && crushSmooth < 0.001f && gameSmooth > 0.999f && noiseSmooth < 0.001f && humSmooth < 0.001f && thudLeft <= 0f;

        float Rand() { rng ^= rng << 13; rng ^= rng >> 17; rng ^= rng << 5; return (rng & 0xFFFFFF) / 16777216f; }

        void OnAudioFilterRead(float[] data, int channels)
        {
            if (Idle && thuds == 0) return;
            if (thuds > 0) { thuds = 0; thudLeft = 0.35f; thudPhase = 0f; }

            int frames = data.Length / channels;
            float k = 1f - Mathf.Exp(-1f / (rate * 0.03f));   // ~30 ms smoothing so nothing clicks
            float dt = 1f / rate;

            for (int f = 0; f < frames; f++)
            {
                gameSmooth += (gameLevel - gameSmooth) * k;
                crushSmooth += (crush - crushSmooth) * k;
                noiseSmooth += (noiseLevel - noiseSmooth) * k;
                humSmooth += (humLevel - humSmooth) * k;
                float c = crushSmooth;

                // events per frame: start a stutter (repeat a short chunk) or a drop-out
                if (c > 0.01f && stutterLeft <= 0 && dropLeft <= 0)
                {
                    float r = Rand();
                    if (r < c * 0.00025f)
                    {
                        stutterLen = Mathf.Clamp((int)(rate * (0.02f + Rand() * 0.07f)), 64, stutter.Length / 2);
                        stutterLeft = stutterLen * (2 + (int)(Rand() * 4f));
                        int half = stutter.Length / 2;
                        playStart = (writePos - stutterLen + half) % half;   // replay the chunk just heard
                        playOff = 0;
                    }
                    else if (r < c * 0.0004f) dropLeft = (int)(rate * (0.01f + Rand() * 0.08f));
                }

                for (int ch = 0; ch < channels; ch++)
                {
                    int i = f * channels + ch;
                    float s = data[i];
                    if (c > 0.001f)
                    {
                        // record into the stutter buffer, or play a chunk of it back over and over
                        int half = stutter.Length / 2;
                        if (stutterLeft > 0) s = stutter[((playStart + playOff) % half) * 2 + (ch & 1)];
                        else stutter[writePos * 2 + (ch & 1)] = s;

                        // sample-rate reduction (hold) and bit crush
                        float levels = Mathf.Lerp(4096f, 6f, c * c);
                        s = Mathf.Round(s * levels) / levels;
                        if (ch == 0) held0 = holdLeft > 0 ? held0 : s; else held1 = holdLeft > 0 ? held1 : s;
                        s = Mathf.Lerp(s, ch == 0 ? held0 : held1, Mathf.Clamp01(c * 1.5f));
                        // a little hard clipping
                        float drive = 1f + c * 3f;
                        float ceiling = Mathf.Lerp(1f, 0.6f, c);
                        s = Mathf.Clamp(s * drive, -ceiling, ceiling);
                        if (dropLeft > 0) s = 0f;
                    }
                    s *= gameSmooth;

                    // what the dead feed adds on its own
                    if (noiseSmooth > 0.0001f)
                    {
                        float n = Rand() * 2f - 1f;
                        if (Rand() < 0.0008f) n *= 6f;   // crackle
                        s += n * 0.22f * noiseSmooth;
                    }
                    if (humSmooth > 0.0001f)
                        s += (Mathf.Sin(humPhase) * 0.6f + Mathf.Sin(humPhase * 3f) * 0.25f + Mathf.Sin(humPhase * 5f) * 0.1f) * 0.08f * humSmooth;
                    if (thudLeft > 0f)
                    {
                        float env = thudLeft / 0.35f;
                        s += (Mathf.Sin(thudPhase) * 0.8f + (Rand() * 2f - 1f) * 0.3f * env) * env * env * 0.7f;
                    }
                    data[i] = s;
                }

                if (holdLeft > 0) holdLeft--;
                else holdLeft = (int)Mathf.Lerp(0f, 12f, c);
                if (stutterLeft > 0) { stutterLeft--; playOff = (playOff + 1) % stutterLen; }
                else writePos = (writePos + 1) % (stutter.Length / 2);
                if (dropLeft > 0) dropLeft--;
                humPhase += 2f * Mathf.PI * 50f * dt;
                if (humPhase > 2f * Mathf.PI * 100f) humPhase -= 2f * Mathf.PI * 100f;
                if (thudLeft > 0f) { thudLeft -= dt; thudPhase += 2f * Mathf.PI * Mathf.Lerp(38f, 70f, thudLeft / 0.35f) * dt; }
            }
        }
    }
}
