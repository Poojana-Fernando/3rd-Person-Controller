using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Splines;

namespace Game.Train
{
    /// <summary>Trackside turnout with lever and route lamps (green open, red closed).</summary>
    [DisallowMultipleComponent]
    public class RailwayJunction : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        [SerializeField] private SplineContainer track;
        [SerializeField] private SplineKnotIndex stemIn;
        [SerializeField] private SplineKnotIndex stemOut;
        [SerializeField] private SplineKnotIndex branch;
        [SerializeField] private bool diverging;
        [SerializeField] private float occupyRouteDistance = 20f;
        [SerializeField] private float interactRadius = 2.5f;
        [SerializeField] private Transform leverPivot;
        [SerializeField] private float leverAngleStraight = 25f;
        [SerializeField] private float leverAngleDiverging = -25f;
        [SerializeField] private Renderer straightLamp;
        [SerializeField] private Renderer branchLamp;
        [SerializeField] private Color lampGreen = new Color(0.1f, 0.9f, 0.15f);
        [SerializeField] private Color lampRed = new Color(0.9f, 0.1f, 0.05f);

        private RailwaySwitchboard _board;
        private int _boardIndex = -1;

        public bool Diverging => diverging;
        public float InteractRadius => interactRadius;
        public float OccupyRouteDistance => occupyRouteDistance;
        public int TopologyHash => stemIn.Spline * 10000 + stemIn.Knot * 100 + branch.Spline * 10 + branch.Knot;
        public Vector3 LeverWorldPosition => leverPivot != null ? leverPivot.position : transform.position;

        private void Awake()
        {
            if (track == null)
                track = TrainSplineDriver.FindRailTrack();
            _board = GetComponentInParent<RailwaySwitchboard>();
        }

        private void Start()
        {
            ApplyVisuals();
        }

        public void Configure(
            SplineContainer container,
            RailwayTopology.TurnoutData data,
            Transform lever,
            Renderer straight,
            Renderer branchLampRenderer)
        {
            track = container;
            stemIn = data.StemIn;
            stemOut = data.StemOut;
            branch = data.Branch;
            leverPivot = lever;
            straightLamp = straight;
            branchLamp = branchLampRenderer;
            transform.position = data.WorldPosition;
        }

        public void RegisterWithBoard(RailwaySwitchboard board, int index)
        {
            _board = board;
            _boardIndex = index;
        }

        public bool TryGetTurnout(out RailwayTopology.TurnoutData data)
        {
            if (track == null)
            {
                data = default;
                return false;
            }

            data = new RailwayTopology.TurnoutData
            {
                StemIn = stemIn,
                StemOut = stemOut,
                Branch = branch,
                WorldPosition = RailwayTopology.KnotWorldPosition(track, stemIn)
            };
            return true;
        }

        public bool Matches(RailwayTopology.TurnoutData other)
        {
            return stemIn.Equals(other.StemIn) && branch.Equals(other.Branch);
        }

        public bool SpanEndMatches(SplineContainer container, RailSpan span, bool forwardAlongRoute, RailwayTopology.TurnoutData _)
        {
            return RailwayNetwork.SpanEndMatchesTopology(container, span, forwardAlongRoute, new RailwayTopology.TurnoutData
            {
                StemIn = stemIn,
                StemOut = stemOut,
                Branch = branch
            });
        }

        public void SetDiverging(bool value, bool fromNetwork)
        {
            diverging = value;
            ApplyVisuals();
            if (!fromNetwork && _board != null && _boardIndex >= 0)
                _board.PushLocalState(_boardIndex, diverging);

            RailwayNetwork net = RailwayNetwork.Instance;
            if (net != null)
                net.NotifyTurnoutStateChanged(this);
        }

        public void ApplyStateFromNetwork(bool value)
        {
            diverging = value;
            ApplyVisuals();
            RailwayNetwork net = RailwayNetwork.Instance;
            if (net != null)
                net.NotifyTurnoutStateChanged(this);
        }

        private const float MovingConsistThreshold = 0.05f;

        public void Toggle()
        {
            if (!TryToggleLever(out string reason) && !string.IsNullOrEmpty(reason))
                Debug.LogWarning($"[RailwayJunction] {name}: {reason}");
        }

        /// <summary>Flips the turnout when allowed. Stopped consists on the junction do not block the lever.</summary>
        public bool TryToggleLever(out string blockReason)
        {
            blockReason = null;
            if (TryGetLeverBlockReason(out blockReason))
                return false;

            if (_board != null)
                return _board.TryRequestToggle(this, out blockReason);

            SetDiverging(!diverging, false);
            return true;
        }

        public bool BlocksLeverToggle() => TryGetLeverBlockReason(out _);

        public bool TryGetLeverBlockReason(out string reason)
        {
            reason = null;
            TrainSplineDriver[] drivers = FindObjectsByType<TrainSplineDriver>(FindObjectsInactive.Exclude);
            foreach (TrainSplineDriver driver in drivers)
            {
                if (driver == null || Mathf.Abs(driver.Velocity) < MovingConsistThreshold)
                    continue;
                if (!IsConsistOnJunction(driver))
                    continue;
                reason = "Train moving on junction";
                return true;
            }

            return false;
        }

        public bool IsOccupied() => IsConsistOnJunction();

        private bool IsConsistOnJunction()
        {
            RailwayNetwork net = RailwayNetwork.Instance;
            if (net != null)
                return net.IsJunctionOccupied(this, occupyRouteDistance);

            TrainSplineDriver[] drivers = FindObjectsByType<TrainSplineDriver>(FindObjectsInactive.Exclude);
            foreach (TrainSplineDriver driver in drivers)
            {
                if (IsConsistOnJunction(driver))
                    return true;
            }
            return false;
        }

        private bool IsConsistOnJunction(TrainSplineDriver driver)
        {
            if (driver == null || driver.Route == null || track == null)
                return false;
            RailwayNetwork net = RailwayNetwork.Instance;
            if (net == null || !net.TryGetRouteDistanceForKnot(driver.Route, stemIn, out float knotRoute))
                return false;

            float zoneLo = knotRoute - occupyRouteDistance;
            float zoneHi = knotRoute + occupyRouteDistance;
            return driver.MaxDistance >= zoneLo && driver.MinDistance <= zoneHi;
        }

        public void ApplyVisuals()
        {
            bool straightOpen = !diverging;
            SetLamp(straightLamp, straightOpen);
            SetLamp(branchLamp, !straightOpen);

            if (leverPivot != null)
            {
                float yaw = diverging ? leverAngleDiverging : leverAngleStraight;
                leverPivot.localRotation = Quaternion.Euler(0f, yaw, 0f);
            }
        }

        private const float LampEmissionScale = 5f;

        private void SetLamp(Renderer lamp, bool green)
        {
            if (lamp == null)
                return;

            Color c = green ? lampGreen : lampRed;
#if UNITY_EDITOR
            Material mat = Application.isPlaying ? lamp.material : lamp.sharedMaterial;
#else
            Material mat = lamp.material;
#endif
            if (mat == null)
                return;
            if (mat.HasProperty(BaseColorId))
                mat.SetColor(BaseColorId, c);
            if (mat.HasProperty(EmissionColorId))
            {
                mat.EnableKeyword("_EMISSION");
                mat.SetColor(EmissionColorId, c * LampEmissionScale);
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }

            Light point = lamp.GetComponent<Light>();
            if (point == null)
            {
                point = lamp.gameObject.AddComponent<Light>();
                point.type = LightType.Point;
                point.range = 2.5f;
                point.shadows = LightShadows.None;
            }

            point.color = c;
            point.intensity = green ? 1.75f : 0.35f;
            point.enabled = true;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            ApplyVisuals();
        }

        private void OnDrawGizmosSelected()
        {
            if (track == null)
                return;
            Gizmos.color = diverging ? Color.yellow : Color.green;
            Gizmos.DrawWireSphere(RailwayTopology.KnotWorldPosition(track, stemIn), 1f);
        }
#endif
    }
}
