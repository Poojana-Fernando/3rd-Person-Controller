using UnityEngine;

public class EnemyHealth : MonoBehaviour, IDamageable
{
    [SerializeField] private float maxHealth = 100f;
    [SerializeField] private EnemyRagdoll ragdoll;
    [SerializeField] private EnemyHealthBar healthBar;
    [SerializeField] private GameObject bloodHitPrefab;
    [SerializeField] private float bloodEffectLifetime = 3f;
    [SerializeField] private bool spawnBloodOnCorpseHits = true;

    private float _currentHealth;
    private bool _isDead;

    public float MaxHealth => maxHealth;
    public float CurrentHealth => _currentHealth;
    public bool IsDead => _isDead;
    public float NormalizedHealth => maxHealth > 0f ? Mathf.Clamp01(_currentHealth / maxHealth) : 0f;

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
            ragdoll?.ApplyImpulse(hitPoint, -hitNormal, amount, true);
            return;
        }

        SpawnBloodHit(hitPoint, hitNormal);

        _currentHealth = Mathf.Max(0f, _currentHealth - amount);
        healthBar?.SetHealth(NormalizedHealth, false);

        if (_currentHealth <= 0f)
        {
            Die(hitPoint, hitNormal, amount);
            return;
        }

        ragdoll?.PlayHitReaction(hitPoint, -hitNormal, amount, hitbox);
    }

    private void Die(Vector3 hitPoint, Vector3 hitNormal, float amount)
    {
        if (_isDead)
            return;

        _isDead = true;
        _currentHealth = 0f;
        healthBar?.SetHealth(0f, false);
        healthBar?.SetVisible(false);
        ragdoll?.ActivateRagdoll(hitPoint, -hitNormal, amount);
    }

    private void SpawnBloodHit(Vector3 hitPoint, Vector3 hitNormal)
    {
        if (bloodHitPrefab == null)
            return;

        Vector3 forward = hitNormal.sqrMagnitude > 0.0001f ? hitNormal : Vector3.up;
        GameObject fx = Instantiate(bloodHitPrefab, hitPoint, Quaternion.LookRotation(forward));
        Destroy(fx, Mathf.Max(0.5f, bloodEffectLifetime));
    }

#if UNITY_EDITOR
    public void EditorBind(EnemyRagdoll enemyRagdoll, EnemyHealthBar bar, float health, GameObject bloodPrefab)
    {
        ragdoll = enemyRagdoll;
        healthBar = bar;
        maxHealth = health;
        if (bloodPrefab != null)
            bloodHitPrefab = bloodPrefab;
    }
#endif
}
