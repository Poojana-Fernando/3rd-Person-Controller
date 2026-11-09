using DeadAuthorGames.ThirdPersonCombat;
using Unity.Netcode;
using UnityEngine;

namespace Game.Net
{
    /// <summary>
    /// Server-authoritative HP replicated via NetworkVariables.
    /// </summary>
    public class NetworkHealth : NetworkBehaviour
    {
        private readonly NetworkVariable<float> _currentHealth = new NetworkVariable<float>(
            100f,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<bool> _isDead = new NetworkVariable<bool>(
            false,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        [SerializeField] private float maxHealth = 100f;

        private PlayerHealth _playerHealth;
        private EnemyHealth _enemyHealth;

        public float CurrentHealth => _currentHealth.Value;
        public float MaxHealth => maxHealth;
        public bool IsDead => _isDead.Value;
        public float NormalizedHealth => maxHealth > 0f ? Mathf.Clamp01(_currentHealth.Value / maxHealth) : 0f;

        private void Awake()
        {
            _playerHealth = GetComponent<PlayerHealth>();
            _enemyHealth = GetComponent<EnemyHealth>();
            if (_playerHealth != null)
                maxHealth = _playerHealth.MaxHealth;
            else if (_enemyHealth != null)
                maxHealth = _enemyHealth.MaxHealth;
        }

        public override void OnNetworkSpawn()
        {
            _currentHealth.OnValueChanged += OnHealthChanged;
            _isDead.OnValueChanged += OnDeadChanged;

            if (IsServer)
            {
                _currentHealth.Value = Mathf.Max(1f, maxHealth);
                _isDead.Value = false;
            }

            SyncLocalViews(_currentHealth.Value, _isDead.Value);
        }

        public override void OnNetworkDespawn()
        {
            _currentHealth.OnValueChanged -= OnHealthChanged;
            _isDead.OnValueChanged -= OnDeadChanged;
        }

        public void ServerApplyDamage(float amount)
        {
            if (!IsServer || amount <= 0f || _isDead.Value)
                return;

            float next = Mathf.Max(0f, _currentHealth.Value - amount);
            _currentHealth.Value = next;
            if (next <= 0f)
                _isDead.Value = true;
        }

        public void ServerRestore(float amount)
        {
            if (!IsServer || amount <= 0f || _isDead.Value)
                return;

            _currentHealth.Value = Mathf.Min(maxHealth, _currentHealth.Value + amount);
        }

        public void ServerReset()
        {
            if (!IsServer)
                return;

            _isDead.Value = false;
            _currentHealth.Value = Mathf.Max(1f, maxHealth);
        }

        public void ServerSetMaxHealth(float value)
        {
            maxHealth = Mathf.Max(1f, value);
            if (IsServer && !_isDead.Value)
                _currentHealth.Value = Mathf.Min(_currentHealth.Value, maxHealth);
        }

        private void OnHealthChanged(float previous, float current)
        {
            SyncLocalViews(current, _isDead.Value);
        }

        private void OnDeadChanged(bool previous, bool current)
        {
            SyncLocalViews(_currentHealth.Value, current);
        }

        private void SyncLocalViews(float current, bool dead)
        {
            _playerHealth?.ApplyNetworkState(current, maxHealth, dead);
            _enemyHealth?.ApplyNetworkState(current, maxHealth, dead);
        }
    }
}
