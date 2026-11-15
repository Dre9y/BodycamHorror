using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace BodycamHorror.Player
{
    /// <summary>
    /// Draws the player's own body so the first-person hands and gun always stay over it.
    /// The body sits on its own layer (<see cref="BodyLayer"/>), which the renderers leave out of their opaque pass.
    /// Right after the opaques this pass draws the hands and gun again into the stencil only
    /// (the view-model rendering layer; they already have their depth, so walls still hide them as before),
    /// then draws the body everywhere the stencil is not marked. The arms can no longer sink into the chest.
    /// </summary>
    public static class PlayerBodyPass
    {
        /// <summary>GameObject layer of the body ("PlayerBody"): left out of the renderers' own opaque pass.</summary>
        public const int BodyLayer = 9;
        const int StencilBit = 64;

        static readonly List<PlayerBody> active = new List<PlayerBody>();
        static bool hooked;
        static BodyPass pass;
        static Material stencilMaterial;

        public static void Register(PlayerBody body, Shader stencilShader)
        {
            if (!active.Contains(body)) active.Add(body);
            if (!stencilMaterial)
            {
                if (!stencilShader) stencilShader = Shader.Find("Hidden/BodycamHorror/ViewModelStencil");
                if (stencilShader) stencilMaterial = new Material(stencilShader) { hideFlags = HideFlags.HideAndDontSave };
            }
            if (!hooked)
            {
                hooked = true;
                RenderPipelineManager.beginCameraRendering += OnBeginCamera;
            }
        }

        public static void Unregister(PlayerBody body) => active.Remove(body);

        static void OnBeginCamera(ScriptableRenderContext context, Camera camera)
        {
            if (camera.cameraType != CameraType.Game && camera.cameraType != CameraType.SceneView) return;
            if (active.Count == 0 || !stencilMaterial) return;
            if ((camera.cullingMask & (1 << BodyLayer)) == 0) return;
            pass ??= new BodyPass();
            pass.stencilMaterial = stencilMaterial;
            camera.GetUniversalAdditionalCameraData().scriptableRenderer.EnqueuePass(pass);
        }

        class BodyPass : ScriptableRenderPass
        {
            public Material stencilMaterial;
            static readonly List<ShaderTagId> forwardTags = new List<ShaderTagId>
            {
                new ShaderTagId("UniversalForward"), new ShaderTagId("UniversalForwardOnly"), new ShaderTagId("SRPDefaultUnlit")
            };
            static readonly ShaderTagId[] tagValue = { ShaderTagId.none };
            static readonly RenderStateBlock[] stateBlock = new RenderStateBlock[1];

            class PassData
            {
                public RendererListHandle hands;
                public RendererListHandle body;
            }

            public BodyPass()
            {
                renderPassEvent = RenderPassEvent.AfterRenderingOpaques;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                var resources = frameData.Get<UniversalResourceData>();
                var rendering = frameData.Get<UniversalRenderingData>();
                var cameraData = frameData.Get<UniversalCameraData>();
                var lights = frameData.Get<UniversalLightData>();
                SortingCriteria sort = cameraData.defaultOpaqueSortFlags;

                // the hands and gun, into the stencil only
                DrawingSettings handsDraw = RenderingUtils.CreateDrawingSettings(forwardTags, rendering, cameraData, lights, sort);
                handsDraw.overrideMaterial = stencilMaterial;
                handsDraw.overrideMaterialPassIndex = 0;
                var handsFilter = new FilteringSettings(RenderQueueRange.opaque, ~0, ViewModelLighting.ViewModelLayer);
                RendererListHandle hands = renderGraph.CreateRendererList(new RendererListParams(rendering.cullResults, handsDraw, handsFilter));

                // the body, lit as usual, wherever the hands are not
                DrawingSettings bodyDraw = RenderingUtils.CreateDrawingSettings(forwardTags, rendering, cameraData, lights, sort);
                var bodyFilter = new FilteringSettings(RenderQueueRange.opaque, 1 << BodyLayer);
                var block = new RenderStateBlock(RenderStateMask.Stencil)
                {
                    stencilReference = StencilBit,
                    stencilState = new StencilState(true, (byte)StencilBit, 0, CompareFunction.NotEqual, StencilOp.Keep, StencilOp.Keep, StencilOp.Keep)
                };
                stateBlock[0] = block;
                var tags = new NativeArray<ShaderTagId>(tagValue, Allocator.Temp);
                var blocks = new NativeArray<RenderStateBlock>(stateBlock, Allocator.Temp);
                var bodyParams = new RendererListParams(rendering.cullResults, bodyDraw, bodyFilter)
                {
                    tagValues = tags,
                    stateBlocks = blocks,
                    isPassTagName = false
                };
                RendererListHandle body = renderGraph.CreateRendererList(bodyParams);

                using (var builder = renderGraph.AddRasterRenderPass<PassData>("Player Body", out var data))
                {
                    data.hands = hands;
                    data.body = body;
                    builder.UseRendererList(hands);
                    builder.UseRendererList(body);
                    builder.SetRenderAttachment(resources.activeColorTexture, 0);
                    builder.SetRenderAttachmentDepth(resources.activeDepthTexture, AccessFlags.ReadWrite);
                    builder.UseAllGlobalTextures(true);   // shadow maps, ambient occlusion, light lists
                    builder.SetRenderFunc((PassData d, RasterGraphContext context) =>
                    {
                        context.cmd.DrawRendererList(d.hands);
                        context.cmd.DrawRendererList(d.body);
                    });
                }
            }
        }
    }
}
