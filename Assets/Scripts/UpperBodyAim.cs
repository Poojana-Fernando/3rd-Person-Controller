using UnityEngine;

[DefaultExecutionOrder(60)]
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

    [Header("Recoil")]
    [SerializeField] private float recoilStiffness = 120f;
    [SerializeField] private float recoilDamping = 16f;
    [SerializeField] private float maxRecoilAngle = 8f;

    private float _targetBlend;
    private float _blendWeight;
    private float _smoothedPitch;
    private float _pitchVelocity;
    private float _smoothedYawOffset;
    private float _yawVelocity;
    private float _recoilAngle;
    private float _recoilVelocity;

    public float CurrentPitch { get; set; }
    public float CurrentYawOffset { get; set; }

    public float MinPitch => minPitch;
    public float MaxPitch => maxPitch;

    private void Awake()
    {
        AutoAssignBones();
    }

    public void SetAimingActive(bool active)
    {
        _targetBlend = active ? 1f : 0f;

        if (!active)
        {
            CurrentPitch = 0f;
            CurrentYawOffset = 0f;
            _smoothedPitch = 0f;
            _pitchVelocity = 0f;
            _smoothedYawOffset = 0f;
            _yawVelocity = 0f;
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
        if (spineUpper == null)
            return;

        _blendWeight = Mathf.MoveTowards(_blendWeight, _targetBlend, blendSpeed * Time.deltaTime);
        UpdateRecoilSpring();

        if (_blendWeight <= 0.001f && Mathf.Abs(_recoilAngle) < 0.01f)
            return;

        float clampedPitch = Mathf.Clamp(CurrentPitch, minPitch, maxPitch);
        _smoothedPitch = Mathf.SmoothDamp(_smoothedPitch, clampedPitch, ref _pitchVelocity, bendSmoothTime);
        _smoothedYawOffset = Mathf.SmoothDamp(_smoothedYawOffset, CurrentYawOffset, ref _yawVelocity, bendSmoothTime);

        float pitchBlend = _smoothedPitch * _blendWeight;
        float yawBlend = _smoothedYawOffset * _blendWeight;
        float middlePitch = pitchBlend * middlePitchWeight;
        float upperPitch = pitchBlend * upperPitchWeight + _recoilAngle;
        float middleYaw = yawBlend * middlePitchWeight;
        float upperYaw = yawBlend * upperPitchWeight;

        if (applySpinePitch && spineMiddle != null)
        {
            Quaternion animatedMiddle = spineMiddle.localRotation;
            spineMiddle.localRotation = animatedMiddle * Quaternion.Euler(middlePitch, middleYaw, 0f);
        }
        else if (spineMiddle != null && Mathf.Abs(middleYaw) > 0.001f)
        {
            Quaternion animatedMiddle = spineMiddle.localRotation;
            spineMiddle.localRotation = animatedMiddle * Quaternion.Euler(0f, middleYaw, 0f);
        }

        if (spineUpper != null)
        {
            Quaternion animatedUpper = spineUpper.localRotation;
            if (applySpinePitch || Mathf.Abs(upperYaw) > 0.001f || Mathf.Abs(_recoilAngle) > 0.001f)
                spineUpper.localRotation = animatedUpper * Quaternion.Euler(upperPitch, upperYaw, 0f);
        }
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
            spineMiddle = transform.Find("guard/Armature/root ground/root hip/spine lower/spine middle");
        }

        if (spineUpper == null && spineMiddle != null)
        {
            spineUpper = spineMiddle.Find("spine upper");
        }
    }
}
