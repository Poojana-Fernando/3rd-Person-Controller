using Game.Net;
using Game.Train;
using Motiong.vCharacterController;
using Unity.Netcode;
using UnityEngine;

namespace DeadAuthorGames.ThirdPersonCombat
{
    /// <summary>
    /// Carries the character along with any <see cref="MovingPlatform"/> it stands on (e.g. the train),
    /// turning with it in curves and keeping its momentum briefly after jumping or stepping off.
    /// The carry is a velocity added after the motor and removed again before it (see <see cref="PlatformRiderPreStep"/>),
    /// so the Invector motor and friction work in the platform's frame and Rigidbody interpolation stays intact.
    /// </summary>
    // Must run after vThirdPersonInput.FixedUpdate (motor) and MovingPlatform (order 50).
    [DefaultExecutionOrder(100)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(PlatformRiderPreStep))]
    public class PlatformRider : MonoBehaviour
    {
        [SerializeField] private float probeDistance = 0.5f;
        [SerializeField] private LayerMask probeLayers = ~(1 << 2);
        [Tooltip("Per-step platform moves larger than this are treated as teleports and ignored (m).")]
        [SerializeField] private float maxStepDelta = 2f;
        [Tooltip("How long train momentum is kept while airborne after leaving it (s).")]
        [SerializeField] private float inheritFadeTime = 3f;

        // The motor may still report grounded for a few steps right after leaving the platform.
        private const float LandingGrace = 0.2f;
        private static readonly RaycastHit[] Hits = new RaycastHit[16];

        private Rigidbody _body;
        private CapsuleCollider _capsule;
        private vThirdPersonMotor _motor;
        private NetworkObject _networkObject;
        private NetworkHealth _health;
        private RigidbodyInterpolation _defaultInterpolation;

        private MovingPlatform _platform;
        private Vector3 _carryVelocity;
        private Vector3 _inheritedVelocity;
        private float _inheritTimer;

        public MovingPlatform CurrentPlatform => _platform;

        private void Awake()
        {
            _body = GetComponent<Rigidbody>();
            _capsule = GetComponent<CapsuleCollider>();
            _motor = GetComponent<vThirdPersonMotor>();
            _networkObject = GetComponent<NetworkObject>();
            _health = GetComponent<NetworkHealth>();
            _defaultInterpolation = _body.interpolation;
        }

        private void OnDisable()
        {
            if (_body != null && !_body.isKinematic)
            {
                _body.linearVelocity -= _carryVelocity;
                _body.interpolation = _defaultInterpolation;
            }
            _platform = null;
            _carryVelocity = Vector3.zero;
            ClearInherited();
        }

        /// <summary>Called before the motor each physics step: strips last step's carry so the motor sees platform-relative velocity.</summary>
        public void BeginStep()
        {
            if (_carryVelocity == Vector3.zero)
                return;
            if (enabled && ShouldSimulate())
                _body.linearVelocity -= _carryVelocity;
            _carryVelocity = Vector3.zero;
        }

        private void FixedUpdate()
        {
            if (!ShouldSimulate())
            {
                _platform = null;
                _carryVelocity = Vector3.zero;
                ClearInherited();
                return;
            }

            MovingPlatform platform = ProbePlatform();
            Vector3 origin = _body.position;
            float maxSpeed = maxStepDelta / Mathf.Max(Time.fixedDeltaTime, 1e-5f);
            Vector3 carry = Vector3.zero;

            if (platform != null)
            {
                ClearInherited();
                if (platform.TryGetStepDelta(origin, out Vector3 delta, out Quaternion yawDelta) &&
                    delta.sqrMagnitude <= maxStepDelta * maxStepDelta)
                {
                    carry = delta / Mathf.Max(Time.fixedDeltaTime, 1e-5f);
                    if (Quaternion.Angle(yawDelta, Quaternion.identity) > 1e-4f)
                        _body.MoveRotation(yawDelta * transform.rotation);
                }
            }
            else if (_platform != null)
            {
                Vector3 velocity = _platform.GetPointVelocity(origin);
                velocity.y = 0f;
                if (velocity.sqrMagnitude <= maxSpeed * maxSpeed)
                {
                    _inheritedVelocity = velocity;
                    _inheritTimer = 0f;
                }
            }

            _platform = platform;

            if (platform == null && _inheritedVelocity != Vector3.zero)
                carry = InheritedCarry();

            if (carry != Vector3.zero)
                _body.linearVelocity += carry;
            _carryVelocity = carry;

            bool riding = platform != null || _inheritedVelocity != Vector3.zero;
            RigidbodyInterpolation wanted = riding ? RigidbodyInterpolation.Interpolate : _defaultInterpolation;
            if (_body.interpolation != wanted)
                _body.interpolation = wanted;
        }

        private bool ShouldSimulate()
        {
            if (_networkObject != null && _networkObject.IsSpawned && !_networkObject.IsOwner)
                return false;
            if (_health != null && _health.IsSpawned && _health.IsDead)
                return false;
            return !_body.isKinematic;
        }

        private Vector3 InheritedCarry()
        {
            bool landed = _motor != null && _motor.isGrounded && _inheritTimer > LandingGrace;
            if (landed || _inheritTimer >= inheritFadeTime)
            {
                ClearInherited();
                return Vector3.zero;
            }

            float fade = inheritFadeTime > 0f ? 1f - _inheritTimer / inheritFadeTime : 0f;
            _inheritTimer += Time.fixedDeltaTime;
            return _inheritedVelocity * fade;
        }

        private void ClearInherited()
        {
            _inheritedVelocity = Vector3.zero;
            _inheritTimer = 0f;
        }

        private MovingPlatform ProbePlatform()
        {
            float radius = 0.25f;
            Vector3 feet = _body.position;
            if (_capsule != null)
            {
                Vector3 scale = transform.lossyScale;
                radius = _capsule.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z)) * 0.9f;
                feet = _body.position + _body.rotation * Vector3.Scale(_capsule.center, scale) -
                       Vector3.up * (_capsule.height * 0.5f * Mathf.Abs(scale.y));
            }

            Vector3 origin = feet + Vector3.up * (radius + 0.1f);
            int count = Physics.SphereCastNonAlloc(origin, radius, Vector3.down, Hits, probeDistance + 0.1f,
                probeLayers, QueryTriggerInteraction.Ignore);

            float best = float.MaxValue;
            Collider bestCollider = null;
            for (int i = 0; i < count; i++)
            {
                Collider col = Hits[i].collider;
                if (col == null || col.attachedRigidbody == _body || col.transform.IsChildOf(transform))
                    continue;
                if (Hits[i].distance < best)
                {
                    best = Hits[i].distance;
                    bestCollider = col;
                }
            }

            return bestCollider != null ? bestCollider.GetComponentInParent<MovingPlatform>() : null;
        }
    }
}
