using UnityEngine;
using UnityEngine.UI;


namespace DeadAuthorGames.ThirdPersonCombat
{
    public class PlayerWeaponHud : MonoBehaviour
    {
        [System.Serializable]
        private struct GunHudEntry
        {
            public int slot;
            public string panelName;
        }

        [SerializeField] private Transform gunsRoot;
        [SerializeField] private PlayerGunController gunController;
        [SerializeField] private WeaponInventory inventory;
        [SerializeField] private GunHudEntry[] entries =
        {
            new() { slot = 1, panelName = "AK47" },
            new() { slot = 2, panelName = "GLOCK" },
            new() { slot = 3, panelName = "M349" },
            new() { slot = 4, panelName = "MAC" },
        };

        private GameObject[] _panels;
        private Text[] _ammoTexts;

        private void Awake()
        {
            if (gunsRoot == null)
            {
                GameObject guns = GameObject.Find("Guns");
                if (guns != null)
                    gunsRoot = guns.transform;
            }

            if (gunController == null)
            {
                var local = Game.Net.NetworkPlayerRegistry.Instance?.LocalPlayer;
                gunController = local != null ? local.Gun : FindObjectOfType<PlayerGunController>();
            }

            if (inventory == null && gunController != null)
                inventory = gunController.GetComponent<WeaponInventory>();

            _panels = new GameObject[entries.Length];
            _ammoTexts = new Text[entries.Length];

            for (int i = 0; i < entries.Length; i++)
                CacheEntry(i);
        }

        public void BindPlayer(PlayerGunController gun, WeaponInventory inv)
        {
            gunController = gun;
            inventory = inv != null ? inv : gun != null ? gun.GetComponent<WeaponInventory>() : null;
        }

        private void CacheEntry(int index)
        {
            if (gunsRoot == null)
                return;

            Transform panel = gunsRoot.Find(entries[index].panelName);
            if (panel == null)
            {
                Transform[] all = gunsRoot.GetComponentsInChildren<Transform>(true);
                for (int i = 0; i < all.Length; i++)
                {
                    if (all[i].name == entries[index].panelName)
                    {
                        panel = all[i];
                        break;
                    }
                }
            }

            if (panel == null)
                return;

            _panels[index] = panel.gameObject;
            _ammoTexts[index] = panel.Find("AmmoText")?.GetComponent<Text>();
            _panels[index].SetActive(false);
        }

        private void Update()
        {
            if (gunController == null || inventory == null)
                return;

            WeaponDefinition current = gunController.CurrentWeapon;
            bool equipped = gunController.IsEquipped;

            for (int i = 0; i < entries.Length; i++)
            {
                if (_panels[i] == null)
                    continue;

                WeaponDefinition weapon = inventory.Get(entries[i].slot);
                bool show = equipped && weapon != null && current == weapon;
                if (_panels[i].activeSelf != show)
                    _panels[i].SetActive(show);

                if (!show || weapon == null || _ammoTexts[i] == null)
                    continue;

                int mag = inventory.GetMag(weapon);
                int reserve = inventory.GetReserve(weapon);
                _ammoTexts[i].text = $"{mag} / {reserve}";
            }
        }
    }
}
