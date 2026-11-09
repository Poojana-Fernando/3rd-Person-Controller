using UnityEngine;
using UnityEngine.Animations.Rigging;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

namespace DeadAuthorGames.ThirdPersonCombat
{
    [DefaultExecutionOrder(-200)]
    [DisallowMultipleComponent]
    public class PlayerCharacterSlot : MonoBehaviour
    {
        static readonly string[] KnownVisualNames =
        {
            "CPD",
            "guard",
            "Sci-FI Warrior Armor"
        };

        static readonly string[] RightHandNames =
        {
            "CC_Base_R_Hand", "r hand", "hand_R", "Hand_R", "mixamorig:RightHand", "mixamorig6:RightHand"
        };

        static readonly string[] SpineMiddleNames =
        {
            "CC_Base_Spine01", "spine2", "spine middle", "Spine", "mixamorig:Spine"
        };

        static readonly string[] SpineUpperNames =
        {
            "CC_Base_Spine02", "spine3", "spine upper", "Chest", "mixamorig:Spine2", "mixamorig:Spine1"
        };

        static readonly string[] NeckNames =
        {
            "CC_Base_NeckTwist01", "neck", "neck lower", "Neck", "mixamorig:Neck"
        };

        static readonly string[] HeadNames =
        {
            "CC_Base_Head", "head", "Head", "mixamorig:Head"
        };

        [Header("Character")]
        [Tooltip("Humanoid character prefab or model. Drop it here to wire animator, weapons, and aim.")]
        [SerializeField] GameObject characterPrefab;
        [Tooltip("When enabled, assigning a prefab in the Inspector applies wiring immediately.")]
        [SerializeField] bool applyOnAssign = true;

        [Header("Optional Bone Overrides")]
        [Tooltip("Leave empty to auto-find from Humanoid mapping or common bone names.")]
        [SerializeField] Transform handOverride;
        [SerializeField] Transform spineMiddleOverride;
        [SerializeField] Transform spineUpperOverride;
        [SerializeField] Transform neckOverride;
        [SerializeField] Transform headOverride;

        [SerializeField, HideInInspector] GameObject spawnedVisual;

        public GameObject CharacterPrefab => characterPrefab;
        public bool ApplyOnAssign => applyOnAssign;
        public string LastResult { get; private set; } = string.Empty;
        public GameObject ActiveVisual { get; private set; }

        void Awake()
        {
            if (characterPrefab != null)
                Apply(false);
        }

        public void SetCharacterPrefab(GameObject prefab)
        {
            characterPrefab = prefab;
        }

        public static PlayerCharacterSlot EnsureOn(GameObject player)
        {
            if (player == null)
                return null;

            var slot = player.GetComponent<PlayerCharacterSlot>();
            if (slot == null)
                slot = player.AddComponent<PlayerCharacterSlot>();
            return slot;
        }

        public bool Apply(bool log)
        {
            if (characterPrefab == null)
            {
                LastResult = "Assign a Humanoid character prefab.";
                if (log)
                    Debug.LogWarning("[PlayerCharacterSlot] " + LastResult, this);
                return false;
            }

            GameObject visual = ResolveOrSpawnVisual();
            if (visual == null)
            {
                LastResult = "Could not create or find the character visual.";
                if (log)
                    Debug.LogError("[PlayerCharacterSlot] " + LastResult, this);
                return false;
            }

            ActiveVisual = visual;
            HideOtherVisuals(visual);
            ResetLocalPose(visual.transform);
            ApplyAvatar(visual);

            Transform hand = ResolveBone(visual, handOverride, HumanBodyBones.RightHand, RightHandNames);
            Transform spineMiddle = ResolveBone(visual, spineMiddleOverride, HumanBodyBones.Chest, SpineMiddleNames);
            if (spineMiddle == null)
                spineMiddle = ResolveBone(visual, null, HumanBodyBones.Spine, SpineMiddleNames);

            Transform spineUpper = ResolveBone(visual, spineUpperOverride, HumanBodyBones.UpperChest, SpineUpperNames);
            if (spineUpper == null)
                spineUpper = ResolveBone(visual, null, HumanBodyBones.Chest, SpineUpperNames);
            if (spineUpper == null)
                spineUpper = spineMiddle;

            Transform neck = ResolveBone(visual, neckOverride, HumanBodyBones.Neck, NeckNames);
            Transform head = ResolveBone(visual, headOverride, HumanBodyBones.Head, HeadNames);
            if (neck == null)
                neck = head;

            if (hand == null || spineMiddle == null || head == null)
            {
                LastResult =
                    "Missing required bones. Need right hand, spine/chest, and head. " +
                    "Use a Humanoid rig or assign the optional bone overrides.";
                if (log)
                    Debug.LogError("[PlayerCharacterSlot] " + LastResult, this);
                return false;
            }

            BindControllers(hand, neck, spineMiddle, spineUpper, head);
            int gunCount = AttachWeaponsToNewHand(hand);
            RebuildRig();

            LastResult =
                $"Wired '{visual.name}'. Avatar={DescribeAvatar()}. " +
                $"Hand={hand.name}, Spine={spineMiddle.name}, Head={head.name}. Guns on hand: {gunCount}.";
            if (log)
                Debug.Log("[PlayerCharacterSlot] " + LastResult, this);

#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                EditorUtility.SetDirty(this);
                EditorUtility.SetDirty(gameObject);
                if (!EditorApplication.isPlayingOrWillChangePlaymode)
                    EditorSceneManager.MarkSceneDirty(gameObject.scene);
            }
#endif
            return true;
        }

        public string BuildValidationMessage()
        {
            if (characterPrefab == null)
                return "Assign a Humanoid character prefab to Character Prefab.";

            Avatar avatar = FindAvatar(characterPrefab);
            if (avatar == null)
                return "No Avatar found on the prefab. Import the model as Humanoid.";
            if (!avatar.isHuman)
                return $"Avatar '{avatar.name}' is not Humanoid. Set Animation Type to Humanoid on the model.";

            Animator rootAnimator = GetComponent<Animator>();
            if (rootAnimator == null)
                return "Player root needs an Animator (Invector locomotion).";
            if (rootAnimator.runtimeAnimatorController == null)
                return "Root Animator has no controller. Keep Invector@BasicLocomotion on the player root.";

            int gunLayer = rootAnimator.GetLayerIndex("Gun Layer");
            if (gunLayer < 0)
                return "Root Animator is missing the 'Gun Layer'. Keep the shared Invector controller.";

            return string.Empty;
        }

        GameObject ResolveOrSpawnVisual()
        {
            if (IsAssignedObjectAlreadyAChild())
            {
                characterPrefab.SetActive(true);
                return characterPrefab;
            }

            Transform existing = FindMatchingVisual();
            if (existing != null)
            {
                existing.gameObject.SetActive(true);
                return existing.gameObject;
            }

            DestroySpawnedIfDifferent();

            GameObject instance = InstantiateVisual();
            if (instance == null)
                return null;

            instance.name = characterPrefab.name;
            instance.transform.SetParent(transform, false);
            ResetLocalPose(instance.transform);
            spawnedVisual = instance;
            return instance;
        }

        bool IsAssignedObjectAlreadyAChild()
        {
            if (characterPrefab == null || characterPrefab.transform == transform)
                return false;
            return characterPrefab.transform.IsChildOf(transform);
        }

        Transform FindMatchingVisual()
        {
            Transform[] all = GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                Transform child = all[i];
                if (child == null || child == transform)
                    continue;
                if (child.parent != transform)
                    continue;
                if (MatchesAssignedPrefab(child.gameObject))
                    return child;
            }

            for (int i = 0; i < all.Length; i++)
            {
                Transform child = all[i];
                if (child == null || child == transform)
                    continue;
                if (MatchesAssignedPrefab(child.gameObject))
                    return child;
            }

            return null;
        }

        bool MatchesAssignedPrefab(GameObject candidate)
        {
            if (candidate == null || characterPrefab == null)
                return false;
            if (candidate == characterPrefab)
                return true;
            if (candidate.name == characterPrefab.name)
                return true;

#if UNITY_EDITOR
            Object source = PrefabUtility.GetCorrespondingObjectFromOriginalSource(candidate);
            if (source == characterPrefab)
                return true;
            source = PrefabUtility.GetCorrespondingObjectFromSource(candidate);
            if (source == characterPrefab)
                return true;
#endif
            return false;
        }

        void DestroySpawnedIfDifferent()
        {
            if (spawnedVisual == null)
                return;
            if (MatchesAssignedPrefab(spawnedVisual))
                return;

            if (Application.isPlaying)
                Destroy(spawnedVisual);
            else
                DestroyImmediate(spawnedVisual);
            spawnedVisual = null;
        }

        GameObject InstantiateVisual()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying && PrefabUtility.IsPartOfPrefabAsset(characterPrefab))
            {
                Object instance = PrefabUtility.InstantiatePrefab(characterPrefab, transform);
                var go = instance as GameObject;
                if (go != null)
                    Undo.RegisterCreatedObjectUndo(go, "Spawn Character Visual");
                return go;
            }
#endif
            return Instantiate(characterPrefab);
        }

        void HideOtherVisuals(GameObject keep)
        {
            for (int i = 0; i < KnownVisualNames.Length; i++)
            {
                Transform child = FindNamedChild(KnownVisualNames[i]);
                if (child != null && child.gameObject != keep)
                    child.gameObject.SetActive(false);
            }

            if (spawnedVisual != null && spawnedVisual != keep)
                spawnedVisual.SetActive(false);
        }

        void ResetLocalPose(Transform visual)
        {
            visual.localPosition = Vector3.zero;
            visual.localRotation = Quaternion.identity;
        }

        void ApplyAvatar(GameObject visual)
        {
            Animator nested = visual.GetComponent<Animator>();
            if (nested == null)
                nested = visual.GetComponentInChildren<Animator>(true);

            Avatar avatar = nested != null ? nested.avatar : FindAvatar(characterPrefab);
            if (nested != null)
                nested.enabled = false;

            Animator rootAnimator = GetComponent<Animator>();
            if (rootAnimator == null)
                return;

            if (avatar != null)
                rootAnimator.avatar = avatar;

            rootAnimator.applyRootMotion = false;
            rootAnimator.enabled = true;
            rootAnimator.Rebind();
            rootAnimator.Update(0f);
        }

        Transform ResolveBone(GameObject visual, Transform overrideBone, HumanBodyBones humanBone, string[] names)
        {
            if (IsUsableOverride(overrideBone, visual.transform))
                return overrideBone;

            Animator rootAnimator = GetComponent<Animator>();
            Transform fromHuman = GetHumanBone(rootAnimator, humanBone);
            if (fromHuman != null)
                return fromHuman;

            Animator nested = visual.GetComponent<Animator>();
            if (nested == null)
                nested = visual.GetComponentInChildren<Animator>(true);
            fromHuman = GetHumanBone(nested, humanBone);
            if (fromHuman != null)
                return fromHuman;

            return FindNamedBone(visual.transform, names);
        }

        static bool IsUsableOverride(Transform overrideBone, Transform visual)
        {
            if (overrideBone == null || visual == null)
                return false;
            return overrideBone == visual || overrideBone.IsChildOf(visual);
        }

        static Transform GetHumanBone(Animator animator, HumanBodyBones bone)
        {
            if (animator == null || !animator.isHuman)
                return null;
            return animator.GetBoneTransform(bone);
        }

        static Transform FindNamedBone(Transform root, string[] names)
        {
            if (root == null || names == null)
                return null;

            for (int n = 0; n < names.Length; n++)
            {
                Transform found = FindNamedChildUnder(root, names[n]);
                if (found != null)
                    return found;
            }

            return null;
        }

        Transform FindNamedChild(string exactName)
        {
            return FindNamedChildUnder(transform, exactName);
        }

        static Transform FindNamedChildUnder(Transform root, string exactName)
        {
            if (root == null || string.IsNullOrEmpty(exactName))
                return null;
            if (root.name == exactName)
                return root;

            Transform[] all = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != null && all[i].name == exactName)
                    return all[i];
            }

            return null;
        }

        int ReparentWeaponVisuals(Transform hand)
        {
            int count = 0;
            Transform[] all = GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                Transform t = all[i];
                if (t == null || t == hand)
                    continue;

                string n = t.name;
                if (!n.StartsWith("HoldGun") && !n.StartsWith("AimGun"))
                    continue;
                if (t.parent == hand)
                    continue;

#if UNITY_EDITOR
                if (!Application.isPlaying)
                    Undo.SetTransformParent(t, hand, "Reparent weapon to character hand");
                else
                    t.SetParent(hand, true);
#else
                t.SetParent(hand, true);
#endif
                count++;
            }

            return count;
        }

        int AttachWeaponsToNewHand(Transform hand)
        {
            PlayerGunController gun = GetComponent<PlayerGunController>();
            if (gun == null)
                return ReparentWeaponVisuals(hand);

            return gun.AttachWeaponsToHand(hand);
        }

        void BindControllers(Transform hand, Transform neck, Transform spineMiddle, Transform spineUpper, Transform head)
        {
            PlayerGunController gun = GetComponent<PlayerGunController>();
            if (gun != null)
            {
                gun.BindWeaponHand(hand);
                gun.BindHeadAimOrigin(neck != null ? neck : head);
            }

            UpperBodyAim upperBody = GetComponent<UpperBodyAim>();
            if (upperBody != null)
                upperBody.BindSpine(spineMiddle, spineUpper);

            ResidentEvilAimCamera aimCamera = FindAimCamera();
            if (aimCamera != null)
            {
                aimCamera.spineAnchor = spineMiddle;
                if (aimCamera.followTarget == null)
                    aimCamera.followTarget = transform;
            }

            MultiAimConstraint headAim = GetComponentInChildren<MultiAimConstraint>(true);
            if (headAim != null && head != null)
            {
                MultiAimConstraintData data = headAim.data;
                data.constrainedObject = head;
                headAim.data = data;
            }
        }

        static ResidentEvilAimCamera FindAimCamera()
        {
            return FindAnyObjectByType<ResidentEvilAimCamera>();
        }

        void RebuildRig()
        {
            RigBuilder rigBuilder = GetComponent<RigBuilder>();
            if (rigBuilder == null)
                return;
            rigBuilder.Build();
        }

        string DescribeAvatar()
        {
            Animator rootAnimator = GetComponent<Animator>();
            if (rootAnimator == null || rootAnimator.avatar == null)
                return "missing";
            return rootAnimator.avatar.name;
        }

        static Avatar FindAvatar(GameObject source)
        {
            if (source == null)
                return null;

            Animator animator = source.GetComponent<Animator>();
            if (animator == null)
                animator = source.GetComponentInChildren<Animator>(true);
            return animator != null ? animator.avatar : null;
        }
    }
}
