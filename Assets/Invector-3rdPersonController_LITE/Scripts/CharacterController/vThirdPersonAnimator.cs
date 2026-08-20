
using UnityEngine;

namespace Motiong.vCharacterController
{
    public class vThirdPersonAnimator : vThirdPersonMotor
    {
        #region Variables                

        public const float walkSpeed = 0.5f;
        public const float runningSpeed = 1f;
        public const float sprintSpeed = 1.5f;

        #endregion  

        public virtual void UpdateAnimator()
        {
            if (animator == null || !animator.enabled) return;

            // Always drive the strafe locomotion blend tree (8-directional WASD).
            // Free vs strafe is rotation-only: free keeps current facing, strafe locks to camera.
            animator.SetBool(vAnimatorParameters.IsStrafing, true);
            animator.SetBool(vAnimatorParameters.IsSprinting, isSprinting);
            animator.SetBool(vAnimatorParameters.IsGrounded, isGrounded);
            animator.SetFloat(vAnimatorParameters.GroundDistance, groundDistance);
            animator.SetFloat(vAnimatorParameters.InputHorizontal, stopMove ? 0 : horizontalSpeed, ActiveSpeed.animationSmooth, Time.deltaTime);
            animator.SetFloat(vAnimatorParameters.InputVertical, stopMove ? 0 : verticalSpeed, ActiveSpeed.animationSmooth, Time.deltaTime);
            animator.SetFloat(vAnimatorParameters.InputMagnitude, stopMove ? 0f : inputMagnitude, ActiveSpeed.animationSmooth, Time.deltaTime);
        }

        public virtual void SetAnimatorMoveSpeed(vMovementSpeed speed)
        {
            Vector3 relativeInput = transform.InverseTransformDirection(moveDirection);

            // Holding W while turning with the mouse should stay on walk-forward,
            // not flicker between strafe clips (that fight rotation and cause shake).
            if (!isStrafing && input.z > 0.1f && Mathf.Abs(input.x) < 0.2f)
            {
                verticalSpeed = relativeInput.magnitude;
                horizontalSpeed = 0f;
            }
            else
            {
                verticalSpeed = relativeInput.z;
                horizontalSpeed = relativeInput.x;
            }

            var newInput = new Vector2(verticalSpeed, horizontalSpeed);

            if (speed.walkByDefault)
                inputMagnitude = Mathf.Clamp(newInput.magnitude, 0, isSprinting ? runningSpeed : walkSpeed);
            else
                inputMagnitude = Mathf.Clamp(isSprinting ? newInput.magnitude + 0.5f : newInput.magnitude, 0, isSprinting ? sprintSpeed : runningSpeed);
        }
    }

    public static partial class vAnimatorParameters
    {
        public static int InputHorizontal = Animator.StringToHash("InputHorizontal");
        public static int InputVertical = Animator.StringToHash("InputVertical");
        public static int InputMagnitude = Animator.StringToHash("InputMagnitude");
        public static int IsGrounded = Animator.StringToHash("IsGrounded");
        public static int IsStrafing = Animator.StringToHash("IsStrafing");
        public static int IsSprinting = Animator.StringToHash("IsSprinting");
        public static int GroundDistance = Animator.StringToHash("GroundDistance");
    }
}