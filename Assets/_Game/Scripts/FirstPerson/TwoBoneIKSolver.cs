using UnityEngine;

namespace Game.FirstPerson
{
    public static class TwoBoneIKSolver
    {
        public static void Solve(
            Transform root,
            Transform mid,
            Transform tip,
            Vector3 targetWorld,
            Vector3 poleWorld,
            float weight)
        {
            if (root == null || mid == null || tip == null || weight <= 0.001f)
                return;

            weight = Mathf.Clamp01(weight);

            Vector3 rootPos = root.position;
            Vector3 midPos = mid.position;
            Vector3 tipPos = tip.position;

            float upperLen = Vector3.Distance(rootPos, midPos);
            float lowerLen = Vector3.Distance(midPos, tipPos);
            float maxReach = upperLen + lowerLen;
            Vector3 toTarget = targetWorld - rootPos;
            float dist = toTarget.magnitude;
            if (dist < 0.001f)
                return;

            if (dist > maxReach * 0.999f)
                targetWorld = rootPos + toTarget.normalized * (maxReach * 0.999f);

            Vector3 desiredTip = Vector3.Lerp(tipPos, targetWorld, weight);

            // Law of cosines for mid joint angle
            toTarget = desiredTip - rootPos;
            dist = toTarget.magnitude;
            float a = upperLen;
            float b = lowerLen;
            float c = dist;
            float cosAngle = Mathf.Clamp((a * a + c * c - b * b) / (2f * a * c), -1f, 1f);
            float angle = Mathf.Acos(cosAngle);

            Vector3 axis = Vector3.Cross(toTarget.normalized, poleWorld - rootPos);
            if (axis.sqrMagnitude < 0.0001f)
                axis = Vector3.Cross(toTarget, Vector3.up);
            axis.Normalize();

            Quaternion rootOffset = Quaternion.AngleAxis(Mathf.Rad2Deg * angle, axis);
            Quaternion desiredRootRot = Quaternion.LookRotation(toTarget, Vector3.up) * rootOffset;
            root.rotation = Quaternion.Slerp(root.rotation, desiredRootRot, weight);

            midPos = mid.position;
            Vector3 midToTip = desiredTip - midPos;
            if (midToTip.sqrMagnitude > 0.0001f)
            {
                Quaternion desiredMid = Quaternion.LookRotation(midToTip, Vector3.up);
                mid.rotation = Quaternion.Slerp(mid.rotation, desiredMid, weight);
            }
        }
    }
}
