#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class WeaponsAssetBuilder
{
    private const string WeaponsRoot = "Assets/Weapons";
    private const string PrefabRoot = "Assets/Weapons/Prefabs";
    private const string DefinitionsRoot = "Assets/Weapons/Definitions";
    private const string PickupRoot = "Assets/Weapons/Pickups";

    private const string AkFbxPath = "Assets/ak-47s/source/ak-47A.fbx";
    private const string PistolFbxPath = "Assets/Spoiled Unknown/XtremeFPS/Weapon/Models/Pistol.fbx";
    private const string ShotgunFbxPath = "Assets/Spoiled Unknown/XtremeFPS/Weapon/Models/870_Shotgun.fbx";
    private const string SniperFbxPath = "Assets/Spoiled Unknown/XtremeFPS/Weapon/Models/L96_Sniper_Rifle.fbx";

    private const string TracerPath = "Assets/Spoiled Unknown/XtremeFPS/Weapon/Bullet/Bullet.prefab";
    private const string MuzzleFlashPath = "Assets/Spoiled Unknown/XtremeFPS/Weapon/Effects/Prefabs/MuzzleFlashEffect.prefab";
    private const string HitPath = "Assets/Spoiled Unknown/XtremeFPS/Weapon/Effects/Prefabs/BulletImpactMetalEffect.prefab";

    private const string AkClipPath = "Assets/Spoiled Unknown/XtremeFPS/Sounds/Weapons/AK - 47.wav";
    private const string PistolClipPath = "Assets/Spoiled Unknown/XtremeFPS/Sounds/Weapons/Pistol.wav";
    private const string ShotgunClipPath = "Assets/Spoiled Unknown/XtremeFPS/Sounds/Weapons/870_Shotgun.wav";
    private const string SniperClipPath = "Assets/Spoiled Unknown/XtremeFPS/Sounds/Weapons/L96_Sniper_Rifle.wav";

    private static readonly Vector3 AkHandPosition = new Vector3(0.00053491455f, 0.002105952f, -0.00044486095f);
    private static readonly Vector3 AkHandEuler = new Vector3(-110.569f, -112.08f, 66.671f);
    private static readonly Vector3 AkHandScale = new Vector3(0.0008f, 0.0008f, 0.0008f);
    private static readonly Vector3 AkMuzzleLocal = new Vector3(1.14f, 0.8f, 4.56f);

    [MenuItem("Tools/Weapons/Build Weapon Assets")]
    public static void BuildWeaponAssets()
    {
        Debug.LogWarning(
            "Build Weapon Assets updates definitions/pickups only. Scene HoldGun*/AimGun* visuals are authored in-scene; " +
            "use Tools/Weapons/Wire Scene Hold Aim Guns after hand posing.");

        EnsureFolders();

        GameObject tracer = AssetDatabase.LoadAssetAtPath<GameObject>(TracerPath);
        GameObject muzzleFlash = AssetDatabase.LoadAssetAtPath<GameObject>(MuzzleFlashPath);
        GameObject hit = AssetDatabase.LoadAssetAtPath<GameObject>(HitPath);

        WeaponDefinition ak = CreateOrUpdateDefinition(
            "AK47",
            "AK-47",
            1,
            "AK",
            0,
            true,
            600f,
            15f,
            120f,
            1,
            0.6f,
            2.5f,
            0.8f,
            5f,
            tracer,
            muzzleFlash,
            hit);

        WeaponDefinition pistol = CreateOrUpdateDefinition(
            "Pistol",
            "Glock",
            2,
            "Glock",
            1,
            false,
            300f,
            18f,
            80f,
            1,
            0.4f,
            1.8f,
            0.5f,
            3f,
            tracer,
            muzzleFlash,
            hit);

        WeaponDefinition shotgun = CreateOrUpdateDefinition(
            "Shotgun870",
            "870 Shotgun",
            3,
            "Shotgun",
            0,
            false,
            70f,
            9f,
            40f,
            8,
            6f,
            5f,
            2f,
            6f,
            tracer,
            muzzleFlash,
            hit);

        WeaponDefinition ar = CreateOrUpdateDefinition(
            "AR",
            "AR",
            4,
            "AR",
            0,
            true,
            650f,
            14f,
            120f,
            1,
            0.5f,
            2.2f,
            0.7f,
            5f,
            tracer,
            muzzleFlash,
            hit);

        var pistolAim = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabRoot}/Pistol_Aim.prefab");
        var shotgunAim = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabRoot}/Shotgun_Aim.prefab");
        var arAim = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabRoot}/Sniper_Aim.prefab");

        CreatePickupPrefab("Pickup_Pistol", pistol, pistolAim);
        CreatePickupPrefab("Pickup_Shotgun", shotgun, shotgunAim);
        CreatePickupPrefab("Pickup_AR", ar, arAim);

        WireScene(ak, pistol, shotgun, ar);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        EditorSceneManager.SaveOpenScenes();
        Debug.Log("Weapons asset build complete.");
    }

    private static void EnsureFolders()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Weapons"))
            AssetDatabase.CreateFolder("Assets", "Weapons");
        if (!AssetDatabase.IsValidFolder(PrefabRoot))
            AssetDatabase.CreateFolder(WeaponsRoot, "Prefabs");
        if (!AssetDatabase.IsValidFolder(DefinitionsRoot))
            AssetDatabase.CreateFolder(WeaponsRoot, "Definitions");
        if (!AssetDatabase.IsValidFolder(PickupRoot))
            AssetDatabase.CreateFolder(WeaponsRoot, "Pickups");
    }

    private static GameObject CreateWeaponVisualPrefab(
        string prefabName,
        string fbxPath,
        Vector3 muzzleLocalPosition,
        bool useAkSceneMeshes = false)
    {
        string prefabPath = $"{PrefabRoot}/{prefabName}.prefab";
        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (existing != null)
            return existing;

        GameObject root = new GameObject(prefabName);

        if (useAkSceneMeshes)
        {
            // Prefer the already-tuned scene AimGun meshes if present.
            GameObject sceneAim = GameObject.Find("AimGun");
            if (sceneAim != null)
            {
                foreach (Transform child in sceneAim.transform)
                {
                    if (child.name == "muzzle" || child.name == "gunAudio")
                        continue;

                    GameObject copy = Object.Instantiate(child.gameObject, root.transform);
                    copy.name = child.name;
                    StripRuntimeJunk(copy);
                }
            }
            else
            {
                InstantiateModelUnder(root.transform, fbxPath);
            }
        }
        else
        {
            InstantiateModelUnder(root.transform, fbxPath);
        }

        Transform muzzle = new GameObject("muzzle").transform;
        muzzle.SetParent(root.transform, false);
        muzzle.localPosition = muzzleLocalPosition;
        muzzle.localRotation = Quaternion.identity;
        muzzle.localScale = Vector3.one;

        StripRuntimeJunk(root);

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        Object.DestroyImmediate(root);
        return prefab;
    }

    private static void InstantiateModelUnder(Transform parent, string fbxPath)
    {
        GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
        if (source == null)
        {
            Debug.LogWarning($"Weapon model missing at {fbxPath}");
            return;
        }

        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(source);
        if (instance == null)
            instance = Object.Instantiate(source);

        instance.name = source.name;
        instance.transform.SetParent(parent, false);
        instance.transform.localPosition = Vector3.zero;
        instance.transform.localRotation = Quaternion.identity;
        instance.transform.localScale = Vector3.one;
        StripRuntimeJunk(instance);
    }

    private static void StripRuntimeJunk(GameObject root)
    {
        foreach (Collider collider in root.GetComponentsInChildren<Collider>(true))
            Object.DestroyImmediate(collider);

        foreach (AudioSource audio in root.GetComponentsInChildren<AudioSource>(true))
            Object.DestroyImmediate(audio);

        foreach (Animator animator in root.GetComponentsInChildren<Animator>(true))
            Object.DestroyImmediate(animator);

        foreach (Rigidbody body in root.GetComponentsInChildren<Rigidbody>(true))
            Object.DestroyImmediate(body);

        // Remove missing / leftover MonoBehaviours from FPS pack.
        foreach (MonoBehaviour behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (behaviour == null)
                continue;

            // Keep nothing except Transform/Mesh/Renderer which aren't MonoBehaviour.
            Object.DestroyImmediate(behaviour);
        }
    }

    private static WeaponDefinition CreateOrUpdateDefinition(
        string fileName,
        string displayName,
        int slot,
        string visualKey,
        int weaponAimStyle,
        bool isAutomatic,
        float fireRateRpm,
        float damage,
        float maxRange,
        int pellets,
        float spread,
        float shoulderRecoil,
        float cameraKick,
        float muzzleFlashScale,
        GameObject tracer,
        GameObject muzzleFlash,
        GameObject hit)
    {
        string path = $"{DefinitionsRoot}/{fileName}.asset";
        WeaponDefinition definition = AssetDatabase.LoadAssetAtPath<WeaponDefinition>(path);
        if (definition == null)
        {
            definition = ScriptableObject.CreateInstance<WeaponDefinition>();
            AssetDatabase.CreateAsset(definition, path);
        }

        definition.displayName = displayName;
        definition.slot = slot;
        definition.visualKey = visualKey;
        definition.weaponAimStyle = weaponAimStyle;
        // Scene-authored HoldGun*/AimGun* are used at runtime; do not spawn prefabs.
        definition.holdPrefab = null;
        definition.aimPrefab = null;
        definition.muzzleChildName = "muzzle";
        definition.isAutomatic = isAutomatic;
        definition.fireRateRpm = fireRateRpm;
        definition.damage = damage;
        definition.maxRange = maxRange;
        definition.pelletsPerShot = pellets;
        definition.spreadDegrees = spread;
        definition.tracerSpeed = 140f;
        definition.recoilShoulderImpulse = shoulderRecoil;
        definition.recoilCameraKick = cameraKick;
        // Leave fireClip for manual assignment unless already empty and we want defaults later.
        definition.tracerPrefab = tracer;
        definition.muzzleFlashPrefab = muzzleFlash;
        definition.muzzleFlashScale = muzzleFlashScale;
        definition.hitPrefab = hit;
        definition.decalPrefab = null;

        EditorUtility.SetDirty(definition);
        return definition;
    }

    private static GameObject CreatePickupPrefab(string name, WeaponDefinition definition, GameObject visualPrefab)
    {
        string path = $"{PickupRoot}/{name}.prefab";
        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (existing != null)
        {
            WeaponPickup existingPickup = existing.GetComponent<WeaponPickup>();
            if (existingPickup != null)
            {
                existingPickup.Configure(definition);
                EditorUtility.SetDirty(existing);
            }

            return existing;
        }

        GameObject root = new GameObject(name);
        root.transform.localScale = Vector3.one * 0.35f;

        if (visualPrefab != null)
        {
            GameObject visual = (GameObject)PrefabUtility.InstantiatePrefab(visualPrefab);
            if (visual == null)
                visual = Object.Instantiate(visualPrefab);
            visual.name = "Visual";
            visual.transform.SetParent(root.transform, false);
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localRotation = Quaternion.identity;
            visual.transform.localScale = Vector3.one;
        }

        WeaponPickup pickup = root.AddComponent<WeaponPickup>();
        pickup.Configure(definition, 2.5f);

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
        Object.DestroyImmediate(root);
        return prefab;
    }

    private static void WireScene(
        WeaponDefinition ak,
        WeaponDefinition pistol,
        WeaponDefinition shotgun,
        WeaponDefinition ar)
    {
        GameObject player = GameObject.Find("ThirdPersonController_LITE");
        if (player == null)
        {
            Debug.LogWarning("Player not found in open scene; skipped scene wiring.");
            return;
        }

        WeaponInventory inventory = player.GetComponent<WeaponInventory>();
        if (inventory == null)
            inventory = Undo.AddComponent<WeaponInventory>(player);

        SerializedObject inventorySo = new SerializedObject(inventory);
        SerializedProperty starting = inventorySo.FindProperty("startingWeapons");
        starting.ClearArray();
        starting.arraySize = 1;
        starting.GetArrayElementAtIndex(0).objectReferenceValue = ak;
        inventorySo.ApplyModifiedPropertiesWithoutUndo();

        WeaponPickupInteractor interactor = player.GetComponent<WeaponPickupInteractor>();
        if (interactor == null)
            interactor = Undo.AddComponent<WeaponPickupInteractor>(player);

        Text prompt = EnsurePickupPrompt();

        SerializedObject interactorSo = new SerializedObject(interactor);
        interactorSo.FindProperty("inventory").objectReferenceValue = inventory;
        interactorSo.FindProperty("gunController").objectReferenceValue = player.GetComponent<PlayerGunController>();
        interactorSo.FindProperty("promptText").objectReferenceValue = prompt;
        interactorSo.ApplyModifiedPropertiesWithoutUndo();

        PlayerGunController gun = player.GetComponent<PlayerGunController>();
        if (gun != null)
        {
            SerializedObject gunSo = new SerializedObject(gun);
            gunSo.FindProperty("inventory").objectReferenceValue = inventory;
            gunSo.FindProperty("defaultWeapon").objectReferenceValue = ak;

            const string WristPath =
                "guard/Armature/root ground/root hip/spine lower/spine middle/spine upper/arm right shoulder 1/arm right shoulder 2/arm right elbow/arm right wrist";
            Transform wrist = player.transform.Find(WristPath);
            gunSo.FindProperty("weaponHand").objectReferenceValue = wrist;
            gunSo.ApplyModifiedPropertiesWithoutUndo();
        }

        // Hide exact legacy single-gun objects only (not HoldGunAK / AimGunGlock, etc.).
        Transform[] all = player.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i].name == "AimGun" || all[i].name == "HoldGun")
                all[i].gameObject.SetActive(false);
        }

        SpawnWorldPickup("Pickup_Pistol", pistol, new Vector3(27f, 0.35f, 28f));
        SpawnWorldPickup("Pickup_Shotgun", shotgun, new Vector3(28.5f, 0.35f, 27f));
        SpawnWorldPickup("Pickup_AR", ar, new Vector3(26f, 0.35f, 26f));

        GameObject oldSniper = GameObject.Find("Pickup_Sniper");
        if (oldSniper != null)
            Undo.DestroyObjectImmediate(oldSniper);
    }

    private static void SpawnWorldPickup(string prefabName, WeaponDefinition definition, Vector3 position)
    {
        string path = $"{PickupRoot}/{prefabName}.prefab";
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (prefab == null)
            return;

        // Replace existing scene instance with the same name.
        GameObject existing = GameObject.Find(prefabName);
        if (existing != null)
            Undo.DestroyObjectImmediate(existing);

        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        instance.name = prefabName;
        instance.transform.position = position;
        instance.transform.rotation = Quaternion.identity;
        WeaponPickup pickup = instance.GetComponent<WeaponPickup>();
        if (pickup != null)
            pickup.Configure(definition, 2.5f);
        Undo.RegisterCreatedObjectUndo(instance, "Spawn Weapon Pickup");
    }

    private static Text EnsurePickupPrompt()
    {
        GameObject canvasObject = GameObject.Find("Canvas");
        if (canvasObject == null)
        {
            canvasObject = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        }

        Transform existing = canvasObject.transform.Find("PickupPrompt");
        GameObject promptObject;
        if (existing != null)
        {
            promptObject = existing.gameObject;
        }
        else
        {
            promptObject = new GameObject("PickupPrompt", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            promptObject.transform.SetParent(canvasObject.transform, false);
        }

        RectTransform rect = promptObject.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.2f);
        rect.anchorMax = new Vector2(0.5f, 0.2f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(520f, 48f);
        rect.anchoredPosition = Vector2.zero;

        Text text = promptObject.GetComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (text.font == null)
            text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        text.fontSize = 28;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = Color.white;
        text.raycastTarget = false;
        text.text = string.Empty;
        promptObject.SetActive(false);
        return text;
    }

    private static Transform FindChildByName(Transform root, string childName)
    {
        Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < transforms.Length; i++)
        {
            if (transforms[i].name == childName)
                return transforms[i];
        }

        return null;
    }
}
#endif
