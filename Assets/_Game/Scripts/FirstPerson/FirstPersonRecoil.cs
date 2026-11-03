using UnityEngine;

namespace Game.FirstPerson
{
    [DefaultExecutionOrder(1005)]
    public class FirstPersonRecoil : MonoBehaviour
    {
        public float recoverySpeed = 14f;

        private FirstPersonCameraController _cameraCtrl;
        private FirstPersonWeaponMotion _weaponMotion;
        private bool _active;

        public void Bind(
            FirstPersonCameraController cameraCtrl,
            FirstPersonWeaponMotion weaponMotion,
            FirstPersonWeaponView weaponView)
        {
            _cameraCtrl = cameraCtrl;
            _weaponMotion = weaponMotion;
        }

        public void SetActive(bool active) => _active = active;

        public void ApplyShot(bool ads, FirstPersonWeaponProfile profile)
        {
            if (!_active || _cameraCtrl == null)
                return;

            if (profile == null)
                profile = ScriptableObject.CreateInstance<FirstPersonWeaponProfile>();

            float m = ads ? profile.adsRecoilScale : 1f;
            float yaw = Random.Range(-profile.viewKickYawRange, profile.viewKickYawRange) * m;
            float roll = Random.Range(-profile.viewKickRollRange, profile.viewKickRollRange) * m;
            _cameraCtrl.ApplyViewRecoil(profile.viewKickPitch * m, yaw, roll);
            _cameraCtrl.ApplyAimPunch(profile, ads);

            _weaponMotion?.AddRecoilKick(profile.weaponBackKick * m, profile.weaponUpKick * m);
        }

        private void LateUpdate()
        {
            if (!_active || _cameraCtrl == null)
                return;

            _cameraCtrl.LateUpdateRecoilRecovery(recoverySpeed);
        }
    }
}
