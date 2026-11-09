using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Splines;

namespace Game.Train
{
    /// <summary>Creates placeholder levers/lamps at turnouts when the scene has no authored junctions.</summary>
    public static class RailwayJunctionRuntimeSetup
    {
        public static void CreateMissingJunctions(RailwayNetwork network, SplineContainer track)
        {
            if (network == null || track == null)
                return;

            var turnouts = new List<RailwayTopology.TurnoutData>();
            var seen = new HashSet<int>();
            RailwayTopology.CollectTurnouts(track, turnouts);
            foreach (RailwayTopology.TurnoutData t in turnouts)
                seen.Add(HashTurnout(t));

            var joins = new List<RailwayTopology.JoinData>();
            RailwayTopology.CollectTwoKnotJoins(track, joins);
            foreach (RailwayTopology.JoinData join in joins)
            {
                if (!RailwayTopology.TryParseSimpleTurnout(track, join, out RailwayTopology.TurnoutData simple))
                    continue;
                if (seen.Add(HashTurnout(simple)))
                    turnouts.Add(simple);
            }

            if (turnouts.Count == 0)
                return;

            Material lampMat = CreateLampMaterial();
            for (int i = 0; i < turnouts.Count; i++)
                CreateJunction(network.transform, track, turnouts[i], i, lampMat);

            RailwaySwitchboard board = network.GetComponent<RailwaySwitchboard>();
            board?.RefreshJunctionList();
        }

        private static int HashTurnout(RailwayTopology.TurnoutData t)
        {
            return t.StemIn.Spline * 10000 + t.StemIn.Knot * 100 + t.Branch.Spline * 10 + t.Branch.Knot;
        }

        private static void CreateJunction(Transform parent, SplineContainer track, RailwayTopology.TurnoutData t, int index, Material lampMat)
        {
            Vector3 knotPos = RailwayTopology.KnotWorldPosition(track, t.StemIn);
            Vector3 mainDir = RailwayTopology.KnotWorldTangent(track, t.StemIn);
            Vector3 side = Vector3.Cross(Vector3.up, mainDir).normalized;

            var go = new GameObject($"Junction_{index}");
            go.transform.SetParent(parent, false);
            go.transform.position = knotPos + side * 2.5f;

            var junction = go.AddComponent<RailwayJunction>();
            Transform lever = CreateLever(go.transform);
            Renderer straight = CreateLamp(go.transform, knotPos + mainDir * 4f + side * 1.2f, "Lamp_Straight", lampMat);
            Renderer branch = CreateLamp(go.transform, knotPos + (RailwayTopology.KnotWorldPosition(track, t.Branch) - knotPos).normalized * 4f + side * 1.2f, "Lamp_Branch", lampMat);

            junction.Configure(track, t, lever, straight, branch);
            junction.ApplyVisuals();
        }

        private static Transform CreateLever(Transform parent)
        {
            var post = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            post.name = "LeverPost";
            post.transform.SetParent(parent, false);
            post.transform.localScale = new Vector3(0.15f, 1f, 0.15f);
            Object.Destroy(post.GetComponent<Collider>());

            var handle = GameObject.CreatePrimitive(PrimitiveType.Cube);
            handle.name = "LeverHandle";
            handle.transform.SetParent(post.transform, false);
            handle.transform.localPosition = new Vector3(0f, 1.1f, 0.35f);
            handle.transform.localScale = new Vector3(0.12f, 0.12f, 0.9f);
            Object.Destroy(handle.GetComponent<Collider>());
            return post.transform;
        }

        private static Renderer CreateLamp(Transform parent, Vector3 worldPos, string name, Material lampMat)
        {
            var lamp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            lamp.name = name;
            lamp.transform.SetParent(parent, true);
            lamp.transform.position = worldPos;
            lamp.transform.localScale = Vector3.one * 0.45f;
            Object.Destroy(lamp.GetComponent<Collider>());
            var renderer = lamp.GetComponent<Renderer>();
            renderer.material = new Material(lampMat);
            return renderer;
        }

        private static Material CreateLampMaterial()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var mat = new Material(shader);
            mat.EnableKeyword("_EMISSION");
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            return mat;
        }
    }
}
