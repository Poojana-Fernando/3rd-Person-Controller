using UnityEngine;

namespace Game.FirstPerson
{
    [CreateAssetMenu(menuName = "Game/First Person Weapon Profile", fileName = "FPWeaponProfile")]
    public class FirstPersonWeaponProfile : ScriptableObject
    {
        [Header("Viewmodel poses (local to weapon root)")]
        public Vector3 hipLocalPosition = new(0.22f, -0.18f, 0.42f);
        public Vector3 hipLocalEuler = new(2f, -2f, 0f);
        public Vector3 adsLocalPosition = new(0f, -0.12f, 0.38f);
        public Vector3 adsLocalEuler = Vector3.zero;
        public Vector3 sprintLocalPosition = new(-0.08f, -0.35f, 0.25f);
        public Vector3 sprintLocalEuler = new(28f, -35f, 8f);

        [Header("ADS")]
        public float eyeRelief = 0.38f;
        public float adsTime = 0.22f;
        public float viewmodelFov = 54f;

        [Header("Hip convergence")]
        public float hipConvergeDegrees = 4f;

        [Header("Procedural motion scales")]
        public float swayScale = 1f;
        public float bobScale = 1f;
        [Range(0f, 1f)] public float adsBobScale = 0.2f;

        [Header("Wall pull-back")]
        public float weaponLength = 0.85f;
        public float wallProbeRadius = 0.08f;

        [Header("Recoil")]
        public float viewKickPitch = 0.35f;
        public float viewKickYawRange = 0.12f;
        public float viewKickRollRange = 0.08f;
        public float weaponBackKick = 0.015f;
        public float weaponUpKick = 0.008f;
        [Range(0.1f, 1f)] public float adsRecoilScale = 0.6f;
        public Vector2[] aimPunchPattern =
        {
            new(0.4f, 0.15f),
            new(-0.12f, 0.22f),
            new(0.08f, 0.1f),
        };

        [Header("Grip offsets (local to aim prefab if transforms missing)")]
        public Vector3 defaultRightGripLocal = new(0.02f, -0.04f, 0.12f);
        public Vector3 defaultLeftGripLocal = new(-0.06f, -0.05f, 0.18f);
        public Vector3 defaultSightLocal = new(0f, 0.06f, 0.35f);
    }
}
