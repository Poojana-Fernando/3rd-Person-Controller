using UnityEngine;
using UnityEngine.UI;


namespace DeadAuthorGames.ThirdPersonCombat
{
    public class PlayerDamageFeedback : MonoBehaviour
    {
        [SerializeField] private Image topBorder;
        [SerializeField] private Image bottomBorder;
        [SerializeField] private Image leftBorder;
        [SerializeField] private Image rightBorder;
        [SerializeField] private float flashDuration = 0.35f;
        [SerializeField] private float maxAlpha = 0.75f;
        [SerializeField] private Color flashColor = new Color(0.85f, 0.05f, 0.05f, 1f);

        private float _flashTimer;
        private bool _isFlashing;

        private void Update()
        {
            if (!_isFlashing)
                return;

            _flashTimer -= Time.deltaTime;
            float t = Mathf.Clamp01(_flashTimer / Mathf.Max(0.01f, flashDuration));
            float alpha = maxAlpha * t;

            SetBorderAlpha(alpha);

            if (_flashTimer <= 0f)
            {
                _isFlashing = false;
                SetBorderAlpha(0f);
            }
        }

        public void PlayDamageFlash()
        {
            _flashTimer = flashDuration;
            _isFlashing = true;
            SetBorderAlpha(maxAlpha);
        }

        private void SetBorderAlpha(float alpha)
        {
            Color c = flashColor;
            c.a = alpha;
            ApplyColor(topBorder, c);
            ApplyColor(bottomBorder, c);
            ApplyColor(leftBorder, c);
            ApplyColor(rightBorder, c);
        }

        private static void ApplyColor(Image image, Color color)
        {
            if (image != null)
                image.color = color;
        }

    #if UNITY_EDITOR
        public void EditorBind(Image top, Image bottom, Image left, Image right)
        {
            topBorder = top;
            bottomBorder = bottom;
            leftBorder = left;
            rightBorder = right;
        }
    #endif
    }
}
