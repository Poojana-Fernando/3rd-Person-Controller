using System;
using Unity.Netcode;
using UnityEngine;

namespace Game.Train
{
    /// <summary>
    /// Server-authoritative driving for <see cref="TrainSplineDriver"/>.
    /// One player at a time can occupy the driver seat; only they may send throttle/brake.
    /// The server simulates the drive and replicates distance/velocity/wheel lock;
    /// clients pose the train (wheels, trucks, rods) locally from that state.
    /// Without a spawned NetworkObject the seat and controls work locally.
    /// </summary>
    [RequireComponent(typeof(TrainSplineDriver))]
    [RequireComponent(typeof(NetworkObject))]
    public class NetworkTrainDriver : NetworkBehaviour
    {
        public const ulong NoOccupant = ulong.MaxValue;

        [SerializeField] private TrainSplineDriver driver;
        [SerializeField] private float clientSmoothing = 12f;
        [SerializeField] private float clientSnapDistance = 5f;

        private readonly NetworkVariable<float> _distance = new NetworkVariable<float>(
            0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<float> _velocity = new NetworkVariable<float>(
            0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<float> _wheelLock = new NetworkVariable<float>(
            0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<ulong> _seatOccupant = new NetworkVariable<ulong>(
            NoOccupant, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkList<RailSpanNet> _routeSpans = new NetworkList<RailSpanNet>(
            null, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly RailRoute _clientRoute = new RailRoute();
        private int _lastPublishedSpanCount = -1;

        private float _clientDistance;
        private bool _clientHasDistance;
        private ulong _offlineOccupant = NoOccupant;
        private TrainAudio _audio;
        private float _nextHornTime;

        private const float HornCooldown = 0.5f;

        public TrainSplineDriver Driver => driver;

        /// <summary>Client id of the seated driver, or <see cref="NoOccupant"/>.</summary>
        public ulong SeatOccupant => IsSpawned ? _seatOccupant.Value : _offlineOccupant;

        public bool IsSeatFree => SeatOccupant == NoOccupant;

        /// <summary>Raised on every peer when the seat occupant changes.</summary>
        public event Action<ulong> SeatOccupantChanged;

        private void Awake()
        {
            if (driver == null)
                driver = GetComponent<TrainSplineDriver>();

            _audio = GetComponent<TrainAudio>();
            if (_audio == null)
                _audio = gameObject.AddComponent<TrainAudio>();
        }

        public override void OnNetworkSpawn()
        {
            if (driver == null)
                return;

            driver.EnsureInitialized();
            _seatOccupant.OnValueChanged += HandleOccupantChanged;
            _routeSpans.OnListChanged += HandleRouteChanged;

            if (IsServer)
            {
                _seatOccupant.Value = NoOccupant;
                driver.SetControls(0, false);
                PublishState();
                if (NetworkManager != null)
                    NetworkManager.OnClientDisconnectCallback += HandleClientDisconnect;
            }
            else
            {
                driver.enabled = false;
                _clientHasDistance = false;
                if (_routeSpans.Count > 0)
                    HandleRouteChanged(default);
            }

            if (IsServer)
                PublishRouteIfChanged();
        }

        public override void OnNetworkDespawn()
        {
            _seatOccupant.OnValueChanged -= HandleOccupantChanged;
            _routeSpans.OnListChanged -= HandleRouteChanged;
            if (NetworkManager != null)
                NetworkManager.OnClientDisconnectCallback -= HandleClientDisconnect;

            if (driver != null)
            {
                driver.enabled = true;
                driver.SetControls(0, false);
            }
        }

        private void HandleOccupantChanged(ulong previous, ulong current)
        {
            SeatOccupantChanged?.Invoke(current);
        }

        private void HandleClientDisconnect(ulong clientId)
        {
            if (IsServer && _seatOccupant.Value == clientId)
                FreeSeatServer();
        }

        private void Update()
        {
            if (IsSpawned && IsServer && driver != null)
                PublishState();
        }

        // Clients pose the train in physics steps so riders (PlatformRider) move in lockstep with it.
        private void FixedUpdate()
        {
            if (!IsSpawned || driver == null || IsServer)
                return;

            FollowServer(Time.fixedDeltaTime);
        }

        private void PublishState()
        {
            if (!Mathf.Approximately(_distance.Value, driver.Distance))
                _distance.Value = driver.Distance;
            if (!Mathf.Approximately(_velocity.Value, driver.Velocity))
                _velocity.Value = driver.Velocity;
            if (!Mathf.Approximately(_wheelLock.Value, driver.WheelLock))
                _wheelLock.Value = driver.WheelLock;
            PublishRouteIfChanged();
        }

        private void PublishRouteIfChanged()
        {
            RailRoute route = driver.Route;
            if (route == null)
                return;
            int count = route.Spans.Count;
            if (count == _routeSpans.Count && count == _lastPublishedSpanCount)
            {
                bool identical = true;
                for (int i = 0; i < count; i++)
                {
                    if (!_routeSpans[i].Equals(new RailSpanNet(route.Spans[i])))
                    {
                        identical = false;
                        break;
                    }
                }

                if (identical)
                    return;
            }

            _lastPublishedSpanCount = count;
            RailRouteNet.WriteList(_routeSpans, route);
        }

        private void HandleRouteChanged(NetworkListEvent<RailSpanNet> change)
        {
            if (IsServer || driver == null || driver.Track == null)
                return;
            RailRouteNet.ApplyList(_clientRoute, _routeSpans, driver.Track);
            driver.ApplyRouteSnapshot(_clientRoute);
        }

        private void FollowServer(float dt)
        {
            float target = _distance.Value;
            if (!_clientHasDistance || Mathf.Abs(target - _clientDistance) > clientSnapDistance)
            {
                _clientDistance = target;
                _clientHasDistance = true;
            }
            else
            {
                int facing = driver.Rig != null ? driver.Rig.Facing : 1;
                _clientDistance += _velocity.Value * facing * dt;
                _clientDistance = Mathf.Lerp(_clientDistance, target, 1f - Mathf.Exp(-clientSmoothing * dt));
            }

            driver.ApplyRemoteState(_clientDistance, _velocity.Value, _wheelLock.Value, true);
        }

        #region Seat API (called by the local player)

        public void RequestSit(ulong localClientId)
        {
            if (IsSpawned)
                RequestSitRpc();
            else if (_offlineOccupant == NoOccupant)
                SetOfflineOccupant(localClientId);
        }

        public void RequestStand(ulong localClientId)
        {
            if (IsSpawned)
                RequestStandRpc();
            else if (_offlineOccupant == localClientId)
            {
                driver.SetControls(0, false);
                SetOfflineOccupant(NoOccupant);
            }
        }

        public void SendControls(int throttle, bool brake)
        {
            if (IsSpawned)
                SendControlsRpc((sbyte)Mathf.Clamp(throttle, -1, 1), brake);
            else
                driver.SetControls(throttle, brake);
        }

        public void RequestHorn(ulong localClientId)
        {
            if (IsSpawned)
                RequestHornRpc();
            else if (_offlineOccupant == localClientId)
                PlayHornLocal();
        }

        private void PlayHornLocal()
        {
            if (_audio != null)
                _audio.PlayHorn();
        }

        private void SetOfflineOccupant(ulong occupant)
        {
            _offlineOccupant = occupant;
            SeatOccupantChanged?.Invoke(occupant);
        }

        #endregion

        #region Server RPCs

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void RequestSitRpc(RpcParams rpcParams = default)
        {
            if (_seatOccupant.Value != NoOccupant)
                return;
            driver.SetControls(0, false);
            _seatOccupant.Value = rpcParams.Receive.SenderClientId;
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void RequestStandRpc(RpcParams rpcParams = default)
        {
            if (_seatOccupant.Value == rpcParams.Receive.SenderClientId)
                FreeSeatServer();
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void SendControlsRpc(sbyte throttle, bool brake, RpcParams rpcParams = default)
        {
            if (_seatOccupant.Value != rpcParams.Receive.SenderClientId)
                return;
            driver.SetControls(throttle, brake);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void RequestHornRpc(RpcParams rpcParams = default)
        {
            if (_seatOccupant.Value != rpcParams.Receive.SenderClientId || Time.time < _nextHornTime)
                return;
            _nextHornTime = Time.time + HornCooldown;
            PlayHornRpc();
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void PlayHornRpc()
        {
            PlayHornLocal();
        }

        private void FreeSeatServer()
        {
            driver.SetControls(0, false);
            _seatOccupant.Value = NoOccupant;
        }

        #endregion
    }
}
