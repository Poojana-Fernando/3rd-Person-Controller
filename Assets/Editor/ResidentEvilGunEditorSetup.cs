#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class ResidentEvilGunEditorSetup
{
    private const string ControllerPath = "Assets/Invector-3rdPersonController_LITE/Animator/Invector@BasicLocomotion.controller";
    private const string MaskPath = "Assets/Animations/GuardSpineMiddleMask.mask";
    private const string WeaponHoldClipPath = "Assets/Kevin Iglesias/Human Animations/Animations/Masked Poses/Weaponhold.anim";

    [MenuItem("Tools/Resident Evil Gun/Attach Guns To Right Hand")]
    public static void AttachGunsToRightHand()
    {
        var player = GameObject.Find("ThirdPersonController_LITE");
        if (player == null)
        {
            Debug.LogError("ThirdPersonController_LITE not found.");
            return;
        }

        Transform rightWrist = player.transform.Find("guard/Armature/root ground/root hip/spine lower/spine middle/spine upper/arm right shoulder 1/arm right shoulder 2/arm right elbow/arm right wrist");
        if (rightWrist == null)
        {
            Debug.LogError("arm right wrist bone not found.");
            return;
        }

        ReparentGun(player, rightWrist, "HoldGun");
        ReparentGun(player, rightWrist, "AimGun");

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Debug.Log("Guns attached to arm right wrist.");
    }

    private static void ReparentGun(GameObject player, Transform hand, string gunName)
    {
        Transform gun = null;
        var transforms = player.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < transforms.Length; i++)
        {
            if (transforms[i].name == gunName)
            {
                gun = transforms[i];
                break;
            }
        }

        if (gun == null)
        {
            Debug.LogWarning($"{gunName} not found.");
            return;
        }

        Undo.RecordObject(gun, "Attach Gun To Hand");
        gun.SetParent(hand, true);
        EditorUtility.SetDirty(gun);
    }

    [MenuItem("Tools/Resident Evil Gun/Setup All")]
    public static void SetupAll()
    {
        CreateSpineMiddleMask();
        SetupAnimator();
        SetupSceneObjects();
        AttachGunsToRightHand();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("Resident Evil Gun setup complete.");
    }

    private static void CreateSpineMiddleMask()
    {
        Directory.CreateDirectory("Assets/Animations");

        var mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(MaskPath);
        if (mask == null)
        {
            mask = new AvatarMask();
            AssetDatabase.CreateAsset(mask, MaskPath);
        }

        mask.transformCount = 0;

        mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.Root, false);
        mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.Body, false);
        mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.Head, true);
        mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftLeg, false);
        mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightLeg, false);
        mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftArm, true);
        mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightArm, true);
        mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftFingers, true);
        mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightFingers, true);
        mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftFootIK, false);
        mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightFootIK, false);
        mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftHandIK, false);
        mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightHandIK, false);

        EditorUtility.SetDirty(mask);
    }

    private static void SetupAnimator()
    {
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller == null)
        {
            Debug.LogError("Animator controller not found.");
            return;
        }

        EnsureParameter(controller, "IsGunEquipped", AnimatorControllerParameterType.Bool);
        EnsureParameter(controller, "IsAiming", AnimatorControllerParameterType.Bool);

        controller.layers[0].defaultWeight = 1f;

        if (controller.layers.Length < 2)
        {
            Debug.LogError("Gun Layer missing on animator controller.");
            return;
        }

        var mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(MaskPath);
        controller.layers[1].avatarMask = mask;
        controller.layers[1].defaultWeight = 0f;

        var weaponHold = AssetDatabase.LoadAssetAtPath<AnimationClip>(WeaponHoldClipPath);
        AnimatorStateMachine gunMachine = controller.layers[1].stateMachine;

        AnimatorState idle = FindState(gunMachine, "GunHoldingIdle");
        AnimatorState run = FindState(gunMachine, "GunHoldingRun");
        AnimatorState sprint = FindState(gunMachine, "GunHoldingSprint");
        AnimatorState aim = FindState(gunMachine, "Aim State");

        if (run != null && run.motion == null && weaponHold != null)
            run.motion = weaponHold;
        if (sprint != null && sprint.motion == null && weaponHold != null)
            sprint.motion = weaponHold;

        ClearTransitions(gunMachine);
        if (idle != null && run != null)
        {
            AddTransition(idle, run, false, 0.12f, new AnimatorCondition
            {
                mode = AnimatorConditionMode.Greater,
                parameter = "InputMagnitude",
                threshold = 0.1f
            }, new AnimatorCondition
            {
                mode = AnimatorConditionMode.IfNot,
                parameter = "IsSprinting"
            }, new AnimatorCondition
            {
                mode = AnimatorConditionMode.IfNot,
                parameter = "IsAiming"
            });
            AddTransition(run, idle, false, 0.12f, new AnimatorCondition
            {
                mode = AnimatorConditionMode.Less,
                parameter = "InputMagnitude",
                threshold = 0.1f
            }, new AnimatorCondition
            {
                mode = AnimatorConditionMode.IfNot,
                parameter = "IsAiming"
            });
        }

        if (run != null && sprint != null)
        {
            AddTransition(run, sprint, false, 0.1f, new AnimatorCondition
            {
                mode = AnimatorConditionMode.If,
                parameter = "IsSprinting"
            }, new AnimatorCondition
            {
                mode = AnimatorConditionMode.IfNot,
                parameter = "IsAiming"
            });
            AddTransition(sprint, run, false, 0.1f, new AnimatorCondition
            {
                mode = AnimatorConditionMode.IfNot,
                parameter = "IsSprinting"
            }, new AnimatorCondition
            {
                mode = AnimatorConditionMode.IfNot,
                parameter = "IsAiming"
            });
        }

        if (aim != null)
        {
            AddAnyStateTransition(gunMachine, aim, false, 0.08f, new AnimatorCondition
            {
                mode = AnimatorConditionMode.If,
                parameter = "IsAiming"
            });

            if (idle != null)
            {
                AddTransition(aim, idle, false, 0.1f, new AnimatorCondition
                {
                    mode = AnimatorConditionMode.IfNot,
                    parameter = "IsAiming"
                }, new AnimatorCondition
                {
                    mode = AnimatorConditionMode.Less,
                    parameter = "InputMagnitude",
                    threshold = 0.1f
                });
            }

            if (run != null)
            {
                AddTransition(aim, run, false, 0.1f, new AnimatorCondition
                {
                    mode = AnimatorConditionMode.IfNot,
                    parameter = "IsAiming"
                }, new AnimatorCondition
                {
                    mode = AnimatorConditionMode.Greater,
                    parameter = "InputMagnitude",
                    threshold = 0.1f
                }, new AnimatorCondition
                {
                    mode = AnimatorConditionMode.IfNot,
                    parameter = "IsSprinting"
                });
            }
        }

        EditorUtility.SetDirty(controller);
    }

    private static void SetupSceneObjects()
    {
        var player = GameObject.Find("ThirdPersonController_LITE");
        if (player != null)
        {
            if (player.GetComponent<PlayerGunController>() == null)
                player.AddComponent<PlayerGunController>();

            if (player.GetComponent<UpperBodyAim>() == null)
                player.AddComponent<UpperBodyAim>();
        }

        var aimCameraObject = GameObject.Find("AimCamera");
        if (aimCameraObject != null)
        {
            if (aimCameraObject.GetComponent<ResidentEvilAimCamera>() == null)
                aimCameraObject.AddComponent<ResidentEvilAimCamera>();

            var camera = aimCameraObject.GetComponent<Camera>();
            var listener = aimCameraObject.GetComponent<AudioListener>();
            if (camera != null) camera.enabled = false;
            if (listener != null) listener.enabled = false;
        }

        var canvas = GameObject.Find("Canvas");
        if (canvas != null)
            canvas.transform.localScale = Vector3.one;

        var aimGunObject = FindChildByName(player.transform, "AimGun");
        if (aimGunObject != null)
            aimGunObject.gameObject.SetActive(false);

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
    }

    private static Transform FindChildByName(Transform root, string childName)
    {
        var transforms = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < transforms.Length; i++)
        {
            if (transforms[i].name == childName)
                return transforms[i];
        }

        return null;
    }

    private static void EnsureParameter(AnimatorController controller, string name, AnimatorControllerParameterType type)
    {
        foreach (var parameter in controller.parameters)
        {
            if (parameter.name == name)
                return;
        }

        controller.AddParameter(name, type);
    }

    private static AnimatorState FindState(AnimatorStateMachine machine, string stateName)
    {
        foreach (ChildAnimatorState child in machine.states)
        {
            if (child.state.name == stateName)
                return child.state;
        }

        return null;
    }

    private static void ClearTransitions(AnimatorStateMachine machine)
    {
        machine.anyStateTransitions = new AnimatorStateTransition[0];
        foreach (ChildAnimatorState child in machine.states)
            child.state.transitions = new AnimatorStateTransition[0];
    }

    private static void AddTransition(AnimatorState from, AnimatorState to, bool hasExitTime, float duration, params AnimatorCondition[] conditions)
    {
        AnimatorStateTransition transition = from.AddTransition(to);
        transition.hasExitTime = hasExitTime;
        transition.duration = duration;
        transition.hasFixedDuration = true;
        transition.canTransitionToSelf = false;
        foreach (AnimatorCondition condition in conditions)
            transition.AddCondition(condition.mode, condition.threshold, condition.parameter);
    }

    private static void AddAnyStateTransition(AnimatorStateMachine machine, AnimatorState to, bool hasExitTime, float duration, params AnimatorCondition[] conditions)
    {
        AnimatorStateTransition transition = machine.AddAnyStateTransition(to);
        transition.hasExitTime = hasExitTime;
        transition.duration = duration;
        transition.hasFixedDuration = true;
        transition.canTransitionToSelf = false;
        foreach (AnimatorCondition condition in conditions)
            transition.AddCondition(condition.mode, condition.threshold, condition.parameter);
    }
}
#endif
