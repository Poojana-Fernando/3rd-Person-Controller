using System;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Splines;

namespace Game.Train
{
    public struct RailSpanNet : INetworkSerializable, IEquatable<RailSpanNet>
    {
        public int SplineIndex;
        public float SplineStart;
        public float SplineEnd;
        public int Sign;

        public RailSpanNet(RailSpan span)
        {
            SplineIndex = span.SplineIndex;
            SplineStart = span.SplineStart;
            SplineEnd = span.SplineEnd;
            Sign = span.Sign;
        }

        public RailSpan ToSpan() => new RailSpan(SplineIndex, SplineStart, SplineEnd, Sign);

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref SplineIndex);
            serializer.SerializeValue(ref SplineStart);
            serializer.SerializeValue(ref SplineEnd);
            serializer.SerializeValue(ref Sign);
        }

        public bool Equals(RailSpanNet other)
        {
            return SplineIndex == other.SplineIndex &&
                   Mathf.Approximately(SplineStart, other.SplineStart) &&
                   Mathf.Approximately(SplineEnd, other.SplineEnd) &&
                   Sign == other.Sign;
        }
    }

    public static class RailRouteNet
    {
        public static void WriteList(NetworkList<RailSpanNet> list, RailRoute route)
        {
            if (list == null || route == null)
                return;
            list.Clear();
            for (int i = 0; i < route.Spans.Count; i++)
                list.Add(new RailSpanNet(route.Spans[i]));
        }

        public static void ApplyList(RailRoute route, NetworkList<RailSpanNet> list, SplineContainer track)
        {
            if (route == null || list == null || track == null)
                return;
            route.Clear();
            route.SetTrack(track);
            for (int i = 0; i < list.Count; i++)
                route.AppendSpan(list[i].ToSpan());
            if (route.Spans.Count == 0)
                route.ResetSingleSpline(track, 0);
        }
    }
}
