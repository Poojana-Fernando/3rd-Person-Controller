using DeadAuthorGames.ThirdPersonCombat;
using Motiong.vCharacterController;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Game.FirstPerson
{
    [DefaultExecutionOrder(1000)]
    public class FirstPersonCameraController : MonoBehaviour
    {
        private Transform _player;
        private Rigidbody _playerRb;
        private Camera _camera;
        private Transform _pitchPivot;
        private Transform _recoilRoot;
        private Transform _weaponRoot;
        private vThirdPersonController _motor;
        private ParkourController _parkour;
        private CapsuleCollider _capsule;
        private Animator _animator;

        private float _yaw;
        private float _pitch;
        private float _eyeHeight;
        private float _eyeHeightVel;
        private bool _active;
        private bool _ads;

        private float _viewKickPitch;
        private float _viewKickYaw;
        private float _viewKickRoll;
        private float _aimPunchPitch;
        private float _aimPunchYaw;
        private int _aimPunchIndex;

        private FirstPersonSettings _settings;

        public float Yaw => _yaw;
        public float Pitch => _pitch;
        public Transform PitchPivot => _pitchPivot;
        public Transform RecoilRoot => _recoilRoot;
        public Transform WeaponRoot => _weaponRoot;
        public bool IsAds => _ads;

        public void Configure(
            Transform player,
            Camera camera,
            Transform pitchPivot,
            Transform recoilRoot,
            Transform weaponRoot)
        {
            _player = player;
            _camera = camera;
            _pitchPivot = pitchPivot;
            _recoilRoot = recoilRoot;
            _weaponRoot = weaponRoot;
            _settings = FirstPersonSettings.Runtime;

            if (_player != null)
            {
                _playerRb = _player.GetComponent<Rigidbody>();
                _motor = _player.GetComponent<vThirdPersonController>();
                _parkour = _player.GetComponent<ParkourController>();
                _capsule = _player.GetComponent<CapsuleCollider>();
                _animator = _player.GetComponentInChildren<Animator>();
                _yaw = _player.eulerAngles.y;
            }

            if (_camera != null)
                _camera.fieldOfView = _settings.worldFov;

            _eyeHeight = _settings.eyeHeightStanding;
            ApplyViewRotationImmediate();
        }

        public void SetActive(bool active)
        {
            _active = active;
            if (active)
                ApplyViewRotationImmediate();
        }

        public void SetAds(bool ads)
        {
            _ads = ads;
            if (_camera == null || _settings == null)
                return;

            float baseFov = _settings.worldFov;
            float target = _ads ? baseFov * _settings.adsWorldFovScale : baseFov;
            _camera.fieldOfView = target;
        }

        public void SyncFromOrbit(float yaw, float pitch)
        {
            _yaw = yaw;
            _pitch = Mathf.Clamp(pitch, _settings.minPitch, _settings.maxPitch);
            ApplyViewRotationImmediate();
        }

        public void ExportToOrbit(out float yaw, out float pitch)
        {
            yaw = _yaw;
            pitch = _pitch;
        }

        public void ApplyLookInput(float mouseX, float mouseY)
        {
            if (!_active)
                return;

            float sensX = _settings.mouseSensitivityX * 100f;
            float sensY = _settings.mouseSensitivityY * 100f;
            if (_ads)
            {
                float fovScale = _settings.adsWorldFovScale;
                sensX *= _settings.adsSensitivityScale * fovScale;
                sensY *= _settings.adsSensitivityScale * fovScale;
            }

            _yaw += mouseX * sensX;
            _pitch += (_settings.invertY ? mouseY : -mouseY) * sensY;
            _pitch = Mathf.Clamp(_pitch, _settings.minPitch, _settings.maxPitch);
        }

        public void ReadInputDirect()
        {
            if (!_active || Game.Net.LobbyMenuController.BlocksLookInput)
                return;

#if ENABLE_INPUT_SYSTEM
            if (Mouse.current != null)
            {
                Vector2 delta = Mouse.current.delta.ReadValue();
                ApplyLookInput(delta.x, delta.y);
                return;
            }
#endif
            ApplyLookInput(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y"));
        }

        public void FixedUpdateYaw()
        {
            if (!_active || _playerRb == null)
                return;

            Quaternion target = Quaternion.Euler(0f, _yaw, 0f);
            _playerRb.MoveRotation(target);
        }

        public void ApplyViewRecoil(float pitchKick, float yawKick, float rollKick)
        {
            _viewKickPitch += pitchKick;
            _viewKickYaw += yawKick;
            _viewKickRoll += rollKick;
        }

        public void ApplyAimPunch(FirstPersonWeaponProfile profile, bool ads)
        {
            if (profile == null || profile.aimPunchPattern == null || profile.aimPunchPattern.Length == 0)
                return;

            Vector2 step = profile.aimPunchPattern[_aimPunchIndex % profile.aimPunchPattern.Length];
            _aimPunchIndex++;
            float scale = ads ? profile.adsRecoilScale : 1f;
            _aimPunchPitch += step.y * scale;
            _aimPunchYaw += step.x * scale;
        }

        public void LateUpdateRecoilRecovery(float speed)
        {
            float t = speed * Time.deltaTime;
            _viewKickPitch = Mathf.Lerp(_viewKickPitch, 0f, t);
            _viewKickYaw = Mathf.Lerp(_viewKickYaw, 0f, t);
            _viewKickRoll = Mathf.Lerp(_viewKickRoll, 0f, t);
            _aimPunchPitch = Mathf.Lerp(_aimPunchPitch, 0f, t * 0.65f);
            _aimPunchYaw = Mathf.Lerp(_aimPunchYaw, 0f, t * 0.65f);

            if (_recoilRoot != null)
            {
                _recoilRoot.localRotation = Quaternion.Euler(
                    -_viewKickPitch - _aimPunchPitch,
                    _viewKickYaw + _aimPunchYaw,
                    _viewKickRoll);
            }
        }

        public Vector3 ComputeEyeWorldPosition()
        {
            if (_player == null)
                return transform.position;

            Vector3 rootPos = _player.position;
            float targetHeight = ResolveTargetEyeHeight();
            _eyeHeight = Mathf.SmoothDamp(_eyeHeight, targetHeight, ref _eyeHeightVel, _settings.eyeHeightSmoothTime);

            Vector3 eye = rootPos + Vector3.up * _eyeHeight;

            if (_parkour != null && _parkour.IsInParkourAction)
            {
                Transform head = ResolveHeadBone();
                if (head != null)
                {
                    Vector3 headEye = head.position + Vector3.up * 0.06f;
                    eye = Vector3.Lerp(eye, headEye, _settings.parkourHeadBlend);
                }
            }

            Vector3 forwardFlat = Quaternion.Euler(0f, _yaw, 0f) * Vector3.forward;
            eye += forwardFlat * _settings.eyeForwardOffset;

            float downPitch = Mathf.Max(0f, -_pitch - _settings.bodyPullbackStartPitch);
            float pull = Mathf.Clamp01(downPitch / 40f) * _settings.bodyPullbackMax;
            eye -= forwardFlat * pull;

            return eye;
        }

        public void ApplyViewRotation(float yaw, float pitch)
        {
            _yaw = yaw;
            _pitch = pitch;

            if (_pitchPivot != null)
                _pitchPivot.localRotation = Quaternion.Euler(pitch, 0f, 0f);

            if (_camera != null)
            {
                _camera.transform.localRotation = Quaternion.identity;
                _camera.transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
            }

            transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        }

        public void PlaceCameraAtEye(Vector3 eyeWorld)
        {
            if (_camera != null)
                _camera.transform.position = eyeWorld;
            transform.position = eyeWorld;
        }

        private void ApplyViewRotationImmediate() => ApplyViewRotation(_yaw, _pitch);

        private float ResolveTargetEyeHeight()
        {
            if (_motor != null)
            {
                if (_motor.isSliding)
                    return _settings.eyeHeightSlide;
                if (_motor.isCrouching)
                    return _settings.eyeHeightCrouch;
            }

            if (_capsule != null)
            {
                float h = _capsule.height;
                return Mathf.Clamp(h * 0.88f, _settings.eyeHeightCrouch, _settings.eyeHeightStanding);
            }

            return _settings.eyeHeightStanding;
        }

        private Transform ResolveHeadBone()
        {
            if (_animator != null && _animator.isHuman)
                return _animator.GetBoneTransform(HumanBodyBones.Head);
            return null;
        }

        private void LateUpdate()
        {
            if (!_active)
                return;

            ApplyViewRotation(_yaw, _pitch);
            Vector3 eye = ComputeEyeWorldPosition();
            PlaceCameraAtEye(eye);
        }
    }
}
