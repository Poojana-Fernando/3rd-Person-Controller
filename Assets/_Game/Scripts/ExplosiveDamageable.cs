using DeadAuthorGames.ThirdPersonCombat;
using ExpObj;
using Game.Net;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Lets guns destroy ExplosiveObjects pack props (barrels, bottles/glass) via IDamageable.
/// When networked, only the server applies damage / AoE; clients play visuals from NetworkWorldSync.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(ExplosiveObject))]
public class ExplosiveDamageable : MonoBehaviour, IDamageable
{
    public enum PropKind
    {
        Barrel,
        Glass
    }

    [SerializeField] private PropKind propKind = PropKind.Barrel;
    [SerializeField] private float maxHealth = 40f;
    [SerializeField] private ExplosiveObject explosive;
    [Header("Explosion (barrels)")]
    [SerializeField] private bool dealExplosionDamage = true;
    [SerializeField] private float explosionDamage = 55f;
    [SerializeField] private float explosionRadius = 4.5f;
    [SerializeField] private float explosionForce = 650f;
    [SerializeField] private LayerMask explosionMask = ~0;

    private float _currentHealth;
    private bool _destroyed;

    [System.NonSerialized] public NetworkWorldSync networkWorldSync;
    [System.NonSerialized] public int networkWorldId;

    public PropKind Kind => propKind;
    public bool IsDestroyed => _destroyed;

    private void Awake()
    {
        if (explosive == null)
            explosive = GetComponent<ExplosiveObject>();

        _currentHealth = Mathf.Max(0.01f, maxHealth);
    }

    public void TakeDamage(float amount, Vector3 hitPoint, Vector3 hitNormal, GameObject source)
    {
        if (_destroyed || amount <= 0f)
            return;

        var nm = NetworkManager.Singleton;
        if (nm != null && nm.IsListening)
        {
            if (!nm.IsServer)
                return;

            if (networkWorldSync != null && networkWorldSync.IsSpawned)
            {
                networkWorldSync.TryServerDamageExplosive(this, amount, hitPoint, source);
                return;
            }
        }

        ServerApplyDamage(amount, hitPoint, source, out _, out _);
    }

    public void ServerApplyDamage(
        float amount,
        Vector3 hitPoint,
        GameObject source,
        out bool detonated,
        out Vector3 epicenter)
    {
        detonated = false;
        epicenter = hitPoint;

        if (_destroyed || amount <= 0f)
            return;

        _currentHealth -= amount;
        if (_currentHealth > 0f)
            return;

        DetonateInternal(hitPoint, source, applyAoe: true);
        detonated = true;
        epicenter = transform.position;
    }

    public void ClientDetonateVisual(Vector3 epicenter, bool playFx)
    {
        if (_destroyed)
            return;

        DetonateInternal(epicenter.sqrMagnitude > 0.0001f ? epicenter : transform.position, null, applyAoe: false);
    }

    public void Detonate(Vector3 epicenter, GameObject source)
    {
        if (_destroyed)
            return;
        DetonateInternal(epicenter, source, applyAoe: true);
    }

    private void DetonateInternal(Vector3 epicenter, GameObject source, bool applyAoe)
    {
        if (_destroyed)
            return;

        _destroyed = true;

        bool isBarrel = propKind == PropKind.Barrel;
        if (applyAoe && dealExplosionDamage && isBarrel)
            ApplyExplosionEffects(epicenter, source);

        PlayerCameraEffects.NotifyExplosion(epicenter, isBarrel);

        if (explosive != null)
            explosive.Explode();
        else
            Destroy(gameObject);
    }

    private void ApplyExplosionEffects(Vector3 epicenter, GameObject source)
    {
        Collider[] hits = Physics.OverlapSphere(epicenter, explosionRadius, explosionMask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < hits.Length; i++)
        {
            Collider hit = hits[i];
            if (hit == null)
                continue;

            if (hit.transform == transform || hit.transform.IsChildOf(transform))
                continue;

            IDamageable damageable = hit.GetComponentInParent<IDamageable>();
            if (damageable != null && !ReferenceEquals(damageable, this))
            {
                float distance = Vector3.Distance(epicenter, hit.ClosestPoint(epicenter));
                float falloff = 1f - Mathf.Clamp01(distance / Mathf.Max(0.01f, explosionRadius));
                float damage = explosionDamage * Mathf.Lerp(0.35f, 1f, falloff);
                Vector3 normal = (hit.transform.position - epicenter).normalized;
                if (normal.sqrMagnitude < 0.0001f)
                    normal = Vector3.up;
                damageable.TakeDamage(damage, hit.ClosestPoint(epicenter), normal, source != null ? source : gameObject);
            }

            Rigidbody body = hit.attachedRigidbody;
            if (body == null || body.isKinematic)
                continue;
            if (body.GetComponentInParent<DetachedBodyPart>() == null &&
                body.GetComponentInParent<NetworkSeveredPart>() == null)
                continue;

            body.AddExplosionForce(explosionForce, epicenter, explosionRadius, 0.35f, ForceMode.Impulse);
        }
    }

#if UNITY_EDITOR
    public void EditorConfigure(PropKind kind, float health, bool explosionAoE)
    {
        propKind = kind;
        maxHealth = health;
        dealExplosionDamage = explosionAoE;
        explosive = GetComponent<ExplosiveObject>();
    }
#endif
}
