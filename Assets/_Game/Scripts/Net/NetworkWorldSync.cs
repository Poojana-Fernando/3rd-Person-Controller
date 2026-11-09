using System.Collections.Generic;
using System.Text;
using DeadAuthorGames.ThirdPersonCombat;
using Unity.Netcode;
using UnityEngine;

namespace Game.Net
{
    /// <summary>
    /// Syncs non-NetworkObject scene props (explosives, weapon pickups, dismemberable targets) via stable IDs.
    /// Attach to an in-scene NetworkObject (e.g. NetworkWorldSync root).
    /// </summary>
    public class NetworkWorldSync : NetworkBehaviour
    {
        private readonly NetworkList<int> _destroyedIds = new NetworkList<int>();
        private readonly NetworkList<int> _pickedIds = new NetworkList<int>();
        private readonly NetworkList<int> _sceneSeveredDismemberIds = new NetworkList<int>();
        private readonly NetworkList<int> _sceneSeveredPartIndices = new NetworkList<int>();

        private readonly Dictionary<int, ExplosiveDamageable> _explosives = new Dictionary<int, ExplosiveDamageable>();
        private readonly Dictionary<int, WeaponPickup> _pickups = new Dictionary<int, WeaponPickup>();
        private readonly Dictionary<int, EnemyDismemberment> _sceneDismembers = new Dictionary<int, EnemyDismemberment>();
        private readonly Dictionary<ExplosiveDamageable, int> _explosiveToId = new Dictionary<ExplosiveDamageable, int>();
        private readonly Dictionary<WeaponPickup, int> _pickupToId = new Dictionary<WeaponPickup, int>();
        private readonly Dictionary<EnemyDismemberment, int> _dismemberToId = new Dictionary<EnemyDismemberment, int>();

        [SerializeField] private NetworkObject severedPartPrefab;

        public static NetworkWorldSync Instance { get; private set; }

        public override void OnNetworkSpawn()
        {
            Instance = this;
            BuildRegistries();

            _destroyedIds.OnListChanged += OnDestroyedChanged;
            _pickedIds.OnListChanged += OnPickedChanged;

            for (int i = 0; i < _destroyedIds.Count; i++)
                ApplyDestroyed(_destroyedIds[i], Vector3.zero, false);
            for (int i = 0; i < _pickedIds.Count; i++)
                ApplyPicked(_pickedIds[i], default, false);

            // Late joiners: hide already-severed scene parts (NetworkSeveredPart objects replicate separately).
            if (!IsServer)
            {
                int n = Mathf.Min(_sceneSeveredDismemberIds.Count, _sceneSeveredPartIndices.Count);
                for (int i = 0; i < n; i++)
                    ApplyScenePartLost(_sceneSeveredDismemberIds[i], _sceneSeveredPartIndices[i], Vector3.zero, Vector3.up, Vector3.up, 0f, false);
            }
        }

        public override void OnNetworkDespawn()
        {
            _destroyedIds.OnListChanged -= OnDestroyedChanged;
            _pickedIds.OnListChanged -= OnPickedChanged;
            if (Instance == this)
                Instance = null;
        }

        public bool TryGetSceneDismemberment(int id, out EnemyDismemberment dismemberment)
        {
            return _sceneDismembers.TryGetValue(id, out dismemberment) && dismemberment != null;
        }

        public int GetSceneDismemberId(EnemyDismemberment dismemberment)
        {
            if (dismemberment == null)
                return 0;
            return _dismemberToId.TryGetValue(dismemberment, out int id) ? id : 0;
        }

        /// <summary>
        /// Server path for scene Targets that have EnemyDismemberment but no NetworkDismemberment.
        /// </summary>
        public void ServerNotifyScenePartLost(
            EnemyDismemberment dismemberment,
            int partIndex,
            Vector3 hitPoint,
            Vector3 hitNormal,
            Vector3 shotDirection,
            float impactForce)
        {
            if (!IsServer || dismemberment == null || partIndex < 0)
                return;
            if (!_dismemberToId.TryGetValue(dismemberment, out int id))
                return;

            for (int i = 0; i < _sceneSeveredDismemberIds.Count && i < _sceneSeveredPartIndices.Count; i++)
            {
                if (_sceneSeveredDismemberIds[i] == id && _sceneSeveredPartIndices[i] == partIndex)
                    return;
            }

            _sceneSeveredDismemberIds.Add(id);
            _sceneSeveredPartIndices.Add(partIndex);

            LoseScenePartClientRpc(id, partIndex, hitPoint, hitNormal, shotDirection, impactForce);
            SpawnSceneSeveredPart(dismemberment, id, partIndex, hitPoint, shotDirection, impactForce);
        }

        private void SpawnSceneSeveredPart(
            EnemyDismemberment dismemberment,
            int sceneDismemberId,
            int partIndex,
            Vector3 hitPoint,
            Vector3 shotDirection,
            float impactForce)
        {
            NetworkObject prefab = ResolveSeveredPrefab();
            if (prefab == null || dismemberment == null)
                return;

            string matchKey = dismemberment.GetPartMatchKey(partIndex);
            string displayName = dismemberment.GetPartDisplayName(partIndex);

            NetworkObject instance = Instantiate(prefab, hitPoint, Quaternion.identity);
            var severed = instance.GetComponent<NetworkSeveredPart>();
            if (severed == null)
                severed = instance.gameObject.AddComponent<NetworkSeveredPart>();

            Vector3 impulse = shotDirection.sqrMagnitude > 0.0001f
                ? shotDirection.normalized * Mathf.Max(0f, impactForce)
                : Vector3.up * Mathf.Max(0f, impactForce);

            severed.PrepareBeforeSpawn(
                NetworkObject,
                partIndex,
                matchKey,
                displayName,
                impulse,
                sceneDismemberId);
            instance.Spawn(true);
        }

        private NetworkObject ResolveSeveredPrefab()
        {
            if (severedPartPrefab != null)
                return severedPartPrefab;

            var nm = NetworkManager.Singleton;
            if (nm == null)
                return null;

            foreach (var entry in nm.NetworkConfig.Prefabs.Prefabs)
            {
                if (entry?.Prefab == null)
                    continue;
                if (entry.Prefab.GetComponent<NetworkSeveredPart>() != null)
                {
                    severedPartPrefab = entry.Prefab.GetComponent<NetworkObject>();
                    return severedPartPrefab;
                }
            }

            return null;
        }

        private void BuildRegistries()
        {
            _explosives.Clear();
            _pickups.Clear();
            _sceneDismembers.Clear();
            _explosiveToId.Clear();
            _pickupToId.Clear();
            _dismemberToId.Clear();

            var explosives = Object.FindObjectsByType<ExplosiveDamageable>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            System.Array.Sort(explosives, CompareTransformPath);
            for (int i = 0; i < explosives.Length; i++)
            {
                int id = StableId(explosives[i].transform, i);
                _explosives[id] = explosives[i];
                _explosiveToId[explosives[i]] = id;
                explosives[i].networkWorldSync = this;
                explosives[i].networkWorldId = id;
            }

            var pickups = Object.FindObjectsByType<WeaponPickup>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            System.Array.Sort(pickups, CompareTransformPath);
            for (int i = 0; i < pickups.Length; i++)
            {
                int id = StableId(pickups[i].transform, 100000 + i);
                _pickups[id] = pickups[i];
                _pickupToId[pickups[i]] = id;
            }

            // Scene mannequins / Targets: EnemyDismemberment without a networked owner.
            var dismembers = Object.FindObjectsByType<EnemyDismemberment>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            System.Array.Sort(dismembers, CompareTransformPath);
            int sceneSalt = 200000;
            for (int i = 0; i < dismembers.Length; i++)
            {
                EnemyDismemberment d = dismembers[i];
                if (d == null)
                    continue;

                var netDim = d.GetComponent<NetworkDismemberment>();
                if (netDim != null)
                    continue;

                int id = StableId(d.transform, sceneSalt + i);
                _sceneDismembers[id] = d;
                _dismemberToId[d] = id;
            }
        }

        private static int CompareTransformPath(Component a, Component b)
        {
            return string.CompareOrdinal(GetPath(a.transform), GetPath(b.transform));
        }

        private static string GetPath(Transform t)
        {
            var sb = new StringBuilder();
            while (t != null)
            {
                sb.Insert(0, "/" + t.name);
                t = t.parent;
            }
            return sb.ToString();
        }

        private static int StableId(Transform t, int salt)
        {
            Vector3 p = t.position;
            unchecked
            {
                int hash = GetPath(t).GetHashCode();
                hash = (hash * 397) ^ Mathf.RoundToInt(p.x * 10f);
                hash = (hash * 397) ^ Mathf.RoundToInt(p.y * 10f);
                hash = (hash * 397) ^ Mathf.RoundToInt(p.z * 10f);
                hash = (hash * 397) ^ salt;
                return hash;
            }
        }

        public bool TryServerDamageExplosive(ExplosiveDamageable explosive, float amount, Vector3 hitPoint, GameObject source)
        {
            if (!IsServer || explosive == null)
                return false;
            if (!_explosiveToId.TryGetValue(explosive, out int id))
                return false;

            explosive.ServerApplyDamage(amount, hitPoint, source, out bool detonated, out Vector3 epicenter);
            if (!detonated)
                return true;

            if (!_destroyedIds.Contains(id))
                _destroyedIds.Add(id);

            ExplodeClientRpc(id, epicenter);
            return true;
        }

        public void RequestWeaponPickup(WeaponPickup pickup, NetworkObject picker)
        {
            if (pickup == null || picker == null || !picker.IsOwner)
                return;
            if (!_pickupToId.TryGetValue(pickup, out int id))
                return;
            PickupServerRpc(id, picker);
        }

        [ServerRpc(RequireOwnership = false)]
        private void PickupServerRpc(int id, NetworkObjectReference pickerRef, ServerRpcParams rpcParams = default)
        {
            if (!_pickups.TryGetValue(id, out WeaponPickup pickup) || pickup == null || pickup.Weapon == null)
                return;
            if (_pickedIds.Contains(id))
                return;
            if (!pickerRef.TryGet(out NetworkObject picker) || picker == null)
                return;
            if (picker.OwnerClientId != rpcParams.Receive.SenderClientId)
                return;

            float dist = Vector3.Distance(picker.transform.position, pickup.transform.position);
            if (dist > Mathf.Max(2.5f, pickup.PickupRadius) + 0.5f)
                return;

            var inventory = picker.GetComponent<WeaponInventory>();
            if (inventory == null)
                return;
            if (!inventory.TryAdd(pickup.Weapon) && !inventory.Has(pickup.Weapon.slot))
                return;

            var gun = picker.GetComponent<PlayerGunController>();
            gun?.SelectSlot(pickup.Weapon.slot);

            _pickedIds.Add(id);
            PickupClientRpc(id, picker);
        }

        [ClientRpc]
        private void ExplodeClientRpc(int id, Vector3 epicenter)
        {
            if (IsServer)
                return;
            ApplyDestroyed(id, epicenter, true);
        }

        [ClientRpc]
        private void PickupClientRpc(int id, NetworkObjectReference pickerRef)
        {
            ApplyPicked(id, pickerRef, true);
        }

        [ClientRpc]
        private void LoseScenePartClientRpc(
            int dismemberId,
            int partIndex,
            Vector3 hitPoint,
            Vector3 hitNormal,
            Vector3 shotDirection,
            float impactForce)
        {
            if (IsServer)
                return;
            ApplyScenePartLost(dismemberId, partIndex, hitPoint, hitNormal, shotDirection, impactForce, true);
        }

        private void OnDestroyedChanged(NetworkListEvent<int> changeEvent)
        {
            if (changeEvent.Type == NetworkListEvent<int>.EventType.Add)
                ApplyDestroyed(changeEvent.Value, Vector3.zero, false);
        }

        private void OnPickedChanged(NetworkListEvent<int> changeEvent)
        {
            if (changeEvent.Type == NetworkListEvent<int>.EventType.Add)
                ApplyPicked(changeEvent.Value, default, false);
        }

        private void ApplyScenePartLost(
            int dismemberId,
            int partIndex,
            Vector3 hitPoint,
            Vector3 hitNormal,
            Vector3 shotDirection,
            float impactForce,
            bool withEffects)
        {
            if (!_sceneDismembers.TryGetValue(dismemberId, out EnemyDismemberment dismemberment) || dismemberment == null)
                return;

            if (withEffects)
                dismemberment.ReplayLosePartVisual(partIndex, hitPoint, hitNormal, shotDirection, impactForce);
            else
                dismemberment.ForceHidePart(partIndex);
        }

        private void ApplyDestroyed(int id, Vector3 epicenter, bool playFx)
        {
            if (!_explosives.TryGetValue(id, out ExplosiveDamageable explosive) || explosive == null)
                return;
            if (explosive.IsDestroyed)
                return;

            explosive.ClientDetonateVisual(epicenter, playFx);
        }

        private void ApplyPicked(int id, NetworkObjectReference pickerRef, bool addWeapon)
        {
            if (!_pickups.TryGetValue(id, out WeaponPickup pickup) || pickup == null)
                return;

            WeaponDefinition weapon = pickup.Weapon;
            if (addWeapon && pickerRef.TryGet(out NetworkObject picker) && picker != null && weapon != null)
            {
                var inventory = picker.GetComponent<WeaponInventory>();
                inventory?.TryAdd(weapon);
                if (!IsServer)
                {
                    var gun = picker.GetComponent<PlayerGunController>();
                }
            }

            if (pickup.gameObject != null)
                Destroy(pickup.gameObject);
            _pickups.Remove(id);
        }

        public void SetSeveredPartPrefab(NetworkObject prefab)
        {
            severedPartPrefab = prefab;
        }
    }
}
