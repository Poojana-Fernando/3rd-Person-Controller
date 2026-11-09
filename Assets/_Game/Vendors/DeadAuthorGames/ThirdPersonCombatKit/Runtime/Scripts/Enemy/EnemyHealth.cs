using System;
using System.Collections;
using UnityEngine;


namespace DeadAuthorGames.ThirdPersonCombat
{
    public class EnemyHealth : MonoBehaviour, IDamageable, IPartHealthPool
    {
        public enum DeathMode
        {
            RagdollImmediate,
            ScriptedThenRagdoll,
            AnimationOnly
        }

        [SerializeField] private float maxHealth = 100f;
        [SerializeField] private DeathMode deathMode = DeathMode.RagdollImmediate;
        [SerializeField] private EnemyRagdoll ragdoll;
        [SerializeField] private EnemyHealthBar healthBar;
        [SerializeField] private GameObject bloodHitPrefab;
        [SerializeField] private float bloodEffectLifetime = 3f;
        [SerializeField] private bool spawnBloodOnCorpseHits = true;

        [SerializeField] private float scriptedDeathFailsafeSeconds = 2.75f;

        private float _currentHealth;
        private bool _isDead;
        private bool _awaitingScriptedDeathCompletion;
        private Vector3 _lastDeathHitPoint;
        private Vector3 _lastDeathHitNormal;
        private float _lastDeathAmount;
        private Coroutine _scriptedDeathFailsafeRoutine;

        public event Action OnDeathStarted;

        public float MaxHealth => maxHealth;
        public float CurrentHealth => _currentHealth;
        public bool IsDead => _isDead;
        public float NormalizedHealth => maxHealth > 0f ? Mathf.Clamp01(_currentHealth / maxHealth) : 0f;
        public DeathMode CurrentDeathMode => deathMode;
        public bool AwaitingScriptedDeathCompletion => _awaitingScriptedDeathCompletion;

        private void Awake()
        {
            if (ragdoll == null)
                ragdoll = GetComponent<EnemyRagdoll>();
            if (healthBar == null)
                healthBar = GetComponentInChildren<EnemyHealthBar>(true);

            ResetHealth();
        }

        private void OnEnable()
        {
            if (!_isDead && _currentHealth <= 0f)
                ResetHealth();
        }

        private void ResetHealth()
        {
            _isDead = false;
            _awaitingScriptedDeathCompletion = false;
            _currentHealth = Mathf.Max(1f, maxHealth);
            healthBar?.SetVisible(true);
            healthBar?.SetHealth(NormalizedHealth, true);
        }

        public void TakeDamage(float amount, Vector3 hitPoint, Vector3 hitNormal, GameObject source)
        {
            ReceiveDamage(amount, hitPoint, hitNormal, source, null);
        }

        public void ReceiveDamage(
            float amount,
            Vector3 hitPoint,
            Vector3 hitNormal,
            GameObject source,
            EnemyHitbox hitbox)
        {
            if (amount <= 0f)
                return;

            if (_isDead)
            {
                if (spawnBloodOnCorpseHits)
                    SpawnBloodHit(hitPoint, hitNormal);
                if (UsesRagdoll())
                    ragdoll?.ApplyImpulse(hitPoint, -hitNormal, amount, true);
                return;
            }

            var networkHealth = GetComponent<Game.Net.NetworkHealth>();
            if (networkHealth != null && networkHealth.IsSpawned)
            {
                SpawnBloodHit(hitPoint, hitNormal);
                if (!networkHealth.IsServer)
                    return;

                networkHealth.ServerApplyDamage(amount);
                return;
            }

            SpawnBloodHit(hitPoint, hitNormal);
            ApplyHealthLoss(amount, hitPoint, hitNormal, hitbox);
        }

        /// <summary>
        /// Subtracts a body-part share from overall HP without spawning blood
        /// (EnemyDismemberment owns organic VFX for that path).
        /// </summary>
        public void ApplyPartLoss(float amount, Vector3 hitPoint, Vector3 hitNormal, GameObject source)
        {
            if (amount <= 0f || _isDead)
                return;

            var networkHealth = GetComponent<Game.Net.NetworkHealth>();
            if (networkHealth != null && networkHealth.IsSpawned)
            {
                if (!networkHealth.IsServer)
                    return;

                networkHealth.ServerApplyDamage(amount);
                return;
            }

            ApplyHealthLoss(amount, hitPoint, hitNormal, null);
        }

        /// <summary>Called by NetworkHealth when replicated HP changes.</summary>
        public void ApplyNetworkState(float current, float max, bool dead)
        {
            maxHealth = Mathf.Max(1f, max);
            bool wasDead = _isDead;
            _currentHealth = Mathf.Clamp(current, 0f, maxHealth);
            healthBar?.SetHealth(NormalizedHealth, false);

            if (dead && !wasDead)
            {
                _isDead = true;
                healthBar?.SetVisible(false);
                OnDeathStarted?.Invoke();
                if (deathMode == DeathMode.RagdollImmediate)
                    ragdoll?.ActivateRagdoll(transform.position + Vector3.up, Vector3.up, 10f);
            }
            else if (!dead)
            {
                _isDead = false;
                healthBar?.SetVisible(true);
            }
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
            healthBar?.SetVisible(true);
            healthBar?.SetHealth(NormalizedHealth, false);
        }

        private void ApplyHealthLoss(float amount, Vector3 hitPoint, Vector3 hitNormal, EnemyHitbox hitbox)
        {
            _currentHealth = Mathf.Max(0f, _currentHealth - amount);
            healthBar?.SetHealth(NormalizedHealth, false);

            if (_currentHealth <= 0f)
            {
                Die(hitPoint, hitNormal, amount);
                return;
            }

            if (deathMode == DeathMode.RagdollImmediate)
                ragdoll?.PlayHitReaction(hitPoint, -hitNormal, amount, hitbox);
        }

        private bool UsesRagdoll()
        {
            return deathMode != DeathMode.AnimationOnly;
        }

        private void Die(Vector3 hitPoint, Vector3 hitNormal, float amount)
        {
            if (_isDead)
                return;

            _isDead = true;
            _currentHealth = 0f;
            healthBar?.SetHealth(0f, false);
            healthBar?.SetVisible(false);

            if (deathMode == DeathMode.ScriptedThenRagdoll || deathMode == DeathMode.AnimationOnly)
            {
                _awaitingScriptedDeathCompletion = deathMode == DeathMode.ScriptedThenRagdoll;
                _lastDeathHitPoint = hitPoint;
                _lastDeathHitNormal = hitNormal;
                _lastDeathAmount = amount;
                OnDeathStarted?.Invoke();

                if (_awaitingScriptedDeathCompletion)
                    BeginScriptedDeathFailsafe();
                return;
            }

            ragdoll?.ActivateRagdoll(hitPoint, -hitNormal, amount);
        }

        public void CompleteScriptedDeath()
        {
            CompleteScriptedDeath(_lastDeathHitPoint, _lastDeathHitNormal, _lastDeathAmount);
        }

        public void CompleteScriptedDeath(Vector3 hitPoint, Vector3 hitNormal, float amount)
        {
            if (deathMode == DeathMode.AnimationOnly)
            {
                _awaitingScriptedDeathCompletion = false;
                StopScriptedDeathFailsafe();
                return;
            }

            if (!_awaitingScriptedDeathCompletion)
                return;

            _awaitingScriptedDeathCompletion = false;
            StopScriptedDeathFailsafe();

            if (ragdoll == null)
                ragdoll = GetComponent<EnemyRagdoll>();

            if (ragdoll == null)
            {
                Debug.LogError($"[EnemyHealth] CompleteScriptedDeath on '{name}' but no EnemyRagdoll was found.", this);
                return;
            }

            ragdoll.ActivateRagdoll(hitPoint, -hitNormal, amount);
        }

        private void BeginScriptedDeathFailsafe()
        {
            StopScriptedDeathFailsafe();
            _scriptedDeathFailsafeRoutine = StartCoroutine(ScriptedDeathFailsafeRoutine());
        }

        private void StopScriptedDeathFailsafe()
        {
            if (_scriptedDeathFailsafeRoutine == null)
                return;

            StopCoroutine(_scriptedDeathFailsafeRoutine);
            _scriptedDeathFailsafeRoutine = null;
        }

        private IEnumerator ScriptedDeathFailsafeRoutine()
        {
            yield return new WaitForSecondsRealtime(Mathf.Max(0.5f, scriptedDeathFailsafeSeconds));
            _scriptedDeathFailsafeRoutine = null;
            if (_awaitingScriptedDeathCompletion)
                CompleteScriptedDeath();
        }

        private void SpawnBloodHit(Vector3 hitPoint, Vector3 hitNormal)
        {
            // EnemyDismemberment owns organic hit VFX when present.
            if (GetComponent<EnemyDismemberment>() != null)
                return;

            if (bloodHitPrefab == null)
                return;

            Vector3 forward = hitNormal.sqrMagnitude > 0.0001f ? hitNormal : Vector3.up;
            GameObject fx = Instantiate(bloodHitPrefab, hitPoint, Quaternion.LookRotation(forward));
            Destroy(fx, Mathf.Max(0.5f, bloodEffectLifetime));
        }

    #if UNITY_EDITOR
        public void EditorBind(EnemyRagdoll enemyRagdoll, EnemyHealthBar bar, float health, GameObject bloodPrefab)
        {
            EditorBind(enemyRagdoll, bar, health, bloodPrefab, deathMode);
        }

        public void EditorBind(EnemyRagdoll enemyRagdoll, EnemyHealthBar bar, float health, GameObject bloodPrefab, DeathMode mode)
        {
            ragdoll = enemyRagdoll;
            healthBar = bar;
            maxHealth = health;
            deathMode = mode;
            if (bloodPrefab != null)
                bloodHitPrefab = bloodPrefab;
        }
    #endif
    }
}
