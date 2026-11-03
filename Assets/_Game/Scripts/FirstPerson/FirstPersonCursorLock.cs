using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Game.FirstPerson
{
    /// <summary>Esc unlocks cursor; click relocks while first-person is active.</summary>
    public class FirstPersonCursorLock : MonoBehaviour
    {
        [SerializeField] private bool active;

        public void SetActive(bool value)
        {
            active = value;
            if (active)
                Lock();
        }

        private void Update()
        {
            if (!active)
                return;

#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                return;
            }

            if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame &&
                Cursor.lockState != CursorLockMode.Locked)
            {
                Lock();
            }
#else
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                return;
            }

            if (Input.GetMouseButtonDown(0) && Cursor.lockState != CursorLockMode.Locked)
                Lock();
#endif
        }

        private static void Lock()
        {
            Cursor.visible = false;
            Cursor.lockState = CursorLockMode.Locked;
        }
    }
}
