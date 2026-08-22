using UnityEngine;
using UnityEngine.UI;

public class WeaponPickupInteractor : MonoBehaviour
{
    [SerializeField] private WeaponInventory inventory;
    [SerializeField] private PlayerGunController gunController;
    [SerializeField] private Text promptText;
    [SerializeField] private KeyCode pickupKey = KeyCode.F;
    [SerializeField] private float interactRadius = 2.5f;

    private WeaponPickup _nearest;

    private void Awake()
    {
        if (inventory == null)
            inventory = GetComponent<WeaponInventory>();
        if (gunController == null)
            gunController = GetComponent<PlayerGunController>();
    }

    private void Update()
    {
        _nearest = FindNearestPickup();
        UpdatePrompt();

        if (_nearest == null || !Input.GetKeyDown(pickupKey))
            return;

        TryPickup(_nearest);
    }

    private WeaponPickup FindNearestPickup()
    {
        WeaponPickup best = null;
        float bestDistance = float.MaxValue;
        Vector3 origin = transform.position;

        var pickups = WeaponPickup.All;
        for (int i = 0; i < pickups.Count; i++)
        {
            WeaponPickup pickup = pickups[i];
            if (pickup == null || !pickup.isActiveAndEnabled || pickup.Weapon == null)
                continue;

            float radius = Mathf.Max(interactRadius, pickup.PickupRadius);
            float distance = Vector3.Distance(origin, pickup.transform.position);
            if (distance > radius || distance >= bestDistance)
                continue;

            bestDistance = distance;
            best = pickup;
        }

        return best;
    }

    private void UpdatePrompt()
    {
        if (promptText == null)
            return;

        bool show = _nearest != null;
        if (promptText.gameObject.activeSelf != show)
            promptText.gameObject.SetActive(show);

        if (show)
            promptText.text = _nearest.PromptText;
    }

    private void TryPickup(WeaponPickup pickup)
    {
        if (inventory == null || pickup.Weapon == null)
            return;

        if (!inventory.TryAdd(pickup.Weapon) && !inventory.Has(pickup.Weapon.slot))
            return;

        if (gunController != null)
            gunController.SelectSlot(pickup.Weapon.slot);

        Destroy(pickup.gameObject);
    }

    public void SetPromptText(Text text)
    {
        promptText = text;
    }
}
