using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Splines;

namespace Game.Train
{
    /// <summary>
    /// Piecewise route over one or more spline spans. Locomotive <see cref="TrainSplineDriver.Distance"/>
    /// is measured from the start of the first span along the combined length.
    /// </summary>
    [Serializable]
    public class RailRoute
    {
        private readonly List<RailSpan> _spans = new List<RailSpan>();
        private SplineContainer _track;

        public IReadOnlyList<RailSpan> Spans => _spans;
        public SplineContainer Track => _track;
        public float TotalLength
        {
            get
            {
                float sum = 0f;
                for (int i = 0; i < _spans.Count; i++)
                    sum += _spans[i].Length;
                return sum;
            }
        }

        public int PrimarySplineIndex => _spans.Count > 0 ? _spans[0].SplineIndex : 0;

        public void Clear()
        {
            _spans.Clear();
            _track = null;
        }

        public void SetTrack(SplineContainer track)
        {
            _track = track;
        }

        public void ResetSingleSpline(SplineContainer track, int splineIndex)
        {
            _spans.Clear();
            _track = track;
            if (track == null || splineIndex < 0 || splineIndex >= track.Splines.Count)
                return;

            float len = track.CalculateLength(splineIndex);
            _spans.Add(new RailSpan(splineIndex, 0f, len, 1));
        }

        public void CopyFrom(RailRoute other)
        {
            _spans.Clear();
            _track = other?._track;
            if (other == null)
                return;
            for (int i = 0; i < other._spans.Count; i++)
                _spans.Add(other._spans[i]);
        }

        public bool TryMapRouteDistance(float routeDistance, out int splineIndex, out float splineDistance)
        {
            splineIndex = 0;
            splineDistance = 0f;
            if (_track == null || _spans.Count == 0)
                return false;

            float d = Mathf.Clamp(routeDistance, 0f, TotalLength);
            float cursor = 0f;
            for (int i = 0; i < _spans.Count; i++)
            {
                RailSpan span = _spans[i];
                float spanLen = span.Length;
                if (d <= cursor + spanLen || i == _spans.Count - 1)
                {
                    splineIndex = span.SplineIndex;
                    splineDistance = span.SplineDistanceAtRouteOffset(d - cursor);
                    return true;
                }
                cursor += spanLen;
            }

            return false;
        }

        public bool AppendSpan(RailSpan span)
        {
            if (_track == null)
                return false;
            if (span.Length <= 1e-4f)
                return false;
            _spans.Add(span);
            return true;
        }

        /// <summary>Shortens the span that contains <paramref name="routeDistance"/> so it ends at that point.</summary>
        public bool TryTruncateAtRouteDistance(float routeDistance)
        {
            if (_track == null || _spans.Count == 0)
                return false;

            float d = Mathf.Clamp(routeDistance, 0f, TotalLength);
            float cursor = 0f;
            for (int i = 0; i < _spans.Count; i++)
            {
                RailSpan span = _spans[i];
                float spanLen = span.Length;
                if (d > cursor + spanLen + 1e-4f)
                {
                    cursor += spanLen;
                    continue;
                }

                float offset = d - cursor;
                float splineEnd = span.SplineDistanceAtRouteOffset(offset);
                _spans[i] = new RailSpan(span.SplineIndex, span.SplineStart, splineEnd, span.Sign);
                while (_spans.Count > i + 1)
                    _spans.RemoveAt(_spans.Count - 1);
                return true;
            }

            return false;
        }

        public bool TryGetSpanIndexAtRouteDistance(float routeDistance, out int spanIndex, out float offsetInSpan)
        {
            spanIndex = -1;
            offsetInSpan = 0f;
            if (_spans.Count == 0)
                return false;

            float d = Mathf.Clamp(routeDistance, 0f, TotalLength);
            float cursor = 0f;
            for (int i = 0; i < _spans.Count; i++)
            {
                float spanLen = _spans[i].Length;
                if (d <= cursor + spanLen || i == _spans.Count - 1)
                {
                    spanIndex = i;
                    offsetInSpan = d - cursor;
                    return true;
                }
                cursor += spanLen;
            }

            return false;
        }
    }
}
