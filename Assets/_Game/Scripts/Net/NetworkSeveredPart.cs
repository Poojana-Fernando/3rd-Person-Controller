using System.Collections;
using System.Collections.Generic;
using DeadAuthorGames.ThirdPersonCombat;
using Unity.Collections;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

namespace Game.Net
{
    /// <summary>
    /// Shared severed limb pickup. Server simulates physics (NetworkRigidbody);
    /// all peers build the visual from the source character (player or scene Target).
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    [RequireComponent(typeof(NetworkTransform))]
    [RequireComponent(typeof(NetworkRigidbody))]
    [RequireComponent(typeof(Rigidbody))]
    public class NetworkSeveredPart : NetworkBehaviour
    {
        private static readonly List<NetworkSeveredPart> Active = new List<NetworkSeveredPart>();

        private readonly NetworkVariable<ulong> _sourceObjectId = new NetworkVariable<ulong>(
            0,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<int> _partIndex = new NetworkVariable<int>(
            -1,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<int> _sceneDismemberId = new NetworkVariable<int>(
            0,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<FixedString64Bytes> _matchKey = new NetworkVariable<FixedString64Bytes>(
            default,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<FixedString64Bytes> _displayName = new NetworkVariable<FixedString64Bytes>(
            default,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private Rigidbody _body;
        private BoxCollider _box;
        private GameObject _visual;
        private Coroutine _retryVisual;
        private float _nextClientPushTime;

        private ulong _pendingSourceId;
        private int _pendingPartIndex = -1;
        private int _pendingSceneDismemberId;
        private string _pendingMatchKey;
        private string _pendingDisplayName;
        private Vector3 _pendingImpulse;
        private bool _hasPending;

        public static IReadOnlyList<NetworkSeveredPart> All => Active;
        public string MatchKey => _matchKey.Value.ToString();
        public string DisplayName
        {
            get
            {
                string n = _displayName.Value.ToString();
                return string.IsNullOrEmpty(n) ? MatchKey : n;
            }
        }

        public ulong SourceObjectId => _sourceObjectId.Value;
        public int PartIndex => _partIndex.Value;

        private void Awake()
        {
            _body = GetComponent<Rigidbody>();
            _box = GetComponent<BoxCollider>();
        }

        /// <summary>Call on the instantiated object before NetworkObject.Spawn.</summary>
        public void PrepareBeforeSpawn(
            NetworkObject source,
            int partIndex,
            string matchKey,
            string displayName,
            Vector3 impulse,
            int sceneDismemberId = 0)
        {
            _pendingSourceId = source != null ? source.NetworkObjectId : 0UL;
            _pendingPartIndex = partIndex;
            _pendingSceneDismemberId = sceneDismemberId;
            _pendingMatchKey = matchKey ?? string.Empty;
            _pendingDisplayName = displayName ?? matchKey ?? "Part";
            _pendingImpulse = impulse;
            _hasPending = true;

            // Write NetworkVariables before Spawn so clients receive them in the spawn snapshot.
            try
            {
                _sourceObjectId.Value = _pendingSourceId;
                _partIndex.Value = _pendingPartIndex;
                _sceneDismemberId.Value = _pendingSceneDismemberId;
                _matchKey.Value = _pendingMatchKey ?? string.Empty;
                _displayName.Value = _pendingDisplayName ?? "Part";
            }
            catch (System.Exception)
            {
                // Some NGO versions only allow writes when spawned; OnNetworkSpawn will apply pending.
            }
        }

        public override void OnNetworkSpawn()
        {
            if (!Active.Contains(this))
                Active.Add(this);

            if (IsServer && _hasPending)
            {
                _sourceObjectId.Value = _pendingSourceId;
                _partIndex.Value = _pendingPartIndex;
                _sceneDismemberId.Value = _pendingSceneDismemberId;
                _matchKey.Value = _pendingMatchKey ?? string.Empty;
                _displayName.Value = _pendingDisplayName ?? "Part";
            }

            if (_body != null)
            {
                _body.useGravity = true;
                _body.interpolation = RigidbodyInterpolation.Interpolate;
                _body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            }

            _sourceObjectId.OnValueChanged += OnMetaChanged;
            _partIndex.OnValueChanged += OnPartIndexChanged;
            _sceneDismemberId.OnValueChanged += OnSceneIdChanged;

            BuildVisual();
            // Always retry — spawn snapshot may arrive after first BuildVisual, or bake may need a posed frame.
            if (_visual == null)
                _retryVisual = StartCoroutine(RetryBuildVisual());

            if (IsServer && _hasPending && _pendingImpulse.sqrMagnitude > 0.0001f && _body != null)
            {
                StartCoroutine(ApplyImpulseNextFixed(_pendingImpulse));
                _hasPending = false;
            }
            else if (IsServer)
            {
                _hasPending = false;
            }
        }

        public override void OnNetworkDespawn()
        {
            Active.Remove(this);
            _sourceObjectId.OnValueChanged -= OnMetaChanged;
            _partIndex.OnValueChanged -= OnPartIndexChanged;
            _sceneDismemberId.OnValueChanged -= OnSceneIdChanged;
            if (_retryVisual != null)
            {
                StopCoroutine(_retryVisual);
                _retryVisual = null;
            }

            if (_visual != null)
                Destroy(_visual);
            _visual = null;
        }

        private void OnMetaChanged(ulong previous, ulong current) => BuildVisual();
        private void OnPartIndexChanged(int previous, int current) => BuildVisual();
        private void OnSceneIdChanged(int previous, int current) => BuildVisual();

        private void OnCollisionStay(Collision collision)
        {
            // Server-authoritative NetworkRigidbody: host physics already pushes.
            // Clients are kinematic proxies on the server, so owning clients report a push impulse.
            if (!IsSpawned || IsServer || _body == null || collision == null || collision.contactCount == 0)
                return;
            if (Time.time < _nextClientPushTime)
                return;

            var player = collision.collider != null
                ? collision.collider.GetComponentInParent<NetworkPlayerSetup>()
                : null;
            if (player == null || !player.IsOwner)
                return;

            ContactPoint contact = collision.GetContact(0);
            Vector3 vel = collision.relativeVelocity;
            if (collision.rigidbody != null)
                vel = collision.rigidbody.linearVelocity;

            float speed = vel.magnitude;
            if (speed < 0.2f)
                return;

            Vector3 away = transform.position - contact.point;
            away.y = 0f;
            if (away.sqrMagnitude < 0.0001f)
                away = -contact.normal;
            away.Normalize();

            Vector3 impulse = away * Mathf.Clamp(speed * 2.75f, 0.75f, 10f);
            impulse.y += Mathf.Clamp(speed * 0.08f, 0.05f, 0.6f);

            _nextClientPushTime = Time.time + 0.05f;
            RequestPushServerRpc(impulse, contact.point);
        }

        [ServerRpc(RequireOwnership = false)]
        private void RequestPushServerRpc(Vector3 impulse, Vector3 worldPoint, ServerRpcParams rpcParams = default)
        {
            if (!IsServer || _body == null)
                return;

            ulong sender = rpcParams.Receive.SenderClientId;
            if (NetworkManager == null ||
                !NetworkManager.ConnectedClients.TryGetValue(sender, out NetworkClient client) ||
                client.PlayerObject == null)
                return;

            float dist = Vector3.Distance(client.PlayerObject.transform.position, transform.position);
            if (dist > 3.25f)
                return;

            if (impulse.sqrMagnitude > 225f)
                impulse = impulse.normalized * 15f;

            if (_body.isKinematic)
                _body.isKinematic = false;
            _body.WakeUp();
            _body.AddForceAtPosition(impulse, worldPoint, ForceMode.Impulse);
        }

        private IEnumerator ApplyImpulseNextFixed(Vector3 impulse)
        {
            yield return new WaitForFixedUpdate();
            if (_body == null || !IsServer)
                yield break;
            if (_body.isKinematic)
                _body.isKinematic = false;
            _body.WakeUp();
            _body.AddForce(impulse, ForceMode.Impulse);
        }

        private IEnumerator RetryBuildVisual()
        {
            for (int i = 0; i < 45; i++)
            {
                yield return null;
                if (_visual != null)
                    yield break;
                BuildVisual();
                if (_visual != null)
                    yield break;
            }

            _retryVisual = null;
        }

        private void BuildVisual()
        {
            if (_visual != null)
            {
                Destroy(_visual);
                _visual = null;
            }

            if (_partIndex.Value < 0)
                return;

            EnemyDismemberment source = ResolveSource();
            if (source == null)
                return;

            GameObject piece = source.CreateDetachedVisual(_partIndex.Value);
            if (piece == null)
                return;

            foreach (var rb in piece.GetComponentsInChildren<Rigidbody>(true))
                Destroy(rb);
            var localPickup = piece.GetComponent<DetachedBodyPart>();
            if (localPickup != null)
                Destroy(localPickup);

            foreach (var col in piece.GetComponentsInChildren<Collider>(true))
                Destroy(col);

            piece.transform.SetParent(transform, false);
            piece.transform.localPosition = Vector3.zero;
            piece.transform.localRotation = Quaternion.identity;
            piece.SetActive(true);
            _visual = piece;

            FitColliderToVisual(piece);

            if (source.Parts != null &&
                _partIndex.Value < source.Parts.Count &&
                source.Parts[_partIndex.Value]?.sourceRenderer != null)
            {
                source.Parts[_partIndex.Value].sourceRenderer.enabled = false;
            }
        }

        private void FitColliderToVisual(GameObject piece)
        {
            if (_box == null || piece == null)
                return;

            Renderer renderer = piece.GetComponentInChildren<Renderer>(true);
            if (renderer == null)
            {
                _box.size = Vector3.one * 0.5f;
                _box.center = Vector3.zero;
                return;
            }

            Bounds b = renderer.bounds;
            Vector3 localCenter = transform.InverseTransformPoint(b.center);
            Vector3 lossy = transform.lossyScale;
            Vector3 localSize = new Vector3(
                SafeDiv(b.size.x, Mathf.Abs(lossy.x)),
                SafeDiv(b.size.y, Mathf.Abs(lossy.y)),
                SafeDiv(b.size.z, Mathf.Abs(lossy.z)));

            _box.center = localCenter;
            _box.size = Vector3.Max(localSize, Vector3.one * 0.35f);
        }

        private static float SafeDiv(float a, float b)
        {
            return Mathf.Abs(b) < 0.0001f ? a : a / b;
        }

        private EnemyDismemberment ResolveSource()
        {
            if (_sceneDismemberId.Value != 0 && NetworkWorldSync.Instance != null &&
                NetworkWorldSync.Instance.TryGetSceneDismemberment(_sceneDismemberId.Value, out EnemyDismemberment sceneDim))
            {
                return sceneDim;
            }

            if (_sourceObjectId.Value == 0 || NetworkManager.Singleton == null)
                return null;

            if (!NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(
                    _sourceObjectId.Value, out NetworkObject netObj) || netObj == null)
                return null;

            return netObj.GetComponent<EnemyDismemberment>()
                   ?? netObj.GetComponentInChildren<EnemyDismemberment>(true);
        }
    }
}
