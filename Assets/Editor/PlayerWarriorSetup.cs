#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations.Rigging;
using UnityEngine.SceneManagement;

public static class PlayerWarriorSetup
{
    private const string PlayerName = "ThirdPersonController_LITE";
    private const string WarriorName = "Sci-FI Warrior Armor";
    private const string GuardName = "guard";

    [MenuItem("Tools/Player/Use Sci-FI Warrior Armor As Player")]
    public static void UseSciFiWarriorAsPlayer()
    {
        GameObject player = GameObject.Find(PlayerName);
        if (player == null)
        {
            Debug.LogError($"[PlayerWarrior] '{PlayerName}' not found.");
            return;
        }

        Transform warrior = FindChildRecursive(player.transform, WarriorName);
        if (warrior == null)
        {
            Debug.LogError($"[PlayerWarrior] '{WarriorName}' not found under player. Drag the model under {PlayerName} first.");
            return;
        }

        Undo.RegisterFullObjectHierarchyUndo(player, "Use Sci-FI Warrior As Player");

        Transform guard = player.transform.Find(GuardName);
        if (guard != null)
            guard.gameObject.SetActive(false);

        warrior.gameObject.SetActive(true);
        warrior.localPosition = Vector3.zero;
        warrior.localRotation = Quaternion.identity;

        // Root Animator drives locomotion; disable nested Animator so they don't fight.
        Animator nested = warrior.GetComponent<Animator>();
        if (nested != null)
            nested.enabled = false;

        Animator rootAnimator = player.GetComponent<Animator>();
        if (rootAnimator != null)
        {
            if (nested != null && nested.avatar != null)
                rootAnimator.avatar = nested.avatar;
            rootAnimator.applyRootMotion = true;
            rootAnimator.enabled = true;
            rootAnimator.Rebind();
            rootAnimator.Update(0f);
        }

        Transform hand = FindChildRecursive(warrior, "CC_Base_R_Hand");
        Transform spine01 = FindChildRecursive(warrior, "CC_Base_Spine01");
        Transform spine02 = FindChildRecursive(warrior, "CC_Base_Spine02");
        Transform neck = FindChildRecursive(warrior, "CC_Base_NeckTwist01");
        Transform head = FindChildRecursive(warrior, "CC_Base_Head");

        if (hand == null || spine01 == null || spine02 == null || head == null)
        {
            Debug.LogError("[PlayerWarrior] Missing CC_Base bones (hand/spine/head). Is the model a CC/Reallusion rig?");
            return;
        }

        int movedGuns = ReparentWeaponVisuals(player.transform, hand);

        PlayerGunController gun = player.GetComponent<PlayerGunController>();
        if (gun != null)
        {
            SerializedObject so = new SerializedObject(gun);
            so.FindProperty("weaponHand").objectReferenceValue = hand;
            so.FindProperty("headAimOrigin").objectReferenceValue = neck != null ? neck : head;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(gun);
        }

        UpperBodyAim upperBody = player.GetComponent<UpperBodyAim>();
        if (upperBody != null)
        {
            SerializedObject so = new SerializedObject(upperBody);
            so.FindProperty("spineMiddle").objectReferenceValue = spine01;
            so.FindProperty("spineUpper").objectReferenceValue = spine02;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(upperBody);
        }

        ResidentEvilAimCamera aimCamera = Object.FindAnyObjectByType<ResidentEvilAimCamera>();
        if (aimCamera != null)
        {
            SerializedObject so = new SerializedObject(aimCamera);
            SerializedProperty spine = so.FindProperty("spineAnchor");
            if (spine != null)
                spine.objectReferenceValue = spine01;
            SerializedProperty follow = so.FindProperty("followTarget");
            if (follow != null && follow.objectReferenceValue == null)
                follow.objectReferenceValue = player.transform;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(aimCamera);
        }

        MultiAimConstraint headAim = player.GetComponentInChildren<MultiAimConstraint>(true);
        if (headAim != null)
        {
            SerializedObject so = new SerializedObject(headAim);
            SerializedProperty constrained = so.FindProperty("m_Data.m_ConstrainedObject");
            if (constrained == null)
                constrained = so.FindProperty("m_ConstrainedObject");
            // Rigging package uses nested data
            if (constrained == null)
            {
                SerializedProperty data = so.FindProperty("m_Data");
                if (data != null)
                    constrained = data.FindPropertyRelative("m_ConstrainedObject");
            }

            if (constrained != null)
            {
                constrained.objectReferenceValue = head;
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(headAim);
            }
        }

        RigBuilder rigBuilder = player.GetComponent<RigBuilder>();
        if (rigBuilder != null)
        {
            rigBuilder.Build();
            EditorUtility.SetDirty(rigBuilder);
        }

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        EditorSceneManager.SaveOpenScenes();
        AssetDatabase.SaveAssets();

        Debug.Log(
            $"[PlayerWarrior] Player visual set to '{WarriorName}'. " +
            $"Guns moved: {movedGuns}. Guard disabled. Spine/hand/head aim retargeted.");
    }

    private static int ReparentWeaponVisuals(Transform playerRoot, Transform hand)
    {
        int count = 0;
        Transform[] all = playerRoot.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < all.Length; i++)
        {
            Transform t = all[i];
            if (t == null || t == hand)
                continue;

            string n = t.name;
            if (!n.StartsWith("HoldGun") && !n.StartsWith("AimGun"))
                continue;

            if (t.parent == hand)
                continue;

            Undo.SetTransformParent(t, hand, "Reparent weapon to warrior hand");
            count++;
        }

        return count;
    }

    private static Transform FindChildRecursive(Transform root, string exactName)
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
}
#endif
