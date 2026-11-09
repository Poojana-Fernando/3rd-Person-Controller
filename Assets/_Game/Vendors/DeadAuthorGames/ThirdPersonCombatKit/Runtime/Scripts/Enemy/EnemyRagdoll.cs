using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;


namespace DeadAuthorGames.ThirdPersonCombat
{
    public class EnemyRagdoll : MonoBehaviour
    {
        [SerializeField] private Animator animator;
        [SerializeField] private float hitReactionDuration = 0.18f;
        [SerializeField] private float hitReactionForce = 6f;
        [SerializeField] private float deathImpulseForce = 10f;
        [SerializeField] private float ragdollHitImpulseForce = 3f;
        [SerializeField] private float maxDeathImpulseUpward = 0.15f;
        [SerializeField] private float groundSnapRayDistance = 3f;
        [SerializeField] private LayerMask groundMask = ~0;
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
            ApplyAntiStretchSettings();
            SnapRagdollPoseToGround();
            SetRagdollPhysics(true);
            ApplyImpulse(hitPoint, impulseDirection, amount, true);
            StartCoroutine(SettleRagdollOnGround());
        }

        public void CancelHitReactionForDeath()
        {
            if (_hitReactionRoutine != null)
            {
                StopCoroutine(_hitReactionRoutine);
                _hitReactionRoutine = null;
            }

            if (_isRagdollActive)
                return;

            for (int i = 0; i < bodies.Count; i++)
            {
                Rigidbody body = bodies[i];
                if (body == null)
                    continue;

                // Unity 6 throws if velocity is written while kinematic.
                if (!body.isKinematic)
                {
                    body.linearVelocity = Vector3.zero;
                    body.angularVelocity = Vector3.zero;
                }

                body.isKinematic = true;
                body.useGravity = false;
            }

            if (animator != null)
            {
                if (_cachedController != null && animator.runtimeAnimatorController == null)
                    animator.runtimeAnimatorController = _cachedController;
                animator.enabled = true;
            }
        }

        public void ApplyImpulse(Vector3 hitPoint, Vector3 impulseDirection, float amount, bool isDeath)
        {
            if (bodies.Count == 0)
                return;

            bool isCorpseHit = _isRagdollActive && isDeath;
            Vector3 dir = ResolveImpulseDirection(hitPoint, impulseDirection, isDeath || isCorpseHit, isCorpseHit);

            float damageScale = Mathf.Clamp(amount / 15f, 0.35f, isCorpseHit ? 1f : 1.25f);
            float baseForce = isCorpseHit
                ? ragdollHitImpulseForce
                : isDeath
                    ? deathImpulseForce
                    : hitReactionForce;
            float force = baseForce * damageScale;

            Rigidbody closest = FindClosestBody(hitPoint);
            if (closest != null)
                closest.AddForceAtPosition(dir * force, hitPoint, ForceMode.Impulse);

            Rigidbody hips = FindBodyByName("Hips");
            float hipsMultiplier = isCorpseHit ? 0.1f : isDeath ? 0.2f : 0.15f;
            if (hips != null && hips != closest)
                hips.AddForce(dir * force * hipsMultiplier, ForceMode.Impulse);
        }

        private IEnumerator HitReactionRoutine(
            Vector3 hitPoint,
            Vector3 impulseDirection,
            float amount,
            EnemyHitbox hitbox)
        {
            Rigidbody hitBody = hitbox != null ? hitbox.GetComponent<Rigidbody>() : FindClosestBody(hitPoint);
            Rigidbody hips = FindBodyByName("Hips");

            StopAnimatorForRagdoll(clearController: false);

            SetBodyKinematic(hitBody, false);
            SetBodyKinematic(hips, false);
            if (hitBody != null)
                hitBody.useGravity = true;
            if (hips != null)
                hips.useGravity = true;

            ApplyImpulse(hitPoint, impulseDirection, amount, false);

            yield return new WaitForSeconds(hitReactionDuration);

            if (_isRagdollActive)
                yield break;

            // Death may have started while this reaction was running.
            EnemyHealth health = GetComponent<EnemyHealth>();
            if (health != null && health.IsDead)
            {
                _hitReactionRoutine = null;
                yield break;
            }

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
            if (enabled)
                DisableRootMovementColliders();

            for (int i = 0; i < bodies.Count; i++)
            {
                Rigidbody body = bodies[i];
                if (body == null)
                    continue;

                Collider bodyCollider = body.GetComponent<Collider>();
                if (bodyCollider != null)
                    bodyCollider.enabled = true;

                body.isKinematic = !enabled;
                body.useGravity = enabled;
                body.detectCollisions = true;

                if (enabled)
                {
                    body.linearVelocity = Vector3.zero;
                    body.angularVelocity = Vector3.zero;
                    body.collisionDetectionMode = CollisionDetectionMode.Continuous;
                    body.WakeUp();
                }
                else
                {
                    body.useGravity = false;
                }
            }

            if (enabled)
                Physics.SyncTransforms();
        }

        private Vector3 ResolveImpulseDirection(Vector3 hitPoint, Vector3 impulseDirection, bool flattenUpward, bool isCorpseHit)
        {
            Vector3 dir = impulseDirection.sqrMagnitude > 0.0001f
                ? impulseDirection.normalized
                : Vector3.zero;

            if (dir.sqrMagnitude < 0.0001f)
            {
                Rigidbody hips = FindBodyByName("Hips");
                Vector3 origin = hips != null ? hips.worldCenterOfMass : transform.position + Vector3.up;
                dir = origin - hitPoint;
                if (dir.sqrMagnitude < 0.0001f)
                    dir = -transform.forward;
            }

            if (flattenUpward)
            {
                dir = Vector3.ProjectOnPlane(dir, Vector3.up);
                if (dir.sqrMagnitude < 0.0001f)
                    dir = -transform.forward;

                dir = dir.normalized;
                float maxUp = isCorpseHit ? 0.05f : maxDeathImpulseUpward;
                dir.y = Mathf.Clamp(dir.y, -0.2f, maxUp);
                return dir.normalized;
            }

            dir = Vector3.ProjectOnPlane(dir, Vector3.up);
            if (dir.sqrMagnitude < 0.0001f)
                dir = -transform.forward;

            return dir.normalized;
        }

        private void SnapRagdollPoseToGround()
        {
            if (!TryGetRagdollBounds(out Bounds bounds))
                return;

            Vector3 rayOrigin = new Vector3(bounds.center.x, bounds.max.y + 0.25f, bounds.center.z);
            if (!Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, groundSnapRayDistance, groundMask, QueryTriggerInteraction.Ignore))
                return;

            float gap = bounds.min.y - hit.point.y;
            if (gap <= 0.02f)
                return;

            float shiftY = hit.point.y - bounds.min.y + 0.02f;
            Transform[] bones = GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < bones.Length; i++)
            {
                if (bones[i] != null)
                    bones[i].position += Vector3.up * shiftY;
            }
        }

        private IEnumerator SettleRagdollOnGround()
        {
            const float duration = 2f;
            float elapsed = 0f;

            while (elapsed < duration)
            {
                if (!_isRagdollActive)
                    yield break;

                for (int i = 0; i < bodies.Count; i++)
                {
                    Rigidbody body = bodies[i];
                    if (body == null || body.isKinematic)
                        continue;

                    Vector3 velocity = body.linearVelocity;
                    if (velocity.y > 0.05f)
                        velocity.y *= 0.85f;

                    body.linearVelocity = velocity;
                }

                elapsed += Time.fixedDeltaTime;
                yield return new WaitForFixedUpdate();
            }
        }

        private bool TryGetRagdollBounds(out Bounds bounds)
        {
            bounds = default;
            bool hasBounds = false;

            for (int i = 0; i < bodies.Count; i++)
            {
                Rigidbody body = bodies[i];
                if (body == null)
                    continue;

                Collider collider = body.GetComponent<Collider>();
                if (collider == null)
                    continue;

                if (!hasBounds)
                {
                    bounds = collider.bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(collider.bounds);
                }
            }

            return hasBounds;
        }

        private void DisableRootMovementColliders()
        {
            NavMeshAgent agent = GetComponent<NavMeshAgent>();
            if (agent != null)
                agent.enabled = false;

            CapsuleCollider rootCapsule = GetComponent<CapsuleCollider>();
            if (rootCapsule != null)
                rootCapsule.enabled = false;
        }

        private void ApplyAntiStretchSettings()
        {
            ConfigurableJoint[] configurableJoints = GetComponentsInChildren<ConfigurableJoint>(true);
            for (int i = 0; i < configurableJoints.Length; i++)
            {
                ConfigurableJoint joint = configurableJoints[i];
                joint.xMotion = ConfigurableJointMotion.Locked;
                joint.yMotion = ConfigurableJointMotion.Locked;
                joint.zMotion = ConfigurableJointMotion.Locked;
                joint.projectionMode = JointProjectionMode.PositionAndRotation;
                joint.projectionDistance = 0.01f;
                joint.projectionAngle = 5f;
            }

            CharacterJoint[] characterJoints = GetComponentsInChildren<CharacterJoint>(true);
            for (int i = 0; i < characterJoints.Length; i++)
            {
                CharacterJoint joint = characterJoints[i];
                joint.enableProjection = true;
                joint.projectionDistance = 0.01f;
                joint.projectionAngle = 5f;
            }

            for (int i = 0; i < bodies.Count; i++)
            {
                Rigidbody body = bodies[i];
                if (body == null)
                    continue;

                body.useGravity = true;
                body.maxAngularVelocity = 10f;
                body.maxDepenetrationVelocity = 4f;
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
}
