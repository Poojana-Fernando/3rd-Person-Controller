using Game.Net;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

namespace DeadAuthorGames.ThirdPersonCombat
{
    /// <summary>
    /// Player F-interact: reattach a nearby severed limb whose MatchKey matches a missing part on this player.
    /// </summary>
    public class BodyPartReattachInteractor : MonoBehaviour
    {
        [SerializeField] private Text promptText;
        [SerializeField] private KeyCode interactKey = KeyCode.F;
        [SerializeField] private float interactRadius = 2.5f;

        private NetworkSeveredPart _nearestNet;
        private DetachedBodyPart _nearestLocal;
        private EnemyDismemberment _selfDismemberment;
        private NetworkDismemberment _selfNetworkDismemberment;
        private NetworkObject _selfNetObj;

        public bool HasFocus => _nearestNet != null || _nearestLocal != null;

        private void Awake()
        {
            if (promptText == null)
            {
                WeaponPickupInteractor weapons = GetComponent<WeaponPickupInteractor>();
                if (weapons != null)
                    promptText = weapons.PromptText;
            }

            _selfDismemberment = GetComponent<EnemyDismemberment>();
            _selfNetworkDismemberment = GetComponent<NetworkDismemberment>();
            _selfNetObj = GetComponent<NetworkObject>();
        }

        private void Update()
        {
            // Only the owning player drives pickup input.
            if (_selfNetObj != null && _selfNetObj.IsSpawned && !_selfNetObj.IsOwner)
            {
                _nearestNet = null;
                _nearestLocal = null;
                UpdatePrompt();
                return;
            }

            _nearestNet = FindNearestNetworkedPart();
            _nearestLocal = _nearestNet == null ? FindNearestLocalPart() : null;
            UpdatePrompt();

            if (!Input.GetKeyDown(interactKey))
                return;

            if (_nearestNet != null && _selfNetworkDismemberment != null && _selfNetworkDismemberment.IsSpawned)
            {
                _selfNetworkDismemberment.RequestReattachFromPickup(_nearestNet.NetworkObject);
                return;
            }

            if (_nearestLocal != null && _selfDismemberment != null)
                TryReattachLocal(_nearestLocal);
        }

        private NetworkSeveredPart FindNearestNetworkedPart()
        {
            if (_selfDismemberment == null)
                return null;

            NetworkSeveredPart best = null;
            float bestDistance = float.MaxValue;
            Vector3 origin = transform.position;
            var parts = NetworkSeveredPart.All;

            for (int i = 0; i < parts.Count; i++)
            {
                NetworkSeveredPart part = parts[i];
                if (part == null || !part.isActiveAndEnabled || !part.IsSpawned)
                    continue;
                if (!_selfDismemberment.HasMissingPart(part.MatchKey))
                    continue;

                float distance = Vector3.Distance(origin, part.transform.position);
                if (distance > interactRadius || distance >= bestDistance)
                    continue;

                bestDistance = distance;
                best = part;
            }

            return best;
        }

        private DetachedBodyPart FindNearestLocalPart()
        {
            if (_selfDismemberment == null)
                return null;

            DetachedBodyPart best = null;
            float bestDistance = float.MaxValue;
            Vector3 origin = transform.position;
            var parts = DetachedBodyPart.All;

            for (int i = 0; i < parts.Count; i++)
            {
                DetachedBodyPart part = parts[i];
                if (part == null || !part.isActiveAndEnabled)
                    continue;
                if (!_selfDismemberment.HasMissingPart(part.MatchKey))
                    continue;

                float distance = Vector3.Distance(origin, part.transform.position);
                if (distance > interactRadius || distance >= bestDistance)
                    continue;

                bestDistance = distance;
                best = part;
            }

            return best;
        }

        private void UpdatePrompt()
        {
            if (promptText == null)
                return;

            bool show = _nearestNet != null || _nearestLocal != null;
            if (promptText.gameObject.activeSelf != show)
                promptText.gameObject.SetActive(show);

            if (!show)
                return;

            string name = _nearestNet != null ? _nearestNet.DisplayName : _nearestLocal.DisplayName;
            promptText.text = $"Press F — Equip {name}";
        }

        private void TryReattachLocal(DetachedBodyPart part)
        {
            if (part == null || _selfDismemberment == null)
                return;

            if (!_selfDismemberment.TryReattachPart(part.MatchKey))
                return;

            Destroy(part.gameObject);
            _nearestLocal = null;
            UpdatePrompt();
        }

        public void SetPromptText(Text text)
        {
            promptText = text;
        }

#if UNITY_EDITOR
        public void EditorBind(Text text)
        {
            if (text != null)
                promptText = text;
        }
#endif
    }
}
