using DeadAuthorGames.ThirdPersonCombat;
using UnityEngine;

namespace Game.FirstPerson
{
    public class FirstPersonWeaponView : MonoBehaviour
    {
        private Transform _weaponRoot;
        private Transform _player;
        private PlayerGunController _gun;
        private FirstPersonCameraController _cameraCtrl;
        private GameObject _spawned;
        private WeaponDefinition _weapon;
        private Transform _sight;
        private Transform _rightGrip;
        private Transform _leftGrip;
        private Transform _muzzle;
        private float _equipBlend;
        private float _reloadBlend;
        private bool _active;

        public Vector3 LastLocalPosition { get; private set; }
        public Quaternion LastLocalRotation { get; private set; } = Quaternion.identity;

        public Transform RightGrip => _rightGrip;
        public Transform LeftGrip => _leftGrip;
        public Transform Muzzle => _muzzle;
        public Transform Sight => _sight;

        public void Bind(Transform weaponRoot, Transform player, FirstPersonCameraController cameraCtrl)
        {
            _weaponRoot = weaponRoot;
            _player = player;
            _cameraCtrl = cameraCtrl;
            _gun = player != null ? player.GetComponent<PlayerGunController>() : null;
        }

        public void SetActive(bool active)
        {
            _active = active;
            if (!active)
                ClearSpawned();
        }

        public void SetReloading(bool reloading)
        {
            _reloadBlend = reloading ? 1f : 0f;
        }

        public FirstPersonWeaponProfile GetActiveProfile()
        {
            if (_weapon == null)
                return null;
            return _weapon.firstPersonProfile != null
                ? _weapon.firstPersonProfile
                : ScriptableObject.CreateInstance<FirstPersonWeaponProfile>();
        }

        public void LateUpdatePose(bool ads, bool sprinting, Camera worldCamera)
        {
            if (!_active || _weaponRoot == null)
                return;

            WeaponDefinition current = _gun != null ? _gun.CurrentWeapon : null;
            if (_gun == null || !_gun.IsEquipped || current == null)
            {
                ClearSpawned();
                return;
            }

            if (_weapon != current)
                SpawnForWeapon(current);

            if (_spawned == null)
                return;

            FirstPersonWeaponProfile profile = GetActiveProfile();
            _equipBlend = Mathf.MoveTowards(_equipBlend, 1f, Time.deltaTime / 0.25f);

            Vector3 hipPos = profile.hipLocalPosition;
            Vector3 hipEuler = profile.hipLocalEuler;
            Vector3 adsPos = profile.adsLocalPosition;
            Vector3 adsEuler = profile.adsLocalEuler;

            if (ads && _sight != null && worldCamera != null)
                SolveAdsPose(profile, worldCamera, ref adsPos, ref adsEuler);

            float adsT = ads ? 1f : 0f;
            if (profile.adsTime > 0.01f)
                adsT = ads ? Mathf.MoveTowards(GetAdsBlend(), 1f, Time.deltaTime / profile.adsTime) : 0f;
            SetAdsBlend(adsT);

            Vector3 pos = Vector3.Lerp(hipPos, adsPos, adsT);
            Vector3 euler = Vector3.Lerp(hipEuler, adsEuler, adsT);

            if (sprinting && !ads)
            {
                pos = profile.sprintLocalPosition;
                euler = profile.sprintLocalEuler;
            }

            if (_reloadBlend > 0.001f)
            {
                pos += new Vector3(0f, -0.08f * _reloadBlend, -0.05f * _reloadBlend);
                euler += new Vector3(12f * _reloadBlend, 0f, 0f);
            }

            pos = Vector3.Lerp(new Vector3(0f, -0.4f, 0.2f), pos, _equipBlend);

            if (!ads && worldCamera != null)
                ApplyHipConvergence(profile, worldCamera, ref euler);

            LastLocalPosition = pos;
            LastLocalRotation = Quaternion.Euler(euler);
            _weaponRoot.localPosition = pos;
            _weaponRoot.localRotation = LastLocalRotation;
        }

        private float _adsBlendInternal;

        private float GetAdsBlend() => _adsBlendInternal;
        private void SetAdsBlend(float v) => _adsBlendInternal = v;

        private void ApplyHipConvergence(FirstPersonWeaponProfile profile, Camera cam, ref Vector3 euler)
        {
            if (profile.hipConvergeDegrees <= 0.01f)
                return;

            Ray ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            Vector3 to = ray.direction;
            Vector3 gunForward = _weaponRoot.TransformDirection(Vector3.forward);
            Quaternion delta = Quaternion.FromToRotation(gunForward, to);
            delta.ToAngleAxis(out float angle, out Vector3 axis);
            angle = Mathf.Clamp(angle, 0f, profile.hipConvergeDegrees);
            euler += (Quaternion.AngleAxis(angle, axis) * _weaponRoot.localRotation).eulerAngles - _weaponRoot.localRotation.eulerAngles;
        }

        private void SolveAdsPose(
            FirstPersonWeaponProfile profile,
            Camera cam,
            ref Vector3 adsPos,
            ref Vector3 adsEuler)
        {
            _weaponRoot.localPosition = profile.adsLocalPosition;
            _weaponRoot.localRotation = Quaternion.Euler(profile.adsLocalEuler);

            if (_sight == null)
                return;

            Vector3 sightWorld = _sight.position;
            Vector3 desired = cam.transform.position + cam.transform.forward * profile.eyeRelief;
            Vector3 delta = desired - sightWorld;
            adsPos = profile.adsLocalPosition + _weaponRoot.InverseTransformVector(delta);
        }

        private void SpawnForWeapon(WeaponDefinition weapon)
        {
            ClearSpawned();
            _weapon = weapon;
            _equipBlend = 0f;

            if (weapon.aimPrefab == null || _weaponRoot == null)
                return;

            _spawned = Instantiate(weapon.aimPrefab, _weaponRoot);
            _spawned.name = "ViewmodelGun";
            SetLayerRecursive(_spawned.transform, FirstPersonLayers.ViewmodelLayer);

            _sight = _spawned.transform.Find("Sight");
            _rightGrip = _spawned.transform.Find("RightGrip");
            _leftGrip = _spawned.transform.Find("LeftGrip");
            _muzzle = FindMuzzle(_spawned.transform, weapon.muzzleChildName);

            FirstPersonWeaponProfile profile = weapon.firstPersonProfile;
            if (_sight == null)
            {
                var s = new GameObject("Sight").transform;
                s.SetParent(_spawned.transform, false);
                s.localPosition = profile != null ? profile.defaultSightLocal : new Vector3(0f, 0.06f, 0.35f);
                _sight = s;
            }

            if (_rightGrip == null)
            {
                var g = new GameObject("RightGrip").transform;
                g.SetParent(_spawned.transform, false);
                g.localPosition = profile != null ? profile.defaultRightGripLocal : new Vector3(0.02f, -0.04f, 0.12f);
                _rightGrip = g;
            }

            if (_leftGrip == null)
            {
                var g = new GameObject("LeftGrip").transform;
                g.SetParent(_spawned.transform, false);
                g.localPosition = profile != null ? profile.defaultLeftGripLocal : new Vector3(-0.06f, -0.05f, 0.18f);
                _leftGrip = g;
            }
        }

        private static Transform FindMuzzle(Transform root, string childName)
        {
            Transform direct = root.Find(childName);
            if (direct != null)
                return direct;

            Transform[] all = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i].name.Equals(childName, System.StringComparison.OrdinalIgnoreCase))
                    return all[i];
            }

            return null;
        }

        private void ClearSpawned()
        {
            if (_spawned != null)
                Destroy(_spawned);
            _spawned = null;
            _weapon = null;
            _sight = _rightGrip = _leftGrip = _muzzle = null;
        }

        private static void SetLayerRecursive(Transform root, int layer)
        {
            root.gameObject.layer = layer;
            for (int i = 0; i < root.childCount; i++)
                SetLayerRecursive(root.GetChild(i), layer);
        }
    }
}
