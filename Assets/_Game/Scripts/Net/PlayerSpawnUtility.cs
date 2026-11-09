using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
using UnityEngine.AI;

namespace Game.Net
{
    /// <summary>
    /// Owner-side random spawn within PlayerSpawnArea (or fallback center).
    /// </summary>
    public static class PlayerSpawnUtility
    {
        public static readonly Vector3 FallbackCenter = new Vector3(59.3f, 0.1f, 102.3f);

        public static Vector3 FindSpawnPosition(Vector3? excludeSelf = null)
        {
            PlayerSpawnArea area = PlayerSpawnArea.Instance;
            Vector3 center = area != null ? area.Center : FallbackCenter;
            float radius = area != null ? area.Radius : 50f;
            float minSep = area != null ? area.MinSeparation : 5f;

            Vector3 best = center;
            float bestMinDist = -1f;

            for (int attempt = 0; attempt < 30; attempt++)
            {
                Vector2 circle = Random.insideUnitCircle * radius;
                Vector3 candidate = center + new Vector3(circle.x, 0f, circle.y);

                if (!TrySnapToGround(candidate, out Vector3 snapped))
                    continue;

                float nearest = NearestPlayerDistance(snapped, excludeSelf);
                if (nearest >= minSep)
                    return snapped;

                if (nearest > bestMinDist)
                {
                    bestMinDist = nearest;
                    best = snapped;
                }
            }

            return best;
        }

        public static void TeleportOwner(NetworkObject netObj, Vector3 position)
        {
            if (netObj == null || !netObj.IsOwner)
                return;

            Transform t = netObj.transform;
            t.position = position;

            var rb = netObj.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                rb.position = position;
            }

            var nt = netObj.GetComponent<NetworkTransform>();
            if (nt != null)
                nt.Teleport(position, t.rotation, t.localScale);
        }

        private static bool TrySnapToGround(Vector3 candidate, out Vector3 snapped)
        {
            snapped = candidate;

            if (NavMesh.SamplePosition(candidate, out NavMeshHit navHit, 4f, NavMesh.AllAreas))
            {
                snapped = navHit.position;
                return true;
            }

            if (Physics.Raycast(candidate + Vector3.up * 50f, Vector3.down, out RaycastHit hit, 100f))
            {
                snapped = hit.point + Vector3.up * 0.05f;
                return true;
            }

            return false;
        }

        private static float NearestPlayerDistance(Vector3 point, Vector3? excludeSelf)
        {
            float best = float.MaxValue;
            if (NetworkPlayerRegistry.Instance == null)
                return best;

            var players = NetworkPlayerRegistry.Instance.Players;
            for (int i = 0; i < players.Count; i++)
            {
                NetworkPlayerSetup p = players[i];
                if (p == null)
                    continue;

                Vector3 pos = p.transform.position;
                if (excludeSelf.HasValue && (pos - excludeSelf.Value).sqrMagnitude < 0.01f)
                    continue;

                float d = Vector3.Distance(point, pos);
                if (d < best)
                    best = d;
            }

            return best;
        }
    }
}
