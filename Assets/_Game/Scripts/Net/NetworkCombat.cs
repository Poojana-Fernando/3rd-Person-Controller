using DeadAuthorGames.ThirdPersonCombat;
using Unity.Netcode;
using UnityEngine;

namespace Game.Net
{
    /// <summary>
    /// Server-authoritative hitscan combat with replicated shot FX for all peers.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public class NetworkCombat : NetworkBehaviour
    {
        public const byte HitKindMiss = 0;
        public const byte HitKindFlesh = 1;
        public const byte HitKindSurface = 2;

        [SerializeField] private PlayerGunController gunController;
        [SerializeField] private WeaponInventory inventory;
        [SerializeField] private LayerMask hitMask = ~0;

        private void Awake()
        {
            if (gunController == null)
                gunController = GetComponent<PlayerGunController>();
            if (inventory == null)
                inventory = GetComponent<WeaponInventory>();
        }

        public override void OnNetworkSpawn()
        {
            if (gunController != null)
                gunController.networkCombat = this;
        }

        public override void OnNetworkDespawn()
        {
            if (gunController != null && gunController.networkCombat == this)
                gunController.networkCombat = null;
        }

        public void RequestFire(
            Vector3 origin,
            Vector3 baseDirection,
            float damage,
            float range,
            float impactForce,
            float spreadDegrees,
            int pellets,
            int weaponSlot,
            NetworkObject claimedTarget,
            int claimedHitboxId,
            Vector3 claimedHitPoint)
        {
            if (!IsOwner)
                return;

            NetworkObjectReference claimRef = default;
            bool hasClaim = claimedTarget != null && claimedTarget.IsSpawned;
            if (hasClaim)
                claimRef = claimedTarget;

            FireServerRpc(
                origin,
                baseDirection.normalized,
                damage,
                range,
                impactForce,
                spreadDegrees,
                Mathf.Max(1, pellets),
                weaponSlot,
                Random.Range(int.MinValue, int.MaxValue),
                hasClaim,
                claimRef,
                claimedHitboxId,
                claimedHitPoint);
        }

        public void NotifyReloadFx()
        {
            if (!IsOwner)
                return;
            ReloadFxServerRpc();
        }

        [ServerRpc]
        private void ReloadFxServerRpc(ServerRpcParams rpcParams = default)
        {
            ReloadFxClientRpc();
        }

        [ClientRpc]
        private void ReloadFxClientRpc()
        {
            if (IsOwner)
                return;
            gunController?.PlayRemoteReloadSound();
        }

        [ServerRpc]
        private void FireServerRpc(
            Vector3 origin,
            Vector3 direction,
            float damage,
            float range,
            float impactForce,
            float spreadDegrees,
            int pellets,
            int weaponSlot,
            int seed,
            bool hasClaim,
            NetworkObjectReference claimRef,
            int claimedHitboxId,
            Vector3 claimedHitPoint,
            ServerRpcParams rpcParams = default)
        {
            if (!IsServer)
                return;

            if (Vector3.Distance(origin, transform.position + Vector3.up * 1.4f) > 1.25f)
                return;

            if (inventory != null)
            {
                WeaponDefinition def = inventory.Get(weaponSlot);
                if (def == null || !inventory.TryConsumeRound(def))
                    return;
            }

            var rng = new System.Random(seed);
            direction = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.forward;
            float dist = Mathf.Max(1f, range);
            int count = Mathf.Max(1, pellets);

            var ends = new Vector3[count];
            var normals = new Vector3[count];
            var kinds = new byte[count];

            for (int i = 0; i < count; i++)
            {
                Vector3 fireDirection = ApplySpread(direction, spreadDegrees, rng);
                bool usedClaim = false;

                if (i == 0 && hasClaim && TryAcceptClaimedHit(
                        origin, fireDirection, dist, claimRef, claimedHitboxId, claimedHitPoint,
                        out NetworkObject claimObj, out Vector3 hitPoint, out Vector3 hitNormal, out Collider hitCol))
                {
                    usedClaim = true;
                    ApplyServerDamage(hitCol, hitPoint, hitNormal, fireDirection, damage, impactForce);
                    ends[i] = hitPoint;
                    normals[i] = hitNormal;
                    kinds[i] = hitCol != null && hitCol.GetComponentInParent<EnemyDismemberment>() != null
                        ? HitKindFlesh
                        : HitKindSurface;
                }

                if (usedClaim)
                    continue;

                RaycastHit[] hits = Physics.RaycastAll(origin, fireDirection, dist, hitMask, QueryTriggerInteraction.Ignore);
                System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

                RaycastHit hit = default;
                bool hasHit = false;
                for (int h = 0; h < hits.Length; h++)
                {
                    if (hits[h].collider != null && hits[h].collider.transform.root == transform.root)
                        continue;
                    hit = hits[h];
                    hasHit = true;
                    break;
                }

                if (!hasHit)
                {
                    ends[i] = origin + fireDirection * dist;
                    normals[i] = -fireDirection;
                    kinds[i] = HitKindMiss;
                    continue;
                }

                ApplyServerDamage(hit.collider, hit.point, hit.normal, fireDirection, damage, impactForce);
                ends[i] = hit.point;
                normals[i] = hit.normal;
                kinds[i] = hit.collider != null && hit.collider.GetComponentInParent<EnemyDismemberment>() != null
                    ? HitKindFlesh
                    : HitKindSurface;
            }

            ShotFxClientRpc(origin, ends, normals, kinds);
        }

        private bool TryAcceptClaimedHit(
            Vector3 origin,
            Vector3 fireDirection,
            float range,
            NetworkObjectReference claimRef,
            int claimedHitboxIndex,
            Vector3 claimedHitPoint,
            out NetworkObject claimObj,
            out Vector3 hitPoint,
            out Vector3 hitNormal,
            out Collider hitCol)
        {
            claimObj = null;
            hitPoint = claimedHitPoint;
            hitNormal = -fireDirection;
            hitCol = null;

            if (!claimRef.TryGet(out claimObj) || claimObj == null)
                return false;

            if (claimObj.transform.root == transform.root)
                return false;

            float distToClaim = Vector3.Distance(origin, claimedHitPoint);
            if (distToClaim > range + 0.5f)
                return false;

            Vector3 toClaim = claimedHitPoint - origin;
            if (toClaim.sqrMagnitude < 0.0001f)
                return false;

            float angle = Vector3.Angle(fireDirection, toClaim.normalized);
            if (angle > 8f)
                return false;

            // Resolve the claimed hitbox by index (stable across peers for the same prefab).
            EnemyHitbox[] boxes = claimObj.GetComponentsInChildren<EnemyHitbox>(true);
            EnemyHitbox hitbox = null;
            if (claimedHitboxIndex >= 0 && claimedHitboxIndex < boxes.Length)
                hitbox = boxes[claimedHitboxIndex];

            if (hitbox == null)
            {
                // Fallback: nearest hitbox to claimed point.
                float best = float.MaxValue;
                for (int i = 0; i < boxes.Length; i++)
                {
                    if (boxes[i] == null)
                        continue;
                    Collider c = boxes[i].GetComponent<Collider>();
                    if (c == null)
                        continue;
                    float d = Vector3.Distance(claimedHitPoint, c.ClosestPoint(claimedHitPoint));
                    if (d < best)
                    {
                        best = d;
                        hitbox = boxes[i];
                    }
                }
            }

            if (hitbox != null)
                hitCol = hitbox.GetComponent<Collider>();
            if (hitCol == null)
                hitCol = claimObj.GetComponentInChildren<Collider>();

            // Validate against hitbox bounds (not character root) — limbs can be >1.5m from root.
            if (hitCol != null)
            {
                Vector3 closest = hitCol.ClosestPoint(claimedHitPoint);
                if (Vector3.Distance(claimedHitPoint, closest) > 0.75f)
                    return false;
            }
            else if (Vector3.Distance(claimedHitPoint, claimObj.transform.position) > 2.5f)
            {
                return false;
            }

            hitNormal = (origin - claimedHitPoint).normalized;
            if (hitNormal.sqrMagnitude < 0.0001f)
                hitNormal = Vector3.up;
            return true;
        }

        private static Vector3 ApplySpread(Vector3 direction, float spreadDegrees, System.Random rng)
        {
            if (spreadDegrees <= 0.0001f)
                return direction.normalized;

            Vector3 forward = direction.normalized;
            Vector3 right = Vector3.Cross(forward, Vector3.up);
            if (right.sqrMagnitude < 0.0001f)
                right = Vector3.Cross(forward, Vector3.right);
            right.Normalize();
            Vector3 up = Vector3.Cross(right, forward).normalized;

            float yaw = (float)(rng.NextDouble() * 2.0 - 1.0) * spreadDegrees;
            float pitch = (float)(rng.NextDouble() * 2.0 - 1.0) * spreadDegrees;
            return (Quaternion.AngleAxis(yaw, up) * Quaternion.AngleAxis(pitch, right) * forward).normalized;
        }

        private void ApplyServerDamage(
            Collider collider,
            Vector3 hitPoint,
            Vector3 hitNormal,
            Vector3 shotDirection,
            float damage,
            float impactForce)
        {
            if (collider == null)
                return;

            var impact = new BulletImpactData
            {
                Damage = damage,
                HitPoint = hitPoint,
                HitNormal = hitNormal,
                ShotDirection = shotDirection,
                ImpactForce = Mathf.Max(0f, impactForce),
                Collider = collider,
                Source = gameObject
            };

            var receiver = collider.GetComponentInParent<IBulletImpactReceiver>();
            if (receiver != null)
            {
                receiver.ReceiveBulletImpact(in impact);
                return;
            }

            var damageable = collider.GetComponentInParent<IDamageable>();
            damageable?.TakeDamage(impact.Damage, impact.HitPoint, impact.HitNormal, impact.Source);
        }

        [ClientRpc]
        private void ShotFxClientRpc(Vector3 origin, Vector3[] ends, Vector3[] normals, byte[] kinds)
        {
            if (IsOwner)
                return;

            gunController?.PlayRemoteShot(origin, ends, normals, kinds);

            if (ends == null || kinds == null)
                return;

            for (int i = 0; i < ends.Length && i < kinds.Length; i++)
            {
                if (kinds[i] != HitKindFlesh)
                    continue;

                // Find nearest dismemberment at the hit for blood VFX on remotes.
                Collider[] cols = Physics.OverlapSphere(ends[i], 0.35f, hitMask, QueryTriggerInteraction.Collide);
                for (int c = 0; c < cols.Length; c++)
                {
                    var dismember = cols[c].GetComponentInParent<EnemyDismemberment>();
                    if (dismember == null)
                        continue;
                    Vector3 n = normals != null && i < normals.Length ? normals[i] : Vector3.up;
                    dismember.PlayHitEffectsVisual(ends[i], n);
                    break;
                }
            }
        }

        public void RequestPickup(NetworkObjectReference pickupRef)
        {
            if (!IsOwner)
                return;
            PickupServerRpc(pickupRef);
        }

        [ServerRpc]
        private void PickupServerRpc(NetworkObjectReference pickupRef, ServerRpcParams rpcParams = default)
        {
            if (!pickupRef.TryGet(out NetworkObject netObj) || netObj == null)
                return;

            WeaponPickup pickup = netObj.GetComponent<WeaponPickup>();
            if (pickup == null || pickup.Weapon == null || inventory == null)
                return;

            if (!inventory.TryAdd(pickup.Weapon) && !inventory.Has(pickup.Weapon.slot))
                return;

            if (gunController != null)
                gunController.SelectSlot(pickup.Weapon.slot);

            netObj.Despawn(true);
        }

        /// <summary>World-sync path: add a weapon definition to this player's inventory on all peers.</summary>
        public void ClientAddWeapon(WeaponDefinition definition)
        {
            if (definition == null || inventory == null)
                return;
            inventory.TryAdd(definition);
        }
    }
}
