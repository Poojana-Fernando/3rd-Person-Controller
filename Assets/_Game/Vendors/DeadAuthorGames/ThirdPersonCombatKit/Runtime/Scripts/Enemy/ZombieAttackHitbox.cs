using UnityEngine;


namespace DeadAuthorGames.ThirdPersonCombat
{
    public class ZombieAttackHitbox : MonoBehaviour
    {
        [SerializeField] private ZombieAI zombieAI;
        [SerializeField] private float attackRadius = 1.35f;
        [SerializeField] private float forwardOffset = 0.75f;
        [SerializeField] private float heightOffset = 1f;
        [SerializeField] private float facingDotThreshold = 0.15f;
        [SerializeField] private LayerMask playerMask = ~0;

        private bool _hasHitThisSwing;

        private void Awake()
        {
            if (zombieAI == null)
                zombieAI = GetComponentInParent<ZombieAI>();
        }

        public void BeginAttackSwing()
        {
            _hasHitThisSwing = false;
        }

        public void OnAttackHit()
        {
            if (_hasHitThisSwing || zombieAI == null)
                return;

            Transform root = zombieAI.transform;
            Vector3 center = root.position + Vector3.up * heightOffset + root.forward * forwardOffset;
            Collider[] hits = Physics.OverlapSphere(center, attackRadius, playerMask, QueryTriggerInteraction.Ignore);

            for (int i = 0; i < hits.Length; i++)
            {
                Collider hitCollider = hits[i];
                if (hitCollider == null)
                    continue;

                PlayerHealth playerHealth = hitCollider.GetComponentInParent<PlayerHealth>();
                if (playerHealth == null || playerHealth.IsDead)
                    continue;

                Vector3 toPlayer = playerHealth.transform.position - root.position;
                toPlayer.y = 0f;
                if (toPlayer.sqrMagnitude > 0.0001f)
                {
                    float facing = Vector3.Dot(root.forward, toPlayer.normalized);
                    if (facing < facingDotThreshold)
                        continue;
                }

                _hasHitThisSwing = true;
                Vector3 hitPoint = hitCollider.ClosestPoint(center);
                Vector3 hitNormal = (hitPoint - center).normalized;
                if (hitNormal.sqrMagnitude < 0.0001f)
                    hitNormal = -root.forward;

                float damage = zombieAI.AttackDamage;
                EnemyDismemberment dismemberment = playerHealth.GetComponent<EnemyDismemberment>();
                if (dismemberment != null)
                {
                    EnemyHitbox hitbox = ResolvePlayerHitbox(hitCollider, playerHealth, hitPoint);
                    dismemberment.HandleHitboxDamage(
                        hitbox,
                        damage,
                        hitPoint,
                        hitNormal,
                        -hitNormal,
                        Mathf.Max(4f, damage * 0.35f),
                        gameObject);
                }
                else
                {
                    playerHealth.TakeDamage(damage, hitPoint, hitNormal, gameObject);
                }

                break;
            }
        }

        private static EnemyHitbox ResolvePlayerHitbox(Collider hitCollider, PlayerHealth playerHealth, Vector3 hitPoint)
        {
            if (hitCollider != null)
            {
                EnemyHitbox direct = hitCollider.GetComponent<EnemyHitbox>();
                if (direct != null)
                    return direct;

                direct = hitCollider.GetComponentInParent<EnemyHitbox>();
                if (direct != null)
                    return direct;
            }

            if (playerHealth == null)
                return null;

            EnemyHitbox[] hitboxes = playerHealth.GetComponentsInChildren<EnemyHitbox>(true);
            EnemyHitbox best = null;
            float bestDist = float.MaxValue;
            for (int i = 0; i < hitboxes.Length; i++)
            {
                EnemyHitbox hitbox = hitboxes[i];
                if (hitbox == null)
                    continue;

                Collider collider = hitbox.GetComponent<Collider>();
                Vector3 point = collider != null ? collider.bounds.center : hitbox.transform.position;
                float dist = (point - hitPoint).sqrMagnitude;
                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = hitbox;
                }
            }

            return best;
        }
    }
}
