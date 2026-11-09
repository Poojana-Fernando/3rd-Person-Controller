using System;
using UnityEngine;


namespace DeadAuthorGames.ThirdPersonCombat
{
    public class PlayerHealth : MonoBehaviour, IDamageable, IPartHealthPool
    {
        [SerializeField] private float maxHealth = 100f;
        [SerializeField] private PlayerDamageFeedback damageFeedback;

        private float _currentHealth;
        private bool _isDead;

        public float MaxHealth => maxHealth;
        public float CurrentHealth => _currentHealth;
        public bool IsDead => _isDead;
        public float NormalizedHealth => maxHealth > 0f ? Mathf.Clamp01(_currentHealth / maxHealth) : 0f;

        public event Action<float, float> OnHealthChanged;
        public event Action OnDied;

        private void Awake()
        {
            if (damageFeedback == null)
                damageFeedback = FindAnyObjectByType<PlayerDamageFeedback>();

            ResetHealth();
        }

        public void ResetHealth()
        {
            _isDead = false;
            _currentHealth = Mathf.Max(1f, maxHealth);
            OnHealthChanged?.Invoke(_currentHealth, maxHealth);
        }

        public void TakeDamage(float amount, Vector3 hitPoint, Vector3 hitNormal, GameObject source)
        {
            ApplyDamageInternal(amount);
        }

        public void ApplyPartLoss(float amount, Vector3 hitPoint, Vector3 hitNormal, GameObject source)
        {
            ApplyDamageInternal(amount);
        }

        public void RestorePartHealth(float amount)
        {
            if (amount <= 0f || _isDead)
                return;

            var networkHealth = GetComponent<Game.Net.NetworkHealth>();
            if (networkHealth != null && networkHealth.IsSpawned)
            {
                if (networkHealth.IsServer)
                    networkHealth.ServerRestore(amount);
                return;
            }

            _currentHealth = Mathf.Min(maxHealth, _currentHealth + amount);
            OnHealthChanged?.Invoke(_currentHealth, maxHealth);
        }

        private void ApplyDamageInternal(float amount)
        {
            if (amount <= 0f || _isDead)
                return;

            // When networked, only the server mutates HP via NetworkHealth.
            var networkHealth = GetComponent<Game.Net.NetworkHealth>();
            if (networkHealth != null && networkHealth.IsSpawned)
            {
                if (!networkHealth.IsServer)
                    return;

                networkHealth.ServerApplyDamage(amount);
                return;
            }

            _currentHealth = Mathf.Max(0f, _currentHealth - amount);
            OnHealthChanged?.Invoke(_currentHealth, maxHealth);

            if (damageFeedback != null)
                damageFeedback.PlayDamageFlash();
            else
            {
                PlayerDamageFeedback feedback = FindAnyObjectByType<PlayerDamageFeedback>();
                feedback?.PlayDamageFlash();
            }

            if (_currentHealth <= 0f)
            {
                _isDead = true;
                OnDied?.Invoke();
            }
        }

        /// <summary>Called by NetworkHealth on every peer when replicated HP changes.</summary>
        public void ApplyNetworkState(float current, float max, bool dead)
        {
            maxHealth = Mathf.Max(1f, max);
            float previous = _currentHealth;
            bool wasDead = _isDead;
            _currentHealth = Mathf.Clamp(current, 0f, maxHealth);
            _isDead = dead;

            if (!Mathf.Approximately(previous, _currentHealth))
            {
                OnHealthChanged?.Invoke(_currentHealth, maxHealth);
                if (_currentHealth < previous)
                {
                    if (damageFeedback != null)
                        damageFeedback.PlayDamageFlash();
                }
            }

            if (dead && !wasDead)
                OnDied?.Invoke();
        }

#if UNITY_EDITOR
        public void EditorBind(PlayerDamageFeedback feedback, float health)
        {
            damageFeedback = feedback;
            maxHealth = health;
        }
#endif
    }
}
