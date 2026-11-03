using DeadAuthorGames.ThirdPersonCombat;
using Motiong.vCharacterController;
using Unity.Netcode;
using UnityEngine;

namespace Game.Net
{
    /// <summary>
    /// Owner-written weapon slot / aim state so remote peers show the same gun and pose.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public class NetworkWeaponState : NetworkBehaviour
    {
        private readonly NetworkVariable<int> _equippedSlot = new NetworkVariable<int>(
            0,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Owner);

        private readonly NetworkVariable<bool> _isAiming = new NetworkVariable<bool>(
            false,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Owner);

        private readonly NetworkVariable<Vector3> _aimPoint = new NetworkVariable<Vector3>(
            Vector3.zero,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Owner);

        private readonly NetworkVariable<short> _lookPitch = new NetworkVariable<short>(
            0,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Owner);

        private PlayerGunController _gun;
        private UpperBodyAim _upperBodyAim;
        private vThirdPersonCamera _tpCamera;
        private float _aimSendTimer;
        private float _pitchSendTimer;
        private float _remotePitch;
        private float _remotePitchVel;

        public int EquippedSlot => _equippedSlot.Value;
        public bool IsAiming => _isAiming.Value;
        public Vector3 AimPoint => _aimPoint.Value;

        private void Awake()
        {
            _gun = GetComponent<PlayerGunController>();
            _upperBodyAim = GetComponent<UpperBodyAim>();
        }

        public override void OnNetworkSpawn()
        {
            if (_gun != null)
                _gun.networkWeaponState = this;

            _equippedSlot.OnValueChanged += OnSlotOrAimChanged;
            _isAiming.OnValueChanged += OnSlotOrAimChanged;
            _aimPoint.OnValueChanged += OnAimPointChanged;
            _lookPitch.OnValueChanged += OnLookPitchChanged;

            if (!IsOwner)
                ApplyRemoteVisuals();
        }

        public override void OnNetworkDespawn()
        {
            _equippedSlot.OnValueChanged -= OnSlotOrAimChanged;
            _isAiming.OnValueChanged -= OnSlotOrAimChanged;
            _aimPoint.OnValueChanged -= OnAimPointChanged;
            _lookPitch.OnValueChanged -= OnLookPitchChanged;

            if (_gun != null && _gun.networkWeaponState == this)
                _gun.networkWeaponState = null;
        }

        private void Update()
        {
            if (!IsOwner || _gun == null)
                return;

            if (_tpCamera == null && _gun.thirdPersonCamera != null)
                _tpCamera = _gun.thirdPersonCamera;

            int slot = 0;
            if (_gun.IsEquipped && _gun.CurrentWeapon != null)
                slot = _gun.CurrentWeapon.slot;

            if (_equippedSlot.Value != slot)
                _equippedSlot.Value = slot;

            if (_isAiming.Value != _gun.IsAiming)
                _isAiming.Value = _gun.IsAiming;

            _pitchSendTimer -= Time.deltaTime;
            if (_pitchSendTimer <= 0f)
            {
                _pitchSendTimer = 1f / 30f;
                if (_tpCamera != null)
                {
                    _tpCamera.ExportLook(out _, out float pitch);
                    short compressed = CompressPitch(pitch);
                    if (_lookPitch.Value != compressed)
                        _lookPitch.Value = compressed;
                }
            }

            if (!_gun.IsAiming)
                return;

            _aimSendTimer -= Time.deltaTime;
            if (_aimSendTimer > 0f)
                return;

            _aimSendTimer = 1f / 30f;
            if (_gun.TryGetNetworkAimPoint(out Vector3 point))
                _aimPoint.Value = point;
        }

        private void LateUpdate()
        {
            if (IsOwner || _upperBodyAim == null)
                return;

            float target = DecompressPitch(_lookPitch.Value);
            _remotePitch = Mathf.SmoothDamp(_remotePitch, target, ref _remotePitchVel, 0.08f);
            ApplyRemoteLookPitch(_remotePitch);
        }

        private void OnSlotOrAimChanged(int previous, int current)
        {
            if (!IsOwner)
                ApplyRemoteVisuals();
        }

        private void OnSlotOrAimChanged(bool previous, bool current)
        {
            if (!IsOwner)
                ApplyRemoteVisuals();
        }

        private void OnAimPointChanged(Vector3 previous, Vector3 current)
        {
            if (IsOwner || _upperBodyAim == null || !_isAiming.Value)
                return;

            ApplyRemoteAimPitch(current);
        }

        private void OnLookPitchChanged(short previous, short current)
        {
            if (IsOwner || _upperBodyAim == null)
                return;

            ApplyRemoteLookPitch(DecompressPitch(current));
        }

        private void ApplyRemoteVisuals()
        {
            if (_gun == null)
                return;

            bool equipped = _equippedSlot.Value > 0;
            _gun.ApplyRemoteState(_equippedSlot.Value, equipped, _isAiming.Value);

            ApplyRemoteLookPitch(DecompressPitch(_lookPitch.Value));
            if (_isAiming.Value)
                ApplyRemoteAimPitch(_aimPoint.Value);
        }

        private void ApplyRemoteLookPitch(float pitch)
        {
            if (_upperBodyAim == null)
                return;

            _upperBodyAim.ClampPitch(ref pitch);
            _upperBodyAim.CurrentPitch = pitch;
            if (_gun != null && _gun.IsEquipped)
            {
                _upperBodyAim.SetAimingActive(true);
                if (_gun.CurrentWeapon != null)
                    _upperBodyAim.SetWeaponAimStyle(_gun.CurrentWeapon.weaponAimStyle);
            }
        }

        private void ApplyRemoteAimPitch(Vector3 worldAimPoint)
        {
            if (_upperBodyAim == null)
                return;

            Vector3 origin = transform.position + Vector3.up * 1.4f;
            Vector3 dir = worldAimPoint - origin;
            if (dir.sqrMagnitude < 0.0001f)
                return;

            float pitch = -Mathf.Asin(Mathf.Clamp(dir.normalized.y, -1f, 1f)) * Mathf.Rad2Deg;
            ApplyRemoteLookPitch(pitch);
        }

        private static short CompressPitch(float pitch) =>
            (short)Mathf.Clamp(Mathf.RoundToInt(pitch * 100f), short.MinValue, short.MaxValue);

        private static float DecompressPitch(short value) => value / 100f;
    }
}
