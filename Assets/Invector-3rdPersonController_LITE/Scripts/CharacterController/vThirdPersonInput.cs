using UnityEngine;

namespace Motiong.vCharacterController
{
    public class vThirdPersonInput : MonoBehaviour
    {
        #region Variables       

        [Header("Controller Input")]
        public string horizontalInput = "Horizontal";
        public string verticallInput = "Vertical";
        public KeyCode jumpInput = KeyCode.Space;
        public KeyCode strafeInput = KeyCode.Tab;
        public KeyCode sprintInput = KeyCode.LeftShift;
        public KeyCode slideInput = KeyCode.C;

        [Header("Camera Input")]
        public string rotateCameraXInput = "Mouse X";
        public string rotateCameraYInput = "Mouse Y";

        [HideInInspector] public vThirdPersonController cc;
        [HideInInspector] public vThirdPersonCamera tpCamera;
        [HideInInspector] public Camera cameraMain;

        protected PlayerGunController gunController;
        protected ParkourController parkourController;

        #endregion

        protected virtual void Start()
        {
            gunController = GetComponent<PlayerGunController>();
            parkourController = GetComponent<ParkourController>();
            InitilizeController();
            InitializeTpCamera();
        }

        protected virtual void FixedUpdate()
        {
            bool inParkourAction = parkourController != null && parkourController.IsInParkourAction;

            if (inParkourAction)
                return;

            cc.UpdateMotor();               // updates the ThirdPersonMotor methods
            cc.UpdateSlide();               // ends slide when timer/grounding expire
            cc.ControlLocomotionType();     // handle the controller locomotion type and movespeed
            cc.ControlRotationType();       // handle the controller rotation type
        }

        protected virtual void Update()
        {
            InputHandle();                  // update the input methods

            bool inParkourAction = parkourController != null && parkourController.IsInParkourAction;
            if (!inParkourAction)
                cc.UpdateAnimator();            // updates the Animator Parameters
        }

        public virtual void OnAnimatorMove()
        {
            cc.ControlAnimatorRootMotion(); // handle root motion animations 
        }

        #region Basic Locomotion Inputs

        protected virtual void InitilizeController()
        {
            cc = GetComponent<vThirdPersonController>();

            if (cc != null)
                cc.Init();
        }

        protected virtual void InitializeTpCamera()
        {
            if (tpCamera == null)
            {
                tpCamera = FindFirstObjectByType<vThirdPersonCamera>();
                if (tpCamera == null)
                    return;
                if (tpCamera)
                {
                    tpCamera.SetMainTarget(this.transform);
                    tpCamera.Init();
                }
            }
        }

        protected virtual void InputHandle()
        {
            if (parkourController != null && parkourController.IsInParkourAction)
            {
                cc.input = Vector3.zero;
                cc.inputSmooth = Vector3.zero;
                cc.Sprint(false);
                CameraInput(updateMoveDirection: false);
                return;
            }

            MoveInput();
            CameraInput();
            SprintInput();
            SlideInput();
            StrafeInput();
            JumpInput();
        }

        public virtual void MoveInput()
        {
            cc.input.x = Input.GetAxis(horizontalInput);
            cc.input.z = Input.GetAxis(verticallInput);
        }

        protected virtual void CameraInput(bool updateMoveDirection = true)
        {
            if (gunController != null && gunController.BlockThirdPersonCameraInput)
            {
                Camera activeCamera = gunController.GetActiveCamera();
                if (activeCamera != null)
                {
                    cameraMain = activeCamera;
                    cc.rotateTarget = activeCamera.transform;
                    if (updateMoveDirection)
                        cc.UpdateMoveDirection(activeCamera.transform);
                }

                return;
            }

            Camera thirdPersonCam = tpCamera != null ? tpCamera.GetComponent<Camera>() : null;
            if (thirdPersonCam != null)
            {
                cameraMain = thirdPersonCam;
                cc.rotateTarget = thirdPersonCam.transform;
            }
            else if (!cameraMain)
            {
                if (!Camera.main) Debug.Log("Missing a Camera with the tag MainCamera, please add one.");
                else
                {
                    cameraMain = Camera.main;
                    cc.rotateTarget = cameraMain.transform;
                }
            }

            if (cameraMain && updateMoveDirection)
            {
                cc.UpdateMoveDirection(cameraMain.transform);
            }

            if (tpCamera == null)
                return;

            var Y = Input.GetAxis(rotateCameraYInput);
            var X = Input.GetAxis(rotateCameraXInput);

            tpCamera.RotateCamera(X, Y);
        }

        protected virtual void StrafeInput()
        {
            if (gunController != null && gunController.BlockStrafeToggle)
                return;

            if (Input.GetKeyDown(strafeInput))
                cc.Strafe();
        }

        protected virtual void SprintInput()
        {
            if (gunController != null && gunController.SuppressSprint)
            {
                cc.Sprint(false);
                return;
            }

            cc.Sprint(Input.GetKey(sprintInput));
        }

        protected virtual void SlideInput()
        {
            if (gunController != null && gunController.SuppressSprint)
                return;

            if (!Input.GetKey(sprintInput) || !Input.GetKeyDown(slideInput))
                return;

            if (cc.isSprinting && cc.isGrounded && !cc.isSliding)
                cc.Slide();
        }

        /// <summary>
        /// Conditions to trigger the Jump animation & behavior
        /// </summary>
        /// <returns></returns>
        protected virtual bool JumpConditions()
        {
            return cc.isGrounded && cc.GroundAngle() < cc.slopeLimit && !cc.isJumping && !cc.stopMove && !cc.isSliding;
        }

        /// <summary>
        /// Input to trigger the Jump 
        /// </summary>
        protected virtual void JumpInput()
        {
            // Near a ledge, Space is owned by Parkour StepUp — do not also jump.
            if (parkourController != null && parkourController.ShouldSuppressJump)
                return;

            if (Input.GetKeyDown(jumpInput) && JumpConditions())
                cc.Jump();
        }

        #endregion       
    }
}