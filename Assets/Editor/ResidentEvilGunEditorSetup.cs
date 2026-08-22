#if UNITY_EDITOR
using System.Collections.Generic;
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
    private const string GlockAimClipPath = "Assets/Animations/GlockAim.anim";

    [MenuItem("Tools/Resident Evil Gun/Build Weapon Assets")]
    public static void BuildWeaponAssetsAlias()
    {
        WeaponsAssetBuilder.BuildWeaponAssets();
    }

    [MenuItem("Tools/Weapons/Wire Scene Hold Aim Guns")]
    public static void WireSceneHoldAimGuns()
    {
        SetupAnimator();
        WireSceneWeaponVisuals();
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        EditorSceneManager.SaveOpenScenes();
        AssetDatabase.SaveAssets();
        Debug.Log("Wired scene Hold/Aim guns, Glock aim animator, and pickups.");
    }

    [MenuItem("Tools/Audio/Setup Player Footsteps")]
    public static void SetupPlayerFootstepsAlias()
    {
        SetupPlayerFootsteps();
    }

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
        EnsureParameter(controller, "WeaponAimStyle", AnimatorControllerParameterType.Int);

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
        var glockAimClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(GlockAimClipPath);
        AnimatorStateMachine gunMachine = controller.layers[1].stateMachine;

        AnimatorState idle = FindState(gunMachine, "GunHoldingIdle");
        AnimatorState run = FindState(gunMachine, "GunHoldingRun");
        AnimatorState sprint = FindState(gunMachine, "GunHoldingSprint");
        AnimatorState aim = FindState(gunMachine, "Aim State");
        AnimatorState glockAim = FindState(gunMachine, "Glock Aim State");
        if (glockAim == null)
            glockAim = gunMachine.AddState("Glock Aim State", new Vector3(430f, 250f, 0f));
        if (glockAimClip != null)
            glockAim.motion = glockAimClip;
        glockAim.writeDefaultValues = false;

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

        if (idle != null && sprint != null)
        {
            AddTransition(idle, sprint, false, 0.1f, new AnimatorCondition
            {
                mode = AnimatorConditionMode.If,
                parameter = "IsSprinting"
            }, new AnimatorCondition
            {
                mode = AnimatorConditionMode.Greater,
                parameter = "InputMagnitude",
                threshold = 0.1f
            }, new AnimatorCondition
            {
                mode = AnimatorConditionMode.IfNot,
                parameter = "IsAiming"
            });
        }

        if (aim != null)
        {
            AddAnyStateTransition(gunMachine, aim, false, 0.08f,
                new AnimatorCondition
                {
                    mode = AnimatorConditionMode.If,
                    parameter = "IsAiming"
                },
                new AnimatorCondition
                {
                    mode = AnimatorConditionMode.Equals,
                    parameter = "WeaponAimStyle",
                    threshold = 0
                });

            AddAimExitTransitions(aim, idle, run);
        }

        if (glockAim != null)
        {
            AddAnyStateTransition(gunMachine, glockAim, false, 0.08f,
                new AnimatorCondition
                {
                    mode = AnimatorConditionMode.If,
                    parameter = "IsAiming"
                },
                new AnimatorCondition
                {
                    mode = AnimatorConditionMode.Equals,
                    parameter = "WeaponAimStyle",
                    threshold = 1
                });

            AddAimExitTransitions(glockAim, idle, run);
        }

        EditorUtility.SetDirty(controller);
    }

    private static void AddAimExitTransitions(AnimatorState aimState, AnimatorState idle, AnimatorState run)
    {
        if (aimState == null)
            return;

        if (idle != null)
        {
            AddTransition(aimState, idle, false, 0.1f, new AnimatorCondition
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
            AddTransition(aimState, run, false, 0.1f, new AnimatorCondition
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

    private static void WireSceneWeaponVisuals()
    {
        var player = GameObject.Find("ThirdPersonController_LITE");
        if (player == null)
        {
            Debug.LogError("ThirdPersonController_LITE not found.");
            return;
        }

        var gun = player.GetComponent<PlayerGunController>();
        if (gun == null)
        {
            Debug.LogError("PlayerGunController missing on player.");
            return;
        }

        SerializedObject so = new SerializedObject(gun);
        Transform weaponHand = so.FindProperty("weaponHand").objectReferenceValue as Transform;
        if (weaponHand == null)
        {
            const string WristPath =
                "guard/Armature/root ground/root hip/spine lower/spine middle/spine upper/arm right shoulder 1/arm right shoulder 2/arm right elbow/arm right wrist";
            weaponHand = player.transform.Find(WristPath);
            so.FindProperty("weaponHand").objectReferenceValue = weaponHand;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        string[] keys = { "AK", "Shotgun", "Glock", "AR" };
        Transform searchRoot = weaponHand != null ? weaponHand : player.transform;
        foreach (string key in keys)
        {
            Transform hold = FindChildByName(searchRoot, $"HoldGun{key}");
            Transform aim = FindChildByName(searchRoot, $"AimGun{key}");
            if (hold != null)
                hold.gameObject.SetActive(false);
            if (aim != null)
                aim.gameObject.SetActive(false);
        }

        Transform legacyHold = FindChildByName(player.transform, "HoldGun");
        Transform legacyAim = FindChildByName(player.transform, "AimGun");
        if (legacyHold != null && legacyHold.name == "HoldGun")
            legacyHold.gameObject.SetActive(false);
        if (legacyAim != null && legacyAim.name == "AimGun")
            legacyAim.gameObject.SetActive(false);

        WeaponDefinition glock = AssetDatabase.LoadAssetAtPath<WeaponDefinition>("Assets/Weapons/Definitions/Pistol.asset");
        WeaponDefinition ar = AssetDatabase.LoadAssetAtPath<WeaponDefinition>("Assets/Weapons/Definitions/AR.asset");
        WeaponDefinition shotgun = AssetDatabase.LoadAssetAtPath<WeaponDefinition>("Assets/Weapons/Definitions/Shotgun870.asset");

        if (ar == null)
        {
            AssetDatabase.ImportAsset("Assets/Weapons/Definitions/AR.asset");
            ar = AssetDatabase.LoadAssetAtPath<WeaponDefinition>("Assets/Weapons/Definitions/AR.asset");
        }

        if (ar == null)
            Debug.LogError("AR WeaponDefinition missing at Assets/Weapons/Definitions/AR.asset");

        ConfigurePickup("Pickup_Pistol", glock, "Pickup_Glock");
        ConfigurePickup("Pickup_Shotgun", shotgun, null);
        ConfigurePickup("Pickup_Sniper", ar, "Pickup_AR");
        ConfigurePickup("Pickup_AR", ar, null);
    }

    private static void ConfigurePickup(string objectName, WeaponDefinition definition, string renameTo)
    {
        GameObject go = GameObject.Find(objectName);
        if (go == null && !string.IsNullOrEmpty(renameTo))
            go = GameObject.Find(renameTo);
        if (go == null)
        {
            // Include inactive objects.
            Transform[] all = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i].name == objectName || (!string.IsNullOrEmpty(renameTo) && all[i].name == renameTo))
                {
                    go = all[i].gameObject;
                    break;
                }
            }
        }

        if (go == null)
        {
            Debug.LogWarning($"Pickup '{objectName}' not found in scene.");
            return;
        }

        if (!string.IsNullOrEmpty(renameTo) && go.name != renameTo)
            go.name = renameTo;

        var pickup = go.GetComponent<WeaponPickup>();
        if (pickup == null)
            pickup = go.AddComponent<WeaponPickup>();

        SerializedObject pickupSo = new SerializedObject(pickup);
        pickupSo.FindProperty("weapon").objectReferenceValue = definition;
        pickupSo.FindProperty("pickupRadius").floatValue = 2.5f;
        pickupSo.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(pickup);
        EditorUtility.SetDirty(go);
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

    private const string FootstepSoundsRoot = "Assets/Spoiled Unknown/XtremeFPS/Sounds";

    public static void SetupPlayerFootsteps()
    {
        EnsureFootstepTags();

        GameObject player = GameObject.Find("ThirdPersonController_LITE");
        if (player == null)
        {
            Debug.LogError("ThirdPersonController_LITE not found in the open scene.");
            return;
        }

        PlayerFootstepSystem footsteps = player.GetComponent<PlayerFootstepSystem>();
        if (footsteps == null)
            footsteps = Undo.AddComponent<PlayerFootstepSystem>(player);

        SerializedObject so = new SerializedObject(footsteps);
        AssignFootstepClips(so, "soundGrass", LoadFootstepClips($"{FootstepSoundsRoot}/Grass"));
        AssignFootstepClips(so, "soundConcrete", LoadFootstepClips($"{FootstepSoundsRoot}/Concrete"));
        AssignFootstepClips(so, "soundWater", LoadFootstepClips($"{FootstepSoundsRoot}/Water"));
        AssignFootstepClips(so, "soundMetal", LoadFootstepClips($"{FootstepSoundsRoot}/Metal"));
        AssignFootstepClips(so, "soundGravel", LoadFootstepClips($"{FootstepSoundsRoot}/Gravel"));
        AssignFootstepClips(so, "soundWood", LoadFootstepClips($"{FootstepSoundsRoot}/Wood"));

        so.FindProperty("jumpClip").objectReferenceValue =
            AssetDatabase.LoadAssetAtPath<AudioClip>($"{FootstepSoundsRoot}/Player/Jump.wav");
        so.FindProperty("landClip").objectReferenceValue =
            AssetDatabase.LoadAssetAtPath<AudioClip>($"{FootstepSoundsRoot}/Player/Land.wav");

        so.FindProperty("grassTag").stringValue = "Grass";
        so.FindProperty("concreteTag").stringValue = "Concrete";
        so.FindProperty("waterTag").stringValue = "Water";
        so.FindProperty("metalTag").stringValue = "Metal";
        so.FindProperty("gravelTag").stringValue = "Gravel";
        so.FindProperty("woodTag").stringValue = "Wood";
        so.FindProperty("walkStepInterval").floatValue = 0.42f;
        so.FindProperty("sprintStepInterval").floatValue = 0.28f;
        so.FindProperty("footstepSensitivity").floatValue = 0.35f;

        Transform audioTransform = player.transform.Find("footstepAudio");
        if (audioTransform == null)
        {
            GameObject audioObject = new GameObject("footstepAudio");
            Undo.RegisterCreatedObjectUndo(audioObject, "Create footstepAudio");
            audioObject.transform.SetParent(player.transform, false);
            AudioSource source = audioObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 1f;
            so.FindProperty("footstepAudioSource").objectReferenceValue = source;
        }
        else
        {
            so.FindProperty("footstepAudioSource").objectReferenceValue =
                audioTransform.GetComponent<AudioSource>();
        }

        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(footsteps);

        GameObject floor = GameObject.Find("Floor");
        if (floor == null)
        {
            Transform level = GameObject.Find("Level")?.transform;
            if (level != null)
            {
                Transform child = level.Find("Floor");
                if (child != null)
                    floor = child.gameObject;
            }
        }

        if (floor != null)
        {
            Undo.RecordObject(floor, "Tag Floor as Concrete");
            floor.tag = "Concrete";
            EditorUtility.SetDirty(floor);
        }
        else
        {
            Debug.LogWarning("Floor not found; tag a ground object as Concrete for default footsteps.");
        }

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        EditorSceneManager.SaveOpenScenes();
        Debug.Log("Player footsteps wired (surface tags + XtremeFPS clips).");
    }

    private static void AssignFootstepClips(SerializedObject so, string propertyName, AudioClip[] clips)
    {
        SerializedProperty property = so.FindProperty(propertyName);
        property.arraySize = clips.Length;
        for (int i = 0; i < clips.Length; i++)
            property.GetArrayElementAtIndex(i).objectReferenceValue = clips[i];
    }

    private static AudioClip[] LoadFootstepClips(string folder)
    {
        if (!AssetDatabase.IsValidFolder(folder))
            return System.Array.Empty<AudioClip>();

        string[] guids = AssetDatabase.FindAssets("t:AudioClip", new[] { folder });
        List<AudioClip> clips = new List<AudioClip>(guids.Length);
        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            if (clip != null)
                clips.Add(clip);
        }

        clips.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
        return clips.ToArray();
    }

    private static void EnsureFootstepTags()
    {
        string[] required = { "Grass", "Concrete", "Water", "Metal", "Gravel", "Wood" };
        SerializedObject tagManager =
            new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
        SerializedProperty tags = tagManager.FindProperty("tags");

        for (int i = 0; i < required.Length; i++)
        {
            string tag = required[i];
            bool exists = false;
            for (int t = 0; t < tags.arraySize; t++)
            {
                if (tags.GetArrayElementAtIndex(t).stringValue == tag)
                {
                    exists = true;
                    break;
                }
            }

            if (exists)
                continue;

            tags.InsertArrayElementAtIndex(tags.arraySize);
            tags.GetArrayElementAtIndex(tags.arraySize - 1).stringValue = tag;
        }

        tagManager.ApplyModifiedProperties();
        AssetDatabase.SaveAssets();
    }
}
#endif
