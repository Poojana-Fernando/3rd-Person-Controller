using UnityEngine;


namespace DeadAuthorGames.ThirdPersonCombat
{
    [RequireComponent(typeof(Collider))]
    public class EnemyHitbox : MonoBehaviour, IDamageable, IBulletImpactReceiver
    {
        [SerializeField] private float damageMultiplier = 1f;
        [SerializeField] private EnemyHealth health;
        [SerializeField] private EnemyDismemberment dismemberment;

        public float DamageMultiplier => damageMultiplier;
        public EnemyHealth Health => health;

        private void Awake()
        {
            ResolveRefs();
        }

        public void TakeDamage(float amount, Vector3 hitPoint, Vector3 hitNormal, GameObject source)
        {
            ResolveRefs();
            float scaled = amount * damageMultiplier;

            if (dismemberment != null)
            {
                dismemberment.HandleHitboxDamage(
                    this,
                    scaled,
                    hitPoint,
                    hitNormal,
                    -hitNormal,
                    0f,
                    source);
                return;
            }

            if (health == null)
                return;

            health.ReceiveDamage(scaled, hitPoint, hitNormal, source, this);
        }

        public void ReceiveBulletImpact(in BulletImpactData impact)
        {
            ResolveRefs();
            float scaled = impact.Damage * damageMultiplier;

            if (dismemberment != null)
            {
                dismemberment.HandleHitboxDamage(
                    this,
                    scaled,
                    impact.HitPoint,
                    impact.HitNormal,
                    impact.ShotDirection,
                    impact.ImpactForce,
                    impact.Source);
                return;
            }

            if (health == null)
                return;

            health.ReceiveDamage(scaled, impact.HitPoint, impact.HitNormal, impact.Source, this);
        }

        private void ResolveRefs()
        {
            if (health == null)
                health = GetComponentInParent<EnemyHealth>();
            if (dismemberment == null)
                dismemberment = GetComponentInParent<EnemyDismemberment>();
        }

#if UNITY_EDITOR
        public void EditorBind(EnemyHealth enemyHealth, float multiplier)
        {
            health = enemyHealth;
            damageMultiplier = multiplier;
            dismemberment = enemyHealth != null
                ? enemyHealth.GetComponent<EnemyDismemberment>()
                : GetComponentInParent<EnemyDismemberment>();
        }
#endif
    }
}
