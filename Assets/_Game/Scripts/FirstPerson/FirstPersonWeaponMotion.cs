using UnityEngine;

namespace Game.FirstPerson
{
    [DefaultExecutionOrder(1010)]
    public class FirstPersonWeaponMotion : MonoBehaviour
    {
        private Transform _weaponRoot;
        private Transform _player;
        private Rigidbody _rb;
        private FirstPersonWeaponView _weaponView;
        private FirstPersonCameraController _cameraCtrl;

        private SpringVector3 _swayRot;
        private SpringVector3 _swayPos;
        private SpringFloat _strafeRoll;
        private SpringFloat _bob;
        private SpringVector3 _recoilOffset;
        private SpringFloat _wallPullback;

        private Vector3 _lastPlayerPos;
        private float _bobPhase;
        private bool _active;
        private bool _ads;
        private bool _sprinting;
        private Vector3 _motionOffset;
        private Vector3 _motionEuler;

        public void Bind(Transform weaponRoot, Transform player, FirstPersonWeaponView weaponView, FirstPersonCameraController cameraCtrl)
        {
            _weaponRoot = weaponRoot;
            _player = player;
            _weaponView = weaponView;
            _cameraCtrl = cameraCtrl;
            if (_player != null)
            {
                _lastPlayerPos = _player.position;
                _rb = _player.GetComponent<Rigidbody>();
            }

            _swayRot = new SpringVector3(180f, 12f);
            _swayPos = new SpringVector3(120f, 10f);
            _strafeRoll = new SpringFloat(140f, 10f);
            _bob = new SpringFloat(16f, 6f);
            _recoilOffset = new SpringVector3(200f, 14f);
            _wallPullback = new SpringFloat(220f, 16f);
        }

        public void SetActive(bool active) => _active = active;
        public void SetAds(bool ads) => _ads = ads;
        public void SetSprinting(bool sprinting) => _sprinting = sprinting;

        public void AddRecoilKick(float back, float up) =>
            _recoilOffset.Impulse(new Vector3(0f, up, -back));

        public void ApplyMouseDelta(float mouseX, float mouseY)
        {
            if (!_active)
                return;

            float scale = _ads ? 0.35f : 1f;
            Vector3 target = new Vector3(-mouseY * 0.15f, -mouseX * 0.15f, 0f) * scale;
            _swayRot.Update(target, Time.deltaTime);
            _swayPos.Update(target * 0.002f, Time.deltaTime);
        }

        public void ApplyPostPose(Vector3 basePos, Quaternion baseRot)
        {
            if (!_active || _weaponRoot == null)
                return;

            var settings = FirstPersonSettings.Runtime;
            FirstPersonWeaponProfile profile = _weaponView != null ? _weaponView.GetActiveProfile() : null;
            float dt = Time.deltaTime;

            float speed = 0f;
            if (_player != null)
            {
                speed = Vector3.Distance(_player.position, _lastPlayerPos) / Mathf.Max(dt, 0.0001f);
                _lastPlayerPos = _player.position;
            }

            float bobScale = settings.enableWeaponBob ? (_ads ? 0.2f : 1f) * (profile != null ? profile.bobScale : 1f) : 0f;
            _bobPhase += speed * 0.35f;
            float bobTarget = Mathf.Sin(_bobPhase * Mathf.PI * 2f) * 0.025f * bobScale;
            float bobY = _bob.Update(bobTarget, dt);

            float strafe = 0f;
            if (_player != null)
            {
                var motor = _player.GetComponent<Motiong.vCharacterController.vThirdPersonController>();
                if (motor != null)
                    strafe = motor.input.x;
            }

            float roll = _strafeRoll.Update(-strafe * 4f * (_ads ? 0.3f : 1f), dt);

            float land = 0f;
            if (_rb != null && _rb.linearVelocity.y < -2f)
                land = Mathf.Clamp01(-_rb.linearVelocity.y / 12f) * 0.04f;

            _recoilOffset.Update(Vector3.zero, dt);

            float wall = 0f;
            if (profile != null)
                wall = ProbeWallPullback(profile);

            float wallVal = _wallPullback.Update(wall, dt);

            Vector3 swayEuler = _swayRot.Current;
            Vector3 swayPos = _swayPos.Current;

            _motionOffset = swayPos + _recoilOffset.Current + new Vector3(0f, bobY - land, -wallVal);
            _motionEuler = swayEuler + new Vector3(0f, 0f, roll);

            _weaponRoot.localPosition = basePos + _motionOffset;
            _weaponRoot.localRotation = baseRot * Quaternion.Euler(_motionEuler);
        }

        private float ProbeWallPullback(FirstPersonWeaponProfile profile)
        {
            Camera cam = Camera.main;
            if (cam == null)
                return 0f;

            Vector3 origin = cam.transform.position;
            Vector3 dir = cam.transform.forward;
            if (Physics.SphereCast(origin, profile.wallProbeRadius, dir, out RaycastHit hit, profile.weaponLength, ~0, QueryTriggerInteraction.Ignore))
            {
                if (hit.collider != null && _player != null && hit.collider.transform.IsChildOf(_player))
                    return 0f;
                return (1f - hit.distance / profile.weaponLength) * 0.25f;
            }

            return 0f;
        }
    }
}
