using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.Net
{
    /// <summary>
    /// Wires NetworkManager connection approval (max 4) and loads the gameplay scene on host start.
    /// Attach to the NetworkManager object in Bootstrap.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class NetworkGameBootstrap : MonoBehaviour
    {
        public const string GameplaySceneName = "Level01";
        public const int MaxPlayers = 4;

        [SerializeField] private string gameplaySceneName = GameplaySceneName;
        [SerializeField] private int maxPlayers = MaxPlayers;

        private NetworkManager _networkManager;
        private bool _wired;

        private void Awake()
        {
            TryWire();
        }

        private void OnEnable()
        {
            TryWire();
        }

        private void Start()
        {
            // Fallback if NetworkManager.Singleton was not ready during Awake (script order).
            TryWire();
        }

        private void OnDestroy()
        {
            Unwire();
        }

        private void TryWire()
        {
            if (_wired)
                return;

            _networkManager = NetworkManager.Singleton;
            if (_networkManager == null)
                _networkManager = GetComponent<NetworkManager>();
            if (_networkManager == null)
                _networkManager = FindAnyObjectByType<NetworkManager>();

            if (_networkManager == null)
            {
                Debug.LogWarning("[NetworkGameBootstrap] NetworkManager not found yet; will retry.");
                return;
            }

            _networkManager.ConnectionApprovalCallback = ApprovalCheck;
            _networkManager.OnServerStarted += OnServerStarted;
            _networkManager.OnClientConnectedCallback += OnClientConnected;
            _wired = true;
            Debug.Log("[NetworkGameBootstrap] Connection approval and scene callbacks wired.");
        }

        private void Unwire()
        {
            if (!_wired || _networkManager == null)
                return;

            _networkManager.OnServerStarted -= OnServerStarted;
            _networkManager.OnClientConnectedCallback -= OnClientConnected;
            if (_networkManager.ConnectionApprovalCallback == ApprovalCheck)
                _networkManager.ConnectionApprovalCallback = null;

            _wired = false;
            _networkManager = null;
        }

        private void ApprovalCheck(
            NetworkManager.ConnectionApprovalRequest request,
            NetworkManager.ConnectionApprovalResponse response)
        {
            NetworkManager nm = _networkManager != null ? _networkManager : NetworkManager.Singleton;
            int connected = nm != null ? nm.ConnectedClientsIds.Count : 0;
            bool approve = connected < maxPlayers;
            response.Approved = approve;
            response.CreatePlayerObject = approve;
            response.Pending = false;
            if (!approve)
                response.Reason = "Session is full (max 4 players).";
        }

        private void OnServerStarted()
        {
            NetworkManager nm = _networkManager != null ? _networkManager : NetworkManager.Singleton;
            if (nm == null || !nm.IsServer)
                return;

            if (nm.SceneManager == null)
                return;

            string active = SceneManager.GetActiveScene().name;
            if (active == gameplaySceneName)
                return;

            nm.SceneManager.LoadScene(gameplaySceneName, LoadSceneMode.Single);
        }

        private void OnClientConnected(ulong clientId)
        {
            Debug.Log($"[NetworkGameBootstrap] Client connected: {clientId}");
        }
    }
}
