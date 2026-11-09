using DeadAuthorGames.ThirdPersonCombat;
using Unity.Netcode;
using UnityEngine;

namespace Game.Net
{
    /// <summary>
    /// Server decides part loss; clients hide meshes. Fallen pieces are NetworkSeveredPart objects.
    /// Pickup reattaches to the picker when MatchKey matches a missing part (cross-player).
    /// </summary>
    [RequireComponent(typeof(EnemyDismemberment))]
    public class NetworkDismemberment : NetworkBehaviour
    {
        [SerializeField] private NetworkObject severedPartPrefab;

        private readonly NetworkVariable<int> _severedMask = new NetworkVariable<int>(
            0,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private EnemyDismemberment _dismemberment;

        private void Awake()
        {
            _dismemberment = GetComponent<EnemyDismemberment>();
        }

        public override void OnNetworkSpawn()
        {
            if (_dismemberment != null)
                _dismemberment.networkDismemberment = this;

            _severedMask.OnValueChanged += OnMaskChanged;

            if (!IsServer)
                ApplyMaskVisuals(_severedMask.Value, false);
        }

        public override void OnNetworkDespawn()
        {
            _severedMask.OnValueChanged -= OnMaskChanged;
            if (_dismemberment != null && _dismemberment.networkDismemberment == this)
                _dismemberment.networkDismemberment = null;
        }

        public bool IsServerAuthority => IsSpawned && IsServer;

        public bool HasMissingPart(string matchKey)
        {
            return _dismemberment != null && _dismemberment.HasMissingPart(matchKey);
        }

        public void ServerNotifyPartLost(
            int partIndex,
            Vector3 hitPoint,
            Vector3 hitNormal,
            Vector3 shotDirection,
            float impactForce)
        {
            if (!IsServer || partIndex < 0 || partIndex >= 31)
                return;

            int bit = 1 << partIndex;
            if ((_severedMask.Value & bit) != 0)
                return;

            _severedMask.Value |= bit;
            LosePartClientRpc(partIndex, hitPoint, hitNormal, shotDirection, impactForce);
            SpawnSeveredPart(partIndex, hitPoint, shotDirection, impactForce);
        }

        private void SpawnSeveredPart(int partIndex, Vector3 hitPoint, Vector3 shotDirection, float impactForce)
        {
            if (severedPartPrefab == null || _dismemberment == null)
                return;

            string matchKey = _dismemberment.GetPartMatchKey(partIndex);
            string displayName = _dismemberment.GetPartDisplayName(partIndex);

            NetworkObject instance = Instantiate(severedPartPrefab, hitPoint, Quaternion.identity);
            var severed = instance.GetComponent<NetworkSeveredPart>();
            if (severed == null)
                severed = instance.gameObject.AddComponent<NetworkSeveredPart>();

            Vector3 impulse = shotDirection.sqrMagnitude > 0.0001f
                ? shotDirection.normalized * Mathf.Max(0f, impactForce)
                : Vector3.up * Mathf.Max(0f, impactForce);

            // Pending meta is applied inside OnNetworkSpawn so the spawn snapshot includes PartIndex.
            severed.PrepareBeforeSpawn(NetworkObject, partIndex, matchKey, displayName, impulse);
            instance.Spawn(true);
        }

        /// <summary>
        /// Owner requests reattach of a networked severed part onto THIS player (matching missing part).
        /// </summary>
        public void RequestReattachFromPickup(NetworkObjectReference severedRef)
        {
            if (!IsOwner)
                return;
            ReattachFromPickupServerRpc(severedRef);
        }

        [ServerRpc]
        private void ReattachFromPickupServerRpc(NetworkObjectReference severedRef, ServerRpcParams rpcParams = default)
        {
            if (_dismemberment == null)
                return;

            if (!severedRef.TryGet(out NetworkObject severedObj) || severedObj == null)
                return;

            var severed = severedObj.GetComponent<NetworkSeveredPart>();
            if (severed == null)
                return;

            string matchKey = severed.MatchKey;
            if (string.IsNullOrEmpty(matchKey) || !_dismemberment.HasMissingPart(matchKey))
                return;

            float distance = Vector3.Distance(transform.position, severedObj.transform.position);
            if (distance > 2.75f)
                return;

            if (!_dismemberment.TryReattachPartNetwork(matchKey, out int partIndex))
                return;

            if (partIndex >= 0 && partIndex < 31)
            {
                int bit = 1 << partIndex;
                _severedMask.Value &= ~bit;
            }

            ReattachClientRpc(partIndex);
            severedObj.Despawn(true);
        }

        [ClientRpc]
        private void LosePartClientRpc(
            int partIndex,
            Vector3 hitPoint,
            Vector3 hitNormal,
            Vector3 shotDirection,
            float impactForce)
        {
            if (IsServer)
                return;

            _dismemberment?.ReplayLosePartVisual(partIndex, hitPoint, hitNormal, shotDirection, impactForce);
        }

        [ClientRpc]
        private void ReattachClientRpc(int partIndex)
        {
            if (IsServer)
                return;

            _dismemberment?.ReplayReattachVisual(partIndex);
        }

        private void OnMaskChanged(int previous, int current)
        {
            if (IsServer)
                return;
            ApplyMaskVisuals(current, true);
        }

        private void ApplyMaskVisuals(int mask, bool withEffects)
        {
            if (_dismemberment == null)
                return;

            for (int i = 0; i < 31; i++)
            {
                bool severed = (mask & (1 << i)) != 0;
                if (!severed)
                    continue;

                if (withEffects)
                    _dismemberment.ReplayLosePartVisual(i, transform.position, Vector3.up, Vector3.up, 1f);
                else
                    _dismemberment.ForceHidePart(i);
            }
        }

        public void SetSeveredPartPrefab(NetworkObject prefab)
        {
            severedPartPrefab = prefab;
        }
    }
}
