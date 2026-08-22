using System.Collections.Generic;
using UnityEngine;

public class WeaponPickup : MonoBehaviour
{
    private static readonly List<WeaponPickup> ActivePickups = new List<WeaponPickup>();

    [SerializeField] private WeaponDefinition weapon;
    [SerializeField] private float pickupRadius = 2.5f;
    [SerializeField] private float spinSpeed = 45f;

    public WeaponDefinition Weapon => weapon;
    public float PickupRadius => pickupRadius;
    public string PromptText =>
        weapon != null ? $"Press F to pick up {weapon.displayName}" : "Press F to pick up";

    public static IReadOnlyList<WeaponPickup> All => ActivePickups;

    private void OnEnable()
    {
        if (!ActivePickups.Contains(this))
            ActivePickups.Add(this);
    }

    private void OnDisable()
    {
        ActivePickups.Remove(this);
    }

    private void Update()
    {
        if (spinSpeed != 0f)
            transform.Rotate(0f, spinSpeed * Time.deltaTime, 0f, Space.World);
    }

    public void Configure(WeaponDefinition definition, float radius = 2.5f)
    {
        weapon = definition;
        pickupRadius = radius;
    }
}
