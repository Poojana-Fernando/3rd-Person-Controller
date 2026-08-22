using UnityEngine;
using UnityEngine.UI;

public class EnemyHealthBar : MonoBehaviour
{
    [SerializeField] private Image fillImage;
    [SerializeField] private Transform followTarget;
    [SerializeField] private Vector3 worldOffset = new Vector3(0f, 0.35f, 0f);
    [SerializeField] private bool hideWhenFull;
    [SerializeField] private CanvasGroup canvasGroup;

    private Camera _camera;
    private bool _visible = true;

    private void Awake()
    {
        if (canvasGroup == null)
            canvasGroup = GetComponent<CanvasGroup>();
        ResolveCamera();
    }

    private void LateUpdate()
    {
        if (followTarget != null)
            transform.position = followTarget.position + worldOffset;

        if (_camera == null)
            ResolveCamera();

        if (_camera != null)
            transform.rotation = Quaternion.LookRotation(transform.position - _camera.transform.position);
    }

    public void SetHealth(float normalized, bool instant)
    {
        normalized = Mathf.Clamp01(normalized);
        if (fillImage != null)
            fillImage.fillAmount = normalized;

        if (hideWhenFull)
            SetVisible(normalized < 0.999f && normalized > 0.001f);
    }

    public void SetVisible(bool visible)
    {
        _visible = visible;
        if (canvasGroup != null)
        {
            canvasGroup.alpha = visible ? 1f : 0f;
            canvasGroup.blocksRaycasts = false;
            canvasGroup.interactable = false;
        }
        else
        {
            gameObject.SetActive(visible);
        }
    }

    private void ResolveCamera()
    {
        PlayerGunController gun = FindAnyObjectByType<PlayerGunController>();
        if (gun != null)
        {
            _camera = gun.GetActiveCamera();
            if (_camera != null)
                return;
        }

        _camera = Camera.main;
    }

#if UNITY_EDITOR
    public void EditorBind(Image fill, Transform follow, Vector3 offset)
    {
        fillImage = fill;
        followTarget = follow;
        worldOffset = offset;
        canvasGroup = GetComponent<CanvasGroup>();
    }
#endif
}
