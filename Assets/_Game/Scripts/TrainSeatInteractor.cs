using Game.Net;
using Game.Train;
using Motiong.vCharacterController;
using Unity.Netcode;
using UnityEngine;

namespace DeadAuthorGames.ThirdPersonCombat
{
    /// <summary>
    /// Lets the local player sit in a <see cref="TrainSeat"/> with F (when close and looking at it) and drive the train:
    /// W forward, S backward, Space brake. Seat occupancy is server-authoritative via <see cref="NetworkTrainDriver"/>;
    /// every peer poses the seated player on the seat locally so they never lag behind the moving train.
    /// </summary>
    // After the rig/NetworkTransform so the seated pose wins this frame.
    [DefaultExecutionOrder(200)]
    [DisallowMultipleComponent]
    public class TrainSeatInteractor : MonoBehaviour
    {
        private static readonly int IsSittingHash = Animator.StringToHash("IsSitting");

        [SerializeField] private KeyCode interactKey = KeyCode.F;
        [SerializeField] private KeyCode forwardKey = KeyCode.W;
        [SerializeField] private KeyCode backwardKey = KeyCode.S;
        [SerializeField] private KeyCode brakeKey = KeyCode.Space;
        [SerializeField] private KeyCode trainCameraKey = KeyCode.V;
        [SerializeField] private KeyCode hornKey = KeyCode.H;

        private const float SeatRefreshInterval = 1f;
        // A denied sit/stand request gets no reply, so allow retrying after this long.
        private const float RequestTimeout = 1f;

        private NetworkObject _networkObject;
        private NetworkPlayerSetup _setup;
        private NetworkHealth _health;
        private Rigidbody _body;
        private vThirdPersonInput _input;
        private ParkourController _parkour;
        private PlayerGunController _gun;

        private TrainSeat[] _seats = new TrainSeat[0];
        private float _nextSeatRefresh;
        private TrainSeat _candidate;
        private TrainSeat _seat;
        private bool _requestPending;
        private float _requestTime;
        private int _sentThrottle;
        private bool _sentBrake;
        private RigidbodyInterpolation _savedInterpolation;
        private bool _savedKinematic;
        private TrainCamera _trainCamera;
        private int _holsteredSlot;

        /// <summary>Locomotives without <see cref="NetworkTrainDriver"/> (e.g. Aurora in world.unity).</summary>
        private static TrainSeat s_offlineOccupiedSeat;

        public bool IsSeated => _seat != null;
        public bool IsUsingTrainCamera => _trainCamera != null && _trainCamera.IsActive;
        /// <summary>Seat currently offered with the "Press F to sit" prompt.</summary>
        public TrainSeat Candidate => _candidate;

        private bool IsLocal => _networkObject == null || !_networkObject.IsSpawned || _networkObject.IsOwner;

        // A non-networked character (offline test player) must stay out of a network session's seats.
        private bool IsDetachedFromSession
        {
            get
            {
                if (_networkObject != null)
                    return false;
                NetworkManager nm = NetworkManager.Singleton;
                return nm != null && nm.IsListening;
            }
        }

        private ulong MyClientId
        {
            get
            {
                if (_networkObject != null && _networkObject.IsSpawned)
                    return _networkObject.OwnerClientId;
                NetworkManager nm = NetworkManager.Singleton;
                return nm != null && nm.IsListening ? nm.LocalClientId : 0;
            }
        }

        private void Awake()
        {
            _networkObject = GetComponent<NetworkObject>();
            _setup = GetComponent<NetworkPlayerSetup>();
            _health = GetComponent<NetworkHealth>();
            _body = GetComponent<Rigidbody>();
            _input = GetComponent<vThirdPersonInput>();
            _parkour = GetComponent<ParkourController>();
            _gun = GetComponent<PlayerGunController>();
        }

        private void OnDisable()
        {
            if (_seat != null)
            {
                if (IsLocal && !IsDetachedFromSession && _seat.Network != null)
                    _seat.Network.RequestStand(MyClientId);
                else if (_seat.Driver != null)
                    _seat.Driver.SetControls(0, false);
                ExitSeat(false);
            }
        }

        private void Update()
        {
            if (IsDetachedFromSession)
            {
                _candidate = null;
                if (_seat != null)
                    ExitSeat(false);
                return;
            }

            RefreshSeats();
            SyncSeatFromOccupancy();

            if (_requestPending && Time.unscaledTime - _requestTime > RequestTimeout)
                _requestPending = false;

            // Pose from the interpolated train before any camera LateUpdate reads the player transform.
            if (_seat != null)
                SnapToSeat(false);

            if (!IsLocal)
                return;

            bool dead = _health != null && _health.IsSpawned && _health.IsDead;
            bool menuOpen = LobbyMenuController.BlocksLookInput;

            if (_seat != null)
            {
                if (dead)
                {
                    RequestStand();
                    SendControls(0, false);
                    return;
                }

                KeepControlsLocked();
                SetSittingAnim(true);

                int throttle = 0;
                bool brake = false;
                if (!menuOpen)
                {
                    if (Input.GetKey(forwardKey)) throttle += 1;
                    if (Input.GetKey(backwardKey)) throttle -= 1;
                    brake = Input.GetKey(brakeKey);
                }
                SendControls(throttle, brake);

                if (!menuOpen)
                {
                    if (Input.GetKeyDown(trainCameraKey))
                        ToggleTrainCamera();
                    if (!IsUsingTrainCamera && Input.GetKeyDown(KeyCode.P) && _input != null)
                        _input.ToggleViewMode();
                    if (Input.GetKeyDown(hornKey))
                        RequestHorn();
                    SeatedLookInput();
                }

                if (!menuOpen && Input.GetKeyDown(interactKey))
                    RequestStand();
                return;
            }

            _candidate = dead || menuOpen ? null : FindCandidate();
            if (_candidate != null && !_requestPending && Input.GetKeyDown(interactKey))
            {
                if (_candidate.Network != null)
                {
                    _requestPending = true;
                    _requestTime = Time.unscaledTime;
                    _candidate.Network.RequestSit(MyClientId);
                }
                else if (_candidate.Driver != null)
                    EnterSeat(_candidate);
            }
        }

        private void FixedUpdate()
        {
            if (_seat != null && IsLocal)
                SnapToSeat(true);
        }

        private void LateUpdate()
        {
            if (_seat != null)
                SnapToSeat(false);
        }

        private void RefreshSeats()
        {
            if (Time.unscaledTime < _nextSeatRefresh)
                return;
            _nextSeatRefresh = Time.unscaledTime + SeatRefreshInterval;
            _seats = FindObjectsByType<TrainSeat>();
        }

        private void SyncSeatFromOccupancy()
        {
            // Solo locomotives (TrainSplineDriver only) use local EnterSeat; no NetworkTrainDriver occupancy to sync.
            if (_seat != null && _seat.Network == null)
                return;

            TrainSeat occupied = null;
            ulong me = MyClientId;
            foreach (TrainSeat seat in _seats)
            {
                if (seat != null && seat.Network != null && seat.Network.SeatOccupant == me)
                {
                    occupied = seat;
                    break;
                }
            }

            if (occupied == _seat)
                return;

            if (_seat != null)
                ExitSeat(true);
            if (occupied != null)
                EnterSeat(occupied);
        }

        private TrainSeat FindCandidate()
        {
            Camera cam = PlayerCamera();

            TrainSeat best = null;
            float bestDot = -1f;
            foreach (TrainSeat seat in _seats)
            {
                if (seat == null || !IsSeatAvailable(seat))
                    continue;

                Vector3 focus = seat.FocusPoint;
                Vector3 toSeatFlat = Vector3.ProjectOnPlane(focus - transform.position, Vector3.up);
                if (toSeatFlat.sqrMagnitude > seat.InteractDistance * seat.InteractDistance)
                    continue;

                float dot = -1f;
                if (cam != null)
                {
                    Vector3 toSeat = focus - cam.transform.position;
                    if (toSeat.sqrMagnitude > 1e-4f)
                        dot = Vector3.Dot(cam.transform.forward, toSeat.normalized);
                }

                // Facing the seat with the body also counts: the camera sits behind and above the player.
                if (toSeatFlat.sqrMagnitude > 1e-4f)
                {
                    Vector3 bodyForward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
                    dot = Mathf.Max(dot, Vector3.Dot(bodyForward, toSeatFlat.normalized));
                }
                else
                {
                    dot = 1f;
                }

                if (dot >= seat.LookDot && dot > bestDot)
                {
                    best = seat;
                    bestDot = dot;
                }
            }

            return best;
        }

        private static bool IsSeatAvailable(TrainSeat seat)
        {
            if (seat.Network != null)
                return seat.Network.IsSeatFree;
            if (seat.Driver == null)
                return false;
            return s_offlineOccupiedSeat == null || s_offlineOccupiedSeat == seat;
        }

        // Camera.main is unreliable: scenes can contain extra cameras tagged MainCamera.
        private Camera PlayerCamera()
        {
            if (_input != null)
            {
                if (_input.cameraMain != null && _input.cameraMain.isActiveAndEnabled)
                    return _input.cameraMain;
                if (_input.tpCamera != null)
                {
                    Camera tp = _input.tpCamera.GetComponent<Camera>();
                    if (tp != null && tp.isActiveAndEnabled)
                        return tp;
                }
            }

            return Camera.main;
        }

        private void EnterSeat(TrainSeat seat)
        {
            _seat = seat;
            if (seat.Network == null && seat.Driver != null)
                s_offlineOccupiedSeat = seat;
            _requestPending = false;
            _sentThrottle = 0;
            _sentBrake = false;

            SetSittingAnim(true);

            if (_body != null)
            {
                _savedKinematic = _body.isKinematic;
                _savedInterpolation = _body.interpolation;
                if (!_body.isKinematic)
                {
                    _body.linearVelocity = Vector3.zero;
                    _body.angularVelocity = Vector3.zero;
                }
                _body.isKinematic = true;
                _body.interpolation = RigidbodyInterpolation.None;
            }

            if (IsLocal)
            {
                // Owner-side unequip replicates to every peer through NetworkWeaponState.
                if (_gun != null)
                {
                    _holsteredSlot = _gun.Holster();
                    _gun.EquipBlocked = true;
                }
                SetControls(false);
            }

            SnapToSeat(false);
        }

        private void ExitSeat(bool placeAtStand)
        {
            TrainSeat seat = _seat;
            _seat = null;
            if (seat != null && seat.Network == null && s_offlineOccupiedSeat == seat)
                s_offlineOccupiedSeat = null;
            _requestPending = false;

            SetSittingAnim(false);

            if (_trainCamera != null)
                _trainCamera.Deactivate();
            _trainCamera = null;

            if (seat != null && placeAtStand && IsLocal)
            {
                Vector3 stand = seat.GetStandPosition();
                Quaternion facing = Quaternion.LookRotation(Vector3.ProjectOnPlane(seat.TrainForward, Vector3.up).normalized, Vector3.up);
                transform.SetPositionAndRotation(stand, facing);
                if (_body != null)
                {
                    _body.position = stand;
                    _body.rotation = facing;
                }
            }

            if (_body != null)
            {
                _body.isKinematic = _savedKinematic;
                _body.interpolation = _savedInterpolation;
            }

            if (IsLocal)
            {
                bool dead = _health != null && _health.IsSpawned && _health.IsDead;
                bool menuOpen = LobbyMenuController.Instance != null && LobbyMenuController.Instance.IsMenuVisible;
                if (!dead && !menuOpen)
                    SetControls(true);

                if (_gun != null)
                {
                    _gun.EquipBlocked = false;
                    WeaponInventory inventory = GetComponent<WeaponInventory>();
                    if (!dead && _holsteredSlot > 0 && inventory != null && inventory.Has(_holsteredSlot))
                        _gun.SelectSlot(_holsteredSlot);
                }
            }
            _holsteredSlot = 0;
        }

        private void RequestStand()
        {
            if (_requestPending || _seat == null)
                return;

            SendControls(0, false);

            if (_seat.Network != null)
            {
                _requestPending = true;
                _requestTime = Time.unscaledTime;
                _seat.Network.RequestStand(MyClientId);
                return;
            }

            ExitSeat(true);
        }

        private void ToggleTrainCamera()
        {
            if (IsUsingTrainCamera)
            {
                _trainCamera.Deactivate();
                return;
            }

            TrainSplineDriver driver = _seat != null ? _seat.Driver : null;
            if (driver == null)
                return;

            _trainCamera = driver.GetComponent<TrainCamera>();
            if (_trainCamera == null)
                _trainCamera = driver.gameObject.AddComponent<TrainCamera>();
            _trainCamera.Activate(PlayerCamera());
        }

        // vThirdPersonInput is disabled while seated, so look input is routed here for whichever camera is live.
        private void SeatedLookInput()
        {
            float x = Input.GetAxis("Mouse X");
            float y = Input.GetAxis("Mouse Y");

            if (IsUsingTrainCamera)
            {
                _trainCamera.AddInput(x, y, Input.mouseScrollDelta.y);
                return;
            }

            if (_input != null && _input.tpCamera != null)
                _input.tpCamera.RotateCamera(x, y);
        }

        private void RequestHorn()
        {
            if (_seat == null)
                return;

            if (_seat.Network != null)
            {
                _seat.Network.RequestHorn(MyClientId);
                return;
            }

            TrainAudio audio = _seat.Driver != null
                ? _seat.Driver.GetComponent<TrainAudio>()
                : null;
            audio ??= _seat.Driver != null ? _seat.Driver.GetComponentInParent<TrainAudio>() : null;
            audio?.PlayHorn();
        }

        private void SendControls(int throttle, bool brake)
        {
            if (_seat == null)
                return;
            if (throttle == _sentThrottle && brake == _sentBrake)
                return;
            _sentThrottle = throttle;
            _sentBrake = brake;

            if (_seat.Network != null)
                _seat.Network.SendControls(throttle, brake);
            else if (_seat.Driver != null)
                _seat.Driver.SetControls(throttle, brake);
        }

        private void SnapToSeat(bool physicsStep)
        {
            _seat.GetSitPose(out Vector3 position, out Quaternion rotation);
            if (physicsStep && _body != null)
            {
                _body.position = position;
                _body.rotation = rotation;
            }
            transform.SetPositionAndRotation(position, rotation);
        }

        private void KeepControlsLocked()
        {
            if (_input != null && _input.enabled)
                SetControls(false);
        }

        private void SetControls(bool enabled)
        {
            if (_setup != null && _networkObject != null && _networkObject.IsSpawned)
            {
                _setup.SetLocalControlsEnabled(enabled);
                return;
            }

            if (_input != null)
                _input.enabled = enabled;
            if (_parkour != null)
                _parkour.enabled = enabled;
            if (_gun != null)
                _gun.enabled = enabled;
        }

        private void SetSittingAnim(bool sitting)
        {
            Animator animator = GetComponent<Animator>();
            if (animator == null)
                animator = GetComponentInChildren<Animator>();
            if (animator == null || animator.runtimeAnimatorController == null)
                return;

            foreach (AnimatorControllerParameter p in animator.parameters)
            {
                if (p.nameHash == IsSittingHash && p.type == AnimatorControllerParameterType.Bool)
                {
                    animator.SetBool(IsSittingHash, sitting);
                    return;
                }
            }
        }

        private void OnGUI()
        {
            if (!IsLocal)
                return;

            string text = null;
            if (_seat != null)
            {
                TrainSplineDriver driver = _seat.Driver;
                float kmh = driver != null ? Mathf.Abs(driver.Velocity) * 3.6f : 0f;
                string view = IsUsingTrainCamera ? "V player camera" : "V train camera";
                text = $"W forward   S backward   Space brake   H horn   {view}   F stand up\nSpeed {kmh:0} km/h";
            }
            else if (_candidate != null)
            {
                text = "Press F to sit";
            }

            if (text == null)
                return;

            var style = new GUIStyle(GUI.skin.box) { fontSize = 18, alignment = TextAnchor.MiddleCenter };
            Vector2 size = style.CalcSize(new GUIContent(text)) + new Vector2(24f, 12f);
            GUI.Box(new Rect((Screen.width - size.x) * 0.5f, Screen.height * 0.72f, size.x, size.y), text, style);
        }
    }
}
