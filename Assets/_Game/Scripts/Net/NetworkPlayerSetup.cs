using DeadAuthorGames.ThirdPersonCombat;
using Motiong.vCharacterController;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.Net
{
    /// <summary>
    /// Owner gating, per-owner camera spawn, registry, remote smoothing, random spawn.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public class NetworkPlayerSetup : NetworkBehaviour
    {
        [SerializeField] private GameObject thirdPersonCameraPrefab;
        [SerializeField] private GameObject aimCameraPrefab;
        [SerializeField] private Transform cameraSpawnAnchor;

        private vThirdPersonInput _input;
        private vThirdPersonController _controller;
        private PlayerGunController _gun;
        private ParkourController _parkour;
        private PlayerHealth _health;
        private NetworkHealth _networkHealth;
        private Rigidbody _rigidbody;
        private CapsuleCollider _capsule;
        private GameObject _tpCamInstance;
        private GameObject _aimCamInstance;
        private bool _listeningForSceneLoad;
        private bool _didRandomSpawn;

        public PlayerGunController Gun => _gun;
        public PlayerHealth Health => _health;
        public NetworkHealth NetworkHealth => _networkHealth;
        public bool IsAlive => _networkHealth == null
            ? (_health == null || !_health.IsDead)
            : !_networkHealth.IsDead;

        private void Awake()
        {
            _input = GetComponent<vThirdPersonInput>();
            _controller = GetComponent<vThirdPersonController>();
            _gun = GetComponent<PlayerGunController>();
            _parkour = GetComponent<ParkourController>();
            _health = GetComponent<PlayerHealth>();
            _networkHealth = GetComponent<NetworkHealth>();
            _rigidbody = GetComponent<Rigidbody>();
            _capsule = GetComponent<CapsuleCollider>();
        }

        public override void OnNetworkSpawn()
        {
            NetworkPlayerRegistry.Ensure().Register(this);

            if (IsOwner)
            {
                SpawnLocalCameras();
                BindLocalHud();
                BeginSceneLoadWatch();

                if (SceneManager.GetActiveScene().name == NetworkGameBootstrap.GameplaySceneName)
                    TryRandomSpawn();
            }
            else
            {
                DisableRemoteControl();
            }
        }

        public override void OnNetworkDespawn()
        {
            EndSceneLoadWatch();
            NetworkPlayerRegistry.Instance?.Unregister(this);

            if (_tpCamInstance != null)
                Destroy(_tpCamInstance);
            if (_aimCamInstance != null)
                Destroy(_aimCamInstance);
            _tpCamInstance = null;
            _aimCamInstance = null;
        }

        private void BeginSceneLoadWatch()
        {
            if (_listeningForSceneLoad)
                return;
            SceneManager.sceneLoaded += OnGameplaySceneLoaded;
            _listeningForSceneLoad = true;
        }

        private void EndSceneLoadWatch()
        {
            if (!_listeningForSceneLoad)
                return;
            SceneManager.sceneLoaded -= OnGameplaySceneLoaded;
            _listeningForSceneLoad = false;
        }

        private void OnGameplaySceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (!IsSpawned || !IsOwner)
                return;
            if (scene.name != NetworkGameBootstrap.GameplaySceneName)
                return;

            if (_tpCamInstance == null)
                SpawnLocalCameras();
            else
                RebindLocalCameras();

            BindLocalHud();
            LobbyMenuController.Instance?.EnterSessionHideMenu();
            TryRandomSpawn();
            // Rebind again after teleport so the camera snaps to the new spawn.
            RebindLocalCameras();
        }

        public void TryRandomSpawn()
        {
            if (!IsOwner || _didRandomSpawn)
                return;

            _didRandomSpawn = true;
            Vector3 pos = PlayerSpawnUtility.FindSpawnPosition(transform.position);
            PlayerSpawnUtility.TeleportOwner(NetworkObject, pos);
        }

        public void OwnerRespawnAtRandom()
        {
            if (!IsOwner)
                return;

            Vector3 pos = PlayerSpawnUtility.FindSpawnPosition(transform.position);
            PlayerSpawnUtility.TeleportOwner(NetworkObject, pos);
            RebindLocalCameras();
        }

        private void DisableRemoteControl()
        {
            if (_input != null)
                _input.enabled = false;
            if (_parkour != null)
                _parkour.enabled = false;
            if (_controller != null)
                _controller.enabled = false;

            if (_gun != null)
            {
                _gun.skipAutoAssign = true;
                // Keep enabled so ApplyRemoteState / animator gun layer can run; input is skipped via skipAutoAssign paths.
                // Actually Update still reads input — disable the component and drive visuals via NetworkWeaponState.
                _gun.enabled = false;
            }

            if (_rigidbody != null)
            {
                _rigidbody.isKinematic = true;
                _rigidbody.interpolation = RigidbodyInterpolation.Interpolate;
                _rigidbody.detectCollisions = true;
            }

            if (_capsule != null)
            {
                // Keep collider for hit detection but avoid physics pushes against interpolated body.
                _capsule.enabled = true;
            }

            AudioListener[] listeners = GetComponentsInChildren<AudioListener>(true);
            for (int i = 0; i < listeners.Length; i++)
                listeners[i].enabled = false;

            Camera[] cams = GetComponentsInChildren<Camera>(true);
            for (int i = 0; i < cams.Length; i++)
                cams[i].enabled = false;
        }

        private void SpawnLocalCameras()
        {
            if (_tpCamInstance != null)
                Destroy(_tpCamInstance);
            if (_aimCamInstance != null)
                Destroy(_aimCamInstance);
            _tpCamInstance = null;
            _aimCamInstance = null;

            vThirdPersonCamera tpCam = null;
            ResidentEvilAimCamera aimCam = null;

            if (thirdPersonCameraPrefab != null)
            {
                _tpCamInstance = Instantiate(thirdPersonCameraPrefab);
                DontDestroyOnLoad(_tpCamInstance);
                tpCam = _tpCamInstance.GetComponentInChildren<vThirdPersonCamera>(true);
                if (tpCam == null)
                    tpCam = _tpCamInstance.GetComponent<vThirdPersonCamera>();
                if (tpCam != null)
                    tpCam.SetMainTarget(transform);

                Camera[] cams = _tpCamInstance.GetComponentsInChildren<Camera>(true);
                for (int i = 0; i < cams.Length; i++)
                    cams[i].enabled = true;
            }

            if (aimCameraPrefab != null)
            {
                _aimCamInstance = Instantiate(aimCameraPrefab);
                DontDestroyOnLoad(_aimCamInstance);
                aimCam = _aimCamInstance.GetComponentInChildren<ResidentEvilAimCamera>(true);
                if (aimCam == null)
                    aimCam = _aimCamInstance.GetComponent<ResidentEvilAimCamera>();
                if (aimCam != null)
                {
                    aimCam.skipAutoAssign = true;
                    aimCam.followTarget = transform;
                    aimCam.spineAnchor = FindSpine(transform);
                    aimCam.upperBodyAim = GetComponent<UpperBodyAim>();
                }
            }

            EnsureSingleAudioListener();

            if (_input != null && tpCam != null)
                _input.tpCamera = tpCam;

            WireGunCameras(tpCam, aimCam);

            PlayerCameraEffects effects = _tpCamInstance != null
                ? _tpCamInstance.GetComponentInChildren<PlayerCameraEffects>(true)
                : null;
            if (effects != null)
                PlayerCameraEffects.RegisterLocal(effects);
        }

        /// <summary>
        /// Re-attach follow targets after Bootstrap→gameplay scene load (DDOL camera survives, targetLookAt may not).
        /// </summary>
        private void RebindLocalCameras()
        {
            if (!IsOwner)
                return;

            vThirdPersonCamera tpCam = null;
            if (_tpCamInstance != null)
            {
                tpCam = _tpCamInstance.GetComponentInChildren<vThirdPersonCamera>(true);
                if (tpCam == null)
                    tpCam = _tpCamInstance.GetComponent<vThirdPersonCamera>();
                if (tpCam != null)
                    tpCam.SetMainTarget(transform);
            }

            ResidentEvilAimCamera aimCam = null;
            if (_aimCamInstance != null)
            {
                aimCam = _aimCamInstance.GetComponentInChildren<ResidentEvilAimCamera>(true);
                if (aimCam == null)
                    aimCam = _aimCamInstance.GetComponent<ResidentEvilAimCamera>();
                if (aimCam != null)
                {
                    aimCam.skipAutoAssign = true;
                    aimCam.followTarget = transform;
                    aimCam.spineAnchor = FindSpine(transform);
                    aimCam.upperBodyAim = GetComponent<UpperBodyAim>();
                }
            }

            if (_input != null && tpCam != null)
                _input.tpCamera = tpCam;

            WireGunCameras(tpCam, aimCam);
            EnsureSingleAudioListener();
        }

        private void WireGunCameras(vThirdPersonCamera tpCam, ResidentEvilAimCamera aimCam)
        {
            if (_gun == null)
                return;

            _gun.skipAutoAssign = true;
            if (aimCam != null)
                _gun.aimCamera = aimCam;
            if (tpCam != null)
                _gun.BindThirdPersonCamera(tpCam);
        }

        private void EnsureSingleAudioListener()
        {
            AudioListener keep = null;
            if (_tpCamInstance != null)
                keep = _tpCamInstance.GetComponentInChildren<AudioListener>(true);

            if (_tpCamInstance != null)
            {
                AudioListener[] tpListeners = _tpCamInstance.GetComponentsInChildren<AudioListener>(true);
                for (int i = 0; i < tpListeners.Length; i++)
                {
                    if (keep == null)
                        keep = tpListeners[i];
                    tpListeners[i].enabled = tpListeners[i] == keep;
                }
            }

            if (_aimCamInstance != null)
            {
                AudioListener[] aimListeners = _aimCamInstance.GetComponentsInChildren<AudioListener>(true);
                for (int i = 0; i < aimListeners.Length; i++)
                    aimListeners[i].enabled = false;
            }

            AudioListener[] bodyListeners = GetComponentsInChildren<AudioListener>(true);
            for (int i = 0; i < bodyListeners.Length; i++)
            {
                if (keep != null && bodyListeners[i] == keep)
                    continue;
                bodyListeners[i].enabled = false;
            }

            if (keep != null)
                keep.enabled = true;
        }

        public void SetLocalControlsEnabled(bool enabled)
        {
            if (!IsOwner)
                return;

            if (_input != null)
                _input.enabled = enabled;
            if (_parkour != null)
                _parkour.enabled = enabled;
            if (_gun != null)
                _gun.enabled = enabled;

            UpperBodyAim aim = GetComponent<UpperBodyAim>();
            if (aim != null)
                aim.enabled = enabled;
        }

        private void BindLocalHud()
        {
            PlayerHealthHud healthHud = FindAnyObjectByType<PlayerHealthHud>();
            healthHud?.BindPlayer(_health);

            PlayerWeaponHud weaponHud = FindAnyObjectByType<PlayerWeaponHud>();
            weaponHud?.BindPlayer(_gun, GetComponent<WeaponInventory>());

            PlayerDeathPanel deathPanel = FindAnyObjectByType<PlayerDeathPanel>();
            deathPanel?.BindPlayer(_health);

            if (LobbyMenuController.Instance != null && LobbyMenuController.Instance.IsMenuVisible)
                SetLocalControlsEnabled(false);
        }

        private static Transform FindSpine(Transform root)
        {
            Transform spine = FindBone(root, "CC_Base_Spine01");
            if (spine != null)
                return spine;
            spine = FindBone(root, "spine2");
            if (spine != null)
                return spine;
            return root.Find("guard/Armature/root ground/root hip/spine lower/spine middle");
        }

        private static Transform FindBone(Transform root, string exactName)
        {
            Transform[] all = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i].name == exactName)
                    return all[i];
            }

            return null;
        }

        public void SetCameraPrefabs(GameObject tp, GameObject aim)
        {
            thirdPersonCameraPrefab = tp;
            aimCameraPrefab = aim;
        }
    }
}
