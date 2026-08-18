using UnityEngine;

public class PlayModeCursorLock : MonoBehaviour
{
    [SerializeField] private bool lockCursor = true;

    private CursorLockMode previousLockState;
    private bool previousVisibility;

    private void OnEnable()
    {
        previousLockState = Cursor.lockState;
        previousVisibility = Cursor.visible;

        Cursor.visible = false;
        if (lockCursor)
            Cursor.lockState = CursorLockMode.Locked;
    }

    private void OnDisable()
    {
        Cursor.lockState = previousLockState;
        Cursor.visible = previousVisibility;
    }
}
