using BodycamHorror.Combat;
using UnityEngine;

namespace BodycamHorror.AI
{
    /// <summary>
    /// Placeholder monster made of primitives, animated procedurally from MonsterBrain: hunched
    /// torso, long arms, digitigrade legs on 2-bone IK, a head that tracks what it looks at, a jaw,
    /// four glowing eyes. Crouches behind cover, leans out when peeking, rears up before a lunge,
    /// flinches and flashes when shot, twitches now and then. Swap for the real model later; the
    /// brain only talks to this through its public properties.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public class MonsterBody : MonoBehaviour
    {
        [Header("Materials (created at runtime if empty)")]
        public Material fleshMaterial;
        public Material boneMaterial;
        public Material eyeMaterial;

        [Header("Parts (built by Build Primitive Body)")]
        public Transform visual;
        public Transform pelvis, torso, head, jaw;
        public Transform hipL, kneeL, hipR, kneeR;
        public Transform shoulderL, elbowL, shoulderR, elbowR;
        public Collider headCollider;

        [Header("Proportions")]
        public float thighLength = 0.5f;
        public float shinLength = 0.55f;
        public float standHipHeight = 0.95f;
        public float crouchHipHeight = 0.55f;
        public float strideLength = 1.1f;
        public float stepLift = 0.14f;

        [Header("Feel")]
        public float headTurnSpeed = 9f;
        public float twitchStrength = 18f;
        public Color hitFlashColor = new Color(1f, 0.85f, 0.7f);

        MonsterBrain brain;
        AudioSource audioSource;
        Renderer[] flashRenderers;
        Color[] baseColors;
        MaterialPropertyBlock block;
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        float phase, lastStepSign = 1f, flash, deathT, breathe;
        Vector3 flinch, flinchVel;            // x = pitch, y = yaw, z = roll offsets (deg)
        Vector3 twitch; float nextTwitch;
        Quaternion headWorld;

        void Awake()
        {
            brain = GetComponent<MonsterBrain>();
            if (!visual) BuildBody();
            CacheRenderers();
            foreach (var c in GetComponentsInChildren<Collider>(true)) Sight.Register(c);
            headWorld = head ? head.rotation : transform.rotation;
        }

        void Start() => audioSource = GetComponent<AudioSource>();

        void OnEnable() { if (brain) brain.Hurt += OnHurt; }
        void OnDisable() { if (brain) brain.Hurt -= OnHurt; }

        void OnDestroy()
        {
            foreach (var c in GetComponentsInChildren<Collider>(true)) Sight.Unregister(c);
        }

        public bool IsHead(Collider c) => c && head && (c == headCollider || c.transform.IsChildOf(head));

        public void ResetPose()
        {
            deathT = 0f;
            flinch = flinchVel = Vector3.zero;
        }

        void CacheRenderers()
        {
            var all = visual ? visual.GetComponentsInChildren<Renderer>(true) : new Renderer[0];
            var list = new System.Collections.Generic.List<Renderer>();
            foreach (var r in all) if (r.sharedMaterial != eyeMaterial) list.Add(r);
            flashRenderers = list.ToArray();
            baseColors = new Color[flashRenderers.Length];
            for (int i = 0; i < flashRenderers.Length; i++)
            {
                var m = flashRenderers[i].sharedMaterial;
                baseColors[i] = m && m.HasProperty(BaseColorId) ? m.GetColor(BaseColorId) : Color.gray;
            }
            block = new MaterialPropertyBlock();
        }

        void OnHurt(DamageInfo info, bool headshot)
        {
            flash = 1f;
            // kick the torso away from the shot
            Vector3 local = transform.InverseTransformDirection(info.direction);
            float k = headshot ? 1.8f : 1f;
            flinchVel += new Vector3(local.z * 260f, local.x * 160f, -local.x * 200f) * k;
            if (headshot) twitch += new Vector3(Random.Range(-1f, 1f), Random.Range(-1f, 1f), Random.Range(-1f, 1f)) * twitchStrength * 2f;
        }

        // ------------------------------------------------------------------ animation

        void LateUpdate()
        {
            if (!brain || !visual) return;
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            Vector3 vel = brain.Velocity; vel.y = 0f;
            Vector3 localVel = transform.InverseTransformDirection(vel);
            float speed = vel.magnitude;
            float crouch = brain.Crouch;
            float attack = brain.AttackProgress;
            bool dead = brain.IsDead;

            // springs
            flinchVel += (-flinch * 180f - flinchVel * 14f) * dt;
            flinch += flinchVel * dt;
            twitch = Vector3.Lerp(twitch, Vector3.zero, dt * 10f);
            if (Time.time > nextTwitch && !dead)
            {
                nextTwitch = Time.time + Random.Range(1.5f, 5f);
                twitch += new Vector3(Random.Range(-1f, 1f), Random.Range(-1f, 1f), Random.Range(-1f, 1f)) * twitchStrength;
            }
            breathe += dt * Mathf.Lerp(1.6f, 4f, Mathf.Clamp01(speed / 6f));

            // gait phase
            float stride = strideLength * Mathf.Lerp(1f, 1.6f, Mathf.Clamp01(speed / 8f));
            phase += speed / stride * Mathf.PI * dt;
            float gait = Mathf.Clamp01(speed / 1.2f);
            float s = Mathf.Sin(phase);
            if (Mathf.Sign(s) != lastStepSign && gait > 0.3f && !dead)
            {
                lastStepSign = Mathf.Sign(s);
                if (audioSource) audioSource.PlayOneShot(MonsterSounds.Step, Mathf.Lerp(0.15f, 0.5f, Mathf.Clamp01(speed / 8f)) * (1f - crouch * 0.6f));
            }

            // --- whole body: height, lean, death collapse
            deathT = dead ? Mathf.MoveTowards(deathT, 1f, dt * 1.8f) : 0f;
            float hip = Mathf.Lerp(standHipHeight, crouchHipHeight, crouch);
            float rear = attack < 0.5f ? Mathf.Sin(attack * 2f * Mathf.PI * 0.5f) : 0f;   // windup: rise
            hip += rear * 0.15f + Mathf.Abs(Mathf.Cos(phase)) * 0.05f * gait;
            hip = Mathf.Lerp(hip, 0.25f, deathT * deathT);

            float lean = brain.Lean;
            visual.localPosition = new Vector3(lean * 0.28f, 0f, 0f);
            visual.localRotation = Quaternion.Euler(0f, 0f, -lean * 24f + deathT * 80f);
            pelvis.localPosition = new Vector3(0f, hip, 0f);
            pelvis.localRotation = Quaternion.Euler(0f, s * 6f * gait, Mathf.Cos(phase) * 4f * gait);

            // --- torso: hunched, more when crouched or running; rears back in the windup, slams forward in the lunge
            float pitch = Mathf.Lerp(38f, 68f, crouch) + Mathf.Clamp(localVel.z * 2.5f, -5f, 15f);
            if (attack > 0f) pitch = attack < 0.5f ? Mathf.Lerp(pitch, 5f, rear) : Mathf.Lerp(5f, 80f, (attack - 0.5f) * 2f);
            pitch += Mathf.Sin(breathe) * 2f + flinch.x;
            torso.localRotation = Quaternion.Euler(pitch, flinch.y - lean * 10f, flinch.z + Mathf.Cos(phase) * 3f * gait);

            // --- legs on IK (feet stay planted-ish while the hip goes up and down)
            float footZ = strideLength * 0.45f * gait;
            Leg(hipL, kneeL, hip, s * footZ + localVel.z * 0.02f, Mathf.Max(0f, Mathf.Cos(phase)) * stepLift * gait);
            Leg(hipR, kneeR, hip, -s * footZ + localVel.z * 0.02f, Mathf.Max(0f, -Mathf.Cos(phase)) * stepLift * gait);
            if (deathT > 0f)
            {
                hipL.localRotation = Quaternion.Slerp(hipL.localRotation, Quaternion.Euler(-80f, 0f, 20f), deathT);
                hipR.localRotation = Quaternion.Slerp(hipR.localRotation, Quaternion.Euler(-60f, 0f, -10f), deathT);
            }

            // --- arms: hang and swing; drag along the floor when hunched; reach forward on the lunge
            float armSwing = 35f * gait;
            float hang = Mathf.Lerp(-10f, -40f, crouch);              // more forward when the torso is pitched
            float armL = hang + s * armSwing, armR = hang - s * armSwing;
            float elbow = -25f - Mathf.Sin(breathe * 0.5f) * 4f;
            if (attack > 0f)
            {
                float up = attack < 0.5f ? rear : 0f;
                float strike = attack >= 0.5f ? Mathf.Sin((attack - 0.5f) * 2f * Mathf.PI * 0.5f) : 0f;
                armL = armR = Mathf.Lerp(hang, -150f, up) + strike * -60f;
                elbow = Mathf.Lerp(-25f, -70f, up) + strike * 50f;
            }
            if (lean != 0f)
            {
                // the arm on the peek side grips the corner
                if (lean > 0f) { armR = Mathf.Lerp(armR, -80f, lean); }
                else { armL = Mathf.Lerp(armL, -80f, -lean); }
            }
            // shoulder X = -pitch hangs the arm straight down in world space; negative angles swing it forward
            shoulderL.localRotation = Quaternion.Euler(armL - pitch, 0f, -12f);
            shoulderR.localRotation = Quaternion.Euler(armR - pitch, 0f, 12f);
            elbowL.localRotation = Quaternion.Euler(elbow, 0f, 0f);
            elbowR.localRotation = Quaternion.Euler(elbow, 0f, 0f);

            // --- head: looks where the brain looks, in world space, within neck limits
            Quaternion bodyRot = torso.rotation * Quaternion.Euler(-90f, 0f, 0f);   // torso "forward"
            Quaternion want = bodyRot;
            if (brain.HasLookTarget && !dead)
            {
                Vector3 dir = brain.LookTarget - head.position;
                if (dir.sqrMagnitude > 0.01f)
                {
                    Quaternion look = Quaternion.LookRotation(dir, transform.up);
                    float angle = Quaternion.Angle(bodyRot, look);
                    want = Quaternion.Slerp(bodyRot, look, angle > 110f ? 110f / angle : 1f);
                }
            }
            else want = Quaternion.LookRotation(transform.forward + Vector3.down * 0.2f, transform.up);
            // head tilts when peeking, like something curious
            want *= Quaternion.Euler(twitch.x, twitch.y, twitch.z - lean * 35f);
            headWorld = Quaternion.Slerp(headWorld, want, 1f - Mathf.Exp(-headTurnSpeed * dt));
            head.rotation = dead ? Quaternion.Slerp(headWorld, bodyRot * Quaternion.Euler(40f, 0f, 30f), deathT) : headWorld;

            jaw.localRotation = Quaternion.Euler(Mathf.Lerp(4f, 48f, brain.JawOpen) + Mathf.Sin(Time.time * 37f) * 3f * brain.JawOpen, 0f, 0f);

            // --- hit flash
            if (flash > 0f)
            {
                flash = Mathf.Max(0f, flash - dt * 9f);
                for (int i = 0; i < flashRenderers.Length; i++)
                {
                    flashRenderers[i].GetPropertyBlock(block);
                    block.SetColor(BaseColorId, Color.Lerp(baseColors[i], hitFlashColor, flash));
                    flashRenderers[i].SetPropertyBlock(block);
                }
            }
        }

        /// <summary>Planar 2-bone IK in the hip's parent space: foot at (forward z, height above ground).</summary>
        void Leg(Transform hipT, Transform kneeT, float hipHeight, float footZ, float footLift)
        {
            // pelvis yaw/roll is small, treat the leg plane as the local YZ plane
            Vector2 target = new Vector2(footZ, -(hipHeight - footLift));   // (z, y)
            float a = thighLength, b = shinLength;
            float d = Mathf.Clamp(target.magnitude, 0.05f, a + b - 0.001f);
            float phi = Mathf.Atan2(target.x, -target.y);                     // from straight down towards +z
            float alpha = Mathf.Acos(Mathf.Clamp((a * a + d * d - b * b) / (2f * a * d), -1f, 1f));
            float knee = Mathf.PI - Mathf.Acos(Mathf.Clamp((a * a + b * b - d * d) / (2f * a * b), -1f, 1f));
            // thigh forward (knee in front), shin folds back: reverse-jointed look
            hipT.localRotation = Quaternion.Euler(-(phi + alpha) * Mathf.Rad2Deg, 0f, 0f);
            kneeT.localRotation = Quaternion.Euler(knee * Mathf.Rad2Deg, 0f, 0f);
        }

        // ------------------------------------------------------------------ construction

        [ContextMenu("Build Primitive Body")]
        public void BuildBody()
        {
            if (visual)
            {
                if (Application.isPlaying) Destroy(visual.gameObject); else DestroyImmediate(visual.gameObject);
            }
            EnsureMaterials();

            visual = Pivot("Visual", transform, Vector3.zero);
            pelvis = Pivot("Pelvis", visual, new Vector3(0f, standHipHeight, 0f));
            Mesh(PrimitiveType.Sphere, "PelvisMesh", pelvis, Vector3.zero, Vector3.zero, new Vector3(0.36f, 0.26f, 0.3f), fleshMaterial, true);

            // torso: pivot at the pelvis, +Y runs up the spine (pitched forward at runtime)
            torso = Pivot("Torso", pelvis, new Vector3(0f, 0.05f, 0f));
            Mesh(PrimitiveType.Capsule, "TorsoMesh", torso, new Vector3(0f, 0.42f, 0f), Vector3.zero, new Vector3(0.44f, 0.46f, 0.32f), fleshMaterial, true);
            Mesh(PrimitiveType.Sphere, "Ribcage", torso, new Vector3(0f, 0.62f, 0.06f), Vector3.zero, new Vector3(0.5f, 0.36f, 0.4f), fleshMaterial, false);
            for (int i = 0; i < 5; i++)
            {
                float y = 0.15f + i * 0.15f;
                Mesh(PrimitiveType.Cylinder, "Spine" + i, torso, new Vector3(0f, y, -0.17f - i * 0.01f), new Vector3(-55f - i * 4f, 0f, 0f),
                     new Vector3(0.035f, 0.08f + i * 0.015f, 0.035f), boneMaterial, false);
            }
            // a lump of extra flesh on one side: it shouldn't look symmetric
            Mesh(PrimitiveType.Sphere, "Growth", torso, new Vector3(0.2f, 0.5f, -0.05f), new Vector3(10f, 30f, 0f), new Vector3(0.22f, 0.3f, 0.2f), fleshMaterial, false);

            // head
            Transform neck = Pivot("Neck", torso, new Vector3(0f, 0.86f, 0.05f));
            Mesh(PrimitiveType.Capsule, "NeckMesh", neck, new Vector3(0f, 0.06f, 0f), Vector3.zero, new Vector3(0.14f, 0.1f, 0.14f), fleshMaterial, false);
            head = Pivot("Head", neck, new Vector3(0f, 0.14f, 0.02f));
            headCollider = Mesh(PrimitiveType.Sphere, "HeadMesh", head, new Vector3(0f, 0.02f, 0.12f), Vector3.zero, new Vector3(0.28f, 0.24f, 0.4f), fleshMaterial, true)
                .GetComponent<Collider>();
            jaw = Pivot("Jaw", head, new Vector3(0f, -0.06f, 0.0f));
            Mesh(PrimitiveType.Cube, "JawMesh", jaw, new Vector3(0f, -0.03f, 0.17f), Vector3.zero, new Vector3(0.22f, 0.05f, 0.32f), fleshMaterial, false);
            for (int i = 0; i < 6; i++)
            {
                float x = (i - 2.5f) * 0.035f;
                Mesh(PrimitiveType.Cube, "Tooth" + i, jaw, new Vector3(x, 0.01f, 0.3f - Mathf.Abs(x) * 0.6f), new Vector3(0f, 0f, 0f), new Vector3(0.012f, 0.05f, 0.012f), boneMaterial, false);
            }
            Mesh(PrimitiveType.Sphere, "EyeL", head, new Vector3(-0.07f, 0.06f, 0.29f), Vector3.zero, Vector3.one * 0.045f, eyeMaterial, false);
            Mesh(PrimitiveType.Sphere, "EyeR", head, new Vector3(0.075f, 0.055f, 0.29f), Vector3.zero, Vector3.one * 0.05f, eyeMaterial, false);
            Mesh(PrimitiveType.Sphere, "EyeL2", head, new Vector3(-0.1f, 0.1f, 0.22f), Vector3.zero, Vector3.one * 0.028f, eyeMaterial, false);
            Mesh(PrimitiveType.Sphere, "EyeR2", head, new Vector3(0.12f, 0.08f, 0.21f), Vector3.zero, Vector3.one * 0.022f, eyeMaterial, false);

            // arms: long, hang from the top of the spine; local -Y runs down the bone
            shoulderL = Pivot("ShoulderL", torso, new Vector3(-0.26f, 0.78f, 0.04f));
            shoulderR = Pivot("ShoulderR", torso, new Vector3(0.26f, 0.78f, 0.04f));
            elbowL = Arm(shoulderL);
            elbowR = Arm(shoulderR);

            // legs
            hipL = Pivot("HipL", pelvis, new Vector3(-0.17f, -0.02f, 0f));
            hipR = Pivot("HipR", pelvis, new Vector3(0.17f, -0.02f, 0f));
            kneeL = LegParts(hipL);
            kneeR = LegParts(hipR);

            CacheRenderers();
        }

        Transform Arm(Transform shoulder)
        {
            const float upper = 0.62f, fore = 0.72f;
            Mesh(PrimitiveType.Capsule, "UpperArm", shoulder, new Vector3(0f, -upper * 0.5f, 0f), Vector3.zero, new Vector3(0.11f, upper * 0.5f, 0.11f), fleshMaterial, true);
            Transform elbow = Pivot("Elbow", shoulder, new Vector3(0f, -upper, 0f));
            Mesh(PrimitiveType.Capsule, "Forearm", elbow, new Vector3(0f, -fore * 0.5f, 0f), Vector3.zero, new Vector3(0.08f, fore * 0.5f, 0.08f), fleshMaterial, false);
            for (int i = 0; i < 3; i++)
                Mesh(PrimitiveType.Cube, "Claw" + i, elbow, new Vector3((i - 1) * 0.035f, -fore - 0.08f, 0.02f), new Vector3(-20f, 0f, (i - 1) * 8f),
                     new Vector3(0.018f, 0.18f, 0.018f), boneMaterial, false);
            return elbow;
        }

        Transform LegParts(Transform hipT)
        {
            Mesh(PrimitiveType.Capsule, "Thigh", hipT, new Vector3(0f, -thighLength * 0.5f, 0f), Vector3.zero, new Vector3(0.16f, thighLength * 0.5f, 0.16f), fleshMaterial, true);
            Transform knee = Pivot("Knee", hipT, new Vector3(0f, -thighLength, 0f));
            Mesh(PrimitiveType.Capsule, "Shin", knee, new Vector3(0f, -shinLength * 0.5f, 0f), Vector3.zero, new Vector3(0.1f, shinLength * 0.5f, 0.1f), fleshMaterial, false);
            return knee;
        }

        static Transform Pivot(string n, Transform parent, Vector3 localPos)
        {
            var t = new GameObject(n).transform;
            t.SetParent(parent, false);
            t.localPosition = localPos;
            return t;
        }

        static Transform Mesh(PrimitiveType type, string n, Transform parent, Vector3 pos, Vector3 euler, Vector3 scale, Material mat, bool collider)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = n;
            var t = go.transform;
            t.SetParent(parent, false);
            t.localPosition = pos;
            t.localEulerAngles = euler;
            t.localScale = scale;
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            var c = go.GetComponent<Collider>();
            if (!collider)
            {
                if (Application.isPlaying) Destroy(c); else DestroyImmediate(c);
            }
            return t;
        }

        void EnsureMaterials()
        {
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            if (!fleshMaterial) fleshMaterial = MakeMat(lit, "MonsterFlesh", new Color(0.32f, 0.2f, 0.19f), 0.55f);
            if (!boneMaterial) boneMaterial = MakeMat(lit, "MonsterBone", new Color(0.72f, 0.66f, 0.55f), 0.3f);
            if (!eyeMaterial)
            {
                eyeMaterial = MakeMat(lit, "MonsterEye", new Color(1f, 0.5f, 0.2f), 0.9f);
                eyeMaterial.EnableKeyword("_EMISSION");
                eyeMaterial.SetColor("_EmissionColor", new Color(1f, 0.35f, 0.1f) * 6f);
            }
        }

        static Material MakeMat(Shader shader, string n, Color c, float smooth)
        {
            var m = new Material(shader) { name = n };
            m.SetColor(BaseColorId, c);
            m.SetFloat("_Smoothness", smooth);
            return m;
        }
    }
}
