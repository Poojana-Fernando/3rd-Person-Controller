using UnityEngine;

[DefaultExecutionOrder(70)]
public class ResidentEvilAimCamera : MonoBehaviour
{
    [Header("Anchor")]
    public Transform followTarget;
    public Transform spineAnchor;
    public Vector3 anchorOffset;

    [Header("Shoulder Camera")]
    public float aimDistance = 2f;
    public float shoulderSide = 0.55f;
    public float shoulderHeight = 1.55f;
    public float bodyTurnSmoothTime = 0.12f;
    public float cameraPositionSmoothTime = 0.04f;

    [Header("Look")]
    public float mouseSensitivityX = 2.5f;
    public float mouseSensitivityY = 2.5f;
    public float minPitch = -40f;
    public float maxPitch = 40f;
    public float maxUpperBodyYaw = 30f;

    [Header("Recoil")]
    public float recoilRecoverySpeed = 14f;

    [Header("Sync")]
    public UpperBodyAim upperBodyAim;

    private Camera _camera;
    private AudioListener _listener;
    private float _bodyYaw;
    private float _upperBodyYawOffset;
    private float _displayBodyYaw;
    private float _bodyYawVelocity;
    private float _pitch;
    private float _recoilKick;
    private Vector3 _displayPosition;
    private Vector3 _positionVelocity;
    private bool _active;
    private bool _displayInitialized;

    public bool IsActive => _active;
    public Camera CameraComponent => _camera;
    public float Pitch => _pitch;
    public float BodyYaw => _bodyYaw;
    public float DisplayBodyYaw => _displayBodyYaw;
    public float UpperBodyYawOffset => _upperBodyYawOffset;
    public float Yaw => _bodyYaw + _upperBodyYawOffset;

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
            SnapDisplayState();
    }

    public void SyncYaw(float yaw)
    {
        float bodyYaw = followTarget != null ? followTarget.eulerAngles.y : yaw;
        float delta = Mathf.DeltaAngle(bodyYaw, yaw);

        if (delta > maxUpperBodyYaw)
        {
            _bodyYaw = yaw - maxUpperBodyYaw;
            _upperBodyYawOffset = maxUpperBodyYaw;
        }
        else if (delta < -maxUpperBodyYaw)
        {
            _bodyYaw = yaw + maxUpperBodyYaw;
            _upperBodyYawOffset = -maxUpperBodyYaw;
        }
        else
        {
            _bodyYaw = bodyYaw;
            _upperBodyYawOffset = delta;
        }

        _pitch = 0f;
        _recoilKick = 0f;

        if (upperBodyAim != null)
            upperBodyAim.CurrentPitch = 0f;

        SyncUpperBodyAim();
    }

    public void SyncYawFromTarget()
    {
        if (followTarget != null)
            SyncYaw(followTarget.eulerAngles.y);
    }

    public void SnapDisplayState()
    {
        _displayBodyYaw = _bodyYaw;
        _bodyYawVelocity = 0f;
        _positionVelocity = Vector3.zero;

        if (followTarget != null)
            _displayPosition = followTarget.position + GetBodyRotation(_displayBodyYaw) * GetLocalShoulderOffset();

        _displayInitialized = followTarget != null;
    }

    public void ResetUpperBodyOffset()
    {
        _bodyYaw = Yaw;
        _upperBodyYawOffset = 0f;
        SyncUpperBodyAim();
    }

    public void AddRecoilKick(float impulse)
    {
        _recoilKick += impulse;
    }

    public Ray GetScreenCenterRay()
    {
        if (_camera == null)
            return new Ray(transform.position, transform.forward);

        return _camera.ScreenPointToRay(new Vector3(Screen.width * 0.5f, Screen.height * 0.5f, 0f));
    }

    private void LateUpdate()
    {
        if (!_active)
            return;

        HandleLookInput();
        ApplyCameraTransform();
    }

    private void HandleLookInput()
    {
        ApplyHorizontalLook(Input.GetAxis("Mouse X") * mouseSensitivityX);
        _pitch -= Input.GetAxis("Mouse Y") * mouseSensitivityY;

        if (upperBodyAim != null)
            upperBodyAim.ClampPitch(ref _pitch);
        else
            _pitch = Mathf.Clamp(_pitch, minPitch, maxPitch);

        if (upperBodyAim != null)
            upperBodyAim.CurrentPitch = _pitch;

        _recoilKick = Mathf.MoveTowards(_recoilKick, 0f, recoilRecoverySpeed * Time.deltaTime);
    }

    private void ApplyHorizontalLook(float deltaYaw)
    {
        _upperBodyYawOffset += deltaYaw;

        if (_upperBodyYawOffset > maxUpperBodyYaw)
        {
            float spill = _upperBodyYawOffset - maxUpperBodyYaw;
            _bodyYaw += spill;
            _upperBodyYawOffset = maxUpperBodyYaw;
        }
        else if (_upperBodyYawOffset < -maxUpperBodyYaw)
        {
            float spill = _upperBodyYawOffset + maxUpperBodyYaw;
            _bodyYaw += spill;
            _upperBodyYawOffset = -maxUpperBodyYaw;
        }

        SyncUpperBodyAim();
    }

    private void SyncUpperBodyAim()
    {
        if (upperBodyAim == null)
            return;

        upperBodyAim.CurrentYawOffset = _upperBodyYawOffset;
    }

    private void ApplyCameraTransform()
    {
        if (followTarget == null)
            return;

        _displayBodyYaw = Mathf.SmoothDampAngle(
            _displayBodyYaw,
            _bodyYaw,
            ref _bodyYawVelocity,
            bodyTurnSmoothTime);

        float aimYaw = _displayBodyYaw + _upperBodyYawOffset;
        float finalPitch = _pitch + _recoilKick;
        transform.rotation = Quaternion.Euler(finalPitch, aimYaw, 0f);

        Vector3 targetPosition = followTarget.position + GetBodyRotation(_displayBodyYaw) * GetLocalShoulderOffset();
        if (!_displayInitialized)
        {
            _displayPosition = targetPosition;
            _displayInitialized = true;
        }
        else
        {
            _displayPosition = Vector3.SmoothDamp(
                _displayPosition,
                targetPosition,
                ref _positionVelocity,
                cameraPositionSmoothTime);
        }

        transform.position = _displayPosition;
    }

    private Vector3 GetLocalShoulderOffset()
    {
        return new Vector3(shoulderSide, shoulderHeight, -Mathf.Max(0.1f, aimDistance));
    }

    private static Quaternion GetBodyRotation(float yaw)
    {
        return Quaternion.Euler(0f, yaw, 0f);
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
            spineAnchor = followTarget.Find("guard/Armature/root ground/root hip/spine lower/spine middle");
        }

        if (upperBodyAim == null && followTarget != null)
            upperBodyAim = followTarget.GetComponent<UpperBodyAim>();

        if (followTarget != null)
            _bodyYaw = followTarget.eulerAngles.y;
    }
}
