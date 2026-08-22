using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

public static class EnemyCombatSetup
{
    private const string EnemyLayerName = "Enemy";
    private const float DefaultMaxHealth = 100f;

    private struct BoneSpec
    {
        public string Name;
        public string ParentName;
        public float Mass;
        public float Radius;
        public float Height;
        public float DamageMultiplier;
        public int Direction; // CapsuleCollider.direction 0=X 1=Y 2=Z
    }

    private static readonly BoneSpec[] BoneSpecs =
    {
        new BoneSpec { Name = "mixamorig:Hips", ParentName = null, Mass = 8f, Radius = 0.14f, Height = 0.28f, DamageMultiplier = 1f, Direction = 1 },
        new BoneSpec { Name = "mixamorig:Spine", ParentName = "mixamorig:Hips", Mass = 5f, Radius = 0.13f, Height = 0.26f, DamageMultiplier = 1f, Direction = 1 },
        new BoneSpec { Name = "mixamorig:Spine1", ParentName = "mixamorig:Spine", Mass = 4f, Radius = 0.13f, Height = 0.24f, DamageMultiplier = 1f, Direction = 1 },
        new BoneSpec { Name = "mixamorig:Spine2", ParentName = "mixamorig:Spine1", Mass = 4f, Radius = 0.13f, Height = 0.24f, DamageMultiplier = 1f, Direction = 1 },
        new BoneSpec { Name = "mixamorig:Head", ParentName = "mixamorig:Spine2", Mass = 2.5f, Radius = 0.11f, Height = 0.22f, DamageMultiplier = 2f, Direction = 1 },
        new BoneSpec { Name = "mixamorig:LeftArm", ParentName = "mixamorig:Spine2", Mass = 2f, Radius = 0.06f, Height = 0.28f, DamageMultiplier = 0.75f, Direction = 0 },
        new BoneSpec { Name = "mixamorig:LeftForeArm", ParentName = "mixamorig:LeftArm", Mass = 1.5f, Radius = 0.05f, Height = 0.26f, DamageMultiplier = 0.75f, Direction = 0 },
        new BoneSpec { Name = "mixamorig:RightArm", ParentName = "mixamorig:Spine2", Mass = 2f, Radius = 0.06f, Height = 0.28f, DamageMultiplier = 0.75f, Direction = 0 },
        new BoneSpec { Name = "mixamorig:RightForeArm", ParentName = "mixamorig:RightArm", Mass = 1.5f, Radius = 0.05f, Height = 0.26f, DamageMultiplier = 0.75f, Direction = 0 },
        new BoneSpec { Name = "mixamorig:LeftUpLeg", ParentName = "mixamorig:Hips", Mass = 4f, Radius = 0.09f, Height = 0.4f, DamageMultiplier = 0.75f, Direction = 1 },
        new BoneSpec { Name = "mixamorig:LeftLeg", ParentName = "mixamorig:LeftUpLeg", Mass = 3f, Radius = 0.07f, Height = 0.38f, DamageMultiplier = 0.75f, Direction = 1 },
        new BoneSpec { Name = "mixamorig:RightUpLeg", ParentName = "mixamorig:Hips", Mass = 4f, Radius = 0.09f, Height = 0.4f, DamageMultiplier = 0.75f, Direction = 1 },
        new BoneSpec { Name = "mixamorig:RightLeg", ParentName = "mixamorig:RightUpLeg", Mass = 3f, Radius = 0.07f, Height = 0.38f, DamageMultiplier = 0.75f, Direction = 1 },
    };

    [MenuItem("Tools/Enemy/Debug Deal 25 Damage (Play Mode)")]
    public static void DebugDealDamage()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("[EnemySetup] Enter Play Mode before dealing debug damage.");
            return;
        }

        EnemyHealth health = Object.FindAnyObjectByType<EnemyHealth>();
        if (health == null)
        {
            Debug.LogError("[EnemySetup] No EnemyHealth in scene.");
            return;
        }

        Vector3 point = health.transform.position + Vector3.up * 1.2f;
        float before = health.CurrentHealth;
        health.ReceiveDamage(25f, point, Vector3.back, null, null);
        Debug.Log($"[EnemySetup] Damage 25: {before} -> {health.CurrentHealth}/{health.MaxHealth}. Dead={health.IsDead} Ragdoll={health.GetComponent<EnemyRagdoll>()?.IsRagdollActive}");
    }

    [MenuItem("Tools/Enemy/Setup Scene Enemy And Save Prefab")]
    public static void SetupSceneEnemyAndSavePrefab()
    {
        GameObject target = GameObject.Find("enemy");
        if (target == null)
            target = Selection.activeGameObject;

        if (target == null)
        {
            Debug.LogError("[EnemySetup] Could not find GameObject named 'enemy'.");
            return;
        }

        string result = SetupEnemy(target);

        if (!AssetDatabase.IsValidFolder("Assets/Prefabs"))
            AssetDatabase.CreateFolder("Assets", "Prefabs");

        string prefabPath = "Assets/Prefabs/Enemy.prefab";
        PrefabUtility.SaveAsPrefabAssetAndConnect(target, prefabPath, InteractionMode.AutomatedAction);
        EditorSceneManager.MarkSceneDirty(target.scene);
        EditorSceneManager.SaveOpenScenes();
        AssetDatabase.SaveAssets();

        result += $" Prefab saved: {prefabPath}";
        Debug.Log(result);
    }

    [MenuItem("Tools/Enemy/Setup Selected Enemy")]
    public static void SetupSelectedEnemy()
    {
        GameObject target = Selection.activeGameObject;
        if (target == null)
            target = GameObject.Find("enemy");

        if (target == null)
        {
            Debug.LogError("[EnemySetup] Select an enemy GameObject (or name one 'enemy' in the scene).");
            return;
        }

        string result = SetupEnemy(target);
        EditorSceneManager.MarkSceneDirty(target.scene);
        Debug.Log(result);
    }

    public static string SetupEnemy(GameObject root)
    {
        Undo.RegisterFullObjectHierarchyUndo(root, "Setup Enemy Combat");

        int enemyLayer = EnsureLayer(EnemyLayerName);
        SetLayerRecursive(root, enemyLayer);

        Animator animator = root.GetComponent<Animator>();
        if (animator != null && !animator.enabled)
            animator.enabled = true;

        EnemyHealth health = root.GetComponent<EnemyHealth>();
        if (health == null)
            health = Undo.AddComponent<EnemyHealth>(root);

        EnemyRagdoll ragdoll = root.GetComponent<EnemyRagdoll>();
        if (ragdoll == null)
            ragdoll = Undo.AddComponent<EnemyRagdoll>(root);

        Dictionary<string, Rigidbody> bodies = new Dictionary<string, Rigidbody>();
        List<Rigidbody> orderedBodies = new List<Rigidbody>();
        int hitboxCount = 0;

        foreach (BoneSpec spec in BoneSpecs)
        {
            Transform bone = FindBone(root.transform, spec.Name);
            if (bone == null)
            {
                Debug.LogWarning($"[EnemySetup] Missing bone: {spec.Name}");
                continue;
            }

            bone.gameObject.layer = enemyLayer;

            CapsuleCollider capsule = bone.GetComponent<CapsuleCollider>();
            if (capsule == null)
                capsule = Undo.AddComponent<CapsuleCollider>(bone.gameObject);

            FitCapsule(capsule, bone, spec);

            Rigidbody rb = bone.GetComponent<Rigidbody>();
            if (rb == null)
                rb = Undo.AddComponent<Rigidbody>(bone.gameObject);

            rb.mass = spec.Mass;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            rb.isKinematic = true;
            rb.useGravity = true;

            if (!string.IsNullOrEmpty(spec.ParentName))
            {
                Transform parentBone = FindBone(root.transform, spec.ParentName);
                if (parentBone != null)
                {
                    CharacterJoint joint = bone.GetComponent<CharacterJoint>();
                    if (joint == null)
                        joint = Undo.AddComponent<CharacterJoint>(bone.gameObject);

                    joint.connectedBody = parentBone.GetComponent<Rigidbody>();
                    joint.enableProjection = true;
                    ConfigureJointLimits(joint, spec.Name);
                }
            }

            EnemyHitbox hitbox = bone.GetComponent<EnemyHitbox>();
            if (hitbox == null)
                hitbox = Undo.AddComponent<EnemyHitbox>(bone.gameObject);
            hitbox.EditorBind(health, spec.DamageMultiplier);
            EditorUtility.SetDirty(hitbox);

            bodies[spec.Name] = rb;
            orderedBodies.Add(rb);
            hitboxCount++;
        }

        // Head joint should connect to Neck if present, else Spine2 (already set).
        Transform neck = FindBone(root.transform, "mixamorig:Neck");
        Transform head = FindBone(root.transform, "mixamorig:Head");
        if (neck != null && head != null)
        {
            Rigidbody neckRb = neck.GetComponent<Rigidbody>();
            if (neckRb == null)
            {
                neckRb = Undo.AddComponent<Rigidbody>(neck.gameObject);
                neckRb.mass = 1.5f;
                neckRb.isKinematic = true;
                neck.gameObject.layer = enemyLayer;

                CharacterJoint neckJoint = neck.GetComponent<CharacterJoint>();
                if (neckJoint == null)
                    neckJoint = Undo.AddComponent<CharacterJoint>(neck.gameObject);
                neckJoint.connectedBody = FindBone(root.transform, "mixamorig:Spine2")?.GetComponent<Rigidbody>();
                orderedBodies.Add(neckRb);
            }

            CharacterJoint headJoint = head.GetComponent<CharacterJoint>();
            if (headJoint != null)
                headJoint.connectedBody = neckRb;
        }

        EnemyHealthBar bar = EnsureHealthBar(root, FindBone(root.transform, "mixamorig:Head"));
        GameObject bloodPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/BloodDecalsAndEffects/BloodGushFX/BloodSprayFX.prefab");

        ragdoll.EditorBind(animator, orderedBodies);
        health.EditorBind(ragdoll, bar, DefaultMaxHealth, bloodPrefab);
        EditorUtility.SetDirty(ragdoll);
        EditorUtility.SetDirty(health);
        EditorUtility.SetDirty(root);

        return $"Enemy combat ready on '{root.name}': {hitboxCount} hitboxes, layer '{EnemyLayerName}', world health bar.";
    }

    private static EnemyHealthBar EnsureHealthBar(GameObject root, Transform head)
    {
        Transform existing = root.transform.Find("HealthBarCanvas");
        GameObject canvasGo;
        if (existing != null)
        {
            canvasGo = existing.gameObject;
        }
        else
        {
            canvasGo = new GameObject("HealthBarCanvas", typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(canvasGo, "Create HealthBarCanvas");
            canvasGo.transform.SetParent(root.transform, false);
        }

        Canvas canvas = canvasGo.GetComponent<Canvas>();
        if (canvas == null)
            canvas = Undo.AddComponent<Canvas>(canvasGo);
        canvas.renderMode = RenderMode.WorldSpace;

        CanvasGroup group = canvasGo.GetComponent<CanvasGroup>();
        if (group == null)
            group = Undo.AddComponent<CanvasGroup>(canvasGo);

        RectTransform canvasRect = canvasGo.GetComponent<RectTransform>();
        canvasRect.sizeDelta = new Vector2(100f, 12f);
        canvasRect.localScale = Vector3.one * 0.012f;

        Transform follow = head != null ? head : root.transform;
        canvasGo.transform.position = follow.position + Vector3.up * 0.35f;

        Sprite uiSprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");

        Image background = EnsureChildImage(canvasGo.transform, "Background", new Color(0.1f, 0.1f, 0.1f, 0.85f), uiSprite);
        RectTransform bgRect = background.rectTransform;
        bgRect.anchorMin = Vector2.zero;
        bgRect.anchorMax = Vector2.one;
        bgRect.offsetMin = Vector2.zero;
        bgRect.offsetMax = Vector2.zero;

        Image fill = EnsureChildImage(canvasGo.transform, "Fill", new Color(0.2f, 0.85f, 0.25f, 1f), uiSprite);
        RectTransform fillRect = fill.rectTransform;
        fillRect.anchorMin = new Vector2(0f, 0f);
        fillRect.anchorMax = new Vector2(1f, 1f);
        fillRect.offsetMin = new Vector2(2f, 2f);
        fillRect.offsetMax = new Vector2(-2f, -2f);
        fill.type = Image.Type.Filled;
        fill.fillMethod = Image.FillMethod.Horizontal;
        fill.fillOrigin = (int)Image.OriginHorizontal.Left;
        fill.fillAmount = 1f;

        EnemyHealthBar bar = canvasGo.GetComponent<EnemyHealthBar>();
        if (bar == null)
            bar = Undo.AddComponent<EnemyHealthBar>(canvasGo);
        bar.EditorBind(fill, follow, new Vector3(0f, 0.35f, 0f));
        EditorUtility.SetDirty(bar);
        return bar;
    }

    private static Image EnsureChildImage(Transform parent, string childName, Color color, Sprite sprite)
    {
        Transform child = parent.Find(childName);
        GameObject go;
        if (child == null)
        {
            go = new GameObject(childName, typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(go, "Create " + childName);
            go.transform.SetParent(parent, false);
        }
        else
        {
            go = child.gameObject;
        }

        Image image = go.GetComponent<Image>();
        if (image == null)
            image = Undo.AddComponent<Image>(go);

        image.sprite = sprite;
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    private static void FitCapsule(CapsuleCollider capsule, Transform bone, BoneSpec spec)
    {
        capsule.direction = spec.Direction;
        capsule.radius = spec.Radius;
        capsule.height = Mathf.Max(spec.Height, spec.Radius * 2f + 0.01f);
        capsule.isTrigger = false;

        // Center toward first child when available for better limb coverage.
        if (bone.childCount > 0)
        {
            Transform child = bone.GetChild(0);
            Vector3 localChild = bone.InverseTransformPoint(child.position);
            float length = localChild.magnitude;
            if (length > 0.05f)
            {
                capsule.height = Mathf.Max(length, spec.Radius * 2f + 0.01f);
                capsule.center = localChild * 0.5f;

                Vector3 abs = new Vector3(Mathf.Abs(localChild.x), Mathf.Abs(localChild.y), Mathf.Abs(localChild.z));
                if (abs.x >= abs.y && abs.x >= abs.z)
                    capsule.direction = 0;
                else if (abs.y >= abs.x && abs.y >= abs.z)
                    capsule.direction = 1;
                else
                    capsule.direction = 2;
            }
            else
            {
                capsule.center = Vector3.zero;
            }
        }
        else
        {
            capsule.center = Vector3.zero;
        }
    }

    private static void ConfigureJointLimits(CharacterJoint joint, string boneName)
    {
        SoftJointLimit low = new SoftJointLimit { limit = -40f };
        SoftJointLimit high = new SoftJointLimit { limit = 40f };
        SoftJointLimit swing = new SoftJointLimit { limit = 30f };

        if (boneName.Contains("Head"))
        {
            low.limit = -30f;
            high.limit = 30f;
            swing.limit = 25f;
        }
        else if (boneName.Contains("UpLeg") || boneName.Contains("Leg"))
        {
            low.limit = -60f;
            high.limit = 20f;
            swing.limit = 25f;
        }
        else if (boneName.Contains("Arm") || boneName.Contains("ForeArm"))
        {
            low.limit = -70f;
            high.limit = 20f;
            swing.limit = 40f;
        }

        joint.lowTwistLimit = low;
        joint.highTwistLimit = high;
        joint.swing1Limit = swing;
        joint.swing2Limit = swing;
    }

    private static Transform FindBone(Transform root, string exactName)
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

    private static void SetLayerRecursive(GameObject root, int layer)
    {
        root.layer = layer;
        Transform[] all = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < all.Length; i++)
            all[i].gameObject.layer = layer;
    }

    private static int EnsureLayer(string layerName)
    {
        SerializedObject tagManager = new SerializedObject(
            AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
        SerializedProperty layers = tagManager.FindProperty("layers");

        for (int i = 0; i < layers.arraySize; i++)
        {
            SerializedProperty layer = layers.GetArrayElementAtIndex(i);
            if (layer.stringValue == layerName)
                return i;
        }

        for (int i = 8; i < layers.arraySize; i++)
        {
            SerializedProperty layer = layers.GetArrayElementAtIndex(i);
            if (string.IsNullOrEmpty(layer.stringValue))
            {
                layer.stringValue = layerName;
                tagManager.ApplyModifiedProperties();
                return i;
            }
        }

        Debug.LogError($"Could not create layer '{layerName}' — TagManager is full.");
        return 0;
    }
}
