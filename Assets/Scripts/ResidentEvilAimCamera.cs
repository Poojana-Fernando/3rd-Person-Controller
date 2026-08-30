using UnityEngine;

[DefaultExecutionOrder(70)]
public class ResidentEvilAimCamera : MonoBehaviour
{
    [Header("Anchor")]
    public Transform followTarget;
    public Transform spineAnchor;
    [Tooltip("Camera position relative to the player root (authored from scene placement).")]
    public Vector3 localPositionOffset = new Vector3(0.48f, 1.65f, -0.95f);
    [Tooltip("Extra rotation applied after aim pitch/yaw (X=pitch, Y=yaw, Z=roll).")]
    public Vector3 localRotationOffset = Vector3.zero;
    [Tooltip("Starting horizontal look angle when aim syncs without a third-person camera.")]
    public float defaultYaw = 182f;

    [Header("Look")]
    public float mouseSensitivityX = 2.5f;
    public float mouseSensitivityY = 2.5f;
    public float minPitch = -40f;
    public float maxPitch = 40f;

    [Header("Smoothing")]
    public float rotationSmoothTime = 0.05f;

    [Header("Transition")]
    public float transitionDuration = 0.25f;

    [Header("Collision")]
    public LayerMask cameraCollisionMask = ~0;
    public float cameraCollisionRadius = 0.2f;
    public float crosshairMinDistance = 0.5f;

    [Header("Aim Point")]
    [Tooltip("UI crosshair. Aim/fire rays go through this screen point (not camera dead-center).")]
    public RectTransform aimCrosshair;

    [Header("Recoil")]
    public float recoilRecoverySpeed = 14f;

    [Header("Sync")]
    public UpperBodyAim upperBodyAim;

    private Camera _camera;
    private AudioListener _listener;
    private float _bodyYaw;
    private float _pitch;
    private float _recoilKick;
    private float _smoothedBodyYaw;
    private float _smoothedPitch;
    private float _bodyYawVelocity;
    private float _pitchVelocity;
    private bool _active;
    private bool _isClippingPlayer;
    private float _enterBlend = 1f;
    private Vector3 _enterBlendStartPos;
    private Quaternion _enterBlendStartRot;

    public bool IsActive => _active;
    public bool IsEnterBlending => _enterBlend < 1f;
    public bool IsClippingPlayer => _isClippingPlayer;
    public Camera CameraComponent => _camera;
    public float Pitch => _pitch;
    public float SmoothedPitch => _smoothedPitch;
    public float AimPitch => _smoothedPitch + _recoilKick;
    public float BodyYaw => _bodyYaw;
    public float SmoothedBodyYaw => _smoothedBodyYaw;
    public float UpperBodyYawOffset => 0f;
    public float Yaw => _bodyYaw;
    public Vector3 AimForward => transform.forward;

    private void Awake()
    {
        _camera = GetComponent<Camera>();
        _listener = GetComponent<AudioListener>();
        SetAimCameraActive(false);
        AutoAssignReferences();
    }

    public void SetAimCameraActive(bool active)
    {
        _active = active;
        enabled = active;

        if (_camera != null)
            _camera.enabled = active;

        if (_listener != null)
            _listener.enabled = active;

        if (active)
        {
            ApplyCameraTransformImmediate();
        }
        else
        {
            _enterBlend = 1f;
        }
    }

    public void BeginEnterBlend(Camera fromCamera, float duration)
    {
        transitionDuration = Mathf.Max(0.01f, duration);

        if (fromCamera == null)
        {
            _enterBlend = 1f;
            return;
        }

        _enterBlend = 0f;
        _enterBlendStartPos = fromCamera.transform.position;
        _enterBlendStartRot = fromCamera.transform.rotation;
        transform.SetPositionAndRotation(_enterBlendStartPos, _enterBlendStartRot);
    }

    public void SyncYaw(float yaw, float pitch = 0f)
    {
        _bodyYaw = yaw;
        _pitch = pitch;
        _recoilKick = 0f;

        if (upperBodyAim != null)
            upperBodyAim.CurrentPitch = pitch;

        SnapSmoothedValues();
        ApplyCameraTransformImmediate();
        SyncUpperBodyAim();
    }

    public void SyncYawFromTarget()
    {
        if (followTarget != null)
            SyncYaw(followTarget.eulerAngles.y);
        else
            SyncYaw(defaultYaw);
    }

    public void AddRecoilKick(float impulse)
    {
        _recoilKick += impulse;
    }

    /// <summary>
    /// Ray from the aim camera through the authored crosshair (or viewport center if unset).
    /// </summary>
    public Ray GetAimRay()
    {
        if (_camera == null)
            _camera = GetComponent<Camera>();

        if (_camera == null)
            return new Ray(transform.position, transform.forward);

        if (aimCrosshair != null)
        {
            // Screen Space Overlay: RectTransform.position is already in screen pixels.
            Vector3 screenPoint = aimCrosshair.position;
            screenPoint.z = 0f;
            return _camera.ScreenPointToRay(screenPoint);
        }

        return _camera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
    }

    public Ray GetScreenCenterRay()
    {
        return GetAimRay();
    }

    public Vector3 GetAimDirection()
    {
        return GetAimRay().direction.normalized;
    }

    public void SetAimCrosshair(RectTransform crosshair)
    {
        aimCrosshair = crosshair;
    }

    private void LateUpdate()
    {
        if (!_active)
            return;

        HandleLookInput();
        ApplyCameraRotation();
        SyncFollowTargetToLookYaw();

        Vector3 targetPosition = ResolveTargetPosition();
        Quaternion targetRotation = ComposeAimRotation(_smoothedPitch + _recoilKick, _smoothedBodyYaw);
        ApplyTransitionOrSnap(targetPosition, targetRotation);

        SyncUpperBodyAim();
    }

    private void HandleLookInput()
    {
        _bodyYaw += Input.GetAxis("Mouse X") * mouseSensitivityX;
        _pitch -= Input.GetAxis("Mouse Y") * mouseSensitivityY;

        if (upperBodyAim != null)
            upperBodyAim.ClampPitch(ref _pitch);
        else
            _pitch = Mathf.Clamp(_pitch, minPitch, maxPitch);

        _recoilKick = Mathf.MoveTowards(_recoilKick, 0f, recoilRecoverySpeed * Time.deltaTime);
    }

    private void SyncUpperBodyAim()
    {
        if (upperBodyAim == null)
            return;

        upperBodyAim.CurrentPitch = AimPitch;
    }

    public void SyncFollowTargetToLookYaw()
    {
        if (followTarget == null)
            return;

        followTarget.rotation = Quaternion.Euler(0f, _smoothedBodyYaw, 0f);
    }

    public void RefreshPosition()
    {
        ApplyCameraPosition();
    }

    private void ApplySnappedRotation()
    {
        transform.rotation = ComposeAimRotation(_smoothedPitch + _recoilKick, _smoothedBodyYaw);
    }

    private Quaternion ComposeAimRotation(float pitch, float yaw)
    {
        Quaternion baseRotation = Quaternion.Euler(pitch, yaw, 0f);
        if (localRotationOffset.sqrMagnitude < 0.0001f)
            return baseRotation;

        return baseRotation * Quaternion.Euler(localRotationOffset);
    }

    private void SnapSmoothedValues()
    {
        _smoothedBodyYaw = _bodyYaw;
        _smoothedPitch = _pitch;
        _bodyYawVelocity = 0f;
        _pitchVelocity = 0f;
    }

    private Vector3 ComputeDesiredPosition()
    {
        if (followTarget == null)
            return transform.position;

        return followTarget.TransformPoint(localPositionOffset);
    }

    private Vector3 ResolveCameraPosition(Vector3 pivot, Vector3 desiredPosition)
    {
        Vector3 toDesired = desiredPosition - pivot;
        float desiredDistance = toDesired.magnitude;

        if (desiredDistance <= 0.001f)
        {
            _isClippingPlayer = true;
            return desiredPosition;
        }

        Vector3 direction = toDesired / desiredDistance;
        float resolvedDistance = desiredDistance;

        RaycastHit[] hits = Physics.SphereCastAll(
            pivot,
            cameraCollisionRadius,
            direction,
            desiredDistance,
            cameraCollisionMask,
            QueryTriggerInteraction.Ignore);

        for (int i = 0; i < hits.Length; i++)
        {
            if (IsIgnoredCollider(hits[i].collider))
                continue;

            resolvedDistance = Mathf.Min(
                resolvedDistance,
                Mathf.Max(0.05f, hits[i].distance - cameraCollisionRadius));
        }

        _isClippingPlayer = resolvedDistance < crosshairMinDistance;
        return pivot + direction * resolvedDistance;
    }

    private bool IsIgnoredCollider(Collider collider)
    {
        if (collider == null || followTarget == null)
            return false;

        return collider.transform == followTarget || collider.transform.IsChildOf(followTarget);
    }

    private void ApplyTransitionOrSnap(Vector3 targetPosition, Quaternion targetRotation)
    {
        if (_enterBlend >= 1f)
        {
            transform.SetPositionAndRotation(targetPosition, targetRotation);
            return;
        }

        _enterBlend = Mathf.MoveTowards(_enterBlend, 1f, Time.deltaTime / transitionDuration);
        float t = SmoothStep(_enterBlend);
        transform.position = Vector3.Lerp(_enterBlendStartPos, targetPosition, t);
        transform.rotation = Quaternion.Slerp(_enterBlendStartRot, targetRotation, t);
    }

    private static float SmoothStep(float t)
    {
        return t * t * (3f - 2f * t);
    }

    private Vector3 ResolveTargetPosition()
    {
        if (followTarget == null)
            return transform.position;

        Vector3 desiredPosition = ComputeDesiredPosition();
        return ResolveCameraPosition(followTarget.position, desiredPosition);
    }

    private void ApplyCameraTransformImmediate()
    {
        SnapSmoothedValues();
        Vector3 targetPosition = ResolveTargetPosition();
        Quaternion targetRotation = ComposeAimRotation(_smoothedPitch + _recoilKick, _smoothedBodyYaw);
        transform.SetPositionAndRotation(targetPosition, targetRotation);
    }

    private void ApplyCameraPosition()
    {
        if (followTarget == null)
            return;

        Vector3 desiredPosition = ComputeDesiredPosition();
        transform.position = ResolveCameraPosition(followTarget.position, desiredPosition);
    }

    private void ApplyCameraRotation()
    {
        _smoothedBodyYaw = Mathf.SmoothDampAngle(_smoothedBodyYaw, _bodyYaw, ref _bodyYawVelocity, rotationSmoothTime);
        _smoothedPitch = Mathf.SmoothDamp(_smoothedPitch, _pitch, ref _pitchVelocity, rotationSmoothTime);
    }

    private void AutoAssignReferences()
    {
        if (followTarget == null)
        {
            var player = GameObject.Find("ThirdPersonController_LITE");
            if (player != null)
                followTarget = player.transform;
        }

        if (spineAnchor == null && followTarget != null)
        {
            spineAnchor = FindBoneByName(followTarget, "CC_Base_Spine01");
            if (spineAnchor == null)
                spineAnchor = followTarget.Find("guard/Armature/root ground/root hip/spine lower/spine middle");
        }

        if (upperBodyAim == null && followTarget != null)
            upperBodyAim = followTarget.GetComponent<UpperBodyAim>();

        if (aimCrosshair == null)
        {
            GameObject crosshairGo = GameObject.Find("Crossair");
            if (crosshairGo != null)
                aimCrosshair = crosshairGo.GetComponent<RectTransform>();
        }

        if (followTarget != null)
        {
            _bodyYaw = followTarget.eulerAngles.y;
            SnapSmoothedValues();
        }
    }

    private static Transform FindBoneByName(Transform root, string exactName)
    {
        Transform[] all = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i].name == exactName)
                return all[i];
        }

        return null;
    }
}
