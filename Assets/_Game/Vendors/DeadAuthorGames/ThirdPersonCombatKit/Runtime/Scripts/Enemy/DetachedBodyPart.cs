using System.Collections.Generic;
using UnityEngine;


namespace DeadAuthorGames.ThirdPersonCombat
{
    /// <summary>
    /// World pickup spawned when a body mesh part is severed. MatchKey identifies
    /// which slot it can restore on a character missing that part.
    /// </summary>
    public class DetachedBodyPart : MonoBehaviour
    {
        private static readonly List<DetachedBodyPart> ActiveParts = new List<DetachedBodyPart>();

        [SerializeField] private string matchKey;
        [SerializeField] private string displayName;
        [SerializeField] private GameObject sourceOwner;

        private Renderer _renderer;

        public string MatchKey => matchKey;
        public string DisplayName => string.IsNullOrEmpty(displayName) ? matchKey : displayName;
        public GameObject SourceOwner => sourceOwner;

        public static IReadOnlyList<DetachedBodyPart> All => ActiveParts;

        private void Awake()
        {
            _renderer = GetComponentInChildren<Renderer>(true);
        }

        private void OnEnable()
        {
            if (!ActiveParts.Contains(this))
                ActiveParts.Add(this);
        }

        private void OnDisable()
        {
            ActiveParts.Remove(this);
        }

        public void Configure(string key, string name, GameObject owner)
        {
            matchKey = key;
            displayName = name;
            sourceOwner = owner;
            if (_renderer == null)
                _renderer = GetComponentInChildren<Renderer>(true);
        }

        public Bounds GetWorldBounds()
        {
            if (_renderer == null)
                _renderer = GetComponentInChildren<Renderer>(true);

            if (_renderer != null)
                return _renderer.bounds;

            return new Bounds(transform.position, Vector3.one * 0.25f);
        }
    }
}
