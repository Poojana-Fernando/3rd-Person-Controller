using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace BKPureNature
{
    /// <summary>
    /// Raymarched ground fog that follows one or more Unity Terrains. The component composites
    /// the terrains' heightmaps into a single height texture, fits a box volume around their
    /// union bounds and draws it every frame with the URP fog shader. Volumetric shadowing uses
    /// URP's main-light shadow map, or a precomputed terrain horizon map when that is unavailable.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [AddComponentMenu("BK/Pure Volumetric Fog")]
    public class PureVolumetricFog : MonoBehaviour
    {
        public enum DebugView
        {
            Fog = 0,
            TerrainHeight = 1,
            SolidVolume = 2,
            NoisePattern = 3,
            ShadowVisibility = 4
        }

        #region Constants

        private const int CurrentSettingsVersion = 1;
        private const string FogShaderResource = "PureVolumetricFog/PureVolumetricFog";
        private const string HeightmapComputeResource = "PureVolumetricFog/PureVolumetricFogHeightmap";
        private const string HorizonShadowComputeResource = "PureVolumetricFog/PureVolumetricFogHorizonShadow";
        private const string NoiseComputeResource = "PureVolumetricFog/PureVolumetricFogNoise";
        private const string HeightmapClearKernelName = "CSClear";
        private const string HeightmapCompositeKernelName = "CSCompositeTile";
        private const string TerrainShadowKernelName = "CSBuildTerrainShadowMap";
        private const string NoiseKernelName = "CSGenerateNoise";
        private const int ComputeThreadGroupSize = 8;
        private const int CompositeMinimumResolution = 64;
        private const int CompositeMaximumResolution = 4096;
        private const int TerrainShadowMapResolution = 512;
        // Rows of the horizon shadow map baked per frame in play mode. 64 rows spread a
        // full rebuild over eight frames.
        private const int TerrainShadowRowsPerFrame = 64;
        private const int TerrainShadowMapSteps = 96;
        private const float TerrainShadowSoftness = 1.5f;
        private const float TerrainShadowAngularRadius = 0.2f;
        private const float ShadowAntiBanding = 0.06f;
        private const float ShadowMapRebuildAngle = 0.75f;
        // Terrains are rediscovered at this interval while automatic discovery is active
        // and no explicit terrains are assigned.
        private const int TerrainDiscoveryInterval = 60;
        // Unity stores Terrain heights in [0, 32766 / 65535] on the GPU.
        // Match Terrain's own _TerrainHeightmapScale conversion when sampling it.
        private const float TerrainHeightmapDecodeScale = 65535f / 32766f;

        #endregion

        #region Serialized settings

        [SerializeField, HideInInspector] private int settingsVersion = CurrentSettingsVersion;

        [Tooltip("Terrains used as the fog height source. Leave empty to use every active Terrain in the scene.")]
        [SerializeField] private List<Terrain> terrains = new List<Terrain>();
        [Tooltip("When no terrains are assigned, all active Terrains are used and rediscovered periodically.")]
        [SerializeField] private bool findActiveTerrainsAutomatically = true;
        [SerializeField, Min(0f)] private float horizontalPadding = 40f;
        [SerializeField, Min(0f)] private float bottomPadding = 20f;
        [SerializeField, Min(1f)] private float topPadding = 160f;
        [SerializeField, Min(0f)] private float terrainEdgeFade = 0f;

        [Tooltip("How strongly the fog layer follows the Terrain heightmap. 0 keeps it horizontal at the lowest Terrain origin; 1 follows the relief completely. Meshes are intentionally ignored.")]
        [SerializeField, Range(0f, 1f)] private float terrainFollow = 0.9f;
        [Tooltip("Rebuilds the height and shadow textures when a Terrain heightmap is edited.")]
        [SerializeField] private bool rebuildWhenTerrainChanges = true;

        [Tooltip("Base extinction per world metre before noise. Higher values make the fog opaque; noise only removes density.")]
        [SerializeField, Range(0f, 0.3f)] private float density = 0.015f;
        [SerializeField, Min(1f)] private float groundFogHeight = 60f;
        [Tooltip("Controls vertical ray spacing and the conservative height margin. Lower values favour finer sampling; this does not change the density profile.")]
        [SerializeField, Min(0.1f)] private float topSoftness = 0.1f;

        [SerializeField, Range(32, 256)] private int noiseTextureResolution = 64;
        [SerializeField] private int noiseSeed = 1337;
        [SerializeField, Min(1f)] private float largeNoiseScale = 800f;
        [SerializeField, Min(1f)] private float detailNoiseScale = 120f;
        [Tooltip("How much noise carves into the fog. Zero keeps the full base density; one applies the complete carving mask.")]
        [SerializeField, Range(0f, 1f)] private float noiseAmount = 0f;
        [Tooltip("Minimum density fraction retained by the noise mask. Zero allows clear holes; one prevents density carving.")]
        [SerializeField, Range(0f, 1f)] private float noiseFloor = 0f;
        [SerializeField, Range(0.25f, 8f)] private float noiseContrast = 1.5f;
        [Tooltip("Strength of the carving mask. Higher values remove more fog without exceeding the base density.")]
        [SerializeField, Range(0f, 3f)] private float noiseStrength = 3f;
        [SerializeField, Range(0f, 0.85f)] private float noiseCoverage = 0.5f;
        [Tooltip("Maximum downward erosion of the fog layer in world units. Follows the density carving mask.")]
        [SerializeField, Min(0f)] private float noiseHeightDistortion = 0f;
        [SerializeField, Range(0f, 0.05f)] private float verticalNoiseShear = 0.004f;
        [SerializeField] private Vector2 windDirection = new Vector2(0.28734788f, 0.95782626f);
        [SerializeField, Min(0f)] private float windSpeed = 20f;

        [SerializeField] private DebugView debugView = DebugView.Fog;
        [SerializeField] private bool useSceneDepth = true;
        [SerializeField, Range(0f, 1f)] private float depthBias = 0f;
        [SerializeField, Range(8, 128)] private int maximumRaySteps = 28;
        [SerializeField, Range(4, 64)] private int minimumRaySteps = 7;
        [SerializeField, Min(2f)] private float targetStepLength = 105f;
        [SerializeField, Min(10f)] private float maximumFogDistance = 4500f;
        [SerializeField, Min(0f)] private float nearFadeDistance = 500f;
        [SerializeField, Range(0f, 1f)] private float farFadeStart = 0f;
        [SerializeField] private Color fogColor = new Color(0.63f, 0.70f, 0.76f, 1f);
        [Tooltip("Uses RenderSettings.fogColor every frame. Disable to use the local Fog Color field instead.")]
        [SerializeField] private bool followSceneFogColor = true;
        [SerializeField] private Color ambientColor = new Color(0.50f, 0.55f, 0.60f, 1f);
        [Tooltip("Uses RenderSettings.ambientLight every frame. Disable to use the local Ambient Color field instead.")]
        [SerializeField] private bool followSceneAmbientColor = true;
        [SerializeField, Min(0f)] private float ambientStrength = 1f;
        [Tooltip("How much terrain occlusion also darkens ambient sky light inside the fog. Direct sunlight remains controlled by Shadow Strength.")]
        [SerializeField, Range(0f, 1f)] private float ambientShadowStrength = 0.7f;
        [SerializeField, Min(0f)] private float sunStrength = 1f;
        [Tooltip("Adds sun-coloured in-scattering only where the directional light reaches the fog. It uses the same terrain and realtime-shadow mask as Sun Strength.")]
        [SerializeField, Range(0f, 2f)] private float sunScattering = 0.5f;
        [SerializeField, Range(-0.8f, 0.8f)] private float anisotropy = 0.01f;
        [SerializeField] private Light directionalLight;

        [Tooltip("0 uses centered ray samples. Values near 1 distribute samples across the complete ray step and prevent coherent camera-facing slice bands.")]
        [SerializeField, Range(0f, 1f)] private float rayJitterStrength = 1f;
        [Tooltip("Higher values use blurrier noise mip levels at long ray steps, reducing distant grain and shimmer.")]
        [SerializeField, Range(-1f, 4f)] private float noiseMipBias = 0.5f;
        [Tooltip("World-space footprint used to filter the procedural noise during raymarching.")]
        [SerializeField, Range(0.25f, 2f)] private float noiseFilterScale = 0.7f;
        [Tooltip("Averages consecutive ray samples using trapezoidal integration. This removes visible slice lines without adding extra density samples.")]
        [SerializeField, Range(0f, 1f)] private float rayStepSmoothing = 1f;

        [SerializeField] private bool enableVolumetricShadows = true;
        [SerializeField] private bool terrainCastsFogShadows = true;
        [Tooltip("Include meshes, trees and rocks casting realtime shadows from URP's main directional light. Disable for terrain-only shadows. Mesh range follows the URP Shadow Distance.")]
        [SerializeField] private bool useUnityRealtimeShadows = true;
        [SerializeField, Range(0f, 1f)] private float shadowStrength = 0.7f;
        [SerializeField, Min(20f)] private float shadowDistance = 900f;
        [Tooltip("Signed vertical receiver bias. Negative values attach/expand the fog shadow; positive values shrink it away from the terrain. Keep this close to zero.")]
        [SerializeField, Range(-4f, 4f)] private float terrainShadowBias = 0f;
        [SerializeField, Range(0f, 4f)] private float fogSelfShadowStrength = 2.25f;

        #endregion

        #region Shared resources

        private static Shader s_fogShader;
        private static ComputeShader s_heightmapCompute;
        private static ComputeShader s_horizonShadowCompute;
        private static ComputeShader s_noiseCompute;
        private static readonly HashSet<string> s_reportedMissingResources = new HashSet<string>();

        private static class ShaderIds
        {
            public static readonly int CullMode = Shader.PropertyToID("_CullMode");
            public static readonly int GroundFogPixelScale = Shader.PropertyToID("_GroundFogPixelScale");
            public static readonly int MaximumFogHeightWS = Shader.PropertyToID("_MaximumFogHeightWS");
            public static readonly int TerrainHeightmap = Shader.PropertyToID("_TerrainHeightmap");
            public static readonly int TerrainShadowMap = Shader.PropertyToID("_TerrainShadowMap");
            public static readonly int NoiseTex = Shader.PropertyToID("_NoiseTex");
            public static readonly int TerrainOrigin = Shader.PropertyToID("_TerrainOrigin");
            public static readonly int TerrainSize = Shader.PropertyToID("_TerrainSize");
            public static readonly int TerrainEdgeFadeWorld = Shader.PropertyToID("_TerrainEdgeFadeWorld");
            public static readonly int TerrainFollow = Shader.PropertyToID("_TerrainFollow");
            public static readonly int Density = Shader.PropertyToID("_Density");
            public static readonly int GroundFogHeight = Shader.PropertyToID("_GroundFogHeight");
            public static readonly int TopSoftness = Shader.PropertyToID("_TopSoftness");
            public static readonly int LargeNoiseScale = Shader.PropertyToID("_LargeNoiseScale");
            public static readonly int DetailNoiseScale = Shader.PropertyToID("_DetailNoiseScale");
            public static readonly int NoiseAmount = Shader.PropertyToID("_NoiseAmount");
            public static readonly int NoiseFloor = Shader.PropertyToID("_NoiseFloor");
            public static readonly int NoiseContrast = Shader.PropertyToID("_NoiseContrast");
            public static readonly int NoiseStrength = Shader.PropertyToID("_NoiseStrength");
            public static readonly int NoiseCoverage = Shader.PropertyToID("_NoiseCoverage");
            public static readonly int NoiseHeightDistortion = Shader.PropertyToID("_NoiseHeightDistortion");
            public static readonly int VerticalNoiseShear = Shader.PropertyToID("_VerticalNoiseShear");
            public static readonly int Wind = Shader.PropertyToID("_Wind");
            public static readonly int FogTime = Shader.PropertyToID("_FogTime");
            public static readonly int NoiseTextureSize = Shader.PropertyToID("_NoiseTextureSize");
            public static readonly int NoiseMipBias = Shader.PropertyToID("_NoiseMipBias");
            public static readonly int NoiseFilterScale = Shader.PropertyToID("_NoiseFilterScale");
            public static readonly int RayJitterStrength = Shader.PropertyToID("_RayJitterStrength");
            public static readonly int RayStepSmoothing = Shader.PropertyToID("_RayStepSmoothing");
            public static readonly int HasTerrainShadowMap = Shader.PropertyToID("_HasTerrainShadowMap");
            public static readonly int UseUnityRealtimeShadows = Shader.PropertyToID("_UseUnityRealtimeShadows");
            public static readonly int TerrainShadowSoftness = Shader.PropertyToID("_TerrainShadowSoftness");
            public static readonly int ShadowAntiBanding = Shader.PropertyToID("_ShadowAntiBanding");
            public static readonly int DebugView = Shader.PropertyToID("_DebugView");
            public static readonly int UseSceneDepth = Shader.PropertyToID("_UseSceneDepth");
            public static readonly int DepthBias = Shader.PropertyToID("_DepthBias");
            public static readonly int MaximumRaySteps = Shader.PropertyToID("_MaximumRaySteps");
            public static readonly int MinimumRaySteps = Shader.PropertyToID("_MinimumRaySteps");
            public static readonly int TargetStepLength = Shader.PropertyToID("_TargetStepLength");
            public static readonly int MaximumFogDistance = Shader.PropertyToID("_MaximumFogDistance");
            public static readonly int NearFadeDistance = Shader.PropertyToID("_NearFadeDistance");
            public static readonly int FarFadeStart = Shader.PropertyToID("_FarFadeStart");
            public static readonly int AmbientStrength = Shader.PropertyToID("_AmbientStrength");
            public static readonly int AmbientShadowStrength = Shader.PropertyToID("_AmbientShadowStrength");
            public static readonly int SunStrength = Shader.PropertyToID("_SunStrength");
            public static readonly int SunScattering = Shader.PropertyToID("_SunScattering");
            public static readonly int Anisotropy = Shader.PropertyToID("_Anisotropy");
            public static readonly int EnableVolumetricShadows = Shader.PropertyToID("_EnableVolumetricShadows");
            public static readonly int TerrainCastsFogShadows = Shader.PropertyToID("_TerrainCastsFogShadows");
            public static readonly int ShadowStrength = Shader.PropertyToID("_ShadowStrength");
            public static readonly int TerrainShadowBias = Shader.PropertyToID("_TerrainShadowBias");
            public static readonly int FogSelfShadowStrength = Shader.PropertyToID("_FogSelfShadowStrength");
            public static readonly int FogColor = Shader.PropertyToID("_FogColor");
            public static readonly int AmbientColor = Shader.PropertyToID("_AmbientColor");
            public static readonly int LightDirectionWS = Shader.PropertyToID("_LightDirectionWS");
            public static readonly int SunColor = Shader.PropertyToID("_SunColor");

            // Heightmap composite compute
            public static readonly int CompositeWrite = Shader.PropertyToID("_CompositeWrite");
            public static readonly int CompositeResolution = Shader.PropertyToID("_CompositeResolution");
            public static readonly int TileHeightmap = Shader.PropertyToID("_TileHeightmap");
            public static readonly int TileHeightmapResolution = Shader.PropertyToID("_TileHeightmapResolution");
            public static readonly int TilePixelMin = Shader.PropertyToID("_TilePixelMin");
            public static readonly int TilePixelMax = Shader.PropertyToID("_TilePixelMax");
            public static readonly int TileUVScale = Shader.PropertyToID("_TileUVScale");
            public static readonly int TileUVOffset = Shader.PropertyToID("_TileUVOffset");
            public static readonly int TileHeightScale = Shader.PropertyToID("_TileHeightScale");
            public static readonly int TileHeightOffset = Shader.PropertyToID("_TileHeightOffset");

            // Horizon shadow compute
            public static readonly int HeightmapTex = Shader.PropertyToID("_HeightmapTex");
            public static readonly int TerrainShadowMapWrite = Shader.PropertyToID("_TerrainShadowMapWrite");
            public static readonly int ShadowOutputResolution = Shader.PropertyToID("_ShadowOutputResolution");
            public static readonly int RowOffset = Shader.PropertyToID("_RowOffset");
            public static readonly int TerrainShadowMapSteps = Shader.PropertyToID("_TerrainShadowMapSteps");
            public static readonly int ShadowDistanceWorld = Shader.PropertyToID("_ShadowDistanceWorld");
            public static readonly int SunAngularRadiusRadians = Shader.PropertyToID("_SunAngularRadiusRadians");

            // Noise compute
            public static readonly int NoiseWrite = Shader.PropertyToID("_NoiseWrite");
            public static readonly int NoiseResolution = Shader.PropertyToID("_NoiseResolution");
            public static readonly int NoiseSeed = Shader.PropertyToID("_NoiseSeed");
        }

        #endregion

        #region Runtime state

        // Cull [_CullMode] is a render-state binding and only reads material values, so
        // the near-face and far-face variants are separate materials chosen per camera.
        private Material _nearFacesMaterial;
        private Material _farFacesMaterial;
        private readonly Material[] _materials = new Material[2];
        private Mesh _volumeMesh;
        private Matrix4x4 _volumeMatrix = Matrix4x4.identity;
        private Matrix4x4 _volumeInverseMatrix = Matrix4x4.identity;
        private Bounds _volumeBounds;
        private Vector3 _volumeSize = Vector3.one;

        private readonly List<Terrain> _resolvedTerrains = new List<Terrain>();
        private int _framesSinceTerrainDiscovery = int.MaxValue;
        private Bounds _terrainBounds;
        private bool _hasTerrainBounds;
        private float _terrainMaximumHeightWS;
        private bool _terrainBoundsSynchronized = true;

        private RenderTexture _terrainHeightmap;
        private int _lastHeightmapHash = int.MinValue;
        private bool _heightmapUnavailable;

        private RenderTexture _noiseTexture;
        private int _lastNoiseHash = int.MinValue;

        private RenderTexture _terrainShadowMap;
        private RenderTexture _terrainShadowMapBuild;
        private int _shadowBuildRow = -1;
        private int _shadowBuildHash;
        private Vector3 _shadowBuildLightDirection;
        private int _lastTerrainShadowHash = int.MinValue;
        private Vector3 _lastTerrainShadowLightDirection;
        private bool _hasTerrainShadowLightDirection;
        private bool _fastTerrainShadowUnavailable;

#if UNITY_6000_4_OR_NEWER
        private readonly HashSet<EntityId> _depthConfiguredCameras = new HashSet<EntityId>();
#else
        private readonly HashSet<int> _depthConfiguredCameras = new HashSet<int>();
#endif
        private bool _pipelineWarningLogged;

        #endregion

        #region Public API

        private static readonly List<PureVolumetricFog> s_activeVolumes = new List<PureVolumetricFog>();

        /// <summary>Every enabled fog volume, in enable order.</summary>
        public static IReadOnlyList<PureVolumetricFog> ActiveVolumes => s_activeVolumes;

        /// <summary>Everything needed to draw this volume's proxy box for one camera.</summary>
        internal struct DrawRequest
        {
            public Mesh mesh;
            public Matrix4x4 matrix;
            public Material material;
        }

        /// <summary>The Terrains currently used as the fog height source.</summary>
        public IReadOnlyList<Terrain> Terrains => _resolvedTerrains;

        /// <summary>World-space bounds enclosing every source Terrain.</summary>
        public Bounds TerrainBounds => _terrainBounds;

        /// <summary>Uses a single Terrain as the height source and refreshes the fog resources.</summary>
        public void SetTerrain(Terrain terrain)
        {
            terrains.Clear();
            if (terrain != null)
                terrains.Add(terrain);
            Refresh();
        }

        /// <summary>Uses the given Terrains as the height source and refreshes the fog resources.</summary>
        public void SetTerrains(IEnumerable<Terrain> sourceTerrains)
        {
            terrains.Clear();
            if (sourceTerrains != null)
            {
                foreach (Terrain terrain in sourceTerrains)
                    if (terrain != null)
                        terrains.Add(terrain);
            }
            Refresh();
        }

        /// <summary>Assigns every active Terrain in the loaded scenes as the height source.</summary>
        public void FindActiveTerrains()
        {
            terrains.Clear();
            Terrain.GetActiveTerrains(terrains);
            Refresh();
        }

        /// <summary>Rebuilds the composite heightmap, shadow map and volume bounds.</summary>
        public void Refresh()
        {
            _framesSinceTerrainDiscovery = int.MaxValue;
            _lastHeightmapHash = int.MinValue;
            _lastTerrainShadowHash = int.MinValue;
            _hasTerrainShadowLightDirection = false;
            _heightmapUnavailable = false;
            _fastTerrainShadowUnavailable = false;
            PrepareFrame(true);
        }

        #endregion

        #region Unity lifecycle

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetSessionState()
        {
            s_fogShader = null;
            s_heightmapCompute = null;
            s_horizonShadowCompute = null;
            s_noiseCompute = null;
            s_reportedMissingResources.Clear();
            Shader.SetGlobalFloat(ShaderIds.GroundFogPixelScale, 1f);

            // Live volumes can survive when scene reload is also disabled.
            s_activeVolumes.RemoveAll(volume => volume == null || !volume.isActiveAndEnabled);
            foreach (PureVolumetricFog volume in s_activeVolumes)
            {
                volume.SubscribeCallbacks();
                volume._depthConfiguredCameras.Clear();
                volume._pipelineWarningLogged = false;
                volume._framesSinceTerrainDiscovery = int.MaxValue;
            }
        }

        private void SubscribeCallbacks()
        {
            RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
            TerrainCallbacks.heightmapChanged -= OnTerrainHeightmapChanged;
            RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
            TerrainCallbacks.heightmapChanged += OnTerrainHeightmapChanged;
        }

        private void Reset()
        {
            terrains.Clear();
            Terrain.GetActiveTerrains(terrains);
        }

        private void OnEnable()
        {
            UpgradeSerializedSettings();
            _fastTerrainShadowUnavailable = false;
            _heightmapUnavailable = false;
            _framesSinceTerrainDiscovery = int.MaxValue;

            if (!IsUniversalPipelineActive() && !_pipelineWarningLogged)
            {
                Debug.LogWarning(
                    "Pure Volumetric Fog requires the Universal Render Pipeline. " +
                    "No fog is rendered while another pipeline is active.",
                    this);
                _pipelineWarningLogged = true;
            }

            SubscribeCallbacks();
            if (!s_activeVolumes.Contains(this))
                s_activeVolumes.Add(this);
            Shader.SetGlobalFloat(ShaderIds.GroundFogPixelScale, 1f);
            PrepareFrame(true);
        }

        private void OnDisable()
        {
            s_activeVolumes.Remove(this);
            RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
            TerrainCallbacks.heightmapChanged -= OnTerrainHeightmapChanged;
            _depthConfiguredCameras.Clear();
            ReleaseGeneratedResources();
        }

        private void OnDestroy()
        {
            ReleaseGeneratedResources();
        }

        private void OnValidate()
        {
            UpgradeSerializedSettings();
            noiseTextureResolution = Mathf.Clamp(Mathf.ClosestPowerOfTwo(noiseTextureResolution), 32, 256);
            density = Mathf.Clamp(density, 0f, 0.3f);
            terrainFollow = Mathf.Clamp01(terrainFollow);
            maximumRaySteps = Mathf.Clamp(maximumRaySteps, 8, 128);
            minimumRaySteps = Mathf.Clamp(minimumRaySteps, 4, maximumRaySteps);
            targetStepLength = Mathf.Max(2f, targetStepLength);
            maximumFogDistance = Mathf.Max(10f, maximumFogDistance);
            largeNoiseScale = Mathf.Max(1f, largeNoiseScale);
            detailNoiseScale = Mathf.Max(1f, detailNoiseScale);
            shadowDistance = Mathf.Max(20f, shadowDistance);
            terrainShadowBias = Mathf.Clamp(terrainShadowBias, -4f, 4f);
            fogSelfShadowStrength = Mathf.Max(0f, fogSelfShadowStrength);
            ambientShadowStrength = Mathf.Clamp01(ambientShadowStrength);
            sunScattering = Mathf.Clamp(sunScattering, 0f, 2f);
            rayJitterStrength = Mathf.Clamp01(rayJitterStrength);
            noiseMipBias = Mathf.Clamp(noiseMipBias, -1f, 4f);
            noiseFilterScale = Mathf.Clamp(noiseFilterScale, 0.25f, 2f);
            rayStepSmoothing = Mathf.Clamp01(rayStepSmoothing);
            windDirection = windDirection.sqrMagnitude > 0.0001f ? windDirection.normalized : Vector2.right;
            _fastTerrainShadowUnavailable = false;
            _framesSinceTerrainDiscovery = int.MaxValue;

#if UNITY_EDITOR
            if (!Application.isPlaying)
                EditorApplication.QueuePlayerLoopUpdate();
#endif
        }

        private void Update()
        {
            PrepareFrame(false);
        }

        /// <summary>
        /// Updates the version marker and invalidates cached shadows for older settings.
        /// </summary>
        private void UpgradeSerializedSettings()
        {
            if (settingsVersion >= CurrentSettingsVersion)
                return;

            settingsVersion = CurrentSettingsVersion;
            _lastTerrainShadowHash = int.MinValue;
            _hasTerrainShadowLightDirection = false;
        }

        private void OnTerrainHeightmapChanged(Terrain terrain, RectInt region, bool synched)
        {
            if (terrain == null || !_resolvedTerrains.Contains(terrain))
                return;

            _terrainBoundsSynchronized = synched;
            // A late-frame GPU height edit may precede both updated CPU bounds and
            // UpdateMaterial. Disable rejection immediately until it is refreshed.
            for (int i = 0; i < _materials.Length; i++)
                if (_materials[i] != null)
                    _materials[i].SetFloat(ShaderIds.MaximumFogHeightWS, float.MaxValue);
        }

        #endregion

        #region Frame preparation

        /// <summary>
        /// Resolves terrains, fits the volume and brings every GPU resource up to date.
        /// Runs once per Update and on demand before the first render of a frame.
        /// </summary>
        private void PrepareFrame(bool force)
        {
            ResolveTerrains();
            UpdateTerrainBounds();
            EnsureRuntimeMaterial();
            EnsureTerrainHeightmap(force);
            EnsureNoiseTexture(force);
            EnsureTerrainShadowMap(force);
            UpdateMaterial();
        }

        private void ResolveTerrains()
        {
            bool explicitTerrains = false;
            for (int i = 0; i < terrains.Count; i++)
            {
                if (terrains[i] != null)
                {
                    explicitTerrains = true;
                    break;
                }
            }

            if (explicitTerrains)
            {
                _resolvedTerrains.Clear();
                for (int i = 0; i < terrains.Count; i++)
                    if (terrains[i] != null)
                        _resolvedTerrains.Add(terrains[i]);
                return;
            }

            if (!findActiveTerrainsAutomatically)
            {
                _resolvedTerrains.Clear();
                return;
            }

            bool stale = _resolvedTerrains.Count == 0 ||
                _framesSinceTerrainDiscovery >= TerrainDiscoveryInterval;
            for (int i = 0; !stale && i < _resolvedTerrains.Count; i++)
                stale = _resolvedTerrains[i] == null || !_resolvedTerrains[i].isActiveAndEnabled;

            if (stale)
            {
                _resolvedTerrains.Clear();
                Terrain.GetActiveTerrains(_resolvedTerrains);
                // Terrains register as active in their own OnEnable, which
                // may not have run yet. Fall back to a scene search in that case.
                if (_resolvedTerrains.Count == 0)
                {
#if UNITY_6000_4_OR_NEWER
                    Terrain[] sceneTerrains = FindObjectsByType<Terrain>(FindObjectsInactive.Exclude);
#else
                    Terrain[] sceneTerrains = FindObjectsByType<Terrain>(
                        FindObjectsInactive.Exclude, FindObjectsSortMode.None);
#endif
                    for (int i = 0; i < sceneTerrains.Length; i++)
                        if (sceneTerrains[i].isActiveAndEnabled)
                            _resolvedTerrains.Add(sceneTerrains[i]);
                }
                _framesSinceTerrainDiscovery = 0;
            }
            else
            {
                _framesSinceTerrainDiscovery++;
            }
        }

        private void UpdateTerrainBounds()
        {
            _hasTerrainBounds = false;
            Vector3 min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            Vector3 max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
            float maximumHeight = float.MinValue;

            for (int i = 0; i < _resolvedTerrains.Count; i++)
            {
                Terrain terrain = _resolvedTerrains[i];
                if (terrain == null || terrain.terrainData == null)
                    continue;

                TerrainData data = terrain.terrainData;
                Vector3 origin = terrain.transform.position;
                Vector3 size = data.size;
                min = Vector3.Min(min, origin);
                max = Vector3.Max(max, origin + size);
                // Terrain bounds include the highest sample. Fall back to the full height
                // range while GPU edits are waiting for CPU-bound synchronization.
                float tileMaximum = _terrainBoundsSynchronized ? data.bounds.max.y : size.y;
                maximumHeight = Mathf.Max(maximumHeight, origin.y + tileMaximum);
                _hasTerrainBounds = true;
            }

            if (!_hasTerrainBounds)
                return;

            _terrainBounds.SetMinMax(min, max);
            _terrainMaximumHeightWS = maximumHeight;

            Vector3 terrainSize = _terrainBounds.size;
            _volumeSize = new Vector3(
                terrainSize.x + horizontalPadding * 2f,
                terrainSize.y + bottomPadding + topPadding,
                terrainSize.z + horizontalPadding * 2f);
            Vector3 center = new Vector3(
                min.x + terrainSize.x * 0.5f,
                min.y + (terrainSize.y + topPadding - bottomPadding) * 0.5f,
                min.z + terrainSize.z * 0.5f);

            _volumeMatrix = Matrix4x4.TRS(center, Quaternion.identity, _volumeSize);
            _volumeInverseMatrix = _volumeMatrix.inverse;
            _volumeBounds = new Bounds(center, _volumeSize);
        }

        private static bool IsUniversalPipelineActive()
        {
            return GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset;
        }

        private static bool LoadResources()
        {
            bool loaded = true;
            loaded &= LoadResource(FogShaderResource, ref s_fogShader);
            loaded &= LoadResource(HeightmapComputeResource, ref s_heightmapCompute);
            loaded &= LoadResource(HorizonShadowComputeResource, ref s_horizonShadowCompute);
            loaded &= LoadResource(NoiseComputeResource, ref s_noiseCompute);
            return loaded;
        }

        /// <summary>
        /// Loads one package asset from Resources. Loading is retried on every call until it
        /// succeeds, because assets may still be importing when the component is first enabled.
        /// The failure is logged once per asset.
        /// </summary>
        private static bool LoadResource<T>(string path, ref T asset) where T : UnityEngine.Object
        {
            if (asset != null)
                return true;

            asset = Resources.Load<T>(path);
            if (asset != null)
                return true;

            if (s_reportedMissingResources.Add(path))
            {
                Debug.LogError(
                    "Pure Volumetric Fog: could not load " + typeof(T).Name + " \"" + path +
                    "\" from a Resources folder. Expected at Runtime/Resources/" + path +
                    " inside the package." + DescribeAssetLocation<T>(path));
            }
            return false;
        }

        private static string DescribeAssetLocation<T>(string path) where T : UnityEngine.Object
        {
#if UNITY_EDITOR
            string assetName = System.IO.Path.GetFileName(path);
            string[] guids = AssetDatabase.FindAssets(assetName + " t:" + typeof(T).Name);
            for (int i = 0; i < guids.Length; i++)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (System.IO.Path.GetFileNameWithoutExtension(assetPath) == assetName)
                    return " The asset exists at \"" + assetPath + "\" but is not reachable via Resources.Load.";
            }
            return " No asset with that name exists in the project.";
#else
            return string.Empty;
#endif
        }

        private bool HasMaterials => _nearFacesMaterial != null && _farFacesMaterial != null;

        private void EnsureRuntimeMaterial()
        {
            if (HasMaterials)
                return;
            if (!LoadResources())
                return;
            if (!s_fogShader.isSupported)
            {
                if (s_reportedMissingResources.Add("unsupported"))
                    Debug.LogError(
                        "Pure Volumetric Fog: the fog shader is not supported on this device or " +
                        "failed to compile. Check the console for shader errors.",
                        this);
                return;
            }

            // The unit cube winds inward: from outside, its back faces are the near faces.
            _nearFacesMaterial = CreateMaterial("Pure Volumetric Fog Near Faces", CullMode.Front);
            _farFacesMaterial = CreateMaterial("Pure Volumetric Fog Far Faces", CullMode.Back);
            _materials[0] = _nearFacesMaterial;
            _materials[1] = _farFacesMaterial;
            if (_volumeMesh == null)
                _volumeMesh = CreateUnitCube();
        }

        private static Material CreateMaterial(string materialName, CullMode cullMode)
        {
            Material material = new Material(s_fogShader)
            {
                name = materialName,
                hideFlags = HideFlags.HideAndDontSave
            };
            material.SetFloat(ShaderIds.CullMode, (float)cullMode);
            return material;
        }

        #endregion

        #region Rendering

        private void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            if (camera == null || camera.cameraType == CameraType.Preview)
                return;
            if (!IsUniversalPipelineActive())
                return;
            if ((camera.cullingMask & (1 << gameObject.layer)) == 0)
                return;

            if (!HasMaterials || _terrainHeightmap == null)
                PrepareFrame(true);
            if (!HasMaterials || !_hasTerrainBounds || _terrainHeightmap == null)
                return;

            ConfigureCameraDepth(camera);
            UpdateDynamicLighting();

            // The renderer feature draws all volumes through its own pass when present.
            if (PureVolumetricFogRendererFeature.HandlesCamera(camera))
                return;

            if (!TryGetDraw(camera, out DrawRequest draw))
                return;

            RenderParams renderParams = new RenderParams(draw.material)
            {
                camera = camera,
                layer = gameObject.layer,
                worldBounds = _volumeBounds,
                shadowCastingMode = ShadowCastingMode.Off,
                receiveShadows = false,
                lightProbeUsage = LightProbeUsage.Off,
                reflectionProbeUsage = ReflectionProbeUsage.Off,
                motionVectorMode = MotionVectorGenerationMode.ForceNoMotion
            };
            Graphics.RenderMesh(renderParams, draw.mesh, 0, draw.matrix);
        }

        /// <summary>
        /// Provides the proxy mesh, transform and cull-mode material for drawing this volume
        /// from the given camera. Returns false when the volume has nothing to draw.
        /// </summary>
        internal bool TryGetDraw(Camera camera, out DrawRequest request)
        {
            request = default;
            if (!HasMaterials || !_hasTerrainBounds || _terrainHeightmap == null || _volumeMesh == null)
                return false;
            if ((camera.cullingMask & (1 << gameObject.layer)) == 0)
                return false;

            // Near faces can be clipped by the near plane once the camera is inside or
            // touching the volume; far faces cover the same pixels without that gap.
            request.material = IsCameraInsideOrNearVolume(camera)
                ? _farFacesMaterial
                : _nearFacesMaterial;
            request.mesh = _volumeMesh;
            request.matrix = _volumeMatrix;
            return true;
        }

        private void ConfigureCameraDepth(Camera camera)
        {
#if UNITY_6000_4_OR_NEWER
            if (!useSceneDepth || !_depthConfiguredCameras.Add(camera.GetEntityId()))
#else
            if (!useSceneDepth || !_depthConfiguredCameras.Add(camera.GetInstanceID()))
#endif
                return;

            UniversalAdditionalCameraData cameraData = camera.GetUniversalAdditionalCameraData();
            if (cameraData != null)
                cameraData.requiresDepthTexture = true;
        }

        private bool IsCameraInsideOrNearVolume(Camera camera)
        {
            Vector3 localPosition = _volumeInverseMatrix.MultiplyPoint3x4(camera.transform.position);
            float padding = Mathf.Max(camera.nearClipPlane * 1.5f, 0.05f);
            Vector3 localPadding = new Vector3(
                padding / Mathf.Max(Mathf.Abs(_volumeSize.x), 0.0001f),
                padding / Mathf.Max(Mathf.Abs(_volumeSize.y), 0.0001f),
                padding / Mathf.Max(Mathf.Abs(_volumeSize.z), 0.0001f));

            return Mathf.Abs(localPosition.x) <= 0.5f + localPadding.x &&
                   Mathf.Abs(localPosition.y) <= 0.5f + localPadding.y &&
                   Mathf.Abs(localPosition.z) <= 0.5f + localPadding.z;
        }

        private void UpdateMaterial()
        {
            if (!HasMaterials || !_hasTerrainBounds)
                return;

            Vector3 terrainOrigin = _terrainBounds.min;
            Vector3 terrainSize = _terrainBounds.size;
            float layerMaximum = Mathf.Max(groundFogHeight, 0.2f) + Mathf.Max(topSoftness, 0f);
            for (int m = 0; m < _materials.Length; m++)
            {
                Material material = _materials[m];
                material.SetFloat(ShaderIds.MaximumFogHeightWS, _terrainMaximumHeightWS +
                    layerMaximum + Mathf.Abs(terrainSize.y) / 1024f + 0.01f);

                material.SetTexture(ShaderIds.TerrainHeightmap,
                    _terrainHeightmap != null ? (Texture)_terrainHeightmap : Texture2D.blackTexture);
                material.SetTexture(ShaderIds.TerrainShadowMap,
                    HasFastTerrainShadowMap ? (Texture)_terrainShadowMap : Texture2D.blackTexture);
                material.SetTexture(ShaderIds.NoiseTex,
                    _noiseTexture != null ? (Texture)_noiseTexture : Texture2D.grayTexture);
                material.SetVector(ShaderIds.TerrainOrigin, new Vector4(terrainOrigin.x, terrainOrigin.y, terrainOrigin.z, 1f));
                material.SetVector(ShaderIds.TerrainSize, new Vector4(terrainSize.x, terrainSize.y, terrainSize.z, 1f));
                material.SetFloat(ShaderIds.TerrainEdgeFadeWorld, terrainEdgeFade);
                material.SetFloat(ShaderIds.TerrainFollow, terrainFollow);

                material.SetFloat(ShaderIds.Density, density);
                material.SetFloat(ShaderIds.GroundFogHeight, groundFogHeight);
                material.SetFloat(ShaderIds.TopSoftness, topSoftness);

                material.SetFloat(ShaderIds.LargeNoiseScale, largeNoiseScale);
                material.SetFloat(ShaderIds.DetailNoiseScale, detailNoiseScale);
                material.SetFloat(ShaderIds.NoiseAmount, noiseAmount);
                material.SetFloat(ShaderIds.NoiseFloor, noiseFloor);
                material.SetFloat(ShaderIds.NoiseContrast, noiseContrast);
                material.SetFloat(ShaderIds.NoiseStrength, noiseStrength);
                material.SetFloat(ShaderIds.NoiseCoverage, noiseCoverage);
                material.SetFloat(ShaderIds.NoiseHeightDistortion, noiseHeightDistortion);
                material.SetFloat(ShaderIds.VerticalNoiseShear, verticalNoiseShear);
                Vector2 wind = windDirection.sqrMagnitude > 0.0001f ? windDirection.normalized : Vector2.right;
                material.SetVector(ShaderIds.Wind, new Vector4(wind.x, wind.y, windSpeed, 0f));
                material.SetFloat(ShaderIds.FogTime, GetClock());
                material.SetFloat(ShaderIds.NoiseTextureSize, _noiseTexture != null ? _noiseTexture.width : 1f);
                material.SetFloat(ShaderIds.NoiseMipBias, noiseMipBias);
                material.SetFloat(ShaderIds.NoiseFilterScale, noiseFilterScale);
                material.SetFloat(ShaderIds.RayJitterStrength, rayJitterStrength);
                material.SetFloat(ShaderIds.RayStepSmoothing, rayStepSmoothing);
                material.SetFloat(ShaderIds.HasTerrainShadowMap, HasFastTerrainShadowMap ? 1f : 0f);
                material.SetFloat(ShaderIds.UseUnityRealtimeShadows, HasRealtimeShadowSupport ? 1f : 0f);
                material.SetFloat(ShaderIds.TerrainShadowSoftness, TerrainShadowSoftness);
                material.SetFloat(ShaderIds.ShadowAntiBanding, ShadowAntiBanding);

                material.SetFloat(ShaderIds.DebugView, (float)debugView);
                material.SetFloat(ShaderIds.UseSceneDepth, useSceneDepth ? 1f : 0f);
                material.SetFloat(ShaderIds.DepthBias, depthBias);
                material.SetFloat(ShaderIds.MaximumRaySteps, maximumRaySteps);
                material.SetFloat(ShaderIds.MinimumRaySteps, minimumRaySteps);
                material.SetFloat(ShaderIds.TargetStepLength, targetStepLength);
                material.SetFloat(ShaderIds.MaximumFogDistance, maximumFogDistance);
                material.SetFloat(ShaderIds.NearFadeDistance, nearFadeDistance);
                material.SetFloat(ShaderIds.FarFadeStart, farFadeStart);
                material.SetFloat(ShaderIds.AmbientStrength, ambientStrength);
                material.SetFloat(ShaderIds.AmbientShadowStrength, ambientShadowStrength);
                material.SetFloat(ShaderIds.SunStrength, sunStrength);
                material.SetFloat(ShaderIds.SunScattering, sunScattering);
                material.SetFloat(ShaderIds.Anisotropy, anisotropy);
                material.SetFloat(ShaderIds.EnableVolumetricShadows, enableVolumetricShadows ? 1f : 0f);
                material.SetFloat(ShaderIds.TerrainCastsFogShadows, terrainCastsFogShadows ? 1f : 0f);
                material.SetFloat(ShaderIds.ShadowStrength, shadowStrength);
                material.SetFloat(ShaderIds.TerrainShadowBias, terrainShadowBias);
                material.SetFloat(ShaderIds.FogSelfShadowStrength, fogSelfShadowStrength);
            }
        }

        private void UpdateDynamicLighting()
        {
            Color fog = followSceneFogColor ? RenderSettings.fogColor : fogColor;
            Color ambient = followSceneAmbientColor ? RenderSettings.ambientLight : ambientColor;
            Light sun = GetDirectionalLight();
            Vector3 towardLight = GetTowardLightDirection(sun);
            Vector4 lightDirection = new Vector4(towardLight.x, towardLight.y, towardLight.z, 0f);
            Color sunColor = sun != null && sun.type == LightType.Directional
                ? sun.color * sun.intensity
                : Color.white;

            for (int m = 0; m < _materials.Length; m++)
            {
                Material material = _materials[m];
                material.SetColor(ShaderIds.FogColor, fog);
                material.SetColor(ShaderIds.AmbientColor, ambient);
                material.SetVector(ShaderIds.LightDirectionWS, lightDirection);
                material.SetColor(ShaderIds.SunColor, sunColor);
            }
        }

        private Light GetDirectionalLight()
        {
            return directionalLight != null ? directionalLight : RenderSettings.sun;
        }

        private static Vector3 GetTowardLightDirection(Light sun)
        {
            if (sun != null && sun.type == LightType.Directional)
            {
                Vector3 towardLight = -sun.transform.forward;

                // Mirror a slightly negative elevation so low-angle terrain shadows
                // remain available while Unity still treats the light as active.
                if (towardLight.y < 0f)
                    towardLight.y = -towardLight.y;
                towardLight.y = Mathf.Max(towardLight.y, 0.01f);
                return towardLight.normalized;
            }
            return new Vector3(0.35f, 0.8f, 0.2f).normalized;
        }

        private static float GetClock()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
                return (float)EditorApplication.timeSinceStartup;
#endif
            return Time.time;
        }

        #endregion

        #region Composite heightmap

        private int CalculateHeightmapHash()
        {
            unchecked
            {
                int hash = 17;
                for (int i = 0; i < _resolvedTerrains.Count; i++)
                {
                    Terrain terrain = _resolvedTerrains[i];
                    if (terrain == null || terrain.terrainData == null)
                        continue;

                    TerrainData data = terrain.terrainData;
                    hash = hash * 31 + terrain.GetHashCode();
                    hash = hash * 31 + data.GetHashCode();
                    hash = hash * 31 + data.heightmapResolution;
                    hash = hash * 31 + terrain.transform.position.GetHashCode();
                    hash = hash * 31 + data.size.GetHashCode();
                    if (rebuildWhenTerrainChanges && data.heightmapTexture != null)
                        hash = hash * 31 + (int)data.heightmapTexture.updateCount;
                }
                return hash;
            }
        }

        private void EnsureTerrainHeightmap(bool force)
        {
            if (!_hasTerrainBounds || _heightmapUnavailable ||
                !SystemInfo.supportsComputeShaders || !LoadResources())
            {
                ReleaseTerrainHeightmap();
                return;
            }

            int hash = CalculateHeightmapHash();
            if (!force && _terrainHeightmap != null && _terrainHeightmap.IsCreated() &&
                hash == _lastHeightmapHash)
                return;

            BuildTerrainHeightmap(hash);
        }

        private void BuildTerrainHeightmap(int hash)
        {
            Vector3 unionMin = _terrainBounds.min;
            Vector3 unionSize = _terrainBounds.size;

            // Use the finest Terrain texel size so no tile loses detail.
            float texelSizeX = float.MaxValue;
            float texelSizeZ = float.MaxValue;
            for (int i = 0; i < _resolvedTerrains.Count; i++)
            {
                Terrain terrain = _resolvedTerrains[i];
                if (terrain == null || terrain.terrainData == null)
                    continue;
                TerrainData data = terrain.terrainData;
                float samples = Mathf.Max(data.heightmapResolution - 1, 1);
                texelSizeX = Mathf.Min(texelSizeX, data.size.x / samples);
                texelSizeZ = Mathf.Min(texelSizeZ, data.size.z / samples);
            }

            int resolutionX = Mathf.Clamp(
                Mathf.CeilToInt(unionSize.x / Mathf.Max(texelSizeX, 0.001f)),
                CompositeMinimumResolution, CompositeMaximumResolution);
            int resolutionZ = Mathf.Clamp(
                Mathf.CeilToInt(unionSize.z / Mathf.Max(texelSizeZ, 0.001f)),
                CompositeMinimumResolution, CompositeMaximumResolution);

            if (_terrainHeightmap == null || !_terrainHeightmap.IsCreated() ||
                _terrainHeightmap.width != resolutionX || _terrainHeightmap.height != resolutionZ)
            {
                ReleaseTerrainHeightmap();

                RenderTextureFormat format = RenderTextureFormat.RFloat;
                if (!SystemInfo.SupportsRenderTextureFormat(format) ||
                    !SystemInfo.SupportsRandomWriteOnRenderTextureFormat(format))
                {
                    Debug.LogWarning(
                        "Pure Volumetric Fog: writable RFloat textures are not supported on this device; " +
                        "no fog is rendered.",
                        this);
                    _heightmapUnavailable = true;
                    return;
                }

                _terrainHeightmap = new RenderTexture(resolutionX, resolutionZ, 0, format)
                {
                    name = "Pure Volumetric Fog Composite Heightmap",
                    enableRandomWrite = true,
                    useMipMap = false,
                    autoGenerateMips = false,
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear,
                    hideFlags = HideFlags.HideAndDontSave
                };
                if (!_terrainHeightmap.Create())
                {
                    ReleaseTerrainHeightmap();
                    _heightmapUnavailable = true;
                    return;
                }
            }

            ComputeShader compute = s_heightmapCompute;
            int clearKernel = compute.FindKernel(HeightmapClearKernelName);
            int compositeKernel = compute.FindKernel(HeightmapCompositeKernelName);
            compute.SetInts(ShaderIds.CompositeResolution, resolutionX, resolutionZ);

            compute.SetTexture(clearKernel, ShaderIds.CompositeWrite, _terrainHeightmap);
            compute.Dispatch(clearKernel,
                ThreadGroups(resolutionX), ThreadGroups(resolutionZ), 1);

            float unionHeight = Mathf.Max(unionSize.y, 0.001f);
            for (int i = 0; i < _resolvedTerrains.Count; i++)
            {
                Terrain terrain = _resolvedTerrains[i];
                if (terrain == null || terrain.terrainData == null)
                    continue;
                TerrainData data = terrain.terrainData;
                Texture heightmap = data.heightmapTexture;
                if (heightmap == null)
                    continue;

                Vector3 origin = terrain.transform.position;
                Vector3 size = data.size;

                // Composite pixel rectangle covered by this tile.
                float u0 = (origin.x - unionMin.x) / Mathf.Max(unionSize.x, 0.001f);
                float v0 = (origin.z - unionMin.z) / Mathf.Max(unionSize.z, 0.001f);
                float u1 = (origin.x + size.x - unionMin.x) / Mathf.Max(unionSize.x, 0.001f);
                float v1 = (origin.z + size.z - unionMin.z) / Mathf.Max(unionSize.z, 0.001f);
                int pixelMinX = Mathf.Clamp(Mathf.FloorToInt(u0 * resolutionX), 0, resolutionX);
                int pixelMinY = Mathf.Clamp(Mathf.FloorToInt(v0 * resolutionZ), 0, resolutionZ);
                int pixelMaxX = Mathf.Clamp(Mathf.CeilToInt(u1 * resolutionX), 0, resolutionX);
                int pixelMaxY = Mathf.Clamp(Mathf.CeilToInt(v1 * resolutionZ), 0, resolutionZ);
                int width = pixelMaxX - pixelMinX;
                int height = pixelMaxY - pixelMinY;
                if (width <= 0 || height <= 0)
                    continue;

                compute.SetTexture(compositeKernel, ShaderIds.CompositeWrite, _terrainHeightmap);
                compute.SetTexture(compositeKernel, ShaderIds.TileHeightmap, heightmap);
                compute.SetInt(ShaderIds.TileHeightmapResolution, data.heightmapResolution);
                compute.SetInts(ShaderIds.TilePixelMin, pixelMinX, pixelMinY);
                compute.SetInts(ShaderIds.TilePixelMax, pixelMaxX, pixelMaxY);
                compute.SetVector(ShaderIds.TileUVScale, new Vector4(
                    unionSize.x / Mathf.Max(size.x, 0.001f),
                    unionSize.z / Mathf.Max(size.z, 0.001f), 0f, 0f));
                compute.SetVector(ShaderIds.TileUVOffset, new Vector4(
                    (unionMin.x - origin.x) / Mathf.Max(size.x, 0.001f),
                    (unionMin.z - origin.z) / Mathf.Max(size.z, 0.001f), 0f, 0f));
                compute.SetFloat(ShaderIds.TileHeightScale, TerrainHeightmapDecodeScale * size.y / unionHeight);
                compute.SetFloat(ShaderIds.TileHeightOffset, (origin.y - unionMin.y) / unionHeight);
                compute.Dispatch(compositeKernel, ThreadGroups(width), ThreadGroups(height), 1);
            }

            _lastHeightmapHash = hash;
        }

        private void ReleaseTerrainHeightmap()
        {
            ReleaseRenderTexture(ref _terrainHeightmap);
            _lastHeightmapHash = int.MinValue;
        }

        #endregion

        #region Noise texture

        private int CalculateNoiseHash()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + noiseTextureResolution;
                hash = hash * 31 + noiseSeed;
                return hash;
            }
        }

        private void EnsureNoiseTexture(bool force)
        {
            if (!SystemInfo.supportsComputeShaders || !LoadResources())
                return;

            int hash = CalculateNoiseHash();
            if (!force && _noiseTexture != null && _noiseTexture.IsCreated() && hash == _lastNoiseHash)
                return;

            int size = noiseTextureResolution;
            if (_noiseTexture == null || !_noiseTexture.IsCreated() || _noiseTexture.width != size)
            {
                ReleaseRenderTexture(ref _noiseTexture);
                _noiseTexture = new RenderTexture(size, size, 0, RenderTextureFormat.ARGB32)
                {
                    name = "Pure Volumetric Fog Noise",
                    enableRandomWrite = true,
                    useMipMap = true,
                    autoGenerateMips = false,
                    wrapMode = TextureWrapMode.Repeat,
                    filterMode = FilterMode.Trilinear,
                    anisoLevel = 0,
                    hideFlags = HideFlags.HideAndDontSave
                };
                if (!_noiseTexture.Create())
                {
                    ReleaseRenderTexture(ref _noiseTexture);
                    return;
                }
            }

            int kernel = s_noiseCompute.FindKernel(NoiseKernelName);
            s_noiseCompute.SetInt(ShaderIds.NoiseResolution, size);
            s_noiseCompute.SetInt(ShaderIds.NoiseSeed, noiseSeed);
            s_noiseCompute.SetTexture(kernel, ShaderIds.NoiseWrite, _noiseTexture);
            s_noiseCompute.Dispatch(kernel, ThreadGroups(size), ThreadGroups(size), 1);
            _noiseTexture.GenerateMips();
            _lastNoiseHash = hash;
        }

        #endregion

        #region Terrain horizon shadow map

        private bool HasFastTerrainShadowMap =>
            _terrainShadowMap != null && _terrainShadowMap.IsCreated();

        private bool HasRealtimeShadowSupport =>
            useUnityRealtimeShadows && HasUrpMainLightShadows();

        /// <summary>
        /// True when URP renders a main-light shadow map the fog shader can sample.
        /// </summary>
        private bool HasUrpMainLightShadows()
        {
            if (!(GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset pipeline))
                return false;
            if (!pipeline.supportsMainLightShadows ||
                pipeline.mainLightRenderingMode != LightRenderingMode.PerPixel)
                return false;

            Light sun = GetDirectionalLight();
            return sun != null && sun.enabled && sun.type == LightType.Directional &&
                sun.shadows != LightShadows.None;
        }

        private int CalculateTerrainShadowHash()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + _lastHeightmapHash;
                hash = hash * 31 + shadowDistance.GetHashCode();
                return hash;
            }
        }

        private void EnsureTerrainShadowMap(bool force)
        {
            if (!enableVolumetricShadows || !terrainCastsFogShadows ||
                _fastTerrainShadowUnavailable ||
                _terrainHeightmap == null || !LoadResources())
            {
                ReleaseTerrainShadowMaps();
                return;
            }

            if (_shadowBuildRow < 0)
            {
                Vector3 lightDirection = GetTowardLightDirection(GetDirectionalLight());
                int hash = CalculateTerrainShadowHash();
                bool directionChanged = !_hasTerrainShadowLightDirection ||
                    Vector3.Angle(_lastTerrainShadowLightDirection, lightDirection) >= ShadowMapRebuildAngle;

                if (force || !HasFastTerrainShadowMap || hash != _lastTerrainShadowHash || directionChanged)
                    BeginTerrainShadowBuild(lightDirection, hash);
            }

            if (_shadowBuildRow >= 0)
            {
                // Bake everything at once outside play mode so the editor never shows
                // a partially built map between repaints.
                bool complete = force || !Application.isPlaying;
                AdvanceTerrainShadowBuild(complete);
            }
        }

        private void BeginTerrainShadowBuild(Vector3 lightDirection, int hash)
        {
            ReleaseRenderTexture(ref _terrainShadowMapBuild);

            if (!TryGetComputeTextureFormat(out RenderTextureFormat format))
            {
                _fastTerrainShadowUnavailable = true;
                return;
            }

            _terrainShadowMapBuild = CreateTerrainShadowTexture(format);
            if (_terrainShadowMapBuild == null)
            {
                _fastTerrainShadowUnavailable = true;
                return;
            }

            _shadowBuildRow = 0;
            _shadowBuildHash = hash;
            _shadowBuildLightDirection = lightDirection;
        }

        private void AdvanceTerrainShadowBuild(bool complete)
        {
            int rows = complete
                ? TerrainShadowMapResolution - _shadowBuildRow
                : Mathf.Min(TerrainShadowRowsPerFrame, TerrainShadowMapResolution - _shadowBuildRow);
            if (rows <= 0)
            {
                FinishTerrainShadowBuild();
                return;
            }

            try
            {
                ComputeShader compute = s_horizonShadowCompute;
                int kernel = compute.FindKernel(TerrainShadowKernelName);
                Vector3 terrainSize = _terrainBounds.size;
                compute.SetInt(ShaderIds.ShadowOutputResolution, TerrainShadowMapResolution);
                compute.SetInt(ShaderIds.RowOffset, _shadowBuildRow);
                compute.SetInt(ShaderIds.TerrainShadowMapSteps, TerrainShadowMapSteps);
                compute.SetFloat(ShaderIds.ShadowDistanceWorld, shadowDistance);
                compute.SetFloat(ShaderIds.SunAngularRadiusRadians, TerrainShadowAngularRadius * Mathf.Deg2Rad);
                compute.SetVector(ShaderIds.TerrainSize, new Vector4(terrainSize.x, terrainSize.y, terrainSize.z, 0f));
                compute.SetVector(ShaderIds.LightDirectionWS, new Vector4(
                    _shadowBuildLightDirection.x,
                    _shadowBuildLightDirection.y,
                    _shadowBuildLightDirection.z,
                    0f));
                compute.SetTexture(kernel, ShaderIds.HeightmapTex, _terrainHeightmap);
                compute.SetTexture(kernel, ShaderIds.TerrainShadowMapWrite, _terrainShadowMapBuild);
                compute.Dispatch(kernel, ThreadGroups(TerrainShadowMapResolution), ThreadGroups(rows), 1);

                _shadowBuildRow += rows;
                if (_shadowBuildRow >= TerrainShadowMapResolution)
                    FinishTerrainShadowBuild();
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    "Pure Volumetric Fog: terrain shadow baking failed; " +
                    "the fog will continue without terrain shadowing. " + exception.Message,
                    this);
                ReleaseTerrainShadowMaps();
                _fastTerrainShadowUnavailable = true;
            }
        }

        private void FinishTerrainShadowBuild()
        {
            ReleaseRenderTexture(ref _terrainShadowMap);
            _terrainShadowMap = _terrainShadowMapBuild;
            _terrainShadowMapBuild = null;
            _shadowBuildRow = -1;
            _lastTerrainShadowHash = _shadowBuildHash;
            _lastTerrainShadowLightDirection = _shadowBuildLightDirection;
            _hasTerrainShadowLightDirection = true;
        }

        private RenderTexture CreateTerrainShadowTexture(RenderTextureFormat format)
        {
            RenderTexture texture = new RenderTexture(
                TerrainShadowMapResolution,
                TerrainShadowMapResolution,
                0,
                format)
            {
                name = "Pure Volumetric Fog Horizon Shadow Map",
                enableRandomWrite = true,
                useMipMap = false,
                autoGenerateMips = false,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave
            };
            if (texture.Create())
                return texture;
            SafeDestroy(texture);
            return null;
        }

        private static bool TryGetComputeTextureFormat(out RenderTextureFormat format)
        {
            format = RenderTextureFormat.ARGBHalf;
            if (SystemInfo.SupportsRenderTextureFormat(format) &&
                SystemInfo.SupportsRandomWriteOnRenderTextureFormat(format))
                return true;
            format = RenderTextureFormat.ARGBFloat;
            return SystemInfo.SupportsRenderTextureFormat(format) &&
                SystemInfo.SupportsRandomWriteOnRenderTextureFormat(format);
        }

        private void ReleaseTerrainShadowMaps()
        {
            ReleaseRenderTexture(ref _terrainShadowMap);
            ReleaseRenderTexture(ref _terrainShadowMapBuild);
            _shadowBuildRow = -1;
            _lastTerrainShadowHash = int.MinValue;
            _hasTerrainShadowLightDirection = false;
        }

        #endregion

        #region Resource helpers

        private static int ThreadGroups(int size)
        {
            return Mathf.CeilToInt(size / (float)ComputeThreadGroupSize);
        }

        private static Mesh CreateUnitCube()
        {
            Mesh mesh = new Mesh
            {
                name = "Pure Volumetric Fog Unit Cube",
                hideFlags = HideFlags.HideAndDontSave
            };

            Vector3[] vertices =
            {
                new Vector3(-0.5f,-0.5f,-0.5f), new Vector3( 0.5f,-0.5f,-0.5f),
                new Vector3( 0.5f, 0.5f,-0.5f), new Vector3(-0.5f, 0.5f,-0.5f),
                new Vector3(-0.5f,-0.5f, 0.5f), new Vector3( 0.5f,-0.5f, 0.5f),
                new Vector3( 0.5f, 0.5f, 0.5f), new Vector3(-0.5f, 0.5f, 0.5f)
            };

            int[] triangles =
            {
                4,7,6, 4,6,5,
                1,2,3, 1,3,0,
                0,3,7, 0,7,4,
                5,6,2, 5,2,1,
                3,2,6, 3,6,7,
                0,4,5, 0,5,1
            };

            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
            return mesh;
        }

        private void ReleaseGeneratedResources()
        {
            ReleaseTerrainShadowMaps();
            ReleaseTerrainHeightmap();
            ReleaseRenderTexture(ref _noiseTexture);
            _lastNoiseHash = int.MinValue;

            for (int i = 0; i < _materials.Length; i++)
            {
                SafeDestroy(_materials[i]);
                _materials[i] = null;
            }
            _nearFacesMaterial = null;
            _farFacesMaterial = null;

            if (_volumeMesh != null)
            {
                SafeDestroy(_volumeMesh);
                _volumeMesh = null;
            }
        }

        private static void ReleaseRenderTexture(ref RenderTexture texture)
        {
            if (texture == null)
                return;
            if (texture.IsCreated())
                texture.Release();
            SafeDestroy(texture);
            texture = null;
        }

        private static void SafeDestroy(UnityEngine.Object target)
        {
            if (target == null)
                return;
#if UNITY_EDITOR
            if (!Application.isPlaying)
                DestroyImmediate(target);
            else
                Destroy(target);
#else
            Destroy(target);
#endif
        }

        #endregion
    }
}
