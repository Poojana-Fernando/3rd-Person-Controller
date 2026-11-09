using UnityEngine;


namespace DeadAuthorGames.ThirdPersonCombat
{
    public interface IPartHealthPool
    {
        float MaxHealth { get; }
        float CurrentHealth { get; }
        bool IsDead { get; }
        void ApplyPartLoss(float amount, Vector3 hitPoint, Vector3 hitNormal, GameObject source);
        void RestorePartHealth(float amount);
    }
}
