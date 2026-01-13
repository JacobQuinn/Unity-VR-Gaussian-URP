// SPDX-License-Identifier: MIT
// Add this as a NEW file: GaussianSplatURPFeature.cs

//#if UNITY_PIPELINE_URP
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace GaussianSplatting.Runtime.URP
{
    public class GaussianSplatURPFeature : ScriptableRendererFeature
    {
        [System.Serializable]
        public class GaussianSplatSettings
        {
            public RenderPassEvent renderPassEvent = RenderPassEvent.BeforeRenderingTransparents;
            public bool supportVR = true;
        }

        public GaussianSplatSettings settings = new GaussianSplatSettings();
        GaussianSplatURPPass m_RenderPass;

        public override void Create()
        {
            m_RenderPass = new GaussianSplatURPPass(settings);
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            // Skip preview cameras
            if (renderingData.cameraData.cameraType == CameraType.Preview)
                return;

            // Gather splats for this camera
            if (!GaussianSplatRenderSystem.instance.GatherSplatsForCamera(renderingData.cameraData.camera))
                return;

            m_RenderPass.Setup(renderer);
            renderer.EnqueuePass(m_RenderPass);
        }

        protected override void Dispose(bool disposing)
        {
            m_RenderPass?.Dispose();
        }
    }

    public class GaussianSplatURPPass : ScriptableRenderPass
    {
        const string k_ProfilerTag = "Gaussian Splats URP";
        readonly ProfilingSampler m_ProfilingSampler;
        readonly GaussianSplatURPFeature.GaussianSplatSettings m_Settings;

        RTHandle m_GaussianRT;
        ScriptableRenderer m_Renderer;

        public GaussianSplatURPPass(GaussianSplatURPFeature.GaussianSplatSettings settings)
        {
            m_Settings = settings;
            m_ProfilingSampler = new ProfilingSampler(k_ProfilerTag);
            renderPassEvent = settings.renderPassEvent;
        }

        public void Setup(ScriptableRenderer renderer)
        {
            m_Renderer = renderer;
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            var descriptor = renderingData.cameraData.cameraTargetDescriptor;
            descriptor.depthBufferBits = 0;
            descriptor.colorFormat = RenderTextureFormat.ARGBHalf;
            descriptor.msaaSamples = 1;

            // VR: This descriptor will automatically handle stereo rendering
            RenderingUtils.ReAllocateIfNeeded(ref m_GaussianRT, descriptor, FilterMode.Point, 
                TextureWrapMode.Clamp, name: "_GaussianSplatRT");
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            var camera = renderingData.cameraData.camera;
            var cmd = CommandBufferPool.Get(k_ProfilerTag);

            using (new ProfilingScope(cmd, m_ProfilingSampler))
            {
                // Clear the Gaussian RT
                cmd.SetRenderTarget(m_GaussianRT);
                cmd.ClearRenderTarget(true, true, new Color(0, 0, 0, 0));

                // Sort and render all splats
                Material matComposite = GaussianSplatRenderSystem.instance.SortAndRenderSplats(camera, cmd);

                if (matComposite != null)
                {
                    // Get the camera color target
                    RTHandle cameraColorTarget = m_Renderer.cameraColorTargetHandle;

                    // Composite the splats onto the camera target
                    cmd.BeginSample(GaussianSplatRenderSystem.s_ProfCompose);
                    
                    // Set the Gaussian RT as a global texture so the composite shader can read it
                    cmd.SetGlobalTexture(GaussianSplatRenderer.Props.GaussianSplatRT, m_GaussianRT);
                    
                    // Blit/compose to camera target
                    cmd.SetRenderTarget(cameraColorTarget);
                    cmd.DrawProcedural(Matrix4x4.identity, matComposite, 0, MeshTopology.Triangles, 3, 1);
                    
                    cmd.EndSample(GaussianSplatRenderSystem.s_ProfCompose);
                }
            }

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        public override void OnCameraCleanup(CommandBuffer cmd)
        {
            // Cleanup is handled by RTHandle system
        }

        public void Dispose()
        {
            m_GaussianRT?.Release();
        }
    }
}
