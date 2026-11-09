using Motiong.vCharacterController;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Sprint FOV punch and explosion camera shake for the active gameplay camera.
/// Shake is applied only around render so it never fights the orbit camera update.
/// </summary>

namespace DeadAuthorGames.ThirdPersonCombat
{
    [DefaultExecutionOrder(100)]
    public class PlayerCameraEffects : MonoBehaviour
    {
        public static PlayerCameraEffects Instance { get; private set; }

        [Header("References")]
        [SerializeField] private Camera thirdPersonCamera;
        [SerializeField] private Camera aimCamera;
        [SerializeField] private vThirdPersonController controller;
        [SerializeField] private PlayerGunController gunController;

        [Header("Sprint FOV")]
        [SerializeField] private float defaultFov = 60f;
        [SerializeField] private float sprintFov = 72f;
        [SerializeField] private float fovSmoothSpeed = 7f;

        [Header("Explosion Shake")]
        [SerializeField] private float maxShakePos = 0.28f;
        [SerializeField] private float maxShakeRot = 3.5f;
        [SerializeField] private float shakeFrequency = 22f;
        [SerializeField] private float traumaDecay = 1.35f;
        [SerializeField] private float explosionShakeRadius = 28f;
        [SerializeField] private float barrelShakeStrength = 1f;
        [SerializeField] private float glassShakeStrength = 0.22f;

        private float _currentFov;
        private float _trauma;
        private float _seed;
        private Vector3 _shakePos;
        private Vector3 _shakeEuler;
        private bool _shakeApplied;
        private Camera _shakeCamera;
        private Vector3 _preShakePos;
        private Quaternion _preShakeRot;

        private void Awake()
        {
            // Prefer the local-player instance registered by NetworkPlayerSetup.
            if (Instance == null)
                Instance = this;
            _seed = Random.value * 100f;
            AutoAssign();
            CaptureDefaultFov();
            _currentFov = defaultFov;
        }

        private void OnEnable()
        {
            RenderPipelineManager.beginCameraRendering += HandleBeginCameraRendering;
            RenderPipelineManager.endCameraRendering += HandleEndCameraRendering;
        }

        private void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= HandleBeginCameraRendering;
            RenderPipelineManager.endCameraRendering -= HandleEndCameraRendering;
            RestoreShakeIfNeeded();
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        private void LateUpdate()
        {
            UpdateSprintFov();
            UpdateShakeValues();
        }

        public void AddExplosionShake(Vector3 worldPosition, float strength)
        {
            if (strength <= 0f)
                return;

            Transform listener = GetActiveCameraTransform();
            if (listener == null)
                return;

            float distance = Vector3.Distance(listener.position, worldPosition);
            float falloff = 1f - Mathf.Clamp01(distance / Mathf.Max(0.01f, explosionShakeRadius));
            if (falloff <= 0.01f)
                return;

            _trauma = Mathf.Clamp01(Mathf.Max(_trauma, strength * falloff));
        }

        public static void RegisterLocal(PlayerCameraEffects effects)
        {
            if (effects == null)
                return;
            Instance = effects;
        }

        public static void NotifyExplosion(Vector3 worldPosition, bool isBarrel)
        {
            if (Instance == null)
                Instance = FindAnyObjectByType<PlayerCameraEffects>();
            if (Instance == null)
                return;

            float strength = isBarrel ? Instance.barrelShakeStrength : Instance.glassShakeStrength;
            Instance.AddExplosionShake(worldPosition, strength);
        }

        private void AutoAssign()
        {
            if (thirdPersonCamera == null)
            {
                vThirdPersonCamera tp = FindAnyObjectByType<vThirdPersonCamera>();
                if (tp != null)
                    thirdPersonCamera = tp.GetComponent<Camera>();
            }

            if (aimCamera == null)
            {
                ResidentEvilAimCamera aim = FindAnyObjectByType<ResidentEvilAimCamera>(FindObjectsInactive.Include);
                if (aim != null)
                    aimCamera = aim.CameraComponent != null ? aim.CameraComponent : aim.GetComponent<Camera>();
            }

            if (controller == null)
                controller = FindAnyObjectByType<vThirdPersonController>();

            if (gunController == null)
                gunController = FindAnyObjectByType<PlayerGunController>();
        }

        private void CaptureDefaultFov()
        {
            if (thirdPersonCamera != null)
                defaultFov = thirdPersonCamera.fieldOfView;
        }

        private void UpdateSprintFov()
        {
            bool sprinting = controller != null
                && controller.isSprinting
                && !controller.isSliding
                && (gunController == null || !gunController.IsAiming);

            float target = sprinting ? sprintFov : defaultFov;
            _currentFov = Mathf.Lerp(_currentFov, target, 1f - Mathf.Exp(-fovSmoothSpeed * Time.unscaledDeltaTime));

            if (thirdPersonCamera != null && thirdPersonCamera.enabled)
                thirdPersonCamera.fieldOfView = _currentFov;
        }

        private void UpdateShakeValues()
        {
            if (_trauma <= 0.0001f)
            {
                _trauma = 0f;
                _shakePos = Vector3.zero;
                _shakeEuler = Vector3.zero;
                return;
            }

            float shake = _trauma * _trauma;
            float t = Time.unscaledTime * shakeFrequency;

            _shakePos = new Vector3(
                (Mathf.PerlinNoise(_seed, t) * 2f - 1f) * maxShakePos * shake,
                (Mathf.PerlinNoise(_seed + 1f, t) * 2f - 1f) * maxShakePos * shake,
                (Mathf.PerlinNoise(_seed + 2f, t) * 2f - 1f) * maxShakePos * 0.45f * shake);

            _shakeEuler = new Vector3(
                (Mathf.PerlinNoise(_seed + 3f, t) * 2f - 1f) * maxShakeRot * shake,
                (Mathf.PerlinNoise(_seed + 4f, t) * 2f - 1f) * maxShakeRot * shake,
                (Mathf.PerlinNoise(_seed + 5f, t) * 2f - 1f) * maxShakeRot * 0.6f * shake);

            _trauma = Mathf.Clamp01(_trauma - traumaDecay * Time.unscaledDeltaTime);
        }

        private void HandleBeginCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            bool hasShake = _shakePos.sqrMagnitude > 0.0000001f || _shakeEuler.sqrMagnitude > 0.0000001f;
            if (!hasShake || !ShouldShakeCamera(camera))
                return;

            _shakeCamera = camera;
            _preShakePos = camera.transform.position;
            _preShakeRot = camera.transform.rotation;
            camera.transform.position = _preShakePos + _shakePos;
            camera.transform.rotation = _preShakeRot * Quaternion.Euler(_shakeEuler);
            _shakeApplied = true;
        }

        private void HandleEndCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            if (!_shakeApplied || camera != _shakeCamera)
                return;

            camera.transform.SetPositionAndRotation(_preShakePos, _preShakeRot);
            _shakeApplied = false;
            _shakeCamera = null;
        }

        private void RestoreShakeIfNeeded()
        {
            if (!_shakeApplied || _shakeCamera == null)
                return;

            _shakeCamera.transform.SetPositionAndRotation(_preShakePos, _preShakeRot);
            _shakeApplied = false;
            _shakeCamera = null;
        }

        private bool ShouldShakeCamera(Camera camera)
        {
            if (camera == null)
                return false;

            if (gunController != null && gunController.IsAiming)
                return aimCamera != null && camera == aimCamera;

            return thirdPersonCamera != null && camera == thirdPersonCamera;
        }

        private Transform GetActiveCameraTransform()
        {
            if (gunController != null && gunController.IsAiming && aimCamera != null && aimCamera.enabled)
                return aimCamera.transform;

            if (thirdPersonCamera != null && thirdPersonCamera.enabled)
                return thirdPersonCamera.transform;

            if (aimCamera != null)
                return aimCamera.transform;

            return thirdPersonCamera != null ? thirdPersonCamera.transform : null;
        }

    #if UNITY_EDITOR
        public void EditorBind(Camera tpCam, Camera aimCam, vThirdPersonController player, PlayerGunController gun)
        {
            thirdPersonCamera = tpCam;
            aimCamera = aimCam;
            controller = player;
            gunController = gun;
            if (tpCam != null)
                defaultFov = tpCam.fieldOfView;
        }
    #endif
    }
}
