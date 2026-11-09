using DeadAuthorGames.ThirdPersonCombat;
using Unity.Netcode;
using UnityEngine;

namespace Game.Net
{
    /// <summary>
    /// Server-side downed/respawn flow. Owner teleports to a random spawn on respawn.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public class PlayerRespawnController : NetworkBehaviour
    {
        [SerializeField] private float respawnDelay = 5f;
        [SerializeField] private Transform[] spawnPoints;

        private NetworkHealth _health;
        private PlayerHealth _playerHealth;
        private NetworkPlayerSetup _setup;
        private float _respawnAt = -1f;
        private bool _downed;

        private readonly NetworkVariable<bool> _isDowned = new NetworkVariable<bool>(
            false,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        public bool IsDowned => _isDowned.Value;

        private void Awake()
        {
            _health = GetComponent<NetworkHealth>();
            _playerHealth = GetComponent<PlayerHealth>();
            _setup = GetComponent<NetworkPlayerSetup>();
        }

        public override void OnNetworkSpawn()
        {
            if (_playerHealth != null)
                _playerHealth.OnDied += HandleDied;

            _isDowned.OnValueChanged += OnDownedChanged;
        }

        public override void OnNetworkDespawn()
        {
            if (_playerHealth != null)
                _playerHealth.OnDied -= HandleDied;
            _isDowned.OnValueChanged -= OnDownedChanged;
        }

        private void Update()
        {
            if (!IsServer || !_downed || _respawnAt < 0f)
                return;

            if (Time.time < _respawnAt)
                return;

            Respawn();
        }

        private void HandleDied()
        {
            if (!IsServer)
                return;

            _downed = true;
            _isDowned.Value = true;
            _respawnAt = Time.time + respawnDelay;

            if (NetworkPlayerRegistry.Instance != null && NetworkPlayerRegistry.Instance.AliveCount <= 0)
                Debug.LogWarning("[PlayerRespawnController] All players down — session failed.");
        }

        private void Respawn()
        {
            _downed = false;
            _isDowned.Value = false;
            _respawnAt = -1f;

            _health?.ServerReset();
            _playerHealth?.ResetHealth();

            // Owner must teleport (owner-authoritative NetworkTransform).
            RespawnOwnerClientRpc();
        }

        [ClientRpc]
        private void RespawnOwnerClientRpc()
        {
            if (!IsOwner)
                return;

            if (_setup != null)
                _setup.OwnerRespawnAtRandom();
            else
            {
                Vector3 pos = PlayerSpawnUtility.FindSpawnPosition(transform.position);
                PlayerSpawnUtility.TeleportOwner(NetworkObject, pos);
            }

            var input = GetComponent<Motiong.vCharacterController.vThirdPersonInput>();
            if (input != null) input.enabled = true;
            var gun = GetComponent<PlayerGunController>();
            if (gun != null) gun.enabled = true;
            var parkour = GetComponent<ParkourController>();
            if (parkour != null) parkour.enabled = true;
            var aim = GetComponent<UpperBodyAim>();
            if (aim != null) aim.enabled = true;
        }

        private void OnDownedChanged(bool previous, bool current)
        {
        }

        public void SetSpawnPoints(Transform[] points)
        {
            spawnPoints = points;
        }
    }
}
