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
    private const string GenericBakedMeshFolder = "Assets/Generated/ClothMeshes";

    // Coherent sheet: thick double-sided capes tear if maxDistance is large + stretch is soft.
    private const float HardPinVertFraction = 0.12f; // top 12% locked to neck
    private const float SoftPinVertFraction = 0.28f; // upper body gently follows skinned pose
    private const float HemMaxDistance = 0.32f;      // enough sway, not enough to rip layers apart
    private const float MidMaxDistance = 0.14f;
    private const float SoftNearPinMaxDistance = 0.04f;

    private static readonly string[] CloakCandidateNames = { "capeeee", "cape", "cloak" };

    [MenuItem("Tools/Player/Fix Cloak Cloth Coherence")]
    public static void FixCloakClothCoherence()
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
            Debug.LogError("[CloakCloth] No cape/cloak mesh found under warrior.");
            return;
        }

        GameObject cloak = cloakTransform.gameObject;
        Undo.RegisterFullObjectHierarchyUndo(cloak, "Fix Cloak Cloth Coherence");

        SkinnedMeshRenderer skinned = cloak.GetComponent<SkinnedMeshRenderer>();
        if (skinned == null || skinned.sharedMesh == null)
        {
            Debug.LogError("[CloakCloth] Cloak needs SkinnedMeshRenderer + mesh. Run Setup first.");
            return;
        }

        // Non-uniform scale makes Unity Cloth tear layers apart — bake it away.
        Vector3 scaleToBake = cloak.transform.localScale;
        bool needsScaleBake = Mathf.Abs(scaleToBake.x - 1f) > 0.01f
            || Mathf.Abs(scaleToBake.y - 1f) > 0.01f
            || Mathf.Abs(scaleToBake.z - 1f) > 0.01f
            || Mathf.Abs(scaleToBake.x - scaleToBake.y) > 0.01f
            || Mathf.Abs(scaleToBake.y - scaleToBake.z) > 0.01f;

        PlayerCloakCloth runtime = cloak.GetComponent<PlayerCloakCloth>();
        if (runtime != null)
            Undo.DestroyObjectImmediate(runtime);
        Cloth existingCloth = cloak.GetComponent<Cloth>();
        if (existingCloth != null)
            Undo.DestroyObjectImmediate(existingCloth);

        Mesh mesh = skinned.sharedMesh;
        if (needsScaleBake)
        {
            string bakePath = ResolveBakeAssetPath(cloak.name + "_coherent", mesh);
            mesh = BakeScaleIntoMeshAsset(mesh, scaleToBake, bakePath);
            cloak.transform.localScale = Vector3.one;
            skinned.sharedMesh = mesh;
            skinned.localBounds = mesh.bounds;
            Debug.Log($"[CloakCloth] Baked non-uniform scale {scaleToBake} → localScale=1.");
        }

        skinned.rootBone = cloak.transform;
        skinned.bones = new[] { cloak.transform };
        skinned.updateWhenOffscreen = true;

        Cloth cloth = Undo.AddComponent<Cloth>(cloak);
        ApplyCoherentClothSettings(cloth);
        ApplyPinCoefficients(cloth, mesh, cloak.transform);

        runtime = Undo.AddComponent<PlayerCloakCloth>(cloak);
        SerializedObject runtimeSo = new SerializedObject(runtime);
        runtimeSo.FindProperty("cloth").objectReferenceValue = cloth;
        runtimeSo.FindProperty("forceNoColliders").boolValue = true;
        runtimeSo.ApplyModifiedPropertiesWithoutUndo();
        runtime.ApplyNoCollision();

        int ignoreRaycast = LayerMask.NameToLayer("Ignore Raycast");
        if (ignoreRaycast >= 0)
            cloak.layer = ignoreRaycast;

        EditorUtility.SetDirty(cloak);
        EditorUtility.SetDirty(skinned);
        EditorUtility.SetDirty(cloth);
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        EditorSceneManager.SaveOpenScenes();
        AssetDatabase.SaveAssets();

        Debug.Log(
            $"[CloakCloth] Coherence fix on '{cloak.name}': high stretch, modest hem travel, " +
            "uniform scale, no body colliders. Test in Play Mode.");
    }

    [MenuItem("Tools/Player/Retune Cloak Soft Cloth (AC drape)")]
    public static void RetuneCloakSoftCloth()
    {
        // Soft retune alone caused tearing on thick capes — route to coherence fix.
        FixCloakClothCoherence();
    }

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
        // Prefer neck so the cloak hangs from the neck/collar area.
        Transform attachParent = neck != null ? neck : (spine02 != null ? spine02 : warrior);

        // Always reparent to neck (preserve world pose) even if currently under spine.
        if (cloak.transform.parent != attachParent)
        {
            Vector3 worldPos = cloak.transform.position;
            Quaternion worldRot = cloak.transform.rotation;
            Vector3 worldScale = cloak.transform.lossyScale;
            Undo.SetTransformParent(cloak.transform, attachParent, "Parent cloak to neck");
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
        string bakePath = ResolveBakeAssetPath(cloak.name, bakeSource);
        Mesh bakedMesh = BakeScaleIntoMeshAsset(bakeSource, scaleToBake, bakePath);
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

        ApplyCoherentClothSettings(cloth);
        // Explicitly no cloth↔body collisions (player capsule / spheres).
        cloth.capsuleColliders = System.Array.Empty<CapsuleCollider>();
        cloth.sphereColliders = System.Array.Empty<ClothSphereColliderPair>();

        ApplyPinCoefficients(cloth, bakedMesh, cloak.transform);

        PlayerCloakCloth runtime = cloak.GetComponent<PlayerCloakCloth>();
        if (runtime == null)
            runtime = Undo.AddComponent<PlayerCloakCloth>(cloak);
        SerializedObject runtimeSo = new SerializedObject(runtime);
        runtimeSo.FindProperty("cloth").objectReferenceValue = cloth;
        runtimeSo.FindProperty("forceNoColliders").boolValue = true;
        runtimeSo.ApplyModifiedPropertiesWithoutUndo();
        runtime.ApplyNoCollision();

        EditorUtility.SetDirty(cloak);
        EditorUtility.SetDirty(skinned);
        EditorUtility.SetDirty(cloth);
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        EditorSceneManager.SaveOpenScenes();
        AssetDatabase.SaveAssets();

        Debug.Log(
            $"[CloakCloth] '{cloak.name}' attached to '{attachParent.name}', " +
            $"baked scale {scaleToBake}, no body collisions, Layer=Ignore Raycast.");
    }

    private static string ResolveBakeAssetPath(string cloakName, Mesh bakeSource)
    {
        string path = bakeSource != null ? AssetDatabase.GetAssetPath(bakeSource) : null;
        if (!string.IsNullOrEmpty(path)
            && path.IndexOf("red-leather-cape", System.StringComparison.OrdinalIgnoreCase) >= 0)
            return BakedMeshPath;

        if (!AssetDatabase.IsValidFolder("Assets/Generated"))
            AssetDatabase.CreateFolder("Assets", "Generated");
        if (!AssetDatabase.IsValidFolder(GenericBakedMeshFolder))
            AssetDatabase.CreateFolder("Assets/Generated", "ClothMeshes");

        string safe = string.IsNullOrEmpty(cloakName) ? "Cloak" : cloakName;
        foreach (char c in System.IO.Path.GetInvalidFileNameChars())
            safe = safe.Replace(c, '_');
        return $"{GenericBakedMeshFolder}/{safe}_ClothBaked.asset";
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
        // Prefer the mesh already on the cloak (FBX sub-asset or previous bake source).
        if (!string.IsNullOrEmpty(path))
        {
            // If this is one of our baked assets, fall back to the original FBX cape mesh.
            if (path.IndexOf("ClothBaked", System.StringComparison.OrdinalIgnoreCase) >= 0
                || path.IndexOf("/Generated/", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                // continue to FBX lookup below for red-leather only
            }
            else
            {
                return mesh;
            }
        }

        // Legacy red-leather bake recovery.
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

    private static void ApplyCoherentClothSettings(Cloth cloth)
    {
        if (cloth == null)
            return;

        // High stretch keeps double-sided layers from ripping; low bend lets the sheet fold.
        cloth.stretchingStiffness = 0.92f;
        cloth.bendingStiffness = 0.18f;
        cloth.damping = 0.12f;
        cloth.useGravity = true;
        cloth.externalAcceleration = new Vector3(0f, -3.5f, 0f);
        cloth.randomAcceleration = new Vector3(0.25f, 0.08f, 0.25f);
        cloth.worldVelocityScale = 1.05f;
        cloth.worldAccelerationScale = 1.2f;
        cloth.friction = 0.2f;
        cloth.collisionMassScale = 0f;
        cloth.enableContinuousCollision = false;
        cloth.useVirtualParticles = 1f;
        cloth.clothSolverFrequency = 180f;
        cloth.useTethers = true;
        cloth.stiffnessFrequency = 8f;
        cloth.capsuleColliders = System.Array.Empty<CapsuleCollider>();
        cloth.sphereColliders = System.Array.Empty<ClothSphereColliderPair>();
    }

    private static void ApplySoftClothSettings(Cloth cloth)
    {
        // Kept for compatibility; soft settings tear thick cape meshes — use coherent.
        ApplyCoherentClothSettings(cloth);
    }

    private static void ApplyPinCoefficients(Cloth cloth, Mesh mesh, Transform cloakTransform)
    {
        if (cloth == null || mesh == null || cloakTransform == null)
            return;

        Vector3[] vertices = mesh.vertices;
        if (vertices == null || vertices.Length == 0)
            return;

        int count = vertices.Length;
        var ranked = new System.Collections.Generic.List<(int index, float y)>(count);
        Matrix4x4 localToWorld = cloakTransform.localToWorldMatrix;
        for (int i = 0; i < count; i++)
        {
            float y = localToWorld.MultiplyPoint3x4(vertices[i]).y;
            ranked.Add((i, y));
        }

        ranked.Sort((a, b) => b.y.CompareTo(a.y)); // highest (collar) first

        int hardPinCount = Mathf.Clamp(Mathf.CeilToInt(count * HardPinVertFraction), 12, count);
        int softEnd = Mathf.Clamp(hardPinCount + Mathf.CeilToInt(count * SoftPinVertFraction), hardPinCount, count);

        ClothSkinningCoefficient[] coeffs = new ClothSkinningCoefficient[count];
        for (int i = 0; i < count; i++)
        {
            coeffs[i].maxDistance = HemMaxDistance;
            coeffs[i].collisionSphereDistance = 0f;
        }

        for (int rank = 0; rank < count; rank++)
        {
            int vi = ranked[rank].index;
            coeffs[vi].collisionSphereDistance = 0f;

            if (rank < hardPinCount)
            {
                coeffs[vi].maxDistance = 0f;
            }
            else if (rank < softEnd)
            {
                float t = Mathf.InverseLerp(hardPinCount, softEnd - 1, rank);
                coeffs[vi].maxDistance = Mathf.Lerp(SoftNearPinMaxDistance, MidMaxDistance, Mathf.SmoothStep(0f, 1f, t));
            }
            else
            {
                float t = Mathf.InverseLerp(softEnd, count - 1, rank);
                coeffs[vi].maxDistance = Mathf.Lerp(MidMaxDistance, HemMaxDistance, Mathf.SmoothStep(0f, 1f, t));
            }
        }

        cloth.coefficients = coeffs;
        Debug.Log(
            $"[CloakCloth] Rank pins: {hardPinCount}/{count} hard + {softEnd - hardPinCount} soft. " +
            $"Hem maxDistance={HemMaxDistance:F2}m.");
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
