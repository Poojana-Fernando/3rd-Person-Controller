using UnityEngine;
using DeadAuthorGames.ThirdPersonCombat;

namespace Motiong.vCharacterController
{
    public class vThirdPersonController : vThirdPersonAnimator
    {
        public float LocomotionRotationSpeed => ActiveSpeed.rotationSpeed;

        [Header("- Slide Audio")]
        [SerializeField] private AudioSource slideAudioSource;
        [SerializeField] private AudioClip slideClip;
        [SerializeField] private bool loopSlideClip = true;

        public virtual void ControlAnimatorRootMotion()
        {
            if (!this.enabled) return;

            // Slide owns position/yaw; never let animator root curves lift or twist the body.
            if (isSliding)
                return;

            ParkourController parkour = GetComponent<ParkourController>();
            bool skipRootRotation = parkour != null && parkour.IsOverridingRootRotation;
            bool inParkour = parkour != null && parkour.IsInParkourAction;

            if (inParkour)
            {
                transform.position = animator.rootPosition;
                transform.rotation = animator.rootRotation;
                return;
            }

            if (inputSmooth == Vector3.zero)
            {
                if (isCrouching)
                {
                    // Crouch clips are in-place with noisy RootT.xz — do not skate the pivot.
                    // Y is owned by ControlCrouchGroundStick / MoveCharacter.
                }
                else if (useRootMotion)
                {
                    // Rigidbody motor owns pose when useRootMotion is false — applying
                    // animator.rootPosition here fights CheckGround and causes idle jitter.
                    if (_rigidbody != null && _rigidbody.isKinematic)
                    {
                        _rigidbody.position = animator.rootPosition;
                        if (!skipRootRotation)
                            _rigidbody.rotation = animator.rootRotation;
                    }
                    else
                    {
                        transform.position = animator.rootPosition;
                        if (!skipRootRotation)
                            transform.rotation = animator.rootRotation;
                    }
                }

                if (lockMovement)
                    return;
            }

            if (useRootMotion)
                MoveCharacter(moveDirection);
        }

        public virtual void ControlLocomotionType()
        {
            ParkourController parkour = GetComponent<ParkourController>();
            if (lockMovement || (parkour != null && parkour.IsInParkourAction))
                return;

            if (locomotionType.Equals(LocomotionType.OnlyStrafe))
                isStrafing = true;

            SetControllerMoveSpeed(ActiveSpeed);
            SetAnimatorMoveSpeed(ActiveSpeed);

            if (!useRootMotion)
                MoveCharacter(moveDirection);
        }

        public virtual void ControlRotationType()
        {
            ParkourController parkour = GetComponent<ParkourController>();
            if (parkour != null && parkour.IsInParkourAction)
                return;

            if (faceCameraYaw)
            {
                ApplyFaceCameraYawRotation();
                return;
            }

            if (lockRotation)
                return;

            if (!isStrafing)
            {
                RotateFreeTowardCameraWhenWalkingForward();
                return;
            }

            freeYawVelocity = 0f;

            bool rotateIdleWithCamera = strafeSpeed.rotateWithCamera;
            if (input == Vector3.zero && !rotateIdleWithCamera)
                return;

            inputSmooth = Vector3.Lerp(inputSmooth, input, ActiveSpeed.movementSmooth * Time.deltaTime);

            bool faceCamera = (!isSprinting || !sprintOnlyFree) && rotateTarget;
            Vector3 dir = faceCamera ? rotateTarget.forward : moveDirection;
            if (dir.sqrMagnitude > 0.001f)
                RotateToDirection(dir);
        }

        protected virtual void RotateFreeTowardCameraWhenWalkingForward()
        {
            if (!rotateTarget || input.z <= 0.1f)
            {
                freeYawVelocity = 0f;
                return;
            }

            Vector3 cameraForward = Vector3.ProjectOnPlane(rotateTarget.forward, Vector3.up);
            if (cameraForward.sqrMagnitude < 0.001f)
            {
                freeYawVelocity = 0f;
                return;
            }

            float targetYaw = Quaternion.LookRotation(cameraForward).eulerAngles.y;
            float currentYaw = transform.eulerAngles.y;
            float newYaw = Mathf.SmoothDampAngle(
                currentYaw,
                targetYaw,
                ref freeYawVelocity,
                0.12f,
                Mathf.Infinity,
                Time.deltaTime);

            transform.rotation = Quaternion.Euler(0f, newYaw, 0f);
        }

        protected virtual void ApplyFaceCameraYawRotation()
        {
            if (!rotateTarget)
                return;

            freeYawVelocity = 0f;
            inputSmooth = Vector3.Lerp(inputSmooth, input, ActiveSpeed.movementSmooth * Time.deltaTime);

            Vector3 dir = Vector3.ProjectOnPlane(rotateTarget.forward, Vector3.up);
            if (dir.sqrMagnitude > 0.001f)
                RotateToDirection(dir);
        }

        public virtual void UpdateMoveDirection(Transform referenceTransform = null)
        {
            if (input.magnitude <= 0.01)
            {
                moveDirection = Vector3.Lerp(moveDirection, Vector3.zero, ActiveSpeed.movementSmooth * Time.deltaTime);
                return;
            }

            if (referenceTransform && !rotateByWorld)
            {
                //get the right-facing direction of the referenceTransform
                var right = referenceTransform.right;
                right.y = 0;
                //get the forward direction relative to referenceTransform Right
                var forward = Quaternion.AngleAxis(-90, Vector3.up) * right;
                // determine the direction the player will face based on input and the referenceTransform's right and forward directions
                moveDirection = (inputSmooth.x * right) + (inputSmooth.z * forward);
            }
            else
            {
                moveDirection = new Vector3(inputSmooth.x, 0, inputSmooth.z);
            }
        }

        public virtual void Sprint(bool shiftHeld)
        {
            ParkourController parkour = GetComponent<ParkourController>();
            if (isSliding || (parkour != null && parkour.IsInParkourAction))
            {
                isSprinting = false;
                return;
            }

            bool pressingW = input.z > 0.1f;
            bool pressingAorD = Mathf.Abs(input.x) > 0.2f;
            bool pressingS = input.z < -0.1f;
            bool wantsSprint = shiftHeld && isGrounded && pressingW && !pressingAorD && !pressingS;

            if (isCrouching)
            {
                if (wantsSprint)
                    Crouch(false);
                else
                {
                    isSprinting = false;
                    return;
                }
            }

            isSprinting = wantsSprint;
        }

        public virtual void Crouch(bool crouch)
        {
            ParkourController parkour = GetComponent<ParkourController>();
            if (crouch)
            {
                if (isSliding || (parkour != null && parkour.IsInParkourAction) || !isGrounded || isSprinting)
                    return;
            }

            if (isCrouching == crouch)
                return;

            isCrouching = crouch;

            if (crouch)
            {
                BeginCrouchCapsule();
                SnapSlideToGround(Vector3.up);
            }
            else
            {
                EndCrouchCapsule();
            }
        }

        public virtual void Strafe()
        {
            isStrafing = !isStrafing;
        }

        public virtual void Jump()
        {
            ParkourController parkour = GetComponent<ParkourController>();
            if (isSliding || (parkour != null && parkour.IsInParkourAction))
                return;

            if (isCrouching)
                Crouch(false);

            // trigger jump behaviour
            jumpCounter = jumpTimer;
            isJumping = true;

            // trigger jump animations
            if (input.sqrMagnitude < 0.1f)
                animator.CrossFadeInFixedTime("Jump", 0.1f);
            else
                animator.CrossFadeInFixedTime("JumpMove", .2f);
        }

        public virtual void Slide()
        {
            if (isSliding || !isGrounded || !isSprinting)
                return;

            lockMovementBeforeSlide = lockMovement;
            lockRotationBeforeSlide = lockRotation;

            slideMoveDirection = ResolveSlideDirection();
            if (slideMoveDirection.sqrMagnitude > 0.0001f)
                transform.rotation = GetSlideFacingRotation(slideMoveDirection, Vector3.up);

            isSliding = true;
            isSprinting = false;
            isJumping = false;
            jumpCounter = 0f;
            slideCounter = Mathf.Max(0.01f, slideDuration);
            lockMovement = true;
            lockRotation = true;

            if (animator != null)
            {
                animatorApplyRootMotionBeforeSlide = animator.applyRootMotion;
                animator.applyRootMotion = false;
            }

            BeginSlideCapsule();

            // Kill upward velocity before snap — capsule resize must not leave lift.
            Vector3 startVel = _rigidbody.linearVelocity;
            startVel.y = 0f;
            _rigidbody.linearVelocity = startVel;

            SnapSlideToGround(Vector3.up);
            PlaySlideAudio();

            if (animator != null && animator.enabled)
                animator.CrossFadeInFixedTime("Sliding", 0.1f);
        }

        public virtual void StopSlide()
        {
            if (!isSliding)
                return;

            isSliding = false;
            slideCounter = 0f;
            slideMoveDirection = Vector3.zero;
            lockMovement = lockMovementBeforeSlide;
            lockRotation = lockRotationBeforeSlide;

            if (animator != null)
                animator.applyRootMotion = animatorApplyRootMotionBeforeSlide;

            EndSlideCapsule();
            StopSlideAudio();
        }

        public virtual void UpdateSlide()
        {
            if (!isSliding)
                return;

            if (slideCounter <= 0f || !isGrounded)
                StopSlide();
        }

        protected virtual void PlaySlideAudio()
        {
            if (slideAudioSource == null || slideClip == null)
                return;

            slideAudioSource.clip = slideClip;
            slideAudioSource.loop = loopSlideClip;
            slideAudioSource.Play();
        }

        protected virtual void StopSlideAudio()
        {
            if (slideAudioSource == null)
                return;

            if (slideAudioSource.isPlaying)
                slideAudioSource.Stop();

            slideAudioSource.loop = false;
        }
    }
}
