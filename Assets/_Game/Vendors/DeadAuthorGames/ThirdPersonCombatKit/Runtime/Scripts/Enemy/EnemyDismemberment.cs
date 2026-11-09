using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;


namespace DeadAuthorGames.ThirdPersonCombat
{
    public class EnemyDismemberment : MonoBehaviour
    {
        [Serializable]
        public class PartEntry
        {
            public string partId = "part";
            public string displayName = "Part";
            public EnemyHitbox[] hitboxes = Array.Empty<EnemyHitbox>();
            public SkinnedMeshRenderer sourceRenderer;
            [Tooltip("Optional static MeshFilter/MeshRenderer template. When empty, the live skinned mesh is baked at sever time.")]
            public GameObject detachedTemplate;
            [Tooltip("Inspector preview only when useFixedPartHealth is false; runtime pool comes from maxHealth / healthShareDivisor.")]
            public float partHealth = 6.25f;
            public bool canDismember = true;
            [Tooltip("When false, hits never reduce this part's pool or overall character health.")]
            public bool contributesHealth = true;
            public GameObject bloodHitOverride;
            public GameObject bloodDecalOverride;
            public GameObject separationImpactOverride;
        }

        [Header("Parts")]
        [SerializeField] private List<PartEntry> parts = new List<PartEntry>();
        [Tooltip("Each flesh part pool is EnemyHealth.MaxHealth divided by this value.")]
        [SerializeField] private int healthShareDivisor = 16;
        [Tooltip("When enabled, each part uses its serialized partHealth instead of maxHealth / divisor.")]
        [SerializeField] private bool useFixedPartHealth;

        [Header("Default Effects")]
        [SerializeField] private GameObject bloodHitPrefab;
        [SerializeField] private float bloodHitLifetime = 3f;
        [SerializeField] private GameObject bloodDecalPrefab;
        [SerializeField] private float bloodDecalLifetime = 12f;
        [SerializeField] private float bloodDecalSurfaceOffset = 0.01f;
        [SerializeField] private Vector3 bloodDecalScale = Vector3.one;
        [SerializeField] private GameObject separationImpactPrefab;
        [SerializeField] private float separationImpactLifetime = 3f;

        [Header("Skeleton Hits")]
        [SerializeField] private SkinnedMeshRenderer skeletonRenderer;
        [SerializeField] private GameObject skeletonHitPrefab;
        [SerializeField] private float skeletonHitLifetime = 3f;

        [Header("Effect Rules")]
        [SerializeField] private bool effectsOnProtectedParts = true;
        [SerializeField] private bool effectsOnDetachedRegions = true;
        [SerializeField] private bool effectsOnCorpseHits = true;

        [Header("Detached Physics")]
        [SerializeField] private float detachedMass = 1.25f;
        [SerializeField] private float detachedDrag = 0.2f;
        [SerializeField] private float detachedAngularDrag = 0.35f;
        [Tooltip("Unused by default — severed pieces persist as pickups. Set > 0 only for optional timed cleanup.")]
        [SerializeField] private float detachedLifetime = 0f;
        [SerializeField] private float upwardForceBias = 0.15f;
        [SerializeField] private float torqueImpulse = 1.5f;
        [SerializeField] private float selfCollisionIgnoreSeconds = 0.35f;
        [SerializeField] private CollisionDetectionMode collisionMode = CollisionDetectionMode.ContinuousSpeculative;

        private readonly Dictionary<EnemyHitbox, List<int>> _hitboxToParts = new Dictionary<EnemyHitbox, List<int>>();
        private readonly Dictionary<string, int> _partIdToIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly HashSet<int> _spentParts = new HashSet<int>();
        private float[] _remainingHealth = Array.Empty<float>();
        private float[] _partShare = Array.Empty<float>();
        private IPartHealthPool _health;
        private Collider[] _ownColliders = Array.Empty<Collider>();

        public IReadOnlyList<PartEntry> Parts => parts;
        public float PartHealthShare => GetPartShareAmount();

        private void Awake()
        {
            ResolveHealthPool();
            if (skeletonRenderer == null)
                skeletonRenderer = FindSkeletonRenderer();

            RebuildLookup();
            CacheOwnColliders();
            IgnoreSiblingHitboxCollisions();
        }

        private void ResolveHealthPool()
        {
            if (_health != null)
                return;

            _health = GetComponent<IPartHealthPool>();
            if (_health == null)
                _health = GetComponentInParent<IPartHealthPool>();
        }

        public float GetPartShareAmount()
        {
            float max = _health != null ? _health.MaxHealth : 100f;
            int divisor = Mathf.Max(1, healthShareDivisor);
            return max / divisor;
        }

        public void RebuildLookup()
        {
            ResolveHealthPool();

            _hitboxToParts.Clear();
            _partIdToIndex.Clear();
            for (int i = 0; i < parts.Count; i++)
            {
                PartEntry part = parts[i];
                if (part == null)
                    continue;

                if (!string.IsNullOrEmpty(part.partId) && !_partIdToIndex.ContainsKey(part.partId))
                    _partIdToIndex[part.partId] = i;

                if (part.hitboxes == null)
                    continue;

                for (int h = 0; h < part.hitboxes.Length; h++)
                {
                    EnemyHitbox hitbox = part.hitboxes[h];
                    if (hitbox == null)
                        continue;

                    if (!_hitboxToParts.TryGetValue(hitbox, out List<int> indices))
                    {
                        indices = new List<int>();
                        _hitboxToParts[hitbox] = indices;
                    }

                    if (!indices.Contains(i))
                        indices.Add(i);
                }
            }

            float share = GetPartShareAmount();
            Array.Resize(ref _remainingHealth, parts.Count);
            Array.Resize(ref _partShare, parts.Count);

            for (int i = 0; i < parts.Count; i++)
            {
                PartEntry part = parts[i];
                bool hasHealth = part != null && part.contributesHealth;
                float pool = 0f;

                if (hasHealth)
                {
                    pool = useFixedPartHealth && part.partHealth > 0f
                        ? part.partHealth
                        : share;

                    if (!useFixedPartHealth)
                        part.partHealth = share;
                }
                else if (part != null)
                {
                    part.partHealth = 0f;
                }

                _partShare[i] = pool;
                _remainingHealth[i] = pool;
            }

            CacheOwnColliders();
            IgnoreSiblingHitboxCollisions();
        }

        public bool HasMissingPart(string matchKey)
        {
            if (string.IsNullOrEmpty(matchKey))
                return false;
            if (!_partIdToIndex.TryGetValue(matchKey, out int index))
                return false;
            return _spentParts.Contains(index);
        }

        public bool TryReattachPart(string matchKey)
        {
            if (string.IsNullOrEmpty(matchKey))
                return false;
            if (!_partIdToIndex.TryGetValue(matchKey, out int index))
                return false;
            if (!_spentParts.Contains(index))
                return false;

            PartEntry part = parts[index];
            if (part == null)
                return false;

            float share = index < _partShare.Length ? _partShare[index] : GetPartShareAmount();
            if (share <= 0.0001f || !part.contributesHealth)
                return false;

            _spentParts.Remove(index);
            _remainingHealth[index] = share;

            if (part.sourceRenderer != null)
                part.sourceRenderer.enabled = true;

            ResolveHealthPool();
            _health?.RestorePartHealth(share);
            return true;
        }

        public void HandleHitboxDamage(
            EnemyHitbox hitbox,
            float amount,
            Vector3 hitPoint,
            Vector3 hitNormal,
            Vector3 shotDirection,
            float impactForce,
            GameObject source)
        {
            if (amount <= 0f)
                return;

            // Clients never mutate dismemberment state when networked (player or scene WorldSync).
            if (networkDismemberment != null && networkDismemberment.IsSpawned &&
                !networkDismemberment.IsServerAuthority)
                return;

            var worldSync = Game.Net.NetworkWorldSync.Instance;
            if ((networkDismemberment == null || !networkDismemberment.IsSpawned) &&
                worldSync != null && worldSync.IsSpawned && !worldSync.IsServer)
                return;

            bool isCorpse = _health != null && _health.IsDead;
            if (isCorpse && !effectsOnCorpseHits)
                return;

            int partIndex = ResolvePartIndex(hitbox, hitPoint);

            if (IsSkeletonHit(partIndex, hitPoint))
            {
                SpawnSkeletonHitEffect(hitPoint, hitNormal);
                return;
            }

            if (partIndex < 0 || partIndex >= parts.Count)
            {
                SpawnSkeletonHitEffect(hitPoint, hitNormal);
                return;
            }

            PartEntry part = parts[partIndex];
            if (part == null)
                return;

            bool alreadySpent = _spentParts.Contains(partIndex);
            if (alreadySpent)
            {
                if (effectsOnDetachedRegions)
                    SpawnSkeletonHitEffect(hitPoint, hitNormal);
                return;
            }

            // Cosmetic-only parts (e.g. .004 / .011): VFX only, never HP or detachment.
            if (!part.contributesHealth)
            {
                if (effectsOnProtectedParts)
                    SpawnHitEffects(part, hitPoint, hitNormal);
                return;
            }

            bool shouldSpawnFleshEffects = part.canDismember || effectsOnProtectedParts;
            if (shouldSpawnFleshEffects)
                SpawnHitEffects(part, hitPoint, hitNormal);

            if (isCorpse)
                return;

            _remainingHealth[partIndex] = Mathf.Max(0f, _remainingHealth[partIndex] - amount);
            if (_remainingHealth[partIndex] > 0f)
                return;

            LosePart(partIndex, hitPoint, hitNormal, shotDirection, impactForce, source);
        }

        private bool IsSkeletonHit(int partIndex, Vector3 hitPoint)
        {
            if (partIndex < 0 || partIndex >= parts.Count)
                return true;

            if (_spentParts.Contains(partIndex))
                return true;

            PartEntry part = parts[partIndex];
            if (part?.sourceRenderer == null || !part.sourceRenderer.enabled)
                return true;

            return false;
        }

        private void LosePart(
            int partIndex,
            Vector3 hitPoint,
            Vector3 hitNormal,
            Vector3 shotDirection,
            float impactForce,
            GameObject source)
        {
            if (!_spentParts.Add(partIndex))
                return;

            PartEntry part = parts[partIndex];
            if (part == null)
                return;

            float share = partIndex < _partShare.Length ? _partShare[partIndex] : GetPartShareAmount();
            if (share <= 0.0001f || !part.contributesHealth)
                return;

            bool playerNet = networkDismemberment != null && networkDismemberment.IsSpawned;
            var worldSync = Game.Net.NetworkWorldSync.Instance;
            bool sceneNet = !playerNet && worldSync != null && worldSync.IsSpawned && worldSync.IsServer;
            bool networked = playerNet || sceneNet;

            if (part.canDismember && part.sourceRenderer != null)
            {
                // When networked, sever visuals come from NetworkSeveredPart (one shared piece).
                // Do not hide the skinned mesh yet — host BakeMesh needs it enabled.
                if (networked)
                    SpawnSeparationEffect(part, hitPoint, hitNormal);
                else
                    DetachPartVisual(part, hitPoint, hitNormal, shotDirection, impactForce);
            }

            _health?.ApplyPartLoss(share, hitPoint, hitNormal, source);

            // Spawn + bake while sourceRenderer is still enabled, then hide the body mesh.
            if (playerNet)
                networkDismemberment.ServerNotifyPartLost(partIndex, hitPoint, hitNormal, shotDirection, impactForce);
            else if (sceneNet)
                worldSync.ServerNotifyScenePartLost(this, partIndex, hitPoint, hitNormal, shotDirection, impactForce);

            if (networked && part.canDismember && part.sourceRenderer != null)
                part.sourceRenderer.enabled = false;
        }

        [System.NonSerialized] public Game.Net.NetworkDismemberment networkDismemberment;

        public bool TryReattachPartNetwork(string matchKey, out int partIndex)
        {
            partIndex = -1;
            if (string.IsNullOrEmpty(matchKey))
                return false;
            if (!_partIdToIndex.TryGetValue(matchKey, out partIndex))
                return false;
            return TryReattachPart(matchKey);
        }

        public void ReplayLosePartVisual(
            int partIndex,
            Vector3 hitPoint,
            Vector3 hitNormal,
            Vector3 shotDirection,
            float impactForce)
        {
            if (partIndex < 0 || partIndex >= parts.Count)
                return;
            if (!_spentParts.Add(partIndex))
                return;

            PartEntry part = parts[partIndex];
            if (part == null)
                return;

            // Networked (player NetworkDismemberment or scene WorldSync): hide only;
            // NetworkSeveredPart owns the fallen piece (avoid local DetachPartVisual duplicates).
            bool hideOnly =
                (networkDismemberment != null && networkDismemberment.IsSpawned) ||
                (Game.Net.NetworkWorldSync.Instance != null && Game.Net.NetworkWorldSync.Instance.IsSpawned);
            if (hideOnly)
            {
                SpawnSeparationEffect(part, hitPoint, hitNormal);
                if (part.sourceRenderer != null)
                    part.sourceRenderer.enabled = false;
                return;
            }

            if (part.canDismember && part.sourceRenderer != null)
                DetachPartVisual(part, hitPoint, hitNormal, shotDirection, impactForce);
            else if (part.sourceRenderer != null)
                part.sourceRenderer.enabled = false;
        }

        /// <summary>Build a detached mesh piece for NetworkSeveredPart without adding local physics/pickup.</summary>
        public GameObject CreateDetachedVisual(int partIndex)
        {
            if (partIndex < 0 || partIndex >= parts.Count)
                return null;
            PartEntry part = parts[partIndex];
            if (part == null)
                return null;
            return CreateDetachedPiece(part);
        }

        public string GetPartMatchKey(int partIndex)
        {
            if (partIndex < 0 || partIndex >= parts.Count || parts[partIndex] == null)
                return string.Empty;
            PartEntry part = parts[partIndex];
            return string.IsNullOrEmpty(part.partId) ? part.displayName : part.partId;
        }

        public string GetPartDisplayName(int partIndex)
        {
            if (partIndex < 0 || partIndex >= parts.Count || parts[partIndex] == null)
                return "Part";
            PartEntry part = parts[partIndex];
            return string.IsNullOrEmpty(part.displayName) ? part.partId : part.displayName;
        }

        /// <summary>Cosmetic blood/hit VFX for networked peers (no HP mutation).</summary>
        public void PlayHitEffectsVisual(Vector3 hitPoint, Vector3 hitNormal)
        {
            int partIndex = ResolvePartIndex(null, hitPoint);
            if (partIndex >= 0 && partIndex < parts.Count && parts[partIndex] != null)
                SpawnHitEffects(parts[partIndex], hitPoint, hitNormal);
            else
                SpawnSkeletonHitEffect(hitPoint, hitNormal);
        }

        public void PlayHitEffectsVisual(int partIndex, Vector3 hitPoint, Vector3 hitNormal)
        {
            if (partIndex >= 0 && partIndex < parts.Count && parts[partIndex] != null)
                SpawnHitEffects(parts[partIndex], hitPoint, hitNormal);
            else
                SpawnSkeletonHitEffect(hitPoint, hitNormal);
        }

        public void ReplayReattachVisual(int partIndex)
        {
            if (partIndex < 0 || partIndex >= parts.Count)
                return;

            _spentParts.Remove(partIndex);
            PartEntry part = parts[partIndex];
            if (part?.sourceRenderer != null)
                part.sourceRenderer.enabled = true;
        }

        public void ForceHidePart(int partIndex)
        {
            if (partIndex < 0 || partIndex >= parts.Count)
                return;

            _spentParts.Add(partIndex);
            PartEntry part = parts[partIndex];
            if (part?.sourceRenderer != null)
                part.sourceRenderer.enabled = false;
        }

        private void DetachPartVisual(
            PartEntry part,
            Vector3 hitPoint,
            Vector3 hitNormal,
            Vector3 shotDirection,
            float impactForce)
        {
            GameObject detached = CreateDetachedPiece(part);
            if (detached == null)
            {
                if (part.sourceRenderer != null)
                    part.sourceRenderer.enabled = false;
                return;
            }

            detached.transform.SetParent(null, true);

            if (part.sourceRenderer != null)
                part.sourceRenderer.enabled = false;

            DetachedBodyPart pickup = detached.GetComponent<DetachedBodyPart>();
            if (pickup == null)
                pickup = detached.AddComponent<DetachedBodyPart>();
            pickup.enabled = true;
            pickup.Configure(
                string.IsNullOrEmpty(part.partId) ? part.displayName : part.partId,
                string.IsNullOrEmpty(part.displayName) ? part.partId : part.displayName,
                gameObject);

            Rigidbody body = ConfigurePhysics(detached);
            IgnoreSelfCollisionsTemporarily(detached);

            // Piece is already world-unparented and ignoring owner colliders —
            // enable dynamics immediately so pickup physics do not depend on pause/FixedUpdate timing.
            body.isKinematic = false;
            body.useGravity = true;
            body.WakeUp();

            Vector3 direction = shotDirection.sqrMagnitude > 0.0001f
                ? shotDirection.normalized
                : -hitNormal.normalized;
            direction.y += upwardForceBias;
            direction.Normalize();

            float force = Mathf.Max(0f, impactForce);
            if (force > 0.001f)
                body.AddForceAtPosition(direction * force, hitPoint, ForceMode.Impulse);

            if (torqueImpulse > 0.001f)
            {
                Vector3 torque = UnityEngine.Random.onUnitSphere * torqueImpulse;
                body.AddTorque(torque, ForceMode.Impulse);
            }

            SpawnSeparationEffect(part, hitPoint, hitNormal);

            // Optional cleanup only — default 0 keeps pickups forever.
            if (detachedLifetime > 0.1f)
                Destroy(detached, detachedLifetime);
        }

        private int ResolvePartIndex(EnemyHitbox hitbox, Vector3 hitPoint)
        {
            if (hitbox != null && _hitboxToParts.TryGetValue(hitbox, out List<int> candidates) && candidates.Count > 0)
            {
                // Closest living mesh wins — including no-health cosmetic parts (.004 / .011).
                int living = FindClosestPartIndex(candidates, hitPoint, preferLiving: true, preferHealth: false);
                if (living >= 0)
                    return living;

                return FindClosestPartIndex(candidates, hitPoint, preferLiving: false, preferHealth: false);
            }

            int best = -1;
            float bestDist = float.MaxValue;

            for (int i = 0; i < parts.Count; i++)
            {
                PartEntry part = parts[i];
                if (part?.sourceRenderer == null)
                    continue;
                if (_spentParts.Contains(i))
                    continue;

                float dist = (part.sourceRenderer.bounds.ClosestPoint(hitPoint) - hitPoint).sqrMagnitude;
                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = i;
                }
            }

            return best;
        }

        private int FindClosestPartIndex(List<int> candidates, Vector3 hitPoint, bool preferLiving, bool preferHealth)
        {
            int best = -1;
            float bestDist = float.MaxValue;

            for (int i = 0; i < candidates.Count; i++)
            {
                int index = candidates[i];
                if (index < 0 || index >= parts.Count)
                    continue;

                if (preferLiving && _spentParts.Contains(index))
                    continue;

                PartEntry part = parts[index];
                if (part?.sourceRenderer == null)
                    continue;

                if (preferHealth && !part.contributesHealth)
                    continue;

                float dist = (part.sourceRenderer.bounds.ClosestPoint(hitPoint) - hitPoint).sqrMagnitude;
                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = index;
                }
            }

            return best;
        }

        private GameObject CreateDetachedPiece(PartEntry part)
        {
            if (part.detachedTemplate != null)
            {
                GameObject clone = Instantiate(part.detachedTemplate);
                clone.name = part.detachedTemplate.name + "_Detached";
                clone.SetActive(true);
                clone.transform.SetParent(null, true);

                if (part.sourceRenderer != null)
                {
                    clone.transform.position = part.sourceRenderer.transform.position;
                    clone.transform.rotation = part.sourceRenderer.transform.rotation;
                    clone.transform.localScale = part.sourceRenderer.transform.lossyScale;
                    CopyMaterials(part.sourceRenderer, clone);
                }

                return clone;
            }

            return BakeSkinnedPiece(part);
        }

        private GameObject BakeSkinnedPiece(PartEntry part)
        {
            SkinnedMeshRenderer source = part.sourceRenderer;
            if (source == null || source.sharedMesh == null)
                return null;

            // BakeMesh on a disabled SMR often yields an empty/invisible mesh (host race).
            bool wasEnabled = source.enabled;
            if (!wasEnabled)
                source.enabled = true;

            Mesh baked = new Mesh { name = source.sharedMesh.name + "_Baked" };
            try
            {
                source.BakeMesh(baked, true);
            }
            finally
            {
                if (!wasEnabled)
                    source.enabled = false;
            }

            if (baked == null || baked.vertexCount == 0)
            {
                if (Application.isPlaying)
                    Destroy(baked);
                else
                    DestroyImmediate(baked);
                return null;
            }

            GameObject go = new GameObject(source.gameObject.name + "_Detached");
            go.transform.SetParent(null, true);
            go.transform.SetPositionAndRotation(source.transform.position, source.transform.rotation);
            go.transform.localScale = source.transform.lossyScale;

            MeshFilter filter = go.AddComponent<MeshFilter>();
            filter.sharedMesh = baked;

            MeshRenderer renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = source.sharedMaterials;
            renderer.shadowCastingMode = source.shadowCastingMode;
            renderer.receiveShadows = source.receiveShadows;

            return go;
        }

        private static void CopyMaterials(SkinnedMeshRenderer source, GameObject target)
        {
            if (source == null || target == null)
                return;

            Renderer renderer = target.GetComponentInChildren<Renderer>(true);
            if (renderer != null)
                renderer.sharedMaterials = source.sharedMaterials;
        }

        private Rigidbody ConfigurePhysics(GameObject detached)
        {
            Collider existingCollider = detached.GetComponentInChildren<Collider>(true);
            if (existingCollider == null)
            {
                Renderer renderer = detached.GetComponentInChildren<Renderer>(true);
                BoxCollider box = detached.AddComponent<BoxCollider>();
                if (renderer != null)
                {
                    Bounds bounds = renderer.bounds;
                    Transform t = detached.transform;
                    Vector3 localCenter = t.InverseTransformPoint(bounds.center);
                    Vector3 lossy = t.lossyScale;
                    Vector3 localSize = new Vector3(
                        SafeDivide(bounds.size.x, Mathf.Abs(lossy.x)),
                        SafeDivide(bounds.size.y, Mathf.Abs(lossy.y)),
                        SafeDivide(bounds.size.z, Mathf.Abs(lossy.z)));
                    box.center = localCenter;
                    box.size = Vector3.Max(localSize, Vector3.one * 0.05f);
                }
            }

            Rigidbody body = detached.GetComponent<Rigidbody>();
            if (body == null)
                body = detached.AddComponent<Rigidbody>();

            body.mass = Mathf.Max(0.05f, detachedMass);
            body.linearDamping = Mathf.Max(0f, detachedDrag);
            body.angularDamping = Mathf.Max(0f, detachedAngularDrag);
            body.collisionDetectionMode = collisionMode;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            // Living character meshes never get this Rigidbody — only world pieces after unparent.
            body.isKinematic = false;
            body.useGravity = true;
            body.WakeUp();
            return body;
        }

        private static float SafeDivide(float value, float divisor)
        {
            return Mathf.Abs(divisor) < 0.0001f ? value : value / divisor;
        }

        private void IgnoreSelfCollisionsTemporarily(GameObject detached)
        {
            Collider[] detachedColliders = detached.GetComponentsInChildren<Collider>(true);
            if (detachedColliders.Length == 0 || _ownColliders.Length == 0)
                return;

            for (int i = 0; i < detachedColliders.Length; i++)
            {
                Collider a = detachedColliders[i];
                if (a == null)
                    continue;

                for (int j = 0; j < _ownColliders.Length; j++)
                {
                    Collider b = _ownColliders[j];
                    if (b == null)
                        continue;
                    Physics.IgnoreCollision(a, b, true);
                }
            }

            if (selfCollisionIgnoreSeconds > 0f)
                StartCoroutine(RestoreCollisionsAfterDelay(detachedColliders, selfCollisionIgnoreSeconds));
        }

        private IEnumerator RestoreCollisionsAfterDelay(Collider[] detachedColliders, float delay)
        {
            yield return new WaitForSeconds(delay);

            for (int i = 0; i < detachedColliders.Length; i++)
            {
                Collider a = detachedColliders[i];
                if (a == null)
                    continue;

                for (int j = 0; j < _ownColliders.Length; j++)
                {
                    Collider b = _ownColliders[j];
                    if (b == null)
                        continue;
                    Physics.IgnoreCollision(a, b, false);
                }
            }
        }

        private void SpawnHitEffects(PartEntry part, Vector3 hitPoint, Vector3 hitNormal)
        {
            GameObject blood = part.bloodHitOverride != null ? part.bloodHitOverride : bloodHitPrefab;
            SpawnTimedEffect(blood, hitPoint, hitNormal, bloodHitLifetime);

            GameObject decal = part.bloodDecalOverride != null ? part.bloodDecalOverride : bloodDecalPrefab;
            if (decal != null)
            {
                Vector3 position = hitPoint + hitNormal.normalized * bloodDecalSurfaceOffset;
                GameObject instance = Instantiate(decal, position, Quaternion.LookRotation(hitNormal));
                instance.transform.localScale = Vector3.Scale(instance.transform.localScale, bloodDecalScale);
                Destroy(instance, Mathf.Max(0.5f, bloodDecalLifetime));
            }
        }

        private void SpawnSeparationEffect(PartEntry part, Vector3 hitPoint, Vector3 hitNormal)
        {
            GameObject prefab = part.separationImpactOverride != null
                ? part.separationImpactOverride
                : separationImpactPrefab;
            SpawnTimedEffect(prefab, hitPoint, hitNormal, separationImpactLifetime);
        }

        private void SpawnSkeletonHitEffect(Vector3 hitPoint, Vector3 hitNormal)
        {
            GameObject prefab = skeletonHitPrefab != null ? skeletonHitPrefab : bloodHitPrefab;
            SpawnTimedEffect(prefab, hitPoint, hitNormal, skeletonHitLifetime);
        }

        private static void SpawnTimedEffect(GameObject prefab, Vector3 point, Vector3 normal, float lifetime)
        {
            if (prefab == null)
                return;

            Vector3 forward = normal.sqrMagnitude > 0.0001f ? normal : Vector3.up;
            GameObject fx = Instantiate(prefab, point, Quaternion.LookRotation(forward));
            Destroy(fx, Mathf.Max(0.5f, lifetime));
        }

        private void CacheOwnColliders()
        {
            _ownColliders = GetComponentsInChildren<Collider>(true);
        }

        /// <summary>
        /// Hitbox capsules must not shove each other (or the locomotion capsule) when they overlap.
        /// Raycasts still hit them; only physics contacts are suppressed.
        /// </summary>
        private void IgnoreSiblingHitboxCollisions()
        {
            EnemyHitbox[] hitboxes = GetComponentsInChildren<EnemyHitbox>(true);
            if (hitboxes == null || hitboxes.Length == 0)
                return;

            var hitboxColliders = new List<Collider>(hitboxes.Length);
            for (int i = 0; i < hitboxes.Length; i++)
            {
                EnemyHitbox hitbox = hitboxes[i];
                if (hitbox == null)
                    continue;

                Collider col = hitbox.GetComponent<Collider>();
                if (col != null)
                    hitboxColliders.Add(col);
            }

            for (int i = 0; i < hitboxColliders.Count; i++)
            {
                Collider a = hitboxColliders[i];
                if (a == null)
                    continue;

                for (int j = i + 1; j < hitboxColliders.Count; j++)
                {
                    Collider b = hitboxColliders[j];
                    if (b == null)
                        continue;
                    Physics.IgnoreCollision(a, b, true);
                }
            }

            // Keep hitboxes from fighting the character's own movement capsule / root colliders.
            Collider[] rootColliders = GetComponents<Collider>();
            for (int i = 0; i < rootColliders.Length; i++)
            {
                Collider root = rootColliders[i];
                if (root == null)
                    continue;

                for (int j = 0; j < hitboxColliders.Count; j++)
                {
                    Collider hitboxCol = hitboxColliders[j];
                    if (hitboxCol == null || hitboxCol == root)
                        continue;
                    Physics.IgnoreCollision(root, hitboxCol, true);
                }
            }
        }

        private SkinnedMeshRenderer FindSkeletonRenderer()
        {
            SkinnedMeshRenderer[] renderers = GetComponentsInChildren<SkinnedMeshRenderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] != null &&
                    string.Equals(renderers[i].gameObject.name, "Skeleton", StringComparison.OrdinalIgnoreCase))
                    return renderers[i];
            }

            return null;
        }

#if UNITY_EDITOR
        public void EditorSetParts(List<PartEntry> entries)
        {
            parts = entries != null ? new List<PartEntry>(entries) : new List<PartEntry>();
            RebuildLookup();
        }

        public void EditorBindEffects(
            GameObject bloodHit,
            GameObject bloodDecal,
            GameObject separationImpact)
        {
            if (bloodHit != null)
                bloodHitPrefab = bloodHit;
            if (bloodDecal != null)
                bloodDecalPrefab = bloodDecal;
            if (separationImpact != null)
                separationImpactPrefab = separationImpact;
        }

        public void EditorBindSkeleton(SkinnedMeshRenderer skeleton, GameObject skeletonHit)
        {
            if (skeleton != null)
                skeletonRenderer = skeleton;
            if (skeletonHit != null)
                skeletonHitPrefab = skeletonHit;
        }

        public void EditorSetHealthShareDivisor(int divisor)
        {
            healthShareDivisor = Mathf.Max(1, divisor);
            useFixedPartHealth = false;
        }

        public List<PartEntry> EditorGetPartsMutable()
        {
            return parts;
        }
#endif
    }
}
