#if UNITY_EDITOR
using Game.Train;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Splines;

/// <summary>
/// Wires the rail driver stack onto Level01's locomotive and the railway spline.
/// </summary>
public static class TrainLevel01Setup
{
    public const string Level01Path = "Assets/_Game/Scenes/Level01.unity";
    public const string TrainName = "SovietTrain_gameready";

    [MenuItem("Tools/Train/Wire Level01 Train Driver")]
    public static void WireLevel01()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (scene.path != Level01Path)
            scene = EditorSceneManager.OpenScene(Level01Path, OpenSceneMode.Single);

        GameObject train = GameObject.Find(TrainName);
        if (train == null)
            throw new System.Exception("Missing GameObject " + TrainName);

        SplineContainer spline = TrainSplineDriver.FindRailTrack();
        if (spline == null)
            throw new System.Exception("No rail spline (SplineInstantiate with a railway prefab) found");

        Rigidbody body = train.GetComponent<Rigidbody>();
        if (body == null)
            body = Undo.AddComponent<Rigidbody>(train);
        body.mass = 80000f;
        body.isKinematic = true;
        body.useGravity = false;
        body.interpolation = RigidbodyInterpolation.Interpolate;

        if (train.GetComponent<TrainWheelRig>() == null)
            Undo.AddComponent<TrainWheelRig>(train);
        if (train.GetComponent<MovingPlatform>() == null)
            Undo.AddComponent<MovingPlatform>(train);

        TrainSplineDriver driver = train.GetComponent<TrainSplineDriver>();
        if (driver == null)
            driver = Undo.AddComponent<TrainSplineDriver>(train);

        var so = new SerializedObject(driver);
        so.FindProperty("track").objectReferenceValue = spline;
        so.FindProperty("splineIndex").intValue = 0;
        so.ApplyModifiedPropertiesWithoutUndo();

        if (train.GetComponent<NetworkObject>() == null)
            Undo.AddComponent<NetworkObject>(train);
        if (train.GetComponent<NetworkTrainDriver>() == null)
            Undo.AddComponent<NetworkTrainDriver>(train);

        EditorUtility.SetDirty(train);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log($"[TrainLevel01Setup] Wired rail driver on {TrainName} -> {spline.name}");
    }

    public static readonly string[] WagonNames = { "trainStructure", "train_vagon_platforma" };

    /// <summary>Adds a TrainConsist to Level01's locomotive and couples the existing flatbed as its first wagon.</summary>
    [MenuItem("Tools/Train/Attach Wagon To Train")]
    public static void AttachWagon()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (scene.path != Level01Path)
            scene = EditorSceneManager.OpenScene(Level01Path, OpenSceneMode.Single);

        GameObject train = GameObject.Find(TrainName);
        if (train == null)
            throw new System.Exception("Missing GameObject " + TrainName);
        if (train.GetComponent<TrainSplineDriver>() == null)
            throw new System.Exception($"{TrainName} has no TrainSplineDriver; run Tools/Train/Wire Level01 Train Driver first");

        GameObject wagon = null;
        foreach (string wagonName in WagonNames)
        {
            wagon = GameObject.Find(wagonName);
            if (wagon != null)
                break;
        }
        if (wagon == null)
            throw new System.Exception("Missing wagon GameObject (looked for " + string.Join(", ", WagonNames) + ")");

        if (wagon.transform.IsChildOf(train.transform))
            Undo.SetTransformParent(wagon.transform, null, "Detach Wagon From Train");

        TrainConsist consist = AddConsist(train);
        TrainCarFollower follower = TrainWagonSetup.Prepare(wagon);
        Undo.RecordObject(consist, "Add Wagon");
        consist.AddWagon(follower);
        TrainWagonSetup.Snap(consist);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log($"[TrainLevel01Setup] Coupled {wagon.name} to {TrainName} via TrainConsist");
    }

    [MenuItem("Tools/Train/Setup Wheel Sparks")]
    public static void SetupWheelSparks()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (scene.path != Level01Path)
            scene = EditorSceneManager.OpenScene(Level01Path, OpenSceneMode.Single);

        GameObject train = GameObject.Find(TrainName);
        if (train == null)
            throw new System.Exception("Missing GameObject " + TrainName);

        if (train.GetComponent<TrainWheelSparks>() == null)
            Undo.AddComponent<TrainWheelSparks>(train);

        EditorUtility.SetDirty(train);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log($"[TrainLevel01Setup] Wheel sparks enabled on {TrainName}");
    }

    [MenuItem("Tools/Train/Add Train Consist To Selected Locomotive")]
    public static void AddConsistToSelection()
    {
        GameObject selected = Selection.activeGameObject;
        if (selected == null || selected.GetComponentInParent<TrainSplineDriver>() == null)
        {
            EditorUtility.DisplayDialog("Train Consist", "Select a locomotive that has a TrainSplineDriver.", "OK");
            return;
        }

        Selection.activeGameObject = AddConsist(selected.GetComponentInParent<TrainSplineDriver>().gameObject).gameObject;
    }

    private static TrainConsist AddConsist(GameObject train)
    {
        TrainConsist consist = train.GetComponent<TrainConsist>();
        return consist != null ? consist : Undo.AddComponent<TrainConsist>(train);
    }
}
#endif
