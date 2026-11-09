using System.Collections.Generic;
using UnityEngine;


namespace DeadAuthorGames.ThirdPersonCombat
{
    public class WeaponInventory : MonoBehaviour
    {
        public const int SlotCount = 4;

        private struct AmmoState
        {
            public int CurrentInMag;
            public int ReserveAmmo;
        }

        [SerializeField] private List<WeaponDefinition> startingWeapons = new List<WeaponDefinition>();

        private readonly WeaponDefinition[] _slots = new WeaponDefinition[SlotCount];
        private readonly Dictionary<WeaponDefinition, AmmoState> _ammo = new Dictionary<WeaponDefinition, AmmoState>();

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
            EnsureAmmo(definition);
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

        public int GetMag(WeaponDefinition definition)
        {
            if (definition == null)
                return 0;

            EnsureAmmo(definition);
            return _ammo[definition].CurrentInMag;
        }

        public int GetReserve(WeaponDefinition definition)
        {
            if (definition == null)
                return 0;

            EnsureAmmo(definition);
            return _ammo[definition].ReserveAmmo;
        }

        public bool HasAmmoInMag(WeaponDefinition definition)
        {
            return GetMag(definition) > 0;
        }

        public bool TryConsumeRound(WeaponDefinition definition)
        {
            if (definition == null)
                return false;

            EnsureAmmo(definition);
            AmmoState state = _ammo[definition];
            if (state.CurrentInMag <= 0)
                return false;

            state.CurrentInMag--;
            _ammo[definition] = state;
            return true;
        }

        public bool CanReload(WeaponDefinition definition)
        {
            if (definition == null)
                return false;

            EnsureAmmo(definition);
            AmmoState state = _ammo[definition];
            if (state.ReserveAmmo <= 0)
                return false;

            return state.CurrentInMag < Mathf.Max(1, definition.magazineSize);
        }

        public void CollectWeapons(List<WeaponDefinition> results)
        {
            if (results == null)
                return;

            for (int i = 0; i < startingWeapons.Count; i++)
            {
                WeaponDefinition definition = startingWeapons[i];
                if (definition != null && !results.Contains(definition))
                    results.Add(definition);
            }

            for (int slot = 1; slot <= SlotCount; slot++)
            {
                WeaponDefinition definition = Get(slot);
                if (definition != null && !results.Contains(definition))
                    results.Add(definition);
            }
        }

        public void ApplyReload(WeaponDefinition definition)
        {
            if (definition == null)
                return;

            EnsureAmmo(definition);
            AmmoState state = _ammo[definition];
            int magSize = Mathf.Max(1, definition.magazineSize);
            int needed = magSize - state.CurrentInMag;
            if (needed <= 0)
                return;

            int transfer = Mathf.Min(needed, state.ReserveAmmo);
            state.CurrentInMag += transfer;
            state.ReserveAmmo -= transfer;
            _ammo[definition] = state;
        }

        private void EnsureAmmo(WeaponDefinition definition)
        {
            if (_ammo.ContainsKey(definition))
                return;

            int magSize = Mathf.Max(1, definition.magazineSize);
            _ammo[definition] = new AmmoState
            {
                CurrentInMag = magSize,
                ReserveAmmo = Mathf.Max(0, definition.startingReserveAmmo)
            };
        }

        private static int SlotToIndex(int slot)
        {
            if (slot < 1 || slot > SlotCount)
                return -1;

            return slot - 1;
        }
    }
}
