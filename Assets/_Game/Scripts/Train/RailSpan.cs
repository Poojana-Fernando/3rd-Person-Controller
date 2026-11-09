using System;
using UnityEngine;

namespace Game.Train
{
    /// <summary>One continuous run along a single spline between two arc-length bounds.</summary>
    [Serializable]
    public struct RailSpan
    {
        public int SplineIndex;
        /// <summary>Spline arc-length at the start of this span (m).</summary>
        public float SplineStart;
        /// <summary>Spline arc-length at the end of this span (m).</summary>
        public float SplineEnd;
        /// <summary>+1 when route distance increases with spline distance, -1 when it decreases.</summary>
        public int Sign;

        public float Length => Mathf.Abs(SplineEnd - SplineStart);

        public RailSpan(int splineIndex, float splineStart, float splineEnd, int sign)
        {
            SplineIndex = splineIndex;
            SplineStart = splineStart;
            SplineEnd = splineEnd;
            Sign = sign >= 0 ? 1 : -1;
        }

        public float SplineDistanceAtRouteOffset(float routeOffsetInSpan)
        {
            float t = Mathf.Clamp01(routeOffsetInSpan / Mathf.Max(1e-5f, Length));
            return Mathf.Lerp(SplineStart, SplineEnd, Sign > 0 ? t : 1f - t);
        }
    }
}
