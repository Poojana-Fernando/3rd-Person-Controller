using UnityEngine;

namespace Game.FirstPerson
{
    [CreateAssetMenu(menuName = "Game/First Person Settings", fileName = "FirstPersonSettings")]
    public class FirstPersonSettings : ScriptableObject
    {
        private const string PrefsPrefix = "Game.FP.";

        [Header("Look")]
        public float mouseSensitivityX = 0.12f;
        public float mouseSensitivityY = 0.12f;
        public float gamepadSensitivityX = 120f;
        public float gamepadSensitivityY = 90f;
        [Range(0.1f, 1f)] public float adsSensitivityScale = 0.65f;
        public bool invertY;

        [Header("Field of view")]
        public float worldFov = 75f;
        public float adsWorldFovScale = 0.85f;
        public float viewmodelFov = 54f;

        [Header("Pitch limits")]
        public float minPitch = -85f;
        public float maxPitch = 85f;

        [Header("Eye pivot")]
        public float eyeHeightStanding = 1.62f;
        public float eyeHeightCrouch = 1.15f;
        public float eyeHeightSlide = 0.95f;
        public float eyeForwardOffset = 0.08f;
        public float eyeHeightSmoothTime = 0.12f;
        [Range(0f, 1f)] public float parkourHeadBlend = 0.6f;

        [Header("Body pull-back when looking down")]
        public float bodyPullbackMax = 0.22f;
        public float bodyPullbackStartPitch = 25f;

        [Header("Motion")]
        public bool enableWeaponBob = true;

        public void LoadFromPlayerPrefs()
        {
            mouseSensitivityX = PlayerPrefs.GetFloat(PrefsPrefix + "SensX", mouseSensitivityX);
            mouseSensitivityY = PlayerPrefs.GetFloat(PrefsPrefix + "SensY", mouseSensitivityY);
            invertY = PlayerPrefs.GetInt(PrefsPrefix + "InvertY", invertY ? 1 : 0) == 1;
            enableWeaponBob = PlayerPrefs.GetInt(PrefsPrefix + "Bob", enableWeaponBob ? 1 : 0) == 1;
        }

        public void SaveToPlayerPrefs()
        {
            PlayerPrefs.SetFloat(PrefsPrefix + "SensX", mouseSensitivityX);
            PlayerPrefs.SetFloat(PrefsPrefix + "SensY", mouseSensitivityY);
            PlayerPrefs.SetInt(PrefsPrefix + "InvertY", invertY ? 1 : 0);
            PlayerPrefs.SetInt(PrefsPrefix + "Bob", enableWeaponBob ? 1 : 0);
            PlayerPrefs.Save();
        }

        private static FirstPersonSettings _runtime;

        public static FirstPersonSettings Runtime
        {
            get
            {
                if (_runtime != null)
                    return _runtime;

                _runtime = Resources.Load<FirstPersonSettings>("FirstPersonSettings");
                if (_runtime == null)
                {
                    _runtime = CreateInstance<FirstPersonSettings>();
                    _runtime.hideFlags = HideFlags.HideAndDontSave;
                }

                _runtime.LoadFromPlayerPrefs();
                return _runtime;
            }
        }
    }
}
