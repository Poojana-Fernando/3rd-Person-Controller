using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Splines;

namespace Game.Train
{
    /// <summary>Runtime index of turnouts, plain joins, and the rail <see cref="SplineContainer"/>.</summary>
    [DisallowMultipleComponent]
    public class RailwayNetwork : MonoBehaviour
    {
        private const float KnotEpsilon = 0.08f;

        private static RailwayNetwork _instance;

        [SerializeField] private SplineContainer track;
        [SerializeField] private RailwaySwitchboard switchboard;

        private readonly List<RailwayJunction> _junctions = new List<RailwayJunction>();
        private readonly List<RailwayTopology.JoinData> _joins = new List<RailwayTopology.JoinData>();
        private readonly List<RailwayTopology.TurnoutData> _implicitTurnouts = new List<RailwayTopology.TurnoutData>();

        public static RailwayNetwork Instance => _instance;

        /// <summary>Returns the network that owns scene junctions (not empty duplicates on the spline).</summary>
        public static RailwayNetwork Resolve()
        {
            if (_instance != null)
            {
                _instance.RefreshJunctionList();
                if (_instance.Junctions.Count > 0)
                    return _instance;
            }

            RailwayNetwork[] all = FindObjectsByType<RailwayNetwork>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            RailwayNetwork best = null;
            int bestCount = -1;
            for (int i = 0; i < all.Length; i++)
            {
                RailwayNetwork n = all[i];
                if (n == null)
                    continue;
                n.RefreshJunctionList();
                int count = n.Junctions.Count;
                if (count > bestCount)
                {
                    bestCount = count;
                    best = n;
                }
            }

            _instance = best;
            return best;
        }

        public SplineContainer Track => track;
        public RailwaySwitchboard Switchboard => switchboard;
        public IReadOnlyList<RailwayJunction> Junctions => _junctions;

#if UNITY_EDITOR
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticInstance()
        {
            _instance = null;
        }
#endif

        private void Awake()
        {
            RegisterInstance(this);

            if (track == null)
                track = TrainSplineDriver.FindRailTrack();

            if (switchboard == null)
                switchboard = GetComponentInChildren<RailwaySwitchboard>(true);

            RefreshJunctionList();
            RebuildJoinIndex();
        }

        private void Start()
        {
            RefreshJunctionList();
            if (_junctions.Count == 0 && track != null)
                RailwayJunctionRuntimeSetup.CreateMissingJunctions(this, track);
            RefreshJunctionList();
            RebuildJoinIndex();
        }

        public void RefreshJunctionList()
        {
            _junctions.Clear();
            if (switchboard != null)
                _junctions.AddRange(switchboard.GetComponentsInChildren<RailwayJunction>(true));
            else
                _junctions.AddRange(GetComponentsInChildren<RailwayJunction>(true));

            if (_junctions.Count == 0)
            {
                RailwayJunction[] sceneJunctions = FindObjectsByType<RailwayJunction>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                for (int i = 0; i < sceneJunctions.Length; i++)
                {
                    if (sceneJunctions[i] != null)
                        _junctions.Add(sceneJunctions[i]);
                }
            }
        }

        private void OnDestroy()
        {
            if (_instance == this)
                _instance = null;
        }

        private static void RegisterInstance(RailwayNetwork candidate)
        {
            if (candidate == null)
                return;

            candidate.RefreshJunctionList();
            int candidateCount = candidate.Junctions.Count;

            if (_instance == null || _instance == candidate)
            {
                _instance = candidate;
                return;
            }

            _instance.RefreshJunctionList();
            int existingCount = _instance.Junctions.Count;

            if (candidateCount > existingCount)
            {
                Debug.LogWarning(
                    $"[RailwayNetwork] Using '{candidate.name}' ({candidateCount} junctions) instead of '{_instance.name}' ({existingCount}).");
                _instance = candidate;
                return;
            }

            if (candidateCount < existingCount)
            {
                if (Application.isPlaying && candidateCount == 0)
                    Object.Destroy(candidate);
                return;
            }

            Debug.LogWarning($"[RailwayNetwork] Multiple networks with {candidateCount} junctions; keeping '{_instance.name}'.");
        }

        public void RebuildJoinIndex()
        {
            _joins.Clear();
            _implicitTurnouts.Clear();
            if (track == null)
                return;

            RailwayTopology.CollectTwoKnotJoins(track, _joins);
            var clusters = new List<RailwayTopology.TurnoutData>();
            RailwayTopology.CollectTurnouts(track, clusters);

            var covered = new HashSet<int>();
            foreach (RailwayJunction j in _junctions)
            {
                if (j != null)
                    covered.Add(j.TopologyHash);
            }

            foreach (RailwayTopology.TurnoutData t in clusters)
            {
                int h = HashTurnout(t);
                if (!covered.Contains(h))
                    _implicitTurnouts.Add(t);
            }
        }

        private static int HashTurnout(RailwayTopology.TurnoutData t)
        {
            return t.StemIn.Spline * 10000 + t.StemIn.Knot * 100 + t.Branch.Spline * 10 + t.Branch.Knot;
        }

        public bool IsDiverging(RailwayTopology.TurnoutData turnout)
        {
            foreach (RailwayJunction j in _junctions)
            {
                if (j != null && j.Matches(turnout))
                    return j.Diverging;
            }
            return false;
        }

        public bool TryGetDiverging(RailwayTopology.TurnoutData turnout, RailwayJunction junction, out bool diverging)
        {
            diverging = junction != null ? junction.Diverging : IsDiverging(turnout);
            return true;
        }

        /// <summary>Distance available along the route in <paramref name="travelSign"/> direction.</summary>
        public float AvailableLengthAhead(RailRoute route, float routeDistance, int travelSign)
        {
            if (route == null || track == null || route.Spans.Count == 0)
                return 0f;

            bool travelIncreasesRoute = travelSign > 0;
            if (TryFindNextTurnoutAlong(route, routeDistance, travelIncreasesRoute, out float knotRoute, out float exitExtra))
            {
                float toKnot = travelIncreasesRoute
                    ? Mathf.Max(0f, knotRoute - routeDistance)
                    : Mathf.Max(0f, routeDistance - knotRoute);
                return toKnot + exitExtra;
            }

            if (travelIncreasesRoute)
            {
                float remainingOnRoute = Mathf.Max(0f, route.TotalLength - routeDistance);
                RailSpan last = route.Spans[route.Spans.Count - 1];
                float extra = ProbeBeyondSpanEnd(last, true);
                return remainingOnRoute + extra;
            }

            return Mathf.Max(0f, routeDistance);
        }

        private bool TryFindNextTurnoutAlong(
            RailRoute route,
            float routeDistance,
            bool travelIncreasesRoute,
            out float knotRoute,
            out float exitExtra)
        {
            knotRoute = travelIncreasesRoute ? float.MaxValue : float.MinValue;
            exitExtra = 0f;
            bool found = false;

            foreach (RailwayJunction j in _junctions)
            {
                if (j == null || !j.TryGetTurnout(out RailwayTopology.TurnoutData t))
                    continue;
                if (ConsiderTurnoutAlong(route, routeDistance, t, j, travelIncreasesRoute, ref knotRoute, ref exitExtra))
                    found = true;
            }

            foreach (RailwayTopology.TurnoutData t in _implicitTurnouts)
            {
                if (ConsiderTurnoutAlong(route, routeDistance, t, null, travelIncreasesRoute, ref knotRoute, ref exitExtra))
                    found = true;
            }

            return found;
        }

        private bool ConsiderTurnoutAlong(
            RailRoute route,
            float routeDistance,
            RailwayTopology.TurnoutData t,
            RailwayJunction junction,
            bool travelIncreasesRoute,
            ref float bestKnotRoute,
            ref float exitExtra)
        {
            if (!TryGetRouteDistanceForKnot(route, t.StemIn, out float kr))
                return false;

            if (travelIncreasesRoute)
            {
                if (kr <= routeDistance + KnotEpsilon)
                    return false;
                if (kr >= bestKnotRoute)
                    return false;
            }
            else
            {
                if (kr >= routeDistance - KnotEpsilon)
                    return false;
                if (kr <= bestKnotRoute)
                    return false;
            }

            TryGetDiverging(t, junction, out bool diverging);
            exitExtra = RailwayTopology.TryBuildExitFromApproachKnot(track, t, travelIncreasesRoute, diverging, out RailSpan next)
                ? next.Length
                : 0f;
            bestKnotRoute = kr;
            return true;
        }

        private float ProbeBeyondSpanEnd(RailSpan span, bool forwardAlongRoute)
        {
            if (TryFindTurnoutAtSpanEnd(span, forwardAlongRoute, out RailwayTopology.TurnoutData turnout, out RailwayJunction junction))
            {
                TryGetDiverging(turnout, junction, out bool diverging);
                if (RailwayTopology.TryBuildExitSpan(track, span, forwardAlongRoute, diverging, turnout, out RailSpan next))
                    return next.Length;
                return 0f;
            }

            foreach (RailwayTopology.JoinData join in _joins)
            {
                if (RailwayTopology.TryBuildJoinSpan(track, join, span, forwardAlongRoute, out RailSpan joinNext))
                    return joinNext.Length;
            }

            return 0f;
        }

        public float ProcessJunctionCrossings(RailRoute route, float previousRoute, float newRoute, int facing, float velocity)
        {
            if (route == null || track == null || Mathf.Abs(velocity) < 0.001f)
                return newRoute;

            bool travelIncreasesRoute = newRoute >= previousRoute;
            float lo = Mathf.Min(previousRoute, newRoute);
            float hi = Mathf.Max(previousRoute, newRoute);

            foreach (RailwayJunction j in _junctions)
            {
                if (j == null || !j.TryGetTurnout(out RailwayTopology.TurnoutData t))
                    continue;
                newRoute = ProcessOneTurnout(route, t, j, lo, hi, newRoute, travelIncreasesRoute);
            }

            foreach (RailwayTopology.TurnoutData t in _implicitTurnouts)
                newRoute = ProcessOneTurnout(route, t, null, lo, hi, newRoute, travelIncreasesRoute);

            if (travelIncreasesRoute && newRoute > route.TotalLength - KnotEpsilon)
                TryAppendAtSpanEnd(route, true);
            else if (!travelIncreasesRoute && newRoute < KnotEpsilon)
                TryAppendAtSpanEnd(route, false);

            if (newRoute < 0f)
                newRoute = 0f;

            return newRoute;
        }

        private float ProcessOneTurnout(
            RailRoute route,
            RailwayTopology.TurnoutData t,
            RailwayJunction junction,
            float lo,
            float hi,
            float newRoute,
            bool forwardAlongRoute)
        {
            if (!TryGetRouteDistanceForKnot(route, t.StemIn, out float knotRoute))
                return newRoute;
            if (knotRoute < lo - KnotEpsilon || knotRoute > hi + KnotEpsilon)
                return newRoute;

            if (TryGetDiverging(t, junction, out bool diverging) &&
                RailwayTopology.TryBuildExitFromApproachKnot(track, t, forwardAlongRoute, diverging, out RailSpan next))
            {
                if (!RouteAlreadyContains(route, next))
                {
                    route.TryTruncateAtRouteDistance(knotRoute);
                    route.AppendSpan(next);
                }

                return newRoute;
            }

            if (forwardAlongRoute)
            {
                float stop = knotRoute - KnotEpsilon;
                if (newRoute > stop)
                    return stop;
            }
            else
            {
                float stop = knotRoute + KnotEpsilon;
                if (newRoute < stop)
                    return stop;
            }

            return newRoute;
        }

        private bool TryAppendAtSpanEnd(RailRoute route, bool forwardAlongRoute)
        {
            if (route == null || track == null || route.Spans.Count == 0)
                return false;

            RailSpan last = route.Spans[route.Spans.Count - 1];
            if (TryFindTurnoutAtSpanEnd(last, forwardAlongRoute, out RailwayTopology.TurnoutData turnout, out RailwayJunction junction))
            {
                TryGetDiverging(turnout, junction, out bool diverging);
                if (junction != null && junction.BlocksLeverToggle())
                    return false;

                if (!RailwayTopology.TryBuildExitSpan(track, last, forwardAlongRoute, diverging, turnout, out RailSpan next))
                    return false;
                if (RouteAlreadyContains(route, next))
                    return false;
                return route.AppendSpan(next);
            }

            foreach (RailwayTopology.JoinData join in _joins)
            {
                if (!RailwayTopology.TryBuildJoinSpan(track, join, last, forwardAlongRoute, out RailSpan joinNext))
                    continue;
                if (RouteAlreadyContains(route, joinNext))
                    return false;
                return route.AppendSpan(joinNext);
            }

            return false;
        }

        private static bool RouteAlreadyContains(RailRoute route, RailSpan candidate)
        {
            for (int i = 0; i < route.Spans.Count; i++)
            {
                RailSpan s = route.Spans[i];
                if (s.SplineIndex == candidate.SplineIndex &&
                    Mathf.Abs(s.SplineStart - candidate.SplineStart) < 0.05f &&
                    Mathf.Abs(s.SplineEnd - candidate.SplineEnd) < 0.05f &&
                    s.Sign == candidate.Sign)
                    return true;
            }
            return false;
        }

        private bool TryFindTurnoutAtSpanEnd(RailSpan span, bool forwardAlongRoute, out RailwayTopology.TurnoutData turnout, out RailwayJunction junction)
        {
            turnout = default;
            junction = null;

            foreach (RailwayJunction j in _junctions)
            {
                if (j != null && j.TryGetTurnout(out RailwayTopology.TurnoutData t) && j.SpanEndMatches(track, span, forwardAlongRoute, t))
                {
                    turnout = t;
                    junction = j;
                    return true;
                }
            }

            foreach (RailwayTopology.TurnoutData t in _implicitTurnouts)
            {
                if (SpanEndMatchesTopology(track, span, forwardAlongRoute, t))
                {
                    turnout = t;
                    return true;
                }
            }

            return false;
        }

        public bool TryGetRouteDistanceForKnot(RailRoute route, SplineKnotIndex knot, out float routeDistance)
        {
            routeDistance = 0f;
            if (route == null || track == null)
                return false;

            float knotD = RailwayTopology.KnotDistance(track, knot);
            float cursor = 0f;
            for (int i = 0; i < route.Spans.Count; i++)
            {
                RailSpan span = route.Spans[i];
                if (span.SplineIndex != knot.Spline)
                {
                    cursor += span.Length;
                    continue;
                }

                float min = Mathf.Min(span.SplineStart, span.SplineEnd);
                float max = Mathf.Max(span.SplineStart, span.SplineEnd);
                if (knotD < min - 0.25f || knotD > max + 0.25f)
                {
                    cursor += span.Length;
                    continue;
                }

                float offsetInSpan = span.Sign > 0 ? knotD - span.SplineStart : span.SplineStart - knotD;
                routeDistance = cursor + Mathf.Clamp(offsetInSpan, 0f, span.Length);
                return true;
            }

            return false;
        }

        /// <summary>Called when a lever changes so consists waiting at a knot can use the new exit span.</summary>
        public void NotifyTurnoutStateChanged(RailwayJunction junction)
        {
            if (junction == null || !junction.TryGetTurnout(out RailwayTopology.TurnoutData t))
                return;

            TrainSplineDriver[] drivers = FindObjectsByType<TrainSplineDriver>(FindObjectsInactive.Exclude);
            foreach (TrainSplineDriver driver in drivers)
            {
                if (driver == null || !driver.isActiveAndEnabled || driver.Route == null)
                    continue;
                if (!TryGetRouteDistanceForKnot(driver.Route, t.StemIn, out float knotRoute))
                    continue;

                float min = driver.MinDistance;
                float max = driver.MaxDistance;
                if (max < knotRoute - KnotEpsilon || min > knotRoute + KnotEpsilon)
                    continue;

                driver.Rig?.SyncRouteLength();
            }
        }

        /// <summary>Whether any locomotive consist overlaps this junction in route space.</summary>
        public bool IsJunctionOccupied(RailwayJunction junction, float routeBuffer)
        {
            if (junction == null || !junction.TryGetTurnout(out RailwayTopology.TurnoutData t))
                return false;

            TrainSplineDriver[] drivers = FindObjectsByType<TrainSplineDriver>(FindObjectsInactive.Exclude);
            foreach (TrainSplineDriver driver in drivers)
            {
                if (driver == null || driver.Route == null)
                    continue;
                if (!TryGetRouteDistanceForKnot(driver.Route, t.StemIn, out float knotRoute))
                    continue;

                float min = driver.MinDistance;
                float max = driver.MaxDistance;
                float zoneLo = knotRoute - routeBuffer;
                float zoneHi = knotRoute + routeBuffer;
                if (max >= zoneLo && min <= zoneHi)
                    return true;
            }

            return false;
        }

        internal static bool SpanEndMatchesTopology(SplineContainer container, RailSpan span, bool forwardAlongRoute, RailwayTopology.TurnoutData turnout)
        {
            float endSplineD = forwardAlongRoute
                ? (span.Sign > 0 ? span.SplineEnd : span.SplineStart)
                : (span.Sign > 0 ? span.SplineStart : span.SplineEnd);

            float stemInD = RailwayTopology.KnotDistance(container, turnout.StemIn);
            float stemOutD = RailwayTopology.KnotDistance(container, turnout.StemOut);
            float branchD = RailwayTopology.KnotDistance(container, turnout.Branch);

            if (span.SplineIndex == turnout.StemIn.Spline && Mathf.Abs(endSplineD - stemInD) < 0.5f)
                return true;
            if (span.SplineIndex == turnout.StemOut.Spline && Mathf.Abs(endSplineD - stemOutD) < 0.5f)
                return true;
            if (span.SplineIndex == turnout.Branch.Spline && Mathf.Abs(endSplineD - branchD) < 0.5f)
                return true;
            return false;
        }
    }
}
