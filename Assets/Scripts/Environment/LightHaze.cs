using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace BodycamHorror.Environment
{
    /// <summary>
    /// Makes the air around a point or spot light glow, as if it hung in dust or thin smoke: the
    /// "Light Haze" shader is drawn in a box around the light's range. Follows the light's colour,
    /// intensity, range and cone, so flicker and swinging carry over on their own.
    /// All the lamps in view are drawn together into one half-size buffer, which is then laid over
    /// the picture: haze is soft, and marching it for every full-size pixel was most of the frame.
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
        static readonly int TargetId = Shader.PropertyToID("_HazeTarget");

        static readonly List<LightHaze> Active = new List<LightHaze>();
        static Mesh box;
        static HazePass pass;
        static bool hooked;

        Light source;
        MaterialPropertyBlock block;
        Matrix4x4 matrix;
        Bounds bounds;
        bool drawn;

        void OnEnable()
        {
            source = GetComponent<Light>();
            if (!Active.Contains(this)) Active.Add(this);
            if (!hooked)
            {
                hooked = true;
                RenderPipelineManager.beginCameraRendering += OnBeginCamera;
            }
        }

        void OnDisable()
        {
            Active.Remove(this);
            drawn = false;
        }

        void LateUpdate()
        {
            drawn = false;
            if (!material || !source || !source.enabled || source.intensity <= 0f || density <= 0f) return;
            if (source.type != LightType.Point && source.type != LightType.Spot) return;
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
            matrix = Matrix4x4.TRS(pos, Quaternion.identity, Vector3.one * size);
            bounds = new Bounds(pos, Vector3.one * size);
            drawn = true;
        }

        static void OnBeginCamera(ScriptableRenderContext context, Camera camera)
        {
            if (camera.cameraType != CameraType.Game && camera.cameraType != CameraType.SceneView) return;
            Material shared = null;
            for (int i = 0; i < Active.Count; i++)
                if (Active[i] && Active[i].drawn) { shared = Active[i].material; break; }
            if (!shared) return;
            if (!box) box = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            pass ??= new HazePass();
            pass.composite = shared;
            camera.GetUniversalAdditionalCameraData().scriptableRenderer.EnqueuePass(pass);
        }

        /// <summary>Every lamp's haze into a half-size buffer, then that buffer over the picture.</summary>
        class HazePass : ScriptableRenderPass
        {
            public Material composite;

            readonly Plane[] frustum = new Plane[6];

            class DrawData
            {
                public readonly List<LightHaze> items = new List<LightHaze>();
                public Vector4 target;
            }

            class CompositeData
            {
                public TextureHandle source;
                public Material material;
                public Vector4 target;
            }

            public HazePass()
            {
                renderPassEvent = RenderPassEvent.AfterRenderingTransparents;
                ConfigureInput(ScriptableRenderPassInput.Depth);
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                var resources = frameData.Get<UniversalResourceData>();
                var cameraData = frameData.Get<UniversalCameraData>();
                if (resources.isActiveTargetBackBuffer || !composite) return;

                TextureDesc desc = renderGraph.GetTextureDesc(resources.activeColorTexture);
                desc.width = Mathf.Max(1, desc.width / 2);
                desc.height = Mathf.Max(1, desc.height / 2);
                desc.depthBufferBits = 0;
                desc.msaaSamples = MSAASamples.None;
                desc.clearBuffer = true;
                desc.clearColor = Color.clear;
                desc.name = "LightHaze";
                TextureHandle haze = renderGraph.CreateTexture(desc);
                var target = new Vector4(desc.width, desc.height, 1f / desc.width, 1f / desc.height);

                using (var builder = renderGraph.AddRasterRenderPass<DrawData>("Light Haze", out var data))
                {
                    // only the lamps whose reach is in view at all
                    GeometryUtility.CalculateFrustumPlanes(cameraData.camera, frustum);
                    data.items.Clear();
                    for (int i = 0; i < Active.Count; i++)
                    {
                        LightHaze h = Active[i];
                        if (h && h.drawn && GeometryUtility.TestPlanesAABB(frustum, h.bounds)) data.items.Add(h);
                    }
                    data.target = target;
                    builder.SetRenderAttachment(haze, 0);
                    builder.UseAllGlobalTextures(true);   // scene depth and the lights' shadow maps
                    builder.AllowGlobalStateModification(true);
                    builder.SetRenderFunc((DrawData d, RasterGraphContext context) =>
                    {
                        context.cmd.SetGlobalVector(TargetId, d.target);
                        for (int i = 0; i < d.items.Count; i++)
                        {
                            LightHaze h = d.items[i];
                            if (h && h.material) context.cmd.DrawMesh(box, h.matrix, h.material, 0, 0, h.block);
                        }
                    });
                }

                using (var builder = renderGraph.AddRasterRenderPass<CompositeData>("Light Haze Composite", out var data))
                {
                    data.source = haze;
                    data.material = composite;
                    data.target = target;
                    builder.UseTexture(haze);
                    builder.SetRenderAttachment(resources.activeColorTexture, 0);
                    builder.AllowGlobalStateModification(true);
                    builder.SetRenderFunc((CompositeData d, RasterGraphContext context) =>
                    {
                        context.cmd.SetGlobalVector(TargetId, d.target);
                        Blitter.BlitTexture(context.cmd, d.source, new Vector4(1f, 1f, 0f, 0f), d.material, 1);
                    });
                }
            }
        }
    }
}
