using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class EnemyRagdoll : MonoBehaviour
{
    [SerializeField] private Animator animator;
    [SerializeField] private float hitReactionDuration = 0.18f;
    [SerializeField] private float hitReactionForce = 18f;
    [SerializeField] private float deathImpulseForce = 45f;
    [SerializeField] private List<Rigidbody> bodies = new List<Rigidbody>();

    private bool _isRagdollActive;
    private Coroutine _hitReactionRoutine;
    private RuntimeAnimatorController _cachedController;

    public bool IsRagdollActive => _isRagdollActive;

    private void Awake()
    {
        if (animator == null)
            animator = GetComponent<Animator>();

        if (animator != null)
            _cachedController = animator.runtimeAnimatorController;

        if (bodies.Count == 0)
            CacheBodies();

        SetRagdollPhysics(false);
    }

    public void CacheBodies()
    {
        bodies.Clear();
        Rigidbody[] found = GetComponentsInChildren<Rigidbody>(true);
        for (int i = 0; i < found.Length; i++)
        {
            if (found[i] != null)
                bodies.Add(found[i]);
        }
    }

    public void PlayHitReaction(Vector3 hitPoint, Vector3 impulseDirection, float amount, EnemyHitbox hitbox)
    {
        if (_isRagdollActive)
            return;

        if (_hitReactionRoutine != null)
            StopCoroutine(_hitReactionRoutine);

        _hitReactionRoutine = StartCoroutine(HitReactionRoutine(hitPoint, impulseDirection, amount, hitbox));
    }

    public void ActivateRagdoll(Vector3 hitPoint, Vector3 impulseDirection, float amount)
    {
        if (_hitReactionRoutine != null)
        {
            StopCoroutine(_hitReactionRoutine);
            _hitReactionRoutine = null;
        }

        _isRagdollActive = true;
        StopAnimatorForRagdoll(clearController: true);
        SetRagdollPhysics(true);
        ApplyImpulse(hitPoint, impulseDirection, amount, true);
    }

    public void ApplyImpulse(Vector3 hitPoint, Vector3 impulseDirection, float amount, bool isDeath)
    {
        if (bodies.Count == 0)
            return;

        Vector3 dir = impulseDirection.sqrMagnitude > 0.0001f
            ? impulseDirection.normalized
            : Vector3.up;
        float force = (isDeath ? deathImpulseForce : hitReactionForce) * Mathf.Clamp(amount / 15f, 0.5f, 2.5f);

        Rigidbody closest = FindClosestBody(hitPoint);
        if (closest != null)
            closest.AddForceAtPosition(dir * force, hitPoint, ForceMode.Impulse);

        Rigidbody hips = FindBodyByName("Hips");
        if (hips != null && hips != closest)
            hips.AddForce(dir * force * 0.35f, ForceMode.Impulse);
    }

    private IEnumerator HitReactionRoutine(
        Vector3 hitPoint,
        Vector3 impulseDirection,
        float amount,
        EnemyHitbox hitbox)
    {
        Rigidbody hitBody = hitbox != null ? hitbox.GetComponent<Rigidbody>() : FindClosestBody(hitPoint);
        Rigidbody hips = FindBodyByName("Hips");

        // Animator must yield briefly or bone forces are overwritten every frame.
        StopAnimatorForRagdoll(clearController: false);

        SetBodyKinematic(hitBody, false);
        SetBodyKinematic(hips, false);
        ApplyImpulse(hitPoint, impulseDirection, amount, false);

        yield return new WaitForSeconds(hitReactionDuration);

        if (_isRagdollActive)
            yield break;

        SetBodyKinematic(hitBody, true);
        SetBodyKinematic(hips, true);
        RestoreAnimatorAfterHitReaction();
        _hitReactionRoutine = null;
    }

    private void StopAnimatorForRagdoll(bool clearController)
    {
        if (animator == null)
            return;

        if (_cachedController == null)
            _cachedController = animator.runtimeAnimatorController;

        animator.enabled = false;
        if (clearController)
            animator.runtimeAnimatorController = null;
    }

    private void RestoreAnimatorAfterHitReaction()
    {
        if (animator == null || _isRagdollActive)
            return;

        if (animator.runtimeAnimatorController == null && _cachedController != null)
            animator.runtimeAnimatorController = _cachedController;

        animator.enabled = true;
        animator.Rebind();
        animator.Update(0f);
    }

    private void SetRagdollPhysics(bool enabled)
    {
        for (int i = 0; i < bodies.Count; i++)
        {
            Rigidbody body = bodies[i];
            if (body == null)
                continue;

            body.isKinematic = !enabled;
            body.detectCollisions = true;
            if (enabled)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }
        }
    }

    private static void SetBodyKinematic(Rigidbody body, bool kinematic)
    {
        if (body == null)
            return;
        body.isKinematic = kinematic;
    }

    private Rigidbody FindClosestBody(Vector3 point)
    {
        Rigidbody best = null;
        float bestDist = float.MaxValue;
        for (int i = 0; i < bodies.Count; i++)
        {
            Rigidbody body = bodies[i];
            if (body == null)
                continue;

            float dist = (body.worldCenterOfMass - point).sqrMagnitude;
            if (dist < bestDist)
            {
                bestDist = dist;
                best = body;
            }
        }

        return best;
    }

    private Rigidbody FindBodyByName(string boneSuffix)
    {
        for (int i = 0; i < bodies.Count; i++)
        {
            Rigidbody body = bodies[i];
            if (body != null && body.name.IndexOf(boneSuffix, System.StringComparison.OrdinalIgnoreCase) >= 0)
                return body;
        }

        return null;
    }

#if UNITY_EDITOR
    public void EditorBind(Animator enemyAnimator, List<Rigidbody> ragdollBodies)
    {
        animator = enemyAnimator;
        bodies = new List<Rigidbody>(ragdollBodies);
    }
#endif
}
