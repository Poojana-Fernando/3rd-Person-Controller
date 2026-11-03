using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Game.FirstPerson
{
    /// <summary>
    /// Owner first-person body visibility: legs/torso visible, head/arms/world gun shadows-only.
    /// Optional mesh splits for viewmodel arms source.
    /// </summary>
    public class FirstPersonBodyMeshSplitter : MonoBehaviour
    {
        private Transform _player;
        private bool _active;

        private readonly List<RendererState> _states = new();
        private Transform _visualPullbackRoot;
        private float _pullback;

        private struct RendererState
        {
            public Renderer Renderer;
            public int Layer;
            public ShadowCastingMode ShadowMode;
            public bool Enabled;
        }

        public SkinnedMeshRenderer ArmsSource { get; private set; }

        public void Bind(Transform player) => _player = player;

        public void SetActive(bool active)
        {
            if (_active == active)
                return;

            _active = active;
            if (active)
                Apply();
            else
                Restore();
        }

        public void SetLookPitch(float pitch)
        {
            if (!_active || _visualPullbackRoot == null)
                return;

            var settings = FirstPersonSettings.Runtime;
            float down = Mathf.Max(0f, -pitch - settings.bodyPullbackStartPitch);
            _pullback = Mathf.Clamp01(down / 40f) * settings.bodyPullbackMax;
            _visualPullbackRoot.localPosition = new Vector3(0f, 0f, -_pullback);
        }

        private void Apply()
        {
            Restore();
            if (_player == null)
                return;

            Animator animator = _player.GetComponentInChildren<Animator>();
            Transform head = GetBone(animator, HumanBodyBones.Head);
            Transform lUpper = GetBone(animator, HumanBodyBones.LeftUpperArm);
            Transform rUpper = GetBone(animator, HumanBodyBones.RightUpperArm);

            _visualPullbackRoot = _player.Find("FPBodyPullback");
            if (_visualPullbackRoot == null)
            {
                var go = new GameObject("FPBodyPullback");
                go.transform.SetParent(_player, false);
                _visualPullbackRoot = go.transform;
            }

            Renderer[] renderers = _player.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer r = renderers[i];
                if (r == null || r is ParticleSystemRenderer)
                    continue;

                if (IsViewmodelOnly(r.gameObject))
                    continue;

                BodyPart part = ClassifyRenderer(r, head, lUpper, rUpper);
                if (part == BodyPart.Arms && r is SkinnedMeshRenderer smr && ArmsSource == null)
                    ArmsSource = smr;

                if (part == BodyPart.Rest)
                    continue;

                _states.Add(new RendererState
                {
                    Renderer = r,
                    Layer = r.gameObject.layer,
                    ShadowMode = r.shadowCastingMode,
                    Enabled = r.enabled,
                });

                r.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
            }

            HideWorldGunMeshes();
        }

        private void HideWorldGunMeshes()
        {
            Transform[] all = _player.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                Transform t = all[i];
                if (t == null || !t.gameObject.activeInHierarchy)
                    continue;
                if (!t.name.StartsWith("AimGun") && !t.name.StartsWith("HoldGun"))
                    continue;

                Renderer[] rs = t.GetComponentsInChildren<Renderer>(true);
                for (int j = 0; j < rs.Length; j++)
                {
                    Renderer r = rs[j];
                    _states.Add(new RendererState
                    {
                        Renderer = r,
                        Layer = r.gameObject.layer,
                        ShadowMode = r.shadowCastingMode,
                        Enabled = r.enabled,
                    });
                    r.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
                }
            }
        }

        private static bool IsViewmodelOnly(GameObject go)
        {
            return go.layer == FirstPersonLayers.ViewmodelLayer;
        }

        private enum BodyPart
        {
            Rest,
            Head,
            Arms,
        }

        private static BodyPart ClassifyRenderer(Renderer r, Transform head, Transform lUpper, Transform rUpper)
        {
            Transform t = r.transform;
            if (head != null && IsUnder(t, head))
                return BodyPart.Head;

            if (lUpper != null && IsUnder(t, lUpper))
                return BodyPart.Arms;
            if (rUpper != null && IsUnder(t, rUpper))
                return BodyPart.Arms;

            if (r is SkinnedMeshRenderer smr)
            {
                if (DominatesArmBones(smr))
                    return BodyPart.Arms;
            }

            string n = t.name.ToLowerInvariant();
            if (n.Contains("head") || n.Contains("hair") || n.Contains("helmet"))
                return BodyPart.Head;
            if (n.Contains("arm") || n.Contains("hand") || n.Contains("shoulder"))
                return BodyPart.Arms;

            return BodyPart.Rest;
        }

        private static bool DominatesArmBones(SkinnedMeshRenderer smr)
        {
            Mesh mesh = smr.sharedMesh;
            if (mesh == null)
                return false;

            BoneWeight[] weights = mesh.boneWeights;
            if (weights == null || weights.Length == 0)
                return false;

            int armHits = 0;
            int total = Mathf.Min(weights.Length, 512);
            Transform[] bones = smr.bones;
            for (int i = 0; i < total; i++)
            {
                if (IsArmBoneName(BoneName(bones, weights[i].boneIndex0, weights[i].weight0))
                    || IsArmBoneName(BoneName(bones, weights[i].boneIndex1, weights[i].weight1))
                    || IsArmBoneName(BoneName(bones, weights[i].boneIndex2, weights[i].weight2))
                    || IsArmBoneName(BoneName(bones, weights[i].boneIndex3, weights[i].weight3)))
                    armHits++;
            }

            return armHits > total * 0.15f;
        }

        private static string BoneName(Transform[] bones, int index, float weight)
        {
            if (weight < 0.25f || bones == null || index < 0 || index >= bones.Length || bones[index] == null)
                return string.Empty;
            return bones[index].name;
        }

        private static bool IsArmBoneName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return false;
            string n = name.ToLowerInvariant();
            return n.Contains("arm") || n.Contains("hand") || n.Contains("shoulder") || n.Contains("clavicle");
        }

        private static bool IsUnder(Transform t, Transform ancestor)
        {
            while (t != null)
            {
                if (t == ancestor)
                    return true;
                t = t.parent;
            }

            return false;
        }

        private static Transform GetBone(Animator animator, HumanBodyBones bone)
        {
            if (animator == null || !animator.isHuman)
                return null;
            return animator.GetBoneTransform(bone);
        }

        private void Restore()
        {
            for (int i = 0; i < _states.Count; i++)
            {
                RendererState s = _states[i];
                if (s.Renderer == null)
                    continue;
                s.Renderer.gameObject.layer = s.Layer;
                s.Renderer.shadowCastingMode = s.ShadowMode;
                s.Renderer.enabled = s.Enabled;
            }

            _states.Clear();
            ArmsSource = null;

            if (_visualPullbackRoot != null)
                _visualPullbackRoot.localPosition = Vector3.zero;
        }

        private void OnDisable() => Restore();
    }
}
