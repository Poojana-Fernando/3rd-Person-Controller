using UnityEngine;


namespace DeadAuthorGames.ThirdPersonCombat
{
    public class ZombiePatrolPath : MonoBehaviour
    {
        [SerializeField] private Transform[] waypoints;

        public int Count => waypoints != null ? waypoints.Length : 0;

        public Vector3 GetWaypointPosition(int index)
        {
            if (waypoints == null || waypoints.Length == 0)
                return transform.position;

            int safeIndex = Mathf.Clamp(index, 0, waypoints.Length - 1);
            Transform point = waypoints[safeIndex];
            return point != null ? point.position : transform.position;
        }

        public int GetNextIndex(ref int currentIndex)
        {
            if (waypoints == null || waypoints.Length == 0)
                return 0;

            currentIndex = (currentIndex + 1) % waypoints.Length;
            return currentIndex;
        }

        private void OnDrawGizmos()
        {
            if (waypoints == null || waypoints.Length == 0)
                return;

            Gizmos.color = new Color(0.2f, 0.9f, 0.3f, 0.85f);
            for (int i = 0; i < waypoints.Length; i++)
            {
                Transform point = waypoints[i];
                if (point == null)
                    continue;

                Gizmos.DrawSphere(point.position, 0.2f);
                int next = (i + 1) % waypoints.Length;
                if (waypoints[next] != null)
                    Gizmos.DrawLine(point.position, waypoints[next].position);
            }
        }
    }
}
