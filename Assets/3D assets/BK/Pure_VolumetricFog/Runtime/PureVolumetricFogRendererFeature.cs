using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace BKPureNature
{
    /// <summary>
    /// Renders every active <see cref="PureVolumetricFog"/> into a reduced-resolution
    /// target and composites it onto the camera colour with a depth-aware upsample. When this
    /// feature is present on a camera's renderer, the volumes stop drawing themselves and use
    /// this path instead. Without it, volumes render directly at full resolution.
    /// </summary>
    public class PureVolumetricFogRendererFeature : ScriptableRendererFeature
    {
        public enum FogResolution
        {
            Full = 1,
            Half = 2,
            Quarter = 4
        }

        private const string CompositeShaderResource = "PureVolumetricFog/PureVolumetricFogComposite";

        [Tooltip("Resolution the fog is raymarched at, relative to the camera target. Half costs roughly a quarter of Full; Quarter a sixteenth.")]
        [SerializeField] private FogResolution resolution = FogResolution.Half;
        [Tooltip("Relative depth difference at which a low-resolution fog sample stops contributing to a full-resolution pixel. Lower values keep silhouettes sharper.")]
        [SerializeField, Range(0.01f, 1f)] private float depthTolerance = 0.1f;

        private PureVolumetricFogPass _pass;
        private Material _compositeMaterial;

        private static readonly HashSet<PureVolumetricFogRendererFeature> s_instances =
            new HashSet<PureVolumetricFogRendererFeature>();
        private sealed class CameraRenderState
        {
            public int sequence;
            public int handledAt;
        }

        // Weak keys let camera history expire without retaining destroyed cameras.
        private static ConditionalWeakTable<Camera, CameraRenderState> s_cameraStates =
            new ConditionalWeakTable<Camera, CameraRenderState>();
        private static bool s_sequenceHookInstalled;

        /// <summary>True when an enabled instance of this feature exists on any renderer.</summary>
        public static bool IsInstalled
        {
            get
            {
                foreach (PureVolumetricFogRendererFeature feature in s_instances)
                    if (feature != null && feature.isActive)
                        return true;
                return false;
            }
        }

        public FogResolution Resolution => resolution;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetSessionState()
        {
            RenderPipelineManager.endCameraRendering -= OnEndCameraRendering;
            s_sequenceHookInstalled = false;
            s_cameraStates = new ConditionalWeakTable<Camera, CameraRenderState>();
            s_instances.RemoveWhere(feature => feature == null);

            // Renderer assets can remain loaded between Play Mode sessions.
            if (s_instances.Count > 0)
            {
                RenderPipelineManager.endCameraRendering += OnEndCameraRendering;
                s_sequenceHookInstalled = true;
            }
        }

        public override void Create()
        {
            s_instances.Add(this);
            if (!s_sequenceHookInstalled)
            {
                RenderPipelineManager.endCameraRendering += OnEndCameraRendering;
                s_sequenceHookInstalled = true;
            }
            _pass?.Dispose();
            _pass = new PureVolumetricFogPass
            {
                renderPassEvent = RenderPassEvent.AfterRenderingTransparents,
                requiresIntermediateTexture = true
            };
            _pass.ConfigureInput(ScriptableRenderPassInput.Depth);
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            Camera camera = renderingData.cameraData.camera;
            if (camera.cameraType == CameraType.Preview)
                return;
            if (PureVolumetricFog.ActiveVolumes.Count == 0)
                return;

            if (_compositeMaterial == null)
            {
                Shader shader = Resources.Load<Shader>(CompositeShaderResource);
                if (shader == null)
                    return;
                _compositeMaterial = CoreUtils.CreateEngineMaterial(shader);
            }

            _pass.Setup((int)resolution, depthTolerance, _compositeMaterial);
            renderer.EnqueuePass(_pass);

            CameraRenderState state = s_cameraStates.GetValue(camera, key => new CameraRenderState());
            state.handledAt = state.sequence;
        }

        protected override void Dispose(bool disposing)
        {
            s_instances.Remove(this);
            _pass?.Dispose();
            _pass = null;
            if (s_instances.Count == 0)
            {
                RenderPipelineManager.endCameraRendering -= OnEndCameraRendering;
                s_sequenceHookInstalled = false;
                s_cameraStates = new ConditionalWeakTable<Camera, CameraRenderState>();
            }
            CoreUtils.Destroy(_compositeMaterial);
            _compositeMaterial = null;
        }

        private static void OnEndCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            if (s_cameraStates.TryGetValue(camera, out CameraRenderState state))
                state.sequence++;
        }

        /// <summary>
        /// True when this feature drew the fog during the camera's previous render, so
        /// volumes skip their own direct draw. AddRenderPasses runs after the camera's
        /// beginCameraRendering callback, which is why the previous render is the reference.
        /// </summary>
        internal static bool HandlesCamera(Camera camera)
        {
            return IsInstalled && s_cameraStates.TryGetValue(camera, out CameraRenderState state) &&
                state.handledAt >= state.sequence - 1;
        }

        private class PureVolumetricFogPass : ScriptableRenderPass
        {
            private static readonly int GroundFogPixelScaleId = Shader.PropertyToID("_GroundFogPixelScale");
            private static readonly int GroundFogTexelSizeId = Shader.PropertyToID("_GroundFogTexelSize");
            private static readonly int GroundFogDepthToleranceId = Shader.PropertyToID("_GroundFogDepthTolerance");

            private static readonly ProfilingSampler s_fogSampler = new ProfilingSampler("Pure Volumetric Fog");
            private static readonly ProfilingSampler s_compositeSampler = new ProfilingSampler("Pure Volumetric Fog Composite");

            private class FogPassData
            {
                public List<PureVolumetricFog.DrawRequest> draws;
                public float pixelScale;
            }

            private class CompositePassData
            {
                public TextureHandle fogTexture;
                public Material material;
                public Vector4 texelSize;
                public float depthTolerance;
            }

            private int _divisor = 2;
            private float _depthTolerance = 0.1f;
            private Material _compositeMaterial;
            private readonly List<PureVolumetricFog.DrawRequest> _draws =
                new List<PureVolumetricFog.DrawRequest>();
#if !UNITY_6000_4_OR_NEWER
            // Compatibility Mode (RenderGraph disabled) keeps a persistent target instead.
            private RTHandle _compatibilityFogTexture;
#endif

            public void Setup(int divisor, float depthTolerance, Material compositeMaterial)
            {
                _divisor = Mathf.Max(divisor, 1);
                _depthTolerance = depthTolerance;
                _compositeMaterial = compositeMaterial;
            }

            public void Dispose()
            {
#if !UNITY_6000_4_OR_NEWER
                _compatibilityFogTexture?.Release();
                _compatibilityFogTexture = null;
#endif
            }

            private bool CollectDraws(Camera camera)
            {
                _draws.Clear();
                IReadOnlyList<PureVolumetricFog> volumes = PureVolumetricFog.ActiveVolumes;
                for (int i = 0; i < volumes.Count; i++)
                {
                    if (volumes[i].TryGetDraw(camera, out PureVolumetricFog.DrawRequest request))
                        _draws.Add(request);
                }
                return _draws.Count > 0;
            }

            private RenderTextureDescriptor GetFogDescriptor(RenderTextureDescriptor cameraDescriptor, out Vector4 texelSize)
            {
                RenderTextureDescriptor descriptor = cameraDescriptor;
                descriptor.width = Mathf.Max(descriptor.width / _divisor, 1);
                descriptor.height = Mathf.Max(descriptor.height / _divisor, 1);
                descriptor.depthBufferBits = 0;
                descriptor.msaaSamples = 1;
                descriptor.graphicsFormat = GraphicsFormat.R16G16B16A16_SFloat;
                texelSize = new Vector4(
                    1f / descriptor.width, 1f / descriptor.height,
                    descriptor.width, descriptor.height);
                return descriptor;
            }

            #region RenderGraph path

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
                UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
                if (resourceData.isActiveTargetBackBuffer)
                    return;
                if (!CollectDraws(cameraData.camera))
                    return;

                RenderTextureDescriptor descriptor = GetFogDescriptor(cameraData.cameraTargetDescriptor, out Vector4 texelSize);
                TextureHandle fogTexture = UniversalRenderer.CreateRenderGraphTexture(
                    renderGraph, descriptor, "_PureVolumetricFogTexture", true, FilterMode.Point);

                using (IRasterRenderGraphBuilder builder =
                    renderGraph.AddRasterRenderPass("Pure Volumetric Fog", out FogPassData passData, s_fogSampler))
                {
                    if (passData.draws == null)
                        passData.draws = new List<PureVolumetricFog.DrawRequest>();
                    passData.draws.Clear();
                    passData.draws.AddRange(_draws);
                    passData.pixelScale = _divisor;

                    builder.UseTexture(resourceData.cameraDepthTexture, AccessFlags.Read);
                    // Keep the main-light atlas alive until the fog has sampled it.
                    if (resourceData.mainShadowsTexture.IsValid())
                        builder.UseTexture(resourceData.mainShadowsTexture, AccessFlags.Read);
                    builder.SetRenderAttachment(fogTexture, 0, AccessFlags.Write);
                    builder.AllowGlobalStateModification(true);
                    builder.AllowPassCulling(false);
                    builder.SetRenderFunc((FogPassData data, RasterGraphContext context) =>
                    {
                        context.cmd.SetGlobalFloat(GroundFogPixelScaleId, data.pixelScale);
                        for (int i = 0; i < data.draws.Count; i++)
                        {
                            PureVolumetricFog.DrawRequest draw = data.draws[i];
                            context.cmd.DrawMesh(draw.mesh, draw.matrix, draw.material, 0, 0);
                        }
                        context.cmd.SetGlobalFloat(GroundFogPixelScaleId, 1f);
                    });
                }

                using (IRasterRenderGraphBuilder builder =
                    renderGraph.AddRasterRenderPass("Pure Volumetric Fog Composite", out CompositePassData passData, s_compositeSampler))
                {
                    passData.fogTexture = fogTexture;
                    passData.material = _compositeMaterial;
                    passData.texelSize = texelSize;
                    passData.depthTolerance = _depthTolerance;

                    builder.UseTexture(fogTexture, AccessFlags.Read);
                    builder.UseTexture(resourceData.cameraDepthTexture, AccessFlags.Read);
                    builder.SetRenderAttachment(resourceData.activeColorTexture, 0, AccessFlags.ReadWrite);
                    builder.AllowPassCulling(false);
                    builder.SetRenderFunc((CompositePassData data, RasterGraphContext context) =>
                    {
                        data.material.SetVector(GroundFogTexelSizeId, data.texelSize);
                        data.material.SetFloat(GroundFogDepthToleranceId, data.depthTolerance);
                        RTHandle fog = data.fogTexture;
                        Blitter.BlitTexture(context.cmd, fog, new Vector4(1f, 1f, 0f, 0f), data.material, 0);
                    });
                }
            }

            #endregion

            #region Compatibility Mode path

#if !UNITY_6000_4_OR_NEWER
            // Unity 6.4 removed the Compatibility Mode API.
#pragma warning disable CS0672, CS0618
            public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
            {
                RenderTextureDescriptor descriptor = GetFogDescriptor(
                    renderingData.cameraData.cameraTargetDescriptor, out _);
                RenderingUtils.ReAllocateHandleIfNeeded(
                    ref _compatibilityFogTexture, descriptor, FilterMode.Point, TextureWrapMode.Clamp,
                    name: "_PureVolumetricFogTexture");
            }

            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
            {
                Camera camera = renderingData.cameraData.camera;
                if (_compatibilityFogTexture == null || !CollectDraws(camera))
                    return;

                GetFogDescriptor(renderingData.cameraData.cameraTargetDescriptor, out Vector4 texelSize);
                RTHandle cameraColor = renderingData.cameraData.renderer.cameraColorTargetHandle;

                CommandBuffer cmd = CommandBufferPool.Get();
                using (new ProfilingScope(cmd, s_fogSampler))
                {
                    cmd.SetRenderTarget(_compatibilityFogTexture);
                    cmd.ClearRenderTarget(false, true, Color.clear);
                    cmd.SetGlobalFloat(GroundFogPixelScaleId, _divisor);
                    for (int i = 0; i < _draws.Count; i++)
                    {
                        PureVolumetricFog.DrawRequest draw = _draws[i];
                        cmd.DrawMesh(draw.mesh, draw.matrix, draw.material, 0, 0);
                    }
                    cmd.SetGlobalFloat(GroundFogPixelScaleId, 1f);
                }

                using (new ProfilingScope(cmd, s_compositeSampler))
                {
                    _compositeMaterial.SetVector(GroundFogTexelSizeId, texelSize);
                    _compositeMaterial.SetFloat(GroundFogDepthToleranceId, _depthTolerance);
                    cmd.SetRenderTarget(cameraColor);
                    Blitter.BlitTexture(cmd, _compatibilityFogTexture, new Vector4(1f, 1f, 0f, 0f), _compositeMaterial, 0);
                }

                context.ExecuteCommandBuffer(cmd);
                CommandBufferPool.Release(cmd);
            }
#pragma warning restore CS0672, CS0618
#endif

            #endregion
        }
    }
}
