using UnityEngine;

namespace Invector.vCharacterController
{
    public class vThirdPersonController : vThirdPersonAnimator
    {
        public virtual void ControlAnimatorRootMotion()
        {
            if (!this.enabled) return;

            if (inputSmooth == Vector3.zero)
            {
                transform.position = animator.rootPosition;
                transform.rotation = animator.rootRotation;
            }

            if (useRootMotion)
                MoveCharacter(moveDirection);
        }

        public virtual void ControlLocomotionType()
        {
            if (lockMovement) return;

            if (locomotionType.Equals(LocomotionType.OnlyStrafe))
                isStrafing = true;

            SetControllerMoveSpeed(ActiveSpeed);
            SetAnimatorMoveSpeed(ActiveSpeed);

            if (!useRootMotion)
                MoveCharacter(moveDirection);
        }

        public virtual void ControlRotationType()
        {
            if (lockRotation) return;

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
            bool pressingW = input.z > 0.1f;
            bool pressingAorD = Mathf.Abs(input.x) > 0.2f;
            bool pressingS = input.z < -0.1f;

            isSprinting = shiftHeld && isGrounded && pressingW && !pressingAorD && !pressingS;
        }

        public virtual void Strafe()
        {
            isStrafing = !isStrafing;
        }

        public virtual void Jump()
        {
            // trigger jump behaviour
            jumpCounter = jumpTimer;
            isJumping = true;

            // trigger jump animations
            if (input.sqrMagnitude < 0.1f)
                animator.CrossFadeInFixedTime("Jump", 0.1f);
            else
                animator.CrossFadeInFixedTime("JumpMove", .2f);
        }
    }
}