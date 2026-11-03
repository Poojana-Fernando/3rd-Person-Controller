using DeadAuthorGames.ThirdPersonCombat;
using Motiong.vCharacterController;
using UnityEngine;

namespace Game.FirstPerson
{
    [DefaultExecutionOrder(50)]
    public class FirstPersonSystem : MonoBehaviour, IFirstPersonRigHost
    {
        [SerializeField] private FirstPersonCameraController cameraCtrl;
        [SerializeField] private FirstPersonRecoil recoil;
        [SerializeField] private FirstPersonWeaponMotion weaponMotion;
        [SerializeField] private FirstPersonWeaponView weaponView;
        [SerializeField] private FirstPersonViewmodelRig viewmodelRig;
        [SerializeField] private FirstPersonBodyMeshSplitter bodySplitter;
        [SerializeField] private FirstPersonCursorLock cursorLock;
        [SerializeField] private FirstPersonCameraStack cameraStack;
        [SerializeField] private FirstPersonMuzzleFlash muzzleFlash;

        private Transform _pitchPivot;
        private Transform _recoilRoot;
        private Transform _shoulderAnchor;
        private Transform _weaponRoot;
        private Camera _camera;
        private Transform _player;
        private PlayerGunController _gun;
        private UpperBodyAim _upperBodyAim;
        private bool _active;

        public FirstPersonCameraController CameraController => cameraCtrl;
        public bool IsActive => _active;

        private void Awake()
        {
            EnsureComponents();
            BuildRig();
        }

        public void BindPlayer(Transform player, Camera camera)
        {
            if (_gun != null)
                _gun.OnLocalShotFired -= HandleLocalShot;

            _player = player;
            _camera = camera;
            _gun = player != null ? player.GetComponent<PlayerGunController>() : null;
            _upperBodyAim = player != null ? player.GetComponent<UpperBodyAim>() : null;

            BuildRig();
            cameraCtrl?.Configure(_player, _camera, _pitchPivot, _recoilRoot, _weaponRoot);
            weaponMotion?.Bind(_weaponRoot, _player, weaponView, cameraCtrl);
            weaponView?.Bind(_weaponRoot, _player, cameraCtrl);
            bodySplitter?.Bind(_player);
            recoil?.Bind(cameraCtrl, weaponMotion, weaponView);
            muzzleFlash?.SetActive(false);
            cameraStack?.Bind(_camera, _recoilRoot);

            if (_gun != null)
                _gun.OnLocalShotFired += HandleLocalShot;

            if (bodySplitter != null && _player != null)
                viewmodelRig?.Bind(_player, bodySplitter.ArmsSource);
        }

        public void SetActive(bool active)
        {
            _active = active;
            cameraCtrl?.SetActive(active);
            recoil?.SetActive(active);
            weaponMotion?.SetActive(active);
            weaponView?.SetActive(active);
            viewmodelRig?.SetActive(active);
            bodySplitter?.SetActive(active);
            cursorLock?.SetActive(active);
            muzzleFlash?.SetActive(active);

            if (active)
            {
                cameraStack?.EnsureStack();
                viewmodelRig?.Bind(_player, bodySplitter != null ? bodySplitter.ArmsSource : null);
            }
            else
                cameraStack?.RestoreBaseCamera();
        }

        public void ApplyLookInput(float mouseX, float mouseY)
        {
            cameraCtrl?.ApplyLookInput(mouseX, mouseY);
            weaponMotion?.ApplyMouseDelta(mouseX, mouseY);
        }

        public void SyncFromOrbit(float yaw, float pitch) => cameraCtrl?.SyncFromOrbit(yaw, pitch);

        public void ExportOrbit(out float yaw, out float pitch)
        {
            if (cameraCtrl != null)
                cameraCtrl.ExportToOrbit(out yaw, out pitch);
            else
            {
                yaw = 0f;
                pitch = 0f;
            }
        }

        public void PlaceCameraAtHead(Vector3 eyeWorld) => cameraCtrl?.PlaceCameraAtEye(eyeWorld);

        public void ApplyViewRotation(float yaw, float pitch) => cameraCtrl?.ApplyViewRotation(yaw, pitch);

        private void Update()
        {
            if (!_active)
                return;

            cameraCtrl?.ReadInputDirect();
        }

        private void FixedUpdate()
        {
            if (!_active)
                return;

            cameraCtrl?.FixedUpdateYaw();
        }

        private void LateUpdate()
        {
            if (!_active || _player == null)
                return;

            bool ads = _gun != null && _gun.IsAiming;
            var motor = _player.GetComponent<vThirdPersonController>();
            bool sprint = motor != null && motor.isSprinting;

            cameraCtrl?.SetAds(ads);
            weaponMotion?.SetAds(ads);
            weaponMotion?.SetSprinting(sprint);

            bodySplitter?.SetLookPitch(cameraCtrl.Pitch);

            weaponView?.LateUpdatePose(ads, sprint, _camera);
            weaponMotion?.ApplyPostPose(weaponView.LastLocalPosition, weaponView.LastLocalRotation);

            viewmodelRig?.LateUpdateSolve(weaponView.RightGrip, weaponView.LeftGrip);

            if (_upperBodyAim != null && _gun != null && _gun.IsEquipped)
            {
                float pitch = cameraCtrl.Pitch;
                _upperBodyAim.ClampPitch(ref pitch);
                _upperBodyAim.CurrentPitch = pitch;
                _upperBodyAim.SetAimingActive(true);
                if (_gun.CurrentWeapon != null)
                    _upperBodyAim.SetWeaponAimStyle(_gun.CurrentWeapon.weaponAimStyle);
            }

            if (_gun != null)
                weaponView?.SetReloading(_gun.IsReloading);
        }

        private void HandleLocalShot(bool ads)
        {
            recoil?.ApplyShot(ads, weaponView?.GetActiveProfile());
            muzzleFlash?.PlayAt(weaponView?.Muzzle);
        }

        private void OnDisable()
        {
            if (_gun != null)
                _gun.OnLocalShotFired -= HandleLocalShot;
        }

        private void EnsureComponents()
        {
            if (cameraCtrl == null)
                cameraCtrl = GetComponent<FirstPersonCameraController>() ?? gameObject.AddComponent<FirstPersonCameraController>();
            if (recoil == null)
                recoil = GetComponent<FirstPersonRecoil>() ?? gameObject.AddComponent<FirstPersonRecoil>();
            if (weaponMotion == null)
                weaponMotion = GetComponent<FirstPersonWeaponMotion>() ?? gameObject.AddComponent<FirstPersonWeaponMotion>();
            if (weaponView == null)
                weaponView = GetComponent<FirstPersonWeaponView>() ?? gameObject.AddComponent<FirstPersonWeaponView>();
            if (viewmodelRig == null)
                viewmodelRig = GetComponent<FirstPersonViewmodelRig>() ?? gameObject.AddComponent<FirstPersonViewmodelRig>();
            if (bodySplitter == null)
                bodySplitter = GetComponent<FirstPersonBodyMeshSplitter>() ?? gameObject.AddComponent<FirstPersonBodyMeshSplitter>();
            if (cursorLock == null)
                cursorLock = GetComponent<FirstPersonCursorLock>() ?? gameObject.AddComponent<FirstPersonCursorLock>();
            if (cameraStack == null)
                cameraStack = GetComponent<FirstPersonCameraStack>() ?? gameObject.AddComponent<FirstPersonCameraStack>();
            if (muzzleFlash == null)
                muzzleFlash = GetComponent<FirstPersonMuzzleFlash>() ?? gameObject.AddComponent<FirstPersonMuzzleFlash>();
        }

        private void BuildRig()
        {
            if (_camera == null)
                _camera = GetComponent<Camera>();

            if (_pitchPivot == null)
            {
                var pivotGo = new GameObject("CameraPivot");
                pivotGo.transform.SetParent(transform, false);
                _pitchPivot = pivotGo.transform;
            }

            if (_recoilRoot == null)
            {
                var recoilGo = new GameObject("RecoilRoot");
                recoilGo.transform.SetParent(_pitchPivot, false);
                _recoilRoot = recoilGo.transform;
            }

            if (_shoulderAnchor == null)
            {
                var shoulderGo = new GameObject("ShoulderAnchor");
                shoulderGo.transform.SetParent(_recoilRoot, false);
                _shoulderAnchor = shoulderGo.transform;
            }

            if (viewmodelRig != null)
                viewmodelRig.SetShoulderAnchor(_shoulderAnchor);

            if (_camera != null && _camera.transform.parent != _recoilRoot)
            {
                _camera.transform.SetParent(_recoilRoot, false);
                _camera.transform.localPosition = Vector3.zero;
                _camera.transform.localRotation = Quaternion.identity;
            }

            if (_weaponRoot == null)
            {
                var weaponGo = new GameObject("WeaponRoot");
                weaponGo.transform.SetParent(_recoilRoot, false);
                _weaponRoot = weaponGo.transform;
            }
        }
    }
}
