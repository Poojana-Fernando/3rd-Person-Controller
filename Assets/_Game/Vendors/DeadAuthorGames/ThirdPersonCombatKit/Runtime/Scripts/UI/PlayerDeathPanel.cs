using Motiong.vCharacterController;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;


namespace DeadAuthorGames.ThirdPersonCombat
{
    public class PlayerDeathPanel : MonoBehaviour
    {
        [SerializeField] private PlayerHealth playerHealth;
        [SerializeField] private GameObject panelRoot;
        [SerializeField] private CanvasGroup canvasGroup;
        [SerializeField] private Button retryButton;
        [SerializeField] private Button quitButton;
        [SerializeField] private bool pauseOnDeath;

        private bool _isShown;
        private CursorLockMode _previousLockMode;
        private bool _previousCursorVisible;
        private Behaviour[] _disabledBehaviours;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void EnsureNormalTimeScale()
        {
            Time.timeScale = 1f;
        }

        private void Awake()
        {
            if (playerHealth == null)
            {
                var local = Game.Net.NetworkPlayerRegistry.Instance?.LocalPlayer;
                playerHealth = local != null ? local.Health : FindAnyObjectByType<PlayerHealth>();
            }

            if (panelRoot == null)
                panelRoot = gameObject;

            if (canvasGroup == null)
                canvasGroup = GetComponent<CanvasGroup>();
            if (canvasGroup == null)
                canvasGroup = gameObject.AddComponent<CanvasGroup>();

            // Keep this GameObject active so OnDied stays subscribed.
            if (!gameObject.activeSelf)
                gameObject.SetActive(true);

            HidePanelImmediate();
        }

        private void OnEnable()
        {
            if (playerHealth != null)
                playerHealth.OnDied += HandlePlayerDied;

            if (retryButton != null)
            {
                retryButton.onClick.RemoveListener(OnRetryClicked);
                retryButton.onClick.AddListener(OnRetryClicked);
            }

            if (quitButton != null)
            {
                quitButton.onClick.RemoveListener(OnQuitClicked);
                quitButton.onClick.AddListener(OnQuitClicked);
            }
        }

        private void OnDisable()
        {
            if (playerHealth != null)
                playerHealth.OnDied -= HandlePlayerDied;

            if (retryButton != null)
                retryButton.onClick.RemoveListener(OnRetryClicked);

            if (quitButton != null)
                quitButton.onClick.RemoveListener(OnQuitClicked);

            if (_isShown && pauseOnDeath && Time.timeScale == 0f)
                Time.timeScale = 1f;
        }

        private void HandlePlayerDied()
        {
            if (_isShown)
                return;

            _isShown = true;
            DisablePlayerControl();
            ShowPanel();
        }

        private void ShowPanel()
        {
            if (panelRoot != null && panelRoot != gameObject && !panelRoot.activeSelf)
                panelRoot.SetActive(true);

            if (canvasGroup != null)
            {
                canvasGroup.alpha = 1f;
                canvasGroup.interactable = true;
                canvasGroup.blocksRaycasts = true;
            }

            _previousLockMode = Cursor.lockState;
            _previousCursorVisible = Cursor.visible;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            // Never freeze the whole session in multiplayer.
            if (pauseOnDeath && Game.Net.NetworkPlayerRegistry.Instance == null)
                Time.timeScale = 0f;
        }

        public void BindPlayer(PlayerHealth health)
        {
            if (playerHealth != null)
                playerHealth.OnDied -= HandlePlayerDied;

            playerHealth = health;
            if (isActiveAndEnabled && playerHealth != null)
                playerHealth.OnDied += HandlePlayerDied;
        }

        private void HidePanelImmediate()
        {
            if (canvasGroup != null)
            {
                canvasGroup.alpha = 0f;
                canvasGroup.interactable = false;
                canvasGroup.blocksRaycasts = false;
            }
            else if (panelRoot != null && panelRoot != gameObject)
            {
                panelRoot.SetActive(false);
            }
        }

        private void DisablePlayerControl()
        {
            if (playerHealth == null)
                return;

            GameObject player = playerHealth.gameObject;
            Behaviour[] candidates =
            {
                player.GetComponent<vThirdPersonInput>(),
                player.GetComponent<PlayerGunController>(),
                player.GetComponent<ParkourController>(),
                player.GetComponent<UpperBodyAim>(),
            };

            int count = 0;
            for (int i = 0; i < candidates.Length; i++)
            {
                if (candidates[i] != null && candidates[i].enabled)
                    count++;
            }

            _disabledBehaviours = new Behaviour[count];
            int index = 0;
            for (int i = 0; i < candidates.Length; i++)
            {
                Behaviour behaviour = candidates[i];
                if (behaviour == null || !behaviour.enabled)
                    continue;

                behaviour.enabled = false;
                _disabledBehaviours[index++] = behaviour;
            }
        }

        private void OnRetryClicked()
        {
            Time.timeScale = 1f;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;

            // In multiplayer, respawn is handled by PlayerRespawnController — just hide the panel.
            if (Game.Net.NetworkPlayerRegistry.Instance != null)
            {
                _isShown = false;
                HidePanelImmediate();
                return;
            }

            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }

        private void OnQuitClicked()
        {
            Time.timeScale = 1f;

    #if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
    #else
            Application.Quit();
    #endif
        }

    #if UNITY_EDITOR
        public void EditorBind(PlayerHealth health, GameObject root, Button retry, Button quit)
        {
            playerHealth = health;
            panelRoot = root;
            retryButton = retry;
            quitButton = quit;
            canvasGroup = root != null ? root.GetComponent<CanvasGroup>() : null;
        }
    #endif
    }
}
