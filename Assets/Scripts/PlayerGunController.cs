using System.Collections.Generic;
using Motiong.vCharacterController;
using UnityEngine;
using UnityEngine.Animations.Rigging;

[DefaultExecutionOrder(-50)]
public class PlayerGunController : MonoBehaviour
{
    public static readonly int IsGunEquippedHash = Animator.StringToHash("IsGunEquipped");
    public static readonly int IsAimingHash = Animator.StringToHash("IsAiming");
    public static readonly int WeaponAimStyleHash = Animator.StringToHash("WeaponAimStyle");

    private struct WeaponVisuals
    {
        public GameObject Hold;
        public GameObject Aim;
        public Transform Muzzle;
        public AudioSource Audio;
    }

    [Header("References")]
    [SerializeField] private WeaponInventory inventory;
    [SerializeField] private WeaponDefinition defaultWeapon;
    [SerializeField] private Transform weaponHand;
    [SerializeField] private GameObject crosshair;
    [SerializeField] private ResidentEvilAimCamera aimCamera;
    [SerializeField] private UpperBodyAim upperBodyAim;
    [SerializeField] private vThirdPersonCamera thirdPersonCamera;
    [SerializeField] private Camera thirdPersonCameraComponent;
    [SerializeField] private AudioListener thirdPersonAudioListener;
    [SerializeField] private Transform headAimTarget;
    [SerializeField] private Transform headAimOrigin;
    [SerializeField] private Rig headAimRig;
    [SerializeField] private float headAimWeight = 0.85f;
    [SerializeField] private float headAimLookDistance = 15f;
    [SerializeField] private float headAimSmoothTime = 0.05f;

    [Header("Animator")]
    [SerializeField] private string gunLayerName = "Gun Layer";
    [SerializeField] private float gunLayerBlendSpeed = 8f;

    [Header("Camera")]
    [SerializeField] private float cameraTransitionDuration = 0.25f;

    [Header("Shared Combat")]
    [SerializeField] private LayerMask hitMask = ~0;
    [SerializeField] private float effectLifetime = 3f;
    [SerializeField] private float decalLifetime = 12f;
    [SerializeField] private float decalSurfaceOffset = 0.01f;
    [SerializeField] private AudioSource gunAudioSource;

    private vThirdPersonController _controller;
    private vThirdPersonInput _input;
    private Animator _animator;
    private int _gunLayerIndex = -1;
    private float _targetGunLayerWeight;
    private float _nextFireTime;
    private bool _isEquipped;
    private bool _isAiming;
    private bool _strafeBeforeAim;
    private bool _strafeBeforeGun;
    private bool _lockRotationBeforeAim;
    private RigBuilder _rigBuilder;
    private MultiAimConstraint _headAimConstraint;
    private Vector3 _smoothedHeadAimTarget;
    private Vector3 _headAimVelocity;
    private WeaponDefinition _weapon;
    private Transform _muzzle;
    private AudioSource _activeGunAudio;
    private readonly Dictionary<WeaponDefinition, WeaponVisuals> _visualCache =
        new Dictionary<WeaponDefinition, WeaponVisuals>();

    public bool IsEquipped => _isEquipped;
    public bool IsAiming => _isAiming;
    public bool BlockThirdPersonCameraInput => _isAiming;
    public bool SuppressSprint => _isAiming;
    public bool BlockStrafeToggle => _isEquipped && !_isAiming && !UsesPistolAimStyle();
    public WeaponDefinition CurrentWeapon => _weapon;

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

        if (inventory == null)
            inventory = GetComponent<WeaponInventory>();

        AutoAssignReferences();
        _rigBuilder = GetComponent<RigBuilder>();
        if (headAimRig != null)
            _headAimConstraint = headAimRig.GetComponentInChildren<MultiAimConstraint>(true);

        CacheGunLayerIndex();
        DisableAllSceneWeaponVisuals();

        if (defaultWeapon != null)
        {
            inventory?.TryAdd(defaultWeapon);
            EquipWeapon(defaultWeapon);
        }

        SetEquipped(false, true);
        SetHeadAimActive(false);
    }

    private void Start()
    {
        if (_headAimConstraint != null)
            _headAimConstraint.enabled = false;

        if (_rigBuilder != null)
        {
            _rigBuilder.enabled = false;
            _rigBuilder.Build();
        }
    }

    private void Update()
    {
        UpdateHeadAimTarget();
        HandleWeaponInput();
        ApplyAimWalkLocomotion();
        ApplyGunHoldLocomotionRules();
        RefreshGunLayerTarget();
        UpdateGunLayerWeight();
        UpdateAnimatorParameters();
        UpdateVisibility();
        HandleFireInput();
    }

    private void AutoAssignReferences()
    {
        if (weaponHand == null)
        {
            weaponHand = FindBoneByName("CC_Base_R_Hand");
            if (weaponHand == null)
            {
                weaponHand = transform.Find(
                    "guard/Armature/root ground/root hip/spine lower/spine middle/spine upper/arm right shoulder 1/arm right shoulder 2/arm right elbow/arm right wrist");
            }

            if (weaponHand == null)
                Debug.LogError("PlayerGunController: right hand/wrist bone not found.", this);
        }

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
                aimCamera.spineAnchor = FindBoneByName("CC_Base_Spine01");
                if (aimCamera.spineAnchor == null)
                    aimCamera.spineAnchor = transform.Find("guard/Armature/root ground/root hip/spine lower/spine middle");
            }

            if (aimCamera.upperBodyAim == null && upperBodyAim != null)
                aimCamera.upperBodyAim = upperBodyAim;

            if (aimCamera.aimCrosshair == null && crosshair != null)
                aimCamera.SetAimCrosshair(crosshair.GetComponent<RectTransform>());
        }

        EnsurePersistentFireAudioSource();

        if (headAimTarget == null)
        {
            var targetObject = GameObject.Find("Target");
            if (targetObject != null)
                headAimTarget = targetObject.transform;
        }

        if (headAimRig == null)
            headAimRig = GetComponentInChildren<Rig>(true);

        if (headAimOrigin == null)
        {
            headAimOrigin = FindBoneByName("CC_Base_NeckTwist01");
            if (headAimOrigin == null)
                headAimOrigin = FindBoneByName("CC_Base_Head");
            if (headAimOrigin == null)
            {
                headAimOrigin = transform.Find(
                    "guard/Armature/root ground/root hip/spine lower/spine middle/spine upper/neck lower");
            }
        }
    }

    private Transform FindBoneByName(string exactName)
    {
        Transform[] all = GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i].name == exactName)
                return all[i];
        }

        return null;
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
        for (int slot = 1; slot <= WeaponInventory.SlotCount; slot++)
        {
            if (!Input.GetKeyDown(KeyCode.Alpha0 + slot))
                continue;

            if (_isEquipped && _weapon != null && _weapon.slot == slot)
                SetEquipped(false);
            else
                SelectSlot(slot);
        }

        bool wantAim = _isEquipped && Input.GetMouseButton(1);
        if (wantAim != _isAiming)
            SetAiming(wantAim);
    }

    public void SelectSlot(int slot)
    {
        if (inventory == null || !inventory.Has(slot))
            return;

        WeaponDefinition definition = inventory.Get(slot);
        if (definition == null)
            return;

        EquipWeapon(definition);
        SetEquipped(true);
    }

    public void EquipWeapon(WeaponDefinition definition)
    {
        if (definition == null)
            return;

        if (_weapon == definition && _visualCache.ContainsKey(definition))
        {
            ResolveMuzzle(definition);
            ResolveWeaponAudio(definition);
            return;
        }

        HideAllWeaponVisuals();
        _weapon = definition;
        EnsureWeaponVisuals(definition);
        ResolveMuzzle(definition);
        ResolveWeaponAudio(definition);
        ApplyWeaponAimStyle(definition);
        UpdateVisibility();
    }

    private void EnsureWeaponVisuals(WeaponDefinition definition)
    {
        if (_visualCache.ContainsKey(definition))
            return;

        if (weaponHand == null)
        {
            Debug.LogError("PlayerGunController: weaponHand is not assigned.", this);
            return;
        }

        string key = string.IsNullOrEmpty(definition.visualKey) ? definition.displayName : definition.visualKey;
        GameObject hold = FindSceneWeaponVisual($"HoldGun{key}");
        GameObject aim = FindSceneWeaponVisual($"AimGun{key}");

        if (hold == null && definition.holdPrefab != null)
        {
            hold = Instantiate(definition.holdPrefab, weaponHand);
            ApplyHandTransform(hold.transform, definition);
            hold.name = $"HoldGun{key}";
            hold.SetActive(false);
        }

        if (aim == null && definition.aimPrefab != null)
        {
            aim = Instantiate(definition.aimPrefab, weaponHand);
            ApplyHandTransform(aim.transform, definition);
            aim.name = $"AimGun{key}";
            aim.SetActive(false);
        }

        if (hold == null)
            Debug.LogWarning($"PlayerGunController: missing HoldGun{key} under weaponHand.", this);
        if (aim == null)
            Debug.LogWarning($"PlayerGunController: missing AimGun{key} under weaponHand.", this);

        if (hold != null)
            hold.SetActive(false);
        if (aim != null)
            aim.SetActive(false);

        Transform muzzleTransform = null;
        if (aim != null && !string.IsNullOrEmpty(definition.muzzleChildName))
            muzzleTransform = FindChildRecursive(aim.transform, definition.muzzleChildName);

        AudioSource audio = ResolveAudioSourceOnVisual(aim);
        if (audio == null)
            audio = ResolveAudioSourceOnVisual(hold);

        _visualCache[definition] = new WeaponVisuals
        {
            Hold = hold,
            Aim = aim,
            Muzzle = muzzleTransform,
            Audio = audio
        };
    }

    private static AudioSource ResolveAudioSourceOnVisual(GameObject visual)
    {
        if (visual == null)
            return null;

        Transform audioTransform = FindChildRecursive(visual.transform, "gunAudio");
        AudioSource source = audioTransform != null
            ? audioTransform.GetComponent<AudioSource>()
            : visual.GetComponentInChildren<AudioSource>(true);

        if (source != null)
            source.playOnAwake = false;

        return source;
    }

    private GameObject FindSceneWeaponVisual(string objectName)
    {
        if (string.IsNullOrEmpty(objectName))
            return null;

        Transform searchRoot = weaponHand != null ? weaponHand : transform;
        Transform[] children = searchRoot.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < children.Length; i++)
        {
            if (children[i].name == objectName)
                return children[i].gameObject;
        }

        // Fallback: search whole player hierarchy (inactive included).
        if (searchRoot != transform)
        {
            children = transform.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < children.Length; i++)
            {
                if (children[i].name == objectName)
                    return children[i].gameObject;
            }
        }

        return null;
    }

    private void DisableAllSceneWeaponVisuals()
    {
        if (weaponHand == null)
            return;

        Transform[] children = weaponHand.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < children.Length; i++)
        {
            Transform child = children[i];
            if (child == weaponHand)
                continue;

            string name = child.name;
            if (name.StartsWith("HoldGun") || name.StartsWith("AimGun"))
                child.gameObject.SetActive(false);
        }
    }

    private static void ApplyHandTransform(Transform target, WeaponDefinition definition)
    {
        target.localPosition = definition.handLocalPosition;
        target.localRotation = Quaternion.Euler(definition.handLocalEuler);
        target.localScale = definition.handLocalScale;
    }

    private void ApplyWeaponAimStyle(WeaponDefinition definition)
    {
        if (_animator == null)
            return;

        int style = definition != null ? definition.weaponAimStyle : 0;
        _animator.SetInteger(WeaponAimStyleHash, style);
    }

    private void ResolveMuzzle(WeaponDefinition definition)
    {
        _muzzle = null;
        if (definition == null)
            return;

        if (!_visualCache.TryGetValue(definition, out WeaponVisuals visuals))
            return;

        if (visuals.Muzzle != null)
        {
            _muzzle = visuals.Muzzle;
            return;
        }

        if (visuals.Aim != null && !string.IsNullOrEmpty(definition.muzzleChildName))
            _muzzle = FindChildRecursive(visuals.Aim.transform, definition.muzzleChildName);
    }

    private void ResolveWeaponAudio(WeaponDefinition definition)
    {
        _activeGunAudio = null;
        if (definition == null)
            return;

        if (_visualCache.TryGetValue(definition, out WeaponVisuals visuals) && visuals.Audio != null)
            _activeGunAudio = visuals.Audio;
        else
            _activeGunAudio = gunAudioSource;
    }

    private void HideAllWeaponVisuals()
    {
        foreach (KeyValuePair<WeaponDefinition, WeaponVisuals> pair in _visualCache)
        {
            if (pair.Value.Hold != null)
                pair.Value.Hold.SetActive(false);
            if (pair.Value.Aim != null)
                pair.Value.Aim.SetActive(false);
        }
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

    private void SetEquipped(bool equipped, bool force = false)
    {
        if (!force && _isEquipped == equipped)
            return;

        if (equipped && _weapon == null)
            return;

        _isEquipped = equipped;

        if (_controller != null && equipped)
            _strafeBeforeGun = _controller.isStrafing;
        else if (_controller != null && !equipped && !_isAiming)
            _controller.isStrafing = _strafeBeforeGun;

        RefreshGunLayerTarget();

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
                _controller.lockRotation = _lockRotationBeforeAim;
            }
        }

        RefreshGunLayerTarget();
        SetHeadAimActive(aiming);

        if (aimCamera != null)
        {
            if (aiming)
            {
                if (thirdPersonCamera != null)
                    aimCamera.SyncYaw(thirdPersonCamera.HorizontalAngle, thirdPersonCamera.VerticalAngle);
                else
                    aimCamera.SyncYawFromTarget();

                aimCamera.SetAimCameraActive(true);
                aimCamera.BeginEnterBlend(thirdPersonCameraComponent, cameraTransitionDuration);
            }
            else
            {
                aimCamera.SetAimCameraActive(false);
            }
        }

        if (aiming && aimCamera != null)
            aimCamera.SyncFollowTargetToLookYaw();

        if (aiming && headAimTarget != null)
        {
            Transform lookOrigin = headAimOrigin != null
                ? headAimOrigin
                : (_muzzle != null ? _muzzle : transform);
            Vector3 initialHeadTarget = lookOrigin.position
                + GetGunPointingDirection().normalized * Mathf.Max(1f, headAimLookDistance);
            _smoothedHeadAimTarget = initialHeadTarget;
            _headAimVelocity = Vector3.zero;
            headAimTarget.position = initialHeadTarget;
        }

        if (!aiming && wasAiming && thirdPersonCamera != null && aimCamera != null)
        {
            thirdPersonCamera.SyncRotation(aimCamera.Yaw, aimCamera.Pitch);
            thirdPersonCamera.BeginExitBlend(
                aimCamera.transform.position,
                aimCamera.transform.rotation,
                cameraTransitionDuration);
        }

        if (upperBodyAim != null)
            upperBodyAim.SetAimingActive(aiming);

        if (thirdPersonCameraComponent != null)
            thirdPersonCameraComponent.enabled = !aiming;

        if (thirdPersonAudioListener != null)
            thirdPersonAudioListener.enabled = !aiming;
    }

    private void ApplyAimWalkLocomotion()
    {
        if (_controller == null)
            return;

        _controller.forceWalkLocomotion = _isAiming;
        _controller.suppressUpperBodyLocomotion = _isAiming;

        if (_isAiming)
            _controller.isSprinting = false;
    }

    private void ApplyGunHoldLocomotionRules()
    {
        // Pistol uses normal locomotion while held; rifle holding forces forward run style.
        if (_controller == null || !_isEquipped || _isAiming || _controller.isSliding)
            return;

        if (UsesPistolAimStyle())
            return;

        _controller.isStrafing = false;
        _controller.lockRotation = false;
    }

    private void RefreshGunLayerTarget()
    {
        // Slide owns full-body pose — drop Gun Layer. Crouch keeps Gun Layer (arms/head)
        // so hold/aim coexist with Base Layer crouch legs.
        if (_controller != null && _controller.isSliding)
        {
            _targetGunLayerWeight = 0f;
            return;
        }

        if (!_isEquipped)
        {
            _targetGunLayerWeight = 0f;
            return;
        }

        // Pistol: no GunHoldingIdle/Run/Sprint — only blend Gun Layer on while aiming.
        if (UsesPistolAimStyle())
        {
            _targetGunLayerWeight = _isAiming ? 1f : 0f;
            return;
        }

        _targetGunLayerWeight = 1f;
    }

    private bool UsesPistolAimStyle()
    {
        return _weapon != null && _weapon.weaponAimStyle == 1;
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
        _animator.SetInteger(WeaponAimStyleHash, _weapon != null ? _weapon.weaponAimStyle : 0);
    }

    private void UpdateVisibility()
    {
        bool showHold = _isEquipped && !_isAiming;
        bool showAim = _isEquipped && _isAiming;

        foreach (KeyValuePair<WeaponDefinition, WeaponVisuals> pair in _visualCache)
        {
            bool isCurrent = pair.Key == _weapon;
            SetActiveIfChanged(pair.Value.Hold, isCurrent && showHold);
            SetActiveIfChanged(pair.Value.Aim, isCurrent && showAim);
        }

        if (crosshair != null)
        {
            bool showCrosshair = showAim && (aimCamera == null || !aimCamera.IsClippingPlayer);
            SetActiveIfChanged(crosshair, showCrosshair);
        }
    }

    private static void SetActiveIfChanged(GameObject target, bool active)
    {
        if (target != null && target.activeSelf != active)
            target.SetActive(active);
    }

    private void UpdateHeadAimTarget()
    {
        if (!_isAiming || headAimTarget == null)
            return;

        Vector3 lookDirection = GetGunPointingDirection();
        if (lookDirection.sqrMagnitude < 0.0001f)
            return;

        Transform lookOrigin = headAimOrigin != null
            ? headAimOrigin
            : (_muzzle != null ? _muzzle : transform);

        Vector3 aimPoint = lookOrigin.position + lookDirection.normalized * Mathf.Max(1f, headAimLookDistance);

        if (headAimSmoothTime > 0f)
        {
            _smoothedHeadAimTarget = Vector3.SmoothDamp(
                _smoothedHeadAimTarget,
                aimPoint,
                ref _headAimVelocity,
                headAimSmoothTime);
            headAimTarget.position = _smoothedHeadAimTarget;
        }
        else
        {
            headAimTarget.position = aimPoint;
            _smoothedHeadAimTarget = aimPoint;
        }
    }

    private Vector3 GetGunPointingDirection()
    {
        if (aimCamera != null && aimCamera.IsActive)
            return aimCamera.GetAimDirection();

        if (_muzzle != null)
            return _muzzle.forward;

        return transform.forward;
    }

    private void SetHeadAimActive(bool active)
    {
        if (_headAimConstraint != null)
            _headAimConstraint.enabled = active;

        if (headAimRig != null)
            headAimRig.weight = active ? headAimWeight : 0f;

        if (_rigBuilder != null)
        {
            _rigBuilder.enabled = active;
            if (active)
                _rigBuilder.Build();
        }
    }

    private void HandleFireInput()
    {
        if (!_isEquipped || !_isAiming || _weapon == null)
            return;

        bool firePressed = _weapon.isAutomatic
            ? Input.GetMouseButton(0)
            : Input.GetMouseButtonDown(0);

        if (!firePressed)
            return;

        if (Time.time < _nextFireTime)
            return;

        FireShot();
        _nextFireTime = Time.time + 60f / Mathf.Max(1f, _weapon.fireRateRpm);
    }

    private void FireShot()
    {
        if (_muzzle == null || aimCamera == null || _weapon == null)
            return;

        float range = Mathf.Max(1f, _weapon.maxRange);
        if (!TryGetGunAimPoint(out Vector3 aimPoint))
            aimPoint = _muzzle.position + aimCamera.GetAimDirection() * range;

        Vector3 baseDirection = aimPoint - _muzzle.position;
        if (baseDirection.sqrMagnitude < 0.001f)
            baseDirection = aimCamera.GetAimDirection();
        baseDirection.Normalize();

        int pellets = Mathf.Max(1, _weapon.pelletsPerShot);
        for (int i = 0; i < pellets; i++)
        {
            Vector3 fireDirection = ApplySpread(baseDirection, _weapon.spreadDegrees);
            Vector3 pelletAimPoint = _muzzle.position + fireDirection * range;

            if (!TryGetMuzzleHit(fireDirection, pelletAimPoint, out RaycastHit hit))
            {
                hit.point = pelletAimPoint;
                hit.normal = -fireDirection;
            }

            ApplyDamage(hit);
            SpawnTracer(_muzzle.position, hit.point);
            SpawnImpactEffects(hit);
        }

        SpawnMuzzleFlash();
        PlayFireSound();
        ApplyRecoil();
    }

    private static Vector3 ApplySpread(Vector3 direction, float spreadDegrees)
    {
        if (spreadDegrees <= 0.0001f)
            return direction.normalized;

        Vector3 forward = direction.normalized;
        Vector3 right = Vector3.Cross(forward, Vector3.up);
        if (right.sqrMagnitude < 0.0001f)
            right = Vector3.Cross(forward, Vector3.right);
        right.Normalize();
        Vector3 up = Vector3.Cross(right, forward).normalized;

        float yaw = Random.Range(-spreadDegrees, spreadDegrees);
        float pitch = Random.Range(-spreadDegrees, spreadDegrees);
        return (Quaternion.AngleAxis(yaw, up) * Quaternion.AngleAxis(pitch, right) * forward).normalized;
    }

    private void ApplyRecoil()
    {
        if (_weapon == null)
            return;

        if (upperBodyAim != null)
            upperBodyAim.AddRecoil(_weapon.recoilShoulderImpulse);

        if (aimCamera != null)
            aimCamera.AddRecoilKick(_weapon.recoilCameraKick);
    }

    private bool TryGetGunAimPoint(out Vector3 aimPoint)
    {
        aimPoint = default;
        if (_muzzle == null || aimCamera == null || _weapon == null)
            return false;

        float range = Mathf.Max(1f, _weapon.maxRange);
        if (!TryGetAimPoint(out Vector3 cameraAimPoint))
            cameraAimPoint = _muzzle.position + aimCamera.GetAimDirection() * range;

        Vector3 fireDirection = cameraAimPoint - _muzzle.position;
        if (fireDirection.sqrMagnitude < 0.001f)
            fireDirection = aimCamera.GetAimDirection();
        else
            fireDirection.Normalize();

        if (TryGetMuzzleHit(fireDirection, cameraAimPoint, out RaycastHit hit))
        {
            aimPoint = hit.point;
            return true;
        }

        aimPoint = _muzzle.position + fireDirection * range;
        return true;
    }

    private bool TryGetAimPoint(out Vector3 aimPoint)
    {
        float range = _weapon != null ? Mathf.Max(1f, _weapon.maxRange) : 120f;
        aimPoint = transform.position + transform.forward * range;

        if (aimCamera == null)
            return false;

        Ray cameraRay = aimCamera.GetAimRay();

        if (Physics.Raycast(cameraRay, out RaycastHit hit, range, hitMask, QueryTriggerInteraction.Ignore))
        {
            if (!IsSelfCollider(hit.collider))
            {
                aimPoint = hit.point;
                return true;
            }
        }

        aimPoint = cameraRay.origin + cameraRay.direction * range;
        return true;
    }

    private bool TryGetMuzzleHit(Vector3 fireDirection, Vector3 aimPoint, out RaycastHit hit)
    {
        float distance = Vector3.Distance(_muzzle.position, aimPoint) + 0.05f;
        RaycastHit[] hits = Physics.RaycastAll(
            _muzzle.position,
            fireDirection,
            distance,
            hitMask,
            QueryTriggerInteraction.Ignore);
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
        if (hit.collider == null || _weapon == null)
            return;

        var damageable = hit.collider.GetComponentInParent<IDamageable>();
        damageable?.TakeDamage(_weapon.damage, hit.point, hit.normal, gameObject);
    }

    private void SpawnTracer(Vector3 start, Vector3 end)
    {
        GameObject prefab = _weapon != null ? _weapon.tracerPrefab : null;
        float speed = _weapon != null ? _weapon.tracerSpeed : 140f;

        GameObject tracerObject;
        if (prefab != null)
        {
            tracerObject = Instantiate(prefab, start, Quaternion.identity);
            DisableTracerColliders(tracerObject);
        }
        else
        {
            tracerObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            tracerObject.name = "BulletTracer";
            tracerObject.transform.localScale = Vector3.one * 0.05f;

            Collider collider = tracerObject.GetComponent<Collider>();
            if (collider != null)
                Destroy(collider);

            Renderer renderer = tracerObject.GetComponent<Renderer>();
            if (renderer != null)
                renderer.material.color = new Color(1f, 0.85f, 0.2f, 1f);
        }

        BulletTracer tracer = tracerObject.GetComponent<BulletTracer>();
        if (tracer == null)
            tracer = tracerObject.AddComponent<BulletTracer>();

        tracer.Launch(start, end, speed);
    }

    private static void DisableTracerColliders(GameObject tracerObject)
    {
        Collider[] colliders = tracerObject.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
            colliders[i].enabled = false;
    }

    private void SpawnMuzzleFlash()
    {
        if (_weapon == null || _weapon.muzzleFlashPrefab == null || _muzzle == null)
            return;

        // Spawn in world space (not parented) so aim-gun enable/disable cannot re-trigger playOnAwake.
        GameObject flash = Instantiate(
            _weapon.muzzleFlashPrefab,
            _muzzle.position,
            _muzzle.rotation);
        flash.transform.localScale = _weapon.muzzleFlashPrefab.transform.localScale * _weapon.muzzleFlashScale;

        float lifetime = effectLifetime;
        ParticleSystem particles = flash.GetComponentInChildren<ParticleSystem>(true);
        if (particles != null)
            lifetime = Mathf.Max(0.15f, particles.main.duration + particles.main.startLifetime.constantMax);

        Destroy(flash, lifetime);
    }

    private void PlayFireSound()
    {
        if (_weapon == null)
            return;

        // Read clip from the current gun's AudioSource / definition, but always play on the
        // persistent player source so exiting aim (disabling AimGun*) cannot cut the sound short.
        AudioClip clip = _weapon.fireClip;
        if (clip == null && _activeGunAudio != null)
            clip = _activeGunAudio.clip;
        if (clip == null)
            return;

        AudioSource source = EnsurePersistentFireAudioSource();
        if (source == null)
            return;

        if (_activeGunAudio != null)
        {
            source.volume = _activeGunAudio.volume;
            source.pitch = _activeGunAudio.pitch;
            source.spatialBlend = _activeGunAudio.spatialBlend;
        }

        source.PlayOneShot(clip);
    }

    /// <summary>
    /// Fire SFX must live on an always-active player child. Sources under AimGun*/HoldGun*
    /// are disabled when leaving aim and Unity stops PlayOneShot mid-clip.
    /// </summary>
    private AudioSource EnsurePersistentFireAudioSource()
    {
        if (IsPersistentFireAudioSource(gunAudioSource))
            return gunAudioSource;

        const string childName = "gunFireAudio";
        Transform existing = transform.Find(childName);
        GameObject host;
        if (existing != null)
        {
            host = existing.gameObject;
        }
        else
        {
            host = new GameObject(childName);
            host.transform.SetParent(transform, false);
            host.transform.localPosition = Vector3.zero;
            host.transform.localRotation = Quaternion.identity;
        }

        AudioSource source = host.GetComponent<AudioSource>();
        if (source == null)
            source = host.AddComponent<AudioSource>();

        source.playOnAwake = false;
        source.loop = false;
        if (source.spatialBlend <= 0f)
            source.spatialBlend = 1f;

        gunAudioSource = source;
        return source;
    }

    private bool IsPersistentFireAudioSource(AudioSource source)
    {
        if (source == null)
            return false;

        Transform t = source.transform;
        if (t != transform && !t.IsChildOf(transform))
            return false;

        while (t != null && t != transform)
        {
            string n = t.name;
            if (n.StartsWith("AimGun", System.StringComparison.Ordinal) ||
                n.StartsWith("HoldGun", System.StringComparison.Ordinal))
                return false;
            t = t.parent;
        }

        return true;
    }

    private void SpawnImpactEffects(RaycastHit hit)
    {
        if (_weapon == null)
            return;

        if (_weapon.hitPrefab != null)
        {
            GameObject impact = Instantiate(_weapon.hitPrefab, hit.point, Quaternion.LookRotation(hit.normal));
            Destroy(impact, effectLifetime);
        }

        if (_weapon.decalPrefab != null)
        {
            Vector3 decalPosition = hit.point + hit.normal * decalSurfaceOffset;
            GameObject decal = Instantiate(_weapon.decalPrefab, decalPosition, Quaternion.LookRotation(hit.normal));
            Destroy(decal, decalLifetime);
        }
    }

    private bool IsSelfCollider(Collider collider)
    {
        return collider != null && collider.transform.IsChildOf(transform);
    }
}
