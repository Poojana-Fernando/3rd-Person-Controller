using UnityEngine;

public class EnvScanner : MonoBehaviour
{
    [SerializeField] private Vector3 forwardRayOffset = new Vector3(0f, 0.5f, 0f);
    [SerializeField] private float forwardRayLength = 1.2f;
    [SerializeField] private float heightRayLength = 3f;
    [SerializeField] private LayerMask obstacleLayer;

    public LayerMask ObstacleLayer => obstacleLayer;

    public ObstacleHitData ObstacleCheck()
    {
        var hitData = new ObstacleHitData();

        Vector3 forwardOrigin = transform.position + forwardRayOffset;
        hitData.forwardHitFound = Physics.Raycast(
            forwardOrigin,
            transform.forward,
            out hitData.forwardHit,
            forwardRayLength,
            obstacleLayer,
            QueryTriggerInteraction.Ignore);

        Debug.DrawRay(
            forwardOrigin,
            transform.forward * forwardRayLength,
            hitData.forwardHitFound ? Color.red : Color.white);

        if (!hitData.forwardHitFound)
            return hitData;

        // Probe from above the forward hit down onto the ledge top.
        Vector3 heightOrigin = hitData.forwardHit.point + Vector3.up * heightRayLength;
        hitData.heightHitFound = Physics.Raycast(
            heightOrigin,
            Vector3.down,
            out hitData.heightHit,
            heightRayLength,
            obstacleLayer,
            QueryTriggerInteraction.Ignore);

        Debug.DrawRay(
            heightOrigin,
            Vector3.down * heightRayLength,
            hitData.heightHitFound ? Color.green : Color.yellow);

        if (hitData.heightHitFound)
            hitData.obstacleHeight = hitData.heightHit.point.y - transform.position.y;

        return hitData;
    }

    public bool TryGetObstacle(out ObstacleHitData hitData)
    {
        hitData = ObstacleCheck();
        return hitData.forwardHitFound && hitData.heightHitFound;
    }

    public struct ObstacleHitData
    {
        public bool forwardHitFound;
        public bool heightHitFound;
        public RaycastHit forwardHit;
        public RaycastHit heightHit;
        public float obstacleHeight;
    }
}
