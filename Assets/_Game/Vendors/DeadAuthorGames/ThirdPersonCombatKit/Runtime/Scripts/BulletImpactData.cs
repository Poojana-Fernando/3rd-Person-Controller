using UnityEngine;


namespace DeadAuthorGames.ThirdPersonCombat
{
    public struct BulletImpactData
    {
        public float Damage;
        public Vector3 HitPoint;
        public Vector3 HitNormal;
        public Vector3 ShotDirection;
        public float ImpactForce;
        public Collider Collider;
        public GameObject Source;
    }

    public interface IBulletImpactReceiver
    {
        void ReceiveBulletImpact(in BulletImpactData impact);
    }
}
