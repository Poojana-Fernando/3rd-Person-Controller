#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class PlayerCloakClothSetup
{
    private const string PlayerPath = "ThirdPersonController_LITE";
    private const string WarriorName = "Sci-FI Warrior Armor";
    private const string BakedMeshPath = "Assets/red-leather-cape-static-and-blowing/CapeClothBaked.asset";

    // Collar lock only — enough to stop spikes, not so much the cape looks rigid.
    private const float PinTopFraction = 0.15f;
    private const float SoftBandFraction = 0.22f;
    // Conservative world travel (meters). Large values + soft stretch = the spikes in the recording.
    private const float HemMaxDistance = 0.55f;
    private const float MidMaxDistance = 0.22f;

    private static readonly string[] CloakCandidateNames = { "cape", "cloak" };

    [MenuItem("Tools/Player/Setup Cloak Cloth Physics")]
    public static void SetupCloakClothPhysics()
    {
        GameObject player = GameObject.Find(PlayerPath);
        if (player == null)
        {
            Debug.LogError($"[CloakCloth] '{PlayerPath}' not found.");
            return;
        }

        Transform warrior = FindChild(player.transform, WarriorName);
        if (warrior == null)
        {
            Debug.LogError($"[CloakCloth] '{WarriorName}' not found under player.");
            return;
        }

        Transform cloakTransform = FindCloakMesh(warrior);
        if (cloakTransform == null)
        {
            Debug.LogError("[CloakCloth] No cape/cloak mesh found under warrior (expected 'cape' or 'cloak').");
            return;
        }

        GameObject cloak = cloakTransform.gameObject;
        Undo.RegisterFullObjectHierarchyUndo(cloak, "Setup Cloak Cloth Physics");

        CleanupOrphanClothOnParents(cloakTransform, warrior);

        MeshFilter meshFilter = cloak.GetComponent<MeshFilter>();
        MeshRenderer meshRenderer = cloak.GetComponent<MeshRenderer>();
        SkinnedMeshRenderer skinned = cloak.GetComponent<SkinnedMeshRenderer>();

        Mesh sourceMesh = null;
        Material[] materials = null;

        if (skinned != null && skinned.sharedMesh != null)
        {
            sourceMesh = skinned.sharedMesh;
            materials = skinned.sharedMaterials;
        }
        else if (meshFilter != null && meshFilter.sharedMesh != null)
        {
            sourceMesh = meshFilter.sharedMesh;
            if (meshRenderer != null)
                materials = meshRenderer.sharedMaterials;
        }
        else if (skinned != null)
        {
            materials = skinned.sharedMaterials;
        }

        if (sourceMesh == null)
        {
            Debug.LogError($"[CloakCloth] '{cloak.name}' has no mesh.");
            return;
        }

        // Prefer original FBX mesh as bake source (not a previous bake).
        Mesh bakeSource = ResolveBakeSourceMesh(sourceMesh);
        EnsureMeshReadable(bakeSource);
        bakeSource = ResolveBakeSourceMesh(bakeSource);

        if (materials == null || materials.Length == 0 || materials[0] == null)
            materials = TryLoadCapeMaterials();

        // Cloth caches topology — remove before mesh/scale change, then re-add.
        PlayerCloakCloth existingRuntime = cloak.GetComponent<PlayerCloakCloth>();
        if (existingRuntime != null)
            Undo.DestroyObjectImmediate(existingRuntime);
        Cloth existingCloth = cloak.GetComponent<Cloth>();
        if (existingCloth != null)
            Undo.DestroyObjectImmediate(existingCloth);

        if (skinned == null)
            skinned = Undo.AddComponent<SkinnedMeshRenderer>(cloak);

        if (meshRenderer != null)
            Undo.DestroyObjectImmediate(meshRenderer);
        if (meshFilter != null)
            Undo.DestroyObjectImmediate(meshFilter);

        Transform spine02 = FindChild(warrior, "CC_Base_Spine02");
        Transform neck = FindChild(warrior, "CC_Base_NeckTwist01");
        Transform attachParent = spine02 != null ? spine02 : (neck != null ? neck : warrior);

        if (!IsUnder(cloak.transform, attachParent))
        {
            Vector3 worldPos = cloak.transform.position;
            Quaternion worldRot = cloak.transform.rotation;
            Vector3 worldScale = cloak.transform.lossyScale;
            Undo.SetTransformParent(cloak.transform, attachParent, "Parent cloak to spine");
            cloak.transform.position = worldPos;
            cloak.transform.rotation = worldRot;
            Vector3 parentScale = attachParent.lossyScale;
            cloak.transform.localScale = new Vector3(
                SafeDiv(worldScale.x, parentScale.x),
                SafeDiv(worldScale.y, parentScale.y),
                SafeDiv(worldScale.z, parentScale.z));
        }

        // Huge local scale (~100) makes Unity Cloth explode into rigid spikes.
        // Bake scale into mesh verts and normalize localScale to 1.
        Vector3 scaleToBake = ResolveScaleToBake(cloak.transform, bakeSource);
        Mesh bakedMesh = BakeScaleIntoMeshAsset(bakeSource, scaleToBake, BakedMeshPath);
        cloak.transform.localScale = Vector3.one;

        skinned.sharedMesh = bakedMesh;
        if (materials != null && materials.Length > 0)
            skinned.sharedMaterials = materials;
        skinned.updateWhenOffscreen = true;
        skinned.quality = SkinQuality.Bone4;
        skinned.rootBone = cloak.transform;
        skinned.bones = new[] { cloak.transform };
        skinned.localBounds = bakedMesh.bounds;

        Rigidbody rb = cloak.GetComponent<Rigidbody>();
        if (rb != null)
            Undo.DestroyObjectImmediate(rb);

        Collider[] colliders = cloak.GetComponents<Collider>();
        for (int i = 0; i < colliders.Length; i++)
            Undo.DestroyObjectImmediate(colliders[i]);

        int ignoreRaycast = LayerMask.NameToLayer("Ignore Raycast");
        if (ignoreRaycast >= 0)
        {
            Transform layerRoot = FindAncestorNamed(cloak.transform, "model") ?? cloak.transform;
            SetLayerRecursive(layerRoot, ignoreRaycast);
        }

        Cloth cloth = Undo.AddComponent<Cloth>(cloak);

        // Stable AC-style drape: enough give to sway, stiff enough not to spike.
        cloth.stretchingStiffness = 0.72f;
        cloth.bendingStiffness = 0.22f;
        cloth.damping = 0.18f;
        cloth.useGravity = true;
        cloth.externalAcceleration = new Vector3(0f, -2.5f, 0f);
        cloth.randomAcceleration = new Vector3(0.5f, 0.15f, 0.5f);
        cloth.worldVelocityScale = 0.8f;
        cloth.worldAccelerationScale = 1.0f;
        cloth.friction = 0.25f;
        cloth.collisionMassScale = 0f;
        cloth.enableContinuousCollision = false;
        cloth.useVirtualParticles = 1f;
        cloth.clothSolverFrequency = 120f;
        cloth.useTethers = true;
        cloth.stiffnessFrequency = 8f;
        cloth.capsuleColliders = System.Array.Empty<CapsuleCollider>();
        cloth.sphereColliders = System.Array.Empty<ClothSphereColliderPair>();

        ApplyPinCoefficients(cloth, bakedMesh, cloak.transform);

        PlayerCloakCloth runtime = Undo.AddComponent<PlayerCloakCloth>(cloak);
        SerializedObject runtimeSo = new SerializedObject(runtime);
        runtimeSo.FindProperty("cloth").objectReferenceValue = cloth;
        runtimeSo.FindProperty("forceNoColliders").boolValue = true;
        runtimeSo.ApplyModifiedPropertiesWithoutUndo();

        EditorUtility.SetDirty(cloak);
        EditorUtility.SetDirty(skinned);
        EditorUtility.SetDirty(cloth);
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        EditorSceneManager.SaveOpenScenes();
        AssetDatabase.SaveAssets();

        Debug.Log(
            $"[CloakCloth] '{cloak.name}' fixed: baked scale {scaleToBake} into mesh, " +
            "localScale=1, stable Cloth + collar pins. Layer=Ignore Raycast.");
    }

    private static Vector3 ResolveScaleToBake(Transform cloakTransform, Mesh bakeSource)
    {
        Vector3 localScale = cloakTransform.localScale;
        bool nearlyUnit = Mathf.Abs(localScale.x - 1f) < 0.05f
            && Mathf.Abs(localScale.y - 1f) < 0.05f
            && Mathf.Abs(localScale.z - 1f) < 0.05f;

        // Re-runs after normalize: localScale is 1, but FBX verts are still tiny — restore import scale.
        bool sourceIsTiny = bakeSource != null && bakeSource.bounds.size.magnitude < 1f;
        string path = bakeSource != null ? AssetDatabase.GetAssetPath(bakeSource) : null;
        bool sourceIsFbx = !string.IsNullOrEmpty(path)
            && path.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase);

        if (nearlyUnit && sourceIsTiny && sourceIsFbx)
            return new Vector3(100f, 100f, 100f);

        return localScale;
    }

    private static Mesh ResolveBakeSourceMesh(Mesh mesh)
    {
        if (mesh == null)
            return null;

        string path = AssetDatabase.GetAssetPath(mesh);
        if (!string.IsNullOrEmpty(path) && path.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase))
            return mesh;

        // If current mesh is a previous bake, pull the FBX cape mesh instead.
        Object[] fbxAssets = AssetDatabase.LoadAllAssetsAtPath(
            "Assets/red-leather-cape-static-and-blowing/source/model.fbx");
        for (int i = 0; i < fbxAssets.Length; i++)
        {
            Mesh candidate = fbxAssets[i] as Mesh;
            if (candidate != null && candidate.name.IndexOf("cape", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return candidate;
        }

        for (int i = 0; i < fbxAssets.Length; i++)
        {
            Mesh candidate = fbxAssets[i] as Mesh;
            if (candidate != null)
                return candidate;
        }

        return mesh;
    }

    private static Mesh BakeScaleIntoMeshAsset(Mesh source, Vector3 localScale, string assetPath)
    {
        // Build a clean mesh (Instantiate + boneWeight rewrite can break vertex stride for Cloth/SMR).
        Mesh baked = new Mesh
        {
            name = source.name + "_ClothBaked",
            indexFormat = source.indexFormat
        };

        Vector3[] verts = source.vertices;
        Vector3 absScale = new Vector3(
            Mathf.Abs(localScale.x),
            Mathf.Abs(localScale.y),
            Mathf.Abs(localScale.z));

        bool needsBake = Mathf.Abs(absScale.x - 1f) > 0.01f
            || Mathf.Abs(absScale.y - 1f) > 0.01f
            || Mathf.Abs(absScale.z - 1f) > 0.01f;

        if (needsBake)
        {
            for (int i = 0; i < verts.Length; i++)
                verts[i] = Vector3.Scale(verts[i], localScale);
        }

        baked.vertices = verts;
        baked.normals = source.normals;
        baked.tangents = source.tangents;
        baked.uv = source.uv;
        baked.uv2 = source.uv2;
        baked.colors = source.colors;

        for (int sub = 0; sub < source.subMeshCount; sub++)
            baked.SetTriangles(source.GetTriangles(sub), sub);

        int vertCount = baked.vertexCount;
        BoneWeight[] weights = new BoneWeight[vertCount];
        for (int i = 0; i < vertCount; i++)
        {
            weights[i].boneIndex0 = 0;
            weights[i].weight0 = 1f;
        }

        baked.boneWeights = weights;
        baked.bindposes = new[] { Matrix4x4.identity };
        baked.RecalculateBounds();
        if (baked.normals == null || baked.normals.Length != vertCount)
            baked.RecalculateNormals();
        if (baked.tangents == null || baked.tangents.Length != vertCount)
            baked.RecalculateTangents();

        Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(assetPath);
        if (existing != null)
            AssetDatabase.DeleteAsset(assetPath);

        AssetDatabase.CreateAsset(baked, assetPath);
        AssetDatabase.SaveAssets();
        Debug.Log($"[CloakCloth] Wrote baked mesh '{assetPath}' (scale {localScale}, verts={vertCount}).");
        return AssetDatabase.LoadAssetAtPath<Mesh>(assetPath);
    }

    private static Transform FindCloakMesh(Transform warrior)
    {
        for (int i = 0; i < CloakCandidateNames.Length; i++)
        {
            Transform named = FindChild(warrior, CloakCandidateNames[i]);
            if (named != null && HasRenderableMesh(named.gameObject))
                return named;
        }

        Transform modelRoot = FindChild(warrior, "model");
        if (modelRoot == null)
            return null;

        for (int i = 0; i < CloakCandidateNames.Length; i++)
        {
            Transform named = FindChild(modelRoot, CloakCandidateNames[i]);
            if (named != null && HasRenderableMesh(named.gameObject))
                return named;
        }

        for (int i = 0; i < modelRoot.childCount; i++)
        {
            Transform child = modelRoot.GetChild(i);
            if (HasRenderableMesh(child.gameObject))
                return child;
        }

        return HasRenderableMesh(modelRoot.gameObject) ? modelRoot : null;
    }

    private static bool HasRenderableMesh(GameObject go)
    {
        SkinnedMeshRenderer skinned = go.GetComponent<SkinnedMeshRenderer>();
        if (skinned != null && skinned.sharedMesh != null)
            return true;

        MeshFilter filter = go.GetComponent<MeshFilter>();
        return filter != null && filter.sharedMesh != null;
    }

    private static void CleanupOrphanClothOnParents(Transform cloak, Transform warrior)
    {
        Transform modelRoot = FindAncestorNamed(cloak, "model");
        if (modelRoot == null || modelRoot == cloak)
            return;

        if (!HasRenderableMesh(modelRoot.gameObject))
        {
            PlayerCloakCloth orphanRuntime = modelRoot.GetComponent<PlayerCloakCloth>();
            if (orphanRuntime != null)
                Undo.DestroyObjectImmediate(orphanRuntime);

            Cloth orphanCloth = modelRoot.GetComponent<Cloth>();
            if (orphanCloth != null)
                Undo.DestroyObjectImmediate(orphanCloth);

            SkinnedMeshRenderer orphanSkinned = modelRoot.GetComponent<SkinnedMeshRenderer>();
            if (orphanSkinned != null && orphanSkinned.sharedMesh == null)
                Undo.DestroyObjectImmediate(orphanSkinned);
        }

        for (int i = 0; i < modelRoot.childCount; i++)
        {
            Transform child = modelRoot.GetChild(i);
            if (child == cloak)
                continue;

            Cloth accessoryCloth = child.GetComponent<Cloth>();
            if (accessoryCloth != null)
                Undo.DestroyObjectImmediate(accessoryCloth);
        }
    }

    private static Material[] TryLoadCapeMaterials()
    {
        string[] candidates =
        {
            "Assets/red-leather-cape-static-and-blowing/RedLeatherCape.mat",
            "Assets/red-leather-cape-static-and-blowing/source/Materials/DefaultMaterial.mat",
            "Assets/red-leather-cape-static-and-blowing/Materials/DefaultMaterial.mat",
            "Assets/red-leather-cape-static-and-blowing/source/DefaultMaterial.mat"
        };

        for (int i = 0; i < candidates.Length; i++)
        {
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(candidates[i]);
            if (mat != null)
                return new[] { mat };
        }

        string[] guids = AssetDatabase.FindAssets(
            "t:Material",
            new[] { "Assets/red-leather-cape-static-and-blowing" });
        if (guids != null && guids.Length > 0)
        {
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guids[0]));
            if (mat != null)
                return new[] { mat };
        }

        return null;
    }

    private static void SetLayerRecursive(Transform root, int layer)
    {
        if (root == null)
            return;

        root.gameObject.layer = layer;
        for (int i = 0; i < root.childCount; i++)
            SetLayerRecursive(root.GetChild(i), layer);
    }

    private static bool IsUnder(Transform child, Transform ancestor)
    {
        Transform current = child;
        while (current != null)
        {
            if (current == ancestor)
                return true;
            current = current.parent;
        }

        return false;
    }

    private static Transform FindAncestorNamed(Transform start, string exactName)
    {
        Transform current = start;
        while (current != null)
        {
            if (current.name == exactName)
                return current;
            current = current.parent;
        }

        return null;
    }

    private static float SafeDiv(float a, float b)
    {
        return Mathf.Abs(b) < 0.0001f ? a : a / b;
    }

    private static void EnsureMeshReadable(Mesh mesh)
    {
        if (mesh == null || mesh.isReadable)
            return;

        string path = AssetDatabase.GetAssetPath(mesh);
        if (string.IsNullOrEmpty(path))
            return;

        ModelImporter importer = AssetImporter.GetAtPath(path) as ModelImporter;
        if (importer == null)
            return;

        if (!importer.isReadable)
        {
            importer.isReadable = true;
            importer.SaveAndReimport();
            Debug.Log($"[CloakCloth] Enabled Read/Write on '{path}'.");
        }
    }

    private static void ApplyPinCoefficients(Cloth cloth, Mesh mesh, Transform cloakTransform)
    {
        if (cloth == null || mesh == null || cloakTransform == null)
            return;

        Vector3[] vertices = mesh.vertices;
        if (vertices == null || vertices.Length == 0)
            return;

        float[] worldY = new float[vertices.Length];
        float minY = float.MaxValue;
        float maxY = float.MinValue;
        Matrix4x4 localToWorld = cloakTransform.localToWorldMatrix;
        for (int i = 0; i < vertices.Length; i++)
        {
            float y = localToWorld.MultiplyPoint3x4(vertices[i]).y;
            worldY[i] = y;
            if (y < minY) minY = y;
            if (y > maxY) maxY = y;
        }

        float height = Mathf.Max(0.0001f, maxY - minY);
        float pinFloor = maxY - height * PinTopFraction;
        float softFloor = pinFloor - height * SoftBandFraction;

        ClothSkinningCoefficient[] coeffs = new ClothSkinningCoefficient[vertices.Length];
        int pinned = 0;
        for (int i = 0; i < vertices.Length; i++)
        {
            float y = worldY[i];
            coeffs[i].collisionSphereDistance = 0f;

            if (y >= pinFloor)
            {
                coeffs[i].maxDistance = 0f;
                pinned++;
            }
            else if (y >= softFloor)
            {
                float t = Mathf.InverseLerp(pinFloor, softFloor, y);
                coeffs[i].maxDistance = Mathf.Lerp(0.02f, MidMaxDistance, Mathf.SmoothStep(0f, 1f, t));
            }
            else
            {
                float t = Mathf.InverseLerp(softFloor, minY, y);
                coeffs[i].maxDistance = Mathf.Lerp(MidMaxDistance, HemMaxDistance, Mathf.SmoothStep(0f, 1f, t));
            }
        }

        cloth.coefficients = coeffs;
        Debug.Log(
            $"[CloakCloth] Pins {pinned}/{vertices.Length} collar verts " +
            $"(worldY {minY:F2}..{maxY:F2}). Hem maxDistance={HemMaxDistance:F2}m.");
    }

    private static Transform FindChild(Transform root, string exactName)
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
