using Motiong.vCharacterController;
using UnityEngine;
using UnityEngine.Animations.Rigging;

[DefaultExecutionOrder(75)]
public class PlayerGunController : MonoBehaviour
{
    public static readonly int IsGunEquippedHash = Animator.StringToHash("IsGunEquipped");
    public static readonly int IsAimingHash = Animator.StringToHash("IsAiming");

    [Header("References")]
    [SerializeField] private GameObject holdGun;
    [SerializeField] private GameObject aimGun;
    [SerializeField] private Transform muzzle;
    [SerializeField] private GameObject crosshair;
    [SerializeField] private ResidentEvilAimCamera aimCamera;
    [SerializeField] private UpperBodyAim upperBodyAim;
    [SerializeField] private vThirdPersonCamera thirdPersonCamera;
    [SerializeField] private Camera thirdPersonCameraComponent;
    [SerializeField] private AudioListener thirdPersonAudioListener;
    [SerializeField] private Transform headAimTarget;
    [SerializeField] private Rig headAimRig;
    [SerializeField] private float headAimWeight = 0.2f;
    [SerializeField] private float headAimTargetDistance = 8f;

    [Header("Animator")]
    [SerializeField] private string gunLayerName = "Gun Layer";
    [SerializeField] private float gunLayerBlendSpeed = 8f;

    [Header("Combat")]
    [SerializeField] private float fireRateRpm = 600f;
    [SerializeField] private float damage = 15f;
    [SerializeField] private float maxRange = 120f;
    [SerializeField] private float tracerSpeed = 140f;
    [SerializeField] private LayerMask hitMask = ~0;
    [SerializeField] private float recoilShoulderImpulse = 2.5f;
    [SerializeField] private float recoilCameraKick = 0.8f;

    [Header("Effect Slots")]
    [SerializeField] private BulletTracer tracerPrefab;
    [SerializeField] private GameObject muzzleFlashPrefab;
    [SerializeField] private AudioSource gunAudioSource;
    [SerializeField] private AudioClip fireClip;
    [SerializeField] private GameObject hitPrefab;
    [SerializeField] private GameObject decalPrefab;
    [SerializeField] private float effectLifetime = 3f;
    [SerializeField] private float decalLifetime = 12f;
    [SerializeField] private float decalSurfaceOffset = 0.01f;

    private vThirdPersonController _controller;
    private vThirdPersonInput _input;
    private Animator _animator;
    private int _gunLayerIndex = -1;
    private float _targetGunLayerWeight;
    private float _nextFireTime;
    private bool _isEquipped;
    private bool _isAiming;
    private bool _strafeBeforeAim;
    private bool _lockRotationBeforeAim;

    public bool IsEquipped => _isEquipped;
    public bool IsAiming => _isAiming;
    public float MaxRange => maxRange;
    public LayerMask HitMask => hitMask;
    public bool BlockThirdPersonCameraInput => _isAiming;
    public bool SuppressSprint => _isEquipped && _isAiming;

    public Camera GetActiveCamera()
    {
        if (_isAiming && aimCamera != null && aimCamera.CameraComponent != null)
            return aimCamera.CameraComponent;

        if (thirdPersonCameraComponent != null)
            return thirdPersonCameraComponent;

        return Camera.main;
    }

    private void Awake()
    {
        _controller = GetComponent<vThirdPersonController>();
        _input = GetComponent<vThirdPersonInput>();
        _animator = GetComponent<Animator>();

        AutoAssignReferences();
        CacheGunLayerIndex();
        SetEquipped(false, true);
        SetHeadAimActive(false);
    }

    private void Update()
    {
        HandleWeaponInput();
        UpdateGunLayerWeight();
        UpdateAnimatorParameters();
        UpdateVisibility();
        HandleAutomaticFire();
    }

    private void LateUpdate()
    {
        UpdateAimingBodyRotation();
        UpdateHeadAimTarget();
    }

    private void AutoAssignReferences()
    {
        Transform rightWrist = transform.Find("guard/Armature/root ground/root hip/spine lower/spine middle/spine upper/arm right shoulder 1/arm right shoulder 2/arm right elbow/arm right wrist");
        Transform spineLower = transform.Find("guard/Armature/root ground/root hip/spine lower");
        Transform gunParent = rightWrist != null ? rightWrist : spineLower;

        if (gunParent != null)
        {
            if (holdGun == null)
                holdGun = gunParent.Find("HoldGun")?.gameObject;
            if (aimGun == null)
                aimGun = gunParent.Find("AimGun")?.gameObject;
        }

        if (muzzle == null && aimGun != null)
            muzzle = aimGun.transform.Find("muzzle");

        if (crosshair == null)
        {
            var crosshairTransform = GameObject.Find("Crossair")?.transform;
            if (crosshairTransform != null)
                crosshair = crosshairTransform.gameObject;
        }

        if (upperBodyAim == null)
            upperBodyAim = GetComponent<UpperBodyAim>();

        if (aimCamera == null)
            aimCamera = FindFirstObjectByType<ResidentEvilAimCamera>(FindObjectsInactive.Include);

        if (thirdPersonCamera == null)
            thirdPersonCamera = FindFirstObjectByType<vThirdPersonCamera>();

        if (thirdPersonCamera != null)
        {
            if (thirdPersonCameraComponent == null)
                thirdPersonCameraComponent = thirdPersonCamera.GetComponent<Camera>();
            if (thirdPersonAudioListener == null)
                thirdPersonAudioListener = thirdPersonCamera.GetComponent<AudioListener>();
        }

        if (aimCamera != null)
        {
            if (aimCamera.followTarget == null)
                aimCamera.followTarget = transform;

            if (aimCamera.spineAnchor == null)
            {
                aimCamera.spineAnchor = transform.Find("guard/Armature/root ground/root hip/spine lower/spine middle");
            }

            if (aimCamera.upperBodyAim == null && upperBodyAim != null)
                aimCamera.upperBodyAim = upperBodyAim;
        }

        if (gunAudioSource == null)
        {
            gunAudioSource = GetComponent<AudioSource>();
            if (gunAudioSource == null)
            {
                gunAudioSource = gameObject.AddComponent<AudioSource>();
                gunAudioSource.playOnAwake = false;
                gunAudioSource.spatialBlend = 1f;
            }
        }

        if (headAimTarget == null)
        {
            var targetObject = GameObject.Find("Target");
            if (targetObject != null)
                headAimTarget = targetObject.transform;
        }

        if (headAimRig == null)
            headAimRig = GetComponentInChildren<Rig>(true);
    }

    private void CacheGunLayerIndex()
    {
        if (_animator == null)
            return;

        for (int i = 0; i < _animator.layerCount; i++)
        {
            if (_animator.GetLayerName(i) == gunLayerName)
            {
                _gunLayerIndex = i;
                break;
            }
        }
    }

    private void HandleWeaponInput()
    {
        if (Input.GetKeyDown(KeyCode.Alpha1))
            SetEquipped(!_isEquipped);

        bool wantAim = _isEquipped && Input.GetMouseButton(1);
        if (wantAim != _isAiming)
            SetAiming(wantAim);
    }

    private void SetEquipped(bool equipped, bool force = false)
    {
        if (!force && _isEquipped == equipped)
            return;

        _isEquipped = equipped;
        _targetGunLayerWeight = equipped ? 1f : 0f;

        if (!equipped)
            SetAiming(false, true);
    }

    private void SetAiming(bool aiming, bool force = false)
    {
        if (!force && _isAiming == aiming)
            return;

        if (aiming && !_isEquipped)
            return;

        bool wasAiming = _isAiming;
        _isAiming = aiming;

        if (_controller != null)
        {
            if (aiming)
            {
                _strafeBeforeAim = _controller.isStrafing;
                _lockRotationBeforeAim = _controller.lockRotation;
                _controller.isStrafing = true;
                _controller.isSprinting = false;
                _controller.lockRotation = true;
            }
            else if (wasAiming)
            {
                _controller.isStrafing = _strafeBeforeAim;
                _controller.lockRotation = _lockRotationBeforeAim;
            }
        }

        SetHeadAimActive(aiming);

        if (aimCamera != null)
        {
            if (aiming)
            {
                if (thirdPersonCamera != null)
                    aimCamera.SyncYaw(thirdPersonCamera.HorizontalAngle);
                else
                    aimCamera.SyncYawFromTarget();

                aimCamera.SnapDisplayState();
            }

            aimCamera.SetAimCameraActive(aiming);
        }

        if (aiming && aimCamera != null)
            transform.rotation = Quaternion.Euler(0f, aimCamera.DisplayBodyYaw, 0f);

        if (!aiming && wasAiming && aimCamera != null)
        {
            transform.rotation = Quaternion.Euler(0f, aimCamera.Yaw, 0f);
            aimCamera.ResetUpperBodyOffset();

            if (thirdPersonCamera != null)
                thirdPersonCamera.SyncRotation(aimCamera.Yaw, aimCamera.Pitch);

            if (_input != null)
                _input.UseThirdPersonCameraAsReference();
        }

        if (upperBodyAim != null)
            upperBodyAim.SetAimingActive(aiming);

        if (thirdPersonCameraComponent != null)
            thirdPersonCameraComponent.enabled = !aiming;

        if (thirdPersonAudioListener != null)
            thirdPersonAudioListener.enabled = !aiming;
    }

    private void UpdateGunLayerWeight()
    {
        if (_animator == null || _gunLayerIndex < 0)
            return;

        float current = _animator.GetLayerWeight(_gunLayerIndex);
        float next = Mathf.MoveTowards(current, _targetGunLayerWeight, gunLayerBlendSpeed * Time.deltaTime);
        _animator.SetLayerWeight(_gunLayerIndex, next);
    }

    private void UpdateAnimatorParameters()
    {
        if (_animator == null)
            return;

        _animator.SetBool(IsGunEquippedHash, _isEquipped);
        _animator.SetBool(IsAimingHash, _isAiming);
    }

    private void UpdateVisibility()
    {
        if (holdGun != null)
            holdGun.SetActive(_isEquipped && !_isAiming);

        if (aimGun != null)
            aimGun.SetActive(_isEquipped && _isAiming);

        if (crosshair != null)
            crosshair.SetActive(_isEquipped && _isAiming);
    }

    private void UpdateAimingBodyRotation()
    {
        if (!_isAiming || aimCamera == null)
            return;

        transform.rotation = Quaternion.Euler(0f, aimCamera.DisplayBodyYaw, 0f);
    }

    private void UpdateHeadAimTarget()
    {
        if (!_isAiming || headAimTarget == null || aimCamera == null)
            return;

        Vector3 headOrigin = transform.position + Vector3.up * 1.6f;
        Vector3 pitchOnlyDirection = Quaternion.Euler(aimCamera.Pitch, aimCamera.BodyYaw, 0f) * Vector3.forward;
        headAimTarget.position = headOrigin + pitchOnlyDirection * headAimTargetDistance;
    }

    private void SetHeadAimActive(bool active)
    {
        if (headAimRig != null)
            headAimRig.weight = active ? headAimWeight : 0f;
    }

    private void HandleAutomaticFire()
    {
        if (!_isEquipped || !_isAiming || !Input.GetMouseButton(0))
            return;

        if (Time.time < _nextFireTime)
            return;

        FireShot();
        _nextFireTime = Time.time + 60f / Mathf.Max(1f, fireRateRpm);
    }

    private void FireShot()
    {
        if (muzzle == null || aimCamera == null)
            return;

        if (!TryGetAimPoint(out Vector3 aimPoint))
            aimPoint = muzzle.position + aimCamera.transform.forward * maxRange;

        Vector3 fireDirection = aimPoint - muzzle.position;
        if (fireDirection.sqrMagnitude < 0.001f)
            fireDirection = aimCamera.transform.forward;

        fireDirection.Normalize();

        if (!TryGetMuzzleHit(fireDirection, aimPoint, out RaycastHit hit))
        {
            hit.point = aimPoint;
            hit.normal = -fireDirection;
        }

        ApplyDamage(hit);
        SpawnTracer(muzzle.position, hit.point);
        SpawnMuzzleFlash();
        PlayFireSound();
        SpawnImpactEffects(hit);
        ApplyRecoil();
    }

    private void ApplyRecoil()
    {
        if (upperBodyAim != null)
            upperBodyAim.AddRecoil(recoilShoulderImpulse);

        if (aimCamera != null)
            aimCamera.AddRecoilKick(recoilCameraKick);
    }

    private bool TryGetAimPoint(out Vector3 aimPoint)
    {
        aimPoint = transform.position + transform.forward * maxRange;
        Ray cameraRay = aimCamera.GetScreenCenterRay();

        if (Physics.Raycast(cameraRay, out RaycastHit hit, maxRange, hitMask, QueryTriggerInteraction.Ignore))
        {
            if (!IsSelfCollider(hit.collider))
            {
                aimPoint = hit.point;
                return true;
            }
        }

        aimPoint = cameraRay.origin + cameraRay.direction * maxRange;
        return true;
    }

    private bool TryGetMuzzleHit(Vector3 fireDirection, Vector3 aimPoint, out RaycastHit hit)
    {
        float distance = Vector3.Distance(muzzle.position, aimPoint) + 0.05f;
        RaycastHit[] hits = Physics.RaycastAll(muzzle.position, fireDirection, distance, hitMask, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        for (int i = 0; i < hits.Length; i++)
        {
            if (IsSelfCollider(hits[i].collider))
                continue;

            hit = hits[i];
            return true;
        }

        hit = default;
        return false;
    }

    private void ApplyDamage(RaycastHit hit)
    {
        if (hit.collider == null)
            return;

        var damageable = hit.collider.GetComponentInParent<IDamageable>();
        damageable?.TakeDamage(damage, hit.point, hit.normal, gameObject);
    }

    private void SpawnTracer(Vector3 start, Vector3 end)
    {
        BulletTracer tracer;
        if (tracerPrefab != null)
        {
            tracer = Instantiate(tracerPrefab, start, Quaternion.identity);
        }
        else
        {
            GameObject tracerObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            tracerObject.name = "BulletTracer";
            tracerObject.transform.localScale = Vector3.one * 0.05f;

            Collider collider = tracerObject.GetComponent<Collider>();
            if (collider != null)
                Destroy(collider);

            Renderer renderer = tracerObject.GetComponent<Renderer>();
            if (renderer != null)
                renderer.material.color = new Color(1f, 0.85f, 0.2f, 1f);

            tracer = tracerObject.AddComponent<BulletTracer>();
        }

        tracer.Launch(start, end, tracerSpeed);
    }

    private void SpawnMuzzleFlash()
    {
        if (muzzleFlashPrefab == null || muzzle == null)
            return;

        GameObject flash = Instantiate(muzzleFlashPrefab, muzzle.position, muzzle.rotation, muzzle);
        Destroy(flash, effectLifetime);
    }

    private void PlayFireSound()
    {
        if (gunAudioSource == null || fireClip == null)
            return;

        gunAudioSource.PlayOneShot(fireClip);
    }

    private void SpawnImpactEffects(RaycastHit hit)
    {
        if (hitPrefab != null)
        {
            GameObject impact = Instantiate(hitPrefab, hit.point, Quaternion.LookRotation(hit.normal));
            Destroy(impact, effectLifetime);
        }

        if (decalPrefab != null)
        {
            Vector3 decalPosition = hit.point + hit.normal * decalSurfaceOffset;
            GameObject decal = Instantiate(decalPrefab, decalPosition, Quaternion.LookRotation(hit.normal));
            Destroy(decal, decalLifetime);
        }
    }

    private bool IsSelfCollider(Collider collider)
    {
        return collider != null && collider.transform.IsChildOf(transform);
    }
}
