using UnityEngine;

[RequireComponent(typeof(Collider))]
public class EnemyHitbox : MonoBehaviour, IDamageable
{
    [SerializeField] private float damageMultiplier = 1f;
    [SerializeField] private EnemyHealth health;

    public float DamageMultiplier => damageMultiplier;
    public EnemyHealth Health => health;

    private void Awake()
    {
        if (health == null)
            health = GetComponentInParent<EnemyHealth>();
    }

    public void TakeDamage(float amount, Vector3 hitPoint, Vector3 hitNormal, GameObject source)
    {
        if (health == null)
            health = GetComponentInParent<EnemyHealth>();
        if (health == null)
            return;

        health.ReceiveDamage(amount * damageMultiplier, hitPoint, hitNormal, source, this);
    }

#if UNITY_EDITOR
    public void EditorBind(EnemyHealth enemyHealth, float multiplier)
    {
        health = enemyHealth;
        damageMultiplier = multiplier;
    }
#endif
}
