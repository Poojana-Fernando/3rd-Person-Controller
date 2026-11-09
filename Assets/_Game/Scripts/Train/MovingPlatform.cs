using Unity.Netcode;
using UnityEngine;

namespace Game.Train
{
    /// <summary>
    /// Reports how far a point standing on this object moved during the current physics step,
    /// so riders (see PlatformRider) can be carried along without relying on physics friction.
    /// </summary>
    // Must run after the train drivers set this step's target pose (order 0) and before riders (order 100).
    [DefaultExecutionOrder(50)]
    [DisallowMultipleComponent]
    public class MovingPlatform : MonoBehaviour
    {
        private TrainWheelRig _rig;
        private TrainCarFollower _car;
        private Rigidbody _body;

        private Vector3 _prevPosition;
        private Quaternion _prevRotation = Quaternion.identity;
        private Vector3 _currPosition;
        private Quaternion _currRotation = Quaternion.identity;
        private bool _hasPrev;
        private bool _hasCurr;

        private NetworkObject _networkObject;

        /// <summary>Network identity riders reference when syncing their pose relative to this platform.</summary>
        public NetworkObject NetworkObject => _networkObject;

        private void Awake()
        {
            _rig = GetComponent<TrainWheelRig>();
            _car = GetComponent<TrainCarFollower>();
            _body = GetComponent<Rigidbody>();
            _networkObject = GetComponentInParent<NetworkObject>();
        }

        /// <summary>Current physics-step pose (not the render-interpolated transform).</summary>
        public void GetPhysicsPose(out Vector3 position, out Quaternion rotation)
        {
            if (_hasCurr)
            {
                position = _currPosition;
                rotation = _currRotation;
                return;
            }

            ReadPose(out position, out rotation);
        }

        private void OnEnable()
        {
            _hasPrev = false;
            _hasCurr = false;
        }

        private void FixedUpdate()
        {
            ReadPose(out Vector3 position, out Quaternion rotation);

            _prevPosition = _hasCurr ? _currPosition : position;
            _prevRotation = _hasCurr ? _currRotation : rotation;
            _hasPrev = true;

            _currPosition = position;
            _currRotation = rotation;
            _hasCurr = true;
        }

        private void ReadPose(out Vector3 position, out Quaternion rotation)
        {
            if (_rig != null && _rig.HasTarget)
            {
                position = _rig.TargetPosition;
                rotation = _rig.TargetRotation;
                return;
            }

            if (_car != null && _car.HasTarget)
            {
                position = _car.TargetPosition;
                rotation = _car.TargetRotation;
                return;
            }

            if (_body != null)
            {
                position = _body.position;
                rotation = _body.rotation;
                return;
            }

            position = transform.position;
            rotation = transform.rotation;
        }

        /// <summary>
        /// Displacement of <paramref name="worldPoint"/> this physics step if it were fixed to the platform,
        /// plus the yaw-only part of the platform's rotation (keeps riders upright).
        /// </summary>
        public bool TryGetStepDelta(Vector3 worldPoint, out Vector3 delta, out Quaternion yawDelta)
        {
            delta = Vector3.zero;
            yawDelta = Quaternion.identity;
            if (!_hasPrev || !_hasCurr)
                return false;

            Quaternion rotationDelta = _currRotation * Quaternion.Inverse(_prevRotation);
            Vector3 moved = _currPosition + rotationDelta * (worldPoint - _prevPosition);
            delta = moved - worldPoint;

            Vector3 forward = rotationDelta * Vector3.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude > 1e-6f)
                yawDelta = Quaternion.FromToRotation(Vector3.forward, forward.normalized);

            return true;
        }

        public Vector3 GetPointVelocity(Vector3 worldPoint)
        {
            if (!TryGetStepDelta(worldPoint, out Vector3 delta, out _))
                return Vector3.zero;
            return delta / Mathf.Max(Time.fixedDeltaTime, 1e-5f);
        }
    }
}
