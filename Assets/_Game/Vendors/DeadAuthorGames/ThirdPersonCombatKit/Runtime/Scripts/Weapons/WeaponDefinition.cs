using Game.FirstPerson;
using UnityEngine;


namespace DeadAuthorGames.ThirdPersonCombat
{
    [CreateAssetMenu(menuName = "Dead Author Games/Weapon Definition", fileName = "WeaponDefinition")]
    public class WeaponDefinition : ScriptableObject
    {
        [Header("Identity")]
        public string displayName = "Weapon";
        [Range(1, 4)] public int slot = 1;

        [Header("Visuals")]
        [Tooltip("Scene objects HoldGun{visualKey} / AimGun{visualKey} under weaponHand.")]
        public string visualKey = "Rifle";
        [Tooltip("0 = rifle aim style, 1 = pistol aim style.")]
        public int weaponAimStyle;
        public GameObject holdPrefab;
        public GameObject aimPrefab;
        public Vector3 handLocalPosition;
        public Vector3 handLocalEuler;
        public Vector3 handLocalScale = Vector3.one;
        public string muzzleChildName = "muzzle";
        [Tooltip("First-person viewmodel tuning (poses, ADS, recoil).")]
        public FirstPersonWeaponProfile firstPersonProfile;

        [Header("Fire")]
        public bool isAutomatic = true;
        public float fireRateRpm = 600f;
        public float damage = 15f;
        [Tooltip("Impulse applied to detachable mesh parts and similar impact receivers.")]
        public float impactForce = 8f;
        public float maxRange = 120f;
        public int pelletsPerShot = 1;
        public float spreadDegrees = 0.6f;
        public float tracerSpeed = 140f;

        [Header("Recoil")]
        public float recoilShoulderImpulse = 2.5f;
        public float recoilCameraKick = 0.8f;

        [Header("Effects / Audio")]
        public AudioClip fireClip;
        public GameObject tracerPrefab;
        public GameObject muzzleFlashPrefab;
        public float muzzleFlashScale = 5f;
        public GameObject hitPrefab;
        public GameObject decalPrefab;

        [Header("Ammo")]
        public int magazineSize = 30;
        public int startingReserveAmmo = 90;
        [Tooltip("Reload lock duration follows this clip length when assigned.")]
        public AnimationClip reloadAnimation;
        [Tooltip("Fallback reload duration when reloadAnimation is not assigned.")]
        public float reloadDuration = 2.2f;
        public AudioClip reloadClip;

        [Header("UI")]
        public GameObject hudPanel;
        public GameObject icon;
        [Tooltip("Optional aim crosshair. Leave empty to keep the default Crossair sprite.")]
        public Sprite aimCrosshair;
        [Tooltip("Optional aim crosshair size. Leave at 0,0 to keep the current Crossair size.")]
        public Vector2 aimCrosshairSize;
    }

}
