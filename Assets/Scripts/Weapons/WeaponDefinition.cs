using UnityEngine;

[CreateAssetMenu(menuName = "Weapons/Weapon Definition", fileName = "WeaponDefinition")]
public class WeaponDefinition : ScriptableObject
{
    [Header("Identity")]
    public string displayName = "Weapon";
    [Range(1, 4)] public int slot = 1;

    [Header("Visuals")]
    [Tooltip("Scene objects HoldGun{visualKey} / AimGun{visualKey} under weaponHand.")]
    public string visualKey = "AK";
    [Tooltip("0 = rifle Aim State, 1 = Glock Aim State.")]
    public int weaponAimStyle;
    public GameObject holdPrefab;
    public GameObject aimPrefab;
    public Vector3 handLocalPosition;
    public Vector3 handLocalEuler;
    public Vector3 handLocalScale = Vector3.one;
    public string muzzleChildName = "muzzle";

    [Header("Fire")]
    public bool isAutomatic = true;
    public float fireRateRpm = 600f;
    public float damage = 15f;
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
}
