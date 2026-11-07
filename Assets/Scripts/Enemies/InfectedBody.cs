using UnityEngine;
using UnityEngine.Rendering;

namespace BodycamHorror.Enemies
{
    /// <summary>
    /// Placeholder infected body built from primitives (pivot at the feet, facing +Z) with procedural
    /// animation driven by <see cref="Infected"/>: running with arms reaching forward, climbing, swinging,
    /// stumbling, lying limp. Also owns the head hitbox used for headshots.
    /// </summary>
    [RequireComponent(typeof(Infected))]
    public class InfectedBody : MonoBehaviour
    {
        [Tooltip("Metres per left+right stride at a run.")]
        public float stride = 1.7f;
        public float poseSharpness = 12f;

        public SphereCollider HeadHitbox { get; private set; }

        Infected brain;
        Transform model, hips, spine, neck, armL, armR, legL, legR;
        float phase, twitchTimer, seed;
        Quaternion twitch = Quaternion.identity, twitchTarget = Quaternion.identity;

        static Material skinMat, shirtMat, pantsMat, eyeMat;

        void Awake()
        {
            brain = GetComponent<Infected>();
            seed = Random.value * 100f;
            Build();
        }

        void Build()
        {
            var old = transform.Find("Model");
            if (old) Destroy(old.gameObject);
            EnsureMaterials();

            // a little variety in size and colours
            float s = Random.Range(0.94f, 1.08f);
            Material skin = Tinted(skinMat, Random.Range(0.85f, 1.1f));
            Material shirt = Tinted(shirtMat, Random.Range(0.6f, 1.4f));

            model = new GameObject("Model").transform;
            model.SetParent(transform, false);
            model.localScale = Vector3.one * s;

            hips = Pivot("Hips", model, new Vector3(0f, 0.88f, 0f));
            Box(hips, "Pelvis", new Vector3(0f, 0f, 0f), new Vector3(0.34f, 0.18f, 0.2f), pantsMat);
            legL = Pivot("LegL", hips, new Vector3(-0.1f, -0.04f, 0f));
            Box(legL, "Leg", new Vector3(0f, -0.42f, 0f), new Vector3(0.14f, 0.84f, 0.15f), pantsMat);
            legR = Pivot("LegR", hips, new Vector3(0.1f, -0.04f, 0f));
            Box(legR, "Leg", new Vector3(0f, -0.42f, 0f), new Vector3(0.14f, 0.84f, 0.15f), pantsMat);

            spine = Pivot("Spine", hips, new Vector3(0f, 0.08f, 0f));
            Box(spine, "Torso", new Vector3(0f, 0.3f, 0f), new Vector3(0.4f, 0.56f, 0.24f), shirt);
            armL = Pivot("ArmL", spine, new Vector3(-0.25f, 0.52f, 0f));
            Box(armL, "Arm", new Vector3(0f, -0.31f, 0f), new Vector3(0.1f, 0.62f, 0.1f), skin);
            armR = Pivot("ArmR", spine, new Vector3(0.25f, 0.52f, 0f));
            Box(armR, "Arm", new Vector3(0f, -0.31f, 0f), new Vector3(0.1f, 0.62f, 0.1f), skin);
            neck = Pivot("Neck", spine, new Vector3(0f, 0.6f, 0f));
            Box(neck, "Head", new Vector3(0f, 0.13f, 0.01f), new Vector3(0.21f, 0.25f, 0.23f), skin);
            Box(neck, "EyeL", new Vector3(-0.05f, 0.15f, 0.125f), new Vector3(0.035f, 0.022f, 0.01f), eyeMat);
            Box(neck, "EyeR", new Vector3(0.05f, 0.15f, 0.125f), new Vector3(0.035f, 0.022f, 0.01f), eyeMat);

            // head hitbox: a separate, un-animated collider so headshots are reliable
            var hb = new GameObject("HeadHitbox");
            hb.transform.SetParent(transform, false);
            hb.transform.localPosition = new Vector3(0f, 1.62f * s, 0.02f);
            hb.layer = gameObject.layer;
            HeadHitbox = hb.AddComponent<SphereCollider>();
            HeadHitbox.radius = 0.14f * s;
        }

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f || !model) return;
            Infected.State state = brain.CurrentState;
            float speed = brain.HorizontalSpeed;
            float run01 = Mathf.InverseLerp(1.5f, 6f, speed);
            float walk01 = Mathf.Clamp01(speed / 1.5f) * (1f - run01);
            phase += speed * dt * Mathf.PI * 2f / stride;
            float sw = Mathf.Sin(phase);
            float p = brain.ActionProgress;

            // defaults: shambling idle / run
            float lean = 6f + 14f * run01;
            Vector3 aL = new Vector3(-12f + 6f * Mathf.Sin(Time.time * 1.3f + seed), 0f, -6f);
            Vector3 aR = new Vector3(-10f + 6f * Mathf.Sin(Time.time * 1.1f + seed * 2f), 0f, 6f);
            if (brain.IsHunting)
            {
                // arms reach out towards the prey, flailing with the stride
                aL = new Vector3(-70f + sw * 25f * run01, -8f, -10f + sw * 10f);
                aR = new Vector3(-70f - sw * 25f * run01, 8f, 10f - sw * 10f);
            }
            float legSwing = sw * (38f * run01 + 22f * walk01);
            Vector3 lL = new Vector3(legSwing, 0f, 0f), lR = new Vector3(-legSwing, 0f, 0f);
            Vector3 headE = new Vector3(8f + sw * 4f * run01, 0f, Mathf.Sin(Time.time * 0.7f + seed) * 8f);

            switch (state)
            {
                case Infected.State.Attack:
                {
                    // raise both arms overhead, then slam them down at the strike
                    float up = p < 0.6f ? Mathf.SmoothStep(0f, 1f, p / 0.6f) : 1f - Mathf.SmoothStep(0f, 1f, (p - 0.6f) / 0.25f);
                    aL = new Vector3(Mathf.Lerp(-35f, -160f, up), -10f, -15f);
                    aR = new Vector3(Mathf.Lerp(-35f, -160f, up), 10f, 15f);
                    lean = Mathf.Lerp(30f, -5f, up);
                    break;
                }
                case Infected.State.Climb:
                {
                    float c = Mathf.Sin(Time.time * 7f + seed);
                    aL = new Vector3(-165f + c * 15f, 0f, -8f);
                    aR = new Vector3(-165f - c * 15f, 0f, 8f);
                    lL = new Vector3(-45f + c * 30f, 0f, 0f);
                    lR = new Vector3(-45f - c * 30f, 0f, 0f);
                    lean = 25f;
                    headE = new Vector3(-20f, 0f, 0f);   // looking up at the edge
                    break;
                }
                case Infected.State.Stumble:
                {
                    float a = Mathf.Sin(Mathf.Clamp01(p) * Mathf.PI);
                    lean = Mathf.Lerp(lean, 45f, a);
                    aL = Vector3.Lerp(aL, new Vector3(-50f, 0f, -55f), a);
                    aR = Vector3.Lerp(aR, new Vector3(-50f, 0f, 55f), a);
                    headE = new Vector3(25f * a, 0f, 0f);
                    break;
                }
                case Infected.State.Fallen:
                case Infected.State.Dead:
                    // limp: arms splayed, legs straight
                    aL = new Vector3(-30f, 0f, -70f);
                    aR = new Vector3(-20f, 0f, 65f);
                    lL = new Vector3(5f, 0f, -8f);
                    lR = new Vector3(-5f, 0f, 8f);
                    lean = 0f;
                    headE = new Vector3(0f, 0f, 30f);
                    break;
                case Infected.State.GettingUp:
                    aL = new Vector3(-60f, 0f, -30f);
                    aR = new Vector3(-60f, 0f, 30f);
                    lL = new Vector3(-60f * (1f - p), 0f, 0f);
                    lR = new Vector3(-30f * (1f - p), 0f, 0f);
                    lean = 40f * (1f - p);
                    break;
            }

            // nervous head twitches
            twitchTimer -= dt;
            if (twitchTimer <= 0f)
            {
                twitchTimer = Random.Range(0.4f, 2.2f);
                twitchTarget = Random.value < 0.6f ? Quaternion.identity
                    : Quaternion.Euler(Random.Range(-15f, 15f), Random.Range(-35f, 35f), Random.Range(-30f, 30f));
            }
            twitch = Quaternion.Slerp(twitch, twitchTarget, 1f - Mathf.Exp(-25f * dt));

            float k = 1f - Mathf.Exp(-poseSharpness * dt);
            float fast = 1f - Mathf.Exp(-poseSharpness * 2.5f * dt);
            Pose(armL, aL, state == Infected.State.Attack ? fast : k);
            Pose(armR, aR, state == Infected.State.Attack ? fast : k);
            Pose(legL, lL, k);
            Pose(legR, lR, k);
            spine.localRotation = Quaternion.Slerp(spine.localRotation, Quaternion.Euler(lean, 0f, 0f), k);
            neck.localRotation = Quaternion.Slerp(neck.localRotation, Quaternion.Euler(headE) * twitch, k);
            // running bounce
            hips.localPosition = new Vector3(0f, 0.88f - Mathf.Abs(sw) * 0.04f * run01, 0f);
        }

        static void Pose(Transform t, Vector3 euler, float k) =>
            t.localRotation = Quaternion.Slerp(t.localRotation, Quaternion.Euler(euler), k);

        Transform Pivot(string pivotName, Transform parent, Vector3 pos)
        {
            var t = new GameObject(pivotName).transform;
            t.SetParent(parent, false);
            t.localPosition = pos;
            return t;
        }

        void Box(Transform parent, string boxName, Vector3 pos, Vector3 size, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = boxName;
            Destroy(go.GetComponent<Collider>());   // the body uses one capsule + the head hitbox
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = size;
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.On;
        }

        static void EnsureMaterials()
        {
            if (skinMat) return;
            skinMat = Lit(new Color(0.42f, 0.45f, 0.37f), 0.45f);    // wet, sickly
            shirtMat = Lit(new Color(0.2f, 0.17f, 0.14f), 0.1f);
            pantsMat = Lit(new Color(0.12f, 0.13f, 0.15f), 0.1f);
            var unlit = Shader.Find("Universal Render Pipeline/Unlit");
            eyeMat = unlit ? new Material(unlit) : Lit(Color.red, 0f);
            eyeMat.color = new Color(1.6f, 0.25f, 0.12f);
            if (eyeMat.HasProperty("_BaseColor")) eyeMat.SetColor("_BaseColor", new Color(1.6f, 0.25f, 0.12f));
        }

        static Material Lit(Color c, float smoothness)
        {
            var baseMat = GraphicsSettings.currentRenderPipeline ? GraphicsSettings.currentRenderPipeline.defaultMaterial : null;
            var m = baseMat ? new Material(baseMat) : new Material(Shader.Find("Standard"));
            m.color = c;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
            return m;
        }

        static Material Tinted(Material source, float k)
        {
            var m = new Material(source);
            Color c = source.color * k;
            c.a = 1f;
            m.color = c;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            return m;
        }
    }
}
