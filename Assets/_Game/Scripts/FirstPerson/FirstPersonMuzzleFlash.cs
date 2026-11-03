using UnityEngine;

namespace Game.FirstPerson
{
    public class FirstPersonMuzzleFlash : MonoBehaviour
    {
        [SerializeField] private float lifetime = 0.04f;
        [SerializeField] private float lightIntensity = 2.5f;
        [SerializeField] private Vector2 scaleRange = new Vector2(0.8f, 1.25f);

        private bool _active;

        public void SetActive(bool active) => _active = active;

        public void PlayAt(Transform muzzle)
        {
            if (!_active || muzzle == null)
                return;

            SpawnFlashQuad(muzzle);
            SpawnFlashLight(muzzle);
        }

        private void SpawnFlashQuad(Transform muzzle)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = "FP_MuzzleFlash";
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(muzzle, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
            float s = Random.Range(scaleRange.x, scaleRange.y) * 0.08f;
            go.transform.localScale = new Vector3(s, s, s);
            go.layer = muzzle.gameObject.layer;

            var mr = go.GetComponent<MeshRenderer>();
            var mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            if (mat.shader == null)
                mat = new Material(Shader.Find("Unlit/Color"));
            mat.color = new Color(1f, 0.85f, 0.45f, 0.85f);
            mat.SetInt("_Surface", 1);
            mr.material = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;

            Destroy(go, lifetime);
        }

        private void SpawnFlashLight(Transform muzzle)
        {
            var lightGo = new GameObject("FP_MuzzleLight");
            lightGo.transform.SetParent(muzzle, false);
            lightGo.transform.localPosition = Vector3.zero;
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = 3f;
            light.intensity = lightIntensity;
            light.shadows = LightShadows.None;
            Destroy(lightGo, lifetime * 0.5f);
        }
    }
}
