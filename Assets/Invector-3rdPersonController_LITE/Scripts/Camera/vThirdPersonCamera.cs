using Motiong;
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
    private const float MinCameraDistance = 0.35f;
    private const float CullingSmoothTime = 0.12f;

    #endregion

    void Start()
    {
        Init();
    }

    public void Init()
    {
        if (target == null)
            return;

        _camera = GetComponent<Camera>();
        currentTarget = target;
        currentTargetPos = new Vector3(currentTarget.position.x, currentTarget.position.y + offSetPlayerPivot, currentTarget.position.z);

        targetLookAt = new GameObject("targetLookAt").transform;
        targetLookAt.position = currentTarget.position;
        targetLookAt.hideFlags = HideFlags.HideInHierarchy;
        targetLookAt.rotation = currentTarget.rotation;

        mouseY = currentTarget.eulerAngles.x;
        mouseX = currentTarget.eulerAngles.y;

        distance = defaultDistance;
        currentHeight = height;
    }

    void FixedUpdate()
    {
        if (target == null || targetLookAt == null)
            return;

        if (_camera != null && !_camera.enabled)
            return;

        CameraMovement();
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
        mouseY = currentTarget.rotation.eulerAngles.x;
        mouseX = currentTarget.rotation.eulerAngles.y;
        Init();
    }

    /// <summary>    
    /// Convert a point in the screen in a Ray for the world
    /// </summary>
    /// <param name="Point"></param>
    /// <returns></returns>
    public Ray ScreenPointToRay(Vector3 Point)
    {
        return this.GetComponent<Camera>().ScreenPointToRay(Point);
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

    public void RotateCamera(float x, float y)
    {
        // free rotation 
        mouseX += x * xMouseSensitivity;
        mouseY -= y * yMouseSensitivity;

        movementSpeed.x = x;
        movementSpeed.y = -y;
        if (!lockCamera)
        {
            mouseY = vExtensions.ClampAngle(mouseY, yMinLimit, yMaxLimit);
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

        float targetDistance = defaultDistance;
        float targetHeight = height;

        var camDir = (forward * targetLookAt.forward) + (rightOffset * targetLookAt.right);
        camDir = camDir.normalized;

        var targetPos = new Vector3(currentTarget.position.x, currentTarget.position.y + offSetPlayerPivot, currentTarget.position.z);
        currentTargetPos = targetPos;
        desired_cPos = targetPos + new Vector3(0, height, 0);

        RaycastHit hitInfo;
        float probeHeight = height;

        if (Physics.SphereCast(targetPos, checkHeightRadius, Vector3.up, out hitInfo, height + 0.2f, cullingLayer))
        {
            float t = hitInfo.distance - 0.2f;
            t -= height;
            t /= (cullingHeight - height);
            probeHeight = Mathf.Lerp(height, cullingHeight, Mathf.Clamp(t, 0.0f, 1.0f));
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
            targetHeight = Mathf.Lerp(probeHeight, height, Mathf.Clamp(t, 0.0f, 1.0f));
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

    private static float SmoothStep(float t)
    {
        return t * t * (3f - 2f * t);
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
