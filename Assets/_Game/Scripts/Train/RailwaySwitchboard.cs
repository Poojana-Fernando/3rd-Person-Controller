using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace Game.Train
{
    /// <summary>Replicates turnout lever positions for every <see cref="RailwayJunction"/> child.</summary>
    [DisallowMultipleComponent]
    public class RailwaySwitchboard : NetworkBehaviour
    {
        [SerializeField] private RailwayNetwork network;

        private readonly List<RailwayJunction> _junctions = new List<RailwayJunction>();
        private readonly NetworkList<byte> _positions = new NetworkList<byte>(
            null, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        public IReadOnlyList<RailwayJunction> Junctions => _junctions;

        private void Awake()
        {
            if (network == null)
                network = GetComponent<RailwayNetwork>();
            RefreshJunctionList();
        }

        public override void OnNetworkSpawn()
        {
            _positions.OnListChanged += HandleListChanged;
            if (IsServer)
                SyncListFromJunctions();
            else
                ApplyListToJunctions();
        }

        public override void OnNetworkDespawn()
        {
            _positions.OnListChanged -= HandleListChanged;
        }

        public void RefreshJunctionList()
        {
            _junctions.Clear();
            _junctions.AddRange(GetComponentsInChildren<RailwayJunction>(true));
            for (int i = 0; i < _junctions.Count; i++)
                _junctions[i].RegisterWithBoard(this, i);
        }

        public void PushLocalState(int index, bool diverging)
        {
            if (index < 0 || index >= _junctions.Count)
                return;

            if (IsSpawned && IsServer)
            {
                while (_positions.Count <= index)
                    _positions.Add(0);
                _positions[index] = diverging ? (byte)1 : (byte)0;
            }
            else if (!IsSpawned)
            {
                _junctions[index].ApplyStateFromNetwork(diverging);
            }
        }

        public bool TryRequestToggle(RailwayJunction junction, out string blockReason)
        {
            blockReason = null;
            if (junction == null)
            {
                blockReason = "No junction";
                return false;
            }

            int index = ResolveJunctionIndex(junction);
            if (index < 0)
            {
                blockReason = "Junction not registered on switchboard";
                return false;
            }

            if (junction.TryGetLeverBlockReason(out blockReason))
                return false;

            if (IsSpawned)
            {
                ToggleJunctionRpc(index);
                return true;
            }

            junction.SetDiverging(!junction.Diverging, false);
            return true;
        }

        public void RequestToggle(RailwayJunction junction)
        {
            TryRequestToggle(junction, out _);
        }

        private int ResolveJunctionIndex(RailwayJunction junction)
        {
            int index = _junctions.IndexOf(junction);
            if (index >= 0)
                return index;
            RefreshJunctionList();
            return _junctions.IndexOf(junction);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void ToggleJunctionRpc(int index)
        {
            if (index < 0 || index >= _junctions.Count)
                return;
            RailwayJunction j = _junctions[index];
            if (j == null || j.TryGetLeverBlockReason(out _))
                return;

            bool next = !j.Diverging;
            j.ApplyStateFromNetwork(next);
            while (_positions.Count <= index)
                _positions.Add(0);
            _positions[index] = next ? (byte)1 : (byte)0;
        }

        private void HandleListChanged(NetworkListEvent<byte> change)
        {
            ApplyListToJunctions();
        }

        private void SyncListFromJunctions()
        {
            _positions.Clear();
            for (int i = 0; i < _junctions.Count; i++)
                _positions.Add(_junctions[i].Diverging ? (byte)1 : (byte)0);
        }

        private void ApplyListToJunctions()
        {
            for (int i = 0; i < _junctions.Count; i++)
            {
                if (i < _positions.Count)
                {
                    _junctions[i].ApplyStateFromNetwork(_positions[i] != 0);
                    _junctions[i].ApplyVisuals();
                }
            }
        }
    }
}
