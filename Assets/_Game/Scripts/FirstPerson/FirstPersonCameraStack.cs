using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Game.FirstPerson
{
    public class FirstPersonCameraStack : MonoBehaviour
    {
        [SerializeField] private Camera baseCamera;
        [SerializeField] private Camera viewmodelCamera;
        [SerializeField] private float viewmodelFov = 50f;
        [SerializeField] private float viewmodelNearClip = 0.01f;

        private int _savedCullingMask;
        private bool _hasSavedCullingMask;

        public Camera ViewmodelCamera => viewmodelCamera;

        public void SetVisibleBodyMode(bool visibleBody)
        {
            if (baseCamera == null)
                baseCamera = GetComponent<Camera>();
            if (baseCamera == null)
                return;

            SaveCullingMaskIfNeeded();
            RemoveViewmodelFromStack();
            if (viewmodelCamera != null)
                viewmodelCamera.enabled = false;
        }

        public void RestoreBaseCamera()
        {
            if (baseCamera == null)
                return;

            if (_hasSavedCullingMask)
            {
                baseCamera.cullingMask = _savedCullingMask;
                _hasSavedCullingMask = false;
            }

            RemoveViewmodelFromStack();
            if (viewmodelCamera != null)
                viewmodelCamera.enabled = false;
        }

        public void EnsureStack(float viewmodelFovOverride = 0f)
        {
            if (baseCamera == null)
                baseCamera = GetComponent<Camera>();

            if (baseCamera == null)
                return;

            SaveCullingMaskIfNeeded();

            int viewLayer = FirstPersonLayers.ViewmodelLayer;
            float vmFov = viewmodelFovOverride > 1f
                ? viewmodelFovOverride
                : FirstPersonSettings.Runtime.viewmodelFov;

            if (viewmodelCamera == null)
            {
                var go = new GameObject("ViewmodelCam");
                go.transform.SetParent(baseCamera.transform, false);
                go.transform.localPosition = Vector3.zero;
                go.transform.localRotation = Quaternion.identity;
                viewmodelCamera = go.AddComponent<Camera>();
            }

            viewmodelCamera.transform.SetParent(baseCamera.transform, false);
            viewmodelCamera.clearFlags = CameraClearFlags.Depth;
            viewmodelCamera.cullingMask = 1 << viewLayer;
            viewmodelCamera.fieldOfView = vmFov;
            viewmodelCamera.nearClipPlane = viewmodelNearClip;
            viewmodelCamera.farClipPlane = baseCamera.farClipPlane;
            viewmodelCamera.depth = baseCamera.depth + 1;
            viewmodelCamera.usePhysicalProperties = false;
            viewmodelCamera.enabled = true;

            var baseData = baseCamera.GetUniversalAdditionalCameraData();
            var overlayData = viewmodelCamera.GetUniversalAdditionalCameraData();
            if (overlayData != null)
                overlayData.renderType = CameraRenderType.Overlay;

            if (baseData != null)
            {
                baseData.renderType = CameraRenderType.Base;
                if (!baseData.cameraStack.Contains(viewmodelCamera))
                    baseData.cameraStack.Add(viewmodelCamera);
            }

            int hiddenLayer = FirstPersonLayers.HiddenBodyLayer;
            baseCamera.cullingMask &= ~(1 << viewLayer);
            if (hiddenLayer >= 0 && hiddenLayer <= 31)
                baseCamera.cullingMask &= ~(1 << hiddenLayer);
        }

        public void Bind(Camera main, Transform parentForOverlay)
        {
            baseCamera = main;
            if (viewmodelCamera != null && parentForOverlay != null)
                viewmodelCamera.transform.SetParent(parentForOverlay, false);
        }

        private void SaveCullingMaskIfNeeded()
        {
            if (baseCamera == null || _hasSavedCullingMask)
                return;

            _savedCullingMask = baseCamera.cullingMask;
            _hasSavedCullingMask = true;
        }

        private void RemoveViewmodelFromStack()
        {
            if (baseCamera == null || viewmodelCamera == null)
                return;

            var baseData = baseCamera.GetUniversalAdditionalCameraData();
            if (baseData != null && baseData.cameraStack.Contains(viewmodelCamera))
                baseData.cameraStack.Remove(viewmodelCamera);
        }
    }
}
