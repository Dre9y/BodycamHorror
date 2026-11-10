using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

namespace BodycamHorror.Player
{
    /// <summary>
    /// The body camera's automatic exposure and white balance. A few times a second it looks at a
    /// thumbnail of the frame as rendered (before post-processing), works out how bright the scene is
    /// and what colour its light has, and eases a gain towards what a cheap camera would settle on:
    /// brighter in the dark, stopped down when a lamp or the flashlight on a near wall fills the view,
    /// and pulled part of the way back to neutral under coloured light. Opening up is slow, stopping
    /// down is quick, so walking out of a lit room leaves you blind for a moment.
    /// The gain is applied to the frame before bloom, grading and the rest of the post stack.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class AutoExposure : MonoBehaviour
    {
        [Tooltip("The Hidden/BodycamHorror/Auto Exposure shader (kept as a reference so builds include it).")]
        public Shader shader;

        [Header("Exposure")]
        [Tooltip("Scene brightness the camera aims for (linear, before grading).")]
        public float targetBrightness = 0.05f;
        [Tooltip("How far it goes towards that: 1 = every scene ends up equally bright, 0 = no auto exposure.")]
        [Range(0f, 1f)] public float strength = 0.6f;
        [Tooltip("Most it will darken / brighten the picture, in stops.")]
        public float minStops = -2f;
        public float maxStops = 1f;
        [Tooltip("Stops per second when the scene got darker (opening up: slow).")]
        public float brightenSpeed = 0.7f;
        [Tooltip("Stops per second when the scene got brighter (stopping down: quick).")]
        public float darkenSpeed = 3f;
        [Tooltip("How much more the middle of the frame counts than the edges.")]
        [Range(0f, 1f)] public float centreWeight = 0.6f;

        [Header("White balance")]
        [Tooltip("How far it pulls coloured light back towards neutral: 0 = off, 1 = everything ends up grey-lit.")]
        [Range(0f, 1f)] public float whiteBalance = 0.35f;
        [Tooltip("Most any one colour channel may be pushed or pulled.")]
        [Range(0f, 0.6f)] public float maxColourShift = 0.22f;
        [Tooltip("How quickly the balance follows (1/s).")]
        public float whiteBalanceSpeed = 0.8f;

        /// <summary>Exposure right now, in stops (0 = untouched).</summary>
        public float Stops { get; private set; }
        /// <summary>White-balance gain right now.</summary>
        public Vector3 ColourGain { get; private set; } = Vector3.one;

        const int MeterWidth = 16, MeterHeight = 9;
        static readonly int GainId = Shader.PropertyToID("_ExposureGain");

        Camera cam;
        Material material;
        RTHandle meter;
        ExposurePass pass;
        bool reading, measured;
        float sceneBrightness;
        Vector3 sceneColour = Vector3.one;

        void OnEnable()
        {
            cam = GetComponent<Camera>();
            if (!shader) shader = Shader.Find("Hidden/BodycamHorror/Auto Exposure");
            if (!shader) { enabled = false; return; }
            material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            material.SetVector(GainId, Vector4.one);
            meter = RTHandles.Alloc(MeterWidth, MeterHeight, GraphicsFormat.R32G32B32A32_SFloat, filterMode: FilterMode.Bilinear, name: "ExposureMeter");
            pass = new ExposurePass(material, meter);
            RenderPipelineManager.beginCameraRendering += OnBeginCamera;
        }

        void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
            meter?.Release();
            meter = null;
            if (material) Destroy(material);
        }

        void OnBeginCamera(ScriptableRenderContext context, Camera rendering)
        {
            if (rendering != cam || pass == null) return;
            rendering.GetUniversalAdditionalCameraData().scriptableRenderer.EnqueuePass(pass);
        }

        void Update()
        {
            if (!reading && meter != null && Time.frameCount % 3 == 0)
            {
                reading = true;
                AsyncGPUReadback.Request(meter.rt, 0, GraphicsFormat.R32G32B32A32_SFloat, OnRead);
            }
            if (!measured) return;

            float dt = Time.deltaTime;
            float want = Mathf.Clamp(strength * Mathf.Log(targetBrightness / Mathf.Max(sceneBrightness, 1e-4f), 2f), minStops, maxStops);
            Stops = Mathf.MoveTowards(Stops, want, (want > Stops ? brightenSpeed : darkenSpeed) * dt);

            // grey-world balance: scale each channel towards the scene's average being neutral, part of the way
            float grey = Vector3.Dot(sceneColour, new Vector3(0.2126f, 0.7152f, 0.0722f));
            Vector3 gain = Vector3.one;
            if (grey > 1e-4f)
            {
                for (int i = 0; i < 3; i++)
                {
                    float full = grey / Mathf.Max(sceneColour[i], 1e-4f);
                    gain[i] = Mathf.Clamp(Mathf.Lerp(1f, full, whiteBalance), 1f - maxColourShift, 1f + maxColourShift);
                }
                // the balance must not change how bright the scene's own light comes out
                float lum = Vector3.Dot(Vector3.Scale(sceneColour, gain), new Vector3(0.2126f, 0.7152f, 0.0722f));
                if (lum > 1e-4f) gain *= grey / lum;
            }
            ColourGain = Vector3.Lerp(ColourGain, gain, 1f - Mathf.Exp(-whiteBalanceSpeed * dt));

            float exposure = Mathf.Pow(2f, Stops);
            material.SetVector(GainId, new Vector4(ColourGain.x * exposure, ColourGain.y * exposure, ColourGain.z * exposure, 1f));
        }

        void OnRead(AsyncGPUReadbackRequest request)
        {
            reading = false;
            if (request.hasError || !this) return;
            var pixels = request.GetData<Vector4>();
            if (pixels.Length < MeterWidth * MeterHeight) return;

            // brightness: centre-weighted average of the logs, so one bare bulb does not speak for the whole frame
            float logSum = 0f, weightSum = 0f;
            Vector3 colourSum = Vector3.zero;
            for (int y = 0; y < MeterHeight; y++)
            {
                for (int x = 0; x < MeterWidth; x++)
                {
                    Vector4 p = pixels[y * MeterWidth + x];
                    var rgb = new Vector3(Mathf.Clamp(p.x, 0f, 4f), Mathf.Clamp(p.y, 0f, 4f), Mathf.Clamp(p.z, 0f, 4f));
                    float lum = Vector3.Dot(rgb, new Vector3(0.2126f, 0.7152f, 0.0722f));
                    float dx = (x + 0.5f) / MeterWidth * 2f - 1f, dy = (y + 0.5f) / MeterHeight * 2f - 1f;
                    float weight = Mathf.Lerp(1f, Mathf.Clamp01(1.2f - Mathf.Sqrt(dx * dx + dy * dy)), centreWeight);
                    logSum += Mathf.Log(lum + 0.002f) * weight;
                    weightSum += weight;
                    colourSum += rgb * weight;
                }
            }
            if (weightSum <= 0f) return;
            sceneBrightness = Mathf.Exp(logSum / weightSum);
            sceneColour = colourSum / weightSum;
            measured = true;
        }

        /// <summary>Takes the thumbnail the meter reads, then swaps the frame for one with the gain applied.</summary>
        class ExposurePass : ScriptableRenderPass
        {
            readonly Material material;
            readonly RTHandle meter;

            public ExposurePass(Material material, RTHandle meter)
            {
                this.material = material;
                this.meter = meter;
                renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                var resources = frameData.Get<UniversalResourceData>();
                if (resources.isActiveTargetBackBuffer || !material || meter == null || meter.rt == null) return;
                TextureHandle source = resources.activeColorTexture;

                // down in steps, so the thumbnail is an average and not a few stray pixels
                TextureDesc small = renderGraph.GetTextureDesc(source);
                small.depthBufferBits = 0;
                small.msaaSamples = MSAASamples.None;
                small.clearBuffer = false;
                small.width = 256; small.height = 144; small.name = "ExposureMeter256";
                TextureHandle step1 = renderGraph.CreateTexture(small);
                small.width = 64; small.height = 36; small.name = "ExposureMeter64";
                TextureHandle step2 = renderGraph.CreateTexture(small);
                renderGraph.AddBlitPass(source, step1, Vector2.one, Vector2.zero, passName: "Exposure Meter 256");
                renderGraph.AddBlitPass(step1, step2, Vector2.one, Vector2.zero, passName: "Exposure Meter 64");
                renderGraph.AddBlitPass(step2, renderGraph.ImportTexture(meter), Vector2.one, Vector2.zero, passName: "Exposure Meter 16");

                TextureDesc full = renderGraph.GetTextureDesc(source);
                full.depthBufferBits = 0;
                full.clearBuffer = false;
                full.name = "AutoExposed";
                TextureHandle exposed = renderGraph.CreateTexture(full);
                renderGraph.AddBlitPass(new RenderGraphUtils.BlitMaterialParameters(source, exposed, material, 0), "Auto Exposure");
                resources.cameraColor = exposed;
            }
        }
    }
}
