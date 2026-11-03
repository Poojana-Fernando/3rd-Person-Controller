#if UNITY_EDITOR
using System.IO;
using DeadAuthorGames.ThirdPersonCombat;
using Game.FirstPerson;
using UnityEditor;
using UnityEngine;

public static class FirstPersonWeaponSetup
{
    const string ProfileFolder = "Assets/_Game/Settings/FirstPerson/WeaponProfiles";
    const string SettingsPath = "Assets/_Game/Resources/FirstPersonSettings.asset";

    [MenuItem("Tools/First Person/Setup Layers Profiles And Gun Anchors")]
    public static void RunFullSetup()
    {
        EnsureLayers();
        EnsureSettingsAsset();
        EnsureProfilesAndGunAnchors();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("First Person setup complete: layers, settings, weapon profiles, gun anchors.");
    }

    static void EnsureLayers()
    {
        SerializedObject tagManager = new SerializedObject(
            AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
        SerializedProperty layers = tagManager.FindProperty("layers");

        SetLayerName(layers, FirstPersonConstants.DefaultHiddenBodyLayer, FirstPersonConstants.HiddenBodyLayerName);
        SetLayerName(layers, FirstPersonConstants.DefaultViewmodelLayer, FirstPersonConstants.ViewmodelLayerName);

        tagManager.ApplyModifiedProperties();
    }

    static void SetLayerName(SerializedProperty layers, int index, string name)
    {
        if (index < 0 || index >= layers.arraySize)
            return;

        SerializedProperty slot = layers.GetArrayElementAtIndex(index);
        if (string.IsNullOrEmpty(slot.stringValue))
            slot.stringValue = name;
        else if (slot.stringValue != name)
            Debug.LogWarning($"Layer {index} is '{slot.stringValue}', expected '{name}'. Rename manually if needed.");
    }

    static void EnsureSettingsAsset()
    {
        if (!AssetDatabase.IsValidFolder("Assets/_Game/Resources"))
            AssetDatabase.CreateFolder("Assets/_Game", "Resources");

        FirstPersonSettings existing = AssetDatabase.LoadAssetAtPath<FirstPersonSettings>(SettingsPath);
        if (existing != null)
            return;

        FirstPersonSettings settings = ScriptableObject.CreateInstance<FirstPersonSettings>();
        AssetDatabase.CreateAsset(settings, SettingsPath);
    }

    static void EnsureProfilesAndGunAnchors()
    {
        if (!AssetDatabase.IsValidFolder("Assets/_Game/Settings"))
            AssetDatabase.CreateFolder("Assets/_Game", "Settings");
        if (!AssetDatabase.IsValidFolder("Assets/_Game/Settings/FirstPerson"))
            AssetDatabase.CreateFolder("Assets/_Game/Settings", "FirstPerson");
        if (!AssetDatabase.IsValidFolder(ProfileFolder))
            AssetDatabase.CreateFolder("Assets/_Game/Settings/FirstPerson", "WeaponProfiles");

        string[] guids = AssetDatabase.FindAssets("t:WeaponDefinition");
        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            WeaponDefinition weapon = AssetDatabase.LoadAssetAtPath<WeaponDefinition>(path);
            if (weapon == null)
                continue;

            string profilePath = $"{ProfileFolder}/{weapon.name}_FPProfile.asset";
            FirstPersonWeaponProfile profile = AssetDatabase.LoadAssetAtPath<FirstPersonWeaponProfile>(profilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<FirstPersonWeaponProfile>();
                ApplyDefaultProfile(weapon, profile);
                AssetDatabase.CreateAsset(profile, profilePath);
            }

            if (weapon.firstPersonProfile != profile)
            {
                weapon.firstPersonProfile = profile;
                EditorUtility.SetDirty(weapon);
            }

            if (weapon.aimPrefab != null)
                EnsureGunAnchors(weapon.aimPrefab);
        }
    }

    static void ApplyDefaultProfile(WeaponDefinition weapon, FirstPersonWeaponProfile profile)
    {
        if (weapon.weaponAimStyle == 1)
        {
            profile.hipLocalPosition = new Vector3(0.18f, -0.16f, 0.35f);
            profile.adsLocalPosition = new Vector3(0f, -0.1f, 0.32f);
            profile.weaponLength = 0.45f;
            profile.eyeRelief = 0.32f;
        }
        else if (weapon.visualKey.Contains("Shotgun") || weapon.displayName.Contains("Shotgun"))
        {
            profile.hipLocalPosition = new Vector3(0.2f, -0.2f, 0.48f);
            profile.weaponLength = 1.05f;
        }
    }

    static void EnsureGunAnchors(GameObject prefab)
    {
        string path = AssetDatabase.GetAssetPath(prefab);
        if (string.IsNullOrEmpty(path))
            return;

        GameObject root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            Transform sight = FindOrCreate(root.transform, "Sight");
            Transform rightGrip = FindOrCreate(root.transform, "RightGrip");
            Transform leftGrip = FindOrCreate(root.transform, "LeftGrip");

            if (sight != null && sight.localPosition == Vector3.zero)
                sight.localPosition = new Vector3(0f, 0.06f, 0.35f);
            if (rightGrip != null && rightGrip.localPosition == Vector3.zero)
                rightGrip.localPosition = new Vector3(0.02f, -0.04f, 0.12f);
            if (leftGrip != null && leftGrip.localPosition == Vector3.zero)
                leftGrip.localPosition = new Vector3(-0.06f, -0.05f, 0.18f);

            PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    static Transform FindOrCreate(Transform parent, string name)
    {
        Transform existing = parent.Find(name);
        if (existing != null)
            return existing;

        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        return go.transform;
    }
}
#endif
