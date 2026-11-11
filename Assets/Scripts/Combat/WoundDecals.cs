using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace BodycamHorror.Combat
{
    /// <summary>
    /// Bullet wounds on flesh: a dark hole with blood running down from it, left where the shot went in.
    /// Each one is a small decal projector riding on the bone that was hit, so it is projected onto
    /// whatever skin is there and knows nothing about the model: swap the character and wounds still work.
    /// Needs the Decal renderer feature on the URP renderer and the WoundDecal material in Resources.
    /// </summary>
    public static class WoundDecals
    {
        /// <summary>Wounds kept in the whole scene / on one body; the oldest go first.</summary>
        public const int MaxTotal = 72, MaxPerBody = 14;

        const int TexW = 128, TexH = 256, Variants = 4;
        const float WoundV = 0.74f;          // where the hole sits in the texture, bottom to top
        const float Depth = 0.26f, Lead = 0.1f;

        struct Wound { public DecalProjector projector; public Transform body; }
        static readonly List<Wound> Live = new List<Wound>();
        static Material[] materials;
        static bool unavailable;

        /// <summary>
        /// Leaves a wound at <paramref name="point"/>. <paramref name="bone"/> is what it sticks to (the hit
        /// collider's transform), <paramref name="body"/> the whole creature, used to cap wounds per body.
        /// </summary>
        public static void Add(Vector3 point, Vector3 normal, Vector3 direction, Transform bone, Transform body, float scale = 1f)
        {
            if (unavailable || !bone) return;
            if ((materials == null || !materials[0]) && !CreateMaterials()) { unavailable = true; return; }
            if (!body) body = bone;

            Prune(body);

            // projected mostly into the surface, a little along the shot; blood runs down
            Vector3 into = (-normal * 0.7f + direction.normalized * 0.3f).normalized;
            Vector3 up = Vector3.ProjectOnPlane(Vector3.up, into);
            up = up.sqrMagnitude < 0.02f ? Vector3.ProjectOnPlane(Random.onUnitSphere, into).normalized : up.normalized;

            float width = 0.14f * scale * Random.Range(0.85f, 1.2f);
            float height = width * 2f;

            var go = new GameObject("Wound");
            go.transform.SetPositionAndRotation(point - into * Lead - up * ((WoundV - 0.5f) * height), Quaternion.LookRotation(into, up));
            go.transform.SetParent(bone, true);

            var projector = go.AddComponent<DecalProjector>();
            projector.material = materials[Random.Range(0, materials.Length)];
            projector.scaleMode = DecalScaleMode.ScaleInvariant;
            projector.size = new Vector3(width, height, Depth);
            projector.pivot = new Vector3(0f, 0f, Depth * 0.5f);
            if (Random.value < 0.5f) { projector.uvScale = new Vector2(-1f, 1f); projector.uvBias = new Vector2(1f, 0f); }
            projector.drawDistance = 45f;
            projector.fadeScale = 0.85f;
            projector.fadeFactor = 1f;

            Live.Add(new Wound { projector = projector, body = body });
        }

        /// <summary>A limb came off: its wounds go with the loose piece instead of hanging in the air.</summary>
        public static void Move(Transform fromBone, Transform toPiece)
        {
            if (!fromBone || !toPiece) return;
            for (int i = 0; i < Live.Count; i++)
            {
                Wound w = Live[i];
                if (!w.projector || !w.projector.transform.IsChildOf(fromBone)) continue;
                w.projector.transform.SetParent(toPiece, true);
                w.body = toPiece;
                Live[i] = w;
            }
        }

        // drops wounds whose body is gone, then makes room under both caps
        static void Prune(Transform body)
        {
            int onBody = 0;
            for (int i = Live.Count - 1; i >= 0; i--)
            {
                if (!Live[i].projector) Live.RemoveAt(i);
                else if (Live[i].body == body) onBody++;
            }
            for (int i = 0; i < Live.Count && onBody >= MaxPerBody; i++)
            {
                if (Live[i].body != body) continue;
                Object.Destroy(Live[i].projector.gameObject);
                Live.RemoveAt(i--);
                onBody--;
            }
            while (Live.Count >= MaxTotal)
            {
                Object.Destroy(Live[0].projector.gameObject);
                Live.RemoveAt(0);
            }
        }

        static bool CreateMaterials()
        {
            var template = Resources.Load<Material>("WoundDecal");
            Shader shader = template ? template.shader : Shader.Find("Shader Graphs/Decal");
            if (!shader || !shader.isSupported) return false;

            materials = new Material[Variants];
            for (int i = 0; i < Variants; i++)
            {
                var m = template ? new Material(template) : new Material(shader);
                m.name = "Wound " + i;
                m.SetTexture("Base_Map", Paint(4711 + i * 131));
                m.enableInstancing = true;
                materials[i] = m;
            }
            return true;
        }

        // ------------------------------------------------------------------ the picture

        /// <summary>One wound: a ragged dark hole, a few spatters and runs of blood below it.</summary>
        static Texture2D Paint(int seed)
        {
            var rng = new System.Random(seed);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);

            float cx = TexW * 0.5f + R(-6f, 6f), cy = TexH * WoundV;
            float coreRadius = R(20f, 27f);
            // ragged outline: the radius wobbles with the angle
            float[] amp = { R(0.1f, 0.25f), R(0.06f, 0.18f), R(0.04f, 0.12f) }, phase = { R(0f, 6.28f), R(0f, 6.28f), R(0f, 6.28f) };
            int[] freq = { 3, 5, 9 };

            int spots = rng.Next(5, 10);
            var spot = new Vector3[spots];
            for (int i = 0; i < spots; i++)
            {
                float a = R(0f, 6.28f), d = R(coreRadius * 0.9f, coreRadius * 1.9f);
                spot[i] = new Vector3(cx + Mathf.Cos(a) * d, cy + Mathf.Sin(a) * d * 0.9f, R(2.5f, 7f));
            }

            int runs = rng.Next(2, 5);
            var run = new Vector4[runs];   // x, length, width, wobble phase
            for (int i = 0; i < runs; i++)
                run[i] = new Vector4(cx + R(-coreRadius * 0.7f, coreRadius * 0.7f), R(60f, cy - 22f), R(9f, 15f), R(0f, 6.28f));

            var pixels = new Color32[TexW * TexH];
            for (int y = 0; y < TexH; y++)
            for (int x = 0; x < TexW; x++)
            {
                float dx = x - cx, dy = y - cy;
                float dist = Mathf.Sqrt(dx * dx + dy * dy), angle = Mathf.Atan2(dy, dx);
                float wobble = 1f;
                for (int k = 0; k < 3; k++) wobble += amp[k] * Mathf.Sin(angle * freq[k] + phase[k]);
                float cover = Mathf.Clamp01((coreRadius * wobble - dist) / 2f);

                for (int i = 0; i < spots; i++)
                {
                    float sx = x - spot[i].x, sy = y - spot[i].y;
                    cover = Mathf.Max(cover, Mathf.Clamp01((spot[i].z - Mathf.Sqrt(sx * sx + sy * sy)) / 1.5f));
                }

                for (int i = 0; i < runs; i++)
                {
                    float end = cy - run[i].y;                 // lowest point of the run
                    if (y > cy || y < end - run[i].z) continue;
                    float along = Mathf.InverseLerp(cy, end, y);          // 0 at the wound, 1 at the tip
                    float centre = run[i].x + Mathf.Sin(along * 5f + run[i].w) * 2.2f;
                    float half = Mathf.Lerp(run[i].z, run[i].z * 0.5f, along) * 0.5f;
                    float d;
                    if (y >= end) d = Mathf.Abs(x - centre);
                    else { half = run[i].z * 0.42f; float ty = y - end; d = Mathf.Sqrt((x - centre) * (x - centre) + ty * ty); }   // bead at the tip
                    if (y >= end && along > 0.92f) half = Mathf.Max(half, run[i].z * 0.42f);
                    cover = Mathf.Max(cover, Mathf.Clamp01((half - d) / 1.3f) * Mathf.Lerp(1f, 0.82f, along));
                }

                // nothing may touch the border: the texture is clamped
                float edge = Mathf.Min(Mathf.Min(x, TexW - 1 - x), Mathf.Min(y, TexH - 1 - y));
                cover *= Mathf.Clamp01(edge / 3f);

                float hole = Mathf.Clamp01(1f - dist / (coreRadius * 0.5f));       // the entry itself is near black
                float grain = 0.85f + 0.3f * Mathf.PerlinNoise(x * 0.09f + seed, y * 0.09f);
                var c = Color.Lerp(new Color(0.6f, 0.02f, 0.015f), new Color(0.1f, 0f, 0f), hole * hole) * grain;
                c.a = cover * 0.94f;
                pixels[y * TexW + x] = c;
            }

            var tex = new Texture2D(TexW, TexH, TextureFormat.RGBA32, true) { name = "Wound " + seed, wrapMode = TextureWrapMode.Clamp, anisoLevel = 2 };
            tex.SetPixels32(pixels);
            tex.Apply(true, true);
            return tex;
        }
    }
}
