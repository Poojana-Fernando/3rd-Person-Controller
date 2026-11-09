using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Splines;

namespace Game.Train
{
    /// <summary>
    /// Drives the train along the rail spline from a throttle (-1 back, 0 coast, +1 forward) and a brake.
    /// Like a real reverser, pushing the opposite direction first slows the train to a stop before it reverses.
    /// Braking ramps up <see cref="WheelLock"/> so the wheels (and rods) freeze and the train skids to a stop.
    /// With locomotive mass configured, speed follows weight, grade gravity, traction, and rolling resistance along the spline.
    /// Position is an arc-length distance so speed stays constant through curves;
    /// <see cref="TrainWheelRig"/> turns that distance into a pose with every wheel on the rails.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(TrainWheelRig))]
    public class TrainSplineDriver : MonoBehaviour
    {
        [SerializeField] private SplineContainer track;
        [SerializeField] private int splineIndex;

        [Header("Speed (m/s)")]
        [SerializeField] private float maxSpeed = 12f;
        [SerializeField] private float maxReverseSpeed = 6f;

        [Header("Mass and traction")]
        [Tooltip("Locomotive mass (kg). 0 = use legacy acceleration fields below.")]
        [SerializeField] private float locomotiveMass = 100000f;
        [Tooltip("Maximum tractive effort (N) at full throttle.")]
        [SerializeField] private float maxTractiveForce = 120000f;
        [Tooltip("Rolling resistance coefficient (fraction of weight).")]
        [SerializeField] private float rollingResistanceCoeff = 0.003f;
        [Tooltip("Maximum service brake force (N) at full wheel lock.")]
        [SerializeField] private float maxBrakeForce = 150000f;
        [Tooltip("Extra coasting slowdown (m/s²) with no throttle on level track.")]
        [SerializeField] private float coastDeceleration = 0.08f;

        [Header("Forces (legacy, used when locomotive mass is 0)")]
        [SerializeField] private float acceleration = 1.2f;
        [Tooltip("Slowdown while coasting with no throttle.")]
        [SerializeField] private float rollingResistance = 0.15f;
        [Tooltip("Slowdown applied when the opposite direction is requested while still moving.")]
        [SerializeField] private float reverseBlockDecel = 1f;
        [SerializeField] private float brakeDecel = 2.5f;

        [Header("Brakes")]
        [Tooltip("Seconds for the wheels to fully lock while braking.")]
        [SerializeField] private float wheelLockTime = 1.2f;
        [Tooltip("Seconds for locked wheels to roll freely again after releasing the brake.")]
        [SerializeField] private float wheelReleaseTime = 0.3f;

        private const float EndTolerance = 0.02f;
        private const float StopEpsilon = 0.01f;
        private const float Gravity = 9.81f;

        private Rigidbody _body;
        private TrainWheelRig _rig;
        private float _distance;
        private float _velocity;
        private float _wheelLock;
        private int _throttle;
        private bool _brake;
        private bool _initialized;
        private readonly List<TrainCarFollower> _cars = new List<TrainCarFollower>();
        private RailwayNetwork _railNetwork;

        public float Distance => _distance;
        /// <summary>Signed speed in m/s; positive is toward the train's front.</summary>
        public float Velocity => _velocity;
        /// <summary>0 = wheels rolling freely, 1 = fully locked.</summary>
        public float WheelLock => _wheelLock;
        public int Throttle => _throttle;
        public bool Brake => _brake;
        public float MaxSpeed => maxSpeed;
        public SplineContainer Track => track;
        public int SplineIndex => splineIndex;
        public TrainWheelRig Rig => _rig;
        public RailRoute Route => _rig != null ? _rig.Route : null;

        /// <summary>Lowest distance at which the locomotive and every coupled car still have their wheels on the track.</summary>
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

        public void RegisterCar(TrainCarFollower car)
        {
            if (car != null && !_cars.Contains(car))
                _cars.Add(car);
        }

        public void UnregisterCar(TrainCarFollower car)
        {
            _cars.Remove(car);
        }

        private void GetDistanceRange(out float min, out float max)
        {
            min = _rig.MinDistance;
            max = _rig.MaxDistance;
            float length = _rig.TrackLength;
            int facing = _rig.Facing;

            foreach (TrainCarFollower car in _cars)
            {
                if (car == null || !car.isActiveAndEnabled)
                    continue;
                float center = facing * car.FrontRelativeOffset;
                min = Mathf.Max(min, car.BogieHalfSpacing - center);
                max = Mathf.Min(max, length - car.BogieHalfSpacing - center);
            }

            if (min > max)
                min = max = (min + max) * 0.5f;
        }

        public void SetTrack(SplineContainer container)
        {
            track = container;
            _initialized = false;
        }

        /// <summary>Bind to a spline and place at arc-length distance (editor / dev spawn).</summary>
        public bool TryConfigureSpawn(int splineIdx, float routeDistance)
        {
            if (_rig == null)
                _rig = GetComponent<TrainWheelRig>();
            if (track == null)
                track = FindRailTrack();
            if (track == null || _rig == null)
                return false;

            splineIndex = Mathf.Clamp(splineIdx, 0, Mathf.Max(0, track.Splines.Count - 1));
            _initialized = false;
            if (!_rig.Bind(track, splineIndex))
                return false;

            _velocity = 0f;
            _throttle = 0;
            _brake = false;
            _distance = Mathf.Clamp(routeDistance, _rig.MinDistance, _rig.MaxDistance);
            _rig.ApplyPose(_distance, false);
            _initialized = true;
            return true;
        }

        private void Awake()
        {
            _body = GetComponent<Rigidbody>();
            _rig = GetComponent<TrainWheelRig>();
            _body.isKinematic = true;
            _body.interpolation = RigidbodyInterpolation.Interpolate;
        }

        private void Start()
        {
            EnsureRailNetwork();
            EnsureInitialized();
        }

        private void EnsureRailNetwork()
        {
            _railNetwork = RailwayNetwork.Resolve();
            if (_railNetwork == null && track == null)
                track = FindRailTrack();

            if (_railNetwork == null)
                Debug.LogWarning("[TrainSplineDriver] No RailwayNetwork in scene; junction routing will not work.");
        }

        public void ApplyRouteSnapshot(RailRoute source)
        {
            if (source == null || _rig == null)
                return;
            _rig.ApplyRoute(source);
            _distance = Mathf.Clamp(_distance, MinDistance, MaxDistance);
        }

        public bool EnsureInitialized()
        {
            if (_initialized)
                return true;

            if (track == null)
                track = FindRailTrack();
            if (track == null || _rig == null)
                return false;

            if (!_rig.Bind(track, splineIndex))
                return false;

            _distance = _rig.NearestReferenceDistance();
            _rig.ApplyPose(_distance, false);
            _initialized = true;
            return true;
        }

        public static SplineContainer FindRailTrack()
        {
            GameObject trainTrack = GameObject.Find("TrainTrack");
            if (trainTrack != null && trainTrack.TryGetComponent(out SplineContainer primary))
                return primary;

            SplineContainer best = null;
            int bestScore = -1;
            SplineInstantiate[] instantiators = FindObjectsByType<SplineInstantiate>();
            foreach (SplineInstantiate inst in instantiators)
            {
                if (inst == null || inst.Container == null)
                    continue;

                bool railway = false;
                foreach (SplineInstantiate.InstantiableItem item in inst.itemsToInstantiate)
                {
                    if (item.Prefab != null && item.Prefab.name.ToLowerInvariant().Contains("rail"))
                    {
                        railway = true;
                        break;
                    }
                }

                if (!railway)
                    continue;

                SplineContainer container = inst.Container;
                int score = container.Splines.Count * 1000;
                for (int i = 0; i < container.Splines.Count; i++)
                    score += (int)container.CalculateLength(i);

                if (score > bestScore)
                {
                    bestScore = score;
                    best = container;
                }
            }

            return best != null ? best : FindAnyObjectByType<SplineContainer>();
        }

        /// <summary>Set the driver's controls. <paramref name="throttle"/>: -1 backward, 0 coast, +1 forward.</summary>
        public void SetControls(int throttle, bool brake)
        {
            _throttle = throttle > 0 ? 1 : throttle < 0 ? -1 : 0;
            _brake = brake;
        }

        private void FixedUpdate()
        {
            if (_railNetwork == null)
                EnsureRailNetwork();

            if (!_initialized)
                return;

            float previous = _distance;
            Step(Time.fixedDeltaTime);
            _rig.WheelSpinScale = 1f - _wheelLock;

            if (!Mathf.Approximately(previous, _distance))
                _rig.ApplyPose(_distance, true);
        }

        /// <summary>Clients apply the server's authoritative state.</summary>
        public void ApplyRemoteState(float distance, float velocity, float wheelLock, bool fromFixedUpdate = false)
        {
            if (!EnsureInitialized())
                return;

            _distance = Mathf.Clamp(distance, MinDistance, MaxDistance);
            _velocity = velocity;
            _wheelLock = Mathf.Clamp01(wheelLock);
            _rig.WheelSpinScale = 1f - _wheelLock;
            _rig.ApplyPose(_distance, fromFixedUpdate);
        }

        private void Step(float dt)
        {
            if (UsesMassPhysics())
                StepMassPhysics(dt);
            else
                StepLegacyVelocity(dt);

            ApplyEndOfTrackBrake(dt);
            IntegrateDistance(dt);
        }

        private bool UsesMassPhysics() => locomotiveMass > 0f;

        private float GetTotalMass()
        {
            float total = locomotiveMass;
            foreach (TrainCarFollower car in _cars)
            {
                if (car == null || !car.isActiveAndEnabled)
                    continue;
                total += car.Mass;
            }

            return Mathf.Max(total, 1f);
        }

        private float SampleGradeSin(float routeDistance)
        {
            if (_rig == null || !_rig.IsBound)
                return 0f;

            _rig.SampleRailPoint(routeDistance, out _, out Vector3 tangent, out _);
            if (tangent.sqrMagnitude < 1e-8f)
                return 0f;

            return Vector3.Dot(tangent.normalized, Vector3.up);
        }

        private float EffectiveBrakeDecel()
        {
            if (UsesMassPhysics())
                return maxBrakeForce / GetTotalMass();
            return brakeDecel;
        }

        private void StepMassPhysics(float dt)
        {
            if (_brake)
                _wheelLock = Mathf.MoveTowards(_wheelLock, 1f, dt / Mathf.Max(0.01f, wheelLockTime));
            else
                _wheelLock = Mathf.MoveTowards(_wheelLock, 0f, dt / Mathf.Max(0.01f, wheelReleaseTime));

            float totalMass = GetTotalMass();
            int facing = _rig.Facing;
            float gradeLoco = SampleGradeSin(_distance);

            float gravityForce = -locomotiveMass * Gravity * gradeLoco;
            foreach (TrainCarFollower car in _cars)
            {
                if (car == null || !car.isActiveAndEnabled)
                    continue;

                float carCenter = _distance + facing * car.FrontRelativeOffset;
                float gradeCar = SampleGradeSin(carCenter);
                gravityForce += -car.Mass * Gravity * gradeCar;
            }

            float tractionForce = 0f;
            if (!_brake)
            {
                if (_throttle != 0 && _velocity * _throttle < -StopEpsilon)
                {
                    float block = reverseBlockDecel * totalMass;
                    tractionForce = -Mathf.Sign(_velocity) * block;
                }
                else if (_throttle != 0)
                {
                    tractionForce = maxTractiveForce * _throttle;
                }
            }

            float resistanceForce = 0f;
            if (Mathf.Abs(_velocity) > StopEpsilon)
                resistanceForce = -Mathf.Sign(_velocity) * rollingResistanceCoeff * totalMass * Gravity;

            if (!_brake && _throttle == 0 && Mathf.Abs(_velocity) > StopEpsilon)
                resistanceForce += -Mathf.Sign(_velocity) * coastDeceleration * totalMass;

            float brakeForceApplied = 0f;
            if (_brake && Mathf.Abs(_velocity) > StopEpsilon)
                brakeForceApplied = -Mathf.Sign(_velocity) * maxBrakeForce * Mathf.Max(_wheelLock, 0.35f);

            float gravityAccelTrain = gravityForce / totalMass * facing;
            float netForceTrain = tractionForce + resistanceForce + brakeForceApplied;
            _velocity += (netForceTrain / totalMass + gravityAccelTrain) * dt;

            ClampSpeedToLimits();
        }

        private void ClampSpeedToLimits()
        {
            if (_velocity > 0f)
                _velocity = Mathf.Min(_velocity, maxSpeed);
            else if (_velocity < 0f)
                _velocity = Mathf.Max(_velocity, -maxReverseSpeed);
        }

        private void StepLegacyVelocity(float dt)
        {
            if (_brake)
            {
                _velocity = Mathf.MoveTowards(_velocity, 0f, brakeDecel * dt);
                _wheelLock = Mathf.MoveTowards(_wheelLock, 1f, dt / Mathf.Max(0.01f, wheelLockTime));
            }
            else
            {
                _wheelLock = Mathf.MoveTowards(_wheelLock, 0f, dt / Mathf.Max(0.01f, wheelReleaseTime));

                if (_throttle != 0 && _velocity * _throttle < -StopEpsilon)
                {
                    _velocity = Mathf.MoveTowards(_velocity, 0f, reverseBlockDecel * dt);
                }
                else if (_throttle != 0)
                {
                    float target = _throttle > 0 ? maxSpeed : -maxReverseSpeed;
                    float rate = Mathf.Abs(_velocity) > Mathf.Abs(target) ? rollingResistance : acceleration;
                    _velocity = Mathf.MoveTowards(_velocity, target, rate * dt);
                }
                else
                {
                    _velocity = Mathf.MoveTowards(_velocity, 0f, rollingResistance * dt);
                }
            }
        }

        private void IntegrateDistance(float dt)
        {
            RailwayNetwork resolved = RailwayNetwork.Resolve();
            if (resolved != null)
                _railNetwork = resolved;

            float previous = _distance;
            float next = _distance + _velocity * _rig.Facing * dt;
            if (_railNetwork != null)
                next = _railNetwork.ProcessJunctionCrossings(_rig.Route, previous, next, _rig.Facing, _velocity);
            _rig.SyncRouteLength();

            GetDistanceRange(out float min, out float max);
            if (_railNetwork != null && Mathf.Abs(_velocity) > StopEpsilon)
            {
                int travelSign = _velocity * _rig.Facing > 0f ? 1 : -1;
                float ahead = _railNetwork.AvailableLengthAhead(_rig.Route, _distance, travelSign);
                if (travelSign > 0)
                    max = Mathf.Min(max, _distance + ahead);
                else
                    min = Mathf.Max(min, _distance - ahead);
            }
            else if (_railNetwork != null && Mathf.Abs(_velocity) <= StopEpsilon)
            {
                int travelSign = _rig.Facing;
                float ahead = _railNetwork.AvailableLengthAhead(_rig.Route, _distance, travelSign);
                max = Mathf.Min(max, _distance + ahead);
            }

            if (next <= min || next >= max)
                _velocity = 0f;
            _distance = Mathf.Clamp(next, min, max);
        }

        private void ApplyEndOfTrackBrake(float dt)
        {
            if (Mathf.Abs(_velocity) <= StopEpsilon)
                return;

            float remaining = RemainingToward(_velocity > 0f ? 1 : -1);
            float speed = Mathf.Abs(_velocity);
            if (remaining <= EndTolerance)
            {
                _velocity = 0f;
                return;
            }

            float brakeRate = EffectiveBrakeDecel();
            float stopDistance = speed * speed / (2f * brakeRate);
            if (remaining > stopDistance + speed * dt)
                return;

            float decel = Mathf.Max(brakeRate * 0.25f, speed * speed / (2f * remaining));
            _velocity = Mathf.MoveTowards(_velocity, 0f, decel * dt);
        }

        /// <summary>Track left in front of (+1) or behind (-1) the train, including coupled cars.</summary>
        private float RemainingToward(int frontRelativeSign)
        {
            if (_rig == null)
                return 0f;
            GetDistanceRange(out float min, out float max);
            int splineDir = frontRelativeSign * _rig.Facing;
            if (_railNetwork != null)
                return _railNetwork.AvailableLengthAhead(_rig.Route, _distance, splineDir);
            return splineDir > 0 ? max - _distance : _distance - min;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            maxSpeed = Mathf.Max(0.1f, maxSpeed);
            maxReverseSpeed = Mathf.Max(0.1f, maxReverseSpeed);
            locomotiveMass = Mathf.Max(0f, locomotiveMass);
            maxTractiveForce = Mathf.Max(0f, maxTractiveForce);
            rollingResistanceCoeff = Mathf.Max(0f, rollingResistanceCoeff);
            maxBrakeForce = Mathf.Max(0f, maxBrakeForce);
            coastDeceleration = Mathf.Max(0f, coastDeceleration);
            acceleration = Mathf.Max(0.05f, acceleration);
            rollingResistance = Mathf.Max(0f, rollingResistance);
            reverseBlockDecel = Mathf.Max(0.05f, reverseBlockDecel);
            brakeDecel = Mathf.Max(0.05f, brakeDecel);
        }
#endif
    }
}
