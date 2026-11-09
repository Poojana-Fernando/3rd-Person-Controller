using UnityEngine;

namespace Game.Train
{
    /// <summary>
    /// Driver seat on the train. The seated player faces the locomotive's front and controls it
    /// through the parent <see cref="NetworkTrainDriver"/> (see TrainSeatInteractor on the player).
    /// </summary>
    [DisallowMultipleComponent]
    public class TrainSeat : MonoBehaviour
    {
        [Tooltip("Where the seated character's root goes. Auto-created at the top centre of the seat if empty.")]
        [SerializeField] private Transform sitPoint;
        [Tooltip("Vertical offset from the seat top to the character root (tune so the hips rest on the cushion).")]
        [SerializeField] private float seatHeightOffset = -0.45f;
        [Tooltip("Offset along the train's front from the seat centre to the character root.")]
        [SerializeField] private float sitForwardOffset = 0.05f;
        [Tooltip("Where the character stands up, relative to the seat in train space (x = right, z = front).")]
        [SerializeField] private Vector3 standOffset = new Vector3(0f, 0f, -0.75f);
        [SerializeField] private float interactDistance = 2f;
        [Tooltip("How directly the camera must look at the seat (dot product) to allow sitting.")]
        [Range(0f, 1f)] [SerializeField] private float lookDot = 0.8f;

        private NetworkTrainDriver _network;
        private TrainSplineDriver _driver;
        private TrainWheelRig _rig;
        private Bounds _localBounds;
        private bool _hasBounds;

        public NetworkTrainDriver Network => _network;
        public TrainSplineDriver Driver => _driver;
        public float InteractDistance => interactDistance;
        public float LookDot => lookDot;

        private void Awake()
        {
            _network = GetComponentInParent<NetworkTrainDriver>();
            _driver = GetComponentInParent<TrainSplineDriver>();
            _rig = GetComponentInParent<TrainWheelRig>();
            EnsureSitPoint();
        }

        /// <summary>World point the camera look-check and distance-check aim at.</summary>
        public Vector3 FocusPoint
        {
            get
            {
                EnsureSitPoint();
                return sitPoint != null ? sitPoint.position : transform.position;
            }
        }

        public Vector3 TrainForward
        {
            get
            {
                Vector3 forward = _rig != null ? _rig.ForwardWorld : transform.forward;
                Vector3 up = TrainUp;
                forward = Vector3.ProjectOnPlane(forward, up);
                return forward.sqrMagnitude > 1e-6f ? forward.normalized : Vector3.forward;
            }
        }

        private Vector3 TrainUp => _rig != null ? _rig.transform.up : Vector3.up;

        public void GetSitPose(out Vector3 position, out Quaternion rotation)
        {
            Vector3 up = TrainUp;
            Vector3 forward = TrainForward;
            rotation = Quaternion.LookRotation(forward, up);
            position = FocusPoint + up * seatHeightOffset + forward * sitForwardOffset;
        }

        public Vector3 GetStandPosition()
        {
            Vector3 up = TrainUp;
            Quaternion frame = Quaternion.LookRotation(TrainForward, up);
            Vector3 basePoint = transform.position;
            if (_hasBounds)
                basePoint = transform.TransformPoint(new Vector3(_localBounds.center.x, _localBounds.min.y, _localBounds.center.z));
            return basePoint + frame * standOffset;
        }

        private void EnsureSitPoint()
        {
            if (!_hasBounds)
                _hasBounds = TryGetLocalBounds(out _localBounds);

            if (sitPoint != null)
                return;

            var point = new GameObject("SitPoint").transform;
            point.SetParent(transform, false);
            point.localPosition = _hasBounds
                ? new Vector3(_localBounds.center.x, _localBounds.max.y, _localBounds.center.z)
                : Vector3.zero;
            sitPoint = point;
        }

        private bool TryGetLocalBounds(out Bounds bounds)
        {
            bounds = default;
            bool any = false;
            foreach (MeshFilter filter in GetComponentsInChildren<MeshFilter>())
            {
                Mesh mesh = filter.sharedMesh;
                if (mesh == null)
                    continue;

                Bounds mb = mesh.bounds;
                Vector3 c = mb.center;
                Vector3 e = mb.extents;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = c + Vector3.Scale(e, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    Vector3 local = transform.InverseTransformPoint(filter.transform.TransformPoint(corner));
                    if (!any)
                    {
                        bounds = new Bounds(local, Vector3.zero);
                        any = true;
                    }
                    else
                    {
                        bounds.Encapsulate(local);
                    }
                }
            }

            return any;
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (!Application.isPlaying)
            {
                _rig = GetComponentInParent<TrainWheelRig>();
                _hasBounds = TryGetLocalBounds(out _localBounds);
            }

            Vector3 top = sitPoint != null
                ? sitPoint.position
                : _hasBounds
                    ? transform.TransformPoint(new Vector3(_localBounds.center.x, _localBounds.max.y, _localBounds.center.z))
                    : transform.position;
            Vector3 up = TrainUp;
            Vector3 forward = TrainForward;
            Vector3 root = top + up * seatHeightOffset + forward * sitForwardOffset;

            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(top, 0.08f);
            Gizmos.DrawLine(root, root + forward * 0.6f);
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(GetStandPosition(), 0.15f);
            Gizmos.color = new Color(0f, 1f, 0f, 0.15f);
            Gizmos.DrawWireSphere(top, interactDistance);
        }
#endif
    }
}
