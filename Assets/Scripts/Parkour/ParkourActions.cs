using UnityEngine;

[CreateAssetMenu(menuName = "Parkour System/Parkour Action")]
public class ParkourActions : ScriptableObject
{
    [SerializeField] private string animName;
    [SerializeField] private float minHeight;
    [SerializeField] private float maxHeight;
    [SerializeField] private float transitionDuration = 0.1f;
    [SerializeField] private int priority;
    [SerializeField] private bool rotateToObstacle;

    [Header("Target Matching")]
    [SerializeField] bool enableTargetMatching = true;
    [SerializeField] AvatarTarget matchBodyPart;
    [SerializeField] float matchStartTime;
    [SerializeField] float matchTargetTime;

    public Quaternion TargetRotation { get; private set; }

    public string AnimName => animName;
    public float MinHeight => minHeight;
    public float MaxHeight => maxHeight;
    public float TransitionDuration => transitionDuration;
    public int Priority => priority;
    public bool RotateToObstacle => rotateToObstacle;

    public Vector3 MatchPosition { get; private set; }

    /// <summary>
    /// Returns true when obstacle height is in range. When rotateToObstacle is enabled,
    /// stores facing rotation from the forward hit surface normal (-normal).
    /// </summary>
    public bool CheckIfPossible(EnvScanner.ObstacleHitData hitData, Transform player)
    {
        if (!hitData.forwardHitFound || !hitData.heightHitFound || player == null)
            return false;

        float height = hitData.heightHit.point.y - player.position.y;
        if (height < minHeight || height > maxHeight)
            return false;

        if (rotateToObstacle)
            TargetRotation = Quaternion.LookRotation(-hitData.forwardHit.normal);

        if (enableTargetMatching)
            MatchPosition = hitData.heightHit.point;

        return true;
    }

    public bool EnableTargetMatching => enableTargetMatching;
    public AvatarTarget MatchBodyPart => matchBodyPart;
    public float MatchStartTime => matchStartTime;
    public float MatchTargetTime => matchTargetTime;
}
