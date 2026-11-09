using UnityEngine;
using UnityEngine.UI;


namespace DeadAuthorGames.ThirdPersonCombat
{
    public class PlayerHealthHud : MonoBehaviour
    {
        [SerializeField] private PlayerHealth playerHealth;
        [SerializeField] private Image fillImage;
        [SerializeField] private Image backgroundImage;

        private void Awake()
        {
            if (playerHealth == null)
            {
                var local = Game.Net.NetworkPlayerRegistry.Instance?.LocalPlayer;
                playerHealth = local != null ? local.Health : FindAnyObjectByType<PlayerHealth>();
            }

            if (playerHealth != null)
                playerHealth.OnHealthChanged += HandleHealthChanged;

            Refresh(playerHealth != null ? playerHealth.CurrentHealth : 0f,
                playerHealth != null ? playerHealth.MaxHealth : 1f);
        }

        public void BindPlayer(PlayerHealth health)
        {
            if (playerHealth != null)
                playerHealth.OnHealthChanged -= HandleHealthChanged;

            playerHealth = health;
            if (playerHealth != null)
            {
                playerHealth.OnHealthChanged += HandleHealthChanged;
                Refresh(playerHealth.CurrentHealth, playerHealth.MaxHealth);
            }
        }

        private void OnDestroy()
        {
            if (playerHealth != null)
                playerHealth.OnHealthChanged -= HandleHealthChanged;
        }

        private void HandleHealthChanged(float current, float max)
        {
            Refresh(current, max);
        }

        private void Refresh(float current, float max)
        {
            if (fillImage == null)
                return;

            float normalized = max > 0f ? Mathf.Clamp01(current / max) : 0f;
            fillImage.fillAmount = normalized;

            if (fillImage != null)
            {
                fillImage.color = normalized <= 0.25f
                    ? new Color(0.9f, 0.15f, 0.1f, 1f)
                    : new Color(0.2f, 0.85f, 0.25f, 1f);
            }
        }

    #if UNITY_EDITOR
        public void EditorBind(PlayerHealth health, Image fill, Image background)
        {
            playerHealth = health;
            fillImage = fill;
            backgroundImage = background;
        }
    #endif
    }
}
