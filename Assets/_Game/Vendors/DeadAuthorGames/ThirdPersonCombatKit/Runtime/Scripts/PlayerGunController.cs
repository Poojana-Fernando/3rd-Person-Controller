using System.Collections.Generic;
using Motiong.vCharacterController;
using UnityEngine;
using UnityEngine.Animations.Rigging;
using UnityEngine.UI;


namespace DeadAuthorGames.ThirdPersonCombat
{
    [DefaultExecutionOrder(-50)]
    public class PlayerGunController : MonoBehaviour
    {
        public static readonly int IsGunEquippedHash = Animator.StringToHash("IsGunEquipped");
        public static readonly int IsAimingHash = Animator.StringToHash("IsAiming");
        public static readonly int WeaponAimStyleHash = Animator.StringToHash("WeaponAimStyle");
        public static readonly int IsReloadingHash = Animator.StringToHash("IsReloading");

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
        [SerializeField] public ResidentEvilAimCamera aimCamera;
        [SerializeField] private UpperBodyAim upperBodyAim;
        [SerializeField] public vThirdPersonCamera thirdPersonCamera;
        [SerializeField] private Camera thirdPersonCameraComponent;
        [SerializeField] private AudioListener thirdPersonAudioListener;
        [SerializeField] private Transform headAimTarget;

        [Tooltip("When true, skip Find-based camera/player wiring (set by NetworkPlayerSetup).")]
        public bool skipAutoAssign;

        [System.NonSerialized] public Game.Net.NetworkCombat networkCombat;
        [System.NonSerialized] public Game.Net.NetworkWeaponState networkWeaponState;
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
        private ParkourController _parkourController;
        private Animator _animator;
        private int _gunLayerIndex = -1;
        private float _targetGunLayerWeight;
        private float _nextFireTime;
        private bool _isEquipped;
        private bool _isAiming;
        private bool _isReloading;
        private float _reloadEndTime;
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
        private Image _crosshairImage;
        private RectTransform _crosshairRect;
        private Sprite _defaultCrosshairSprite;
        private Vector2 _defaultCrosshairSize;
        private bool _hasCachedDefaultCrosshair;

        /// <summary>While true, weapons cannot be drawn (e.g. driving a vehicle).</summary>
        public bool EquipBlocked { get; set; }
        public bool IsEquipped => _isEquipped;
        public bool IsAiming => _isAiming;
        public bool IsReloading => _isReloading;
        public bool BlockThirdPersonCameraInput => _isAiming && !UsesFirstPersonAimCamera();
        public bool SuppressSprint => _isAiming && !UsesFirstPersonAimCamera();
        public event System.Action<bool> OnLocalShotFired;
        public bool BlockStrafeToggle => _isEquipped && !_isAiming && !UsesPistolAimStyle();
        public WeaponDefinition CurrentWeapon => _weapon;
        public Transform MuzzleTransform => _muzzle != null ? _muzzle : weaponHand;

        /// <summary>Apply replicated equip/aim state on remote (non-owner) peers.</summary>
        public void ApplyRemoteState(int slot, bool equipped, bool aiming)
        {
            skipAutoAssign = true;

            if (!equipped || slot <= 0)
            {
                SetEquipped(false, true);
                UpdateVisibility();
                UpdateAnimatorParameters();
                return;
            }

            if (inventory != null && inventory.Has(slot))
            {
                WeaponDefinition def = inventory.Get(slot);
                if (def != null)
                    EquipWeapon(def);
            }

            SetEquipped(true, true);
            SetAiming(aiming, true);

            // Remotes never drive local cameras — keep aim gun visible without enabling aim cam.
            if (aimCamera != null)
                aimCamera.SetAimCameraActive(false);
            if (thirdPersonCameraComponent != null)
                thirdPersonCameraComponent.enabled = false;

            UpdateVisibility();
            UpdateAnimatorParameters();
            RefreshGunLayerTarget();
            if (_animator != null && _gunLayerIndex >= 0)
                _animator.SetLayerWeight(_gunLayerIndex, _targetGunLayerWeight);
        }

        public bool TryGetNetworkAimPoint(out Vector3 aimPoint)
        {
            return TryGetGunAimPoint(out aimPoint);
        }

        /// <summary>Remote peer cue for another player's shot (muzzle, SFX, tracers, impacts).</summary>
        public void PlayRemoteShot(
            Vector3 origin,
            Vector3[] endPoints,
            Vector3[] normals,
            byte[] hitKinds)
        {
            if (_weapon == null)
                return;

            Transform muzzle = MuzzleTransform;
            if (muzzle != null)
                SpawnMuzzleFlashAt(muzzle);
            else
                SpawnMuzzleFlashAtWorld(origin, Quaternion.LookRotation(
                    endPoints != null && endPoints.Length > 0
                        ? (endPoints[0] - origin).normalized
                        : transform.forward));

            PlayFireSound();

            if (endPoints == null)
                return;

            for (int i = 0; i < endPoints.Length; i++)
            {
                Vector3 end = endPoints[i];
                Vector3 normal = normals != null && i < normals.Length ? normals[i] : Vector3.up;
                SpawnTracer(origin, end);

                byte kind = hitKinds != null && i < hitKinds.Length ? hitKinds[i] : (byte)0;
                if (kind == 1)
                {
                    // Flesh / dismemberment — blood VFX spawned by NetworkCombat / EnemyDismemberment.
                    continue;
                }

                if (kind == 0)
                    continue;

                if (_weapon.hitPrefab != null)
                {
                    GameObject impact = Instantiate(_weapon.hitPrefab, end, Quaternion.LookRotation(normal));
                    Destroy(impact, effectLifetime);
                }

                if (_weapon.decalPrefab != null)
                {
                    Vector3 decalPosition = end + normal * decalSurfaceOffset;
                    GameObject decal = Instantiate(_weapon.decalPrefab, decalPosition, Quaternion.LookRotation(normal));
                    Destroy(decal, decalLifetime);
                }
            }
        }

        public void PlayRemoteReloadSound()
        {
            PlayReloadSound();
        }

        private void SpawnMuzzleFlashAtWorld(Vector3 position, Quaternion rotation)
        {
            if (_weapon == null || _weapon.muzzleFlashPrefab == null)
                return;

            GameObject flash = Instantiate(_weapon.muzzleFlashPrefab, position, rotation);
            flash.transform.localScale = _weapon.muzzleFlashPrefab.transform.localScale * _weapon.muzzleFlashScale;
            float lifetime = effectLifetime;
            ParticleSystem particles = flash.GetComponentInChildren<ParticleSystem>(true);
            if (particles != null)
                lifetime = Mathf.Max(0.15f, particles.main.duration + particles.main.startLifetime.constantMax);
            Destroy(flash, lifetime);
        }

        public Camera GetActiveCamera()
        {
            if (_isAiming && UsesFirstPersonAimCamera() && thirdPersonCameraComponent != null)
                return thirdPersonCameraComponent;

            if (_isAiming && aimCamera != null && aimCamera.CameraComponent != null)
                return aimCamera.CameraComponent;

            if (thirdPersonCameraComponent != null)
                return thirdPersonCameraComponent;

            return Camera.main;
        }

        public void RefreshAimCameraMode()
        {
            if (!_isAiming)
                return;

            ApplyAimCameraBinding();
        }

        public void RefreshFirstPersonWeaponView()
        {
            bool wantAim = ComputeWantAim();
            SetAiming(wantAim, true);
        }

        private bool IsFirstPersonGunViewActive()
        {
            return _isEquipped && UsesFirstPersonAimCamera();
        }

        private bool IsLocomotionBlockingFirstPersonAim()
        {
            return _controller != null &&
                   (_controller.isSprinting || _controller.isSliding || IsParkourActive());
        }

        private bool ComputeWantAim()
        {
            if (!_isEquipped)
                return false;

            if (IsFirstPersonGunViewActive())
            {
                if (IsLocomotionBlockingFirstPersonAim())
                    return false;
                return true;
            }

            return Input.GetMouseButton(1);
        }

        private bool UsesFirstPersonAimCamera()
        {
            return thirdPersonCamera != null && thirdPersonCamera.IsFirstPerson;
        }

        private void ApplyAimCameraBinding()
        {
            if (UsesFirstPersonAimCamera())
            {
                if (aimCamera != null)
                    aimCamera.SetAimCameraActive(false);

                if (thirdPersonCameraComponent != null)
                    thirdPersonCameraComponent.enabled = true;

                if (thirdPersonAudioListener != null)
                    thirdPersonAudioListener.enabled = true;
                return;
            }

            if (aimCamera == null)
                return;

            if (thirdPersonCamera != null)
                aimCamera.SyncYaw(thirdPersonCamera.HorizontalAngle, thirdPersonCamera.VerticalAngle);
            else
                aimCamera.SyncYawFromTarget();

            aimCamera.SetAimCameraActive(true);
            aimCamera.BeginEnterBlend(thirdPersonCameraComponent, cameraTransitionDuration);
            aimCamera.SyncFollowTargetToLookYaw();

            if (thirdPersonCameraComponent != null)
                thirdPersonCameraComponent.enabled = false;

            if (thirdPersonAudioListener != null)
                thirdPersonAudioListener.enabled = false;
        }

        public void BindWeaponHand(Transform hand)
        {
            if (hand != null)
                weaponHand = hand;
        }

        public void BindThirdPersonCamera(vThirdPersonCamera camera, Camera cameraComponent = null)
        {
            if (camera != null)
                thirdPersonCamera = camera;

            if (cameraComponent != null)
                thirdPersonCameraComponent = cameraComponent;
            else if (thirdPersonCamera != null)
            {
                thirdPersonCameraComponent = thirdPersonCamera.GetComponent<Camera>();
                if (thirdPersonCameraComponent == null)
                    thirdPersonCameraComponent = thirdPersonCamera.GetComponentInChildren<Camera>(true);
            }

            if (thirdPersonCameraComponent != null)
            {
                AudioListener listener = thirdPersonCameraComponent.GetComponent<AudioListener>();
                if (listener != null)
                    thirdPersonAudioListener = listener;
            }
        }

        public void BindHeadAimOrigin(Transform origin)
        {
            if (origin != null)
                headAimOrigin = origin;
        }

        public int AttachWeaponsToHand(Transform hand)
        {
            BindWeaponHand(hand);
            if (weaponHand == null)
                return 0;

            if (inventory == null)
                inventory = GetComponent<WeaponInventory>();

            _visualCache.Clear();
            try
            {
                ReparentExistingWeaponVisuals(weaponHand);
            }
            catch (System.Exception exception)
            {
                Debug.LogWarning($"PlayerGunController: could not reparent existing guns ({exception.Message}). Spawning from weapon prefabs.", this);
            }

            var weapons = new List<WeaponDefinition>();
            inventory?.CollectWeapons(weapons);
            if (defaultWeapon != null && !weapons.Contains(defaultWeapon))
                weapons.Add(defaultWeapon);

            for (int i = 0; i < weapons.Count; i++)
                EnsureWeaponVisuals(weapons[i], false);

            ShowDefaultHoldVisualInEditor();
            return CountWeaponVisualsOnHand();
        }

        private void Awake()
        {
            _controller = GetComponent<vThirdPersonController>();
            _input = GetComponent<vThirdPersonInput>();
            _parkourController = GetComponent<ParkourController>();
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
            HandleReloadInput();
            UpdateAnimatorParameters();
            UpdateVisibility();
            UpdateReload();
            HandleFireInput();
        }

        private void AutoAssignReferences()
        {
            if (skipAutoAssign)
                return;

            if (weaponHand == null)
            {
                weaponHand = FindBoneByName("CC_Base_R_Hand");
                if (weaponHand == null)
                    weaponHand = FindBoneByName("r hand");
                if (weaponHand == null)
                    weaponHand = FindBoneByName("hand_R");
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
                        aimCamera.spineAnchor = FindBoneByName("spine2");
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
                    headAimOrigin = FindBoneByName("neck");
                if (headAimOrigin == null)
                    headAimOrigin = FindBoneByName("CC_Base_Head");
                if (headAimOrigin == null)
                    headAimOrigin = FindBoneByName("head");
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
            if (EquipBlocked)
                return;

            for (int slot = 1; slot <= WeaponInventory.SlotCount; slot++)
            {
                if (!Input.GetKeyDown(KeyCode.Alpha0 + slot))
                    continue;

                if (_isEquipped && _weapon != null && _weapon.slot == slot)
                    SetEquipped(false);
                else
                    SelectSlot(slot);
            }

            bool wantAim = ComputeWantAim();
            if (wantAim != _isAiming)
                SetAiming(wantAim);
        }

        /// <summary>
        /// Unequips immediately (works while this component is disabled) and returns the slot that was held, or 0.
        /// </summary>
        public int Holster()
        {
            int slot = _isEquipped && _weapon != null ? _weapon.slot : 0;

            SetEquipped(false, true);
            ApplyAimWalkLocomotion();
            UpdateVisibility();
            UpdateAnimatorParameters();
            RefreshGunLayerTarget();
            if (_animator != null && _gunLayerIndex >= 0)
                _animator.SetLayerWeight(_gunLayerIndex, _targetGunLayerWeight);

            return slot;
        }

        public void SelectSlot(int slot)
        {
            if (EquipBlocked)
                return;

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

            _isReloading = false;

            if (_weapon == definition && _visualCache.ContainsKey(definition))
            {
                ResolveMuzzle(definition);
                ResolveWeaponAudio(definition);
                ApplyWeaponCrosshair(definition);
                return;
            }

            HideAllWeaponVisuals();
            _weapon = definition;
            EnsureWeaponVisuals(definition);
            ResolveMuzzle(definition);
            ResolveWeaponAudio(definition);
            ApplyWeaponAimStyle(definition);
            ApplyWeaponCrosshair(definition);
            UpdateVisibility();
        }

        private void EnsureWeaponVisuals(WeaponDefinition definition, bool searchWholePlayer = true)
        {
            if (_visualCache.ContainsKey(definition))
                return;

            if (weaponHand == null)
            {
                Debug.LogError("PlayerGunController: weaponHand is not assigned.", this);
                return;
            }

            string key = string.IsNullOrEmpty(definition.visualKey) ? definition.displayName : definition.visualKey;
            GameObject hold = FindSceneWeaponVisual($"HoldGun{key}", searchWholePlayer);
            GameObject aim = FindSceneWeaponVisual($"AimGun{key}", searchWholePlayer);

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
            {
                muzzleTransform = FindChildRecursive(aim.transform, definition.muzzleChildName);
                if (muzzleTransform == null)
                {
                    GameObject muzzleGo = new GameObject(definition.muzzleChildName);
                    muzzleGo.transform.SetParent(aim.transform, false);
                    muzzleGo.transform.localPosition = new Vector3(0f, 0f, 0.35f);
                    muzzleTransform = muzzleGo.transform;
                }
            }

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

        private GameObject FindSceneWeaponVisual(string objectName, bool searchWholePlayer = true)
        {
            if (string.IsNullOrEmpty(objectName))
                return null;

            Transform searchRoot = weaponHand != null ? weaponHand : transform;
            Transform[] children = searchRoot.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < children.Length; i++)
            {
                if (string.Equals(children[i].name, objectName, System.StringComparison.OrdinalIgnoreCase))
                    return children[i].gameObject;
            }

            if (!searchWholePlayer || searchRoot == transform)
                return null;

            children = transform.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < children.Length; i++)
            {
                if (string.Equals(children[i].name, objectName, System.StringComparison.OrdinalIgnoreCase))
                    return children[i].gameObject;
            }

            return null;
        }

        private int ReparentExistingWeaponVisuals(Transform hand)
        {
            int count = 0;
            Transform[] all = transform.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                Transform t = all[i];
                if (t == null || t == hand)
                    continue;

                string n = t.name;
                if (!n.StartsWith("HoldGun", System.StringComparison.OrdinalIgnoreCase) &&
                    !n.StartsWith("AimGun", System.StringComparison.OrdinalIgnoreCase))
                    continue;

                if (t.parent == hand)
                    continue;

                Transform parent = t.parent;
                if (parent != null)
                {
                    string parentName = parent.name;
                    if (parentName.StartsWith("HoldGun", System.StringComparison.OrdinalIgnoreCase) ||
                        parentName.StartsWith("AimGun", System.StringComparison.OrdinalIgnoreCase))
                        continue;
                }

#if UNITY_EDITOR
                if (!Application.isPlaying &&
                    UnityEditor.PrefabUtility.IsPartOfPrefabInstance(t) &&
                    !UnityEditor.PrefabUtility.IsAddedGameObjectOverride(t.gameObject))
                    continue;
#endif

                t.SetParent(hand, false);
                t.localPosition = Vector3.zero;
                t.localRotation = Quaternion.identity;
                t.localScale = Vector3.one;
                count++;
            }

            return count;
        }

        private int CountWeaponVisualsOnHand()
        {
            if (weaponHand == null)
                return 0;

            int count = 0;
            Transform[] children = weaponHand.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < children.Length; i++)
            {
                Transform child = children[i];
                if (child == null || child == weaponHand || child.parent != weaponHand)
                    continue;

                string n = child.name;
                if (n.StartsWith("HoldGun", System.StringComparison.OrdinalIgnoreCase) ||
                    n.StartsWith("AimGun", System.StringComparison.OrdinalIgnoreCase))
                    count++;
            }

            return count;
        }

        private void ShowDefaultHoldVisualInEditor()
        {
            if (Application.isPlaying || defaultWeapon == null)
                return;

            if (!_visualCache.TryGetValue(defaultWeapon, out WeaponVisuals visuals))
                return;

            if (visuals.Hold != null)
                visuals.Hold.SetActive(true);
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
                if (name.StartsWith("HoldGun", System.StringComparison.OrdinalIgnoreCase) ||
                    name.StartsWith("AimGun", System.StringComparison.OrdinalIgnoreCase))
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
            int style = definition != null ? definition.weaponAimStyle : 0;

            if (_animator != null)
                _animator.SetInteger(WeaponAimStyleHash, style);

            if (upperBodyAim != null)
                upperBodyAim.SetWeaponAimStyle(style);
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

            if (!equipped)
                _isReloading = false;

            if (_controller != null && equipped)
                _strafeBeforeGun = _controller.isStrafing;
            else if (_controller != null && !equipped && !_isAiming)
                _controller.isStrafing = _strafeBeforeGun;

            RefreshGunLayerTarget();

            if (!equipped)
                SetAiming(false, true);
            else
                RefreshFirstPersonWeaponView();
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
                    if (!UsesFirstPersonAimCamera())
                    {
                        _controller.isSprinting = false;
                        _controller.lockRotation = true;
                    }
                }
                else if (wasAiming)
                {
                    _controller.lockRotation = _lockRotationBeforeAim;
                }
            }

            RefreshGunLayerTarget();
            SetHeadAimActive(aiming);

            if (aiming)
            {
                ApplyAimCameraBinding();
            }
            else if (aimCamera != null)
            {
                aimCamera.SetAimCameraActive(false);
            }

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

            if (!aiming && wasAiming && thirdPersonCamera != null && aimCamera != null && !UsesFirstPersonAimCamera())
            {
                thirdPersonCamera.SyncRotation(aimCamera.Yaw, aimCamera.Pitch);
                thirdPersonCamera.BeginExitBlend(
                    aimCamera.transform.position,
                    aimCamera.transform.rotation,
                    cameraTransitionDuration);
            }

            if (upperBodyAim != null)
                upperBodyAim.SetAimingActive(aiming);

            if (!aiming)
            {
                if (thirdPersonCameraComponent != null)
                    thirdPersonCameraComponent.enabled = true;

                if (thirdPersonAudioListener != null)
                    thirdPersonAudioListener.enabled = true;
            }
            else if (!UsesFirstPersonAimCamera())
            {
                if (thirdPersonCameraComponent != null)
                    thirdPersonCameraComponent.enabled = false;

                if (thirdPersonAudioListener != null)
                    thirdPersonAudioListener.enabled = false;
            }
        }

        private void ApplyAimWalkLocomotion()
        {
            if (_controller == null)
                return;

            _controller.forceWalkLocomotion = _isAiming && !UsesFirstPersonAimCamera();
            _controller.suppressUpperBodyLocomotion = _isAiming && !UsesFirstPersonAimCamera();

            if (_isAiming && !UsesFirstPersonAimCamera())
                _controller.isSprinting = false;
        }

        private void ApplyGunHoldLocomotionRules()
        {
            // Pistol uses normal locomotion while held; rifle holding forces forward run style.
            if (_controller == null || !_isEquipped || _isAiming || _controller.isSliding || _controller.isCrouching)
                return;

            if (UsesPistolAimStyle())
                return;

            _controller.isStrafing = false;
            _controller.lockRotation = false;
        }

        private void RefreshGunLayerTarget()
        {
            // Slide / parkour own full-body pose — drop Gun Layer. Crouch keeps Gun Layer
            // so hold/aim coexist with Base Layer crouch legs.
            if (_controller != null && (_controller.isSliding || IsParkourActive()))
            {
                _targetGunLayerWeight = 0f;
                return;
            }

            if (!_isEquipped)
            {
                _targetGunLayerWeight = 0f;
                return;
            }

            // Pistol: blend Gun Layer on while aiming or reloading.
            if (UsesPistolAimStyle())
            {
                _targetGunLayerWeight = (_isAiming || _isReloading) ? 1f : 0f;
                return;
            }

            _targetGunLayerWeight = 1f;
        }

        private bool UsesPistolAimStyle()
        {
            return _weapon != null && _weapon.weaponAimStyle == 1;
        }

        private bool IsParkourActive()
        {
            return _parkourController != null && _parkourController.IsInParkourAction;
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
            _animator.SetBool(IsReloadingHash, _isReloading);
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
                if (showCrosshair)
                    ApplyWeaponCrosshair(_weapon);
            }
        }

        private void CacheDefaultCrosshair()
        {
            if (_hasCachedDefaultCrosshair || crosshair == null)
                return;

            _crosshairImage = crosshair.GetComponent<Image>();
            _crosshairRect = crosshair.GetComponent<RectTransform>();
            if (_crosshairImage != null)
                _defaultCrosshairSprite = _crosshairImage.sprite;
            if (_crosshairRect != null)
                _defaultCrosshairSize = _crosshairRect.sizeDelta;

            _hasCachedDefaultCrosshair = true;
        }

        private void ApplyWeaponCrosshair(WeaponDefinition definition)
        {
            CacheDefaultCrosshair();
            if (_crosshairImage == null)
                return;

            Sprite sprite = definition != null && definition.aimCrosshair != null
                ? definition.aimCrosshair
                : _defaultCrosshairSprite;
            if (_crosshairImage.sprite != sprite)
                _crosshairImage.sprite = sprite;

            if (_crosshairRect == null)
                return;

            Vector2 size = definition != null
                && definition.aimCrosshairSize.x > 0.01f
                && definition.aimCrosshairSize.y > 0.01f
                ? definition.aimCrosshairSize
                : _defaultCrosshairSize;
            if (_crosshairRect.sizeDelta != size)
                _crosshairRect.sizeDelta = size;
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

        private void HandleReloadInput()
        {
            if (!_isEquipped || _isReloading || _weapon == null || inventory == null)
                return;

            if (Input.GetKeyDown(KeyCode.R))
                BeginReload();
        }

        private void UpdateReload()
        {
            if (!_isReloading)
                return;

            if (TryCompleteReloadFromAnimator())
                return;

            if (Time.time < _reloadEndTime)
                return;

            CompleteReload();
        }

        private bool TryCompleteReloadFromAnimator()
        {
            if (_animator == null || _gunLayerIndex < 0)
                return false;

            AnimatorStateInfo state = _animator.GetCurrentAnimatorStateInfo(_gunLayerIndex);
            if (!state.IsName("Rifle Reload") && !state.IsName("Pistol Reload"))
                return false;

            if (state.normalizedTime < 0.95f)
                return false;

            CompleteReload();
            return true;
        }

        private void BeginReload()
        {
            if (_isReloading || _weapon == null || inventory == null)
                return;

            if (!inventory.CanReload(_weapon))
                return;

            _isReloading = true;
            _reloadEndTime = Time.time + GetReloadDuration();
            PlayReloadSound();
            if (networkCombat != null && networkCombat.IsSpawned && networkCombat.IsOwner)
                networkCombat.NotifyReloadFx();
        }

        private float GetReloadDuration()
        {
            if (_weapon == null)
                return 0.1f;

            if (_weapon.reloadAnimation != null)
                return Mathf.Max(0.01f, _weapon.reloadAnimation.length);

            float animatorClipLength = GetReloadClipLengthFromAnimator();
            if (animatorClipLength > 0f)
                return animatorClipLength;

            return Mathf.Max(0.1f, _weapon.reloadDuration);
        }

        private float GetReloadClipLengthFromAnimator()
        {
            if (_animator == null)
                return 0f;

            RuntimeAnimatorController controller = _animator.runtimeAnimatorController;
            if (controller == null)
                return 0f;

            AnimationClip[] clips = controller.animationClips;
            for (int i = 0; i < clips.Length; i++)
            {
                AnimationClip clip = clips[i];
                if (clip == null)
                    continue;

                if (_weapon != null && _weapon.reloadAnimation != null && clip == _weapon.reloadAnimation)
                    return Mathf.Max(0.01f, clip.length);

                if (UsesPistolAimStyle())
                {
                    if (clip.name.Contains("Gun_Reload"))
                        return Mathf.Max(0.01f, clip.length);
                }
                else if (clip.name.Contains("AssaultRifle_Reload") || clip.name.Contains("Rifle_Reload"))
                {
                    return Mathf.Max(0.01f, clip.length);
                }
            }

            return 0f;
        }

        private void CompleteReload()
        {
            if (_weapon != null && inventory != null)
                inventory.ApplyReload(_weapon);

            _isReloading = false;
        }

        private void TryAutoReload()
        {
            if (_isReloading || _weapon == null || inventory == null)
                return;

            if (inventory.CanReload(_weapon))
                BeginReload();
        }

        private void PlayReloadSound()
        {
            if (_weapon == null || _weapon.reloadClip == null)
                return;

            if (gunAudioSource != null)
                gunAudioSource.PlayOneShot(_weapon.reloadClip);
            else if (_activeGunAudio != null)
                _activeGunAudio.PlayOneShot(_weapon.reloadClip);
        }

        private void HandleFireInput()
        {
            if (!_isEquipped || !_isAiming || _weapon == null || _isReloading)
                return;

            if (inventory != null && !inventory.HasAmmoInMag(_weapon))
            {
                TryAutoReload();
                return;
            }

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
            if (aimCamera == null || _weapon == null)
                return;

            // Prefer cached muzzle; fall back so guns still fire if the AimGun
            // visual has no "muzzle" child (common after character swaps).
            Transform fireOrigin = _muzzle;
            if (fireOrigin == null && _visualCache.TryGetValue(_weapon, out WeaponVisuals visuals))
            {
                if (visuals.Aim != null)
                    fireOrigin = visuals.Aim.transform;
                else if (visuals.Hold != null)
                    fireOrigin = visuals.Hold.transform;
            }

            if (fireOrigin == null)
                fireOrigin = weaponHand != null ? weaponHand : transform;

            bool networked = networkCombat != null && networkCombat.IsSpawned;
            if (!networked)
            {
                if (inventory != null && !inventory.TryConsumeRound(_weapon))
                {
                    TryAutoReload();
                    return;
                }
            }
            else
            {
                // Server consumes; pure clients optimistically update local HUD ammo.
                if (inventory != null && !inventory.HasAmmoInMag(_weapon))
                {
                    TryAutoReload();
                    return;
                }

                var nm = Unity.Netcode.NetworkManager.Singleton;
                if (nm != null && !nm.IsServer)
                    inventory?.TryConsumeRound(_weapon);
            }

            float range = Mathf.Max(1f, _weapon.maxRange);
            Vector3 fireOriginPos = fireOrigin.position;
            Vector3 baseDirection;

            if (_isAiming && UsesFirstPersonAimCamera() && thirdPersonCameraComponent != null)
            {
                fireOriginPos = thirdPersonCameraComponent.transform.position;
                baseDirection = thirdPersonCameraComponent.transform.forward;
            }
            else
            {
                if (!TryGetGunAimPoint(out Vector3 aimPoint))
                    aimPoint = fireOrigin.position + aimCamera.GetAimDirection() * range;

                baseDirection = aimPoint - fireOrigin.position;
                if (baseDirection.sqrMagnitude < 0.001f)
                    baseDirection = aimCamera.GetAimDirection();
                baseDirection.Normalize();
            }

            if (networked)
            {
                Unity.Netcode.NetworkObject claimedTarget = null;
                int claimedHitbox = -1;
                Vector3 claimedPoint = Vector3.zero;
                if (TryGetMuzzleHitFrom(fireOriginPos, baseDirection, fireOriginPos + baseDirection * range, out RaycastHit claimHit)
                    && claimHit.collider != null)
                {
                    claimedTarget = claimHit.collider.GetComponentInParent<Unity.Netcode.NetworkObject>();
                    claimedPoint = claimHit.point;
                    var hitHitbox = claimHit.collider.GetComponentInParent<EnemyHitbox>();
                    if (claimedTarget != null && hitHitbox != null)
                    {
                        EnemyHitbox[] boxes = claimedTarget.GetComponentsInChildren<EnemyHitbox>(true);
                        for (int b = 0; b < boxes.Length; b++)
                        {
                            if (boxes[b] == hitHitbox)
                            {
                                claimedHitbox = b;
                                break;
                            }
                        }
                    }
                }

                networkCombat.RequestFire(
                    fireOriginPos,
                    baseDirection,
                    _weapon.damage,
                    range,
                    _weapon.impactForce,
                    _weapon.spreadDegrees,
                    Mathf.Max(1, _weapon.pelletsPerShot),
                    _weapon.slot,
                    claimedTarget,
                    claimedHitbox,
                    claimedPoint);
            }

            int pellets = Mathf.Max(1, _weapon.pelletsPerShot);
            for (int i = 0; i < pellets; i++)
            {
                Vector3 fireDirection = ApplySpread(baseDirection, _weapon.spreadDegrees);
                Vector3 pelletAimPoint = fireOriginPos + fireDirection * range;

                if (!TryGetMuzzleHitFrom(fireOriginPos, fireDirection, pelletAimPoint, out RaycastHit hit))
                {
                    hit.point = pelletAimPoint;
                    hit.normal = -fireDirection;
                }

                if (!networked)
                    ApplyDamage(hit, fireDirection);

                SpawnTracer(fireOriginPos, hit.point);
                SpawnImpactEffects(hit);
            }

            if (!UsesFirstPersonAimCamera())
                SpawnMuzzleFlashAt(fireOrigin);
            PlayFireSound();
            ApplyRecoil();
            OnLocalShotFired?.Invoke(_isAiming);

            if (!networked && inventory != null && !inventory.HasAmmoInMag(_weapon))
                TryAutoReload();
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
            if (aimCamera == null || _weapon == null)
                return false;

            float range = Mathf.Max(1f, _weapon.maxRange);
            Vector3 origin = _muzzle != null
                ? _muzzle.position
                : (weaponHand != null ? weaponHand.position : transform.position);

            if (!TryGetAimPoint(out Vector3 cameraAimPoint))
                cameraAimPoint = origin + aimCamera.GetAimDirection() * range;

            Vector3 fireDirection = cameraAimPoint - origin;
            if (fireDirection.sqrMagnitude < 0.001f)
                fireDirection = aimCamera.GetAimDirection();
            else
                fireDirection.Normalize();

            if (TryGetMuzzleHitFrom(origin, fireDirection, cameraAimPoint, out RaycastHit hit))
            {
                aimPoint = hit.point;
                return true;
            }

            aimPoint = origin + fireDirection * range;
            return true;
        }

        private bool TryGetAimPoint(out Vector3 aimPoint)
        {
            float range = _weapon != null ? Mathf.Max(1f, _weapon.maxRange) : 120f;
            aimPoint = transform.position + transform.forward * range;

            if (_isAiming && UsesFirstPersonAimCamera() && thirdPersonCameraComponent != null)
            {
                Ray cameraRay = thirdPersonCameraComponent.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
                if (Physics.Raycast(cameraRay, out RaycastHit fpHit, range, hitMask, QueryTriggerInteraction.Ignore))
                {
                    if (!IsSelfCollider(fpHit.collider))
                    {
                        aimPoint = fpHit.point;
                        return true;
                    }
                }

                aimPoint = cameraRay.origin + cameraRay.direction * range;
                return true;
            }

            if (aimCamera == null)
                return false;

            Ray aimRay = aimCamera.GetAimRay();

            if (Physics.Raycast(aimRay, out RaycastHit hit, range, hitMask, QueryTriggerInteraction.Ignore))
            {
                if (!IsSelfCollider(hit.collider))
                {
                    aimPoint = hit.point;
                    return true;
                }
            }

            aimPoint = aimRay.origin + aimRay.direction * range;
            return true;
        }

        private bool TryGetMuzzleHit(Vector3 fireDirection, Vector3 aimPoint, out RaycastHit hit)
        {
            Vector3 origin = _muzzle != null
                ? _muzzle.position
                : (weaponHand != null ? weaponHand.position : transform.position);
            return TryGetMuzzleHitFrom(origin, fireDirection, aimPoint, out hit);
        }

        private bool TryGetMuzzleHitFrom(Vector3 origin, Vector3 fireDirection, Vector3 aimPoint, out RaycastHit hit)
        {
            float distance = Vector3.Distance(origin, aimPoint) + 0.05f;
            RaycastHit[] hits = Physics.RaycastAll(
                origin,
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

        private void ApplyDamage(RaycastHit hit, Vector3 shotDirection)
        {
            if (hit.collider == null || _weapon == null)
                return;

            var impact = new BulletImpactData
            {
                Damage = _weapon.damage,
                HitPoint = hit.point,
                HitNormal = hit.normal,
                ShotDirection = shotDirection.sqrMagnitude > 0.0001f
                    ? shotDirection.normalized
                    : -hit.normal,
                ImpactForce = Mathf.Max(0f, _weapon.impactForce),
                Collider = hit.collider,
                Source = gameObject
            };

            var receiver = hit.collider.GetComponentInParent<IBulletImpactReceiver>();
            if (receiver != null)
            {
                receiver.ReceiveBulletImpact(in impact);
                return;
            }

            var damageable = hit.collider.GetComponentInParent<IDamageable>();
            damageable?.TakeDamage(impact.Damage, impact.HitPoint, impact.HitNormal, impact.Source);
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
            Transform origin = _muzzle != null ? _muzzle : weaponHand;
            SpawnMuzzleFlashAt(origin);
        }

        private void SpawnMuzzleFlashAt(Transform origin)
        {
            if (_weapon == null || _weapon.muzzleFlashPrefab == null || origin == null)
                return;

            // Spawn in world space (not parented) so aim-gun enable/disable cannot re-trigger playOnAwake.
            GameObject flash = Instantiate(
                _weapon.muzzleFlashPrefab,
                origin.position,
                origin.rotation);
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

            // Body regions with EnemyDismemberment spawn blood via PlayHitEffectsVisual.
            if (hit.collider != null)
            {
                var dismember = hit.collider.GetComponentInParent<EnemyDismemberment>();
                if (dismember != null)
                {
                    dismember.PlayHitEffectsVisual(hit.point, hit.normal);
                    return;
                }
            }

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

        /// <summary>Remote-client impact cue from NetworkCombat.ImpactClientRpc.</summary>
        public void PlayNetworkImpact(Vector3 point, Vector3 normal)
        {
            if (_weapon == null)
                return;

            if (_weapon.hitPrefab != null)
            {
                GameObject impact = Instantiate(_weapon.hitPrefab, point, Quaternion.LookRotation(normal));
                Destroy(impact, effectLifetime);
            }

            if (_weapon.decalPrefab != null)
            {
                Vector3 decalPosition = point + normal * decalSurfaceOffset;
                GameObject decal = Instantiate(_weapon.decalPrefab, decalPosition, Quaternion.LookRotation(normal));
                Destroy(decal, decalLifetime);
            }
        }
    }
}
