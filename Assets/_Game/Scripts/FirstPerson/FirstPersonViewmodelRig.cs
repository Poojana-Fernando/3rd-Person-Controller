using UnityEngine;

namespace Game.FirstPerson
{
    public class FirstPersonViewmodelRig : MonoBehaviour
    {
        [SerializeField] private Transform shoulderAnchor;
        private Transform _player;
        private Animator _sourceAnimator;
        private Animator _viewAnimator;
        private Transform _viewRoot;
        private bool _active;

        public void SetShoulderAnchor(Transform anchor) => shoulderAnchor = anchor;

        public void Bind(Transform player, SkinnedMeshRenderer armsSource)
        {
            _player = player;
            DestroyViewRoot();

            if (player == null)
                return;

            _sourceAnimator = player.GetComponentInChildren<Animator>();
            BuildViewRoot(armsSource);
        }

        public void SetActive(bool active)
        {
            _active = active;
            if (_viewRoot != null)
                _viewRoot.gameObject.SetActive(active);
        }

        public void LateUpdateSolve(Transform rightGrip, Transform leftGrip)
        {
            if (!_active || _viewAnimator == null || shoulderAnchor == null)
                return;

            CopyBoneRotations(_sourceAnimator, _viewAnimator);

            Transform rShoulder = _viewAnimator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            Transform rMid = _viewAnimator.GetBoneTransform(HumanBodyBones.RightLowerArm);
            Transform rHand = _viewAnimator.GetBoneTransform(HumanBodyBones.RightHand);
            Transform lShoulder = _viewAnimator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            Transform lMid = _viewAnimator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
            Transform lHand = _viewAnimator.GetBoneTransform(HumanBodyBones.LeftHand);

            if (rShoulder != null)
                rShoulder.position = shoulderAnchor.TransformPoint(new Vector3(0.14f, -0.02f, 0.02f));
            if (lShoulder != null)
                lShoulder.position = shoulderAnchor.TransformPoint(new Vector3(-0.14f, -0.02f, 0.02f));

            if (rightGrip != null && rShoulder != null && rMid != null && rHand != null)
            {
                TwoBoneIKSolver.Solve(
                    rShoulder,
                    rMid,
                    rHand,
                    rightGrip.position,
                    shoulderAnchor.position + shoulderAnchor.forward * 0.5f + shoulderAnchor.up * 0.2f,
                    1f);
            }

            if (leftGrip != null && lShoulder != null && lMid != null && lHand != null)
            {
                TwoBoneIKSolver.Solve(
                    lShoulder,
                    lMid,
                    lHand,
                    leftGrip.position,
                    shoulderAnchor.position + shoulderAnchor.forward * 0.5f + shoulderAnchor.up * 0.2f,
                    1f);
            }
        }

        private void BuildViewRoot(SkinnedMeshRenderer armsSource)
        {
            _viewRoot = new GameObject("FPViewmodelArms").transform;
            _viewRoot.SetParent(shoulderAnchor != null ? shoulderAnchor : transform, false);

            if (armsSource == null)
                return;

            SkinnedMeshRenderer clone = Instantiate(armsSource, _viewRoot);
            clone.gameObject.name = "ViewmodelArmsMesh";
            SetLayerRecursive(clone.transform, FirstPersonLayers.ViewmodelLayer);

            _viewAnimator = clone.GetComponent<Animator>();
            if (_viewAnimator == null && _sourceAnimator != null)
            {
                _viewAnimator = clone.gameObject.AddComponent<Animator>();
                _viewAnimator.avatar = _sourceAnimator.avatar;
                _viewAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            }
        }

        private static void CopyBoneRotations(Animator source, Animator dest)
        {
            if (source == null || dest == null || !source.isHuman || !dest.isHuman)
                return;

            for (int i = 0; i < (int)HumanBodyBones.LastBone; i++)
            {
                HumanBodyBones bone = (HumanBodyBones)i;
                Transform s = source.GetBoneTransform(bone);
                Transform d = dest.GetBoneTransform(bone);
                if (s == null || d == null)
                    continue;
                d.SetPositionAndRotation(s.position, s.rotation);
            }
        }

        private void DestroyViewRoot()
        {
            if (_viewRoot != null)
                Destroy(_viewRoot.gameObject);
            _viewRoot = null;
            _viewAnimator = null;
        }

        private static void SetLayerRecursive(Transform root, int layer)
        {
            root.gameObject.layer = layer;
            for (int i = 0; i < root.childCount; i++)
                SetLayerRecursive(root.GetChild(i), layer);
        }

        private void OnDisable()
        {
            SetActive(false);
        }
    }
}
