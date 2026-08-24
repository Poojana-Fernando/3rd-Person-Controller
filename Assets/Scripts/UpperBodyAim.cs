using UnityEngine;

// RigBuilder evaluates constraints during the Animator's PlayableGraph update,
// which finishes before LateUpdate. High execution order keeps spine offsets applied
// after rig solve and other LateUpdate scripts (camera 70 / gun controller 75).
[DefaultExecutionOrder(500)]
public class UpperBodyAim : MonoBehaviour
{
    [Header("Bones")]
    [SerializeField] private Transform spineMiddle;
    [SerializeField] private Transform spineUpper;

    [Header("Pitch")]
    [SerializeField] private float minPitch = -40f;
    [SerializeField] private float maxPitch = 40f;
    [SerializeField] private bool applySpinePitch = true;
    [SerializeField] private float middlePitchWeight = 0.4f;
    [SerializeField] private float upperPitchWeight = 0.6f;
    [SerializeField] private float bendSmoothTime = 0.08f;
    [SerializeField] private float blendSpeed = 8f;

    [Header("Aim Pose")]
    [Tooltip("Spine yaw twist while aiming. Negative = left (shows right-hand gun on shoulder cam). Positive = right.")]
    [SerializeField] private float aimSpineYawOffset = -14f;

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
    private int _baseFrame = -1;

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

    public void AddRecoil(float impulse)
    {
        _recoilVelocity += impulse;
    }

    public void ClampPitch(ref float pitch)
    {
        pitch = Mathf.Clamp(pitch, minPitch, maxPitch);
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
        float yawBlend = aimSpineYawOffset * _blendWeight;

        if (spineMiddle != null)
        {
            ApplyBoneOffset(
                spineMiddle,
                _middleBase,
                applySpinePitch ? pitchBlend * middlePitchWeight : 0f,
                yawBlend * middlePitchWeight);
        }

        float upperPitch = applySpinePitch
            ? pitchBlend * upperPitchWeight + _recoilAngle
            : _recoilAngle;

        ApplyBoneOffset(spineUpper, _upperBase, upperPitch, yawBlend);
    }

    private void AdvanceFrameState()
    {
        _baseFrame = Time.frameCount;

        if (spineMiddle != null)
            _middleBase = spineMiddle.localRotation;

        _upperBase = spineUpper.localRotation;

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
        }

        if (spineUpper == null)
        {
            spineUpper = FindBone(
                "CC_Base_Spine02",
                null);
            if (spineUpper == null && spineMiddle != null)
                spineUpper = spineMiddle.Find("spine upper");
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
