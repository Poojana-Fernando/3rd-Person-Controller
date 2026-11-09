using System.Collections;
using UnityEngine;
using UnityEngine.AI;


namespace DeadAuthorGames.ThirdPersonCombat
{
    [RequireComponent(typeof(NavMeshAgent))]
    public class ZombieAI : MonoBehaviour
    {
        public enum ZombieState
        {
            Idle,
            Patrol,
            Alert,
            Chase,
            CrawlChase,
            Attack,
            Dead
        }

        private static readonly int SpeedHash = Animator.StringToHash("Speed");
        private static readonly int IsCrawlingHash = Animator.StringToHash("IsCrawling");
        private static readonly int ScreamHash = Animator.StringToHash("Scream");
        private static readonly int AttackHash = Animator.StringToHash("Attack");
        private static readonly int DeathHash = Animator.StringToHash("Death");
        private static readonly int ScreamStateHash = Animator.StringToHash("Scream");
        private static readonly int AttackStateHash = Animator.StringToHash("Attack");
        private static readonly int DeathStateHash = Animator.StringToHash("Death");

        [Header("References")]
        [SerializeField] private Transform player;
        [SerializeField] private Transform faceOrigin;
        [SerializeField] private Animator animator;
        [SerializeField] private EnemyHealth health;
        [SerializeField] private ZombiePatrolPath patrolPath;
        [SerializeField] private ZombieAttackHitbox attackHitbox;
        [SerializeField] private NavMeshAgent agent;
        [SerializeField] private AudioSource bodyAudio;
        [SerializeField] private AudioSource oneShotAudio;

        [Header("Detection")]
        [SerializeField] private float sightDetectRadius = 18f;
        [SerializeField] private float proximityDetectRadius = 3.5f;
        [SerializeField] private float loseAggroRadius = 24f;
        [SerializeField] private int visionRayCount = 5;
        [SerializeField] private float visionConeAngle = 70f;
        [SerializeField] private LayerMask detectionMask = ~0;

        [Header("Combat")]
        [SerializeField] private float attackRange = 1.8f;
        [SerializeField] private float attackDamage = 20f;
        [SerializeField] private float attackCooldown = 1.4f;
        [SerializeField] private float turnSpeed = 360f;
        [SerializeField] private float crawlHealthThreshold = 0.2f;

        [Header("Movement")]
        [SerializeField] private float walkSpeed = 1.1f;
        [SerializeField] private float runSpeed = 4.2f;
        [SerializeField] private float crawlSpeed = 2.2f;
        [SerializeField] private float patrolArriveDistance = 0.45f;
        [SerializeField] private float idleMinTime = 1.5f;
        [SerializeField] private float idleMaxTime = 4f;

        [Header("Animation")]
        [SerializeField] private float walkAnimSpeed = 0.45f;
        [SerializeField] private float runAnimSpeed = 1f;
        [SerializeField] private float crawlAnimSpeed = 0.8f;
        [SerializeField] private float screamDuration = 1.6f;
        [SerializeField] private float attackDuration = 1.2f;
        [SerializeField] private float deathDuration = 2.5f;

        [Header("Audio")]
        [SerializeField] private AudioClip screamClip;
        [SerializeField] private AudioClip walkLoopClip;
        [SerializeField] private AudioClip runLoopClip;
        [SerializeField] private AudioClip crawlLoopClip;
        [SerializeField] private AudioClip chaseLoopClip;
        [SerializeField] private AudioClip deathClip;

        private ZombieState _state = ZombieState.Idle;
        private int _patrolIndex;
        private float _idleTimer;
        private float _attackCooldownTimer;
        private float _stateTimer;
        private bool _playerDetected;
        private bool _waitingForScream;
        private bool _waitingForAttackAnimation;
        private bool _attackFromCrawl;

        private float _deathWaitElapsed;

        public ZombieState CurrentState => _state;
        public float AttackDamage => attackDamage;

        private void Awake()
        {
            if (agent == null)
                agent = GetComponent<NavMeshAgent>();
            if (animator == null)
                animator = GetComponent<Animator>();
            if (health == null)
                health = GetComponent<EnemyHealth>();
            if (attackHitbox == null)
                attackHitbox = GetComponentInChildren<ZombieAttackHitbox>(true);
            if (bodyAudio == null)
                bodyAudio = GetComponent<AudioSource>();
            if (oneShotAudio == null)
            {
                oneShotAudio = gameObject.AddComponent<AudioSource>();
                oneShotAudio.playOnAwake = false;
                oneShotAudio.spatialBlend = 1f;
            }

            if (faceOrigin == null)
                faceOrigin = FindBone(transform, "mixamorig5:Head") ?? transform;

            if (animator != null)
                animator.applyRootMotion = false;

            agent.updateRotation = true;
            agent.autoBraking = true;
            agent.acceleration = 18f;
            agent.angularSpeed = 360f;
            agent.stoppingDistance = attackRange * 0.85f;

            if (health != null)
                health.OnDeathStarted += HandleDeathStarted;
        }

        private void Start()
        {
            if (player == null)
            {
                var local = Game.Net.NetworkPlayerRegistry.Instance != null
                    ? Game.Net.NetworkPlayerRegistry.Instance.LocalPlayer
                    : null;
                if (local != null)
                    player = local.transform;
                else
                {
                    PlayerGunController gun = FindAnyObjectByType<PlayerGunController>();
                    if (gun != null)
                        player = gun.transform;
                }
            }

            EnterIdle();
        }

        public void SetPlayerTarget(Transform target)
        {
            player = target;
        }

        private void OnDestroy()
        {
            if (health != null)
                health.OnDeathStarted -= HandleDeathStarted;
        }

        private void Update()
        {
            if (_state == ZombieState.Dead)
            {
                // Failsafe: if scripted ragdoll never completed (e.g. coroutine interrupted), force it.
                if (health != null && health.AwaitingScriptedDeathCompletion)
                {
                    _deathWaitElapsed += Time.unscaledDeltaTime;
                    if (_deathWaitElapsed >= Mathf.Max(0.5f, deathDuration))
                        health.CompleteScriptedDeath();
                }

                return;
            }

            if (health != null && health.IsDead && _state != ZombieState.Dead)
            {
                EnterDead();
                return;
            }

            _attackCooldownTimer = Mathf.Max(0f, _attackCooldownTimer - Time.deltaTime);

            if (_state != ZombieState.Alert && _state != ZombieState.Attack && _state != ZombieState.Dead)
                UpdateDetection();

            switch (_state)
            {
                case ZombieState.Idle:
                    UpdateIdle();
                    break;
                case ZombieState.Patrol:
                    UpdatePatrol();
                    break;
                case ZombieState.Alert:
                    UpdateAlert();
                    break;
                case ZombieState.Chase:
                case ZombieState.CrawlChase:
                    UpdateChase();
                    break;
                case ZombieState.Attack:
                    UpdateAttack();
                    break;
            }
        }

        private void UpdateDetection()
        {
            if (player == null)
                return;

            float distance = Vector3.Distance(transform.position, player.position);

            if (distance <= proximityDetectRadius)
            {
                DetectPlayer();
                return;
            }

            if (distance <= sightDetectRadius && HasLineOfSightToPlayer())
                DetectPlayer();
            else if (_playerDetected && distance > loseAggroRadius)
                LosePlayer();
        }

        private bool HasLineOfSightToPlayer()
        {
            if (player == null || faceOrigin == null)
                return false;

            Vector3 origin = faceOrigin.position;
            Vector3 toPlayer = (player.position + Vector3.up * 1f) - origin;
            float distance = toPlayer.magnitude;
            if (distance <= 0.01f)
                return true;

            Vector3 forward = faceOrigin.forward;
            Vector3 flatForward = Vector3.ProjectOnPlane(forward, Vector3.up).normalized;
            if (flatForward.sqrMagnitude < 0.0001f)
                flatForward = transform.forward;

            int rays = Mathf.Max(1, visionRayCount);
            float halfCone = visionConeAngle * 0.5f;

            for (int i = 0; i < rays; i++)
            {
                float t = rays == 1 ? 0.5f : i / (float)(rays - 1);
                float angle = Mathf.Lerp(-halfCone, halfCone, t);
                Vector3 dir = Quaternion.AngleAxis(angle, Vector3.up) * flatForward;
                if (Physics.Raycast(origin, dir, out RaycastHit hit, sightDetectRadius, detectionMask, QueryTriggerInteraction.Ignore))
                {
                    if (IsOwnCollider(hit.collider))
                        continue;
                    if (IsPlayerCollider(hit.collider))
                        return true;
                }
            }

            Vector3 direct = toPlayer.normalized;
            if (Physics.Raycast(origin, direct, out RaycastHit directHit, distance + 0.1f, detectionMask, QueryTriggerInteraction.Ignore))
            {
                if (!IsOwnCollider(directHit.collider) && IsPlayerCollider(directHit.collider))
                    return true;
            }

            return false;
        }

        private bool IsOwnCollider(Collider collider)
        {
            return collider != null && collider.transform.IsChildOf(transform);
        }

        private bool IsPlayerCollider(Collider collider)
        {
            if (collider == null)
                return false;

            return collider.GetComponentInParent<PlayerHealth>() != null
                || collider.GetComponentInParent<PlayerGunController>() != null;
        }

        private void DetectPlayer()
        {
            if (_playerDetected)
                return;

            _playerDetected = true;
            EnterAlert();
        }

        private void LosePlayer()
        {
            _playerDetected = false;
            EnterIdle();
        }

        private void UpdateIdle()
        {
            SetAgentMoving(false);
            SetLocomotionAnim(0f, false);

            _idleTimer -= Time.deltaTime;
            if (_idleTimer <= 0f)
                EnterPatrol();
        }

        private void UpdatePatrol()
        {
            if (patrolPath == null || patrolPath.Count == 0)
            {
                EnterIdle();
                return;
            }

            SetAgentMoving(true);
            agent.speed = walkSpeed;
            PlayLoop(walkLoopClip);

            Vector3 target = patrolPath.GetWaypointPosition(_patrolIndex);
            agent.SetDestination(target);

            float planarSpeed = GetPlanarSpeed();
            float animSpeed = walkSpeed > 0.01f
                ? Mathf.Clamp(planarSpeed / walkSpeed, 0.05f, 1f) * walkAnimSpeed
                : 0f;
            SetLocomotionAnim(animSpeed, false);

            if (!agent.pathPending && agent.remainingDistance <= patrolArriveDistance)
            {
                patrolPath.GetNextIndex(ref _patrolIndex);
                EnterIdle();
            }
        }

        private void UpdateAlert()
        {
            SetAgentMoving(false);
            SetLocomotionAnim(0f, false);
            StopLoop();

            if (player != null)
                RotateToward(player.position - transform.position);

            if (animator != null && _waitingForScream)
            {
                AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
                if (state.shortNameHash == ScreamStateHash || state.IsName("Scream"))
                {
                    if (state.normalizedTime >= 0.98f)
                    {
                        _waitingForScream = false;
                        EnterChase();
                    }

                    return;
                }

                return;
            }

            _stateTimer -= Time.deltaTime;
            if (_stateTimer <= 0f)
            {
                _waitingForScream = false;
                EnterChase();
            }
        }

        private void UpdateChase()
        {
            if (player == null)
            {
                LosePlayer();
                return;
            }

            float distance = Vector3.Distance(transform.position, player.position);
            if (distance > loseAggroRadius)
            {
                LosePlayer();
                return;
            }

            bool shouldCrawl = health != null && health.NormalizedHealth <= crawlHealthThreshold;
            if (shouldCrawl && _state != ZombieState.CrawlChase)
                EnterCrawlChase();
            else if (!shouldCrawl && _state == ZombieState.CrawlChase)
                EnterChase();

            if (distance <= attackRange && _attackCooldownTimer <= 0f)
            {
                EnterAttack();
                return;
            }

            SetAgentMoving(true);
            float moveSpeed = _state == ZombieState.CrawlChase ? crawlSpeed : runSpeed;
            agent.speed = moveSpeed;
            agent.SetDestination(player.position);

            float planarSpeed = GetPlanarSpeed();
            float animSpeed = moveSpeed > 0.01f
                ? Mathf.Clamp(planarSpeed / moveSpeed, 0.05f, 1f) * (_state == ZombieState.CrawlChase ? crawlAnimSpeed : runAnimSpeed)
                : 0f;
            SetLocomotionAnim(animSpeed, _state == ZombieState.CrawlChase);
            PlayLoop(_state == ZombieState.CrawlChase ? crawlLoopClip : (chaseLoopClip != null ? chaseLoopClip : runLoopClip));
        }

        private void UpdateAttack()
        {
            SetAgentMoving(false);
            SetLocomotionAnim(0f, _attackFromCrawl);
            StopLoop();

            if (player != null)
                RotateToward(player.position - transform.position);

            if (animator != null && _waitingForAttackAnimation)
            {
                AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
                bool inAttack = state.shortNameHash == AttackStateHash || state.IsName("Attack");
                if (inAttack)
                {
                    if (state.normalizedTime >= 0.98f && !animator.IsInTransition(0))
                        FinishAttack();

                    return;
                }

                if (animator.IsInTransition(0))
                    return;
            }

            _stateTimer -= Time.deltaTime;
            if (_stateTimer <= 0f)
                FinishAttack();
        }

        private void FinishAttack()
        {
            _waitingForAttackAnimation = false;
            _attackCooldownTimer = attackCooldown;

            if (_attackFromCrawl && health != null && health.NormalizedHealth <= crawlHealthThreshold)
                EnterCrawlChase();
            else
                EnterChase();
        }

        private void EnterIdle()
        {
            _state = ZombieState.Idle;
            _idleTimer = Random.Range(idleMinTime, idleMaxTime);
            SetAgentMoving(false);
            SetLocomotionAnim(0f, false);
            StopLoop();
        }

        private void EnterPatrol()
        {
            _state = ZombieState.Patrol;
            SetAgentMoving(true);
            agent.speed = walkSpeed;
        }

        private void EnterAlert()
        {
            _state = ZombieState.Alert;
            _waitingForScream = true;
            _stateTimer = screamDuration;
            SetAgentMoving(false);
            agent.ResetPath();
            SetLocomotionAnim(0f, false);
            StopLoop();

            if (animator != null)
                animator.SetTrigger(ScreamHash);

            if (oneShotAudio != null && screamClip != null)
                oneShotAudio.PlayOneShot(screamClip);
        }

        private void EnterChase()
        {
            _state = ZombieState.Chase;
            agent.ResetPath();
            SetAgentMoving(true);
            agent.speed = runSpeed;
            SetLocomotionAnim(0f, false);
        }

        private void EnterCrawlChase()
        {
            _state = ZombieState.CrawlChase;
            agent.ResetPath();
            SetAgentMoving(true);
            agent.speed = crawlSpeed;
            SetLocomotionAnim(0f, true);
        }

        private void EnterAttack()
        {
            _attackFromCrawl = _state == ZombieState.CrawlChase;
            _state = ZombieState.Attack;
            _stateTimer = attackDuration;
            _waitingForAttackAnimation = animator != null;
            SetAgentMoving(false);
            agent.ResetPath();
            SetLocomotionAnim(0f, _attackFromCrawl);

            if (animator != null)
                animator.SetTrigger(AttackHash);

            attackHitbox?.BeginAttackSwing();
        }

        private void HandleDeathStarted()
        {
            EnterDead();
        }

        private void EnterDead()
        {
            if (_state == ZombieState.Dead)
                return;

            _state = ZombieState.Dead;
            _playerDetected = false;
            _waitingForScream = false;
            _waitingForAttackAnimation = false;
            _deathWaitElapsed = 0f;
            StopLoop();

            try
            {
                EnemyRagdoll ragdoll = GetComponent<EnemyRagdoll>();
                ragdoll?.CancelHitReactionForDeath();

                if (agent != null)
                {
                    try
                    {
                        if (agent.enabled && agent.isOnNavMesh)
                        {
                            agent.ResetPath();
                            agent.isStopped = true;
                        }
                    }
                    catch (System.Exception)
                    {
                        // Agent may already be off the NavMesh.
                    }

                    agent.enabled = false;
                }

                SetLocomotionAnim(0f, false);

                if (animator != null)
                {
                    if (!animator.enabled)
                        animator.enabled = true;

                    animator.ResetTrigger(ScreamHash);
                    animator.ResetTrigger(AttackHash);
                    animator.ResetTrigger(DeathHash);
                    animator.SetTrigger(DeathHash);
                    animator.Play("Death", 0, 0f);
                    animator.Update(0f);
                }

                if (oneShotAudio != null && deathClip != null)
                    oneShotAudio.PlayOneShot(deathClip);

                DisableRootCollider();
            }
            finally
            {
                // Always start the death wait even if agent/ragdoll cleanup throws.
                StartCoroutine(FinishDeathAfterAnimation());
            }
        }

        private IEnumerator FinishDeathAfterAnimation()
        {
            if (animator == null)
            {
                yield return new WaitForSecondsRealtime(deathDuration);
                health?.CompleteScriptedDeath();
                yield break;
            }

            // Give the animator a frame to enter Death after the trigger.
            yield return null;

            float timeout = Mathf.Max(0.5f, deathDuration);
            float elapsed = 0f;
            bool inDeath = false;

            while (elapsed < timeout)
            {
                if (animator == null || !animator.enabled)
                {
                    // Hit reaction or other systems disabled the animator — complete ragdoll immediately.
                    break;
                }

                AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
                bool inDeathState = state.shortNameHash == DeathStateHash || state.IsName("Death");
                if (inDeathState)
                {
                    inDeath = true;
                    if (state.normalizedTime >= 0.98f)
                        break;
                }
                else if (inDeath)
                {
                    break;
                }
                else if (elapsed > 1.5f)
                {
                    // Death state never entered — fall through to ragdoll.
                    break;
                }

                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            health?.CompleteScriptedDeath();

            if (health != null
                && health.CurrentDeathMode == EnemyHealth.DeathMode.AnimationOnly
                && animator != null)
            {
                animator.enabled = false;
            }
        }

        private void SetAgentMoving(bool moving)
        {
            if (agent == null || !agent.enabled || !agent.isOnNavMesh)
                return;

            try
            {
                agent.updateRotation = moving;
                agent.isStopped = !moving;
            }
            catch (System.Exception)
            {
                // Ignore NavMeshAgent state errors when the agent is being disabled.
            }
        }

        private float GetPlanarSpeed()
        {
            Vector3 velocity = agent.velocity;
            velocity.y = 0f;
            return velocity.magnitude;
        }

        private void DisableRootCollider()
        {
            CapsuleCollider rootCollider = GetComponent<CapsuleCollider>();
            if (rootCollider != null)
                rootCollider.enabled = false;
        }

        private void SetLocomotionAnim(float speed, bool crawling)
        {
            if (animator == null)
                return;

            animator.SetFloat(SpeedHash, speed);
            animator.SetBool(IsCrawlingHash, crawling);
        }

        private void RotateToward(Vector3 direction)
        {
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.0001f)
                return;

            Quaternion target = Quaternion.LookRotation(direction.normalized, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, target, turnSpeed * Time.deltaTime);
        }

        private void PlayLoop(AudioClip clip)
        {
            if (bodyAudio == null || clip == null)
                return;

            if (bodyAudio.clip != clip)
            {
                bodyAudio.clip = clip;
                bodyAudio.loop = true;
                bodyAudio.Play();
            }
            else if (!bodyAudio.isPlaying)
            {
                bodyAudio.loop = true;
                bodyAudio.Play();
            }
        }

        private void StopLoop()
        {
            if (bodyAudio != null && bodyAudio.isPlaying)
                bodyAudio.Stop();
        }

        private static Transform FindBone(Transform root, string exactName)
        {
            if (root.name == exactName)
                return root;

            Transform[] all = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i].name == exactName)
                    return all[i];
            }

            return null;
        }

        public void OnAttackHit()
        {
            attackHitbox?.OnAttackHit();
        }

    #if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.2f, 0.2f, 0.35f);
            Gizmos.DrawWireSphere(transform.position, sightDetectRadius);
            Gizmos.color = new Color(1f, 0.6f, 0.1f, 0.35f);
            Gizmos.DrawWireSphere(transform.position, proximityDetectRadius);
            Gizmos.color = new Color(0.2f, 0.5f, 1f, 0.35f);
            Gizmos.DrawWireSphere(transform.position, attackRange);
        }
    #endif
    }
}
