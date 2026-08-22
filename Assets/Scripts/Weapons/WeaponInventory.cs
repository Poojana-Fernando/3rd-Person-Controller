using System.Collections.Generic;
using UnityEngine;

public class WeaponInventory : MonoBehaviour
{
    public const int SlotCount = 4;

    [SerializeField] private List<WeaponDefinition> startingWeapons = new List<WeaponDefinition>();

    private readonly WeaponDefinition[] _slots = new WeaponDefinition[SlotCount];

    private void Awake()
    {
        for (int i = 0; i < startingWeapons.Count; i++)
            TryAdd(startingWeapons[i]);
    }

    public bool TryAdd(WeaponDefinition definition)
    {
        if (definition == null)
            return false;

        int index = SlotToIndex(definition.slot);
        if (index < 0)
            return false;

        if (_slots[index] == definition)
            return false;

        _slots[index] = definition;
        return true;
    }

    public bool Has(int slot)
    {
        int index = SlotToIndex(slot);
        return index >= 0 && _slots[index] != null;
    }

    public WeaponDefinition Get(int slot)
    {
        int index = SlotToIndex(slot);
        return index >= 0 ? _slots[index] : null;
    }

    private static int SlotToIndex(int slot)
    {
        if (slot < 1 || slot > SlotCount)
            return -1;

        return slot - 1;
    }
}
