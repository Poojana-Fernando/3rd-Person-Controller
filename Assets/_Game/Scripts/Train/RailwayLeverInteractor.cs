using DeadAuthorGames.ThirdPersonCombat;
using Unity.Netcode;
using UnityEngine;

namespace Game.Train
{
    /// <summary>
    /// Local player throws trackside levers with F after leaving the train and walking up to the handle.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(150)]
    public class RailwayLeverInteractor : MonoBehaviour
    {
        [SerializeField] private KeyCode interactKey = KeyCode.F;

        private TrainSeatInteractor _seat;
        private PlatformRider _rider;
        private NetworkObject _networkObject;
        private RailwayJunction[] _junctions = new RailwayJunction[0];
        private float _nextRefresh;
        private RailwayJunction _candidate;

        /// <summary>Junction lever in range for <see cref="interactKey"/>.</summary>
        public RailwayJunction Candidate => _candidate;

        private void Awake()
        {
            _seat = GetComponent<TrainSeatInteractor>();
            _rider = GetComponent<PlatformRider>();
            _networkObject = GetComponent<NetworkObject>();
        }

        private bool IsLocal => _networkObject == null || !_networkObject.IsSpawned || _networkObject.IsOwner;

        private void Update()
        {
            if (!IsLocal)
                return;

            if (Time.unscaledTime >= _nextRefresh)
            {
                _junctions = FindObjectsByType<RailwayJunction>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
                _nextRefresh = Time.unscaledTime + 1f;
            }

            if (!CanUseLevers())
            {
                _candidate = null;
                return;
            }

            _candidate = FindNearestLever();
            if (_candidate != null && Input.GetKeyDown(interactKey))
            {
                RailwaySwitchboard board = _candidate.GetComponentInParent<RailwaySwitchboard>();
                bool toggled = board != null
                    ? board.TryRequestToggle(_candidate, out string reason)
                    : _candidate.TryToggleLever(out reason);
                if (!toggled && !string.IsNullOrEmpty(reason))
                    Debug.LogWarning($"[RailwayLever] {_candidate.name}: {reason}");
            }
        }

        private bool CanUseLevers()
        {
            if (_seat != null && _seat.IsSeated)
                return false;
            if (_rider != null && _rider.CurrentPlatform != null)
                return false;
            return true;
        }

        private RailwayJunction FindNearestLever()
        {
            Vector3 p = transform.position;
            RailwayJunction best = null;
            float bestDist = float.MaxValue;
            foreach (RailwayJunction j in _junctions)
            {
                if (j == null)
                    continue;
                float r = j.InteractRadius;
                float d = (j.LeverWorldPosition - p).sqrMagnitude;
                if (d <= r * r && d < bestDist)
                {
                    bestDist = d;
                    best = j;
                }
            }
            return best;
        }

    }
}
