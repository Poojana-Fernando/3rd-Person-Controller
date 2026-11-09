using UnityEngine;

namespace DeadAuthorGames.ThirdPersonCombat
{
    /// <summary>
    /// Removes last step's platform carry from the Rigidbody velocity before the motor runs,
    /// so the motor always works in the platform's frame. <see cref="PlatformRider"/> adds the new carry after it.
    /// </summary>
    // Must run before vThirdPersonInput.FixedUpdate (order 0).
    [DefaultExecutionOrder(-100)]
    [DisallowMultipleComponent]
    public class PlatformRiderPreStep : MonoBehaviour
    {
        private PlatformRider _rider;

        private void Awake()
        {
            _rider = GetComponent<PlatformRider>();
        }

        private void FixedUpdate()
        {
            if (_rider == null)
                _rider = GetComponent<PlatformRider>();
            if (_rider != null)
                _rider.BeginStep();
        }
    }
}
