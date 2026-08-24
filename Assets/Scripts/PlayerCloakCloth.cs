using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Cloth))]
public class PlayerCloakCloth : MonoBehaviour
{
    [SerializeField] private Cloth cloth;
    [SerializeField] private bool forceNoColliders = true;

    private void Awake()
    {
        if (cloth == null)
            cloth = GetComponent<Cloth>();
        ApplyNoCollision();
    }

    private void OnEnable()
    {
        ApplyNoCollision();
    }

    public void ApplyNoCollision()
    {
        if (!forceNoColliders)
            return;

        if (cloth == null)
            cloth = GetComponent<Cloth>();
        if (cloth == null)
            return;

        cloth.capsuleColliders = System.Array.Empty<CapsuleCollider>();
        cloth.sphereColliders = System.Array.Empty<ClothSphereColliderPair>();
    }
}
