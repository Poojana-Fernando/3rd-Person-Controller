using Motiong;
using Motiong.vCharacterController;
using UnityEngine;

public class vThirdPersonCamera : MonoBehaviour
{
    #region inspector properties    

    public Transform target;
    [Tooltip("Lerp speed between Camera States")]
    public float smoothCameraRotation = 12f;
    [Tooltip("What layer will be culled")]
    public LayerMask cullingLayer = 1 << 0;
    [Tooltip("Debug purposes, lock the camera behind the character for better align the states")]
    public bool lockCamera;

    public float rightOffset = 0f;
    public float defaultDistance = 2.5f;
    public float height = 1.4f;
    public float smoothFollow = 10f;
    public float xMouseSensitivity = 3f;
    public float yMouseSensitivity = 3f;
    public float yMinLimit = -40f;
    public float yMaxLimit = 80f;

    [Header("Transition")]
    public float transitionDuration = 0.25f;

    [Header("Slide Camera")]
    [Tooltip("Lower the orbit height while sliding for a cinematic angle.")]
    public float slideHeightOffset = -0.55f;
    public float slideOffsetSmoothTime = 0.15f;

    [Header("Crouch Camera")]
    [Tooltip("Lower the orbit height while crouching.")]
    public float crouchHeightOffset = -0.45f;
    public float crouchOffsetSmoothTime = 0.12f;

    [Header("First Person")]
    [Tooltip("Eye offset in look space (Y up, Z forward from head bone).")]
    public Vector3 firstPersonEyeLocalOffset = new Vector3(0f, 0.06f, 0.1f);
    [Tooltip("Fallback eye height from root when no head bone is found.")]
    public float firstPersonFallbackEyeHeight = 1.55f;
    public float firstPersonNearClip = 0.05f;
    public float firstPersonYMinLimit = -80f;
    public float firstPersonYMaxLimit = 85f;

    #endregion

    #region hide properties    

    [HideInInspector]
    public int indexList, indexLookPoint;
    [HideInInspector]
    public float offSetPlayerPivot;
    [HideInInspector]
    public string currentStateName;
    [HideInInspector]
    public Transform currentTarget;
    [HideInInspector]
    public Vector2 movementSpeed;

    private Transform targetLookAt;
    private Vector3 currentTargetPos;
    private Vector3 lookPoint;
    private Vector3 current_cPos;
    private Vector3 desired_cPos;
    private Camera _camera;
    private float distance = 5f;
    private float mouseY = 0f;
    private float mouseX = 0f;
    private float currentHeight;
    private float cullingDistance;
    private float checkHeightRadius = 0.4f;
    private float clipPlaneMargin = 0f;
    private float forward = -1f;
    private float xMinLimit = -360f;
    private float xMaxLimit = 360f;
    private float cullingHeight = 0.2f;
    private float cullingMinDist = 0.1f;
    private float _distanceVelocity;
    private float _heightVelocity;
    private float _exitBlend = 1f;
    private Vector3 _exitBlendStartPos;
    private Quaternion _exitBlendStartRot;
    private float _currentSlideHeightOffset;
    private float _slideHeightOffsetVelocity;
    private float _currentCrouchHeightOffset;
    private float _crouchHeightOffsetVelocity;
    private vThirdPersonController _controller;
    private Rigidbody _targetBody;
    private Transform _targetBodyOwner;
    private bool _firstPerson;
    private float _defaultNearClip = 0.3f;
    private Transform _headBone;
    private Transform _headResolveRoot;
    private IFirstPersonRigHost _firstPersonRig;
    private static readonly string[] HeadBoneNames =
    {
        "CC_Base_Head", "head", "Head", "mixamorig:Head"
    };
    private const float MinCameraDistance = 0.35f;
    private const float CullingSmoothTime = 0.12f;

    #endregion

    public bool IsFirstPerson => _firstPerson;

    public void ExportLook(out float yaw, out float pitch)
    {
        if (_firstPersonRig != null && _firstPersonRig.IsActive)
            _firstPersonRig.ExportOrbit(out yaw, out pitch);
        else
        {
            yaw = mouseX;
            pitch = mouseY;
        }
    }

    void Start()
    {
        Init();
    }

    public void Init()
    {
        if (target == null)
            return;

        _camera = GetComponent<Camera>();
        if (_camera == null)
            _camera = GetComponentInChildren<Camera>(true);
        if (_camera != null)
            _defaultNearClip = _camera.nearClipPlane;
        currentTarget = target;
        currentTargetPos = new Vector3(currentTarget.position.x, currentTarget.position.y + offSetPlayerPivot, currentTarget.position.z);

        _firstPersonRig = FirstPersonRigUtility.EnsureRig(gameObject);
        if (_camera != null && currentTarget != null && _firstPersonRig != null)
            _firstPersonRig.BindPlayer(currentTarget, _camera);

        // Keep targetLookAt unparented
        // then moves this transform. Parenting under the camera creates a feedback shake.
        // DontDestroyOnLoad so Bootstrap→gameplay scene loads don't destroy it.
        if (targetLookAt == null)
        {
            var go = new GameObject("targetLookAt");
            targetLookAt = go.transform;
            targetLookAt.hideFlags = HideFlags.HideInHierarchy;
            DontDestroyOnLoad(go);
        }

        targetLookAt.position = currentTarget.position;
        targetLookAt.rotation = currentTarget.rotation;

        mouseY = currentTarget.eulerAngles.x;
        mouseX = currentTarget.eulerAngles.y;

        distance = defaultDistance;
        currentHeight = height;
    }

    void FixedUpdate()
    {
        if (!LateFollow)
            TickCamera();
    }

    // An interpolated target moves every rendered frame, so following it at the physics rate would shake.
    void LateUpdate()
    {
        if (LateFollow)
            TickCamera();
    }

    private bool LateFollow
    {
        get
        {
            Transform t = currentTarget != null ? currentTarget : target;
            if (t != _targetBodyOwner)
            {
                _targetBodyOwner = t;
                _targetBody = t != null ? t.GetComponentInParent<Rigidbody>() : null;
            }
            // Kinematic targets (e.g. seated on the train) are posed per frame, not per physics step.
            return _targetBody != null &&
                   (_targetBody.interpolation != RigidbodyInterpolation.None || _targetBody.isKinematic);
        }
    }

    private void TickCamera()
    {
        if (target == null)
            return;

        // Recreate helper if somehow destroyed (e.g. before DDOL applied).
        if (targetLookAt == null)
            Init();

        if (targetLookAt == null)
            return;

        if (_camera != null && !_camera.enabled)
            return;

        CameraMovement();
    }

    void OnDestroy()
    {
        if (targetLookAt != null)
        {
            Destroy(targetLookAt.gameObject);
            targetLookAt = null;
        }
    }

    /// <summary>
    /// Set the target for the camera
    /// </summary>
    /// <param name="New cursorObject"></param>
    public void SetTarget(Transform newTarget)
    {
        currentTarget = newTarget ? newTarget : target;
    }

    public void SetMainTarget(Transform newTarget)
    {
        target = newTarget;
        currentTarget = newTarget;
        _headBone = null;
        _headResolveRoot = null;
        // Preserve current orbit angles when rebinding after scene load / teleport.
        if (targetLookAt == null)
        {
            mouseY = currentTarget.rotation.eulerAngles.x;
            mouseX = currentTarget.rotation.eulerAngles.y;
        }

        InitPreserveAngles();
    }

    /// <summary>
    /// Re-init helper without resetting mouseX/mouseY (avoids snap/shake on rebind).
    /// </summary>
    public void InitPreserveAngles()
    {
        if (target == null)
            return;

        float keepX = mouseX;
        float keepY = mouseY;
        float keepDistance = distance;
        float keepHeight = currentHeight;
        Init();
        mouseX = keepX;
        mouseY = keepY;
        distance = keepDistance > 0.01f ? keepDistance : defaultDistance;
        currentHeight = keepHeight > 0.01f ? keepHeight : height;
        if (targetLookAt != null)
            targetLookAt.rotation = Quaternion.Euler(mouseY, mouseX, 0f);
    }

    /// <summary>    
    /// Convert a point in the screen in a Ray for the world
    /// </summary>
    /// <param name="Point"></param>
    /// <returns></returns>
    public Ray ScreenPointToRay(Vector3 Point)
    {
        if (_camera == null)
            _camera = GetComponent<Camera>();
        if (_camera == null)
            _camera = GetComponentInChildren<Camera>(true);
        return _camera != null ? _camera.ScreenPointToRay(Point) : new Ray(transform.position, transform.forward);
    }

    /// <summary>
    /// Camera Rotation behaviour
    /// </summary>
    /// <param name="x"></param>
    /// <param name="y"></param>
    public void SyncRotation(float yaw, float pitch)
    {
        mouseX = yaw;
        mouseY = vExtensions.ClampAngle(pitch, yMinLimit, yMaxLimit);

        if (targetLookAt != null)
            targetLookAt.rotation = Quaternion.Euler(mouseY, mouseX, 0f);
    }

    public float HorizontalAngle => mouseX;
    public float VerticalAngle => mouseY;
    public bool IsExitBlending => _exitBlend < 1f;

    public void BeginExitBlend(Vector3 worldPosition, Quaternion worldRotation, float duration)
    {
        transitionDuration = Mathf.Max(0.01f, duration);
        _exitBlend = 0f;
        _exitBlendStartPos = worldPosition;
        _exitBlendStartRot = worldRotation;
        transform.SetPositionAndRotation(worldPosition, worldRotation);
    }

    public void SetFirstPerson(bool firstPerson)
    {
        if (_firstPerson == firstPerson)
            return;

        BeginExitBlend(transform.position, transform.rotation, transitionDuration);
        _firstPerson = firstPerson;

        if (_camera == null)
            _camera = GetComponent<Camera>();
        if (_camera == null)
            _camera = GetComponentInChildren<Camera>(true);

        if (_camera != null)
            _camera.nearClipPlane = firstPerson ? firstPersonNearClip : _defaultNearClip;

        if (firstPerson)
            mouseY = vExtensions.ClampAngle(mouseY, firstPersonYMinLimit, firstPersonYMaxLimit);

        if (_firstPersonRig == null)
            _firstPersonRig = FirstPersonRigUtility.EnsureRig(gameObject);

        if (_firstPersonRig != null && currentTarget != null)
        {
            _firstPersonRig.BindPlayer(currentTarget, _camera);
            if (firstPerson)
                _firstPersonRig.SyncFromOrbit(mouseX, mouseY);
            _firstPersonRig.SetActive(firstPerson);
            if (firstPerson && _firstPersonRig.IsActive)
                _exitBlend = 1f;
        }
    }

    public void RotateCamera(float x, float y)
    {
        if (_firstPerson && _firstPersonRig != null && _firstPersonRig.IsActive)
        {
            // Look is driven only by FirstPersonSystem.ReadInputDirect — sync orbit for legacy readers.
            _firstPersonRig.ExportOrbit(out mouseX, out mouseY);
            movementSpeed = Vector2.zero;
            return;
        }

        if (_firstPerson)
        {
            mouseX += x * xMouseSensitivity;
            mouseY -= y * yMouseSensitivity;
            if (!lockCamera)
            {
                mouseY = vExtensions.ClampAngle(mouseY, firstPersonYMinLimit, firstPersonYMaxLimit);
                mouseX = vExtensions.ClampAngle(mouseX, xMinLimit, xMaxLimit);
            }
            movementSpeed.x = x;
            movementSpeed.y = -y;
            return;
        }

        // free rotation 
        mouseX += x * xMouseSensitivity;
        mouseY -= y * yMouseSensitivity;

        movementSpeed.x = x;
        movementSpeed.y = -y;
        if (!lockCamera)
        {
            float minY = _firstPerson ? firstPersonYMinLimit : yMinLimit;
            float maxY = _firstPerson ? firstPersonYMaxLimit : yMaxLimit;
            mouseY = vExtensions.ClampAngle(mouseY, minY, maxY);
            mouseX = vExtensions.ClampAngle(mouseX, xMinLimit, xMaxLimit);
        }
        else
        {
            mouseY = currentTarget.root.localEulerAngles.x;
            mouseX = currentTarget.root.localEulerAngles.y;
        }
    }

    /// <summary>
    /// Camera behaviour
    /// </summary>    
    void CameraMovement()
    {
        if (currentTarget == null)
            return;

        UpdateSlideHeightOffset();
        UpdateCrouchHeightOffset();

        if (_firstPerson)
        {
            FirstPersonCameraMovement();
            return;
        }

        float orbitHeight = height + _currentSlideHeightOffset + _currentCrouchHeightOffset;

        float targetDistance = defaultDistance;
        float targetHeight = orbitHeight;

        var camDir = (forward * targetLookAt.forward) + (rightOffset * targetLookAt.right);
        camDir = camDir.normalized;

        var targetPos = new Vector3(currentTarget.position.x, currentTarget.position.y + offSetPlayerPivot, currentTarget.position.z);
        currentTargetPos = targetPos;
        desired_cPos = targetPos + new Vector3(0, orbitHeight, 0);

        RaycastHit hitInfo;
        float probeHeight = orbitHeight;

        if (Physics.SphereCast(targetPos, checkHeightRadius, Vector3.up, out hitInfo, orbitHeight + 0.2f, cullingLayer))
        {
            float t = hitInfo.distance - 0.2f;
            t -= orbitHeight;
            t /= (cullingHeight - orbitHeight);
            probeHeight = Mathf.Lerp(orbitHeight, cullingHeight, Mathf.Clamp(t, 0.0f, 1.0f));
        }

        Vector3 probePos = currentTargetPos + new Vector3(0, probeHeight, 0);
        ClipPlanePoints planePoints = _camera.NearClipPlanePoints(probePos + (camDir * targetDistance), clipPlaneMargin);
        ClipPlanePoints oldPoints = _camera.NearClipPlanePoints(desired_cPos + (camDir * targetDistance), clipPlaneMargin);

        float closestHit = targetDistance;
        if (CullingRayCast(desired_cPos, oldPoints, out hitInfo, targetDistance + 0.2f, cullingLayer))
            closestHit = Mathf.Min(closestHit, hitInfo.distance - 0.2f);

        if (CullingRayCast(probePos, planePoints, out hitInfo, targetDistance, cullingLayer))
            closestHit = Mathf.Min(closestHit, hitInfo.distance - 0.2f);

        targetDistance = Mathf.Clamp(closestHit, MinCameraDistance, defaultDistance);

        if (targetDistance < defaultDistance - 0.01f)
        {
            float t = targetDistance;
            t -= cullingMinDist;
            t /= cullingMinDist;
            targetHeight = Mathf.Lerp(probeHeight, orbitHeight, Mathf.Clamp(t, 0.0f, 1.0f));
        }

        distance = Mathf.SmoothDamp(
            distance,
            targetDistance,
            ref _distanceVelocity,
            CullingSmoothTime,
            Mathf.Infinity,
            Time.deltaTime);

        currentHeight = Mathf.SmoothDamp(
            currentHeight,
            targetHeight,
            ref _heightVelocity,
            CullingSmoothTime,
            Mathf.Infinity,
            Time.deltaTime);

        current_cPos = currentTargetPos + new Vector3(0, currentHeight, 0);

        var lookPoint = current_cPos + targetLookAt.forward * 2f;
        lookPoint += (targetLookAt.right * Vector3.Dot(camDir * distance, targetLookAt.right));
        targetLookAt.position = current_cPos;

        Quaternion newRot = Quaternion.Euler(mouseY, mouseX, 0);
        targetLookAt.rotation = Quaternion.Slerp(targetLookAt.rotation, newRot, smoothCameraRotation * Time.deltaTime);
        transform.position = current_cPos + (camDir * distance);

        Vector3 lookDirection = lookPoint - transform.position;
        if (lookDirection.sqrMagnitude > 0.0001f)
            transform.rotation = Quaternion.LookRotation(lookDirection);

        if (_exitBlend < 1f)
        {
            Vector3 orbitPosition = transform.position;
            Quaternion orbitRotation = transform.rotation;
            _exitBlend = Mathf.MoveTowards(_exitBlend, 1f, Time.deltaTime / transitionDuration);
            float t = SmoothStep(_exitBlend);
            transform.position = Vector3.Lerp(_exitBlendStartPos, orbitPosition, t);
            transform.rotation = Quaternion.Slerp(_exitBlendStartRot, orbitRotation, t);
        }

        movementSpeed = Vector2.zero;
    }

    private void FirstPersonCameraMovement()
    {
        var targetPos = new Vector3(
            currentTarget.position.x,
            currentTarget.position.y + offSetPlayerPivot,
            currentTarget.position.z);
        currentTargetPos = targetPos;

        Transform head = ResolveHeadBone();
        Vector3 anchor = head != null ? head.position : targetPos + Vector3.up * firstPersonFallbackEyeHeight;
        Vector3 eyePos = anchor + Vector3.up * firstPersonEyeLocalOffset.y +
                         (currentTarget != null ? currentTarget.forward : Vector3.forward) * firstPersonEyeLocalOffset.z;

        if (_firstPersonRig != null && _firstPersonRig.IsActive)
        {
            _firstPersonRig.ExportOrbit(out float yaw, out float pitch);

            if (targetLookAt != null)
            {
                targetLookAt.position = anchor;
                targetLookAt.rotation = Quaternion.Euler(pitch, yaw, 0f);
            }

            if (_exitBlend < 1f)
                ApplyExitBlendPositionOnly();
            movementSpeed = Vector2.zero;
            return;
        }

        Quaternion lookRot = Quaternion.Euler(mouseY, mouseX, 0f);
        eyePos = anchor + lookRot * firstPersonEyeLocalOffset;

        if (targetLookAt != null)
        {
            targetLookAt.position = anchor;
            targetLookAt.rotation = lookRot;
        }

        transform.SetPositionAndRotation(eyePos, lookRot);

        if (_exitBlend < 1f)
            ApplyExitBlend();

        movementSpeed = Vector2.zero;
    }

    private void ApplyExitBlend()
    {
        Vector3 fpPosition = transform.position;
        Quaternion fpRotation = transform.rotation;
        _exitBlend = Mathf.MoveTowards(_exitBlend, 1f, Time.deltaTime / transitionDuration);
        float t = SmoothStep(_exitBlend);
        transform.position = Vector3.Lerp(_exitBlendStartPos, fpPosition, t);
        transform.rotation = Quaternion.Slerp(_exitBlendStartRot, fpRotation, t);
    }

    private void ApplyExitBlendPositionOnly()
    {
        Vector3 fpPosition = transform.position;
        _exitBlend = Mathf.MoveTowards(_exitBlend, 1f, Time.deltaTime / transitionDuration);
        float t = SmoothStep(_exitBlend);
        transform.position = Vector3.Lerp(_exitBlendStartPos, fpPosition, t);
    }

    private void UpdateSlideHeightOffset()
    {
        if (_controller == null && currentTarget != null)
            _controller = currentTarget.GetComponent<vThirdPersonController>();

        float targetOffset = _controller != null && _controller.isSliding ? slideHeightOffset : 0f;
        _currentSlideHeightOffset = Mathf.SmoothDamp(
            _currentSlideHeightOffset,
            targetOffset,
            ref _slideHeightOffsetVelocity,
            Mathf.Max(0.01f, slideOffsetSmoothTime),
            Mathf.Infinity,
            Time.deltaTime);
    }

    private void UpdateCrouchHeightOffset()
    {
        if (_controller == null && currentTarget != null)
            _controller = currentTarget.GetComponent<vThirdPersonController>();

        bool crouching = _controller != null && _controller.isCrouching && !_controller.isSliding;
        float targetOffset = crouching ? crouchHeightOffset : 0f;
        _currentCrouchHeightOffset = Mathf.SmoothDamp(
            _currentCrouchHeightOffset,
            targetOffset,
            ref _crouchHeightOffsetVelocity,
            Mathf.Max(0.01f, crouchOffsetSmoothTime),
            Mathf.Infinity,
            Time.deltaTime);
    }

    private static float SmoothStep(float t)
    {
        return t * t * (3f - 2f * t);
    }

    private Transform ResolveHeadBone()
    {
        Transform root = currentTarget != null ? currentTarget : target;
        if (root == null)
            return null;

        if (_headBone != null && _headResolveRoot == root)
            return _headBone;

        _headResolveRoot = root;
        _headBone = null;

        Animator animator = root.GetComponentInChildren<Animator>();
        if (animator != null && animator.isHuman)
            _headBone = animator.GetBoneTransform(HumanBodyBones.Head);

        if (_headBone != null)
            return _headBone;

        for (int i = 0; i < HeadBoneNames.Length; i++)
        {
            _headBone = FindChildRecursive(root, HeadBoneNames[i]);
            if (_headBone != null)
                return _headBone;
        }

        return null;
    }

    private static Transform FindChildRecursive(Transform parent, string childName)
    {
        if (parent.name == childName)
            return parent;

        for (int i = 0; i < parent.childCount; i++)
        {
            Transform found = FindChildRecursive(parent.GetChild(i), childName);
            if (found != null)
                return found;
        }

        return null;
    }

    /// <summary>
    /// Custom Raycast using NearClipPlanesPoints
    /// </summary>
    /// <param name="_to"></param>
    /// <param name="from"></param>
    /// <param name="hitInfo"></param>
    /// <param name="distance"></param>
    /// <param name="cullingLayer"></param>
    /// <returns></returns>
    bool CullingRayCast(Vector3 from, ClipPlanePoints _to, out RaycastHit hitInfo, float rayDistance, LayerMask cullingLayer)
    {
        hitInfo = default;
        bool value = false;
        float closestDistance = rayDistance;

        TryRaycast(from, _to.LowerLeft - from, rayDistance, cullingLayer, ref value, ref closestDistance, ref hitInfo);
        TryRaycast(from, _to.LowerRight - from, rayDistance, cullingLayer, ref value, ref closestDistance, ref hitInfo);
        TryRaycast(from, _to.UpperLeft - from, rayDistance, cullingLayer, ref value, ref closestDistance, ref hitInfo);
        TryRaycast(from, _to.UpperRight - from, rayDistance, cullingLayer, ref value, ref closestDistance, ref hitInfo);

        return value;
    }

    static void TryRaycast(Vector3 from, Vector3 direction, float rayDistance, LayerMask cullingLayer, ref bool value, ref float closestDistance, ref RaycastHit hitInfo)
    {
        if (!Physics.Raycast(from, direction, out RaycastHit hit, rayDistance, cullingLayer))
            return;

        if (!value || hit.distance < closestDistance)
        {
            value = true;
            closestDistance = hit.distance;
            hitInfo = hit;
        }
    }
}

public interface IFirstPersonRigHost
{
    bool IsActive { get; }
    void BindPlayer(Transform player, Camera camera);
    void SetActive(bool active);
    void ApplyLookInput(float mouseX, float mouseY);
    void ExportOrbit(out float yaw, out float pitch);
    void SyncFromOrbit(float yaw, float pitch);
    void PlaceCameraAtHead(Vector3 eyeWorld);
    void ApplyViewRotation(float yaw, float pitch);
}

public static class FirstPersonRigUtility
{
    public static IFirstPersonRigHost EnsureRig(GameObject host)
    {
        if (host == null)
            return null;

        MonoBehaviour[] behaviours = host.GetComponents<MonoBehaviour>();
        for (int i = 0; i < behaviours.Length; i++)
        {
            if (behaviours[i] is IFirstPersonRigHost rig)
                return rig;
        }

        System.Type type = System.Type.GetType("Game.FirstPerson.FirstPersonSystem, Assembly-CSharp");
        if (type == null)
        {
            System.Reflection.Assembly[] assemblies = System.AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                type = assemblies[i].GetType("Game.FirstPerson.FirstPersonSystem");
                if (type != null)
                    break;
            }
        }

        if (type == null)
            return null;

        return host.AddComponent(type) as IFirstPersonRigHost;
    }
}
