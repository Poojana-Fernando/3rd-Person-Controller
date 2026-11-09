using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Splines;

namespace Game.Train
{
    /// <summary>
    /// Places a locomotive on a rail spline so every wheel sits on the rails:
    /// the rigid frame is posed from its front-most and rear-most axles, lead/trail trucks swivel to the curve,
    /// wheels roll by distance / radius and the side rods follow the driving-wheel cranks.
    /// Parts are found by name (GEO_Wheel_{Axle}_{L|R}, GEO_Axle_{Axle}, GEO_CouplingRod_{L|R}, GEO_MainRod_{L|R}).
    /// </summary>
    [DisallowMultipleComponent]
    public class TrainWheelRig : MonoBehaviour
    {
        private static readonly string[] AxleKeys =
        {
            "Lead1", "Lead2", "Drive1", "Drive2", "Drive3", "Drive4", "Trail1", "Trail2"
        };

        [Header("Rails (auto-calibrated from the instantiated rail pieces)")]
        [SerializeField] private bool autoCalibrateRails = true;
        [Tooltip("Sideways offset of the rail centreline from the spline, along the spline's right vector (m).")]
        [SerializeField] private float railLateralOffset = 0.69f;
        [Tooltip("Distance between the two rail centres (m).")]
        [SerializeField] private float railGauge = 1.82f;
        [Tooltip("Height of the rail tops above the spline (m).")]
        [SerializeField] private float railTopHeight = 0.15f;
        [SerializeField] private string railRendererPrefix = "rail";

        [Header("Fit")]
        [Tooltip("Uniformly scale the train so its wheel gauge matches the rail gauge.")]
        [SerializeField] private bool scaleToRailGauge = true;

        [Header("Trucks")]
        [SerializeField] private bool swivelTrucks = true;
        [SerializeField] private float maxTruckYaw = 20f;
        [SerializeField] private float maxTruckShift = 0.6f;

        [Header("Rods")]
        [SerializeField] private bool animateRods = true;
        [Tooltip("Crank radius as a fraction of the driving-wheel radius (used when override is 0).")]
        [SerializeField] private float crankRadiusFactor = 0.35f;
        [Tooltip("Crank radius in the train's local units. 0 = use the factor.")]
        [SerializeField] private float crankRadiusOverride;

        [Header("Wheel discovery")]
        [SerializeField] private WheelNamingMode wheelNamingMode = WheelNamingMode.GeoStandard;
        [Tooltip("Local-space tolerance when clustering Aurora wheel01.. meshes into axles.")]
        [SerializeField] private float auroraAxleClusterTolerance = 0.15f;
        [SerializeField] private Transform auroraMainRodLeft;
        [SerializeField] private Transform auroraMainRodRight;

        [Header("Manual axles (Wheel Naming Mode = Manual)")]
        [Tooltip("Front-to-back axle rows. Assign wheel meshes, role, and how play mode poses each row.")]
        [SerializeField] private List<ManualAxleDefinition> manualAxles = new List<ManualAxleDefinition>();

        public enum WheelNamingMode
        {
            GeoStandard,
            AuroraNumeric,
            Manual
        }

        public enum ManualAxleRole
        {
            Lead,
            Trail,
            Drive
        }

        public enum ManualWheelPoseMode
        {
            OrbitBounds,
            RestLocalRoll,
            StaticMesh
        }

        [System.Serializable]
        public struct ManualAxleDefinition
        {
            [Tooltip("Optional label for logging (e.g. Lead front).")]
            public string label;
            [Tooltip("One full-axle mesh or left/right pair on this axle.")]
            public Transform[] wheelParts;
            public ManualAxleRole role;
            [Tooltip("OrbitBounds: truck + spin around bounds. RestLocalRoll: keep authored local pose, roll only. StaticMesh: no rig posing.")]
            public ManualWheelPoseMode poseMode;
        }

        private class Part
        {
            public Transform Transform;
            public Vector3 RestPosition;
            public Quaternion RestRotation;
        }

        private class AxleGroup
        {
            public string Key;
            public bool IsTruck;
            public Vector3 Center;
            public float Radius;
            public float Along;
            public Vector3 Contact;
            public Vector3 WheelSpan;
            public float Angle;
            public ManualWheelPoseMode PoseMode;
            public readonly List<Part> Parts = new List<Part>();
        }

        private class Rod
        {
            public Part Part;
            public bool IsMain;
            public float Phase;
            public Vector3 CrankEnd;
            public Vector3 CrossheadEnd;
        }

        private readonly List<AxleGroup> _groups = new List<AxleGroup>();
        private readonly List<Rod> _rods = new List<Rod>();

        private bool _measured;
        private Vector3 _forwardLocal = Vector3.forward;
        private Vector3 _lateralLocal = Vector3.right;
        private AxleGroup _lead;
        private AxleGroup _trail;
        private AxleGroup _drive;
        private float _alongMin;
        private float _alongMax;
        private float _wheelGaugeLocal;

        private SplineContainer _track;
        private int _splineIndex;
        private float _trackLength;
        private readonly RailRoute _route = new RailRoute();
        private int _facing = 1;
        private bool _bound;

        private Rigidbody _body;
        private float _prevDistance;
        private float _currDistance;
        private float _lastPoseFixedTime = -1f;
        private bool _fixedMode;
        private float _lastAnimDistance;
        private bool _hasAnimDistance;

        /// <summary>+1 if the train's front points toward increasing spline distance.</summary>
        public int Facing => _facing;
        /// <summary>Root pose most recently requested by <see cref="ApplyPose"/> (the body may still be moving there).</summary>
        public Vector3 TargetPosition { get; private set; }
        public Quaternion TargetRotation { get; private set; } = Quaternion.identity;
        public bool HasTarget { get; private set; }

        /// <summary>1 = wheels roll with the train, 0 = locked (brakes on, train skids). Rods follow the drive wheels.</summary>
        [System.NonSerialized] public float WheelSpinScale = 1f;

        /// <summary>World direction of the locomotive's front.</summary>
        public Vector3 ForwardWorld => transform.TransformDirection(_forwardLocal).normalized;
        public float TrackLength => _trackLength;
        public bool IsBound => _bound;
        public RailRoute Route => _route;

        /// <summary>World point midway between the end axles' rail contacts; the point <see cref="TrainSplineDriver.Distance"/> refers to.</summary>
        public Vector3 ReferenceWorldPoint => transform.TransformPoint((_lead.Contact + _trail.Contact) * 0.5f);

        public float MinDistance
        {
            get
            {
                GetDistanceRange(out float min, out _);
                return min;
            }
        }

        public float MaxDistance
        {
            get
            {
                GetDistanceRange(out _, out float max);
                return max;
            }
        }

        private float Scale => Mathf.Abs(transform.lossyScale.x);

        private void GetDistanceRange(out float min, out float max)
        {
            float s = Scale;
            min = _facing > 0 ? -_alongMin * s : _alongMax * s;
            max = _facing > 0 ? _trackLength - _alongMax * s : _trackLength + _alongMin * s;
            if (min > max)
                min = max = (min + max) * 0.5f;
        }

        private void Awake()
        {
            _body = GetComponent<Rigidbody>();
            Measure();
        }

        public bool Bind(SplineContainer track, int splineIndex)
        {
            Measure();
            if (!_measured || track == null || track.Splines.Count == 0)
                return false;

            _track = track;
            _splineIndex = Mathf.Clamp(splineIndex, 0, Mathf.Max(0, track.Splines.Count - 1));
            _route.ResetSingleSpline(track, _splineIndex);
            _trackLength = _route.TotalLength;

            CalibrateRails();
            ApplyGaugeScale();

            _facing = NearestDistance(transform.TransformPoint(_lead.Center)) >=
                      NearestDistance(transform.TransformPoint(_trail.Center))
                ? 1
                : -1;

            _bound = true;
            return true;
        }

        public float NearestReferenceDistance()
        {
            Vector3 reference = transform.TransformPoint((_lead.Contact + _trail.Contact) * 0.5f);
            return Mathf.Clamp(NearestDistance(reference), MinDistance, MaxDistance);
        }

        /// <summary>Pose the train body so its end axles sit on the rail centreline at distance <paramref name="distance"/>.</summary>
        public void ApplyPose(float distance, bool fromFixedUpdate)
        {
            if (!_bound)
                return;

            ComputeRootPose(distance, out Vector3 position, out Quaternion rotation);
            TargetPosition = position;
            TargetRotation = rotation;
            HasTarget = true;

            if (_body != null && fromFixedUpdate && Application.isPlaying)
            {
                _body.MovePosition(position);
                _body.MoveRotation(rotation);
            }
            else
            {
                transform.SetPositionAndRotation(position, rotation);
                if (_body != null)
                {
                    _body.position = position;
                    _body.rotation = rotation;
                }
            }

            if (fromFixedUpdate)
            {
                _prevDistance = _fixedMode ? _currDistance : distance;
                _currDistance = distance;
                _lastPoseFixedTime = Time.fixedTime;
                _fixedMode = true;
            }
            else
            {
                _prevDistance = distance;
                _currDistance = distance;
                _fixedMode = false;
            }

            if (!_hasAnimDistance)
            {
                _lastAnimDistance = distance;
                _hasAnimDistance = true;
            }
        }

        private void LateUpdate()
        {
            if (!_bound || !_hasAnimDistance)
                return;

            float visual = _currDistance;
            if (_fixedMode && Time.fixedTime - _lastPoseFixedTime < Time.fixedDeltaTime * 0.5f)
            {
                float alpha = Mathf.Clamp01((Time.time - Time.fixedTime) / Time.fixedDeltaTime);
                visual = Mathf.Lerp(_prevDistance, _currDistance, alpha);
            }

            AnimateParts(visual);
        }

        private void ComputeRootPose(float distance, out Vector3 position, out Quaternion rotation)
        {
            float s = Scale;
            SampleRail(distance + _facing * _lead.Along * s, out Vector3 front, out Vector3 frontTangent, out Vector3 frontUp);
            SampleRail(distance + _facing * _trail.Along * s, out Vector3 rear, out _, out Vector3 rearUp);

            Vector3 forward = front - rear;
            if (forward.sqrMagnitude < 1e-6f)
                forward = frontTangent * _facing;
            forward.Normalize();

            Vector3 up = (frontUp + rearUp).normalized;
            if (up.sqrMagnitude < 1e-6f)
                up = Vector3.up;

            rotation = Quaternion.LookRotation(forward, up) *
                       Quaternion.Inverse(Quaternion.LookRotation(_forwardLocal, Vector3.up));

            Vector3 localMid = (_lead.Contact + _trail.Contact) * 0.5f;
            position = (front + rear) * 0.5f - rotation * (localMid * s);
        }

        private void AnimateParts(float distance)
        {
            float s = Scale;
            float travel = (distance - _lastAnimDistance) * _facing * Mathf.Clamp01(WheelSpinScale);
            _lastAnimDistance = distance;

            foreach (AxleGroup group in _groups)
            {
                if (group.Radius > 1e-4f)
                    group.Angle = Mathf.Repeat(group.Angle + travel / (group.Radius * s) * Mathf.Rad2Deg, 360f);

                Quaternion truckYaw = Quaternion.identity;
                Vector3 truckShift = Vector3.zero;

                if (group.IsTruck && swivelTrucks)
                {
                    SampleRail(distance + _facing * group.Along * s, out Vector3 railPoint, out Vector3 railTangent, out _);
                    Vector3 localPoint = transform.InverseTransformPoint(railPoint);
                    float shift = Vector3.Dot(localPoint - group.Contact, _lateralLocal);
                    truckShift = _lateralLocal * Mathf.Clamp(shift, -maxTruckShift, maxTruckShift);

                    Vector3 localTangent = transform.InverseTransformDirection(railTangent * _facing);
                    localTangent = Vector3.ProjectOnPlane(localTangent, Vector3.up);
                    if (localTangent.sqrMagnitude > 1e-6f)
                    {
                        float yaw = Vector3.SignedAngle(_forwardLocal, localTangent, Vector3.up);
                        truckYaw = Quaternion.AngleAxis(Mathf.Clamp(yaw, -maxTruckYaw, maxTruckYaw), Vector3.up);
                    }
                }

                Quaternion spin = Quaternion.AngleAxis(group.Angle, _lateralLocal);
                Quaternion combined = truckYaw * spin;

                foreach (Part part in group.Parts)
                {
                    switch (group.PoseMode)
                    {
                        case ManualWheelPoseMode.StaticMesh:
                            continue;
                        case ManualWheelPoseMode.RestLocalRoll:
                            SetPartPose(part, part.RestPosition, spin * part.RestRotation);
                            continue;
                        default:
                            Vector3 local = group.Center + truckShift + combined * (part.RestPosition - group.Center);
                            SetPartPose(part, local, combined * part.RestRotation);
                            break;
                    }
                }
            }

            if (animateRods)
                AnimateRods();
        }

        private void AnimateRods()
        {
            if (_rods.Count == 0 || _drive == null)
                return;

            float crankRadius = crankRadiusOverride > 0f ? crankRadiusOverride : crankRadiusFactor * _drive.Radius;
            float theta = _drive.Angle * Mathf.Deg2Rad;

            foreach (Rod rod in _rods)
            {
                Vector3 offset = crankRadius *
                                 (Vector3.up * (Mathf.Cos(theta + rod.Phase) - Mathf.Cos(rod.Phase)) +
                                  _forwardLocal * (Mathf.Sin(theta + rod.Phase) - Mathf.Sin(rod.Phase)));

                if (!rod.IsMain)
                {
                    SetPartPose(rod.Part, rod.Part.RestPosition + offset, rod.Part.RestRotation);
                    continue;
                }

                Vector3 crank = rod.CrankEnd + offset;
                Vector3 rest = rod.CrossheadEnd - rod.CrankEnd;
                float dx0 = Vector3.Dot(rest, _forwardLocal);
                float dy0 = Vector3.Dot(rest, Vector3.up);
                float length = Mathf.Sqrt(dx0 * dx0 + dy0 * dy0);

                float dy1 = Vector3.Dot(rod.CrossheadEnd, Vector3.up) - Vector3.Dot(crank, Vector3.up);
                float dx1 = Mathf.Sign(dx0) * Mathf.Sqrt(Mathf.Max(0f, length * length - dy1 * dy1));

                Vector3 from = _forwardLocal * dx0 + Vector3.up * dy0;
                Vector3 to = _forwardLocal * dx1 + Vector3.up * dy1;
                Quaternion delta = Quaternion.FromToRotation(from, to);

                SetPartPose(rod.Part, crank + delta * (rod.Part.RestPosition - rod.CrankEnd), delta * rod.Part.RestRotation);
            }
        }

        private void SetPartPose(Part part, Vector3 localPosition, Quaternion localRotation)
        {
            part.Transform.SetPositionAndRotation(
                transform.TransformPoint(localPosition),
                transform.rotation * localRotation);
        }

        public void ApplyRoute(RailRoute route)
        {
            if (route == null || route.Track == null)
                return;
            _route.CopyFrom(route);
            _track = route.Track;
            SyncRouteLength();
            _splineIndex = _route.PrimarySplineIndex;
        }

        public void SyncRouteLength()
        {
            _trackLength = _route.TotalLength;
        }

        private void SampleSpline(float distance, out Vector3 position, out Vector3 tangent, out Vector3 up, out Vector3 right)
        {
            if (!_route.TryMapRouteDistance(distance, out int splineIndex, out float splineDistance))
            {
                splineIndex = _splineIndex;
                splineDistance = Mathf.Clamp(distance, 0f, _trackLength);
            }

            Spline spline = _track.Splines[splineIndex];
            float len = _track.CalculateLength(splineIndex);
            float t = spline.ConvertIndexUnit(Mathf.Clamp(splineDistance, 0f, len), PathIndexUnit.Distance, PathIndexUnit.Normalized);
            _track.Evaluate(splineIndex, Mathf.Clamp01(t), out float3 p, out float3 tan, out float3 u);

            position = p;
            tangent = ((Vector3)tan).sqrMagnitude > 1e-8f ? ((Vector3)tan).normalized : transform.forward;
            up = ((Vector3)u).sqrMagnitude > 1e-8f ? ((Vector3)u).normalized : Vector3.up;
            right = Vector3.Cross(up, tangent).normalized;
        }

        /// <summary>Rail centreline point (on the rail tops), tangent and up at spline <paramref name="distance"/>.</summary>
        public void SampleRailPoint(float distance, out Vector3 point, out Vector3 tangent, out Vector3 up)
        {
            SampleRail(distance, out point, out tangent, out up);
        }

        private void SampleRail(float distance, out Vector3 point, out Vector3 tangent, out Vector3 up)
        {
            SampleSpline(distance, out Vector3 position, out tangent, out up, out Vector3 right);
            point = position + right * railLateralOffset + up * railTopHeight;
        }

        private float NearestDistance(Vector3 worldPoint)
        {
            float best = float.MaxValue;
            float bestRoute = 0f;
            float cursor = 0f;
            for (int i = 0; i < _route.Spans.Count; i++)
            {
                RailSpan span = _route.Spans[i];
                Spline spline = _track.Splines[span.SplineIndex];
                float3 local = _track.transform.InverseTransformPoint(worldPoint);
                SplineUtility.GetNearestPoint(spline, local, out _, out float t);
                float splineD = spline.ConvertIndexUnit(t, PathIndexUnit.Normalized, PathIndexUnit.Distance);
                float min = Mathf.Min(span.SplineStart, span.SplineEnd);
                float max = Mathf.Max(span.SplineStart, span.SplineEnd);
                if (splineD < min - 1f || splineD > max + 1f)
                {
                    cursor += span.Length;
                    continue;
                }

                float offset = span.Sign > 0 ? splineD - span.SplineStart : span.SplineStart - splineD;
                float routeD = cursor + Mathf.Clamp(offset, 0f, span.Length);
                float3 localPos = spline.EvaluatePosition(t);
                Vector3 world = _track.transform.TransformPoint(localPos);
                float err = (world - worldPoint).sqrMagnitude;
                if (err < best)
                {
                    best = err;
                    bestRoute = routeD;
                }
                cursor += span.Length;
            }

            if (best < float.MaxValue)
                return bestRoute;

            Spline fallback = _track.Splines[_splineIndex];
            float3 localFallback = _track.transform.InverseTransformPoint(worldPoint);
            SplineUtility.GetNearestPoint(fallback, localFallback, out _, out float tf);
            return fallback.ConvertIndexUnit(tf, PathIndexUnit.Normalized, PathIndexUnit.Distance);
        }

        private void CalibrateRails()
        {
            if (!autoCalibrateRails || _track == null)
                return;

            var railsByPiece = new Dictionary<Transform, List<Renderer>>();
            foreach (Renderer r in _track.GetComponentsInChildren<Renderer>(true))
            {
                if (r.transform.parent == null ||
                    !r.name.StartsWith(railRendererPrefix, System.StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!railsByPiece.TryGetValue(r.transform.parent, out List<Renderer> list))
                {
                    list = new List<Renderer>();
                    railsByPiece[r.transform.parent] = list;
                }
                list.Add(r);
            }

            Transform bestPiece = null;
            float bestDistance = float.MaxValue;
            foreach (KeyValuePair<Transform, List<Renderer>> pair in railsByPiece)
            {
                if (pair.Value.Count < 2)
                    continue;
                float d = (pair.Key.position - transform.position).sqrMagnitude;
                if (d < bestDistance)
                {
                    bestDistance = d;
                    bestPiece = pair.Key;
                }
            }

            if (bestPiece == null)
            {
                Debug.LogWarning("[TrainWheelRig] No instantiated rail pieces found; using the rail values set in the Inspector.");
                return;
            }

            float s = NearestDistance(bestPiece.position);
            SampleSpline(s, out Vector3 position, out _, out Vector3 up, out Vector3 right);

            float minLateral = float.MaxValue;
            float maxLateral = float.MinValue;
            float top = float.MinValue;
            foreach (Renderer rail in railsByPiece[bestPiece])
            {
                Bounds b = rail.bounds;
                float lateral = Vector3.Dot(b.center - position, right);
                minLateral = Mathf.Min(minLateral, lateral);
                maxLateral = Mathf.Max(maxLateral, lateral);
                top = Mathf.Max(top, Vector3.Dot(new Vector3(b.center.x, b.max.y, b.center.z) - position, up));
            }

            float gauge = maxLateral - minLateral;
            if (gauge < 0.3f || gauge > 4f)
            {
                Debug.LogWarning($"[TrainWheelRig] Measured rail gauge {gauge:F2} m looks wrong; using the Inspector values.");
                return;
            }

            railGauge = gauge;
            railLateralOffset = (minLateral + maxLateral) * 0.5f;
            railTopHeight = top;
            Debug.Log($"[TrainWheelRig] Rails calibrated: gauge {railGauge:F3} m, offset {railLateralOffset:F3} m, top {railTopHeight:F3} m.");
        }

        private void ApplyGaugeScale()
        {
            if (!scaleToRailGauge || _wheelGaugeLocal < 0.01f)
                return;

            float currentGauge = _wheelGaugeLocal * Scale;
            float factor = railGauge / currentGauge;
            if (Mathf.Abs(factor - 1f) < 0.01f)
                return;

#if UNITY_EDITOR
            if (!Application.isPlaying)
                UnityEditor.Undo.RecordObject(transform, "Scale Train To Rail Gauge");
#endif
            transform.localScale *= factor;
            Debug.Log($"[TrainWheelRig] Scaled train by {factor:F3} so wheel gauge {currentGauge:F3} m matches rail gauge {railGauge:F3} m.");
        }

        private void Measure()
        {
            if (_measured)
                return;

            var byName = new Dictionary<string, Transform>();
            foreach (Transform t in GetComponentsInChildren<Transform>(true))
            {
                if (!byName.ContainsKey(t.name))
                    byName[t.name] = t;
            }

            _groups.Clear();
            _rods.Clear();

            if (wheelNamingMode == WheelNamingMode.Manual)
            {
                if (!TryMeasureManualWheels())
                {
                    Debug.LogError("[TrainWheelRig] Manual mode needs at least 2 manual axles with wheel transforms assigned.");
                    enabled = false;
                    return;
                }
            }
            else
            {
                if (wheelNamingMode != WheelNamingMode.AuroraNumeric)
                    TryMeasureGeoWheels(byName);

                if (_groups.Count < 2)
                {
                    _groups.Clear();
                    if (!TryMeasureAuroraWheels())
                    {
                        Debug.LogError("[TrainWheelRig] Need GEO_Wheel_* names, Aurora wheel01.. meshes, or Manual axles.");
                        enabled = false;
                        return;
                    }
                }
            }

            if (!FinalizeWheelGroups(byName))
            {
                enabled = false;
                return;
            }

            _measured = true;
        }

        private void TryMeasureGeoWheels(Dictionary<string, Transform> byName)
        {
            foreach (string key in AxleKeys)
            {
                byName.TryGetValue($"GEO_Wheel_{key}_L", out Transform left);
                byName.TryGetValue($"GEO_Wheel_{key}_R", out Transform right);
                byName.TryGetValue($"GEO_Axle_{key}", out Transform axle);
                if (left == null && right == null)
                    continue;

                if (!TryBuildAxleGroup(key, key.StartsWith("Lead") || key.StartsWith("Trail"), left, right, axle, out AxleGroup group))
                    continue;

                _groups.Add(group);
            }
        }

        private bool TryBuildAxleGroup(
            string key,
            bool isTruck,
            Transform left,
            Transform right,
            Transform axle,
            out AxleGroup group,
            ManualWheelPoseMode poseMode = ManualWheelPoseMode.OrbitBounds)
        {
            group = new AxleGroup { Key = key, IsTruck = isTruck, PoseMode = poseMode };

            Vector3 centerSum = Vector3.zero;
            float radiusSum = 0f;
            int wheels = 0;
            foreach (Transform wheel in new[] { left, right })
            {
                if (wheel == null || !TryGetLocalBounds(wheel, out Bounds b))
                    continue;
                centerSum += b.center;
                radiusSum += b.extents.y;
                wheels++;
            }

            if (wheels == 0)
                return false;

            group.Center = centerSum / wheels;
            group.Radius = radiusSum / wheels;

            if (axle != null)
                group.Parts.Add(CapturePart(axle));
            if (left != null)
                group.Parts.Add(CapturePart(left));
            if (right != null && right != left)
                group.Parts.Add(CapturePart(right));

            if (left != null && right != null &&
                TryGetLocalBounds(left, out Bounds lb) && TryGetLocalBounds(right, out Bounds rb))
                group.WheelSpan = lb.center - rb.center;

            return true;
        }

        private bool TryMeasureAuroraWheels()
        {
            var wheels = new List<(Transform transform, Vector3 center, float radius)>();
            foreach (Transform t in GetComponentsInChildren<Transform>(true))
            {
                if (t == transform)
                    continue;

                if (!System.Text.RegularExpressions.Regex.IsMatch(t.name, @"^wheel\d+$", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                    continue;

                if (!TryGetLocalBounds(t, out Bounds b))
                    continue;

                wheels.Add((t, b.center, b.extents.y));
            }

            if (wheels.Count < 2)
                return false;

            Vector3 min = wheels[0].center;
            Vector3 max = wheels[0].center;
            foreach (var w in wheels)
            {
                min = Vector3.Min(min, w.center);
                max = Vector3.Max(max, w.center);
            }

            _forwardLocal = Vector3.ProjectOnPlane(max - min, Vector3.up);
            if (_forwardLocal.sqrMagnitude < 1e-6f)
                _forwardLocal = Vector3.forward;
            _forwardLocal.Normalize();
            _lateralLocal = Vector3.Cross(Vector3.up, _forwardLocal).normalized;

            wheels.Sort((a, b) =>
            {
                float alongA = Vector3.Dot(a.center, _forwardLocal);
                float alongB = Vector3.Dot(b.center, _forwardLocal);
                return alongB.CompareTo(alongA);
            });

            var axles = new List<List<(Transform transform, Vector3 center, float radius)>>();
            List<(Transform transform, Vector3 center, float radius)> current = null;
            float clusterAlong = 0f;

            foreach (var w in wheels)
            {
                float along = Vector3.Dot(w.center, _forwardLocal);
                if (current == null || Mathf.Abs(along - clusterAlong) > auroraAxleClusterTolerance)
                {
                    current = new List<(Transform, Vector3, float)> { w };
                    axles.Add(current);
                    clusterAlong = along;
                }
                else
                {
                    current.Add(w);
                }
            }

            if (axles.Count < 2)
                return false;

            int axleCount = axles.Count;
            for (int i = 0; i < axleCount; i++)
            {
                string key = ClassifyAuroraAxleKey(i, axleCount);
                bool isTruck = key.StartsWith("Lead") || key.StartsWith("Trail");

                Vector3 centerSum = Vector3.zero;
                int count = axles[i].Count;
                foreach (var w in axles[i])
                    centerSum += w.center;

                float midLateral = Vector3.Dot(centerSum / count, _lateralLocal);

                Transform left = null;
                Transform right = null;
                foreach (var w in axles[i])
                {
                    float lateral = Vector3.Dot(w.center, _lateralLocal);
                    if (lateral <= midLateral)
                    {
                        if (left == null || lateral < Vector3.Dot(transform.InverseTransformPoint(left.position), _lateralLocal))
                            left = w.transform;
                    }
                    else if (right == null || lateral > Vector3.Dot(transform.InverseTransformPoint(right.position), _lateralLocal))
                        right = w.transform;
                }

                if (left == null)
                    left = right;
                if (right == null)
                    right = left;

                if (!TryBuildAxleGroup(key, isTruck, left, right, null, out AxleGroup group))
                    continue;

                _groups.Add(group);
            }

            return _groups.Count >= 2;
        }

        private bool TryMeasureManualWheels()
        {
            if (manualAxles == null || manualAxles.Count < 2)
                return false;

            int leadIndex = 0;
            int trailIndex = 0;
            int driveIndex = 0;
            int built = 0;

            for (int i = 0; i < manualAxles.Count; i++)
            {
                ManualAxleDefinition def = manualAxles[i];
                if (def.wheelParts == null || def.wheelParts.Length == 0)
                    continue;

                Transform left = null;
                Transform right = null;
                int partCount = 0;
                foreach (Transform t in def.wheelParts)
                {
                    if (t == null)
                        continue;
                    partCount++;
                    if (left == null)
                        left = t;
                    else if (right == null)
                        right = t;
                }

                if (partCount == 0)
                    continue;

                bool isTruck = def.role == ManualAxleRole.Lead || def.role == ManualAxleRole.Trail;
                string key = ManualAxleKey(def, ref leadIndex, ref trailIndex, ref driveIndex);

                if (!TryBuildAxleGroup(key, isTruck, left, right, null, out AxleGroup group, def.poseMode))
                    continue;

                _groups.Add(group);
                built++;
            }

            return built >= 2;
        }

        private static string ManualAxleKey(ManualAxleDefinition def, ref int leadIndex, ref int trailIndex, ref int driveIndex)
        {
            if (!string.IsNullOrWhiteSpace(def.label))
                return def.label.Trim();

            switch (def.role)
            {
                case ManualAxleRole.Lead:
                    leadIndex++;
                    return $"Lead{leadIndex}";
                case ManualAxleRole.Trail:
                    trailIndex++;
                    return $"Trail{trailIndex}";
                default:
                    driveIndex++;
                    return $"Drive{driveIndex}";
            }
        }

        private static string ClassifyAuroraAxleKey(int index, int count)
        {
            int leadCount = count >= 6 ? 2 : 1;
            int trailCount = count >= 6 ? 2 : 1;
            if (index < leadCount)
                return leadCount > 1 && index == 1 ? "Lead2" : "Lead1";
            if (index >= count - trailCount)
            {
                if (trailCount > 1 && index == count - 2)
                    return "Trail1";
                return trailCount > 1 ? "Trail2" : "Trail1";
            }

            int driveIndex = index - leadCount + 1;
            return $"Drive{Mathf.Clamp(driveIndex, 1, 4)}";
        }

        private bool FinalizeWheelGroups(Dictionary<string, Transform> byName)
        {
            if (_groups.Count < 2)
                return false;

            _lead = _groups.Find(g => g.Key.StartsWith("Lead")) ?? _groups[0];
            _trail = _groups.FindLast(g => g.Key.StartsWith("Trail")) ?? _groups[_groups.Count - 1];
            _drive = _groups.Find(g => g.Key.StartsWith("Drive"));

            _forwardLocal = Vector3.ProjectOnPlane(_lead.Center - _trail.Center, Vector3.up);
            _forwardLocal = _forwardLocal.sqrMagnitude > 1e-6f ? _forwardLocal.normalized : Vector3.forward;
            _lateralLocal = Vector3.Cross(Vector3.up, _forwardLocal).normalized;

            AxleGroup gaugeGroup = _drive ?? _lead;
            _wheelGaugeLocal = gaugeGroup.WheelSpan.sqrMagnitude > 1e-6f
                ? Mathf.Abs(Vector3.Dot(gaugeGroup.WheelSpan, _lateralLocal))
                : 1f;

            Vector3 reference = (_lead.Center + _trail.Center) * 0.5f;
            float contactY = float.MaxValue;
            foreach (AxleGroup g in _groups)
                contactY = Mathf.Min(contactY, g.Center.y - g.Radius);

            _alongMin = float.MaxValue;
            _alongMax = float.MinValue;
            foreach (AxleGroup g in _groups)
            {
                g.Along = Vector3.Dot(g.Center - reference, _forwardLocal);
                _alongMin = Mathf.Min(_alongMin, g.Along);
                _alongMax = Mathf.Max(_alongMax, g.Along);

                Vector3 contact = new Vector3(g.Center.x, contactY, g.Center.z);
                contact -= _lateralLocal * Vector3.Dot(contact - reference, _lateralLocal);
                g.Contact = contact;
            }

            MeasureRods(byName);
            MeasureAuroraCouplingRods();
            return true;
        }

        private void MeasureAuroraCouplingRods()
        {
            if (_drive == null)
                return;

            float driveAlong = _drive.Along;
            Vector3 reference = (_lead.Center + _trail.Center) * 0.5f;

            var leftRods = new List<Transform>();
            var rightRods = new List<Transform>();

            foreach (Transform t in GetComponentsInChildren<Transform>(true))
            {
                string normalized = t.name.Replace(" ", string.Empty);
                if (!normalized.StartsWith("couplingrod", System.StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!TryGetLocalBounds(t, out Bounds b))
                    continue;

                float lateral = Vector3.Dot(b.center, _lateralLocal);
                if (lateral <= 0f)
                    leftRods.Add(t);
                else
                    rightRods.Add(t);
            }

            RegisterAuroraSideRods(leftRods, 0f, driveAlong, reference);
            RegisterAuroraSideRods(rightRods, 90f * Mathf.Deg2Rad, driveAlong, reference);

            TryRegisterAuroraMainRod(auroraMainRodLeft, 0f, driveAlong, reference);
            TryRegisterAuroraMainRod(auroraMainRodRight, 90f * Mathf.Deg2Rad, driveAlong, reference);
        }

        private void RegisterAuroraSideRods(List<Transform> rods, float phaseRad, float driveAlong, Vector3 reference)
        {
            if (rods.Count == 0)
                return;

            rods.Sort((a, b) =>
            {
                if (!TryGetLocalBounds(a, out Bounds ba) || !TryGetLocalBounds(b, out Bounds bb))
                    return 0;
                float alongA = Vector3.Dot(ba.center - reference, _forwardLocal);
                float alongB = Vector3.Dot(bb.center - reference, _forwardLocal);
                return Mathf.Abs(alongA - driveAlong).CompareTo(Mathf.Abs(alongB - driveAlong));
            });

            foreach (Transform rod in rods)
                _rods.Add(new Rod { Part = CapturePart(rod), Phase = phaseRad });
        }

        private void TryRegisterAuroraMainRod(Transform main, float phaseRad, float driveAlong, Vector3 reference)
        {
            if (main == null || !TryGetLocalCorners(main, out List<Vector3> corners))
                return;

            float minProj = float.MaxValue;
            float maxProj = float.MinValue;
            Vector3 center = Vector3.zero;
            foreach (Vector3 c in corners)
            {
                float p = Vector3.Dot(c, _forwardLocal);
                minProj = Mathf.Min(minProj, p);
                maxProj = Mathf.Max(maxProj, p);
                center += c;
            }

            center /= corners.Count;
            float centerProj = Vector3.Dot(center, _forwardLocal);
            Vector3 endA = center + _forwardLocal * (minProj - centerProj);
            Vector3 endB = center + _forwardLocal * (maxProj - centerProj);

            float alongA = Vector3.Dot(endA - reference, _forwardLocal);
            float alongB = Vector3.Dot(endB - reference, _forwardLocal);
            bool aIsCrank = Mathf.Abs(alongA - driveAlong) <= Mathf.Abs(alongB - driveAlong);

            _rods.Add(new Rod
            {
                Part = CapturePart(main),
                IsMain = true,
                Phase = phaseRad,
                CrankEnd = aIsCrank ? endA : endB,
                CrossheadEnd = aIsCrank ? endB : endA
            });
        }

        /// <summary>Clears cached axle data so <see cref="Measure"/> runs again (Editor tooling).</summary>
        public void ResetMeasurement()
        {
            _measured = false;
            _hasAnimDistance = false;
            _bound = false;
            _groups.Clear();
            _rods.Clear();
        }

        public void LogAuroraWheelAxles()
        {
            ResetMeasurement();
            Measure();
            if (!_measured)
                return;

            Debug.Log($"[TrainWheelRig] Axles: {_groups.Count}, rods: {_rods.Count}, gauge(local): {_wheelGaugeLocal:F3}, mode: {wheelNamingMode}");
            foreach (AxleGroup g in _groups)
                Debug.Log($"  {g.Key} truck={g.IsTruck} pose={g.PoseMode} radius={g.Radius:F3} along={g.Along:F3} parts={g.Parts.Count}");
        }

        [ContextMenu("Log Wheel Axles")]
        private void LogAuroraWheelAxlesContext() => LogAuroraWheelAxles();

#if UNITY_EDITOR
        public void UseManualWheelMode()
        {
            wheelNamingMode = WheelNamingMode.Manual;
            ResetMeasurement();
        }

        public void AppendManualAxleFromSelection(ManualAxleRole role, ManualWheelPoseMode poseMode)
        {
            var transforms = new List<Transform>();
            foreach (GameObject go in UnityEditor.Selection.gameObjects)
            {
                if (go != null && go.transform.IsChildOf(transform))
                    transforms.Add(go.transform);
            }

            if (transforms.Count == 0)
            {
                Debug.LogWarning("[TrainWheelRig] Select wheel transforms under this locomotive to add a manual axle.");
                return;
            }

            manualAxles.Add(new ManualAxleDefinition
            {
                label = string.Empty,
                wheelParts = transforms.ToArray(),
                role = role,
                poseMode = poseMode
            });
            ResetMeasurement();
            UnityEditor.EditorUtility.SetDirty(this);
        }
#endif

        private void MeasureRods(Dictionary<string, Transform> byName)
        {
            _rods.Clear();
            float driveAlong = 0f;
            int driveCount = 0;
            foreach (AxleGroup g in _groups)
            {
                if (g.IsTruck)
                    continue;
                driveAlong += g.Along;
                driveCount++;
            }
            if (driveCount > 0)
                driveAlong /= driveCount;

            Vector3 reference = (_lead.Center + _trail.Center) * 0.5f;

            foreach (string side in new[] { "L", "R" })
            {
                float phase = side == "L" ? 0f : 90f * Mathf.Deg2Rad;

                if (byName.TryGetValue($"GEO_CouplingRod_{side}", out Transform coupling))
                    _rods.Add(new Rod { Part = CapturePart(coupling), Phase = phase });

                if (!byName.TryGetValue($"GEO_MainRod_{side}", out Transform main) ||
                    !TryGetLocalCorners(main, out List<Vector3> corners))
                    continue;

                float minProj = float.MaxValue;
                float maxProj = float.MinValue;
                Vector3 center = Vector3.zero;
                foreach (Vector3 c in corners)
                {
                    float p = Vector3.Dot(c, _forwardLocal);
                    minProj = Mathf.Min(minProj, p);
                    maxProj = Mathf.Max(maxProj, p);
                    center += c;
                }
                center /= corners.Count;

                float centerProj = Vector3.Dot(center, _forwardLocal);
                Vector3 endA = center + _forwardLocal * (minProj - centerProj);
                Vector3 endB = center + _forwardLocal * (maxProj - centerProj);

                float alongA = Vector3.Dot(endA - reference, _forwardLocal);
                float alongB = Vector3.Dot(endB - reference, _forwardLocal);
                bool aIsCrank = Mathf.Abs(alongA - driveAlong) <= Mathf.Abs(alongB - driveAlong);

                _rods.Add(new Rod
                {
                    Part = CapturePart(main),
                    IsMain = true,
                    Phase = phase,
                    CrankEnd = aIsCrank ? endA : endB,
                    CrossheadEnd = aIsCrank ? endB : endA
                });
            }
        }

        private Part CapturePart(Transform t)
        {
            return new Part
            {
                Transform = t,
                RestPosition = transform.InverseTransformPoint(t.position),
                RestRotation = Quaternion.Inverse(transform.rotation) * t.rotation
            };
        }

        private bool TryGetLocalBounds(Transform part, out Bounds bounds)
        {
            bounds = default;
            if (!TryGetLocalCorners(part, out List<Vector3> corners))
                return false;

            bounds = new Bounds(corners[0], Vector3.zero);
            for (int i = 1; i < corners.Count; i++)
                bounds.Encapsulate(corners[i]);
            return true;
        }

        private bool TryGetLocalCorners(Transform part, out List<Vector3> corners)
        {
            corners = null;
            MeshFilter filter = part.GetComponent<MeshFilter>();
            Mesh mesh = filter != null ? filter.sharedMesh : null;

            Bounds source;
            Matrix4x4 toWorld;
            if (mesh != null)
            {
                source = mesh.bounds;
                toWorld = part.localToWorldMatrix;
            }
            else
            {
                Renderer r = part.GetComponent<Renderer>();
                if (r == null)
                    return false;
                source = r.bounds;
                toWorld = Matrix4x4.identity;
            }

            corners = new List<Vector3>(8);
            Vector3 min = source.min;
            Vector3 max = source.max;
            for (int i = 0; i < 8; i++)
            {
                var c = new Vector3(
                    (i & 1) == 0 ? min.x : max.x,
                    (i & 2) == 0 ? min.y : max.y,
                    (i & 4) == 0 ? min.z : max.z);
                corners.Add(transform.InverseTransformPoint(toWorld.MultiplyPoint3x4(c)));
            }

            return true;
        }

        [ContextMenu("Preview On Track")]
        private void PreviewOnTrack()
        {
            var driver = GetComponent<TrainSplineDriver>();
            SplineContainer track = driver != null ? driver.Track : null;
            if (track == null)
                track = TrainSplineDriver.FindRailTrack();
            if (track == null)
            {
                Debug.LogWarning("[TrainWheelRig] No rail spline found for preview.");
                return;
            }

#if UNITY_EDITOR
            UnityEditor.Undo.RecordObject(transform, "Preview Train On Track");
#endif
            if (!Bind(track, driver != null ? driver.SplineIndex : 0))
                return;
            ComputeRootPose(NearestReferenceDistance(), out Vector3 position, out Quaternion rotation);
            transform.SetPositionAndRotation(position, rotation);
        }
    }
}
