using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Game.Train
{
    /// <summary>
    /// Local orbit camera that rides with the train. The seated driver switches to it (see TrainSeatInteractor);
    /// mouse orbits around the locomotive relative to its heading, scroll wheel zooms.
    /// Purely local: each client has its own instance and nothing is networked.
    /// </summary>
    // Runs after everything that poses the train or riders in LateUpdate.
    [DefaultExecutionOrder(300)]
    [DisallowMultipleComponent]
    public class TrainCamera : MonoBehaviour
    {
        [Header("Orbit")]
        [Tooltip("Yaw relative to the train's heading. 0 = directly behind, looking forward.")]
        [SerializeField] private float defaultYaw = 25f;
        [SerializeField] private float defaultPitch = 15f;
        [SerializeField] private float minPitch = -5f;
        [SerializeField] private float maxPitch = 75f;
        [SerializeField] private float mouseSensitivity = 3f;

        [Header("Distance")]
        [Tooltip("0 = derive from the train's size.")]
        [SerializeField] private float defaultDistance;
        [SerializeField] private float minDistance = 6f;
        [SerializeField] private float maxDistance = 40f;
        [SerializeField] private float zoomSpeed = 8f;
        [SerializeField] private float zoomSmoothing = 10f;
        [Tooltip("Extra height of the orbit pivot above the train's centre.")]
        [SerializeField] private float pivotHeight = 1f;

        [Header("Collision")]
        [SerializeField] private LayerMask collisionMask = ~(1 << 2);
        [SerializeField] private float collisionRadius = 0.3f;
        [Tooltip("How fast the camera eases back out after an obstacle stops blocking it.")]
        [SerializeField] private float collisionRecoverSpeed = 4f;

        private static readonly RaycastHit[] Hits = new RaycastHit[16];

        private TrainWheelRig _rig;
        private Vector3 _localCenter;
        private Camera _camera;
        private Camera _restoreCamera;
        private float _yaw;
        private float _pitch;
        private float _targetDistance;
        private float _distance;
        private float _clippedDistance;

        public bool IsActive => _camera != null && _camera.enabled;

        private void Awake()
        {
            _rig = GetComponent<TrainWheelRig>();
            MeasureTrain();
            ResetView();
        }

        private void OnDisable()
        {
            Deactivate();
        }

        private void OnDestroy()
        {
            if (_camera != null)
                Destroy(_camera.gameObject);
        }

        public void ResetView()
        {
            _yaw = defaultYaw;
            _pitch = defaultPitch;
            _distance = _targetDistance = Mathf.Clamp(defaultDistance, minDistance, maxDistance);
        }

        /// <summary>Switch rendering from <paramref name="source"/> to the train camera.</summary>
        public void Activate(Camera source)
        {
            EnsureCamera(source);
            _restoreCamera = source;
            UpdatePose(true);
            _camera.enabled = true;
            if (source != null && source != _camera)
                source.enabled = false;
        }

        /// <summary>Hand rendering back to the camera that was active before <see cref="Activate"/>.</summary>
        public void Deactivate()
        {
            if (_camera != null)
                _camera.enabled = false;
            if (_restoreCamera != null)
                _restoreCamera.enabled = true;
            _restoreCamera = null;
        }

        /// <summary>Feed look input (mouse axes and scroll) while active.</summary>
        public void AddInput(float mouseX, float mouseY, float scroll)
        {
            _yaw = Mathf.Repeat(_yaw + mouseX * mouseSensitivity, 360f);
            _pitch = Mathf.Clamp(_pitch - mouseY * mouseSensitivity, minPitch, maxPitch);
            _targetDistance = Mathf.Clamp(_targetDistance - scroll * zoomSpeed, minDistance, maxDistance);
        }

        private void LateUpdate()
        {
            if (IsActive)
                UpdatePose(false);
        }

        private void UpdatePose(bool snap)
        {
            Vector3 pivot = transform.TransformPoint(_localCenter) + Vector3.up * pivotHeight;

            Vector3 heading = _rig != null ? _rig.ForwardWorld : transform.forward;
            heading = Vector3.ProjectOnPlane(heading, Vector3.up);
            if (heading.sqrMagnitude < 1e-6f)
                heading = Vector3.forward;

            Quaternion rotation = Quaternion.LookRotation(heading.normalized, Vector3.up) *
                                  Quaternion.Euler(_pitch, _yaw, 0f);

            _distance = snap
                ? _targetDistance
                : Mathf.Lerp(_distance, _targetDistance, 1f - Mathf.Exp(-zoomSmoothing * Time.deltaTime));

            Vector3 back = rotation * Vector3.back;
            float clipped = ClipDistance(pivot, back, _distance);
            // Pull in instantly so nothing gets between camera and train, but ease back out:
            // poles and terrain flicking past would otherwise make the distance pop every frame.
            if (snap || clipped < _clippedDistance)
                _clippedDistance = clipped;
            else
                _clippedDistance = Mathf.Lerp(_clippedDistance, clipped, 1f - Mathf.Exp(-collisionRecoverSpeed * Time.deltaTime));
            _camera.transform.SetPositionAndRotation(pivot + back * _clippedDistance, rotation);
        }

        private float ClipDistance(Vector3 pivot, Vector3 direction, float distance)
        {
            int count = Physics.SphereCastNonAlloc(pivot, collisionRadius, direction, Hits, distance,
                collisionMask, QueryTriggerInteraction.Ignore);

            float nearest = distance;
            for (int i = 0; i < count; i++)
            {
                Collider col = Hits[i].collider;
                if (col == null || col.transform.IsChildOf(transform) || col.attachedRigidbody != null)
                    continue;
                if (Hits[i].distance > 0f && Hits[i].distance < nearest)
                    nearest = Hits[i].distance;
            }

            return Mathf.Max(1f, nearest);
        }

        private void EnsureCamera(Camera source)
        {
            if (_camera != null)
                return;

            var go = new GameObject("TrainCamera (local)");
            _camera = go.AddComponent<Camera>();
            if (source != null)
            {
                _camera.CopyFrom(source);
                var src = source.GetComponent<UniversalAdditionalCameraData>();
                if (src != null)
                {
                    var dst = _camera.GetUniversalAdditionalCameraData();
                    dst.renderPostProcessing = src.renderPostProcessing;
                    dst.antialiasing = src.antialiasing;
                    dst.antialiasingQuality = src.antialiasingQuality;
                    dst.renderShadows = src.renderShadows;
                }
            }

            _camera.tag = "Untagged";
            _camera.enabled = false;
        }

        private void MeasureTrain()
        {
            Renderer[] renderers = GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
            {
                _localCenter = Vector3.zero;
                if (defaultDistance <= 0f)
                    defaultDistance = 15f;
                return;
            }

            Bounds world = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                world.Encapsulate(renderers[i].bounds);

            _localCenter = transform.InverseTransformPoint(world.center);
            if (defaultDistance <= 0f)
                defaultDistance = Mathf.Clamp(world.extents.magnitude * 1.3f, minDistance, maxDistance);
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            minDistance = Mathf.Max(1f, minDistance);
            maxDistance = Mathf.Max(minDistance, maxDistance);
            maxPitch = Mathf.Clamp(maxPitch, minPitch, 89f);
        }
#endif
    }
}
