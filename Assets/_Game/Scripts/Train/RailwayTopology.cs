using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Splines;

namespace Game.Train
{
    /// <summary>Builds turnout / join data from a <see cref="SplineContainer"/> knot-link graph.</summary>
    public static class RailwayTopology
    {
        public struct TurnoutData
        {
            public SplineKnotIndex StemIn;
            public SplineKnotIndex StemOut;
            public SplineKnotIndex Branch;
            public Vector3 WorldPosition;
        }

        public struct JoinData
        {
            public SplineKnotIndex A;
            public SplineKnotIndex B;
        }

        public static float KnotDistance(SplineContainer container, SplineKnotIndex knot)
        {
            if (container == null || knot.Spline >= container.Splines.Count)
                return 0f;
            Spline spline = container.Splines[knot.Spline];
            if (knot.Knot < 0 || knot.Knot >= spline.Count)
                return 0f;
            return spline.ConvertIndexUnit(knot.Knot, PathIndexUnit.Knot, PathIndexUnit.Distance);
        }

        public static bool IsValidKnot(SplineContainer container, SplineKnotIndex knot)
        {
            if (container == null || knot.Spline < 0 || knot.Spline >= container.Splines.Count)
                return false;
            Spline spline = container.Splines[knot.Spline];
            return knot.Knot >= 0 && knot.Knot < spline.Count;
        }

        /// <summary>Find the knot on <paramref name="targetSpline"/> linked to <paramref name="from"/> in the knot graph.</summary>
        public static bool KnotsShareLink(SplineContainer container, SplineKnotIndex a, SplineKnotIndex b)
        {
            if (container == null || !IsValidKnot(container, a) || !IsValidKnot(container, b))
                return false;
            if (!container.KnotLinkCollection.TryGetKnotLinks(a, out IReadOnlyList<SplineKnotIndex> cluster))
                return false;
            for (int i = 0; i < cluster.Count; i++)
            {
                if (cluster[i].Spline == b.Spline && cluster[i].Knot == b.Knot)
                    return true;
            }
            return false;
        }

        public static bool TryFindLinkedKnot(
            SplineContainer container,
            SplineKnotIndex from,
            int targetSpline,
            out SplineKnotIndex linked)
        {
            return TryFindLinkedKnot(container, from, targetSpline, default, out linked);
        }

        public static bool TryFindLinkedKnot(
            SplineContainer container,
            SplineKnotIndex from,
            int targetSpline,
            SplineKnotIndex exclude,
            out SplineKnotIndex linked)
        {
            linked = default;
            if (container == null || !IsValidKnot(container, from))
                return false;
            if (!container.KnotLinkCollection.TryGetKnotLinks(from, out IReadOnlyList<SplineKnotIndex> cluster))
                return false;

            Vector3 fromWorld = KnotWorldPosition(container, from);
            float bestDist = float.MaxValue;
            bool found = false;
            for (int i = 0; i < cluster.Count; i++)
            {
                SplineKnotIndex candidate = cluster[i];
                if (candidate.Spline != targetSpline)
                    continue;
                if (exclude.Spline == candidate.Spline && exclude.Knot == candidate.Knot)
                    continue;
                if (!IsValidKnot(container, candidate))
                    continue;

                float dist = (KnotWorldPosition(container, candidate) - fromWorld).sqrMagnitude;
                if (dist < bestDist)
                {
                    bestDist = dist;
                    linked = candidate;
                    found = true;
                }
            }

            return found;
        }

        public static bool TryResolveKnotOnSpline(
            SplineContainer container,
            SplineKnotIndex knot,
            SplineKnotIndex anchor,
            out SplineKnotIndex resolved,
            out float distance)
        {
            return TryResolveKnotOnSpline(container, knot, anchor, default, out resolved, out distance);
        }

        public static bool TryResolveKnotOnSpline(
            SplineContainer container,
            SplineKnotIndex knot,
            SplineKnotIndex anchor,
            SplineKnotIndex exclude,
            out SplineKnotIndex resolved,
            out float distance)
        {
            resolved = knot;
            distance = 0f;
            if (container == null)
                return false;

            if (IsValidKnot(container, knot) && KnotsShareLink(container, anchor, knot))
            {
                Vector3 anchorWorld = KnotWorldPosition(container, anchor);
                Vector3 knotWorld = KnotWorldPosition(container, knot);
                if ((anchorWorld - knotWorld).sqrMagnitude < 0.25f)
                {
                    distance = KnotDistance(container, knot);
                    return true;
                }
            }

            if (TryFindLinkedKnot(container, anchor, knot.Spline, exclude, out SplineKnotIndex linked))
            {
                resolved = linked;
                distance = KnotDistance(container, linked);
                return true;
            }

            if (IsValidKnot(container, knot))
            {
                distance = KnotDistance(container, knot);
                return true;
            }

            return false;
        }

        public static Vector3 KnotWorldTangent(SplineContainer container, SplineKnotIndex knot)
        {
            if (container == null || knot.Spline >= container.Splines.Count)
                return Vector3.forward;
            float len = container.CalculateLength(knot.Spline);
            float d = KnotDistance(container, knot);
            float t = len > 1e-5f ? d / len : 0f;
            container.Evaluate(knot.Spline, t, out _, out float3 tan, out _);
            Vector3 world = container.transform.TransformDirection(tan);
            return world.sqrMagnitude > 1e-6f ? world.normalized : container.transform.forward;
        }

        public static Vector3 KnotWorldPosition(SplineContainer container, SplineKnotIndex knot)
        {
            if (container == null || knot.Spline >= container.Splines.Count)
                return Vector3.zero;
            Spline spline = container.Splines[knot.Spline];
            if (knot.Knot < 0 || knot.Knot >= spline.Count)
                return Vector3.zero;
            float3 local = spline[knot.Knot].Position;
            return container.transform.TransformPoint(local);
        }

        public static void CollectTurnouts(SplineContainer container, List<TurnoutData> turnouts)
        {
            turnouts.Clear();
            if (container == null)
                return;

            var seen = new HashSet<int>();
            var visited = new HashSet<SplineKnotIndex>();
            for (int s = 0; s < container.Splines.Count; s++)
            {
                Spline spline = container.Splines[s];
                for (int k = 0; k < spline.Count; k++)
                {
                    var idx = new SplineKnotIndex(s, k);
                    if (visited.Contains(idx))
                        continue;
                    if (!container.KnotLinkCollection.TryGetKnotLinks(idx, out IReadOnlyList<SplineKnotIndex> cluster))
                        continue;
                    if (cluster.Count < 3)
                        continue;

                    int hash = ClusterHash(cluster);
                    if (seen.Contains(hash))
                        continue;
                    seen.Add(hash);

                    if (TryParseTurnout(container, cluster, out TurnoutData turnout))
                        turnouts.Add(turnout);

                    for (int i = 0; i < cluster.Count; i++)
                        visited.Add(cluster[i]);
                }
            }
        }

        public static void CollectTwoKnotJoins(SplineContainer container, List<JoinData> joins)
        {
            joins.Clear();
            if (container == null)
                return;

            var seen = new HashSet<int>();
            var visited = new HashSet<SplineKnotIndex>();
            for (int s = 0; s < container.Splines.Count; s++)
            {
                Spline spline = container.Splines[s];
                for (int k = 0; k < spline.Count; k++)
                {
                    var idx = new SplineKnotIndex(s, k);
                    if (visited.Contains(idx))
                        continue;
                    if (!container.KnotLinkCollection.TryGetKnotLinks(idx, out IReadOnlyList<SplineKnotIndex> cluster))
                        continue;
                    visited.Add(idx);
                    if (cluster.Count != 2)
                    {
                        for (int i = 0; i < cluster.Count; i++)
                            visited.Add(cluster[i]);
                        continue;
                    }

                    int hash = ClusterHash(cluster);
                    if (seen.Contains(hash))
                        continue;
                    seen.Add(hash);

                    joins.Add(new JoinData { A = cluster[0], B = cluster[1] });
                    visited.Add(cluster[1]);
                }
            }
        }

        private static int ClusterHash(IReadOnlyList<SplineKnotIndex> cluster)
        {
            int h = 17;
            for (int i = 0; i < cluster.Count; i++)
            {
                h = h * 31 + cluster[i].Spline;
                h = h * 31 + cluster[i].Knot;
            }
            return h;
        }

        private static bool TryParseTurnout(SplineContainer container, IReadOnlyList<SplineKnotIndex> cluster, out TurnoutData turnout)
        {
            turnout = default;
            var bySpline = new Dictionary<int, List<SplineKnotIndex>>();
            for (int i = 0; i < cluster.Count; i++)
            {
                SplineKnotIndex k = cluster[i];
                if (!bySpline.TryGetValue(k.Spline, out List<SplineKnotIndex> list))
                {
                    list = new List<SplineKnotIndex>();
                    bySpline[k.Spline] = list;
                }
                list.Add(k);
            }

            int mainSpline = -1;
            foreach (var pair in bySpline)
            {
                if (pair.Value.Count >= 2)
                {
                    mainSpline = pair.Key;
                    break;
                }
            }

            if (mainSpline < 0)
                return false;

            List<SplineKnotIndex> mainKnots = bySpline[mainSpline];
            if (mainKnots.Count < 2)
                return false;

            SplineKnotIndex branch = default;
            bool hasBranch = false;
            foreach (var pair in bySpline)
            {
                if (pair.Key == mainSpline)
                    continue;
                if (pair.Value.Count >= 1)
                {
                    branch = pair.Value[0];
                    hasBranch = true;
                    break;
                }
            }

            if (!hasBranch)
                return false;

            float d0 = KnotDistance(container, mainKnots[0]);
            float d1 = KnotDistance(container, mainKnots[1]);
            turnout.StemIn = d0 <= d1 ? mainKnots[0] : mainKnots[1];
            turnout.StemOut = d0 <= d1 ? mainKnots[1] : mainKnots[0];
            turnout.Branch = branch;
            turnout.WorldPosition = KnotWorldPosition(container, turnout.StemIn);
            return true;
        }

        /// <summary>
        /// For a 2-knot link: the knot on the spline with more knots is treated as the main line attachment.
        /// </summary>
        public static bool TryParseSimpleTurnout(SplineContainer container, JoinData join, out TurnoutData turnout)
        {
            turnout = default;
            if (container == null)
                return false;

            int countA = container.Splines[join.A.Spline].Count;
            int countB = container.Splines[join.B.Spline].Count;
            SplineKnotIndex mainKnot = countA >= countB ? join.A : join.B;
            SplineKnotIndex branchKnot = countA >= countB ? join.B : join.A;

            turnout.StemIn = mainKnot;
            turnout.StemOut = mainKnot;
            turnout.Branch = branchKnot;
            turnout.WorldPosition = KnotWorldPosition(container, mainKnot);
            return true;
        }

        /// <summary>Build the exit leg when the train reaches a turnout knot mid-span (approach along main).</summary>
        public static bool TryBuildExitFromApproachKnot(
            SplineContainer container,
            TurnoutData turnout,
            bool travelIncreasesRoute,
            bool diverging,
            out RailSpan next)
        {
            next = default;
            if (container == null)
                return false;

            float stemInD = KnotDistance(container, turnout.StemIn);
            float stemOutD = KnotDistance(container, turnout.StemOut);
            bool sameStem = turnout.StemIn.Spline == turnout.StemOut.Spline &&
                            turnout.StemIn.Knot == turnout.StemOut.Knot;

            RailSpan atKnot;
            if (sameStem)
            {
                atKnot = new RailSpan(turnout.StemIn.Spline, stemInD, stemInD, travelIncreasesRoute ? 1 : -1);
            }
            else if (travelIncreasesRoute)
            {
                if (TryBuildExitOntoStemOut(container, turnout, diverging, true, out next))
                    return true;
                atKnot = new RailSpan(turnout.StemIn.Spline, stemInD, stemInD, 1);
            }
            else
            {
                atKnot = new RailSpan(turnout.StemOut.Spline, stemOutD, stemOutD, -1);
            }

            return TryBuildExitSpan(container, atKnot, travelIncreasesRoute, diverging, turnout, out next);
        }

        public static bool TryResolveBranchKnot(
            SplineContainer container,
            SplineKnotIndex stemIn,
            SplineKnotIndex stemOutKnot,
            SplineKnotIndex preferredBranch,
            out SplineKnotIndex branchKnot,
            out float branchD)
        {
            branchKnot = preferredBranch;
            branchD = KnotDistance(container, preferredBranch);
            if (container == null)
                return false;

            if (IsValidKnot(container, preferredBranch) &&
                KnotsShareLink(container, stemIn, preferredBranch) &&
                !(preferredBranch.Spline == stemOutKnot.Spline && preferredBranch.Knot == stemOutKnot.Knot))
            {
                branchKnot = preferredBranch;
                branchD = KnotDistance(container, preferredBranch);
                return true;
            }

            if (!container.KnotLinkCollection.TryGetKnotLinks(stemIn, out IReadOnlyList<SplineKnotIndex> cluster))
                return IsValidKnot(container, preferredBranch);

            Vector3 stemOutWorld = KnotWorldPosition(container, stemOutKnot);
            float bestDist = -1f;
            bool found = false;
            for (int i = 0; i < cluster.Count; i++)
            {
                SplineKnotIndex candidate = cluster[i];
                if (candidate.Spline != stemOutKnot.Spline)
                    continue;
                if (candidate.Spline == stemOutKnot.Spline && candidate.Knot == stemOutKnot.Knot)
                    continue;
                if (!IsValidKnot(container, candidate))
                    continue;

                float dist = (KnotWorldPosition(container, candidate) - stemOutWorld).sqrMagnitude;
                if (dist > bestDist)
                {
                    bestDist = dist;
                    branchKnot = candidate;
                    branchD = KnotDistance(container, candidate);
                    found = true;
                }
            }

            return found || IsValidKnot(container, preferredBranch);
        }

        public static bool TryBuildExitOntoBranch(
            SplineContainer container,
            TurnoutData turnout,
            bool travelIncreasesRoute,
            out RailSpan next)
        {
            next = default;
            if (container == null)
                return false;

            TryResolveKnotOnSpline(container, turnout.Branch, turnout.StemIn, out SplineKnotIndex branchKnot, out float branchD);
            int branchSpline = branchKnot.Spline;
            float branchLen = container.CalculateLength(branchSpline);
            if (travelIncreasesRoute)
            {
                if (branchD < branchLen * 0.5f)
                    next = new RailSpan(branchSpline, branchD, branchLen, 1);
                else
                    next = new RailSpan(branchSpline, branchD, 0f, -1);
            }
            else
            {
                if (branchD < branchLen * 0.5f)
                    next = new RailSpan(branchSpline, branchD, 0f, -1);
                else
                    next = new RailSpan(branchSpline, branchD, branchLen, 1);
            }

            return next.Length > 0.1f;
        }

        /// <summary>
        /// Exit onto the stem-out spline after approaching on a different spline (linked knot at StemIn / StemOut).
        /// When branch shares the stem-out spline, straight continues away from the branch knot; diverging runs toward it.
        /// </summary>
        public static bool TryBuildExitOntoStemOut(
            SplineContainer container,
            TurnoutData turnout,
            bool diverging,
            bool travelIncreasesRoute,
            out RailSpan next)
        {
            next = default;
            if (container == null)
                return false;

            int stemOutSpline = turnout.StemOut.Spline;
            TryResolveKnotOnSpline(container, turnout.StemOut, turnout.StemIn, out SplineKnotIndex stemOutKnot, out float stemOutD);
            SplineKnotIndex branchKnot = turnout.Branch;
            float branchD = KnotDistance(container, branchKnot);
            if (branchKnot.Spline == stemOutSpline)
                TryResolveBranchKnot(container, turnout.StemIn, stemOutKnot, turnout.Branch, out branchKnot, out branchD);

            float stemOutLen = container.CalculateLength(stemOutSpline);
            bool branchOnStemOut = branchKnot.Spline == stemOutSpline;

            if (branchOnStemOut)
            {
                if (Mathf.Abs(stemOutD - branchD) < 0.05f)
                {
                    if (stemOutKnot.Knot == branchKnot.Knot)
                        return false;
                    return TryBuildExitByKnotOrder(container, stemOutSpline, stemOutKnot, branchKnot, stemOutLen, diverging, out next);
                }

                if (diverging)
                {
                    if (branchD < stemOutD)
                        next = new RailSpan(stemOutSpline, stemOutD, branchD, -1);
                    else
                        next = new RailSpan(stemOutSpline, stemOutD, branchD, 1);
                }
                else if (!TryBuildStraightAwayFromBranch(stemOutSpline, stemOutD, stemOutLen, branchD, out next))
                {
                    return false;
                }

                return next.Length > 0.1f;
            }

            if (diverging)
            {
                int branchSpline = turnout.Branch.Spline;
                float branchLen = container.CalculateLength(branchSpline);
                if (branchD < branchLen * 0.5f)
                    next = new RailSpan(branchSpline, branchD, branchLen, 1);
                else
                    next = new RailSpan(branchSpline, branchD, 0f, -1);
                return next.Length > 0.1f;
            }

            if (travelIncreasesRoute)
            {
                if (stemOutD < stemOutLen * 0.5f)
                    next = new RailSpan(stemOutSpline, stemOutD, stemOutLen, 1);
                else
                    next = new RailSpan(stemOutSpline, stemOutD, 0f, -1);
            }
            else
            {
                if (stemOutD < stemOutLen * 0.5f)
                    next = new RailSpan(stemOutSpline, stemOutD, 0f, -1);
                else
                    next = new RailSpan(stemOutSpline, stemOutD, stemOutLen, 1);
            }

            return next.Length > 0.1f;
        }

        private static bool TryBuildExitByKnotOrder(
            SplineContainer container,
            int splineIndex,
            SplineKnotIndex stemOutKnot,
            SplineKnotIndex branchKnot,
            float stemOutLen,
            bool diverging,
            out RailSpan next)
        {
            next = default;
            float stemOutD = KnotDistance(container, stemOutKnot);
            float branchD = KnotDistance(container, branchKnot);
            bool branchTowardStart = branchKnot.Knot < stemOutKnot.Knot;

            if (diverging)
            {
                if (branchTowardStart)
                    next = new RailSpan(splineIndex, stemOutD, branchD, -1);
                else
                    next = new RailSpan(splineIndex, stemOutD, branchD, 1);
            }
            else
            {
                return TryBuildStraightAwayFromBranch(splineIndex, stemOutD, stemOutLen, branchD, out next)
                    || (branchTowardStart
                        ? TryAssignSpan(splineIndex, stemOutD, stemOutLen, 1, out next)
                        : TryAssignSpan(splineIndex, stemOutD, 0f, -1, out next));
            }

            return next.Length > 0.1f;
        }

        private static bool TryAssignSpan(int splineIndex, float from, float to, int sign, out RailSpan next)
        {
            next = new RailSpan(splineIndex, from, to, sign);
            return next.Length > 0.1f;
        }

        private static bool TryBuildStraightAwayFromBranch(
            int splineIndex,
            float stemOutD,
            float stemOutLen,
            float branchD,
            out RailSpan next)
        {
            next = default;
            float towardStart = stemOutD;
            float towardEnd = stemOutLen - stemOutD;
            bool branchTowardStart = branchD < stemOutD;

            if (branchTowardStart)
            {
                if (towardEnd > 0.1f)
                    next = new RailSpan(splineIndex, stemOutD, stemOutLen, 1);
                else if (towardStart > 0.1f)
                    next = new RailSpan(splineIndex, stemOutD, 0f, -1);
            }
            else
            {
                if (towardStart > 0.1f)
                    next = new RailSpan(splineIndex, stemOutD, 0f, -1);
                else if (towardEnd > 0.1f)
                    next = new RailSpan(splineIndex, stemOutD, stemOutLen, 1);
            }

            return next.Length > 0.1f;
        }

        /// <summary>
        /// Next span when leaving the current span end toward increasing route distance.
        /// </summary>
        public static bool TryBuildExitSpan(
            SplineContainer container,
            RailSpan currentSpan,
            bool travelIncreasesRoute,
            bool diverging,
            TurnoutData turnout,
            out RailSpan next)
        {
            next = default;
            if (container == null)
                return false;

            float endSplineD = travelIncreasesRoute
                ? (currentSpan.Sign > 0 ? currentSpan.SplineEnd : currentSpan.SplineStart)
                : (currentSpan.Sign > 0 ? currentSpan.SplineStart : currentSpan.SplineEnd);

            int endSpline = currentSpan.SplineIndex;
            float stemInD = KnotDistance(container, turnout.StemIn);
            float stemOutD = KnotDistance(container, turnout.StemOut);
            float branchD = KnotDistance(container, turnout.Branch);

            bool atStemIn = endSpline == turnout.StemIn.Spline && Mathf.Abs(endSplineD - stemInD) < 0.5f;
            bool atStemOut = endSpline == turnout.StemOut.Spline && Mathf.Abs(endSplineD - stemOutD) < 0.5f;
            bool atBranch = endSpline == turnout.Branch.Spline && Mathf.Abs(endSplineD - branchD) < 0.5f;

            if (!atStemIn && !atStemOut && !atBranch)
                return false;

            if (atStemIn && turnout.StemIn.Spline != turnout.StemOut.Spline)
                return TryBuildExitOntoStemOut(container, turnout, diverging, travelIncreasesRoute, out next);

            if (atStemIn && diverging && turnout.Branch.Spline != turnout.StemIn.Spline &&
                TryBuildExitOntoBranch(container, turnout, travelIncreasesRoute, out next))
            {
                return true;
            }

            if (diverging)
            {
                if (atBranch)
                    return false;
                int branchSpline = turnout.Branch.Spline;
                float branchLen = container.CalculateLength(branchSpline);
                if (endSpline != turnout.Branch.Spline)
                {
                    float entry = branchD;
                    if (entry < branchLen * 0.5f)
                        next = new RailSpan(branchSpline, entry, branchLen, 1);
                    else
                        next = new RailSpan(branchSpline, entry, 0f, -1);
                }
                else
                {
                    next = new RailSpan(branchSpline, branchD, branchLen, currentSpan.Sign);
                }
                return next.Length > 0.1f;
            }

            // Straight
            if (atBranch)
                return false;

            bool sameStem = turnout.StemIn.Spline == turnout.StemOut.Spline &&
                            turnout.StemIn.Knot == turnout.StemOut.Knot;

            if (sameStem && atStemIn)
            {
                int s = turnout.StemIn.Spline;
                float len = container.CalculateLength(s);
                if (travelIncreasesRoute)
                {
                    if (stemInD < len - 0.05f)
                        next = new RailSpan(s, stemInD, len, 1);
                    else if (stemInD > 0.05f)
                        next = new RailSpan(s, stemInD, 0f, -1);
                }
                else
                {
                    if (stemInD > 0.05f)
                        next = new RailSpan(s, stemInD, 0f, -1);
                    else if (stemInD < len - 0.05f)
                        next = new RailSpan(s, stemInD, len, 1);
                }
                return next.Length > 0.1f;
            }

            if (atStemIn && turnout.StemIn.Spline == turnout.StemOut.Spline)
            {
                int s = turnout.StemIn.Spline;
                float len = container.CalculateLength(s);
                if (travelIncreasesRoute)
                    next = new RailSpan(s, stemInD, len, 1);
                else
                    next = new RailSpan(s, stemInD, 0f, -1);
                return next.Length > 0.1f;
            }

            if (atStemOut && turnout.StemIn.Spline == turnout.StemOut.Spline)
            {
                int s = turnout.StemOut.Spline;
                if (travelIncreasesRoute)
                    next = new RailSpan(s, stemOutD, container.CalculateLength(s), 1);
                else
                    next = new RailSpan(s, stemOutD, 0f, -1);
                return next.Length > 0.1f;
            }

            return false;
        }

        public static bool TryBuildJoinSpan(SplineContainer container, JoinData join, RailSpan currentSpan, bool travelIncreasesRoute, out RailSpan next)
        {
            next = default;
            float endSplineD = travelIncreasesRoute
                ? (currentSpan.Sign > 0 ? currentSpan.SplineEnd : currentSpan.SplineStart)
                : (currentSpan.Sign > 0 ? currentSpan.SplineStart : currentSpan.SplineEnd);

            SplineKnotIndex hit = default;
            bool matched = false;
            float dA = KnotDistance(container, join.A);
            float dB = KnotDistance(container, join.B);
            if (currentSpan.SplineIndex == join.A.Spline && Mathf.Abs(endSplineD - dA) < 0.5f)
            {
                hit = join.A;
                matched = true;
            }
            else if (currentSpan.SplineIndex == join.B.Spline && Mathf.Abs(endSplineD - dB) < 0.5f)
            {
                hit = join.B;
                matched = true;
            }

            if (!matched)
                return false;

            SplineKnotIndex other = hit.Equals(join.A) ? join.B : join.A;
            float otherD = KnotDistance(container, other);
            float otherLen = container.CalculateLength(other.Spline);
            if (otherD < otherLen * 0.5f)
                next = new RailSpan(other.Spline, otherD, otherLen, 1);
            else
                next = new RailSpan(other.Spline, otherD, 0f, -1);
            return next.Length > 0.1f;
        }

        /// <summary>
        /// Links knots that share the same world position (within <paramref name="maxDistance"/>).
        /// Required for turnout detection when spline knot links were not saved in the scene.
        /// </summary>
        public static int RebuildCoincidentKnotLinks(SplineContainer container, float maxDistance = 0.25f)
        {
            if (container == null)
                return 0;

            container.KnotLinkCollection.Clear();
            float maxDistSq = maxDistance * maxDistance;
            var knots = new List<(SplineKnotIndex index, Vector3 world)>();

            for (int s = 0; s < container.Splines.Count; s++)
            {
                Spline spline = container.Splines[s];
                for (int k = 0; k < spline.Count; k++)
                {
                    var idx = new SplineKnotIndex(s, k);
                    knots.Add((idx, KnotWorldPosition(container, idx)));
                }
            }

            int linkOps = 0;
            for (int i = 0; i < knots.Count; i++)
            {
                for (int j = i + 1; j < knots.Count; j++)
                {
                    if ((knots[i].world - knots[j].world).sqrMagnitude > maxDistSq)
                        continue;
                    container.KnotLinkCollection.Link(knots[i].index, knots[j].index);
                    linkOps++;
                }
            }

            return linkOps;
        }

        public static int CountTurnouts(SplineContainer container)
        {
            if (container == null)
                return 0;

            var turnouts = new List<TurnoutData>();
            var seen = new HashSet<int>();
            CollectTurnouts(container, turnouts);
            foreach (TurnoutData t in turnouts)
                seen.Add(t.StemIn.Spline * 10000 + t.StemIn.Knot * 100 + t.Branch.Spline * 10 + t.Branch.Knot);

            var joins = new List<JoinData>();
            CollectTwoKnotJoins(container, joins);
            foreach (JoinData join in joins)
            {
                if (!TryParseSimpleTurnout(container, join, out TurnoutData simple))
                    continue;
                int h = simple.StemIn.Spline * 10000 + simple.StemIn.Knot * 100 + simple.Branch.Spline * 10 + simple.Branch.Knot;
                if (seen.Add(h))
                    turnouts.Add(simple);
            }

            return turnouts.Count;
        }
    }
}
