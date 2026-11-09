using System.Collections.Generic;
using UnityEngine;

namespace Game.Train
{
    /// <summary>
    /// Couples a car (wagon) to a locomotive driven by <see cref="TrainSplineDriver"/>.
    /// The car keeps a fixed spline distance from the locomotive and is posed from its front and rear bogie
    /// points on the rails, so it bends through curves like a real coupled car. It reads the leader's distance,
    /// which clients already receive through <see cref="NetworkTrainDriver"/>, so it needs no networking of its own.
    /// </summary>
    // After the train drivers (order 0) and before MovingPlatform (order 50).
    [DefaultExecutionOrder(20)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    public class TrainCarFollower : MonoBehaviour
    {
        public enum CouplingSide
        {
            Auto,
            Behind,
            InFront
        }

        [SerializeField] private TrainSplineDriver leader;
        [Tooltip("Auto picks the side the car is placed on relative to the locomotive in the scene.")]
        [SerializeField] private CouplingSide side = CouplingSide.Auto;

        [Header("Mass")]
        [Tooltip("Wagon mass (kg) for consist weight and grade forces.")]
        [SerializeField] private float carMass = 40000f;

        [Header("Spacing (m, 0 = auto-measure)")]
        [Tooltip("Spline distance from the locomotive's wheelbase centre to this car's centre.")]
        [SerializeField] private float gap;
        [Tooltip("Clearance added between the two bodies when the gap is auto-measured.")]
        [SerializeField] private float couplerGap = 0.3f;
        [Tooltip("Distance from the car centre to each bogie centre.")]
        [SerializeField] private float bogieHalfSpacing;

        [Header("Fit")]
        [Tooltip("Turn the car around if the mesh faces the wrong way.")]
        [SerializeField] private bool flipForward;
        [Tooltip("Raise (+) or lower (-) the car relative to the rail tops (m).")]
        [SerializeField] private float heightOffset;

        [Header("Wheels")]
        [Tooltip("Wheel meshes to spin. Empty = find children whose name contains one of the keywords below.")]
        [SerializeField] private Transform[] wheels;
        [SerializeField] private string[] wheelNameKeywords = { "wheel", "wheal" };
        [Tooltip("Wheel radius (m). 0 = measured from the wheel meshes.")]
        [SerializeField] private float wheelRadius;

        // Set by TrainConsist; when managed, side/gap above are ignored.
        [SerializeField, HideInInspector] private bool managedByConsist;
        [SerializeField, HideInInspector] private float managedOffset;

        private const float MaxWheelSize = 1.6f;

        private struct Wheel
        {
            public Transform Transform;
            public Vector3 LocalCenter;
        }

        private readonly List<Wheel> _wheels = new List<Wheel>();

        private Rigidbody _body;
        private Vector3 _forwardLocal = Vector3.forward;
        private Vector3 _lateralLocal = Vector3.right;
        private Vector3 _contactLocal;
        private float _halfLength;
        private float _offset;
        private float _bogieHalf;
        private float _wheelRadius;
        private float _lastCenter;
        private bool _hasLast;
        private bool _ready;

        /// <summary>Signed spline offset of this car's centre from the leader, toward the leader's front.</summary>
        public float FrontRelativeOffset => _offset;
        public float BogieHalfSpacing => _bogieHalf;
        /// <summary>Half the car body's length along the track (m). Valid after <see cref="MeasureSelf"/>.</summary>
        public float HalfLength => _halfLength;
        public float Mass => carMass;
        public TrainSplineDriver Leader => leader;
        public bool IsManagedByConsist => managedByConsist;

        public Vector3 TargetPosition { get; private set; }
        public Quaternion TargetRotation { get; private set; } = Quaternion.identity;
        public bool HasTarget { get; private set; }

        private void Awake()
        {
            _body = GetComponent<Rigidbody>();
            _body.isKinematic = true;
            _body.useGravity = false;
            _body.interpolation = RigidbodyInterpolation.Interpolate;
        }

        /// <summary>Couple this car to <paramref name="newLeader"/> at a fixed offset (toward the leader's front, m). Used by <see cref="TrainConsist"/>.</summary>
        public void SetCoupling(TrainSplineDriver newLeader, float frontRelativeOffset)
        {
            if (leader != newLeader)
            {
                if (leader != null)
                    leader.UnregisterCar(this);
                leader = newLeader;
                if (_ready && leader != null && isActiveAndEnabled)
                    leader.RegisterCar(this);
            }

            managedByConsist = true;
            managedOffset = frontRelativeOffset;
            _offset = frontRelativeOffset;
            MarkDirty();
        }

        /// <summary>Return to standalone coupling (uses this component's own side/gap settings).</summary>
        public void ReleaseCoupling()
        {
            managedByConsist = false;
            MarkDirty();
        }

        /// <summary>Pose the car on the rails right now for a leader at <paramref name="leaderDistance"/> (edit-mode preview).</summary>
        public void PoseNow(float leaderDistance)
        {
            TrainWheelRig rig = LeaderRig;
            if (rig == null || !rig.IsBound)
                return;
            MeasureSelf();
            if (managedByConsist)
                _offset = managedOffset;
            _hasLast = false;
            Pose(leaderDistance, false);
            _hasLast = false;
        }

        // TrainSplineDriver caches its rig in Awake, which doesn't run in edit mode.
        private TrainWheelRig LeaderRig =>
            leader == null ? null : leader.Rig != null ? leader.Rig : leader.GetComponent<TrainWheelRig>();

        private void MarkDirty()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
                UnityEditor.EditorUtility.SetDirty(this);
#endif
        }

        private void Start()
        {
            TryInitialize();
        }

        private void OnEnable()
        {
            if (_ready && leader != null)
                leader.RegisterCar(this);
        }

        private void OnDisable()
        {
            if (leader != null)
                leader.UnregisterCar(this);
        }

        private bool TryInitialize()
        {
            if (_ready)
                return true;
            if (leader == null || !leader.EnsureInitialized() || leader.Rig == null || !leader.Rig.IsBound)
                return false;

            Measure();
            _ready = true;
            leader.RegisterCar(this);
            Pose(leader.Distance, false);
            return true;
        }

        private void FixedUpdate()
        {
            if (!TryInitialize())
                return;
            Pose(leader.Distance, true);
        }

        private void Pose(float leaderDistance, bool fromFixedUpdate)
        {
            if (_body == null)
                _body = GetComponent<Rigidbody>();

            TrainWheelRig rig = LeaderRig;
            int facing = rig.Facing;
            float center = leaderDistance + facing * _offset;

            rig.SampleRailPoint(center + facing * _bogieHalf, out Vector3 front, out Vector3 frontTangent, out Vector3 frontUp);
            rig.SampleRailPoint(center - facing * _bogieHalf, out Vector3 rear, out _, out Vector3 rearUp);

            Vector3 forward = front - rear;
            if (forward.sqrMagnitude < 1e-6f)
                forward = frontTangent * facing;
            forward.Normalize();

            Vector3 up = (frontUp + rearUp).normalized;
            if (up.sqrMagnitude < 1e-6f)
                up = Vector3.up;

            Quaternion rotation = Quaternion.LookRotation(forward, up) *
                                  Quaternion.Inverse(Quaternion.LookRotation(_forwardLocal, Vector3.up));
            Vector3 position = (front + rear) * 0.5f + up * heightOffset -
                               rotation * Vector3.Scale(_contactLocal, transform.lossyScale);

            TargetPosition = position;
            TargetRotation = rotation;
            HasTarget = true;

            if (fromFixedUpdate && Application.isPlaying)
            {
                _body.MovePosition(position);
                _body.MoveRotation(rotation);
            }
            else
            {
                transform.SetPositionAndRotation(position, rotation);
                if (_body != null && Application.isPlaying)
                {
                    _body.position = position;
                    _body.rotation = rotation;
                }
            }

            if (Application.isPlaying)
                SpinWheels(center, facing, leader.WheelLock);
        }

        private void SpinWheels(float center, int facing, float wheelLock)
        {
            if (!_hasLast)
            {
                _lastCenter = center;
                _hasLast = true;
                return;
            }

            float travel = (center - _lastCenter) * facing * (1f - wheelLock);
            _lastCenter = center;
            if (_wheels.Count == 0 || _wheelRadius < 1e-3f || Mathf.Abs(travel) < 1e-6f)
                return;

            Quaternion spin = Quaternion.AngleAxis(travel / _wheelRadius * Mathf.Rad2Deg, _lateralLocal);
            Quaternion inverseRoot = Quaternion.Inverse(transform.rotation);
            foreach (Wheel wheel in _wheels)
            {
                if (wheel.Transform == null)
                    continue;
                Vector3 local = transform.InverseTransformPoint(wheel.Transform.position);
                Quaternion localRotation = inverseRoot * wheel.Transform.rotation;
                wheel.Transform.SetPositionAndRotation(
                    transform.TransformPoint(wheel.LocalCenter + spin * (local - wheel.LocalCenter)),
                    transform.rotation * (spin * localRotation));
            }
        }

        private void Measure()
        {
            MeasureSelf();

            if (managedByConsist)
            {
                _offset = managedOffset;
                return;
            }

            int sign = ResolveSide();
            float magnitude = gap > 0f ? gap : MeasureGap(sign);
            _offset = sign * magnitude;
        }

        /// <summary>Measure this car's own geometry (length, rail contact, wheels, bogies). Works in edit mode.</summary>
        public void MeasureSelf()
        {
            if (!TryGetLocalBounds(transform, transform, out Bounds bounds))
                bounds = new Bounds(Vector3.zero, Vector3.one);

            bool alongX = bounds.size.x > bounds.size.z;
            _forwardLocal = alongX ? Vector3.right : Vector3.forward;
            if (flipForward)
                _forwardLocal = -_forwardLocal;
            _lateralLocal = Vector3.Cross(Vector3.up, _forwardLocal).normalized;

            float scale = Mathf.Abs(transform.lossyScale.x);
            _halfLength = (alongX ? bounds.extents.x : bounds.extents.z) * scale;
            _contactLocal = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);

            CollectWheels(bounds);

            _bogieHalf = bogieHalfSpacing > 0f ? bogieHalfSpacing : MeasureBogieHalfSpacing(bounds, scale);
        }

        private int ResolveSide()
        {
            if (side == CouplingSide.Behind)
                return -1;
            if (side == CouplingSide.InFront)
                return 1;

            Vector3 toCar = transform.position - leader.Rig.ReferenceWorldPoint;
            return Vector3.Dot(toCar, leader.Rig.ForwardWorld) >= 0f ? 1 : -1;
        }

        private float MeasureGap(int sign)
        {
            TrainWheelRig rig = leader.Rig;
            float extent = MaxExtentAlong(leader.transform, rig.ReferenceWorldPoint, rig.ForwardWorld * sign);
            return extent + _halfLength + couplerGap;
        }

        /// <summary>
        /// Furthest any mesh under <paramref name="root"/> reaches from <paramref name="origin"/> along <paramref name="direction"/>.
        /// Uses mesh-space boxes, so the result does not grow when the object is rotated.
        /// </summary>
        public static float MaxExtentAlong(Transform root, Vector3 origin, Vector3 direction)
        {
            direction.Normalize();
            float extent = 0f;
            foreach (Renderer r in root.GetComponentsInChildren<Renderer>())
            {
                if (r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer)
                    continue;

                MeshFilter filter = r.GetComponent<MeshFilter>();
                Mesh mesh = filter != null ? filter.sharedMesh : r is SkinnedMeshRenderer skinned ? skinned.sharedMesh : null;
                Bounds source = mesh != null ? mesh.bounds : r.bounds;
                Matrix4x4 toWorld = mesh != null ? r.transform.localToWorldMatrix : Matrix4x4.identity;

                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = new Vector3(
                        (i & 1) == 0 ? source.min.x : source.max.x,
                        (i & 2) == 0 ? source.min.y : source.max.y,
                        (i & 4) == 0 ? source.min.z : source.max.z);
                    extent = Mathf.Max(extent, Vector3.Dot(toWorld.MultiplyPoint3x4(corner) - origin, direction));
                }
            }

            return extent;
        }

        private float MeasureBogieHalfSpacing(Bounds bounds, float scale)
        {
            if (_wheels.Count >= 2)
            {
                float frontSum = 0f, rearSum = 0f;
                int frontCount = 0, rearCount = 0;
                foreach (Wheel wheel in _wheels)
                {
                    float along = Vector3.Dot(wheel.LocalCenter - bounds.center, _forwardLocal);
                    if (along >= 0f)
                    {
                        frontSum += along;
                        frontCount++;
                    }
                    else
                    {
                        rearSum += along;
                        rearCount++;
                    }
                }

                if (frontCount > 0 && rearCount > 0)
                {
                    float spacing = frontSum / frontCount - rearSum / rearCount;
                    if (spacing > 0.5f)
                        return spacing * 0.5f * scale;
                }
            }

            return _halfLength * 0.7f;
        }

        private void CollectWheels(Bounds carBounds)
        {
            _wheels.Clear();
            var candidates = new List<Transform>();
            if (wheels != null && wheels.Length > 0)
            {
                candidates.AddRange(wheels);
            }
            else
            {
                foreach (Transform t in GetComponentsInChildren<Transform>(true))
                {
                    if (t != transform && t.GetComponent<Renderer>() != null && MatchesWheelName(t.name))
                        candidates.Add(t);
                }
            }

            float scale = Mathf.Abs(transform.lossyScale.x);
            float radiusSum = 0f;
            foreach (Transform t in candidates)
            {
                if (t == null || !TryGetLocalBounds(t, transform, out Bounds b))
                    continue;
                if (b.size.y * scale > MaxWheelSize && (wheels == null || wheels.Length == 0))
                    continue;

                _wheels.Add(new Wheel { Transform = t, LocalCenter = b.center });
                radiusSum += b.extents.y * scale;
            }

            _wheelRadius = wheelRadius > 0f ? wheelRadius : _wheels.Count > 0 ? radiusSum / _wheels.Count : 0f;
        }

        private bool MatchesWheelName(string objectName)
        {
            if (wheelNameKeywords == null)
                return false;
            string lower = objectName.ToLowerInvariant();
            foreach (string keyword in wheelNameKeywords)
            {
                if (!string.IsNullOrEmpty(keyword) && lower.Contains(keyword.ToLowerInvariant()))
                    return true;
            }
            return false;
        }

        /// <summary>Bounds of every renderer under <paramref name="root"/>, in <paramref name="space"/>'s local coordinates.</summary>
        private static bool TryGetLocalBounds(Transform root, Transform space, out Bounds bounds)
        {
            bounds = default;
            bool any = false;
            foreach (Renderer r in root.GetComponentsInChildren<Renderer>())
            {
                if (r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer)
                    continue;

                MeshFilter filter = r.GetComponent<MeshFilter>();
                Bounds source;
                Matrix4x4 toWorld;
                if (filter != null && filter.sharedMesh != null)
                {
                    source = filter.sharedMesh.bounds;
                    toWorld = r.transform.localToWorldMatrix;
                }
                else if (r is SkinnedMeshRenderer skinned && skinned.sharedMesh != null)
                {
                    source = skinned.sharedMesh.bounds;
                    toWorld = r.transform.localToWorldMatrix;
                }
                else
                {
                    source = r.bounds;
                    toWorld = Matrix4x4.identity;
                }

                for (int i = 0; i < 8; i++)
                {
                    Vector3 c = new Vector3(
                        (i & 1) == 0 ? source.min.x : source.max.x,
                        (i & 2) == 0 ? source.min.y : source.max.y,
                        (i & 4) == 0 ? source.min.z : source.max.z);
                    Vector3 local = space.InverseTransformPoint(toWorld.MultiplyPoint3x4(c));
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
        private void OnValidate()
        {
            carMass = Mathf.Max(0f, carMass);
            gap = Mathf.Max(0f, gap);
            couplerGap = Mathf.Max(0f, couplerGap);
            bogieHalfSpacing = Mathf.Max(0f, bogieHalfSpacing);
            wheelRadius = Mathf.Max(0f, wheelRadius);
        }
#endif
    }
}
