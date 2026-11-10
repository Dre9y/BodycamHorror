using UnityEngine;
using UnityEngine.Rendering;

namespace BodycamHorror.Environment
{
    /// <summary>
    /// Makes the air around a point or spot light glow, as if it hung in dust or thin smoke: the
    /// "Light Haze" shader is drawn in a box around the light's range. Follows the light's colour,
    /// intensity, range and cone, so flicker and swinging carry over on their own.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(Light))]
    public class LightHaze : MonoBehaviour
    {
        [Tooltip("Material with the BodycamHorror/Light Haze shader.")]
        public Material material;
        [Tooltip("How thick the air is: share of the light scattered per metre.")]
        [Range(0f, 0.1f)] public float density = 0.012f;
        [Tooltip("Haze reach as a share of the light's range.")]
        [Range(0.1f, 1f)] public float rangeScale = 1f;
        [Tooltip("Let whatever the light shadows cut dark shafts into the haze (the light needs shadows on).")]
        public bool shadows = true;
        [Tooltip("How uneven the air is: 0 = even haze, 1 = drifting wisps of smoke with clear gaps between them.")]
        [Range(0f, 1f)] public float wisps = 0.45f;
        [Tooltip("Size of the glowing source (m). Bigger = softer, dimmer core right at the lamp.")]
        public float sourceSize = 0.06f;

        static readonly int PosId = Shader.PropertyToID("_HazePos");
        static readonly int DirId = Shader.PropertyToID("_HazeDir");
        static readonly int ConeId = Shader.PropertyToID("_HazeCone");
        static readonly int ColorId = Shader.PropertyToID("_HazeColor");

        static Mesh box;
        Light source;
        MaterialPropertyBlock block;

        void OnEnable()
        {
            source = GetComponent<Light>();
        }

        void LateUpdate()
        {
            if (!material || !source || !source.enabled || source.intensity <= 0f || density <= 0f) return;
            if (source.type != LightType.Point && source.type != LightType.Spot) return;
            if (!box) box = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            block ??= new MaterialPropertyBlock();

            Vector3 pos = transform.position;
            float range = source.range * rangeScale;
            Color color = source.color.linear * source.intensity * density;
            if (source.useColorTemperature) color *= Mathf.CorrelatedColorTemperatureToRGB(source.colorTemperature);

            // a point light passes every direction: both cone limits below -1
            Vector2 cone = new Vector2(-3f, -2f);
            if (source.type == LightType.Spot)
            {
                cone.x = Mathf.Cos(source.spotAngle * 0.5f * Mathf.Deg2Rad);
                cone.y = Mathf.Max(Mathf.Cos(source.innerSpotAngle * 0.5f * Mathf.Deg2Rad), cone.x + 0.001f);
            }
            bool shadowed = shadows && source.shadows != LightShadows.None;

            block.SetVector(PosId, new Vector4(pos.x, pos.y, pos.z, range));
            Vector3 forward = transform.forward;
            block.SetVector(DirId, new Vector4(forward.x, forward.y, forward.z, wisps));
            block.SetVector(ConeId, new Vector4(cone.x, cone.y, shadowed ? 1f : 0f, sourceSize));
            block.SetVector(ColorId, color);

            float size = range * 2f;
            var rp = new RenderParams(material)
            {
                matProps = block,
                worldBounds = new Bounds(pos, Vector3.one * size),
                shadowCastingMode = ShadowCastingMode.Off,
                receiveShadows = false,
                layer = gameObject.layer,
            };
            Graphics.RenderMesh(rp, box, 0, Matrix4x4.TRS(pos, Quaternion.identity, Vector3.one * size));
        }
    }
}
