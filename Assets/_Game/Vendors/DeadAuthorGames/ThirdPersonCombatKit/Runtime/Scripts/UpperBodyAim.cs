using UnityEngine;

// RigBuilder evaluates constraints during the Animator's PlayableGraph update,
// which finishes before LateUpdate. High execution order keeps spine offsets applied
// after rig solve and other LateUpdate scripts (camera 70 / gun controller 75).

namespace DeadAuthorGames.ThirdPersonCombat
{
    [DefaultExecutionOrder(500)]
    public class UpperBodyAim : MonoBehaviour
    {
        [Header("Bones")]
        [SerializeField] private Transform spineMiddle;
        [SerializeField] private Transform spineUpper;
        [SerializeField] private Transform neck;

        [Header("Pitch")]
        [SerializeField] private float minPitch = -70f;
        [SerializeField] private float maxPitch = 70f;
        [SerializeField] private bool applySpinePitch = true;
        [Tooltip("Middle spine pitch share while aiming rifles and shotguns (WeaponAimStyle 0).")]
        [SerializeField] private float middlePitchWeight = 0.4f;
        [Tooltip("Upper spine pitch share while aiming rifles and shotguns (WeaponAimStyle 0).")]
        [SerializeField] private float upperPitchWeight = 0.6f;
        [Tooltip("Middle spine pitch share while aiming the Glock (WeaponAimStyle 1).")]
        [SerializeField] private float glockMiddlePitchWeight = 0.4f;
        [Tooltip("Upper spine pitch share while aiming the Glock (WeaponAimStyle 1).")]
        [SerializeField] private float glockUpperPitchWeight = 0.6f;
        [Tooltip("Neck pitch share while aiming.")]
        [SerializeField] private float neckPitchWeight = 0.25f;
        [SerializeField] private float bendSmoothTime = 0.08f;
        [SerializeField] private float blendSpeed = 8f;

        [Header("Aim Pose")]
        [Tooltip("Spine yaw while aiming rifles and shotguns (WeaponAimStyle 0).")]
        [SerializeField] private float aimSpineYawOffset = 40f;
        [Tooltip("Spine yaw while aiming the Glock (WeaponAimStyle 1). Independent from rifle/shotgun yaw.")]
        [SerializeField] private float glockAimSpineYawOffset = -18f;

        [Header("Recoil")]
        [SerializeField] private float recoilStiffness = 120f;
        [SerializeField] private float recoilDamping = 16f;
        [SerializeField] private float maxRecoilAngle = 8f;

        private float _targetBlend;
        private float _blendWeight;
        private float _smoothedPitch;
        private float _pitchVelocity;
        private float _recoilAngle;
        private float _recoilVelocity;
        private Quaternion _middleBase = Quaternion.identity;
        private Quaternion _upperBase = Quaternion.identity;
        private Quaternion _neckBase = Quaternion.identity;
        private int _baseFrame = -1;
        private bool _usePistolAimYaw;

        public float CurrentPitch { get; set; }

        public float MinPitch => minPitch;
        public float MaxPitch => maxPitch;

        private void Awake()
        {
            AutoAssignBones();
        }

        private void OnEnable()
        {
            Application.onBeforeRender += OnBeforeRender;
        }

        private void OnDisable()
        {
            Application.onBeforeRender -= OnBeforeRender;
            ClearBoneOffsets();
        }

        public void SetAimingActive(bool active)
        {
            _targetBlend = active ? 1f : 0f;

            if (!active)
            {
                ClearBoneOffsets();
                CurrentPitch = 0f;
                _smoothedPitch = 0f;
                _pitchVelocity = 0f;
                _recoilAngle = 0f;
                _recoilVelocity = 0f;
            }
        }

        public void SetWeaponAimStyle(int weaponAimStyle)
        {
            _usePistolAimYaw = weaponAimStyle == 1;
        }

        public void AddRecoil(float impulse)
        {
            _recoilVelocity += impulse;
        }

        public void ClampPitch(ref float pitch)
        {
            pitch = Mathf.Clamp(pitch, minPitch, maxPitch);
        }

        public void BindSpine(Transform middle, Transform upper)
        {
            if (middle != null)
                spineMiddle = middle;
            if (upper != null)
                spineUpper = upper;
        }

        private void LateUpdate()
        {
            ApplySpineOffsets();
        }

        private void OnBeforeRender()
        {
            if (_blendWeight <= 0.001f && Mathf.Abs(_recoilAngle) < 0.01f)
                return;

            ApplySpineOffsets();
        }

        private void ApplySpineOffsets()
        {
            if (spineUpper == null)
                return;

            // The Animator rewrites both spine bones every frame, so the pose captured here is
            // the animated one. Both passes of the same frame rebuild from that cached base
            // instead of trying to subtract offsets that the Animator already discarded.
            if (_baseFrame != Time.frameCount)
                AdvanceFrameState();

            if (_blendWeight <= 0.001f && Mathf.Abs(_recoilAngle) < 0.01f)
                return;

            float pitchBlend = _smoothedPitch * _blendWeight;
            float yawBlend = GetActiveAimSpineYawOffset() * _blendWeight;
            float activeMiddlePitchWeight = GetActiveMiddlePitchWeight();
            float activeUpperPitchWeight = GetActiveUpperPitchWeight();

            if (spineMiddle != null)
            {
                ApplyBoneOffset(
                    spineMiddle,
                    _middleBase,
                    applySpinePitch ? pitchBlend * activeMiddlePitchWeight : 0f,
                    yawBlend * activeMiddlePitchWeight);
            }

            float upperPitch = applySpinePitch
                ? pitchBlend * activeUpperPitchWeight + _recoilAngle
                : _recoilAngle;

            ApplyBoneOffset(spineUpper, _upperBase, upperPitch, yawBlend);

            if (neck != null)
            {
                float neckPitch = applySpinePitch ? pitchBlend * neckPitchWeight : 0f;
                ApplyBoneOffset(neck, _neckBase, neckPitch, yawBlend * 0.35f);
            }
        }

        private float GetActiveAimSpineYawOffset()
        {
            return _usePistolAimYaw ? glockAimSpineYawOffset : aimSpineYawOffset;
        }

        private float GetActiveMiddlePitchWeight()
        {
            return _usePistolAimYaw ? glockMiddlePitchWeight : middlePitchWeight;
        }

        private float GetActiveUpperPitchWeight()
        {
            return _usePistolAimYaw ? glockUpperPitchWeight : upperPitchWeight;
        }

        private void AdvanceFrameState()
        {
            _baseFrame = Time.frameCount;

            if (spineMiddle != null)
                _middleBase = spineMiddle.localRotation;

            _upperBase = spineUpper.localRotation;

            if (neck != null)
                _neckBase = neck.localRotation;

            _blendWeight = Mathf.MoveTowards(_blendWeight, _targetBlend, blendSpeed * Time.deltaTime);
            UpdateRecoilSpring();

            float clampedPitch = Mathf.Clamp(CurrentPitch, minPitch, maxPitch);
            _smoothedPitch = Mathf.SmoothDamp(_smoothedPitch, clampedPitch, ref _pitchVelocity, bendSmoothTime);
        }

        // Offsets are built from the player root axes so the bend direction does not depend on
        // how the skeleton's spine bones happen to be oriented, then converted into the bone's
        // parent space so each bone still inherits its parent's bend.
        private void ApplyBoneOffset(Transform bone, Quaternion baseLocalRotation, float pitch, float yaw)
        {
            Quaternion offset = Quaternion.identity;

            if (Mathf.Abs(pitch) > 0.001f)
                offset = Quaternion.AngleAxis(pitch, ToParentSpace(bone, transform.right));

            if (Mathf.Abs(yaw) > 0.001f)
                offset = Quaternion.AngleAxis(yaw, ToParentSpace(bone, transform.up)) * offset;

            bone.localRotation = offset * baseLocalRotation;
        }

        private static Vector3 ToParentSpace(Transform bone, Vector3 worldAxis)
        {
            return bone.parent != null
                ? bone.parent.InverseTransformDirection(worldAxis).normalized
                : worldAxis;
        }

        private void ClearBoneOffsets()
        {
            // No transform restore needed: the Animator overwrites both spine bones on its next
            // evaluation, so dropping the cached base is enough to stop contributing.
            _middleBase = Quaternion.identity;
            _upperBase = Quaternion.identity;
            _baseFrame = -1;
        }

        private void UpdateRecoilSpring()
        {
            _recoilVelocity += (-recoilStiffness * _recoilAngle - recoilDamping * _recoilVelocity) * Time.deltaTime;
            _recoilAngle += _recoilVelocity * Time.deltaTime;
            _recoilAngle = Mathf.Clamp(_recoilAngle, 0f, maxRecoilAngle);
        }

        private void AutoAssignBones()
        {
            if (spineMiddle == null)
            {
                spineMiddle = FindBone(
                    "CC_Base_Spine01",
                    "guard/Armature/root ground/root hip/spine lower/spine middle");
                if (spineMiddle == null)
                    spineMiddle = FindBone("spine2", null);
            }

            if (spineUpper == null)
            {
                spineUpper = FindBone(
                    "CC_Base_Spine02",
                    null);
                if (spineUpper == null)
                    spineUpper = FindBone("spine3", null);
                if (spineUpper == null && spineMiddle != null)
                    spineUpper = spineMiddle.Find("spine upper");
            }

            if (neck == null)
            {
                neck = FindBone("CC_Base_NeckTwist01", null);
                if (neck == null)
                    neck = FindBone("CC_Base_NeckTwist02", null);
            }
        }

        private Transform FindBone(string preferredName, string legacyPath)
        {
            Transform[] all = GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i].name == preferredName)
                    return all[i];
            }

            if (!string.IsNullOrEmpty(legacyPath))
                return transform.Find(legacyPath);

            return null;
        }
    }
}
