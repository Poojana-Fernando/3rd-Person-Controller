#if UNITY_EDITOR
using Game.Train;
using UnityEditor;
using UnityEngine;
using UnityEngine.Splines;

namespace Game.Train.Editor
{
    public static class AuroraTrainSetupEditor
    {
        const string MenuRoot = "Game/Train/";

        [MenuItem(MenuRoot + "Validate Aurora Rig")]
        static void ValidateAuroraRig()
        {
            TrainWheelRig rig = FindAuroraRig();
            if (rig == null)
            {
                EditorUtility.DisplayDialog("Aurora", "Select the Aurora locomotive root (with TrainWheelRig).", "OK");
                return;
            }

            rig.ResetMeasurement();
            rig.LogAuroraWheelAxles();

            var driver = rig.GetComponent<TrainSplineDriver>();
            if (driver == null)
                Debug.LogWarning("[Aurora] Missing TrainSplineDriver on locomotive root.");
            else if (driver.Track == null)
                Debug.LogWarning("[Aurora] TrainSplineDriver.track is not assigned.");
            else
                Debug.Log($"[Aurora] Track assigned: {driver.Track.name}, spline {driver.SplineIndex}.");

            var net = rig.GetComponent<NetworkTrainDriver>();
            if (net == null)
                Debug.Log("[Aurora] No NetworkTrainDriver (OK for solo play — TrainSeatInteractor drives via TrainSplineDriver).");

            if (rig.GetComponent<Rigidbody>() == null)
                Debug.LogWarning("[Aurora] Missing Rigidbody on locomotive root.");

            TrainSeat[] seats = rig.GetComponentsInChildren<TrainSeat>(true);
            Debug.Log($"[Aurora] TrainSeat count under locomotive: {seats.Length}");
        }

        [MenuItem(MenuRoot + "Set Aurora Wheel Mode On Selected Rig")]
        static void SetAuroraWheelMode()
        {
            TrainWheelRig rig = FindAuroraRig();
            if (rig == null)
                return;

            Undo.RecordObject(rig, "Set Aurora wheel mode");
            SerializedObject so = new SerializedObject(rig);
            so.FindProperty("wheelNamingMode").enumValueIndex = (int)TrainWheelRig.WheelNamingMode.AuroraNumeric;
            so.ApplyModifiedPropertiesWithoutUndo();
            rig.ResetMeasurement();
            EditorUtility.SetDirty(rig);
            Debug.Log("[Aurora] wheelNamingMode set to AuroraNumeric on " + rig.name);
        }

        [MenuItem(MenuRoot + "Set Manual Wheel Mode On Selected Rig")]
        static void SetManualWheelMode()
        {
            TrainWheelRig rig = FindAuroraRig();
            if (rig == null)
                return;

            Undo.RecordObject(rig, "Set manual wheel mode");
            rig.UseManualWheelMode();
            EditorUtility.SetDirty(rig);
            Debug.Log("[Aurora] wheelNamingMode set to Manual on " + rig.name + ". Fill Manual Axles in the Inspector.");
        }

        [MenuItem(MenuRoot + "Parent Stool To Aurora (Driver Seat)")]
        static void ParentStoolToAurora()
        {
            TrainWheelRig rig = FindAuroraRig();
            if (rig == null)
                return;

            GameObject stool = Selection.activeGameObject;
            if (stool == null || !stool.name.ToLowerInvariant().Contains("stool"))
            {
                foreach (Transform child in rig.transform.GetComponentsInChildren<Transform>(true))
                {
                    if (child.name.ToLowerInvariant().Contains("stool"))
                    {
                        stool = child.gameObject;
                        break;
                    }
                }
            }

            if (stool == null)
            {
                EditorUtility.DisplayDialog("Aurora", "Select the Stool object or place a Stool under the scene.", "OK");
                return;
            }

            Undo.SetTransformParent(stool.transform, rig.transform, "Parent stool to Aurora");
            stool.transform.localPosition = new Vector3(0f, 1.35f, -2.5f);
            stool.transform.localRotation = Quaternion.identity;
            stool.transform.localScale = Vector3.one * 0.5f;

            StripSeatInteractorFromStool(stool);

            TrainSeat seat = stool.GetComponent<TrainSeat>();
            if (seat == null)
            {
                Undo.AddComponent<TrainSeat>(stool);
                seat = stool.GetComponent<TrainSeat>();
            }

            EditorUtility.SetDirty(stool);
            Debug.Log("[Aurora] Stool parented to " + rig.name + " with TrainSeat. Tune local position in Inspector.");
        }

        [MenuItem(MenuRoot + "Remove TrainSeatInteractor From Stool")]
        static void RemoveInteractorFromSelected()
        {
            if (Selection.activeGameObject == null)
                return;
            StripSeatInteractorFromStool(Selection.activeGameObject);
        }

        static TrainWheelRig FindAuroraRig()
        {
            if (Selection.activeGameObject != null)
            {
                TrainWheelRig onSelection = Selection.activeGameObject.GetComponent<TrainWheelRig>();
                if (onSelection != null)
                    return onSelection;
                onSelection = Selection.activeGameObject.GetComponentInParent<TrainWheelRig>();
                if (onSelection != null)
                    return onSelection;
            }

            TrainWheelRig[] rigs = Object.FindObjectsByType<TrainWheelRig>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            foreach (TrainWheelRig rig in rigs)
            {
                if (rig.name.IndexOf("aurora", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    return rig;
            }

            return rigs.Length > 0 ? rigs[0] : null;
        }

        static void StripSeatInteractorFromStool(GameObject stool)
        {
            MonoBehaviour[] behaviours = stool.GetComponents<MonoBehaviour>();
            for (int i = 0; i < behaviours.Length; i++)
            {
                if (behaviours[i] == null)
                    continue;
                string typeName = behaviours[i].GetType().Name;
                if (typeName == "TrainSeatInteractor")
                {
                    Undo.DestroyObjectImmediate(behaviours[i]);
                    Debug.Log("[Aurora] Removed TrainSeatInteractor from stool (belongs on player).");
                }
            }
        }
    }
}
#endif
