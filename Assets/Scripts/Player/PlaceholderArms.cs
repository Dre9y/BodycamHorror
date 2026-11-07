using UnityEngine;
using UnityEngine.Rendering;

namespace BodycamHorror.Player
{
    /// <summary>
    /// Simple box arms parented to the camera. They swing with the footstep phase and take poses for
    /// mantling, vaulting, sliding, bracing against walls and stumbling. Placeholder until real hands.
    /// </summary>
    public class PlaceholderArms : MonoBehaviour
    {
        public BodycamPlayerController player;
        public BodycamCameraRig rig;
        public Color sleeveColor = new Color(0.10f, 0.11f, 0.10f);
        public Color handColor = new Color(0.45f, 0.34f, 0.29f);
        public float poseSharpness = 14f;

        Transform left, right;

        static readonly Vector3 RestL = new Vector3(-0.24f, -0.30f, 0.42f);
        static readonly Vector3 RestR = new Vector3(0.24f, -0.30f, 0.42f);

        void Awake()
        {
            if (!player) player = GetComponentInParent<BodycamPlayerController>();
            if (!rig) rig = GetComponentInParent<BodycamCameraRig>();
            left = transform.Find("LeftArm") ?? BuildArm("LeftArm");
            right = transform.Find("RightArm") ?? BuildArm("RightArm");
            left.localPosition = RestL;
            right.localPosition = RestR;
        }

        Transform BuildArm(string armName)
        {
            var root = new GameObject(armName).transform;
            root.SetParent(transform, false);
            root.gameObject.layer = gameObject.layer;

            var baseMat = GraphicsSettings.currentRenderPipeline ? GraphicsSettings.currentRenderPipeline.defaultMaterial : null;
            MakeBox(root, "Sleeve", new Vector3(0f, 0f, -0.27f), new Vector3(0.10f, 0.10f, 0.50f), baseMat, sleeveColor);
            MakeBox(root, "Hand", new Vector3(0f, -0.005f, 0.04f), new Vector3(0.085f, 0.045f, 0.13f), baseMat, handColor);
            return root;
        }

        static void MakeBox(Transform parent, string boxName, Vector3 pos, Vector3 scale, Material baseMat, Color color)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = boxName;
            go.layer = parent.gameObject.layer;
            DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            var r = go.GetComponent<MeshRenderer>();
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            if (baseMat)
            {
                var m = new Material(baseMat) { color = color };
                if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
                if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.15f);
                r.sharedMaterial = m;
            }
        }

        void LateUpdate()
        {
            if (!player) return;
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            Vector3 pl = RestL, pr = RestR;
            Vector3 rl = new Vector3(5f, 8f, 0f), rr = new Vector3(5f, -8f, 0f);   // euler

            float phase = rig ? rig.StepPhase : 0f;
            float bob = rig ? rig.BobAmount : 0f;
            float swing = Mathf.Sin(phase);
            float sprint = player.SprintBlend;

            // Running swing: arms pump opposite to each other
            pl.z += swing * 0.07f * bob; pr.z -= swing * 0.07f * bob;
            pl.y += Mathf.Abs(swing) * 0.02f * bob - sprint * 0.04f;
            pr.y += Mathf.Abs(swing) * 0.02f * bob - sprint * 0.04f;
            rl.x += swing * 12f * bob; rr.x -= swing * 12f * bob;
            if (player.IsCrouching) { pl.y -= 0.03f; pr.y -= 0.03f; }

            float p = player.ActionProgress;
            switch (player.State)
            {
                case MoveState.Airborne:
                    pl.y += 0.06f; pr.y += 0.06f; pl.x -= 0.04f; pr.x += 0.04f;
                    break;
                case MoveState.Mantling:
                    // reach up and grab, then push the body over the edge
                    float reach = 1f - Mathf.Clamp01((p - 0.45f) / 0.55f);
                    pl = Vector3.Lerp(new Vector3(-0.22f, -0.38f, 0.45f), new Vector3(-0.25f, 0.02f, 0.62f), reach);
                    pr = Vector3.Lerp(new Vector3(0.22f, -0.38f, 0.45f), new Vector3(0.25f, 0.02f, 0.62f), reach);
                    rl = Vector3.Lerp(new Vector3(45f, 5f, 0f), new Vector3(-30f, 5f, 0f), reach);
                    rr = Vector3.Lerp(new Vector3(45f, -5f, 0f), new Vector3(-30f, -5f, 0f), reach);
                    break;
                case MoveState.Vaulting:
                    pl = new Vector3(-0.12f, -0.48f, 0.5f); rl = new Vector3(55f, 10f, -10f);   // planted hand
                    pr = new Vector3(0.42f, -0.18f, 0.3f); rr = new Vector3(0f, -30f, 25f);     // balance arm out
                    break;
                case MoveState.Sliding:
                    pl = new Vector3(-0.22f, -0.32f, 0.5f); rl = new Vector3(10f, 5f, 0f);
                    pr = new Vector3(0.38f, -0.55f, 0.05f); rr = new Vector3(40f, -10f, 0f);
                    break;
                case MoveState.Bumped:
                    // braced against the wall
                    float brace = 1f - p;
                    pl = Vector3.Lerp(pl, new Vector3(-0.2f, -0.12f, 0.58f), brace);
                    pr = Vector3.Lerp(pr, new Vector3(0.2f, -0.12f, 0.58f), brace);
                    rl = Vector3.Lerp(rl, new Vector3(-15f, 0f, 0f), brace);
                    rr = Vector3.Lerp(rr, new Vector3(-15f, 0f, 0f), brace);
                    break;
                case MoveState.Stumbling:
                    pl = new Vector3(-0.32f, -0.30f, 0.6f); rl = new Vector3(20f, 15f, -20f);
                    pr = new Vector3(0.32f, -0.30f, 0.6f); rr = new Vector3(20f, -15f, 20f);
                    break;
            }

            float k = 1f - Mathf.Exp(-poseSharpness * dt);
            left.localPosition = Vector3.Lerp(left.localPosition, pl, k);
            right.localPosition = Vector3.Lerp(right.localPosition, pr, k);
            left.localRotation = Quaternion.Slerp(left.localRotation, Quaternion.Euler(rl), k);
            right.localRotation = Quaternion.Slerp(right.localRotation, Quaternion.Euler(rr), k);
        }
    }
}
