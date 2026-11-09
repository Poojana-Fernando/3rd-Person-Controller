using UnityEngine;

namespace Game.Net
{
    /// <summary>
    /// Marks the center of the random player spawn area (50 m radius by default).
    /// Place one in MyThirdperson; NetworkPlayerSetup falls back to a baked center if missing.
    /// </summary>
    public class PlayerSpawnArea : MonoBehaviour
    {
        public static PlayerSpawnArea Instance { get; private set; }

        [SerializeField] private float radius = 50f;
        [SerializeField] private float minSeparation = 5f;

        public float Radius => radius;
        public float MinSeparation => minSeparation;
        public Vector3 Center => transform.position;

        private void Awake()
        {
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.25f);
            Gizmos.DrawWireSphere(transform.position, radius);
        }
#endif
    }
}
