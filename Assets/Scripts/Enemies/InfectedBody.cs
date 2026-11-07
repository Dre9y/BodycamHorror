using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BodycamHorror.Enemies
{
    /// <summary>
    /// The infected's body and procedural animation, driven by <see cref="Infected"/>: running with arms
    /// reaching forward, climbing, swinging, stumbling, lying limp, nervous head twitches.
    /// Without a model it builds a placeholder from boxes. With <see cref="UseModel"/> it drives a
    /// Mixamo-style skeleton instead (limbs are aimed, so any rest pose works). Owns the head hitbox.
    /// </summary>
    [RequireComponent(typeof(Infected))]
    public class InfectedBody : MonoBehaviour
    {
        [Tooltip("Metres per left+right stride at a run.")]
        public float stride = 1.7f;
        public float poseSharpness = 12f;

        public SphereCollider HeadHitbox { get; private set; }

        Infected brain;
        Transform model;
        float phase, twitchTimer, seed, sizeScale = 1f;
        Quaternion twitch = Quaternion.identity, twitchTarget = Quaternion.identity;

        // smoothed pose (shared by both body kinds)
        Quaternion armL = Quaternion.identity, armR = Quaternion.identity, legL = Quaternion.identity, legR = Quaternion.identity;
        Quaternion headQ = Quaternion.identity;
        float lean, elbow = 20f, kneeL = 10f, kneeR = 10f, bob;

        // box body
        Transform bHips, bSpine, bNeck, bArmL, bArmR, bLegL, bLegR;

        // rigged body
        bool rigged;
        Transform rHips, rSpine, rSpine1, rSpine2, rNeck, rHead, rHeadTop;
        Transform rArmL, rForeL, rHandL, rArmR, rForeR, rHandR, rThighL, rShinL, rFootL, rThighR, rShinR, rFootR;
        readonly Dictionary<Transform, Quaternion> bindLocal = new Dictionary<Transform, Quaternion>();
        Vector3 hipsBindLocalPos;

        static Material skinMat, shirtMat, pantsMat, eyeMat;

        void Awake()
        {
            brain = GetComponent<Infected>();
            seed = Random.value * 100f;
            sizeScale = Random.Range(0.94f, 1.08f);
            BuildBoxes();
        }

        /// <summary>Swaps the box placeholder for an instance of a rigged model (Mixamo bone names).</summary>
        public void UseModel(GameObject prefab, Material material, float scale = 1f)
        {
            if (!prefab) return;
            ClearModel();
            model = new GameObject("Model").transform;
            model.SetParent(transform, false);
            var inst = Instantiate(prefab, model, false);
            inst.name = prefab.name;
            inst.transform.localScale = Vector3.one * (scale * sizeScale);
            foreach (var a in inst.GetComponentsInChildren<Animator>(true)) Destroy(a);
            foreach (var a in inst.GetComponentsInChildren<Animation>(true)) Destroy(a);
            foreach (var c in inst.GetComponentsInChildren<Collider>(true)) Destroy(c);
            foreach (var r in inst.GetComponentsInChildren<Renderer>(true))
            {
                if (material) r.sharedMaterial = material;
                r.shadowCastingMode = ShadowCastingMode.On;
                if (r is SkinnedMeshRenderer smr) smr.updateWhenOffscreen = true;   // bones are moved by hand
            }

            rHips = Bone(inst.transform, "Hips");
            rSpine = Bone(inst.transform, "Spine"); rSpine1 = Bone(inst.transform, "Spine1"); rSpine2 = Bone(inst.transform, "Spine2");
            rNeck = Bone(inst.transform, "Neck"); rHead = Bone(inst.transform, "Head"); rHeadTop = Bone(inst.transform, "HeadTop_End");
            rArmL = Bone(inst.transform, "LeftArm"); rForeL = Bone(inst.transform, "LeftForeArm"); rHandL = Bone(inst.transform, "LeftHand");
            rArmR = Bone(inst.transform, "RightArm"); rForeR = Bone(inst.transform, "RightForeArm"); rHandR = Bone(inst.transform, "RightHand");
            rThighL = Bone(inst.transform, "LeftUpLeg"); rShinL = Bone(inst.transform, "LeftLeg"); rFootL = Bone(inst.transform, "LeftFoot");
            rThighR = Bone(inst.transform, "RightUpLeg"); rShinR = Bone(inst.transform, "RightLeg"); rFootR = Bone(inst.transform, "RightFoot");

            rigged = rHips && rSpine && rNeck && rArmL && rForeL && rHandL && rArmR && rForeR && rHandR
                     && rThighL && rShinL && rFootL && rThighR && rShinR && rFootR;
            if (!rigged)
            {
                Debug.LogWarning($"[InfectedBody] {prefab.name}: Mixamo bones not found, falling back to boxes.", this);
                ClearModel();
                BuildBoxes();
                return;
            }
            bindLocal.Clear();
            foreach (var t in new[] { rSpine, rSpine1, rSpine2, rNeck, rHead, rArmL, rForeL, rArmR, rForeR, rThighL, rShinL, rThighR, rShinR })
                if (t) bindLocal[t] = t.localRotation;
            hipsBindLocalPos = rHips.localPosition;
            HeadHitbox.radius = 0.14f * scale * sizeScale;
        }

        void ClearModel()
        {
            var old = transform.Find("Model");
            if (old) { old.SetParent(null); Destroy(old.gameObject); }
            model = null;
            rigged = false;
        }

        void BuildBoxes()
        {
            ClearModel();
            EnsureMaterials();
            float s = sizeScale;
            Material skin = Tinted(skinMat, Random.Range(0.85f, 1.1f));
            Material shirt = Tinted(shirtMat, Random.Range(0.6f, 1.4f));

            model = new GameObject("Model").transform;
            model.SetParent(transform, false);
            model.localScale = Vector3.one * s;

            bHips = Pivot("Hips", model, new Vector3(0f, 0.88f, 0f));
            Box(bHips, "Pelvis", Vector3.zero, new Vector3(0.34f, 0.18f, 0.2f), pantsMat);
            bLegL = Pivot("LegL", bHips, new Vector3(-0.1f, -0.04f, 0f));
            Box(bLegL, "Leg", new Vector3(0f, -0.42f, 0f), new Vector3(0.14f, 0.84f, 0.15f), pantsMat);
            bLegR = Pivot("LegR", bHips, new Vector3(0.1f, -0.04f, 0f));
            Box(bLegR, "Leg", new Vector3(0f, -0.42f, 0f), new Vector3(0.14f, 0.84f, 0.15f), pantsMat);
            bSpine = Pivot("Spine", bHips, new Vector3(0f, 0.08f, 0f));
            Box(bSpine, "Torso", new Vector3(0f, 0.3f, 0f), new Vector3(0.4f, 0.56f, 0.24f), shirt);
            bArmL = Pivot("ArmL", bSpine, new Vector3(-0.25f, 0.52f, 0f));
            Box(bArmL, "Arm", new Vector3(0f, -0.31f, 0f), new Vector3(0.1f, 0.62f, 0.1f), skin);
            bArmR = Pivot("ArmR", bSpine, new Vector3(0.25f, 0.52f, 0f));
            Box(bArmR, "Arm", new Vector3(0f, -0.31f, 0f), new Vector3(0.1f, 0.62f, 0.1f), skin);
            bNeck = Pivot("Neck", bSpine, new Vector3(0f, 0.6f, 0f));
            Box(bNeck, "Head", new Vector3(0f, 0.13f, 0.01f), new Vector3(0.21f, 0.25f, 0.23f), skin);
            Box(bNeck, "EyeL", new Vector3(-0.05f, 0.15f, 0.125f), new Vector3(0.035f, 0.022f, 0.01f), eyeMat);
            Box(bNeck, "EyeR", new Vector3(0.05f, 0.15f, 0.125f), new Vector3(0.035f, 0.022f, 0.01f), eyeMat);

            if (!HeadHitbox)
            {
                var hb = new GameObject("HeadHitbox");
                hb.transform.SetParent(transform, false);
                hb.layer = gameObject.layer;
                HeadHitbox = hb.AddComponent<SphereCollider>();
            }
            HeadHitbox.transform.localPosition = new Vector3(0f, 1.62f * s, 0.02f);
            HeadHitbox.radius = 0.14f * s;
        }

        // ------------------------------------------------------------------ pose

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
            float tLean = 6f + 14f * run01;
            float tElbow = 25f;
            Vector3 aL = new Vector3(-12f + 6f * Mathf.Sin(Time.time * 1.3f + seed), 0f, -6f);
            Vector3 aR = new Vector3(-10f + 6f * Mathf.Sin(Time.time * 1.1f + seed * 2f), 0f, 6f);
            if (brain.IsHunting)
            {
                // arms reach out towards the prey, flailing with the stride
                aL = new Vector3(-70f + sw * 25f * run01, -8f, -10f + sw * 10f);
                aR = new Vector3(-70f - sw * 25f * run01, 8f, 10f - sw * 10f);
                tElbow = 18f;
            }
            float legSwing = sw * (38f * run01 + 22f * walk01);
            Vector3 lL = new Vector3(legSwing, 0f, 0f), lR = new Vector3(-legSwing, 0f, 0f);
            // knees fold on the recovery swing
            float tKneeL = 12f + 55f * run01 * Mathf.Max(0f, Mathf.Sin(phase + 1.2f)) + 15f * walk01;
            float tKneeR = 12f + 55f * run01 * Mathf.Max(0f, Mathf.Sin(phase + 1.2f + Mathf.PI)) + 15f * walk01;
            Vector3 headE = new Vector3(8f + sw * 4f * run01, 0f, Mathf.Sin(Time.time * 0.7f + seed) * 8f);

            switch (state)
            {
                case Infected.State.Attack:
                {
                    // raise both arms overhead, then slam them down at the strike
                    float up = p < 0.6f ? Mathf.SmoothStep(0f, 1f, p / 0.6f) : 1f - Mathf.SmoothStep(0f, 1f, (p - 0.6f) / 0.25f);
                    aL = new Vector3(Mathf.Lerp(-35f, -160f, up), -10f, -15f);
                    aR = new Vector3(Mathf.Lerp(-35f, -160f, up), 10f, 15f);
                    tLean = Mathf.Lerp(30f, -5f, up);
                    tElbow = Mathf.Lerp(30f, 10f, up);
                    tKneeL = tKneeR = 25f;
                    break;
                }
                case Infected.State.Climb:
                {
                    float c = Mathf.Sin(Time.time * 7f + seed);
                    aL = new Vector3(-165f + c * 15f, 0f, -8f);
                    aR = new Vector3(-165f - c * 15f, 0f, 8f);
                    lL = new Vector3(-45f + c * 30f, 0f, 0f);
                    lR = new Vector3(-45f - c * 30f, 0f, 0f);
                    tLean = 25f;
                    tElbow = 45f;
                    tKneeL = tKneeR = 75f;
                    headE = new Vector3(-20f, 0f, 0f);   // looking up at the edge
                    break;
                }
                case Infected.State.Stumble:
                {
                    float a = Mathf.Sin(Mathf.Clamp01(p) * Mathf.PI);
                    tLean = Mathf.Lerp(tLean, 45f, a);
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
                    tLean = 0f; tElbow = 10f; tKneeL = 5f; tKneeR = 10f;
                    headE = new Vector3(0f, 0f, 30f);
                    break;
                case Infected.State.GettingUp:
                    aL = new Vector3(-60f, 0f, -30f);
                    aR = new Vector3(-60f, 0f, 30f);
                    lL = new Vector3(-60f * (1f - p), 0f, 0f);
                    lR = new Vector3(-30f * (1f - p), 0f, 0f);
                    tLean = 40f * (1f - p);
                    tKneeL = tKneeR = 90f * (1f - p) + 12f;
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
            float armK = state == Infected.State.Attack ? 1f - Mathf.Exp(-poseSharpness * 2.5f * dt) : k;
            armL = Quaternion.Slerp(armL, Quaternion.Euler(aL), armK);
            armR = Quaternion.Slerp(armR, Quaternion.Euler(aR), armK);
            legL = Quaternion.Slerp(legL, Quaternion.Euler(lL), k);
            legR = Quaternion.Slerp(legR, Quaternion.Euler(lR), k);
            headQ = Quaternion.Slerp(headQ, Quaternion.Euler(headE) * twitch, k);
            lean = Mathf.Lerp(lean, tLean, k);
            elbow = Mathf.Lerp(elbow, tElbow, k);
            kneeL = Mathf.Lerp(kneeL, tKneeL, k);
            kneeR = Mathf.Lerp(kneeR, tKneeR, k);
            bob = Mathf.Abs(sw) * 0.04f * run01;

            if (rigged) ApplyRig();
            else ApplyBoxes();
        }

        void ApplyBoxes()
        {
            bArmL.localRotation = armL;
            bArmR.localRotation = armR;
            bLegL.localRotation = legL;
            bLegR.localRotation = legR;
            bSpine.localRotation = Quaternion.Euler(lean, 0f, 0f);
            bNeck.localRotation = headQ;
            bHips.localPosition = new Vector3(0f, 0.88f - bob, 0f);
        }

        void ApplyRig()
        {
            // start every frame from the rest pose, then layer the procedural pose on top
            foreach (var kv in bindLocal) kv.Key.localRotation = kv.Value;
            rHips.localPosition = hipsBindLocalPos;

            Quaternion root = transform.rotation;
            Vector3 right = root * Vector3.right, up = root * Vector3.up;

            rHips.position -= up * bob;
            Rotate(rSpine, Quaternion.AngleAxis(lean * 0.4f, right));
            Rotate(rSpine1, Quaternion.AngleAxis(lean * 0.3f, right));
            Rotate(rSpine2, Quaternion.AngleAxis(lean * 0.3f, right));

            // arms hang off the leaning torso, like the box arms do
            Quaternion torso = root * Quaternion.Euler(lean, 0f, 0f);
            AimLimb(rArmL, rForeL, rHandL, torso * armL * Vector3.down, -elbow, right);
            AimLimb(rArmR, rForeR, rHandR, torso * armR * Vector3.down, -elbow, right);
            AimLimb(rThighL, rShinL, rFootL, root * legL * Vector3.down, kneeL, right);
            AimLimb(rThighR, rShinR, rFootR, root * legR * Vector3.down, kneeR, right);

            Quaternion headDelta = root * headQ * Quaternion.Inverse(root);
            Rotate(rNeck, Quaternion.Slerp(Quaternion.identity, headDelta, 0.5f));
            if (rHead) Rotate(rHead, Quaternion.Slerp(Quaternion.identity, headDelta, 0.5f));

            // the head hitbox rides on the head bone so headshots land where the head really is
            if (HeadHitbox && rHead)
                HeadHitbox.transform.position = rHeadTop ? (rHead.position + rHeadTop.position) * 0.5f : rHead.position + up * 0.1f;
        }

        static void Rotate(Transform bone, Quaternion worldDelta)
        {
            if (bone) bone.rotation = worldDelta * bone.rotation;
        }

        /// <summary>Points the upper bone along <paramref name="dir"/>, then the lower one bent by
        /// <paramref name="bend"/> degrees about <paramref name="bendAxis"/>.</summary>
        static void AimLimb(Transform upper, Transform lower, Transform end, Vector3 dir, float bend, Vector3 bendAxis)
        {
            Aim(upper, lower, dir);
            Aim(lower, end, Quaternion.AngleAxis(bend, bendAxis) * dir);
        }

        static void Aim(Transform bone, Transform child, Vector3 dir)
        {
            Vector3 cur = child.position - bone.position;
            if (cur.sqrMagnitude < 1e-6f || dir.sqrMagnitude < 1e-6f) return;
            bone.rotation = Quaternion.FromToRotation(cur, dir) * bone.rotation;
        }

        static Transform Bone(Transform root, string boneName)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == boneName || t.name.EndsWith(":" + boneName)) return t;
            return null;
        }

        // ------------------------------------------------------------------ box builder

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
