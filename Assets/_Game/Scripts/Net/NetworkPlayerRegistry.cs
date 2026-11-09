using System.Collections.Generic;
using UnityEngine;

namespace Game.Net
{
    /// <summary>
    /// Local registry of spawned networked players. Not a NetworkObject.
    /// </summary>
    public class NetworkPlayerRegistry : MonoBehaviour
    {
        public static NetworkPlayerRegistry Instance { get; private set; }

        private readonly List<NetworkPlayerSetup> _players = new List<NetworkPlayerSetup>();

        public IReadOnlyList<NetworkPlayerSetup> Players => _players;

        public NetworkPlayerSetup LocalPlayer
        {
            get
            {
                for (int i = 0; i < _players.Count; i++)
                {
                    NetworkPlayerSetup p = _players[i];
                    if (p != null && p.IsOwner)
                        return p;
                }

                return null;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Instance = null;
        }

        private void Awake()
        {
            // Keep the persistent registry (and its registered players) when a scene brings its own copy.
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }

            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        public static NetworkPlayerRegistry Ensure()
        {
            if (Instance != null)
                return Instance;

            GameObject go = new GameObject("NetworkPlayerRegistry");
            DontDestroyOnLoad(go);
            return go.AddComponent<NetworkPlayerRegistry>();
        }

        public void Register(NetworkPlayerSetup player)
        {
            if (player == null || _players.Contains(player))
                return;
            _players.Add(player);
        }

        public void Unregister(NetworkPlayerSetup player)
        {
            _players.Remove(player);
        }

        public NetworkPlayerSetup GetNearestAlive(Vector3 from)
        {
            NetworkPlayerSetup best = null;
            float bestDist = float.MaxValue;
            for (int i = 0; i < _players.Count; i++)
            {
                NetworkPlayerSetup p = _players[i];
                if (p == null || !p.IsAlive)
                    continue;

                float d = (p.transform.position - from).sqrMagnitude;
                if (d >= bestDist)
                    continue;

                bestDist = d;
                best = p;
            }

            return best;
        }

        public int AliveCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < _players.Count; i++)
                {
                    if (_players[i] != null && _players[i].IsAlive)
                        count++;
                }

                return count;
            }
        }

        public int PlayerCount => _players.Count;
    }
}
