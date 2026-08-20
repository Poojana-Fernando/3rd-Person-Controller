using UnityEngine;

[DefaultExecutionOrder(80)]
public class GunAimAlign : MonoBehaviour
{
    [SerializeField] private PlayerGunController gunController;
    [SerializeField] private ResidentEvilAimCamera aimCamera;
    [SerializeField] private Transform aimGun;
    [SerializeField] private Transform muzzle;
    [SerializeField] private float maxCorrectionAngle = 25f;
    [SerializeField] private float alignSmoothTime = 0.06f;

    private void Awake()
    {
        AutoAssignReferences();
    }

    private void LateUpdate()
    {
        if (aimGun == null || muzzle == null || aimGun.parent == null || gunController == null || !gunController.IsAiming)
            return;

        if (aimCamera == null)
            aimCamera = FindFirstObjectByType<ResidentEvilAimCamera>(FindObjectsInactive.Include);

        if (aimCamera == null || !aimCamera.IsActive)
            return;

        Quaternion animatedLocal = aimGun.localRotation;
        Vector3 aimPoint = GetAimPoint();
        Vector3 desiredDirection = aimPoint - muzzle.position;
        if (desiredDirection.sqrMagnitude < 0.001f)
            return;

        desiredDirection.Normalize();

        Vector3 currentDirection = muzzle.forward;
        float angle = Vector3.Angle(currentDirection, desiredDirection);
        if (angle < 0.01f)
            return;

        Quaternion correction = Quaternion.FromToRotation(currentDirection, desiredDirection);
        if (angle > maxCorrectionAngle)
            correction = Quaternion.RotateTowards(Quaternion.identity, correction, maxCorrectionAngle);

        Quaternion targetWorldRotation = correction * aimGun.rotation;
        Quaternion targetLocalRotation = Quaternion.Inverse(aimGun.parent.rotation) * targetWorldRotation;

        float smooth = alignSmoothTime <= 0.001f ? 1f : 1f - Mathf.Exp(-Time.deltaTime / alignSmoothTime);
        aimGun.localRotation = Quaternion.Slerp(animatedLocal, targetLocalRotation, smooth);
    }

    private Vector3 GetAimPoint()
    {
        Ray cameraRay = aimCamera.GetScreenCenterRay();
        if (Physics.Raycast(cameraRay, out RaycastHit hit, gunController.MaxRange, gunController.HitMask, QueryTriggerInteraction.Ignore))
        {
            if (!IsSelfCollider(hit.collider))
                return hit.point;
        }

        return cameraRay.origin + cameraRay.direction * gunController.MaxRange;
    }

    private bool IsSelfCollider(Collider collider)
    {
        return collider != null && collider.transform.IsChildOf(transform);
    }

    private void AutoAssignReferences()
    {
        if (gunController == null)
            gunController = GetComponent<PlayerGunController>();

        if (aimCamera == null)
            aimCamera = FindFirstObjectByType<ResidentEvilAimCamera>(FindObjectsInactive.Include);

        if (aimGun == null)
        {
            Transform wrist = transform.Find("guard/Armature/root ground/root hip/spine lower/spine middle/spine upper/arm right shoulder 1/arm right shoulder 2/arm right elbow/arm right wrist");
            if (wrist != null)
                aimGun = wrist.Find("AimGun");
        }

        if (muzzle == null && aimGun != null)
            muzzle = aimGun.Find("muzzle");
    }
}
