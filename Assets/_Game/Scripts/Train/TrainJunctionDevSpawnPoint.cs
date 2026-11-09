using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Splines;

namespace Game.Train
{
    /// <summary>
    /// Developer spawn marker: places the scene locomotive on the stem approach before a <see cref="RailwayJunction"/>.
    /// </summary>
    [DisallowMultipleComponent]
    [ExecuteAlways]
    public class TrainJunctionDevSpawnPoint : MonoBehaviour
    {
        public enum LeverPreset
        {
            Unchanged,
            Straight,
            Diverging
        }

        [SerializeField] private RailwayJunction junction;
        [Tooltip("Meters along the stem spline before the turnout knot (approach from lower arc-length).")]
        [Min(5f)]
        [SerializeField] private float approachMeters = 35f;
        [SerializeField] private LeverPreset leverPreset = LeverPreset.Unchanged;
        [Tooltip("Locomotive in the scene. Leave empty to find SovietTrain_gameready.")]
        [SerializeField] private TrainSplineDriver trainOverride;
        [SerializeField] private string trainObjectName = "SovietTrain_gameready";

        public RailwayJunction Junction => junction;
        public float ApproachMeters => approachMeters;
        public LeverPreset Preset => leverPreset;

        private void OnValidate()
        {
            if (junction == null)
                junction = GetComponentInParent<RailwayJunction>();
            SnapMarkerToTrack();
        }

        /// <summary>World pose on the stem spline used for gizmos and spawn.</summary>
        public bool TryGetSpawnPose(out Pose pose, out int splineIndex, out float splineDistance)
        {
            pose = default;
            splineIndex = 0;
            splineDistance = 0f;
            if (junction == null || !junction.TryGetTurnout(out RailwayTopology.TurnoutData turnout))
                return false;

            SplineContainer track = TrainSplineDriver.FindRailTrack();
            if (track == null)
                return false;

            splineIndex = turnout.StemIn.Spline;
            float knotD = RailwayTopology.KnotDistance(track, turnout.StemIn);
            float len = track.CalculateLength(splineIndex);
            splineDistance = Mathf.Clamp(knotD - approachMeters, 0f, len);

            Spline spline = track.Splines[splineIndex];
            float t = spline.ConvertIndexUnit(splineDistance, PathIndexUnit.Distance, PathIndexUnit.Normalized);
            float3 local = spline.EvaluatePosition(t);
            float3 tan = spline.EvaluateTangent(t);
            Vector3 world = track.transform.TransformPoint(local);
            Vector3 forward = track.transform.TransformDirection(tan).normalized;
            if (forward.sqrMagnitude < 1e-6f)
                forward = transform.forward;

            pose = new Pose(world, Quaternion.LookRotation(forward, Vector3.up));
            return true;
        }

        public void SnapMarkerToTrack()
        {
            if (!TryGetSpawnPose(out Pose pose, out _, out _))
                return;
            transform.SetPositionAndRotation(pose.position, pose.rotation);
        }

        /// <summary>Moves the configured locomotive onto the approach and optionally sets the lever.</summary>
        public bool TrySpawnTrain(out string message)
        {
            message = null;
            if (!TryGetSpawnPose(out _, out int splineIndex, out float splineDistance))
            {
                message = "Could not resolve spawn pose (missing junction or track).";
                return false;
            }

            TrainSplineDriver driver = ResolveTrain();
            if (driver == null)
            {
                message = $"No TrainSplineDriver found (looked for '{trainObjectName}').";
                return false;
            }

            SplineContainer track = TrainSplineDriver.FindRailTrack();
            if (track == null)
            {
                message = "No railway SplineContainer.";
                return false;
            }

            ApplyLeverPreset();

            driver.SetTrack(track);
            if (!driver.TryConfigureSpawn(splineIndex, splineDistance))
            {
                message = "Failed to bind locomotive to track.";
                return false;
            }

            message = $"Placed {driver.name} on spline {splineIndex} at {splineDistance:F1} m (junction {junction.name}).";
            return true;
        }

        private TrainSplineDriver ResolveTrain()
        {
            if (trainOverride != null)
                return trainOverride;
            TrainSplineDriver[] all = FindObjectsByType<TrainSplineDriver>(FindObjectsInactive.Include);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != null && all[i].gameObject.name == trainObjectName)
                    return all[i];
            }
            return all.Length > 0 ? all[0] : null;
        }

        [ContextMenu("Spawn Train Here")]
        private void SpawnTrainContextMenu()
        {
            if (TrySpawnTrain(out string message))
                Debug.Log($"[TrainJunctionDevSpawn] {message}");
            else
                Debug.LogWarning($"[TrainJunctionDevSpawn] {message}");
        }

        [ContextMenu("Snap Marker To Track")]
        private void SnapContextMenu()
        {
            SnapMarkerToTrack();
        }

        private void ApplyLeverPreset()
        {
            if (junction == null || leverPreset == LeverPreset.Unchanged)
                return;

            bool diverging = leverPreset == LeverPreset.Diverging;
            if (junction.Diverging != diverging)
                junction.SetDiverging(diverging, false);
        }

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            if (!TryGetSpawnPose(out Pose pose, out _, out _))
                return;

            Gizmos.color = new Color(0.2f, 0.85f, 1f, 0.9f);
            Gizmos.DrawWireSphere(pose.position, 1.2f);
            Gizmos.DrawLine(pose.position, pose.position + pose.forward * 4f);
        }

        private void OnDrawGizmosSelected()
        {
            if (junction == null)
                return;
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(junction.LeverWorldPosition, junction.InteractRadius);
        }
#endif
    }
}
