using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Motiong.vCharacterController;

[RequireComponent(typeof(EnvScanner))]
[RequireComponent(typeof(Animator))]
[DefaultExecutionOrder(-20)]
public class ParkourController : MonoBehaviour
{
    [SerializeField] private List<ParkourActions> parkourActions;

    [Header("References")]
    [SerializeField] private EnvScanner envScanner;
    [SerializeField] private Animator animator;
    [SerializeField] private vThirdPersonController characterController;

    [Header("Parkour")]
    [SerializeField] private float defaultTransitionDuration = 0.1f;
    [SerializeField] private float defaultObstacleRotationSpeed = 16f;
    [SerializeField] private float largeAngleSnapThreshold = 45f;
    [SerializeField] private float rotationBoostDuration = 0.15f;
    [SerializeField] private float rotationBoostMultiplier = 3f;

    [Header("Input")]
    [Tooltip("Same as jump by default. Near a valid obstacle this consumes Space for parkour instead of jump.")]
    [SerializeField] private KeyCode parkourKey = KeyCode.Space;

    private bool inAction;
    private bool _overrideRootRotation;
    private Quaternion _targetObstacleRotation;
    private ParkourActions _activeAction;
    private Rigidbody _rigidbody;
    private float _rotationBoostEndTime;

    private bool ShouldRotateToObstacle =>
        inAction && _activeAction != null && _activeAction.RotateToObstacle;

    /// <summary>
    /// True while a parkour vault animation is playing.
    /// </summary>
    public bool IsInParkourAction => inAction;

    /// <summary>
    /// When true, root-motion rotation is skipped so obstacle-facing rotation can drive the body yaw.
    /// </summary>
    public bool IsOverridingRootRotation => _overrideRootRotation;

    /// <summary>
    /// True while parkour is playing, or Space would start a matching parkour action (blocks normal jump).
    /// </summary>
    public bool ShouldSuppressJump
    {
        get
        {
            if (inAction)
                return true;

            if (envScanner == null)
                return false;

            if (!envScanner.TryGetObstacle(out EnvScanner.ObstacleHitData hit))
                return false;

            return SelectAction(hit) != null;
        }
    }

    private struct ParkourSnapshot
    {
        public bool lockMovement;
        public bool lockRotation;
        public bool applyRootMotion;
        public bool suppressUpperBodyLocomotion;
        public bool wasKinematic;
    }

    private void Awake()
    {
        if (envScanner == null)
            envScanner = GetComponent<EnvScanner>();

        if (animator == null)
            animator = GetComponent<Animator>();

        if (characterController == null)
            characterController = GetComponent<vThirdPersonController>();

        _rigidbody = GetComponent<Rigidbody>();

        if (envScanner == null)
            Debug.LogError("ParkourController: EnvScanner was not found.");

        if (animator == null)
            Debug.LogError("ParkourController: Animator was not found.");

        if (characterController == null)
            Debug.LogError("ParkourController: vThirdPersonController was not found.");
    }

    private void Update()
    {
        if (inAction)
            return;

        if (!Input.GetKeyDown(parkourKey))
            return;

        TryStartParkour();
    }

    private void LateUpdate()
    {
        if (!ShouldRotateToObstacle)
            return;

        ApplyObstacleRotation(_targetObstacleRotation);
    }

    private void TryStartParkour()
    {
        if (envScanner == null || animator == null)
            return;

        if (!envScanner.TryGetObstacle(out EnvScanner.ObstacleHitData hitData))
        {
            Debug.Log(
                "Parkour: No valid obstacle (forward=" +
                hitData.forwardHitFound +
                ", height=" +
                hitData.heightHitFound +
                ")."
            );
            return;
        }

        ParkourActions action = SelectAction(hitData);
        if (action == null)
        {
            Debug.Log(
                "Parkour: No action matches obstacle height " +
                hitData.obstacleHeight.ToString("F2") +
                "m."
            );
            return;
        }

        StartCoroutine(DoParkourAction(action));
    }

    private ParkourActions SelectAction(EnvScanner.ObstacleHitData hitData)
    {
        if (parkourActions == null || parkourActions.Count == 0)
            return null;

        ParkourActions best = null;
        float bestRange = float.MaxValue;
        int bestPriority = int.MinValue;

        foreach (ParkourActions action in parkourActions)
        {
            if (action == null || !action.CheckIfPossible(hitData, transform))
                continue;

            float range = action.MaxHeight - action.MinHeight;
            if (action.Priority > bestPriority ||
                (action.Priority == bestPriority && range < bestRange))
            {
                best = action;
                bestRange = range;
                bestPriority = action.Priority;
            }
        }

        return best;
    }

    private void ApplyObstacleRotation(Quaternion targetRotation)
    {
        float rotationSpeed = characterController != null
            ? characterController.LocomotionRotationSpeed
            : defaultObstacleRotationSpeed;

        if (Time.time < _rotationBoostEndTime)
            rotationSpeed *= rotationBoostMultiplier;

        Quaternion newRotation = Quaternion.RotateTowards(
            transform.rotation,
            targetRotation,
            rotationSpeed * Time.deltaTime
        );

        transform.rotation = newRotation;

        if (_rigidbody != null)
            _rigidbody.rotation = newRotation;
    }

    private void MatchTarget(ParkourActions action)
    {
        if (animator.isMatchingTarget)
            return;

        animator.MatchTarget(
            action.MatchPosition,
            transform.rotation,
            action.MatchBodyPart,
            new MatchTargetWeightMask(new Vector3(0f, 1f, 1f), 0f),
            action.MatchStartTime,
            action.MatchTargetTime
        );
    }

    private void BeginObstacleRotation(ParkourActions action)
    {
        _targetObstacleRotation = action.TargetRotation;
        _rotationBoostEndTime = Time.time + rotationBoostDuration;

        float angle = Quaternion.Angle(transform.rotation, _targetObstacleRotation);
        if (angle > largeAngleSnapThreshold)
            ApplyObstacleRotation(_targetObstacleRotation);
    }

    private IEnumerator DoParkourAction(ParkourActions action)
    {
        inAction = true;
        _activeAction = action;
        _overrideRootRotation = action.RotateToObstacle;

        if (_overrideRootRotation)
            BeginObstacleRotation(action);

        ParkourSnapshot snapshot = BeginParkourLockout();

        string animName = action.AnimName;
        float transition = action.TransitionDuration > 0f
            ? action.TransitionDuration
            : defaultTransitionDuration;

        Debug.Log("Parkour: Playing " + animName + " (height action: " + action.name + ")");

        animator.CrossFadeInFixedTime(animName, transition, 0);

        yield return null;

        float timeout = 1f;
        float enterTimer = 0f;

        while (!IsInActiveAnimState())
        {
            enterTimer += Time.deltaTime;
            if (enterTimer >= timeout)
            {
                Debug.LogError(
                    "Parkour: Could not enter animation state '" +
                    animName +
                    "'. Confirm it exists on the live Base Layer."
                );

                EndParkourAction(snapshot);
                yield break;
            }

            if (ShouldRotateToObstacle)
                ApplyObstacleRotation(_targetObstacleRotation);

            if (_activeAction != null && _activeAction.EnableTargetMatching)
                MatchTarget(_activeAction);

            yield return null;
        }

        AnimatorStateInfo stateInfo = animator.GetCurrentAnimatorStateInfo(0);
        Debug.Log("Parkour: " + animName + " started. length=" + stateInfo.length);

        float timer = 0f;
        float animLength = stateInfo.length;

        while (timer <= animLength &&
               IsInActiveAnimState() &&
               animator.GetCurrentAnimatorStateInfo(0).normalizedTime < 0.98f)
        {
            timer += Time.deltaTime;

            if (_activeAction != null && _activeAction.EnableTargetMatching)
                MatchTarget(_activeAction);

            yield return null;
        }

        EndParkourAction(snapshot);
    }

    private void EndParkourAction(ParkourSnapshot snapshot)
    {
        RestoreController(snapshot);

        _overrideRootRotation = false;
        inAction = false;
        _activeAction = null;

        Debug.Log("Parkour: Finished.");
    }

    private ParkourSnapshot BeginParkourLockout()
    {
        var snapshot = new ParkourSnapshot
        {
            applyRootMotion = animator.applyRootMotion,
        };

        SetObstacleLayerCollisionIgnored(true);

        if (characterController != null)
        {
            snapshot.lockMovement = characterController.lockMovement;
            snapshot.lockRotation = characterController.lockRotation;
            snapshot.suppressUpperBodyLocomotion = characterController.suppressUpperBodyLocomotion;

            characterController.lockMovement = true;
            characterController.lockRotation = true;
            characterController.suppressUpperBodyLocomotion = true;
            characterController.input = Vector3.zero;
            characterController.inputSmooth = Vector3.zero;
            characterController.moveDirection = Vector3.zero;
            characterController.isSprinting = false;
            characterController.isJumping = false;
        }

        if (_rigidbody != null)
        {
            snapshot.wasKinematic = _rigidbody.isKinematic;

            if (!snapshot.wasKinematic)
            {
                _rigidbody.linearVelocity = Vector3.zero;
                _rigidbody.angularVelocity = Vector3.zero;
            }

            _rigidbody.isKinematic = true;
        }

        animator.applyRootMotion = true;
        return snapshot;
    }

    private void SetObstacleLayerCollisionIgnored(bool ignore)
    {
        if (envScanner == null)
            return;

        int playerLayer = gameObject.layer;
        LayerMask obstacleMask = envScanner.ObstacleLayer;

        for (int layer = 0; layer < 32; layer++)
        {
            if ((obstacleMask.value & (1 << layer)) == 0)
                continue;

            Physics.IgnoreLayerCollision(playerLayer, layer, ignore);
        }
    }

    private bool IsInActiveAnimState()
    {
        if (_activeAction == null)
            return false;

        AnimatorStateInfo info = animator.GetCurrentAnimatorStateInfo(0);
        string animName = _activeAction.AnimName;

        if (info.IsName(animName))
            return true;

        // Common aliases for traversal clips wired under nested state machines.
        if (animName == "Stepup" || animName == "StepUp")
        {
            return info.IsName("Stepup") ||
                   info.IsName("StepUp") ||
                   info.IsName("Actions.StepUp.StepUp") ||
                   info.IsName("StepUp.StepUp");
        }

        if (animName == "JumpUp")
        {
            return info.IsName("JumpUp") ||
                   info.IsName("Actions.JumpUp.JumpUp") ||
                   info.IsName("JumpUp.JumpUp");
        }

        return false;
    }

    private void RestoreController(ParkourSnapshot snapshot)
    {
        SetObstacleLayerCollisionIgnored(false);

        if (_rigidbody != null)
            _rigidbody.isKinematic = snapshot.wasKinematic;

        if (characterController != null)
        {
            characterController.lockMovement = snapshot.lockMovement;
            characterController.lockRotation = snapshot.lockRotation;
            characterController.suppressUpperBodyLocomotion = snapshot.suppressUpperBodyLocomotion;
        }

        if (animator != null)
            animator.applyRootMotion = snapshot.applyRootMotion;
    }
}
